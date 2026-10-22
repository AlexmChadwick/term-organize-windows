// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Security.Cryptography;
using System.Text;

namespace TermOrganize.Mcp;

/// <summary>
/// Finds the UserData.json file the desktop app reads.
/// An unpackaged build uses LocalApplicationData\TermOrganize\UserData.json.
/// A packaged build uses the same API, which Windows redirects into the package's
/// LocalCache\Local folder (virtualized AppData), not ApplicationData LocalState.
/// LocalState is still checked because a future build might write there.
/// </summary>
public static class DataPaths
{
    public const string PackageName = "TermOrganize";
    public const string Publisher = "CN=Term Organize";
    public const string FileName = "UserData.json";

    /// <summary>
    /// Windows package publisher id: SHA-256 of the UTF-16LE publisher string,
    /// first 8 bytes as bits (most significant bit first), one trailing zero,
    /// then 13 groups of 5 bits in Crockford's base32 alphabet (no i, l, o, u).
    /// </summary>
    public static string PublisherId(string publisher)
    {
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));
        const string alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
        Span<byte> bits = stackalloc byte[65];
        for (var i = 0; i < 64; i++)
        {
            var value = hash[i / 8];
            bits[i] = (byte)((value >> (7 - (i % 8))) & 1);
        }
        var id = new char[13];
        for (var group = 0; group < 13; group++)
        {
            var value = 0;
            for (var bit = 0; bit < 5; bit++)
                value = (value << 1) | bits[group * 5 + bit];
            id[group] = alphabet[value];
        }
        return new string(id);
    }

    public static string PackageFamilyName => PackageName + "_" + PublisherId(Publisher);

    public static string Resolve(string? explicitPath, string? localAppData = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        var root = string.IsNullOrWhiteSpace(localAppData)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppData;
        string? newest = null;
        var newestTime = DateTime.MinValue;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in Candidates(root))
        {
            if (!seen.Add(candidate) || !File.Exists(candidate)) continue;
            var written = File.GetLastWriteTimeUtc(candidate);
            if (newest is null || written > newestTime)
            {
                newest = candidate;
                newestTime = written;
            }
        }
        if (newest is not null) return newest;

        var packageRoot = Path.Combine(root, "Packages", PackageFamilyName);
        if (Directory.Exists(packageRoot))
            return Path.Combine(packageRoot, "LocalCache", "Local", PackageName, FileName);
        return Path.Combine(root, PackageName, FileName);
    }

    public static IEnumerable<string> Candidates(string localAppData)
    {
        yield return Path.Combine(localAppData, PackageName, FileName);
        var packages = Path.Combine(localAppData, "Packages");
        foreach (var folder in PackageFolders(packages))
        {
            yield return Path.Combine(folder, "LocalCache", "Local", PackageName, FileName);
            yield return Path.Combine(folder, "LocalState", PackageName, FileName);
            yield return Path.Combine(folder, "LocalState", FileName);
        }
    }

    private static IEnumerable<string> PackageFolders(string packages)
    {
        yield return Path.Combine(packages, PackageFamilyName);
        if (!Directory.Exists(packages)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(packages, PackageName + "_*"))
            yield return dir;
    }
}
