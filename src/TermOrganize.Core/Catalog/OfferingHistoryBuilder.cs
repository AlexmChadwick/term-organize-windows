// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;

namespace TermOrganize.Core;

/// <summary>
/// Offering history from catalogs already on disk, or from Banner checks of specific courses.
/// A cached snapshot is reused until it is older than a week or is missing a course.
/// </summary>
public static class OfferingHistoryBuilder
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    public static OfferingHistory FromCatalogs(IEnumerable<TermCatalog> catalogs, IReadOnlyCollection<CourseKey>? limit = null)
    {
        var offered = new Dictionary<CourseKey, HashSet<string>>();
        var terms = new List<string>();
        foreach (var catalog in catalogs)
        {
            if (catalog.Term.Code.Length == 0 || terms.Contains(catalog.Term.Code)) continue;
            terms.Add(catalog.Term.Code);
            foreach (var section in catalog.Sections)
            {
                if (limit is not null && !limit.Contains(section.CourseKey)) continue;
                if (!offered.TryGetValue(section.CourseKey, out var set))
                    offered[section.CourseKey] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(catalog.Term.Code);
            }
        }
        var ordered = terms.OrderByDescending(code => code, StringComparer.Ordinal).ToList();
        return new OfferingHistory(
            ordered,
            offered.ToDictionary(pair => pair.Key, pair => (IEnumerable<string>)pair.Value),
            limit);
    }

    public static async Task<OfferingHistory> FetchAsync(
        IReadOnlyList<Term> terms,
        IReadOnlyList<CourseKey> keys,
        Func<string, IReadOnlyList<CourseKey>, CancellationToken, Task<IReadOnlySet<CourseKey>>> offeredInTerm,
        CancellationToken cancellationToken = default)
    {
        var offered = keys.ToDictionary(key => key, _ => new List<string>());
        var codes = new List<string>();
        foreach (var term in terms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (term.Code.Length == 0 || codes.Contains(term.Code)) continue;
            codes.Add(term.Code);
            var found = await offeredInTerm(term.Code, keys, cancellationToken).ConfigureAwait(false);
            foreach (var key in found)
            {
                if (offered.TryGetValue(key, out var list)) list.Add(term.Code);
            }
        }
        return new OfferingHistory(
            codes,
            offered.ToDictionary(pair => pair.Key, pair => (IEnumerable<string>)pair.Value),
            keys);
    }

    public static OfferingHistory? TryLoad(string path, IReadOnlyCollection<CourseKey> keys, TimeSpan? maxAge = null)
    {
        if (!File.Exists(path)) return null;
        OfferingFile? file;
        try
        {
            file = JsonSerializer.Deserialize<OfferingFile>(File.ReadAllText(path), AppJson.UserData);
        }
        catch (JsonException)
        {
            return null;
        }
        if (file is null) return null;
        var age = maxAge ?? CacheLifetime;
        if (DateTimeOffset.UtcNow - file.FetchedAt > age) return null;
        var wanted = keys.Select(key => key.ToString()).ToHashSet(StringComparer.Ordinal);
        if (!wanted.IsSubsetOf(file.Keys)) return null;
        var offered = new Dictionary<CourseKey, IEnumerable<string>>();
        foreach (var key in keys)
        {
            file.Offered.TryGetValue(key.ToString(), out var terms);
            offered[key] = terms ?? [];
        }
        return new OfferingHistory(file.Terms, offered, keys);
    }

    public static void Save(string path, OfferingHistory history)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var file = new OfferingFile
        {
            FetchedAt = DateTimeOffset.UtcNow,
            Terms = history.CheckedTerms.ToList(),
            Keys = (history.CheckedKeys ?? history.Offered.Keys).Select(key => key.ToString()).Distinct(StringComparer.Ordinal).ToList(),
            Offered = history.Offered.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.ToList(), StringComparer.Ordinal),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(file, AppJson.UserData));
    }

    private sealed class OfferingFile
    {
        public DateTimeOffset FetchedAt { get; set; }
        public List<string> Terms { get; set; } = [];
        public List<string> Keys { get; set; } = [];
        public Dictionary<string, List<string>> Offered { get; set; } = new(StringComparer.Ordinal);
    }
}
