// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// Which degree-plan and sibling-schedule suggestions apply to a schedule, which one deserves
/// a nudge, and the bookkeeping for dismissed nudges. The suggestion list itself is
/// <see cref="DegreePlanAutofill"/>.
/// </summary>
public static class ScheduleAutofillSupport
{
    /// <summary>The Banner term a schedule belongs to, even when the term list hasn't loaded.</summary>
    public static Term TermFor(string code, IEnumerable<Term> terms, CatalogIndex? index)
    {
        var known = terms.FirstOrDefault(term => term.Code == code);
        if (known is not null) return known;
        if (index?.Term.Code == code) return index.Term;
        return new Term(code, "");
    }

    /// <summary>
    /// Courses worth starting <paramref name="plan"/> with, best first. Courses already in the
    /// plan are left out. An empty list when nothing else could contribute courses.
    /// </summary>
    public static List<ScheduleAutofillSuggestion> Suggestions(
        SchedulePlan plan,
        IEnumerable<Term> terms,
        IEnumerable<DegreePlan> degreePlans,
        IEnumerable<SchedulePlan> schedulePlans,
        CatalogIndex? index)
    {
        var plans = schedulePlans.ToList();
        var degrees = degreePlans.ToList();
        var hasSiblings = plans.Any(other => other.TermCode == plan.TermCode && other.Id != plan.Id && other.Courses.Count > 0);
        if (degrees.Count == 0 && !hasSiblings) return [];
        return DegreePlanAutofill.ScheduleSuggestions(
            TermFor(plan.TermCode, terms, index),
            degrees,
            plans,
            plan.Id,
            plan.Courses.Select(course => course.Key).ToHashSet(),
            index);
    }

    /// <summary>The exact-semester degree-plan suggestion that still has courses to add, skipping dismissed ones.</summary>
    public static ScheduleAutofillSuggestion? Nudge(
        IEnumerable<ScheduleAutofillSuggestion> suggestions,
        Func<ScheduleAutofillSuggestion, bool>? isDismissed = null)
    {
        isDismissed ??= _ => false;
        return suggestions.FirstOrDefault(suggestion =>
            suggestion.IsExactTerm
            && suggestion.Origin is ScheduleAutofillSuggestion.Source.FromDegreePlan
            && suggestion.Courses.Count > 0
            && !isDismissed(suggestion));
    }

    /// <summary>The suggestion an empty schedule previews: the chosen one, else the best that has courses.</summary>
    public static ScheduleAutofillSuggestion? Preview(IReadOnlyList<ScheduleAutofillSuggestion> suggestions, string? choice)
    {
        if (choice is not null && suggestions.FirstOrDefault(suggestion => suggestion.Id == choice) is ScheduleAutofillSuggestion chosen)
            return chosen;
        return suggestions.FirstOrDefault(suggestion => suggestion.Courses.Count > 0) ?? suggestions.FirstOrDefault();
    }

    /// <summary><c>“New Plan”</c> and <c>Spring 2027</c> for a degree semester; <c>Copy “Name”</c> and no semester for another schedule.</summary>
    public static (string Name, string? Term) SourceParts(ScheduleAutofillSuggestion suggestion) =>
        suggestion.Origin is ScheduleAutofillSuggestion.Source.FromDegreePlan
            ? ($"\u201c{suggestion.PlanName}\u201d", suggestion.Semester)
            : ($"Copy \u201c{suggestion.PlanName}\u201d", null);

    /// <summary><c>“New Plan” · Spring 2027</c>.</summary>
    public static string SourceName(ScheduleAutofillSuggestion suggestion)
    {
        var parts = SourceParts(suggestion);
        return parts.Term is null ? parts.Name : $"{parts.Name} \u00b7 {parts.Term}";
    }

    /// <summary>The palette indexes <paramref name="keys"/> receive when added to <paramref name="plan"/> in order.</summary>
    public static List<int> ColorIndices(IReadOnlyList<CourseKey> keys, SchedulePlan plan)
    {
        var scratch = new SchedulePlan
        {
            Courses = plan.Courses.Select(course => new PlanCourse
            {
                Key = course.Key,
                IsRequired = course.IsRequired,
                ColorIndex = course.ColorIndex,
                ExcludedCRNs = course.ExcludedCRNs,
            }).ToList(),
        };
        var indices = new List<int>();
        foreach (var key in keys)
        {
            var color = scratch.NextColorIndex;
            indices.Add(color);
            scratch.Courses.Add(new PlanCourse { Key = key, IsRequired = true, ColorIndex = color });
        }
        return indices;
    }

    /// <summary>Identifies one nudge for one schedule.</summary>
    public static string DismissalKey(Guid planId, ScheduleAutofillSuggestion suggestion) =>
        $"{planId.ToString("D").ToUpperInvariant()}|{suggestion.Id}";

    public static bool IsDismissed(string key, string raw) =>
        raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(line => line == key);

    /// <summary><paramref name="raw"/> with <paramref name="key"/> added. Only the most recent <paramref name="limit"/> dismissals are kept.</summary>
    public static string Adding(string key, string raw, int limit = 200)
    {
        var keys = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => line != key).ToList();
        keys.Add(key);
        if (keys.Count > limit) keys = keys.Skip(keys.Count - limit).ToList();
        return string.Join('\n', keys);
    }

    public static string CourseCountLabel(int count) => count == 1 ? "1 course" : $"{count} courses";

    public static string UnavailableLine(IEnumerable<CourseKey> keys) =>
        "Not offered this term: " + string.Join(", ", keys.Select(key => key.ToString()));
}
