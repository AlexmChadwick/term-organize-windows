// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Display helpers on a section shared by the course, section and professor screens.

namespace TermOrganize.Core;

public static class CourseSectionDisplay
{
    /// <summary>The campus for display, or null for Banner's placeholder campuses.</summary>
    public static string? CourseDisplayCampus(this CourseSection section)
    {
        var trimmed = section.Campus.Trim();
        if (trimmed.Length == 0 || trimmed.Contains("no campus", StringComparison.OrdinalIgnoreCase)) return null;
        return trimmed;
    }

    /// <summary>In person, Online, Hybrid…</summary>
    public static string? CourseInstructionLabel(this CourseSection section)
    {
        var method = section.InstructionMethod?.Trim();
        if (string.IsNullOrEmpty(method)) return null;
        return method.Equals("Traditional", StringComparison.OrdinalIgnoreCase) ? "In person" : method;
    }

    /// <summary>Boston · In person, without repeating identical parts.</summary>
    public static string? CourseWhereLabel(this CourseSection section)
    {
        var campus = section.CourseDisplayCampus();
        // "Traditional" with no campus means arranged individually, not "in person".
        var isTraditional = section.InstructionMethod?.Equals("Traditional", StringComparison.OrdinalIgnoreCase) ?? false;
        var method = campus is null && isTraditional ? null : section.CourseInstructionLabel();
        var parts = new List<string>();
        foreach (var part in new[] { campus, method })
        {
            if (part is null) continue;
            if (parts.Any(existing => existing.Equals(part, StringComparison.OrdinalIgnoreCase))) continue;
            parts.Add(part);
        }
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>Instructors with the primary one first, without duplicates, or null while they have not been loaded.</summary>
    public static List<Instructor>? CourseSortedInstructors(this CourseSection section)
    {
        if (section.Instructors is null) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return section.Instructors
            .Select((instructor, index) => (instructor, index))
            .OrderByDescending(pair => pair.instructor.Primary)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.instructor)
            .Where(instructor => seen.Add(instructor.Name))
            .ToList();
    }

    /// <summary>CRNs of cross-listed sections, without duplicates or this section itself.</summary>
    public static List<string> CourseCrossListedCrns(this CourseSection section)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { section.Crn };
        return section.CrossListCRNs.Where(crn => seen.Add(crn)).ToList();
    }

    /// <summary>Attribute codes without duplicates, in Banner order.</summary>
    public static List<string> CourseUniqueAttributes(this CourseSection section)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return section.Attributes.Where(code => seen.Add(code)).ToList();
    }

    /// <summary>Part of term 2A · Sep 9 – Oct 25, or null for full-term sections.</summary>
    public static string? CoursePartOfTermLabel(this CourseSection section)
    {
        var code = section.PartOfTerm.Trim();
        if (code.Length == 0 || code == "1") return null;
        var starts = section.Meetings.Select(meeting => meeting.StartDate).Where(date => date is not null).ToList();
        var ends = section.Meetings.Select(meeting => meeting.EndDate).Where(date => date is not null).ToList();
        var range = CourseFormat.DateRange(starts.Count == 0 ? null : starts.Min(), ends.Count == 0 ? null : ends.Max());
        return range is null ? $"Part of term {code}" : $"Part of term {code} · {range}";
    }

    /// <summary>The special-topics description, when present and non-empty.</summary>
    public static string? CourseTopicDescription(this CourseSection section)
    {
        var text = section.Description?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Spoken seat summary, e.g. 12 of 40 seats open.</summary>
    public static string CourseSeatsSpoken(this CourseSection section)
    {
        if (section.SeatCapacity <= 0 && section.SeatsRemaining <= 0) return "No seat limit listed";
        if (section.SeatsRemaining > 0) return $"{section.SeatsRemaining} of {section.SeatCapacity} seats open";
        if (section.WaitlistCapacity > 0)
            return $"Full, {section.WaitlistRemaining} of {section.WaitlistCapacity} waitlist spots open";
        return "Full";
    }
}
