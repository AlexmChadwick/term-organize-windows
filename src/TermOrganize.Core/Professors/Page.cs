// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// What an instructor's page shows, worked out from the term's catalog.

using System.Globalization;
using System.Text;

namespace TermOrganize.Core;

/// <summary>
/// A stable color for a course when no plan color is assigned.
/// The palette matches the app's ten course colors; only the name travels, not a platform color.
/// </summary>
public readonly record struct CourseColor(int Index, string Name)
{
    public const double FillOpacity = 0.18;

    /// <summary>Light-mode SwiftUI system colors, in palette order.</summary>
    private static readonly (byte R, byte G, byte B)[] RgbPalette =
    [
        (0x00, 0x7A, 0xFF), (0xFF, 0x95, 0x00), (0x34, 0xC7, 0x59), (0xAF, 0x52, 0xDE), (0xFF, 0x2D, 0x55),
        (0x30, 0xB0, 0xC7), (0x58, 0x56, 0xD6), (0xA2, 0x84, 0x5E), (0x00, 0xC7, 0xBE), (0xFF, 0x3B, 0x30),
    ];

    public static readonly string[] Palette =
        ["blue", "orange", "green", "purple", "pink", "teal", "indigo", "brown", "mint", "red"];

    public byte R => RgbPalette[Slot].R;
    public byte G => RgbPalette[Slot].G;
    public byte B => RgbPalette[Slot].B;

    private int Slot => ((Index % Palette.Length) + Palette.Length) % Palette.Length;

    public static CourseColor At(int index)
    {
        var count = Palette.Length;
        var slot = ((index % count) + count) % count;
        return new CourseColor(slot, Palette[slot]);
    }

    /// <summary>djb2 over the UTF-8 bytes of the code, then a slot in the palette.</summary>
    public static CourseColor ForCode(string code)
    {
        ulong hash = 5381;
        foreach (var bite in Encoding.UTF8.GetBytes(code))
            hash = unchecked(hash * 33 + bite);
        return At((int)(hash % (ulong)Palette.Length));
    }

    public static CourseColor ForCourse(CourseKey key) => ForCode(key.ToString());
}

public static class ProfessorPage
{
    /// <summary>
    /// Their sections, ordered by course and section number. A live refresh can drop sections the
    /// directory still remembers, so only the ones the index still lists come back.
    /// </summary>
    public static List<CourseSection> Sections(InstructorSummary summary, CatalogIndex index)
    {
        var order = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
        return summary.Crns
            .Select(index.Section)
            .OfType<CourseSection>()
            .OrderBy(section => section.CourseKey)
            .ThenBy(section => section.SectionNumber, order)
            .ToList();
    }

    /// <summary>
    /// A color per course. Each course keeps its stable color unless another of the instructor's
    /// courses already took it, so a calendar and its legend are never ambiguous.
    /// </summary>
    public static Dictionary<CourseKey, CourseColor> CourseColors(IReadOnlyList<CourseKey> keys)
    {
        var used = new HashSet<int>();
        var colors = new Dictionary<CourseKey, CourseColor>();
        foreach (var key in keys)
        {
            var slot = CourseColor.ForCourse(key).Index;
            if (used.Count < CourseColor.Palette.Length)
            {
                while (used.Contains(slot)) slot = (slot + 1) % CourseColor.Palette.Length;
            }
            used.Add(slot);
            colors[key] = CourseColor.At(slot);
        }
        return colors;
    }

    /// <summary>Rate My Professors search for Northeastern (school 696).</summary>
    public static Uri RateMyProfessorsUrl(string displayName)
    {
        var query = Uri.EscapeDataString(displayName);
        return new Uri($"https://www.ratemyprofessors.com/search/professors/696?q={query}");
    }

    /// <summary>Section 02 / Sections 01, 04: which sections of a course the instructor teaches.</summary>
    public static string SectionNote(CourseKey key, IEnumerable<CourseSection> sections)
    {
        var numbers = sections.Where(section => section.CourseKey == key).Select(section => section.SectionNumber).ToList();
        return $"{(numbers.Count == 1 ? "Section" : "Sections")} {string.Join(", ", numbers)}";
    }

    /// <summary>Teaches 6 sections across 3 courses, counted from the sections that still exist.</summary>
    public static string SummaryText(IReadOnlyList<CourseSection> sections)
    {
        var courseCount = sections.Select(section => section.CourseKey).Distinct().Count();
        return $"Teaches {CourseFormat.Count(sections.Count, "section")} across {CourseFormat.Count(courseCount, "course")}";
    }
}
