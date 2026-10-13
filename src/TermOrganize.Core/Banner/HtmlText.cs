// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TermOrganize.Core;

/// <summary>Minimal helpers for the small HTML fragments Banner returns.</summary>
public static partial class HtmlText
{
    private static readonly Dictionary<string, string> NamedEntities = new(StringComparer.Ordinal)
    {
        ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"", ["apos"] = "'", ["nbsp"] = " ",
        ["ndash"] = "\u2013", ["mdash"] = "\u2014", ["rsquo"] = "\u2019", ["lsquo"] = "\u2018",
        ["rdquo"] = "\u201d", ["ldquo"] = "\u201c", ["hellip"] = "\u2026", ["eacute"] = "é",
        ["egrave"] = "è", ["aacute"] = "á", ["iacute"] = "í", ["oacute"] = "ó", ["uacute"] = "ú",
        ["ntilde"] = "ñ", ["ccedil"] = "ç", ["uuml"] = "ü", ["ouml"] = "ö", ["auml"] = "ä",
        ["copy"] = "©", ["reg"] = "®", ["trade"] = "™", ["deg"] = "°", ["middot"] = "·",
        ["bull"] = "•", ["times"] = "×", ["frac12"] = "½",
    };

    /// <summary>Decodes HTML character references such as <c>&amp;amp;</c>, <c>&amp;#39;</c> and <c>&amp;#x27;</c>.</summary>
    public static string DecodeEntities(string input)
    {
        if (!input.Contains('&')) return input;
        var output = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] != '&')
            {
                output.Append(input[i]);
                continue;
            }
            var window = Math.Min(12, input.Length - i);
            var semi = input.IndexOf(';', i, window);
            if (semi < 0)
            {
                output.Append('&');
                continue;
            }
            var entity = input[(i + 1)..semi];
            var replacement = Replacement(entity);
            if (replacement is null)
            {
                output.Append('&');
                continue;
            }
            output.Append(replacement);
            i = semi;
        }
        return output.ToString();
    }

    /// <summary>
    /// Removes comments and tags, converts <c>&lt;br&gt;</c> to newlines, decodes entities
    /// (twice, as Banner sometimes double-encodes) and collapses whitespace.
    /// </summary>
    public static string PlainText(string html)
    {
        var s = CommentPattern().Replace(html, "");
        s = BreakPattern().Replace(s, "\n");
        s = ParagraphPattern().Replace(s, "\n");
        s = TagPattern().Replace(s, "");
        s = DecodeEntities(DecodeEntities(s));
        var lines = s.Split('\n');
        var result = new List<string>();
        foreach (var line in lines)
        {
            var collapsed = SpacePattern().Replace(line, " ").Trim();
            if (collapsed.Length == 0 && (result.Count == 0 || result[^1].Length == 0)) continue;
            result.Add(collapsed);
        }
        return string.Join('\n', result).Trim();
    }

    /// <summary>The rows of the first <c>&lt;tbody&gt;</c> as arrays of plain-text cells.</summary>
    public static List<List<string>>? TableRows(string html)
    {
        var bodyStart = BodyStartPattern().Match(html);
        if (!bodyStart.Success) return null;
        var bodyEnd = BodyEndPattern().Match(html, bodyStart.Index + bodyStart.Length);
        if (!bodyEnd.Success) return null;
        var body = html[(bodyStart.Index + bodyStart.Length)..bodyEnd.Index];
        return RowPattern().Matches(body).Select(row =>
            CellPattern().Matches(row.Groups[1].Value).Select(cell => PlainText(cell.Groups[1].Value)).ToList()
        ).ToList();
    }

    private static string? Replacement(string entity)
    {
        if (entity.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(entity[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) && IsScalar(hex))
                return char.ConvertFromUtf32((int)hex);
            return null;
        }
        if (entity.StartsWith('#'))
        {
            if (uint.TryParse(entity[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var dec) && IsScalar(dec))
                return char.ConvertFromUtf32((int)dec);
            return null;
        }
        return NamedEntities.TryGetValue(entity.ToLowerInvariant(), out var named) ? named : null;
    }

    private static bool IsScalar(uint value) => value <= 0x10FFFF && (value < 0xD800 || value > 0xDFFF);

    [GeneratedRegex(@"<!--[\s\S]*?-->", RegexOptions.CultureInvariant)]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakPattern();

    [GeneratedRegex("</p>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParagraphPattern();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"[ \t\u00A0]+", RegexOptions.CultureInvariant)]
    private static partial Regex SpacePattern();

    [GeneratedRegex("<tbody", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BodyStartPattern();

    [GeneratedRegex("</tbody>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BodyEndPattern();

    [GeneratedRegex(@"<tr[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RowPattern();

    [GeneratedRegex(@"<td[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CellPattern();
}
