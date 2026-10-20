// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using TermOrganize.Core;

namespace TermOrganize.Mcp;

public sealed class ReorganizeMove
{
    public required string Subject { get; init; }
    public required string Number { get; init; }
    public required string FromTerm { get; init; }
    public required string ToTerm { get; init; }
    public required string Reason { get; init; }
}

public sealed class ReorganizeOutcome
{
    public required DegreePlan Plan { get; init; }
    public required List<ReorganizeMove> Moves { get; init; }
    public required List<Dictionary<string, object?>> Unresolved { get; init; }
    public const string Note =
        "Whole-plan reorganize is implemented on top of DegreePlanReordering. " +
        "The core library only suggests moves for one semester. " +
        "This tool walks the plan and applies suggestions. It does not add a semester, so graduation is not delayed.";
}

/// <summary>
/// Walks every semester and applies one-semester suggestions from
/// <see cref="DegreePlanReordering"/> when they do not add a semester.
/// </summary>
public static class PlanReorganizer
{
    public const int MaxPasses = 12;

    public static ReorganizeOutcome Reorganize(DegreePlan source, CatalogLibrary library)
    {
        var working = Clone(source);
        working.SortTerms();
        var moves = new List<ReorganizeMove>();
        var indexes = library.ByCode.Values.ToList();
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            working.SortTerms();
            DegreePlanReordering.Suggestion? chosen = null;
            foreach (var term in working.Terms.ToList())
            {
                var blocking = Blocking(working, term, library);
                if (blocking.Count == 0) continue;
                var suggestions = DegreePlanReordering.Suggestions(
                    working,
                    term.Id,
                    blocking,
                    History(library, Keys(working)),
                    key => RequisiteOf(indexes, key),
                    working.DeclaredEquivalences);
                chosen = suggestions.FirstOrDefault(item => item.AddsSemester is null && item.Moves.Count > 0);
                if (chosen is not null) break;
            }
            if (chosen is null) break;
            var names = working.Terms.ToDictionary(term => term.Id, term => term.Name);
            foreach (var move in chosen.Moves)
            {
                moves.Add(new ReorganizeMove
                {
                    Subject = move.Course.Subject,
                    Number = move.Course.Number,
                    FromTerm = names.TryGetValue(move.From, out var from) ? from : move.From.ToString("D"),
                    ToTerm = names.TryGetValue(move.To, out var to) ? to : move.To.ToString("D"),
                    Reason = string.Join("; ", chosen.Reasons.Select(TextFormat.Sanitize)),
                });
            }
            DegreePlanReordering.Apply(chosen, working);
        }

        var unresolved = new List<Dictionary<string, object?>>();
        working.SortTerms();
        foreach (var term in working.Terms)
        {
            foreach (var key in Blocking(working, term, library))
            {
                unresolved.Add(new Dictionary<string, object?>
                {
                    ["subject"] = key.Subject,
                    ["number"] = key.Number,
                    ["term"] = term.Name,
                    ["reason"] = "Still blocked. The only remaining fix would add a semester, which this tool will not do.",
                });
            }
        }
        return new ReorganizeOutcome { Plan = working, Moves = moves, Unresolved = unresolved };
    }

    public static DegreePlan FromInput(PlanInput input, CatalogLibrary library)
    {
        var plan = new DegreePlan { Name = "Reorganize" };
        foreach (var termInput in input.Terms)
        {
            var label = termInput.Term ?? termInput.Code ?? "";
            if (!TextFormat.TryParseTerm(label, out var season, out var year, out _))
                throw new ModelContextProtocol.McpException($"Unknown term '{label}'.");
            var term = plan.Terms.FirstOrDefault(item => item.Season == season && item.Year == year);
            if (term is null)
            {
                term = new DegreePlanTerm { Season = season, Year = year };
                plan.Terms.Add(term);
            }
            foreach (var course in termInput.Courses)
            {
                var key = RequireKey(course.Subject, course.Number);
                if (library.FindCourse(key) is null)
                    throw new ModelContextProtocol.McpException($"Unknown course {key}.");
                var details = library.FindCourse(key)!;
                term.Courses.Add(new DegreePlanCourse(key, TextFormat.Sanitize(details.Title), details.MinCredits));
            }
        }
        plan.SortTerms();
        return plan;
    }

    public static List<CourseKey> Blocking(DegreePlan plan, DegreePlanTerm term, CatalogLibrary library)
    {
        var keys = term.Courses.Select(course => course.Key).OfType<CourseKey>().ToList();
        if (keys.Count == 0) return [];
        var index = library.IndexFor(term.Season, term.Year);
        var history = History(library, keys);
        var missing = new List<CourseKey>();
        foreach (var key in keys)
        {
            if (index is not null && !library.OfferedIn(index, key))
            {
                missing.Add(key);
                continue;
            }
            if (history.IsLikelyOffered(key, term.Season) == false)
                missing.Add(key);
        }
        if (missing.Count > 0) return missing;
        if (index is null) return [];

        var inputs = keys.Select(key => new ScheduleCourseInput(
            key,
            isRequired: true,
            index.Sections(key),
            minCredits: index.Course(key)?.MinCredits,
            maxCredits: index.Course(key)?.MaxCredits)).ToList();
        var generated = ScheduleGenerator.Generate(
            inputs,
            new ScheduleFilters(),
            limits: new ScheduleLimits
            {
                MaxResults = 1,
                MaxExhaustiveLeaves = 1_000,
                MaxNodes = 250_000,
                TimeBudget = 0.2,
                SampleLeaves = 0,
                SampleTimeBudget = 0,
                AllowSampling = false,
            });
        if (generated.IsExhaustive && generated.TotalFound == 0) return keys;
        return [];
    }

    public static CourseKey RequireKey(string? subject, string? number)
    {
        var key = new CourseKey((subject ?? "").Trim().ToUpperInvariant(), (number ?? "").Trim().ToUpperInvariant());
        if (key.Subject.Length == 0 || key.Number.Length == 0 || key.Subject.Any(ch => !char.IsLetter(ch)))
            throw new ModelContextProtocol.McpException("subject and number are required.");
        return key;
    }

    private static OfferingHistory History(CatalogLibrary library, IReadOnlyCollection<CourseKey> keys) =>
        OfferingHistoryBuilder.FromCatalogs(library.Catalogs, keys);

    private static List<CourseKey> Keys(DegreePlan plan) =>
        plan.Terms.SelectMany(term => term.Courses).Select(course => course.Key).OfType<CourseKey>().Distinct().ToList();

    private static RequisiteAnswer RequisiteOf(IReadOnlyList<CatalogIndex> indexes, CourseKey key)
    {
        foreach (var index in indexes)
        {
            var course = index.Course(key);
            if (course is not { DetailsLoaded: true }) continue;
            return course.Prereqs is Requisite requisite ? RequisiteAnswer.Known(requisite) : RequisiteAnswer.None;
        }
        return RequisiteAnswer.Unknown;
    }

    private static DegreePlan Clone(DegreePlan plan)
    {
        var json = JsonSerializer.Serialize(plan, AppJson.UserData);
        return JsonSerializer.Deserialize<DegreePlan>(json, AppJson.UserData)
            ?? throw new ModelContextProtocol.McpException("Could not copy the plan.");
    }
}
