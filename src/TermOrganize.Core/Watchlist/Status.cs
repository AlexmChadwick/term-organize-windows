// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>Availability of a watched section as of its last check.</summary>
public enum WatchlistStatus
{
    Open,
    Waitlist,
    Full,
    /// <summary>Banner no longer lists the section.</summary>
    Missing,
}

public static class WatchlistStatusInfo
{
    public static WatchlistStatus For(WatchItem item, bool isMissing)
    {
        if (isMissing) return WatchlistStatus.Missing;
        if (item.LastSeatsRemaining > 0) return WatchlistStatus.Open;
        if (item.LastWaitlistRemaining > 0) return WatchlistStatus.Waitlist;
        return WatchlistStatus.Full;
    }

    public static string Symbol(this WatchlistStatus status) => status switch
    {
        WatchlistStatus.Open => "checkmark.circle.fill",
        WatchlistStatus.Waitlist => "hourglass.circle.fill",
        WatchlistStatus.Full => "xmark.circle.fill",
        WatchlistStatus.Missing => "questionmark.circle.fill",
        _ => "",
    };

    public static string ColorName(this WatchlistStatus status) => status switch
    {
        WatchlistStatus.Open => "green",
        WatchlistStatus.Waitlist => "orange",
        WatchlistStatus.Full => "red",
        WatchlistStatus.Missing => "secondary",
        _ => "",
    };

    /// <summary>One line describing the numbers, e.g. 3 of 40 open.</summary>
    public static string Detail(this WatchlistStatus status, WatchItem item) => status switch
    {
        WatchlistStatus.Open => item.SeatCapacity > 0
            ? $"{item.LastSeatsRemaining} of {item.SeatCapacity} open"
            : $"{item.LastSeatsRemaining} open",
        WatchlistStatus.Waitlist => $"Full · {item.LastWaitlistRemaining} waitlist {(item.LastWaitlistRemaining == 1 ? "spot" : "spots")} open",
        WatchlistStatus.Full => "Full",
        WatchlistStatus.Missing => "No longer listed",
        _ => "",
    };

    /// <summary>The same numbers phrased to be read aloud: 3 of 40 seats open.</summary>
    public static string SpokenDetail(this WatchlistStatus status, WatchItem item)
    {
        if (status != WatchlistStatus.Open) return status.Detail(item);
        var seats = item.LastSeatsRemaining;
        return item.SeatCapacity > 0
            ? $"{seats} of {item.SeatCapacity} seats open"
            : $"{seats} {(seats == 1 ? "seat" : "seats")} open";
    }
}

public static class WatchlistFormat
{
    /// <summary>just now, 5 min ago, 3 hr ago, yesterday, 4 days ago.</summary>
    public static string Ago(DateTimeOffset date, DateTimeOffset now)
    {
        var seconds = Math.Max(0, (now - date).TotalSeconds);
        if (seconds < 60) return "just now";
        if (seconds < 3_600) return $"{(int)(seconds / 60)} min ago";
        if (seconds < 86_400) return $"{(int)(seconds / 3_600)} hr ago";
        var days = (int)(seconds / 86_400);
        return days == 1 ? "yesterday" : $"{days} days ago";
    }
}
