// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// What a substitution form can save. A planning aid; the official audit is the registrar's.
// Term Organize is not affiliated with Northeastern University.

namespace TermOrganize.Core;

/// <summary>A substitution to add or edit, for one requirement.</summary>
public sealed class DegreeSubstitutionRequest
{
    public enum Kind
    {
        CountCourse,
        Replace,
        Waive,
    }

    public Guid PlanId { get; }
    public string RuleId { get; }
    /// <summary>The requirement's name, saved with a new substitution.</summary>
    public string RuleTitle { get; }
    public RequirementSubstitution? Existing { get; }
    public Kind ChangeKind { get; }
    /// <summary>The course a new "count a course" substitution starts with.</summary>
    public CourseKey? Course { get; }

    public DegreeSubstitutionRequest(
        Guid planId,
        string ruleId,
        string ruleTitle,
        RequirementSubstitution? existing = null,
        Kind? kind = null,
        CourseKey? course = null)
    {
        PlanId = planId;
        RuleId = ruleId;
        RuleTitle = ruleTitle;
        Existing = existing;
        ChangeKind = kind ?? (existing is null ? Kind.CountCourse : KindOf(existing.Change));
        Course = course;
    }

    public string Id => Existing is RequirementSubstitution existing
        ? $"edit-{existing.Id}"
        : $"new-{RuleId}-{Raw(ChangeKind)}{(Course is CourseKey key ? "-" + key : "")}";

    public static Kind KindOf(SubstitutionChange change) => change switch
    {
        SubstitutionChange.CountCourseChange => Kind.CountCourse,
        SubstitutionChange.ReplaceChange => Kind.Replace,
        _ => Kind.Waive,
    };

    public static string Raw(Kind kind) => kind switch
    {
        Kind.CountCourse => "countCourse",
        Kind.Replace => "replace",
        _ => "waive",
    };

    /// <summary>What a petition starts with: as many courses (or semester hours) as the requirement asks for.</summary>
    public static (int Count, double Hours, bool InHours) InitialAmount(RequirementRule? rule)
    {
        switch (rule?.Kind)
        {
            case RequirementKind.Choose choose:
                if (choose.Count is int count) return (Math.Max(1, count), choose.Hours ?? count * 4d, false);
                if (choose.Hours is double hours) return (Math.Max(1, (int)(hours / 4)), hours, true);
                break;
            case RequirementKind.Required required:
                var courses = Math.Max(1, required.Option.Alternatives.FirstOrDefault()?.Count ?? 1);
                return (courses, rule?.Hours ?? courses * 4d, false);
            case RequirementKind.GeneralElectives general:
                return (Math.Max(1, (int)(general.Hours / 4)), general.Hours, true);
        }
        return (1, 4, false);
    }
}

/// <summary>
/// What the substitution form decides, apart from how it looks: what can be saved, what the
/// change is, and how a course of the plan is described next to the requirement.
/// </summary>
public static class DegreeSubstitutionEditing
{
    /// <summary>
    /// Whether the form has what the chosen kind of change needs. A petition taking courses like
    /// a requirement with a minimum course number must say which numbers count.
    /// </summary>
    public static bool CanSave(
        DegreeSubstitutionRequest.Kind kind,
        CourseKey? course,
        string? likeRuleId,
        IReadOnlyCollection<CourseKey> specificCourses,
        bool needsCourseNumbers = false,
        CourseNumberLimit? courseNumbers = null) => kind switch
    {
        DegreeSubstitutionRequest.Kind.CountCourse => course is not null,
        DegreeSubstitutionRequest.Kind.Replace => (likeRuleId is not null || specificCourses.Count > 0)
            && (!needsCourseNumbers || likeRuleId is null || courseNumbers is not null),
        _ => true,
    };

    /// <summary>Whether a petition taking courses like <paramref name="likeRuleId"/> has to say which course numbers count.</summary>
    public static bool NeedsCourseNumbers(string? likeRuleId, ProgramRequirements? requirements)
    {
        if (likeRuleId is null || requirements is null) return false;
        return new RequirementReplacement { LikeRuleID = likeRuleId }.AsksForCourseNumbers(requirements);
    }

    /// <summary>The ranges of the requirement a petition takes courses like, as the catalog lists them.</summary>
    public static List<CourseRange> LikeRanges(string? likeRuleId, ProgramRequirements? requirements)
    {
        if (likeRuleId is null || requirements is null) return [];
        return new RequirementReplacement { LikeRuleID = likeRuleId }.LikeRanges(requirements);
    }

    /// <summary>A requirement's name as a source of "courses like…".</summary>
    public static string SourceName(RequirementRule rule, ProgramRequirements? requirements) =>
        rule.DegreeIsGeneralElectives()
            ? "General electives (any course)"
            : requirements?.DegreeShortName(rule.Id) ?? rule.DisplayLabel;

    /// <summary>The change the form describes; null while it is incomplete.</summary>
    public static SubstitutionChange? Change(
        DegreeSubstitutionRequest.Kind kind,
        CourseKey? course,
        bool amountInHours,
        int count,
        double hours,
        string? likeRuleId,
        IReadOnlyCollection<CourseKey> specificCourses,
        CourseNumberLimit? courseNumbers,
        ProgramRequirements? requirements,
        string ruleTitle,
        Guid? rowId = null)
    {
        switch (kind)
        {
            case DegreeSubstitutionRequest.Kind.CountCourse:
                return course is CourseKey key ? SubstitutionChange.CountCourse(key, rowId) : null;
            case DegreeSubstitutionRequest.Kind.Replace:
                string? likeTitle = null;
                if (likeRuleId is not null)
                {
                    likeTitle = requirements?.Rule(likeRuleId) is RequirementRule like
                        ? SourceName(like, requirements)
                        : ruleTitle;
                }
                return SubstitutionChange.Replace(new RequirementReplacement
                {
                    Count = amountInHours ? null : count,
                    Hours = amountInHours ? hours : null,
                    LikeRuleID = likeRuleId,
                    LikeRuleTitle = likeTitle,
                    Courses = specificCourses.OrderBy(item => item).ToList(),
                    CourseNumbers = NeedsCourseNumbers(likeRuleId, requirements) ? courseNumbers : null,
                });
            default:
                return SubstitutionChange.Waive;
        }
    }

    /// <summary>The substitution to store: the one being edited, or a new one.</summary>
    public static RequirementSubstitution Substitution(
        DegreeSubstitutionRequest request,
        SubstitutionChange change,
        string note,
        DateTimeOffset? approvedOn)
    {
        var substitution = request.Existing is RequirementSubstitution existing
            ? DegreePlanCopies.Clone(existing)
            : new RequirementSubstitution { RuleID = request.RuleId, RuleTitle = request.RuleTitle, Change = change };
        substitution.RuleID = request.RuleId;
        substitution.Change = change;
        substitution.Note = note.Trim();
        substitution.ApprovedOn = approvedOn;
        return substitution;
    }

    /// <summary>
    /// The equivalence a "count a course" substitution amounts to when its course is the
    /// required course under a new number. Null for anything else.
    /// </summary>
    public static CourseEquivalence? Renumbering(RequirementSubstitution substitution, ProgramRequirements? requirements)
    {
        if (substitution.Change is not SubstitutionChange.CountCourseChange count) return null;
        var rule = requirements?.DegreeRule(substitution.RuleID, substitution.RuleTitle);
        if (rule?.Kind is not RequirementKind.Required required || required.Option.Alternatives.Count != 1) return null;
        var named = required.Option.Alternatives[0];
        if (named.Count != 1 || named[0] == count.Course) return null;
        return new CourseEquivalence { Course = count.Course, CountsAs = named[0], Note = "Renumbered" };
    }

    /// <summary>Turns a substitution into the equivalence it amounts to.</summary>
    public static void ConvertToEquivalence(RequirementSubstitution substitution, ProgramRequirements? requirements, DegreePlan plan)
    {
        var equivalence = Renumbering(substitution, requirements);
        if (equivalence is null) return;
        plan.Substitutions.RemoveAll(item => item.Id == substitution.Id);
        plan.DeclareEquivalence(equivalence.Course, equivalence.CountsAs, equivalence.Note);
    }

    /// <summary>A substitution that is most likely a renumbering, to offer turning it into one.</summary>
    public sealed class LikelyRenumbering : IEquatable<LikelyRenumbering>
    {
        public required RequirementSubstitution Substitution { get; init; }
        /// <summary>The course the plan has, and the retired course the requirement names.</summary>
        public required CourseKey Course { get; init; }
        public required CourseKey CountsAs { get; init; }

        public Guid Id => Substitution.Id;
        public string Question => $"Is {Course} the new number of {CountsAs}?";
        public string Explanation =>
            $"{CountsAs} isn\u2019t offered anymore. If {Course} is the same course under a new number, it counts as {CountsAs} everywhere, prerequisites included, like other renumbered courses.";

        public bool Equals(LikelyRenumbering? other) =>
            other is not null && Substitution.Id == other.Substitution.Id && Course == other.Course && CountsAs == other.CountsAs;

        public override bool Equals(object? obj) => Equals(obj as LikelyRenumbering);
        public override int GetHashCode() => HashCode.Combine(Substitution.Id, Course, CountsAs);
    }

    /// <summary>
    /// The plan's substitutions that count a course for a requirement naming one course that
    /// has been retired since. <paramref name="isRetired"/> is null when that isn't known yet.
    /// </summary>
    public static List<LikelyRenumbering> LikelyRenumberings(
        DegreePlan plan,
        ProgramRequirements? requirements,
        Func<CourseKey, bool?> isRetired) =>
        plan.Substitutions.Select(substitution =>
        {
            var equivalence = Renumbering(substitution, requirements);
            if (equivalence is null || isRetired(equivalence.CountsAs) != true || isRetired(equivalence.Course) == true
                || plan.DeclaredEquivalences.AreEquivalent(equivalence.Course, equivalence.CountsAs))
                return null;
            return new LikelyRenumbering
            {
                Substitution = substitution,
                Course = equivalence.Course,
                CountsAs = equivalence.CountsAs,
            };
        }).OfType<LikelyRenumbering>().ToList();

    /// <summary>Stores a substitution in a plan: replaces the one with the same id, else adds it.</summary>
    public static void Store(RequirementSubstitution substitution, DegreePlan plan)
    {
        var index = plan.Substitutions.FindIndex(item => item.Id == substitution.Id);
        if (index >= 0) plan.Substitutions[index] = substitution;
        else plan.Substitutions.Add(substitution);
    }

    /// <summary>
    /// Whether choosing a course for the requirement takes nothing from another one: the audit
    /// counts it nowhere, here, or only as a general elective.
    /// </summary>
    public static bool IsFree(string? countingRuleId, string ruleId, ProgramRequirements? requirements)
    {
        if (countingRuleId is null) return true;
        return countingRuleId == ruleId || requirements?.Rule(countingRuleId)?.DegreeIsGeneralElectives() == true;
    }

    /// <summary>Where the audit counts a course of the plan, beside the requirement being changed.</summary>
    public static string? Caption(
        DegreePlanCourseChoice choice,
        string? countingRuleId,
        string ruleId,
        ProgramRequirements? requirements)
    {
        if (countingRuleId is null) return null;
        if (countingRuleId == ruleId) return "Counts toward this requirement";
        if (requirements?.Rule(countingRuleId)?.DegreeIsGeneralElectives() == true) return "Now a general elective";
        return $"Counts toward {requirements?.DegreeCountingName(countingRuleId, choice.Key) ?? "another requirement"}";
    }

    /// <summary>The plan's courses that match what was typed in a picker's search field, by code or title.</summary>
    public static List<DegreePlanCourseChoice> Matching(IReadOnlyList<DegreePlanCourseChoice> choices, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return choices.ToList();
        var compact = query.Replace(" ", "", StringComparison.Ordinal);
        return choices.Where(choice =>
            choice.Key.ToString().Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || (choice.Key.Subject + choice.Key.Number).Contains(compact, StringComparison.CurrentCultureIgnoreCase)
            || choice.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }
}
