// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// What the Settings screens offer to choose from.

namespace TermOrganize.Core;

public static class SettingsFormat
{
    /// <summary>8 AM, 12 PM, 9 PM. 24 is midnight.</summary>
    public static string HourLabel(int hour)
    {
        var hour24 = ((hour % 24) + 24) % 24;
        var hour12 = hour24 % 12 == 0 ? 12 : hour24 % 12;
        return $"{hour12} {(hour24 < 12 ? "AM" : "PM")}";
    }
}

/// <summary>Picker contents shared by the settings screens. There is no catalog store; pass the lists you have.</summary>
public static class SettingsSupport
{
    /// <summary>Terms to offer, always including the selected one so a picker has a matching tag.</summary>
    public static List<Term> TermPickerOptions(IEnumerable<Term> terms, Term? selected)
    {
        var options = terms.ToList();
        if (selected is not null && options.All(term => term.Code != selected.Code))
            options.Insert(0, selected);
        return options;
    }

    /// <summary>Options by college, in the order Northeastern, Professional Studies, Law.</summary>
    public static List<(TermCollege College, List<Term> Terms)> TermPickerGroups(IEnumerable<Term> terms, Term? selected)
    {
        var options = TermPickerOptions(terms, selected);
        var groups = new List<(TermCollege, List<Term>)>();
        foreach (var college in new[] { TermCollege.Neu, TermCollege.Cps, TermCollege.Law })
        {
            var matching = options.Where(term => term.College == college).ToList();
            if (matching.Count > 0) groups.Add((college, matching));
        }
        return groups;
    }

    /// <summary>Campuses with sections this term, plus the saved choice so a picker always has a matching tag.</summary>
    public static List<string> CampusPickerOptions(IEnumerable<string> campusNames, string? saved)
    {
        var names = campusNames.Where(name => !name.Contains("no campus", StringComparison.OrdinalIgnoreCase)).ToList();
        if (saved is not null && !names.Contains(saved)) names.Insert(0, saved);
        return names;
    }
}
