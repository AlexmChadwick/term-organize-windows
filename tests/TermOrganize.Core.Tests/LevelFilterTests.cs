// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class LevelFilterTests
{
    [Fact]
    public void UndergradStopsBelow5000AndCombinesWithRequisiteFilters()
    {
        var under = Course("CS", "4999", loaded: true);
        var boundary = Course("CS", "5000", loaded: true);
        var suffix = Course("CS", "5000A", loaded: true);
        var withPrereq = Course("CS", "2500", loaded: true, prereqs: Requisite.Course(new CourseKey("CS", "1800")));
        var unknown = Course("CS", "1800", loaded: false);

        Assert.Equal(4999, under.Key.NumericValue);
        Assert.Equal(5000, boundary.Key.NumericValue);
        Assert.Equal(5000, suffix.Key.NumericValue);

        var index = CatalogIndex.Build(new TermCatalog
        {
            Term = new Term("202730", "Spring 2027 Semester"),
            Courses = [under, boundary, suffix, withPrereq, unknown],
            Sections =
            [
                Section("1", under),
                Section("2", boundary),
                Section("3", suffix),
                Section("4", withPrereq),
                Section("5", unknown),
            ],
        });

        var undergrad = new SearchFilters { Division = CourseDivision.Undergrad };
        Assert.Equal(["CS 1800", "CS 2500", "CS 4999"], Codes(SearchEngine.Search("", index, undergrad)));
        Assert.Equal(1, undergrad.ActiveCount);
        Assert.False(undergrad.IsEmpty);

        var grad = new SearchFilters { Division = CourseDivision.Grad };
        Assert.Equal(["CS 5000", "CS 5000A"], Codes(SearchEngine.Search("", index, grad)));

        var both = new SearchFilters { Division = CourseDivision.Undergrad, NoPrerequisites = true };
        Assert.Equal(["CS 4999"], Codes(SearchEngine.Search("", index, both)));
        Assert.Equal(2, both.ActiveCount);

        Assert.Equal(["CS 1800", "CS 2500", "CS 4999", "CS 5000", "CS 5000A"], Codes(SearchEngine.Search("", index, new SearchFilters())));
    }

    static List<string> Codes(SearchResponse response) =>
        response.Results.Select(result => result.Course.Code).OrderBy(code => code, StringComparer.Ordinal).ToList();

    static Course Course(string subject, string number, bool loaded, Requisite? prereqs = null) => new()
    {
        Subject = subject,
        Number = number,
        Title = $"{subject} {number}",
        MinCredits = 4,
        MaxCredits = 4,
        DetailsLoaded = loaded,
        Prereqs = prereqs,
    };

    static CourseSection Section(string crn, Course course) => new()
    {
        Crn = crn,
        Subject = course.Subject,
        Number = course.Number,
        Title = course.Title,
    };
}
