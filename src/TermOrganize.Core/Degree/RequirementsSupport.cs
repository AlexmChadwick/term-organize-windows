// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Shared wording and setup for a plan's degree requirements, read from Northeastern's public
// academic catalog. Term Organize is not affiliated with Northeastern University.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TermOrganize.Core;

/// <summary>Whether a program's requirements have been loaded. A saved copy wins over a failure.</summary>
public abstract record ProgramCatalogLoadState
{
    public static ProgramCatalogLoadState Idle { get; } = new IdleState();
    public static ProgramCatalogLoadState Loading { get; } = new LoadingState();
    public static ProgramCatalogLoadState Failed(string message) => new FailedState(message);

    public sealed record IdleState : ProgramCatalogLoadState;
    public sealed record LoadingState : ProgramCatalogLoadState;
    public sealed record FailedState(string Message) : ProgramCatalogLoadState;

    public string? FailureMessage => this is FailedState failed ? failed.Message : null;
}

/// <summary>
/// A plan's degree requirements as the screens need them: whether the plan is set up, what has loaded,
/// the audit, and when the plan finishes the degree.
/// </summary>
public sealed class DegreeRequirementsContext
{
    public DegreeRequirementsContext(
        DegreePlan plan,
        ProgramRequirements? requirements,
        ProgramCatalogLoadState state,
        CatalogEditions editions,
        DegreeAudit? audit,
        DegreeMilestone? milestone)
    {
        Plan = plan;
        Requirements = requirements;
        State = state;
        Editions = editions;
        Audit = audit;
        Milestone = milestone;
    }

    public DegreePlan Plan { get; }
    public ProgramRequirements? Requirements { get; }
    public ProgramCatalogLoadState State { get; }
    public CatalogEditions Editions { get; }
    public DegreeAudit? Audit { get; }
    /// <summary>When the plan meets every requirement; null when it doesn't (or isn't checked).</summary>
    public DegreeMilestone? Milestone { get; }

    /// <summary>A catalog year and program are chosen.</summary>
    public bool IsSetUp => Plan.RequirementsYear() is not null && Plan.CatalogProgramPath is not null;

    public CatalogYear? Year => Plan.RequirementsYear();

    /// <summary><c>Computer Science, BSCS</c>.</summary>
    public string ProgramTitle =>
        Plan.CatalogProgramTitle ?? Requirements?.Program.TitleWithoutCampus ?? "Your program";

    public RequirementSection? Concentration =>
        Plan.ConcentrationID is string id ? Requirements?.Concentrations.FirstOrDefault(section => section.Id == id) : null;

    /// <summary><c>2023–2024 catalog · Human-Centered Computing</c>.</summary>
    public string CatalogLine
    {
        get
        {
            var parts = new List<string>();
            if (Year is CatalogYear year) parts.Add($"{year} catalog");
            if (Concentration is RequirementSection concentration) parts.Add(concentration.ConcentrationName);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>The error to show when nothing could be loaded (a saved copy wins over errors).</summary>
    public string? Failure => Requirements is null ? State.FailureMessage : null;

    public bool IsLoading => IsSetUp && Requirements is null && Failure is null;

    /// <summary>The program's page in its catalog edition.</summary>
    public Uri? CatalogUrl =>
        Year is CatalogYear year && Plan.CatalogProgramPath is string path ? Editions.Url(year, path) : null;

    public bool IsCurrentEdition => Year is CatalogYear year && year == Editions.Current;

    /// <summary>Requirements the plan doesn't cover yet, in catalog order.</summary>
    public List<DegreeAudit.RuleResult> OpenResults =>
        Audit?.RuleResults.Where(result => !result.Status.IsSatisfied()).ToList() ?? [];

    /// <summary>Catalog sentences the app checks that the plan doesn't meet yet. Inconclusive checks aren't listed.</summary>
    public List<DegreeAudit.NoteCheck> OpenChecks =>
        Audit?.CountedChecks.Where(check => !check.Status.IsSatisfied()).ToList() ?? [];

    /// <summary>A name for a requirement that reads on its own ("Counts toward …").</summary>
    public string Name(string ruleId, string fallback) => Requirements?.DegreeShortName(ruleId) ?? fallback;

    /// <summary>
    /// <c>Counts toward Khoury Elective Courses</c> for a course of the plan (completed when <paramref name="semester"/> is null).
    /// Only the instance the audit counts gets a note.
    /// </summary>
    public string? RequirementNote(DegreePlanCourse course, DegreePlanTerm? semester)
    {
        if (course.Key is not CourseKey key || Audit is null || Requirements is null) return null;
        var result = Audit.RuleCounting(key);
        if (result is null) return null;
        var use = result.Used.FirstOrDefault(item => item.Key == key);
        if (use is null || use.Semester != semester?.Name || use.Credits != course.Credits) return null;
        if (result.Rule.DegreeIsGeneralElectives()) return "Counts as a general elective";
        var name = Requirements.DegreeCountingName(result.Rule.Id, key) ?? result.Rule.DisplayLabel;
        if (use.BySubstitution) return $"Counts toward {name} by substitution";
        if (use.Satisfies is CourseKey required)
            return result.Rule.DegreeNamesCourses() ? $"Counts as {required}" : $"Counts toward {name} as {required}";
        return $"Counts toward {name}";
    }

    /// <summary>
    /// A plan's requirements, audit, and (optionally) finish milestone.
    /// The catalog store's own wrapper is not part of this library port: pass the requirements and load state in.
    /// </summary>
    public static DegreeRequirementsContext Create(
        DegreePlan plan,
        ProgramRequirements? requirements,
        ProgramCatalogLoadState state,
        CatalogEditions editions,
        CourseEquivalences? equivalences = null,
        DegreeCourseAttributes? attributes = null,
        DegreeRequirementsCache? cache = null,
        bool includesMilestone = false)
    {
        equivalences ??= CourseEquivalences.None;
        attributes ??= DegreeCourseAttributes.None;
        DegreeAudit? audit = null;
        DegreeMilestone? milestone = null;
        if (requirements is not null)
        {
            if (cache is not null)
            {
                var results = cache.Results(plan, requirements, equivalences, attributes, includesMilestone);
                audit = results.Audit;
                milestone = results.Milestone;
            }
            else
            {
                audit = DegreeAudit.Run(plan, requirements, equivalences, attributes);
                milestone = includesMilestone ? DegreeMilestone.Find(plan, requirements, equivalences, attributes) : null;
            }
        }
        return new DegreeRequirementsContext(plan, requirements, state, editions, audit, milestone);
    }
}

/// <summary>
/// When a plan meets every degree requirement: with the completed courses alone, or after a planned semester.
/// </summary>
public abstract class DegreeMilestone : IEquatable<DegreeMilestone>
{
    public static DegreeMilestone Completed { get; } = new CompletedMilestone();
    public static DegreeMilestone After(Guid termId, string name) => new AfterMilestone(termId, name);

    public virtual Guid? TermId => null;
    public abstract string SummaryLine { get; }
    public abstract bool Equals(DegreeMilestone? other);
    public override bool Equals(object? obj) => Equals(obj as DegreeMilestone);
    public abstract override int GetHashCode();

    /// <summary>
    /// Audits the plan with only its completed courses, then adding one semester at a time, and returns the first
    /// point where everything is satisfied.
    /// </summary>
    public static DegreeMilestone? Find(
        DegreePlan plan,
        ProgramRequirements requirements,
        CourseEquivalences? equivalences = null,
        DegreeCourseAttributes? attributes = null)
    {
        equivalences ??= CourseEquivalences.None;
        attributes ??= DegreeCourseAttributes.None;
        var partial = DegreePlanCopies.Shell(plan);
        partial.Terms = [];
        if (IsFinished(partial, requirements, equivalences, attributes)) return Completed;
        for (var index = 0; index < plan.Terms.Count; index++)
        {
            if (plan.Terms[index].Courses.Count == 0) continue;
            partial.Terms = plan.Terms.Take(index + 1).ToList();
            if (IsFinished(partial, requirements, equivalences, attributes))
                return After(plan.Terms[index].Id, plan.Terms[index].ShortName);
        }
        return null;
    }

    private static bool IsFinished(
        DegreePlan plan,
        ProgramRequirements requirements,
        CourseEquivalences equivalences,
        DegreeCourseAttributes attributes)
    {
        var audit = DegreeAudit.Run(plan, requirements, equivalences, attributes);
        if (audit.TotalCount == 0 || audit.NeedsConcentration) return false;
        if (!audit.RuleResults.All(result => result.Status.IsSatisfied())) return false;
        if (!audit.CountedChecks.All(check => check.Status.IsSatisfied())) return false;
        if (audit.RequiredHours is double required)
            return audit.CompletedHours + audit.PlannedHours >= required;
        return true;
    }

    private sealed class CompletedMilestone : DegreeMilestone
    {
        public override string SummaryLine => "All degree requirements complete";
        public override bool Equals(DegreeMilestone? other) => other is CompletedMilestone;
        public override int GetHashCode() => 1;
    }

    private sealed class AfterMilestone(Guid termId, string name) : DegreeMilestone
    {
        public override Guid? TermId { get; } = termId;
        public string Name { get; } = name;
        public override string SummaryLine => $"On track to finish after {Name}";
        public override bool Equals(DegreeMilestone? other) => other is AfterMilestone after && after.TermId == TermId && after.Name == Name;
        public override int GetHashCode() => HashCode.Combine(TermId, Name);
    }
}

/// <summary>Remembers the last audit and milestone for a plan so a view can show them without re-running.</summary>
public sealed class DegreeRequirementsCache
{
    private string? _key;
    private DegreeAudit? _audit;
    private bool _milestoneKnown;
    private DegreeMilestone? _milestone;

    public (DegreeAudit Audit, DegreeMilestone? Milestone) Results(
        DegreePlan plan,
        ProgramRequirements requirements,
        CourseEquivalences? equivalences = null,
        DegreeCourseAttributes? attributes = null,
        bool includesMilestone = false)
    {
        equivalences ??= CourseEquivalences.None;
        attributes ??= DegreeCourseAttributes.None;
        var key = CacheKey(plan, requirements, equivalences, attributes);
        if (key != _key)
        {
            _key = key;
            _audit = null;
            _milestoneKnown = false;
            _milestone = null;
        }
        _audit ??= DegreeAudit.Run(plan, requirements, equivalences, attributes);
        if (!includesMilestone) return (_audit, null);
        if (!_milestoneKnown)
        {
            _milestone = DegreeMilestone.Find(plan, requirements, equivalences, attributes);
            _milestoneKnown = true;
        }
        return (_audit, _milestone);
    }

    private static string CacheKey(
        DegreePlan plan,
        ProgramRequirements requirements,
        CourseEquivalences equivalences,
        DegreeCourseAttributes attributes)
    {
        var builder = new StringBuilder();
        builder.Append(DegreePlanCopies.Fingerprint(plan)).Append('|');
        builder.Append(requirements.Year.StartYear).Append('|').Append(requirements.Program.Path).Append('|');
        builder.Append(requirements.FetchedAt.UtcTicks).Append('|').Append(requirements.Concentrations.Count).Append('|');
        builder.Append(equivalences.GetHashCode()).Append('|').Append(attributes.GetHashCode()).Append('|');
        var minimums = requirements.Universitywide;
        builder.Append(minimums?.TotalHours).Append('|').Append(minimums?.Gpa).Append('|');
        builder.Append(minimums?.ResidencyHours).Append('|').Append(minimums?.RequiresExperientialLearning);
        return builder.ToString();
    }
}

internal static class CatalogClock
{
    public static TimeZoneInfo NewYork { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public static DateOnly Today(DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, NewYork).DateTime);
}

internal static class DegreeLists
{
    public static string And(IReadOnlyList<string> items)
    {
        if (items.Count == 0) return "";
        if (items.Count == 1) return items[0];
        if (items.Count == 2) return $"{items[0]} and {items[1]}";
        return string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1];
    }

    public static string Or(IReadOnlyList<string> items) => And(items).Replace(" and ", " or ", StringComparison.Ordinal);
}

internal static class DegreePlanCopies
{
    public static DegreePlan Shell(DegreePlan plan) => new()
    {
        Id = plan.Id,
        Name = plan.Name,
        Program = plan.Program,
        StudentName = plan.StudentName,
        StudentID = plan.StudentID,
        Completed = plan.Completed,
        Terms = plan.Terms,
        IsPrimary = plan.IsPrimary,
        ImportedFrom = plan.ImportedFrom,
        PrintedAt = plan.PrintedAt,
        CreatedAt = plan.CreatedAt,
        UpdatedAt = plan.UpdatedAt,
        CatalogYear = plan.CatalogYear,
        CatalogProgramPath = plan.CatalogProgramPath,
        CatalogProgramTitle = plan.CatalogProgramTitle,
        ConcentrationID = plan.ConcentrationID,
        Substitutions = plan.Substitutions.Select(Clone).ToList(),
        Equivalences = plan.Equivalences,
    };

    public static RequirementSubstitution Clone(RequirementSubstitution source)
    {
        var change = source.Change;
        if (source.Change is SubstitutionChange.ReplaceChange replace)
            change = SubstitutionChange.Replace(Clone(replace.Replacement));
        return new RequirementSubstitution
        {
            Id = source.Id,
            RuleID = source.RuleID,
            RuleTitle = source.RuleTitle,
            Change = change,
            Note = source.Note,
            ApprovedOn = source.ApprovedOn,
            CreatedAt = source.CreatedAt,
        };
    }

    public static RequirementReplacement Clone(RequirementReplacement source) => new()
    {
        Count = source.Count,
        Hours = source.Hours,
        LikeRuleID = source.LikeRuleID,
        LikeRuleTitle = source.LikeRuleTitle,
        Courses = source.Courses.ToList(),
        CourseNumbers = source.CourseNumbers,
    };

    public static string Fingerprint(DegreePlan plan)
    {
        var builder = new StringBuilder();
        builder.Append(plan.Id).Append('\n').Append(plan.ConcentrationID).Append('\n');
        builder.Append(plan.CatalogYear).Append('\n').Append(plan.CatalogProgramPath).Append('\n');
        foreach (var course in plan.Completed) AppendCourse(builder, course, null);
        foreach (var term in plan.Terms)
        {
            builder.Append("T ").Append(term.Id).Append(' ').Append(term.Name).Append('\n');
            foreach (var course in term.Courses) AppendCourse(builder, course, term.Name);
        }
        foreach (var substitution in plan.Substitutions)
        {
            builder.Append("S ").Append(substitution.Id).Append(' ').Append(substitution.RuleID).Append(' ');
            builder.Append(substitution.RuleTitle).Append(' ').Append(substitution.Note).Append(' ');
            builder.Append(substitution.ApprovedOn?.UtcTicks).Append(' ');
            switch (substitution.Change)
            {
                case SubstitutionChange.CountCourseChange count:
                    builder.Append("count ").Append(count.Course);
                    break;
                case SubstitutionChange.ReplaceChange replace:
                    var item = replace.Replacement;
                    builder.Append("replace ").Append(item.Count).Append(' ').Append(item.Hours).Append(' ');
                    builder.Append(item.LikeRuleID).Append(' ').Append(item.LikeRuleTitle).Append(' ');
                    builder.Append(item.CourseNumbers?.Summary).Append(' ');
                    builder.Append(string.Join(",", item.Courses));
                    break;
                default:
                    builder.Append("waive");
                    break;
            }
            builder.Append('\n');
        }
        foreach (var equivalence in plan.Equivalences)
            builder.Append("E ").Append(equivalence.Course).Append(' ').Append(equivalence.CountsAs).Append('\n');
        return builder.ToString();
    }

    private static void AppendCourse(StringBuilder builder, DegreePlanCourse course, string? semester) =>
        builder.Append(semester).Append(' ').Append(course.Key).Append(' ').Append(course.Credits).Append(' ').Append(course.Title).Append('\n');
}

public static class ProgramRequirementsNaming
{
    public static (RequirementSection Section, RequirementGroup Group, RequirementRule Rule)? DegreeLocation(
        this ProgramRequirements requirements, string id)
    {
        foreach (var section in requirements.Sections)
        {
            foreach (var group in section.Groups)
            {
                var rule = group.Rules.FirstOrDefault(item => item.Id == id);
                if (rule is not null) return (section, group, rule);
            }
        }
        return null;
    }

    /// <summary>
    /// A name that makes sense on its own: the group's title, a rule that names its courses, or the concentration.
    /// </summary>
    public static string? DegreeShortName(this ProgramRequirements requirements, string id)
    {
        if (requirements.DegreeLocation(id) is not (var section, var group, var rule)) return null;
        if (group.Rules.Count == 1 && requirements.Title(id) is string title)
        {
            if (section.IsConcentration && group.Title is null && !rule.DegreeNamesCourses())
                return $"{section.ConcentrationName} concentration";
            return title;
        }
        if (rule.DegreeNamesCourses()) return rule.DisplayLabel;
        if (group.Title is string groupTitle) return groupTitle;
        if (section.IsConcentration) return $"{section.ConcentrationName} concentration";
        return section.Title;
    }

    /// <summary>The requirement a course counts toward, for "Counts toward …" captions.</summary>
    public static string? DegreeCountingName(this ProgramRequirements requirements, string id, CourseKey course)
    {
        var name = requirements.DegreeShortName(id);
        if (name is null) return null;
        if (!name.Contains(course.ToString(), StringComparison.Ordinal) || requirements.DegreeLocation(id) is not (var section, var group, _))
            return name;
        if (group.Title is string title) return title;
        return section.IsConcentration ? $"{section.ConcentrationName} concentration" : section.Title;
    }

    public static string DegreeRowTitle(this ProgramRequirements requirements, RequirementRule rule) =>
        requirements.Title(rule.Id) ?? rule.DisplayLabel;

    public static List<CourseKey> DegreeListedCourses(this ProgramRequirements requirements, string? concentrationId)
    {
        var seen = new HashSet<CourseKey>();
        var keys = new List<CourseKey>();
        foreach (var key in requirements.ApplicableSections(concentrationId).SelectMany(section => section.Groups).SelectMany(group => group.Rules).SelectMany(rule => rule.ListedCourses))
        {
            if (seen.Add(key)) keys.Add(key);
        }
        return keys;
    }

    /// <summary>Rules a replacement can draw courses "like".</summary>
    public static List<RequirementRule> DegreeReplacementSources(this ProgramRequirements requirements, string excludingRuleId, string? concentrationId) =>
        requirements.ApplicableSections(concentrationId)
            .SelectMany(section => section.Groups)
            .SelectMany(group => group.Rules)
            .Where(rule => rule.Id != excludingRuleId && rule.Kind switch
            {
                RequirementKind.Choose choose => choose.Options.Count > 0 || choose.Ranges.Count > 0,
                RequirementKind.GeneralElectives => true,
                _ => false,
            })
            .ToList();

    /// <summary>A requirement by id, else the one requirement with this name.</summary>
    public static RequirementRule? DegreeRule(this ProgramRequirements requirements, string? id, string? name)
    {
        if (id is not null && requirements.Rule(id) is RequirementRule byId) return byId;
        if (name is null) return null;
        var lower = name.ToLowerInvariant();
        var matches = requirements.AllRules.Where(rule =>
            requirements.DegreeShortName(rule.Id)?.ToLowerInvariant() == lower
            || requirements.Title(rule.Id)?.ToLowerInvariant() == lower).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}

public static class RequirementRuleText
{
    private static readonly Regex NamesCoursesPattern = new(@"^[A-Z]{2,5}\s?\d{4}", RegexOptions.CultureInvariant);

    /// <summary>The label names courses rather than describing a choice.</summary>
    public static bool DegreeNamesCourses(this RequirementRule rule) => NamesCoursesPattern.IsMatch(rule.DisplayLabel);

    public static bool DegreeIsGeneralElectives(this RequirementRule rule) => rule.Kind is RequirementKind.GeneralElectives;

    /// <summary>Short notes that aren't sentences: Biology, Physics.</summary>
    public static List<string> DegreeCategories(this RequirementRule rule) => rule.Notes.Where(DegreeIsCategory).ToList();

    public static List<string> DegreeParagraphs(this RequirementRule rule) => rule.Notes.Where(note => !DegreeIsCategory(note)).ToList();

    private static bool DegreeIsCategory(string note) => note.Length <= 48 && !note.Contains('.') && !note.Contains(':');
}

public static class DegreeAuditStatusText
{
    public static string DegreeSymbol(this DegreeAudit.Status status) => status switch
    {
        DegreeAudit.Status.Complete => "checkmark.circle.fill",
        DegreeAudit.Status.Planned => "calendar.circle.fill",
        DegreeAudit.Status.Partial => "circle.lefthalf.filled",
        DegreeAudit.Status.Missing => "circle",
        DegreeAudit.Status.Waived => "minus.circle.fill",
        _ => "circle",
    };

    /// <summary>green, blue, orange, secondary, or gray.</summary>
    public static string DegreeColor(this DegreeAudit.Status status) => status switch
    {
        DegreeAudit.Status.Complete => "green",
        DegreeAudit.Status.Planned => "blue",
        DegreeAudit.Status.Partial => "orange",
        DegreeAudit.Status.Missing => "secondary",
        DegreeAudit.Status.Waived => "gray",
        _ => "secondary",
    };

    public static string DegreeLabel(this DegreeAudit.Status status) => status switch
    {
        DegreeAudit.Status.Complete => "complete",
        DegreeAudit.Status.Planned => "planned",
        DegreeAudit.Status.Partial => "partly done",
        DegreeAudit.Status.Missing => "not in your plan",
        DegreeAudit.Status.Waived => "waived",
        _ => "",
    };
}

public static partial class DegreeRequirementText
{
    /// <summary><c>4 semester hours</c>, <c>1 semester hour</c>.</summary>
    public static string Hours(double value) =>
        $"{CreditFormatter.Number(value)} {(value == 1 ? "semester hour" : "semester hours")}";

    /// <summary><c>105 of 134 credits done · 29 planned</c>.</summary>
    public static string CreditsLine(DegreeAudit audit)
    {
        var done = CreditFormatter.Number(audit.CompletedHours);
        var line = audit.RequiredHours is double required
            ? $"{done} of {CreditFormatter.Number(required)} credits done"
            : $"{done} credits done";
        if (audit.PlannedHours > 0) line += $" · {CreditFormatter.Number(audit.PlannedHours)} planned";
        return line;
    }

    /// <summary><c>Your plan covers 21 of 25 requirements</c>.</summary>
    public static string CoverageLine(DegreeAudit audit)
    {
        if (audit.TotalCount > 0 && audit.SatisfiedCount == audit.TotalCount)
            return audit.TotalCount == 1 ? "Your plan covers the requirement" : $"Your plan covers all {audit.TotalCount} requirements";
        return $"Your plan covers {audit.SatisfiedCount} of {audit.TotalCount} requirements";
    }

    /// <summary>
    /// What's left of a requirement: <c>1 more of: CS 4550, DS 4200</c>, <c>4 semester hours more</c>,
    /// <c>Still needs EECE 2323</c>. Null when nothing is left.
    /// </summary>
    public static string? Remaining(DegreeAudit.RuleResult result)
    {
        if (result.EffectiveKind is not RequirementKind kind || result.Status.IsSatisfied()) return null;
        var usedKeys = result.Used.SelectMany(use => use.Satisfies is CourseKey named ? new[] { use.Key, named } : new[] { use.Key }).ToHashSet();
        switch (kind)
        {
            case RequirementKind.Required required:
                if (result.Used.Count == 0) return null;
                var missing = required.Option.Alternatives
                    .Select(alternative => alternative.Where(key => !usedKeys.Contains(key)).ToList())
                    .MinBy(keys => keys.Count) ?? [];
                return missing.Count == 0 ? null : "Still needs " + string.Join(" and ", missing.Select(key => key.ToString()));
            case RequirementKind.Choose choose:
                if (result.RemainingCount is int count && count > 0)
                {
                    var open = choose.Options.Where(option => !option.AllCourses.Any(usedKeys.Contains)).ToList();
                    if (open.Count > 0)
                    {
                        var names = string.Join(", ", open.Take(3).Select(option => option.Label));
                        return $"{count} more of: {names}{(open.Count > 3 ? ", …" : "")}";
                    }
                    if (choose.Ranges.Count > 0) return $"{count} more from {choose.Ranges[0].Label}";
                    return count == 1 ? "1 more course" : $"{count} more courses";
                }
                if (result.RemainingHours is double hours && hours > 0) return $"{Hours(hours)} more";
                return null;
            case RequirementKind.GeneralElectives:
                if (result.RemainingHours is double left && left > 0) return $"{Hours(left)} more";
                return null;
            default:
                return null;
        }
    }

    public static string SubstitutionTitle(RequirementSubstitution substitution, string ruleName) =>
        substitution.Change is SubstitutionChange.CountCourseChange count
            ? $"{count.Course} counts toward {ruleName}"
            : ruleName;

    public static string? SubstitutionDetail(RequirementSubstitution substitution) => substitution.Change switch
    {
        SubstitutionChange.ReplaceChange replace => "Replaced by " + LowercasingFirst(replace.Replacement.Summary),
        _ when substitution.Change.Equals(SubstitutionChange.Waive) => "Waived",
        _ => null,
    };

    /// <summary><c>Petition approved by Khoury advising · Approved Sep 12, 2026</c>.</summary>
    public static string? SubstitutionCaption(RequirementSubstitution substitution)
    {
        var parts = new List<string>();
        if (BlankToNull(substitution.Note) is string note) parts.Add(note);
        if (substitution.ApprovedOn is DateTimeOffset date) parts.Add("Approved " + ApprovedDate(date));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    public static string LowercasingFirst(string text)
    {
        if (text.Length == 0 || !char.IsUpper(text[0])) return text;
        if (text.Length > 1 && char.IsUpper(text[1])) return text;
        return char.ToLowerInvariant(text[0]) + text[1..];
    }

    /// <summary><c>Sep 12, 2026</c>, in the date's own offset.</summary>
    public static string ApprovedDate(DateTimeOffset date) =>
        date.ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));

    internal static string? BlankToNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>A course in the plan, once, for pickers: completed courses first, then each semester's.</summary>
public sealed class DegreePlanCourseChoice : IEquatable<DegreePlanCourseChoice>
{
    public DegreePlanCourseChoice(CourseKey key, string title, double credits, string? semester = null, string? semesterShortName = null)
    {
        Key = key;
        Title = title;
        Credits = credits;
        Semester = semester;
        SemesterShortName = semesterShortName;
    }

    public CourseKey Key { get; }
    public string Title { get; }
    public double Credits { get; }
    /// <summary><c>Spring 2027</c>; null for completed courses.</summary>
    public string? Semester { get; }
    /// <summary><c>Summer A 2027</c>.</summary>
    public string? SemesterShortName { get; }

    public CourseKey Id => Key;
    public bool IsCompleted => Semester is null;
    /// <summary><c>Completed</c> or <c>Summer A 2027</c>.</summary>
    public string WhenLine => SemesterShortName ?? "Completed";

    public bool Equals(DegreePlanCourseChoice? other) =>
        other is not null && Key == other.Key && Title == other.Title && Credits == other.Credits
        && Semester == other.Semester && SemesterShortName == other.SemesterShortName;

    public override bool Equals(object? obj) => Equals(obj as DegreePlanCourseChoice);
    public override int GetHashCode() => HashCode.Combine(Key, Title, Credits, Semester, SemesterShortName);
}

public static class DegreePlanRequirementInfo
{
    public static CatalogYear? RequirementsYear(this DegreePlan plan) =>
        plan.CatalogYear is int year ? new CatalogYear(year) : null;

    public static List<CourseKey> DegreePlanCourseKeys(this DegreePlan plan)
    {
        var seen = new HashSet<CourseKey>();
        var keys = new List<CourseKey>();
        foreach (var key in plan.Completed.Select(course => course.Key).Concat(plan.Terms.SelectMany(term => term.Courses).Select(course => course.Key)))
        {
            if (key is CourseKey course && seen.Add(course)) keys.Add(course);
        }
        return keys;
    }

    public static List<DegreePlanCourseChoice> DegreeCourseChoices(this DegreePlan plan)
    {
        var seen = new HashSet<CourseKey>();
        var choices = new List<DegreePlanCourseChoice>();
        foreach (var course in plan.Completed)
        {
            if (course.Key is not CourseKey key || !seen.Add(key)) continue;
            choices.Add(new DegreePlanCourseChoice(key, course.Title, course.Credits));
        }
        foreach (var term in plan.Terms)
        {
            foreach (var course in term.Courses)
            {
                if (course.Key is not CourseKey key || !seen.Add(key)) continue;
                choices.Add(new DegreePlanCourseChoice(key, course.Title, course.Credits, term.Name, term.ShortName));
            }
        }
        return choices;
    }

    /// <summary><c>Summer Session A 2027</c> → <c>Summer A 2027</c>.</summary>
    public static Dictionary<string, string> DegreeSemesterShortNames(this DegreePlan plan)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var term in plan.Terms) names.TryAdd(term.Name, term.ShortName);
        return names;
    }

    /// <summary>The Banner code of this semester at Northeastern (<c>202730</c> for Spring 2027).</summary>
    public static string DegreePlanBannerCode(this DegreePlanTerm term) => term.Season switch
    {
        PlanSeason.Fall => $"{term.Year + 1}10",
        PlanSeason.Winter => $"{term.Year}20",
        PlanSeason.Spring => $"{term.Year}30",
        PlanSeason.SummerA => $"{term.Year}40",
        PlanSeason.SummerFull => $"{term.Year}50",
        PlanSeason.SummerB => $"{term.Year}60",
        _ => $"{term.Year + 1}10",
    };

    /// <summary>Whether the semester is over (approximately), so it shouldn't be offered as "next".</summary>
    public static bool DegreePlanHasEnded(this DegreePlanTerm term, DateTimeOffset? now = null)
    {
        var today = CatalogClock.Today(now ?? DateTimeOffset.Now);
        var window = TermCalendar.Window(new Term(term.DegreePlanBannerCode(), ""));
        return window.End < today;
    }
}
