// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace TermOrganize.Core;

public enum RestrictionKind
{
    Must,
    Cannot,
}

/// <summary>One enrollment list from Banner's section restrictions page.</summary>
public sealed class SectionRestriction
{
    public RestrictionKind Kind { get; set; }
    public string Category { get; set; } = "";
    public List<string> Values { get; set; } = [];
}

public static partial class SectionRestrictions
{
    /// <summary>
    /// Parses the HTML from <c>searchResults/getRestrictions</c>.
    /// An empty document is a loaded section with no lists. A null document stays unknown.
    /// </summary>
    public static List<SectionRestriction> Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return [];
        var text = BlockBreaks().Replace(html, "\n");
        text = Breaks().Replace(text, "\n");
        text = Tags().Replace(text, "");
        text = HtmlText.DecodeEntities(text).Replace('\u00a0', ' ');
        var result = new List<SectionRestriction>();
        SectionRestriction? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (line.Length == 0) continue;
            var heading = Heading().Match(line);
            if (heading.Success)
            {
                current = new SectionRestriction
                {
                    Kind = heading.Groups[1].Value.StartsWith('M') || heading.Groups[1].Value.StartsWith('m')
                        ? RestrictionKind.Must
                        : RestrictionKind.Cannot,
                    Category = heading.Groups["category"].Value.Trim().TrimEnd(':'),
                };
                result.Add(current);
                continue;
            }
            current?.Values.Add(line);
        }
        return result;
    }

    /// <summary>
    /// True when every Must Levels value starts with "CPS -", or every Must Colleges value is
    /// College of Professional Studies. Cannot lists never count. Null means the section was not loaded.
    /// </summary>
    public static bool? CpsOnly(IReadOnlyList<SectionRestriction>? restrictions)
    {
        if (restrictions is null) return null;
        if (Must(restrictions, "Levels") is { Count: > 0 } levels
            && levels.All(value => value.StartsWith("CPS -", StringComparison.Ordinal)))
            return true;
        if (Must(restrictions, "Colleges") is { Count: > 0 } colleges
            && colleges.All(value => value.Equals("College of Professional Studies", StringComparison.OrdinalIgnoreCase)))
            return true;
        return false;
    }

    private static List<string>? Must(IReadOnlyList<SectionRestriction> restrictions, string category)
    {
        var values = restrictions
            .Where(item => item.Kind == RestrictionKind.Must && item.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .SelectMany(item => item.Values)
            .ToList();
        return values;
    }

    [GeneratedRegex("(?i)<br\\s*/?>", RegexOptions.CultureInvariant)]
    private static partial Regex Breaks();

    [GeneratedRegex("(?i)</(p|div|li|tr|h[1-6])>", RegexOptions.CultureInvariant)]
    private static partial Regex BlockBreaks();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();

    [GeneratedRegex(@"^(Must|Cannot) be enrolled in one of the following (?<category>.+?):?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Heading();
}

public static class CpsNotes
{
    public const string Restricted = "Restricted to CPS (College of Professional Studies) students";
    public const string Some = "Some sections are CPS only";

    /// <summary>
    /// The course caption. All known sections CPS-only uses the full sentence. A mix uses the shorter note.
    /// Any unknown section, with no known non-CPS section beside a CPS section, shows nothing.
    /// </summary>
    public static string? ForCourse(IEnumerable<CourseSection> sections)
    {
        var anyTrue = false;
        var anyFalse = false;
        var anyUnknown = false;
        foreach (var section in sections)
        {
            switch (section.CpsOnly)
            {
                case true: anyTrue = true; break;
                case false: anyFalse = true; break;
                default: anyUnknown = true; break;
            }
        }
        if (!anyTrue) return null;
        if (anyFalse) return Some;
        return anyUnknown ? null : Restricted;
    }

    /// <summary>True when every section is known and CPS-only, false when every section is known and one is not, null when any section is unknown.</summary>
    public static bool? Flag(IReadOnlyList<CourseSection> sections)
    {
        if (sections.Count == 0) return null;
        var all = true;
        foreach (var section in sections)
        {
            if (section.CpsOnly is not bool value) return null;
            all &= value;
        }
        return all;
    }
}
