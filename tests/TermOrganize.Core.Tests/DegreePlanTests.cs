// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

/// <summary>Parsing, prerequisite summaries, autofill, and the user-data store for degree plans.</summary>
public class DegreePlanTests
{
    /// <summary>A synthetic degree audit "View Plan" export, as PDFKit extracts it.</summary>
    public const string SampleExport = """
        Printed: 10/03/2026 12:42
        Student: SAMPLE STUDENT
        StudentID: 000000000
        Degree Audit Plan Name: New Plan
        Program: CS-B-CSCI-BSCS
        -
        Completed Courses:
        -
        CS 1800 (4.0 Hours) Discrete Structures
        -
        CS 1802 (1.0 Hour) Seminar for CS 1800
        -
        NO AP CREDIT (0.0 Hours) No description
        -
        CS 2510 (4.0 Hours) Fundamentals of Computer Sci
        -
        CS 3500 (4.0 Hours) Object-Oriented Design
        -
        CS 3000 (4.0 Hours) Algorithms & Data
        -
        -
        -------------------------------------------------------------------------------------------------------------------
        Spring 2027 (9.0 Hours)
        -
        CS 3650 (4.0 Hours) Computer Systems
        -
        CS 4530 (4.0 Hours) Fundamentals of Software Engin
        -
        CS 2963 (1.0 Hour) Topics
        -------------------------------------------------------------------------------------------------------------------
        Summer Session A 2027 (4.0 Hours)
        -
        CS 4500 (4.0 Hours) Software Development
        -------------------------------------------------------------------------------------------------------------------
        Summer Session B 2027 (0.0 Hours)
        -------------------------------------------------------------------------------------------------------------------
        Fall 2027 (0.0 Hours)
        """;

    static CourseKey Key(string subject, string number) => new(subject, number);

    [Fact]
    public void ParsesTheExport()
    {
        var result = DegreePlanFormat.Parse(SampleExport, "view-plan.pdf");
        var plan = result.Plan;
        Assert.Equal("New Plan", plan.Name);
        Assert.Equal("CS-B-CSCI-BSCS", plan.Program);
        Assert.Equal("SAMPLE STUDENT", plan.StudentName);
        Assert.Equal("view-plan.pdf", plan.ImportedFrom);
        Assert.NotNull(plan.PrintedAt);
        Assert.Equal(6, plan.Completed.Count);
        Assert.Equal(1, plan.Completed[1].Credits);
        Assert.Null(plan.Completed[2].Key);
        Assert.Equal("NO AP CREDIT", plan.Completed[2].Label);
        Assert.Equal(Key("CS", "1800"), plan.Completed[0].Key);
        Assert.Equal(
            ["Spring 2027", "Summer Session A 2027", "Summer Session B 2027", "Fall 2027"],
            plan.Terms.Select(term => term.Name));
        Assert.Equal(3, plan.Terms[0].Courses.Count);
        Assert.Equal(9, plan.Terms[0].Credits);
        Assert.Empty(plan.Terms[2].Courses);
        Assert.Empty(result.SkippedLines);
    }

    [Fact]
    public void ParsesRawPaddedLines()
    {
        var raw = "Degree Audit Plan Name:\tMy Plan\nProgram:\t\tCS-B\nCompleted Courses:  \n" +
            "GAME1110 (4.0 Hours) Introduction to Video Game De\nCS  2500 (4.0 Hours) Fundamentals of Computer Sci \n" +
            new string('-', 115) + "\nSpring  2027 (4.0 Hours) \nCS  3484 (4.0 Hours) GUI Programming\n";
        var plan = DegreePlanFormat.Parse(raw).Plan;
        Assert.Equal("My Plan", plan.Name);
        Assert.Equal(["GAME 1110", "CS 2500"], plan.Completed.Select(course => course.Label));
        Assert.Equal("Fundamentals of Computer Sci", plan.Completed[1].Title);
        Assert.Equal(PlanSeason.Spring, plan.Terms[0].Season);
        Assert.Equal(Key("CS", "3484"), plan.Terms[0].Courses[0].Key);
    }

    [Fact]
    public void RejectsOtherDocuments()
    {
        var error = Assert.Throws<DegreePlanFormat.ImportException>(() =>
            DegreePlanFormat.Parse("Hello world\nThis is a receipt\nTotal $4.00"));
        Assert.Equal(DegreePlanFormat.ImportErrorKind.NotADegreePlan, error.Kind);
    }

    [Fact]
    public void CleansUpExtractionArtifacts()
    {
        var text = "Degree Audit Plan Name:\u0000New Plan\n" +
            "Program:\u0000\u0000CS-B-CSCI-BSCS\n" +
            "Student:\u0000\u0000SAMPLE STUDENT\n" +
            "Completed Courses:\n" +
            "-ENGW3302 (4.0 Hours) Advanced Writing in Tech Prof\n" +
            "- CS 1800 (4.0 Hours) Discrete\u200B Structures\n" +
            DegreePlanFormat.Separator + "\n" +
            "-Spring 2027 (4.0 Hours)\n" +
            "CS\u00A03484 (4.0 Hours) GUI Programming\n";
        var plan = DegreePlanFormat.Parse(text).Plan;
        Assert.Equal("New Plan", plan.Name);
        Assert.Equal("CS-B-CSCI-BSCS", plan.Program);
        Assert.Equal("SAMPLE STUDENT", plan.StudentName);
        Assert.Equal(["ENGW 3302", "CS 1800"], plan.Completed.Select(course => course.Label));
        Assert.Equal("Discrete Structures", plan.Completed[1].Title);
        Assert.Single(plan.Terms);
        Assert.Equal(["CS 3484"], plan.Terms[0].Courses.Select(course => course.Label));
    }

    [Fact]
    public void FormatsLikeTheAudit()
    {
        var plan = DegreePlanFormat.Parse(SampleExport).Plan;
        Assert.Equal("CS  1800 (4.0 Hours) Discrete Structures", DegreePlanFormat.CourseLine(plan.Completed[0]));
        Assert.Equal("CS  1802 (1.0 Hour) Seminar for CS 1800", DegreePlanFormat.CourseLine(plan.Completed[1]));
        Assert.Equal("NO AP CREDIT (0.0 Hours) No description", DegreePlanFormat.CourseLine(plan.Completed[2]));
        Assert.Equal("Spring  2027 (9.0 Hours) ", DegreePlanFormat.TermHeader(plan.Terms[0]));
        Assert.Equal("Summer Session A 2027 (4.0 Hours) ", DegreePlanFormat.TermHeader(plan.Terms[1]));
        Assert.Equal(115, DegreePlanFormat.Separator.Length);
        var text = DegreePlanFormat.Text(plan, includeStudent: false);
        Assert.DoesNotContain("StudentID", text);
        Assert.Contains("Degree Audit Plan Name:\tNew Plan", text);
        Assert.Contains("Program:\t\tCS-B-CSCI-BSCS", text);
    }

    [Fact]
    public void PrerequisiteChecks()
    {
        var cs2100 = Key("CS", "2100");
        var cs2510 = Key("CS", "2510");
        var cs1800 = Key("CS", "1800");
        var requisite = Requisite.And(Requisite.Or(Requisite.Course(cs2100), Requisite.Course(cs2510)), Requisite.Course(cs1800));
        Assert.Equal(DegreePlanAnalysis.PrerequisiteStatus.Met, DegreePlanAnalysis.Status(requisite, [cs2510, cs1800]));
        Assert.Equal(DegreePlanAnalysis.PrerequisiteStatus.Missing("CS 1800"), DegreePlanAnalysis.Status(requisite, [cs2510]));
        Assert.Equal(DegreePlanAnalysis.PrerequisiteStatus.Missing("CS 2100 or CS 2510"), DegreePlanAnalysis.Status(requisite, [cs1800]));
        Assert.Equal(
            DegreePlanAnalysis.PrerequisiteStatus.Missing("(CS 2100 or CS 2510) and CS 1800"),
            DegreePlanAnalysis.Status(requisite, []));
        Assert.Equal(DegreePlanAnalysis.PrerequisiteStatus.Met, DegreePlanAnalysis.Status(null, []));
        Assert.Equal(
            DegreePlanAnalysis.PrerequisiteStatus.Met,
            DegreePlanAnalysis.Status(Requisite.Or(Requisite.Test("Placement"), Requisite.Course(cs2100)), []));
    }

    [Fact]
    public void UnmetSummariesAreShort()
    {
        static Requisite Course(string code) => Requisite.Course(CourseKey.Parse(code) ?? throw new InvalidOperationException(code));

        var programming = Requisite.Or(Course("CS 2100"), Course("CS 201M"), Course("CS 2510"), Course("DS 2500"));
        var discrete = Requisite.Or(Course("CS 1800"), Course("MATH 209M"), Course("EECE 2160"));
        var cs3000 = Requisite.Or(Requisite.And(programming, discrete), Course("CS 2700"));
        Assert.Equal("CS 2100, CS 201M or 2 others", DegreePlanAnalysis.UnmetSummary(programming, []));
        Assert.Equal("CS 1800 or MATH 209M or EECE 2160", DegreePlanAnalysis.UnmetSummary(discrete, []));
        Assert.Equal("CS 2700 or another option", DegreePlanAnalysis.UnmetSummary(cs3000, []));
        var cs1800 = new HashSet<CourseKey> { Key("CS", "1800") };
        Assert.Equal("(CS 2100, CS 201M or 2 others) or another option", DegreePlanAnalysis.UnmetSummary(cs3000, cs1800));
        Assert.Equal(1, DegreePlanAnalysis.MissingCount(cs3000, []));
        Assert.Null(DegreePlanAnalysis.UnmetSummary(cs3000, cs1800.Append(Key("CS", "2510"))));

        var nursing = Requisite.And(new[] { "NRSG 3344", "NRSG 3345", "NRSG 3400", "NRSG 3401", "NRSG 3420" }
            .Select(code => Requisite.Or(Course(code), Course(code), Course(code))));
        Assert.Equal("NRSG 3344, NRSG 3345 and 3 more", DegreePlanAnalysis.UnmetSummary(nursing, []));
        Assert.Equal("(NRSG 3344, NRSG 3345 and 3 more) and CS 1800", DegreePlanAnalysis.UnmetSummary(Requisite.And(nursing, Course("CS 1800")), []));
    }

    [Fact]
    public void CoursesBeforeATermIncludeEarlierTerms()
    {
        var plan = DegreePlanFormat.Parse(SampleExport).Plan;
        var beforeSummer = DegreePlanAnalysis.CoursesBefore(1, plan);
        Assert.Contains(Key("CS", "3650"), beforeSummer);
        Assert.Contains(Key("CS", "1800"), beforeSummer);
        Assert.DoesNotContain(Key("CS", "4500"), beforeSummer);
    }

    [Fact]
    public void AutofillPrefersTheExactTermThenTheClosest()
    {
        var plan = DegreePlanFormat.Parse(SampleExport).Plan;
        plan.IsPrimary = true;
        var spring = new Term("202730", "Spring 2027 Semester");
        var exact = DegreePlanAutofill.ScheduleSuggestions(spring, [plan], []);
        Assert.True(exact[0].IsExactTerm);
        Assert.Equal(3, exact[0].Courses.Count);

        var fall = new Term("202710", "Fall 2026 Semester");
        var closest = DegreePlanAutofill.ScheduleSuggestions(fall, [plan], []);
        Assert.False(closest[0].IsExactTerm);
        Assert.Contains("Spring 2027", closest[0].Title);

        var partial = DegreePlanAutofill.ScheduleSuggestions(
            spring, [plan], [], alreadyInPlan: new HashSet<CourseKey> { Key("CS", "3650") });
        Assert.Equal(2, partial[0].Courses.Count);
    }

    [Fact]
    public void AutofillMarksCoursesNotOfferedThisTerm()
    {
        var index = CatalogIndex.Build(BundledCatalog.Load());
        var plan = new DegreePlan
        {
            Name = "Test",
            Terms =
            [
                new DegreePlanTerm
                {
                    Season = PlanSeason.Fall,
                    Year = 2026,
                    Courses =
                    [
                        new DegreePlanCourse(Key("CS", "3000"), "Algorithms", 4),
                        new DegreePlanCourse(Key("CS", "9999"), "Nope", 4),
                    ],
                },
            ],
        };
        var suggestions = DegreePlanAutofill.ScheduleSuggestions(index.Term, [plan], [], index: index);
        Assert.Equal([Key("CS", "3000")], suggestions[0].Courses);
        Assert.Equal([Key("CS", "9999")], suggestions[0].Unavailable);
    }

    [Fact]
    public void NewPlanDraftCopiesTheLatestPlanAndAddsSchedules()
    {
        var source = DegreePlanFormat.Parse(SampleExport).Plan;
        var schedule = new SchedulePlan
        {
            Name = "Plan A",
            TermCode = "202730",
            IsPrimary = true,
            Courses =
            [
                new PlanCourse { Key = Key("CS", "3650"), ColorIndex = 0 },
                new PlanCourse { Key = Key("MATH", "2331"), ColorIndex = 1 },
            ],
        };
        var now = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
        var draft = DegreePlanAutofill.NewDegreePlanDraft("Draft", source, [schedule], _ => null, now);
        Assert.Equal(source.Completed.Count, draft.Completed.Count);
        Assert.NotEqual(source.Completed[0].Id, draft.Completed[0].Id);
        var spring = Assert.Single(draft.Terms, term => term.Season == PlanSeason.Spring && term.Year == 2027);
        Assert.Equal(["CS 3650", "CS 4530", "CS 2963", "MATH 2331"], spring.Courses.Select(course => course.Label));
    }

    [Fact]
    public void TermMatching()
    {
        var summerA = new DegreePlanTerm { Season = PlanSeason.SummerA, Year = 2027 };
        Assert.True(summerA.Matches(new Term("202740", "")));
        Assert.True(summerA.Matches(new Term("202750", "")));
        Assert.False(summerA.Matches(new Term("202760", "")));
        Assert.True(new DegreePlanTerm { Season = PlanSeason.Fall, Year = 2026 }.Matches(new Term("202710", "")));
    }

    [Fact]
    public void StoreKeepsOnePrimaryPlan()
    {
        var path = Path.Combine(Path.GetTempPath(), $"UserData-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserDataStore(path);
            var first = store.AddDegreePlan(new DegreePlan { Name = "A" });
            var second = store.AddDegreePlan(new DegreePlan { Name = "B" });
            Assert.True(store.DegreePlan(first.Id)?.IsPrimary);
            Assert.False(store.DegreePlan(second.Id)?.IsPrimary);
            store.SetPrimaryDegreePlan(second.Id);
            Assert.Equal(second.Id, store.PrimaryDegreePlan?.Id);
            store.DeleteDegreePlan(second.Id);
            Assert.Equal(first.Id, store.PrimaryDegreePlan?.Id);
            Assert.True(store.DegreePlan(first.Id)?.IsPrimary);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
