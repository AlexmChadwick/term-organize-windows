// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using ModelContextProtocol.Server;
using TermOrganize.Core;

namespace TermOrganize.Mcp;

[McpServerToolType]
public sealed class CatalogTools
{
    private readonly CatalogLibrary _library;

    public CatalogTools(CatalogLibrary library) => _library = library;

    [McpServerTool(Name = "list_terms", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists the bundled term codes and names. This server does not contact Banner.")]
    public Dictionary<string, object?> list_terms() => new()
    {
        ["terms"] = _library.Catalogs
            .OrderBy(catalog => catalog.Term.Code, StringComparer.Ordinal)
            .Select(catalog => new Dictionary<string, object?>
            {
                ["code"] = catalog.Term.Code,
                ["name"] = TextFormat.Sanitize(catalog.Term.Name),
            })
            .ToList(),
    };

    [McpServerTool(Name = "search_courses", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Searches one bundled term. Returns title, credits, and attributes. Instructor emails are omitted.")]
    public Dictionary<string, object?> search_courses(
        [Description("Banner term code, such as 202710 or 202730.")] string term,
        [Description("Optional text query.")] string? query = null,
        [Description("Optional subject code, such as CS.")] string? subject = null,
        [Description("Optional course number, such as 3000.")] string? number = null,
        [Description("Optional attribute code or NUpath short name.")] string? attribute = null,
        [Description("Maximum courses to return. Defaults to 25.")] int? limit = null)
    {
        var index = RequireIndex(term);
        var cap = limit is null ? 25 : Math.Clamp(limit.Value, 1, 100);
        var filters = new SearchFilters();
        if (!string.IsNullOrWhiteSpace(subject))
            filters.Subjects.Add(subject.Trim().ToUpperInvariant());
        var path = AttributePath(attribute);
        if (path is not null) filters.Nupaths.Add(path.Value);
        var text = query?.Trim() ?? "";
        if (text.Length == 0 && !string.IsNullOrWhiteSpace(number))
            text = string.IsNullOrWhiteSpace(subject) ? number.Trim() : $"{subject.Trim()} {number.Trim()}";
        var response = SearchEngine.Search(text, index, filters);
        IEnumerable<Course> courses = response.Results.Select(result => result.Course);
        if (!string.IsNullOrWhiteSpace(number))
        {
            var wanted = number.Trim().ToUpperInvariant();
            courses = courses.Where(course => course.Number.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(attribute) && path is null)
        {
            var code = attribute.Trim();
            courses = courses.Where(course => course.Attributes.Any(item => item.Equals(code, StringComparison.OrdinalIgnoreCase)));
        }
        var list = courses.ToList();
        return new Dictionary<string, object?>
        {
            ["term"] = index.Term.Code,
            ["courses"] = list.Take(cap).Select(CourseRow).ToList(),
            ["truncated"] = list.Count > cap,
        };
    }

    [McpServerTool(Name = "get_course", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Course details and every section. Instructor emails are never returned.")]
    public Dictionary<string, object?> get_course(
        [Description("Banner term code.")] string term,
        [Description("Subject code.")] string subject,
        [Description("Course number.")] string number)
    {
        var index = RequireIndex(term);
        var key = PlanReorganizer.RequireKey(subject, number);
        var course = index.Course(key) ?? throw new ModelContextProtocol.McpException($"Unknown course {key} in {index.Term.Code}.");
        return new Dictionary<string, object?>
        {
            ["term"] = index.Term.Code,
            ["subject"] = course.Subject,
            ["number"] = course.Number,
            ["title"] = TextFormat.Sanitize(course.Title),
            ["description"] = TextFormat.Sanitize(course.Description),
            ["credits"] = Credits(course),
            ["attributes"] = course.Attributes,
            ["sections"] = index.Sections(key).Select(SectionJson.Full).ToList(),
        };
    }

    [McpServerTool(Name = "check_schedule", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reports time clashes among CRNs and lists CRNs that are not in the bundled term.")]
    public Dictionary<string, object?> check_schedule(
        [Description("Banner term code.")] string term,
        [Description("Section CRNs to compare.")] string[] crns)
    {
        var index = RequireIndex(term);
        var unknown = new List<string>();
        var sections = new List<CourseSection>();
        foreach (var raw in crns ?? [])
        {
            var crn = raw.Trim();
            if (crn.Length == 0) continue;
            if (index.Section(crn) is CourseSection section) sections.Add(section);
            else unknown.Add(crn);
        }
        return new Dictionary<string, object?>
        {
            ["term"] = index.Term.Code,
            ["clashes"] = ScheduleConflicts.Find(sections).Select(clash => new Dictionary<string, object?>
            {
                ["crn_a"] = clash.First.Crn,
                ["crn_b"] = clash.Second.Crn,
                ["summary"] = TextFormat.Sanitize(clash.Summary.Replace('\u2013', '-').Replace('\u2014', '-')),
            }).ToList(),
            ["unknown_crns"] = unknown,
        };
    }

    [McpServerTool(Name = "suggest_schedule", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Conflict-free section combinations for the requested courses, best first.")]
    public Dictionary<string, object?> suggest_schedule(
        [Description("Banner term code.")] string term,
        [Description("Courses, each with subject and number.")] List<CourseRef> courses,
        [Description("Optional no_classes_before, no_classes_after, and days_off.")] ScheduleConstraints? constraints = null)
    {
        var index = RequireIndex(term);
        if (courses is null || courses.Count == 0)
            throw new ModelContextProtocol.McpException("courses is required.");
        var inputs = new List<ScheduleCourseInput>();
        foreach (var course in courses)
        {
            var key = PlanReorganizer.RequireKey(course.Subject, course.Number);
            if (index.Course(key) is null)
                throw new ModelContextProtocol.McpException($"Unknown course {key} in {index.Term.Code}.");
            var sections = index.Sections(key);
            if (sections.Count == 0)
                throw new ModelContextProtocol.McpException($"{key} has no sections in {index.Term.Code}.");
            var details = index.Course(key)!;
            inputs.Add(new ScheduleCourseInput(key, true, sections, minCredits: details.MinCredits, maxCredits: details.MaxCredits));
        }
        var filters = new ScheduleFilters
        {
            EarliestStart = TextFormat.ParseClock(constraints?.NoClassesBefore),
            LatestEnd = TextFormat.ParseClock(constraints?.NoClassesAfter),
            FreeDays = TextFormat.ParseDaysOff(constraints?.DaysOff),
        };
        var generated = ScheduleGenerator.Generate(
            inputs,
            filters,
            sort: ScheduleSortOption.Best,
            limits: new ScheduleLimits
            {
                MaxResults = 8,
                MaxExhaustiveLeaves = 50_000,
                MaxNodes = 500_000,
                TimeBudget = 1.5,
                SampleLeaves = 0,
                SampleTimeBudget = 0,
                AllowSampling = false,
            });
        return new Dictionary<string, object?>
        {
            ["term"] = index.Term.Code,
            ["schedules"] = generated.Schedules.Take(8).Select(schedule => new Dictionary<string, object?>
            {
                ["score"] = schedule.Score,
                ["sections"] = schedule.Sections.Select(SectionJson.Picked).ToList(),
            }).ToList(),
            ["truncated"] = generated.Schedules.Count > 8 || !generated.IsExhaustive,
        };
    }

    [McpServerTool(Name = "reorganize_plan", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Moves courses across terms to avoid clashes and unoffered semesters. Does not save and does not add a semester.")]
    public Dictionary<string, object?> reorganize_plan(
        [Description("Plan with terms, each holding subject and number.")] PlanInput plan)
    {
        if (plan is null || plan.Terms.Count == 0)
            throw new ModelContextProtocol.McpException("plan.terms is required.");
        var draft = PlanReorganizer.FromInput(plan, _library);
        var outcome = PlanReorganizer.Reorganize(draft, _library);
        return new Dictionary<string, object?>
        {
            ["terms"] = outcome.Plan.Terms.OrderBy(term => term.SortKey).Select(term => new Dictionary<string, object?>
            {
                ["term"] = TextFormat.BannerCode(term.Season, term.Year),
                ["name"] = term.Name,
                ["courses"] = term.Courses.Select(course => new Dictionary<string, object?>
                {
                    ["subject"] = course.Key?.Subject,
                    ["number"] = course.Key?.Number,
                    ["title"] = TextFormat.Sanitize(course.Title),
                }).ToList(),
            }).ToList(),
            ["moves"] = outcome.Moves.Select(move => new Dictionary<string, object?>
            {
                ["subject"] = move.Subject,
                ["number"] = move.Number,
                ["from_term"] = move.FromTerm,
                ["to_term"] = move.ToTerm,
                ["reason"] = move.Reason,
            }).ToList(),
            ["unresolved"] = outcome.Unresolved,
            ["note"] = ReorganizeOutcome.Note,
        };
    }

    private CatalogIndex RequireIndex(string? term)
    {
        if (!TextFormat.TryParseTerm(term, out _, out _, out var code))
            throw new ModelContextProtocol.McpException($"Unknown term '{term}'.");
        return _library.IndexForCode(code)
            ?? throw new ModelContextProtocol.McpException($"No bundled catalog for {code}. This server reads only the bundled terms.");
    }

    private static NUPath? AttributePath(string? attribute)
    {
        if (string.IsNullOrWhiteSpace(attribute)) return null;
        var text = attribute.Trim();
        var fromCode = NUPathInfo.FromAttributeCodes([text.ToUpperInvariant()]);
        if (fromCode.Count == 1) return fromCode[0];
        return NUPathInfo.FromShort(text);
    }

    private static Dictionary<string, object?> CourseRow(Course course) => new()
    {
        ["subject"] = course.Subject,
        ["number"] = course.Number,
        ["title"] = TextFormat.Sanitize(course.Title),
        ["credits"] = Credits(course),
        ["attributes"] = course.Attributes,
    };

    private static object Credits(Course course) =>
        course.MinCredits == course.MaxCredits
            ? course.MinCredits
            : $"{CreditFormatter.Number(course.MinCredits)} to {CreditFormatter.Number(course.MaxCredits)}";
}
