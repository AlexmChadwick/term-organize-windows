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
        var rows = semester.Courses.Where(course => course.Key is not null).ToList();
        if (rows.Count == 0) return null;

        var existing = store.PlansIn(termCode).FirstOrDefault(plan => RowsMatch(plan, rows));
        if (existing is not null)
        {
            store.UpdatePlan(existing.Id, plan => SyncRows(plan, rows));
            return new DegreeScheduleBuild(store.Plan(existing.Id) ?? existing, false, rows.Count);
        }

        var name = DegreePlanEditing.ScheduleName(semester, store.PlansIn(termCode).Select(plan => plan.Name));
        var schedule = store.CreatePlan(name, termCode);
        store.UpdatePlan(schedule.Id, plan => SyncRows(plan, rows));
        return new DegreeScheduleBuild(store.Plan(schedule.Id) ?? schedule, true, rows.Count);
    }

    /// <summary>
    /// Copies each degree-plan row onto the schedule. A repeated subject and number stays a second row.
    /// Returns how many rows were added and how many new CRNs were copied.
    /// </summary>
    public static (int Added, int Pins) SyncRows(SchedulePlan schedule, IEnumerable<DegreePlanCourse> courses)
    {
        var added = 0;
        var pins = 0;
        var used = new HashSet<int>();
        foreach (var course in courses)
        {
            if (course.Key is not CourseKey key) continue;
            var pin = DegreeCourseRepeats.CleanCrn(course.SelectedCrn);
            var index = MatchRow(schedule, course.Id, key, pin, used);
            if (index < 0)
            {
                schedule.Courses.Add(new PlanCourse
                {
                    Key = key,
                    IsRequired = true,
                    ColorIndex = schedule.NextColorIndex,
                    PinnedCrn = pin,
                    DegreeRowId = course.Id,
                });
                if (pin is not null && !schedule.SelectedCRNs.Contains(pin))
                {
                    schedule.SelectedCRNs.Add(pin);
                    pins++;
                }
                added++;
                continue;
            }
            used.Add(index);
            var row = schedule.Courses[index];
            row.DegreeRowId = course.Id;
            if (pin is null || string.Equals(DegreeCourseRepeats.CleanCrn(row.PinnedCrn), pin, StringComparison.Ordinal)) continue;
            row.PinnedCrn = pin;
            if (schedule.SelectedCRNs.Contains(pin)) continue;
            schedule.SelectedCRNs.Add(pin);
            pins++;
        }
        return (added, pins);
    }

    /// <summary>
    /// Copies each distinct manual CRN onto the matching schedule row without removing a CRN already selected there.
    /// A course that is not on the schedule is left alone.
    /// </summary>
    public static int CopyPins(SchedulePlan schedule, IEnumerable<DegreePlanCourse> courses)
    {
        var added = 0;
        var used = new HashSet<int>();
        foreach (var course in courses)
        {
            if (course.Key is not CourseKey key || !schedule.Contains(key)) continue;
            var pin = DegreeCourseRepeats.CleanCrn(course.SelectedCrn);
            if (pin is null) continue;
            var index = MatchRow(schedule, course.Id, key, pin, used);
            if (index < 0) continue;
            used.Add(index);
            var row = schedule.Courses[index];
            if (string.IsNullOrWhiteSpace(row.PinnedCrn)) row.PinnedCrn = pin;
            row.DegreeRowId ??= course.Id;
            if (schedule.SelectedCRNs.Contains(pin)) continue;
            schedule.SelectedCRNs.Add(pin);
            added++;
        }
        return added;
    }

    private static bool RowsMatch(SchedulePlan plan, List<DegreePlanCourse> rows)
    {
        if (plan.Courses.Count != rows.Count) return false;
        var left = plan.Courses
            .Select(course => (course.Key, DegreeCourseRepeats.CleanCrn(course.PinnedCrn)))
            .OrderBy(item => item.Key)
            .ThenBy(item => item.Item2, StringComparer.Ordinal)
            .ToList();
        var right = rows
            .Select(course => (course.Key!.Value, DegreeCourseRepeats.CleanCrn(course.SelectedCrn)))
            .OrderBy(item => item.Item1)
            .ThenBy(item => item.Item2, StringComparer.Ordinal)
            .ToList();
        return left.SequenceEqual(right);
    }

    private static int MatchRow(SchedulePlan schedule, Guid degreeRowId, CourseKey key, string? pin, HashSet<int> used)
    {
        for (var i = 0; i < schedule.Courses.Count; i++)
        {
            if (!used.Contains(i) && schedule.Courses[i].DegreeRowId == degreeRowId) return i;
        }
        var open = -1;
        for (var i = 0; i < schedule.Courses.Count; i++)
        {
            if (used.Contains(i)) continue;
            var row = schedule.Courses[i];
            if (row.Key != key || row.DegreeRowId is not null) continue;
            var existing = DegreeCourseRepeats.CleanCrn(row.PinnedCrn);
            if (pin is not null && string.Equals(existing, pin, StringComparison.Ordinal)) return i;
            if (existing is null && open < 0) open = i;
        }
        return open;
    }
}
