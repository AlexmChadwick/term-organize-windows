// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Mcp.Tests;

public sealed class SectionJsonTests
{
    [Fact]
    public void FullExposesOpenSeatsAndTheQueue()
    {
        var section = new CourseSection { Crn = "39766", SeatCapacity = 15, SeatsRemaining = 2, WaitlistCapacity = 10, WaitlistCount = 3 };
        var json = SectionJson.Full(section);
        Assert.Equal(2, json["seats_remaining"]);
        Assert.Equal(0, json["open_seats"]);
        Assert.Equal(3, json["waitlist_count"]);
        Assert.Equal(10, json["waitlist_capacity"]);
    }
}
