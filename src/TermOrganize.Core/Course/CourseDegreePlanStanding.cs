// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// What the primary degree plan does with a course: "Completed", or "Planned · Spring 2027",
/// and "via CS 2510" when a course that counts as it is the one in the plan.
/// </summary>
public sealed class CourseDegreePlanStanding : IEquatable<CourseDegreePlanStanding>
{
    public abstract record Kind
    {
        public sealed record Completed(CourseKey? Via) : Kind;
        public sealed record Planned(string Semester, CourseKey? Via) : Kind;
    }

    public required Guid PlanId { get; init; }
    public required Kind Standing { get; init; }

    public string Text => Standing switch
    {
        Kind.Completed completed => completed.Via is CourseKey via ? $"Completed via {via}" : "Completed",
        Kind.Planned planned => planned.Via is CourseKey via ? $"Planned via {via} \u00b7 {planned.Semester}" : $"Planned \u00b7 {planned.Semester}",
        _ => "",
    };

    public string Symbol => Standing switch
    {
        Kind.Completed => "checkmark.circle.fill",
        Kind.Planned => "calendar",
        _ => "",
    };

    public string SpokenText => Standing switch
    {
        Kind.Completed completed => completed.Via is CourseKey via
            ? $"Completed via {via}, in your degree plan"
            : "Completed, in your degree plan",
        Kind.Planned planned => planned.Via is CourseKey via
            ? $"Planned via {via} for {planned.Semester} in your degree plan"
            : $"Planned for {planned.Semester} in your degree plan",
        _ => "",
    };

    public bool Equals(CourseDegreePlanStanding? other) =>
        other is not null && PlanId == other.PlanId && Standing == other.Standing;

    public override bool Equals(object? obj) => Equals(obj as CourseDegreePlanStanding);
    public override int GetHashCode() => HashCode.Combine(PlanId, Standing);
}

public static class CourseDegreePlanStandingLookup
{
    /// <summary>
    /// The standing of a course in the primary degree plan: completed, else planned in one of its
    /// semesters. A course counts when an equivalent is the one in the plan.
    /// </summary>
    public static CourseDegreePlanStanding? For(UserDataStore store, CourseKey key, CourseEquivalences? equivalences = null)
    {
        if (store.PrimaryDegreePlan?.Id is not Guid planId) return null;
        var completed = store.CompletedCourses(equivalences);
        var done = completed.Completion(key);
        if (done.IsComplete) return new CourseDegreePlanStanding { PlanId = planId, Standing = new CourseDegreePlanStanding.Kind.Completed(done.Via) };
        if (PlannedTerm(store, key, completed.Equivalences) is { } planned)
            return new CourseDegreePlanStanding { PlanId = planId, Standing = new CourseDegreePlanStanding.Kind.Planned(planned.Term.ShortName, planned.Via) };
        return null;
    }

    /// <summary>
    /// Where the primary degree plan schedules a course, or a course that counts as it.
    /// Via is that other course when it isn't the key itself.
    /// </summary>
    public static (DegreePlanTerm Term, CourseKey? Via)? PlannedTerm(UserDataStore store, CourseKey key, CourseEquivalences equivalences)
    {
        if (store.PlannedTerm(key) is DegreePlanTerm direct) return (direct, null);
        foreach (var other in equivalences.EquivalentsOf(key).OrderBy(item => item))
        {
            if (store.PlannedTerm(other) is DegreePlanTerm term) return (term, other);
        }
        return null;
    }
}
