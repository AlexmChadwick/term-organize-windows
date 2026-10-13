// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// The next class to show in the tray. Scans the chosen sections; no widget file is written.

using System.Globalization;

namespace TermOrganize.Core;

public static class UpNext
{
    public abstract record Result
    {
        public sealed record Class(Item Meeting) : Result;
        public sealed record TermEnded : Result;
        public sealed record NoClasses : Result;
        public sealed record Unavailable : Result;
    }

    public sealed class Item
    {
        public required string CourseCode { get; init; }
        public required string ClassType { get; init; }
        public required string Location { get; init; }
        public int StartMinutes { get; init; }
        public int EndMinutes { get; init; }
        public DateTime Start { get; init; }
        public DateTime End { get; init; }
        public DateTime Now { get; init; }

        public bool IsInProgress => Start <= Now && Now < End;

        public string? DayLabel
        {
            get
            {
                var daysAway = (Start.Date - Now.Date).Days;
                return daysAway switch
                {
                    < 1 => null,
                    1 => "Tomorrow",
                    >= 2 and <= 6 => Start.ToString("ddd", CultureInfo.InvariantCulture),
                    _ => Start.ToString("MMM d", CultureInfo.InvariantCulture),
                };
            }
        }

        public string? LocationText =>
            string.IsNullOrEmpty(Location) || Location == "Location TBA" ? null : Location;

        public string ExpandedText(bool full = true, bool compact = false)
        {
            var parts = new List<string>();
            if (full) parts.Add(IsInProgress ? "In class" : "Up next");
            parts.Add(CourseCode);
            parts.Add(IsInProgress ? $"until {TimeOfDay.Label(EndMinutes)}" : TimeOfDay.Label(StartMinutes));
            if (!compact && LocationText is string location) parts.Add(location);
            return string.Join(" · ", parts);
        }

        public string InlineText =>
            $"{CourseCode} · {(IsInProgress ? "now" : TimeOfDay.Label(StartMinutes, includePeriod: false))}";

        public string RelativeText
        {
            get
            {
                var seconds = (Start - Now).TotalSeconds;
                if (seconds <= 0) return "now";
                if (DayLabel is string day) return day;
                var minutes = (int)Math.Ceiling(seconds / 60);
                if (minutes < 60) return $"in {minutes} min";
                var hours = minutes / 60;
                var rest = minutes % 60;
                return rest == 0 ? $"in {hours}h" : $"in {hours}h {rest}m";
            }
        }

        public string AccessibilityText
        {
            get
            {
                var text = IsInProgress ? "In class: " : "Up next: ";
                text += $"{CourseCode} {ClassType}";
                text += IsInProgress ? $", until {TimeOfDay.Label(EndMinutes)}" : $", at {TimeOfDay.Label(StartMinutes)}";
                if (LocationText is string location) text += $", {location}";
                if (!IsInProgress)
                    text += DayLabel is string day ? $", {day}" : $", {RelativeText}";
                return text;
            }
        }
    }

    /// <summary>The next class for a plan whose term matches <paramref name="index"/>.</summary>
    public static Result Find(SchedulePlan? plan, CatalogIndex? index, DateTime now)
    {
        if (plan is null || index is null || index.Term.Code != plan.TermCode) return new Result.Unavailable();
        var data = ScheduleWidgetBuilder.Make(plan, index, now);
        if (data.Meetings.Count == 0) return new Result.NoClasses();
        if (data.ClassesEnd is DateTime end && now.Date > end.Date) return new Result.TermEnded();
        var anchor = now;
        if (data.ClassesStart is DateTime start && start > now) anchor = start;
        if (data.Occurrences(anchor, 1).FirstOrDefault() is not WidgetScheduleData.Occurrence next) return new Result.NoClasses();
        return new Result.Class(new Item
        {
            CourseCode = next.Meeting.CourseCode,
            ClassType = next.Meeting.ClassType,
            Location = next.Meeting.Location,
            StartMinutes = next.Meeting.Start,
            EndMinutes = next.Meeting.End,
            Start = next.Start,
            End = next.End,
            Now = now,
        });
    }

    /// <summary>The next (or in-progress) meeting among <paramref name="sections"/>, as of <paramref name="now"/>.</summary>
    public static Result Next(IReadOnlyList<CourseSection>? sections, DateTime now)
    {
        if (sections is null) return new Result.Unavailable();
        var meetings = Flatten(sections);
        if (meetings.Count == 0) return new Result.NoClasses();
        var classesEnd = meetings.Select(meeting => ScheduleDates.Date(meeting.EndDate)).Where(date => date is not null).Select(date => date!.Value).DefaultIfEmpty().Max();
        var hasEnd = meetings.Any(meeting => meeting.EndDate is not null);
        if (hasEnd && now.Date > classesEnd.Date) return new Result.TermEnded();

        var anchor = now;
        var starts = meetings.Select(meeting => ScheduleDates.Date(meeting.StartDate)).Where(date => date is not null).Select(date => date!.Value).ToList();
        if (starts.Count > 0)
        {
            var classesStart = starts.Min();
            if (classesStart > now) anchor = classesStart;
        }
        if (Occurrence(meetings, anchor, now) is not Item item) return new Result.NoClasses();
        return new Result.Class(item);
    }

    private sealed record Flat(string CourseCode, string ClassType, string Location, int Weekday, int Start, int End, string? StartDate, string? EndDate);

    private static List<Flat> Flatten(IReadOnlyList<CourseSection> sections)
    {
        var meetings = new List<Flat>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            foreach (var meeting in section.WeeklyMeetings)
            {
                if (meeting.Start is not int start || meeting.End is not int end) continue;
                var location = meeting.IsOnline || section.IsOnline ? "Online" : meeting.LocationLabel;
                foreach (var day in meeting.Days.Indices)
                {
                    var key = $"{section.Crn}|{day}|{start}|{end}|{meeting.StartDate}|{meeting.EndDate}";
                    if (!seen.Add(key)) continue;
                    meetings.Add(new Flat(section.CourseKey.ToString(), section.ClassType, location, day, start, end, meeting.StartDate, meeting.EndDate));
                }
            }
        }
        return meetings;
    }

    private static Item? Occurrence(List<Flat> meetings, DateTime anchor, DateTime now)
    {
        var startOfToday = anchor.Date;
        for (var dayOffset = 0; dayOffset < 14; dayOffset++)
        {
            var day = startOfToday.AddDays(dayOffset);
            var classesEnd = meetings.Where(meeting => meeting.EndDate is not null).Select(meeting => ScheduleDates.Date(meeting.EndDate)!.Value).DefaultIfEmpty().Max();
            if (meetings.Any(meeting => meeting.EndDate is not null) && day.Date > classesEnd.Date) break;
            var starts = meetings.Where(meeting => meeting.StartDate is not null).Select(meeting => ScheduleDates.Date(meeting.StartDate)!.Value);
            if (starts.Any() && day.Date < starts.Min().Date) continue;
            var weekday = (int)day.DayOfWeek;
            foreach (var meeting in meetings.Where(item => item.Weekday == weekday && IsActive(item, day)).OrderBy(item => item.Start))
            {
                var start = day.AddMinutes(meeting.Start);
                var end = day.AddMinutes(meeting.End);
                if (end <= anchor) continue;
                return new Item
                {
                    CourseCode = meeting.CourseCode,
                    ClassType = meeting.ClassType,
                    Location = meeting.Location,
                    StartMinutes = meeting.Start,
                    EndMinutes = meeting.End,
                    Start = start,
                    End = end,
                    Now = now,
                };
            }
        }
        return null;
    }

    private static bool IsActive(Flat meeting, DateTime day)
    {
        if (meeting.StartDate is null && meeting.EndDate is null) return true;
        var iso = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (meeting.StartDate is not null && string.CompareOrdinal(iso, meeting.StartDate) < 0) return false;
        if (meeting.EndDate is not null && string.CompareOrdinal(iso, meeting.EndDate) > 0) return false;
        return true;
    }
}
