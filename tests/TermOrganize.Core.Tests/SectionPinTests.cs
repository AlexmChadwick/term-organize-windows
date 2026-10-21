// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

/// <summary>
/// A chosen CRN stays chosen, and the same subject and number may be listed twice
/// only when each listing has a different manual CRN.
/// </summary>
public class SectionPinTests
{
    static readonly CourseKey Topics = new("CS", "4973");
    static readonly CourseKey Math = new("MATH", "2331");
    static readonly CourseKey Fundies = new("CS", "2500");
    static readonly CourseKey Directed = new("CS", "4990");

    [Fact]
    public void GenerationKeepsThePinnedSection()
    {
        var index = TopicsIndex();
        var plan = Schedule(["CS 4973"], ["11111"]);
        var result = ScheduleGenerator.Generate(ScheduleCourseInputs.Inputs(plan, index), new ScheduleFilters());
        Assert.True(result.TotalFound > 0);
        Assert.All(result.Schedules, schedule =>
        {
            Assert.Contains("11111", schedule.Crns);
            Assert.DoesNotContain("22222", schedule.Crns);
        });
    }

    [Fact]
    public void AConflictingPinIsNotReplaced()
    {
        var index = Index(
            [Course("CS", "4973", topics: true), Course("MATH", "2331")],
            [
                Section("11111", "CS", "4973", [1], 10, 11.5),
                Section("22222", "CS", "4973", [3], 10, 11.5),
                Section("M1", "MATH", "2331", [1], 10, 11.5),
            ]);
        var plan = Schedule(["CS 4973", "MATH 2331"], ["11111"]);
        var result = ScheduleGenerator.Generate(ScheduleCourseInputs.Inputs(plan, index), new ScheduleFilters());
        Assert.Equal(0, result.TotalFound);
        Assert.DoesNotContain(result.Schedules, schedule => schedule.Crns.Contains("22222"));
        Assert.Equal(["11111"], plan.SelectedCRNs);
    }

    [Fact]
    public void TwoPinnedSectionsOfOneNumberStaySeparateSlots()
    {
        var index = TopicsIndex();
        var plan = Schedule(["CS 4973"], ["11111", "22222"]);
        var inputs = ScheduleCourseInputs.Inputs(plan, index);
        Assert.Equal(2, inputs.Count);
        Assert.All(inputs, input => Assert.Equal(Topics, input.Key));
        Assert.Equal(["11111", "22222"], inputs.Select(input => input.PinnedCrn).OrderBy(crn => crn, StringComparer.Ordinal));

        var result = ScheduleGenerator.Generate(inputs, new ScheduleFilters());
        Assert.True(result.TotalFound > 0);
        Assert.All(result.Schedules, schedule =>
        {
            Assert.Contains("11111", schedule.Crns);
            Assert.Contains("22222", schedule.Crns);
        });
    }

    [Fact]
    public void AMissingPinStaysLockedAndUnavailable()
    {
        var index = Index(
            [Course("CS", "4973", topics: true), Course("MATH", "2331")],
            [Section("22222", "CS", "4973", [3], 10, 11.5), Section("M1", "MATH", "2331", [2], 13, 14.5)]);
        var plan = Schedule(["CS 4973"], ["11111"]);
        var inputs = ScheduleCourseInputs.Inputs(plan, index);
        var input = Assert.Single(inputs);
        Assert.Equal("11111", input.PinnedCrn);
        Assert.Empty(input.Sections);
        var result = ScheduleGenerator.Generate(inputs, new ScheduleFilters());
        Assert.Equal(0, result.TotalFound);
        Assert.DoesNotContain(result.Schedules, schedule => schedule.Crns.Contains("22222"));
        Assert.Equal("11111", result.Diagnostics.Courses[0].UnavailableCrn);
        var reasons = ScheduleNoResultsText.Reasons(result.Diagnostics, "Fall 2026");
        Assert.Equal(["CRN 11111 is unavailable"], reasons.Select(reason => reason.Title));
        Assert.Equal("CS 4973 stays locked to that section.", reasons[0].Detail);
        Assert.Equal(["11111"], plan.SelectedCRNs);

        var withMath = Schedule(["CS 4973", "MATH 2331"], ["11111"]);
        withMath.Courses[0].PinnedCrn = "11111";
        var mixed = ScheduleCourseInputs.Inputs(withMath, index);
        Assert.Equal(["11111", null], mixed.Select(item => item.PinnedCrn));
        Assert.Empty(mixed[0].Sections);
        Assert.Equal(["M1"], mixed[1].Sections.Select(section => section.Crn));
        var mixedResult = ScheduleGenerator.Generate(mixed, new ScheduleFilters());
        Assert.Equal(0, mixedResult.TotalFound);
        Assert.DoesNotContain(mixedResult.Schedules, schedule => schedule.Crns.Contains("22222"));

        var degree = ScheduleCourseInputs.ForDegreeCourses(
            [new DegreePlanCourse(Topics, "Directed Study", 4) { SelectedCrn = "11111" }],
            index);
        Assert.Equal("11111", Assert.Single(degree).PinnedCrn);
        Assert.Empty(degree[0].Sections);
        var degreeResult = ScheduleGenerator.Generate(degree, new ScheduleFilters());
        Assert.Equal(0, degreeResult.TotalFound);
        Assert.Equal("11111", degreeResult.Diagnostics.Courses[0].UnavailableCrn);
        Assert.DoesNotContain(degreeResult.Schedules, schedule => schedule.Crns.Contains("22222"));
    }

    [Fact]
    public void AnUnpinnedCourseStillOffersEverySection()
    {
        var index = TopicsIndex();
        var plan = Schedule(["CS 4973"]);
        var inputs = ScheduleCourseInputs.Inputs(plan, index);
        var input = Assert.Single(inputs);
        Assert.Null(input.PinnedCrn);
        Assert.Equal(2, input.Sections.Count);
        var result = ScheduleGenerator.Generate(inputs, new ScheduleFilters());
        Assert.Equal(2, result.TotalFound);
        Assert.Contains(result.Schedules, schedule => schedule.Crns.Contains("11111"));
        Assert.Contains(result.Schedules, schedule => schedule.Crns.Contains("22222"));
    }

    [Fact]
    public void DegreeCoursesWithDifferentCrnsAreNotMerged()
    {
        var index = TopicsIndex();
        var courses = new[]
        {
            new DegreePlanCourse(Topics, "Directed Study", 4) { SelectedCrn = "11111" },
            new DegreePlanCourse(Topics, "Directed Study", 4) { SelectedCrn = "22222" },
        };
        var inputs = ScheduleCourseInputs.ForDegreeCourses(courses, index);
        Assert.Equal(["11111", "22222"], inputs.Select(input => input.PinnedCrn));
        var result = ScheduleGenerator.Generate(inputs, new ScheduleFilters());
        Assert.True(result.TotalFound > 0);
        Assert.All(result.Schedules, schedule => Assert.Equal(2, schedule.Crns.Count));
    }

    [Fact]
    public void ApplyingAScheduleKeepsPinsAndFillsUnpinnedCourses()
    {
        var index = Index(
            [Course("CS", "4973", topics: true), Course("ENGW", "1111")],
            [
                Section("11111", "CS", "4973", [1], 10, 11.5),
                Section("22222", "CS", "4973", [3], 10, 11.5),
                Section("E1", "ENGW", "1111", [2], 13, 14.5),
            ]);
        var plan = Schedule(["CS 4973", "ENGW 1111"], ["11111", "99999"]);
        PlanEditing.ApplyGeneratedSchedule(["22222", "E1"], index, plan);
        Assert.Equal(["11111", "99999", "E1"], plan.SelectedCRNs);
        Assert.Equal(["11111"], PlanEditing.PinsAfterRefresh(["11111"], Index(
            [Course("CS", "4973", topics: true)],
            [Section("22222", "CS", "4973", [3], 10, 11.5)])));
    }

    [Fact]
    public void SpecialTopicsCanKeepTwoSectionsAndANormalCourseKeepsOne()
    {
        var plan = Schedule(["CS 4973", "MATH 2331"]);
        var topics = new HashSet<string>(["11111", "22222"], StringComparer.Ordinal);
        PlanEditing.SetSection("11111", topics, plan, replaceOtherSections: false);
        PlanEditing.SetSection("22222", topics, plan, replaceOtherSections: false);
        PlanEditing.SetSection("22222", topics, plan, replaceOtherSections: false);
        var math = new HashSet<string>(["M1", "M2"], StringComparer.Ordinal);
        PlanEditing.SetSection("M1", math, plan);
        PlanEditing.SetSection("M2", math, plan);
        Assert.Equal(["11111", "22222", "M2"], plan.SelectedCRNs);
        PlanEditing.ClearCrn("11111", plan);
        Assert.Equal(["22222", "M2"], plan.SelectedCRNs);
    }

    [Fact]
    public void ConflictsReportOverlapsWithoutChangingPins()
    {
        var sections = new[]
        {
            Section("11111", "CS", "4973", [1], 10, 11.5),
            Section("M1", "MATH", "2331", [1], 10, 11.5),
        };
        var plan = Schedule(["CS 4973", "MATH 2331"], ["11111", "M1"]);
        var conflicts = ScheduleConflicts.Find(sections);
        Assert.Single(conflicts);
        Assert.Equal(["11111", "M1"], plan.SelectedCRNs);
    }

    [Fact]
    public void ReorganizeKeepsTheSelectedCrn()
    {
        var course = new DegreePlanCourse(Topics, "Directed Study", 4) { SelectedCrn = "11111" };
        var fall = new DegreePlanTerm { Season = PlanSeason.Fall, Year = 2026, Courses = [course] };
        var spring = new DegreePlanTerm { Season = PlanSeason.Spring, Year = 2027 };
        var plan = new DegreePlan { Name = "Pins", Terms = [fall, spring] };
        DegreePlanReordering.Apply(new DegreePlanReordering.Suggestion
        {
            Id = "move",
            Moves = [new DegreePlanReordering.Move(Topics, fall.Id, spring.Id)],
            Title = "Move CS 4973 to Spring 2027",
            Reasons = ["Offered in spring"],
        }, plan);
        var moved = plan.Terms.Single(term => term.Season == PlanSeason.Spring).Courses.Single();
        Assert.Equal("11111", moved.SelectedCrn);
        Assert.Empty(plan.Terms.Single(term => term.Season == PlanSeason.Fall).Courses);
    }

    [Fact]
    public void RepeatedCoursesRequireDistinctManualCrns()
    {
        var plan = new DegreePlan
        {
            Name = "Topics",
            Terms = [new DegreePlanTerm { Season = PlanSeason.Fall, Year = 2026 }],
        };
        var termId = plan.Terms[0].Id;
        Assert.True(DegreePlanEditing.Add(new DegreePlanCourse(Directed, "Directed Study", 4), termId, plan));
        Assert.False(DegreePlanEditing.Add(new DegreePlanCourse(Directed, "Directed Study", 4), termId, plan));
        Assert.False(DegreePlanEditing.Add(new DegreePlanCourse(Directed, "Directed Study", 4) { SelectedCrn = "11111" }, termId, plan));
        Assert.Single(plan.Terms[0].Courses);

        plan.Terms[0].Courses.Clear();
        Assert.True(DegreePlanEditing.Add(new DegreePlanCourse(Directed, "Directed Study", 4) { SelectedCrn = "11111" }, termId, plan));
        Assert.True(DegreePlanEditing.Add(new DegreePlanCourse(Directed, "Directed Study", 4) { SelectedCrn = "22222" }, termId, plan));
        Assert.False(DegreePlanEditing.Add(new DegreePlanCourse(Directed, "Directed Study", 4) { SelectedCrn = "22222" }, termId, plan));
        Assert.False(DegreePlanEditing.Add(new DegreePlanCourse(Directed, "Directed Study", 4), termId, plan));
        Assert.Equal(8, plan.Terms[0].Credits);
        Assert.Empty(DegreePlanAnalysis.DuplicateKeys(plan));

        var same = new DegreePlan
        {
            Terms =
            [
                new DegreePlanTerm
                {
                    Season = PlanSeason.Fall,
                    Year = 2026,
                    Courses =
                    [
                        new DegreePlanCourse(Directed, "Directed Study", 4) { SelectedCrn = "11111" },
                        new DegreePlanCourse(Directed, "Directed Study", 4) { SelectedCrn = "11111" },
                    ],
                },
            ],
        };
        Assert.Equal(new HashSet<CourseKey> { Directed }, DegreePlanAnalysis.DuplicateKeys(same));
    }

    [Fact]
    public void AuditNupathAndPrereqsCountEachPinnedEntry()
    {
        var pinned = new DegreePlan
        {
            Name = "Directed",
            Terms =
            [
                new DegreePlanTerm
                {
                    Season = PlanSeason.Fall,
                    Year = 2026,
                    Courses = [new DegreePlanCourse(Fundies, "Fundamentals", 4)],
                },
                new DegreePlanTerm
                {
                    Season = PlanSeason.Spring,
                    Year = 2027,
                    Courses =
                    [
                        new DegreePlanCourse(Directed, "Directed Study", 4) { SelectedCrn = "11111" },
                        new DegreePlanCourse(Directed, "Directed Study", 4) { SelectedCrn = "22222" },
                    ],
                },
            ],
        };
        var requirements = DegreeNUpathTests.Requirements();
        var attributes = DegreeNUpathTests.CourseAttributes(new Dictionary<string, NUPath[]> { ["CS 4990"] = [NUPath.Wi] });
        var audit = DegreeAudit.Run(pinned, requirements, attributes: attributes);
        var without = DegreeAudit.Run(WithoutDirected(pinned), requirements, attributes: attributes);
        Assert.Equal(8, audit.PlannedHours - without.PlannedHours);
        var writing = audit.NoteChecks.First(check => check.CheckKind is DegreeAudit.NoteCheck.Kind.Nupath)
            .Items.First(item => item.Id == NUPath.Wi.AttributeCode());
        Assert.Equal(["CS 4990", "CS 4990 (2)"], writing.Courses.OrderBy(id => id, StringComparer.Ordinal));

        var catalog = Index(
            [new Course { Subject = "CS", Number = "4990", Title = "Directed Study", MinCredits = 4, MaxCredits = 4, DetailsLoaded = true, Prereqs = Requisite.Course(Fundies) }],
            []);
        var review = new DegreePlanReview(pinned, new DegreePlanCatalogSnapshot([catalog], [catalog.Term], catalog.Term.Code));
        var directed = pinned.Terms[1].Courses;
        Assert.Equal(DegreePlanAnalysis.PrerequisiteStatus.Met, review.Status(directed[0]));
        Assert.Equal(DegreePlanAnalysis.PrerequisiteStatus.Met, review.Status(directed[1]));
        Assert.Null(review.Note(directed[0]));
        Assert.Null(review.Note(directed[1]));
        Assert.Equal(0, review.DuplicateCount);

        var repeated = new DegreePlan
        {
            Terms =
            [
                new DegreePlanTerm
                {
                    Season = PlanSeason.Spring,
                    Year = 2027,
                    Courses =
                    [
                        new DegreePlanCourse(Directed, "Directed Study", 4),
                        new DegreePlanCourse(Directed, "Directed Study", 4),
                    ],
                },
            ],
        };
        var collapsed = DegreeAudit.Run(repeated, new ProgramRequirements());
        Assert.Equal(4, collapsed.PlannedHours);
        Assert.Equal(new HashSet<CourseKey> { Directed }, DegreePlanAnalysis.DuplicateKeys(repeated));
    }

    [Fact]
    public void OpeningASemesterCopiesEveryPin()
    {
        var path = Path.Combine(Path.GetTempPath(), $"UserData-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserDataStore(path);
            var semester = new DegreePlanTerm
            {
                Season = PlanSeason.Spring,
                Year = 2027,
                Courses =
                [
                    new DegreePlanCourse(Topics, "Directed Study", 4) { SelectedCrn = "11111" },
                    new DegreePlanCourse(Topics, "Directed Study", 4) { SelectedCrn = "22222" },
                    new DegreePlanCourse(Math, "Linear Algebra", 4),
                ],
            };
            var built = DegreePlanSchedule.ForSemester(semester, "202730", store);
            Assert.NotNull(built);
            Assert.Equal(["CS 4973", "MATH 2331"], built.Value.Plan.Courses.Select(course => course.Key.ToString()));
            Assert.Equal(["11111", "22222"], built.Value.Plan.SelectedCRNs);
            var again = DegreePlanSchedule.ForSemester(semester, "202730", store);
            Assert.NotNull(again);
            Assert.False(again.Value.IsNew);
            Assert.Equal(["11111", "22222"], again.Value.Plan.SelectedCRNs);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AScheduleWithTwoPinsBecomesTwoDegreeCourses()
    {
        var index = TopicsIndex();
        var schedule = Schedule(["CS 4973"], ["11111", "22222"]);
        schedule.IsPrimary = true;
        schedule.TermCode = "202730";
        var draft = DegreePlanAutofill.NewDegreePlanDraft("Next", null, [schedule], code => code == "202730" ? index : null, new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));
        var courses = draft.Terms.SelectMany(term => term.Courses).ToList();
        Assert.Equal(2, courses.Count);
        Assert.All(courses, course => Assert.Equal(Topics, course.Key));
        Assert.Equal(["11111", "22222"], courses.Select(course => course.SelectedCrn).OrderBy(crn => crn, StringComparer.Ordinal));
    }

    [Fact]
    public void SelectedCrnSurvivesASave()
    {
        var plan = new DegreePlan
        {
            Name = "Saved",
            Terms =
            [
                new DegreePlanTerm
                {
                    Season = PlanSeason.Fall,
                    Year = 2026,
                    Courses = [new DegreePlanCourse(Topics, "Directed Study", 4) { SelectedCrn = "11111" }],
                },
            ],
        };
        var json = JsonSerializer.Serialize(plan, AppJson.UserData);
        var loaded = JsonSerializer.Deserialize<DegreePlan>(json, AppJson.UserData);
        Assert.Equal("11111", loaded?.Terms[0].Courses[0].SelectedCrn);
    }

    [Fact]
    public void TwoSelectedSectionsCountTheirCreditsSeparately()
    {
        var index = TopicsIndex();
        var plan = Schedule(["CS 4973"], ["11111", "22222"]);
        var resolved = new ResolvedPlan(plan, index);
        Assert.Equal(8, resolved.Credits.Min);
        Assert.Equal(2, resolved.SelectedSections.Count);
    }

    static DegreePlan WithoutDirected(DegreePlan plan)
    {
        var copy = new DegreePlan { Name = plan.Name, Terms = plan.Terms.Select(term => new DegreePlanTerm
        {
            Season = term.Season,
            Year = term.Year,
            Courses = term.Courses.Where(course => course.Key != Directed).ToList(),
        }).ToList() };
        return copy;
    }

    static CatalogIndex TopicsIndex() => Index(
        [Course("CS", "4973", topics: true)],
        [
            Section("11111", "CS", "4973", [1], 10, 11.5),
            Section("22222", "CS", "4973", [3], 10, 11.5),
        ]);

    static SchedulePlan Schedule(string[] codes, string[]? selected = null)
    {
        var plan = new SchedulePlan { Name = "Plan A", TermCode = "202710" };
        foreach (var code in codes)
            plan.Courses.Add(new PlanCourse { Key = CourseKey.Parse(code)!.Value });
        if (selected is not null) plan.SelectedCRNs = selected.ToList();
        return plan;
    }

    static Course Course(string subject, string number, bool topics = false) => new()
    {
        Subject = subject,
        Number = number,
        Title = $"{subject} {number}",
        MinCredits = 4,
        MaxCredits = 4,
        DetailsLoaded = true,
        SpecialTopics = topics,
    };

    static CourseSection Section(string crn, string subject, string number, int[] days, double start, double end) => new()
    {
        Crn = crn,
        Subject = subject,
        Number = number,
        Title = $"{subject} {number}",
        SectionNumber = "01",
        PartOfTerm = "1",
        SeatCapacity = 40,
        SeatsRemaining = 10,
        Enrollment = 30,
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

    static CatalogIndex Index(Course[] courses, CourseSection[] sections) => CatalogIndex.Build(new TermCatalog
    {
        Term = new Term("202710", "Fall 2026 Semester"),
        Courses = courses.ToList(),
        Sections = sections.ToList(),
    });
}
