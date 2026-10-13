// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// One seat check: fetch the watched sections, compare them, and store the new counts.
/// The window timer, the scheduled task, and the Windows background task all call this.
/// A term whose fetch fails is left out, so a Banner outage does not wipe the last counts.
/// </summary>
public static class SeatCheckService
{
    public delegate Task<IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>> Fetch(
        string termCode,
        IReadOnlyList<CourseKey> keys,
        CancellationToken cancellationToken);

    public static async Task<WatchlistComparison.Outcome> RunAsync(
        UserDataStore store,
        Fetch fetch,
        CancellationToken cancellationToken = default)
    {
        var live = new Dictionary<string, IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>>(StringComparer.Ordinal);
        foreach (var group in store.Watchlist.GroupBy(item => item.TermCode))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keys = group.Select(item => item.CourseKey).Distinct().ToList();
            try
            {
                live[group.Key] = await fetch(group.Key, keys, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // WatchlistComparison leaves items in a missing term untouched.
            }
        }
        var outcome = WatchlistComparison.Compare(store.Watchlist, live);
        foreach (var item in outcome.Items) store.UpdateWatchItem(item);
        return outcome;
    }
}

/// <summary>Alerts the background check could not toast, shown the next time the window opens.</summary>
public static class SeatAlertLog
{
    public static string DefaultPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TermOrganize",
        "seat-alerts.txt");

    public static void Append(IEnumerable<WatchlistAlert> alerts, string? path = null)
    {
        var lines = alerts.Select(alert => alert.Title + "\t" + alert.Body).ToList();
        if (lines.Count == 0) return;
        var file = path ?? DefaultPath;
        var directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.AppendAllLines(file, lines);
    }

    public static List<(string Title, string Body)> Take(string? path = null)
    {
        var file = path ?? DefaultPath;
        if (!File.Exists(file)) return [];
        var found = new List<(string, string)>();
        foreach (var line in File.ReadAllLines(file))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            found.Add((line[..tab], line[(tab + 1)..]));
        }
        File.Delete(file);
        return found;
    }
}
