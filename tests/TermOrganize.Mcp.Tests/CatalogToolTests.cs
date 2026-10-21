// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using TermOrganize.Core;
using TermOrganize.Mcp;

namespace TermOrganize.Mcp.Tests;

public sealed class CatalogToolTests
{
    private static CatalogLibrary Library => SharedCatalog.Library;
    private readonly CatalogTools _tools = new(SharedCatalog.Library, new PlanService(Path.Combine(Path.GetTempPath(), "term-organize-unused-" + Guid.NewGuid().ToString("N") + ".json"), SharedCatalog.Library));

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
        Assert.DoesNotContain("husky.neu.edu", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ccs.neu.edu", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coe.neu.edu", detail, StringComparison.OrdinalIgnoreCase);
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
                    Assert.Contains("\"name\"", detail);
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
        var reported = (Dictionary<string, object?>)result["term"]!;
        Assert.Equal(term, reported["term"]);
        Assert.False(string.IsNullOrWhiteSpace((string)reported["name"]!));
        var clashes = (List<Dictionary<string, object?>>)result["clashes"]!;
        var unknown = (List<string>)result["unknown_crns"]!;
        Assert.Contains(clashes, clash =>
        {
            var left = (string)clash["first"]!;
            var right = (string)clash["second"]!;
            return (left == first && right == second) || (left == second && right == first);
        });
        Assert.All(clashes, clash =>
        {
            Assert.False(string.IsNullOrWhiteSpace((string)clash["reason"]!));
            Assert.False(clash.ContainsKey("crn_a"));
            Assert.False(clash.ContainsKey("crn_b"));
            Assert.False(clash.ContainsKey("summary"));
        });
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
        var outcome = _tools.reorganize_plan(plan: new PlanInput
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
            && (string)move["from_term"]! == "202710"
            && (string)move["to_term"]! == "202730");
        var plan = (Dictionary<string, object?>)outcome["plan"]!;
        Assert.NotEmpty((List<Dictionary<string, object?>>)plan["terms"]!);
    }

    [Fact]
    public void AllowedInstructorEmailIsOptionalAndExact()
    {
        string? found = null;
        foreach (var section in Library.ByCode["202730"].Catalog.Sections)
        {
            foreach (var instructor in section.Instructors ?? [])
            {
                if (SectionJson.AllowedEmail(instructor.Email) is null) continue;
                found = _tools.get_course("202730", section.Subject, section.Number) is { } detail
                    ? JsonSerializer.Serialize(detail)
                    : null;
                break;
            }
            if (found is not null) break;
        }
        Assert.NotNull(found);
        Assert.Contains("\"email\"", found);
        Assert.Contains("northeastern.edu", found, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("husky.neu.edu", found, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mail.neu.edu", found, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EightAmIsAClockAndGarbageTimeIsInvalid()
    {
        var index = Library.ByCode["202730"];
        var course = index.Courses.First(item => index.Sections(item.Key).Any(section => section.WeeklyMeetings.Any()));
        var courses = new List<CourseRef> { new() { Subject = course.Subject, Number = course.Number } };
        var parsed = _tools.suggest_schedule("202730", courses, new ScheduleConstraints
        {
            NoClassesBefore = JsonSerializer.SerializeToElement("8am"),
        });
        Assert.NotEmpty((List<Dictionary<string, object?>>)parsed["schedules"]!);

        var error = Assert.Throws<ToolFailure>(() => _tools.suggest_schedule("202730", courses, new ScheduleConstraints
        {
            NoClassesBefore = JsonSerializer.SerializeToElement("tomorrow"),
        }));
        Assert.Equal("invalid_argument", error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void UnknownTermCourseAndCrnStayNotFound()
    {
        var term = Assert.Throws<ToolFailure>(() => _tools.check_schedule("209930", []));
        Assert.Equal("not_found", term.Code);
        var course = Assert.Throws<ToolFailure>(() => _tools.get_course("202730", "ZZZZ", "9999"));
        Assert.Equal("not_found", course.Code);
        Assert.False(string.IsNullOrWhiteSpace(course.Message));
    }

    [Fact]
    public void SharedReorganizeFixtureMatches()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "mcp-reorganize-fixture.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var plan = new PlanInput();
        foreach (var term in doc.RootElement.GetProperty("plan").GetProperty("terms").EnumerateArray())
        {
            var input = new PlanTermInput { Term = term.GetProperty("term").GetString() };
            if (term.TryGetProperty("courses", out var courses))
            {
                foreach (var course in courses.EnumerateArray())
                {
                    input.Courses.Add(new CourseRef
                    {
                        Subject = course.GetProperty("subject").GetString() ?? "",
                        Number = course.GetProperty("number").GetString() ?? "",
                    });
                }
            }
            plan.Terms.Add(input);
        }
        var outcome = _tools.reorganize_plan(plan: plan);
        var actual = (List<Dictionary<string, object?>>)outcome["moves"]!;
        var expected = doc.RootElement.GetProperty("moves").EnumerateArray().ToList();
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].GetProperty("subject").GetString(), actual[i]["subject"]);
            Assert.Equal(expected[i].GetProperty("number").GetString(), actual[i]["number"]);
            Assert.Equal(expected[i].GetProperty("from_term").GetString(), actual[i]["from_term"]);
            Assert.Equal(expected[i].GetProperty("to_term").GetString(), actual[i]["to_term"]);
            Assert.Equal(expected[i].GetProperty("reason").GetString(), actual[i]["reason"]);
        }
        Assert.Empty((List<Dictionary<string, object?>>)outcome["unresolved"]!);
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
