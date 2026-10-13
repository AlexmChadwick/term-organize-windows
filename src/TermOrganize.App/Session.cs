// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize;

public sealed class AppSession
{
    public UserDataStore Store { get; } = new();
    public CatalogIndex? Index { get; private set; }
    public Term? Term { get; private set; }
    public string? Error { get; private set; }
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
            Index = CatalogIndex.Build(catalog);
            Term = catalog.Term;
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        Changed?.Invoke();
    }

    public void Select(Term term)
    {
        if (Index?.Term.Code == term.Code)
        {
            Term = Index.Term;
            Changed?.Invoke();
            return;
        }
        Term = term;
        Error = $"The bundled snapshot is {Index?.Term.Name ?? "Fall 2026"}. {term.Name} can be downloaded from Banner when you refresh that term.";
        Changed?.Invoke();
    }

    public Course? Course(CourseKey key) => Index?.Course(key);

    public IReadOnlyList<CourseSection> Sections(CourseKey key) => Index?.Sections(key) ?? [];
}
