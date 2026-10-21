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
        Assert.Contains(moves, move => (string)move["to_term"]! == "Spring 2027");
        var loaded = new UserDataStore(_path).DegreePlan(Guid.Parse(id))!;
        Assert.Contains(loaded.Terms.Single(term => term.Season == PlanSeason.Spring).Courses, course => course.Key == sample.Key);
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

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static JsonElement Rev(int revision) => JsonSerializer.SerializeToElement(revision);
}
