// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

public static class CourseCompletion
{
    /// <summary>
    /// What the primary degree plan lists as completed, with the equivalences the caller has
    /// already gathered (Banner's mutual exclusions and cross-lists, merged with the plan's declared ones).
    /// </summary>
    public static CompletedCourses CompletedCourses(this UserDataStore store, CourseEquivalences? equivalences = null) =>
        new()
        {
            Taken = store.CompletedCourseKeys,
            Equivalences = equivalences ?? store.PrimaryDegreePlan?.DeclaredEquivalences ?? CourseEquivalences.None,
        };

    /// <summary>
    /// Whether the user has completed the course, and which course counted if it was a different one.
    /// When a renumbered or cross-listed course counts, via is the one the student took.
    /// </summary>
    public static (bool IsComplete, CourseKey? Via) CompletionFor(this UserDataStore store, CourseKey key, CourseEquivalences? equivalences = null) =>
        store.CompletedCourses(equivalences).Completion(key);
}
