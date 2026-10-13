// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Notification payloads for seat alerts. Delivery is the host's job; Windows has no
// UserNotifications center, so this builds the same content a banner would show.

namespace TermOrganize.Core;

/// <summary>One local notification, ready for the host to deliver.</summary>
public sealed class WatchlistNotification
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string Body { get; init; }
    public required string Identifier { get; init; }
    public required string ThreadIdentifier { get; init; }
    public int Badge { get; init; }
    public required IReadOnlyDictionary<string, string> UserInfo { get; init; }
    /// <summary>Null means deliver immediately. The test notification waits a few seconds.</summary>
    public TimeSpan? Delay { get; init; }
}

/// <summary>Turns seat alerts into notification payloads. A host may also deliver them.</summary>
public static class WatchlistNotifications
{
    public static List<WatchlistNotification> Content(IReadOnlyList<WatchlistAlert> alerts, int openCount)
    {
        var badge = Math.Max(1, openCount);
        var notes = new List<WatchlistNotification>(alerts.Count);
        foreach (var alert in alerts)
        {
            var userInfo = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["termCode"] = alert.TermCode,
                ["crn"] = alert.Crn,
                ["url"] = alert.Url.AbsoluteUri,
            };
            notes.Add(new WatchlistNotification
            {
                Title = alert.Title,
                Subtitle = string.IsNullOrEmpty(alert.CourseTitle) ? null : alert.CourseTitle,
                Body = alert.Body,
                Identifier = alert.Identifier,
                ThreadIdentifier = alert.ThreadIdentifier,
                Badge = badge,
                UserInfo = userInfo,
            });
        }
        return notes;
    }

    public static WatchlistNotification Test() => new()
    {
        Title = "Seat alerts are on",
        Body = "This is how you'll hear about open seats in the sections you watch.",
        Identifier = "watch-test",
        ThreadIdentifier = "watch-test",
        Badge = 0,
        Delay = TimeSpan.FromSeconds(3),
        UserInfo = new Dictionary<string, string> { ["url"] = $"{AppLink.Scheme}://watchlist" },
    };
}

/// <summary>Optional delivery. The library never posts a notification itself.</summary>
public interface IWatchlistNotifier
{
    bool IsAuthorized { get; }
    void Post(IReadOnlyList<WatchlistNotification> notifications);
    void ClearBadge();
    void RemoveAll();
}
