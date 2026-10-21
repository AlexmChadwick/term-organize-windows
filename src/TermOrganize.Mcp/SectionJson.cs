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
        ["instructors"] = InstructorRows(section),
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

    /// <summary>Name, plus email only when the domain is exactly northeastern.edu or neu.edu.</summary>
    public static List<Dictionary<string, object?>> InstructorRows(CourseSection section)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (var instructor in (section.Instructors ?? []).OrderByDescending(item => item.Primary))
        {
            var name = TextFormat.Sanitize(instructor.DisplayName);
            if (name.Length == 0) continue;
            var row = new Dictionary<string, object?> { ["name"] = name };
            if (AllowedEmail(instructor.Email) is string email)
                row["email"] = email;
            rows.Add(row);
        }
        return rows;
    }

    public static string? AllowedEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var trimmed = email.Trim();
        var at = trimmed.LastIndexOf('@');
        if (at <= 0 || at != trimmed.IndexOf('@') || at == trimmed.Length - 1) return null;
        var domain = trimmed[(at + 1)..];
        return TextFormat.IsAllowedEmailDomain(domain) ? trimmed : null;
    }
}
