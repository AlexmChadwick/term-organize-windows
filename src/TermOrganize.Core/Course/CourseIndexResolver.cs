// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Resolving the catalog index a course, section or professor screen reads for its term.

namespace TermOrganize.Core;

/// <summary>
/// Loads a term's catalog index when it is not cached yet, and remembers that the first attempt has finished.
/// The caller owns the cache; this type only decides when a load is still outstanding.
/// </summary>
public sealed class CourseIndexResolver
{
    public bool HasResolved { get; private set; }

    public async Task ResolveAsync(string termCode, Func<string, CancellationToken, Task> load, CancellationToken cancellationToken = default)
    {
        await load(termCode, cancellationToken).ConfigureAwait(false);
        HasResolved = true;
    }

    public static CatalogIndex? Index(string termCode, Func<string, CatalogIndex?> cached) => cached(termCode);

    public static Term? TermFor(string termCode, Func<string, CatalogIndex?> cached, Term? selected, IEnumerable<Term> terms)
    {
        if (cached(termCode) is CatalogIndex index) return index.Term;
        if (selected is not null && selected.Code == termCode) return selected;
        return terms.FirstOrDefault(term => term.Code == termCode);
    }

    /// <summary>True during the first resolution, or while the selected term itself is still loading.</summary>
    public bool IsLoading(string termCode, string? selectedTermCode, bool selectedIsLoading)
    {
        if (!HasResolved) return true;
        return selectedTermCode == termCode && selectedIsLoading;
    }

    /// <summary>Loads the term again when it has disappeared from the cache.</summary>
    public async Task ReloadIfEvictedAsync(string termCode, Func<string, CatalogIndex?> cached, Func<string, CancellationToken, Task> load, CancellationToken cancellationToken = default)
    {
        if (cached(termCode) is not null) return;
        await ResolveAsync(termCode, load, cancellationToken).ConfigureAwait(false);
    }
}

public enum CourseDetailsStatus
{
    Ready,
    Loading,
    Unavailable,
}

public static class CourseScheduleMembership
{
    /// <summary>
    /// Whether a plan for the term already holds the course (crn null) or the given section.
    /// </summary>
    public static bool CourseIsInSchedule(this UserDataStore store, string termCode, CourseKey key, string? crn = null)
    {
        var plans = store.PlansIn(termCode);
        if (crn is not null) return plans.Any(plan => plan.SelectedCRNs.Contains(crn));
        return plans.Any(plan => plan.Contains(key));
    }
}
