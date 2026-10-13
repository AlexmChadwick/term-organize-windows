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
    public const string DefaultTerm = "202710";

    public static TermCatalog Load(string termCode = DefaultTerm)
    {
        var names = new[] { $"term-{termCode}.json.gz", $"term-{termCode}.catalog", $"term-{termCode}.json" };
        foreach (var name in names)
        {
            var path = Resolve(name);
            if (path is not null) return CatalogArchive.Read(path);
        }
        throw new FileNotFoundException($"No bundled catalog for term {termCode}.");
    }

    public static string? Resolve(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Catalog", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
        };
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var nested = Path.Combine(dir.FullName, "src", "TermOrganize.Core", "Catalog", fileName);
            if (File.Exists(nested)) return nested;
            var direct = Path.Combine(dir.FullName, "Catalog", fileName);
            if (File.Exists(direct)) return direct;
        }
        return null;
    }
}
