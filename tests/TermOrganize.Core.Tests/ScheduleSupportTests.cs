// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// Portions derived from SearchNEU where the Swift file says so. Modified 2026-10-13: ported from Swift to C#.

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

file static class SupportFixture
{
    public static CourseSection Section(string crn, string subject, string number, int[] days, double start, double end, int seats = 10, int waitlist = 0) => new()
    {
        Crn = crn,
        Subject = subject,
        Number = number,
        Title = $"{subject} {number}",
        SectionNumber = "01",
        PartOfTerm = "1",
        SeatCapacity = 40,
        SeatsRemaining = seats,
        Enrollment = 40 - seats,
        WaitlistCapacity = waitlist,
        WaitlistRemaining = waitlist,
        ClassType = "Lecture",
        Campus = "Boston",
        InstructionMethod = "Traditional",
        Meetings =
        [
            new Meeting
            {
                Days = DaySet.FromIndices(days),
                Start = (int)(start * 60),
                End = (int)(end * 60),
                BuildingCode = "WVH",
                Building = "West Village H",
                Room = "210",
                CampusCode = "BOS",
                Kind = "Class",
                StartDate = "2026-09-09",
                EndDate = "2026-12-13",
            },
        ],
    };

    public static Course Course(string subject, string number, double credits = 4) => new()
    {
        Subject = subject,
        Number = number,
        Title = $"{subject} {number}",
        MinCredits = credits,
        MaxCredits = credits,
        DetailsLoaded = true,
    };

    public static CatalogIndex Index(Course[] courses, CourseSection[] sections) => CatalogIndex.Build(new TermCatalog
    {
        Term = new Term("202710", "Fall 2026 Semester"),
        Courses = courses.ToList(),
        Sections = sections.ToList(),
    });

    public static SchedulePlan Plan(string[] codes, string[]? selected = null)
    {
        var plan = new SchedulePlan { Name = "Plan A", TermCode = "202710" };
        for (var i = 0; i < codes.Length; i++)
        {
            var key = CourseKey.Parse(codes[i])!.Value;
            plan.Courses.Add(new PlanCourse { Key = key, ColorIndex = i });
        }
        if (selected is not null) plan.SelectedCRNs = selected.ToList();
        return plan;
    }

    public static CatalogIndex ClashingIndex() => Index(
        [Course("CS", "3000"), Course("MATH", "2331"), Course("ENGW", "1111")],
        [
            Section("A1", "CS", "3000", [1, 3], 10, 11.5),
            Section("B1", "MATH", "2331", [1], 11, 12, seats: 0, waitlist: 5),
            Section("C1", "ENGW", "1111", [2, 4], 13, 14.5),
        ]);
}

public class ScheduleSupportTests
{
    [Fact]
    public void WarningsListWhatNeedsAttention()
    {
        var index = SupportFixture.ClashingIndex();
        var resolved = new ResolvedPlan(SupportFixture.Plan(["CS 3000", "MATH 2331", "ENGW 1111"], ["A1", "B1"]), index);
        Assert.Equal(["1 conflict", "1 full section", "1 course needs a section"], resolved.Warnings().Select(w => w.Text).ToArray());
        Assert.Equal(ScheduleWarning.Severity.Error, resolved.Warnings()[0].Level);
        Assert.Equal(ScheduleWarning.Severity.Neutral, resolved.Warnings()[^1].Level);
        Assert.Empty(new ResolvedPlan(SupportFixture.Plan(["CS 3000"]), null).Warnings());
    }

    [Fact]
    public void NoticesExplainConflictsAndFullSections()
    {
        var index = SupportFixture.ClashingIndex();
        var resolved = new ResolvedPlan(SupportFixture.Plan(["CS 3000", "MATH 2331"], ["A1", "B1"]), index);
        var notices = ScheduleNotices.Make(resolved, "Fall 2026", catalogUnavailable: false);
        Assert.Equal(["conflicts", "full"], notices.Select(n => n.Id).ToArray());
        Assert.Equal(ScheduleNotice.Severity.Error, notices[0].Level);
        Assert.Equal("1 time conflict", notices[0].Title);
        Assert.Contains("CS 3000 overlaps MATH 2331", notices[0].Lines[0]);
        Assert.Equal(["MATH 2331 Lecture 01 is full · 5 waitlist spots left"], notices[1].Lines);
        Assert.All(notices, notice => Assert.Null(notice.NoticeFix));
    }

    [Fact]
    public void NoticesOfferToForgetSectionsTheCatalogDoesNotList()
    {
        var index = SupportFixture.ClashingIndex();
        var resolved = new ResolvedPlan(SupportFixture.Plan(["CS 3000"], ["A1", "99999", "C1"]), index);
        var notices = ScheduleNotices.Make(resolved, "Fall 2026", catalogUnavailable: false);
        var ids = notices.Select(n => n.Id).ToArray();
        Assert.Contains("unlisted", ids);
        Assert.Contains("orphaned", ids);
        Assert.Equal(new ScheduleNotice.Fix.RemoveSections(["99999"]), notices.First(n => n.Id == "unlisted").NoticeFix);
        Assert.Equal(new ScheduleNotice.Fix.RemoveSections(["C1"]), notices.First(n => n.Id == "orphaned").NoticeFix);
        Assert.Equal("Remove", notices.First(n => n.Id == "orphaned").FixTitle);
    }

    [Fact]
    public void AMissingCatalogIsReportedOnceItIsKnownToBeMissing()
    {
        var resolved = new ResolvedPlan(SupportFixture.Plan(["CS 3000"]), null);
        Assert.Empty(ScheduleNotices.Make(resolved, "Spring 2027", catalogUnavailable: false));
        var notices = ScheduleNotices.Make(resolved, "Spring 2027", catalogUnavailable: true);
        Assert.Equal(["catalog"], notices.Select(n => n.Id).ToArray());
        Assert.Equal("Course data for Spring 2027 isn\u2019t on this device", notices[0].Title);
        Assert.Empty(ScheduleNotices.Make(new ResolvedPlan(SupportFixture.Plan([]), null), "Spring 2027", catalogUnavailable: true));
    }

    [Fact]
    public void FavoritesAreMeasuredAndSkipWhenTheyCanNoLongerBeApplied()
    {
        var index = SupportFixture.ClashingIndex();
        var plan = SupportFixture.Plan(["CS 3000", "ENGW 1111"]);
        plan.FavoriteCombinations =
        [
            ["A1", "C1"],
            ["A1", "GONE"],
            ["B1", "C1"],
        ];
        var favorites = GeneratedScheduleFavorites.Favorites(plan, index, new SchedulePreferences());
        Assert.Equal(new[] { "A1", "C1" }, Assert.Single(favorites).Crns);
        Assert.Equal(["A1", "C1"], favorites[0].SortedCRNs);
    }

    [Fact]
    public void FavoritesComeNewestFirstInPlanOrder()
    {
        var index = SupportFixture.ClashingIndex();
        var plan = SupportFixture.Plan(["ENGW 1111", "CS 3000"]);
        plan.FavoriteCombinations = [["A1"], ["C1", "A1"]];
        var favorites = GeneratedScheduleFavorites.Favorites(plan, index, new SchedulePreferences());
        Assert.Equal(["C1", "A1"], favorites[0].Crns);
        Assert.Equal(["A1"], favorites[1].Crns);
    }

    [Fact]
    public void CourseInputsRoundCreditsUpAndKeepExclusions()
    {
        var index = SupportFixture.Index(
            [SupportFixture.Course("CS", "3000", 3.5), SupportFixture.Course("MATH", "2331")],
            [SupportFixture.Section("A1", "CS", "3000", [1], 9, 10), SupportFixture.Section("A2", "CS", "3000", [2], 9, 10)]);
        var plan = SupportFixture.Plan(["CS 3000", "MATH 2331"]);
        plan.Courses[0].ExcludedCRNs = ["A2"];
        plan.Courses[1].IsRequired = false;
        var inputs = ScheduleCourseInputs.Inputs(plan, index);
        Assert.Equal(["CS 3000", "MATH 2331"], inputs.Select(i => i.Key.ToString()).ToArray());
        Assert.Equal(4, inputs[0].WholeMinCredits);
        Assert.Equal(4d, inputs[0].MinCredits);
        Assert.Equal(2, inputs[0].Sections.Count);
        Assert.Equal(["A2"], inputs[0].ExcludedCRNs);
        Assert.False(inputs[1].IsRequired);
        Assert.Empty(inputs[1].Sections);
    }

    [Fact]
    public void LooseningARuleEditsThePlanOrItsPreferences()
    {
        var plan = SupportFixture.Plan(["CS 3000", "MATH 2331"]);
        plan.Filters.EarliestStart = 10 * 60;
        plan.Filters.FreeDays = DaySet.Friday;
        plan.Preferences = new SchedulePreferences { DaysOff = DaySet.Friday, DaysOffStrength = PreferenceStrength.Required };

        PlanEditing.Apply(new ScheduleRelaxation.ClearEarliestStart(), plan);
        Assert.Null(plan.Filters.EarliestStart);
        Assert.Equal(DaySet.Friday, plan.Filters.FreeDays);

        PlanEditing.Apply(new ScheduleRelaxation.MakeOptional(new CourseKey("CS", "3000")), plan);
        Assert.False(plan.Courses[0].IsRequired);
        Assert.True(plan.Courses[1].IsRequired);

        PlanEditing.Apply(new ScheduleRelaxation.PreferenceChange(new PreferenceRelaxation.SoftenDaysOff()), plan);
        Assert.Equal(PreferenceStrength.Preferred, plan.Preferences!.DaysOffStrength);

        var onlyBlock = SupportFixture.Plan(["CS 3000"]);
        onlyBlock.Preferences = new SchedulePreferences { MaxClassesPerDay = 2 };
        PlanEditing.Apply(new ScheduleRelaxation.PreferenceChange(new PreferenceRelaxation.ClearMaxClassesPerDay()), onlyBlock);
        Assert.Null(onlyBlock.Preferences);

        PlanEditing.MakeOptional([new CourseKey("CS", "3000"), new CourseKey("MATH", "2331")], plan);
        Assert.All(plan.Courses, course => Assert.False(course.IsRequired));
    }

    [Fact]
    public void NoResultsWordingNamesWhatBlocksAndWhatWouldWork()
    {
        var index = SupportFixture.ClashingIndex();
        var plan = SupportFixture.Plan(["CS 3000", "MATH 2331"]);
        var courses = ScheduleCourseInputs.Inputs(plan, index);
        var result = ScheduleGenerator.Generate(courses, new ScheduleFilters());
        Assert.Empty(result.Schedules);
        Assert.Equal("No schedule fits all 2 courses", ScheduleNoResultsText.Title(result.Diagnostics));

        var analysis = ScheduleGenerator.AnalyzeEmptyResult(courses, new ScheduleFilters(), new SchedulePreferences(), result.Diagnostics);
        Assert.Equal("Here\u2019s what would work.", ScheduleNoResultsText.Headline(result.Diagnostics, analysis.Blockers));
        var reasons = ScheduleNoResultsText.Reasons(result.Diagnostics, "Fall 2026");
        Assert.Equal(["CS 3000 and MATH 2331 always overlap"], reasons.Select(r => r.Title).ToArray());
        var option = analysis.Blockers.Options[0];
        Assert.StartsWith("1 schedule", ScheduleNoResultsText.Detail(option, analysis.Blockers, "Fall 2026"));
        Assert.Equal(
            "CS 3000, MATH 2331 and PHYS 1151",
            ScheduleNoResultsText.List([new CourseKey("CS", "3000"), new CourseKey("MATH", "2331"), new CourseKey("PHYS", "1151")]));
    }

    [Fact]
    public void NoResultsWordingSaysWhyEverySectionWasRuledOut()
    {
        var index = SupportFixture.ClashingIndex();
        var plan = SupportFixture.Plan(["CS 3000"]);
        var filters = new ScheduleFilters { EarliestStart = 20 * 60 };
        var result = ScheduleGenerator.Generate(ScheduleCourseInputs.Inputs(plan, index), filters);
        var reasons = ScheduleNoResultsText.Reasons(result.Diagnostics, "Fall 2026");
        Assert.Equal(["No CS 3000 sections left"], reasons.Select(r => r.Title).ToArray());
        Assert.Equal("Its only section starts before 8:00 PM.", reasons[0].Detail);
    }

    [Fact]
    public void UncertainReorderReasonsAreNotGoodNews()
    {
        Assert.True(ScheduleNoResultsText.IsUncertain("Offering history unknown"));
        Assert.True(ScheduleNoResultsText.IsUncertain("Couldn\u2019t check the catalog"));
        Assert.False(ScheduleNoResultsText.IsUncertain("Offered every spring"));
    }

    [Fact]
    public void BlockedTimesReadAsTheyAreConfigured()
    {
        var block = new BlockedTime { Label = "Club", Days = DaySet.FromIndices([2, 4]), Start = 18 * 60, End = 20 * 60 };
        Assert.Equal("Tue, Thu 6:00 \u2013 8:00 PM", block.ListDetail);
        block.Strength = PreferenceStrength.Preferred;
        Assert.Equal("Tue, Thu 6:00 \u2013 8:00 PM \u00B7 If I must", block.ListDetail);
        block.IsEnabled = false;
        Assert.Equal("Tue, Thu 6:00 \u2013 8:00 PM \u00B7 Off", block.ListDetail);
        Assert.Equal("Choose the days", new BlockedTime { Label = "", Days = DaySet.None, Start = 600, End = 660 }.ListDetail);
        Assert.Equal("Check the times", new BlockedTime { Label = "", Days = DaySet.FromIndices([1]), Start = 700, End = 660 }.ListDetail);
    }

    [Fact]
    public void CalendarLayoutFitsEverySchedule()
    {
        var early = new GeneratedSchedule([SupportFixture.Section("A", "CS", "1", [1], 8, 9)]);
        var late = new GeneratedSchedule([SupportFixture.Section("B", "CS", "2", [6], 17, 20)]);
        var layout = new ScheduleCarouselLayout(early.Sections.Concat(late.Sections), new AppPreferences());
        Assert.Equal(8, layout.HourStart);
        Assert.Equal(20, layout.HourEnd);
        Assert.Equal([1, 2, 3, 4, 5, 6], layout.Days);
        Assert.Equal([1, 2, 3, 4, 5], new ScheduleCarouselLayout([], new AppPreferences()).Days);
    }

    [Fact]
    public void AutofillSuggestionsComeFromTheDegreePlanAndOtherSchedules()
    {
        var index = SupportFixture.ClashingIndex();
        var target = SupportFixture.Plan([]);
        var sibling = SupportFixture.Plan(["CS 3000", "ENGW 1111"]);
        sibling.Name = "Plan B";
        var suggestions = ScheduleAutofillSupport.Suggestions(target, [], [], [sibling, target], index);
        var suggestion = Assert.Single(suggestions);
        Assert.Equal(["CS 3000", "ENGW 1111"], suggestion.Courses.Select(k => k.ToString()).ToArray());
        Assert.Equal("Copy \u201cPlan B\u201d", ScheduleAutofillSupport.SourceName(suggestion));
        Assert.Empty(ScheduleAutofillSupport.Suggestions(target, [], [], [target], index));
        Assert.Equal("1 course", ScheduleAutofillSupport.CourseCountLabel(1));
        Assert.Equal("3 courses", ScheduleAutofillSupport.CourseCountLabel(3));
        var key = ScheduleAutofillSupport.DismissalKey(target.Id, suggestion);
        var stored = ScheduleAutofillSupport.Adding(key, "");
        Assert.True(ScheduleAutofillSupport.IsDismissed(key, stored));
        Assert.False(ScheduleAutofillSupport.IsDismissed("other", stored));
    }

    [Fact]
    public void DraggedTextNamesCoursesAndSections()
    {
        var parsed = ScheduleDroppedCourse.Parse(["CS 3000", "math 2331 12345\nPHYS 1151 99\nCS 3000", "Dr. Smith", "John Smith"]);
        Assert.Equal(["CS 3000", "MATH 2331", "PHYS 1151"], parsed.Select(item => item.Key.ToString()).ToArray());
        Assert.Null(parsed[0].Crn);
        Assert.Equal("12345", parsed[1].Crn);
        Assert.Null(parsed[2].Crn);
        Assert.Empty(ScheduleDroppedCourse.Parse([]));
        Assert.Empty(ScheduleDroppedCourse.Parse(["hello"]));
    }

    [Fact]
    public void ClockConvertsMinutesAndPickerDates()
    {
        foreach (var minutes in new[] { 0, 9 * 60 + 15, 23 * 60 + 59 })
            Assert.Equal(minutes, ScheduleClock.Minutes(ScheduleClock.Date(minutes)));
        Assert.Equal(24 * 60 - 1, ScheduleClock.Minutes(ScheduleClock.Date(24 * 60)));
        Assert.Equal(0, ScheduleClock.Minutes(ScheduleClock.Date(-5)));
    }

    [Fact]
    public void RemovingACourseTakesItsSectionsAndFavoritesAndUndoPutsThemBack()
    {
        var path = Path.Combine(Path.GetTempPath(), $"UserData-{Guid.NewGuid():N}.json");
        try
        {
            var userData = new UserDataStore(path);
            var index = SupportFixture.ClashingIndex();
            var plan = userData.CreatePlan("Plan A", "202710");
            foreach (var code in new[] { "CS 3000", "MATH 2331", "ENGW 1111" })
                userData.AddCourse(CourseKey.Parse(code)!.Value, plan.Id);
            userData.UpdatePlan(plan.Id, current =>
            {
                current.SelectedCRNs = ["A1", "C1"];
                current.FavoriteCombinations = [["A1", "C1"], ["B1", "C1"]];
            });
            var current = userData.Plan(plan.Id)!;
            var resolved = new ResolvedPlan(current, index);
            var entry = resolved.EntryFor(new CourseKey("CS", "3000"))!;
            var removal = SchedulePlanActions.RemoveCourse(entry, current, index, userData);
            var after = userData.Plan(plan.Id)!;
            Assert.Equal(["MATH 2331", "ENGW 1111"], after.Courses.Select(c => c.Key.ToString()).ToArray());
            Assert.Equal(["C1"], after.SelectedCRNs);
            Assert.Equal(new[] { new[] { "B1", "C1" } }, after.FavoriteCombinations.Select(c => c.ToArray()).ToArray());
            Assert.Equal(0, removal.Index);
            Assert.Equal(["A1"], removal.SelectedCRNs);

            userData.UpdatePlan(plan.Id, restored => PlanEditing.Restore(removal, restored));
            var back = userData.Plan(plan.Id)!;
            Assert.Equal(["CS 3000", "MATH 2331", "ENGW 1111"], back.Courses.Select(c => c.Key.ToString()).ToArray());
            Assert.Equal(["A1", "C1"], back.SelectedCRNs.OrderBy(c => c, StringComparer.Ordinal).ToArray());
            Assert.Contains(back.FavoriteCombinations, combo => combo.SequenceEqual(["A1", "C1"]));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AutofillAddsRequiredCoursesOnceAndUndoTakesThemOut()
    {
        var path = Path.Combine(Path.GetTempPath(), $"UserData-{Guid.NewGuid():N}.json");
        try
        {
            var userData = new UserDataStore(path);
            var plan = userData.CreatePlan("Plan A", "202710");
            userData.AddCourse(new CourseKey("CS", "3000"), plan.Id);
            var suggestion = new ScheduleAutofillSuggestion
            {
                Origin = new ScheduleAutofillSuggestion.Source.FromSchedulePlan(Guid.NewGuid()),
                PlanName = "Plan B",
                Title = "Copy \u201cPlan B\u201d",
                Detail = "",
                Courses = [new CourseKey("CS", "3000"), new CourseKey("ENGW", "1111")],
                Unavailable = [],
            };
            var added = SchedulePlanActions.AddAutofill(suggestion, plan.Id, userData);
            Assert.Equal(["ENGW 1111"], added.Select(k => k.ToString()).ToArray());
            Assert.Equal(2, userData.Plan(plan.Id)!.Courses.Count);
            Assert.True(userData.Plan(plan.Id)!.Course(new CourseKey("ENGW", "1111"))!.IsRequired);
            SchedulePlanActions.UndoAutofill(added, plan.Id, SupportFixture.ClashingIndex(), userData);
            Assert.Equal(["CS 3000"], userData.Plan(plan.Id)!.Courses.Select(c => c.Key.ToString()).ToArray());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ReorderingTheDegreePlanNeedsASemesterThatMatchesTheTerm()
    {
        var plan = SupportFixture.Plan(["CS 3000"]);
        var degreePlan = new DegreePlan { Name = "Plan" };
        degreePlan.Terms =
        [
            new DegreePlanTerm
            {
                Season = PlanSeason.Fall,
                Year = 2026,
                Courses = [new DegreePlanCourse(new CourseKey("CS", "3000"), "Algorithms", 4)],
            },
        ];
        var terms = new[] { new Term("202710", "Fall 2026 Semester") };
        var found = ScheduleReorderSuggestions.Semester(plan, degreePlan, terms, null);
        Assert.NotNull(found);
        Assert.Equal("202710", found.Value.Term.Code);
        Assert.Equal(degreePlan.Id, found.Value.Plan.Id);
        var spring = SupportFixture.Plan(["CS 3000"]);
        spring.TermCode = "202730";
        Assert.Null(ScheduleReorderSuggestions.Semester(spring, degreePlan, terms, null));
        Assert.Null(ScheduleReorderSuggestions.Semester(plan, null, terms, null));
    }
}
