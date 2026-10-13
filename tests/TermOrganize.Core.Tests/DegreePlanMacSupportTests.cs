// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

/// <summary>Dropping a course, building a schedule from a semester, and listing a requirement.</summary>
public class DegreePlanMacSupportTests
{
    static DegreePlanCourse Course(string code, double credits = 4) =>
        new(CourseKey.Parse(code), code, credits);

    static List<string> Codes(DegreePlanTerm term) =>
        term.Courses.Select(course => course.Key?.ToString() ?? course.Label).ToList();

    static DegreePlan Plan() => new()
    {
        Name = "Plan",
        Terms =
        [
            new DegreePlanTerm
            {
                Season = PlanSeason.Spring,
                Year = 2027,
                Courses = [Course("CS 3000"), Course("CS 3500"), Course("CS 3650")],
            },
            new DegreePlanTerm
            {
                Season = PlanSeason.Fall,
                Year = 2027,
                Courses = [Course("CS 4400")],
            },
        ],
    };

    static List<List<string>> Snapshot(DegreePlan plan) => plan.Terms.Select(Codes).ToList();

    [Fact]
    public void ReordersWithinASemester()
    {
        var plan = Plan();
        var spring = plan.Terms[0];
        var a = spring.Courses[0].Id;
        var b = spring.Courses[1].Id;
        var c = spring.Courses[2].Id;

        DegreePlanEditing.Move(a, spring.Id, 3, plan);
        Assert.Equal(["CS 3500", "CS 3650", "CS 3000"], Codes(plan.Terms[0]));
        DegreePlanEditing.Move(a, spring.Id, 1, plan);
        Assert.Equal(["CS 3500", "CS 3000", "CS 3650"], Codes(plan.Terms[0]));
        DegreePlanEditing.Move(c, spring.Id, 0, plan);
        Assert.Equal(["CS 3650", "CS 3500", "CS 3000"], Codes(plan.Terms[0]));

        DegreePlanEditing.Move(b, spring.Id, 1, plan);
        Assert.Equal(["CS 3650", "CS 3500", "CS 3000"], Codes(plan.Terms[0]));
        DegreePlanEditing.Move(b, spring.Id, 2, plan);
        Assert.Equal(["CS 3650", "CS 3500", "CS 3000"], Codes(plan.Terms[0]));
    }

    [Fact]
    public void MovesToAPositionInAnotherSemester()
    {
        var plan = Plan();
        var spring = plan.Terms[0];
        var fall = plan.Terms[1];
        var b = spring.Courses[1].Id;

        DegreePlanEditing.Move(b, fall.Id, 0, plan);
        Assert.Equal(["CS 3000", "CS 3650"], Codes(plan.Terms[0]));
        Assert.Equal(["CS 3500", "CS 4400"], Codes(plan.Terms[1]));

        var a = plan.Terms[0].Courses[0].Id;
        DegreePlanEditing.Move(a, fall.Id, 99, plan);
        Assert.Equal(["CS 3500", "CS 4400", "CS 3000"], Codes(plan.Terms[1]));
        var c = plan.Terms[0].Courses[0].Id;
        DegreePlanEditing.Move(c, fall.Id, -5, plan);
        Assert.Equal(["CS 3650", "CS 3500", "CS 4400", "CS 3000"], Codes(plan.Terms[1]));
        Assert.Empty(plan.Terms[0].Courses);
    }

    [Fact]
    public void AddsADraggedCourseAtAPosition()
    {
        var plan = Plan();
        var spring = plan.Terms[0];
        void Drop(string code, int position)
        {
            var added = Course(code);
            DegreePlanEditing.Add(added, spring.Id, plan);
            DegreePlanEditing.Move(added.Id, spring.Id, position, plan);
        }

        Drop("CS 4500", 1);
        Assert.Equal(["CS 3000", "CS 4500", "CS 3500", "CS 3650"], Codes(plan.Terms[0]));
        Drop("CS 4530", 0);
        Assert.Equal(["CS 4530", "CS 3000", "CS 4500", "CS 3500", "CS 3650"], Codes(plan.Terms[0]));
        Drop("CS 4535", plan.Terms[0].Courses.Count);
        Assert.Equal(["CS 4530", "CS 3000", "CS 4500", "CS 3500", "CS 3650", "CS 4535"], Codes(plan.Terms[0]));
        Assert.Equal(["CS 4400"], Codes(plan.Terms[1]));
    }

    [Fact]
    public void IgnoresACourseOrSemesterThatIsGone()
    {
        var plan = Plan();
        var before = Snapshot(plan);
        DegreePlanEditing.Move(Guid.NewGuid(), plan.Terms[1].Id, 0, plan);
        DegreePlanEditing.Move(plan.Terms[0].Courses[0].Id, Guid.NewGuid(), 0, plan);
        Assert.Equal(before, Snapshot(plan));
    }

    [Fact]
    public void BuildsAScheduleFromASemesterOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), $"UserData-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserDataStore(path);
            var semester = Plan().Terms[0];
            var first = store.ScheduleFor(semester);
            Assert.NotNull(first);
            Assert.True(first.Value.IsNew);
            Assert.Equal(3, first.Value.CourseCount);
            Assert.Equal("Spring 2027 plan", first.Value.Plan.Name);
            Assert.Equal(["CS 3000", "CS 3500", "CS 3650"], store.Plan(first.Value.Plan.Id)?.Courses.Select(course => course.Key.ToString()));

            var again = store.ScheduleFor(semester);
            Assert.NotNull(again);
            Assert.False(again.Value.IsNew);
            Assert.Equal(first.Value.Plan.Id, again.Value.Plan.Id);
            Assert.Single(store.Plans);

            var empty = new DegreePlanTerm
            {
                Season = PlanSeason.SummerA,
                Year = 2027,
                Courses = [new DegreePlanCourse(null, "", 0, "NO AP CREDIT")],
            };
            Assert.Null(DegreePlanSchedule.ForSemester(empty, "202740", store));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SummarizesAPlanForTheOverview()
    {
        var plan = new DegreePlan { Name = "Plan" };
        Assert.Equal("No completed courses yet", plan.CompletedLine());
        Assert.Equal("No semesters planned yet", plan.NextLine());

        plan.Completed = [Course("CS 1800"), Course("CS 1802", 1)];
        Assert.Equal("5 credits completed", plan.CompletedLine());

        plan.Terms = [new DegreePlanTerm { Season = PlanSeason.Fall, Year = 2001, Courses = [Course("CS 1800")] }];
        Assert.Equal("No upcoming semesters", plan.NextLine());
        plan.Terms.Add(new DegreePlanTerm { Season = PlanSeason.Spring, Year = 2099 });
        Assert.Equal("No courses planned yet", plan.NextLine());
        plan.Terms[1].Courses = [Course("CS 3000"), Course("CS 3500", 5)];
        Assert.Equal("Spring 2099 \u00b7 9 credits \u00b7 2 courses", plan.NextSummary());
        Assert.Equal("Next: Spring 2099 \u00b7 9 credits \u00b7 2 courses", plan.NextLine());
    }

    [Fact]
    public void LaysRequirementsOutLikeTheCatalog()
    {
        var requirements = DegreeRequirementsTests.Requirements();
        var audit = DegreeAudit.Run(DegreeRequirementsTests.Plan(), requirements);
        var either = audit.RuleResults.First(result => result.Rule.Label == "CS 4500 or CS 4530");
        Assert.False(DegreeRequirementsListing.HasHeaderRow(either));
        Assert.False(DegreeRequirementsListing.IsReplaced(either));
        var option = Assert.IsType<RequirementKind.Required>(either.Rule.Kind).Option;
        var alternatives = DegreeRequirementsListing.Entries(option);
        Assert.Equal(
            new[] { new[] { new CourseKey("CS", "4500") }, new[] { new CourseKey("CS", "4530") } }.Select(keys => keys.ToList()).ToList(),
            alternatives.Select(entry => entry.Courses).ToList());

        var security = audit.RuleResults.First(result => result.Rule.Label.StartsWith("Complete one of the following", StringComparison.Ordinal));
        Assert.True(DegreeRequirementsListing.HasHeaderRow(security));
        var (options, ranges) = DegreeRequirementsListing.Listing(security.Rule);
        Assert.Equal(2, options.Count);
        Assert.Empty(ranges);

        var science = audit.RuleResults.First(result => result.Rule.Label.StartsWith("Complete two courses", StringComparison.Ordinal));
        var listing = DegreeRequirementsListing.Listing(science.Rule);
        var listed = listing.Options.SelectMany(item => item.AllCourses).ToHashSet();
        var entries = DegreeRequirementsListing.Entries(listing.Options, science.Rule, _ => null);
        Assert.Equal(
            ["0 Biology", "1 Mathematics"],
            entries.Where(entry => entry.Category is not null).Select(entry => $"{entry.Id} {entry.Category}"));
        var counted = new DegreeAudit.CourseUse { Key = new CourseKey("BIOL", "1112"), Title = "", Credits = 1, Order = 0 };
        Assert.True(DegreeRequirementsListing.IsShown(counted, listed));
        var other = new DegreeAudit.CourseUse { Key = new CourseKey("CS", "4973"), Title = "", Credits = 4, Order = 1 };
        Assert.False(DegreeRequirementsListing.IsShown(other, listed));
    }

    [Fact]
    public void LongListsKeepTheOptionsThatMatter()
    {
        static DegreeListEntry Entry(int id) => new(id, [[new CourseKey("CS", (4000 + id).ToString())]]);
        var entries = Enumerable.Range(0, 10).Select(Entry).ToList();
        Assert.Equal([0, 1, 2, 3, 7], DegreeRequirementsListing.Collapsed(entries, 3, entry => entry.Id is 2 or 7).Select(entry => entry.Id));
        Assert.Equal([2, 7], DegreeRequirementsListing.Collapsed(entries, 0, entry => entry.Id is 2 or 7).Select(entry => entry.Id));
    }

    [Fact]
    public void FindsPlanCoursesByCodeOrTitle()
    {
        var choices = new List<DegreePlanCourseChoice>
        {
            new(new CourseKey("CS", "3000"), "Algorithms and Data", 4),
            new(new CourseKey("MATH", "1341"), "Calculus 1", 4, "Spring 2027", "Spring 2027"),
        };
        Assert.Equal(2, DegreeSubstitutionEditing.Matching(choices, "").Count);
        Assert.Equal(["CS 3000"], DegreeSubstitutionEditing.Matching(choices, "cs 3000").Select(choice => choice.Key.ToString()));
        Assert.Equal(["MATH 1341"], DegreeSubstitutionEditing.Matching(choices, "math1341").Select(choice => choice.Key.ToString()));
        Assert.Equal(["MATH 1341"], DegreeSubstitutionEditing.Matching(choices, "calculus").Select(choice => choice.Key.ToString()));
        Assert.Empty(DegreeSubstitutionEditing.Matching(choices, "chemistry"));
    }
}

static class DegreeScheduleStore
{
    public static DegreeScheduleBuild? ScheduleFor(this UserDataStore store, DegreePlanTerm semester) =>
        DegreePlanSchedule.ForSemester(semester, semester.BannerCode(), store);
}
