// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// The examples the empty search screen offers, built from the loaded catalog so every one of them
// finds something, and the busiest subjects it lists.

namespace TermOrganize.Core;

/// <summary>An example query offered on the empty search screen.</summary>
public sealed class SearchExample
{
    public required string Title { get; init; }
    public required string Caption { get; init; }
    public required string Symbol { get; init; }
    public string Id => Caption;
}

/// <summary>Builds the examples from real catalog data once per catalog snapshot.</summary>
public static class SearchExampleCache
{
    private static readonly object Gate = new();
    private static string? _cachedKey;
    private static List<SearchExample> _cached = [];

    public static IReadOnlyList<SearchExample> Examples(CatalogIndex index)
    {
        var key = $"{index.Term.Code}|{index.Catalog.SectionsUpdatedAt.UtcTicks}";
        lock (Gate)
        {
            if (key == _cachedKey) return _cached;
            _cached = Build(index);
            _cachedKey = key;
            return _cached;
        }
    }

    private static List<SearchExample> Build(CatalogIndex index)
    {
        var result = new List<SearchExample>();
        var preferred = new[] { "CS 3000", "CS 2500", "MATH 1341", "ENGW 1111", "PHYS 1151" };
        CourseKey? exampleKey = null;
        foreach (var text in preferred)
        {
            var parsed = CourseKey.Parse(text);
            if (parsed is CourseKey key && index.Course(key) is not null)
            {
                exampleKey = key;
                break;
            }
        }
        exampleKey ??= index.Courses.Count > 0 ? index.Courses[0].Key : null;
        if (exampleKey is CourseKey chosen)
            result.Add(new SearchExample { Title = chosen.ToString(), Caption = "Course code", Symbol = "book.closed" });

        var topics = new[] { "machine learning", "data structures", "calculus", "psychology", "writing" };
        var topic = topics.FirstOrDefault(candidate =>
            index.SearchDocuments.Any(document => document.TitleNormalized.Contains(candidate, StringComparison.Ordinal)));
        if (topic is not null)
            result.Add(new SearchExample { Title = topic, Caption = "Topic or title", Symbol = "lightbulb" });

        string? crn = null;
        if (exampleKey is CourseKey courseKey)
            crn = index.Sections(courseKey).FirstOrDefault()?.Crn;
        crn ??= index.Catalog.Sections.FirstOrDefault()?.Crn;
        if (crn is not null)
            result.Add(new SearchExample { Title = crn, Caption = "CRN", Symbol = "number" });

        InstructorSummary? teacher = null;
        if (exampleKey is CourseKey taught)
        {
            foreach (var section in index.Sections(taught))
            {
                var named = section.Instructors?.FirstOrDefault(instructor => instructor.Primary)
                    ?? section.Instructors?.FirstOrDefault();
                if (named is null) continue;
                teacher = index.Instructor(named.Name);
                if (teacher is not null) break;
            }
        }
        var professor = teacher ?? index.Instructors.MaxBy(summary => summary.CourseKeys.Count);
        if (professor is not null)
            result.Add(new SearchExample { Title = professor.DisplayName, Caption = "Professor", Symbol = "person.crop.circle" });
        return result;
    }
}

public static class PopularSubjects
{
    /// <summary>The subjects with the most courses (ties broken by code), for the quick subject buttons.</summary>
    public static List<Subject> Popular(this CatalogIndex index, int limit = 12) =>
        index.ActiveSubjects
            .OrderByDescending(subject => index.CourseCountBySubject.TryGetValue(subject.Code, out var count) ? count : 0)
            .ThenBy(subject => subject.Code, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
}
