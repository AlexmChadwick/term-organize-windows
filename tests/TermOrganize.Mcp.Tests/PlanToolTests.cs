// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using TermOrganize.Core;
using TermOrganize.Mcp;

namespace TermOrganize.Mcp.Tests;

public sealed class PlanToolTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;
    private readonly PlanService _plans;
    private readonly PlanReadTools _read;
    private readonly PlanWriteTools _write;

    public PlanToolTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "term-organize-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "UserData.json");
        _plans = new PlanService(_path, SharedCatalog.Library);
        _read = new PlanReadTools(_plans);
        _write = new PlanWriteTools(_plans);
    }

    [Fact]
    public void ListIsEmptyUntilAPlanIsCreated()
    {
        var listed = _read.list_plans();
        Assert.Empty((List<Dictionary<string, object?>>)listed["plans"]!);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void WritesRoundTripThroughTheAppLoader()
    {
        var both = SharedCatalog.OfferedBoth;
        var created = _write.create_plan("Primary", ["Fall 2026", "Spring 2027"]);
        var id = (string)created["id"]!;
        var revision = (int)created["revision"]!;
        Assert.Equal(1, revision);
        var section = SharedCatalog.Library.ByCode["202710"].Sections(new CourseKey(both.Subject, both.Number))[0];

        var added = _write.add_course(id, "Fall 2026", both.Subject, both.Number, Rev(revision), section.Crn);
        revision = (int)added["revision"]!;
        var renamed = _write.rename_plan(id, "Renamed", Rev(revision));
        revision = (int)renamed["revision"]!;
        Assert.Equal("Renamed", renamed["name"]);

        var moved = _write.move_course(id, both.Subject, both.Number, "Fall 2026", "Spring 2027", Rev(revision));
        revision = (int)moved["revision"]!;
        var springSection = SharedCatalog.Library.ByCode["202730"].Sections(new CourseKey(both.Subject, both.Number))[0];
        var selected = _write.set_section(id, "Spring 2027", both.Subject, both.Number, springSection.Crn, Rev(revision));
        revision = (int)selected["revision"]!;

        var loaded = new UserDataStore(_path);
        var plan = loaded.DegreePlan(Guid.Parse(id))!;
        Assert.Equal("Renamed", plan.Name);
        var spring = plan.Terms.Single(term => term.Season == PlanSeason.Spring);
        Assert.Equal(springSection.Crn, spring.Courses.Single().SelectedCrn);
        var link = loaded.Plans.Single(item => ScheduleLink.Owns(plan.Id, item));
        Assert.Equal("202730", link.TermCode);
        Assert.Contains(springSection.Crn, link.SelectedCRNs);
        Assert.False(link.IsPrimary);

        var removed = _write.remove_course(id, "Spring 2027", both.Subject, both.Number, Rev(revision));
        revision = (int)removed["revision"]!;
        var after = new UserDataStore(_path).DegreePlan(plan.Id)!;
        Assert.Empty(after.Terms.SelectMany(term => term.Courses));

        var refused = Assert.Throws<ToolFailure>(() => _write.delete_plan(id, Rev(revision), confirm: false));
        Assert.Equal("confirm_required", refused.Code);
        Assert.False(string.IsNullOrWhiteSpace(refused.Message));
        Assert.NotNull(new UserDataStore(_path).DegreePlan(plan.Id));
        var deleted = _write.delete_plan(id, Rev(revision), confirm: true);
        Assert.True((bool)deleted["deleted"]!);
        Assert.Equal(id, deleted["plan_id"]);
        Assert.False(deleted.ContainsKey("id"));
        Assert.Null(new UserDataStore(_path).DegreePlan(plan.Id));
    }

    [Fact]
    public void RevisionConflictLeavesTheFileAlone()
    {
        var created = _write.create_plan("One", null);
        var id = Guid.Parse((string)created["id"]!);
        var stale = (int)created["revision"]!;
        var other = new UserDataStore(_path);
        other.UpdateDegreePlan(id, plan => plan.Name = "From the app");
        var current = PlanService.RevisionOf(new UserDataStore(_path).DegreePlan(id)!);

        var error = Assert.Throws<ToolFailure>(() => _write.rename_plan(id.ToString("D"), "Lost", Rev(stale)));
        Assert.Equal("revision_conflict", error.Code);
        Assert.Contains(current.ToString(), error.Message);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        var reloaded = new UserDataStore(_path).DegreePlan(id)!;
        Assert.Equal("From the app", reloaded.Name);
        Assert.Equal(current, PlanService.RevisionOf(reloaded));
    }

    [Fact]
    public void SecondWriteKeepsABackupTheLoaderCanRead()
    {
        var created = _write.create_plan("First", null);
        _write.rename_plan((string)created["id"]!, "Second", Rev((int)created["revision"]!));
        Assert.True(File.Exists(_path + ".bak"));
        var backup = new UserDataStore(_path + ".bak");
        Assert.Equal("First", backup.DegreePlans.Single().Name);
        Assert.Equal("Second", new UserDataStore(_path).DegreePlans.Single().Name);
    }

    [Fact]
    public void ApplyReorganizeSavesTheMove()
    {
        var sample = SharedCatalog.SpringOnly;
        var created = _write.create_plan("Reorder", ["Fall 2026", "Spring 2027"]);
        var id = (string)created["id"]!;
        var added = _write.add_course(id, "Fall 2026", sample.Subject, sample.Number, Rev((int)created["revision"]!));
        var applied = _write.apply_reorganize(id, Rev((int)added["revision"]!));
        var moves = (List<Dictionary<string, object?>>)applied["moves"]!;
        Assert.Contains(moves, move => (string)move["from_term"]! == "202710" && (string)move["to_term"]! == "202730");
        var loaded = new UserDataStore(_path).DegreePlan(Guid.Parse(id))!;
        Assert.Contains(loaded.Terms.Single(term => term.Season == PlanSeason.Spring).Courses, course => course.Key == sample.Key);
    }

    [Fact]
    public void EmptyApplyReorganizeDoesNotRewriteOrBumpRevision()
    {
        var created = _write.create_plan("Algorithms", null);
        var id = (string)created["id"]!;
        Assert.Equal(1, (int)created["revision"]!);
        var added = _write.add_course(id, "202730", "CS", "3000", Rev(1));
        Assert.Equal(2, (int)added["revision"]!);
        var validation = (Dictionary<string, object?>)added["validation"]!;
        Assert.True(validation.ContainsKey("not_offered"));
        var before = File.ReadAllBytes(_path);
        var applied = _write.apply_reorganize(id, Rev(2));
        Assert.Equal(2, (int)applied["revision"]!);
        Assert.Empty((List<Dictionary<string, object?>>)applied["moves"]!);
        Assert.Equal(before, File.ReadAllBytes(_path));
        var loaded = new UserDataStore(_path).DegreePlan(Guid.Parse(id))!;
        Assert.Equal(2, PlanService.RevisionOf(loaded));
        Assert.Contains(loaded.Terms.Single().Courses, course => course.Key == new CourseKey("CS", "3000"));
    }

    [Fact]
    public void RepeatedCoursesStayOnThePlanAndSetSectionChangesOnlyOnePin()
    {
        var index = SharedCatalog.Library.ByCode["202710"];
        var course = index.Courses.First(item => index.Sections(item.Key).Count >= 3);
        var sections = index.Sections(course.Key);
        var created = _write.create_plan("Topics", ["Fall 2026"]);
        var id = (string)created["id"]!;
        var revision = (int)created["revision"]!;
        var message = $"{course.Key} appears more than once. Each copy still counts.";

        var first = _write.add_course(id, "Fall 2026", course.Subject, course.Number, Rev(revision), sections[0].Crn);
        revision = (int)first["revision"]!;
        var second = _write.add_course(id, "Fall 2026", course.Subject, course.Number, Rev(revision), sections[1].Crn);
        revision = (int)second["revision"]!;
        Assert.DoesNotContain(Duplicates(second), item => (string?)item["message"] == message);

        var unchanged = _write.set_section(id, "Fall 2026", course.Subject, course.Number, sections[0].Crn, Rev(revision));
        Assert.Equal(revision, (int)unchanged["revision"]!);
        var alsoUnchanged = _write.set_section(id, "Fall 2026", course.Subject, course.Number, sections[1].Crn, Rev(revision));
        Assert.Equal(revision, (int)alsoUnchanged["revision"]!);
        var ambiguous = Assert.Throws<ToolFailure>(() => _write.set_section(id, "Fall 2026", course.Subject, course.Number, sections[2].Crn, Rev(revision)));
        Assert.Equal("invalid_argument", ambiguous.Code);

        var same = _write.add_course(id, "Fall 2026", course.Subject, course.Number, Rev(revision), sections[0].Crn);
        revision = (int)same["revision"]!;
        Assert.Contains(Duplicates(same), item => (string?)item["subject"] == course.Subject && (string?)item["number"] == course.Number && (string?)item["message"] == message);
        var open = _write.add_course(id, "Fall 2026", course.Subject, course.Number, Rev(revision));
        Assert.Contains(Duplicates(open), item => (string?)item["message"] == message);

        var loaded = new UserDataStore(_path).DegreePlan(Guid.Parse(id))!;
        var pins = loaded.Terms.Single().Courses.Select(item => item.SelectedCrn).ToList();
        Assert.Equal(4, pins.Count);
        Assert.Equal(2, pins.Count(crn => crn == sections[0].Crn));
        Assert.Equal(1, pins.Count(crn => crn == sections[1].Crn));
        Assert.Contains(pins, crn => crn is null);
        var link = new UserDataStore(_path).Plans.Single(item => ScheduleLink.Owns(loaded.Id, item));
        Assert.Equal(4, link.Courses.Count);
        Assert.Equal(
            [sections[0].Crn, sections[0].Crn, sections[1].Crn],
            link.Courses.Select(item => item.PinnedCrn).Where(crn => crn is not null).OrderBy(crn => crn, StringComparer.Ordinal));
        Assert.Equal(
            [sections[0].Crn, sections[1].Crn],
            link.SelectedCRNs.OrderBy(crn => crn, StringComparer.Ordinal));
    }

    [Fact]
    public void SetSectionReplacesTheOnlyPin()
    {
        var both = SharedCatalog.OfferedBoth;
        var sections = SharedCatalog.Library.ByCode["202710"].Sections(both.Key);
        var created = _write.create_plan("One section", ["Fall 2026"]);
        var id = (string)created["id"]!;
        var added = _write.add_course(id, "Fall 2026", both.Subject, both.Number, Rev(1));
        var chosen = sections[0].Crn;
        var selected = _write.set_section(id, "Fall 2026", both.Subject, both.Number, chosen, Rev((int)added["revision"]!));
        Assert.True((int)selected["revision"]! > (int)added["revision"]!);
        var course = new UserDataStore(_path).DegreePlan(Guid.Parse(id))!.Terms.Single().Courses.Single();
        Assert.Equal(chosen, course.SelectedCrn);
    }

    [Fact]
    public void ReorganizeKeepsAPinnedCrn()
    {
        var sample = SharedCatalog.SpringOnly;
        var created = _write.create_plan("Pinned move", ["Fall 2026", "Spring 2027"]);
        var id = Guid.Parse((string)created["id"]!);
        var store = new UserDataStore(_path);
        store.UpdateDegreePlan(id, plan =>
        {
            plan.Terms.Single(term => term.Season == PlanSeason.Fall).Courses.Add(
                new DegreePlanCourse(sample.Key, sample.Title, sample.MinCredits) { SelectedCrn = "11111" });
        });
        var revision = PlanService.RevisionOf(new UserDataStore(_path).DegreePlan(id)!);
        var applied = _write.apply_reorganize(id.ToString("D"), Rev(revision));
        Assert.Contains((List<Dictionary<string, object?>>)applied["moves"]!, move => (string)move["to_term"]! == "202730");
        var moved = new UserDataStore(_path).DegreePlan(id)!.Terms.Single(term => term.Season == PlanSeason.Spring).Courses.Single();
        Assert.Equal(sample.Key, moved.Key);
        Assert.Equal("11111", moved.SelectedCrn);
    }

    [Fact]
    public void UnknownCourseAndCrnAreRejected()
    {
        var created = _write.create_plan("Check", ["Fall 2026"]);
        var id = (string)created["id"]!;
        var revision = (int)created["revision"]!;
        var missing = Assert.Throws<ToolFailure>(() => _write.add_course(id, "Fall 2026", "ZZZZ", "9999", Rev(revision)));
        Assert.Equal("not_found", missing.Code);
        Assert.False(string.IsNullOrWhiteSpace(missing.Message));
        var unknownTerm = Assert.Throws<ToolFailure>(() => _write.remove_course(id, "209930", "CS", "3000", Rev(revision)));
        Assert.Equal("not_found", unknownTerm.Code);
        var both = SharedCatalog.OfferedBoth;
        Assert.Throws<ToolFailure>(() =>
            _write.add_course(id, "Fall 2026", both.Subject, both.Number, Rev(revision), "0000000"));
        Assert.Equal(revision, (int)_read.get_plan(id)["revision"]!);
    }

    [Fact]
    public void GetPlanOmitsStudentFields()
    {
        var created = _write.create_plan("Private", null);
        var id = Guid.Parse((string)created["id"]!);
        var store = new UserDataStore(_path);
        store.UpdateDegreePlan(id, plan =>
        {
            plan.StudentName = "Secret Student";
            plan.StudentID = "001234567";
        });
        var json = JsonSerializer.Serialize(_read.get_plan(id.ToString("D")));
        Assert.DoesNotContain("Secret Student", json);
        Assert.DoesNotContain("001234567", json);
    }

    [Fact]
    public void MissingNameAndBadRevisionHaveMessages()
    {
        var missing = Assert.Throws<ToolFailure>(() => _write.create_plan(null, null));
        Assert.Equal("invalid_argument", missing.Code);
        Assert.False(string.IsNullOrWhiteSpace(missing.Message));
        var created = _write.create_plan("Named", ["202710", "202730"]);
        Assert.Equal(2, ((List<Dictionary<string, object?>>)created["terms"]!).Count);
        var hex = Assert.Throws<ToolFailure>(() => _write.rename_plan((string)created["id"]!, "X", JsonSerializer.SerializeToElement("0123abcd")));
        Assert.Equal("invalid_argument", hex.Code);
        Assert.Contains("integer", hex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnreadableFileIsStorageErrorAndIsNotReplaced()
    {
        File.WriteAllText(_path, "{");
        var before = File.ReadAllBytes(_path);
        var error = Assert.Throws<ToolFailure>(() => _read.list_plans());
        Assert.Equal("storage_error", error.Code);
        Assert.Contains(_path, error.Message);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Equal(before, File.ReadAllBytes(_path));

        File.WriteAllText(_path, "{\"version\":99,\"degreePlans\":[]}");
        var newer = Assert.Throws<ToolFailure>(() => _plans.Create("Nope", null));
        Assert.Equal("storage_error", newer.Code);
        Assert.Contains("99", newer.Message);
        Assert.Contains("\"version\":99", File.ReadAllText(_path));
    }

    [Fact]
    public void SetCreditsKeepsEachRowAndRejectsAFixedOrOutOfRangeValue()
    {
        var spring = SharedCatalog.Library.ByCode["202730"];
        var variable = spring.Courses.First(course => course.IsVariableCredit && spring.Sections(course.Key).Count > 0);
        var fixedCourse = spring.Courses.First(course => !course.IsVariableCredit && course.MinCredits > 0 && course.Key != variable.Key);
        var section = spring.Sections(variable.Key)[0];
        var created = _write.create_plan("Credits", ["Spring 2027"]);
        var id = (string)created["id"]!;
        var revision = (int)created["revision"]!;

        var added = _write.add_course(id, "Spring 2027", variable.Subject, variable.Number, Rev(revision));
        revision = (int)added["revision"]!;
        var first = Courses(added).Single();
        Assert.Equal(variable.StartingCredits, Convert.ToDouble(first["credits"]));
        Assert.Equal(variable.MinCredits, Convert.ToDouble(first["credit_low"]));
        Assert.Equal(variable.MaxCredits, Convert.ToDouble(first["credit_high"]));
        Assert.True(first.ContainsKey("cps_only"));

        var range = Assert.Throws<ToolFailure>(() => _write.set_credits(id, "Spring 2027", variable.Subject, variable.Number, Rev(revision), credits: variable.MaxCredits + 1));
        Assert.Equal("invalid_argument", range.Code);
        var fraction = Assert.Throws<ToolFailure>(() => _write.set_credits(id, "Spring 2027", variable.Subject, variable.Number, Rev(revision), credits: variable.MinCredits + 0.5));
        Assert.Equal("invalid_argument", fraction.Code);

        var lowChoice = variable.MinCredits > 0 ? variable.MinCredits : variable.StartingCredits;
        var highChoice = variable.MaxCredits > lowChoice ? variable.MaxCredits : lowChoice;
        var lowered = _write.set_credits(id, "Spring 2027", variable.Subject, variable.Number, Rev(revision), credits: highChoice);
        revision = (int)lowered["revision"]!;
        Assert.Equal(highChoice, Convert.ToDouble(Courses(lowered).Single()["credits"]));

        var second = _write.add_course(id, "Spring 2027", variable.Subject, variable.Number, Rev(revision), section.Crn, lowChoice);
        revision = (int)second["revision"]!;
        var ambiguous = Assert.Throws<ToolFailure>(() => _write.set_credits(id, "Spring 2027", variable.Subject, variable.Number, Rev(revision), credits: lowChoice));
        Assert.Equal("invalid_argument", ambiguous.Code);
        Assert.Contains("crn", ambiguous.Message, StringComparison.OrdinalIgnoreCase);

        var named = _write.set_credits(id, "Spring 2027", variable.Subject, variable.Number, Rev(revision), section.Crn, lowChoice);
        var rows = Courses(named);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => (string?)row.GetValueOrDefault("crn") == section.Crn && Convert.ToDouble(row["credits"]) == lowChoice);
        Assert.Contains(rows, row => !row.ContainsKey("crn") && Convert.ToDouble(row["credits"]) == highChoice);

        var stored = new UserDataStore(_path);
        var link = stored.Plans.Single(item => item.TermCode == "202730");
        Assert.Equal(2, link.Courses.Count);
        Assert.Contains(link.Courses, row => row.Credits == highChoice);
        Assert.Contains(link.Courses, row => row.PinnedCrn == section.Crn && row.Credits == lowChoice);

        var addedFixed = _write.add_course(id, "Spring 2027", fixedCourse.Subject, fixedCourse.Number, Rev((int)named["revision"]!));
        var fixedSet = Assert.Throws<ToolFailure>(() => _write.set_credits(id, "Spring 2027", fixedCourse.Subject, fixedCourse.Number, Rev((int)addedFixed["revision"]!), credits: fixedCourse.MinCredits));
        Assert.Equal("invalid_argument", fixedSet.Code);
        var wrongFixed = Assert.Throws<ToolFailure>(() => _write.add_course(id, "Spring 2027", fixedCourse.Subject, fixedCourse.Number, Rev((int)addedFixed["revision"]!), credits: fixedCourse.MinCredits + 1));
        Assert.Equal("invalid_argument", wrongFixed.Code);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static JsonElement Rev(int revision) => JsonSerializer.SerializeToElement(revision);

    private static List<Dictionary<string, object?>> Courses(Dictionary<string, object?> plan)
    {
        var terms = (List<Dictionary<string, object?>>)plan["terms"]!;
        return terms.SelectMany(term => (List<Dictionary<string, object?>>)term["courses"]!).ToList();
    }

    private static List<Dictionary<string, object?>> Duplicates(Dictionary<string, object?> plan)
    {
        var validation = (Dictionary<string, object?>)plan["validation"]!;
        return (List<Dictionary<string, object?>>)validation["duplicates"]!;
    }
}
