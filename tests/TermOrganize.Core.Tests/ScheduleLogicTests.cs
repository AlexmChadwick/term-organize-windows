// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// Portions derived from SearchNEU where the Swift file says so. Modified 2026-10-13: ported from Swift to C#.

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

file static class ScheduleFixture
{
    public static CourseSection Section(
        string crn, string subject, string number, int[] days, double start, double end,
        int seats = 10, int capacity = 40, int waitlist = 0, string building = "WVH", string campus = "Boston",
        string method = "Traditional", string[]? instructors = null,
        string startDate = "2026-09-09", string endDate = "2026-12-13") => new()
    {
        Crn = crn,
        Subject = subject,
        Number = number,
        Title = $"{subject} {number}",
        SectionNumber = "01",
        PartOfTerm = "1",
        SeatCapacity = capacity,
        SeatsRemaining = seats,
        Enrollment = capacity - seats,
        WaitlistCapacity = waitlist,
        WaitlistRemaining = waitlist,
        ClassType = "Lecture",
        Campus = campus,
        InstructionMethod = method,
        Instructors = instructors?.Select(name => new Instructor { Name = name, Primary = true }).ToList(),
        Meetings =
        [
            new Meeting
            {
                Days = DaySet.FromIndices(days),
                Start = (int)(start * 60),
                End = (int)(end * 60),
                BuildingCode = building,
                Building = $"{building} Hall",
                Room = "210",
                CampusCode = "BOS",
                Kind = "Class",
                StartDate = startDate,
                EndDate = endDate,
            },
        ],
    };

    public static ScheduleCourseInput Course(string subject, string number, bool required, double credits, params CourseSection[] sections) => new()
    {
        Key = new CourseKey(subject, number),
        IsRequired = required,
        Sections = sections,
        MinCredits = credits,
        MaxCredits = credits,
    };

    public static ScheduleCourseInput Course(string subject, string number, params CourseSection[] sections) =>
        Course(subject, number, true, 4, sections);

    public static ScheduleGenerationResult Generate(
        IReadOnlyList<ScheduleCourseInput> courses, SchedulePreferences? preferences = null,
        ScheduleFilters? filters = null, ScheduleSortOption sort = ScheduleSortOption.Best,
        ScheduleLimits? limits = null) =>
        ScheduleGenerator.Generate(courses, filters ?? new ScheduleFilters(), preferences ?? new SchedulePreferences(), sort, limits);

    public static void AssertCrnSets(ScheduleGenerationResult result, params string[][] expected)
    {
        var actual = result.Schedules.Select(Canonical).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var want = expected.Select(Canonical).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Assert.Equal(want, actual);
    }

    public static string Canonical(IEnumerable<string> crns) => string.Join(",", crns.OrderBy(c => c, StringComparer.Ordinal));
    public static string Canonical(GeneratedSchedule schedule) => Canonical(schedule.Crns);
}

public class ScheduleLogicTests
{
    private static Meeting MeetingAt(int[] days, int start, int end, string? from, string? to) => new()
    {
        Days = DaySet.FromIndices(days),
        Start = start,
        End = end,
        BuildingCode = "RI",
        Building = "Richards Hall",
        Room = "300",
        CampusCode = "BOS",
        Kind = "Class",
        StartDate = from,
        EndDate = to,
    };

    [Fact]
    public void HalfTermSectionsDoNotConflict()
    {
        var firstHalf = MeetingAt([1, 3], 600, 665, "2026-09-09", "2026-10-27");
        var secondHalf = MeetingAt([1, 3], 600, 665, "2026-10-28", "2026-12-13");
        var fullTerm = MeetingAt([3], 630, 700, "2026-09-09", "2026-12-13");
        Assert.False(firstHalf.Overlaps(secondHalf));
        Assert.True(firstHalf.Overlaps(fullTerm));
        Assert.True(secondHalf.Overlaps(fullTerm));
        Assert.True(firstHalf.Overlaps(MeetingAt([1], 640, 700, null, null)));
    }

    [Fact]
    public void CalendarConflictsRespectDates()
    {
        var color = CourseColor.At(0);
        var a = new CalendarEvent
        {
            Id = "a", Title = "A", Day = 1, Start = 600, End = 665, Color = color, Payload = "1",
            StartDate = "2026-09-09", EndDate = "2026-10-27",
        };
        var b = new CalendarEvent
        {
            Id = "b", Title = "B", Day = 1, Start = 600, End = 665, Color = color, Payload = "2",
            StartDate = "2026-10-28", EndDate = "2026-12-13",
        };
        var c = new CalendarEvent { Id = "c", Title = "C", Day = 1, Start = 630, End = 700, Color = color, Payload = "3" };
        Assert.All(CalendarEvent.MarkingConflicts([a, b, c]), item => Assert.True(item.IsConflict));
        Assert.DoesNotContain(CalendarEvent.MarkingConflicts([a, b]), item => item.IsConflict);
    }

    [Fact]
    public void TermWindowsAreSensible()
    {
        var fall = TermCalendar.Window(new Term("202710", "Fall 2026 Semester"));
        var spring = TermCalendar.Window(new Term("202730", "Spring 2027 Semester"));
        Assert.Equal(2026, fall.Start.Year);
        Assert.Equal(9, fall.Start.Month);
        Assert.Equal(2027, spring.Start.Year);
        Assert.True(fall.End < spring.Start);
    }
}

public class SchedulePreferencesFilterTests
{
    [Fact]
    public void ABlockedTimeRemovesSectionsThatOverlapIt()
    {
        var course = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("EVE", "CS", "3000", [2, 4], 18, 19.5),
            ScheduleFixture.Section("DAY", "CS", "3000", [1, 3, 5], 10, 11),
            ScheduleFixture.Section("LATE", "CS", "3000", [2, 4], 20, 21));
        var preferences = new SchedulePreferences
        {
            BlockedTimes = [new BlockedTime { Label = "Club meeting", Days = DaySet.FromIndices([2, 4]), Start = 18 * 60, End = 20 * 60 }],
        };
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate([course], preferences), ["DAY"], ["LATE"]);
        Assert.Equal(3, ScheduleFixture.Generate([course]).TotalFound);
    }

    [Fact]
    public void BlockedTimesOnlyApplyOnTheirDaysAndHours()
    {
        var course = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("MON", "CS", "3000", [1], 18, 19.5),
            ScheduleFixture.Section("TUE", "CS", "3000", [2], 18, 19.5),
            ScheduleFixture.Section("EARLY", "CS", "3000", [2], 16, 18),
            ScheduleFixture.Section("OVER", "CS", "3000", [2], 19.5, 21));
        var preferences = new SchedulePreferences
        {
            BlockedTimes = [new BlockedTime { Label = "Club", Days = DaySet.FromIndices([2]), Start = 18 * 60, End = 20 * 60 }],
        };
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate([course], preferences), ["MON"], ["EARLY"]);
    }

    [Fact]
    public void ASwitchedOffBlockIsIgnored()
    {
        var course = ScheduleFixture.Course("CS", "3000", ScheduleFixture.Section("A", "CS", "3000", [2], 18, 19));
        var preferences = new SchedulePreferences
        {
            BlockedTimes = [new BlockedTime { Label = "Club", Days = DaySet.FromIndices([2]), Start = 18 * 60, End = 20 * 60, IsEnabled = false }],
        };
        Assert.Equal(1, ScheduleFixture.Generate([course], preferences).TotalFound);
    }

    [Fact]
    public void APreferredBlockOnlyRanksLower()
    {
        var course = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("CLASH", "CS", "3000", [2], 18, 19),
            ScheduleFixture.Section("FREE", "CS", "3000", [3], 18, 19));
        var preferences = new SchedulePreferences
        {
            BlockedTimes = [new BlockedTime { Label = "Club", Days = DaySet.FromIndices([2]), Start = 18 * 60, End = 20 * 60, Strength = PreferenceStrength.Preferred }],
        };
        var result = ScheduleFixture.Generate([course], preferences);
        Assert.Equal(["FREE"], result.Schedules[0].Crns);
        Assert.Equal(["CLASH"], result.Schedules[1].Crns);
        Assert.Contains(result.Schedules[^1].Reasons, reason => reason.IsCaveat && reason.Text == "Overlaps Club");
    }

    [Fact]
    public void DaysOffAreHardOnlyWhenRequired()
    {
        var course = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("FRI", "CS", "3000", [5], 10, 11),
            ScheduleFixture.Section("TUE", "CS", "3000", [2], 10, 11));
        var preferences = new SchedulePreferences { DaysOff = DaySet.Friday, DaysOffStrength = PreferenceStrength.Required };
        Assert.Equal(["TUE"], ScheduleFixture.Generate([course], preferences).Schedules.Single().Crns);
        preferences.DaysOffStrength = PreferenceStrength.Preferred;
        Assert.Equal(["TUE", "FRI"], ScheduleFixture.Generate([course], preferences).Schedules.Select(s => s.Crns[0]).ToArray());
    }

    [Fact]
    public void EarliestStartAndLatestEndFilterWhenRequired()
    {
        var course = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("EARLY", "CS", "3000", [1], 8, 9.5),
            ScheduleFixture.Section("MID", "CS", "3000", [2], 10, 11.5),
            ScheduleFixture.Section("LATE", "CS", "3000", [3], 16, 18.5));
        var preferences = new SchedulePreferences { EarliestStart = new TimeLimit(10 * 60) };
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate([course], preferences), ["MID"], ["LATE"]);
        preferences.LatestEnd = new TimeLimit(17 * 60);
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate([course], preferences), ["MID"]);
    }

    [Fact]
    public void SoftLimitsRankByHowFarOutsideTheyAre()
    {
        var course = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("VERY", "CS", "3000", [1], 8, 9),
            ScheduleFixture.Section("SLIGHTLY", "CS", "3000", [2], 9.75, 11),
            ScheduleFixture.Section("OK", "CS", "3000", [3], 10, 11));
        var preferences = new SchedulePreferences { EarliestStart = new TimeLimit(10 * 60, PreferenceStrength.Preferred) };
        Assert.Equal(["OK", "SLIGHTLY", "VERY"], ScheduleFixture.Generate([course], preferences).Schedules.Select(s => s.Crns[0]).ToArray());
    }

    [Fact]
    public void RequiredFormatSeatsAndCampusFilter()
    {
        var course = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("INPERSON", "CS", "3000", [1], 9, 10),
            ScheduleFixture.Section("HYBRID", "CS", "3000", [2], 9, 10, method: "Hybrid"),
            ScheduleFixture.Section("ONLINE", "CS", "3000", [3], 9, 10, campus: "Online", method: "Online"),
            ScheduleFixture.Section("FULL", "CS", "3000", [4], 9, 10, seats: 0, waitlist: 5),
            ScheduleFixture.Section("FULLNOLIST", "CS", "3000", [5], 9, 10, seats: 0),
            ScheduleFixture.Section("OAKLAND", "CS", "3000", [1], 12, 13, campus: "Oakland, CA"));
        var preferences = new SchedulePreferences { Format = FormatPreference.InPerson, FormatStrength = PreferenceStrength.Required };
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate([course], preferences), ["INPERSON"], ["FULL"], ["FULLNOLIST"], ["OAKLAND"]);
        preferences.Seats = SeatPolicy.OpenOnly;
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate([course], preferences), ["INPERSON"], ["OAKLAND"]);
        preferences.Seats = SeatPolicy.WaitlistOK;
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate([course], preferences), ["INPERSON"], ["FULL"], ["OAKLAND"]);
        preferences.Campus = "Boston";
        preferences.CampusStrength = PreferenceStrength.Required;
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate([course], preferences), ["INPERSON"], ["FULL"]);
    }

    [Fact]
    public void SectionsWithoutASeatLimitAreNeverFull()
    {
        var course = ScheduleFixture.Course("CS", "3000", ScheduleFixture.Section("OPEN", "CS", "3000", [1], 9, 10, seats: 0, capacity: 0));
        var preferences = new SchedulePreferences { Seats = SeatPolicy.OpenOnly };
        Assert.Equal(1, ScheduleFixture.Generate([course], preferences).TotalFound);
    }

    [Fact]
    public void MaxClassesPerDayLimitsHowManySectionsShareADay()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 8, 9)),
            ScheduleFixture.Course("B", "1", ScheduleFixture.Section("B1", "B", "1", [1], 10, 11), ScheduleFixture.Section("B2", "B", "1", [2], 10, 11)),
            ScheduleFixture.Course("C", "1", ScheduleFixture.Section("C1", "C", "1", [1], 12, 13), ScheduleFixture.Section("C2", "C", "1", [3], 12, 13)),
        };
        Assert.Equal(4, ScheduleFixture.Generate(courses).TotalFound);
        var preferences = new SchedulePreferences { MaxClassesPerDay = 2 };
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate(courses, preferences), ["A1", "B1", "C2"], ["A1", "B2", "C1"], ["A1", "B2", "C2"]);
        preferences.MaxClassesPerDay = 1;
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate(courses, preferences), ["A1", "B2", "C2"]);
    }

    [Fact]
    public void MaxMinutesPerDayLimitsClassTimeOnOneDay()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 11)),
            ScheduleFixture.Course("B", "1", ScheduleFixture.Section("B1", "B", "1", [1], 13, 15), ScheduleFixture.Section("B2", "B", "1", [2], 13, 15)),
        };
        var preferences = new SchedulePreferences { MaxMinutesPerDay = 3 * 60 };
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate(courses, preferences), ["A1", "B2"]);
        preferences.MaxMinutesPerDay = 90;
        var blocked = ScheduleFixture.Generate(courses, preferences);
        Assert.Equal(0, blocked.TotalFound);
        Assert.Equal(1, blocked.Diagnostics.Courses[0].Rejections[ScheduleFilterReason.PrefWorkload]);
    }

    [Fact]
    public void RequiredWalkingTimeOnlyMattersBetweenBuildings()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 10, building: "RY")),
            ScheduleFixture.Course("B", "1",
                ScheduleFixture.Section("SAME", "B", "1", [1], 10.1, 11, building: "RY"),
                ScheduleFixture.Section("NEAR", "B", "1", [1], 10.1, 11, building: "SL"),
                ScheduleFixture.Section("FAR", "B", "1", [1], 10.5, 11.5, building: "SL"),
                ScheduleFixture.Section("OTHERDAY", "B", "1", [2], 10.1, 11, building: "SL")),
        };
        var preferences = new SchedulePreferences
        {
            Breaks = BreakPreference.AtLeast,
            MinimumBreakMinutes = 15,
            MinimumBreakStrength = PreferenceStrength.Required,
        };
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate(courses, preferences), ["A1", "SAME"], ["A1", "FAR"], ["A1", "OTHERDAY"]);
        Assert.Empty(ScheduleFixture.Generate(courses, preferences).Diagnostics.ConflictingPairs);
    }

    [Fact]
    public void ATightWalkIsReportedWhenItIsTheOnlyProblem()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 10, building: "RY")),
            ScheduleFixture.Course("B", "1", ScheduleFixture.Section("B1", "B", "1", [1], 10.1, 11, building: "SL")),
        };
        var preferences = new SchedulePreferences
        {
            Breaks = BreakPreference.AtLeast,
            MinimumBreakMinutes = 15,
            MinimumBreakStrength = PreferenceStrength.Required,
        };
        var result = ScheduleFixture.Generate(courses, preferences);
        Assert.Equal(0, result.TotalFound);
        Assert.Equal([ScheduleDiagnostics.ConflictCause.TravelTime], result.Diagnostics.ConflictingPairs.Select(p => p.Reason).ToArray());
        preferences.MinimumBreakStrength = PreferenceStrength.Preferred;
        var relaxed = ScheduleFixture.Generate(courses, preferences);
        Assert.Equal(1, relaxed.TotalFound);
        Assert.Contains(relaxed.Schedules[0].Reasons, reason => reason.IsCaveat && reason.Text == "Tight between buildings");
    }

    [Fact]
    public void HalfTermSectionsAtTheSameTimeStillDoNotConflictWithPreferences()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1, 3], 10, 11.5, startDate: "2026-09-09", endDate: "2026-10-23")),
            ScheduleFixture.Course("B", "1", ScheduleFixture.Section("B1", "B", "1", [1, 3], 10, 11.5, startDate: "2026-10-26", endDate: "2026-12-13")),
        };
        var preferences = new SchedulePreferences
        {
            MaxClassesPerDay = 3,
            Breaks = BreakPreference.AtLeast,
            MinimumBreakStrength = PreferenceStrength.Required,
        };
        preferences[TimeBand.Morning] = TimePreference.Prefer;
        Assert.Equal(1, ScheduleFixture.Generate(courses, preferences).TotalFound);
        Assert.Equal(1, ScheduleFixture.Generate(courses).TotalFound);
    }

    [Fact]
    public void RequiredCoursesAreStillRequired()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 10)),
            ScheduleFixture.Course("B", "1", ScheduleFixture.Section("B1", "B", "1", [1], 9, 10)),
        };
        var preferences = new SchedulePreferences();
        preferences[TimeBand.Morning] = TimePreference.Prefer;
        Assert.Equal(0, ScheduleFixture.Generate(courses, preferences).TotalFound);
    }

    [Fact]
    public void CreditTargetsReplaceTheMaximalRule()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 10)),
            ScheduleFixture.Course("B", "1", false, 4, ScheduleFixture.Section("B1", "B", "1", [2], 9, 10)),
            ScheduleFixture.Course("C", "1", false, 4, ScheduleFixture.Section("C1", "C", "1", [3], 9, 10)),
        };
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate(courses), ["A1", "B1", "C1"]);
        var preferences = new SchedulePreferences { CreditTarget = new CreditTarget(8, 8) };
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate(courses, preferences), ["A1", "B1"], ["A1", "C1"]);
        preferences.CreditTarget = new CreditTarget(4, 8);
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate(courses, preferences), ["A1"], ["A1", "B1"], ["A1", "C1"]);
        preferences.CreditTarget = new CreditTarget(12, 16);
        ScheduleFixture.AssertCrnSets(ScheduleFixture.Generate(courses, preferences), ["A1", "B1", "C1"]);
    }

    [Fact]
    public void ImpossibleCreditTargetsAreExplained()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 10)),
            ScheduleFixture.Course("B", "1", false, 4, ScheduleFixture.Section("B1", "B", "1", [2], 9, 10)),
        };
        var preferences = new SchedulePreferences { CreditTarget = new CreditTarget(2, 3) };
        var over = Assert.IsType<ScheduleDiagnostics.CountProblem.CreditsOverTarget>(ScheduleFixture.Generate(courses, preferences).Diagnostics.CountProblemKind);
        Assert.Equal(4, over.Required);
        Assert.Equal(3, over.Limit);
        preferences.CreditTarget = new CreditTarget(12, 14);
        var under = Assert.IsType<ScheduleDiagnostics.CountProblem.CreditsUnderTarget>(ScheduleFixture.Generate(courses, preferences).Diagnostics.CountProblemKind);
        Assert.Equal(8, under.Available);
        Assert.Equal(12, under.Wanted);
    }

    [Fact]
    public void DiagnosticsNameThePreferenceThatRemovedSections()
    {
        var course = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("A", "CS", "3000", [2], 18, 19),
            ScheduleFixture.Section("B", "CS", "3000", [4], 18.5, 19.5));
        var preferences = new SchedulePreferences
        {
            BlockedTimes = [new BlockedTime { Label = "Club", Days = DaySet.FromIndices([2, 4]), Start = 18 * 60, End = 20 * 60 }],
        };
        var result = ScheduleFixture.Generate([course], preferences);
        Assert.Equal(0, result.TotalFound);
        var report = result.Diagnostics.Courses[0];
        Assert.True(report.IsBlocked);
        Assert.Equal(2, report.Rejections[ScheduleFilterReason.BlockedTime]);
    }

    [Fact]
    public void RelaxationsOfferToSoftenWhatIsInTheWay()
    {
        var course = ScheduleFixture.Course("CS", "3000", ScheduleFixture.Section("A", "CS", "3000", [2], 18, 19));
        var block = new BlockedTime { Label = "Club", Days = DaySet.FromIndices([2]), Start = 18 * 60, End = 20 * 60 };
        var preferences = new SchedulePreferences
        {
            BlockedTimes = [block],
            EarliestStart = new TimeLimit(20 * 60),
        };
        Assert.Empty(ScheduleGenerator.SuggestRelaxations([course], new ScheduleFilters(), preferences));

        preferences.EarliestStart = null;
        var single = ScheduleGenerator.SuggestRelaxations([course], new ScheduleFilters(), preferences);
        var change = Assert.IsType<ScheduleRelaxation.PreferenceChange>(Assert.Single(single).Relaxation);
        var soften = Assert.IsType<PreferenceRelaxation.SoftenBlockedTime>(change.Relaxation);
        Assert.Equal(block.Id, soften.BlockId);
        Assert.Equal("Club", soften.Label);

        var relaxed = preferences.Clone();
        single[0].Relaxation.Apply(relaxed);
        Assert.Equal(PreferenceStrength.Preferred, relaxed.BlockedTimes[0].Strength);
        Assert.Equal(1, ScheduleFixture.Generate([course], relaxed).TotalFound);
    }
}

public class SchedulePreferencesRankingTests
{
    private static ScheduleCourseInput MorningOrNight() => ScheduleFixture.Course("CS", "3000",
        ScheduleFixture.Section("NIGHT", "CS", "3000", [2, 4], 19, 20.5),
        ScheduleFixture.Section("MORNING", "CS", "3000", [2, 4], 9.5, 11),
        ScheduleFixture.Section("AFTERNOON", "CS", "3000", [2, 4], 14.5, 16));

    [Fact]
    public void WithoutPreferencesTheDefaultOrderIsUnchanged()
    {
        var result = ScheduleFixture.Generate([MorningOrNight()]);
        Assert.Equal("NIGHT", result.Schedules[^1].Crns[0]);
        Assert.All(result.Schedules, schedule => Assert.Empty(schedule.Reasons));
    }

    [Fact]
    public void AMorningPersonSeesMorningsFirstAndANightOwlSeesNightsFirst()
    {
        var morning = SchedulePreset.MorningPerson.Toggled(new SchedulePreferences());
        Assert.Equal(["MORNING", "AFTERNOON", "NIGHT"], ScheduleFixture.Generate([MorningOrNight()], morning).Schedules.Select(s => s.Crns[0]).ToArray());
        var night = SchedulePreset.NightOwl.Toggled(new SchedulePreferences());
        var order = ScheduleFixture.Generate([MorningOrNight()], night).Schedules.Select(s => s.Crns[0]).ToArray();
        Assert.Equal("NIGHT", order[0]);
        Assert.Equal("MORNING", order[^1]);
    }

    [Fact]
    public void AvoidingEarlyMorningsPutsThemLast()
    {
        var course = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("EIGHT", "CS", "3000", [1, 3, 5], 8, 9),
            ScheduleFixture.Section("NOON", "CS", "3000", [1, 3, 5], 12, 13));
        var preferences = new SchedulePreferences();
        preferences[TimeBand.EarlyMorning] = TimePreference.Avoid;
        Assert.Equal(["NOON", "EIGHT"], ScheduleFixture.Generate([course], preferences).Schedules.Select(s => s.Crns[0]).ToArray());

        var eightOnly = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("EIGHT", "CS", "3000", [2], 8, 9),
            ScheduleFixture.Section("NINEFIFTEEN", "CS", "3000", [2, 4, 5], 9.25, 10.25));
        preferences = new SchedulePreferences();
        preferences[TimeBand.EarlyMorning] = TimePreference.Prefer;
        Assert.Equal(["EIGHT"], ScheduleFixture.Generate([eightOnly], preferences).Schedules[0].Crns);
    }

    [Fact]
    public void AWishedForDayOffBeatsEverythingElseTheDefaultRankingLikes()
    {
        var course = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("ONEDAY", "CS", "3000", [5], 9, 10),
            ScheduleFixture.Section("TWODAYS", "CS", "3000", [2, 4], 9, 10));
        Assert.Equal(["ONEDAY"], ScheduleFixture.Generate([course]).Schedules[0].Crns);
        var preferences = new SchedulePreferences { DaysOff = DaySet.Friday };
        Assert.Equal(["TWODAYS"], ScheduleFixture.Generate([course], preferences).Schedules[0].Crns);
    }

    [Fact]
    public void FewerDaysAndSpreadOutPullInOppositeDirections()
    {
        var course = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("TWO", "CS", "3000", [2, 4], 9, 11),
            ScheduleFixture.Section("FIVE", "CS", "3000", [1, 2, 3, 4, 5], 9, 9.8));
        Assert.Equal(["TWO"], ScheduleFixture.Generate([course], new SchedulePreferences { DayLayout = DayLayout.FewerDays }).Schedules[0].Crns);
        Assert.Equal(["FIVE"], ScheduleFixture.Generate([course], new SchedulePreferences { DayLayout = DayLayout.SpreadOut }).Schedules[0].Crns);
    }

    [Fact]
    public void ProtectingLunchRanksFreeLunchesFirst()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1, 3], 9, 10.5)),
            ScheduleFixture.Course("B", "1",
                ScheduleFixture.Section("BUSY", "B", "1", [1, 3], 11.75, 13.75),
                ScheduleFixture.Section("FREE", "B", "1", [1, 3], 14, 15.5)),
        };
        var preferences = new SchedulePreferences { Lunch = new LunchBreak { Start = 11 * 60 + 30, End = 14 * 60, Minutes = 45 } };
        var result = ScheduleFixture.Generate(courses, preferences);
        Assert.Equal(ScheduleFixture.Canonical(["A1", "FREE"]), ScheduleFixture.Canonical(result.Schedules[0]));
        Assert.Contains(result.Schedules[0].Reasons, reason => reason.Text == "Lunch free every day");
        Assert.Contains(result.Schedules[^1].Reasons, reason => reason.Text == "Lunch often busy");
    }

    [Fact]
    public void AvoidingBackToBackPrefersRealBreaks()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 10)),
            ScheduleFixture.Course("B", "1",
                ScheduleFixture.Section("TIGHT", "B", "1", [1], 10.1, 11),
                ScheduleFixture.Section("SPACED", "B", "1", [1], 11, 12)),
        };
        var preferences = new SchedulePreferences { Breaks = BreakPreference.AvoidBackToBack };
        Assert.Equal(ScheduleFixture.Canonical(["A1", "SPACED"]), ScheduleFixture.Canonical(ScheduleFixture.Generate(courses, preferences).Schedules[0]));
    }

    [Fact]
    public void KeepingGapsShortPrefersCompactDays()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 10)),
            ScheduleFixture.Course("B", "1",
                ScheduleFixture.Section("GAP", "B", "1", [1], 13, 14),
                ScheduleFixture.Section("NOGAP", "B", "1", [1], 10.25, 11.25)),
        };
        var preferences = new SchedulePreferences { Gaps = GapPreference.Compact };
        Assert.Equal(ScheduleFixture.Canonical(["A1", "NOGAP"]), ScheduleFixture.Canonical(ScheduleFixture.Generate(courses, preferences).Schedules[0]));
        preferences.Gaps = GapPreference.Fine;
        Assert.Equal(2, ScheduleFixture.Generate(courses, preferences).TotalFound);
    }

    [Fact]
    public void PreferredAndAvoidedInstructorsMoveSchedules()
    {
        var course = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("NEUTRAL", "CS", "3000", [1], 9, 10, instructors: ["Lee, Kim"]),
            ScheduleFixture.Section("LOVED", "CS", "3000", [2], 9, 10, instructors: ["Smith, Alex"]),
            ScheduleFixture.Section("AVOIDED", "CS", "3000", [3], 9, 10, instructors: ["Jones, Sam"]));
        var preferences = new SchedulePreferences
        {
            PreferredInstructors = ["Smith, Alex"],
            AvoidedInstructors = ["jones, sam"],
        };
        var result = ScheduleFixture.Generate([course], preferences);
        Assert.Equal(["LOVED", "NEUTRAL", "AVOIDED"], result.Schedules.Select(s => s.Crns[0]).ToArray());
        Assert.Contains(result.Schedules[0].Reasons, reason => reason.Text == "With Alex Smith");
    }

    [Fact]
    public void PreferredFormatAndCampusRankWithoutRemoving()
    {
        var course = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("ONLINE", "CS", "3000", [1], 9, 10, campus: "Online", method: "Online"),
            ScheduleFixture.Section("INPERSON", "CS", "3000", [2], 9, 10),
            ScheduleFixture.Section("OAKLAND", "CS", "3000", [3], 9, 10, campus: "Oakland, CA"));
        var preferences = new SchedulePreferences { Format = FormatPreference.InPerson };
        Assert.Equal(["INPERSON"], ScheduleFixture.Generate([course], preferences).Schedules[0].Crns);
        Assert.Equal(3, ScheduleFixture.Generate([course], preferences).TotalFound);

        var inPerson = ScheduleFixture.Course("CS", "3000",
            ScheduleFixture.Section("BOSTON", "CS", "3000", [2], 9, 10),
            ScheduleFixture.Section("OAKLAND", "CS", "3000", [3], 9, 10, campus: "Oakland, CA"));
        var campus = new SchedulePreferences { Campus = "Oakland, CA" };
        Assert.Equal(["OAKLAND", "BOSTON"], ScheduleFixture.Generate([inPerson], campus).Schedules.Select(s => s.Crns[0]).ToArray());
        campus.Campus = "Boston";
        Assert.Equal(["BOSTON", "OAKLAND"], ScheduleFixture.Generate([inPerson], campus).Schedules.Select(s => s.Crns[0]).ToArray());
    }

    [Fact]
    public void ASearchThatRunsOutOfLeavesStillMeetsTheBestSectionsFirst()
    {
        CourseSection Night(string crn, string course, int day) => ScheduleFixture.Section(crn, course, "1", [day], 19, 20);
        var a = ScheduleFixture.Course("A", "1", Enumerable.Range(1, 8).Select(n => Night($"A{n}", "A", 1)).Append(ScheduleFixture.Section("AMORNING", "A", "1", [1], 9, 10)).ToArray());
        var b = ScheduleFixture.Course("B", "1", Enumerable.Range(1, 8).Select(n => Night($"B{n}", "B", 2)).Append(ScheduleFixture.Section("BMORNING", "B", "1", [2], 9, 10)).ToArray());
        var preferences = new SchedulePreferences();
        preferences[TimeBand.Morning] = TimePreference.Prefer;
        preferences[TimeBand.Night] = TimePreference.Avoid;
        var tiny = new ScheduleLimits
        {
            MaxResults = 100,
            MaxExhaustiveLeaves = 3,
            MaxNodes = 1_000_000,
            TimeBudget = 5,
            SampleLeaves = 0,
            SampleTimeBudget = 0,
            AllowSampling = false,
        };
        var result = ScheduleFixture.Generate([a, b], preferences, limits: tiny);
        Assert.False(result.IsExhaustive);
        Assert.Equal(ScheduleFixture.Canonical(["AMORNING", "BMORNING"]), ScheduleFixture.Canonical(result.Schedules[0]));
    }

    [Fact]
    public void OtherSortOrdersStillWorkWithPreferences()
    {
        var preferences = new SchedulePreferences();
        preferences[TimeBand.Morning] = TimePreference.Prefer;
        var result = ScheduleFixture.Generate([MorningOrNight()], preferences, sort: ScheduleSortOption.LatestStart);
        Assert.Equal(["NIGHT"], result.Schedules[0].Crns);
    }

    [Fact]
    public void TheScoreOfADefaultScheduleMatchesTheOriginalFormula()
    {
        var sections = new[]
        {
            ScheduleFixture.Section("A1", "A", "1", [1, 3], 8, 9.5),
            ScheduleFixture.Section("B1", "B", "1", [2, 4], 13, 14.5),
            ScheduleFixture.Section("C1", "C", "1", [1], 17, 18.5, seats: 0),
        };
        var schedule = new GeneratedSchedule(sections);
        var intervals = sections.SelectMany(section => ScheduleInterval.IntervalsFor(section)).ToList();
        var measure = ScheduleMeasure.Measure(intervals);
        var expected = 100.0;
        expected -= 8.0 * measure.CampusDays;
        expected -= 2.0 * Math.Max(0, measure.ClassDays - measure.CampusDays);
        expected -= measure.GapMinutes / 10.0;
        if (measure.EarliestStart < 9 * 60) expected -= (9 * 60 - measure.EarliestStart) / 15.0;
        if (measure.LatestEnd > 17 * 60) expected -= (measure.LatestEnd - 17 * 60) / 20.0;
        expected -= 6.0;
        Assert.Equal(expected, schedule.Score);
        Assert.Equal(expected, ScheduleScoring.Score(measure, 1, 0));
    }

    [Fact]
    public void GeneratingWithEmptyPreferencesMatchesGeneratingWithout()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1, 3], 9, 10), ScheduleFixture.Section("A2", "A", "1", [2, 4], 11, 12)),
            ScheduleFixture.Course("B", "1", ScheduleFixture.Section("B1", "B", "1", [1, 3], 9.5, 11), ScheduleFixture.Section("B2", "B", "1", [5], 14, 17)),
        };
        var plain = ScheduleGenerator.Generate(courses, new ScheduleFilters());
        var withEmpty = ScheduleFixture.Generate(courses, new SchedulePreferences());
        Assert.Equal(plain.Schedules.Select(s => s.Crns.ToArray()), withEmpty.Schedules.Select(s => s.Crns.ToArray()));
        Assert.Equal(plain.Schedules.Select(s => s.Score), withEmpty.Schedules.Select(s => s.Score));
    }
}

public class ScheduleReasonTests
{
    [Fact]
    public void ReasonsAreEmptyWithoutPreferences()
    {
        var sections = new[] { ScheduleFixture.Section("A", "A", "1", [1], 9, 10) };
        Assert.Empty(SchedulePreferenceRules.Reasons(sections, new SchedulePreferences()));
    }

    [Fact]
    public void AScheduleThatMeetsTheWishesSaysSo()
    {
        var sections = new[]
        {
            ScheduleFixture.Section("A", "A", "1", [2, 4], 10.5, 12),
            ScheduleFixture.Section("B", "B", "1", [1, 3], 14, 15.5),
        };
        var preferences = new SchedulePreferences
        {
            DaysOff = DaySet.Friday,
            EarliestStart = new TimeLimit(10 * 60),
            Lunch = new LunchBreak(),
        };
        var reasons = SchedulePreferenceRules.Reasons(sections, preferences);
        Assert.Equal(["Fridays off", "No classes before 10 AM", "Lunch free every day"], reasons.Select(r => r.Text).ToArray());
        Assert.All(reasons, reason => Assert.False(reason.IsCaveat));
    }

    [Fact]
    public void WishesItMissesShowUpAsCaveats()
    {
        var sections = new[] { ScheduleFixture.Section("A", "A", "1", [5], 8, 9) };
        var preferences = new SchedulePreferences
        {
            DaysOff = DaySet.Friday,
            EarliestStart = new TimeLimit(10 * 60, PreferenceStrength.Preferred),
        };
        preferences[TimeBand.EarlyMorning] = TimePreference.Avoid;
        var reasons = SchedulePreferenceRules.Reasons(sections, preferences);
        Assert.DoesNotContain(reasons, reason => !reason.IsCaveat);
        Assert.Equal(["Class on Fri", "Starts 8:00 AM"], reasons.Where(reason => reason.IsCaveat).Select(r => r.Text).ToArray());
        var all = SchedulePreferenceRules.Reasons(sections, preferences, maxCaveats: 5);
        Assert.Equal(["Class on Fri", "Starts 8:00 AM", "Includes early mornings"], all.Where(reason => reason.IsCaveat).Select(r => r.Text).ToArray());
    }

    [Fact]
    public void MorningsAndFormatsAreDescribed()
    {
        var sections = new[]
        {
            ScheduleFixture.Section("A", "A", "1", [1, 3], 9.5, 11),
            ScheduleFixture.Section("B", "B", "1", [2, 4], 10, 11.5),
        };
        var preferences = new SchedulePreferences { Format = FormatPreference.InPerson, DayLayout = DayLayout.FewerDays };
        preferences[TimeBand.Morning] = TimePreference.Prefer;
        preferences[TimeBand.Evening] = TimePreference.Avoid;
        var reasons = SchedulePreferenceRules.Reasons(sections, preferences).Select(r => r.Text).ToArray();
        Assert.Contains("All mornings", reasons);
        Assert.Contains("No evenings", reasons);
        Assert.Contains("All in person", reasons);
    }

    [Fact]
    public void FavoritesGetTheSameTreatment()
    {
        var sections = new[] { ScheduleFixture.Section("A", "A", "1", [2, 4], 11, 12.5) };
        var preferences = new SchedulePreferences { DaysOff = DaySet.Friday };
        Assert.Equal(["Fridays off"], new GeneratedSchedule(sections, preferences).Reasons.Select(r => r.Text).ToArray());
        Assert.Empty(new GeneratedSchedule(sections).Reasons);
    }

    [Fact]
    public void TheSameThingIsNeverSaidTwice()
    {
        var sections = new[] { ScheduleFixture.Section("A", "A", "1", [2, 4], 18, 19.5) };
        var preferences = new SchedulePreferences
        {
            BlockedTimes =
            [
                new BlockedTime { Label = "Club", Days = DaySet.FromIndices([2]), Start = 17 * 60, End = 19 * 60, Strength = PreferenceStrength.Preferred },
                new BlockedTime { Label = "Club", Days = DaySet.FromIndices([4]), Start = 17 * 60, End = 19 * 60, Strength = PreferenceStrength.Preferred },
            ],
        };
        var reasons = SchedulePreferenceRules.Reasons(sections, preferences);
        Assert.Equal(["Overlaps Club"], reasons.Select(r => r.Text).ToArray());
        Assert.Equal(reasons.Count, reasons.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void CampusesAreNamedPlainly()
    {
        var sections = new[] { ScheduleFixture.Section("A", "A", "1", [1], 9, 10, campus: "Oakland, CA") };
        var preferences = new SchedulePreferences { Campus = "Oakland, CA" };
        Assert.Equal(["Oakland, CA campus"], SchedulePreferenceRules.Reasons(sections, preferences).Select(r => r.Text).ToArray());
    }

    [Fact]
    public void CardTagsAreCapped()
    {
        var sections = new[] { ScheduleFixture.Section("A", "A", "1", [2, 4], 11, 12.5) };
        var preferences = new SchedulePreferences
        {
            DaysOff = DaySet.Friday,
            EarliestStart = new TimeLimit(600),
            LatestEnd = new TimeLimit(1_020),
            DayLayout = DayLayout.FewerDays,
            Lunch = new LunchBreak(),
            Format = FormatPreference.InPerson,
        };
        preferences[TimeBand.Morning] = TimePreference.Prefer;
        Assert.Equal(4, SchedulePreferenceRules.Reasons(sections, preferences).Count(reason => !reason.IsCaveat));
    }
}

public class ScheduleBlockerTests
{
    [Fact]
    public void FindsTheCourseThatAlwaysClashes()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 10)),
            ScheduleFixture.Course("B", "1", ScheduleFixture.Section("B1", "B", "1", [1], 9.5, 10.5)),
            ScheduleFixture.Course("C", "1", ScheduleFixture.Section("C1", "C", "1", [2], 9, 10)),
        };
        var result = ScheduleFixture.Generate(courses);
        Assert.Equal(0, result.TotalFound);
        var analysis = ScheduleGenerator.AnalyzeBlockers(courses, new ScheduleFilters(), diagnostics: result.Diagnostics);
        Assert.Equal(3, analysis.RequiredCount);
        Assert.Equal(["A 1", "B 1"], analysis.Options.Select(o => o.Courses[0].ToString()).OrderBy(s => s, StringComparer.Ordinal).ToArray());
        Assert.Equal(["A 1", "B 1"], analysis.AllBlockingCourses.Select(k => k.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToArray());
        Assert.Equal([new CourseKey("B", "1")], analysis.Clashes(new CourseKey("A", "1")));
        Assert.All(analysis.Options, option => Assert.Equal(1, option.ScheduleCount));
    }

    [Fact]
    public void ACourseWithNoSectionsAlwaysHasToGo()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 10)),
            ScheduleFixture.Course("B", "1"),
            ScheduleFixture.Course("C", "1", ScheduleFixture.Section("C1", "C", "1", [2], 9, 10)),
        };
        var analysis = ScheduleGenerator.AnalyzeBlockers(courses, new ScheduleFilters());
        Assert.Equal(["B 1"], analysis.NotOffered.Select(k => k.ToString()).ToArray());
        Assert.Equal(["B 1"], Assert.Single(analysis.Options).Courses.Select(k => k.ToString()).ToArray());
    }

    [Fact]
    public void CoursesTheRulesRuleOutAreReportedSeparately()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [5], 9, 10)),
            ScheduleFixture.Course("B", "1", ScheduleFixture.Section("B1", "B", "1", [2], 9, 10)),
        };
        var preferences = new SchedulePreferences { DaysOff = DaySet.Friday, DaysOffStrength = PreferenceStrength.Required };
        var analysis = ScheduleGenerator.AnalyzeBlockers(courses, new ScheduleFilters(), preferences);
        Assert.Equal(["A 1"], analysis.FilteredOut.Select(k => k.ToString()).ToArray());
        Assert.Empty(analysis.NotOffered);
        Assert.Equal(["A 1"], Assert.Single(analysis.Options).Courses.Select(k => k.ToString()).ToArray());
    }

    [Fact]
    public void LooksAtPairsWhenNoSingleCourseIsToBlame()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 10)),
            ScheduleFixture.Course("B", "1", ScheduleFixture.Section("B1", "B", "1", [1], 9, 10)),
            ScheduleFixture.Course("C", "1", ScheduleFixture.Section("C1", "C", "1", [2], 9, 10)),
            ScheduleFixture.Course("D", "1", ScheduleFixture.Section("D1", "D", "1", [2], 9, 10)),
        };
        var analysis = ScheduleGenerator.AnalyzeBlockers(courses, new ScheduleFilters());
        Assert.Equal(4, analysis.Options.Count);
        Assert.All(analysis.Options, option => Assert.Equal(2, option.Courses.Count));
        var sets = analysis.Options.Select(o => ScheduleFixture.Canonical(o.Courses.Select(k => k.ToString()))).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Assert.Equal(["A 1,C 1", "A 1,D 1", "B 1,C 1", "B 1,D 1"], sets);
    }

    [Fact]
    public void CountProblemsAreNotCourseProblems()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 10)),
            ScheduleFixture.Course("B", "1", ScheduleFixture.Section("B1", "B", "1", [2], 9, 10)),
        };
        var filters = new ScheduleFilters { CourseCount = 1 };
        var result = ScheduleGenerator.Generate(courses, filters);
        var analysis = ScheduleGenerator.AnalyzeBlockers(courses, filters, diagnostics: result.Diagnostics);
        Assert.Empty(analysis.Options);
    }

    [Fact]
    public void MakingTheBlockersOptionalProducesSchedules()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 10)),
            ScheduleFixture.Course("B", "1", ScheduleFixture.Section("B1", "B", "1", [1], 9.5, 10.5)),
        };
        var analysis = ScheduleGenerator.AnalyzeBlockers(courses, new ScheduleFilters());
        var drop = analysis.BlockingCourses.ToHashSet();
        var relaxed = courses.Select(input =>
        {
            var copy = input.Clone();
            if (drop.Contains(copy.Key)) copy.IsRequired = false;
            return copy;
        }).ToList();
        Assert.True(ScheduleGenerator.Generate(relaxed, new ScheduleFilters()).TotalFound > 0);
    }

    [Fact]
    public void NoResultsAnalysisCombinesBlockersAndRelaxations()
    {
        var courses = new[]
        {
            ScheduleFixture.Course("A", "1", ScheduleFixture.Section("A1", "A", "1", [1], 9, 10)),
            ScheduleFixture.Course("B", "1", ScheduleFixture.Section("B1", "B", "1", [1], 9.5, 10.5)),
        };
        var result = ScheduleFixture.Generate(courses);
        var analysis = ScheduleGenerator.AnalyzeEmptyResult(courses, new ScheduleFilters(), new SchedulePreferences(), result.Diagnostics);
        Assert.NotEmpty(analysis.Blockers.Options);
        Assert.Empty(analysis.Suggestions);
    }
}

public class ScheduleSearchAgainstBruteForceTests
{
    private struct SplitMix64
    {
        public ulong State;
        public SplitMix64(ulong state) => State = state;
        public ulong Next()
        {
            State += 0x9E3779B97F4A7C15;
            var z = State;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }

        public int NextInt(int upper)
        {
            if (upper <= 1) return 0;
            var bound = (ulong)upper;
            while (true)
            {
                var random = Next();
                var high = Math.BigMul(random, bound, out var low);
                if (low < bound)
                {
                    var threshold = (0UL - bound) % bound;
                    if (low < threshold) continue;
                }
                return (int)high;
            }
        }
    }

    [Fact]
    public void RandomPlansMatchAnObviouslyCorrectEnumeration()
    {
        var rng = new SplitMix64(20_261_004);
        var nonTrivial = 0;
        for (var round = 0; round < 400; round++)
        {
            var (courses, filters, preferences) = RandomInstance(ref rng);
            var expected = BruteForce(courses, filters, preferences);
            var result = ScheduleGenerator.Generate(courses, filters, preferences);
            Assert.True(result.IsExhaustive, $"Round {round} was not exhaustive");
            var actual = result.Schedules.Select(ScheduleFixture.Canonical).ToHashSet(StringComparer.Ordinal);
            if (!actual.SetEquals(expected))
            {
                var missing = expected.Except(actual).Take(3);
                var extra = actual.Except(expected).Take(3);
                Assert.Fail($"Round {round}: generator {actual.Count}, enumeration {expected.Count}. Missing: {string.Join(" | ", missing)}. Extra: {string.Join(" | ", extra)}.");
            }
            Assert.Equal(expected.Count, result.TotalFound);
            if (expected.Count > 0) nonTrivial++;
        }
        Assert.True(nonTrivial > 100);
    }

    private static (List<ScheduleCourseInput> Courses, ScheduleFilters Filters, SchedulePreferences Preferences) RandomInstance(ref SplitMix64 rng)
    {
        double[] starts = [8, 9.5, 11, 12.5, 14, 15.5, 17];
        string[] buildings = ["RY", "SL", "WVH"];
        int[][] dayChoices = [[1, 3], [2, 4], [1, 3, 5], [1], [2], [3], [4], [5], [2, 5]];
        (string, string)[] windows = [("2026-09-09", "2026-12-13"), ("2026-09-09", "2026-10-23"), ("2026-10-26", "2026-12-13")];
        double[] lengths = [0.75, 1.25, 1.5, 2];
        var courseCount = 2 + rng.NextInt(4);
        var courses = new List<ScheduleCourseInput>();
        for (var c = 0; c < courseCount; c++)
        {
            var sections = new List<CourseSection>();
            var sectionCount = 1 + rng.NextInt(4);
            for (var s = 0; s < sectionCount; s++)
            {
                var start = starts[rng.NextInt(starts.Length)];
                var window = windows[rng.NextInt(windows.Length)];
                sections.Add(ScheduleFixture.Section(
                    $"C{c}S{s}", $"C{c}", "1", dayChoices[rng.NextInt(dayChoices.Length)],
                    start, start + lengths[rng.NextInt(lengths.Length)],
                    building: buildings[rng.NextInt(buildings.Length)], startDate: window.Item1, endDate: window.Item2));
            }
            var credits = 1 + rng.NextInt(4);
            courses.Add(ScheduleFixture.Course($"C{c}", "1", rng.NextInt(2) == 0 || c == 0, credits, sections.ToArray()));
        }
        var filters = new ScheduleFilters();
        if (rng.NextInt(6) == 0) filters.CourseCount = 1 + rng.NextInt(courseCount);
        var preferences = new SchedulePreferences();
        if (rng.NextInt(2) == 0) preferences.MaxClassesPerDay = 1 + rng.NextInt(3);
        if (rng.NextInt(2) == 0) preferences.MaxMinutesPerDay = new[] { 90, 150, 210, 300 }[rng.NextInt(4)];
        if (rng.NextInt(2) == 0)
        {
            preferences.Breaks = BreakPreference.AtLeast;
            preferences.MinimumBreakMinutes = new[] { 10, 20, 40 }[rng.NextInt(3)];
            preferences.MinimumBreakStrength = PreferenceStrength.Required;
        }
        if (rng.NextInt(3) == 0)
        {
            var lower = 2 + rng.NextInt(9);
            preferences.CreditTarget = new CreditTarget(lower, lower + rng.NextInt(6));
        }
        if (rng.NextInt(3) == 0)
        {
            preferences.BlockedTimes =
            [
                new BlockedTime { Label = "Block", Days = DaySet.FromIndices(dayChoices[rng.NextInt(dayChoices.Length)]), Start = 11 * 60, End = 14 * 60 },
            ];
        }
        return (courses, filters, preferences);
    }

    private static HashSet<string> BruteForce(IReadOnlyList<ScheduleCourseInput> courses, ScheduleFilters filters, SchedulePreferences preferences)
    {
        var kept = courses.Select(course => course.Sections.Where(section =>
            ScheduleGenerator.RejectionReasons(section, filters, preferences, course.ExcludedCRNs) == 0).ToList()).ToList();
        var result = new HashSet<string>(StringComparer.Ordinal);
        int? target = filters.CourseCount is int count && count > 0 ? count : null;

        bool Fits(CourseSection section, List<CourseSection> picked)
        {
            foreach (var other in picked)
            {
                if (section.ConflictsWith(other)) return false;
                if (preferences.Breaks == BreakPreference.AtLeast && preferences.MinimumBreakStrength == PreferenceStrength.Required &&
                    SchedulePreferenceRules.TooClose(section, section.WeeklyMeetings.ToList(), other, other.WeeklyMeetings.ToList(), preferences.MinimumBreakMinutes))
                    return false;
            }
            var all = picked.Append(section).ToList();
            for (var day = 0; day < 7; day++)
            {
                var onDay = all.Where(item => item.MeetingDays.Contains(day)).ToList();
                if (preferences.MaxClassesPerDay is int classCap && onDay.Count > classCap) return false;
                if (preferences.MaxMinutesPerDay is int minuteCap)
                {
                    var minutes = 0;
                    foreach (var item in onDay)
                    {
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var meeting in item.WeeklyMeetings.Where(m => m.Days.Contains(day)))
                        {
                            var key = $"{meeting.Start}-{meeting.End}";
                            if (seen.Add(key)) minutes += (meeting.End ?? 0) - (meeting.Start ?? 0);
                        }
                    }
                    if (minutes > minuteCap) return false;
                }
            }
            return true;
        }

        void Finish(List<(int Course, CourseSection Section)> picks)
        {
            if (picks.Count == 0) return;
            if (target is int wanted && picks.Count != wanted) return;
            if (preferences.CreditTarget is CreditTarget credits)
            {
                var low = picks.Sum(pick => courses[pick.Course].WholeMinCredits);
                var high = picks.Sum(pick => courses[pick.Course].WholeMaxCredits);
                if (low > credits.Upper || high < credits.Lower) return;
            }
            else if (target is null)
            {
                var chosen = picks.Select(pick => pick.Course).ToHashSet();
                var sections = picks.Select(pick => pick.Section).ToList();
                for (var index = 0; index < courses.Count; index++)
                {
                    if (chosen.Contains(index)) continue;
                    if (kept[index].Any(section => Fits(section, sections))) return;
                }
            }
            result.Add(ScheduleFixture.Canonical(picks.Select(pick => pick.Section.Crn)));
        }

        void Walk(int index, List<(int Course, CourseSection Section)> picks)
        {
            if (index == courses.Count)
            {
                Finish(picks);
                return;
            }
            if (!courses[index].IsRequired) Walk(index + 1, picks);
            foreach (var section in kept[index])
            {
                if (!Fits(section, picks.Select(pick => pick.Section).ToList())) continue;
                var next = picks.ToList();
                next.Add((index, section));
                Walk(index + 1, next);
            }
        }

        Walk(0, []);
        return result;
    }
}

public class SchedulePreferencesCatalogTests
{
    [Fact]
    public void PreferencesRankAndFilterARealPlanQuickly()
    {
        var index = CatalogIndex.Build(BundledCatalog.Load());
        var courses = new[] { "CS 3000", "CS 3001", "MATH 2331", "PHYS 1151" }.Select(code =>
        {
            var key = CourseKey.Parse(code)!.Value;
            var course = index.Course(key);
            return new ScheduleCourseInput
            {
                Key = key,
                IsRequired = true,
                Sections = index.Sections(key),
                MinCredits = Math.Ceiling(course?.MinCredits ?? 0),
                MaxCredits = Math.Ceiling(course?.MaxCredits ?? 0),
            };
        }).ToList();
        var plain = ScheduleGenerator.Generate(courses, new ScheduleFilters());
        Assert.True(plain.TotalFound > 0);

        var preferences = SchedulePreset.LongWeekends.Toggled(new SchedulePreferences());
        preferences = SchedulePreset.MorningPerson.Toggled(preferences);
        preferences.BlockedTimes = [new BlockedTime { Label = "Club", Days = DaySet.FromIndices([2, 4]), Start = 18 * 60, End = 20 * 60 }];
        preferences.Lunch = new LunchBreak();
        preferences.MaxClassesPerDay = 3;
        var ranked = ScheduleGenerator.Generate(courses, new ScheduleFilters(), preferences);
        Assert.True(ranked.TotalFound <= plain.TotalFound);
        Assert.True(ranked.Elapsed < 10);
        var best = ranked.Schedules[0];
        foreach (var section in best.Sections)
        {
            foreach (var meeting in section.WeeklyMeetings)
            {
                var overlapsClub = !meeting.Days.IsDisjoint(DaySet.FromIndices([2, 4]))
                    && (meeting.Start ?? 0) < 20 * 60 && (meeting.End ?? 0) > 18 * 60;
                Assert.False(overlapsClub);
            }
        }
        Assert.NotEmpty(best.Reasons);
        var scores = ranked.Schedules.Select(s => s.Score).ToArray();
        Assert.Equal(scores.OrderByDescending(s => s), scores);
    }
}
