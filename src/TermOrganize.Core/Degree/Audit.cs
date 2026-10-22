// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Checks a degree plan against its program's requirements from the academic catalog.
// A planning aid; the official audit is the registrar's. Term Organize is not affiliated
// with Northeastern University.

using System.Globalization;
using System.Text.RegularExpressions;

namespace TermOrganize.Core;

/// <summary>
/// Which requirements are complete, planned, partly done, or missing, and where each course counts.
/// Each course counts toward one requirement. Approved substitutions apply first.
/// </summary>
public sealed partial class DegreeAudit
{
    public enum Status
    {
        Missing,
        Partial,
        Planned,
        Complete,
        Waived,
    }

    public sealed class CourseUse
    {
        public CourseKey Key { get; set; }
        public string Title { get; set; } = "";
        public double Credits { get; set; }
        public string? Semester { get; set; }
        public int Order { get; set; }
        public bool BySubstitution { get; set; }
        public CourseKey? Satisfies { get; set; }
        public int Instance { get; set; }
        /// <summary>The degree-plan row this entry came from. A substitution names this id, not only the course number.</summary>
        public Guid RowId { get; set; }
        /// <summary>Every copy that has credit counts. A zero-credit line is not a second course.</summary>
        public bool CountsTowardHours { get; set; } = true;

        /// <summary>Manual section, when the plan named one. Distinct CRNs are separate audit entries.</summary>
        public string? SelectedCrn { get; set; }

        public string Id => Instance == 0 ? Key.ToString() : $"{Key} ({Instance + 1})";
        public bool IsCompleted => Semester is null;
        public bool ViaEquivalence => Satisfies is not null;
        public string? CountsAsLabel => Satisfies is CourseKey key ? $"counts as {key}" : null;

        public CourseUse Copy() => new()
        {
            Key = Key,
            Title = Title,
            Credits = Credits,
            Semester = Semester,
            Order = Order,
            BySubstitution = BySubstitution,
            Satisfies = Satisfies,
            Instance = Instance,
            RowId = RowId,
            CountsTowardHours = CountsTowardHours,
            SelectedCrn = SelectedCrn,
        };
    }

    public sealed class RuleResult
    {
        public required RequirementRule Rule { get; init; }
        public RequirementKind? EffectiveKind { get; init; }
        public Status Status { get; init; }
        public List<CourseUse> Used { get; init; } = [];
        public int? RemainingCount { get; init; }
        public double? RemainingHours { get; init; }
        public List<RequirementSubstitution> Substitutions { get; init; } = [];
        public string Id => Rule.Id;

        public CourseUse? Use(CourseKey required) =>
            Used.FirstOrDefault(use => use.Key == required && use.Satisfies is null)
            ?? Used.FirstOrDefault(use => use.Satisfies == required);
    }

    public sealed class GroupResult
    {
        public required RequirementGroup Group { get; init; }
        public List<RuleResult> Rules { get; init; } = [];
        public string Id => Group.Id;
        public Status Status => Rules.Count == 0 ? Status.Complete : Rules.Min(rule => rule.Status);
    }

    public sealed class SectionResult
    {
        public required RequirementSection Section { get; init; }
        public List<GroupResult> Groups { get; init; } = [];
        public string Id => Section.Id;
        public List<RuleResult> Rules => Groups.SelectMany(group => group.Rules).ToList();
        public Status Status => Rules.Count == 0 ? Status.Complete : Rules.Min(rule => rule.Status);
    }

    public sealed class NoteCheck
    {
        public abstract record Kind
        {
            public sealed record MajorHours(double Required) : Kind;
            public sealed record Coop : Kind;
            public sealed record Nupath : Kind;
            public sealed record Universitywide : Kind;
            public sealed record TotalHours(double Required) : Kind;
        }

        public sealed class Item
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public Status? Status { get; set; }
            public string Detail { get; set; } = "";
            public List<string> Courses { get; set; } = [];
        }

        public string SectionId { get; set; } = "";
        public string Note { get; set; } = "";
        public Kind CheckKind { get; set; } = new Kind.Coop();
        public Status Status { get; set; }
        public string Detail { get; set; } = "";
        public List<Item> Items { get; set; } = [];
        public bool Counts { get; set; } = true;
        public string Id => $"{SectionId}|{Note}";

        public string Title => CheckKind switch
        {
            Kind.MajorHours => "Semester hours in the major",
            Kind.Coop => "Co-op",
            Kind.Nupath => "NUpath requirements",
            Kind.Universitywide => "Universitywide requirements",
            Kind.TotalHours => "Total semester hours",
            _ => "",
        };
    }

    public ProgramRequirements Requirements { get; internal set; } = new();
    public List<SectionResult> Sections { get; internal set; } = [];
    public List<CourseUse> GeneralElectives { get; internal set; } = [];
    public List<CourseUse> Unused { get; internal set; } = [];
    public double CompletedHours { get; internal set; }
    public double PlannedHours { get; internal set; }
    public double? RequiredHours { get; internal set; }
    public List<RequirementSubstitution> UnmatchedSubstitutions { get; internal set; } = [];
    public Dictionary<Guid, string> Placements { get; internal set; } = [];
    public bool NeedsConcentration { get; internal set; }
    public List<NoteCheck> NoteChecks { get; internal set; } = [];
    public DegreeCourseAttributes Attributes { get; internal set; } = DegreeCourseAttributes.None;

    public List<RuleResult> RuleResults => Sections.SelectMany(section => section.Rules).ToList();
    public List<NoteCheck> CountedChecks => NoteChecks.Where(check => check.Counts).ToList();
    public int SatisfiedCount => RuleResults.Count(result => result.Status.IsSatisfied()) + CountedChecks.Count(check => check.Status.IsSatisfied());
    public int CompleteCount =>
        RuleResults.Count(result => result.Status is Status.Complete or Status.Waived) +
        CountedChecks.Count(check => check.Status == Status.Complete);
    public int TotalCount => RuleResults.Count + CountedChecks.Count;

    public NoteCheck? NoteCheckFor(string sectionId, string note) =>
        NoteChecks.FirstOrDefault(check => check.SectionId == sectionId && check.Note == note);

    public RuleResult? Result(string ruleId) => RuleResults.FirstOrDefault(result => result.Rule.Id == ruleId);

    public RuleResult? RuleCounting(CourseKey key) =>
        RuleResults.FirstOrDefault(result => result.Used.Any(use => use.Key == key));

    /// <summary>Courses that still-open requirements list and the plan doesn't have, in catalog order.</summary>
    public List<CourseKey> DegreeOpenListedCourses(DegreePlan plan)
    {
        var inPlan = plan.DegreePlanCourseKeys().ToHashSet();
        var seen = new HashSet<CourseKey>();
        var keys = new List<CourseKey>();
        foreach (var key in RuleResults.Where(result => !result.Status.IsSatisfied()).SelectMany(result => result.Rule.ListedCourses))
        {
            if (!inPlan.Contains(key) && seen.Add(key)) keys.Add(key);
        }
        return keys;
    }

    public static HashSet<string>? FewestCreditsChoice(IReadOnlyList<CourseUse> uses, double need) =>
        AuditEngine.FewestCredits(uses, need);

    public static DegreeAudit Run(
        DegreePlan plan,
        ProgramRequirements requirements,
        CourseEquivalences? equivalences = null,
        DegreeCourseAttributes? attributes = null)
    {
        equivalences ??= CourseEquivalences.None;
        attributes ??= DegreeCourseAttributes.None;
        var engine = new AuditEngine(plan, requirements, equivalences.Merging(plan.DeclaredEquivalences), attributes);
        var audit = engine.Run();
        audit.Attributes = attributes;
        return audit;
    }

    public static bool IsCoopNote(string note) => CoopNotePattern().IsMatch(note);

    public static bool IsNupathNote(string note) => NupathNotePattern().IsMatch(note);

    public static bool IsUniversitywideNote(string note) => UniversitywideNotePattern().IsMatch(note);

    public static double? TotalHours(string note)
    {
        var match = TotalHoursPattern().Match(note);
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) ? hours : null;
    }

    public static double? MajorHours(string note)
    {
        var match = MajorHoursPattern().Match(note);
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) ? hours : null;
    }

    /// <summary>Why a catalog sentence cannot be checked from a plan. Null for anything else.</summary>
    public static string? UncheckedReason(string note)
    {
        if (GradeNotePattern().IsMatch(note)) return "Not checked: plans don\u2019t include grades";
        if (ResidencyNotePattern().IsMatch(note)) return "Not checked: a plan doesn\u2019t say where credits were earned";
        if (ConcentrationNotePattern().IsMatch(note)) return "Checked through the concentration you choose";
        return null;
    }

    /// <summary>
    /// How a semester-hour minimum stands, for the university minimum, the program total,
    /// and hours in the major. The line is <c>completed of required semester hours</c>,
    /// then <c> · N planned</c> when planned credits are above zero. The first number
    /// is credits already earned. It is not completed plus planned.
    /// Complete when completed meets the requirement, planned when the rest is in a later term,
    /// partial when some credits count and the sum is still short, and missing when both are zero.
    /// </summary>
    public static (Status Status, string Detail) HoursStatus(double completed, double planned, double required)
    {
        var status = completed >= required ? Status.Complete
            : completed + planned >= required ? Status.Planned
            : completed + planned > 0 ? Status.Partial
            : Status.Missing;
        var detail = $"{HoursText(completed)} of {HoursText(required)} semester hours";
        if (planned > 0) detail += $" \u00b7 {HoursText(planned)} planned";
        return (status, detail);
    }

    internal static string HoursText(double value)
    {
        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return rounded == Math.Truncate(rounded)
            ? ((int)rounded).ToString(CultureInfo.InvariantCulture)
            : rounded.ToString("0.0", CultureInfo.InvariantCulture);
    }

    internal static string JoinAnd(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            2 => $"{list[0]} and {list[1]}",
            _ => string.Join(", ", list.Take(list.Count - 1)) + ", and " + list[^1],
        };
    }

    private static Regex CoopNotePattern() => new(@"(?i)satisfied\s+(?:through|by|with)\s+(?:a\s+)?co-?op", RegexOptions.CultureInvariant);
    private static Regex NupathNotePattern() => new(@"(?i)complete\s+(?:the\s+)?NUpath\s+requirements", RegexOptions.CultureInvariant);
    private static Regex UniversitywideNotePattern() => new(@"(?i)complete\s+(?:the\s+)?university-?\s?wide\s+requirements", RegexOptions.CultureInvariant);
    private static Regex TotalHoursPattern() => new(@"(?i)(\d+(?:\.\d+)?)\s+total\s+semester\s+hours\s+required", RegexOptions.CultureInvariant);
    private static Regex MajorHoursPattern() => new(@"(?i)(\d+(?:\.\d+)?)\s+(?:semester\s+)?(?:credit\s+)?hours\s+(?:in|within)\s+the\s+major", RegexOptions.CultureInvariant);
    private static Regex GradeNotePattern() => new(@"(?i)\bGPA\b|grade\s+point|minimum\s+grade|grade\s+of\s+[A-D]", RegexOptions.CultureInvariant);
    private static Regex ResidencyNotePattern() => new(@"(?i)residen|(?:at|from)\s+Northeastern|Northeastern\s+University\s+semester\s+hours", RegexOptions.CultureInvariant);
    private static Regex ConcentrationNotePattern() => new(@"(?i)(?:pick|choose|select|complete)\s+(?:one|a)\s+(?:of\s+the\s+following\s+)?concentrations?", RegexOptions.CultureInvariant);
}

public static class DegreeAuditInfo
{
    public static bool IsSatisfied(this DegreeAudit.Status status) => status >= DegreeAudit.Status.Planned;
}

/// <summary>When another listing of a course is a different class, not a repeat.</summary>
public static class DegreeCourseRepeats
{
    public static bool CountsSeparately(string title, double credits, string otherTitle, double otherCredits)
    {
        if (credits <= 0 || otherCredits <= 0) return false;
        var first = NormalizedTitle(title);
        var second = NormalizedTitle(otherTitle);
        if (first.Length > 0 && second.Length > 0 && first != second) return true;
        return first.Contains("topic", StringComparison.Ordinal) || second.Contains("topic", StringComparison.Ordinal);
    }

    /// <summary>
    /// Another section counts on its own when both rows name a different CRN, or, when
    /// neither names a CRN, the titles differ or a title contains "topic". The same CRN
    /// twice is one course for the warning. Lines without credit never count separately.
    /// The audit still counts every copy that has credit.
    /// </summary>
    public static bool CountsSeparately(DegreePlanCourse a, DegreePlanCourse b)
    {
        if (a.Credits <= 0 || b.Credits <= 0) return false;
        var first = CleanCrn(a.SelectedCrn);
        var second = CleanCrn(b.SelectedCrn);
        if (first is not null || second is not null)
            return first is not null && second is not null && !string.Equals(first, second, StringComparison.Ordinal);
        return CountsSeparately(a.Title, a.Credits, b.Title, b.Credits);
    }

    /// <summary>True when both listings name a CRN and the CRNs differ.</summary>
    public static bool DistinctManualCrns(string? crn, string? other)
    {
        var first = CleanCrn(crn);
        var second = CleanCrn(other);
        return first is not null && second is not null && !string.Equals(first, second, StringComparison.Ordinal);
    }

    public static string? CleanCrn(string? crn) =>
        string.IsNullOrWhiteSpace(crn) ? null : crn.Trim();

    private static string NormalizedTitle(string title) =>
        new(title.ToLowerInvariant().Replace("&", " and ", StringComparison.Ordinal).Where(char.IsLetterOrDigit).ToArray());
}
