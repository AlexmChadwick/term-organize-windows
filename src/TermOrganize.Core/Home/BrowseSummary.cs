// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Count lines and level names for the lists of courses (subject, NUpath, saved).

using System.Globalization;

namespace TermOrganize.Core;

public static class BrowseSummary
{
    /// <summary>
    /// A count line such as "12 of 42 courses · 9 with open seats".
    /// Open is left out when "open seats only" is already applied, since it would only repeat the shown count.
    /// </summary>
    public static string Text(int shown, int total, int open, bool openOnly = false, string noun = "course")
    {
        string Plural(int count) => count == 1 ? noun : noun + "s";
        var text = shown == total
            ? $"{total.ToString("N0", CultureInfo.CurrentCulture)} {Plural(total)}"
            : $"{shown.ToString("N0", CultureInfo.CurrentCulture)} of {total.ToString("N0", CultureInfo.CurrentCulture)} {Plural(total)}";
        if (open > 0 && !openOnly)
            text += $" · {open.ToString("N0", CultureInfo.CurrentCulture)} with open seats";
        return text;
    }

    /// <summary>1000-level, or a neutral label for course numbers that do not start with 1…9.</summary>
    public static string LevelTitle(int level) => level == 0 ? "Under 1000" : $"{level}000-level";

    /// <summary>3000-level courses, or courses under 1000 for numbers that do not start with 1…9.</summary>
    public static string LevelNoun(int level) => level == 0 ? "courses under 1000" : $"{level}000-level courses";
}
