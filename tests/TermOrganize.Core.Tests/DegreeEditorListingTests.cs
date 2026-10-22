// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

/// <summary>
/// The degree plan editor can place the same subject and number on one term twice
/// when each row has its own manually picked CRN. The screen calls these methods.
/// </summary>
public class DegreeEditorListingTests
{
    static readonly CourseKey Directed = new("CS", "4990");

    [Fact]
    public void TheEditorAddsTheSameCourseTwiceOnOneTermWhenEachRowHasACrn()
    {
        var fall = Term(PlanSeason.Fall, 2026);
        var plan = new DegreePlan { Name = "Topics", Terms = [fall, Term(PlanSeason.Spring, 2027)] };

        Assert.Null(DegreeCourseEntries.BlockReason(plan, Directed, "11111"));
        Assert.True(DegreePlanEditing.Add(Row("11111"), fall.Id, plan));
        Assert.True(DegreePlanEditing.SetSelectedCrn(plan.Terms[0].Courses[0].Id, "11111", plan));

        var same = DegreePlanEditing.TermWith(Directed, plan);
        Assert.Equal(fall.Id, same?.Id);
        Assert.Null(DegreeCourseEntries.BlockReason(plan, Directed, "22222"));
        Assert.True(DegreePlanEditing.AddCourse(Row("22222"), same!, plan));

        var rows = plan.Terms.Single(term => term.Id == fall.Id).Courses;
        Assert.Equal(["11111", "22222"], rows.Select(course => course.SelectedCrn));
        Assert.Equal(8, plan.Terms.Single(term => term.Id == fall.Id).Credits);
        Assert.Empty(plan.Terms.Single(term => term.Season == PlanSeason.Spring).Courses);
        Assert.Empty(DegreePlanAnalysis.DuplicateKeys(plan));
    }

    [Fact]
    public void ASecondRowWithoutItsOwnCrnIsRefused()
    {
        var fall = Term(PlanSeason.Fall, 2026);
        var plan = new DegreePlan { Terms = [fall] };
        Assert.True(DegreePlanEditing.Add(Row(null), fall.Id, plan));

        Assert.Equal(
            "CS 4990 is already on this plan. A second listing needs its own CRN.",
            DegreeCourseEntries.BlockReason(plan, Directed, "  "));
        Assert.False(DegreePlanEditing.Add(Row(null), fall.Id, plan));
        Assert.False(DegreePlanEditing.AddCourse(Row("22222"), fall, plan));
        Assert.Equal(
            "CS 4990 is already on this plan. Set a CRN on each existing listing before adding another.",
            DegreeCourseEntries.BlockReason(plan, Directed, "22222"));
        Assert.Single(plan.Terms[0].Courses);
    }

    [Fact]
    public void TheSameCrnCannotBeSavedTwice()
    {
        var fall = Term(PlanSeason.Fall, 2026);
        var plan = new DegreePlan { Terms = [fall] };
        Assert.True(DegreePlanEditing.Add(Row("11111"), fall.Id, plan));
        var first = plan.Terms[0].Courses[0].Id;

        Assert.Null(DegreeCourseEntries.BlockReason(plan, Directed, "22222"));
        Assert.True(DegreePlanEditing.Add(Row("22222"), fall.Id, plan));
        Assert.Equal("CS 4990 already uses CRN 22222.", DegreeCourseEntries.BlockReason(plan, Directed, " 22222 "));
        Assert.False(DegreePlanEditing.Add(Row("22222"), fall.Id, plan));
        Assert.False(DegreePlanEditing.SetSelectedCrn(first, "22222", plan));
        Assert.Equal("11111", plan.Terms[0].Courses[0].SelectedCrn);
        Assert.Equal(2, plan.Terms[0].Courses.Count);
    }

    [Fact]
    public void TheOnlyRowCanTakeOrClearACrn()
    {
        var fall = Term(PlanSeason.Fall, 2026);
        var plan = new DegreePlan { Terms = [fall] };
        Assert.True(DegreePlanEditing.Add(Row(null), fall.Id, plan));
        var id = plan.Terms[0].Courses[0].Id;

        Assert.True(DegreePlanEditing.SetSelectedCrn(id, " 11111 ", plan));
        Assert.Equal("11111", plan.Terms[0].Courses[0].SelectedCrn);
        Assert.True(DegreePlanEditing.SetSelectedCrn(id, "", plan));
        Assert.Null(plan.Terms[0].Courses[0].SelectedCrn);
        Assert.False(DegreePlanEditing.SetSelectedCrn(Guid.NewGuid(), "33333", plan));
    }

    static DegreePlanCourse Row(string? crn) =>
        new(Directed, "Directed Study", 4) { SelectedCrn = crn };

    static DegreePlanTerm Term(PlanSeason season, int year) => new() { Season = season, Year = year };
}
