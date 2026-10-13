// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Nodes;
using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

/// <summary>Catalog parsing and the degree audit, against a small made-up program page.</summary>
public class DegreeRequirementsTests
{
    public const string ProgramPage = """
        <html><body>
        <div id="textcontainer" class="page_content"><p>About the program.</p></div>
        <div id="programrequirementstextcontainer" class="tab_content">
        <p>Complete all courses listed below unless otherwise indicated.</p>
        <h2>Major Requirements</h2>
        <table class="sc_courselist"><tbody>
        <tr class="hidden noscript"><th scope="col">Code</th><th scope="col">Title</th><th scope="col" class="hourscol">Hours</th></tr>
        <tr class="even areaheader firstrow"><td colspan="2"><span class="courselistcomment areaheader">Required Courses</span></td><td class="hourscol"></td></tr>
        <tr class="odd"><td class="codecol"><a href="/search/?P=CS%201800" title="CS 1800" class="bubblelink code">CS&#160;1800</a><span style="margin-left:20px;" class="blockindent">and <a href="/search/?P=CS%201802" class="bubblelink code">CS&#160;1802</a></span></td><td>Discrete Structures</td><td class="hourscol">5</td></tr>
        <tr class="even"><td class="codecol"><a class="bubblelink code">CS&#160;3000</a></td><td>Algorithms and Data</td><td class="hourscol">4</td></tr>
        <tr class="odd"><td class="codecol"><a class="bubblelink code">CS&#160;4500</a></td><td>Software Development</td><td class="hourscol">4</td></tr>
        <tr class="orclass odd"><td class="codecol orclass">or <a class="bubblelink code">CS&#160;4530</a></td><td colspan="2"> Fundamentals of Software Engineering</td></tr>
        <tr class="even areaheader"><td colspan="2"><span class="courselistcomment areaheader">Security Course</span></td><td class="hourscol"></td></tr>
        <tr class="odd"><td colspan="2"><span class="courselistcomment">Complete one of the following:</span></td><td class="hourscol">4</td></tr>
        <tr class="even"><td class="codecol"><a class="bubblelink code">CY&#160;2550</a></td><td>Foundations of Cybersecurity</td><td class="hourscol"></td></tr>
        <tr class="odd"><td class="codecol"><a class="bubblelink code">CY&#160;3740</a></td><td>Systems Security</td><td class="hourscol"></td></tr>
        <tr class="even areaheader"><td colspan="2"><span class="courselistcomment areaheader">Khoury Elective Courses</span></td><td class="hourscol"></td></tr>
        <tr class="odd"><td colspan="2"><span class="courselistcomment">With adviser approval, directed study may also be taken as an elective.</span></td><td class="hourscol"></td></tr>
        <tr class="even"><td colspan="2"><span class="courselistcomment">Complete 8 semester hours of CS, CY, DS, or IS classes that are not already required. Choose courses within the following ranges:</span></td><td class="hourscol">8</td></tr>
        <tr class="odd"><td colspan="2"><div style="margin-left:20px;" class="blockindent"><span class="courselistcomment commentindent"><a class="bubblelink code">CS&#160;2500</a> or higher, except <a class="bubblelink code">CS&#160;5010</a></span></div></td><td class="hourscol"></td></tr>
        <tr class="even lastrow"><td colspan="2"><div style="margin-left:20px;" class="blockindent"><span class="courselistcomment commentindent"><a class="bubblelink code">DS&#160;2500</a> to DS 4999 but not <a class="bubblelink code">DS&#160;4900</a></span></div></td><td class="hourscol"></td></tr>
        </tbody></table>
        <h2>Supporting Courses</h2>
        <table class="sc_courselist"><tbody>
        <tr class="even areaheader firstrow"><td colspan="2"><span class="courselistcomment areaheader">Science Requirement</span></td><td class="hourscol"></td></tr>
        <tr class="odd"><td colspan="2"><span class="courselistcomment">Complete two courses (and any required labs) from the following science categories:</span></td><td class="hourscol">8</td></tr>
        <tr class="even areasubheader undefined subheader "><td colspan="2"><span class="courselistcomment areasubheader undefined"><i>Biology</i></span></td><td class="hourscol"></td></tr>
        <tr class="odd"><td class="codecol"><a class="bubblelink code">BIOL&#160;1111</a><br/><span class="blockindent">and <a class="bubblelink code">BIOL&#160;1112</a></span></td><td>General Biology 1<br/><span class="blockindent">and Lab for BIOL 1111</span></td><td class="hourscol"></td></tr>
        <tr class="even areasubheader undefined subheader "><td colspan="2"><span class="courselistcomment areasubheader undefined"><i>Mathematics</i></span></td><td class="hourscol"></td></tr>
        <tr class="odd lastrow"><td class="codecol"><a class="bubblelink code">MATH&#160;2331</a></td><td>Linear Algebra</td><td class="hourscol"></td></tr>
        </tbody></table>
        <h2>Required General Electives</h2>
        <table class="sc_courselist"><tbody>
        <tr class="even lastrow firstrow"><td colspan="2"><span class="courselistcomment">Complete 8 semester hours of general electives.</span></td><td class="hourscol">8</td></tr>
        </tbody></table>
        <h2>Major GPA Requirement</h2>
        <p>Minimum 2.000 GPA required in the major.</p>
        <h2>Program Requirement</h2>
        <p>52 total semester hours required</p>
        <h2>Concentration in Human-Centered Computing*</h2>
        <table class="sc_courselist"><tbody>
        <tr class="even firstrow"><td class="codecol"><a class="bubblelink code">CS&#160;2484</a></td><td>Principles of Human-Computer Interaction</td><td class="hourscol">4</td></tr>
        <tr class="odd"><td colspan="2"><span class="courselistcomment">Complete two of the following not already taken:</span></td><td class="hourscol">8</td></tr>
        <tr class="even"><td class="codecol"><a class="bubblelink code">CS&#160;4520</a></td><td>Mobile Application Development</td><td class="hourscol"></td></tr>
        <tr class="odd"><td class="codecol"><a class="bubblelink code">CS&#160;4550</a></td><td>Web Development</td><td class="hourscol"></td></tr>
        <tr class="even lastrow"><td class="codecol"><a class="bubblelink code">DS&#160;4200</a></td><td>Information Presentation and Visualization</td><td class="hourscol"></td></tr>
        </tbody></table>
        <p>*This concentration requires a fall co-op pattern.</p>
        <h2>Software Concentration</h2>
        <table class="sc_courselist"><tbody>
        <tr class="even firstrow lastrow"><td class="codecol"><a class="bubblelink code">CS&#160;4400</a></td><td>Programming Languages</td><td class="hourscol">4</td></tr>
        </tbody></table>
        </div>
        <div id="planofstudytextcontainer" class="tab_content">
        <table class="sc_courselist"><tbody><tr><td class="codecol"><a>CS&#160;9999</a></td><td>Not a requirement</td><td class="hourscol">4</td></tr></tbody></table>
        </div>
        </body></html>
        """;

    public static readonly CatalogProgram Program = new(
        "Computer Science, BSCS (Boston)",
        "undergraduate/computer-information-science/computer-science/bscs/");

    public static ProgramRequirements Requirements() =>
        ProgramCatalogParser.Requirements(ProgramPage, new CatalogYear(2023), Program)
        ?? throw new InvalidOperationException("requirements");

    public static DegreePlanCourse Course(string code, double credits = 4, string title = "") =>
        new(CourseKey.Parse(code), title, credits);

    public static CourseKey Key(string code) => CourseKey.Parse(code) ?? throw new InvalidOperationException(code);

    /// <summary>A student in the human-centered computing concentration.</summary>
    public static DegreePlan Plan()
    {
        var plan = new DegreePlan { Name = "Plan" };
        plan.Completed =
        [
            Course("CS 1800"), Course("CS 1802", 1), Course("CS 3000"), Course("CY 2550"),
            Course("CS 4973", 4, "Topics in Computer Science"), Course("CS 2484"), Course("CS 4520"),
            Course("CS 4535", 4, "Prof Practicum Capstone"), Course("CS 2501", 1), Course("MATH 1341"),
            Course("CS 3000", 0),
        ];
        plan.Terms =
        [
            new DegreePlanTerm
            {
                Season = PlanSeason.Spring,
                Year = 2027,
                Courses = [Course("CS 3800"), Course("CS 4100"), Course("DS 3000"), Course("CS 4410")],
            },
        ];
        plan.CatalogYear = 2023;
        plan.CatalogProgramPath = Program.Path;
        plan.ConcentrationID = "concentration-in-human-centered-computing";
        return plan;
    }

    static RequirementRule Rule(ProgramRequirements requirements, Func<RequirementRule, bool> match) =>
        requirements.AllRules.FirstOrDefault(match) ?? throw new InvalidOperationException("rule");

    [Fact]
    public void ParsesRequirementTables()
    {
        var requirements = Requirements();
        Assert.Equal(52, requirements.TotalHours);
        Assert.Equal(
            [
                "Major Requirements", "Supporting Courses", "Required General Electives", "Major GPA Requirement",
                "Program Requirement", "Concentration in Human-Centered Computing", "Software Concentration",
            ],
            requirements.Sections.Select(section => section.Title));
        Assert.Equal(["Human-Centered Computing", "Software"], requirements.Concentrations.Select(section => section.ConcentrationName));
        Assert.DoesNotContain(requirements.AllRules, rule => rule.ListedCourses.Contains(Key("CS 9999")));

        var major = requirements.Sections[0];
        Assert.Equal(["Required Courses", "Security Course", "Khoury Elective Courses"], major.Groups.Select(group => group.Title));
        var required = major.Groups[0].Rules;
        Assert.Equal(["CS 1800 and CS 1802", "CS 3000", "CS 4500 or CS 4530"], required.Select(rule => rule.Label));
        Assert.Equal(
            new RequirementKind.Required(new CourseOption([[Key("CS 1800"), Key("CS 1802")]])),
            required[0].Kind);
        Assert.Equal(4, required[2].Hours);

        var security = Assert.IsType<RequirementKind.Choose>(major.Groups[1].Rules[0].Kind);
        Assert.Equal(1, security.Count);
        Assert.Equal(2, security.Options.Count);
        Assert.Empty(security.Ranges);

        var electives = Assert.IsType<RequirementKind.Choose>(major.Groups[2].Rules[0].Kind);
        Assert.Equal(8, electives.Hours);
        Assert.Equal(
            ["CS 2500 or higher, except CS 5010", "DS 2500 to DS 4999 but not DS 4900"],
            electives.Ranges.Select(range => range.Label));
        Assert.True(electives.Ranges[0].Contains(Key("CS 4973")));
        Assert.False(electives.Ranges[0].Contains(Key("CS 5010")));
        Assert.False(electives.Ranges[0].Contains(Key("CS 2100")));
        Assert.False(electives.Ranges[1].Contains(Key("DS 4900")));
        Assert.False(electives.Ranges[1].Contains(Key("DS 5110")));
        Assert.Single(major.Groups[2].Notes);

        var science = requirements.Sections[1].Groups[0].Rules[0];
        var scienceKind = Assert.IsType<RequirementKind.Choose>(science.Kind);
        Assert.Equal(2, scienceKind.Count);
        Assert.Equal(["BIOL 1111 and BIOL 1112", "MATH 2331"], scienceKind.Options.Select(option => option.Label));
        Assert.Equal(["Biology", "Mathematics"], science.Notes);
        Assert.Equal(["Biology", "Mathematics"], science.OptionCategories);
        Assert.Equal("Science Requirement", requirements.Title(science.Id));

        var general = Assert.IsType<RequirementKind.GeneralElectives>(requirements.Sections[2].Groups[0].Rules[0].Kind);
        Assert.Equal(8, general.Hours);
        Assert.Equal(["Minimum 2.000 GPA required in the major."], requirements.Sections[3].Notes);

        string? Title(string code) => requirements.CourseTitles.TryGetValue(Key(code), out var title) ? title : null;
        Assert.Equal("Algorithms and Data", Title("CS 3000"));
        Assert.Equal("Fundamentals of Software Engineering", Title("CS 4530"));
        Assert.Equal("General Biology 1", Title("BIOL 1111"));
        Assert.Equal("Lab for BIOL 1111", Title("BIOL 1112"));
        Assert.Equal("Web Development", Title("CS 4550"));
    }

    [Fact]
    public void RuleIDsAreStable()
    {
        var first = Requirements().AllRules.Select(rule => rule.Id).ToList();
        var second = Requirements().AllRules.Select(rule => rule.Id).ToList();
        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.ToHashSet().Count);
        Assert.Contains("major-requirements/required-courses/cs-4500-or-cs-4530", first);
    }

    [Fact]
    public void ReadsEditionsFromTheSite()
    {
        var page = """
            <header><span class="edition">Academic Catalog 2031-2032</span></header>
            <a href="/archive/2030-2031">2030-2031 Catalog</a>
            <a href="https://catalog.northeastern.edu/archive/2029-2030/">2029-2030 Catalog</a>
            <a href="https://catalog.northeastern.edu/pdf/2029-2030.pdf">PDF</a>
            """;
        Assert.Equal(new CatalogYear(2031), ProgramCatalogParser.CurrentEdition(page));
        Assert.Equal([new CatalogYear(2030), new CatalogYear(2029)], ProgramCatalogParser.ArchivedEditions(page));
    }

    [Fact]
    public void EditionURLsFollowTheSite()
    {
        var editions = new CatalogEditions(new CatalogYear(2031), [new CatalogYear(2030)], DateTimeOffset.UtcNow);
        var path = "undergraduate/computer-information-science/computer-science/bscs/";
        Assert.Equal(
            "https://catalog.northeastern.edu/undergraduate/computer-information-science/computer-science/bscs/",
            editions.Url(new CatalogYear(2031), path).AbsoluteUri);
        Assert.Equal(
            "https://catalog.northeastern.edu/archive/2030-2031/undergraduate/computer-information-science/computer-science/bscs/",
            editions.Url(new CatalogYear(2030), path).AbsoluteUri);
        Assert.Equal(new CatalogYear(2030), CatalogYear.Parse("2030\u20132031"));
        Assert.Equal("2030\u20132031", CatalogYear.Parse("2030-31")?.ToString());

        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var juneLocal = new DateTime(2040, 6, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var octoberLocal = new DateTime(2040, 10, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var june = new DateTimeOffset(juneLocal, zone.GetUtcOffset(juneLocal));
        var october = new DateTimeOffset(octoberLocal, zone.GetUtcOffset(octoberLocal));
        Assert.Equal(new CatalogYear(2039), CatalogYear.Containing(june, zone));
        Assert.Equal(new CatalogYear(2040), CatalogYear.Containing(october, zone));
        Assert.Equal(new CatalogYear(2016), CatalogEditions.Estimated(october, zone).Archived.Last());
    }

    [Fact]
    public void ListsProgramsFromTheIndex()
    {
        var index = """
            <a href="/archive/2023-2024/undergraduate/computer-information-science/computer-science/bscs/">Computer Science, BSCS</a>
            <a href="/graduate/computer-information-science/computer-science/computer-science-ms/">Computer Science, MS (Boston)</a>
            <a href="/undergraduate/computer-information-science/computer-science/computer-science-minor/">Computer Science Minor</a>
            <a href="/undergraduate/about/">About, The University</a>
            <a href="#programs">Programs, AZ</a>
            """;
        var programs = ProgramCatalogParser.Programs(index);
        Assert.Equal(["Computer Science, BSCS", "Computer Science, MS (Boston)"], programs.Select(program => program.Title));
        Assert.Equal("undergraduate/computer-information-science/computer-science/bscs/", programs[0].Path);
        Assert.Equal(CatalogProgram.LevelKind.Graduate, programs[1].Level);
        Assert.Equal("MS", programs[1].Degree);
        Assert.Equal("Boston", programs[1].Campus);
        Assert.Equal(["BSCS"], ProgramCatalogStore.SuggestedPrograms("CS-B-CSCI-BSCS", programs).Select(program => program.Degree));
    }

    [Fact]
    public void AuditMatchesCoursesToRequirements()
    {
        var audit = DegreeAudit.Run(Plan(), Requirements());
        DegreeAudit.Status? Status(string label) => audit.RuleResults.FirstOrDefault(result => result.Rule.Label == label)?.Status;
        Assert.False(audit.NeedsConcentration);
        Assert.Equal(DegreeAudit.Status.Complete, Status("CS 1800 and CS 1802"));
        Assert.Equal(DegreeAudit.Status.Complete, Status("CS 3000"));
        Assert.Equal(DegreeAudit.Status.Missing, Status("CS 4500 or CS 4530"));
        Assert.Equal(DegreeAudit.Status.Complete, Status("Complete one of the following:"));
        Assert.Equal(DegreeAudit.Status.Complete, Status("CS 2484"));
        var hcc = audit.RuleResults.First(result => result.Rule.Label == "Complete two of the following not already taken:");
        Assert.Equal(DegreeAudit.Status.Partial, hcc.Status);
        Assert.Equal(1, hcc.RemainingCount);
        var electives = audit.RuleResults.First(result => result.Rule.Label.StartsWith("Complete 8 semester hours of CS", StringComparison.Ordinal));
        Assert.Equal(DegreeAudit.Status.Complete, electives.Status);
        Assert.Equal(new HashSet<string> { "CS 4973", "CS 4535" }, electives.Used.Select(use => use.Key.ToString()).ToHashSet());
        Assert.Equal(DegreeAudit.Status.Missing, Status("Complete two courses (and any required labs) from the following science categories:"));
        Assert.DoesNotContain(audit.Sections, section => section.Section.Title == "Software Concentration");
        Assert.Equal(34, audit.CompletedHours);
        Assert.Equal(16, audit.PlannedHours);
        Assert.Equal(52, audit.RequiredHours);
    }

    [Fact]
    public void SubstitutionsAndPetitions()
    {
        var requirements = Requirements();
        var plan = Plan();
        var hccRule = Rule(requirements, rule => rule.Label == "Complete two of the following not already taken:");
        var softwareEngineering = Rule(requirements, rule => rule.Label == "CS 4500 or CS 4530");
        var science = Rule(requirements, rule => rule.Label.StartsWith("Complete two courses", StringComparison.Ordinal));
        var electives = Rule(requirements, rule => rule.Label.StartsWith("Complete 8 semester hours of CS", StringComparison.Ordinal));
        var softwareOnly = Rule(requirements, rule => rule.Label == "CS 4400");
        plan.Substitutions =
        [
            new RequirementSubstitution { RuleID = hccRule.Id, RuleTitle = "HCI electives", Change = SubstitutionChange.CountCourse(Key("CS 4973")) },
            new RequirementSubstitution { RuleID = softwareEngineering.Id, RuleTitle = "Software engineering", Change = SubstitutionChange.CountCourse(Key("CS 4535")) },
            new RequirementSubstitution
            {
                RuleID = science.Id,
                RuleTitle = "Science Requirement",
                Change = SubstitutionChange.Replace(new RequirementReplacement { Count = 2, LikeRuleID = electives.Id, LikeRuleTitle = "Khoury Elective Courses" }),
                Note = "Petition approved",
            },
            new RequirementSubstitution { RuleID = softwareOnly.Id, RuleTitle = "CS 4400", Change = SubstitutionChange.Waive },
            new RequirementSubstitution { RuleID = electives.Id, RuleTitle = "Electives", Change = SubstitutionChange.CountCourse(Key("CS 4999")) },
        ];
        var audit = DegreeAudit.Run(plan, requirements);

        var hcc = audit.Result(hccRule.Id) ?? throw new InvalidOperationException("hcc");
        Assert.Equal(DegreeAudit.Status.Complete, hcc.Status);
        Assert.Contains(hcc.Used, use => use.Key.ToString() == "CS 4973" && use.BySubstitution);

        var engineering = audit.Result(softwareEngineering.Id) ?? throw new InvalidOperationException("engineering");
        Assert.Equal(DegreeAudit.Status.Complete, engineering.Status);
        Assert.Equal(["CS 4535"], engineering.Used.Select(use => use.Key.ToString()));

        var electiveResult = audit.Result(electives.Id) ?? throw new InvalidOperationException("electives");
        Assert.Equal(DegreeAudit.Status.Planned, electiveResult.Status);
        Assert.Equal(new HashSet<string> { "DS 3000", "CS 4410" }, electiveResult.Used.Select(use => use.Key.ToString()).ToHashSet());
        var scienceResult = audit.Result(science.Id) ?? throw new InvalidOperationException("science");
        Assert.Equal(DegreeAudit.Status.Planned, scienceResult.Status);
        Assert.Equal(new HashSet<string> { "CS 3800", "CS 4100" }, scienceResult.Used.Select(use => use.Key.ToString()).ToHashSet());
        Assert.Equal("Petition approved", scienceResult.Substitutions[0].Note);

        Assert.Equal(new HashSet<string> { "CS 4400", "Electives" }, audit.UnmatchedSubstitutions.Select(item => item.RuleTitle).ToHashSet());
        var counted = audit.RuleResults.SelectMany(result => result.Used.Select(use => use.Key)).ToList();
        Assert.Equal(counted.Count, counted.ToHashSet().Count);
    }

    [Fact]
    public void ShortfallLandsOnTheHoursRequirement()
    {
        var requirements = Requirements();
        var plan = new DegreePlan { Name = "Short" };
        plan.Completed = [Course("CS 1800"), Course("CS 1802", 1), Course("CS 3000"), Course("CY 2550")];
        plan.Terms =
        [
            new DegreePlanTerm
            {
                Season = PlanSeason.Spring,
                Year = 2027,
                Courses = [Course("CS 3386"), Course("CS 4050"), Course("CS 2963", 1)],
            },
        ];
        plan.ConcentrationID = "concentration-in-human-centered-computing";
        var science = Rule(requirements, rule => rule.Label.StartsWith("Complete two courses", StringComparison.Ordinal));
        var electives = Rule(requirements, rule => rule.Label.StartsWith("Complete 8 semester hours of CS", StringComparison.Ordinal));
        plan.Substitutions =
        [
            new RequirementSubstitution
            {
                RuleID = science.Id,
                RuleTitle = "Science Requirement",
                Change = SubstitutionChange.Replace(new RequirementReplacement { Count = 2, LikeRuleID = electives.Id, LikeRuleTitle = "Khoury Elective Courses" }),
            },
        ];
        var audit = DegreeAudit.Run(plan, requirements);
        var scienceResult = audit.Result(science.Id)!;
        Assert.Equal(DegreeAudit.Status.Planned, scienceResult.Status);
        Assert.Equal(new HashSet<string> { "CS 3386", "CS 4050" }, scienceResult.Used.Select(use => use.Key.ToString()).ToHashSet());
        var electiveResult = audit.Result(electives.Id)!;
        Assert.Equal(DegreeAudit.Status.Partial, electiveResult.Status);
        Assert.Equal(["CS 2963"], electiveResult.Used.Select(use => use.Key.ToString()));
        Assert.Equal(7, electiveResult.RemainingHours);
    }

    [Fact]
    public void RebalancesToMeetShortRequirements()
    {
        var page = """
            <div id="programrequirementstextcontainer" class="tab_content">
            <h2>Major Requirements</h2>
            <table class="sc_courselist"><tbody>
            <tr class="even areaheader firstrow"><td colspan="2"><span class="courselistcomment areaheader">Computing and Social Issues</span></td><td class="hourscol"></td></tr>
            <tr class="odd"><td colspan="2"><span class="courselistcomment">Complete one of the following:</span></td><td class="hourscol">4</td></tr>
            <tr class="even"><td class="codecol"><a class="bubblelink code">CY&#160;4170</a></td><td>The Law, Ethics, and Policy of Data and Digital Technologies</td><td class="hourscol"></td></tr>
            <tr class="odd"><td class="codecol"><a class="bubblelink code">HIST&#160;2220</a></td><td>History of Technology</td><td class="hourscol"></td></tr>
            <tr class="even areaheader"><td colspan="2"><span class="courselistcomment areaheader">Humanities</span></td><td class="hourscol"></td></tr>
            <tr class="odd"><td colspan="2"><span class="courselistcomment">Complete one of the following:</span></td><td class="hourscol">4</td></tr>
            <tr class="even"><td class="codecol"><a class="bubblelink code">HIST&#160;2220</a></td><td>History of Technology</td><td class="hourscol"></td></tr>
            <tr class="odd"><td class="codecol"><a class="bubblelink code">PHIL&#160;1145</a></td><td>Technology and Human Values</td><td class="hourscol"></td></tr>
            <tr class="even areaheader"><td colspan="2"><span class="courselistcomment areaheader">Khoury Elective Courses</span></td><td class="hourscol"></td></tr>
            <tr class="odd"><td colspan="2"><span class="courselistcomment">Complete 8 semester hours of CS, CY, DS, or IS classes that are not already required. Choose courses within the following ranges:</span></td><td class="hourscol">8</td></tr>
            <tr class="even"><td colspan="2"><div style="margin-left:20px;" class="blockindent"><span class="courselistcomment commentindent"><a class="bubblelink code">CS&#160;2500</a> or higher</span></div></td><td class="hourscol"></td></tr>
            <tr class="odd lastrow"><td colspan="2"><div style="margin-left:20px;" class="blockindent"><span class="courselistcomment commentindent"><a class="bubblelink code">CY&#160;2000</a> or higher</span></div></td><td class="hourscol"></td></tr>
            </tbody></table>
            <h2>Required General Electives</h2>
            <table class="sc_courselist"><tbody>
            <tr class="even lastrow firstrow"><td colspan="2"><span class="courselistcomment">Complete 4 semester hours of general electives.</span></td><td class="hourscol">4</td></tr>
            </tbody></table>
            </div>
            """;
        var requirements = ProgramCatalogParser.Requirements(page, new CatalogYear(2024), Program) ?? throw new InvalidOperationException("page");
        var rules = requirements.AllRules;
        Assert.Equal(4, rules.Count);
        var (socialIssues, humanities, electives) = (rules[0], rules[1], rules[2]);

        DegreeAudit Audit(string[] completed, string[] planned)
        {
            var plan = new DegreePlan { Name = "Plan" };
            plan.Completed = completed.Select(code => Course(code)).ToList();
            plan.Terms = [new DegreePlanTerm { Season = PlanSeason.Spring, Year = 2027, Courses = planned.Select(code => Course(code)).ToList() }];
            return DegreeAudit.Run(plan, requirements);
        }
        HashSet<string> Courses(DegreeAudit audit, RequirementRule rule) =>
            audit.Result(rule.Id)?.Used.Select(use => use.Key.ToString()).ToHashSet() ?? [];

        var swap = Audit(["CY 4170", "CS 3200", "PHIL 1145"], ["HIST 2220", "MKTG 4510"]);
        Assert.Equal(swap.TotalCount, swap.SatisfiedCount);
        Assert.Equal(new HashSet<string> { "CS 3200", "CY 4170" }, Courses(swap, electives));
        Assert.Equal(new HashSet<string> { "HIST 2220" }, Courses(swap, socialIssues));
        Assert.Equal(new HashSet<string> { "PHIL 1145" }, Courses(swap, humanities));
        Assert.Equal(["MKTG 4510"], swap.GeneralElectives.Select(use => use.Key.ToString()));

        var chain = Audit(["CY 4170", "CS 3200", "HIST 2220"], ["PHIL 1145", "MKTG 4510"]);
        Assert.Equal(chain.TotalCount, chain.SatisfiedCount);
        Assert.Equal(new HashSet<string> { "CS 3200", "CY 4170" }, Courses(chain, electives));
        Assert.Equal(DegreeAudit.Status.Complete, chain.Result(electives.Id)?.Status);
        Assert.Equal(new HashSet<string> { "HIST 2220" }, Courses(chain, socialIssues));
        Assert.Equal(new HashSet<string> { "PHIL 1145" }, Courses(chain, humanities));
        var counted = chain.RuleResults.SelectMany(result => result.Used.Select(use => use.Key)).ToList();
        Assert.Equal(counted.Count, counted.ToHashSet().Count);

        var shortPlan = Audit(["CY 4170", "CS 3200"], ["MKTG 4510"]);
        Assert.Equal(DegreeAudit.Status.Complete, shortPlan.Result(socialIssues.Id)?.Status);
        Assert.Equal(4, shortPlan.Result(electives.Id)?.RemainingHours);
    }

    [Fact]
    public void PetitionsAskWhetherCourseNumberMinimumsApply()
    {
        var requirements = Requirements();
        var electives = Rule(requirements, rule => rule.Label.StartsWith("Complete 8 semester hours of CS", StringComparison.Ordinal));
        var science = Rule(requirements, rule => rule.Label.StartsWith("Complete two courses", StringComparison.Ordinal));
        var plan = new DegreePlan { Name = "Petition" };
        plan.Completed =
        [
            Course("CS 1800"), Course("CS 1802", 1), Course("CS 3000"), Course("CS 4500"), Course("CY 2550"),
            Course("CS 2386", 4, "Game Programming 1"), Course("CS 2484"), Course("CS 4520"), Course("CS 4550"),
        ];
        plan.Terms =
        [
            new DegreePlanTerm
            {
                Season = PlanSeason.Spring,
                Year = 2027,
                Courses = [Course("CS 3386", 4, "Game Programming 2"), Course("CS 3200"), Course("CS 4100"), Course("MKTG 2602"), Course("PHIL 1102")],
            },
        ];
        plan.ConcentrationID = "concentration-in-human-centered-computing";
        RequirementSubstitution Petition(CourseNumberLimit? limit) => new()
        {
            RuleID = science.Id,
            RuleTitle = "Science Requirement",
            Change = SubstitutionChange.Replace(new RequirementReplacement
            {
                Count = 2,
                LikeRuleID = electives.Id,
                LikeRuleTitle = "Khoury Elective Courses",
                CourseNumbers = limit,
            }),
        };

        plan.Substitutions = [Petition(null)];
        var unanswered = DegreeAudit.Run(plan, requirements);
        Assert.Equal(DegreeAudit.Status.Partial, unanswered.Result(electives.Id)?.Status);
        var questions = DegreePetitionQuestions.Questions(plan, requirements, audit: unanswered);
        var question = Assert.Single(questions);
        Assert.Equal([Key("CS 2386")], question.Courses);
        Assert.Equal("Does your Science Requirement petition accept any CS or DS course?", question.Question);
        Assert.Contains("start at CS 2500 or DS 2500", question.Explanation);
        Assert.Contains("CS 2386 counts", question.Explanation);

        plan.Substitutions = [Petition(CourseNumberLimit.AnyNumber)];
        var anyNumber = DegreeAudit.Run(plan, requirements);
        Assert.Contains(anyNumber.Result(science.Id)!.Used, use => use.Key.ToString() == "CS 2386");
        Assert.Equal(DegreeAudit.Status.Planned, anyNumber.Result(electives.Id)?.Status);
        Assert.Empty(DegreePetitionQuestions.Questions(plan, requirements));
        var ranges = Assert.IsType<RequirementKind.Choose>(anyNumber.Result(science.Id)?.EffectiveKind).Ranges;
        Assert.Equal(
            ["Any CS course, except CS 5010", "DS courses up to DS 4999, except DS 4900"],
            ranges.Select(range => range.Label));

        plan.Substitutions = [Petition(CourseNumberLimit.AtLeast(2000))];
        Assert.Equal(DegreeAudit.Status.Planned, DegreeAudit.Run(plan, requirements).Result(electives.Id)?.Status);
        plan.Substitutions = [Petition(CourseNumberLimit.AtLeast(3000))];
        Assert.Equal(DegreeAudit.Status.Partial, DegreeAudit.Run(plan, requirements).Result(electives.Id)?.Status);

        plan.Substitutions = [Petition(CourseNumberLimit.SameAsRequirement)];
        Assert.Empty(DegreePetitionQuestions.Questions(plan, requirements));
        var same = DegreeAudit.Run(plan, requirements);
        var miss = same.NearMisses(electives.Id).First();
        Assert.Equal("CS 2386", miss.Course.Key.ToString());
        Assert.Equal("Below CS 2500", miss.Reason);

        var old = """{"count":2,"likeRuleID":"x","likeRuleTitle":"Khoury Elective Courses","courses":[]}""";
        var decoded = JsonSerializer.Deserialize<RequirementReplacement>(old, AppJson.UserData);
        Assert.NotNull(decoded);
        Assert.Null(decoded.CourseNumbers);
        Assert.Equal(
            "1 course like Khoury Elective Courses, any course number",
            new RequirementReplacement { Count = 1, LikeRuleTitle = "Khoury Elective Courses", CourseNumbers = CourseNumberLimit.AnyNumber }.Summary);
    }

    [Fact]
    public void TopicsCoursesCountEachTime()
    {
        var requirements = Requirements();
        var electives = Rule(requirements, rule => rule.Label.StartsWith("Complete 8 semester hours of CS", StringComparison.Ordinal));
        var plan = new DegreePlan { Name = "Topics" };
        plan.Completed =
        [
            Course("CS 1800"), Course("CS 1802", 1), Course("CS 3000"), Course("CS 4500"), Course("CY 2550"),
            Course("CS 4973", 4, "Topics in Computer Science"), Course("MKTG 2602", 4, "Marketing Management"),
        ];
        plan.Terms =
        [
            new DegreePlanTerm
            {
                Season = PlanSeason.Spring,
                Year = 2027,
                Courses = [Course("CS 4973", 4, "Topics in Computer Science"), Course("MKTG 2602", 4, "Marketing Management")],
            },
        ];
        var audit = DegreeAudit.Run(plan, requirements);
        var result = audit.Result(electives.Id)!;
        Assert.Equal(DegreeAudit.Status.Planned, result.Status);
        Assert.Equal(["CS 4973", "CS 4973 (2)"], result.Used.Select(use => use.Id).OrderBy(id => id, StringComparer.Ordinal));
        var counted = audit.RuleResults.SelectMany(item => item.Used).Concat(audit.Unused).ToList();
        Assert.Single(counted, use => use.Key.ToString() == "MKTG 2602");
        Assert.Equal(counted.Count, counted.Select(use => use.Id).ToHashSet().Count);
        Assert.Equal(new HashSet<CourseKey> { Key("MKTG 2602") }, DegreePlanAnalysis.DuplicateKeys(plan));
    }

    [Fact]
    public void RequiredAlternativesMakeRoom()
    {
        var page = """
            <div id="programrequirementstextcontainer" class="tab_content">
            <h2>Major Requirements</h2>
            <table class="sc_courselist"><tbody>
            <tr class="even areaheader firstrow"><td colspan="2"><span class="courselistcomment areaheader">Software Engineering</span></td><td class="hourscol"></td></tr>
            <tr class="odd"><td class="codecol"><a class="bubblelink code">CS&#160;4500</a></td><td>Software Development</td><td class="hourscol">4</td></tr>
            <tr class="orclass even"><td class="codecol orclass">or <a class="bubblelink code">CS&#160;4530</a></td><td colspan="2">Fundamentals of Software Engineering</td></tr>
            <tr class="odd areaheader"><td colspan="2"><span class="courselistcomment areaheader">Concentration Courses</span></td><td class="hourscol"></td></tr>
            <tr class="even"><td colspan="2"><span class="courselistcomment">Complete two of the following:</span></td><td class="hourscol">8</td></tr>
            <tr class="odd"><td class="codecol"><a class="bubblelink code">CS&#160;4530</a></td><td>Fundamentals of Software Engineering</td><td class="hourscol"></td></tr>
            <tr class="even lastrow"><td class="codecol"><a class="bubblelink code">CS&#160;4550</a></td><td>Web Development</td><td class="hourscol"></td></tr>
            </tbody></table>
            </div>
            """;
        var requirements = ProgramCatalogParser.Requirements(page, new CatalogYear(2024), Program) ?? throw new InvalidOperationException("page");
        var rules = requirements.AllRules;
        Assert.Equal(2, rules.Count);
        var plan = new DegreePlan
        {
            Name = "Plan",
            Completed = [Course("CS 4530"), Course("CS 4550")],
            Terms = [new DegreePlanTerm { Season = PlanSeason.Spring, Year = 2027, Courses = [Course("CS 4500")] }],
        };
        var audit = DegreeAudit.Run(plan, requirements);
        Assert.Equal(2, audit.SatisfiedCount);
        Assert.Equal(["CS 4500"], audit.Result(rules[0].Id)?.Used.Select(use => use.Key.ToString()));
        Assert.Equal(new HashSet<string> { "CS 4530", "CS 4550" }, audit.Result(rules[1].Id)?.Used.Select(use => use.Key.ToString()).ToHashSet());
    }

    [Fact]
    public void SubstitutionsFollowRequirementsAcrossCatalogYears()
    {
        var requirements = Requirements();
        var science = Rule(requirements, rule => rule.Label.StartsWith("Complete two courses", StringComparison.Ordinal));
        var electives = Rule(requirements, rule => rule.Label.StartsWith("Complete 8 semester hours of CS", StringComparison.Ordinal));
        var plan = Plan();
        plan.Substitutions =
        [
            new RequirementSubstitution
            {
                RuleID = "supporting-courses/science-requirement/complete-any-two-courses-from-last-year",
                RuleTitle = "Science Requirement",
                Change = SubstitutionChange.Replace(new RequirementReplacement
                {
                    Count = 2,
                    LikeRuleID = "major-requirements/khoury-elective-courses/older-wording",
                    LikeRuleTitle = "Khoury Elective Courses",
                    CourseNumbers = CourseNumberLimit.SameAsRequirement,
                }),
            },
            new RequirementSubstitution { RuleID = "gone", RuleTitle = "A requirement this year doesn't have", Change = SubstitutionChange.Waive },
        ];
        var audit = DegreeAudit.Run(plan, requirements);
        var result = audit.Result(science.Id)!;
        Assert.Single(result.Substitutions);
        Assert.Equal(DegreeAudit.Status.Complete, result.Status);
        Assert.Equal(new HashSet<string> { "CS 4973", "CS 4535" }, result.Used.Select(use => use.Key.ToString()).ToHashSet());
        Assert.Equal(science.Id, audit.Placements[plan.Substitutions[0].Id]);
        Assert.Equal(["A requirement this year doesn't have"], audit.UnmatchedSubstitutions.Select(item => item.RuleTitle));
        var replacement = Assert.IsType<SubstitutionChange.ReplaceChange>(plan.Substitutions[0].Change).Replacement;
        Assert.Equal(2, replacement.LikeRanges(requirements).Count);
        Assert.Equal(DegreeAudit.Status.Planned, audit.Result(electives.Id)?.Status);
    }

    [Fact]
    public void HourRequirementsKeepOnlyWhatTheyNeed()
    {
        var requirements = Requirements();
        var electives = Rule(requirements, rule => rule.Label.StartsWith("Complete 8 semester hours of CS", StringComparison.Ordinal));
        var plan = new DegreePlan { Name = "Hours" };
        plan.Completed =
        [
            Course("CS 1800"), Course("CS 1802", 1), Course("CS 3000"), Course("CS 4500"), Course("CY 2550"),
            Course("CS 4100"), Course("MATH 1341"), Course("PHIL 1102"),
        ];
        plan.Terms =
        [
            new DegreePlanTerm { Season = PlanSeason.Spring, Year = 2027, Courses = [Course("CS 2963", 1, "Topics")] },
            new DegreePlanTerm { Season = PlanSeason.SummerB, Year = 2027, Courses = [Course("CS 3200")] },
        ];
        var audit = DegreeAudit.Run(plan, requirements);
        var result = audit.Result(electives.Id)!;
        Assert.Equal(DegreeAudit.Status.Planned, result.Status);
        Assert.Equal(new HashSet<string> { "CS 4100", "CS 3200" }, result.Used.Select(use => use.Key.ToString()).ToHashSet());
        Assert.Equal(8, result.Used.Sum(use => use.Credits));
        Assert.Equal(new HashSet<string> { "MATH 1341", "PHIL 1102" }, audit.GeneralElectives.Select(use => use.Key.ToString()).ToHashSet());
        Assert.Equal(["CS 2963"], audit.Unused.Select(use => use.Key.ToString()));

        (string Title, double Credits, bool Completed)[] items = [("A", 5, true), ("B", 4, false), ("C", 4, false)];
        var uses = items.Select((item, index) => new DegreeAudit.CourseUse
        {
            Key = new CourseKey("CS", (4000 + index).ToString()),
            Title = item.Title,
            Credits = item.Credits,
            Semester = item.Completed ? null : "Spring 2027",
            Order = index,
        }).ToList();
        Assert.Equal(new HashSet<string> { "CS 4001", "CS 4002" }, DegreeAudit.FewestCreditsChoice(uses, 8));
    }

    [Fact]
    public void ChecksHoursInTheMajorAndCoop()
    {
        var page = """
            <div id="programrequirementstextcontainer" class="tab_content">
            <h2>Major Requirements</h2>
            <table class="sc_courselist"><tbody>
            <tr class="even firstrow"><td class="codecol"><a class="bubblelink code">CS&#160;3000</a></td><td>Algorithms and Data</td><td class="hourscol">4</td></tr>
            <tr class="odd"><td colspan="2"><span class="courselistcomment">Complete 8 semester hours of CS classes. Choose courses within the following ranges:</span></td><td class="hourscol">8</td></tr>
            <tr class="even lastrow"><td colspan="2"><div style="margin-left:20px;" class="blockindent"><span class="courselistcomment commentindent"><a class="bubblelink code">CS&#160;2500</a> or higher</span></div></td><td class="hourscol"></td></tr>
            </tbody></table>
            <h2>Writing Requirement</h2>
            <table class="sc_courselist"><tbody>
            <tr class="even firstrow lastrow"><td class="codecol"><a class="bubblelink code">ENGW&#160;1111</a></td><td>First-Year Writing</td><td class="hourscol">4</td></tr>
            </tbody></table>
            <h2>Required General Electives</h2>
            <table class="sc_courselist"><tbody>
            <tr class="even lastrow firstrow"><td colspan="2"><span class="courselistcomment">Complete 4 semester hours of general electives.</span></td><td class="hourscol">4</td></tr>
            </tbody></table>
            <h2>GPA Requirement</h2>
            <p>Minimum 2.000 GPA required in the major.</p>
            <h2>Credit Requirement</h2>
            <p>Complete 16 semester hours in the major.</p>
            <h2>NUpath Requirements Satisfied</h2>
            <p>Integrating Knowledge and Skills Through Experience is satisfied through co-op.</p>
            </div>
            """;
        var requirements = ProgramCatalogParser.Requirements(page, new CatalogYear(2024), Program) ?? throw new InvalidOperationException("page");
        DegreeAudit Audit(string[] completed, string[]? planned = null)
        {
            var plan = new DegreePlan { Name = "Checks" };
            plan.Completed = completed.Select(code =>
                code == "COOP 3946" ? Course(code, 0, "Co-op Work Experience - HT") : Course(code)).ToList();
            plan.Terms = [new DegreePlanTerm { Season = PlanSeason.Fall, Year = 2027, Courses = (planned ?? []).Select(code => Course(code)).ToList() }];
            return DegreeAudit.Run(plan, requirements);
        }

        var done = Audit(["CS 3000", "CS 3200", "CS 3500", "CS 4100", "ENGW 1111", "MKTG 2602", "COOP 3946", "CS 1210"]);
        Assert.Equal(2, done.NoteChecks.Count);
        var hours = done.NoteChecks.First(check => check.CheckKind is DegreeAudit.NoteCheck.Kind.MajorHours);
        Assert.Equal(DegreeAudit.Status.Complete, hours.Status);
        Assert.Equal("16 of 16 semester hours", hours.Detail);
        var coop = done.NoteChecks.First(check => check.CheckKind is DegreeAudit.NoteCheck.Kind.Coop);
        Assert.Equal(DegreeAudit.Status.Complete, coop.Status);
        Assert.Equal("COOP 3946 completed", coop.Detail);
        Assert.Equal(done.TotalCount, done.SatisfiedCount);

        var shortPlan = Audit(["CS 3000", "CS 3200", "CS 1210"], ["CS 3500"]);
        Assert.Equal(DegreeAudit.Status.Partial, shortPlan.NoteChecks.First(check => check.CheckKind is DegreeAudit.NoteCheck.Kind.MajorHours).Status);
        Assert.Equal("No co-op in the plan", shortPlan.NoteChecks.First(check => check.CheckKind is DegreeAudit.NoteCheck.Kind.Coop).Detail);
        Assert.True(shortPlan.SatisfiedCount < shortPlan.TotalCount);
    }

    [Fact]
    public void WaiversAndConcentrationChoice()
    {
        var requirements = Requirements();
        var plan = Plan();
        var security = Rule(requirements, rule => rule.Label == "Complete one of the following:");
        plan.Substitutions = [new RequirementSubstitution { RuleID = security.Id, RuleTitle = "Security Course", Change = SubstitutionChange.Waive }];
        plan.ConcentrationID = null;
        var audit = DegreeAudit.Run(plan, requirements);
        Assert.Equal(DegreeAudit.Status.Waived, audit.Result(security.Id)?.Status);
        Assert.True(audit.NeedsConcentration);
        Assert.DoesNotContain(audit.Sections, section => section.Section.IsConcentration);
        Assert.NotEqual(security.Id, audit.RuleCounting(Key("CY 2550"))?.Rule.Id);
    }

    [Fact]
    public void UserDataSkipsUnreadableEntries()
    {
        var data = new UserData { DegreePlans = [Plan(), new DegreePlan { Name = "Second" }] };
        var node = JsonNode.Parse(JsonSerializer.Serialize(data, AppJson.UserData))!.AsObject();
        var plans = node["degreePlans"]!.AsArray();
        plans[0]!["completed"] = "not a list";
        node["searchHistory"] = new JsonArray("cs 3000", 42, "orgo");
        var decoded = JsonSerializer.Deserialize<UserData>(node.ToJsonString(), AppJson.UserData)!;
        Assert.Equal(["Second"], decoded.DegreePlans.Select(plan => plan.Name));
        Assert.Equal(["cs 3000", "orgo"], decoded.SearchHistory);
    }

    [Fact]
    public void OlderSavedPlansStillDecode()
    {
        var plan = Plan();
        plan.Substitutions = [new RequirementSubstitution { RuleID = "x", RuleTitle = "X", Change = SubstitutionChange.Waive }];
        var node = JsonNode.Parse(JsonSerializer.Serialize(plan, AppJson.UserData))!.AsObject();
        foreach (var key in new[] { "substitutions", "catalogYear", "catalogProgramPath", "catalogProgramTitle", "concentrationID" })
            node.Remove(key);
        var decoded = JsonSerializer.Deserialize<DegreePlan>(node.ToJsonString(), AppJson.UserData)!;
        Assert.Equal("Plan", decoded.Name);
        Assert.Equal(plan.Completed.Count, decoded.Completed.Count);
        Assert.Empty(decoded.Substitutions);
        Assert.Null(decoded.CatalogYear);

        var roundTrip = JsonSerializer.Deserialize<DegreePlan>(JsonSerializer.Serialize(plan, AppJson.UserData), AppJson.UserData)!;
        Assert.Equal(plan.Name, roundTrip.Name);
        Assert.Equal(plan.Completed.Select(course => (course.Label, course.Credits, course.Key)), roundTrip.Completed.Select(course => (course.Label, course.Credits, course.Key)));
        Assert.Equal(plan.Terms.Select(term => term.Name), roundTrip.Terms.Select(term => term.Name));
        Assert.Equal(plan.Substitutions.Select(item => item.RuleID), roundTrip.Substitutions.Select(item => item.RuleID));
        Assert.Equal(plan.CatalogYear, roundTrip.CatalogYear);
        Assert.Equal(plan.ConcentrationID, roundTrip.ConcentrationID);
        Assert.Equal(plan.CatalogProgramPath, roundTrip.CatalogProgramPath);
    }
}
