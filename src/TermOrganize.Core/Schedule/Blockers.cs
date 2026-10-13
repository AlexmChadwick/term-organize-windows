// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// Portions derived from SearchNEU where the Swift file says so. Modified 2026-10-13: ported from Swift to C#.

using System.Diagnostics;

namespace TermOrganize.Core;

public sealed class ScheduleBlockerOption : IEquatable<ScheduleBlockerOption>
{
    public List<CourseKey> Courses { get; set; } = [];
    public int ScheduleCount { get; set; }
    public bool IsLowerBound { get; set; }
    public string Id => string.Join("+", Courses.Select(c => c.ToString()));

    public bool Equals(ScheduleBlockerOption? other) =>
        other is not null && ScheduleCount == other.ScheduleCount && IsLowerBound == other.IsLowerBound && Courses.SequenceEqual(other.Courses);

    public override bool Equals(object? obj) => Equals(obj as ScheduleBlockerOption);
    public override int GetHashCode() => HashCode.Combine(ScheduleCount, IsLowerBound, Courses.Count);
}

public sealed class ScheduleBlockerAnalysis
{
    public int RequiredCount { get; set; }
    public List<CourseKey> NotOffered { get; set; } = [];
    public List<CourseKey> FilteredOut { get; set; } = [];
    public List<ScheduleBlockerOption> Options { get; set; } = [];
    public List<ScheduleDiagnostics.ConflictPair> ConflictingPairs { get; set; } = [];
    public bool IsIncomplete { get; set; }

    public List<CourseKey> Unschedulable => NotOffered.Concat(FilteredOut).ToList();
    public List<CourseKey> BlockingCourses => Options.Count == 0 ? [] : Options[0].Courses;

    public List<CourseKey> AllBlockingCourses
    {
        get
        {
            var seen = new HashSet<CourseKey>();
            var result = new List<CourseKey>();
            foreach (var key in Options.SelectMany(o => o.Courses))
                if (seen.Add(key)) result.Add(key);
            return result;
        }
    }

    public List<CourseKey> Clashes(CourseKey key)
    {
        var result = new List<CourseKey>();
        foreach (var pair in ConflictingPairs)
        {
            if (pair.First == key) result.Add(pair.Second);
            else if (pair.Second == key) result.Add(pair.First);
        }
        return result;
    }
}

public static partial class ScheduleGenerator
{
    public static ScheduleBlockerAnalysis AnalyzeBlockers(
        IReadOnlyList<ScheduleCourseInput> courses,
        ScheduleFilters filters,
        SchedulePreferences? preferences = null,
        ScheduleDiagnostics? diagnostics = null,
        double timeBudget = 1.5,
        int maxProbes = 60,
        CancellationToken cancellationToken = default)
    {
        preferences ??= new SchedulePreferences();
        var started = Stopwatch.GetTimestamp();
        var analysis = new ScheduleBlockerAnalysis();
        var required = new List<int>();
        for (var i = 0; i < courses.Count; i++)
            if (courses[i].IsRequired) required.Add(i);
        analysis.RequiredCount = required.Count;
        if (required.Count == 0) return analysis;

        var basis = diagnostics ?? Generate(courses, filters, preferences, ScheduleSortOption.Best, ScheduleLimits.Probe, cancellationToken).Diagnostics;
        analysis.ConflictingPairs = basis.ConflictingPairs;
        if (basis.CountProblemKind is not null || basis.Courses.Count != courses.Count) return analysis;

        var dead = new List<int>();
        foreach (var index in required)
        {
            if (!basis.Courses[index].IsBlocked) continue;
            dead.Add(index);
            if (basis.Courses[index].TotalSections == 0) analysis.NotOffered.Add(courses[index].Key);
            else analysis.FilteredOut.Add(courses[index].Key);
        }
        var others = required.Where(index => !dead.Contains(index)).ToList();
        var probes = 0;
        bool OverBudget() =>
            cancellationToken.IsCancellationRequested || probes >= maxProbes ||
            (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency > timeBudget;

        ScheduleBlockerOption? Probe(List<int> drop)
        {
            probes++;
            var relaxed = courses.Select(c => c.Clone()).ToList();
            foreach (var index in drop) relaxed[index].IsRequired = false;
            var result = Generate(relaxed, filters, preferences, ScheduleSortOption.Best, ScheduleLimits.BlockerProbe, cancellationToken);
            if (result.TotalFound <= 0) return null;
            return new ScheduleBlockerOption
            {
                Courses = drop.OrderBy(i => i).Select(i => courses[i].Key).ToList(),
                ScheduleCount = result.TotalFound,
                IsLowerBound = !result.IsExhaustive,
            };
        }

        var found = new List<ScheduleBlockerOption>();
        if (dead.Count > 0 && Probe(dead) is ScheduleBlockerOption onlyDead) found = [onlyDead];
        if (found.Count == 0)
        {
            foreach (var index in others)
            {
                if (OverBudget())
                {
                    analysis.IsIncomplete = true;
                    break;
                }
                var drop = dead.Concat([index]).ToList();
                if (Probe(drop) is ScheduleBlockerOption option) found.Add(option);
            }
        }
        if (found.Count == 0 && !analysis.IsIncomplete && others.Count >= 2)
        {
            for (var i = 0; i < others.Count && !analysis.IsIncomplete; i++)
            {
                for (var j = i + 1; j < others.Count; j++)
                {
                    if (OverBudget())
                    {
                        analysis.IsIncomplete = true;
                        break;
                    }
                    var drop = dead.Concat([others[i], others[j]]).ToList();
                    if (Probe(drop) is ScheduleBlockerOption option) found.Add(option);
                }
            }
        }
        analysis.Options = found
            .OrderBy(o => o.Courses.Count)
            .ThenByDescending(o => o.ScheduleCount)
            .ToList();
        return analysis;
    }
}
