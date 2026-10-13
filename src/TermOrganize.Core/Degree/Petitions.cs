// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Course-number limits on petitions. A planning aid read from Northeastern's public catalog.
// Term Organize is not affiliated with, endorsed by, or sponsored by Northeastern University.

namespace TermOrganize.Core;

/// <summary>Wording for a list of catalog ranges: subjects, and the minimums they start at.</summary>
public static class CourseRangeText
{
    /// <summary><c>CS, CY, DS or IS</c>.</summary>
    public static string SubjectsText(this IReadOnlyList<CourseRange> ranges)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var subjects = new List<string>();
        foreach (var range in ranges)
        {
            if (seen.Add(range.Subject)) subjects.Add(range.Subject);
        }
        return DegreeLists.Or(subjects);
    }

    /// <summary>
    /// The lowest numbers they start at, e.g. <c>CS 2500, CY 2000, DS 2500 or IS 2000</c>.
    /// Null when none has a minimum.
    /// </summary>
    public static string? MinimumsText(this IReadOnlyList<CourseRange> ranges)
    {
        var minimums = ranges.Where(range => range.Lowest > 0).Select(range => $"{range.Subject} {range.Lowest}").ToList();
        return minimums.Count == 0 ? null : DegreeLists.Or(minimums);
    }
}

public static class RequirementReplacementCourses
{
    /// <summary>
    /// The ranges of the requirement it takes courses like, as the catalog lists them.
    /// Empty when that requirement has none.
    /// </summary>
    public static List<CourseRange> LikeRanges(this RequirementReplacement replacement, ProgramRequirements requirements)
    {
        var like = requirements.DegreeRule(replacement.LikeRuleID, replacement.LikeRuleTitle);
        return like?.Kind is RequirementKind.Choose choose ? choose.Ranges.ToList() : [];
    }

    /// <summary>Whether the petition has to say which course numbers count.</summary>
    public static bool AsksForCourseNumbers(this RequirementReplacement replacement, ProgramRequirements requirements) =>
        replacement.LikeRanges(requirements).Any(range => range.Lowest > 0);
}

public static class DegreePlanCourseNumbers
{
    /// <summary>Sets which course numbers a petition accepts (null: not answered).</summary>
    public static void SetCourseNumbers(this DegreePlan plan, CourseNumberLimit? limit, Guid substitutionId)
    {
        var index = plan.Substitutions.FindIndex(item => item.Id == substitutionId);
        if (index < 0 || plan.Substitutions[index].Change is not SubstitutionChange.ReplaceChange replace) return;
        var updated = DegreePlanCopies.Clone(replace.Replacement);
        updated.CourseNumbers = limit;
        plan.Substitutions[index].Change = SubstitutionChange.Replace(updated);
    }
}

/// <summary>The answers a petition form offers for which course numbers count.</summary>
public enum CourseNumberChoice
{
    SameAsRequirement,
    AnyNumber,
    AtLeast,
}

public static class CourseNumberChoiceInfo
{
    public static string Raw(this CourseNumberChoice choice) => choice switch
    {
        CourseNumberChoice.SameAsRequirement => "sameAsRequirement",
        CourseNumberChoice.AnyNumber => "anyNumber",
        _ => "atLeast",
    };

    /// <summary>The answer a saved limit corresponds to; null when the petition hasn't answered.</summary>
    public static CourseNumberChoice? From(CourseNumberLimit? limit)
    {
        if (limit is null) return null;
        if (limit.Equals(CourseNumberLimit.SameAsRequirement)) return CourseNumberChoice.SameAsRequirement;
        if (limit.Equals(CourseNumberLimit.AnyNumber)) return CourseNumberChoice.AnyNumber;
        if (limit is CourseNumberLimit.AtLeastLimit) return CourseNumberChoice.AtLeast;
        return null;
    }

    /// <summary>The limit to save; null when "a different minimum" isn't a course number.</summary>
    public static CourseNumberLimit? ToLimit(this CourseNumberChoice choice, int minimum) => choice switch
    {
        CourseNumberChoice.SameAsRequirement => CourseNumberLimit.SameAsRequirement,
        CourseNumberChoice.AnyNumber => CourseNumberLimit.AnyNumber,
        CourseNumberChoice.AtLeast => IsValidMinimum(minimum) ? CourseNumberLimit.AtLeast(minimum) : null,
        _ => null,
    };

    public static bool IsValidMinimum(int minimum) => minimum is >= 1 and <= 9999;

    /// <summary>
    /// A starting point for "a different minimum": the requirement's lowest minimum rounded
    /// down to the thousand, else the saved one.
    /// </summary>
    public static int SuggestedMinimum(IReadOnlyList<CourseRange> ranges, CourseNumberLimit? saved)
    {
        if (saved is CourseNumberLimit.AtLeastLimit atLeast) return atLeast.Minimum;
        var positive = ranges.Select(range => range.Lowest).Where(lowest => lowest > 0).ToList();
        var lowest = positive.Count == 0 ? 2000 : positive.Min();
        return Math.Max(1000, lowest / 1000 * 1000);
    }

    /// <summary><c>Same as Khoury Elective Courses</c>, <c>Any course number</c>, <c>A different minimum</c>.</summary>
    public static string Title(this CourseNumberChoice choice, string likeTitle) => choice switch
    {
        CourseNumberChoice.SameAsRequirement => $"Same as {likeTitle}",
        CourseNumberChoice.AnyNumber => "Any course number",
        _ => "A different minimum",
    };

    /// <summary>
    /// <c>CS 2500 or higher · CY 2000 or higher</c>, <c>Any CS, CY, DS, or IS course</c>,
    /// <c>CS 2000 or higher · CY 2000 or higher</c>.
    /// </summary>
    public static string Detail(this CourseNumberChoice choice, IReadOnlyList<CourseRange> ranges, int minimum) => choice switch
    {
        CourseNumberChoice.SameAsRequirement => string.Join(" \u00b7 ", ranges.Select(range => range.Label)),
        CourseNumberChoice.AnyNumber => $"Any {ranges.SubjectsText()} course",
        _ => IsValidMinimum(minimum)
            ? string.Join(" \u00b7 ", ranges.Select(range => range.Applying(CourseNumberLimit.AtLeast(minimum)).Label))
            : "Enter a course number, like 2000",
    };
}

/// <summary>
/// A petition whose answer changes the audit: it takes courses like a requirement that starts at
/// a minimum course number, the student hasn't said whether that minimum applies, and without it
/// more requirements are met.
/// </summary>
public sealed class DegreePetitionQuestion
{
    public required RequirementSubstitution Substitution { get; init; }
    /// <summary>The requirement the petition replaces (where the audit applied it).</summary>
    public required string RuleId { get; init; }
    /// <summary>Its name, e.g. <c>Electrical Engineering</c>.</summary>
    public required string RuleTitle { get; init; }
    /// <summary>The requirement it takes courses like, e.g. <c>Khoury Elective Courses</c>.</summary>
    public required string LikeTitle { get; init; }
    /// <summary>That requirement's ranges as the catalog lists them.</summary>
    public required List<CourseRange> Ranges { get; init; }
    /// <summary>Courses of the plan that count once there's no minimum.</summary>
    public required List<CourseKey> Courses { get; init; }
    public int Satisfied { get; init; }
    public int SatisfiedWithoutMinimum { get; init; }
    public int Total { get; init; }

    public Guid Id => Substitution.Id;

    /// <summary><c>Does your Electrical Engineering petition accept any CS, CY, DS or IS course?</c></summary>
    public string Question => $"Does your {RuleTitle} petition accept any {Ranges.SubjectsText()} course?";

    /// <summary>Whether the answer changes how many requirements are met.</summary>
    public bool Matters => SatisfiedWithoutMinimum > Satisfied;

    /// <summary>
    /// <c>Khoury Elective Courses start at CS 2500, CY 2000, DS 2500 or IS 2000. With any number,
    /// CS 2386 counts and every requirement is met.</c>
    /// </summary>
    public string Explanation
    {
        get
        {
            var parts = new List<string>();
            if (Ranges.MinimumsText() is string minimums) parts.Add($"{LikeTitle} start at {minimums}.");
            if (!Matters)
            {
                parts.Add("Your petition decides whether that minimum applies here.");
                return string.Join(" ", parts);
            }
            var courseList = DegreeLists.And(Courses.Select(course => course.ToString()).ToList());
            var gained = SatisfiedWithoutMinimum - Satisfied;
            var effect = SatisfiedWithoutMinimum >= Total
                ? "every requirement is met"
                : $"{gained} more {(gained == 1 ? "requirement is" : "requirements are")} met";
            parts.Add(Courses.Count == 0
                ? $"With any number, {effect}."
                : $"With any number, {courseList} {(Courses.Count == 1 ? "counts" : "count")} and {effect}.");
            return string.Join(" ", parts);
        }
    }
}

public static class DegreePetitionQuestions
{
    /// <summary>
    /// The petitions to ask about, in the plan's order: unanswered course-number limits whose
    /// answer changes how many requirements are met.
    /// </summary>
    public static List<DegreePetitionQuestion> Questions(
        DegreePlan plan,
        ProgramRequirements requirements,
        CourseEquivalences? equivalences = null,
        DegreeAudit? audit = null)
    {
        equivalences ??= CourseEquivalences.None;
        var candidates = plan.Substitutions.Where(substitution => IsUnanswered(substitution, requirements)).ToList();
        if (candidates.Count == 0) return [];
        var current = audit ?? DegreeAudit.Run(plan, requirements, equivalences);
        return candidates
            .Select(substitution => Question(substitution, plan, requirements, equivalences, current))
            .OfType<DegreePetitionQuestion>()
            .Where(question => question.Matters)
            .ToList();
    }

    /// <summary>
    /// The question about one petition's course numbers, whether or not the answer changes what's
    /// met. Null when the petition has answered, doesn't need to, or doesn't apply.
    /// </summary>
    public static DegreePetitionQuestion? Question(
        RequirementSubstitution substitution,
        DegreePlan plan,
        ProgramRequirements requirements,
        CourseEquivalences? equivalences,
        DegreeAudit audit)
    {
        equivalences ??= CourseEquivalences.None;
        var ruleId = audit.Placements.TryGetValue(substitution.Id, out var placed) ? placed : substitution.RuleID;
        if (!IsUnanswered(substitution, requirements) || substitution.Change is not SubstitutionChange.ReplaceChange replace || audit.Result(ruleId) is null)
            return null;
        var relaxed = DegreePlanCopies.Shell(plan);
        relaxed.SetCourseNumbers(CourseNumberLimit.AnyNumber, substitution.Id);
        var without = DegreeAudit.Run(relaxed, requirements, equivalences, audit.Attributes);
        var ranges = replace.Replacement.LikeRanges(requirements);
        var newlyCounted = (without.Result(ruleId)?.Used ?? [])
            .Select(use => use.Key)
            .Where(key => !ranges.Any(range => range.Contains(key)) && !replace.Replacement.Courses.Contains(key))
            .ToList();
        return new DegreePetitionQuestion
        {
            Substitution = substitution,
            RuleId = ruleId,
            RuleTitle = requirements.Title(ruleId) ?? substitution.RuleTitle,
            LikeTitle = replace.Replacement.LikeRuleTitle
                ?? (replace.Replacement.LikeRuleID is string likeId ? requirements.Title(likeId) : null)
                ?? "That requirement",
            Ranges = ranges,
            Courses = newlyCounted,
            Satisfied = audit.SatisfiedCount,
            SatisfiedWithoutMinimum = without.SatisfiedCount,
            Total = audit.TotalCount,
        };
    }

    /// <summary>
    /// A petition that takes courses like a requirement with a minimum course number and
    /// hasn't said whether it applies.
    /// </summary>
    public static bool IsUnanswered(RequirementSubstitution substitution, ProgramRequirements requirements)
    {
        if (substitution.Change is not SubstitutionChange.ReplaceChange replace) return false;
        return replace.Replacement.CourseNumbers is null && replace.Replacement.AsksForCourseNumbers(requirements);
    }
}

/// <summary>
/// Remembers a plan's petition questions until the plan, its requirements, or the equivalences change.
/// </summary>
public sealed class DegreePetitionQuestionCache
{
    private string? _key;
    private List<DegreePetitionQuestion> _cached = [];

    public List<DegreePetitionQuestion> Questions(
        DegreePlan plan,
        ProgramRequirements requirements,
        CourseEquivalences equivalences,
        DegreeAudit audit)
    {
        var key = string.Join(
            "\n",
            DegreePlanCopies.Fingerprint(plan),
            requirements.Year.StartYear.ToString(),
            requirements.Program.Path,
            requirements.FetchedAt.UtcTicks.ToString(),
            string.Join(";", equivalences.Groups.Select(group => string.Join(",", group))),
            string.Join(";", audit.Attributes.Nupaths.OrderBy(pair => pair.Key).Select(pair =>
                pair.Key + ":" + string.Join(",", pair.Value.OrderBy(path => path)))),
            string.Join(",", audit.Attributes.Loading.OrderBy(item => item)));
        if (key != _key)
        {
            _cached = DegreePetitionQuestions.Questions(plan, requirements, equivalences, audit);
            _key = key;
        }
        return _cached;
    }
}

public sealed partial class DegreeAudit
{
    /// <summary>
    /// A course of the plan that counts toward nothing specific but nearly fits a requirement
    /// that's short: it's in one of the requirement's subjects and only its number keeps it out.
    /// </summary>
    public sealed class NearMiss : IEquatable<NearMiss>
    {
        public required CourseUse Course { get; init; }
        public required CourseRange Range { get; init; }
        public required string Reason { get; init; }
        public string Id => Course.Id;

        public bool Equals(NearMiss? other) =>
            other is not null && Course.Id == other.Course.Id && Range.Equals(other.Range) && Reason == other.Reason;

        public override bool Equals(object? obj) => Equals(obj as NearMiss);
        public override int GetHashCode() => HashCode.Combine(Course.Id, Range, Reason);
    }

    /// <summary>
    /// For a requirement that's short and has ranges: the plan's general electives and unused
    /// courses that are in one of its subjects but outside its ranges, completed ones first.
    /// </summary>
    public List<NearMiss> NearMisses(string ruleId)
    {
        var result = Result(ruleId);
        if (result is null || result.Status.IsSatisfied() || result.EffectiveKind is not RequirementKind.Choose choose || choose.Ranges.Count == 0)
            return [];
        var misses = new List<NearMiss>();
        foreach (var use in GeneralElectives.Concat(Unused))
        {
            if (use.Credits <= 0 || (choose.Count is not null && use.Credits < 2)) continue;
            if (choose.Ranges.Any(range => range.Contains(use.Key))) continue;
            foreach (var range in choose.Ranges)
            {
                if (range.ReasonExcluding(use.Key) is not string reason) continue;
                misses.Add(new NearMiss { Course = use, Range = range, Reason = reason });
                break;
            }
        }
        return misses
            .OrderBy(miss => miss.Course.IsCompleted ? 0 : 1)
            .ThenBy(miss => miss.Course.Order)
            .ToList();
    }
}
