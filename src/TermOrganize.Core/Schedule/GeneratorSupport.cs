// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// Portions derived from SearchNEU where the Swift file says so. Modified 2026-10-13: ported from Swift to C#.

namespace TermOrganize.Core;

public static class ScheduleCourseInputs
{
    /// <summary>
    /// One search slot per plan course. Each manually selected CRN of that course is its own slot,
    /// so two special-topics sections are not merged into one choice. A pin the catalog no longer
    /// lists stays locked: that slot is empty and is reported unavailable, rather than opening every section.
    /// </summary>
    public static List<ScheduleCourseInput> Inputs(SchedulePlan plan, CatalogIndex index)
    {
        var result = new List<ScheduleCourseInput>();
        var onlyCourse = plan.Courses.Count == 1 ? plan.Courses[0] : null;
        foreach (var planCourse in plan.Courses)
        {
            var course = index.Course(planCourse.Key);
            var sections = index.Sections(planCourse.Key);
            var resolved = new List<string>();
            foreach (var crn in plan.SelectedCRNs)
            {
                var section = index.Section(crn);
                if (section is null || section.CourseKey != planCourse.Key) continue;
                if (resolved.Contains(section.Crn)) continue;
                resolved.Add(section.Crn);
            }
            var rowPin = DegreeCourseRepeats.CleanCrn(planCourse.PinnedCrn);
            var missingPins = new List<string>();
            if (rowPin is null && resolved.Count == 0 && ReferenceEquals(planCourse, onlyCourse))
            {
                missingPins = plan.SelectedCRNs
                    .Select(DegreeCourseRepeats.CleanCrn)
                    .Where(crn => crn is not null && index.Section(crn!) is null)
                    .Cast<string>()
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
            if (rowPin is not null && !resolved.Contains(rowPin))
                result.Add(Locked(planCourse, course, rowPin));
            foreach (var pin in missingPins)
                result.Add(Locked(planCourse, course, pin));
            if (resolved.Count > 0)
            {
                foreach (var pin in resolved)
                    result.Add(Input(planCourse.Key, planCourse.IsRequired, sections, planCourse.ExcludedCRNs, course, pin));
                continue;
            }
            if (rowPin is not null || missingPins.Count > 0) continue;
            result.Add(Input(planCourse.Key, planCourse.IsRequired, sections, planCourse.ExcludedCRNs, course, null));
        }
        return result;
    }

    /// <summary>One search slot per degree-plan course. A selected CRN locks that slot. Entries are not merged by subject and number.</summary>
    public static List<ScheduleCourseInput> ForDegreeCourses(IEnumerable<DegreePlanCourse> courses, CatalogIndex index)
    {
        var result = new List<ScheduleCourseInput>();
        foreach (var course in courses)
        {
            if (course.Key is not CourseKey key) continue;
            var listed = index.Course(key);
            var pin = DegreeCourseRepeats.CleanCrn(course.SelectedCrn);
            var sections = index.Sections(key);
            IReadOnlyList<CourseSection> usable = pin is null
                ? sections
                : sections.Where(section => string.Equals(section.Crn, pin, StringComparison.Ordinal)).ToList();
            result.Add(Input(key, true, usable, [], listed, pin));
        }
        return result;
    }

    private static ScheduleCourseInput Locked(PlanCourse planCourse, Course? course, string pin) =>
        Input(planCourse.Key, planCourse.IsRequired, [], planCourse.ExcludedCRNs, course, pin);

    private static ScheduleCourseInput Input(
        CourseKey key,
        bool isRequired,
        IReadOnlyList<CourseSection> sections,
        IEnumerable<string> excluded,
        Course? course,
        string? pinnedCrn) =>
        new(key, isRequired, sections, excluded, (int)Math.Ceiling(course?.MinCredits ?? 0), (int)Math.Ceiling(course?.MaxCredits ?? 0))
        {
            PinnedCrn = pinnedCrn,
        };
}

public static class GeneratedScheduleFavorites
{
    public static List<GeneratedSchedule> Favorites(SchedulePlan plan, CatalogIndex index, SchedulePreferences preferences)
    {
        var order = new Dictionary<CourseKey, int>();
        for (var i = 0; i < plan.Courses.Count; i++)
            order.TryAdd(plan.Courses[i].Key, i);
        var result = new List<GeneratedSchedule>();
        for (var i = plan.FavoriteCombinations.Count - 1; i >= 0; i--)
        {
            var crns = plan.FavoriteCombinations[i];
            var sections = new List<CourseSection>();
            var complete = true;
            foreach (var crn in crns)
            {
                var section = index.Section(crn);
                if (section is null)
                {
                    complete = false;
                    break;
                }
                sections.Add(section);
            }
            if (!complete || sections.Count != crns.Count || sections.Count == 0) continue;
            if (sections.Any(section => !order.ContainsKey(section.CourseKey))) continue;
            sections.Sort((a, b) => order[a.CourseKey].CompareTo(order[b.CourseKey]));
            result.Add(new GeneratedSchedule(sections, preferences));
        }
        return result;
    }
}

public static partial class PlanEditing
{
    public static void Apply(ScheduleRelaxation relaxation, SchedulePlan plan)
    {
        switch (relaxation)
        {
            case ScheduleRelaxation.MakeOptional optional:
                SetRequired(false, optional.Key, plan);
                break;
            case ScheduleRelaxation.PreferenceChange:
                var preferences = plan.Preferences ?? new SchedulePreferences();
                relaxation.Apply(preferences);
                plan.Preferences = preferences.IsDefault ? null : preferences;
                break;
            default:
                relaxation.Apply(plan.Filters);
                break;
        }
    }

    public static void MakeOptional(IEnumerable<CourseKey> keys, SchedulePlan plan)
    {
        foreach (var key in keys) SetRequired(false, key, plan);
    }
}

public enum ScheduleReorderOutcomeKind { Stale, Applied }

public sealed class ScheduleReorderOutcome
{
    public ScheduleReorderOutcomeKind Kind { get; }
    public Action? Undo { get; }

    private ScheduleReorderOutcome(ScheduleReorderOutcomeKind kind, Action? undo)
    {
        Kind = kind;
        Undo = undo;
    }

    public static ScheduleReorderOutcome Stale { get; } = new(ScheduleReorderOutcomeKind.Stale, null);
    public static ScheduleReorderOutcome Applied(Action undo) => new(ScheduleReorderOutcomeKind.Applied, undo);
    public bool IsStale => Kind == ScheduleReorderOutcomeKind.Stale;
}

public static class ScheduleReorderApplication
{
    public static ScheduleReorderOutcome Apply(ScheduleReorderOption option, Guid planId, CatalogIndex? index, UserDataStore userData)
    {
        var plan = userData.Plan(planId);
        var degreePlan = userData.PrimaryDegreePlan;
        if (plan is null || degreePlan is null) return ScheduleReorderOutcome.Stale;
        var trial = CloneDegreePlan(degreePlan);
        option.Apply(trial);
        if (SameTerms(trial, degreePlan)) return ScheduleReorderOutcome.Stale;
        var degreePlanBefore = CloneDegreePlan(degreePlan);
        userData.UpdateDegreePlan(degreePlan.Id, current => option.Apply(current));

        var removals = new List<PlanCourseRemoval>();
        foreach (var key in option.MovedAway)
        {
            var planCourse = plan.Course(key);
            if (planCourse is null) continue;
            var crns = index?.Sections(key).Select(s => s.Crn).ToHashSet(StringComparer.Ordinal) ?? [];
            removals.Add(new PlanCourseRemoval(
                ClonePlanCourse(planCourse),
                plan.Courses.FindIndex(c => c.Key == key) is var found && found >= 0 ? found : plan.Courses.Count,
                plan.SelectedCRNs.Where(crns.Contains).ToList(),
                plan.FavoriteCombinations.Where(combo => combo.Any(crns.Contains)).Select(combo => combo.ToList()).ToList()));
            userData.RemoveCourse(key, planId, crns);
        }
        var added = new List<CourseKey>();
        foreach (var key in option.MovedIn)
        {
            if (index is not null && index.Sections(key).Count == 0) continue;
            if (index is null) continue;
            if (userData.AddCourse(key, planId, required: true)) added.Add(key);
        }

        return ScheduleReorderOutcome.Applied(() =>
        {
            userData.ReplaceDegreePlan(degreePlanBefore.Id, degreePlanBefore);
            foreach (var key in added) userData.RemoveCourse(key, planId);
            userData.UpdatePlan(planId, current =>
            {
                for (var i = removals.Count - 1; i >= 0; i--) PlanEditing.Restore(removals[i], current);
            });
        });
    }

    internal static DegreePlan CloneDegreePlan(DegreePlan plan) =>
        System.Text.Json.JsonSerializer.Deserialize<DegreePlan>(
            System.Text.Json.JsonSerializer.Serialize(plan, AppJson.UserData), AppJson.UserData)!;

    internal static bool SameTerms(DegreePlan a, DegreePlan b)
    {
        if (a.Terms.Count != b.Terms.Count) return false;
        for (var i = 0; i < a.Terms.Count; i++)
        {
            var left = a.Terms[i];
            var right = b.Terms[i];
            if (left.Id != right.Id || left.Season != right.Season || left.Year != right.Year || left.Courses.Count != right.Courses.Count)
                return false;
            for (var j = 0; j < left.Courses.Count; j++)
            {
                var x = left.Courses[j];
                var y = right.Courses[j];
                if (x.Id != y.Id || x.Key != y.Key || x.Label != y.Label || x.Title != y.Title || x.Credits != y.Credits)
                    return false;
            }
        }
        return true;
    }

    private static PlanCourse ClonePlanCourse(PlanCourse course) => new()
    {
        Key = course.Key,
        IsRequired = course.IsRequired,
        ExcludedCRNs = new HashSet<string>(course.ExcludedCRNs, StringComparer.Ordinal),
        ColorIndex = course.ColorIndex,
        PinnedCrn = course.PinnedCrn,
    };
}

public static class SchedulePlanActions
{
    public static PlanCourseRemoval RemoveCourse(ResolvedPlan.Entry entry, SchedulePlan plan, CatalogIndex? index, UserDataStore userData)
    {
        var crns = new HashSet<string>(entry.CourseCrns, StringComparer.Ordinal);
        foreach (var section in entry.Selected) crns.Add(section.Crn);
        if (index is not null)
            foreach (var section in index.Sections(entry.Key)) crns.Add(section.Crn);
        var removal = new PlanCourseRemoval(
            new PlanCourse
            {
                Key = entry.PlanCourse.Key,
                IsRequired = entry.PlanCourse.IsRequired,
                ExcludedCRNs = new HashSet<string>(entry.PlanCourse.ExcludedCRNs, StringComparer.Ordinal),
                ColorIndex = entry.PlanCourse.ColorIndex,
                PinnedCrn = entry.PlanCourse.PinnedCrn,
            },
            plan.Courses.FindIndex(c => c.Key == entry.Key) is var found && found >= 0 ? found : plan.Courses.Count,
            plan.SelectedCRNs.Where(crns.Contains).ToList(),
            plan.FavoriteCombinations.Where(combo => combo.Any(crns.Contains)).Select(combo => combo.ToList()).ToList());
        userData.RemoveCourse(entry.Key, plan.Id, crns);
        return removal;
    }

    public static void PruneLeftovers(PlanCourseRemoval removal, Guid planId, CatalogIndex? index, UserDataStore userData)
    {
        if (index is null) return;
        userData.UpdatePlan(planId, plan =>
        {
            var pruned = PlanEditing.PruneOrphans(index, plan);
            bool Own(string crn) => index.Section(crn)?.CourseKey == removal.Course.Key;
            foreach (var crn in pruned.Selected)
                if (Own(crn) && !removal.SelectedCRNs.Contains(crn)) removal.SelectedCRNs.Add(crn);
            foreach (var combination in pruned.Favorites)
            {
                if (!combination.Any(Own)) continue;
                if (removal.FavoriteCombinations.Any(existing => existing.SequenceEqual(combination))) continue;
                removal.FavoriteCombinations.Add(combination.ToList());
            }
        });
    }

    public static List<CourseKey> AddAutofill(ScheduleAutofillSuggestion suggestion, Guid planId, UserDataStore userData)
    {
        var added = new List<CourseKey>();
        foreach (var key in suggestion.Courses)
            if (userData.AddCourse(key, planId, required: true)) added.Add(key);
        if (suggestion.Origin is ScheduleAutofillSuggestion.Source.FromDegreePlan degree)
        {
            var term = userData.DegreePlan(degree.PlanId)?.Terms.FirstOrDefault(item => item.Id == degree.TermId);
            if (term is not null)
                userData.UpdatePlan(planId, plan => DegreePlanSchedule.CopyPins(plan, term.Courses));
        }
        return added;
    }

    public static void UndoAutofill(IReadOnlyList<CourseKey> keys, Guid planId, CatalogIndex? index, UserDataStore userData)
    {
        foreach (var key in keys)
        {
            var crns = index?.Sections(key).Select(s => s.Crn).ToList() ?? [];
            userData.RemoveCourse(key, planId, crns);
        }
    }
}
