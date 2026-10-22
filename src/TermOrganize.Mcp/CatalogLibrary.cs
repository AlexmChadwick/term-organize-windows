// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Mcp;

/// <summary>Bundled catalogs only. This process does not download or log in.</summary>
public sealed class CatalogLibrary
{
    public IReadOnlyList<TermCatalog> Catalogs { get; }
    public IReadOnlyDictionary<string, CatalogIndex> ByCode { get; }

    public CatalogLibrary()
    {
        var catalogs = new List<TermCatalog>();
        var indexes = new Dictionary<string, CatalogIndex>(StringComparer.Ordinal);
        foreach (var code in BundledCatalog.TermCodes())
        {
            var catalog = BundledCatalog.TryLoad(code);
            if (catalog is null || indexes.ContainsKey(catalog.Term.Code)) continue;
            catalogs.Add(catalog);
            indexes[catalog.Term.Code] = CatalogIndex.Build(catalog);
        }
        Catalogs = catalogs;
        ByCode = indexes;
    }

    public CatalogIndex? IndexForCode(string? code) =>
        code is not null && ByCode.TryGetValue(code, out var index) ? index : null;

    public CatalogIndex? IndexFor(PlanSeason season, int year)
    {
        foreach (var index in ByCode.Values)
        {
            if (index.Term.CalendarYear == year && PlanSeasonInfo.FromTerm(index.Term.Season) == season
                && season.Matches(index.Term.Season))
                return index;
        }
        return null;
    }

    public Course? FindCourse(CourseKey key)
    {
        foreach (var index in ByCode.Values)
        {
            if (index.Course(key) is Course course) return course;
        }
        return null;
    }

    public bool OfferedIn(CatalogIndex index, CourseKey key) => index.Sections(key).Count > 0;
}
