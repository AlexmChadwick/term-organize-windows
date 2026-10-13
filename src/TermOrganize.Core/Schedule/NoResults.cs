// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// Portions derived from SearchNEU where the Swift file says so. Modified 2026-10-13: ported from Swift to C#.

using System.Globalization;

namespace TermOrganize.Core;

public sealed class ScheduleNoResults
{
    public ScheduleDiagnostics Diagnostics { get; set; } = new();
    public List<ScheduleSuggestion> Suggestions { get; set; } = [];
    public ScheduleBlockerAnalysis Blockers { get; set; } = new();
}

public static partial class ScheduleGenerator
{
    public static ScheduleNoResults AnalyzeEmptyResult(
        IReadOnlyList<ScheduleCourseInput> courses,
        ScheduleFilters filters,
        SchedulePreferences preferences,
        ScheduleDiagnostics diagnostics,
        bool includeBlockers = true,
        CancellationToken cancellationToken = default)
    {
        var blockers = includeBlockers
            ? AnalyzeBlockers(courses, filters, preferences, diagnostics, cancellationToken: cancellationToken)
            : new ScheduleBlockerAnalysis();
        var suggestions = cancellationToken.IsCancellationRequested
            ? []
            : SuggestRelaxations(courses, filters, preferences, includeCourseRelaxations: !includeBlockers, cancellationToken);
        return new ScheduleNoResults { Diagnostics = diagnostics, Suggestions = suggestions, Blockers = blockers };
    }
}

public static class ScheduleNoResultsText
{
    public sealed class Reason
    {
        public required string Symbol { get; init; }
        public required string Title { get; init; }
        public string? Detail { get; init; }
    }

    public static int RequiredCount(ScheduleDiagnostics diagnostics) => diagnostics.Courses.Count(c => c.IsRequired);

    public static string Title(ScheduleDiagnostics diagnostics)
    {
        var required = RequiredCount(diagnostics);
        if (diagnostics.CountProblemKind is not null || required <= 1) return "No schedules fit";
        return diagnostics.Courses.Count > required
            ? $"No schedule fits all {required} required courses"
            : $"No schedule fits all {required} courses";
    }

    public static string Headline(ScheduleDiagnostics diagnostics, ScheduleBlockerAnalysis? blockers)
    {
        if (diagnostics.HasNoCourses) return "Add some courses to this schedule first.";
        if (blockers is not null && blockers.Options.Count > 0) return "Here\u2019s what would work.";
        if (diagnostics.CountProblemKind is not null || diagnostics.BlockedRequiredCourses.Count > 0 || diagnostics.ConflictingPairs.Count > 0)
            return "Here\u2019s what\u2019s getting in the way.";
        return "These courses can\u2019t all be taken together with the current rules.";
    }

    public static string List(IReadOnlyList<CourseKey> keys)
    {
        var names = keys.Select(k => k.ToString()).ToList();
        return names.Count switch
        {
            0 => "",
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        };
    }

    public static string Detail(ScheduleBlockerOption option, ScheduleBlockerAnalysis blockers, string termName)
    {
        var parts = new List<string>();
        var count = Format(option.ScheduleCount);
        var noun = option.ScheduleCount == 1 && !option.IsLowerBound ? "schedule" : "schedules";
        parts.Add(option.IsLowerBound ? $"{count}+ {noun}" : $"{count} {noun}");
        foreach (var key in option.Courses)
        {
            if (blockers.NotOffered.Contains(key)) parts.Add($"{key} isn\u2019t offered in {termName}");
            else if (blockers.FilteredOut.Contains(key)) parts.Add($"your rules rule out every {key} section");
        }
        if (option.Courses.Count == 1)
        {
            var clashes = blockers.Clashes(option.Courses[0]);
            if (clashes.Count > 0) parts.Add($"overlaps {List(clashes)}");
        }
        return string.Join(" \u00B7 ", parts);
    }

    public static bool IsUncertain(string reason) =>
        reason.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("couldn\u2019t", StringComparison.OrdinalIgnoreCase);

    public static string CountText(ScheduleSuggestion suggestion)
    {
        var count = Format(suggestion.ScheduleCount);
        var noun = suggestion.ScheduleCount == 1 && !suggestion.IsLowerBound ? "schedule" : "schedules";
        return suggestion.IsLowerBound ? $"{count}+ {noun}" : $"{count} {noun}";
    }

    public static List<Reason> Reasons(ScheduleDiagnostics diagnostics, string termName)
    {
        var result = new List<Reason>();
        foreach (var report in diagnostics.Courses.Where(r => r.IsBlocked && r.IsRequired))
        {
            if (report.TotalSections == 0)
            {
                result.Add(new Reason
                {
                    Symbol = "questionmark.folder",
                    Title = $"{report.Key} isn\u2019t offered in {termName}",
                    Detail = "Remove it from the schedule or make it optional.",
                });
            }
            else
            {
                result.Add(new Reason
                {
                    Symbol = "xmark.circle",
                    Title = $"No {report.Key} sections left",
                    Detail = RejectionText(report, diagnostics),
                });
            }
        }

        foreach (var pair in diagnostics.ConflictingPairs)
        {
            switch (pair.Reason)
            {
                case ScheduleDiagnostics.ConflictCause.Overlap:
                    result.Add(new Reason
                    {
                        Symbol = "rectangle.on.rectangle.slash",
                        Title = $"{pair.First} and {pair.Second} always overlap",
                        Detail = "Every remaining section of one meets while a section of the other does. Make one optional or pick different sections.",
                    });
                    break;
                case ScheduleDiagnostics.ConflictCause.TravelTime:
                    result.Add(new Reason
                    {
                        Symbol = "figure.walk",
                        Title = $"{pair.First} and {pair.Second} are always too close together",
                        Detail = $"Every combination leaves less than {diagnostics.Preferences.MinimumBreakMinutes} minutes between them in different buildings. Lower the walking time or make one optional.",
                    });
                    break;
            }
        }

        switch (diagnostics.CountProblemKind)
        {
            case ScheduleDiagnostics.CountProblem.TooManyRequired(var required, var wanted):
                result.Add(new Reason
                {
                    Symbol = "number.circle",
                    Title = $"{required} courses are required, but schedules are limited to {wanted}",
                    Detail = "Raise the number of courses or make some courses optional.",
                });
                break;
            case ScheduleDiagnostics.CountProblem.TooFewCourses(var available, var wanted):
                result.Add(new Reason
                {
                    Symbol = "number.circle",
                    Title = $"Only {available} {(available == 1 ? "course has" : "courses have")} sections left, but {wanted} are wanted",
                    Detail = "Lower the number of courses or relax a filter.",
                });
                break;
            case ScheduleDiagnostics.CountProblem.CreditsOverTarget(var required, var limit):
                result.Add(new Reason
                {
                    Symbol = "number.circle",
                    Title = $"The required courses are {required} credits, but your target stops at {limit}",
                    Detail = "Raise the credit target or make a course optional.",
                });
                break;
            case ScheduleDiagnostics.CountProblem.CreditsUnderTarget(var available, var wanted):
                result.Add(new Reason
                {
                    Symbol = "number.circle",
                    Title = $"Your courses add up to {available} credits, but you want {wanted}",
                    Detail = "Lower the credit target or add a course.",
                });
                break;
        }

        foreach (var report in diagnostics.Courses.Where(r => r.IsBlocked && !r.IsRequired && r.TotalSections > 0))
        {
            result.Add(new Reason
            {
                Symbol = "minus.circle",
                Title = $"{report.Key} (optional) can\u2019t be added",
                Detail = RejectionText(report, diagnostics),
            });
        }
        return result;
    }

    public static string RejectionText(ScheduleDiagnostics.CourseReport report, ScheduleDiagnostics diagnostics)
    {
        var total = report.TotalSections;
        var ordered = report.Rejections.Where(p => p.Value > 0)
            .OrderByDescending(p => p.Value)
            .ThenBy(p => (int)p.Key)
            .ToList();
        if (ordered.Count == 0)
            return total == 1 ? "Its only section was filtered out." : $"All {total} sections were filtered out.";
        var scheduleFilters = diagnostics.Filters;
        var preferences = diagnostics.Preferences;

        string Phrase(ScheduleFilterReason reason, bool plural) => reason switch
        {
            ScheduleFilterReason.ExcludedByUser => plural ? "are skipped by you" : "is skipped by you",
            ScheduleFilterReason.StartsTooEarly => (plural ? "start" : "starts") + " before " + (scheduleFilters.EarliestStart is int start ? TimeOfDay.Label(start) : "your limit"),
            ScheduleFilterReason.EndsTooLate => (plural ? "end" : "ends") + " after " + (scheduleFilters.LatestEnd is int end ? TimeOfDay.Label(end) : "your limit"),
            ScheduleFilterReason.FreeDay => (plural ? "meet" : "meets") + " on a day you want free",
            ScheduleFilterReason.Honors => plural ? "are honors sections" : "is an honors section",
            ScheduleFilterReason.Online => plural ? "are online" : "is online",
            ScheduleFilterReason.Seats => plural ? "are full or low on seats" : "is full or low on seats",
            ScheduleFilterReason.Campus => plural ? "are on another campus" : "is on another campus",
            ScheduleFilterReason.BlockedTime => plural ? "overlap a blocked time" : "overlaps a blocked time",
            ScheduleFilterReason.PrefStartsTooEarly => (plural ? "start" : "starts") + " before " + (preferences.EarliestStart is TimeLimit early ? TimeOfDay.Label(early.Minutes) : "your earliest time"),
            ScheduleFilterReason.PrefEndsTooLate => (plural ? "end" : "ends") + " after " + (preferences.LatestEnd is TimeLimit late ? TimeOfDay.Label(late.Minutes) : "your latest time"),
            ScheduleFilterReason.PrefDayOff => (plural ? "meet" : "meets") + " on a day you want off",
            ScheduleFilterReason.PrefFormat => (plural ? "aren\u2019t " : "isn\u2019t ") + preferences.Format.Title().ToLowerInvariant(),
            ScheduleFilterReason.PrefSeats => plural ? "have no open seats" : "has no open seats",
            ScheduleFilterReason.PrefCampus => plural ? "are on another campus" : "is on another campus",
            ScheduleFilterReason.PrefWorkload => plural ? "run longer in a day than your limit" : "runs longer in a day than your limit",
            _ => "",
        };

        if (ordered.Count == 1 && ordered[0].Value == total)
        {
            if (total == 1) return $"Its only section {Phrase(ordered[0].Key, false)}.";
            return $"All {total} sections {Phrase(ordered[0].Key, true)}.";
        }
        var parts = ordered.Select(pair => $"{pair.Value} {Phrase(pair.Key, pair.Value != 1)}");
        return "Of " + total + " sections: " + string.Join(", ", parts) + ".";
    }

    private static string Format(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
