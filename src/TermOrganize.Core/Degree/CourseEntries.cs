// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// When a degree plan may list one subject and number more than once.
/// A second listing is kept only when every listing of that course has a manually
/// selected CRN and the CRNs differ, as with special topics. The same CRN twice is blocked.
/// </summary>
public static class DegreeCourseEntries
{
    public static bool Allows(DegreePlan plan, CourseKey key, string? crn, Guid? ignoreId = null)
    {
        var others = Others(plan, key, ignoreId);
        if (others.Count == 0) return true;
        var incoming = DegreeCourseRepeats.CleanCrn(crn);
        if (incoming is null) return false;
        foreach (var other in others)
        {
            var existing = DegreeCourseRepeats.CleanCrn(other.SelectedCrn);
            if (existing is null || string.Equals(existing, incoming, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static List<DegreePlanCourse> Others(DegreePlan plan, CourseKey key, Guid? ignoreId)
    {
        var others = new List<DegreePlanCourse>();
        foreach (var course in plan.Completed)
            Consider(course);
        foreach (var term in plan.Terms)
            foreach (var course in term.Courses)
                Consider(course);
        return others;

        void Consider(DegreePlanCourse course)
        {
            if (course.Key != key) return;
            if (ignoreId is Guid id && course.Id == id) return;
            others.Add(course);
        }
    }
}
