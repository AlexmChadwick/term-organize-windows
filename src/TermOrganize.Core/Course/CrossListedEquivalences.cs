// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Courses that are one course listed under several subjects, read from cross-listed sections.
// Banner cross-lists far more than duplicate courses: combined undergraduate and graduate
// sections and special-topics shells meet together too, and are not interchangeable.
// Only cross-listed courses with the same number and the same title, in different subjects, count.

using System.Text.RegularExpressions;

namespace TermOrganize.Core;

public static class CrossListedEquivalences
{
    public static CourseEquivalences FromCatalog(CatalogIndex index)
    {
        var pairs = new HashSet<(CourseKey First, CourseKey Second)>();
        foreach (var section in index.Catalog.Sections)
        {
            if (section.CrossListCRNs.Count == 0) continue;
            var key = section.CourseKey;
            if (index.Course(key)?.SpecialTopics == true) continue;
            foreach (var crn in section.CrossListCRNs)
            {
                var other = index.Section(crn);
                if (other is null) continue;
                var otherKey = other.CourseKey;
                if (otherKey.Number != key.Number || otherKey.Subject == key.Subject) continue;
                if (index.Course(otherKey)?.SpecialTopics == true) continue;
                var title = NormalizedTitle(index.Course(key)?.Title ?? section.Title);
                var otherTitle = NormalizedTitle(index.Course(otherKey)?.Title ?? other.Title);
                if (title.Length == 0 || title != otherTitle) continue;
                var first = key.CompareTo(otherKey) <= 0 ? key : otherKey;
                var second = key.CompareTo(otherKey) <= 0 ? otherKey : key;
                pairs.Add((first, second));
            }
        }
        return new CourseEquivalences(pairs.Select(pair => (pair.First, pair.Second)));
    }

    private static string NormalizedTitle(string title) =>
        Regex.Replace(title.Trim(), @"\s+", " ").ToLowerInvariant();
}
