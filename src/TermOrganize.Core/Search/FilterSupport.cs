// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// The pieces of the search filters that don't draw anything: day presets, chip titles and the
// minutes-after-midnight conversion for time pickers.

namespace TermOrganize.Core;

/// <summary>Meeting-day presets offered by the filter bar's Days menu.</summary>
public enum SearchDayPreset
{
    Any,
    NoFridays,
    Mwr,
    Tf,
    Mr,
    Wf,
    /// <summary>Any day set that no preset describes. Choosing it opens the full day editor.</summary>
    Custom,
}

public static class SearchDayPresetInfo
{
    public static IReadOnlyList<SearchDayPreset> All { get; } =
        [SearchDayPreset.Any, SearchDayPreset.NoFridays, SearchDayPreset.Mwr, SearchDayPreset.Tf, SearchDayPreset.Mr, SearchDayPreset.Wf, SearchDayPreset.Custom];

    /// <summary>The days sections may meet on, or null for no restriction. Custom has no fixed days of its own.</summary>
    public static DaySet? Days(this SearchDayPreset preset) => preset switch
    {
        SearchDayPreset.NoFridays => DaySet.Monday | DaySet.Tuesday | DaySet.Wednesday | DaySet.Thursday,
        SearchDayPreset.Mwr => DaySet.Monday | DaySet.Wednesday | DaySet.Thursday,
        SearchDayPreset.Tf => DaySet.Tuesday | DaySet.Friday,
        SearchDayPreset.Mr => DaySet.Monday | DaySet.Thursday,
        SearchDayPreset.Wf => DaySet.Wednesday | DaySet.Friday,
        _ => null,
    };

    public static string MenuTitle(this SearchDayPreset preset) => preset switch
    {
        SearchDayPreset.Any => "Any days",
        SearchDayPreset.NoFridays => "No Friday classes",
        SearchDayPreset.Mwr => "Mon, Wed, Thu only",
        SearchDayPreset.Tf => "Tue, Fri only",
        SearchDayPreset.Mr => "Mon, Thu only",
        SearchDayPreset.Wf => "Wed, Fri only",
        SearchDayPreset.Custom => "Custom…",
        _ => "",
    };

    public static string ChipTitle(this SearchDayPreset preset) => preset switch
    {
        SearchDayPreset.Any => "Days",
        SearchDayPreset.NoFridays => "No Fridays",
        SearchDayPreset.Mwr => "MWR only",
        SearchDayPreset.Tf => "TF only",
        SearchDayPreset.Mr => "MR only",
        SearchDayPreset.Wf => "WF only",
        SearchDayPreset.Custom => "Custom days",
        _ => "",
    };

    /// <summary>The preset that describes exactly these days, if any. Never Custom.</summary>
    public static SearchDayPreset? Matching(DaySet? days)
    {
        foreach (var preset in All)
        {
            if (preset == SearchDayPreset.Custom) continue;
            var presetDays = preset.Days();
            if (presetDays is null && days is null) return preset;
            if (presetDays is DaySet expected && days is DaySet actual && expected.Raw == actual.Raw) return preset;
        }
        return null;
    }

    /// <summary>The preset to show as selected: a matching preset, or Custom.</summary>
    public static SearchDayPreset Selection(DaySet? days) => Matching(days) ?? SearchDayPreset.Custom;
}

/// <summary>Short chip titles that summarize the active filters.</summary>
public static class SearchFilterTitles
{
    public static string Days(DaySet? days)
    {
        if (days is null) return "Days";
        if (SearchDayPresetInfo.Matching(days) is SearchDayPreset preset) return preset.ChipTitle();
        return days.Value.IsEmpty ? "No set days" : $"{days.Value.CompactLabel} only";
    }

    public static string Subjects(ISet<string> subjects) => subjects.Count switch
    {
        0 => "Subject",
        1 => subjects.First(),
        _ => $"{subjects.Count} subjects",
    };

    public static string Nupaths(ISet<NUPath> nupaths) => nupaths.Count switch
    {
        0 => "NUpath",
        1 or 2 => string.Join(", ", Enum.GetValues<NUPath>().Where(nupaths.Contains).Select(path => path.Short())),
        _ => $"NUpath · {nupaths.Count}",
    };

    public static string Level(int level) => level == 0 ? "Under 1000" : $"{level}000";
}

/// <summary>Converts between minutes after midnight and a time-only date a picker can edit.</summary>
public static class SearchTimeOfDay
{
    /// <summary>A fixed mid-January day, so a daylight-saving shift can never skip a time.</summary>
    public static DateTime Date(int minutes)
    {
        var clamped = Math.Clamp(minutes, 0, 24 * 60 - 1);
        return new DateTime(2001, 1, 15, clamped / 60, clamped % 60, 0, DateTimeKind.Unspecified);
    }

    public static int Minutes(DateTime date) => date.Hour * 60 + date.Minute;
}
