// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.IO.Compression;
using System.Text.Json;

namespace TermOrganize.Core;

/// <summary>
/// Term catalogs on disk. The Mac app writes LZFSE (<c>.catalog</c>, magic <c>bvx</c>).
/// This port also reads raw JSON and gzip, and writes gzip so a catalog round-trips
/// without a native compressor. Decoders accept all three.
/// </summary>
public static class CatalogArchive
{
    public const string FileExtension = "catalog";

    public static string FileName(string termCode) => $"term-{termCode}.{FileExtension}";

    public static string? TermCode(string name)
    {
        var file = Path.GetFileName(name);
        if (!file.StartsWith("term-", StringComparison.Ordinal)) return null;
        var code = file[5..];
        if (code.EndsWith(".json.gz", StringComparison.Ordinal)) code = code[..^8];
        else if (code.EndsWith(".catalog", StringComparison.Ordinal)) code = code[..^8];
        else if (code.EndsWith(".json", StringComparison.Ordinal)) code = code[..^5];
        else return null;
        return code.Length == 6 && code.All(char.IsDigit) ? code : null;
    }

    public static byte[] Encode(TermCatalog catalog)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(catalog, AppJson.Catalog);
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(json);
        return stream.ToArray();
    }

    public static TermCatalog Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) throw new InvalidDataException("The catalog is empty.");
        byte[] json;
        if (data[0] == (byte)'{') json = data.ToArray();
        else if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
        {
            using var gzip = new GZipStream(new MemoryStream(data.ToArray()), CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            json = output.ToArray();
        }
        else if (data.Length >= 4 && data[0] == (byte)'b' && data[1] == (byte)'v' && data[2] == (byte)'x')
            json = LzfseDecoder.Decompress(data);
        else throw new InvalidDataException("Unrecognized catalog encoding.");
        return JsonSerializer.Deserialize<TermCatalog>(json, AppJson.Catalog)
            ?? throw new InvalidDataException("The catalog JSON was empty.");
    }

    public static TermCatalog Read(string path) => Decode(File.ReadAllBytes(path));

    public static void Write(TermCatalog catalog, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, Encode(catalog));
    }
}

public static class BundledCatalog
{
    /// <summary>The newest bundled term. A launch with no saved catalog opens this snapshot.</summary>
    public static string DefaultTerm
    {
        get
        {
            var codes = TermCodes();
            return codes.Count == 0 ? "202710" : codes[^1];
        }
    }

    public static TermCatalog Load(string? termCode = null) =>
        TryLoad(termCode ?? DefaultTerm) ?? throw new FileNotFoundException($"No bundled catalog for term {termCode ?? DefaultTerm}.");

    public static TermCatalog? TryLoad(string termCode)
    {
        foreach (var name in FileNames(termCode))
        {
            var path = Resolve(name);
            if (path is not null) return CatalogArchive.Read(path);
        }
        return null;
    }

    /// <summary>Bundled term codes, oldest first. <see cref="DefaultTerm"/> is the last one.</summary>
    public static IReadOnlyList<string> TermCodes()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in CandidateDirectories())
        {
            try
            {
                if (!Directory.Exists(directory)) continue;
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    var code = CatalogArchive.TermCode(file);
                    if (code is not null) codes.Add(code);
                }
            }
            catch (Exception) { continue; }
        }
        return codes.OrderBy(code => code, StringComparer.Ordinal).ToList();
    }

    /// <summary>Bundled terms, newest first, using the same file each <see cref="Load"/> call would open.</summary>
    public static IReadOnlyList<Term> BundledTerms()
    {
        var terms = new List<Term>();
        foreach (var code in TermCodes().OrderByDescending(code => code, StringComparer.Ordinal))
        {
            var catalog = TryLoad(code);
            if (catalog is not null) terms.Add(catalog.Term);
        }
        return terms;
    }

    /// <summary>
    /// Title-bar terms. The loaded term stays first, then live Banner terms, then any
    /// bundled term those two did not already include.
    /// </summary>
    public static List<Term> PickerTerms(Term? loaded, IEnumerable<Term> live, IEnumerable<Term>? bundled = null)
    {
        var result = new List<Term>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(Term term)
        {
            if (term.Code.Length == 0 || !seen.Add(term.Code)) return;
            result.Add(term);
        }
        if (loaded is not null) Add(loaded);
        foreach (var term in live) Add(term);
        foreach (var term in bundled ?? BundledTerms()) Add(term);
        return result;
    }

    /// <summary>
    /// Term opened at startup. A saved code that is bundled, or that
    /// <paramref name="isCached"/> reports as already downloaded, wins.
    /// Otherwise <see cref="DefaultTerm"/>.
    /// </summary>
    public static string StartupTerm(string? savedCode, Func<string, bool>? isCached = null)
    {
        if (!string.IsNullOrEmpty(savedCode) && (TermCodes().Contains(savedCode) || (isCached?.Invoke(savedCode) ?? false)))
            return savedCode;
        return DefaultTerm;
    }

    /// <summary>
    /// The row to highlight. A catalog the session already opened wins. Otherwise a
    /// saved choice, then <see cref="DefaultTerm"/>.
    /// </summary>
    public static string SelectedCode(string? loadedCode, string? savedCode)
    {
        if (!string.IsNullOrEmpty(loadedCode)) return loadedCode;
        if (!string.IsNullOrEmpty(savedCode)) return savedCode;
        return DefaultTerm;
    }

    /// <summary>Gzip first. The Mac <c>.catalog</c> is only opened when gzip is absent.</summary>
    private static string[] FileNames(string termCode) =>
        [$"term-{termCode}.json.gz", $"term-{termCode}.catalog", $"term-{termCode}.json"];

    public static string? Resolve(string fileName)
    {
        foreach (var directory in CandidateDirectories())
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Catalog");
        yield return AppContext.BaseDirectory;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            yield return Path.Combine(dir.FullName, "src", "TermOrganize.Core", "Catalog");
            yield return Path.Combine(dir.FullName, "Catalog");
        }
    }
}
