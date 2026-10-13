// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>The plan's courses, in plan order, and the note for a declared equivalence.</summary>
public static class DegreePlanKeys
{
    public static List<CourseKey> PlannedCourseKeys(this DegreePlan plan)
    {
        var seen = new HashSet<CourseKey>();
        var result = new List<CourseKey>();
        foreach (var term in plan.Terms)
            foreach (var course in term.Courses)
                if (course.Key is CourseKey key && seen.Add(key)) result.Add(key);
        return result;
    }

    public static List<CourseKey> CompletedCourseKeys(this DegreePlan plan)
    {
        var seen = new HashSet<CourseKey>();
        var result = new List<CourseKey>();
        foreach (var course in plan.Completed)
            if (course.Key is CourseKey key && seen.Add(key)) result.Add(key);
        return result;
    }

    /// <summary>Completed courses, then planned courses, each once.</summary>
    public static List<CourseKey> AllCourseKeys(this DegreePlan plan)
    {
        var seen = new HashSet<CourseKey>();
        var result = new List<CourseKey>();
        foreach (var key in plan.CompletedCourseKeys().Concat(plan.PlannedCourseKeys()))
            if (seen.Add(key)) result.Add(key);
        return result;
    }

    /// <summary><c>Counts as CS 2100</c>, for a course the student declared equivalent to another.</summary>
    public static string? EquivalenceNote(this DegreePlan plan, CourseKey? key)
    {
        if (key is not CourseKey course) return null;
        var targets = plan.Equivalences.Where(item => item.Course == course).Select(item => item.CountsAs.ToString()).ToList();
        return targets.Count == 0 ? null : "Counts as " + string.Join(", ", targets);
    }
}

/// <summary>"Does CS 2510 count as CS 2100?": a hint the student confirms. It is never applied on its own.</summary>
public sealed class DegreeEquivalencePrompt : IEquatable<DegreeEquivalencePrompt>
{
    public required CourseKey Course { get; init; }
    public required CourseKey CountsAs { get; init; }
    public required string Evidence { get; init; }

    public string Id => PairId(Course, CountsAs);

    public string Question => $"Does {Unbroken(Course)} count as {Unbroken(CountsAs)}?";

    public static string PairId(CourseKey a, CourseKey b) => a < b ? $"{a}|{b}" : $"{b}|{a}";

    public bool Equals(DegreeEquivalencePrompt? other) =>
        other is not null && Course == other.Course && CountsAs == other.CountsAs && Evidence == other.Evidence;

    public override bool Equals(object? obj) => Equals(obj as DegreeEquivalencePrompt);
    public override int GetHashCode() => HashCode.Combine(Course, CountsAs, Evidence);

    private static string Unbroken(CourseKey key) => key.Subject + "\u00a0" + key.Number;
}

/// <summary>Suggestions for retired plan courses, and for courses a requirement lists that the plan doesn't have.</summary>
public sealed class DegreeEquivalencePrompts
{
    private readonly Dictionary<CourseKey, DegreeEquivalencePrompt> _byListed = [];

    public List<DegreeEquivalencePrompt> ForPlanCourses { get; } = [];

    public DegreeEquivalencePrompt? ForListed(CourseKey key) =>
        _byListed.TryGetValue(key, out var prompt) ? prompt : null;

    public DegreeEquivalencePrompt? ForPlanCourse(CourseKey key) =>
        ForPlanCourses.FirstOrDefault(prompt => prompt.Course == key);

    internal void RememberListed(CourseKey key, DegreeEquivalencePrompt prompt) => _byListed.TryAdd(key, prompt);
}

/// <summary>
/// What a suggestion cache reads instead of a course-info store: lookup state, known equivalences,
/// and the guesses for one course. <see cref="Newest"/> is the catalog those guesses were built from.
/// </summary>
public sealed class DegreeEquivalenceSource
{
    public int Version { get; init; }
    public CatalogIndex? Newest { get; init; }
    public Func<CourseKey, DegreeCourseLookup> State { get; init; } = static _ => DegreeCourseLookup.Unknown;
    public Func<DegreePlan?, CourseEquivalences> Equivalences { get; init; } =
        static plan => plan?.DeclaredEquivalences ?? CourseEquivalences.None;
    public Func<CourseKey, DegreePlan, IReadOnlyDictionary<CourseKey, string>, IReadOnlyList<CourseKey>, IReadOnlyList<EquivalenceSuggestion>> Suggestions { get; init; } =
        static (_, _, _, _) => [];
}

/// <summary>Builds equivalence prompts only when their inputs change.</summary>
public sealed class DegreeEquivalenceSuggestionCache
{
    private CacheKey? _key;
    private DegreeEquivalencePrompts _cached = new();

    public DegreeEquivalencePrompts Prompts(
        DegreePlan plan,
        DegreeEquivalenceSource source,
        IReadOnlyList<CourseKey>? listed = null,
        ISet<string>? dismissed = null,
        IReadOnlyDictionary<CourseKey, string>? catalogTitles = null,
        IReadOnlyList<CourseKey>? alsoComparing = null)
    {
        listed ??= [];
        dismissed ??= new HashSet<string>(StringComparer.Ordinal);
        catalogTitles ??= new Dictionary<CourseKey, string>();
        alsoComparing ??= [];
        var planKeys = plan.AllCourseKeys();
        var inPlan = planKeys.ToHashSet();
        var candidates = planKeys.Concat(listed.Where(key => !inPlan.Contains(key))).ToList();
        var eligible = candidates.Where(key => IsEligible(key, source)).ToList();
        var key = new CacheKey(
            source.Version,
            planKeys,
            eligible,
            catalogTitles.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)).ToList(),
            alsoComparing.ToList(),
            source.Equivalences(plan),
            dismissed.ToHashSet(StringComparer.Ordinal),
            source.Newest?.Term.Code,
            source.Newest?.Catalog.SectionsUpdatedAt);
        if (_key is not null && _key.Equals(key)) return _cached;
        _key = key;
        _cached = Build(plan, planKeys, eligible.ToHashSet(), listed, dismissed, source, catalogTitles, alsoComparing);
        return _cached;
    }

    private static bool IsEligible(CourseKey key, DegreeEquivalenceSource source)
    {
        if (source.Newest?.Course(key) is not null) return false;
        var state = source.State(key);
        return state.Kind switch
        {
            DegreeLookupKind.Found => state.IsRetired != false,
            DegreeLookupKind.NotFound => true,
            _ => false,
        };
    }

    private static DegreeEquivalencePrompts Build(
        DegreePlan plan,
        List<CourseKey> planKeys,
        HashSet<CourseKey> eligible,
        IReadOnlyList<CourseKey> listed,
        ISet<string> dismissed,
        DegreeEquivalenceSource source,
        IReadOnlyDictionary<CourseKey, string> catalogTitles,
        IReadOnlyList<CourseKey> alsoComparing)
    {
        var result = new DegreeEquivalencePrompts();
        var inPlan = planKeys.ToHashSet();
        List<EquivalenceSuggestion> Open(CourseKey course)
        {
            if (!eligible.Contains(course)) return [];
            return source.Suggestions(course, plan, catalogTitles, alsoComparing)
                .Where(suggestion => !dismissed.Contains(DegreeEquivalencePrompt.PairId(course, suggestion.Candidate)))
                .ToList();
        }

        foreach (var key in planKeys)
        {
            var suggestions = Open(key);
            if (suggestions.Count > 0)
            {
                var best = suggestions[0];
                result.ForPlanCourses.Add(new DegreeEquivalencePrompt
                {
                    Course = key,
                    CountsAs = best.Candidate,
                    Evidence = best.Evidence,
                });
            }
            foreach (var suggestion in suggestions)
            {
                result.RememberListed(suggestion.Candidate, new DegreeEquivalencePrompt
                {
                    Course = key,
                    CountsAs = suggestion.Candidate,
                    Evidence = suggestion.Evidence,
                });
            }
        }

        foreach (var key in listed)
        {
            if (inPlan.Contains(key) || result.ForListed(key) is not null) continue;
            var suggestions = Open(key);
            var best = suggestions.FirstOrDefault(suggestion => inPlan.Contains(suggestion.Candidate)) ?? suggestions.FirstOrDefault();
            if (best is null) continue;
            result.RememberListed(key, new DegreeEquivalencePrompt
            {
                Course = best.Candidate,
                CountsAs = key,
                Evidence = best.Evidence,
            });
        }
        return result;
    }

    private sealed class CacheKey : IEquatable<CacheKey>
    {
        private readonly int _version;
        private readonly List<CourseKey> _planKeys;
        private readonly List<CourseKey> _eligible;
        private readonly List<(CourseKey Key, string Title)> _catalogTitles;
        private readonly List<CourseKey> _alsoComparing;
        private readonly CourseEquivalences _equivalences;
        private readonly HashSet<string> _dismissed;
        private readonly string? _newestTerm;
        private readonly DateTimeOffset? _newestUpdatedAt;

        public CacheKey(
            int version,
            List<CourseKey> planKeys,
            List<CourseKey> eligible,
            List<(CourseKey Key, string Title)> catalogTitles,
            List<CourseKey> alsoComparing,
            CourseEquivalences equivalences,
            HashSet<string> dismissed,
            string? newestTerm,
            DateTimeOffset? newestUpdatedAt)
        {
            _version = version;
            _planKeys = planKeys;
            _eligible = eligible;
            _catalogTitles = catalogTitles;
            _alsoComparing = alsoComparing;
            _equivalences = equivalences;
            _dismissed = dismissed;
            _newestTerm = newestTerm;
            _newestUpdatedAt = newestUpdatedAt;
        }

        public bool Equals(CacheKey? other)
        {
            if (other is null) return false;
            return _version == other._version &&
                _planKeys.SequenceEqual(other._planKeys) &&
                _eligible.SequenceEqual(other._eligible) &&
                TitlesEqual(_catalogTitles, other._catalogTitles) &&
                _alsoComparing.SequenceEqual(other._alsoComparing) &&
                _equivalences.Equals(other._equivalences) &&
                _dismissed.SetEquals(other._dismissed) &&
                _newestTerm == other._newestTerm &&
                _newestUpdatedAt == other._newestUpdatedAt;
        }

        public override bool Equals(object? obj) => Equals(obj as CacheKey);
        public override int GetHashCode() => HashCode.Combine(_version, _planKeys.Count, _eligible.Count, _newestTerm, _newestUpdatedAt);

        private static bool TitlesEqual(List<(CourseKey Key, string Title)> left, List<(CourseKey Key, string Title)> right)
        {
            if (left.Count != right.Count) return false;
            for (var i = 0; i < left.Count; i++)
                if (left[i].Key != right[i].Key || left[i].Title != right[i].Title) return false;
            return true;
        }
    }
}

/// <summary>What the equivalence sheet starts with.</summary>
public sealed class DegreeEquivalenceRequest
{
    public Guid Id { get; } = Guid.NewGuid();
    public required Guid PlanId { get; init; }
    public CourseKey? Course { get; init; }
    public CourseKey? CountsAs { get; init; }
    public CourseEquivalence? Existing { get; init; }
}

/// <summary>Declares a plan equivalence through <see cref="UserDataStore"/>.</summary>
public static class DegreeEquivalenceEditing
{
    /// <summary>Returns the new equivalence id, or null when the plan already counts the courses as each other.</summary>
    public static Guid? Declare(UserDataStore store, Guid planId, CourseKey course, CourseKey countsAs, string note = "")
    {
        var plan = store.DegreePlan(planId);
        if (plan is null || course == countsAs || plan.DeclaredEquivalences.AreEquivalent(course, countsAs)) return null;
        Guid? added = null;
        store.UpdateDegreePlan(planId, updated =>
        {
            if (updated.DeclareEquivalence(course, countsAs, note) && updated.Equivalences.Count > 0)
                added = updated.Equivalences[^1].Id;
        });
        return added;
    }
}
