// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

/// <summary>
/// "No prerequisites" and "No corequisites" keep a loaded course whose requisite was omitted.
/// A course whose details were not loaded is unknown, so the filter leaves it out.
/// </summary>
public class RequisiteFilterTests
{
    [Fact]
    public void NoneIsALoadedCourseWithTheRequisiteOmitted()
    {
        var open = Course("CS", "1100", loaded: true);
        var prereq = Course("CS", "2500", loaded: true, prereqs: Requisite.Course(new CourseKey("CS", "1800")));
        var coreq = Course("CS", "2501", loaded: true, coreqs: Requisite.Course(new CourseKey("CS", "2500")));
        var testOnly = Course("CS", "1800", loaded: true, prereqs: Requisite.Test("Math Placement", 30));
        var unknown = Course("CS", "9999", loaded: false);

        Assert.True(open.HasNoPrerequisites);
        Assert.True(open.HasNoCorequisites);
        Assert.False(prereq.HasNoPrerequisites);
        Assert.True(prereq.HasNoCorequisites);
        Assert.True(coreq.HasNoPrerequisites);
        Assert.False(coreq.HasNoCorequisites);
        Assert.False(testOnly.HasNoPrerequisites);
        Assert.False(unknown.HasNoPrerequisites);
        Assert.False(unknown.HasNoCorequisites);

        var index = CatalogIndex.Build(new TermCatalog
        {
            Term = new Term("202710", "Fall 2026 Semester"),
            Courses = [open, prereq, coreq, testOnly, unknown],
            Sections =
            [
                Section("10001", open),
                Section("10002", prereq),
                Section("10003", coreq),
                Section("10004", testOnly),
                Section("10005", unknown),
            ],
        });

        var noPrereqs = SearchEngine.Search("", index, new SearchFilters { NoPrerequisites = true });
        Assert.Equal(["CS 1100", "CS 2501"], Codes(noPrereqs));
        Assert.Equal(1, new SearchFilters { NoPrerequisites = true }.ActiveCount);

        var noCoreqs = SearchEngine.Search("", index, new SearchFilters { NoCorequisites = true });
        Assert.Equal(["CS 1100", "CS 1800", "CS 2500"], Codes(noCoreqs));

        var bothFilters = new SearchFilters { NoPrerequisites = true, NoCorequisites = true };
        var both = SearchEngine.Search("", index, bothFilters);
        Assert.Equal(["CS 1100"], Codes(both));
        Assert.Equal(2, bothFilters.ActiveCount);
        Assert.False(bothFilters.IsEmpty);
    }

    static List<string> Codes(SearchResponse response) =>
        response.Results.Select(result => result.Course.Code).OrderBy(code => code, StringComparer.Ordinal).ToList();

    static Course Course(string subject, string number, bool loaded, Requisite? prereqs = null, Requisite? coreqs = null) => new()
    {
        Subject = subject,
        Number = number,
        Title = $"{subject} {number}",
        MinCredits = 4,
        MaxCredits = 4,
        DetailsLoaded = loaded,
        Prereqs = prereqs,
        Coreqs = coreqs,
    };

    static CourseSection Section(string crn, Course course) => new()
    {
        Crn = crn,
        Subject = course.Subject,
        Number = course.Number,
        Title = course.Title,
        SectionNumber = "01",
        ClassType = "Lecture",
        Campus = "Boston",
        SeatCapacity = 20,
        SeatsRemaining = 5,
    };
}
