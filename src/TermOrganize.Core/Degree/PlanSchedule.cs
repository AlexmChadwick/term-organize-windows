// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>A schedule opened or created for one semester of a degree plan.</summary>
public readonly record struct DegreeScheduleBuild(SchedulePlan Plan, bool IsNew, int CourseCount);

/// <summary>Builds a term schedule from a degree-plan semester.</summary>
public static class DegreePlanSchedule
{
    /// <summary>
    /// The schedule to open for <paramref name="semester"/> in a Banner term: one that already
    /// holds exactly those courses, otherwise a new one named like the semester. Null when the
    /// semester has no catalog courses.
    /// </summary>
    public static DegreeScheduleBuild? ForSemester(DegreePlanTerm semester, string termCode, UserDataStore store)
    {
        var seen = new HashSet<CourseKey>();
        var keys = new List<CourseKey>();
        foreach (var course in semester.Courses)
        {
            if (course.Key is not CourseKey key || !seen.Add(key)) continue;
            keys.Add(key);
        }
        if (keys.Count == 0) return null;

        var wanted = keys.ToHashSet();
        var existing = store.PlansIn(termCode).FirstOrDefault(plan =>
            plan.Courses.Select(course => course.Key).ToHashSet().SetEquals(wanted));
        if (existing is not null)
        {
            store.UpdatePlan(existing.Id, plan => CopyPins(plan, semester.Courses));
            return new DegreeScheduleBuild(store.Plan(existing.Id) ?? existing, false, keys.Count);
        }

        var name = DegreePlanEditing.ScheduleName(semester, store.PlansIn(termCode).Select(plan => plan.Name));
        var schedule = store.CreatePlan(name, termCode);
        foreach (var key in keys) store.AddCourse(key, schedule.Id);
        store.UpdatePlan(schedule.Id, plan => CopyPins(plan, semester.Courses));
        return new DegreeScheduleBuild(store.Plan(schedule.Id) ?? schedule, true, keys.Count);
    }

    /// <summary>
    /// Copies each distinct manual CRN onto the schedule without removing a CRN already selected there.
    /// A course that is not on the schedule is left alone.
    /// </summary>
    public static int CopyPins(SchedulePlan schedule, IEnumerable<DegreePlanCourse> courses)
    {
        var added = 0;
        foreach (var course in courses)
        {
            if (course.Key is not CourseKey key || !schedule.Contains(key)) continue;
            var pin = DegreeCourseRepeats.CleanCrn(course.SelectedCrn);
            if (pin is null || schedule.SelectedCRNs.Contains(pin)) continue;
            schedule.SelectedCRNs.Add(pin);
            added++;
        }
        return added;
    }
}
