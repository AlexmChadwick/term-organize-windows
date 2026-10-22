// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

/// <summary>
/// A degree plan may list one course twice. The plan warns, a substitution names one row,
/// and schedule sync keeps each row.
/// </summary>
public class DegreeDuplicateRuleTests
{
    static readonly CourseKey Directed = new("CS", "4990");

    [Fact]
    public void ASubstitutionCountsOneRowAndLeavesTheOther()
    {
        var requirements = DegreeRequirementsTests.Requirements();
        var electives = requirements.AllRules.First(rule => rule.Label.StartsWith("Complete 8 semester hours", StringComparison.Ordinal));
        var first = new DegreePlanCourse(Directed, "Directed Study", 4);
        var second = new DegreePlanCourse(Directed, "Directed Study", 4);
        var plan = new DegreePlan
        {
            Name = "Twice",
            Terms = [new DegreePlanTerm { Season = PlanSeason.Fall, Year = 2026, Courses = [first, second] }],
        };
        Assert.Equal(new HashSet<CourseKey> { Directed }, DegreePlanAnalysis.DuplicateKeys(plan));
        Assert.Equal("1 course appears more than once in this plan. Each copy still counts.", DegreePlanAnalysis.RepeatHeader(plan));
        Assert.Equal(8, DegreeAudit.Run(plan, requirements).PlannedHours);

        plan.Substitutions =
        [
            new RequirementSubstitution
            {
                RuleID = electives.Id,
                RuleTitle = electives.DisplayLabel,
                Change = SubstitutionChange.CountCourse(Directed, second.Id),
            },
        ];
        var audit = DegreeAudit.Run(plan, requirements);
        var used = audit.Result(electives.Id)!.Used;
        Assert.Contains(used, use => use.RowId == second.Id && use.BySubstitution);
        Assert.Contains(used, use => use.RowId == first.Id && !use.BySubstitution);
        Assert.Equal(8, audit.PlannedHours);
        Assert.Equal(2, plan.DegreeCourseRows().Count(row => row.Key == Directed));
        Assert.Equal($"CS 4990 Directed Study \u00b7 Fall 2026 (2)", plan.DuplicateRowLabel(plan.DegreeCourseRows()[1]));
        Assert.Equal(["CS 4990", "CS 4990 (2)"], audit.Result(electives.Id)!.Used.Select(use => use.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryCreditedCopyCountsAndRepeatsStillWarn()
    {
        var seminar = new CourseKey("CS", "4973");
        var open = Plan(seminar, "Seminar", null, null);
        var openAudit = DegreeAudit.Run(open, new ProgramRequirements());
        Assert.Equal(8, openAudit.PlannedHours);
        Assert.Equal(["CS 4973", "CS 4973 (2)"], openAudit.Unused.Select(use => use.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(new HashSet<CourseKey> { seminar }, DegreePlanAnalysis.DuplicateKeys(open));
        Assert.Equal("Already in Fall 2026. Adding it again keeps both copies.", DegreePlanAnalysis.AddRepeatNote(open, seminar, open.Terms[0]));

        var same = Plan(seminar, "Seminar", "11111", "11111");
        Assert.Equal(8, DegreeAudit.Run(same, new ProgramRequirements()).PlannedHours);
        Assert.Equal(new HashSet<CourseKey> { seminar }, DegreePlanAnalysis.DuplicateKeys(same));
        Assert.Equal("CS 4973 appears more than once. Each copy still counts.", DegreePlanAnalysis.RepeatMessage(seminar));

        var distinct = Plan(seminar, "Seminar", "11111", "22222");
        Assert.Equal(8, DegreeAudit.Run(distinct, new ProgramRequirements()).PlannedHours);
        Assert.Empty(DegreePlanAnalysis.DuplicateKeys(distinct));
        Assert.Null(DegreePlanAnalysis.RepeatHeader(distinct));

        var topics = Plan(seminar, "Topics in Computer Science", null, null);
        Assert.Equal(8, DegreeAudit.Run(topics, new ProgramRequirements()).PlannedHours);
        Assert.Empty(DegreePlanAnalysis.DuplicateKeys(topics));

        var second = new DegreePlan
        {
            Terms = [new DegreePlanTerm { Season = PlanSeason.Spring, Year = 2027, Courses = [new DegreePlanCourse(new CourseKey("EECE", "2160"), "Embedded", 4), new DegreePlanCourse(new CourseKey("EECE", "2160"), "Embedded", 4)] }],
        };
        Assert.Equal("2 courses appear more than once in this plan. Each copy still counts.", DegreePlanAnalysis.RepeatHeader(new DegreePlan
        {
            Terms = [open.Terms[0], second.Terms[0]],
        }));
    }

    private static DegreePlan Plan(CourseKey key, string title, string? firstCrn, string? secondCrn) => new()
    {
        Name = "Seminars",
        Terms =
        [
            new DegreePlanTerm
            {
                Season = PlanSeason.Fall,
                Year = 2026,
                Courses =
                [
                    new DegreePlanCourse(key, title, 4) { SelectedCrn = firstCrn },
                    new DegreePlanCourse(key, title, 4) { SelectedCrn = secondCrn },
                ],
            },
        ],
    };

    [Fact]
    public void ARowIdSurvivesSubstitutionJson()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var change = SubstitutionChange.CountCourse(Directed, id);
        var json = AppJson.Serialize(change, AppJson.UserData);
        var loaded = AppJson.Deserialize<SubstitutionChange>(json, AppJson.UserData);
        Assert.Equal(change, loaded);
        Assert.Contains("\"row\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ScheduleSyncKeepsEachDegreeRow()
    {
        var first = new PlanCourse { Key = Directed };
        var second = new PlanCourse { Key = Directed, PinnedCrn = "11111" };
        var schedule = new SchedulePlan
        {
            Name = "Fall",
            TermCode = "202710",
            IsPrimary = true,
            Courses = [first, second],
        };
        var draft = DegreePlanAutofill.NewDegreePlanDraft(
            "Draft",
            null,
            [schedule],
            _ => null,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var rows = draft.Terms.SelectMany(term => term.Courses).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(Directed, row.Key));
        Assert.Equal([null, "11111"], rows.Select(row => row.SelectedCrn));

        var path = Path.Combine(Path.GetTempPath(), $"UserData-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserDataStore(path);
            var semester = draft.Terms.Single();
            var built = DegreePlanSchedule.ForSemester(semester, "202710", store);
            Assert.NotNull(built);
            Assert.Equal(2, built.Value.Plan.Courses.Count);
            Assert.Equal(semester.Courses[0].Id, built.Value.Plan.Courses[0].DegreeRowId);
            Assert.Equal("11111", built.Value.Plan.Courses[1].PinnedCrn);
            var again = DegreePlanSchedule.ForSemester(semester, "202710", store);
            Assert.False(again!.Value.IsNew);
            Assert.Equal(2, again.Value.Plan.Courses.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
