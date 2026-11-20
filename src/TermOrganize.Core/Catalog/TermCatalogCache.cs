// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// Term catalogs downloaded from Banner, stored as gzip next to the user's data.
/// Bundled snapshots stay in the app. Any other term is fetched on demand.
/// </summary>
public static class TermCatalogCache
{
    public static string Root { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TermOrganize",
        "Catalogs");

    public static string PathFor(string termCode) => Path.Combine(Root, CatalogArchive.FileName(termCode));

    public static TermCatalog? TryLoad(string termCode)
    {
        var path = PathFor(termCode);
        return File.Exists(path) ? CatalogArchive.Read(path) : null;
    }

    public static void Save(TermCatalog catalog)
    {
        Directory.CreateDirectory(Root);
        CatalogArchive.Write(catalog, PathFor(catalog.Term.Code));
    }

    /// <summary>
    /// Course details known from other local terms' catalogs, used to fill in stubs
    /// right after a new term catalog is downloaded.
    /// </summary>
    public static Dictionary<CourseKey, Course> LocalKnowledge(string excludingTermCode)
    {
        var result = new Dictionary<CourseKey, Course>();
        var codes = new HashSet<string>(BundledCatalog.TermCodes(), StringComparer.Ordinal);
        if (Directory.Exists(Root))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(Root))
                {
                    if (CatalogArchive.TermCode(file) is string code) codes.Add(code);
                }
            }
            catch (Exception) { }
        }

        foreach (var code in codes)
        {
            if (code == excludingTermCode) continue;
            TermCatalog? catalog = null;
            try { catalog = TryLoad(code) ?? BundledCatalog.TryLoad(code); }
            catch (Exception) { }
            if (catalog is null) continue;

            foreach (var course in catalog.Courses)
            {
                if ((course.DetailsLoaded || !string.IsNullOrWhiteSpace(course.Description) || course.Prereqs is not null)
                    && !result.ContainsKey(course.Key))
                {
                    result[course.Key] = course;
                }
            }
        }
        return result;
    }

    public static async Task<TermCatalog> DownloadAsync(
        Term term,
        BannerClient? client = null,
        Action<int, int>? progress = null,
        IReadOnlyDictionary<CourseKey, Course>? knowledge = null,
        CancellationToken cancellationToken = default)
    {
        client ??= BannerClient.Shared;
        var sections = await client.FetchAllSectionsAsync(term.Code, progress: progress, cancellationToken: cancellationToken).ConfigureAwait(false);
        var subjects = await client.FetchSubjectsAsync(term.Code, cancellationToken).ConfigureAwait(false);
        var campuses = await client.FetchCampusesAsync(term.Code, cancellationToken).ConfigureAwait(false);
        var attributes = await client.FetchAttributesAsync(term.Code, cancellationToken).ConfigureAwait(false);
        knowledge ??= LocalKnowledge(term.Code);
        var catalog = CatalogBuilder.Merge(TryLoad(term.Code), sections, term, subjects, campuses, attributes, knowledge: knowledge);
        Save(catalog);
        return catalog;
    }
}
