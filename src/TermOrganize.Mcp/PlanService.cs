// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Security.Cryptography;
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
                    throw new ModelContextProtocol.McpException($"Unknown term '{label}'.");
                if (plan.Terms.Any(term => term.Season == season && term.Year == year)) continue;
                plan.Terms.Add(new DegreePlanTerm { Season = season, Year = year });
            }
        }
        var store = Open();
        store.AddDegreePlan(plan);
        return Project(plan);
    }

    public Dictionary<string, object?> Rename(string planId, string? name, string? expectedRevision)
    {
        var title = RequireName(name);
        return Mutate(planId, expectedRevision, plan => plan.Name = title);
    }

    public Dictionary<string, object?> AddCourse(string planId, string? termLabel, string? subject, string? number, string? crn, string? expectedRevision)
    {
        var key = PlanReorganizer.RequireKey(subject, number);
        return Mutate(planId, expectedRevision, plan =>
        {
            var term = EnsureTerm(plan, termLabel);
            EnsureKnown(key, term, crn);
            if (FindCourse(plan, key) is { } existing)
            {
                var where = plan.Terms.First(item => item.Courses.Contains(existing)).Name;
                if (where == term.Name)
                    throw new ModelContextProtocol.McpException($"{key} is already in {where}.");
                throw new ModelContextProtocol.McpException($"{key} is already in {where}. Use move_course.");
            }
            var details = _library.FindCourse(key)!;
            term.Courses.Add(new DegreePlanCourse(key, TextFormat.Sanitize(details.Title), details.MinCredits)
            {
                SelectedCrn = string.IsNullOrWhiteSpace(crn) ? null : crn.Trim(),
            });
        });
    }

    public Dictionary<string, object?> RemoveCourse(string planId, string? termLabel, string? subject, string? number, string? expectedRevision)
    {
        var key = PlanReorganizer.RequireKey(subject, number);
        return Mutate(planId, expectedRevision, plan =>
        {
            var term = RequireTerm(plan, termLabel);
            var course = term.Courses.FirstOrDefault(item => item.Key == key)
                ?? throw new ModelContextProtocol.McpException($"{key} is not in {term.Name}.");
            term.Courses.Remove(course);
        });
    }

    public Dictionary<string, object?> MoveCourse(string planId, string? subject, string? number, string? fromLabel, string? toLabel, string? expectedRevision)
    {
        var key = PlanReorganizer.RequireKey(subject, number);
        return Mutate(planId, expectedRevision, plan =>
        {
            var from = RequireTerm(plan, fromLabel);
            var to = EnsureTerm(plan, toLabel);
            if (from.Id == to.Id)
                throw new ModelContextProtocol.McpException("from_term and to_term are the same.");
            var course = from.Courses.FirstOrDefault(item => item.Key == key)
                ?? throw new ModelContextProtocol.McpException($"{key} is not in {from.Name}.");
            if (to.Courses.Any(item => item.Key == key))
                throw new ModelContextProtocol.McpException($"{key} is already in {to.Name}.");
            if (!string.IsNullOrWhiteSpace(course.SelectedCrn))
            {
                var destination = _library.IndexFor(to.Season, to.Year);
                if (destination?.Section(course.SelectedCrn)?.CourseKey != key)
                    course.SelectedCrn = null;
            }
            from.Courses.Remove(course);
            to.Courses.Add(course);
        });
    }

    public Dictionary<string, object?> SetSection(string planId, string? termLabel, string? subject, string? number, string? crn, string? expectedRevision)
    {
        var key = PlanReorganizer.RequireKey(subject, number);
        if (string.IsNullOrWhiteSpace(crn))
            throw new ModelContextProtocol.McpException("crn is required.");
        return Mutate(planId, expectedRevision, plan =>
        {
            var term = RequireTerm(plan, termLabel);
            var course = term.Courses.FirstOrDefault(item => item.Key == key)
                ?? throw new ModelContextProtocol.McpException($"{key} is not in {term.Name}.");
            EnsureCrn(key, term, crn);
            course.SelectedCrn = crn.Trim();
        });
    }

    public Dictionary<string, object?> ApplyReorganize(string planId, string? expectedRevision)
    {
        ReorganizeOutcome? outcome = null;
        var projected = Mutate(planId, expectedRevision, plan =>
        {
            outcome = PlanReorganizer.Reorganize(plan, _library);
            plan.Terms = outcome.Plan.Terms;
        });
        projected["moves"] = outcome!.Moves.Select(MoveJson).ToList();
        projected["unresolved"] = outcome.Unresolved;
        projected["note"] = ReorganizeOutcome.Note;
        return projected;
    }

    public Dictionary<string, object?> Delete(string planId, string? expectedRevision, bool confirm)
    {
        if (!confirm)
            throw new ModelContextProtocol.McpException("delete_plan requires confirm: true.");
        var id = ParseId(planId);
        var store = Open();
        var plan = store.DegreePlan(id) ?? throw new ModelContextProtocol.McpException($"Unknown plan {planId}.");
        CheckRevision(plan, expectedRevision);
        store.Plans.RemoveAll(item => ScheduleLink.Owns(id, item));
        store.DeleteDegreePlan(id);
        return new Dictionary<string, object?> { ["deleted"] = true, ["id"] = id.ToString("D") };
    }

    public static string RevisionOf(DegreePlan plan)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(plan, AppJson.UserData);
        return Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
    }

    private Dictionary<string, object?> Mutate(string planId, string? expectedRevision, Action<DegreePlan> change)
    {
        var id = ParseId(planId);
        var store = Open();
        var plan = store.DegreePlan(id) ?? throw new ModelContextProtocol.McpException($"Unknown plan {planId}.");
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
            schedule.Courses = courses.Select((course, index) => new PlanCourse
            {
                Key = course.Key!.Value,
                IsRequired = true,
                ColorIndex = index % CourseColorPalette.Count,
            }).ToList();
            schedule.SelectedCRNs = courses
                .Select(course => course.SelectedCrn)
                .Where(crn => !string.IsNullOrWhiteSpace(crn))
                .Select(crn => crn!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        store.Plans.RemoveAll(item => ScheduleLink.Owns(plan.Id, item) && !keep.Contains(item.Id));
    }

    private void EnsureKnown(CourseKey key, DegreePlanTerm term, string? crn)
    {
        if (_library.FindCourse(key) is null)
            throw new ModelContextProtocol.McpException($"Unknown course {key}.");
        if (!string.IsNullOrWhiteSpace(crn))
            EnsureCrn(key, term, crn);
    }

    private void EnsureCrn(CourseKey key, DegreePlanTerm term, string crn)
    {
        var index = _library.IndexFor(term.Season, term.Year)
            ?? throw new ModelContextProtocol.McpException($"No bundled catalog for {term.Name}, so CRN {crn.Trim()} cannot be checked.");
        var section = index.Section(crn.Trim());
        if (section is null || section.CourseKey != key)
            throw new ModelContextProtocol.McpException($"CRN {crn.Trim()} is not a section of {key} in {term.Name}.");
    }

    private UserDataStore Open() => new(_path);

    private static DegreePlan Require(UserDataStore store, string planId) =>
        store.DegreePlan(ParseId(planId)) ?? throw new ModelContextProtocol.McpException($"Unknown plan {planId}.");

    private static void CheckRevision(DegreePlan plan, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
            throw new ModelContextProtocol.McpException("expected_revision is required.");
        var current = RevisionOf(plan);
        if (!current.Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ModelContextProtocol.McpException($"Plan changed. Current revision is {current}.");
    }

    private static Guid ParseId(string? planId)
    {
        if (!Guid.TryParse(planId, out var id))
            throw new ModelContextProtocol.McpException("plan_id must be a UUID.");
        return id;
    }

    private static string RequireName(string? name)
    {
        var title = name?.Trim() ?? "";
        if (title.Length == 0)
            throw new ModelContextProtocol.McpException("name is required.");
        return title;
    }

    private static DegreePlanTerm RequireTerm(DegreePlan plan, string? label)
    {
        if (!TextFormat.TryParseTerm(label, out var season, out var year, out _))
            throw new ModelContextProtocol.McpException($"Unknown term '{label}'.");
        return plan.Terms.FirstOrDefault(term => term.Season == season && term.Year == year)
            ?? throw new ModelContextProtocol.McpException($"The plan has no {season.ExportName()} {year} term.");
    }

    private static DegreePlanTerm EnsureTerm(DegreePlan plan, string? label)
    {
        if (!TextFormat.TryParseTerm(label, out var season, out var year, out _))
            throw new ModelContextProtocol.McpException($"Unknown term '{label}'.");
        var term = plan.Terms.FirstOrDefault(item => item.Season == season && item.Year == year);
        if (term is not null) return term;
        term = new DegreePlanTerm { Season = season, Year = year };
        plan.Terms.Add(term);
        return term;
    }

    private static DegreePlanCourse? FindCourse(DegreePlan plan, CourseKey key) =>
        plan.Terms.SelectMany(term => term.Courses).FirstOrDefault(course => course.Key == key);

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
            ["subject"] = course.Key?.Subject ?? "",
            ["number"] = course.Key?.Number ?? "",
            ["title"] = TextFormat.Sanitize(course.Title),
            ["credits"] = course.Credits,
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
                    ["crn_a"] = clash.First.Crn,
                    ["crn_b"] = clash.Second.Crn,
                    ["summary"] = TextFormat.Sanitize(clash.Summary.Replace('\u2013', '-').Replace('\u2014', '-')),
                });
            }
        }
        return new Dictionary<string, object?>
        {
            ["clashes"] = clashes,
            ["not_offered"] = missing,
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
