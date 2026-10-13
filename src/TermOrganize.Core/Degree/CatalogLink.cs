// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>Where a lookup of one course stands, for a degree-plan review.</summary>
public enum DegreeLookupKind
{
    Unknown,
    Loading,
    Found,
    NotFound,
    Failed,
}

/// <summary>A course-catalog lookup the plan review can keep without a course-info store.</summary>
public sealed class DegreeCourseLookup
{
    public DegreeLookupKind Kind { get; }
    public CourseInfo? Info { get; }
    public bool? IsRetired => Kind == DegreeLookupKind.Found ? Info?.IsRetired : null;

    /// <summary><c>No longer offered; last listed Spring 2025</c> when the course is retired.</summary>
    public string? RetiredNote =>
        Kind == DegreeLookupKind.Found && Info is { IsRetired: true }
            ? "No longer offered; last listed " + OfferingHistory.Label(Info.SourceTerm)
            : null;

    private DegreeCourseLookup(DegreeLookupKind kind, CourseInfo? info)
    {
        Kind = kind;
        Info = info;
    }

    public static DegreeCourseLookup Unknown { get; } = new(DegreeLookupKind.Unknown, null);
    public static DegreeCourseLookup Loading { get; } = new(DegreeLookupKind.Loading, null);
    public static DegreeCourseLookup NotFound { get; } = new(DegreeLookupKind.NotFound, null);
    public static DegreeCourseLookup Failed { get; } = new(DegreeLookupKind.Failed, null);
    public static DegreeCourseLookup Found(CourseInfo info) => new(DegreeLookupKind.Found, info);
}

/// <summary>Why a planned course's prerequisites were not checked.</summary>
public enum DegreePlanUncheckedReason
{
    Checking,
    Failed,
    NotFound,
    NotChecked,
}

/// <summary>Loaded Northeastern catalogs and Banner's term list, as a degree plan screen reads them.</summary>
public sealed class DegreePlanCatalogSnapshot
{
    public DegreePlanCatalogSnapshot(IEnumerable<CatalogIndex> indexes, IEnumerable<Term> terms, string? selectedTermCode)
    {
        Indexes = indexes.ToList();
        Terms = terms.ToList();
        SelectedTermCode = selectedTermCode;
    }

    public IReadOnlyList<CatalogIndex> Indexes { get; }
    public IReadOnlyList<Term> Terms { get; }
    public string? SelectedTermCode { get; }
    public CatalogIndex? Reference => Indexes.Count == 0 ? null : Indexes[0];
    public bool HasData => Indexes.Count > 0;

    public CatalogIndex? Index(string termCode) => Indexes.FirstOrDefault(index => index.Term.Code == termCode);

    /// <summary>The newest index that lists the course with its requisites, else the newest that lists it at all.</summary>
    public CatalogIndex? IndexContaining(CourseKey key) =>
        Indexes.FirstOrDefault(index => index.Course(key) is { DetailsLoaded: true }) ??
        Indexes.FirstOrDefault(index => index.Course(key) is not null);

    public Course? Course(CourseKey key)
    {
        foreach (var index in Indexes)
        {
            var course = index.Course(key);
            if (course is not null) return course;
        }
        return null;
    }

    public string Title(DegreePlanCourse course)
    {
        if (course.Key is CourseKey key)
        {
            var title = Course(key)?.Title;
            if (!string.IsNullOrEmpty(title)) return title;
        }
        return course.Title;
    }

    /// <summary>The Northeastern term on Banner that is this semester, preferring the exact session.</summary>
    public Term? BannerTerm(DegreePlanTerm semester)
    {
        var matches = Terms.Where(term => term.College == TermCollege.Neu && semester.Matches(term)).ToList();
        return matches.FirstOrDefault(term => PlanSeasonInfo.FromTerm(term.Season) == semester.Season) ?? matches.FirstOrDefault();
    }

    public string? RouteTermCode(CourseKey key, DegreePlanTerm? semester = null)
    {
        if (semester is not null)
        {
            var term = BannerTerm(semester);
            if (term is not null && Index(term.Code)?.Course(key) is not null) return term.Code;
        }
        if (SelectedTermCode is not null && Index(SelectedTermCode)?.Course(key) is not null) return SelectedTermCode;
        var containing = IndexContaining(key);
        if (containing is not null) return containing.Term.Code;
        return SelectedTermCode ?? Reference?.Term.Code;
    }

    public string? ScheduleUnavailableReason(DegreePlanTerm semester)
    {
        if (Terms.Count == 0) return "Banner\u2019s term list hasn\u2019t loaded yet";
        if (BannerTerm(semester) is null)
            return semester.HasEnded() ? $"{semester.Name} is over" : $"{semester.Name} isn\u2019t on Banner yet";
        return null;
    }

    /// <summary>
    /// Northeastern indexes from a ready catalog and a term list, newest first.
    /// <paramref name="cachedIndex"/> is asked for each term code.
    /// </summary>
    public static DegreePlanCatalogSnapshot Capture(
        CatalogIndex? readyIndex,
        IEnumerable<Term> terms,
        Func<string, CatalogIndex?> cachedIndex,
        string? selectedTermCode)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var indexes = new List<CatalogIndex>();
        void Add(CatalogIndex? index)
        {
            if (index is null || index.Term.College != TermCollege.Neu) return;
            if (!seen.Add(index.Term.Code)) return;
            indexes.Add(index);
        }
        Add(readyIndex);
        foreach (var term in terms) Add(cachedIndex(term.Code));
        indexes.Sort((a, b) => string.CompareOrdinal(b.Term.Code, a.Term.Code));
        return new DegreePlanCatalogSnapshot(indexes, terms, selectedTermCode);
    }
}

/// <summary>Prerequisite checks and duplicate flags for every course in a plan.</summary>
public sealed class DegreePlanReview
{
    private readonly Dictionary<Guid, DegreePlanAnalysis.PrerequisiteStatus> _statuses = [];
    private readonly Dictionary<Guid, string> _notes = [];
    private readonly Dictionary<Guid, DegreePlanUncheckedReason> _unchecked = [];
    private readonly Dictionary<Guid, string> _offeringNotes = [];

    public int RetiredCount => _offeringNotes.Count;
    public int MissingCount { get; }
    public int UncheckedCount { get; }
    public int CheckingCount { get; }
    public int FailedCount { get; }
    public int DuplicateCount { get; }
    public int PlannedCourseCount { get; }
    public bool HasCatalog { get; }

    public DegreePlanReview(
        DegreePlan plan,
        DegreePlanCatalogSnapshot catalog,
        Func<CourseKey, RequisiteAnswer>? lookup = null,
        CourseEquivalences? equivalences = null,
        IReadOnlyDictionary<CourseKey, DegreeCourseLookup>? lookupStates = null)
    {
        equivalences ??= CourseEquivalences.None;
        lookupStates ??= new Dictionary<CourseKey, DegreeCourseLookup>();
        HasCatalog = catalog.HasData || lookup is not null;
        var duplicates = DegreePlanAnalysis.DuplicateKeys(plan);
        var completed = plan.CompletedKeys;
        var flagged = new HashSet<CourseKey>();
        var missing = 0;
        var uncheckedCount = 0;
        var checking = 0;
        var failed = 0;
        var planned = 0;

        for (var position = 0; position < plan.Terms.Count; position++)
        {
            var term = plan.Terms[position];
            foreach (var course in term.Courses)
            {
                planned++;
                if (course.Key is not CourseKey key) continue;
                var status = DegreePlanAnalysis.ForCourse(
                    key,
                    position,
                    plan,
                    catalog.IndexContaining(key) ?? catalog.Reference,
                    lookup,
                    equivalences);
                _statuses[course.Id] = status;
                if (status.IsMissing) missing++;
                if (status == DegreePlanAnalysis.PrerequisiteStatus.Unknown)
                {
                    lookupStates.TryGetValue(key, out var state);
                    var reason = Reason(state, lookup is not null);
                    _unchecked[course.Id] = reason;
                    uncheckedCount++;
                    if (reason == DegreePlanUncheckedReason.Checking) checking++;
                    if (reason == DegreePlanUncheckedReason.Failed) failed++;
                }
                if (lookupStates.TryGetValue(key, out var looked) && looked.RetiredNote is string retired)
                    _offeringNotes[course.Id] = retired;

                if (!duplicates.Contains(key)) continue;
                flagged.Add(key);
                if (completed.Contains(key))
                {
                    _notes[course.Id] = "Already in your completed courses";
                }
                else
                {
                    var others = plan.Terms.Where(other => other.Id != term.Id && other.Courses.Any(item => item.Key == key)).ToList();
                    _notes[course.Id] = others.Count == 0
                        ? $"Listed more than once in {term.Name}"
                        : "Also planned in " + string.Join(", ", others.Select(other => other.Name));
                }
            }
        }

        MissingCount = missing;
        UncheckedCount = uncheckedCount;
        CheckingCount = checking;
        FailedCount = failed;
        DuplicateCount = flagged.Count;
        PlannedCourseCount = planned;

        foreach (var course in plan.Completed)
        {
            if (course.Key is not CourseKey key || !duplicates.Contains(key)) continue;
            var plannedTerms = plan.Terms.Where(term => term.Courses.Any(item => item.Key == key)).ToList();
            if (plannedTerms.Count == 0) continue;
            _notes[course.Id] = "Also planned in " + string.Join(", ", plannedTerms.Select(term => term.Name));
        }
    }

    public DegreePlanAnalysis.PrerequisiteStatus? Status(DegreePlanCourse course) =>
        _statuses.TryGetValue(course.Id, out var status) ? status : null;

    public string? Note(DegreePlanCourse course) => _notes.TryGetValue(course.Id, out var note) ? note : null;

    public DegreePlanUncheckedReason? UncheckedReason(DegreePlanCourse course) =>
        _unchecked.TryGetValue(course.Id, out var reason) ? reason : null;

    public string? OfferingNote(DegreePlanCourse course) =>
        _offeringNotes.TryGetValue(course.Id, out var note) ? note : null;

    private static DegreePlanUncheckedReason Reason(DegreeCourseLookup? state, bool hasLookup)
    {
        if (!hasLookup) return DegreePlanUncheckedReason.NotChecked;
        return state?.Kind switch
        {
            DegreeLookupKind.Failed => DegreePlanUncheckedReason.Failed,
            DegreeLookupKind.NotFound => DegreePlanUncheckedReason.NotFound,
            _ => DegreePlanUncheckedReason.Checking,
        };
    }
}
