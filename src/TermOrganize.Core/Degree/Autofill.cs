// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>A ready-made set of courses to start a new schedule with.</summary>
public sealed class ScheduleAutofillSuggestion
{
    public abstract record Source
    {
        public sealed record FromDegreePlan(Guid PlanId, Guid TermId) : Source;
        public sealed record FromSchedulePlan(Guid PlanId) : Source;
    }

    public required Source Origin { get; init; }
    public required string PlanName { get; init; }
    public string? Semester { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public bool IsExactTerm { get; init; }
    public required IReadOnlyList<CourseKey> Courses { get; init; }
    public required IReadOnlyList<CourseKey> Unavailable { get; init; }

    public string Id => Origin switch
    {
        Source.FromDegreePlan degree => $"degree-{degree.PlanId.ToString("D").ToUpperInvariant()}-{degree.TermId.ToString("D").ToUpperInvariant()}",
        Source.FromSchedulePlan schedule => $"schedule-{schedule.PlanId.ToString("D").ToUpperInvariant()}",
        _ => "",
    };
}

/// <summary>Suggestions for filling a schedule, and a draft of the next degree plan.</summary>
public static class DegreePlanAutofill
{
    public static int MonthIndex(Term term)
    {
        var season = PlanSeasonInfo.FromTerm(term.Season);
        return term.CalendarYear * 12 + season.StartMonth() - 1;
    }

    /// <summary>
    /// Suggestions for a schedule in <paramref name="term"/>, best first: the degree plan's exact
    /// semester, then other schedules for the same term, then the closest planned semester.
    /// </summary>
    public static List<ScheduleAutofillSuggestion> ScheduleSuggestions(
        Term term,
        IEnumerable<DegreePlan> degreePlans,
        IEnumerable<SchedulePlan> schedulePlans,
        Guid? excludingPlanId = null,
        ISet<CourseKey>? alreadyInPlan = null,
        CatalogIndex? index = null)
    {
        alreadyInPlan ??= new HashSet<CourseKey>();
        var target = MonthIndex(term);
        var exact = new List<ScheduleAutofillSuggestion>();
        var closest = new List<ScheduleAutofillSuggestion>();

        (List<CourseKey> Offered, List<CourseKey> Unavailable) Split(IReadOnlyList<CourseKey> keys)
        {
            if (index is null || index.Term.Code != term.Code) return (keys.ToList(), []);
            var offered = new List<CourseKey>();
            var unavailable = new List<CourseKey>();
            foreach (var key in keys)
            {
                if (index.Sections(key).Count == 0) unavailable.Add(key);
                else offered.Add(key);
            }
            return (offered, unavailable);
        }

        foreach (var plan in degreePlans.OrderBy(item => item.IsPrimary ? 0 : 1).ThenByDescending(item => item.UpdatedAt))
        {
            var done = plan.CompletedKeys;
            var candidates = new List<(DegreePlanTerm Term, List<CourseKey> Keys, int Distance)>();
            foreach (var planTerm in plan.Terms)
            {
                var seen = new HashSet<CourseKey>();
                var keys = new List<CourseKey>();
                foreach (var course in planTerm.Courses)
                {
                    if (course.Key is not CourseKey key) continue;
                    if (done.Contains(key) || alreadyInPlan.Contains(key) || !seen.Add(key)) continue;
                    keys.Add(key);
                }
                if (keys.Count == 0) continue;
                candidates.Add((planTerm, keys, Math.Abs(planTerm.MonthIndex - target)));
            }
            if (candidates.Count == 0) continue;
            var best = candidates.OrderBy(item => item.Distance).ThenByDescending(item => item.Term.SortKey).First();
            var parts = Split(best.Keys);
            if (parts.Offered.Count == 0 && parts.Unavailable.Count == 0) continue;
            var isExact = best.Term.Matches(term);
            var suggestion = new ScheduleAutofillSuggestion
            {
                Origin = new ScheduleAutofillSuggestion.Source.FromDegreePlan(plan.Id, best.Term.Id),
                PlanName = plan.Name,
                Semester = best.Term.ShortName,
                Title = $"From \u201c{plan.Name}\u201d \u00b7 {best.Term.ShortName}",
                Detail = isExact
                    ? "Your plan for this term"
                    : best.Distance == 0 ? "Planned for this time" : "Closest semester you planned",
                IsExactTerm = isExact,
                Courses = parts.Offered,
                Unavailable = parts.Unavailable,
            };
            if (isExact) exact.Add(suggestion);
            else closest.Add(suggestion);
        }

        var copies = schedulePlans
            .Where(plan => plan.TermCode == term.Code && plan.Id != excludingPlanId && plan.Courses.Count > 0)
            .OrderByDescending(plan => plan.UpdatedAt)
            .Take(2)
            .Select(plan =>
            {
                var keys = plan.Courses.Select(course => course.Key).Where(key => !alreadyInPlan.Contains(key)).ToList();
                if (keys.Count == 0) return null;
                return new ScheduleAutofillSuggestion
                {
                    Origin = new ScheduleAutofillSuggestion.Source.FromSchedulePlan(plan.Id),
                    PlanName = plan.Name,
                    Semester = null,
                    Title = $"Copy \u201c{plan.Name}\u201d",
                    Detail = plan.IsPrimary ? "Your schedule for this term" : "Another schedule for this term",
                    IsExactTerm = true,
                    Courses = keys,
                    Unavailable = [],
                };
            })
            .OfType<ScheduleAutofillSuggestion>()
            .ToList();

        return exact.Concat(copies).Concat(closest).ToList();
    }

    /// <summary>
    /// A draft degree plan: a copy of <paramref name="source"/>, plus courses from primary schedules
    /// whose terms have not ended.
    /// </summary>
    public static DegreePlan NewDegreePlanDraft(
        string name,
        DegreePlan? source,
        IEnumerable<SchedulePlan> schedulePlans,
        Func<string, CatalogIndex?> catalogIndex,
        DateTimeOffset? now = null)
    {
        var moment = now ?? DateTimeOffset.UtcNow;
        DegreePlan draft;
        if (source is null)
        {
            draft = new DegreePlan { Name = name };
        }
        else
        {
            draft = DegreePlanCopy.Clone(source);
            var stamp = DateTimeOffset.UtcNow;
            draft.Id = Guid.NewGuid();
            draft.Name = name;
            draft.IsPrimary = false;
            draft.ImportedFrom = null;
            draft.PrintedAt = null;
            draft.CreatedAt = stamp;
            draft.UpdatedAt = stamp;
            foreach (var substitution in draft.Substitutions) substitution.Id = Guid.NewGuid();
            foreach (var equivalence in draft.Equivalences) equivalence.Id = Guid.NewGuid();
            foreach (var course in draft.Completed) course.Id = Guid.NewGuid();
            foreach (var term in draft.Terms)
            {
                term.Id = Guid.NewGuid();
                foreach (var course in term.Courses) course.Id = Guid.NewGuid();
            }
        }

        var done = draft.CompletedKeys;
        foreach (var plan in schedulePlans)
        {
            if (!plan.IsPrimary || plan.Courses.Count == 0) continue;
            var window = TermCalendar.ApproximateWindow(plan.TermCode);
            if (window is not null && window.Value.End < moment) continue;
            var banner = new Term(plan.TermCode, "");
            var season = PlanSeasonInfo.FromTerm(banner.Season);
            var index = catalogIndex(plan.TermCode);
            var termPosition = draft.Terms.FindIndex(term => term.Season == season && term.Year == banner.CalendarYear);
            if (termPosition < 0)
            {
                draft.Terms.Add(new DegreePlanTerm { Season = season, Year = banner.CalendarYear });
                termPosition = draft.Terms.Count - 1;
            }
            var present = draft.Terms[termPosition].Courses.Select(course => course.Key).OfType<CourseKey>().ToHashSet();
            foreach (var planCourse in plan.Courses)
            {
                if (present.Contains(planCourse.Key) || done.Contains(planCourse.Key)) continue;
                var course = index?.Course(planCourse.Key);
                draft.Terms[termPosition].Courses.Add(new DegreePlanCourse(
                    planCourse.Key,
                    DegreePlanFormat.ExportTitle(course?.Title ?? planCourse.Key.ToString()),
                    course?.MaxCredits ?? 4));
                present.Add(planCourse.Key);
            }
        }
        draft.SortTerms();
        return draft;
    }

    /// <summary>The next few semesters after the plan's last one (or after now), for "Add semester".</summary>
    public static List<DegreePlanTerm> NextSemesters(DegreePlan plan, int count = 4, DateTimeOffset? now = null)
    {
        var order = new[] { PlanSeason.Spring, PlanSeason.SummerA, PlanSeason.SummerB, PlanSeason.Fall };
        var moment = now ?? DateTimeOffset.UtcNow;
        var local = TimeZoneInfo.ConvertTime(moment, TermCalendar.NewYork);
        var year = local.Year;
        var seasonIndex = local.Month < 5 ? 0 : local.Month < 7 ? 1 : local.Month < 9 ? 2 : 3;
        if (plan.Terms.Count > 0)
        {
            var last = plan.Terms[^1];
            var index = Array.IndexOf(order, last.Season);
            if (index >= 0)
            {
                year = last.Year;
                seasonIndex = index + 1;
            }
        }
        var existing = plan.Terms.Select(term => $"{PlanSeasonNames.Raw(term.Season)}-{term.Year}").ToHashSet(StringComparer.Ordinal);
        var result = new List<DegreePlanTerm>();
        while (result.Count < count)
        {
            if (seasonIndex >= order.Length)
            {
                seasonIndex = 0;
                year++;
            }
            var season = order[seasonIndex];
            if (existing.Add($"{PlanSeasonNames.Raw(season)}-{year}"))
                result.Add(new DegreePlanTerm { Season = season, Year = year });
            seasonIndex++;
        }
        return result;
    }
}
