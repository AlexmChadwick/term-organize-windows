// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// The courses the user has completed and what counts as what, read once.
/// A requirements card asks about dozens of courses, so a screen gathers this once per update.
/// </summary>
public sealed class CompletedCourses
{
    public static CompletedCourses None { get; } = new();

    /// <summary>What the primary degree plan lists as completed (the audit counts courses in progress too).</summary>
    public HashSet<CourseKey> Taken { get; init; } = [];

    /// <summary>Which courses count as which: Banner's, cross-listed courses and the plan's declared ones.</summary>
    public CourseEquivalences Equivalences { get; init; } = CourseEquivalences.None;

    /// <summary>
    /// Whether the key is done, and which completed course counted when it wasn't the key itself
    /// ("Completed via CS 2510"). The via course is the one <see cref="CourseEquivalences.Satisfier"/> picks.
    /// </summary>
    public (bool IsComplete, CourseKey? Via) Completion(CourseKey key)
    {
        var counted = Equivalences.Satisfier(key, Taken);
        if (counted is not CourseKey course) return (false, null);
        return (true, course == key ? null : course);
    }

    /// <summary>Every course that counts as done: the ones taken and what they count as.</summary>
    public HashSet<CourseKey> Covered => Equivalences.Expanding(Taken);

    /// <summary>Whether a requisite is met. Placement tests count as met, as in the degree plan's own checks.</summary>
    public bool Meets(Requisite requisite) => requisite.IsSatisfied(Covered, assumeTestsMet: true);
}
