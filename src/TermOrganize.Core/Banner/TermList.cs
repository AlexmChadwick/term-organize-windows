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
    public const string TermsUrl = "https://nubanner.neu.edu/StudentRegistrationSsb/ssb/classSearch/getTerms?searchTerm=&offset=1&max=50";

    public static async Task<IReadOnlyList<Term>> FetchAsync(HttpClient? http = null, CancellationToken cancellationToken = default)
    {
        var client = http ?? Shared;
        using var response = await client.GetAsync(TermsUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var terms = new List<Term>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return terms;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var code = item.TryGetProperty("code", out var codeEl) ? codeEl.GetString() ?? "" : "";
            var description = item.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";
            if (code.Length > 0) terms.Add(new Term(code, description));
        }
        return terms;
    }

    private static readonly HttpClient Shared = Create();

    public static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TermOrganize", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
