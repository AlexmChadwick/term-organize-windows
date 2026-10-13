// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize;

public sealed class AppSession
{
    private readonly Dictionary<string, CatalogIndex> _indexes = new(StringComparer.Ordinal);

    public UserDataStore Store { get; } = new();
    public CatalogIndex? Index { get; private set; }
    public Term? Term { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<Term> KnownTerms { get; private set; } = [];
    public event Action? Changed;

    public AppSession()
    {
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        try
        {
            var catalog = await Task.Run(BundledCatalog.Load).ConfigureAwait(false);
            Remember(catalog);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        Changed?.Invoke();
    }

    public void RememberTerms(IReadOnlyList<Term> terms) => KnownTerms = terms;

    /// <summary>
    /// Switches to a Banner term. A term already in memory or on disk opens immediately.
    /// Anything else is downloaded from Banner and kept for the next launch.
    /// </summary>
    public async Task SelectAsync(Term term, bool force = false, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!force && _indexes.TryGetValue(term.Code, out var loaded))
        {
            Use(loaded, null);
            return;
        }
        if (!force)
        {
            TermCatalog? disk = null;
            try { disk = TermCatalogCache.TryLoad(term.Code); }
            catch (Exception) { disk = null; }
            if (disk is not null)
            {
                Remember(disk);
                return;
            }
        }
        progress?.Invoke($"Downloading {term.Name}\u2026");
        try
        {
            var catalog = await TermCatalogCache.DownloadAsync(
                term,
                progress: (done, total) => progress?.Invoke(total <= 0 ? $"Downloading {term.Name}\u2026" : $"Downloading {term.Name}\u2026 {done:N0} sections"),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            Remember(catalog);
        }
        catch (Exception ex)
        {
            Index = null;
            Term = term;
            Error = $"Couldn\u2019t download {term.Name}. {ex.Message}";
            Changed?.Invoke();
        }
    }

    public Course? Course(CourseKey key) => Index?.Course(key);

    /// <summary>
    /// Replaces one course's sections with a live Banner response and rebuilds the lookup tables.
    /// Seat counts, schedules, and rooms then see the same sections.
    /// </summary>
    public void RefreshCourse(CourseKey key, IReadOnlyList<BannerSection> sections)
    {
        if (Index is null) return;
        CatalogBuilder.ApplyCourseRefresh(Index.Catalog, key, sections);
        var rebuilt = CatalogIndex.Build(Index.Catalog);
        _indexes[rebuilt.Term.Code] = rebuilt;
        Index = rebuilt;
        Term = rebuilt.Term;
    }

    public IReadOnlyList<CourseSection> Sections(CourseKey key) => Index?.Sections(key) ?? [];

    private void Remember(TermCatalog catalog)
    {
        var index = CatalogIndex.Build(catalog);
        _indexes[catalog.Term.Code] = index;
        Use(index, null);
    }

    private void Use(CatalogIndex index, string? error)
    {
        Index = index;
        Term = index.Term;
        Error = error;
        Changed?.Invoke();
    }
}
