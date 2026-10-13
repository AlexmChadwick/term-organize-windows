// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class PrerequisiteDataTests
{
    private static CourseKey Key(string code) => CourseKey.Parse(code) ?? throw new InvalidOperationException(code);

    [Fact]
    public void CourseInfoNormalizesEquivalents()
    {
        var info = new CourseInfo(Key("CS 2100"), "PDI 1", 4, 4, equivalents: [Key("CS 2510"), Key("CS 2100"), Key("CS 2510"), Key("CS 2000")], sourceTerm: "202710");
        Assert.Equal([Key("CS 2000"), Key("CS 2510")], info.Equivalents);
        Assert.Equal("4 credits", info.CreditsLabel);
        Assert.Equal("1\u20134 credits", new CourseInfo(Key("CS 4973"), "Topics", 1, 4, sourceTerm: "202710").CreditsLabel);
        Assert.Null(new CourseInfo(Key("CS 4973"), "Topics", sourceTerm: "202710").CreditsLabel);
    }

    [Fact]
    public void GroupsAreSymmetricAndChainsMerge()
    {
        var cs2100 = Key("CS 2100");
        var cs2510 = Key("CS 2510");
        var equivalences = new CourseEquivalences([(cs2100, cs2510), (Key("ENGW 1102"), Key("ENGL 1102"))]);
        Assert.Equal([cs2510], equivalences.EquivalentsOf(cs2100).OrderBy(key => key));
        Assert.True(equivalences.AreEquivalent(Key("ENGL 1102"), Key("ENGW 1102")));
        Assert.True(equivalences.AreEquivalent(cs2100, cs2100));
        Assert.False(equivalences.AreEquivalent(cs2100, Key("ENGL 1102")));
        Assert.Empty(equivalences.EquivalentsOf(Key("CS 3100")));
        Assert.Equal([Key("CS 3100")], equivalences.GroupOf(Key("CS 3100")));
        Assert.True(CourseEquivalences.None.IsEmpty);

        var forward = new CourseEquivalences([(Key("CS 2500"), Key("CS 2000")), (Key("CS 2000"), cs2100)]);
        var backward = new CourseEquivalences([(cs2100, Key("CS 2000")), (Key("CS 2000"), Key("CS 2500"))]);
        Assert.Equal(forward, backward);
        Assert.Single(forward.Groups);
        Assert.True(forward.AreEquivalent(Key("CS 2500"), cs2100));
        Assert.Equal([Key("CS 2000"), cs2100, Key("CS 2500")], forward.GroupOf(cs2100));
    }

    [Fact]
    public void OverlappingGroupsMergeAndSingletonsAreDropped()
    {
        var equivalences = new CourseEquivalences(new CourseKey[][]
        {
            [Key("CS 2500"), Key("CS 2000")],
            [Key("CS 3100"), Key("CS 3500")],
            [Key("CS 2000"), Key("CS 2100")],
            [Key("CS 2510")],
            [],
            [Key("CS 2510"), Key("CS 2510")],
        });
        Assert.Equal(2, equivalences.Groups.Count);
        Assert.Equal([Key("CS 2000"), Key("CS 2100"), Key("CS 2500")], equivalences.Groups[0]);
        Assert.DoesNotContain(Key("CS 2510"), equivalences.Courses);
    }

    [Fact]
    public void ExpandingAndSatisfierUseTheTakenCourse()
    {
        var equivalences = new CourseEquivalences([(Key("CS 2100"), Key("CS 2510")), (Key("CS 3100"), Key("CS 3500"))]);
        var taken = new HashSet<CourseKey> { Key("CS 2510"), Key("ENGL 1102") };
        Assert.True(equivalences.Expanding(taken).SetEquals([Key("CS 2510"), Key("CS 2100"), Key("ENGL 1102")]));
        Assert.Empty(equivalences.Expanding([]));
        Assert.True(CourseEquivalences.None.Expanding(taken).SetEquals(taken));

        var chain = new CourseEquivalences([(Key("CS 2500"), Key("CS 2000")), (Key("CS 2000"), Key("CS 2100"))]);
        Assert.Equal(Key("CS 2100"), chain.Satisfier(Key("CS 2100"), [Key("CS 2100"), Key("CS 2500")]));
        Assert.Equal(Key("CS 2500"), chain.Satisfier(Key("CS 2100"), [Key("CS 2500")]));
        Assert.Equal(Key("CS 2000"), chain.Satisfier(Key("CS 2100"), [Key("CS 2500"), Key("CS 2000")]));
        Assert.Equal([Key("CS 2000"), Key("CS 2500")], chain.Satisfiers(Key("CS 2100"), [Key("CS 2500"), Key("CS 2000")]));
        Assert.Null(chain.Satisfier(Key("CS 2100"), [Key("CS 3100")]));
        Assert.Equal(Key("CS 3100"), chain.Satisfier(Key("CS 3100"), [Key("CS 3100")]));
        Assert.Null(CourseEquivalences.None.Satisfier(Key("CS 2100"), [Key("CS 2510")]));
    }

    [Fact]
    public void MergingTwoSourcesAndJsonRoundTrip()
    {
        var banner = new CourseEquivalences([(Key("CS 2100"), Key("CS 2510"))]);
        var student = new CourseEquivalences([(Key("CS 2510"), Key("CS 2500")), (Key("CS 3100"), Key("CS 3500"))]);
        var merged = banner.Merging(student);
        Assert.True(merged.AreEquivalent(Key("CS 2100"), Key("CS 2500")));
        Assert.True(merged.AreEquivalent(Key("CS 3100"), Key("CS 3500")));
        Assert.Equal(banner, banner.Merging(CourseEquivalences.None));
        Assert.Equal(student, CourseEquivalences.None.Merging(student));
        Assert.True(banner.Adding(Key("CS 2000"), Key("CS 2100")).AreEquivalent(Key("CS 2000"), Key("CS 2510")));

        var json = JsonSerializer.Serialize(merged, AppJson.UserData);
        var decoded = JsonSerializer.Deserialize<CourseEquivalences>(json, AppJson.UserData);
        Assert.Equal(merged, decoded);
    }

    [Fact]
    public void StudentDeclarationsBecomeGroups()
    {
        var declared = new[]
        {
            new CourseEquivalence { Course = Key("CS 2510"), CountsAs = Key("CS 2100"), Note = "Renumbered" },
            new CourseEquivalence { Course = Key("CS 2500"), CountsAs = Key("CS 2000") },
            new CourseEquivalence { Course = Key("CS 2000"), CountsAs = Key("CS 2100") },
        };
        var equivalences = CourseEquivalences.FromDeclared(declared);
        Assert.Equal([Key("CS 2000"), Key("CS 2100"), Key("CS 2500"), Key("CS 2510")], equivalences.Groups[0]);

        var plan = new DegreePlan { Name = "Plan" };
        Assert.True(plan.DeclareEquivalence(Key("CS 2510"), Key("CS 2100"), "Renumbered"));
        Assert.False(plan.DeclareEquivalence(Key("CS 2100"), Key("CS 2510")));
        Assert.False(plan.DeclareEquivalence(Key("CS 2100"), Key("CS 2100")));
        Assert.Single(plan.Equivalences);
        Assert.True(plan.DeclaredEquivalences.AreEquivalent(Key("CS 2100"), Key("CS 2510")));
        plan.RemoveEquivalence(plan.Equivalences[0].Id);
        Assert.True(plan.DeclaredEquivalences.IsEmpty);

        var draft = DegreePlanAutofill.NewDegreePlanDraft("Draft", plan, [], _ => null);
        Assert.Empty(draft.Equivalences);
        plan.DeclareEquivalence(Key("CS 2510"), Key("CS 2100"), "Renumbered");
        draft = DegreePlanAutofill.NewDegreePlanDraft("Draft", plan, [], _ => null);
        Assert.Equal(Key("CS 2510"), Assert.Single(draft.Equivalences).Course);
        Assert.NotEqual(plan.Equivalences[0].Id, draft.Equivalences[0].Id);
    }

    [Fact]
    public void CrossListedCoursesAreEquivalentOnlyWhenTheyAreTheSameCourse()
    {
        Course Course(string code, string title, bool topics = false)
        {
            var key = Key(code);
            return new Course { Subject = key.Subject, Number = key.Number, Title = title, SpecialTopics = topics, DetailsLoaded = true };
        }
        CourseSection Section(string crn, string code, params string[] cross) 
        {
            var key = Key(code);
            return new CourseSection { Crn = crn, Subject = key.Subject, Number = key.Number, Title = key.ToString(), CrossListCRNs = cross.ToList() };
        }
        var catalog = new TermCatalog
        {
            Term = new Term("202710", "Fall 2026 Semester"),
            Courses =
            [
                Course("AFCS 1225", "Gender, Race, and Medicine"),
                Course("HIST 1225", "Gender, Race, and Medicine "),
                Course("WMNS 1225", "Gender, Race, and Medicine"),
                Course("ALY 6983", "Special Topics", topics: true),
                Course("CS 6983", "Special Topics", topics: true),
                Course("CS 3000", "Algorithms and Data"),
                Course("CS 5800", "Algorithms"),
                Course("ARCH 5310", "Design Tactics"),
                Course("LARC 5310", "Urban Landscape Seminar"),
            ],
            Sections =
            [
                Section("1", "AFCS 1225", "2", "3"),
                Section("2", "HIST 1225", "1", "3"),
                Section("3", "WMNS 1225", "1", "2"),
                Section("4", "ALY 6983", "5"),
                Section("5", "CS 6983", "4"),
                Section("6", "CS 3000", "7"),
                Section("7", "CS 5800", "6"),
                Section("8", "ARCH 5310", "9"),
                Section("9", "LARC 5310", "8"),
            ],
        };
        var equivalences = CrossListedEquivalences.FromCatalog(CatalogIndex.Build(catalog));
        Assert.Equal([Key("AFCS 1225"), Key("HIST 1225"), Key("WMNS 1225")], Assert.Single(equivalences.Groups));
    }

    [Fact]
    public void TitlesAreReducedAndCompared()
    {
        Assert.True(new CourseTitleMatcher.Title("Principles of Human-Computer Interaction").Words.SetEquals(["human", "computer", "interaction"]));
        Assert.True(new CourseTitleMatcher.Title("Intr to Data Structures & Algorithms").Words.SetEquals(["data", "structure", "algorithm"]));
        Assert.True(new CourseTitleMatcher.Title("Fundamentals of Computer Science 2").Parts.SetEquals(["2"]));
        Assert.Equal(1, CourseTitleMatcher.Similarity("Human Computer Interaction", "Principles of Human-Computer Interaction"));
        Assert.Null(CourseTitleMatcher.Similarity("Human Computer Interaction", "Computer Systems"));
        Assert.Null(CourseTitleMatcher.Similarity("Fundamentals of Computer Science 1", "Fundamentals of Computer Science 2"));
        Assert.Null(CourseTitleMatcher.Similarity("Machine Learning", "Advanced Machine Learning"));
        Assert.Null(CourseTitleMatcher.Similarity("Seminar", "Seminar"));
        Assert.Equal(1, CourseTitleMatcher.Similarity("Machine Learning", "Machine Learning"));
        Assert.Equal("engin", new CourseTitleMatcher.Title("Fundamentals of Software Engin").CutOffWord);
        Assert.Equal(1, CourseTitleMatcher.Similarity("Fundamentals of Software Engin", "Fundamentals of Software Engineering"));
    }

    [Fact]
    public void SuggestsTheCurrentCourseListedBesideTheRetiredOne()
    {
        Requisite Either(params string[] codes) => Requisite.Or(codes.Select(code => Requisite.Course(Key(code))));
        var catalog = new (CourseKey Course, Requisite Requisite)[]
        {
            (Key("CS 3000"), Requisite.And(Either("CS 2100", "CS 2510", "DS 2500"), Either("CS 1800", "MATH 1365"))),
            (Key("CS 3200"), Either("CS 2100", "CS 2510")),
            (Key("CS 3386"), Requisite.And(
                Requisite.Or(Requisite.Or(Requisite.Course(Key("CS 2100")), Requisite.Course(Key("CS 201M"))), Requisite.Course(Key("CS 2510"))),
                Either("CS 2386", "CS 3540"))),
            (Key("CS 4100"), Requisite.Course(Key("CS 2510"))),
            (Key("CS 4200"), Requisite.And(Requisite.Course(Key("CS 2510")), Requisite.Course(Key("MATH 1365")))),
            (Key("ANTH 2000"), Either("ANTH 1101", "POLS 1160")),
        };
        var current = new HashSet<CourseKey> { Key("CS 2100"), Key("DS 2500"), Key("CS 3000"), Key("ANTH 1101") };
        var found = EquivalenceSuggester.Suggestions(Key("CS 2510"), catalog, key => current.Contains(key));
        var suggestion = Assert.Single(found);
        Assert.Equal(Key("CS 2100"), suggestion.Candidate);
        Assert.Equal(3, suggestion.Together);
        Assert.Equal(5, suggestion.Total);
        Assert.Contains("Listed together", suggestion.Evidence, StringComparison.Ordinal);

        var titles = new Dictionary<CourseKey, string>
        {
            [Key("IS 4300")] = "Human Computer Interaction",
            [Key("CS 2484")] = "Principles of Human-Computer Interaction",
            [Key("CS 3650")] = "Computer Systems",
        };
        var byTitle = EquivalenceSuggester.Suggestions(
            Key("IS 4300"),
            [],
            _ => true,
            title: key => titles.GetValueOrDefault(key),
            titleCandidates: [Key("CS 3650"), Key("CS 2484")]);
        Assert.Equal(Key("CS 2484"), Assert.Single(byTitle).Candidate);
        Assert.Equal(0, byTitle[0].Total);
        Assert.Contains("Similar title", byTitle[0].Evidence, StringComparison.Ordinal);
    }
}
