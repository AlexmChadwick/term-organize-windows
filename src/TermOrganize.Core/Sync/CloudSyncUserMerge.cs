// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// UserData-level merge. Record payloads, fingerprints, and conflict resolution live in CloudSync.cs.
// CloudKit, the sync controller, and the entitlement check are not called: there is no iCloud on
// this runtime. Local JSON (UserDataStore / CloudSyncFolder) is the store. Device-only fields stay
// off the merge: student name and ID, seat-check timestamps and counts, search history, recents,
// the selected term, and onboarding.

namespace TermOrganize.Core;

public static class CloudSync
{
    /// <summary>
    /// Unions <paramref name="local"/> and <paramref name="remote"/>. The newer copy of each record wins;
    /// a tie goes to the remote copy. Preferences merge field by field. A remote schema newer than
    /// this build is left alone.
    /// </summary>
    public static UserData Merge(UserData local, UserData remote)
    {
        if (remote.Version > CloudSyncConfig.SchemaVersion) return CloudSyncClone.Copy(local);

        var result = CloudSyncClone.Copy(local);
        result.Plans = MergePlans(local.Plans, remote.Plans);
        result.DegreePlans = MergeDegrees(local.DegreePlans, remote.DegreePlans);
        result.Watchlist = MergeWatch(local.Watchlist, remote.Watchlist);
        result.Saved = MergeSaved(local.Saved, remote.Saved);
        result.Recents = CloudSyncClone.Copy(local.Recents);
        result.SearchHistory = local.SearchHistory.ToList();
        result.Version = local.Version;

        var preferences = CloudSyncedPreferences.MergeWith(
            new CloudSyncedPreferences(local.Preferences),
            new Dictionary<string, DateTimeOffset>(),
            SanitizeHours(new CloudSyncedPreferences(remote.Preferences)),
            new Dictionary<string, DateTimeOffset>());
        result.Preferences = SanitizeHours(preferences.Values).Applied(local.Preferences);
        return result;
    }

    private static List<SchedulePlan> MergePlans(List<SchedulePlan> local, List<SchedulePlan> remote)
    {
        var merged = new Dictionary<Guid, SchedulePlan>();
        foreach (var plan in local) merged[plan.Id] = CloudSyncClone.Copy(plan);
        foreach (var plan in remote)
        {
            if (!merged.TryGetValue(plan.Id, out var current) || plan.UpdatedAt >= current.UpdatedAt)
                merged[plan.Id] = CloudSyncClone.Copy(plan);
        }
        var plans = merged.Values.ToList();
        foreach (var term in plans.Select(plan => plan.TermCode).Distinct(StringComparer.Ordinal))
        {
            var primaries = plans.Where(plan => plan.TermCode == term && plan.IsPrimary).ToList();
            if (primaries.Count < 2) continue;
            var keep = primaries.OrderBy(plan => plan.UpdatedAt).ThenBy(plan => plan.Id.ToString("D").ToUpperInvariant(), StringComparer.Ordinal).Last();
            foreach (var plan in primaries)
                if (plan.Id != keep.Id) plan.IsPrimary = false;
        }
        return plans;
    }

    private static List<DegreePlan> MergeDegrees(List<DegreePlan> local, List<DegreePlan> remote)
    {
        var localById = local.ToDictionary(plan => plan.Id);
        var merged = new Dictionary<Guid, DegreePlan>();
        foreach (var plan in local) merged[plan.Id] = CloudSyncClone.Copy(plan);
        foreach (var plan in remote)
        {
            var copy = CloudSyncClone.Copy(plan);
            if (localById.TryGetValue(plan.Id, out var current))
            {
                if (current.UpdatedAt > plan.UpdatedAt)
                {
                    merged[plan.Id] = CloudSyncClone.Copy(current);
                    continue;
                }
                copy.StudentName = current.StudentName;
                copy.StudentID = current.StudentID;
            }
            else
            {
                copy.StudentName = null;
                copy.StudentID = null;
            }
            merged[plan.Id] = copy;
        }
        var plans = merged.Values.ToList();
        var primaries = plans.Where(plan => plan.IsPrimary).ToList();
        if (primaries.Count >= 2)
        {
            var keep = primaries.OrderBy(plan => plan.UpdatedAt).ThenBy(plan => plan.Id.ToString("D").ToUpperInvariant(), StringComparer.Ordinal).Last();
            foreach (var plan in primaries)
                if (plan.Id != keep.Id) plan.IsPrimary = false;
        }
        return plans;
    }

    private static List<WatchItem> MergeWatch(List<WatchItem> local, List<WatchItem> remote)
    {
        var localById = local.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var merged = new Dictionary<string, WatchItem>(StringComparer.Ordinal);
        foreach (var item in local) merged[item.Id] = CloudSyncClone.Copy(item);
        foreach (var item in remote)
        {
            var copy = CloudSyncClone.Copy(item);
            if (localById.TryGetValue(item.Id, out var current))
            {
                if (current.AddedAt > item.AddedAt)
                {
                    merged[item.Id] = CloudSyncClone.Copy(current);
                    continue;
                }
                copy.LastSeatsRemaining = current.LastSeatsRemaining;
                copy.LastWaitlistRemaining = current.LastWaitlistRemaining;
                copy.SeatCapacity = current.SeatCapacity;
                copy.LastCheckedAt = current.LastCheckedAt;
                copy.LastNotifiedAt = current.LastNotifiedAt;
            }
            else
            {
                copy.LastSeatsRemaining = 0;
                copy.LastWaitlistRemaining = 0;
                copy.SeatCapacity = 0;
                copy.LastCheckedAt = null;
                copy.LastNotifiedAt = null;
            }
            merged[item.Id] = copy;
        }
        return merged.Values.OrderByDescending(item => item.AddedAt).ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
    }

    private static List<SavedCourse> MergeSaved(List<SavedCourse> local, List<SavedCourse> remote)
    {
        var merged = new Dictionary<string, SavedCourse>(StringComparer.Ordinal);
        foreach (var course in local) merged[course.Id] = CloudSyncClone.Copy(course);
        foreach (var course in remote)
        {
            if (!merged.TryGetValue(course.Id, out var current) || course.SavedAt >= current.SavedAt)
                merged[course.Id] = CloudSyncClone.Copy(course);
        }
        return merged.Values.OrderByDescending(course => course.SavedAt).ThenBy(course => course.Id, StringComparer.Ordinal).ToList();
    }

    private static CloudSyncedPreferences SanitizeHours(CloudSyncedPreferences preferences)
    {
        var start = preferences.CalendarStartHour;
        var end = preferences.CalendarEndHour;
        if (start is >= 0 and <= 23 && end is >= 1 and <= 24 && start < end) return preferences;
        var defaults = new CloudSyncedPreferences();
        preferences.CalendarStartHour = defaults.CalendarStartHour;
        preferences.CalendarEndHour = defaults.CalendarEndHour;
        return preferences;
    }
}
