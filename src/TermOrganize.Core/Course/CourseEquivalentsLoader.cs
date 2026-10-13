// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// Which courses still need a catalog lookup before completed courses can count for them.
/// Equivalences come from Banner's catalog entries, which are only known for courses that were asked about.
/// </summary>
public static class CourseEquivalentsLoader
{
    public readonly record struct Request(bool Needed, IReadOnlyList<CourseKey> Keys);

    /// <summary>
    /// Indexes are newest term first. Courses the newest term already lists need nothing.
    /// The requested courses are the ones the screen names that aren't completed, plus completed
    /// courses the newest loaded term doesn't list.
    /// </summary>
    public static Request Select(
        IReadOnlyList<CatalogIndex> indexesNewestFirst,
        IReadOnlyList<CourseKey> courses,
        CompletedCourses completed,
        bool hasDegreePlan)
    {
        if (!hasDegreePlan || indexesNewestFirst.Count == 0) return new Request(false, []);
        var newest = indexesNewestFirst[0];
        var unmet = courses.Where(key => !completed.Covered.Contains(key)).ToHashSet();
        if (unmet.Count == 0) return new Request(false, []);
        var wanted = unmet.Where(key => newest.Course(key) is null).ToHashSet();
        foreach (var taken in completed.Taken)
        {
            if (newest.Course(taken) is null) wanted.Add(taken);
        }
        return new Request(wanted.Count > 0, wanted.OrderBy(key => key).ToList());
    }
}
