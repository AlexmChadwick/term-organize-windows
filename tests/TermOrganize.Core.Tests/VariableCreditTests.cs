// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class VariableCreditTests
{
    static readonly CourseKey Readings = new("CS", "8982");

    [Fact]
    public void DefaultIsFourWhenItSitsInsideTheRange()
    {
        var span = VariableCredits.FromBanner(creditHours: 4, low: 1, high: 8);
        Assert.True(span.IsVariable);
        Assert.Equal(4, span.Starting);
        Assert.Equal(1, span.Clamp(0.4));
        Assert.Equal(8, span.Clamp(9));
        Assert.Equal(3, span.Clamp(3.2));

        var outside = VariableCredits.FromBanner(creditHours: 4, low: 6, high: 8);
        Assert.Equal(6, outside.Starting);

        var fixedCredits = VariableCredits.FromBanner(creditHours: 4, low: 4, high: null);
        Assert.False(fixedCredits.IsVariable);
        Assert.Equal(4, fixedCredits.Starting);

        var bundled = new Course { Subject = "CS", Number = "8982", MinCredits = 1, MaxCredits = 8 };
        Assert.Equal(4, bundled.StartingCredits);
        Assert.Equal("Credits: 4 (1 to 8)", VariableCredits.Caption(4, VariableCredits.Of(bundled)));
    }

    [Fact]
    public void TwoCopiesKeepDifferentCreditsAndBothCount()
    {
        var first = new DegreePlanCourse(Readings, "Readings", 3);
        var second = new DegreePlanCourse(Readings, "Readings", 5);
        var plan = new DegreePlan
        {
            Terms = [new DegreePlanTerm { Season = PlanSeason.Spring, Year = 2027, Courses = [first, second] }],
        };
        Assert.Equal(8, plan.PlannedCredits);
        Assert.Equal(8, DegreeAudit.Run(plan, new ProgramRequirements()).PlannedHours);

        var schedule = new SchedulePlan { TermCode = "202730" };
        DegreePlanSchedule.SyncRows(schedule, plan.Terms[0].Courses);
        Assert.Equal(2, schedule.Courses.Count);
        Assert.Equal([3d, 5d], schedule.Courses.Select(row => row.Credits));
        Assert.Equal([first.Id, second.Id], schedule.Courses.Select(row => row.DegreeRowId));

        schedule.Courses[0].Credits = 2;
        Assert.Equal(1, DegreePlanSchedule.PullCredits(plan, schedule));
        Assert.Equal(2, first.Credits);
        Assert.Equal(5, second.Credits);
        Assert.Equal(2, schedule.Courses.Count);

        second.Credits = 8;
        DegreePlanSchedule.SyncRows(schedule, plan.Terms[0].Courses);
        Assert.Equal(2, schedule.Courses.Count);
        Assert.Equal(8, schedule.Courses.Single(row => row.DegreeRowId == second.Id).Credits);
    }

    [Fact]
    public void ScheduleTotalsUseTheChosenValueOnce()
    {
        var course = ReadingsCourse();
        var index = Index(course, Section("34001", course), Section("34002", course));
        var plan = new SchedulePlan
        {
            Courses =
            [
                new PlanCourse { Key = Readings, IsRequired = true, Credits = 3 },
                new PlanCourse { Key = Readings, IsRequired = true, Credits = 5 },
            ],
            SelectedCRNs = ["34001", "34002"],
        };
        var resolved = new ResolvedPlan(plan, index);
        Assert.Equal(8, resolved.Credits.Min);
        Assert.Equal(8, resolved.Credits.Max);
        Assert.False(resolved.IsOverCreditLimit);

        var inputs = ScheduleCourseInputs.Inputs(plan, index);
        Assert.Equal([3, 5], inputs.Select(input => input.WholeMinCredits));
        Assert.Equal([3, 5], inputs.Select(input => input.WholeMaxCredits));

        var single = new SchedulePlan
        {
            Courses = [new PlanCourse { Key = Readings, IsRequired = true, Credits = 4 }],
            SelectedCRNs = ["34001", "34002"],
        };
        var slots = ScheduleCourseInputs.Inputs(single, index);
        Assert.Equal(4, slots.Sum(slot => slot.WholeMinCredits));
    }

    [Fact]
    public void MissingCreditsMigrateAndRefreshClampsWithoutResetting()
    {
        var path = Path.Combine(Path.GetTempPath(), "term-organize-credits-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new UserDataStore(path);
            var schedule = store.CreatePlan("Scratch", "202730");
            store.UpdatePlan(schedule.Id, plan => plan.Courses.Add(new PlanCourse { Key = Readings, IsRequired = true }));
            var degree = store.AddDegreePlan(new DegreePlan
            {
                Name = "Scratch",
                Terms = [new DegreePlanTerm
                {
                    Season = PlanSeason.Spring,
                    Year = 2027,
                    Courses = [new DegreePlanCourse(Readings, "Readings", 6)],
                }],
            });

            var loaded = new UserDataStore(path);
            Assert.Null(loaded.Plan(schedule.Id)!.Courses.Single().Credits);
            Assert.Equal(6, loaded.DegreePlan(degree.Id)!.Terms.Single().Courses.Single().Credits);

            var wide = ReadingsCourse();
            VariableCredits.ClampStored(loaded, _ => wide);
            var migrated = new UserDataStore(path);
            Assert.Equal(4, migrated.Plan(schedule.Id)!.Courses.Single().Credits);
            Assert.Equal(6, migrated.DegreePlan(degree.Id)!.Terms.Single().Courses.Single().Credits);

            var narrower = new Course { Subject = "CS", Number = "8982", MinCredits = 2, MaxCredits = 4, DefaultCredits = 4 };
            VariableCredits.ClampStored(migrated, _ => narrower);
            var clamped = new UserDataStore(path);
            Assert.Equal(4, clamped.Plan(schedule.Id)!.Courses.Single().Credits);
            Assert.Equal(4, clamped.DegreePlan(degree.Id)!.Terms.Single().Courses.Single().Credits);
        }
        finally
        {
            try { File.Delete(path); } catch (Exception) { }
        }

        var omitted = JsonSerializer.Deserialize<PlanCourse>(
            """{"key":{"subject":"CS","number":"8982"},"isRequired":true,"colorIndex":0}""",
            AppJson.UserData);
        Assert.NotNull(omitted);
        Assert.Null(omitted!.Credits);
        Assert.True(VariableCredits.Fill(omitted, ReadingsCourse()));
        Assert.Equal(4, omitted.Credits);
    }

    [Fact]
    public void RefreshKeepsLowHighAndAChosenValueInsideTheNewRange()
    {
        var banner = Banner("34001", 4, 1, 8, "TO");
        var (courses, _) = CatalogBuilder.StubCourses([banner]);
        Assert.Equal((1d, 8d, 4d), (courses[0].MinCredits, courses[0].MaxCredits, courses[0].DefaultCredits));
        var sections = CatalogBuilder.MakeSections([banner]);
        Assert.Equal(1, sections[0].CreditLow);
        Assert.Equal(8, sections[0].CreditHigh);
        Assert.Equal("TO", sections[0].CreditHourIndicator);
        sections[0].Restrictions = [new SectionRestriction { Kind = RestrictionKind.Must, Category = "Levels", Values = ["CPS - Undergraduate Semester (UC)"] }];

        var catalog = new TermCatalog
        {
            Term = new Term("202730", "Spring 2027 Semester"),
            Courses = courses,
            Sections = sections,
        };
        CatalogBuilder.ApplyCourseRefresh(catalog, Readings, [Banner("34001", 3, 2, 4, "TO")]);
        Assert.Equal((2d, 4d, 3d), (catalog.Courses[0].MinCredits, catalog.Courses[0].MaxCredits, catalog.Courses[0].DefaultCredits));
        Assert.Equal(2, catalog.Sections[0].CreditLow);
        Assert.NotNull(catalog.Sections[0].Restrictions);

        var kept = new DegreePlanCourse(Readings, "Readings", 3);
        Assert.False(VariableCredits.Fill(kept, catalog.Courses[0]));
        Assert.Equal(3, kept.Credits);
        var high = new DegreePlanCourse(Readings, "Readings", 8);
        Assert.True(VariableCredits.Fill(high, catalog.Courses[0]));
        Assert.Equal(4, high.Credits);
    }

    static Course ReadingsCourse() => new()
    {
        Subject = "CS",
        Number = "8982",
        Title = "Readings",
        MinCredits = 1,
        MaxCredits = 8,
    };

    static CourseSection Section(string crn, Course course) => new()
    {
        Crn = crn,
        Subject = course.Subject,
        Number = course.Number,
        Title = course.Title,
        SectionNumber = "01",
    };

    static CatalogIndex Index(Course course, params CourseSection[] sections) =>
        CatalogIndex.Build(new TermCatalog
        {
            Term = new Term("202730", "Spring 2027 Semester"),
            Courses = [course],
            Sections = sections.ToList(),
        });

    static BannerSection Banner(string crn, double hours, double low, double high, string indicator) => new()
    {
        CourseReferenceNumber = crn,
        Subject = "CS",
        CourseNumber = "8982",
        CourseTitle = "Readings",
        CreditHours = hours,
        CreditHourLow = low,
        CreditHourHigh = high,
        CreditHourIndicator = indicator,
    };
}
