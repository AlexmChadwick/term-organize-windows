// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using TermOrganize.Core;

namespace TermOrganize.Mcp;

public sealed class PlanService
{
    private readonly string _path;
    private readonly CatalogLibrary _library;

    public PlanService(string path, CatalogLibrary library)
    {
        _path = path;
        _library = library;
    }

    public string Path => _path;

    public Dictionary<string, object?> List()
    {
        var store = Open();
        return new Dictionary<string, object?>
        {
            ["plans"] = store.DegreePlans
                .OrderByDescending(plan => plan.UpdatedAt)
                .Select(Summary)
                .ToList(),
        };
    }

    public Dictionary<string, object?> Get(string planId) => Project(Require(Open(), planId));

    public Dictionary<string, object?> Create(string? name, IReadOnlyList<string>? terms)
    {
        var title = RequireName(name);
        var store = Open();
        var plan = new DegreePlan
        {
            Name = title,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        if (terms is not null)
        {
            foreach (var label in terms)
            {
                if (!TextFormat.TryParseTerm(label, out var season, out var year, out _))
                    throw new ToolFailure("invalid_argument", $"Unknown term '{label}'.");
                if (plan.Terms.Any(term => term.Season == season && term.Year == year)) continue;
                plan.Terms.Add(new DegreePlanTerm { Season = season, Year = year });
            }
        }
        store.AddDegreePlan(plan);
        return Project(plan);
    }

    public Dictionary<string, object?> Rename(string planId, string? name, int? expectedRevision)
    {
        var title = RequireName(name);
        return Mutate(planId, expectedRevision, plan => plan.Name = title);
    }

    public Dictionary<string, object?> AddCourse(string planId, string? termLabel, string? subject, string? number, string? crn, int? expectedRevision, double? credits = null)
    {
        var key = PlanReorganizer.RequireKey(subject, number);
        return Mutate(planId, expectedRevision, plan =>
        {
            var term = EnsureTerm(plan, termLabel);
            EnsureKnown(key, term, crn);
            var incoming = DegreeCourseRepeats.CleanCrn(crn);
            var details = _library.FindCourse(key)!;
            if (!VariableCredits.TryResolve(details, credits, out var chosen, out var error))
                throw new ToolFailure("invalid_argument", error ?? "credits is not valid for this course.");
            term.Courses.Add(new DegreePlanCourse(key, TextFormat.Sanitize(details.Title), chosen)
            {
                SelectedCrn = incoming,
            });
        });
    }

    public Dictionary<string, object?> SetCredits(string planId, string? termLabel, string? subject, string? number, string? crn, double? credits, int? expectedRevision)
    {
        var key = PlanReorganizer.RequireKey(subject, number);
        if (credits is not double requested)
            throw new ToolFailure("invalid_argument", "credits is required.");
        return Mutate(planId, expectedRevision, plan =>
        {
            var term = RequireTerm(plan, termLabel);
            var matches = term.Courses.Where(item => item.Key == key).ToList();
            if (matches.Count == 0)
                throw new ToolFailure("not_found", $"{key} is not in {term.Name}.");
            var wanted = DegreeCourseRepeats.CleanCrn(crn);
            if (matches.Count > 1 && wanted is null)
                throw new ToolFailure("invalid_argument", $"{key} is in {term.Name} more than once. Pass crn to name the row.");
            var target = wanted is null
                ? matches[0]
                : matches.FirstOrDefault(item => string.Equals(DegreeCourseRepeats.CleanCrn(item.SelectedCrn), wanted, StringComparison.Ordinal))
                    ?? throw new ToolFailure("not_found", $"CRN {wanted} is not a chosen section of {key} in {term.Name}.");
            var details = _library.FindCourse(key) ?? throw new ToolFailure("not_found", $"Unknown course {key}.");
            if (!VariableCredits.TrySet(details, requested, out var chosen, out var error))
                throw new ToolFailure("invalid_argument", error ?? "credits is not valid for this course.");
            target.Credits = chosen;
        });
    }

    public Dictionary<string, object?> RemoveCourse(string planId, string? termLabel, string? subject, string? number, int? expectedRevision)
    {
        var key = PlanReorganizer.RequireKey(subject, number);
        return Mutate(planId, expectedRevision, plan =>
        {
            var term = RequireTerm(plan, termLabel);
            var course = term.Courses.FirstOrDefault(item => item.Key == key)
                ?? throw new ToolFailure("not_found", $"{key} is not in {term.Name}.");
            term.Courses.Remove(course);
        });
    }

    public Dictionary<string, object?> MoveCourse(string planId, string? subject, string? number, string? fromLabel, string? toLabel, int? expectedRevision)
    {
        var key = PlanReorganizer.RequireKey(subject, number);
        return Mutate(planId, expectedRevision, plan =>
        {
            var from = RequireTerm(plan, fromLabel);
            var to = EnsureTerm(plan, toLabel);
            if (from.Id == to.Id)
                throw new ToolFailure("invalid_argument", "from_term and to_term are the same.");
            var course = from.Courses.FirstOrDefault(item => item.Key == key)
                ?? throw new ToolFailure("not_found", $"{key} is not in {from.Name}.");
            from.Courses.Remove(course);
            to.Courses.Add(course);
        });
    }

    public Dictionary<string, object?> SetSection(string planId, string? termLabel, string? subject, string? number, string? crn, int? expectedRevision)
    {
        var key = PlanReorganizer.RequireKey(subject, number);
        if (string.IsNullOrWhiteSpace(crn))
            throw new ToolFailure("invalid_argument", "crn is required.");
        var store = Open();
        var current = Require(store, planId);
        CheckRevision(current, expectedRevision);
        var term = RequireTerm(current, termLabel);
        var matches = term.Courses.Where(item => item.Key == key).ToList();
        if (matches.Count == 0)
            throw new ToolFailure("not_found", $"{key} is not in {term.Name}.");
        var incoming = crn.Trim();
        EnsureCrn(key, term, incoming);
        if (matches.Any(item => string.Equals(DegreeCourseRepeats.CleanCrn(item.SelectedCrn), incoming, StringComparison.Ordinal)))
            return Project(current);
        DegreePlanCourse target;
        if (matches.Count == 1)
            target = matches[0];
        else if (matches.Where(item => DegreeCourseRepeats.CleanCrn(item.SelectedCrn) is null).ToList() is { Count: 1 } open)
            target = open[0];
        else
            throw new ToolFailure("invalid_argument", $"{key} has more than one chosen section in {term.Name}. set_section can fill the listing that has no CRN, or replace the CRN when it is the only listing.");
        return Mutate(planId, expectedRevision, plan =>
        {
            var storedTerm = RequireTerm(plan, termLabel);
            var stored = storedTerm.Courses.FirstOrDefault(item => item.Id == target.Id)
                ?? throw new ToolFailure("not_found", $"{key} is not in {storedTerm.Name}.");
            stored.SelectedCrn = incoming;
        });
    }

    public Dictionary<string, object?> ApplyReorganize(string planId, int? expectedRevision)
    {
        var store = Open();
        var plan = Require(store, planId);
        CheckRevision(plan, expectedRevision);
        var outcome = PlanReorganizer.Reorganize(plan, _library);
        if (outcome.Moves.Count == 0)
        {
            var unchanged = Project(plan);
            unchanged["moves"] = new List<Dictionary<string, object?>>();
            unchanged["unresolved"] = outcome.Unresolved;
            return unchanged;
        }
        var projected = Mutate(planId, expectedRevision, stored =>
        {
            stored.Terms = outcome.Plan.Terms;
        });
        projected["moves"] = outcome.Moves.Select(MoveJson).ToList();
        projected["unresolved"] = outcome.Unresolved;
        return projected;
    }

    public Dictionary<string, object?> Reorganize(string? planId, PlanInput? plan)
    {
        var hasId = !string.IsNullOrWhiteSpace(planId);
        var hasPlan = plan is { Terms.Count: > 0 };
        if (hasId && plan is not null)
            throw new ToolFailure("invalid_argument", "Pass plan_id or plan, not both.");
        if (!hasId && !hasPlan)
            throw new ToolFailure("invalid_argument", "plan_id or plan.terms is required.");
        DegreePlan source = hasId
            ? Require(Open(), planId!)
            : PlanReorganizer.FromInput(plan!, _library);
        var outcome = PlanReorganizer.Reorganize(source, _library);
        var body = new Dictionary<string, object?>
        {
            ["moves"] = outcome.Moves.Select(MoveJson).ToList(),
            ["unresolved"] = outcome.Unresolved,
            ["plan"] = hasId ? Project(outcome.Plan) : Draft(outcome.Plan),
        };
        return body;
    }

    public Dictionary<string, object?> Delete(string planId, int? expectedRevision, bool confirm)
    {
        if (!confirm)
            throw new ToolFailure("confirm_required", "delete_plan requires confirm: true.");
        var id = ParseId(planId);
        var store = Open();
        var plan = store.DegreePlan(id) ?? throw new ToolFailure("not_found", $"Unknown plan {planId}.");
        CheckRevision(plan, expectedRevision);
        store.Plans.RemoveAll(item => ScheduleLink.Owns(id, item));
        store.DeleteDegreePlan(id);
        return new Dictionary<string, object?> { ["deleted"] = true, ["plan_id"] = id.ToString("D") };
    }

    public static int RevisionOf(DegreePlan plan) => Math.Max(plan.Revision, 1);

    private Dictionary<string, object?> Mutate(string planId, int? expectedRevision, Action<DegreePlan> change)
    {
        var id = ParseId(planId);
        var store = Open();
        var plan = store.DegreePlan(id) ?? throw new ToolFailure("not_found", $"Unknown plan {planId}.");
        CheckRevision(plan, expectedRevision);
        change(plan);
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        plan.SortTerms();
        SyncSchedules(store, plan);
        store.FlushNow();
        return Project(plan);
    }

    private void SyncSchedules(UserDataStore store, DegreePlan plan)
    {
        var keep = new HashSet<Guid>();
        foreach (var term in plan.Terms)
        {
            var courses = term.Courses.Where(course => course.Key is not null).ToList();
            if (courses.Count == 0) continue;
            var code = TextFormat.BannerCode(term.Season, term.Year);
            var id = ScheduleLink.IdFor(plan.Id, code);
            keep.Add(id);
            var schedule = store.Plans.FirstOrDefault(item => item.Id == id);
            if (schedule is null)
            {
                schedule = new SchedulePlan
                {
                    Id = id,
                    TermCode = code,
                    CreatedAt = plan.UpdatedAt,
                    IsPrimary = false,
                };
                store.Plans.Add(schedule);
            }
            schedule.Name = plan.Name;
            schedule.TermCode = code;
            schedule.IsPrimary = false;
            schedule.UpdatedAt = plan.UpdatedAt;
            schedule.Courses = [];
            schedule.SelectedCRNs = [];
            DegreePlanSchedule.SyncRows(schedule, courses);
        }
        store.Plans.RemoveAll(item => ScheduleLink.Owns(plan.Id, item) && !keep.Contains(item.Id));
    }

    private void EnsureKnown(CourseKey key, DegreePlanTerm term, string? crn)
    {
        if (_library.FindCourse(key) is null)
            throw new ToolFailure("not_found", $"Unknown course {key}.");
        if (!string.IsNullOrWhiteSpace(crn))
            EnsureCrn(key, term, crn);
    }

    private void EnsureCrn(CourseKey key, DegreePlanTerm term, string crn)
    {
        var index = _library.IndexFor(term.Season, term.Year)
            ?? throw new ToolFailure("not_found", $"No bundled catalog for {term.Name}, so CRN {crn.Trim()} cannot be checked.");
        var section = index.Section(crn.Trim());
        if (section is null || section.CourseKey != key)
            throw new ToolFailure("not_found", $"CRN {crn.Trim()} is not a section of {key} in {term.Name}.");
    }

    private UserDataStore Open()
    {
        EnsureReadable();
        return new(_path);
    }

    private void EnsureReadable()
    {
        if (!File.Exists(_path)) return;
        byte[] bytes;
        try { bytes = File.ReadAllBytes(_path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ToolFailure("storage_error", $"Could not read {_path}: {ex.Message}");
        }
        JsonDocument doc;
        try { doc = JsonDocument.Parse(bytes); }
        catch (JsonException ex)
        {
            throw new ToolFailure("storage_error", $"Could not read {_path}: malformed JSON ({ex.Message}).");
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ToolFailure("storage_error", $"Could not read {_path}: malformed JSON (the file is not an object).");
            if (root.TryGetProperty("version", out var version))
            {
                if (!version.TryGetInt32(out var number))
                    throw new ToolFailure("storage_error", $"Could not read {_path}: malformed JSON (version is not an integer).");
                if (number > UserData.SupportedVersion)
                    throw new ToolFailure("storage_error", $"Could not read {_path}: unknown newer version {number}.");
            }
            foreach (var name in new[] { "degreePlans", "plans", "saved", "recents", "watchlist", "searchHistory" })
            {
                if (root.TryGetProperty(name, out var list) && list.ValueKind != JsonValueKind.Array)
                    throw new ToolFailure("storage_error", $"Could not read {_path}: malformed JSON ({name} is not a list).");
            }
            if (!root.TryGetProperty("degreePlans", out var plans)) return;
            foreach (var item in plans.EnumerateArray())
            {
                try
                {
                    if (item.Deserialize<DegreePlan>(AppJson.UserData) is null)
                        throw new JsonException("a degree plan was empty");
                }
                catch (JsonException ex)
                {
                    throw new ToolFailure("storage_error", $"Could not read {_path}: malformed JSON ({ex.Message}).");
                }
            }
        }
    }

    private static DegreePlan Require(UserDataStore store, string planId) =>
        store.DegreePlan(ParseId(planId)) ?? throw new ToolFailure("not_found", $"Unknown plan {planId}.");

    private static void CheckRevision(DegreePlan plan, int? expected)
    {
        if (expected is null)
            throw new ToolFailure("invalid_argument", "expected_revision is required.");
        var current = RevisionOf(plan);
        if (expected.Value != current)
            throw new ToolFailure("revision_conflict", $"Plan changed. Current revision is {current}.");
    }

    private static Guid ParseId(string? planId)
    {
        if (!Guid.TryParse(planId, out var id))
            throw new ToolFailure("invalid_argument", "plan_id must be a UUID.");
        return id;
    }

    private static string RequireName(string? name)
    {
        var title = name?.Trim() ?? "";
        if (title.Length == 0)
            throw new ToolFailure("invalid_argument", "name is required.");
        return title;
    }

    private static DegreePlanTerm RequireTerm(DegreePlan plan, string? label)
    {
        if (!TextFormat.TryParseTerm(label, out var season, out var year, out _))
            throw new ToolFailure("invalid_argument", $"Unknown term '{label}'.");
        return plan.Terms.FirstOrDefault(term => term.Season == season && term.Year == year)
            ?? throw new ToolFailure("not_found", $"The plan has no {season.ExportName()} {year} term.");
    }

    private static DegreePlanTerm EnsureTerm(DegreePlan plan, string? label)
    {
        if (!TextFormat.TryParseTerm(label, out var season, out var year, out _))
            throw new ToolFailure("invalid_argument", $"Unknown term '{label}'.");
        var term = plan.Terms.FirstOrDefault(item => item.Season == season && item.Year == year);
        if (term is not null) return term;
        term = new DegreePlanTerm { Season = season, Year = year };
        plan.Terms.Add(term);
        return term;
    }

    private Dictionary<string, object?> Summary(DegreePlan plan) => new()
    {
        ["id"] = plan.Id.ToString("D"),
        ["name"] = plan.Name,
        ["terms"] = plan.Terms.OrderBy(term => term.SortKey).Select(term => new Dictionary<string, object?>
        {
            ["term"] = TextFormat.BannerCode(term.Season, term.Year),
            ["name"] = term.Name,
            ["course_count"] = term.Courses.Count,
        }).ToList(),
        ["updated_at"] = plan.UpdatedAt.ToString("O"),
        ["revision"] = RevisionOf(plan),
    };

    private Dictionary<string, object?> Project(DegreePlan plan)
    {
        var body = Summary(plan);
        body["terms"] = plan.Terms.OrderBy(term => term.SortKey).Select(TermJson).ToList();
        body["validation"] = Validate(plan);
        return body;
    }

    private Dictionary<string, object?> Draft(DegreePlan plan) => new()
    {
        ["name"] = plan.Name,
        ["terms"] = plan.Terms.OrderBy(term => term.SortKey).Select(TermJson).ToList(),
        ["validation"] = Validate(plan),
    };

    private Dictionary<string, object?> TermJson(DegreePlanTerm term)
    {
        var index = _library.IndexFor(term.Season, term.Year);
        return new Dictionary<string, object?>
        {
            ["term"] = TextFormat.BannerCode(term.Season, term.Year),
            ["name"] = term.Name,
            ["courses"] = term.Courses.Select(course => CourseJson(course, index)).ToList(),
        };
    }

    private static Dictionary<string, object?> CourseJson(DegreePlanCourse course, CatalogIndex? index)
    {
        var body = new Dictionary<string, object?>
        {
            ["id"] = course.Id.ToString("D"),
            ["subject"] = course.Key?.Subject ?? "",
            ["number"] = course.Key?.Number ?? "",
            ["title"] = TextFormat.Sanitize(course.Title),
            ["credits"] = course.Credits,
            ["credit_low"] = course.Key is CourseKey lowKey ? index?.Course(lowKey)?.MinCredits : null,
            ["credit_high"] = course.Key is CourseKey highKey && index?.Course(highKey) is { IsVariableCredit: true } variable ? variable.MaxCredits : null,
            ["cps_only"] = course.Key is CourseKey flagKey && index is not null ? CpsNotes.Flag(index.Sections(flagKey)) : null,
        };
        if (string.IsNullOrWhiteSpace(course.SelectedCrn)) return body;
        body["crn"] = course.SelectedCrn;
        if (index?.Section(course.SelectedCrn) is CourseSection section)
            body["section"] = SectionJson.Brief(section);
        return body;
    }

    private Dictionary<string, object?> Validate(DegreePlan plan)
    {
        var clashes = new List<Dictionary<string, object?>>();
        var missing = new List<Dictionary<string, object?>>();
        foreach (var term in plan.Terms)
        {
            var index = _library.IndexFor(term.Season, term.Year);
            var chosen = new List<CourseSection>();
            foreach (var course in term.Courses)
            {
                if (course.Key is not CourseKey key) continue;
                var offered = index is not null
                    ? _library.OfferedIn(index, key)
                    : OfferingHistoryBuilder.FromCatalogs(_library.Catalogs, [key]).IsLikelyOffered(key, term.Season) != false;
                if (!offered)
                {
                    missing.Add(new Dictionary<string, object?>
                    {
                        ["subject"] = key.Subject,
                        ["number"] = key.Number,
                        ["term"] = term.Name,
                    });
                }
                if (index is not null && !string.IsNullOrWhiteSpace(course.SelectedCrn) && index.Section(course.SelectedCrn) is CourseSection section)
                    chosen.Add(section);
            }
            foreach (var clash in ScheduleConflicts.Find(chosen))
            {
                clashes.Add(new Dictionary<string, object?>
                {
                    ["first"] = clash.First.Crn,
                    ["second"] = clash.Second.Crn,
                    ["reason"] = TextFormat.Sanitize(clash.Summary.Replace('\u2013', '-').Replace('\u2014', '-')),
                });
            }
        }
        return new Dictionary<string, object?>
        {
            ["clashes"] = clashes,
            ["not_offered"] = missing,
            ["duplicates"] = DegreePlanAnalysis.DuplicateKeys(plan)
                .OrderBy(key => key.ToString(), StringComparer.Ordinal)
                .Select(key => new Dictionary<string, object?>
                {
                    ["subject"] = key.Subject,
                    ["number"] = key.Number,
                    ["message"] = DegreePlanAnalysis.RepeatMessage(key),
                })
                .ToList(),
        };
    }

    private static Dictionary<string, object?> MoveJson(ReorganizeMove move) => new()
    {
        ["subject"] = move.Subject,
        ["number"] = move.Number,
        ["from_term"] = move.FromTerm,
        ["to_term"] = move.ToTerm,
        ["reason"] = move.Reason,
    };
}

public static class ScheduleLink
{
    public static Guid IdFor(Guid planId, string termCode) =>
        DegreePlanReordering.StableId($"mcp-schedule-{planId:D}-{termCode}");

    public static bool Owns(Guid planId, SchedulePlan schedule) =>
        schedule.Id == IdFor(planId, schedule.TermCode);
}
