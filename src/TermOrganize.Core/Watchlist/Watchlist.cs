// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Seat-check comparison for the tray and the watchlist screen.
// UNUserNotificationCenter is not used; WatchlistNotifications builds the title, body, and thread
// a notifier would post. Delivery stays behind IWatchlistNotifier.

namespace TermOrganize.Core;

public static class WatchlistCheck
{
    /// <summary>
    /// Compares watched sections with a live fetch. An alert is raised when seats open after a
    /// baseline of zero, or when an opted-in waitlist opens while the section is still full.
    /// Terms absent from <paramref name="liveSections"/> are left untouched.
    /// </summary>
    public static WatchlistComparison.Outcome Compare(
        IReadOnlyList<WatchItem> items,
        IReadOnlyDictionary<string, IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>> liveSections,
        DateTimeOffset? now = null) =>
        WatchlistComparison.Compare(items, liveSections, now);
}
