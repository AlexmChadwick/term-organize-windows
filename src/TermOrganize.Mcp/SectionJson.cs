// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Mcp;

public static class SectionJson
{
    public static Dictionary<string, object?> Full(CourseSection section) => new()
    {
        ["crn"] = section.Crn,
        ["section"] = section.SectionNumber,
        ["days"] = Days(section),
        ["times"] = Times(section),
        ["location"] = Location(section),
        ["instructors"] = Names(section),
        ["seats_remaining"] = section.SeatsRemaining,
        ["seat_capacity"] = section.SeatCapacity,
    };

    public static Dictionary<string, object?> Brief(CourseSection section) => new()
    {
        ["crn"] = section.Crn,
        ["section"] = section.SectionNumber,
        ["days"] = Days(section),
        ["times"] = Times(section),
        ["location"] = Location(section),
        ["instructors"] = Names(section),
    };

    public static Dictionary<string, object?> Picked(CourseSection section) => new()
    {
        ["crn"] = section.Crn,
        ["subject"] = section.Subject,
        ["number"] = section.Number,
        ["days"] = Days(section),
        ["times"] = Times(section),
    };

    private static string Days(CourseSection section) => section.MeetingDays.CompactLabel;

    private static string Times(CourseSection section)
    {
        var weekly = section.WeeklyMeetings.ToList();
        if (weekly.Count == 0) return section.IsOnline ? "Asynchronous / online" : "Times TBA";
        return string.Join(", ", weekly.Select(meeting =>
            meeting.Start is int start && meeting.End is int end
                ? $"{meeting.Days.CompactLabel} {TextFormat.TimeRange(start, end)}"
                : "Times TBA"));
    }

    private static string Location(CourseSection section)
    {
        var weekly = section.WeeklyMeetings.ToList();
        if (weekly.Count == 0) return section.IsOnline ? "Online" : "Location TBA";
        return string.Join(", ", weekly.Select(meeting => TextFormat.Sanitize(meeting.LocationLabel)).Distinct());
    }

    private static List<string> Names(CourseSection section) =>
        (section.Instructors ?? [])
            .OrderByDescending(instructor => instructor.Primary)
            .Select(instructor => TextFormat.Sanitize(instructor.DisplayName))
            .Where(name => name.Length > 0)
            .ToList();
}
