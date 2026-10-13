// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// The questions under a course's requirements. Nothing is declared until the student confirms.

namespace TermOrganize.Core;

public static class CourseEquivalencePrompts
{
    /// <summary>
    /// The questions to show for a requisite. Dismissed pairs are left out before the limit applies.
    /// Suggestions come from the caller (the course store on Apple; whatever catalog evidence is on hand here).
    /// </summary>
    public static List<EquivalencePrompt> Choose(
        Requisite requisite,
        CompletedCourses completed,
        string dismissedRaw,
        Func<CourseKey, IReadOnlyList<EquivalenceSuggestion>> suggestions,
        int limit = 2) =>
        EquivalencePrompt.Choose(requisite, completed, EquivalenceSuggestionDismissals.Decode(dismissedRaw), suggestions, limit)
            .Where(prompt => !EquivalenceSuggestionDismissals.IsDismissed(prompt, dismissedRaw))
            .ToList();

    /// <summary>The student said yes: declare the equivalence on the degree plan.</summary>
    public static void Confirm(UserDataStore store, Guid planId, EquivalencePrompt prompt) =>
        store.UpdateDegreePlan(planId, plan => plan.DeclareEquivalence(prompt.Taken, prompt.Required));

    /// <summary>The student said no: remember it, and never ask this pair again.</summary>
    public static string Dismiss(EquivalencePrompt prompt, string dismissedRaw) =>
        EquivalenceSuggestionDismissals.Dismiss(prompt, dismissedRaw);
}
