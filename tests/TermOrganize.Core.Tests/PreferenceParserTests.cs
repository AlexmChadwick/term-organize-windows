// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class PreferenceParserTests
{
    [Fact]
    public void ExampleSentenceSetsStartBlockAndFriday()
    {
        var result = PreferenceParser.Interpret("No classes before 10, choir Tuesdays 6-8pm, Fridays off");
        Assert.Null(result.Message);
        Assert.Equal(10 * 60, result.Preferences.EarliestStart!.Minutes);
        Assert.Equal(PreferenceStrength.Required, result.Preferences.EarliestStart.Strength);
        Assert.True(result.Preferences.DaysOff.Contains(5));
        var block = Assert.Single(result.Preferences.BlockedTimes);
        Assert.Equal("Choir", block.Label);
        Assert.Equal(DaySet.Tuesday, block.Days);
        Assert.Equal(18 * 60, block.Start);
        Assert.Equal(20 * 60, block.End);
    }

    [Fact]
    public void SoftFridayDoesNotHardenAFirmStart()
    {
        var result = PreferenceParser.Interpret("No classes before 10, but I'd like Fridays off");
        Assert.Equal(PreferenceStrength.Required, result.Preferences.EarliestStart!.Strength);
        Assert.Equal(PreferenceStrength.Preferred, result.Preferences.DaysOffStrength);
    }

    [Fact]
    public void WorkFridaysIsRequired()
    {
        var result = PreferenceParser.Interpret("I work Fridays");
        Assert.Equal(DaySet.Friday, result.Preferences.DaysOff);
        Assert.Equal(PreferenceStrength.Required, result.Preferences.DaysOffStrength);
    }

    [Fact]
    public void DoneByFiveIsAfternoon()
    {
        var result = PreferenceParser.Interpret("Done by 5");
        Assert.Equal(17 * 60, result.Preferences.LatestEnd!.Minutes);
    }

    [Fact]
    public void BandsFormatSeatsBreaksAndWorkload()
    {
        var result = PreferenceParser.Interpret("Prefer mornings, no night classes, online only, open seats, no back-to-back, at most 3 classes a day, and lunch");
        Assert.Equal(TimePreference.Prefer, result.Preferences[TimeBand.Morning]);
        Assert.Equal(TimePreference.Avoid, result.Preferences[TimeBand.Night]);
        Assert.Equal(FormatPreference.Online, result.Preferences.Format);
        Assert.Equal(SeatPolicy.OpenOnly, result.Preferences.Seats);
        Assert.Equal(BreakPreference.AvoidBackToBack, result.Preferences.Breaks);
        Assert.Equal(3, result.Preferences.MaxClassesPerDay);
        Assert.NotNull(result.Preferences.Lunch);
    }

    [Fact]
    public void EmptyAndUnknownSentencesDoNotInventWishes()
    {
        Assert.Contains("Describe", PreferenceParser.Interpret("  ").Message);
        var unknown = PreferenceParser.Interpret("hello there");
        Assert.Equal(0, unknown.ChangedCount);
        Assert.True(unknown.Preferences.IsDefault);
    }

    [Fact]
    public void WorkRangeWithoutMeridianIsAfternoon()
    {
        var result = PreferenceParser.Interpret("work Tuesdays 1-5");
        var block = Assert.Single(result.Preferences.BlockedTimes);
        Assert.Equal(13 * 60, block.Start);
        Assert.Equal(17 * 60, block.End);
    }
}
