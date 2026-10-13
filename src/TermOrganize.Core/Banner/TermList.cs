// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Portions derived from SearchNEU's Banner client. The app talks only to
// Northeastern's public class search. It is not affiliated with the university.

using System.Net.Http.Headers;
using System.Text.Json;

namespace TermOrganize.Core;

public static class BannerTermList
{
    public const int PageSize = 100;
    public const string TermsUrl = "https://nubanner.neu.edu/StudentRegistrationSsb/ssb/classSearch/getTerms?searchTerm=&offset=1&max=100";

    public static string PageUrl(int offset, int pageSize = PageSize) =>
        $"https://nubanner.neu.edu/StudentRegistrationSsb/ssb/classSearch/getTerms?searchTerm=&offset={offset}&max={pageSize}";

    /// <summary>
    /// Every term Banner will list, newest first. Pages stop when a page is short
    /// or repeats a code already seen, so a server that ignores offset cannot loop.
    /// </summary>
    public static async Task<IReadOnlyList<Term>> FetchAsync(HttpClient? http = null, CancellationToken cancellationToken = default)
    {
        var client = http ?? Shared;
        var all = new List<Term>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var offset = 1;
        while (all.Count < 800)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var response = await client.GetAsync(PageUrl(offset), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var page = Read(doc.RootElement);
            if (page.Count == 0) break;
            var added = 0;
            foreach (var term in page)
            {
                if (!seen.Add(term.Code)) continue;
                all.Add(term);
                added++;
            }
            if (added == 0 || page.Count < PageSize) break;
            offset += PageSize;
        }
        return all;
    }

    public static List<Term> Read(JsonElement root)
    {
        var terms = new List<Term>();
        if (root.ValueKind != JsonValueKind.Array) return terms;
        foreach (var item in root.EnumerateArray())
        {
            var code = item.TryGetProperty("code", out var codeEl) ? codeEl.GetString() ?? "" : "";
            var description = item.TryGetProperty("description", out var desc) ? descEl(desc) : "";
            if (code.Length > 0) terms.Add(new Term(code, description));
        }
        return terms;
    }

    private static string descEl(JsonElement desc) => desc.ValueKind == JsonValueKind.String ? desc.GetString() ?? "" : "";

    private static readonly HttpClient Shared = Create();

    public static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TermOrganize", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
