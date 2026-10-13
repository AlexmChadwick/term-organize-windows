// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

/// <summary>NUpath, universitywide minimums, and the program's total hours.</summary>
public class DegreeNUpathTests
{
    public static readonly CatalogProgram Program = new(
        "Computer Science, BSCS (Boston)",
        "undergraduate/computer-information-science/computer-science/bscs/");

    public const string Page = """
        <div id="programrequirementstextcontainer" class="tab_content">
        <h2>Universitywide Requirements</h2>
        <p>All undergraduate students are required to complete the <a href="/archive/2024-2025/undergraduate/university-academics/university-wide-requirements/">Universitywide Requirements</a>.</p>
        <h2>NUpath Requirements</h2>
        <p>All undergraduate students are required to complete the <a href="/archive/2024-2025/undergraduate/university-academics/nupath/">NUpath Requirements</a>.</p>
        <h2>Major Requirements</h2>
        <table class="sc_courselist"><tbody>
        <tr class="even firstrow lastrow"><td class="codecol"><a class="bubblelink code">CS&#160;3000</a></td><td>Algorithms and Data</td><td class="hourscol">4</td></tr>
        </tbody></table>
        <h2>Khoury College GPA Requirement</h2>
        <p>Minimum cumulative 2.000 GPA required in all CS, CY, DS, and IS courses</p>
        <h2>NUpath Requirements Satisfied</h2>
        <p>Integrating Knowledge and Skills Through Experience is satisfied through co-op.</p>
        <h2>Program Requirement</h2>
        <p>134 total semester hours required</p>
        </div>
        """;

    public static readonly Dictionary<string, NUPath[]> Attributes = new()
    {
        ["ENGW 1111"] = [NUPath.Wf],
        ["MATH 1341"] = [NUPath.Fq],
        ["PHYS 1151"] = [NUPath.Nd],
        ["CS 3200"] = [NUPath.Ad, NUPath.Fq],
        ["PHIL 1145"] = [NUPath.Er, NUPath.Si],
        ["ARTH 1111"] = [NUPath.Ic, NUPath.Ei],
        ["HIST 1130"] = [NUPath.Dd, NUPath.Si],
        ["CS 3000"] = [NUPath.Fq],
        ["ENGW 3302"] = [NUPath.Wd],
        ["CS 4535"] = [NUPath.Ce, NUPath.Ex, NUPath.Wi],
        ["CS 3484"] = [NUPath.Wi],
    };

    public static ProgramRequirements Requirements(UniversitywideRequirements? universitywide = null)
    {
        var requirements = ProgramCatalogParser.Requirements(Page, new CatalogYear(2024), Program)
            ?? throw new InvalidOperationException("requirements");
        requirements.Universitywide = universitywide;
        return requirements;
    }

    public static DegreePlanCourse Course(string code, double credits = 4, string title = "") =>
        new(CourseKey.Parse(code), title, credits);

    public static CourseKey Key(string code) => CourseKey.Parse(code) ?? throw new InvalidOperationException(code);

    public static DegreePlan Plan()
    {
        var plan = new DegreePlan { Name = "NUpath" };
        plan.Completed = new[] { "ENGW 1111", "MATH 1341", "PHYS 1151", "CS 3200", "PHIL 1145", "ARTH 1111", "HIST 1130", "CS 3000" }
            .Select(code => Course(code))
            .Append(Course("COOP 3946", 0, "Co-op Work Experience - HT"))
            .ToList();
        plan.Terms =
        [
            new DegreePlanTerm
            {
                Season = PlanSeason.Spring,
                Year = 2027,
                Courses = [Course("ENGW 3302"), Course("CS 4535"), Course("CS 3484")],
            },
        ];
        return plan;
    }

    public static DegreeCourseAttributes CourseAttributes(Dictionary<string, NUPath[]>? table = null, string[]? loading = null)
    {
        table ??= Attributes;
        return new DegreeCourseAttributes(
            table.Select(pair => new KeyValuePair<CourseKey, IEnumerable<NUPath>>(Key(pair.Key), pair.Value)),
            loading?.Select(Key));
    }

    static DegreeAudit.NoteCheck? Nupath(DegreeAudit audit) =>
        audit.NoteChecks.FirstOrDefault(check => check.CheckKind is DegreeAudit.NoteCheck.Kind.Nupath);

    static DegreeAudit.NoteCheck.Item? Item(DegreeAudit.NoteCheck? check, NUPath path) =>
        check?.Items.FirstOrDefault(item => item.Id == path.AttributeCode());

    [Fact]
    public void EveryNUpathRequirementIsChecked()
    {
        var audit = DegreeAudit.Run(Plan(), Requirements(), attributes: CourseAttributes());
        var check = Nupath(audit) ?? throw new InvalidOperationException("nupath");
        Assert.Equal(NUpathRequirement.All.Select(requirement => requirement.Path.AttributeCode()), check.Items.Select(item => item.Id));
        Assert.All(check.Items, item => Assert.True(item.Status?.IsSatisfied()));
        Assert.Equal(DegreeAudit.Status.Planned, check.Status);
        Assert.True(check.Counts);
        Assert.Equal("13 of 13 met", check.Detail);
        Assert.Equal("ENGW 1111 completed", Item(check, NUPath.Wf)?.Detail);
        Assert.Equal(["CS 4535", "CS 3484"], Item(check, NUPath.Wi)?.Courses);
        Assert.Equal("CS 4535 planned \u00b7 Spring 2027 \u00b7 CS 3484 planned \u00b7 Spring 2027", Item(check, NUPath.Wi)?.Detail);
        Assert.Equal(["CS 4535"], Item(check, NUPath.Ce)?.Courses);
        Assert.Equal(["COOP 3946"], Item(check, NUPath.Ex)?.Courses);
        Assert.Equal(DegreeAudit.Status.Complete, Item(check, NUPath.Ex)?.Status);
        var areas = new Dictionary<string, int>();
        foreach (var requirement in NUpathRequirement.All.Where(requirement => requirement.IsLimited))
        {
            foreach (var id in Item(check, requirement.Path)?.Courses ?? [])
                areas[id] = areas.GetValueOrDefault(id) + 1;
        }
        Assert.All(areas.Values, count => Assert.True(count <= 2));
        Assert.Contains(audit.CountedChecks, item => item.CheckKind is DegreeAudit.NoteCheck.Kind.Nupath && item.Status.IsSatisfied());
    }

    [Fact]
    public void ACourseCountsForTwoAreasAtMost()
    {
        var table = Attributes.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        table["ARTH 1111"] = [NUPath.Ic, NUPath.Ei, NUPath.Dd];
        table["HIST 1130"] = [];
        var baseline = DegreeAudit.Run(Plan(), Requirements(), attributes: CourseAttributes());
        var audit = DegreeAudit.Run(Plan(), Requirements(), attributes: CourseAttributes(table));
        var check = Nupath(audit) ?? throw new InvalidOperationException("nupath");
        var open = check.Items.Where(item => item.Status?.IsSatisfied() != true).ToList();
        Assert.Single(open);
        Assert.Equal(DegreeAudit.Status.Missing, open[0].Status);
        Assert.Equal("No course in the plan has it", open[0].Detail);
        Assert.Equal(DegreeAudit.Status.Partial, check.Status);
        Assert.True(check.Counts);
        Assert.StartsWith("12 of 13 met \u00b7 missing ", check.Detail);
        Assert.Equal(baseline.SatisfiedCount - 1, audit.SatisfiedCount);
        Assert.Equal(baseline.TotalCount, audit.TotalCount);
    }

    [Fact]
    public void UnknownCoursesKeepAShortfallFromCounting()
    {
        var table = Attributes.ToDictionary(pair => pair.Key, pair => (NUPath[]?)pair.Value.ToArray());
        table["HIST 1130"] = null;
        var present = table.Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!);
        var requirements = Requirements();
        var audit = DegreeAudit.Run(Plan(), requirements, attributes: CourseAttributes(present));
        var check = Nupath(audit) ?? throw new InvalidOperationException("nupath");
        Assert.Null(Item(check, NUPath.Dd)?.Status);
        Assert.Equal("Not found among the courses with NUpath data", Item(check, NUPath.Dd)?.Detail);
        Assert.False(check.Counts);
        Assert.Equal("12 of 13 met \u00b7 no NUpath data for HIST 1130", check.Detail);
        Assert.DoesNotContain(audit.CountedChecks, item => item.CheckKind is DegreeAudit.NoteCheck.Kind.Nupath);

        var loading = DegreeAudit.Run(Plan(), requirements, attributes: CourseAttributes(present, ["HIST 1130"]));
        var pending = Nupath(loading) ?? throw new InvalidOperationException("loading");
        Assert.Equal("Looking up the plan\u2019s courses\u2026", Item(pending, NUPath.Dd)?.Detail);
        Assert.Equal("12 of 13 met \u00b7 looking up 1 course", pending.Detail);

        present["ARTH 1111"] = [NUPath.Ic, NUPath.Dd];
        present["CS 3000"] = [NUPath.Ei];
        var met = Nupath(DegreeAudit.Run(Plan(), requirements, attributes: CourseAttributes(present)))
            ?? throw new InvalidOperationException("met");
        Assert.True(met.Counts);
        Assert.Equal(DegreeAudit.Status.Planned, met.Status);
    }

    [Fact]
    public void WithoutAttributesNUpathIsNotChecked()
    {
        var audit = DegreeAudit.Run(Plan(), Requirements());
        Assert.Null(Nupath(audit));
    }

    [Fact]
    public void ARenumberedCourseUsesItsReplacementsAttributes()
    {
        var plan = Plan();
        plan.Completed[2] = Course("PHYS 1145");
        var table = Attributes.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        table.Remove("PHYS 1151");
        table["PHYS 1149"] = [NUPath.Nd];
        plan.Equivalences = [new CourseEquivalence { Course = Key("PHYS 1145"), CountsAs = Key("PHYS 1149"), Note = "Renumbered" }];
        var check = Nupath(DegreeAudit.Run(plan, Requirements(), attributes: CourseAttributes(table)))
            ?? throw new InvalidOperationException("nupath");
        Assert.Equal(["PHYS 1145"], Item(check, NUPath.Nd)?.Courses);
        Assert.True(check.Counts);
    }

    [Fact]
    public void WithoutACoopAnExperienceCourseCounts()
    {
        var plan = Plan();
        plan.Completed.RemoveAll(course => course.Key?.Subject == "COOP");
        var audit = DegreeAudit.Run(plan, Requirements(), attributes: CourseAttributes());
        var coop = audit.NoteChecks.FirstOrDefault(check => check.CheckKind is DegreeAudit.NoteCheck.Kind.Coop)
            ?? throw new InvalidOperationException("coop");
        Assert.Equal(DegreeAudit.Status.Planned, coop.Status);
        Assert.Equal("No co-op in the plan \u00b7 CS 4535 (EX) planned", coop.Detail);
        Assert.Equal(["CS 4535"], Item(Nupath(audit), NUPath.Ex)?.Courses);
    }

    [Fact]
    public void MatchingIsOptimal()
    {
        var generator = new Generator(2016);
        var areas = NUpathRequirement.All.Where(requirement => requirement.IsLimited).Select(requirement => requirement.Path).ToList();
        for (var trial = 0; trial < 600; trial++)
        {
            var wanted = generator.Shuffled(areas).Take(generator.NextInt(1, 5)).ToList();
            var courses = Enumerable.Range(0, generator.NextInt(1, 5)).Select(index =>
                new NUpathMatching.Course(
                    "C" + index,
                    wanted.Where(_ => generator.NextInt(0, 9) < 4),
                    generator.NextBool())).ToList();
            var matching = new NUpathMatching(
                courses,
                wanted.Select(area => new NUpathRequirement(area, 1, true)).ToList());
            var chosen = wanted
                .Select(area => matching.Courses(area).FirstOrDefault() is string id ? (area, id) : ((NUPath, string)?)null)
                .OfType<(NUPath Area, string Id)>()
                .ToList();
            var completed = chosen.Count(pair => courses.First(course => course.Id == pair.Id).IsCompleted);
            Assert.All(chosen, pair => Assert.Contains(pair.Area, courses.First(course => course.Id == pair.Id).Paths));
            Assert.All(chosen.GroupBy(pair => pair.Id), group => Assert.True(group.Count() <= 2));

            var best = (Met: 0, Completed: 0);
            void Search(int position, int[] load, int met, int done)
            {
                if (position >= wanted.Count)
                {
                    if (met > best.Met || (met == best.Met && done > best.Completed)) best = (met, done);
                    return;
                }
                Search(position + 1, load, met, done);
                for (var index = 0; index < courses.Count; index++)
                {
                    if (!courses[index].Paths.Contains(wanted[position]) || load[index] >= 2) continue;
                    var next = (int[])load.Clone();
                    next[index]++;
                    Search(position + 1, next, met + 1, done + (courses[index].IsCompleted ? 1 : 0));
                }
            }
            Search(0, new int[courses.Count], 0, 0);
            Assert.Equal(best.Met, chosen.Count);
            Assert.Equal(best.Completed, completed);
        }
    }

    [Fact]
    public void ReadsTheUniversitywideMinimums()
    {
        var archived = """
            <div id="textcontainer"><p>Note: Individual program requirements may exceed the following minima.</p>
            <ul><li>Minimum 128 total semester hours required</li><li>Minimum 2.000 GPA required</li></ul>
            <p>Students must earn a minimum of 64 Northeastern University semester hours in order to receive a bachelor’s degree.</p></div>
            """;
        var minimums = ProgramCatalogParser.UniversitywideRequirements(archived) ?? throw new InvalidOperationException("minimums");
        Assert.Equal(128, minimums.TotalHours);
        Assert.Equal(2, minimums.Gpa);
        Assert.Equal(64, minimums.ResidencyHours);
        Assert.False(minimums.RequiresExperientialLearning);
        var current = archived.Replace(
            "</div>",
            "<p>Students must complete all university and program requirements, including the experiential learning graduation requirement.</p></div>");
        Assert.True(ProgramCatalogParser.UniversitywideRequirements(current)?.RequiresExperientialLearning);
        Assert.Null(ProgramCatalogParser.UniversitywideRequirements("<div id=\"textcontainer\"><p>Nothing here.</p></div>"));
        Assert.Equal(
            "undergraduate/university-academics/university-wide-requirements/",
            ProgramCatalogParser.UniversitywideLink(Page));
    }

    [Fact]
    public void ChecksTheUniversitywideMinimumsItCan()
    {
        var minimums = new UniversitywideRequirements
        {
            TotalHours = 128,
            Gpa = 2,
            ResidencyHours = 64,
            FetchedAt = DateTimeOffset.UtcNow,
        };
        var audit = DegreeAudit.Run(Plan(), Requirements(minimums), attributes: CourseAttributes());
        var check = audit.NoteChecks.FirstOrDefault(item => item.CheckKind is DegreeAudit.NoteCheck.Kind.Universitywide)
            ?? throw new InvalidOperationException("universitywide");
        Assert.Equal(["total-hours", "gpa", "residency"], check.Items.Select(item => item.Id));
        Assert.Equal(DegreeAudit.Status.Partial, check.Items[0].Status);
        Assert.Equal("44 of 128 semester hours \u00b7 12 planned", check.Items[0].Detail);
        Assert.Null(check.Items[1].Status);
        Assert.Equal("Not checked: plans don\u2019t include grades", check.Items[1].Detail);
        Assert.Null(check.Items[2].Status);
        Assert.Equal(DegreeAudit.Status.Partial, check.Status);
        Assert.True(check.Counts);
        Assert.Equal("44 of 128 semester hours \u00b7 12 planned \u00b7 GPA and residency not checked", check.Detail);

        var unread = DegreeAudit.Run(Plan(), Requirements(), attributes: CourseAttributes());
        Assert.DoesNotContain(unread.NoteChecks, item => item.CheckKind is DegreeAudit.NoteCheck.Kind.Universitywide);
    }

    [Fact]
    public void ChecksTheProgramsTotalHours()
    {
        var audit = DegreeAudit.Run(Plan(), Requirements());
        var check = audit.NoteChecks.FirstOrDefault(item => item.CheckKind is DegreeAudit.NoteCheck.Kind.TotalHours)
            ?? throw new InvalidOperationException("total");
        Assert.Equal(DegreeAudit.Status.Partial, check.Status);
        Assert.Equal("44 of 134 semester hours \u00b7 12 planned", check.Detail);
    }

    [Fact]
    public void SaysWhySomeSentencesArentChecked()
    {
        Assert.Equal(
            "Not checked: plans don\u2019t include grades",
            DegreeAudit.UncheckedReason("Minimum cumulative 2.000 GPA required in all CS, CY, DS, and IS courses"));
        Assert.Equal(
            "Not checked: a plan doesn\u2019t say where credits were earned",
            DegreeAudit.UncheckedReason("Students must earn a minimum of 64 Northeastern University semester hours."));
        Assert.Equal(
            "Checked through the concentration you choose",
            DegreeAudit.UncheckedReason("Pick one of the following concentrations and complete four courses in that concentration."));
        Assert.Null(DegreeAudit.UncheckedReason("Courses may not be taken pass/fail."));
    }

    sealed class Generator
    {
        ulong _state;
        public Generator(ulong seed) => _state = seed;

        public ulong Next()
        {
            _state += 0x9E3779B97F4A7C15;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }

        public int NextInt(int minInclusive, int maxInclusive) =>
            minInclusive + (int)(Next() % (uint)(maxInclusive - minInclusive + 1));

        public bool NextBool() => (Next() & 1) == 1;

        public List<T> Shuffled<T>(IReadOnlyList<T> items)
        {
            var list = items.ToList();
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = (int)(Next() % (ulong)(i + 1));
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }
    }
}
