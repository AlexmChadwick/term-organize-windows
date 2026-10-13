// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// "Did CS 2510 count as CS 2100?" Banner has no renumbering links, so a requirement that names
// CS 2100 can look unmet to a student who took CS 2510. The answer is the student's to give.

namespace TermOrganize.Core;

/// <summary>A question for the student: did a course they completed count as one a requirement names?</summary>
public sealed class EquivalencePrompt : IEquatable<EquivalencePrompt>
{
    public required CourseKey Taken { get; init; }
    public required CourseKey Required { get; init; }
    public required string Evidence { get; init; }
    public int Together { get; init; }
    public string Id => $"{Taken}|{Required}";

    /// <summary>
    /// The questions a requirement calls for, likeliest first. A question is asked only when
    /// answering it matters: the requirement isn't met yet, it names the required course and not
    /// the taken one, and the student hasn't completed the required course or anything counting as it.
    /// </summary>
    public static List<EquivalencePrompt> Choose(
        Requisite requisite,
        CompletedCourses completed,
        ISet<string>? dismissed = null,
        Func<CourseKey, IReadOnlyList<EquivalenceSuggestion>>? suggestions = null,
        int limit = 2)
    {
        suggestions ??= static _ => [];
        dismissed ??= new HashSet<string>(StringComparer.Ordinal);
        var tree = requisite.NormalizedForDisplay();
        if (tree is null || completed.Meets(tree)) return [];
        var covered = completed.Covered;
        var named = tree.ReferencedCourses().ToHashSet();
        var unmet = named.Where(key => !covered.Contains(key)).ToHashSet();
        if (unmet.Count == 0) return [];
        var best = new Dictionary<CourseKey, EquivalenceSuggestion>();
        foreach (var taken in completed.Taken.OrderBy(key => key))
        {
            if (named.Contains(taken)) continue;
            foreach (var suggestion in suggestions(taken))
            {
                if (!unmet.Contains(suggestion.Candidate)) continue;
                var pair = EquivalenceSuggestionDismissals.PairId(taken, suggestion.Candidate);
                if (dismissed.Contains(pair)) continue;
                if (best.TryGetValue(suggestion.Candidate, out var current) && current.Together >= suggestion.Together) continue;
                best[suggestion.Candidate] = suggestion;
            }
        }
        return best.Values
            .OrderByDescending(suggestion => suggestion.Together)
            .ThenBy(suggestion => suggestion.Candidate)
            .Take(limit)
            .Select(suggestion => new EquivalencePrompt
            {
                Taken = suggestion.Course,
                Required = suggestion.Candidate,
                Evidence = suggestion.Evidence,
                Together = suggestion.Together,
            })
            .ToList();
    }

    public bool Equals(EquivalencePrompt? other) =>
        other is not null && Taken == other.Taken && Required == other.Required && Evidence == other.Evidence && Together == other.Together;

    public override bool Equals(object? obj) => Equals(obj as EquivalencePrompt);
    public override int GetHashCode() => HashCode.Combine(Taken, Required, Together);
}

/// <summary>
/// The suggestions the student turned down, kept on this device.
/// The pair is unordered: "CS 2510 counts as CS 2100" and the reverse are one suggestion.
/// </summary>
public static class EquivalenceSuggestionDismissals
{
    public const string StorageKey = "degreePlan.dismissedEquivalenceSuggestions";
    public const int Limit = 200;

    public static string PairId(CourseKey a, CourseKey b) => a.CompareTo(b) < 0 ? $"{a}|{b}" : $"{b}|{a}";

    public static HashSet<string> Decode(string raw) =>
        raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    public static bool IsDismissed(CourseKey a, CourseKey b, string raw) => Decode(raw).Contains(PairId(a, b));

    public static bool IsDismissed(EquivalencePrompt prompt, string raw) => IsDismissed(prompt.Taken, prompt.Required, raw);

    public static string Dismiss(CourseKey a, CourseKey b, string raw) => Adding(PairId(a, b), raw);

    public static string Dismiss(EquivalencePrompt prompt, string raw) => Dismiss(prompt.Taken, prompt.Required, raw);

    public static string Adding(string id, string raw, int limit = Limit)
    {
        var ids = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(item => item != id).ToList();
        ids.Add(id);
        if (ids.Count > limit) ids = ids.Skip(ids.Count - limit).ToList();
        return string.Join('\n', ids);
    }
}
