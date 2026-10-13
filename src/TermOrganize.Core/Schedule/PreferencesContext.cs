// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// What the survey needs to know about the schedule it belongs to.

using System.Globalization;

namespace TermOrganize.Core;

/// <summary>An instructor teaching one of the plan's courses, for the "prefer or avoid" list.</summary>
public sealed class ScheduleInstructorOption : IEquatable<ScheduleInstructorOption>
{
    /// <summary>Banner's name (<c>Last, First</c>): what preferences store.</summary>
    public required string Name { get; init; }
    /// <summary><c>First Last</c>.</summary>
    public required string DisplayName { get; init; }
    public List<CourseKey> Courses { get; init; } = [];
    public string Id => Name;

    public bool Equals(ScheduleInstructorOption? other) => other is not null && Name == other.Name;
    public override bool Equals(object? obj) => Equals(obj as ScheduleInstructorOption);
    public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);
}

/// <summary>Campuses to choose from, the instructors of the plan's courses, and how many credits they add up to.</summary>
public sealed class SchedulePreferencesContext
{
    public List<string> CampusNames { get; set; } = [];
    public List<ScheduleInstructorOption> Instructors { get; set; } = [];
    /// <summary>Credits only matter when some courses are optional.</summary>
    public int OptionalCourseCount { get; set; }
    /// <summary>What the required courses are worth at least, and what every course is worth at most.</summary>
    public int RequiredCredits { get; set; }
    public int TotalCredits { get; set; }

    public SchedulePreferencesContext() { }

    public SchedulePreferencesContext(SchedulePlan plan, CatalogIndex? index)
    {
        if (index is null) return;
        CampusNames = index.CampusNames
            .Where(name => !name.Contains("online", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("no campus", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var byName = new Dictionary<string, ScheduleInstructorOption>(StringComparer.Ordinal);
        foreach (var planCourse in plan.Courses)
        {
            var course = index.Course(planCourse.Key);
            var least = (int)Math.Ceiling(course?.MinCredits ?? 0);
            var most = (int)Math.Ceiling(course?.MaxCredits ?? 0);
            if (planCourse.IsRequired) RequiredCredits += least;
            else OptionalCourseCount++;
            TotalCredits += Math.Max(least, most);
            foreach (var section in index.Sections(planCourse.Key).Where(section => !planCourse.ExcludedCRNs.Contains(section.Crn)))
            {
                foreach (var instructor in section.Instructors ?? [])
                {
                    if (!byName.TryGetValue(instructor.Name, out var option))
                    {
                        option = new ScheduleInstructorOption { Name = instructor.Name, DisplayName = instructor.DisplayName };
                        byName[instructor.Name] = option;
                    }
                    if (!option.Courses.Contains(planCourse.Key)) option.Courses.Add(planCourse.Key);
                }
            }
        }
        Instructors = byName.Values
            .OrderBy(option => option.Name, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
            .ToList();
    }
}
