// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class ShellSupportTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_790_000_000);
    private static readonly CourseKey Cs3000 = new("CS", "3000");

    [Fact]
    public void ASectionThatGainsSeatsRaisesOneAlert()
    {
        var item = WatchItem();
        var outcome = WatchlistCheck.Compare([item], Live(Section(3)), Now);
        var alert = Assert.Single(outcome.Alerts);
        Assert.Equal(WatchlistAlert.Kind.Seats, alert.AlertKind);
        Assert.Equal("Seats open in CS 3000", alert.Title);
        Assert.Equal("3 of 40 seats in Lecture 02 (CRN 12345). Register soon!", alert.Body);
        Assert.Equal("termorganize://section/202710/12345", alert.Url.AbsoluteUri);
        Assert.Equal(3, outcome.Items[0].LastSeatsRemaining);
        Assert.Equal(Now, outcome.Items[0].LastNotifiedAt);
        Assert.Equal(Now, outcome.Items[0].LastCheckedAt);
    }

    [Fact]
    public void ASectionThatAlreadyHadSeatsRaisesNoAlert()
    {
        var outcome = WatchlistCheck.Compare([WatchItem(seats: 2)], Live(Section(5)), Now);
        Assert.Empty(outcome.Alerts);
        Assert.Equal(5, outcome.Items[0].LastSeatsRemaining);
    }

    [Fact]
    public void WaitlistAlertsNeedTheOptIn()
    {
        var live = Live(Section(0, waitlist: 2));
        Assert.Empty(WatchlistCheck.Compare([WatchItem()], live, Now).Alerts);
        var optedIn = WatchlistCheck.Compare([WatchItem(notify: true)], live, Now);
        Assert.Equal(WatchlistAlert.Kind.Waitlist, Assert.Single(optedIn.Alerts).AlertKind);
        Assert.Equal("2 waitlist spots open in Lecture 02 (CRN 12345). Join soon!", optedIn.Alerts[0].Body);
    }

    [Fact]
    public void ASectionTheRegistrarNoLongerListsIsReportedMissing()
    {
        var item = WatchItem(crn: "99999", seats: 1);
        var outcome = WatchlistCheck.Compare([item], Live(Section(3)), Now);
        Assert.Contains(item.Id, outcome.MissingIds);
        Assert.Empty(outcome.Alerts);
        Assert.Equal(1, outcome.Items[0].LastSeatsRemaining);
    }

    [Fact]
    public void TermsThatWereNotCheckedStayUntouched()
    {
        var other = WatchItem(seats: 0, term: "202730");
        var outcome = WatchlistCheck.Compare([other], Live(Section(3)), Now);
        Assert.Equal(0, outcome.Items[0].LastSeatsRemaining);
        Assert.Null(outcome.Items[0].LastCheckedAt);
        Assert.Contains("202710", outcome.CheckedTermCodes);
    }

    [Fact]
    public void StatusesDescribeTheLastCheck()
    {
        var open = WatchItem(seats: 3);
        Assert.Equal(WatchlistStatus.Open, WatchlistStatusInfo.For(open, false));
        Assert.Equal("3 of 40 open", WatchlistStatus.Open.Detail(open));
        Assert.Equal("3 of 40 seats open", WatchlistStatus.Open.SpokenDetail(open));

        var noLimit = WatchItem(seats: 1, capacity: 0);
        Assert.Equal("1 open", WatchlistStatus.Open.Detail(noLimit));
        Assert.Equal("1 seat open", WatchlistStatus.Open.SpokenDetail(noLimit));

        var waitlist = WatchItem(seats: 0, waitlist: 1);
        Assert.Equal(WatchlistStatus.Waitlist, WatchlistStatusInfo.For(waitlist, false));
        Assert.Equal("Full · 1 waitlist spot open", WatchlistStatus.Waitlist.Detail(waitlist));

        var full = WatchItem();
        Assert.Equal(WatchlistStatus.Full, WatchlistStatusInfo.For(full, false));
        Assert.Equal("Full", WatchlistStatus.Full.Detail(full));
        Assert.Equal(WatchlistStatus.Missing, WatchlistStatusInfo.For(open, true));
        Assert.Equal("No longer listed", WatchlistStatus.Missing.Detail(open));
    }

    [Fact]
    public void AgoWordingGrowsWithTheTime()
    {
        Assert.Equal("just now", WatchlistFormat.Ago(Now.AddSeconds(-30), Now));
        Assert.Equal("5 min ago", WatchlistFormat.Ago(Now.AddMinutes(-5), Now));
        Assert.Equal("3 hr ago", WatchlistFormat.Ago(Now.AddHours(-3), Now));
        Assert.Equal("yesterday", WatchlistFormat.Ago(Now.AddHours(-30), Now));
        Assert.Equal("4 days ago", WatchlistFormat.Ago(Now.AddDays(-4), Now));
        Assert.Equal("just now", WatchlistFormat.Ago(Now.AddSeconds(60), Now));
    }

    [Fact]
    public void AClassOccupiesItsStartUpToButNotIncludingItsEnd()
    {
        var classes = new[] { Booking("CS 3000", 600, 665) };
        Assert.Equal(new RoomAvailability.Status.Free(600), Status(classes, 599));
        Assert.False(Status(classes, 600).IsFree);
        Assert.False(Status(classes, 664).IsFree);
        Assert.Equal(new RoomAvailability.Status.Free(null), Status(classes, 665));
        Assert.Equal(new RoomAvailability.Status.Free(null), Status(classes, 620, weekday: 3));
    }

    [Fact]
    public void BackToBackClassesAreOneStretchOfUse()
    {
        var classes = new[] { Booking("CS 3000", 600, 665), Booking("MATH 1341", 665, 730) };
        var busy = Assert.IsType<RoomAvailability.Status.Busy>(Status(classes, 620));
        Assert.Equal(730, busy.Occupancy.Until);
        Assert.Null(busy.Occupancy.FreeUntil);
    }

    [Fact]
    public void AGapBetweenClassesIsReported()
    {
        var classes = new[] { Booking("CS 3000", 600, 665), Booking("MATH 1341", 720, 780) };
        var busy = Assert.IsType<RoomAvailability.Status.Busy>(Status(classes, 620));
        Assert.Equal(665, busy.Occupancy.Until);
        Assert.Equal(720, busy.Occupancy.FreeUntil);
        Assert.Equal(new RoomAvailability.Status.Free(720), Status(classes, 670));
    }

    [Fact]
    public void StatusesReadAsSentences()
    {
        Assert.Equal("Free until 10:00 AM", new RoomAvailability.Status.Free(600).Summary);
        Assert.Equal("Free for the rest of the day", new RoomAvailability.Status.Free(null).Summary);
        var status = Status([Booking("CS 3000", 600, 665)], 620);
        Assert.Equal("In use until 11:05 AM", status.Summary);
        Assert.Equal("In use: CS 3000 until 11:05 AM", status.DetailedSummary);
    }

    [Fact]
    public void ARoomFreeForUnderHalfAnHourIsOnlyBrieflyFree()
    {
        var classes = new[] { Booking("CS 3000", 600, 665) };
        var soon = new RoomAvailability.Moment(2, 580);
        var early = new RoomAvailability.Moment(2, 500);
        Assert.True(Status(classes, 580).IsFreeOnlyBriefly(soon));
        Assert.False(Status(classes, 500).IsFreeOnlyBriefly(early));
    }

    [Fact]
    public void ShortSessionsOnlyCountOnTheirOwnDates()
    {
        var workshop = Booking("CS 2500", 600, 665, from: "2026-09-15", to: "2026-09-16");
        var room = Room([workshop]);
        Assert.Empty(RoomAvailability.Bookings(room, 2));
        Assert.Single(RoomAvailability.Bookings(room, 2, new DateOnly(2026, 9, 15)));
        Assert.Empty(RoomAvailability.Bookings(room, 2, new DateOnly(2026, 10, 6)));
    }

    [Fact]
    public void FreeStretchesAreTheGapsBetweenClasses()
    {
        var day = new[]
        {
            Booking("CS 3000", 600, 665),
            Booking("MATH 1341", 665, 730),
            Booking("PHYS 1151", 780, 840),
        };
        Assert.Equal(
            [new RoomAvailability.Stretch(420, 600), new RoomAvailability.Stretch(730, 780), new RoomAvailability.Stretch(840, 1320)],
            RoomAvailability.FreeStretches(day, 420, 1320));
        Assert.Equal([new RoomAvailability.Stretch(730, 780)], RoomAvailability.FreeStretches(day, 620, 800));
        Assert.Empty(RoomAvailability.FreeStretches(day, 600, 730));
        Assert.Equal([new RoomAvailability.Stretch(420, 1320)], RoomAvailability.FreeStretches([], 420, 1320));
        Assert.Empty(RoomAvailability.FreeStretches([], 600, 600));
        var overlapping = new[] { Booking("CS 3000", 600, 700), Booking("CS 3001", 650, 680) };
        Assert.Equal([new RoomAvailability.Stretch(700, 800)], RoomAvailability.FreeStretches(overlapping, 600, 800));
    }

    [Fact]
    public void FreeRoomCountsAreTheRoomsWithNoClassDuringTheWholeHour()
    {
        var busyMorning = Room([Booking("CS 3000", 9 * 60 + 50, 11 * 60 + 30)], "210");
        var busyAfternoon = Room([Booking("MATH 1341", 13 * 60, 14 * 60)], "300");
        var empty = Room([], "400");
        var counts = RoomAvailability.FreeRoomCounts([busyMorning, busyAfternoon, empty], 2);
        Assert.Equal(15, counts.Length);
        Assert.Equal(3, counts[0]);
        Assert.Equal(2, counts[2]);
        Assert.Equal(2, counts[3]);
        Assert.Equal(2, counts[4]);
        Assert.Equal(3, counts[5]);
        Assert.Equal(2, counts[6]);
        Assert.Equal(3, counts[7]);
        Assert.Equal(Enumerable.Repeat(0, 15), RoomAvailability.FreeRoomCounts([], 2));
        Assert.Equal(Enumerable.Repeat(1, 15), RoomAvailability.FreeRoomCounts([busyMorning], 0));
    }

    [Fact]
    public void SearchMatchesBuildingsAndRoomsByTheirWords()
    {
        var ryder = new BuildingSummary
        {
            Code = "RY",
            Name = "Ryder Hall",
            CampusCode = "BOS",
            Rooms = [Room([], "210"), Room([], "300")],
        };
        var snell = new BuildingSummary
        {
            Code = "SL",
            Name = "Snell Library",
            CampusCode = "BOS",
            Rooms = [Room([], "210", "SL")],
        };
        var moment = new RoomAvailability.Moment(2, 600);
        var all = RoomAvailability.Of([ryder, snell], moment);
        Assert.Equal(["RY"], RoomAvailability.Filter(all, "ry").Select(item => item.Building.Code));
        Assert.Equal(["SL"], RoomAvailability.Filter(all, "snell").Select(item => item.Building.Code));
        Assert.Equal(2, RoomAvailability.Filter(all, "ryder")[0].Free.Count);
        var byRoom = RoomAvailability.Filter(all, "210");
        Assert.Equal(["RY", "SL"], byRoom.Select(item => item.Building.Code));
        Assert.Equal(["210"], byRoom[0].Free.Select(entry => entry.Room.Room));
        Assert.Equal(2, RoomAvailability.Filter(all, "  ").Count);
    }

    [Fact]
    public void MomentsStayWithinTheWeek()
    {
        var wrapped = new RoomAvailability.Moment(8, 99_999);
        Assert.Equal(1, wrapped.Weekday);
        Assert.Equal(24 * 60 - 1, wrapped.Minutes);
        Assert.True(new RoomAvailability.Moment(2, 600).IsWithinClassHours);
        Assert.False(new RoomAvailability.Moment(2, 6 * 60).IsWithinClassHours);
        Assert.Equal("Tuesday at 2:30 PM", new RoomAvailability.Moment(2, 14 * 60 + 30).Label);
    }

    [Fact]
    public void FollowUpNotesSayWhatComesNext()
    {
        var today = new[] { Booking("CS 3000", 600, 665), Booking("MATH 1341", 720, 780) };
        Assert.Equal("Next: CS 3000, 10:00 \u2013 11:05 AM", RoomsText.FollowUpNote(new RoomAvailability.Status.Free(600), today));
        Assert.Equal("No more classes today.", RoomsText.FollowUpNote(new RoomAvailability.Status.Free(null), today));
        Assert.Equal("No classes are scheduled here today.", RoomsText.FollowUpNote(new RoomAvailability.Status.Free(null), []));
        var busy = Assert.IsType<RoomAvailability.Status.Busy>(RoomAvailability.At(today, 620));
        Assert.Equal("Then free until 12:00 PM", RoomsText.FollowUpNote(busy, today));
        Assert.Equal("12 of 40 rooms free", RoomsText.FreeSummary(12, 40));
        Assert.Equal("1 of 1 room free", RoomsText.FreeSummary(1, 1));
    }

    [Fact]
    public void HourLabelsUseTheTwelveHourClock()
    {
        Assert.Equal("12 AM", SettingsFormat.HourLabel(0));
        Assert.Equal("8 AM", SettingsFormat.HourLabel(8));
        Assert.Equal("12 PM", SettingsFormat.HourLabel(12));
        Assert.Equal("9 PM", SettingsFormat.HourLabel(21));
        Assert.Equal("12 AM", SettingsFormat.HourLabel(24));
    }

    [Fact]
    public void LegalTextNamesTheLicenseAndWhereToReadIt()
    {
        Assert.Equal("https://www.gnu.org/licenses/agpl-3.0.html", LegalText.LicenseUrl);
        Assert.Contains($"{LegalText.PlatformName} adaptation of SearchNEU", LegalText.SearchNeuSummary, StringComparison.Ordinal);
    }

    private static WatchItem WatchItem(string crn = "12345", int seats = 0, int waitlist = 0, int capacity = 40, bool notify = false, string term = "202710") => new()
    {
        TermCode = term,
        Crn = crn,
        CourseKey = Cs3000,
        CourseTitle = "Algorithms and Data",
        SectionLabel = "Lecture · Section 02",
        LastSeatsRemaining = seats,
        LastWaitlistRemaining = waitlist,
        SeatCapacity = capacity,
        NotifyOnWaitlist = notify,
    };

    private static CourseSection Section(int seats, int waitlist = 0, int capacity = 40) => new()
    {
        Crn = "12345",
        Subject = "CS",
        Number = "3000",
        Title = "Algorithms and Data",
        SectionNumber = "02",
        SeatCapacity = capacity,
        SeatsRemaining = seats,
        WaitlistCapacity = 10,
        WaitlistRemaining = waitlist,
        ClassType = "Lecture",
        Campus = "Boston",
    };

    private static Dictionary<string, IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>> Live(CourseSection section) =>
        new() { ["202710"] = new Dictionary<CourseKey, IReadOnlyList<CourseSection>> { [Cs3000] = [section] } };

    private static RoomBooking Booking(string code, int start, int end, int[]? days = null, string? from = null, string? to = null)
    {
        var key = CourseKey.Parse(code) ?? Cs3000;
        return new RoomBooking
        {
            Crn = $"1{start}",
            Course = key,
            Days = DaySet.FromIndices(days ?? [2]),
            Start = start,
            End = end,
            StartDate = from,
            EndDate = to,
        };
    }

    private static RoomSchedule Room(IEnumerable<RoomBooking> bookings, string number = "210", string building = "WVH") => new()
    {
        BuildingCode = building,
        BuildingName = building == "SL" ? "Snell Library" : "West Village H",
        Room = number,
        Bookings = bookings.ToList(),
    };

    private static RoomAvailability.Status Status(IReadOnlyList<RoomBooking> bookings, int minutes, int weekday = 2) =>
        RoomAvailability.At(Room(bookings), new RoomAvailability.Moment(weekday, minutes));
}
