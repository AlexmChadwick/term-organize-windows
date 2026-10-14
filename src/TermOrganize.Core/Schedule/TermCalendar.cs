// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// Approximate Northeastern term windows in America/New_York. The last day is inclusive.
/// Fall 2026 (code 202710) starts in September 2026.
/// </summary>
public static class TermCalendar
{
    public static TimeZoneInfo NewYork { get; } = ResolveNewYork();

    public static (DateOnly Start, DateOnly End) Window(Term term)
    {
        var year = term.CalendarYear;
        return term.Season switch
        {
            TermSeason.Fall => (new DateOnly(year, 9, 1), new DateOnly(year, 12, 23)),
            TermSeason.Winter => (new DateOnly(year, 1, 1), new DateOnly(year, 3, 31)),
            TermSeason.Spring => (new DateOnly(year, 1, 3), new DateOnly(year, 4, 30)),
            TermSeason.Summer1 => (new DateOnly(year, 5, 1), new DateOnly(year, 6, 30)),
            TermSeason.SummerFull => (new DateOnly(year, 5, 1), new DateOnly(year, 8, 20)),
            TermSeason.Summer2 => (new DateOnly(year, 6, 25), new DateOnly(year, 8, 20)),
            _ => (new DateOnly(year, 9, 1), new DateOnly(year, 12, 23)),
        };
    }

    public static bool Contains(Term term, DateOnly day)
    {
        var (start, end) = Window(term);
        return day >= start && day <= end;
    }

    /// <summary>
    /// Northeastern class times are America/New_York wall times. Passing a UTC instant
    /// (or omitting it for now) keeps "right now" on the campus clock.
    /// </summary>
    public static DateTime Now(DateTimeOffset? utc = null)
    {
        var instant = utc ?? DateTimeOffset.UtcNow;
        var local = TimeZoneInfo.ConvertTime(instant, NewYork);
        return DateTime.SpecifyKind(local.DateTime, DateTimeKind.Unspecified);
    }

    public static (DateTimeOffset Start, DateTimeOffset End)? ApproximateWindow(string termCode)
    {
        var term = new Term(termCode, "");
        if (term.CalendarYear <= 0) return null;
        var (start, end) = Window(term);
        var startLocal = new DateTime(start.Year, start.Month, start.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var endLocal = new DateTime(end.Year, end.Month, end.Day, 0, 0, 0, DateTimeKind.Unspecified).AddDays(1).AddSeconds(-1);
        return (At(startLocal), At(endLocal));
    }

    public static int MonthIndex(string termCode)
    {
        var term = new Term(termCode, "");
        return term.CalendarYear * 12 + PlanSeasonInfo.FromTerm(term.Season).StartMonth() - 1;
    }

    private static DateTimeOffset At(DateTime local) => new(local, NewYork.GetUtcOffset(local));

    private static TimeZoneInfo ResolveNewYork()
    {
        foreach (var id in new[] { "America/New_York", "US/Eastern", "Eastern Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
