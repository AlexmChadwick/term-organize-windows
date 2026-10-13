// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

/// <summary>Renumbering a substitution, where a substitute sits, and which catalog sentences show.</summary>
public class DegreeSubstitutionTests
{
    static readonly CourseKey Cs3000 = new("CS", "3000");
    static readonly CourseKey Cs3001 = new("CS", "3001");
    static readonly CourseKey Cs4535 = new("CS", "4535");
    static readonly CourseKey Cs4973 = new("CS", "4973");
    static readonly CourseKey Cy3740 = new("CY", "3740");

    static (UserDataStore Store, string Path) Store()
    {
        var path = Path.Combine(Path.GetTempPath(), $"UserData-{Guid.NewGuid():N}.json");
        return (new UserDataStore(path), path);
    }

    static RequirementRule Rule(string label, ProgramRequirements requirements) =>
        requirements.AllRules.FirstOrDefault(rule => rule.Label == label) ?? throw new InvalidOperationException(label);

    static (DegreePlan Plan, RequirementSubstitution Renumbering, RequirementSubstitution Waiver) RenumberedPlan(ProgramRequirements requirements)
    {
        var plan = DegreeRequirementsTests.Plan();
        plan.Completed.RemoveAll(course => course.Key == Cs3000);
        plan.Completed.Add(DegreeRequirementsTests.Course("CS 3001"));
        var required = Rule("CS 3000", requirements);
        var security = Rule("Complete one of the following:", requirements);
        var renumbering = new RequirementSubstitution
        {
            RuleID = required.Id,
            RuleTitle = "CS 3000",
            Change = SubstitutionChange.CountCourse(Cs3001),
            Note = "Approved",
        };
        var waiver = new RequirementSubstitution { RuleID = security.Id, RuleTitle = "Security Course", Change = SubstitutionChange.Waive };
        plan.Substitutions = [renumbering, waiver];
        return (plan, renumbering, waiver);
    }

    [Fact]
    public void OnlyASubstitutionForOneNamedCourseIsARenumbering()
    {
        var requirements = DegreeRequirementsTests.Requirements();
        var (plan, renumbering, waiver) = RenumberedPlan(requirements);
        var either = Rule("CS 4500 or CS 4530", requirements);
        plan.Substitutions.Add(new RequirementSubstitution
        {
            RuleID = either.Id,
            RuleTitle = "CS 4500 or CS 4530",
            Change = SubstitutionChange.CountCourse(Cs4535),
        });
        plan.Substitutions.Add(new RequirementSubstitution
        {
            RuleID = renumbering.RuleID,
            RuleTitle = "CS 3000",
            Change = SubstitutionChange.CountCourse(Cs3000),
        });
        Assert.Equal(new HashSet<Guid> { renumbering.Id }, DegreeRenumbering.Renumberable(plan, requirements));
        Assert.DoesNotContain(waiver.Id, DegreeRenumbering.Renumberable(plan, requirements));
        Assert.Equal([Cs3000, Cs3001], DegreeRenumbering.CoursesToLookUp(plan, requirements));

        var none = DegreeRequirementsTests.Plan();
        none.Completed.RemoveAll(course => course.Key == Cs3000);
        none.Completed.Add(DegreeRequirementsTests.Course("CS 3001"));
        none.Substitutions = [waiver];
        Assert.Empty(DegreeRenumbering.CoursesToLookUp(none, requirements));
    }

    [Fact]
    public void TurnsASubstitutionIntoTheEquivalenceItAmountsTo()
    {
        var requirements = DegreeRequirementsTests.Requirements();
        var (plan, renumbering, waiver) = RenumberedPlan(requirements);
        var (store, path) = Store();
        try
        {
            var stored = store.AddDegreePlan(plan);
            var change = DegreeRenumbering.Convert(renumbering, stored.Id, requirements, store)
                ?? throw new InvalidOperationException("convert");
            Assert.Equal(Cs3001, change.Course);
            Assert.Equal(Cs3000, change.CountsAs);
            Assert.Equal("CS 3001 now counts as CS 3000", change.Message);
            Assert.True(change.CanUndo);

            var after = store.DegreePlan(stored.Id)!;
            Assert.Equal([waiver.Id], after.Substitutions.Select(item => item.Id));
            Assert.Equal([Cs3001], after.Equivalences.Select(item => item.Course));
            Assert.Equal(Cs3000, after.Equivalences[0].CountsAs);
            Assert.Equal("Renumbered", after.Equivalences[0].Note);

            var audit = DegreeAudit.Run(after, requirements);
            var result = audit.Result(renumbering.RuleID)!;
            Assert.Equal(DegreeAudit.Status.Complete, result.Status);
            Assert.Equal([Cs3001], result.Used.Select(use => use.Key));
            Assert.Equal(Cs3000, result.Used[0].Satisfies);
            Assert.False(result.Used[0].BySubstitution);
            Assert.Null(DegreeRenumbering.Convert(renumbering, stored.Id, requirements, store));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void UndoPutsTheSubstitutionBackWhereItWas()
    {
        var requirements = DegreeRequirementsTests.Requirements();
        var (plan, renumbering, waiver) = RenumberedPlan(requirements);
        var (store, path) = Store();
        try
        {
            var stored = store.AddDegreePlan(plan);
            var change = DegreeRenumbering.Convert(renumbering, stored.Id, requirements, store)!;
            var extra = new RequirementSubstitution { RuleID = "elsewhere", RuleTitle = "Elsewhere", Change = SubstitutionChange.Waive };
            store.UpdateDegreePlan(stored.Id, updated => updated.Substitutions.Add(extra));
            change.Undo(stored.Id, store);
            var restored = store.DegreePlan(stored.Id)!;
            Assert.Equal([renumbering.Id, waiver.Id, extra.Id], restored.Substitutions.Select(item => item.Id));
            Assert.Empty(restored.Equivalences);
            change.Undo(stored.Id, store);
            Assert.Equal(3, store.DegreePlan(stored.Id)?.Substitutions.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DeclaresARenumberingWithoutASubstitution()
    {
        var (store, path) = Store();
        try
        {
            var stored = store.AddDegreePlan(new DegreePlan { Name = "Plan" });
            Assert.Null(DegreeRenumbering.Declare(Cs3000, Cs3000, stored.Id, store));
            var change = DegreeRenumbering.Declare(Cs3001, Cs3000, stored.Id, store)!;
            Assert.Null(change.Substitution);
            Assert.True(change.CanUndo);
            Assert.Equal(["Renumbered"], store.DegreePlan(stored.Id)?.Equivalences.Select(item => item.Note));

            var again = DegreeRenumbering.Declare(Cs3001, Cs3000, stored.Id, store)!;
            Assert.False(again.CanUndo);
            Assert.Single(store.DegreePlan(stored.Id)!.Equivalences);
            change.Undo(stored.Id, store);
            Assert.Empty(store.DegreePlan(stored.Id)!.Equivalences);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AsksAboutARetiredRequiredCourseOnly()
    {
        var requirements = DegreeRequirementsTests.Requirements();
        var setup = RenumberedPlan(requirements);
        var plan = setup.Plan;
        var retired = DegreeSubstitutionEditing.LikelyRenumberings(plan, requirements, key => key == Cs3000);
        Assert.Equal([setup.Renumbering.Id], retired.Select(item => item.Substitution.Id));
        Assert.Equal("Is CS 3001 the new number of CS 3000?", retired[0].Question);
        Assert.Empty(DegreeSubstitutionEditing.LikelyRenumberings(plan, requirements, _ => null));
        Assert.Empty(DegreeSubstitutionEditing.LikelyRenumberings(plan, requirements, _ => false));

        plan.DeclareEquivalence(Cs3001, Cs3000);
        Assert.Empty(DegreeSubstitutionEditing.LikelyRenumberings(plan, requirements, key => key == Cs3000));
    }

    [Fact]
    public void AKeptSubstitutionIsNotAskedAboutAgain()
    {
        var requirements = DegreeRequirementsTests.Requirements();
        var (plan, renumbering, waiver) = RenumberedPlan(requirements);
        bool? Retired(CourseKey key) => key == Cs3000;
        var asked = DegreeRenumbering.Likely(plan, requirements, new HashSet<string>(), Retired);
        Assert.Equal(new HashSet<Guid> { renumbering.Id }, asked.Keys.ToHashSet());
        Assert.False(asked.ContainsKey(waiver.Id));

        var kept = DegreeEquivalencePrompt.PairId(Cs3001, Cs3000);
        Assert.Empty(DegreeRenumbering.Likely(plan, requirements, new HashSet<string> { kept }, Retired));
        var other = DegreeEquivalencePrompt.PairId(Cs3001, Cs4535);
        Assert.Single(DegreeRenumbering.Likely(plan, requirements, new HashSet<string> { other }, Retired));
        Assert.Empty(DegreeRenumbering.Likely(plan, requirements, new HashSet<string>(), _ => null));
    }

    [Fact]
    public void ASubstituteSitsUnderTheRequirementItReplaces()
    {
        var requirements = DegreeRequirementsTests.Requirements();
        var plan = DegreeRequirementsTests.Plan();
        var either = Rule("CS 4500 or CS 4530", requirements);
        var security = Rule("Complete one of the following:", requirements);
        plan.Substitutions =
        [
            new RequirementSubstitution
            {
                RuleID = either.Id,
                RuleTitle = "CS 4500 or CS 4530",
                Change = SubstitutionChange.CountCourse(Cs4535),
                Note = "Approved by advising",
            },
            new RequirementSubstitution
            {
                RuleID = security.Id,
                RuleTitle = "Security Course",
                Change = SubstitutionChange.CountCourse(Cs4973),
            },
        ];
        var audit = DegreeAudit.Run(plan, requirements);
        var eitherResult = audit.Result(either.Id)!;
        var split = DegreeSubstituteRows.Split(eitherResult, either.ListedCourses.ToHashSet());
        Assert.Equal([Cs4535], split.Substitutes.Select(use => use.Key));
        Assert.Empty(split.Others);
        Assert.Equal("CS 4500 or CS 4530", DegreeSubstituteRows.RequirementName(eitherResult, requirements));
        Assert.Equal("Approved by advising", DegreeSubstituteRows.Note(Cs4535, eitherResult));
        Assert.Equal(
            "Counts for CS 4500 or CS 4530 by substitution \u00b7 Approved by advising",
            DegreeSubstituteRows.Caption(Cs4535, eitherResult, requirements));

        var securityResult = audit.Result(security.Id)!;
        Assert.Equal([Cs4973], DegreeSubstituteRows.Split(securityResult, security.ListedCourses.ToHashSet()).Substitutes.Select(use => use.Key));
        Assert.Equal("Counts for Security Course by substitution", DegreeSubstituteRows.Caption(Cs4973, securityResult, requirements));

        var info = new DegreeCourseRowInfo
        {
            Status = new DegreeCourseStatus.Planned("Spring 2027"),
            IsCountedHere = true,
            BySubstitution = true,
            Caption = "ignored",
        };
        Assert.Equal("Approved by advising", DegreeSubstituteRows.Caption(info, Cs4535, eitherResult, requirements, isChild: true));
        Assert.Equal(
            "Counts for CS 4500 or CS 4530 by substitution \u00b7 Approved by advising",
            DegreeSubstituteRows.Caption(info, Cs4535, eitherResult, requirements, isChild: false));
        var plain = new DegreeCourseRowInfo { Status = DegreeCourseStatus.NotInPlan.Instance, Caption = "No longer offered" };
        Assert.Equal("No longer offered", DegreeSubstituteRows.Caption(plain, Cs4535, eitherResult, requirements, isChild: false));

        var listed = DegreeRequirementsTests.Plan();
        listed.Substitutions = [new RequirementSubstitution { RuleID = security.Id, RuleTitle = "Security Course", Change = SubstitutionChange.CountCourse(Cy3740) }];
        listed.Completed.Add(DegreeRequirementsTests.Course("CY 3740"));
        var listedResult = DegreeAudit.Run(listed, requirements).Result(security.Id)!;
        Assert.Empty(DegreeSubstituteRows.Split(listedResult, security.ListedCourses.ToHashSet()).Substitutes);
    }

    const string NotesPage = """
        <div id="programrequirementstextcontainer" class="tab_content">
        <h2>Major Requirements</h2>
        <table class="sc_courselist"><tbody>
        <tr class="even firstrow"><td class="codecol"><a class="bubblelink code">CS&#160;3000</a></td><td>Algorithms and Data</td><td class="hourscol">4</td></tr>
        <tr class="odd"><td colspan="2"><span class="courselistcomment">Complete 8 semester hours of CS classes. Choose courses within the following ranges:</span></td><td class="hourscol">8</td></tr>
        <tr class="even lastrow"><td colspan="2"><div style="margin-left:20px;" class="blockindent"><span class="courselistcomment commentindent"><a class="bubblelink code">CS&#160;2500</a> or higher</span></div></td><td class="hourscol"></td></tr>
        </tbody></table>
        <h2>GPA Requirement</h2>
        <p>Minimum 2.000 GPA required in the major.</p>
        <h2>Credit Requirement</h2>
        <p>Complete 12 semester hours in the major.</p>
        <h2>NUpath Requirements Satisfied</h2>
        <p>Integrating Knowledge and Skills Through Experience is satisfied through co-op.</p>
        <h2>Program Requirement</h2>
        <p>12 total semester hours required</p>
        </div>
        """;

    static (DegreeAudit Audit, ProgramRequirements Requirements) NotesAudit(string[] completed, string[]? planned = null)
    {
        var program = new CatalogProgram("Notes, BS", "undergraduate/notes/");
        var requirements = ProgramCatalogParser.Requirements(NotesPage, new CatalogYear(2024), program)
            ?? throw new InvalidOperationException("notes");
        var plan = new DegreePlan { Name = "Notes" };
        plan.Completed = completed.Select(code =>
            code == "COOP 3946" ? DegreeRequirementsTests.Course(code, 0, "Co-op Work Experience - HT") : DegreeRequirementsTests.Course(code)).ToList();
        plan.Terms =
        [
            new DegreePlanTerm
            {
                Season = PlanSeason.Fall,
                Year = 2027,
                Courses = (planned ?? []).Select(code => DegreeRequirementsTests.Course(code)).ToList(),
            },
        ];
        return (DegreeAudit.Run(plan, requirements), requirements);
    }

    static Dictionary<string, DegreeNoteEntry> Entries(DegreeRequirementsFilter filter, DegreeAudit audit, ProgramRequirements requirements)
    {
        var found = new Dictionary<string, DegreeNoteEntry>();
        foreach (var section in requirements.Sections)
        {
            foreach (var entry in DegreeRequirementNotes.Entries(section, audit, filter, requirements.TotalHours is not null))
                found[section.Title] = entry;
        }
        return found;
    }

    [Fact]
    public void ShowsEachSentenceWithItsCheckForTheFilter()
    {
        var (done, requirements) = NotesAudit(["CS 3000", "CS 3200", "CS 3500", "COOP 3946"]);
        var all = Entries(DegreeRequirementsFilter.All, done, requirements);
        Assert.Equal(
            new HashSet<string> { "GPA Requirement", "Credit Requirement", "NUpath Requirements Satisfied", "Program Requirement" },
            all.Keys.ToHashSet());
        Assert.Null(all["GPA Requirement"].Check);
        Assert.Equal(DegreeAudit.Status.Complete, all["Credit Requirement"].Check?.Status);
        Assert.Equal("COOP 3946 completed", all["NUpath Requirements Satisfied"].Check?.Detail);
        Assert.NotNull(all["Program Requirement"].Check);
        Assert.Equal(
            new HashSet<string> { "Credit Requirement", "NUpath Requirements Satisfied", "Program Requirement" },
            Entries(DegreeRequirementsFilter.Done, done, requirements).Keys.ToHashSet());
        Assert.Empty(Entries(DegreeRequirementsFilter.Remaining, done, requirements));

        var (shortAudit, shortRequirements) = NotesAudit(["CS 3000"], ["CS 3200"]);
        var remaining = Entries(DegreeRequirementsFilter.Remaining, shortAudit, shortRequirements);
        Assert.Equal(
            new HashSet<string> { "Credit Requirement", "NUpath Requirements Satisfied", "Program Requirement" },
            remaining.Keys.ToHashSet());
        Assert.Equal("No co-op in the plan", remaining["NUpath Requirements Satisfied"].Check?.Detail);
        Assert.False(remaining.ContainsKey("GPA Requirement"));
        Assert.False(Entries(DegreeRequirementsFilter.Done, shortAudit, shortRequirements).ContainsKey("NUpath Requirements Satisfied"));
    }

    [Fact]
    public void ACheckThatDoesNotCountYetShowsInAllOnly()
    {
        var check = new DegreeAudit.NoteCheck
        {
            SectionId = "nupath-requirements",
            Note = "NUpath",
            CheckKind = new DegreeAudit.NoteCheck.Kind.Nupath(),
            Status = DegreeAudit.Status.Missing,
            Detail = "looking up 2 courses",
            Counts = false,
        };
        Assert.True(DegreeRequirementsFilter.All.Includes(check));
        Assert.False(DegreeRequirementsFilter.Remaining.Includes(check));
        Assert.False(DegreeRequirementsFilter.Done.Includes(check));
        check.Counts = true;
        Assert.True(DegreeRequirementsFilter.Remaining.Includes(check));
        Assert.False(DegreeRequirementsFilter.Done.Includes(check));
    }
}
