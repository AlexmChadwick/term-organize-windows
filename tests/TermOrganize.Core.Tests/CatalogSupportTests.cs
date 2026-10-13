// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class CatalogSupportTests
{
    private static readonly Lazy<CatalogIndex> Index = new(() => CatalogIndex.Build(BundledCatalog.Load()));

    [Fact]
    public void DayPresetsDescribeTheirDays()
    {
        Assert.Equal(SearchDayPreset.Any, SearchDayPresetInfo.Selection(null));
        Assert.Equal(SearchDayPreset.NoFridays, SearchDayPresetInfo.Selection(DaySet.Monday | DaySet.Tuesday | DaySet.Wednesday | DaySet.Thursday));
        Assert.Equal(SearchDayPreset.Tf, SearchDayPresetInfo.Selection(DaySet.Tuesday | DaySet.Friday));
        Assert.Equal(SearchDayPreset.Custom, SearchDayPresetInfo.Selection(DaySet.Monday | DaySet.Friday));
        Assert.Null(SearchDayPresetInfo.Matching(DaySet.Monday | DaySet.Friday));
        foreach (var preset in SearchDayPresetInfo.All.Where(preset => preset != SearchDayPreset.Custom))
            Assert.Equal(preset, SearchDayPresetInfo.Selection(preset.Days()));
    }

    [Fact]
    public void FilterChipTitles()
    {
        Assert.Equal("Days", SearchFilterTitles.Days(null));
        Assert.Equal("No Fridays", SearchFilterTitles.Days(DaySet.Monday | DaySet.Tuesday | DaySet.Wednesday | DaySet.Thursday));
        Assert.Equal("MF only", SearchFilterTitles.Days(DaySet.Monday | DaySet.Friday));
        Assert.Equal("No set days", SearchFilterTitles.Days(DaySet.None));
        Assert.Equal("Subject", SearchFilterTitles.Subjects(new HashSet<string>()));
        Assert.Equal("CS", SearchFilterTitles.Subjects(new HashSet<string> { "CS" }));
        Assert.Equal("3 subjects", SearchFilterTitles.Subjects(new HashSet<string> { "CS", "DS", "MATH" }));
        Assert.Equal("FQ, WI", SearchFilterTitles.Nupaths(new HashSet<NUPath> { NUPath.Fq, NUPath.Wi }));
        Assert.Equal("NUpath · 3", SearchFilterTitles.Nupaths(new HashSet<NUPath> { NUPath.Fq, NUPath.Wi, NUPath.Ce }));
        Assert.Equal("Under 1000", SearchFilterTitles.Level(0));
        Assert.Equal("3000", SearchFilterTitles.Level(3));
    }

    [Fact]
    public void TimeOfDayConversionRoundTrips()
    {
        foreach (var minutes in new[] { 0, 9 * 60, 12 * 60 + 35, 17 * 60, 23 * 60 + 59 })
            Assert.Equal(minutes, SearchTimeOfDay.Minutes(SearchTimeOfDay.Date(minutes)));
        Assert.Equal(0, SearchTimeOfDay.Minutes(SearchTimeOfDay.Date(-30)));
        Assert.Equal(24 * 60 - 1, SearchTimeOfDay.Minutes(SearchTimeOfDay.Date(5_000)));
    }

    [Fact]
    public void QuickSearchesApplyExactlyOneFilter()
    {
        foreach (var preset in HomeQuickSearchInfo.All)
        {
            Assert.Equal(1, preset.Filters().ActiveCount);
            Assert.False(string.IsNullOrEmpty(preset.Symbol()));
        }
        Assert.True(HomeQuickSearch.OpenSeats.Filters().OpenSeatsOnly);
        Assert.True(HomeQuickSearch.Honors.Filters().HonorsOnly);
        Assert.Equal(InstructionFilter.Online, HomeQuickSearch.Online.Filters().Instruction);
        Assert.Equal(DaySet.Monday | DaySet.Tuesday | DaySet.Wednesday | DaySet.Thursday, HomeQuickSearch.NoFridays.Filters().AllowedDays);
        Assert.Equal(new HashSet<int> { 1 }, HomeQuickSearch.FirstYear.Filters().Levels);
    }

    [Fact]
    public void QuickSearchesFindCourses()
    {
        foreach (var preset in HomeQuickSearchInfo.All)
        {
            var response = SearchEngine.Search(new SearchQuery { Filters = preset.Filters() }, Index.Value);
            Assert.NotEmpty(response.Results);
        }
    }

    [Fact]
    public void BrowseSummaryLines()
    {
        Assert.Equal("42 courses", BrowseSummary.Text(42, 42, 0));
        Assert.Equal("1 course", BrowseSummary.Text(1, 1, 0));
        Assert.Equal("12 of 42 courses · 9 with open seats", BrowseSummary.Text(12, 42, 9));
        Assert.Equal("9 of 42 courses", BrowseSummary.Text(9, 42, 9, openOnly: true));
        Assert.Equal("2 professors", BrowseSummary.Text(2, 2, 0, noun: "professor"));
        Assert.Equal("1000-level", BrowseSummary.LevelTitle(1));
        Assert.Equal("Under 1000", BrowseSummary.LevelTitle(0));
        Assert.Equal("3000-level courses", BrowseSummary.LevelNoun(3));
        Assert.Equal("courses under 1000", BrowseSummary.LevelNoun(0));
    }

    [Fact]
    public void PopularSubjectsAreTheBusiest()
    {
        var index = Index.Value;
        var popular = index.Popular(12);
        Assert.Equal(12, popular.Count);
        var counts = popular.Select(subject => index.CourseCountBySubject.GetValueOrDefault(subject.Code)).ToList();
        Assert.Equal(counts.OrderByDescending(count => count), counts);
        var busiestOutside = index.ActiveSubjects
            .Where(subject => popular.All(item => item.Code != subject.Code))
            .Select(subject => index.CourseCountBySubject.GetValueOrDefault(subject.Code))
            .DefaultIfEmpty()
            .Max();
        Assert.True(counts[^1] >= busiestOutside);
        Assert.Equal(popular.Take(3).Select(subject => subject.Code), index.Popular(3).Select(subject => subject.Code));
    }

    [Fact]
    public void SearchExamplesAllFindSomething()
    {
        var index = Index.Value;
        var examples = SearchExampleCache.Examples(index);
        Assert.True(examples.Count >= 3);
        foreach (var example in examples)
        {
            var response = SearchEngine.Search(example.Title, index);
            Assert.True(response.Results.Count > 0 || response.CrnMatch is not null || response.Instructors.Count > 0);
        }
    }

    [Fact]
    public void DegreePlanStandingTexts()
    {
        var plan = Guid.NewGuid();
        var completed = new CourseDegreePlanStanding { PlanId = plan, Standing = new CourseDegreePlanStanding.Kind.Completed(null) };
        Assert.Equal("Completed", completed.Text);
        Assert.Equal("checkmark.circle.fill", completed.Symbol);
        Assert.Equal("Completed, in your degree plan", completed.SpokenText);
        var via = new CourseDegreePlanStanding { PlanId = plan, Standing = new CourseDegreePlanStanding.Kind.Completed(new CourseKey("CS", "2510")) };
        Assert.Equal("Completed via CS 2510", via.Text);
        var planned = new CourseDegreePlanStanding { PlanId = plan, Standing = new CourseDegreePlanStanding.Kind.Planned("Spring 2027", null) };
        Assert.Equal("Planned · Spring 2027", planned.Text);
        Assert.Equal("calendar", planned.Symbol);
        var plannedVia = new CourseDegreePlanStanding { PlanId = plan, Standing = new CourseDegreePlanStanding.Kind.Planned("Spring 2027", new CourseKey("CS", "2510")) };
        Assert.Equal("Planned via CS 2510 · Spring 2027", plannedVia.Text);
        Assert.Equal("Planned via CS 2510 for Spring 2027 in your degree plan", plannedVia.SpokenText);
    }

    [Fact]
    public void CourseIsInScheduleCountsOnlyThePlansOfTheTerm()
    {
        var path = Path.Combine(Path.GetTempPath(), $"CatalogSupportTests-{Guid.NewGuid():N}.json");
        var store = new UserDataStore(path);
        var plan = store.CreatePlan("Fall", "202710");
        store.UpdatePlan(plan.Id, item => item.Courses.Add(new PlanCourse { Key = new CourseKey("CS", "3000") }));
        store.CreatePlan("Spring", "202730");
        Assert.True(store.CourseIsInSchedule("202710", new CourseKey("CS", "3000")));
        Assert.False(store.CourseIsInSchedule("202730", new CourseKey("CS", "3000")));
        File.Delete(path);
    }
}
