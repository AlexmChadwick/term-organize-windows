// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// Deep copies of a degree plan. <see cref="DegreePlan"/> is a class, so edits that
/// must succeed completely (and duplicates) copy before they change anything.
/// </summary>
internal static class DegreePlanCopy
{
    public static DegreePlan Clone(DegreePlan plan) => new()
    {
        Id = plan.Id,
        Name = plan.Name,
        Program = plan.Program,
        StudentName = plan.StudentName,
        StudentID = plan.StudentID,
        Completed = plan.Completed.Select(CloneCourse).ToList(),
        Terms = plan.Terms.Select(CloneTerm).ToList(),
        IsPrimary = plan.IsPrimary,
        ImportedFrom = plan.ImportedFrom,
        PrintedAt = plan.PrintedAt,
        CreatedAt = plan.CreatedAt,
        UpdatedAt = plan.UpdatedAt,
        CatalogYear = plan.CatalogYear,
        CatalogProgramPath = plan.CatalogProgramPath,
        CatalogProgramTitle = plan.CatalogProgramTitle,
        ConcentrationID = plan.ConcentrationID,
        Substitutions = plan.Substitutions.Select(CloneSubstitution).ToList(),
        Equivalences = plan.Equivalences.Select(CloneEquivalence).ToList(),
    };

    public static DegreePlanCourse CloneCourse(DegreePlanCourse course) =>
        new(course.Key, course.Title, course.Credits, course.Label, course.Id)
        {
            SelectedCrn = course.SelectedCrn,
        };

    public static DegreePlanTerm CloneTerm(DegreePlanTerm term) => new()
    {
        Id = term.Id,
        Season = term.Season,
        Year = term.Year,
        Courses = term.Courses.Select(CloneCourse).ToList(),
    };

    public static RequirementSubstitution CloneSubstitution(RequirementSubstitution source) => new()
    {
        Id = source.Id,
        RuleID = source.RuleID,
        RuleTitle = source.RuleTitle,
        Change = source.Change,
        Note = source.Note,
        ApprovedOn = source.ApprovedOn,
        CreatedAt = source.CreatedAt,
    };

    public static CourseEquivalence CloneEquivalence(CourseEquivalence source) => new()
    {
        Id = source.Id,
        Course = source.Course,
        CountsAs = source.CountsAs,
        Note = source.Note,
    };
}

/// <summary>Edits to a degree plan. Each method changes <paramref name="plan"/> in place.</summary>
public static class DegreePlanEditing
{
    public static void RemovePlannedCourse(Guid courseId, DegreePlan plan)
    {
        foreach (var term in plan.Terms)
            term.Courses.RemoveAll(course => course.Id == courseId);
    }

    public static void RemoveCompletedCourse(Guid courseId, DegreePlan plan) =>
        plan.Completed.RemoveAll(course => course.Id == courseId);

    public static bool Add(DegreePlanCourse course, Guid termId, DegreePlan plan)
    {
        if (!CanPlace(course, plan)) return false;
        var term = plan.Terms.FirstOrDefault(item => item.Id == termId);
        if (term is null) return false;
        term.Courses.Add(course);
        return true;
    }

    /// <summary>Appends a course to the plan's semester for that season and year, adding the semester first when needed.</summary>
    public static bool AddCourse(DegreePlanCourse course, DegreePlanTerm semester, DegreePlan plan)
    {
        if (!CanPlace(course, plan)) return false;
        AddTerm(semester, plan);
        var term = plan.Terms.FirstOrDefault(item => item.Season == semester.Season && item.Year == semester.Year);
        if (term is null) return false;
        term.Courses.Add(course);
        return true;
    }

    /// <summary>A repeated subject and number is placed only when each listing has a different manual CRN.</summary>
    private static bool CanPlace(DegreePlanCourse course, DegreePlan plan) =>
        course.Key is not CourseKey key || DegreeCourseEntries.Allows(plan, key, course.SelectedCrn);

    public static void ReplacePlannedCourse(Guid courseId, DegreePlanCourse replacement, DegreePlan plan)
    {
        foreach (var term in plan.Terms)
        {
            var position = term.Courses.FindIndex(course => course.Id == courseId);
            if (position < 0) continue;
            term.Courses[position] = replacement;
            return;
        }
    }

    public static void Move(Guid courseId, Guid termId, DegreePlan plan)
    {
        var destination = plan.Terms.FindIndex(term => term.Id == termId);
        if (destination < 0) return;
        var moved = TakePlannedCourse(courseId, plan);
        if (moved is null) return;
        plan.Terms[destination].Courses.Add(moved);
    }

    /// <summary>Moves a planned course to a position in a semester. 0 is the top. Dropping it on itself changes nothing.</summary>
    public static void Move(Guid courseId, Guid termId, int position, DegreePlan plan)
    {
        var destination = plan.Terms.FindIndex(term => term.Id == termId);
        if (destination < 0) return;
        var index = position;
        var current = plan.Terms[destination].Courses.FindIndex(course => course.Id == courseId);
        if (current >= 0 && current < position) index--;
        var moved = TakePlannedCourse(courseId, plan);
        if (moved is null) return;
        var courses = plan.Terms[destination].Courses;
        courses.Insert(Math.Min(Math.Max(0, index), courses.Count), moved);
    }

    public static void MarkCompleted(Guid courseId, DegreePlan plan)
    {
        var moved = TakePlannedCourse(courseId, plan);
        if (moved is null) return;
        if (moved.Key is CourseKey key && !DegreeCourseEntries.Allows(plan, key, moved.SelectedCrn)) return;
        plan.Completed.Add(moved);
    }

    public static void RemoveTerm(Guid termId, DegreePlan plan) =>
        plan.Terms.RemoveAll(term => term.Id == termId);

    public static void AddTerm(DegreePlanTerm term, DegreePlan plan)
    {
        if (plan.Terms.Any(item => item.Season == term.Season && item.Year == term.Year)) return;
        plan.Terms.Add(term);
        plan.SortTerms();
    }

    public static void CarryOverRequirements(DegreePlan existing, DegreePlan plan)
    {
        plan.CatalogYear = existing.CatalogYear;
        plan.CatalogProgramPath = existing.CatalogProgramPath;
        plan.CatalogProgramTitle = existing.CatalogProgramTitle;
        plan.ConcentrationID = existing.ConcentrationID;
        plan.Substitutions = existing.Substitutions;
        plan.IsPrimary = existing.IsPrimary;
    }

    public static void RemoveStudentInfo(DegreePlan plan)
    {
        plan.StudentName = null;
        plan.StudentID = null;
    }

    /// <summary>A copy with fresh plan, term, and course ids, named "<c>name copy</c>" (numbered when taken).</summary>
    public static DegreePlan Duplicate(DegreePlan plan, IEnumerable<string> existingNames)
    {
        var copy = DegreePlanCopy.Clone(plan);
        var now = DateTimeOffset.UtcNow;
        copy.Id = Guid.NewGuid();
        copy.Name = UniqueName($"{plan.Name} copy", existingNames);
        copy.IsPrimary = false;
        copy.ImportedFrom = null;
        copy.PrintedAt = null;
        copy.CreatedAt = now;
        copy.UpdatedAt = now;
        copy.Completed = plan.Completed.Select(FreshCourse).ToList();
        copy.Terms = plan.Terms.Select(term =>
        {
            var fresh = DegreePlanCopy.CloneTerm(term);
            fresh.Id = Guid.NewGuid();
            fresh.Courses = term.Courses.Select(FreshCourse).ToList();
            return fresh;
        }).ToList();
        return copy;
    }

    public static string UniqueName(string baseName, IEnumerable<string> existing)
    {
        var taken = existing.Select(name => name.Trim().ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var wanted = baseName.Trim();
        if (!taken.Contains(wanted.ToLowerInvariant())) return wanted;
        var number = 2;
        while (taken.Contains($"{wanted} {number}".ToLowerInvariant())) number++;
        return $"{wanted} {number}";
    }

    public static string DefaultNewPlanName(IEnumerable<string> existing) => UniqueName("New Plan", existing);

    public static string ScheduleName(DegreePlanTerm semester, IEnumerable<string> existing) =>
        UniqueName($"{semester.Name} plan", existing);

    /// <summary>An existing plan an import would replace: same name and program, latest update on a tie.</summary>
    public static DegreePlan? ExistingMatch(DegreePlan plan, string name, IEnumerable<DegreePlan> plans)
    {
        var wantedName = name.Trim();
        var wantedProgram = (plan.Program ?? "").Trim();
        return plans
            .Where(candidate =>
                string.Equals(candidate.Name.Trim(), wantedName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((candidate.Program ?? "").Trim(), wantedProgram, StringComparison.OrdinalIgnoreCase))
            .MaxBy(candidate => candidate.UpdatedAt);
    }

    private static DegreePlanCourse FreshCourse(DegreePlanCourse course)
    {
        var fresh = DegreePlanCopy.CloneCourse(course);
        fresh.Id = Guid.NewGuid();
        return fresh;
    }

    private static DegreePlanCourse? TakePlannedCourse(Guid courseId, DegreePlan plan)
    {
        foreach (var term in plan.Terms)
        {
            var position = term.Courses.FindIndex(course => course.Id == courseId);
            if (position < 0) continue;
            var course = term.Courses[position];
            term.Courses.RemoveAt(position);
            return course;
        }
        return null;
    }
}
