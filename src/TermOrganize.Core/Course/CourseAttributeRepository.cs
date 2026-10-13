// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// NUpath attributes live on Banner sections, not on the course catalog entry.
// Term Organize is not affiliated with Northeastern University.

using System.Text.Json;

namespace TermOrganize.Core;

/// <summary>Where <see cref="CourseAttributeRepository"/> reads section attributes. Tests answer without the network.</summary>
public interface IAttributeFetcher
{
    Task<IReadOnlyList<Term>> TermsAsync(CancellationToken cancellationToken = default);

    /// <summary>Attribute codes of each course that has sections in <paramref name="term"/>.</summary>
    Task<IReadOnlyDictionary<CourseKey, IReadOnlySet<string>>> SectionAttributesAsync(
        string term,
        IReadOnlyList<CourseKey> courses,
        CancellationToken cancellationToken = default);
}

/// <summary>Banner class search as the attribute source.</summary>
public sealed class BannerAttributeFetcher : IAttributeFetcher
{
    private readonly BannerClient _banner;

    public BannerAttributeFetcher(BannerClient? banner = null) => _banner = banner ?? BannerClient.Shared;

    public async Task<IReadOnlyList<Term>> TermsAsync(CancellationToken cancellationToken = default) =>
        await _banner.FetchTermsAsync(max: 80, cancellationToken: cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<CourseKey, IReadOnlySet<string>>> SectionAttributesAsync(
        string term,
        IReadOnlyList<CourseKey> courses,
        CancellationToken cancellationToken = default)
    {
        var sections = await _banner.FetchSectionsAsync(term, courses, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<CourseKey, IReadOnlySet<string>>();
        foreach (var (key, list) in sections)
        {
            if (list.Count == 0) continue;
            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in list)
            {
                foreach (var attribute in section.SectionAttributes ?? [])
                    if (!string.IsNullOrEmpty(attribute.Code)) codes.Add(attribute.Code);
            }
            result[key] = codes;
        }
        return result;
    }
}

/// <summary>
/// Finds NUpath and other attributes by course key: loaded catalogs, then a disk cache, then
/// Banner's class search over the last three years, newest term first, stopping at the first term with sections.
/// Attributes are kept for a month. Courses with no sections in the window are kept for a week. Failures are not cached.
/// </summary>
public sealed class CourseAttributeRepository
{
    public abstract record Lookup
    {
        public sealed record Found(IReadOnlySet<string> Codes, string Term) : Lookup
        {
            public bool Equals(Found? other) =>
                other is not null && Term == other.Term && Codes.SetEquals(other.Codes);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add(Term);
                foreach (var code in Codes.OrderBy(code => code, StringComparer.Ordinal)) hash.Add(code);
                return hash.ToHashCode();
            }
        }

        public sealed record NotFound : Lookup;
        public sealed record Failed : Lookup;

        public static Lookup FoundCodes(IEnumerable<string> codes, string term) =>
            new Found(codes.ToHashSet(StringComparer.Ordinal), term);

        public static Lookup None { get; } = new NotFound();
        public static Lookup Error { get; } = new Failed();
    }

    private const int StoredVersion = 1;
    private const int BatchSize = 10;

    private readonly IAttributeFetcher _fetcher;
    private readonly string _filePath;
    private readonly Func<DateTimeOffset> _now;
    private readonly int _windowMonths;
    private readonly TimeSpan _foundLifetime;
    private readonly TimeSpan _notFoundLifetime;
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private bool _loaded;
    private bool _dirty;

    public CourseAttributeRepository(
        IAttributeFetcher fetcher,
        string? directory = null,
        Func<DateTimeOffset>? now = null,
        int windowMonths = 36,
        TimeSpan? foundLifetime = null,
        TimeSpan? notFoundLifetime = null)
    {
        _fetcher = fetcher;
        var folder = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TermOrganize", "CourseInfo");
        _filePath = Path.Combine(folder, "course-attributes.json");
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _windowMonths = windowMonths;
        _foundLifetime = foundLifetime ?? TimeSpan.FromDays(30);
        _notFoundLifetime = notFoundLifetime ?? TimeSpan.FromDays(7);
    }

    /// <summary>The Northeastern terms within <paramref name="months"/> of the newest one, newest first.</summary>
    public static List<Term> Window(IReadOnlyList<Term> terms, int months)
    {
        var sorted = terms.OrderByDescending(term => term.Code, StringComparer.Ordinal).ToList();
        if (sorted.Count == 0) return [];
        var oldest = TermCalendar.MonthIndex(sorted[0].Code) - months;
        return sorted.Where(term => TermCalendar.MonthIndex(term.Code) >= oldest).ToList();
    }

    /// <summary>Attributes of <paramref name="keys"/>. Loaded catalogs are newest first.</summary>
    public async Task<Dictionary<CourseKey, Lookup>> LookupAsync(
        IReadOnlyList<CourseKey> keys,
        IReadOnlyList<CatalogIndex>? indexes = null,
        CancellationToken cancellationToken = default)
    {
        indexes ??= [];
        LoadIfNeeded();
        var seen = new HashSet<CourseKey>();
        var remaining = keys.Where(seen.Add).ToList();
        var results = new Dictionary<CourseKey, Lookup>();
        remaining = remaining.Where(key =>
        {
            if (FromLoaded(key, indexes) is Lookup loaded)
            {
                results[key] = loaded;
                return false;
            }
            if (Cached(key) is Lookup cached)
            {
                results[key] = cached;
                return false;
            }
            return true;
        }).ToList();
        if (remaining.Count == 0) return results;

        IReadOnlyList<Term> fetched;
        try { fetched = await _fetcher.TermsAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception) { fetched = []; }
        var terms = fetched.Where(term => term.College == TermCollege.Neu).ToList();
        foreach (var index in indexes)
        {
            if (index.Term.College == TermCollege.Neu && terms.All(term => term.Code != index.Term.Code))
                terms.Add(index.Term);
        }
        var window = Window(terms, _windowMonths);
        if (window.Count == 0)
        {
            foreach (var key in remaining) results[key] = Lookup.Error;
            return results;
        }
        var loadedTerms = indexes.Select(index => index.Term.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var term in window)
        {
            if (loadedTerms.Contains(term.Code)) continue;
            for (var start = 0; start < remaining.Count; start += BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = remaining.Skip(start).Take(BatchSize).ToList();
                IReadOnlyDictionary<CourseKey, IReadOnlySet<string>> found;
                try
                {
                    found = await _fetcher.SectionAttributesAsync(term.Code, batch, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    foreach (var key in remaining)
                        if (!results.ContainsKey(key)) results[key] = Lookup.Error;
                    SaveIfNeeded();
                    return results;
                }
                foreach (var (key, codes) in found)
                {
                    results[key] = Lookup.FoundCodes(codes, term.Code);
                    Store(key, codes.OrderBy(code => code, StringComparer.Ordinal).ToList(), term.Code);
                }
            }
            remaining.RemoveAll(results.ContainsKey);
            if (remaining.Count == 0) break;
        }
        foreach (var key in remaining)
        {
            if (results.ContainsKey(key)) continue;
            results[key] = Lookup.None;
            Store(key, null, null);
        }
        SaveIfNeeded();
        return results;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _loaded = true;
            _dirty = false;
        }
        try { File.Delete(_filePath); } catch (IOException) { }
    }

    /// <summary>The course's attributes in the newest loaded term that has sections of it.</summary>
    public static Lookup? FromLoaded(CourseKey key, IReadOnlyList<CatalogIndex> indexes)
    {
        foreach (var index in indexes)
        {
            if (index.Sections(key).Count == 0) continue;
            return Lookup.FoundCodes(index.Course(key)?.Attributes ?? [], index.Term.Code);
        }
        return null;
    }

    private Lookup? Cached(CourseKey key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key.ToString(), out var entry)) return null;
            var age = _now() - DateTimeOffset.FromUnixTimeSeconds((long)Math.Floor(entry.CheckedAt));
            if (entry.Codes is not null && entry.Term is not null)
                return age < _foundLifetime ? Lookup.FoundCodes(entry.Codes, entry.Term) : null;
            return age < _notFoundLifetime ? Lookup.None : null;
        }
    }

    private void Store(CourseKey key, List<string>? codes, string? term)
    {
        lock (_gate)
        {
            _entries[key.ToString()] = new CacheEntry
            {
                Codes = codes,
                Term = term,
                CheckedAt = _now().ToUnixTimeSeconds(),
            };
            _dirty = true;
        }
    }

    private void LoadIfNeeded()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
            if (!File.Exists(_filePath)) return;
            try
            {
                var stored = JsonSerializer.Deserialize<StoredFile>(File.ReadAllText(_filePath));
                if (stored is null || stored.Version != StoredVersion || stored.Entries is null) return;
                foreach (var (key, entry) in stored.Entries) _entries[key] = entry;
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
    }

    private void SaveIfNeeded()
    {
        string json;
        lock (_gate)
        {
            if (!_dirty) return;
            _dirty = false;
            json = JsonSerializer.Serialize(new StoredFile { Version = StoredVersion, Entries = new Dictionary<string, CacheEntry>(_entries) });
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _filePath, overwrite: true);
        }
        catch (IOException) { }
    }

    private sealed class StoredFile
    {
        public int Version { get; set; }
        public Dictionary<string, CacheEntry>? Entries { get; set; }
    }

    private sealed class CacheEntry
    {
        public List<string>? Codes { get; set; }
        public string? Term { get; set; }
        public double CheckedAt { get; set; }
    }
}
