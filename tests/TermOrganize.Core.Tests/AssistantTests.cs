// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class AssistantTests
{
    [Fact]
    public void TimesAreReadTheWayAModelWritesThem()
    {
        Assert.Equal(18 * 60, ScheduleAssistant.Minutes("18:00"));
        Assert.Equal(18 * 60 + 30, ScheduleAssistant.Minutes("6:30 PM"));
        Assert.Equal(18 * 60, ScheduleAssistant.Minutes("6pm"));
        Assert.Equal(18 * 60, ScheduleAssistant.Minutes("6 p.m."));
        Assert.Equal(10 * 60, ScheduleAssistant.Minutes("10"));
        Assert.Equal(18 * 60, ScheduleAssistant.Minutes("1800"));
        Assert.Equal(9 * 60 + 30, ScheduleAssistant.Minutes("930"));
        Assert.Equal(0, ScheduleAssistant.Minutes("12am"));
        Assert.Equal(12 * 60, ScheduleAssistant.Minutes("12pm"));
        Assert.Equal(12 * 60, ScheduleAssistant.Minutes("noon"));
        Assert.Equal(24 * 60, ScheduleAssistant.Minutes("midnight"));
        Assert.Equal(24 * 60, ScheduleAssistant.Minutes("24:00"));
        Assert.Null(ScheduleAssistant.Minutes("25:00"));
        Assert.Null(ScheduleAssistant.Minutes("13pm"));
        Assert.Null(ScheduleAssistant.Minutes("soon"));
        Assert.Null(ScheduleAssistant.Minutes(""));
        Assert.Null(ScheduleAssistant.Minutes("7:75"));
    }

    [Fact]
    public void TheBriefsExampleBecomesTheExpectedSurvey()
    {
        var draft = new SchedulePreferencesDraft
        {
            EarliestStart = "10:00",
            DaysOff = [ScheduleDraftWeekday.Friday],
            WantsLunchBreak = true,
            BlockedTimes =
            [
                new ScheduleDraftBlockedTime
                {
                    Label = "Robotics club",
                    Days = [ScheduleDraftWeekday.Tuesday, ScheduleDraftWeekday.Thursday],
                    Start = "18:00",
                    End = "20:00",
                },
            ],
        };
        var applied = draft.Applied();
        Assert.Equal(new TimeLimit(600, PreferenceStrength.Required), applied.EarliestStart);
        Assert.Equal(DaySet.Friday, applied.DaysOff);
        Assert.Equal(PreferenceStrength.Preferred, applied.DaysOffStrength);
        Assert.NotNull(applied.Lunch);
        var block = Assert.Single(applied.BlockedTimes);
        Assert.Equal("Robotics club", block.Label);
        Assert.Equal(DaySet.Tuesday | DaySet.Thursday, block.Days);
        Assert.Equal(18 * 60, block.Start);
        Assert.Equal(20 * 60, block.End);
        Assert.Equal(PreferenceStrength.Required, block.Strength);
        Assert.Equal("Nothing before 10 AM · Fridays off · 1 blocked time · Lunch break", applied.Summary(4));
    }

    [Fact]
    public void InterpretingTheBriefsSentenceGroundsTheSameSurvey()
    {
        const string text = "No classes before 10, I have robotics club Tuesdays and Thursdays 6\u20138pm, I\u2019d like Fridays off and a lunch break";
        var applied = ScheduleAssistant.Interpret(text).Draft.Applied();
        Assert.Equal(new TimeLimit(600, PreferenceStrength.Required), applied.EarliestStart);
        Assert.Equal(DaySet.Friday, applied.DaysOff);
        Assert.Equal(PreferenceStrength.Preferred, applied.DaysOffStrength);
        Assert.NotNull(applied.Lunch);
        var block = Assert.Single(applied.BlockedTimes);
        Assert.Equal("Robotics club", block.Label);
        Assert.Equal(DaySet.Tuesday | DaySet.Thursday, block.Days);
        Assert.Equal(18 * 60, block.Start);
        Assert.Equal(20 * 60, block.End);
        Assert.Equal("Nothing before 10 AM · Fridays off · 1 blocked time · Lunch break", applied.Summary(4));
    }

    [Fact]
    public void BandsAndLayoutsMap()
    {
        var draft = new SchedulePreferencesDraft
        {
            PreferredTimes = [ScheduleDraftTimeOfDay.Morning, ScheduleDraftTimeOfDay.EarlyMorning],
            AvoidedTimes = [ScheduleDraftTimeOfDay.Night, ScheduleDraftTimeOfDay.Morning],
            Layout = ScheduleDraftLayout.SpreadOut,
            MaxClassesPerDay = 3,
            MaxHoursPerDay = 5,
            Breaks = ScheduleDraftBreaks.TimeToWalk,
            Format = ScheduleDraftFormat.Hybrid,
            Seats = ScheduleDraftSeats.WaitlistOK,
        };
        var preferences = draft.Applied();
        Assert.Equal(TimePreference.Prefer, preferences[TimeBand.EarlyMorning]);
        Assert.Equal(TimePreference.Avoid, preferences[TimeBand.Morning]);
        Assert.Equal(TimePreference.Avoid, preferences[TimeBand.Night]);
        Assert.Equal(DayLayout.SpreadOut, preferences.DayLayout);
        Assert.Equal(3, preferences.MaxClassesPerDay);
        Assert.Equal(300, preferences.MaxMinutesPerDay);
        Assert.Equal(BreakPreference.AtLeast, preferences.Breaks);
        Assert.Equal(15, preferences.MinimumBreakMinutes);
        Assert.Equal(PreferenceStrength.Preferred, preferences.MinimumBreakStrength);
        Assert.Equal(FormatPreference.Hybrid, preferences.Format);
        Assert.Equal(SeatPolicy.WaitlistOK, preferences.Seats);
    }

    [Fact]
    public void FlexibleLimitsAndRequiredDaysOff()
    {
        var draft = new SchedulePreferencesDraft
        {
            EarliestStart = "9:30",
            LatestEnd = "5pm",
            TimeLimitsAreFlexible = true,
            DaysOff = [ScheduleDraftWeekday.Monday, ScheduleDraftWeekday.Friday],
            DaysOffAreRequired = true,
        };
        var preferences = draft.Applied();
        Assert.Equal(new TimeLimit(9 * 60 + 30, PreferenceStrength.Preferred), preferences.EarliestStart);
        Assert.Equal(new TimeLimit(17 * 60, PreferenceStrength.Preferred), preferences.LatestEnd);
        Assert.Equal(DaySet.Monday | DaySet.Friday, preferences.DaysOff);
        Assert.Equal(PreferenceStrength.Required, preferences.DaysOffStrength);
    }

    [Fact]
    public void AnEndBeforeTheStartIsRepairedAsTheAfternoon()
    {
        var draft = new SchedulePreferencesDraft
        {
            BlockedTimes =
            [
                new ScheduleDraftBlockedTime { Label = "Lunch", Days = [ScheduleDraftWeekday.Monday], Start = "11:00", End = "01:00" },
                new ScheduleDraftBlockedTime { Label = "Choir", Days = [ScheduleDraftWeekday.Wednesday], Start = "18:00", End = "08:00" },
                new ScheduleDraftBlockedTime { Label = "Run", Days = [ScheduleDraftWeekday.Friday], Start = "06:00", End = "08:00" },
            ],
        };
        var blocks = draft.Applied().BlockedTimes;
        Assert.Equal(["Lunch", "Choir", "Run"], blocks.Select(block => block.Label));
        Assert.Equal(11 * 60, blocks[0].Start);
        Assert.Equal(13 * 60, blocks[0].End);
        Assert.Equal(18 * 60, blocks[1].Start);
        Assert.Equal(20 * 60, blocks[1].End);
        Assert.Equal(6 * 60, blocks[2].Start);
        Assert.Equal(8 * 60, blocks[2].End);
    }

    [Fact]
    public void AFinishBeforeTheStartIsNotBelieved()
    {
        var draft = new SchedulePreferencesDraft { EarliestStart = "10:00", LatestEnd = "09:00" };
        var preferences = draft.Applied();
        Assert.Equal(10 * 60, preferences.EarliestStart?.Minutes);
        Assert.Null(preferences.LatestEnd);
        var current = new SchedulePreferences { LatestEnd = new TimeLimit(15 * 60) };
        Assert.Equal(15 * 60, new SchedulePreferencesDraft { EarliestStart = "16:00" }.Applied(current).LatestEnd?.Minutes);
    }

    [Fact]
    public void UnusableAnswersAreDroppedNotGuessedAt()
    {
        var draft = new SchedulePreferencesDraft
        {
            EarliestStart = "whenever",
            LatestEnd = "26:00",
            MaxClassesPerDay = 40,
            MaxHoursPerDay = 0,
            BlockedTimes =
            [
                new ScheduleDraftBlockedTime { Label = "No days", Days = [], Start = "10:00", End = "11:00" },
                new ScheduleDraftBlockedTime { Label = "No time", Days = [ScheduleDraftWeekday.Monday], Start = "later", End = "11:00" },
                new ScheduleDraftBlockedTime { Label = "Backwards", Days = [ScheduleDraftWeekday.Monday], Start = "17:00", End = "16:00" },
            ],
        };
        Assert.True(draft.Applied().IsDefault);
    }

    [Fact]
    public void ADescriptionAddsToTheSurveyInsteadOfReplacingIt()
    {
        var current = new SchedulePreferences
        {
            DaysOff = DaySet.Monday,
            BlockedTimes = [new BlockedTime { Label = "Work", Days = DaySet.Wednesday, Start = 13 * 60, End = 17 * 60 }],
        };
        current[TimeBand.Afternoon] = TimePreference.Prefer;
        var draft = new SchedulePreferencesDraft
        {
            DaysOff = [ScheduleDraftWeekday.Friday],
            BlockedTimes =
            [
                new ScheduleDraftBlockedTime { Label = "work", Days = [ScheduleDraftWeekday.Wednesday], Start = "13:00", End = "17:00" },
                new ScheduleDraftBlockedTime { Label = "Choir", Days = [ScheduleDraftWeekday.Thursday], Start = "18:00", End = "19:30" },
            ],
        };
        var merged = draft.Applied(current);
        Assert.Equal(TimePreference.Prefer, merged[TimeBand.Afternoon]);
        Assert.Equal(DaySet.Friday, merged.DaysOff);
        Assert.Equal(["Work", "Choir"], merged.BlockedTimes.Select(block => block.Label));
        var changed = merged.ChangedFields(current);
        Assert.Contains(PreferenceField.DaysOff, changed);
        Assert.DoesNotContain(new PreferenceField.TimeOfDayField(TimeBand.Afternoon), changed);
        Assert.Equal(2, changed.Count);
    }

    [Fact]
    public void AnEmptyDraftIsEmpty()
    {
        Assert.True(new SchedulePreferencesDraft().IsEmpty);
        Assert.True(new SchedulePreferencesDraft { WantsLunchBreak = false }.IsEmpty);
        Assert.False(new SchedulePreferencesDraft { WantsLunchBreak = true }.IsEmpty);
        Assert.True(new SchedulePreferencesDraft().Applied().IsDefault);
        Assert.Equal(2, new SchedulePreferencesDraft
        {
            PreferredTimes = [ScheduleDraftTimeOfDay.Morning],
            DaysOff = [ScheduleDraftWeekday.Friday],
        }.FilledCount);
    }

    [Fact]
    public void AnswersTheStudentNeverMentionedAreDropped()
    {
        var grounded = Overeager().Grounded(Briefs);
        Assert.Null(grounded.Format);
        Assert.Null(grounded.Seats);
        Assert.Null(grounded.Breaks);
        Assert.Null(grounded.Layout);
        Assert.Null(grounded.MaxClassesPerDay);
        Assert.Null(grounded.MaxHoursPerDay);
        Assert.Equal("10:00", grounded.EarliestStart);
        Assert.True(grounded.WantsLunchBreak);
        Assert.Equal([ScheduleDraftWeekday.Friday], grounded.DaysOff);
        Assert.NotNull(grounded.BlockedTimes);
        Assert.Single(grounded.BlockedTimes!);
    }

    [Fact]
    public void FirmnessFollowsTheWordsNotTheModel()
    {
        var applied = Overeager().Grounded(Briefs).Applied();
        Assert.Equal(new TimeLimit(600, PreferenceStrength.Required), applied.EarliestStart);
        Assert.Equal(PreferenceStrength.Preferred, applied.DaysOffStrength);
        Assert.Equal("Nothing before 10 AM · Fridays off · 1 blocked time · Lunch break", applied.Summary(4));
    }

    [Fact]
    public void CommitmentsAndSoftenersAreRecognized()
    {
        var work = new SchedulePreferencesDraft { DaysOff = [ScheduleDraftWeekday.Friday], DaysOffAreRequired = false }
            .Grounded("I work Fridays so I need them off");
        Assert.True(work.DaysOffAreRequired);
        var never = new SchedulePreferencesDraft { DaysOff = [ScheduleDraftWeekday.Monday] }.Grounded("No classes on Mondays please");
        Assert.True(never.DaysOffAreRequired);
        var wish = new SchedulePreferencesDraft { DaysOff = [ScheduleDraftWeekday.Monday], DaysOffAreRequired = true }
            .Grounded("Mondays off would be great");
        Assert.False(wish.DaysOffAreRequired);

        var soft = new SchedulePreferencesDraft { EarliestStart = "10:00", TimeLimitsAreFlexible = false }
            .Grounded("Ideally nothing before 10 in the morning");
        Assert.True(soft.TimeLimitsAreFlexible);
        var firm = new SchedulePreferencesDraft { EarliestStart = "10:00", TimeLimitsAreFlexible = true }
            .Grounded("I can\u2019t do classes before 10");
        Assert.False(firm.TimeLimitsAreFlexible);
    }

    [Fact]
    public void LimitsNeedANumberAndADay()
    {
        var draft = new SchedulePreferencesDraft { MaxClassesPerDay = 3, MaxHoursPerDay = 5 };
        Assert.Equal(3, draft.Grounded("I don\u2019t want more than three classes a day").MaxClassesPerDay);
        Assert.Null(draft.Grounded("I don\u2019t want too many classes").MaxClassesPerDay);
        Assert.Equal(5, draft.Grounded("No more than 5 hours of class each day").MaxHoursPerDay);
    }

    [Fact]
    public void OtherLanguagesAreLeftToTheModel()
    {
        var draft = new SchedulePreferencesDraft
        {
            TimeLimitsAreFlexible = true,
            DaysOff = [ScheduleDraftWeekday.Friday],
            DaysOffAreRequired = true,
            Format = ScheduleDraftFormat.Online,
        };
        const string text = "Quiero las mañanas libres y los viernes sin clase, porque trabajo por las tardes";
        Assert.False(ScheduleAssistant.IsEnglish(text));
        Assert.Equal(draft, draft.Grounded(text));
        Assert.True(ScheduleAssistant.IsEnglish(Briefs));
    }

    [Fact]
    public void TooManyBlocksAreCapped()
    {
        var blocks = Enumerable.Range(0, 20).Select(i => new ScheduleDraftBlockedTime
        {
            Label = $"Block {i}",
            Days = [ScheduleDraftWeekday.Monday],
            Start = $"{8 + i % 10}:00",
            End = $"{9 + i % 10}:00",
        }).ToList();
        Assert.True(new SchedulePreferencesDraft { BlockedTimes = blocks }.Applied().BlockedTimes.Count <= SchedulePreferencesDraft.MaxBlockedTimes);
    }

    [Fact]
    public void FailuresHaveHumanMessages()
    {
        ScheduleAssistant.Failure[] failures =
        [
            ScheduleAssistant.Failure.EmptyDescription,
            ScheduleAssistant.Failure.DescriptionTooLong,
            ScheduleAssistant.Failure.Guardrail,
            ScheduleAssistant.Failure.ContextTooLarge,
            ScheduleAssistant.Failure.UnsupportedLanguage,
            ScheduleAssistant.Failure.Busy,
            ScheduleAssistant.Failure.TimedOut,
            ScheduleAssistant.Failure.NothingFound,
            ScheduleAssistant.Failure.Failed("x"),
            ScheduleAssistant.Failure.Unavailable(ScheduleAssistantAvailability.NotEnabled),
        ];
        Assert.All(failures, failure => Assert.False(string.IsNullOrEmpty(failure.Message)));
        Assert.Contains("survey", ScheduleAssistantAvailability.DeviceNotEligible.Note(), StringComparison.Ordinal);
        Assert.Contains("Settings", ScheduleAssistantAvailability.NotEnabled.Note(), StringComparison.Ordinal);
        Assert.Contains("later", ScheduleAssistantAvailability.NotReady.Note(), StringComparison.Ordinal);
        Assert.Null(ScheduleAssistantAvailability.Available.Note());
    }

    [Fact]
    public void EmptyAndUnknownDescriptionsFail()
    {
        var empty = Assert.Throws<ScheduleAssistant.Failure>(() => ScheduleAssistant.Interpret("  "));
        Assert.Contains("Describe", empty.Message, StringComparison.Ordinal);
        var unknown = Assert.Throws<ScheduleAssistant.Failure>(() => ScheduleAssistant.Interpret("hello there"));
        Assert.Contains("Couldn", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkRangeWithoutMeridianIsAfternoon()
    {
        var applied = ScheduleAssistant.Interpret("work Tuesdays 1-5").Draft.Applied();
        var block = Assert.Single(applied.BlockedTimes);
        Assert.Equal(13 * 60, block.Start);
        Assert.Equal(17 * 60, block.End);
    }

    [Fact]
    public void ClockConvertsMinutesAndPickerDates()
    {
        var date = ScheduleClock.Date(10 * 60 + 30, new DateTime(2026, 10, 13));
        Assert.Equal(10, date.Hour);
        Assert.Equal(30, date.Minute);
        Assert.Equal(10 * 60 + 30, ScheduleClock.Minutes(date));
        Assert.Equal(23 * 60 + 59, ScheduleClock.Minutes(ScheduleClock.Date(24 * 60, date)));
    }

    [Fact]
    public void DraggedTextNamesCoursesAndSections()
    {
        var dropped = ScheduleDroppedCourse.Parse(["CS 3000\nCS 2500 12345\nProfessor Smith\nCS 3000"]);
        Assert.Equal(2, dropped.Count);
        Assert.Equal(new CourseKey("CS", "3000"), dropped[0].Key);
        Assert.Null(dropped[0].Crn);
        Assert.Equal("12345", dropped[1].Crn);
    }

    [Fact]
    public void NextPlanNameSkipsLettersInUse()
    {
        Assert.Equal("Plan A", PlanEditing.NextPlanName([]));
        Assert.Equal("Plan C", PlanEditing.NextPlanName(["Plan A", " plan b "]));
    }

    [Fact]
    public void CarouselUsesSectionsAndPreferences()
    {
        var empty = new ScheduleCarouselLayout([], new AppPreferences());
        Assert.Equal(8, empty.HourStart);
        Assert.Equal(21, empty.HourEnd);
        Assert.Equal([1, 2, 3, 4, 5], empty.Days);
        var saturday = new CourseSection
        {
            Crn = "1",
            Subject = "CS",
            Number = "3000",
            Meetings = [new Meeting { Days = DaySet.Saturday, Start = 9 * 60, End = 10 * 60 }],
        };
        var layout = new ScheduleCarouselLayout([saturday], new AppPreferences());
        Assert.Contains(6, layout.Days);
        Assert.DoesNotContain(0, layout.Days);
    }

    [Fact]
    public void UpNextNamesTheComingClass()
    {
        var section = new CourseSection
        {
            Crn = "12345",
            Subject = "CS",
            Number = "3000",
            ClassType = "Lecture",
            Meetings = [new Meeting { Days = DaySet.Monday, Start = 10 * 60, End = 11 * 60, BuildingCode = "WVH", Room = "210" }],
        };
        var now = new DateTime(2026, 10, 12, 9, 0, 0);
        var result = UpNext.Next([section], now);
        var item = Assert.IsType<UpNext.Result.Class>(result).Meeting;
        Assert.Equal("CS 3000", item.CourseCode);
        Assert.Equal("in 1h", item.RelativeText);
        Assert.False(item.IsInProgress);
        Assert.Contains("WVH 210", item.ExpandedText());
        var during = UpNext.Next([section], new DateTime(2026, 10, 12, 10, 15, 0));
        Assert.Equal("now", Assert.IsType<UpNext.Result.Class>(during).Meeting.RelativeText);
        Assert.IsType<UpNext.Result.NoClasses>(UpNext.Next([], now));
    }

    [Fact]
    public void ReorderWordingAndAutofillDismissal()
    {
        var option = new ScheduleReorderOption
        {
            Id = "move",
            Title = "Move CS 3386 to Spring 2028",
            MovedAway = [new CourseKey("CS", "3386"), new CourseKey("CS", "4100"), new CourseKey("DS", "3000")],
            MovedIn = [new CourseKey("CS", "2500")],
        };
        Assert.Equal("Moved CS 3386 to Spring 2028", option.AppliedText);
        Assert.Equal("CS 3386, CS 4100 and DS 3000", ScheduleReorderOption.List(option.MovedAway));
        Assert.Contains("come out of this schedule", option.ScheduleEffect, StringComparison.Ordinal);
        Assert.Contains("it\u2019s", option.ScheduleEffect, StringComparison.Ordinal);

        var suggestion = new ScheduleAutofillSuggestion
        {
            Origin = new ScheduleAutofillSuggestion.Source.FromDegreePlan(Guid.NewGuid(), Guid.NewGuid()),
            PlanName = "New Plan",
            Semester = "Spring 2027",
            Title = "From \u201cNew Plan\u201d · Spring 2027",
            Detail = "Your plan for this term",
            IsExactTerm = true,
            Courses = [new CourseKey("CS", "3000")],
            Unavailable = [],
        };
        var key = ScheduleAutofillSupport.DismissalKey(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), suggestion);
        var stored = ScheduleAutofillSupport.Adding(key, "");
        Assert.True(ScheduleAutofillSupport.IsDismissed(key, stored));
        Assert.Equal("1 course", ScheduleAutofillSupport.CourseCountLabel(1));
        Assert.Equal("Not offered this term: CS 3386", ScheduleAutofillSupport.UnavailableLine([new CourseKey("CS", "3386")]));
        Assert.Equal([0, 1], ScheduleAutofillSupport.ColorIndices([new CourseKey("CS", "3000"), new CourseKey("CS", "2500")], new SchedulePlan()));
    }

    private const string Briefs = "No classes before 10, I have robotics club Tuesdays and Thursdays 6\u20138pm, I\u2019d like Fridays off and a lunch break";

    private static SchedulePreferencesDraft Overeager() => new()
    {
        EarliestStart = "10:00",
        TimeLimitsAreFlexible = true,
        DaysOff = [ScheduleDraftWeekday.Friday],
        DaysOffAreRequired = true,
        Layout = ScheduleDraftLayout.FewerDays,
        MaxClassesPerDay = 3,
        MaxHoursPerDay = 5,
        Breaks = ScheduleDraftBreaks.AvoidBackToBack,
        WantsLunchBreak = true,
        BlockedTimes =
        [
            new ScheduleDraftBlockedTime
            {
                Label = "Robotics club",
                Days = [ScheduleDraftWeekday.Tuesday, ScheduleDraftWeekday.Thursday],
                Start = "18:00",
                End = "20:00",
            },
        ],
        Format = ScheduleDraftFormat.Online,
        Seats = ScheduleDraftSeats.OpenSeatsOnly,
    };
}
