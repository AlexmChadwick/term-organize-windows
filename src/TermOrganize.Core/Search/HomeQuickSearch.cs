// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// The preset searches the catalog home screens offer.

namespace TermOrganize.Core;

/// <summary>Preset searches offered on the catalog home screen.</summary>
public enum HomeQuickSearch
{
    OpenSeats,
    Honors,
    Online,
    NoFridays,
    FirstYear,
}

public static class HomeQuickSearchInfo
{
    public static IReadOnlyList<HomeQuickSearch> All { get; } =
        [HomeQuickSearch.OpenSeats, HomeQuickSearch.Honors, HomeQuickSearch.Online, HomeQuickSearch.NoFridays, HomeQuickSearch.FirstYear];

    public static string Title(this HomeQuickSearch search) => search switch
    {
        HomeQuickSearch.OpenSeats => "Open seats",
        HomeQuickSearch.Honors => "Honors",
        HomeQuickSearch.Online => "Online",
        HomeQuickSearch.NoFridays => "No Friday classes",
        HomeQuickSearch.FirstYear => "1000-level",
        _ => "",
    };

    public static string Symbol(this HomeQuickSearch search) => search switch
    {
        HomeQuickSearch.OpenSeats => "chair",
        HomeQuickSearch.Honors => "star",
        HomeQuickSearch.Online => "laptopcomputer",
        HomeQuickSearch.NoFridays => "calendar.badge.minus",
        HomeQuickSearch.FirstYear => "1.circle",
        _ => "",
    };

    public static SearchFilters Filters(this HomeQuickSearch search)
    {
        var filters = new SearchFilters();
        switch (search)
        {
            case HomeQuickSearch.OpenSeats: filters.OpenSeatsOnly = true; break;
            case HomeQuickSearch.Honors: filters.HonorsOnly = true; break;
            case HomeQuickSearch.Online: filters.Instruction = InstructionFilter.Online; break;
            case HomeQuickSearch.NoFridays:
                filters.AllowedDays = DaySet.Monday | DaySet.Tuesday | DaySet.Wednesday | DaySet.Thursday;
                break;
            case HomeQuickSearch.FirstYear: filters.Levels.Add(1); break;
        }
        return filters;
    }
}
