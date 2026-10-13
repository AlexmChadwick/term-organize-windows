// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// A substitution that is really a course under a new number, recorded as an equivalence.
// Term Organize is not affiliated with Northeastern University.

namespace TermOrganize.Core;

/// <summary>
/// A course the plan has that is the required course under a new number, recorded as the
/// equivalence it is instead of as a substitution, with what Undo needs to put the plan back.
/// </summary>
public sealed class DegreeRenumbering
{
    /// <summary>The course the plan has.</summary>
    public CourseKey Course { get; }

    /// <summary>The course the requirement names, which it now counts as.</summary>
    public CourseKey CountsAs { get; }

    /// <summary>
    /// The substitution that was turned into the equivalence, and where it was in the plan's list.
    /// Null when the equivalence was declared without one.
    /// </summary>
    public RequirementSubstitution? Substitution { get; }

    public int SubstitutionIndex { get; }

    /// <summary>The equivalence that was declared. Null when the plan already counted the two as one course.</summary>
    public Guid? EquivalenceId { get; }

    /// <summary><c>CS 3001 now counts as CS 3000</c>.</summary>
    public string Message => $"{Course} now counts as {CountsAs}";

    /// <summary>Whether Undo has anything to put back.</summary>
    public bool CanUndo => Substitution is not null || EquivalenceId is not null;

    public DegreeRenumbering(
        CourseKey course,
        CourseKey countsAs,
        RequirementSubstitution? substitution = null,
        int substitutionIndex = 0,
        Guid? equivalenceId = null)
    {
        Course = course;
        CountsAs = countsAs;
        Substitution = substitution;
        SubstitutionIndex = substitutionIndex;
        EquivalenceId = equivalenceId;
    }

    /// <summary>
    /// Turns a plan's "count a course" substitution into the equivalence it amounts to.
    /// Null when the substitution isn't in the plan or isn't a renumbering.
    /// </summary>
    public static DegreeRenumbering? Convert(
        RequirementSubstitution substitution,
        Guid planId,
        ProgramRequirements? requirements,
        UserDataStore store)
    {
        var plan = store.DegreePlan(planId);
        if (plan is null) return null;
        var index = plan.Substitutions.FindIndex(item => item.Id == substitution.Id);
        if (index < 0) return null;
        var stored = plan.Substitutions[index];
        var equivalence = DegreeSubstitutionEditing.Renumbering(stored, requirements);
        if (equivalence is null) return null;
        var known = plan.Equivalences.Select(item => item.Id).ToHashSet();
        store.UpdateDegreePlan(planId, updated => DegreeSubstitutionEditing.ConvertToEquivalence(stored, requirements, updated));
        var declared = store.DegreePlan(planId)?.Equivalences.FirstOrDefault(item => !known.Contains(item.Id))?.Id;
        return new DegreeRenumbering(equivalence.Course, equivalence.CountsAs, stored, index, declared);
    }

    /// <summary>
    /// Declares that <paramref name="course"/> is <paramref name="countsAs"/> under a new number.
    /// Null when the courses are the same or the plan is gone. When they already count as one
    /// course, the result has nothing to undo.
    /// </summary>
    public static DegreeRenumbering? Declare(CourseKey course, CourseKey countsAs, Guid planId, UserDataStore store)
    {
        if (course == countsAs || store.DegreePlan(planId) is null) return null;
        var id = DegreeEquivalenceEditing.Declare(store, planId, course, countsAs, "Renumbered");
        return new DegreeRenumbering(course, countsAs, equivalenceId: id);
    }

    /// <summary>
    /// Puts the substitution back where it was and removes the equivalence, leaving anything else
    /// that changed in the meantime alone.
    /// </summary>
    public void Undo(Guid planId, UserDataStore store)
    {
        store.UpdateDegreePlan(planId, plan =>
        {
            if (EquivalenceId is Guid id) plan.RemoveEquivalence(id);
            if (Substitution is RequirementSubstitution substitution && plan.Substitutions.All(item => item.Id != substitution.Id))
            {
                var at = Math.Clamp(SubstitutionIndex, 0, plan.Substitutions.Count);
                plan.Substitutions.Insert(at, substitution);
            }
        });
    }

    /// <summary>
    /// Substitutions most likely a course under a new number, by substitution id, without the
    /// pairs the student kept as substitutions (<paramref name="dismissed"/>, pair ids).
    /// <paramref name="isRetired"/> is null when that isn't known yet.
    /// </summary>
    public static Dictionary<Guid, DegreeSubstitutionEditing.LikelyRenumbering> Likely(
        DegreePlan plan,
        ProgramRequirements? requirements,
        ISet<string> dismissed,
        Func<CourseKey, bool?> isRetired) =>
        DegreeSubstitutionEditing.LikelyRenumberings(plan, requirements, isRetired)
            .Where(item => !dismissed.Contains(DegreeEquivalencePrompt.PairId(item.Course, item.CountsAs)))
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());

    /// <summary>Substitutions that amount to a renumbered course, by id.</summary>
    public static HashSet<Guid> Renumberable(DegreePlan plan, ProgramRequirements? requirements) =>
        plan.Substitutions
            .Where(item => DegreeSubstitutionEditing.Renumbering(item, requirements) is not null)
            .Select(item => item.Id)
            .ToHashSet();

    /// <summary>
    /// Courses to look up to know whether a renumbering-shaped substitution's requirement is
    /// retired: the course it names, then the course that stands in for it.
    /// </summary>
    public static List<CourseKey> CoursesToLookUp(DegreePlan plan, ProgramRequirements? requirements)
    {
        var seen = new HashSet<CourseKey>();
        var keys = new List<CourseKey>();
        foreach (var substitution in plan.Substitutions)
        {
            var equivalence = DegreeSubstitutionEditing.Renumbering(substitution, requirements);
            if (equivalence is null) continue;
            if (seen.Add(equivalence.CountsAs)) keys.Add(equivalence.CountsAs);
            if (seen.Add(equivalence.Course)) keys.Add(equivalence.Course);
        }
        return keys;
    }
}
