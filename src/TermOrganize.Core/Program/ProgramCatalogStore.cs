// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Suggestions for picking a catalog program from a degree-audit code.
// Term Organize is not affiliated with Northeastern University.

namespace TermOrganize.Core;

/// <summary>
/// Offline helpers that the Mac catalog store uses when suggesting a program and catalog year.
/// Fetching pages lives in the app; these suggestions are pure.
/// </summary>
public static class ProgramCatalogStore
{
    /// <summary>
    /// Programs matching a degree-audit code such as <c>CS-B-CSCI-BSCS</c> (by its degree, <c>BSCS</c>).
    /// Campus-less listings, then Boston, then other campuses. Empty when nothing matches.
    /// </summary>
    public static List<CatalogProgram> SuggestedPrograms(string? auditCode, IReadOnlyList<CatalogProgram> programs)
    {
        var degree = auditCode?.Split('-').LastOrDefault();
        if (degree is null || degree.Length < 2) return [];
        return programs
            .Where(program => program.Degree is not null && program.Degree.Equals(degree, StringComparison.OrdinalIgnoreCase))
            .OrderBy(program => CampusRank(program.Campus))
            .ThenBy(program => program.Title, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// A guess at the catalog year a student entered under: about 32 credits a year before the current edition.
    /// Only a starting point; the student confirms it.
    /// </summary>
    public static CatalogYear SuggestedYear(double completedCredits, CatalogEditions editions)
    {
        var yearsDone = Math.Max(0, (int)(completedCredits / 32));
        var guess = new CatalogYear(editions.Current.StartYear - yearsDone);
        return editions.All.Contains(guess) ? guess : editions.All.LastOrDefault(editions.Current);
    }

    private static int CampusRank(string? campus) => campus switch
    {
        null => 0,
        "Boston" => 1,
        _ => 2,
    };
}
