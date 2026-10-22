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
    private readonly PlanService _plans;

    public CatalogTools(CatalogLibrary library, PlanService plans)
    {
        _library = library;
        _plans = plans;
    }

    [McpServerTool(Name = "list_terms", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists the bundled term codes and names. This server does not contact Banner.")]
    public Dictionary<string, object?> list_terms() => new()
    {
        ["terms"] = _library.Catalogs
            .OrderBy(catalog => catalog.Term.Code, StringComparer.Ordinal)
            .Select(catalog => new Dictionary<string, object?>
            {
                ["term"] = catalog.Term.Code,
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
        [Description("When true, only courses whose loaded details list no prerequisites. Courses whose prerequisites were not loaded are left out.")] bool? no_prerequisites = null,
        [Description("When true, only courses whose loaded details list no corequisites. Courses whose corequisites were not loaded are left out.")] bool? no_corequisites = null,
        [Description("Maximum courses to return. Defaults to 25.")] int? limit = null,
        [Description("all, undergrad, or grad. Undergrad is a course number below 5000. Grad is 5000 and up. Combines with the other filters.")] string? level = null)
    {
        var index = RequireIndex(term);
        var cap = limit is null ? 25 : Math.Clamp(limit.Value, 1, 100);
        var filters = new SearchFilters
        {
            NoPrerequisites = no_prerequisites == true,
            NoCorequisites = no_corequisites == true,
            Division = ParseLevel(level),
        };
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
            ["courses"] = list.Take(cap).Select(course => CourseRow(course, index)).ToList(),
            ["truncated"] = list.Count > cap,
        };
    }

    [McpServerTool(Name = "get_course", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Course details and every section. An instructor email is included only when its domain is exactly northeastern.edu or neu.edu.")]
    public Dictionary<string, object?> get_course(
        [Description("Banner term code.")] string term,
        [Description("Subject code.")] string subject,
        [Description("Course number.")] string number)
    {
        var index = RequireIndex(term);
        var key = PlanReorganizer.RequireKey(subject, number);
        var course = index.Course(key) ?? throw new ToolFailure("not_found", $"Unknown course {key} in {index.Term.Code}.");
        return new Dictionary<string, object?>
        {
            ["term"] = index.Term.Code,
            ["subject"] = course.Subject,
            ["number"] = course.Number,
            ["title"] = TextFormat.Sanitize(course.Title),
            ["description"] = TextFormat.Sanitize(course.Description),
            ["credits"] = Credits(course),
            ["credit_low"] = course.MinCredits,
            ["credit_high"] = course.IsVariableCredit ? course.MaxCredits : null,
            ["cps_only"] = CpsNotes.Flag(index.Sections(key)),
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
            ["term"] = new Dictionary<string, object?>
            {
                ["term"] = index.Term.Code,
                ["name"] = TextFormat.Sanitize(index.Term.Name),
            },
            ["clashes"] = ScheduleConflicts.Find(sections).Select(clash => new Dictionary<string, object?>
            {
                ["first"] = clash.First.Crn,
                ["second"] = clash.Second.Crn,
                ["reason"] = TextFormat.Sanitize(clash.Summary.Replace('\u2013', '-').Replace('\u2014', '-')),
            }).ToList(),
            ["unknown_crns"] = unknown,
        };
    }

    [McpServerTool(Name = "suggest_schedule", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Conflict-free section combinations for the requested courses, best first.")]
    public Dictionary<string, object?> suggest_schedule(
        [Description("Banner term code.")] string term,
        [Description("Courses, each with subject and number. Optional crn locks that section. The same subject and number may be repeated only when every listing has a different crn.")] List<CourseRef> courses,
        [Description("Optional no_classes_before, no_classes_after, and days_off.")] ScheduleConstraints? constraints = null)
    {
        var index = RequireIndex(term);
        if (courses is null || courses.Count == 0)
            throw new ToolFailure("invalid_argument", "courses is required.");
        var inputs = new List<ScheduleCourseInput>();
        var listed = new Dictionary<CourseKey, List<string?>>();
        foreach (var course in courses)
        {
            var key = PlanReorganizer.RequireKey(course.Subject, course.Number);
            if (index.Course(key) is null)
                throw new ToolFailure("not_found", $"Unknown course {key} in {index.Term.Code}.");
            var sections = index.Sections(key);
            if (sections.Count == 0)
                throw new ToolFailure("not_found", $"{key} has no sections in {index.Term.Code}.");
            var pin = DegreeCourseRepeats.CleanCrn(course.Crn);
            if (!listed.TryGetValue(key, out var prior)) listed[key] = prior = [];
            if (prior.Count > 0 && (pin is null || prior.Any(existing => existing is null || string.Equals(existing, pin, StringComparison.Ordinal))))
                throw new ToolFailure("invalid_argument", pin is null || prior.Any(existing => existing is null)
                    ? $"{key} is listed more than once. Each listing needs its own CRN."
                    : $"{key} CRN {pin} is listed more than once.");
            if (pin is not null && sections.All(section => !string.Equals(section.Crn, pin, StringComparison.Ordinal)))
                throw new ToolFailure("not_found", $"CRN {pin} is not a section of {key} in {index.Term.Code}.");
            prior.Add(pin);
            var details = index.Course(key)!;
            var exact = VariableCredits.Exact(details, null);
            inputs.Add(new ScheduleCourseInput(key, true, sections, minCredits: exact ?? details.MinCredits, maxCredits: exact ?? details.MaxCredits)
            {
                PinnedCrn = pin,
            });
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
    [Description("Moves courses into terms the plan already has when that term offers them and prerequisite order is kept. Does not save and does not add a semester. Pass plan_id or plan, not both.")]
    public Dictionary<string, object?> reorganize_plan(
        [Description("Saved plan UUID.")] string? plan_id = null,
        [Description("Unsaved plan: {terms:[{term, courses:[{subject, number}]}]}.")] PlanInput? plan = null) =>
        _plans.Reorganize(plan_id, plan);

    private CatalogIndex RequireIndex(string? term)
    {
        if (!TextFormat.TryParseTerm(term, out _, out _, out var code))
            throw new ToolFailure("invalid_argument", $"Unknown term '{term}'.");
        return _library.IndexForCode(code)
            ?? throw new ToolFailure("not_found", $"No bundled catalog for {code}. This server reads only the bundled terms.");
    }

    private static NUPath? AttributePath(string? attribute)
    {
        if (string.IsNullOrWhiteSpace(attribute)) return null;
        var text = attribute.Trim();
        var fromCode = NUPathInfo.FromAttributeCodes([text.ToUpperInvariant()]);
        if (fromCode.Count == 1) return fromCode[0];
        return NUPathInfo.FromShort(text);
    }

    private static CourseDivision ParseLevel(string? level)
    {
        if (string.IsNullOrWhiteSpace(level) || level.Equals("all", StringComparison.OrdinalIgnoreCase))
            return CourseDivision.All;
        if (level.Equals("undergrad", StringComparison.OrdinalIgnoreCase))
            return CourseDivision.Undergrad;
        if (level.Equals("grad", StringComparison.OrdinalIgnoreCase))
            return CourseDivision.Grad;
        throw new ToolFailure("invalid_argument", "level must be all, undergrad, or grad.");
    }

    private static Dictionary<string, object?> CourseRow(Course course, CatalogIndex index) => new()
    {
        ["subject"] = course.Subject,
        ["number"] = course.Number,
        ["title"] = TextFormat.Sanitize(course.Title),
        ["credits"] = Credits(course),
        ["credit_low"] = course.MinCredits,
        ["credit_high"] = course.IsVariableCredit ? course.MaxCredits : null,
        ["cps_only"] = CpsNotes.Flag(index.Sections(course.Key)),
        ["attributes"] = course.Attributes,
    };

    private static object Credits(Course course) =>
        course.MinCredits == course.MaxCredits
            ? course.MinCredits
            : $"{CreditFormatter.Number(course.MinCredits)} to {CreditFormatter.Number(course.MaxCredits)}";
}
