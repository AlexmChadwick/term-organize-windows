// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Foundation-only data for the professors directory and monograms.

using System.Globalization;
using System.Text;

namespace TermOrganize.Core;

/// <summary>One row of the directory.</summary>
public sealed class ProfessorDirectoryEntry : IEquatable<ProfessorDirectoryEntry>
{
    /// <summary>The Banner name (Last, First).</summary>
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public int SectionCount { get; init; }
    /// <summary>CS 3000, CS 4530 +2</summary>
    public required string CourseSummary { get; init; }
    /// <summary>Last-name initial, or #.</summary>
    public required string Letter { get; init; }
    /// <summary>Lowercased, diacritic-free names and course codes for matching.</summary>
    public required string SearchText { get; init; }

    public static List<ProfessorDirectoryEntry> Build(CatalogIndex index)
    {
        var order = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
        return index.Instructors.Select(summary => new ProfessorDirectoryEntry
        {
            Id = summary.Name,
            DisplayName = summary.DisplayName,
            SectionCount = summary.Crns.Count,
            CourseSummary = SummarizeCourses(summary.CourseKeys),
            Letter = LetterFor(summary.Name),
            SearchText = Fold($"{summary.DisplayName} {summary.Name} {string.Join(" ", summary.CourseKeys)}"),
        }).OrderBy(entry => entry.Id, order).ToList();
    }

    private static string SummarizeCourses(IReadOnlyList<CourseKey> keys)
    {
        if (keys.Count == 0) return "No courses";
        var shown = string.Join(", ", keys.Take(3));
        return keys.Count > 3 ? $"{shown} +{keys.Count - 3}" : shown;
    }

    public static string LetterFor(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return "#";
        var folded = Fold(trimmed[..1]).ToUpperInvariant();
        if (folded.Length == 0) return "#";
        var scalar = folded[0];
        return char.IsLetter(scalar) ? scalar.ToString() : "#";
    }

    /// <summary>Lowercases and strips diacritics so searches match Jose with José.</summary>
    public static string Fold(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(character);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant().Trim();
    }

    public bool Equals(ProfessorDirectoryEntry? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as ProfessorDirectoryEntry);
    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
}

/// <summary>Entries sharing a last-name initial.</summary>
public sealed class ProfessorDirectoryGroup
{
    public required string Letter { get; init; }
    public List<ProfessorDirectoryEntry> Entries { get; init; } = [];
    public string Id => Letter;

    /// <summary>Groups entries already sorted by name, keeping # last.</summary>
    public static List<ProfessorDirectoryGroup> Grouping(IReadOnlyList<ProfessorDirectoryEntry> entries)
    {
        var groups = new List<ProfessorDirectoryGroup>();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (positions.TryGetValue(entry.Letter, out var position)) groups[position].Entries.Add(entry);
            else
            {
                positions[entry.Letter] = groups.Count;
                groups.Add(new ProfessorDirectoryGroup { Letter = entry.Letter, Entries = [entry] });
            }
        }
        return groups.Where(group => group.Letter != "#").Concat(groups.Where(group => group.Letter == "#")).ToList();
    }
}

public static class ProfessorNames
{
    /// <summary>Last, First becomes FL. Single names give one letter.</summary>
    public static string Initials(string bannerName)
    {
        var tokens = Instructor.DisplayNameOf(bannerName)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 0 && char.IsLetter(token[0]))
            .ToList();
        if (tokens.Count == 0 || !char.IsLetter(tokens[0][0])) return "?";
        if (tokens.Count < 2) return char.ToUpperInvariant(tokens[0][0]).ToString();
        return $"{char.ToUpperInvariant(tokens[0][0])}{char.ToUpperInvariant(tokens[^1][0])}";
    }
}
