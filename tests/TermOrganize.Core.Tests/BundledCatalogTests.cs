// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class BundledCatalogTests
{
    [Fact]
    public void StartupOpensTheNewestBundledTerm()
    {
        Assert.Equal(["202710", "202730"], BundledCatalog.TermCodes());
        Assert.Equal("202730", BundledCatalog.DefaultTerm);
        Assert.Equal("202730", BundledCatalog.Load().Term.Code);
        Assert.Equal("202730", BundledCatalog.StartupTerm(null));
        Assert.Equal("202730", BundledCatalog.StartupTerm("199999"));
        Assert.Equal("202710", BundledCatalog.StartupTerm("202710"));
        Assert.Equal("202650", BundledCatalog.StartupTerm("202650", _ => true));
        Assert.Equal("202730", BundledCatalog.SelectedCode(loadedCode: null, savedCode: null));
        Assert.Equal("202730", BundledCatalog.SelectedCode("202730", "202710"));
        Assert.Equal("202710", BundledCatalog.SelectedCode(loadedCode: null, savedCode: "202710"));
    }

    [Fact]
    public void Spring2027GzipLoads()
    {
        var catalog = BundledCatalog.Load("202730");
        Assert.Equal("Spring 2027 Semester (View Only)", catalog.Term.BannerDescription);
        Assert.Equal(3_076, catalog.Courses.Count);
        Assert.Equal(8_326, catalog.Sections.Count);
        Assert.All(catalog.Courses, course => Assert.True(course.DetailsLoaded));
        Assert.Contains(catalog.Sections, section => section.Instructors?.Any(instructor => instructor.Email is null) == true);
        Assert.Contains(catalog.Sections, section => section.Instructors?.Any(instructor => instructor.Email is not null) == true);
    }

    [Fact]
    public void Spring2027BundleHasNoOtherEmailDomains()
    {
        var gzipPath = BundledCatalog.Resolve("term-202730.json.gz");
        var catalogPath = BundledCatalog.Resolve("term-202730.catalog");
        Assert.NotNull(gzipPath);
        Assert.NotNull(catalogPath);
        var fromGzip = GzipJson(gzipPath);
        var fromCatalog = LzfseDecoder.Decompress(File.ReadAllBytes(catalogPath));
        Assert.Equal(fromGzip, fromCatalog);
        AssertOnlyNortheasternDomains(fromGzip);
    }

    [Fact]
    public void TheLzfseCatalogFixtureIsCopiedNextToTheTestBinaries()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Catalog", "term-202730.catalog")));
    }

    [Fact]
    public void EveryShippedCatalogAssetHasOnlyNortheasternEmails()
    {
        // Resolve each asset by name; the test project copies term-202730.catalog to its output.
        var files = new[] { "term-202710.catalog", "term-202710.json.gz", "term-202730.catalog", "term-202730.json.gz" }
            .Select(name => BundledCatalog.Resolve(name) ?? throw new Xunit.Sdk.XunitException($"Missing bundled catalog asset {name}"))
            .ToList();
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            var json = bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B ? GzipJson(file)
                : bytes.Length > 3 && bytes[0] == (byte)'b' && bytes[1] == (byte)'v' && bytes[2] == (byte)'x' ? LzfseDecoder.Decompress(bytes)
                : bytes;
            AssertOnlyNortheasternDomains(json);
        }
    }

    private static byte[] GzipJson(string path)
    {
        using var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static void AssertOnlyNortheasternDomains(byte[] json)
    {
        var addresses = AddressTokens(json);
        Assert.NotEmpty(addresses);
        Assert.All(addresses, address => Assert.True(InstructorEmail.IsAllowedAddress(address), address));
        // Independent of InstructorEmail and of AddressTokens: scans every decoded string by hand.
        Assert.Empty(RawScan.Violations(json));
    }

    [Fact]
    public void RawScanSeesTheBundledAddressesAndFlagsNothing()
    {
        var json202730 = GzipJson(BundledCatalog.Resolve("term-202730.json.gz")!);
        Assert.True(RawScan.Candidates(json202730) > 100);
        Assert.Empty(RawScan.Violations(json202730));

        var json202710 = GzipJson(BundledCatalog.Resolve("term-202710.json.gz")!);
        Assert.True(RawScan.Candidates(json202710) > 100);
        Assert.Empty(RawScan.Violations(json202710));
    }

    /// <summary>
    /// A deliberately separate oracle: no regex and no InstructorEmail. Every at-sign look-alike and
    /// spelled-out " at " in decoded text must belong to an ASCII address on exactly neu.edu or northeastern.edu.
    /// </summary>
    private static class RawScan
    {
        private static readonly char[] AtSigns = ['@', '\uFF20', '\uFE6B'];
        private static readonly string[] Domains = ["neu.edu", "northeastern.edu"];
        private static readonly string[] EntityAtSigns = ["&#64;", "&#x40;", "&commat;"];

        public static List<string> Strings(byte[] json)
        {
            var strings = new List<string>();
            var reader = new Utf8JsonReader(json);
            while (reader.Read())
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName) strings.Add(reader.GetString()!);
            return strings;
        }

        private static string DecodeHtml(string text)
        {
            var s = text;
            foreach (var entity in EntityAtSigns)
                s = s.Replace(entity, "@", StringComparison.OrdinalIgnoreCase);
            return WebUtility.HtmlDecode(s);
        }

        public static int Candidates(byte[] json) => Strings(json).Sum(text =>
        {
            var normalized = DecodeHtml(text).Normalize(NormalizationForm.FormKC);
            return normalized.Count(character => character == '@');
        });

        public static List<string> Violations(byte[] json) => Strings(json).SelectMany(Violations).ToList();

        public static List<string> Violations(string text)
        {
            var found = new List<string>();
            var decoded = DecodeHtml(text);
            var normalized = decoded.Normalize(NormalizationForm.FormKC);

            for (var i = 0; i < normalized.Length; i++)
            {
                if (normalized[i] != '@') continue;
                var start = i + 1;
                var end = start;
                while (end < normalized.Length && !char.IsWhiteSpace(normalized[end]) && ",;<>".IndexOf(normalized[end]) < 0) end++;
                var domain = normalized[start..end];
                var tokenStart = i;
                while (tokenStart > 0 && !char.IsWhiteSpace(normalized[tokenStart - 1]) && ",;<>".IndexOf(normalized[tokenStart - 1]) < 0) tokenStart--;
                // A sentence may end with an address, but only when the address is not the whole value.
                if (domain.EndsWith('.') && tokenStart > 0 && char.IsWhiteSpace(normalized[tokenStart - 1])) domain = domain[..^1];
                var token = normalized[tokenStart..end];
                var plain = text.Contains(token, StringComparison.Ordinal)
                    && token.Contains('@')
                    && domain.All(character => character < 128);
                if (!plain || !Domains.Any(allowed => string.Equals(domain, allowed, StringComparison.OrdinalIgnoreCase)))
                    found.Add(token);
            }
            foreach (var marker in new[] { " at ", "[at]", "(at)" })
            {
                for (var at = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(marker, at + 1, StringComparison.OrdinalIgnoreCase))
                {
                    var tail = text[(at + marker.Length)..];
                    var token = tail.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    var dotIdx = tail.IndexOf(" dot ", StringComparison.OrdinalIgnoreCase);
                    var hasDot = false;
                    if (dotIdx >= 0)
                    {
                        var afterDot = tail[(dotIdx + 5)..].Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                        if (LooksLikeDomain("." + afterDot))
                        {
                            hasDot = true;
                            token = tail[..(dotIdx + 5 + afterDot.Length)];
                        }
                    }
                    // "contact at ada@neu.edu" is plain prose; the at-sign scan above judges the address itself.
                    if (marker == " at " && !hasDot && (token.IndexOfAny(AtSigns) >= 0 || !LooksLikeDomain(token))) continue;
                    found.Add(text[at..Math.Min(text.Length, at + marker.Length + token.Length)]);
                }
            }
            return found;
        }

        private static bool LooksLikeDomain(string token)
        {
            var lower = token.TrimEnd('.', ',', ';', ')').ToLowerInvariant();
            return new[] { ".edu", ".com", ".org", ".net" }.Any(lower.EndsWith);
        }
    }

    [Theory]
    [InlineData("ada@neu.edu", 0)]
    [InlineData("Write to ada@northeastern.edu.", 0)]
    [InlineData("ada\uFF20neu.edu", 1)]
    [InlineData("ada\uFE6Bneu.edu", 1)]
    [InlineData("ada at neu.edu", 1)]
    [InlineData("ada AT northeastern.edu.", 1)]
    [InlineData("ada[at]neu.edu", 1)]
    [InlineData("ada(at)neu.edu", 1)]
    [InlineData("ada@neu.edu.", 1)]
    [InlineData("ada@husky.neu.edu", 1)]
    [InlineData("ada@mail.northeastern.edu", 1)]
    [InlineData("ada@neu.edu.evil.com", 1)]
    [InlineData("ada@neu\uFF0Eedu", 1)]
    [InlineData("ada@northea\u017Ftern.edu", 1)]
    [InlineData("ada@ne\u0443.edu", 1)]
    [InlineData("ada@neu.edu@evil.com", 2)]
    [InlineData("The class meets at 5 pm in Ryder.", 0)]
    [InlineData("ada&#64;gmail.com", 1)]
    [InlineData("ada&#x40;gmail.com", 1)]
    [InlineData("ada&commat;gmail.com", 1)]
    [InlineData("x at gmail dot com", 1)]
    public void RawScanFlagsObfuscatedAddresses(string jsonValue, int expectedViolations)
    {
        var json = System.Text.Encoding.UTF8.GetBytes($"\"{jsonValue}\"");
        Assert.Equal(expectedViolations, RawScan.Violations(json).Count);
    }

    [Theory]
    [InlineData("ada\uFF20neu.edu")]
    [InlineData("ada@neu.edu.")]
    [InlineData("ada@husky.neu.edu")]
    [InlineData("ada@neu\uFF0Eedu")]
    [InlineData("ada@northea\u017Ftern.edu")]
    [InlineData("ada@ne\u0443.edu")]
    [InlineData("ada&#64;gmail.com")]
    [InlineData("ada&#x40;gmail.com")]
    [InlineData("ada&commat;gmail.com")]
    [InlineData("x at gmail dot com")]
    public void ProductionFilterAlsoRejectsTheObfuscatedAddresses(string jsonValue)
    {
        var value = JsonSerializer.Deserialize<string>($"\"{jsonValue}\"");
        Assert.False(InstructorEmail.IsAllowedAddress(value));
        Assert.Null(InstructorEmail.Institutional(value));
    }

    private static List<string> AddressTokens(byte[] json)
    {
        var addresses = new List<string>();
        var reader = new Utf8JsonReader(json);
        while (reader.Read())
        {
            if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName)) continue;
            if (!reader.ValueIsEscaped && !reader.ValueSpan.Contains((byte)'@')) continue;
            var value = reader.GetString()!;
            foreach (Match token in Regex.Matches(value, @"[^\s,;<>]*@[^\s,;<>]*"))
            {
                // Course descriptions can end a sentence with an email address.
                var address = token.Value;
                if (token.Index > 0 && char.IsWhiteSpace(value[token.Index - 1]) && address.EndsWith('.'))
                    address = address[..^1];
                addresses.Add(address);
            }
        }
        return addresses;
    }

    [Theory]
    [InlineData("a@neu.edu_evil.com", "a@neu.edu_evil.com", false)]
    [InlineData("a@neu.edu@evil.com", "a@neu.edu@evil.com", false)]
    [InlineData(@"a\u0040neu.edu@evil.com", "a@neu.edu@evil.com", false)]
    [InlineData(@"a\u0040NEU.EDU", "a@NEU.EDU", true)]
    [InlineData("Contact a@neu.edu.", "a@neu.edu", true)]
    [InlineData("a@neu.edu.", "a@neu.edu.", false)]
    public void AddressScanChecksWholeDecodedTokens(string jsonValue, string expectedToken, bool allowed)
    {
        var json = System.Text.Encoding.UTF8.GetBytes($"\"{jsonValue}\"");
        var token = Assert.Single(AddressTokens(json));
        Assert.Equal(expectedToken, token);
        Assert.Equal(allowed, InstructorEmail.IsAllowedAddress(token));
    }

    [Fact]
    public void ReferenceLibraryLzfseMatchesTheGzipSnapshot()
    {
        var path = BundledCatalog.Resolve("term-202730.catalog");
        Assert.NotNull(path);
        var fromLzfse = CatalogArchive.Read(path);
        var fromGzip = BundledCatalog.Load("202730");
        Assert.Equal(fromGzip.Term.Code, fromLzfse.Term.Code);
        Assert.Equal(fromGzip.Term.BannerDescription, fromLzfse.Term.BannerDescription);
        Assert.Equal(fromGzip.Courses.Select(course => course.Code), fromLzfse.Courses.Select(course => course.Code));
        Assert.Equal(fromGzip.Sections.Select(section => section.Crn), fromLzfse.Sections.Select(section => section.Crn));
    }

    [Fact]
    public void Fall2026StillLoadsBesideSpring()
    {
        var fall = BundledCatalog.Load("202710");
        Assert.Equal("202710", fall.Term.Code);
        Assert.NotEqual(BundledCatalog.Load("202730").Courses.Count, fall.Courses.Count);
    }

    [Fact]
    public void PickerListsBothBundledTermsWhenBannerIsOffline()
    {
        var terms = BundledCatalog.PickerTerms(loaded: null, live: []);
        Assert.Equal(["202730", "202710"], terms.Select(term => term.Code));
        Assert.Equal("Spring 2027", terms[0].Name);
        Assert.Contains("Fall 2026", terms[1].Name);
    }

    [Fact]
    public void PickerKeepsALiveTermAndBothBundledSnapshots()
    {
        var loaded = BundledCatalog.Load("202730").Term;
        var live = new[] { new Term("202750", "Summer 2027 Semester"), new Term("202730", "Spring 2027 Semester") };
        var terms = BundledCatalog.PickerTerms(loaded, live);
        Assert.Equal("202730", terms[0].Code);
        Assert.Contains(terms, term => term.Code == "202710");
        Assert.Contains(terms, term => term.Code == "202750");
    }
}
