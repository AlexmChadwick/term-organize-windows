// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>One way to rearrange the degree plan so this semester's courses work.</summary>
public sealed class ScheduleReorderOption
{
    public required string Id { get; init; }
    /// <summary>For example <c>Move CS 3386 to Spring 2028</c>.</summary>
    public required string Title { get; init; }
    public List<string> Reasons { get; init; } = [];
    public bool AddsSemester { get; init; }
    public List<CourseKey> MovedAway { get; init; } = [];
    public List<CourseKey> MovedIn { get; init; } = [];
    /// <summary>Edits a copy of the degree plan. The store writes the real change.</summary>
    public Action<DegreePlan> Apply { get; init; } = _ => { };

    /// <summary><c>Moved CS 3386 to Spring 2028</c>.</summary>
    public string AppliedText =>
        Title.StartsWith("Move ", StringComparison.Ordinal) ? "Moved " + Title["Move ".Length..] : "Updated your degree plan";

    /// <summary>What applying it does to this schedule.</summary>
    public string ScheduleEffect
    {
        get
        {
            var parts = new List<string>();
            if (MovedAway.Count > 0)
                parts.Add($"{List(MovedAway)} {(MovedAway.Count == 1 ? "comes" : "come")} out of this schedule.");
            if (MovedIn.Count > 0)
                parts.Add($"{List(MovedIn)} will be added to it if {(MovedIn.Count == 1 ? "it\u2019s" : "they\u2019re")} offered this term.");
            return string.Join(' ', parts);
        }
    }

    /// <summary><c>CS 3386</c>, <c>CS 3386 and CS 4100</c>, <c>CS 3386, CS 4100 and DS 3000</c>.</summary>
    public static string List(IReadOnlyList<CourseKey> keys)
    {
        var names = keys.Select(key => key.ToString()).ToList();
        if (names.Count == 0) return "";
        if (names.Count == 1) return names[0];
        return string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }
}

public sealed class ScheduleReorderResult
{
    public List<ScheduleReorderOption> Options { get; set; } = [];
    /// <summary>False when offering history could not be checked. Options say so themselves.</summary>
    public bool HistoryKnown { get; set; } = true;
}

/// <summary>Where the "rearrange your degree plan" suggestions stand.</summary>
public abstract record ScheduleReorderState
{
    public enum Reason
    {
        /// <summary>There is no degree plan to rearrange.</summary>
        NoDegreePlan,
        /// <summary>The degree plan has no semester for this schedule's term, or nothing blocks.</summary>
        NotApplicable,
    }

    public sealed record Unavailable(Reason Why) : ScheduleReorderState;
    public sealed record Loading : ScheduleReorderState;
    public sealed record Ready(ScheduleReorderResult Result) : ScheduleReorderState;
}

/// <summary>
/// How to move the blocking courses of a semester, using loaded catalogs for when courses
/// were offered and what they require. Banner offering-history requests are not made.
/// </summary>
public static class ScheduleReorderSuggestions
{
    /// <summary>How long a network lookup was allowed to run. Catalogs already in memory answer immediately.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>The primary degree plan's semester that matches a schedule's term.</summary>
    public static (DegreePlan Plan, Term Term)? Semester(
        SchedulePlan plan, DegreePlan? degreePlan, IReadOnlyList<Term> terms, CatalogIndex? index)
    {
        if (degreePlan is null) return null;
        var term = ScheduleAutofillSupport.TermFor(plan.TermCode, terms, index);
        if (degreePlan.TermMatching(term) is null) return null;
        return (degreePlan, term);
    }

    /// <summary>
    /// Suggestions for moving <paramref name="blocking"/> courses out of <paramref name="term"/>.
    /// <paramref name="term"/> must be a semester of <paramref name="degreePlan"/>. Offering history
    /// comes from <paramref name="indexes"/>: a course with sections was offered that term, and a
    /// course whose details are loaded supplies its prerequisites (null means it has none).
    /// </summary>
    public static ScheduleReorderResult Load(
        IReadOnlyList<CourseKey> blocking,
        DegreePlan degreePlan,
        Term term,
        IReadOnlyList<CatalogIndex> indexes)
    {
        var plan = DegreePlanCopy.Clone(degreePlan);
        plan.SortTerms();
        var origin = plan.Terms.FirstOrDefault(item => item.Matches(term));
        if (origin is null) return new ScheduleReorderResult();
        var inSemester = origin.Courses.Select(course => course.Key).OfType<CourseKey>().ToHashSet();
        var candidates = new List<CourseKey>();
        var seen = new HashSet<CourseKey>();
        foreach (var key in blocking)
        {
            if (!inSemester.Contains(key) || !seen.Add(key)) continue;
            candidates.Add(key);
        }
        if (candidates.Count == 0) return new ScheduleReorderResult();

        var originIndex = plan.Terms.FindIndex(item => item.Id == origin.Id);
        var later = plan.Terms.Skip(originIndex + 1)
            .SelectMany(item => item.Courses.Select(course => course.Key).OfType<CourseKey>())
            .Distinct()
            .ToList();

        var history = HistoryFrom(indexes, candidates);
        var equivalences = EquivalencesFor(degreePlan, indexes);
        List<DegreePlanReordering.Suggestion> Suggest() =>
            DegreePlanReordering.Suggestions(degreePlan, origin.Id, candidates, history, key => RequisiteOf(indexes, key), equivalences);

        var found = Suggest();
        if (found.Any(item => item.Moves.Count == 1))
            found = found.Where(item => item.Moves.Count == 1).ToList();
        else if (later.Count > 0)
        {
            history = history.Merging(HistoryFrom(indexes, later));
            found = Suggest();
        }

        return new ScheduleReorderResult
        {
            Options = found.Select(item => Option(item, origin)).ToList(),
            HistoryKnown = history.CheckedTerms.Count > 0,
        };
    }

    /// <summary>The engine's suggestion as the generator screen shows it.</summary>
    public static ScheduleReorderOption Option(DegreePlanReordering.Suggestion suggestion, DegreePlanTerm origin)
    {
        var captured = suggestion;
        return new ScheduleReorderOption
        {
            Id = captured.Id,
            Title = captured.Title,
            Reasons = captured.Reasons.ToList(),
            AddsSemester = captured.AddsSemester is not null,
            MovedAway = captured.Moves.Where(move => move.From == origin.Id).Select(move => move.Course).ToList(),
            MovedIn = captured.Moves.Where(move => move.To == origin.Id).Select(move => move.Course).ToList(),
            Apply = plan => DegreePlanReordering.Apply(captured, plan),
        };
    }

    private static RequisiteAnswer RequisiteOf(IReadOnlyList<CatalogIndex> indexes, CourseKey key)
    {
        foreach (var index in indexes)
        {
            var course = index.Course(key);
            if (course is not { DetailsLoaded: true }) continue;
            return course.Prereqs is Requisite requisite ? RequisiteAnswer.Known(requisite) : RequisiteAnswer.None;
        }
        return RequisiteAnswer.Unknown;
    }

    private static OfferingHistory HistoryFrom(IReadOnlyList<CatalogIndex> indexes, IReadOnlyList<CourseKey> keys)
    {
        var terms = indexes.Select(index => index.Term.Code)
            .Where(code => !string.IsNullOrEmpty(code))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(code => code, StringComparer.Ordinal)
            .ToList();
        var offered = new Dictionary<CourseKey, IEnumerable<string>>();
        var asked = keys.ToHashSet();
        foreach (var index in indexes)
        {
            foreach (var key in asked)
            {
                if (index.Sections(key).Count == 0) continue;
                if (!offered.TryGetValue(key, out var existing))
                    offered[key] = existing = new HashSet<string>(StringComparer.Ordinal);
                ((HashSet<string>)existing).Add(index.Term.Code);
            }
        }
        return new OfferingHistory(terms, offered, asked);
    }

    private static CourseEquivalences EquivalencesFor(DegreePlan plan, IReadOnlyList<CatalogIndex> indexes)
    {
        var combined = plan.DeclaredEquivalences;
        foreach (var index in indexes) combined = combined.Merging(CrossListed(index));
        return combined;
    }

    /// <summary>
    /// Cross-listed sections that are the same course under two subjects: same number, same
    /// normalized title, different subject. Special topics never count.
    /// </summary>
    private static CourseEquivalences CrossListed(CatalogIndex index)
    {
        var pairs = new HashSet<(CourseKey First, CourseKey Second)>();
        foreach (var section in index.Catalog.Sections)
        {
            if (section.CrossListCRNs.Count == 0) continue;
            var key = section.CourseKey;
            if (index.Course(key)?.SpecialTopics == true) continue;
            foreach (var crn in section.CrossListCRNs)
            {
                var other = index.Section(crn);
                if (other is null) continue;
                var otherKey = other.CourseKey;
                if (otherKey.Number != key.Number || otherKey.Subject == key.Subject) continue;
                if (index.Course(otherKey)?.SpecialTopics == true) continue;
                var title = NormalizeTitle(index.Course(key)?.Title ?? section.Title);
                var otherTitle = NormalizeTitle(index.Course(otherKey)?.Title ?? other.Title);
                if (title.Length == 0 || title != otherTitle) continue;
                var ordered = key.CompareTo(otherKey) <= 0 ? (key, otherKey) : (otherKey, key);
                pairs.Add(ordered);
            }
        }
        return new CourseEquivalences(pairs.Select(pair => (pair.First, pair.Second)));
    }

    private static string NormalizeTitle(string title) =>
        string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
