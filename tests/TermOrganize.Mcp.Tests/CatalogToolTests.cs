// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using TermOrganize.Core;
using TermOrganize.Mcp;

namespace TermOrganize.Mcp.Tests;

public sealed class CatalogToolTests
{
    private static CatalogLibrary Library => SharedCatalog.Library;
    private readonly CatalogTools _tools = new(SharedCatalog.Library);

    [Fact]
    public void ListTermsIncludesFallAndSpring()
    {
        var json = JsonSerializer.Serialize(_tools.list_terms());
        Assert.Contains("202710", json);
        Assert.Contains("202730", json);
    }

    [Fact]
    public void SearchAndGetCourseOmitOtherEmailDomains()
    {
        var spring = Library.ByCode["202730"];
        var course = spring.Courses.First(item => spring.Sections(item.Key).Count > 0);
        var found = _tools.search_courses("202730", subject: course.Subject, number: course.Number, limit: 5);
        var rows = (List<Dictionary<string, object?>>)found["courses"]!;
        Assert.Contains(rows, row => (string)row["number"]! == course.Number);
        var detail = JsonSerializer.Serialize(_tools.get_course("202730", course.Subject, course.Number));
        Assert.DoesNotContain("gmail.com", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kent.edu", detail, StringComparison.OrdinalIgnoreCase);
        AssertNoForeignEmails(detail);
    }

    [Fact]
    public void ForbiddenInstructorEmailsNeverAppear()
    {
        var seen = 0;
        foreach (var index in Library.ByCode.Values)
        {
            foreach (var section in index.Catalog.Sections)
            {
                foreach (var instructor in section.Instructors ?? [])
                {
                    if (instructor.Email is not string email || !email.Contains('@')) continue;
                    var domain = email[(email.LastIndexOf('@') + 1)..];
                    if (TextFormat.IsAllowedEmailDomain(domain)) continue;
                    var detail = JsonSerializer.Serialize(_tools.get_course(index.Term.Code, section.Subject, section.Number));
                    Assert.DoesNotContain(email, detail, StringComparison.OrdinalIgnoreCase);
                    AssertNoForeignEmails(detail);
                    if (++seen >= 8) return;
                }
            }
        }
    }

    [Fact]
    public void CheckScheduleReportsClashAndUnknownCrn()
    {
        var (term, first, second) = SharedCatalog.Overlap;
        var result = _tools.check_schedule(term, [first, second, "99999"]);
        var clashes = (List<Dictionary<string, object?>>)result["clashes"]!;
        var unknown = (List<string>)result["unknown_crns"]!;
        Assert.Contains(clashes, clash => (string)clash["crn_a"]! == first || (string)clash["crn_b"]! == first);
        Assert.Contains("99999", unknown);
        Assert.DoesNotContain("\u2013", JsonSerializer.Serialize(clashes));
    }

    [Fact]
    public void SuggestScheduleReturnsASection()
    {
        var index = Library.ByCode["202730"];
        var course = index.Courses.First(item => index.Sections(item.Key).Any(section => section.WeeklyMeetings.Any()));
        var result = _tools.suggest_schedule("Spring 2027", [new CourseRef { Subject = course.Subject, Number = course.Number }]);
        var schedules = (List<Dictionary<string, object?>>)result["schedules"]!;
        Assert.NotEmpty(schedules);
        var sections = (List<Dictionary<string, object?>>)schedules[0]["sections"]!;
        Assert.Equal(course.Subject, sections[0]["subject"]);
    }

    [Fact]
    public void ReorganizeMovesACourseThatIsNotOffered()
    {
        var sample = SharedCatalog.SpringOnly;
        var outcome = _tools.reorganize_plan(new PlanInput
        {
            Terms =
            [
                new PlanTermInput
                {
                    Term = "Fall 2026",
                    Courses = [new CourseRef { Subject = sample.Subject, Number = sample.Number }],
                },
                new PlanTermInput { Term = "Spring 2027" },
            ],
        });
        var moves = (List<Dictionary<string, object?>>)outcome["moves"]!;
        Assert.Contains(moves, move =>
            (string)move["subject"]! == sample.Subject
            && (string)move["number"]! == sample.Number
            && (string)move["to_term"]! == "Spring 2027");
        Assert.Contains("does not add a semester", (string)outcome["note"]!);
    }

    private static void AssertNoForeignEmails(string json)
    {
        var at = 0;
        while ((at = json.IndexOf('@', at)) >= 0)
        {
            var start = at;
            while (start > 0 && json[start - 1] != '"' && json[start - 1] != ' ') start--;
            var end = json.IndexOfAny(['"', ' ', ',', '}'], at);
            if (end < 0) end = json.Length;
            var address = json[start..end];
            var domain = address[(address.LastIndexOf('@') + 1)..].TrimEnd('.', ',', '"');
            Assert.True(TextFormat.IsAllowedEmailDomain(domain), address);
            at = end;
        }
    }
}

public static class SharedCatalog
{
    public static CatalogLibrary Library { get; } = new();

    public static (string Term, string First, string Second) Overlap { get; } = FindOverlap();

    public static Course SpringOnly { get; } = FindSpringOnly();

    public static Course OfferedBoth { get; } = FindBoth();

    private static (string Term, string First, string Second) FindOverlap()
    {
        foreach (var index in Library.ByCode.Values)
        {
            var sections = index.Catalog.Sections.Where(section => section.WeeklyMeetings.Any()).Take(500).ToList();
            for (var i = 0; i < sections.Count; i++)
            {
                for (var j = i + 1; j < sections.Count; j++)
                {
                    if (sections[i].CourseKey == sections[j].CourseKey) continue;
                    if (ScheduleConflicts.Overlap(sections[i], sections[j]) is null) continue;
                    return (index.Term.Code, sections[i].Crn, sections[j].Crn);
                }
            }
        }
        throw new InvalidOperationException("No overlapping sections in the bundled catalogs.");
    }

    private static Course FindSpringOnly()
    {
        var fall = Library.ByCode["202710"];
        var spring = Library.ByCode["202730"];
        return spring.Courses.First(course =>
            spring.Sections(course.Key).Count > 0
            && fall.Sections(course.Key).Count == 0
            && Clear(spring, course.Key)
            && Clear(fall, course.Key));
    }

    private static bool Clear(CatalogIndex index, CourseKey key)
    {
        var course = index.Course(key);
        return course is null || !course.DetailsLoaded || course.Prereqs is null;
    }

    private static Course FindBoth()
    {
        var fall = Library.ByCode["202710"];
        var spring = Library.ByCode["202730"];
        return spring.Courses.First(course => spring.Sections(course.Key).Count > 0 && fall.Sections(course.Key).Count > 0);
    }
}
