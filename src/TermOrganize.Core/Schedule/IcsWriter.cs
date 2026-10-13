// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;

namespace TermOrganize.Core;

public static class IcsWriter
{
    public static string Write(string calendarName, Term term, IEnumerable<CourseSection> sections)
    {
        var (start, end) = TermCalendar.Window(term);
        var builder = new StringBuilder();
        void Line(string text) => builder.Append(text).Append('\n');
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
                var until = end.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "T235959";
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
