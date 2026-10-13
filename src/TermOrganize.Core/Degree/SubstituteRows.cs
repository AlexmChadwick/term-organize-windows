// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Where a course counted by substitution sits under the requirement it replaces.
// Term Organize is not affiliated with Northeastern University.

namespace TermOrganize.Core;

/// <summary>
/// How a requirement's counted courses that it doesn't list are laid out: the ones counted by
/// substitution sit directly under the requirement they replace, the others after its list.
/// </summary>
public static class DegreeSubstituteRows
{
    /// <param name="listed">The courses the requirement lists.</param>
    public static (List<DegreeAudit.CourseUse> Substitutes, List<DegreeAudit.CourseUse> Others) Split(
        DegreeAudit.RuleResult result,
        ISet<CourseKey> listed)
    {
        var unlisted = result.Used.Where(use => !DegreeRequirementsListing.IsShown(use, listed)).ToList();
        return (
            unlisted.Where(use => use.BySubstitution).ToList(),
            unlisted.Where(use => !use.BySubstitution).ToList());
    }

    /// <summary>The substitution that counts a course toward the requirement.</summary>
    public static RequirementSubstitution? Substitution(CourseKey key, DegreeAudit.RuleResult result) =>
        result.Substitutions.FirstOrDefault(item =>
            item.Change is SubstitutionChange.CountCourseChange count && count.Course == key);

    /// <summary>
    /// The requirement under the name that reads on its own
    /// (<c>CS 4500 or CS 4530</c>, <c>Science Requirement</c>, <c>Human-Centered Computing concentration</c>).
    /// </summary>
    public static string RequirementName(DegreeAudit.RuleResult result, ProgramRequirements requirements) =>
        requirements.DegreeShortName(result.Rule.Id) ?? result.Rule.DisplayLabel;

    /// <summary>The substitution's note and approval.</summary>
    public static string? Note(CourseKey key, DegreeAudit.RuleResult result)
    {
        var substitution = Substitution(key, result);
        return substitution is null ? null : DegreeRequirementText.SubstitutionCaption(substitution);
    }

    /// <summary><c>Counts for CS 4500 or CS 4530 by substitution · Approved by advising</c>.</summary>
    public static string Caption(CourseKey key, DegreeAudit.RuleResult result, ProgramRequirements requirements)
    {
        var parts = new List<string> { $"Counts for {RequirementName(result, requirements)} by substitution" };
        if (Note(key, result) is string note) parts.Add(note);
        return string.Join(" \u00b7 ", parts);
    }

    /// <summary>
    /// The caption of a course row. A course counted by substitution says where it counts; one that
    /// sits under its requirement (<paramref name="isChild"/>) says so in its heading, so only the
    /// note and approval remain. Any other row keeps the lookup's caption.
    /// </summary>
    public static string? Caption(
        DegreeCourseRowInfo info,
        CourseKey key,
        DegreeAudit.RuleResult result,
        ProgramRequirements requirements,
        bool isChild)
    {
        if (!info.BySubstitution) return info.Caption;
        return isChild ? Note(key, result) : Caption(key, result, requirements);
    }
}
