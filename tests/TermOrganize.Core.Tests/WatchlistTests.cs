// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class WatchlistTests
{
    [Fact]
    public void SeatsAndWaitlistDoNotAlertTogether()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_790_000_000);
        var item = new WatchItem
        {
            TermCode = "202710",
            Crn = "12345",
            CourseKey = new CourseKey("CS", "3000"),
            CourseTitle = "Algorithms and Data",
            NotifyOnWaitlist = true,
        };
        var section = new CourseSection
        {
            Crn = "12345",
            Subject = "CS",
            Number = "3000",
            SectionNumber = "02",
            ClassType = "Lecture",
            SeatsRemaining = 1,
            SeatCapacity = 40,
            WaitlistRemaining = 2,
        };
        var live = new Dictionary<string, IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>>
        {
            ["202710"] = new Dictionary<CourseKey, IReadOnlyList<CourseSection>> { [item.CourseKey] = [section] },
        };
        var alert = Assert.Single(WatchlistCheck.Compare([item], live, now).Alerts);
        Assert.Equal(WatchlistAlert.Kind.Seats, alert.AlertKind);
    }

    [Fact]
    public void ServiceIntervalsMatchTheMacApp()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), WatchlistService.MinimumRefreshInterval);
        Assert.Equal(TimeSpan.FromMinutes(10), WatchlistService.StaleInterval);
    }
}
