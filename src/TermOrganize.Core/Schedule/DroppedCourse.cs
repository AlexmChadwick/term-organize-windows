// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// A course, and optionally one of its sections, dropped on a schedule from a course table.

namespace TermOrganize.Core;

/// <summary>One <c>CS 3000</c> or <c>CS 3000 12345</c> (course and CRN) per line of dragged text.</summary>
public readonly record struct ScheduleDroppedCourse(CourseKey Key, string? Crn)
{
    public static ScheduleDroppedCourse? ParseLine(string line)
    {
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var key = CourseKey.Parse($"{parts[0]} {parts[1]}");
        if (key is not CourseKey course || !course.Number.Any(char.IsDigit)) return null;
        string? crn = null;
        if (parts.Length >= 3 && parts[2].Length == 5 && parts[2].All(char.IsDigit)) crn = parts[2];
        return new ScheduleDroppedCourse(course, crn);
    }

    /// <summary>Every course in the dragged text, in order and without repeats. Text that isn't a course is skipped.</summary>
    public static List<ScheduleDroppedCourse> Parse(IEnumerable<string> items)
    {
        var seen = new HashSet<ScheduleDroppedCourse>();
        var result = new List<ScheduleDroppedCourse>();
        foreach (var item in items)
        {
            foreach (var line in item.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (ParseLine(line) is not ScheduleDroppedCourse course) continue;
                if (seen.Add(course)) result.Add(course);
            }
        }
        return result;
    }
}
