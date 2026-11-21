// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class WaitlistDisplayTests
{
    private static CourseSection Make(int seats, int waitCapacity, int waitCount) => new()
    {
        Crn = "39766",
        Subject = "INAM",
        Number = "1925",
        SectionNumber = "01",
        ClassType = "Lecture",
        SeatCapacity = 15,
        SeatsRemaining = seats,
        WaitlistCapacity = waitCapacity,
        WaitlistCount = waitCount,
    };

    [Fact]
    public void NoWaitlistShowsNothingNew()
    {
        var section = Make(0, 0, 0);
        Assert.Null(section.WaitlistLabel);
        Assert.Equal("0 of 15 seats", section.SeatsLabel);
        Assert.Equal("Full", section.CourseSeatsSpoken());
        Assert.False(section.IsOpen);
    }

    [Fact]
    public void WaitlistWithNoSeatsIsFullWithSize()
    {
        var section = Make(0, 10, 7);
        Assert.Equal("Waitlist 7/10", section.WaitlistLabel);
        Assert.Equal("0 of 15 seats · Waitlist 7/10", section.SeatsLabel);
        Assert.False(section.IsOpen);
    }

    [Fact]
    public void WaitlistWithSeatsIsNotOpen()
    {
        var section = Make(2, 10, 3);
        Assert.False(section.IsOpen);
        Assert.StartsWith("Waitlist", section.CourseSeatsSpoken());
        var stats = CatalogIndex.MakeStats([section, Make(4, 10, 0)]);
        Assert.Equal(1, stats.OpenSectionCount);
        var item = new WatchItem { LastSeatsRemaining = 2, LastWaitlistCount = 3 };
        Assert.Equal(WatchlistStatus.Waitlist, WatchlistStatusInfo.For(item, false));
    }

    [Fact]
    public void EmptyWaitlistWithSeatsIsOpen()
    {
        var section = Make(2, 10, 0);
        Assert.True(section.IsOpen);
        Assert.Equal("Waitlist 0/10", section.WaitlistLabel);
        var item = new WatchItem { LastSeatsRemaining = 2 };
        Assert.Equal(WatchlistStatus.Open, WatchlistStatusInfo.For(item, false));
    }

    [Fact]
    public void SeatAlertSkipsAWaitlistedSection()
    {
        var item = new WatchItem { TermCode = "202730", Crn = "39766", CourseKey = new CourseKey("INAM", "1925") };
        var live = new Dictionary<string, IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>>
        {
            ["202730"] = new Dictionary<CourseKey, IReadOnlyList<CourseSection>> { [item.CourseKey] = [Make(1, 10, 7)] },
        };
        Assert.Empty(WatchlistCheck.Compare([item], live).Alerts);
    }

    [Fact]
    public void CachedSectionWithoutWaitFieldsDecodesAsZero()
    {
        var json = """{"crn":"1","subject":"CS","number":"1000","seatCapacity":10,"seatsRemaining":3}""";
        var section = JsonSerializer.Deserialize<CourseSection>(json, AppJson.Catalog)!;
        Assert.Equal(0, section.WaitlistCapacity);
        Assert.Equal(0, section.WaitlistCount);
        Assert.True(section.IsOpen);
        var banner = JsonSerializer.Deserialize<BannerSection>(
            """{"courseReferenceNumber":"1","courseNumber":"1000","subject":"CS"}""", AppJson.Catalog)!;
        Assert.Null(banner.WaitCount);
    }
}
