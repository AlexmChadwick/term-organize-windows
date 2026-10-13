// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class CloudSyncTests
{
    [Fact]
    public void NewerSchemaIsLeftAlone()
    {
        var local = new UserData { Version = 1, Preferences = new AppPreferences { PreferredCampus = "Boston" } };
        var remote = new UserData { Version = 2, Preferences = new AppPreferences { PreferredCampus = "Oakland", ShowWeekends = true } };
        var merged = CloudSync.Merge(local, remote);
        Assert.Equal("Boston", merged.Preferences.PreferredCampus);
        Assert.False(merged.Preferences.ShowWeekends);
        Assert.Equal("Boston", local.Preferences.PreferredCampus);
    }

    [Fact]
    public void MostRecentPlanWinsAndATieGoesToRemote()
    {
        var id = Guid.NewGuid();
        var local = Plan(id, "Local", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var older = Plan(id, "Remote", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal("Local", CloudSync.Merge(Data(local), Data(older)).Plans[0].Name);

        var same = Plan(id, "Remote", local.UpdatedAt);
        Assert.Equal("Remote", CloudSync.Merge(Data(local), Data(same)).Plans[0].Name);
    }

    [Fact]
    public void OnePrimarySurvivesPerTerm()
    {
        var older = Plan(Guid.NewGuid(), "Older", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), primary: true);
        var newer = Plan(Guid.NewGuid(), "Newer", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), primary: true);
        var merged = CloudSync.Merge(Data(older), Data(newer));
        Assert.Equal("Newer", Assert.Single(merged.Plans, plan => plan.IsPrimary).Name);
    }

    [Fact]
    public void DegreeStudentDetailsStayOnTheDevice()
    {
        var id = Guid.NewGuid();
        var local = new DegreePlan
        {
            Id = id,
            Name = "Local",
            StudentName = "Ada",
            StudentID = "001",
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var remote = new DegreePlan
        {
            Id = id,
            Name = "Remote",
            StudentName = "Other",
            StudentID = "999",
            Program = "CS",
            UpdatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var merged = CloudSync.Merge(new UserData { DegreePlans = [local] }, new UserData { DegreePlans = [remote] });
        var plan = Assert.Single(merged.DegreePlans);
        Assert.Equal("Remote", plan.Name);
        Assert.Equal("CS", plan.Program);
        Assert.Equal("Ada", plan.StudentName);
        Assert.Equal("001", plan.StudentID);
    }

    [Fact]
    public void WatchSeatChecksStayOnTheDevice()
    {
        var local = Watch("12345", seats: 4, added: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        local.LastCheckedAt = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var remote = Watch("12345", seats: 0, added: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        remote.NotifyOnWaitlist = true;
        remote.CourseTitle = "Updated";
        var merged = CloudSync.Merge(new UserData { Watchlist = [local] }, new UserData { Watchlist = [remote] });
        var item = Assert.Single(merged.Watchlist);
        Assert.Equal("Updated", item.CourseTitle);
        Assert.True(item.NotifyOnWaitlist);
        Assert.Equal(4, item.LastSeatsRemaining);
        Assert.Equal(local.LastCheckedAt, item.LastCheckedAt);
    }

    [Fact]
    public void PreferencesMergeFieldByFieldAndDeviceFieldsStay()
    {
        var local = new UserData
        {
            Preferences = new AppPreferences
            {
                PreferredCampus = "Boston",
                SelectedTermCode = "202710",
                HasCompletedOnboarding = true,
                ShowWeekends = false,
                CalendarStartHour = 8,
                CalendarEndHour = 21,
                SeatCheckMinutes = 30,
            },
            SearchHistory = ["algorithms"],
            Recents = [new RecentCourse { TermCode = "202710", Key = new CourseKey("CS", "3000") }],
        };
        var remote = new UserData
        {
            Preferences = new AppPreferences
            {
                PreferredCampus = null,
                SelectedTermCode = "202630",
                HasCompletedOnboarding = false,
                ShowWeekends = true,
                CalendarStartHour = 9,
                CalendarEndHour = 18,
                SeatCheckMinutes = 5,
            },
            SearchHistory = ["other"],
        };
        var merged = CloudSync.Merge(local, remote);
        Assert.Equal("Boston", merged.Preferences.PreferredCampus);
        Assert.True(merged.Preferences.ShowWeekends);
        Assert.Equal(9, merged.Preferences.CalendarStartHour);
        Assert.Equal(18, merged.Preferences.CalendarEndHour);
        Assert.Equal("202710", merged.Preferences.SelectedTermCode);
        Assert.True(merged.Preferences.HasCompletedOnboarding);
        Assert.Equal(30, merged.Preferences.SeatCheckMinutes);
        Assert.Equal(["algorithms"], merged.SearchHistory);
        Assert.Single(merged.Recents);
    }

    [Fact]
    public void InvalidRemoteCalendarHoursFallBack()
    {
        var remote = new UserData { Preferences = new AppPreferences { CalendarStartHour = 20, CalendarEndHour = 8, ShowWeekends = true } };
        var merged = CloudSync.Merge(new UserData(), remote);
        Assert.Equal(8, merged.Preferences.CalendarStartHour);
        Assert.Equal(21, merged.Preferences.CalendarEndHour);
        Assert.True(merged.Preferences.ShowWeekends);
    }

    private static UserData Data(SchedulePlan plan) => new() { Plans = [plan] };

    private static SchedulePlan Plan(Guid id, string name, DateTimeOffset updated, bool primary = false) => new()
    {
        Id = id,
        Name = name,
        TermCode = "202710",
        UpdatedAt = updated,
        IsPrimary = primary,
    };

    private static WatchItem Watch(string crn, int seats, DateTimeOffset added) => new()
    {
        TermCode = "202710",
        Crn = crn,
        CourseKey = new CourseKey("CS", "3000"),
        CourseTitle = "Algorithms",
        LastSeatsRemaining = seats,
        AddedAt = added,
    };
}
