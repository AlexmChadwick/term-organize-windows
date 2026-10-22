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
}

/// <summary>
/// Moves a course only into a term the plan already has. The destination catalog must
/// offer it, prerequisite order among courses on the plan is kept, and no semester is added.
/// </summary>
public static class PlanReorganizer
{
    public static ReorganizeOutcome Reorganize(DegreePlan source, CatalogLibrary library)
    {
        var working = Clone(source);
        working.SortTerms();
        var moves = new List<ReorganizeMove>();
        var guard = Math.Max(1, working.Terms.Sum(term => term.Courses.Count) + 1);
        for (var pass = 0; pass < guard; pass++)
        {
            working.SortTerms();
            if (!TryMoveOne(working, library, moves)) break;
        }

        var unresolved = new List<Dictionary<string, object?>>();
        working.SortTerms();
        foreach (var term in working.Terms)
        {
            foreach (var course in term.Courses)
            {
                if (course.Key is not CourseKey key || PlacementOk(working, term, key, library)) continue;
                unresolved.Add(new Dictionary<string, object?>
                {
                    ["subject"] = key.Subject,
                    ["number"] = key.Number,
                    ["reason"] = UnresolvedReason(working, term, key, library),
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
                throw new ToolFailure("invalid_argument", $"Unknown term '{label}'.");
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
                    throw new ToolFailure("not_found", $"Unknown course {key}.");
                var details = library.FindCourse(key)!;
                var pin = DegreeCourseRepeats.CleanCrn(course.Crn);
                if (pin is not null)
                {
                    var index = library.IndexFor(season, year);
                    var section = index?.Section(pin);
                    if (section is null || section.CourseKey != key)
                        throw new ToolFailure("not_found", $"CRN {pin} is not a section of {key}.");
                }
                term.Courses.Add(new DegreePlanCourse(key, TextFormat.Sanitize(details.Title), details.MinCredits)
                {
                    SelectedCrn = pin,
                });
            }
        }
        plan.SortTerms();
        return plan;
    }

    public static CourseKey RequireKey(string? subject, string? number)
    {
        var key = new CourseKey((subject ?? "").Trim().ToUpperInvariant(), (number ?? "").Trim().ToUpperInvariant());
        if (key.Subject.Length == 0 || key.Number.Length == 0 || key.Subject.Any(ch => !char.IsLetter(ch)))
            throw new ToolFailure("invalid_argument", "subject and number are required.");
        return key;
    }

    private static bool TryMoveOne(DegreePlan plan, CatalogLibrary library, List<ReorganizeMove> moves)
    {
        foreach (var term in plan.Terms.OrderBy(item => item.SortKey).ToList())
        {
            foreach (var course in term.Courses.ToList())
            {
                if (course.Key is not CourseKey key || PlacementOk(plan, term, key, library)) continue;
                var target = Destination(plan, term, key, library);
                if (target is null) continue;
                term.Courses.Remove(course);
                target.Courses.Add(course);
                var why = Offered(library, term, key)
                    ? $"Prerequisite order on the plan is not kept in {term.Name}."
                    : $"{key} is not offered in {term.Name}.";
                moves.Add(new ReorganizeMove
                {
                    Subject = key.Subject,
                    Number = key.Number,
                    FromTerm = TextFormat.BannerCode(term.Season, term.Year),
                    ToTerm = TextFormat.BannerCode(target.Season, target.Year),
                    Reason = $"{why} {target.Name} offers it, and the move keeps prerequisite order without adding a semester.",
                });
                return true;
            }
        }
        return false;
    }

    private static DegreePlanTerm? Destination(DegreePlan plan, DegreePlanTerm current, CourseKey key, CatalogLibrary library)
    {
        DegreePlanTerm? best = null;
        foreach (var term in plan.Terms.OrderBy(item => item.SortKey))
        {
            if (term.Id == current.Id) continue;
            if (!Offered(library, term, key)) continue;
            if (!OrderKept(plan, term, key, library)) continue;
            best ??= term;
        }
        return best;
    }

    private static bool PlacementOk(DegreePlan plan, DegreePlanTerm term, CourseKey key, CatalogLibrary library) =>
        Offered(library, term, key) && OrderKept(plan, term, key, library);

    private static bool Offered(CatalogLibrary library, DegreePlanTerm term, CourseKey key)
    {
        var index = library.IndexFor(term.Season, term.Year);
        return index is not null && library.OfferedIn(index, key);
    }

    /// <summary>
    /// Prerequisites that are not on the plan are treated as outside this plan.
    /// A prerequisite that is on the plan must sit in an earlier term, and a later
    /// course on the plan that requires this one must stay later.
    /// </summary>
    private static bool OrderKept(DegreePlan plan, DegreePlanTerm destination, CourseKey key, CatalogLibrary library)
    {
        var tree = RequisiteOf(library, key);
        if (tree is not null && !tree.IsSatisfied(AssumedComplete(plan, destination, key, tree), assumeTestsMet: true))
            return false;
        foreach (var otherTerm in plan.Terms)
        {
            if (otherTerm.SortKey > destination.SortKey) continue;
            foreach (var other in otherTerm.Courses)
            {
                if (other.Key is not CourseKey otherKey || otherKey == key) continue;
                var required = RequisiteOf(library, otherKey);
                if (required is null || !required.ReferencedCourses().Contains(key)) continue;
                var without = AssumedComplete(plan, otherTerm, otherKey, required);
                without.Remove(key);
                if (!required.IsSatisfied(without, assumeTestsMet: true))
                    return false;
            }
        }
        return true;
    }

    private static HashSet<CourseKey> AssumedComplete(DegreePlan plan, DegreePlanTerm destination, CourseKey moving, Requisite tree)
    {
        var onPlan = new HashSet<CourseKey>();
        foreach (var term in plan.Terms)
            foreach (var course in term.Courses)
                if (course.Key is CourseKey key) onPlan.Add(key);
        var earlier = new HashSet<CourseKey>();
        foreach (var term in plan.Terms)
        {
            if (term.SortKey >= destination.SortKey) continue;
            foreach (var course in term.Courses)
                if (course.Key is CourseKey key && key != moving) earlier.Add(key);
        }
        foreach (var referenced in tree.ReferencedCourses())
            if (!onPlan.Contains(referenced)) earlier.Add(referenced);
        return earlier;
    }

    private static string UnresolvedReason(DegreePlan plan, DegreePlanTerm term, CourseKey key, CatalogLibrary library)
    {
        var elsewhere = plan.Terms.Any(candidate => candidate.Id != term.Id && Offered(library, candidate, key));
        if (!elsewhere)
            return $"{key} is not offered in {term.Name}, and no other term already on the plan offers it.";
        return $"{key} cannot move to another term already on the plan without breaking prerequisite order.";
    }

    private static Requisite? RequisiteOf(CatalogLibrary library, CourseKey key)
    {
        foreach (var index in library.ByCode.Values)
        {
            var course = index.Course(key);
            if (course is not { DetailsLoaded: true }) continue;
            return course.Prereqs;
        }
        return null;
    }

    private static DegreePlan Clone(DegreePlan plan)
    {
        var json = JsonSerializer.Serialize(plan, AppJson.UserData);
        return JsonSerializer.Deserialize<DegreePlan>(json, AppJson.UserData)
            ?? throw new ToolFailure("storage_error", "Could not copy the plan.");
    }
}
