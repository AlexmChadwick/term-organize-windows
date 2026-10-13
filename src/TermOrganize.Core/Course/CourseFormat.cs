// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;

namespace TermOrganize.Core;

public static class CourseFormat
{
    /// <summary>Sep 9 for an ISO yyyy-MM-dd string.</summary>
    public static string? ShortDate(string? iso)
    {
        if (iso is null || !DateOnly.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        return date.ToString("MMM d", CultureInfo.CurrentCulture);
    }

    /// <summary>Sep 9 – Dec 13, or a single date when both ends match or only one is known.</summary>
    public static string? DateRange(string? start, string? end)
    {
        var first = ShortDate(start);
        var last = ShortDate(end);
        return (first, last) switch
        {
            (not null, not null) => first == last ? first : $"{first} \u2013 {last}",
            (not null, null) => first,
            (null, not null) => last,
            _ => null,
        };
    }

    /// <summary>5 minutes ago, yesterday, now. Future dates read as now.</summary>
    public static string Relative(DateTimeOffset date, DateTimeOffset? now = null)
    {
        var moment = now ?? DateTimeOffset.UtcNow;
        if (date >= moment) return "now";
        var elapsed = moment - date;
        if (elapsed.TotalSeconds < 45) return "now";
        if (elapsed.TotalMinutes < 2) return "1 minute ago";
        if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes} minutes ago";
        if (elapsed.TotalHours < 2) return "1 hour ago";
        if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours} hours ago";
        var days = (int)elapsed.TotalDays;
        if (days == 1) return "yesterday";
        if (days < 7) return $"{days} days ago";
        if (days < 14) return "last week";
        if (days < 30) return $"{days / 7} weeks ago";
        if (days < 60) return "last month";
        if (days < 365) return $"{days / 30} months ago";
        if (days < 730) return "last year";
        return $"{days / 365} years ago";
    }

    public static string Count(int number, string singular, string? plural = null) =>
        $"{number.ToString(CultureInfo.CurrentCulture)} {(number == 1 ? singular : plural ?? singular + "s")}";
}

/// <summary>Deep links understood by the app router.</summary>
public static class CourseLinks
{
    public static Uri Course(string termCode, CourseKey key) =>
        new($"{AppLink.Scheme}://course/{Uri.EscapeDataString(termCode)}/{Uri.EscapeDataString(key.Subject)}/{Uri.EscapeDataString(key.Number)}");

    public static Uri Section(string termCode, string crn) =>
        new($"{AppLink.Scheme}://section/{Uri.EscapeDataString(termCode)}/{Uri.EscapeDataString(crn)}");
}
