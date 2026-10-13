// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// Term catalogs downloaded from Banner, stored as gzip next to the user's data.
/// The bundled Fall 2026 snapshot stays in the app; every other term is fetched on demand.
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

    public static async Task<TermCatalog> DownloadAsync(
        Term term,
        BannerClient? client = null,
        Action<int, int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        client ??= BannerClient.Shared;
        var sections = await client.FetchAllSectionsAsync(term.Code, progress: progress, cancellationToken: cancellationToken).ConfigureAwait(false);
        var subjects = await client.FetchSubjectsAsync(term.Code, cancellationToken).ConfigureAwait(false);
        var campuses = await client.FetchCampusesAsync(term.Code, cancellationToken).ConfigureAwait(false);
        var attributes = await client.FetchAttributesAsync(term.Code, cancellationToken).ConfigureAwait(false);
        var catalog = CatalogBuilder.Merge(TryLoad(term.Code), sections, term, subjects, campuses, attributes);
        Save(catalog);
        return catalog;
    }
}
