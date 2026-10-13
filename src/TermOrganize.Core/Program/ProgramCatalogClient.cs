// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Reads Northeastern's public academic catalog (catalog.northeastern.edu).
// Term Organize is not affiliated with the university.

using System.Net.Http.Headers;
using System.Text.Json;

namespace TermOrganize.Core;

/// <summary>
/// Downloads catalog editions, the A–Z program index, and a program's requirements,
/// and keeps them on disk so the degree page can open offline after the first fetch.
/// </summary>
public static class ProgramCatalogClient
{
    public static readonly TimeSpan CurrentEditionLifetime = TimeSpan.FromDays(7);

    public static string Root { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TermOrganize",
        "ProgramCatalog");

    public static async Task<CatalogEditions> EditionsAsync(HttpClient? http = null, bool force = false, CancellationToken cancellationToken = default)
    {
        var cached = LoadEditions();
        if (!force && cached is not null && !cached.IsEstimated && DateTimeOffset.UtcNow - cached.FetchedAt < CurrentEditionLifetime)
            return cached;
        try
        {
            var html = await GetAsync(CatalogEditions.SiteUrl, http, cancellationToken).ConfigureAwait(false);
            var current = ProgramCatalogParser.CurrentEdition(html);
            if (current is null) return cached ?? CatalogEditions.Estimated();
            var editions = new CatalogEditions(current.Value, ProgramCatalogParser.ArchivedEditions(html), DateTimeOffset.UtcNow);
            SaveEditions(editions);
            return editions;
        }
        catch (Exception) when (cached is not null)
        {
            return cached;
        }
        catch (Exception)
        {
            return CatalogEditions.Estimated();
        }
    }

    public static async Task<IReadOnlyList<CatalogProgram>> ProgramsAsync(
        CatalogEditions editions,
        CatalogYear year,
        HttpClient? http = null,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var cached = LoadPrograms(year);
        if (!force && cached is not null && IsFresh(cached.FetchedAt, year, editions)) return cached.Programs;
        var html = await GetAsync(editions.Url(year, "azindex/"), http, cancellationToken).ConfigureAwait(false);
        var programs = ProgramCatalogParser.Programs(html);
        SavePrograms(year, programs);
        return programs;
    }

    public static async Task<ProgramRequirements?> RequirementsAsync(
        CatalogEditions editions,
        CatalogYear year,
        CatalogProgram program,
        HttpClient? http = null,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var cached = LoadRequirements(year, program.Path);
        if (!force && cached is not null && IsFresh(cached.FetchedAt, year, editions)) return cached;
        var client = http ?? Shared;
        var html = await GetAsync(editions.Url(year, program.Path), client, cancellationToken).ConfigureAwait(false);
        var requirements = ProgramCatalogParser.Requirements(html, year, program);
        if (requirements is null) return cached;
        var widePath = ProgramCatalogParser.UniversitywideLink(html)
            ?? "undergraduate/university-academics/university-wide-requirements/";
        try
        {
            var wideHtml = await GetAsync(editions.Url(year, widePath), client, cancellationToken).ConfigureAwait(false);
            requirements.Universitywide = ProgramCatalogParser.UniversitywideRequirements(wideHtml);
        }
        catch (Exception)
        {
            requirements.Universitywide = cached?.Universitywide;
        }
        SaveRequirements(requirements);
        return requirements;
    }

    public static void SaveEditions(CatalogEditions editions)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "editions.json"), JsonSerializer.Serialize(editions, AppJson.UserData));
    }

    public static CatalogEditions? LoadEditions()
    {
        var path = Path.Combine(Root, "editions.json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<CatalogEditions>(File.ReadAllText(path), AppJson.UserData); }
        catch (JsonException) { return null; }
    }

    public static void SavePrograms(CatalogYear year, IReadOnlyList<CatalogProgram> programs)
    {
        var file = new ProgramFile { FetchedAt = DateTimeOffset.UtcNow, Programs = programs.ToList() };
        var path = ProgramsPath(year);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(file, AppJson.UserData));
    }

    public static ProgramFile? LoadPrograms(CatalogYear year)
    {
        var path = ProgramsPath(year);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<ProgramFile>(File.ReadAllText(path), AppJson.UserData); }
        catch (JsonException) { return null; }
    }

    public static void SaveRequirements(ProgramRequirements requirements)
    {
        var path = RequirementsPath(requirements.Year, requirements.Program.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(requirements, AppJson.UserData));
    }

    public static ProgramRequirements? LoadRequirements(CatalogYear year, string programPath)
    {
        var path = RequirementsPath(year, programPath);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<ProgramRequirements>(File.ReadAllText(path), AppJson.UserData); }
        catch (JsonException) { return null; }
    }

    private static bool IsFresh(DateTimeOffset fetchedAt, CatalogYear year, CatalogEditions editions) =>
        year != editions.Current || DateTimeOffset.UtcNow - fetchedAt < CurrentEditionLifetime;

    private static string ProgramsPath(CatalogYear year) => Path.Combine(Root, year.Slug, "programs.json");

    public static string RequirementsPath(CatalogYear year, string programPath)
    {
        var name = new string(programPath.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        while (name.Contains("--", StringComparison.Ordinal)) name = name.Replace("--", "-", StringComparison.Ordinal);
        if (name.Length == 0) name = "program";
        return Path.Combine(Root, year.Slug, name + ".json");
    }

    private static async Task<string> GetAsync(Uri url, HttpClient? http, CancellationToken cancellationToken)
    {
        var client = http ?? Shared;
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static readonly HttpClient Shared = Create();

    public static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TermOrganize", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        return client;
    }

    public sealed class ProgramFile
    {
        public DateTimeOffset FetchedAt { get; set; }
        public List<CatalogProgram> Programs { get; set; } = [];
    }
}
