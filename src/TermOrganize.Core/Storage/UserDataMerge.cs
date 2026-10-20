// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;

namespace TermOrganize.Core;

/// <summary>
/// Combines an in-memory edit with a UserData.json that changed after the app loaded it.
/// Plans the two sides did not both edit are kept as edited. A plan both sides edited keeps
/// the copy on disk, and the in-memory plan is saved beside it as "<name> (edited in app)".
/// </summary>
public static class UserDataMerge
{
    public const string EditedInAppSuffix = " (edited in app)";

    public static UserData Merge(UserData baseline, UserData local, UserData disk) => new()
    {
        Version = Math.Max(local.Version, disk.Version),
        Preferences = Pick(baseline.Preferences, local.Preferences, disk.Preferences),
        Saved = MergeRows(baseline.Saved, local.Saved, disk.Saved, item => item.Id),
        Recents = MergeRows(baseline.Recents, local.Recents, disk.Recents, item => item.Id),
        Plans = MergePlans(baseline.Plans, local.Plans, disk.Plans, plan => plan.Id, plan => plan.Name, (plan, name) => plan.Name = name, (plan, id) => plan.Id = id, plan => plan.IsPrimary = false),
        Watchlist = MergeRows(baseline.Watchlist, local.Watchlist, disk.Watchlist, item => item.Id),
        SearchHistory = MergeHistory(baseline.SearchHistory, local.SearchHistory, disk.SearchHistory),
        DegreePlans = MergePlans(baseline.DegreePlans, local.DegreePlans, disk.DegreePlans, plan => plan.Id, plan => plan.Name, (plan, name) => plan.Name = name, (plan, id) => plan.Id = id, plan => plan.IsPrimary = false),
    };

    public static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, AppJson.UserData);
        return JsonSerializer.Deserialize<T>(json, AppJson.UserData)!;
    }

    public static bool Same<T>(T left, T right) => Canonical(left) == Canonical(right);

    public static string Canonical<T>(T value) => JsonSerializer.Serialize(value, AppJson.UserData);

    private static T Pick<T>(T baseline, T local, T disk)
    {
        var localChanged = !Same(baseline, local);
        var diskChanged = !Same(baseline, disk);
        if (localChanged && !diskChanged) return Clone(local);
        if (localChanged && diskChanged) return Clone(local);
        return Clone(disk);
    }

    private static List<T> MergeRows<T>(List<T> baseline, List<T> local, List<T> disk, Func<T, string> id)
    {
        var baseMap = Index(baseline, id);
        var localMap = Index(local, id);
        var diskMap = Index(disk, id);
        var result = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in disk)
        {
            var key = id(item);
            if (!seen.Add(key)) continue;
            baseMap.TryGetValue(key, out var baseItem);
            localMap.TryGetValue(key, out var localItem);
            var localChanged = localItem is not null && (baseItem is null || !Same(baseItem, localItem));
            var diskChanged = baseItem is null || !Same(baseItem, item);
            if (localItem is null && baseItem is not null && !diskChanged) continue;
            result.Add(Clone(localChanged && !diskChanged && localItem is not null ? localItem : item));
        }
        foreach (var item in local)
        {
            var key = id(item);
            if (seen.Contains(key)) continue;
            baseMap.TryGetValue(key, out var baseItem);
            var localChanged = baseItem is null || !Same(baseItem, item);
            if (baseItem is not null && !diskMap.ContainsKey(key) && !localChanged) continue;
            result.Add(Clone(item));
        }
        return result;
    }

    private static List<T> MergePlans<T>(
        List<T> baseline,
        List<T> local,
        List<T> disk,
        Func<T, Guid> id,
        Func<T, string> name,
        Action<T, string> rename,
        Action<T, Guid> reidentify,
        Action<T> clearPrimary)
    {
        var baseMap = Index(baseline, id);
        var localMap = Index(local, id);
        var result = new List<T>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<Guid>();

        void Add(T item, bool fork)
        {
            var copy = Clone(item);
            if (fork)
            {
                reidentify(copy, Guid.NewGuid());
                clearPrimary(copy);
                rename(copy, Unique(Display(name(copy)) + EditedInAppSuffix, names));
            }
            else
            {
                names.Add(name(copy));
            }
            result.Add(copy);
        }

        foreach (var item in disk)
        {
            var key = id(item);
            if (!seen.Add(key)) continue;
            baseMap.TryGetValue(key, out var baseItem);
            localMap.TryGetValue(key, out var localItem);
            var localChanged = localItem is not null && (baseItem is null || !Same(baseItem, localItem));
            var diskChanged = baseItem is null || !Same(baseItem, item);
            if (localItem is null)
            {
                if (baseItem is not null && !diskChanged) continue;
                Add(item, fork: false);
                continue;
            }
            if (localChanged && diskChanged && !Same(localItem, item))
            {
                Add(item, fork: false);
                Add(localItem, fork: true);
                continue;
            }
            Add(localChanged && !diskChanged ? localItem : item, fork: false);
        }

        foreach (var item in local)
        {
            var key = id(item);
            if (seen.Contains(key)) continue;
            baseMap.TryGetValue(key, out var baseItem);
            var localChanged = baseItem is null || !Same(baseItem, item);
            if (baseItem is not null && !localChanged) continue;
            Add(item, fork: baseItem is not null);
        }
        return result;
    }

    private static List<string> MergeHistory(List<string> baseline, List<string> local, List<string> disk)
    {
        if (Same(baseline, local)) return disk.ToList();
        if (Same(baseline, disk)) return local.ToList();
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in local.Concat(disk))
        {
            if (item.Length == 0 || !seen.Add(item)) continue;
            result.Add(item);
            if (result.Count == 12) break;
        }
        return result;
    }

    private static Dictionary<TKey, T> Index<T, TKey>(IEnumerable<T> items, Func<T, TKey> id) where TKey : notnull
    {
        var map = new Dictionary<TKey, T>();
        foreach (var item in items) map[id(item)] = item;
        return map;
    }

    private static string Display(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        return trimmed.Length == 0 ? "Plan" : trimmed;
    }

    private static string Unique(string desired, HashSet<string> used)
    {
        var name = desired;
        var n = 2;
        while (!used.Add(name))
            name = desired + " " + n++;
        return name;
    }
}
