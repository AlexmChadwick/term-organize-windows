// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// A guess at the course a retired course became, or the other way around.
/// Evidence is either that prerequisites list the two as alternatives, or that their titles are alike.
/// A suggestion is never applied on its own: the student confirms it.
/// </summary>
public sealed class EquivalenceSuggestion : IEquatable<EquivalenceSuggestion>
{
    public required CourseKey Course { get; init; }
    public required CourseKey Candidate { get; init; }
    public int Together { get; init; }
    public int Total { get; init; }
    public string? SimilarTitle { get; init; }
    public double? TitleScore { get; init; }

    public string Id => $"{Course}|{Candidate}";
    public double Confidence => Total > 0 ? (double)Together / Total : 0;
    public bool IsListedTogether => Together > 0;

    /// <summary>
    /// "Listed together in 7 of the 10 prerequisites that name CS 2510",
    /// "Similar title: …", or the first followed by " · similar title".
    /// </summary>
    public string Evidence
    {
        get
        {
            string? listing = null;
            if (Together > 0)
            {
                listing = Total == 1
                    ? $"Listed together in the prerequisites of the 1 course that names {Course}"
                    : $"Listed together in {Together} of the {Total} prerequisites that name {Course}";
            }
            if (listing is not null && SimilarTitle is not null) return $"{listing} · similar title";
            if (listing is not null) return listing;
            if (SimilarTitle is not null) return $"Similar title: {SimilarTitle}";
            return "";
        }
    }

    public bool Equals(EquivalenceSuggestion? other) =>
        other is not null && Course == other.Course && Candidate == other.Candidate && Together == other.Together &&
        Total == other.Total && SimilarTitle == other.SimilarTitle && TitleScore == other.TitleScore;

    public override bool Equals(object? obj) => Equals(obj as EquivalenceSuggestion);
    public override int GetHashCode() => HashCode.Combine(Course, Candidate, Together, Total);
}

public static class EquivalenceSuggester
{
    public static List<EquivalenceSuggestion> Suggestions(
        CourseKey retired,
        IEnumerable<(CourseKey Course, Requisite Requisite)> prerequisites,
        Func<CourseKey, bool> isCurrent,
        CourseEquivalences? known = null,
        int minimumTogether = 2,
        double minimumShare = 0.5,
        int limit = 3,
        Func<CourseKey, string?>? title = null,
        IReadOnlyList<CourseKey>? titleCandidates = null,
        double minimumTitleSimilarity = CourseTitleMatcher.MinimumSimilarity)
    {
        if (limit <= 0) return [];
        known ??= CourseEquivalences.None;
        title ??= static _ => null;
        titleCandidates ??= [];
        var (listed, dependents) = ListedTogether(retired, prerequisites, isCurrent, known, minimumTogether, minimumShare);
        var similar = SimilarTitles(retired, titleCandidates, known, title, minimumTitleSimilarity);

        var byPrerequisites = listed.Select(suggestion =>
        {
            if (!similar.TryGetValue(suggestion.Candidate, out var match)) return suggestion;
            return new EquivalenceSuggestion
            {
                Course = suggestion.Course,
                Candidate = suggestion.Candidate,
                Together = suggestion.Together,
                Total = suggestion.Total,
                SimilarTitle = match.Title,
                TitleScore = match.Score,
            };
        }).OrderByDescending(suggestion => suggestion.Together).ThenBy(suggestion => suggestion.Candidate).ToList();

        var listedKeys = listed.Select(suggestion => suggestion.Candidate).ToHashSet();
        var byTitle = similar
            .Where(pair => !listedKeys.Contains(pair.Key))
            .Select(pair => new EquivalenceSuggestion
            {
                Course = retired,
                Candidate = pair.Key,
                Together = 0,
                Total = dependents,
                SimilarTitle = pair.Value.Title,
                TitleScore = pair.Value.Score,
            })
            .OrderByDescending(suggestion => suggestion.TitleScore ?? 0)
            .ThenBy(suggestion => suggestion.Candidate)
            .ToList();

        var free = Math.Max(0, limit - byPrerequisites.Count);
        var reserved = limit >= 2 && byPrerequisites.Count > 0 ? 1 : 0;
        var titleSlots = Math.Min(byTitle.Count, Math.Max(free, reserved));
        return byPrerequisites.Take(limit - titleSlots).Concat(byTitle.Take(titleSlots)).ToList();
    }

    private static (List<EquivalenceSuggestion> Suggestions, int Dependents) ListedTogether(
        CourseKey retired,
        IEnumerable<(CourseKey Course, Requisite Requisite)> prerequisites,
        Func<CourseKey, bool> isCurrent,
        CourseEquivalences known,
        int minimumTogether,
        double minimumShare)
    {
        var dependents = new HashSet<CourseKey>();
        var listedTogether = new Dictionary<CourseKey, HashSet<CourseKey>>();
        foreach (var (course, requisite) in prerequisites)
        {
            if (course == retired) continue;
            var mentionsRetired = false;
            void Visit(Requisite node)
            {
                switch (node)
                {
                    case Requisite.CourseNode courseNode:
                        if (courseNode.Key == retired) mentionsRetired = true;
                        break;
                    case Requisite.TestNode:
                        break;
                    case Requisite.AndNode and:
                        foreach (var item in and.Items) Visit(item);
                        break;
                    case Requisite.OrNode or:
                        var alternatives = or.Items.OfType<Requisite.CourseNode>().Select(item => item.Key).ToList();
                        if (alternatives.Contains(retired))
                        {
                            foreach (var other in alternatives.Where(key => key != retired))
                            {
                                if (!listedTogether.TryGetValue(other, out var set)) listedTogether[other] = set = [];
                                set.Add(course);
                            }
                        }
                        foreach (var item in or.Items) Visit(item);
                        break;
                }
            }
            var flat = requisite.Simplified();
            if (flat is not null) Visit(flat);
            if (mentionsRetired) dependents.Add(course);
        }
        var total = dependents.Count;
        if (total == 0) return ([], 0);
        var found = new List<EquivalenceSuggestion>();
        foreach (var (candidate, courses) in listedTogether)
        {
            if (!isCurrent(candidate) || known.AreEquivalent(retired, candidate)) continue;
            if (courses.Count < minimumTogether || (double)courses.Count / total < minimumShare) continue;
            found.Add(new EquivalenceSuggestion { Course = retired, Candidate = candidate, Together = courses.Count, Total = total });
        }
        return (found, total);
    }

    private static Dictionary<CourseKey, (string Title, double Score)> SimilarTitles(
        CourseKey retired,
        IReadOnlyList<CourseKey> candidates,
        CourseEquivalences known,
        Func<CourseKey, string?> title,
        double minimum)
    {
        var found = new Dictionary<CourseKey, (string Title, double Score)>();
        if (candidates.Count == 0 || title(retired) is not string retiredText) return found;
        var retiredTitle = new CourseTitleMatcher.Title(retiredText);
        if (!retiredTitle.IsDistinctive) return found;
        foreach (var candidate in candidates)
        {
            if (candidate == retired || found.ContainsKey(candidate) || known.AreEquivalent(retired, candidate)) continue;
            var text = title(candidate)?.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            var score = CourseTitleMatcher.Similarity(retiredTitle, new CourseTitleMatcher.Title(text), minimum);
            if (score is double value) found[candidate] = (text, value);
        }
        return found;
    }
}
