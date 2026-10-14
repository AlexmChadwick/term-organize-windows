// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text;

namespace TermOrganize.Core;

public static class IcsWriter
{
    public static string Write(string calendarName, Term term, IEnumerable<CourseSection> sections)
    {
        var (start, end) = TermCalendar.Window(term);
        var builder = new StringBuilder();
        void Line(string text) => AppendFolded(builder, text);
        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Term Organize//Schedule//EN");
        Line("CALSCALE:GREGORIAN");
        Line("X-WR-CALNAME:" + Escape(calendarName));
        Line("X-WR-TIMEZONE:America/New_York");
        foreach (var section in sections)
        {
            foreach (var meeting in section.WeeklyMeetings)
            {
                if (meeting.Start is not int startMin || meeting.End is not int endMin) continue;
                var first = FirstDate(start, meeting.Days);
                if (first is null) continue;
                // RFC 5545: when DTSTART carries a TZID, UNTIL is UTC and ends with Z.
                var until = UntilUtc(end);
                Line("BEGIN:VEVENT");
                Line("UID:" + section.Crn + "-" + meeting.Days.Raw + "-" + startMin + "@termorganize");
                Line("SUMMARY:" + Escape($"{section.CourseKey} {section.Title}"));
                Line("LOCATION:" + Escape(meeting.LongLocationLabel));
                Line("DESCRIPTION:" + Escape($"CRN {section.Crn} · {section.ClassType} · {section.InstructorLabel}"));
                Line("DTSTART;TZID=America/New_York:" + Stamp(first.Value, startMin));
                Line("DTEND;TZID=America/New_York:" + Stamp(first.Value, endMin));
                Line("RRULE:FREQ=WEEKLY;BYDAY=" + ByDay(meeting.Days) + ";UNTIL=" + until);
                Line("END:VEVENT");
            }
        }
        Line("END:VCALENDAR");
        return builder.ToString().Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    /// <summary>Folds at 75 octets. The newline is a bare LF so the final CRLF pass does not double the CR.</summary>
    private static void AppendFolded(StringBuilder builder, string text)
    {
        var column = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (column > 0 && column + size > 75)
            {
                builder.Append('\n').Append(' ');
                column = 1;
            }
            builder.Append(rune.ToString());
            column += size;
        }
        builder.Append('\n');
    }

    /// <summary>23:59:59 America/New_York on the last day of the term, written as UTC.</summary>
    private static string UntilUtc(DateOnly end)
    {
        var local = new DateTime(end.Year, end.Month, end.Day, 23, 59, 59, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, TermCalendar.NewYork);
        return utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }

    private static DateOnly? FirstDate(DateOnly start, DaySet days)
    {
        for (var i = 0; i < 7; i++)
        {
            var day = start.AddDays(i);
            if (days.Contains((int)day.DayOfWeek)) return day;
        }
        return null;
    }

    private static string ByDay(DaySet days)
    {
        string[] names = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];
        return string.Join(',', days.Indices.Select(i => names[i]));
    }

    private static string Stamp(DateOnly day, int minutes) =>
        $"{day:yyyyMMdd}T{(minutes / 60):00}{(minutes % 60):00}00";

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}
