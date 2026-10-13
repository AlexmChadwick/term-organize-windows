// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class BannerParserTests
{
    private readonly BannerParsers.SubjectLookup _subjects = new(
    [
        new Subject { Code = "CS", Name = "Computer Science" },
        new Subject { Code = "DS", Name = "Data Science" },
        new Subject { Code = "MATH", Name = "Mathematics" },
        new Subject { Code = "EECE", Name = "Electrical and Comp Engineerng" },
        new Subject { Code = "ENGL", Name = "English" },
    ]);

    [Fact]
    public void ParsesNestedPrerequisites()
    {
        var html = Table(
        [
            Row(open: true),
            Row(open: true, subject: "Computer Science", number: "2100"),
            Row("Or", subject: "Computer Science - MMC", number: "201M"),
            Row("Or", subject: "Computer Science", number: "2510"),
            Row("Or", subject: "Data Science", number: "2500", close: true),
            Row("And", open: true, subject: "Computer Science", number: "1800"),
            Row("Or", subject: "Mathematics", number: "209M"),
            Row("Or", subject: "Electrical and Comp Engineerng", number: "2160", close: true),
            Row(close: true),
            Row("Or", subject: "Computer Science", number: "2700"),
        ]);
        var requisite = BannerParsers.Prerequisites(html, _subjects);
        Assert.NotNull(requisite);
        var top = Assert.IsType<Requisite.OrNode>(requisite);
        Assert.Equal(2, top.Items.Count);
        Assert.Equal(Requisite.Course(new CourseKey("CS", "2700")), top.Items[^1]);
        var groups = Assert.IsType<Requisite.AndNode>(top.Items[0]);
        Assert.Equal(2, groups.Items.Count);
        Assert.Contains(new CourseKey("EECE", "2160"), requisite.ReferencedCourses());
        Assert.Contains(new CourseKey("CS", "201M"), requisite.ReferencedCourses());
    }

    [Fact]
    public void ParsesSingleCourseAndTests()
    {
        Assert.Equal(Requisite.Course(new CourseKey("CS", "2100")), BannerParsers.Prerequisites(Table([Row(subject: "Computer Science", number: "2100")]), _subjects));
        var parsed = BannerParsers.Prerequisites(Table(
        [
            Row(subject: "Mathematics", number: "1341"),
            Row("Or", test: "Graduate Admission"),
        ]), _subjects);
        Assert.Equal(
            Requisite.Or(Requisite.Course(new CourseKey("MATH", "1341")), Requisite.Test("Graduate Admission")),
            parsed);
    }

    [Fact]
    public void EmptyPrerequisitesAreNil()
    {
        const string html = "<section aria-labelledby=\"preReqs\"><h3>Catalog Prerequisites</h3> No prerequisite information available.</section>";
        Assert.Null(BannerParsers.Prerequisites(html, _subjects));
    }

    [Fact]
    public void ParsesCorequisites()
    {
        const string html = """
        <section><table class="basePreqTable"><thead><tr><th>Subject</th></tr></thead><tbody>
        <tr><td>Computer Science</td><td>3001</td><td>Recitation for CS 3000</td></tr>
        </tbody></table></section>
        """;
        Assert.Equal(Requisite.Course(new CourseKey("CS", "3001")), BannerParsers.Corequisites(html, _subjects));
    }

    [Fact]
    public void ParsesDescriptionAndCatalogDetails()
    {
        const string description = """
        <section aria-labelledby="courseDescription">
            <!--display course description-->
            Covers the derivative &amp; integration.<br/>
        </section>
        """;
        Assert.Equal("Covers the derivative & integration.", BannerParsers.Description(description));

        const string details = """
        <section aria-labelledby="catalog">
         <span class="status-bold">Title:</span>Algorithms and Data<br/>
         <span class="status-bold">College:</span>
         <span>Khoury Coll of Comp Sciences CS</span><br/>
         <span class="status-bold">Department:</span>
         <span>Computer Science CS</span><br/>
        </section>
        """;
        var parsed = BannerParsers.CatalogDetailsFrom(details);
        Assert.Equal("Algorithms and Data", parsed.Title);
        Assert.Equal("Khoury College of Computer Sciences", parsed.College);
        Assert.Equal("Computer Science", parsed.Department);
    }

    [Fact]
    public void DecodesEntities()
    {
        Assert.Equal("Fin Accounting & Reporting", HtmlText.DecodeEntities("Fin Accounting &amp; Reporting"));
        Assert.Equal("O'Brien \u2014 \"Hi\"", HtmlText.DecodeEntities("O&#39;Brien &#x2014; &quot;Hi&quot;"));
        Assert.Equal("AT&T", HtmlText.DecodeEntities("AT&T"));
    }

    [Fact]
    public void ConvertsBannerMeetingTimes()
    {
        var banner = new BannerMeetingTime
        {
            BeginTime = "0800",
            EndTime = "0905",
            Building = "DG",
            BuildingDescription = "Dodge Hall",
            Room = "070",
            Campus = "BOS",
            CampusDescription = "Boston",
            StartDate = "09/09/2026",
            EndDate = "12/13/2026",
            MeetingType = "CLAS",
            MeetingTypeDescription = "Class",
            Monday = true,
            Wednesday = true,
            Thursday = true,
        };
        var meeting = banner.ToMeeting();
        Assert.Equal(480, meeting.Start);
        Assert.Equal(545, meeting.End);
        Assert.Equal("MWR", meeting.Days.CompactLabel);
        Assert.Equal("DG 070", meeting.LocationLabel);
        Assert.Equal("2026-09-09", meeting.StartDate);
        Assert.False(meeting.IsFinalExam);
    }

    [Fact]
    public void TypeaheadCodesMustMatchExactly()
    {
        const string json = """[{"code":"CS3386","description":"CS3386 Computer Science"},{"code":"CS33860","description":"CS33860 Computer Science"}]""";
        var items = JsonSerializer.Deserialize<List<BannerCodeDescription>>(json, AppJson.Catalog)!;
        Assert.True(BannerParsers.CatalogCodes(items, new CourseKey("CS", "3386")));
        Assert.False(BannerParsers.CatalogCodes(items, new CourseKey("CS", "338")));
        Assert.False(BannerParsers.CatalogCodes([], new CourseKey("CS", "3386")));
    }

    [Fact]
    public void ReadsCatalogDetailsAndCredits()
    {
        const string html = """
        <section aria-labelledby="catalog">
        <span class="status-bold">Title:</span><span>Directed Study</span><br/>
        <span class="status-bold">College:</span>
        <span>Coll of Soc Sci &amp; Humanities  SH</span>
        <br/>
        <span class="status-bold">Department:</span>
        <span>Sociology and Anthropology  SOCL</span><br/>
        <span class="status-bold">Hours:</span><br/>
        <span class="indent-left">Credit Hours:</span><span
        class="credit-hours-direction">1 TO 4</span><br/>
        </section>
        """;
        var details = BannerParsers.CatalogDetailsFrom(html);
        Assert.Equal("Directed Study", details.Title);
        Assert.Equal("Sociology and Anthropology", details.Department);
        Assert.Equal(1, details.MinCredits);
        Assert.Equal(4, details.MaxCredits);
        var range = BannerParsers.CreditRange("1.000 TO 4.000");
        Assert.Equal(4, range?.High);
        Assert.Equal(0.5, BannerParsers.CreditRange("0.5")?.Low);
        Assert.Null(BannerParsers.CreditRange("TBA"));
    }

    [Fact]
    public void ReadsMutualExclusionsAndCrossListedSections()
    {
        Assert.Empty(BannerParsers.MutualExclusions("<section aria-labelledby=\"mexc\">No Mutual Exclusion information available.</section>", _subjects));
        const string table = """
        <section aria-labelledby="mexc"><table><thead><tr><th>Subject</th><th>Course Number</th><th>Title</th></tr></thead><tbody>
        <tr><td>Computer Science</td><td>2510</td><td>Fundamentals of Computer Science 2</td></tr>
        <tr><td>Mathematics</td><td>1341</td><td>Calculus 1</td></tr>
        </tbody></table></section>
        """;
        Assert.Equal([new CourseKey("CS", "2510"), new CourseKey("MATH", "1341")], BannerParsers.MutualExclusions(table, _subjects));
        Assert.Equal(
            [new CourseKey("CS", "2500")],
            BannerParsers.MutualExclusions("<section><table><tbody><tr><td>Or</td><td>Computer Science</td><td>2500</td></tr></tbody></table></section>", _subjects));
        Assert.Equal(
            [new CourseKey("CS", "2510"), new CourseKey("CS", "2100")],
            BannerParsers.MutualExclusions("<section>Credit cannot be earned for both CS 2510 and CS 2100.</section>", _subjects));

        const string cross = """
        <section aria-labelledby="xlstSections">
        <table>
            <thead><tr><th>CRN</th><th>Subject</th><th>Course Number</th><th>Title</th><th>Section</th></tr></thead>
            <tbody>
                <tr><td>22773</td><td>English</td><td>2690</td><td>Boston in Literature</td><td>01</td></tr>
            </tbody>
        </table>
        </section>
        """;
        var sections = BannerParsers.CrossListedSections(cross, _subjects);
        Assert.Equal("22773", Assert.Single(sections).Crn);
        Assert.Equal(new CourseKey("ENGL", "2690"), sections[0].Course);
        Assert.Equal("Boston in Literature", sections[0].Title);
        Assert.Empty(BannerParsers.CrossListedSections("<section>No cross list sections</section>", _subjects));
    }

    private static string Row(string andOr = "", bool open = false, string test = "", string subject = "", string number = "", bool close = false) =>
        $"<tr><td>{andOr}</td><td>{(open ? "(" : "")}</td><td>{test}</td><td></td><td>{subject}</td><td>{number}</td><td>Undergraduate</td><td>D-</td><td>{(close ? ")" : "")}</td></tr>";

    private static string Table(IEnumerable<string> rows) =>
        $"<section><table class=\"basePreqTable\"><thead><tr><th>And/Or</th></tr></thead><tbody>{string.Concat(rows)}</tbody></table></section>";
}
