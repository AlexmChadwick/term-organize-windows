// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Security.Cryptography;
using System.Text.Json;

namespace TermOrganize.Core;

/// <summary>
/// Plans, watchlist, bookmarks and preferences as JSON. Saves are written immediately
/// so a closed window does not drop an edit. iCloud is not available on Windows;
/// <see cref="ExportTo"/> and <see cref="ImportReplacing"/> move the same file between machines.
/// A save that finds a newer file on disk merges plans it did not also edit, and keeps
/// the on-disk copy of a plan both sides edited.
/// </summary>
public sealed class UserDataStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private bool _applyingRemote;
    private bool _writing;
    private UserData _baseline = new();
    private string _diskHash = "";
    private DateTime _diskWriteTimeUtc;
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private TimeSpan _reloadDelay = TimeSpan.FromMilliseconds(300);

    public AppPreferences Preferences { get; private set; } = new();
    public List<SavedCourse> Saved { get; private set; } = [];
    public List<RecentCourse> Recents { get; private set; } = [];
    public List<SchedulePlan> Plans { get; private set; } = [];
    public List<WatchItem> Watchlist { get; private set; } = [];
    public List<string> SearchHistory { get; private set; } = [];
    public List<DegreePlan> DegreePlans { get; private set; } = [];

    public Action? OnPlansChanged { get; set; }
    public Action? DidChange { get; set; }
    /// <summary>Raised after an outside edit is reloaded, or folded in on the next save.</summary>
    public event Action? ExternalEditApplied;
    public bool IsApplyingRemoteChanges => _applyingRemote;

    public UserDataStore(string? filePath = null)
    {
        _path = filePath ?? DefaultPath();
        lock (_gate)
            LoadInitial();
    }

    public static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "TermOrganize", "UserData.json");
    }

    public string FilePath => _path;

    /// <summary>
    /// Reloads when another process writes this file. Events are debounced so a temp-file
    /// rename settles into one read. The revision checked before each save is the SHA-256
    /// of the file bytes, recorded with the UTC write time.
    /// </summary>
    public void WatchForExternalEdits(TimeSpan? debounce = null)
    {
        StopWatching();
        _reloadDelay = debounce is { } delay && delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(300);
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        _reloadTimer = new Timer(_ => ReloadFromDisk(), null, Timeout.Infinite, Timeout.Infinite);
        var watcher = new FileSystemWatcher(directory, Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        watcher.Changed += OnDiskEvent;
        watcher.Created += OnDiskEvent;
        watcher.Renamed += OnDiskEvent;
        _watcher = watcher;
    }

    public void StopWatching()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnDiskEvent;
            _watcher.Created -= OnDiskEvent;
            _watcher.Renamed -= OnDiskEvent;
            _watcher.Dispose();
            _watcher = null;
        }
        _reloadTimer?.Dispose();
        _reloadTimer = null;
    }

    public UserData Data() => new()
    {
        Preferences = Preferences,
        Saved = Saved,
        Recents = Recents,
        Plans = Plans,
        Watchlist = Watchlist,
        SearchHistory = SearchHistory,
        DegreePlans = DegreePlans,
    };

    public void ReplacePreferences(AppPreferences preferences)
    {
        Preferences = preferences;
        Save();
    }

    public List<SavedCourse> SavedCourses(string termCode) =>
        Saved.Where(s => s.TermCode == termCode).OrderByDescending(s => s.SavedAt).ToList();

    public bool IsSaved(CourseKey key, string termCode) =>
        Saved.Any(s => s.Key == key && s.TermCode == termCode);

    public void ToggleSaved(CourseKey key, string termCode)
    {
        var index = Saved.FindIndex(s => s.Key == key && s.TermCode == termCode);
        if (index >= 0) Saved.RemoveAt(index);
        else Saved.Insert(0, new SavedCourse { TermCode = termCode, Key = key, SavedAt = DateTimeOffset.UtcNow });
        Save();
    }

    public List<RecentCourse> RecentsIn(string termCode) => Recents.Where(r => r.TermCode == termCode).ToList();

    public void RecordView(CourseKey key, string termCode)
    {
        Recents.RemoveAll(r => r.Key == key && r.TermCode == termCode);
        Recents.Insert(0, new RecentCourse { TermCode = termCode, Key = key, ViewedAt = DateTimeOffset.UtcNow });
        if (Recents.Count > 30) Recents.RemoveRange(30, Recents.Count - 30);
        Save();
    }

    public void ClearRecents()
    {
        Recents.Clear();
        Save();
    }

    public void RecordSearch(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 2) return;
        SearchHistory.RemoveAll(s => s.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        SearchHistory.Insert(0, trimmed);
        if (SearchHistory.Count > 12) SearchHistory.RemoveRange(12, SearchHistory.Count - 12);
        Save();
    }

    public void ClearSearchHistory()
    {
        SearchHistory.Clear();
        Save();
    }

    public List<SchedulePlan> PlansIn(string termCode) =>
        Plans.Where(p => p.TermCode == termCode)
            .OrderByDescending(p => p.IsPrimary)
            .ThenByDescending(p => p.UpdatedAt)
            .ToList();

    public SchedulePlan? Plan(Guid id) => Plans.FirstOrDefault(p => p.Id == id);

    public SchedulePlan? PrimaryPlan(string termCode) =>
        Plans.FirstOrDefault(p => p.TermCode == termCode && p.IsPrimary);

    public SchedulePlan CreatePlan(string name, string termCode)
    {
        var plan = new SchedulePlan
        {
            Name = name,
            TermCode = termCode,
            IsPrimary = !Plans.Any(p => p.TermCode == termCode),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        Plans.Add(plan);
        PlansChanged();
        return plan;
    }

    public void Update(SchedulePlan plan)
    {
        var index = Plans.FindIndex(p => p.Id == plan.Id);
        if (index < 0) return;
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        Plans[index] = plan;
        PlansChanged();
    }

    public void UpdatePlan(Guid id, Action<SchedulePlan> change)
    {
        var plan = Plan(id);
        if (plan is null) return;
        change(plan);
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        PlansChanged();
    }

    public void DeletePlan(Guid id)
    {
        var plan = Plan(id);
        if (plan is null) return;
        Plans.RemoveAll(p => p.Id == id);
        if (plan.IsPrimary)
        {
            var next = Plans.FirstOrDefault(p => p.TermCode == plan.TermCode);
            if (next is not null) next.IsPrimary = true;
        }
        PlansChanged();
    }

    public SchedulePlan? DuplicatePlan(Guid id)
    {
        var plan = Plan(id);
        if (plan is null) return null;
        var copy = JsonSerializer.Deserialize<SchedulePlan>(JsonSerializer.Serialize(plan, AppJson.UserData), AppJson.UserData)!;
        copy.Id = Guid.NewGuid();
        copy.Name = plan.Name + " Copy";
        copy.IsPrimary = false;
        copy.CreatedAt = DateTimeOffset.UtcNow;
        copy.UpdatedAt = DateTimeOffset.UtcNow;
        Plans.Add(copy);
        PlansChanged();
        return copy;
    }

    public void SetPrimary(Guid id)
    {
        var target = Plan(id);
        if (target is null) return;
        foreach (var plan in Plans.Where(p => p.TermCode == target.TermCode))
            plan.IsPrimary = plan.Id == id;
        PlansChanged();
    }

    public bool AddCourse(CourseKey key, Guid planId, bool required = true)
    {
        var added = false;
        UpdatePlan(planId, plan =>
        {
            if (plan.Contains(key)) return;
            plan.Courses.Add(new PlanCourse { Key = key, IsRequired = required, ColorIndex = plan.NextColorIndex });
            added = true;
        });
        return added;
    }

    public void RemoveCourse(CourseKey key, Guid planId, IEnumerable<string>? sectionsOfCourse = null)
    {
        var crns = sectionsOfCourse?.ToHashSet(StringComparer.Ordinal) ?? [];
        UpdatePlan(planId, plan =>
        {
            plan.Courses.RemoveAll(c => c.Key == key);
            if (crns.Count > 0)
            {
                plan.SelectedCRNs.RemoveAll(crns.Contains);
                plan.FavoriteCombinations.RemoveAll(combo => combo.Any(crns.Contains));
            }
        });
    }

    public List<WatchItem> WatchItems(string termCode) => Watchlist.Where(w => w.TermCode == termCode).ToList();

    public bool IsWatching(string crn, string termCode) =>
        Watchlist.Any(w => w.Crn == crn && w.TermCode == termCode);

    public void Watch(CourseSection section, string courseTitle, string termCode)
    {
        if (IsWatching(section.Crn, termCode)) return;
        Watchlist.Insert(0, new WatchItem
        {
            TermCode = termCode,
            Crn = section.Crn,
            CourseKey = section.CourseKey,
            CourseTitle = courseTitle,
            SectionLabel = $"{section.ClassType} · Section {section.SectionNumber}",
            LastSeatsRemaining = section.SeatsRemaining,
            LastWaitlistRemaining = section.WaitlistRemaining,
            SeatCapacity = section.SeatCapacity,
            LastCheckedAt = DateTimeOffset.UtcNow,
        });
        Save();
    }

    public void Unwatch(string crn, string termCode)
    {
        Watchlist.RemoveAll(w => w.Crn == crn && w.TermCode == termCode);
        Save();
    }

    public void ToggleWatch(CourseSection section, string courseTitle, string termCode)
    {
        if (IsWatching(section.Crn, termCode)) Unwatch(section.Crn, termCode);
        else Watch(section, courseTitle, termCode);
    }

    public void UpdateWatchItem(WatchItem item)
    {
        var index = Watchlist.FindIndex(w => w.Id == item.Id);
        if (index < 0) return;
        Watchlist[index] = item;
        Save();
    }

    public void ReplaceWatchlist(IReadOnlyList<WatchItem> items)
    {
        Watchlist = items.ToList();
        Save();
    }

    public DegreePlan? DegreePlan(Guid id) => DegreePlans.FirstOrDefault(p => p.Id == id);

    public DegreePlan? PrimaryDegreePlan =>
        DegreePlans.FirstOrDefault(p => p.IsPrimary) ?? DegreePlans.MaxBy(p => p.UpdatedAt);

    public List<DegreePlan> SortedDegreePlans =>
        DegreePlans.OrderByDescending(p => p.IsPrimary).ThenByDescending(p => p.UpdatedAt).ToList();

    public DegreePlan AddDegreePlan(DegreePlan plan)
    {
        plan.SortTerms();
        if (DegreePlans.Count == 0) plan.IsPrimary = true;
        if (plan.IsPrimary)
            foreach (var existing in DegreePlans) existing.IsPrimary = false;
        DegreePlans.Add(plan);
        Save();
        return plan;
    }

    public void UpdateDegreePlan(Guid id, Action<DegreePlan> change)
    {
        var plan = DegreePlan(id);
        if (plan is null) return;
        change(plan);
        plan.SortTerms();
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        Save();
    }

    public void DeleteDegreePlan(Guid id)
    {
        var wasPrimary = DegreePlan(id)?.IsPrimary ?? false;
        DegreePlans.RemoveAll(p => p.Id == id);
        if (wasPrimary && DegreePlans.Count > 0)
            DegreePlans.MaxBy(p => p.UpdatedAt)!.IsPrimary = true;
        Save();
    }

    public void SetPrimaryDegreePlan(Guid id)
    {
        foreach (var plan in DegreePlans) plan.IsPrimary = plan.Id == id;
        Save();
    }

    /// <summary>Replaces a stored plan's contents, keeping its id, primary flag and created time.</summary>
    public void ReplaceDegreePlan(Guid id, DegreePlan plan)
    {
        UpdateDegreePlan(id, stored =>
        {
            var keepId = stored.Id;
            var keepPrimary = stored.IsPrimary;
            var keepCreated = stored.CreatedAt;
            CopyDegreePlan(plan, stored);
            stored.Id = keepId;
            stored.IsPrimary = keepPrimary;
            stored.CreatedAt = keepCreated;
        });
    }

    /// <summary>Courses the primary degree plan lists as completed (or in progress).</summary>
    public HashSet<CourseKey> CompletedCourseKeys => PrimaryDegreePlan?.CompletedKeys ?? [];

    /// <summary>Where the primary degree plan schedules a course, if anywhere.</summary>
    public DegreePlanTerm? PlannedTerm(CourseKey key) =>
        PrimaryDegreePlan?.Terms.FirstOrDefault(term => term.Courses.Any(course => course.Key == key));

    public void ResetAll(bool keepingPreferences = false)
    {
        if (!keepingPreferences) Preferences = new AppPreferences();
        Saved.Clear();
        Recents.Clear();
        Plans.Clear();
        Watchlist.Clear();
        SearchHistory.Clear();
        DegreePlans.Clear();
        PlansChanged();
    }

    public void ExportTo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(Data(), AppJson.UserData));
    }

    public void ImportReplacing(string path)
    {
        var data = JsonSerializer.Deserialize<UserData>(File.ReadAllText(path), AppJson.UserData) ?? new UserData();
        Apply(data, remote: false);
    }

    public void FlushNow() => Save(report: true);

    /// <summary>
    /// Applies values from another device. Each save replaces the value with the same id
    /// (or is inserted) and each delete removes one. The file is written immediately and
    /// <see cref="DidChange"/> is not called, so the change is not sent back.
    /// </summary>
    public void ApplyRemoteChanges(IReadOnlyList<CloudSyncEntity> saves, IReadOnlyList<CloudSyncKey> deletes)
    {
        if (saves.Count == 0 && deletes.Count == 0) return;
        var updated = Data().ApplyingCloudChanges(saves, deletes);
        var plansChanged = !SamePlans(updated.Plans, Plans);
        _applyingRemote = true;
        Preferences = updated.Preferences;
        Saved = updated.Saved;
        Watchlist = updated.Watchlist;
        DegreePlans = updated.DegreePlans;
        if (plansChanged) Plans = updated.Plans;
        _applyingRemote = false;
        var merged = WriteFile();
        if (merged) ExternalEditApplied?.Invoke();
        if (plansChanged) OnPlansChanged?.Invoke();
    }

    public void Apply(UserData data, bool remote)
    {
        var plansChanged = !SamePlans(data.Plans, Plans);
        _applyingRemote = remote;
        Preferences = data.Preferences;
        Saved = data.Saved;
        Recents = data.Recents;
        Plans = data.Plans;
        Watchlist = data.Watchlist;
        SearchHistory = data.SearchHistory;
        DegreePlans = data.DegreePlans;
        _applyingRemote = false;
        var merged = WriteFile();
        if (!remote) DidChange?.Invoke();
        if (merged) ExternalEditApplied?.Invoke();
        if (plansChanged) OnPlansChanged?.Invoke();
    }

    private void PlansChanged()
    {
        Save();
        OnPlansChanged?.Invoke();
    }

    private void Save(bool report = true)
    {
        if (_applyingRemote) return;
        var merged = WriteFile();
        if (report) DidChange?.Invoke();
        if (merged) ExternalEditApplied?.Invoke();
    }

    private bool WriteFile()
    {
        lock (_gate)
            return WriteFileLocked();
    }

    /// <summary>
    /// Writes the current memory. When the file hash or write time no longer matches the
    /// last load, plans edited on only one side are merged and a plan edited on both sides
    /// keeps the disk copy.
    /// </summary>
    private bool WriteFileLocked()
    {
        _writing = true;
        try
        {
            var merged = false;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (!TryReadDisk(out _, out var diskData, out var changed, out var unreadable))
                    continue;
                if (unreadable)
                    return false;
                if (changed && diskData is not null)
                {
                    AssignFrom(UserDataMerge.Merge(_baseline, Data(), diskData));
                    merged = true;
                }
                var payload = JsonSerializer.SerializeToUtf8Bytes(Data(), AppJson.UserData);
                var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                if (File.Exists(_path))
                {
                    try { File.Copy(_path, _path + ".bak", overwrite: true); }
                    catch (IOException) { }
                }
                var temp = _path + ".tmp";
                File.WriteAllBytes(temp, payload);
                File.Move(temp, _path, overwrite: true);
                byte[] written;
                try { written = File.ReadAllBytes(_path); }
                catch (IOException) { continue; }
                var writtenHash = Hash(written);
                if (writtenHash != Hash(payload))
                    continue;
                _baseline = UserDataMerge.Clone(Data());
                _diskHash = writtenHash;
                _diskWriteTimeUtc = File.GetLastWriteTimeUtc(_path);
                return merged;
            }
            return merged;
        }
        finally
        {
            _writing = false;
        }
    }

    private void LoadInitial()
    {
        if (!File.Exists(_path))
        {
            Adopt(new UserData(), "");
            return;
        }
        try
        {
            var bytes = File.ReadAllBytes(_path);
            var data = JsonSerializer.Deserialize<UserData>(bytes, AppJson.UserData) ?? new UserData();
            Adopt(data, Hash(bytes));
        }
        catch (JsonException)
        {
            Adopt(new UserData(), Hash(File.ReadAllBytes(_path)));
        }
        catch (IOException)
        {
            Adopt(new UserData(), "");
        }
    }

    private void OnDiskEvent(object? sender, FileSystemEventArgs e) => ScheduleReload();

    private void ScheduleReload()
    {
        try { _reloadTimer?.Change(_reloadDelay, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }
    }

    private void ReloadFromDisk()
    {
        var notify = false;
        var wrote = false;
        lock (_gate)
        {
            if (_writing)
            {
                ScheduleReload();
                return;
            }
            if (!TryReadDisk(out var hash, out var disk, out var changed, out var unreadable) || unreadable || !changed)
                return;
            if (disk is null)
            {
                Adopt(new UserData(), "");
                notify = true;
            }
            else if (UserDataMerge.Same(Data(), _baseline))
            {
                Adopt(disk, hash);
                notify = true;
            }
            else
            {
                wrote = WriteFileLocked();
                notify = true;
            }
        }
        if (wrote) DidChange?.Invoke();
        if (notify) ExternalEditApplied?.Invoke();
    }

    /// <summary>
    /// False on a transient read error. <paramref name="changed"/> is true when the SHA-256
    /// no longer matches the last load. A newer write time with the same hash is not a change.
    /// <paramref name="unreadable"/> means the bytes are not UserData JSON, so a save must not replace them.
    /// </summary>
    private bool TryReadDisk(out string hash, out UserData? data, out bool changed, out bool unreadable)
    {
        hash = "";
        data = null;
        changed = false;
        unreadable = false;
        if (!File.Exists(_path))
        {
            changed = _diskHash.Length > 0;
            return true;
        }
        byte[] bytes;
        try { bytes = File.ReadAllBytes(_path); }
        catch (IOException) { return false; }
        hash = Hash(bytes);
        DateTime mtime;
        try { mtime = File.GetLastWriteTimeUtc(_path); }
        catch (IOException) { mtime = default; }
        if (hash == _diskHash)
        {
            if (mtime != _diskWriteTimeUtc) _diskWriteTimeUtc = mtime;
            return true;
        }
        changed = true;
        try { data = JsonSerializer.Deserialize<UserData>(bytes, AppJson.UserData) ?? new UserData(); }
        catch (JsonException) { unreadable = true; }
        return true;
    }

    private void Adopt(UserData data, string hash)
    {
        AssignFrom(data);
        _baseline = UserDataMerge.Clone(data);
        _diskHash = hash;
        try { _diskWriteTimeUtc = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : default; }
        catch (IOException) { _diskWriteTimeUtc = default; }
    }

    private void AssignFrom(UserData data)
    {
        Preferences = data.Preferences;
        Saved = data.Saved;
        Recents = data.Recents;
        Plans = data.Plans;
        Watchlist = data.Watchlist;
        SearchHistory = data.SearchHistory;
        DegreePlans = data.DegreePlans;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static bool SamePlans(List<SchedulePlan> a, List<SchedulePlan> b) =>
        a.Count == b.Count && a.Select(p => p.Id).SequenceEqual(b.Select(p => p.Id));

    private static void CopyDegreePlan(DegreePlan source, DegreePlan target)
    {
        var copy = JsonSerializer.Deserialize<DegreePlan>(JsonSerializer.Serialize(source, AppJson.UserData), AppJson.UserData)!;
        target.Name = copy.Name;
        target.Program = copy.Program;
        target.StudentName = copy.StudentName;
        target.StudentID = copy.StudentID;
        target.Completed = copy.Completed;
        target.Terms = copy.Terms;
        target.IsPrimary = copy.IsPrimary;
        target.ImportedFrom = copy.ImportedFrom;
        target.PrintedAt = copy.PrintedAt;
        target.CreatedAt = copy.CreatedAt;
        target.UpdatedAt = copy.UpdatedAt;
        target.CatalogYear = copy.CatalogYear;
        target.CatalogProgramPath = copy.CatalogProgramPath;
        target.CatalogProgramTitle = copy.CatalogProgramTitle;
        target.ConcentrationID = copy.ConcentrationID;
        target.Substitutions = copy.Substitutions;
        target.Equivalences = copy.Equivalences;
    }
}
