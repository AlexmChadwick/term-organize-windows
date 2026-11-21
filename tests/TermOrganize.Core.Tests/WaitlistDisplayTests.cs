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

    private static CourseSection Timed(string crn, int seats, int waitCount, int waitCapacity = 10)
    {
        var section = Make(seats, waitCapacity, waitCount);
        section.Crn = crn;
        section.Subject = "CS";
        section.Number = "3000";
        section.Meetings =
        [
            new Meeting { Days = DaySet.FromIndices([1, 3]), Start = 600, End = 690, Kind = "Class", StartDate = "2026-09-09", EndDate = "2026-12-13" },
        ];
        return section;
    }

    private static ScheduleCourseInput Input(params CourseSection[] sections) => new()
    {
        Key = new CourseKey("CS", "3000"),
        IsRequired = true,
        Sections = sections,
    };

    private static IEnumerable<string> Picked(ScheduleGenerationResult result) =>
        result.Schedules.SelectMany(schedule => schedule.Crns);

    [Fact]
    public void OpenSeatsIsZeroBehindAQueue()
    {
        Assert.Equal(0, Make(2, 10, 3).OpenSeats);
        Assert.Equal(0, Make(2, 0, 3).OpenSeats);
        Assert.Equal(2, Make(2, 10, 0).OpenSeats);
        Assert.Equal(0, Make(-1, 0, 0).OpenSeats);
    }

    [Fact]
    public void StatusLabelIsFullWaitlistOrOpen()
    {
        Assert.Equal("Full", Make(0, 10, 7).StatusLabel);
        Assert.Equal("Waitlist", Make(2, 10, 3).StatusLabel);
        Assert.Equal("Open", Make(2, 10, 0).StatusLabel);
    }

    [Fact]
    public void NoRenderedTextRepeatsTheWordWaitlist()
    {
        foreach (var section in new[] { Make(2, 10, 3), Make(0, 10, 7), Make(2, 0, 3), Make(0, 0, 3) })
        {
            foreach (var text in new[] { section.SeatsLabel, ScheduleFormat.SeatsText(section), section.CourseSeatsSpoken() })
                Assert.True(System.Text.RegularExpressions.Regex.Matches(text, "waitlist", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count <= 1, text);
        }
        Assert.Equal("Waitlist 3/10", ScheduleFormat.SeatsText(Make(2, 10, 3)));
        Assert.Equal("Full · Waitlist 7/10", ScheduleFormat.SeatsText(Make(0, 10, 7)));
        Assert.Equal("2 of 15 open", ScheduleFormat.SeatsText(Make(2, 10, 0)));
        Assert.Equal("Full", ScheduleFormat.SeatsText(Make(0, 0, 0)));
    }

    [Fact]
    public void WaitCountWithZeroCapacityShowsCountOnlyAndIsNotOpen()
    {
        var section = Make(2, 0, 3);
        Assert.Equal("Waitlist 3", section.WaitlistLabel);
        Assert.Equal("2 of 15 seats · Waitlist 3", section.SeatsLabel);
        Assert.False(section.IsOpen);
        Assert.Equal("Waitlist 3", ScheduleFormat.SeatsText(section));
        Assert.Equal("Waitlist, 3 waiting", section.CourseSeatsSpoken());
        Assert.Equal("Full, 3 waiting", Make(0, 0, 3).CourseSeatsSpoken());
    }

    [Fact]
    public void CourseAggregatesIgnoreSeatsBehindAQueue()
    {
        var stats = CatalogIndex.MakeStats([Make(2, 10, 3), Make(4, 10, 0)]);
        Assert.Equal(4, stats.SeatsRemaining);
        Assert.Equal("1 of 2 sections open", stats.AvailabilityLabel);
        var queued = CatalogIndex.MakeStats([Make(2, 10, 3)]);
        Assert.Equal(0, queued.SeatsRemaining);
        Assert.Equal("1 section · full", queued.AvailabilityLabel);
        Assert.True(queued.IsFull);
    }

    [Fact]
    public void CrossListedSectionsKeepTheirOwnWaitCount()
    {
        var banner = new BannerSection { CourseReferenceNumber = "1", CourseNumber = "3000", Subject = "CS", CrossList = "X", CrossListCapacity = 30, CrossListAvailable = 5, WaitCount = 0, WaitCapacity = 10 };
        var sibling = new BannerSection { CourseReferenceNumber = "2", CourseNumber = "3000", Subject = "CS", CrossList = "X", CrossListCapacity = 30, CrossListAvailable = 5, WaitCount = 4, WaitCapacity = 10 };
        var built = CatalogBuilder.MakeSections([banner, sibling]);
        Assert.Equal(0, built.Single(section => section.Crn == "1").WaitlistCount);
        Assert.True(built.Single(section => section.Crn == "1").IsOpen);
        Assert.False(built.Single(section => section.Crn == "2").IsOpen);
    }

    [Fact]
    public void GeneratorHideFullSkipsAQueuedSection()
    {
        var queued = Timed("Q", 2, 3);
        var open = Timed("O", 2, 0);
        var filters = new ScheduleFilters { HideFullSections = true };
        Assert.Equal(["O"], Picked(ScheduleGenerator.Generate([Input(queued, open)], filters)).Distinct());
        Assert.Empty(ScheduleGenerator.Generate([Input(queued)], filters).Schedules);
        Assert.NotEmpty(ScheduleGenerator.Generate([Input(queued)], new ScheduleFilters()).Schedules);
    }

    [Fact]
    public void GeneratorMinSeatsSkipsAQueuedSection()
    {
        var filters = new ScheduleFilters { MinSeats = 1 };
        Assert.Empty(ScheduleGenerator.Generate([Input(Timed("Q", 5, 3))], filters).Schedules);
        Assert.Equal(["O"], Picked(ScheduleGenerator.Generate([Input(Timed("Q", 5, 3), Timed("O", 5, 0))], filters)).Distinct());
    }

    [Fact]
    public void GeneratorOpenSeatsOnlyPreferenceSkipsAQueuedSection()
    {
        var preferences = new SchedulePreferences { Seats = SeatPolicy.OpenOnly };
        var result = ScheduleGenerator.Generate([Input(Timed("Q", 2, 3), Timed("O", 2, 0))], new ScheduleFilters(), preferences);
        Assert.Equal(["O"], Picked(result).Distinct());
        Assert.Empty(ScheduleGenerator.Generate([Input(Timed("Q", 2, 3))], new ScheduleFilters(), preferences).Schedules);
    }

    [Fact]
    public void PreferenceRulesTreatAQueuedSectionAsFull()
    {
        Assert.True(SchedulePreferenceRules.IsFull(Make(2, 10, 3)));
        Assert.False(SchedulePreferenceRules.IsFull(Make(2, 10, 0)));
    }

    [Fact]
    public void WatchSeedsFromTheQueueAndAlertsOnlyWhenItClears()
    {
        var path = Path.Combine(Path.GetTempPath(), $"WaitlistDisplayTests-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserDataStore(path);
            var queued = Make(2, 10, 3);
            store.Watch(queued, "Intro", "202730");
            var item = store.WatchItems("202730").Single();
            Assert.Equal(3, item.LastWaitlistCount);
            Assert.Equal(0, item.OpenSeats);
            Assert.Equal(WatchlistStatus.Waitlist, WatchlistStatusInfo.For(item, false));
            var live = (CourseSection section) => new Dictionary<string, IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>>
            {
                ["202730"] = new Dictionary<CourseKey, IReadOnlyList<CourseSection>> { [item.CourseKey] = [section] },
            };
            Assert.Empty(WatchlistCheck.Compare([item], live(queued)).Alerts);
            var cleared = WatchlistCheck.Compare([item], live(Make(2, 10, 0)));
            Assert.Equal(WatchlistAlert.Kind.Seats, cleared.Alerts.Single().AlertKind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WatchDetailNeverRepeatsWaitlistOrCountsQueuedSeats()
    {
        var item = new WatchItem { LastSeatsRemaining = 2, LastWaitlistCount = 3, LastWaitlistRemaining = 7, SeatCapacity = 15 };
        var status = WatchlistStatusInfo.For(item, false);
        Assert.Equal("7 waitlist spots open", status.Detail(item));
        Assert.Equal("7 waitlist spots open", status.SpokenDetail(item));
    }

    [Fact]
    public void PlanWarnsAboutAQueuedSection()
    {
        var queued = Timed("A1", 2, 3);
        var course = new Course { Subject = "CS", Number = "3000", Title = "CS 3000", MinCredits = 4, MaxCredits = 4, DetailsLoaded = true };
        var index = CatalogIndex.Build(new TermCatalog
        {
            Term = new Term("202730", "Spring 2027 Semester"),
            Courses = [course],
            Sections = [queued],
        });
        var plan = new SchedulePlan { Name = "Plan A", TermCode = "202730", SelectedCRNs = ["A1"] };
        plan.Courses.Add(new PlanCourse { Key = new CourseKey("CS", "3000") });
        var resolved = new ResolvedPlan(plan, index);
        Assert.Single(resolved.FullSections);
        Assert.Equal(["1 full section"], resolved.Warnings().Select(w => w.Text).ToArray());
    }
}
