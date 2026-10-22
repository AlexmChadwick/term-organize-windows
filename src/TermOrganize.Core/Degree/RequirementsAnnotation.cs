// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Where each course the academic catalog lists stands in a plan. Term Organize is not
// affiliated with, endorsed by, or sponsored by Northeastern University.

namespace TermOrganize.Core;

/// <summary>Where a course stands in the student's plan: the requirements page's status column.</summary>
public abstract record DegreeCourseStatus
{
    public sealed record Completed(string? Term) : DegreeCourseStatus;
    public sealed record InProgress(string Term) : DegreeCourseStatus;
    public sealed record Planned(string Term) : DegreeCourseStatus;
    public sealed record NotInPlan : DegreeCourseStatus
    {
        public static NotInPlan Instance { get; } = new();
    }

    public sealed record NotNeeded : DegreeCourseStatus
    {
        public static NotNeeded Instance { get; } = new();
    }

    /// <summary><c>Completed</c>, <c>In progress · Fall 2026</c>, <c>Planned · Spring 2027</c>, <c>Not in plan</c>.</summary>
    public string Label => this switch
    {
        Completed completed => completed.Term is string term ? $"Completed \u00b7 {term}" : "Completed",
        InProgress progress => $"In progress \u00b7 {progress.Term}",
        Planned planned => $"Planned \u00b7 {planned.Term}",
        NotNeeded => "Not needed",
        _ => "Not in plan",
    };

    /// <summary><c>completed</c>, <c>in progress, Fall 2026</c>, <c>planned for Spring 2027</c>.</summary>
    public string Spoken => this switch
    {
        Completed completed => completed.Term is string term ? $"completed, {term}" : "completed",
        InProgress progress => $"in progress, {progress.Term}",
        Planned planned => $"planned for {planned.Term}",
        NotNeeded => "not needed",
        _ => "not in plan",
    };

    /// <summary>green, blue, or secondary.</summary>
    public string Color => this switch
    {
        Completed => "green",
        InProgress or Planned => "blue",
        _ => "secondary",
    };

    public bool IsInPlan => this is not NotInPlan and not NotNeeded;
}

/// <summary>
/// How a course the catalog lists stands for one requirement: its status, whether it counts
/// toward the requirement, a caption, and what "Add to plan" would add.
/// </summary>
public sealed class DegreeCourseRowInfo
{
    public DegreeCourseStatus Status { get; set; } = DegreeCourseStatus.NotInPlan.Instance;
    public bool IsCountedHere { get; set; }
    public bool BySubstitution { get; set; }
    public string? Caption { get; set; }
    /// <summary>
    /// The course "Add to plan" adds: the listed course, or a current equivalent of one that is
    /// no longer offered. Null when there is nothing to add.
    /// </summary>
    public CourseKey? AddKey { get; set; }
}

/// <summary>
/// Answers, for any course the catalog lists: is it in the plan (and when), where does the
/// audit count it, and what is it called. Swift calls this <c>DegreeCourseLookup</c>; that name
/// is the course-catalog lookup state (<see cref="DegreeCourseLookup"/>).
/// </summary>
public sealed class DegreeRequirementsLookup
{
    private readonly Dictionary<CourseKey, DegreePlanCourseChoice> _choices;
    private readonly HashSet<CourseKey> _planKeys;
    private readonly IReadOnlyDictionary<CourseKey, DegreeCourseLookup> _infoStates;
    private readonly Dictionary<CourseKey, (string RuleId, DegreeAudit.CourseUse Use)> _countedBy;
    private readonly Dictionary<CourseKey, string> _inProgressHints;
    private readonly Dictionary<CourseKey, string> _completedHints;
    private readonly HashSet<string> _semestersInSession;
    private readonly Func<CourseKey, Course?> _bannerCourse;

    public ProgramRequirements Requirements { get; }
    public DegreeAudit Audit { get; }
    /// <summary>Which courses count as which: Banner's and the plan's declared ones.</summary>
    public CourseEquivalences Equivalences { get; }

    public DegreeRequirementsLookup(
        DegreePlan plan,
        ProgramRequirements requirements,
        DegreeAudit audit,
        IReadOnlyList<SchedulePlan> schedulePlans,
        Func<string, string> termName,
        Func<CourseKey, Course?> bannerCourse,
        CourseEquivalences? equivalences = null,
        IReadOnlyDictionary<CourseKey, DegreeCourseLookup>? infoStates = null,
        DateTimeOffset? now = null)
    {
        Requirements = requirements;
        Audit = audit;
        _bannerCourse = bannerCourse;
        Equivalences = (equivalences ?? CourseEquivalences.None).Merging(plan.DeclaredEquivalences);
        _infoStates = infoStates ?? new Dictionary<CourseKey, DegreeCourseLookup>();
        _choices = [];
        foreach (var choice in plan.DegreeCourseChoices()) _choices.TryAdd(choice.Key, choice);
        _planKeys = _choices.Keys.ToHashSet();

        _countedBy = [];
        foreach (var result in audit.RuleResults)
        {
            foreach (var use in result.Used)
                _countedBy.TryAdd(use.Key, (result.Rule.Id, use));
        }

        var moment = now ?? DateTimeOffset.Now;
        var inProgress = new Dictionary<CourseKey, string>();
        var completed = new Dictionary<CourseKey, (string Name, DateTimeOffset End)>();
        foreach (var schedule in schedulePlans.Where(item => item.IsPrimary))
        {
            var window = TermCalendar.ApproximateWindow(schedule.TermCode);
            if (window is null) continue;
            var name = termName(schedule.TermCode);
            foreach (var course in schedule.Courses)
            {
                if (moment >= window.Value.Start && moment <= window.Value.End)
                    inProgress[course.Key] = name;
                else if (window.Value.End < moment && (!completed.TryGetValue(course.Key, out var prior) || prior.End < window.Value.End))
                    completed[course.Key] = (name, window.Value.End);
            }
        }
        _inProgressHints = inProgress;
        _completedHints = completed.ToDictionary(pair => pair.Key, pair => pair.Value.Name);
        _semestersInSession = plan.Terms
            .Where(term =>
            {
                var window = TermCalendar.ApproximateWindow(term.BannerCode());
                return window is not null && moment >= window.Value.Start && moment <= window.Value.End;
            })
            .Select(term => term.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    public DegreeCourseStatus StatusOf(CourseKey key)
    {
        if (!_choices.TryGetValue(key, out var choice)) return DegreeCourseStatus.NotInPlan.Instance;
        if (choice.Semester is string semester)
        {
            var name = choice.SemesterShortName ?? semester;
            return _semestersInSession.Contains(semester)
                ? new DegreeCourseStatus.InProgress(name)
                : new DegreeCourseStatus.Planned(name);
        }
        if (_inProgressHints.TryGetValue(key, out var term)) return new DegreeCourseStatus.InProgress(term);
        _completedHints.TryGetValue(key, out var completed);
        return new DegreeCourseStatus.Completed(completed);
    }

    /// <summary>The catalog's title, else Banner's, else the plan's.</summary>
    public string? TitleOf(CourseKey key)
    {
        if (Requirements.CourseTitles.TryGetValue(key, out var catalog)) return catalog;
        var banner = _bannerCourse(key);
        if (banner is not null) return banner.Title;
        if (_infoStates.TryGetValue(key, out var state) && NilIfBlank(state.Info?.Title) is string infoTitle) return infoTitle;
        return _choices.TryGetValue(key, out var choice) ? NilIfBlank(choice.Title) : null;
    }

    /// <summary>The credits of the listing the audit counts, else the plan's, else Banner's.</summary>
    public double? CreditsOf(CourseKey key)
    {
        if (_countedBy.TryGetValue(key, out var counted) && counted.Use.Credits > 0) return counted.Use.Credits;
        if (_choices.TryGetValue(key, out var choice) && choice.Credits > 0) return choice.Credits;
        var banner = _bannerCourse(key);
        if (banner is not null) return banner.StartingCredits;
        if (_infoStates.TryGetValue(key, out var state) && state.Info?.MaxCredits is double max) return max;
        return _choices.TryGetValue(key, out choice) ? choice.Credits : null;
    }

    /// <summary>Only catalogs older than the newest term list the course.</summary>
    public bool IsRetired(CourseKey key) =>
        _infoStates.TryGetValue(key, out var state) && state.IsRetired == true;

    /// <summary>
    /// <c>No longer offered; last listed Spring 2025</c>, or <c>Not in Banner’s catalog for recent terms</c>.
    /// Null for a current course, or one not looked up.
    /// </summary>
    public string? OfferingNote(CourseKey key)
    {
        if (!_infoStates.TryGetValue(key, out var state)) return null;
        if (state.RetiredNote is string retired) return retired;
        return state.Kind == DegreeLookupKind.NotFound ? "Not in Banner\u2019s catalog for recent terms" : null;
    }

    /// <summary>A course of the plan that counts as <paramref name="key"/> (not <paramref name="key"/> itself).</summary>
    public CourseKey? EquivalentInPlan(CourseKey key)
    {
        foreach (var satisfier in Equivalences.Satisfiers(key, _planKeys))
        {
            if (satisfier != key) return satisfier;
        }
        return null;
    }

    /// <summary>A course that counts as <paramref name="key"/> and is still offered.</summary>
    public CourseKey? CurrentEquivalent(CourseKey key)
    {
        foreach (var equivalent in Equivalences.EquivalentsOf(key).OrderBy(item => item))
        {
            if (!IsRetired(equivalent)) return equivalent;
        }
        return null;
    }

    /// <summary>Where a listed course stands for a requirement. Pass <paramref name="counted"/> for a course the requirement counts that it doesn't list.</summary>
    public DegreeCourseRowInfo RowInfo(CourseKey key, DegreeAudit.RuleResult result, DegreeAudit.CourseUse? counted = null)
    {
        var use = counted ?? result.Use(key);
        if (use is not null)
        {
            if (use.Key != key)
            {
                return new DegreeCourseRowInfo
                {
                    Status = StatusOf(use.Key),
                    IsCountedHere = true,
                    BySubstitution = use.BySubstitution,
                    Caption = $"Via {use.Key} ({use.CountsAsLabel ?? $"counts as {key}"})",
                };
            }
            return new DegreeCourseRowInfo
            {
                Status = StatusOf(key),
                IsCountedHere = true,
                BySubstitution = use.BySubstitution,
                Caption = use.Satisfies is CourseKey named ? $"Counts as {named}" : null,
            };
        }
        if (!_choices.ContainsKey(key) && result.EffectiveKind is RequirementKind.Required && result.Status.IsSatisfied()
            && result.Used.FirstOrDefault(item => item.BySubstitution) is DegreeAudit.CourseUse substitute)
        {
            return new DegreeCourseRowInfo
            {
                Status = DegreeCourseStatus.NotNeeded.Instance,
                Caption = $"{substitute.Key} counts instead",
            };
        }
        if (_choices.ContainsKey(key))
        {
            return new DegreeCourseRowInfo
            {
                Status = StatusOf(key),
                Caption = CountingCaption(key, result.Rule.Id),
            };
        }
        if (EquivalentInPlan(key) is CourseKey equivalent)
        {
            var caption = $"{equivalent} counts as this course";
            var elsewhere = CountingCaption(equivalent, result.Rule.Id);
            if (elsewhere is not null && _countedBy.ContainsKey(equivalent)) caption += " \u00b7 " + elsewhere;
            return new DegreeCourseRowInfo { Status = StatusOf(equivalent), Caption = caption };
        }
        if (IsRetired(key))
        {
            var current = CurrentEquivalent(key);
            var parts = new List<string>();
            if (OfferingNote(key) is string note) parts.Add(note);
            if (current is CourseKey currentKey) parts.Add($"{currentKey} counts as it");
            return new DegreeCourseRowInfo
            {
                Status = DegreeCourseStatus.NotInPlan.Instance,
                Caption = string.Join(" \u00b7 ", parts),
                AddKey = current,
            };
        }
        return new DegreeCourseRowInfo
        {
            Status = DegreeCourseStatus.NotInPlan.Instance,
            Caption = OfferingNote(key),
            AddKey = key,
        };
    }

    /// <summary>The requirement the audit counts a course toward, if any.</summary>
    public string? CountingRuleId(CourseKey key) =>
        _countedBy.TryGetValue(key, out var counted) ? counted.RuleId : null;

    public bool IsCountedBySubstitution(CourseKey key) =>
        _countedBy.TryGetValue(key, out var counted) && counted.Use.BySubstitution;

    /// <summary>
    /// <c>Counts toward Khoury Elective Courses</c>, <c>Counts as a general elective</c>, or
    /// <c>Doesn’t count toward a requirement</c>.
    /// </summary>
    public string? CountingCaption(CourseKey key, string? exceptRuleId = null)
    {
        if (!_choices.ContainsKey(key)) return null;
        if (!_countedBy.TryGetValue(key, out var counted)) return "Doesn\u2019t count toward a requirement";
        if (counted.RuleId == exceptRuleId) return null;
        if (Requirements.Rule(counted.RuleId)?.DegreeIsGeneralElectives() == true) return "Counts as a general elective";
        return $"Counts toward {Requirements.DegreeCountingName(counted.RuleId, key) ?? "another requirement"}";
    }

    /// <summary>The plan's semesters a course can still be added to; when there are none, the next few.</summary>
    public static List<DegreePlanTerm> AddTargets(DegreePlan plan)
    {
        var upcoming = plan.Terms.Where(term => !term.HasEnded()).ToList();
        return upcoming.Count == 0 ? DegreePlanAutofill.NextSemesters(plan).Take(3).ToList() : upcoming;
    }

    /// <summary>A Banner term code's short name: <c>Fall 2026</c>, <c>Summer A 2027</c>.</summary>
    public static string TermName(string code, IReadOnlyList<Term> terms)
    {
        var known = terms.FirstOrDefault(term => term.Code == code);
        if (known is not null && known.BannerDescription.Length > 0 && !known.BannerDescription.StartsWith("Term ", StringComparison.Ordinal))
            return known.ShortName;
        var parsed = new Term(code, "");
        return new DegreePlanTerm { Season = PlanSeasonInfo.FromTerm(parsed.Season), Year = parsed.CalendarYear }.ShortName;
    }

    private static string? NilIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}

public static partial class DegreeRequirementText
{
    /// <summary>
    /// A list's or hour requirement's progress: <c>1 of 1 done</c>, <c>2 of 2 planned</c>,
    /// <c>Needs 1 more</c>, <c>Needs 4 more semester hours</c>, <c>Waived</c>.
    /// </summary>
    public static string Progress(DegreeAudit.RuleResult result)
    {
        var verb = result.Status == DegreeAudit.Status.Planned ? "planned" : "done";
        switch (result.EffectiveKind)
        {
            case null:
                return "Waived";
            case RequirementKind.Required:
                return result.Status.IsSatisfied()
                    ? (result.Status == DegreeAudit.Status.Planned ? "Planned" : "Done")
                    : (Remaining(result) ?? "Not in plan");
            case RequirementKind.Choose choose:
                if (choose.Count is int count)
                {
                    var left = result.RemainingCount ?? 0;
                    return left == 0 ? $"{count} of {count} {verb}" : $"Needs {left} more";
                }
                if (choose.Hours is double hours)
                {
                    var left = result.RemainingHours ?? 0;
                    return left == 0
                        ? $"{CreditFormatter.Number(hours)} of {Hours(hours)} {verb}"
                        : $"Needs {Hours(left)} more";
                }
                return result.Status.IsSatisfied() ? Capitalize(verb) : "Needs 1 more";
            case RequirementKind.GeneralElectives general:
                var remaining = result.RemainingHours ?? 0;
                return remaining == 0
                    ? $"{CreditFormatter.Number(general.Hours)} of {Hours(general.Hours)} {verb}"
                    : $"Needs {Hours(remaining)} more";
            default:
                return "";
        }
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
