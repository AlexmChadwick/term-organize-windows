// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text;

namespace TermOrganize.Core;

/// <summary>
/// Compares course titles, to tell whether two courses may be one course under two numbers.
/// Titles are reduced to the words that say what a course is about. It is a hint, not proof:
/// generic titles are not compared, sequence numbers never match, and a course does not match
/// its lab or its advanced version.
/// </summary>
public static class CourseTitleMatcher
{
    /// <summary>The least similarity that counts as alike: the share of the words in either title that both have.</summary>
    public const double MinimumSimilarity = 0.6;

    /// <summary>A course title reduced to what tells courses apart.</summary>
    public sealed class Title : IEquatable<Title>
    {
        public HashSet<string> Words { get; }
        /// <summary>Part numbers ("1", "ii" read as "2"): the courses of a sequence differ only by them.</summary>
        public HashSet<string> Parts { get; }
        public HashSet<string> Qualifiers { get; }
        /// <summary>The last word when the title is long enough to have been cut off (degree audits stop at 30 characters).</summary>
        public string? CutOffWord { get; }

        public Title(string text)
        {
            var words = new HashSet<string>(StringComparer.Ordinal);
            var parts = new HashSet<string>(StringComparer.Ordinal);
            string? last = null;
            foreach (var raw in Tokens(text))
            {
                var part = Part(raw);
                if (part is not null)
                {
                    parts.Add(part);
                    last = null;
                    continue;
                }
                var singular = Singular(raw);
                var word = Synonyms.TryGetValue(singular, out var synonym) ? synonym : singular;
                if (Filler.Contains(word))
                {
                    last = null;
                    continue;
                }
                words.Add(word);
                last = word;
            }
            Words = words;
            Parts = parts;
            Qualifiers = words.Where(QualifiersSet.Contains).ToHashSet(StringComparer.Ordinal);
            var trimmed = text.Trim();
            CutOffWord = trimmed.Length >= 29 && last is { Length: >= 3 } ? last : null;
        }

        /// <summary>Two or more telling words, not all of them words every kind of course has.</summary>
        public bool IsDistinctive => Words.Count >= 2 && !Words.IsSubsetOf(Formats);

        public bool Equals(Title? other) =>
            other is not null && Words.SetEquals(other.Words) && Parts.SetEquals(other.Parts) &&
            Qualifiers.SetEquals(other.Qualifiers) && CutOffWord == other.CutOffWord;

        public override bool Equals(object? obj) => Equals(obj as Title);
        public override int GetHashCode() => HashCode.Combine(Words.Count, Parts.Count, CutOffWord);

        private static List<string> Tokens(string text)
        {
            var folded = Fold(text.Replace("&", " and ", StringComparison.Ordinal).Replace("'", "", StringComparison.Ordinal).Replace("\u2019", "", StringComparison.Ordinal));
            var tokens = new List<string>();
            var current = new StringBuilder();
            foreach (var character in folded)
            {
                if (char.IsLetter(character) || char.IsNumber(character)) current.Append(character);
                else if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            if (current.Length > 0) tokens.Add(current.ToString());
            return tokens;
        }

        private static string? Part(string token)
        {
            if (token.Length > 0 && char.IsDigit(token[0])) return token;
            return RomanNumerals.TryGetValue(token, out var value) ? value.ToString(CultureInfo.InvariantCulture) : null;
        }

        /// <summary>Systems to system, studies to study; words that only look plural are left alone.</summary>
        private static string Singular(string word)
        {
            if (word.Length <= 3) return word;
            if (word.EndsWith("ies", StringComparison.Ordinal) && word.Length > 4) return word[..^3] + "y";
            if (word.EndsWith("sses", StringComparison.Ordinal)) return word[..^2];
            if (word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) && !word.EndsWith("us", StringComparison.Ordinal) && !word.EndsWith("is", StringComparison.Ordinal))
                return word[..^1];
            return word;
        }
    }

    public static double? Similarity(string a, string b, double minimum = MinimumSimilarity) =>
        Similarity(new Title(a), new Title(b), minimum);

    public static double? Similarity(Title a, Title b, double minimum = MinimumSimilarity)
    {
        if (a.Parts.Count > 0 && b.Parts.Count > 0 && !a.Parts.SetEquals(b.Parts)) return null;
        if (!a.Qualifiers.SetEquals(b.Qualifiers)) return null;
        var aWords = Completing(a, b.Words);
        var bWords = Completing(b, a.Words);
        if (aWords.Count < 2 || bWords.Count < 2) return null;
        var shared = aWords.Count(bWords.Contains);
        if (shared == 0) return null;
        if (aWords.IsSubsetOf(Formats) || bWords.IsSubsetOf(Formats)) return null;
        var union = aWords.Union(bWords).Count();
        var score = (double)shared / union;
        if (score >= minimum) return score;
        var contained = shared == aWords.Count || shared == bWords.Count;
        return contained ? minimum : null;
    }

    private static HashSet<string> Completing(Title title, HashSet<string> other)
    {
        if (title.CutOffWord is not string cut || other.Contains(cut)) return title.Words;
        var full = other.OrderBy(word => word, StringComparer.Ordinal).FirstOrDefault(word => word.StartsWith(cut, StringComparison.Ordinal) && word.Length > cut.Length);
        if (full is null) return title.Words;
        var words = new HashSet<string>(title.Words, StringComparer.Ordinal);
        words.Remove(cut);
        words.Add(full);
        return words;
    }

    private static string Fold(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "as", "at", "by", "for", "from", "in", "into", "of", "on", "or", "the", "to", "with",
        "principle", "introduction", "introductory", "intro", "fundamental", "topic",
        "prncple", "prncpl", "intr", "introd",
    };

    private static readonly HashSet<string> Formats = new(StringComparer.Ordinal)
    {
        "special", "directed", "independent", "study", "research", "thesis", "dissertation", "seminar", "project",
        "practicum", "capstone", "continuation", "experience", "internship", "co", "op", "work", "term", "workshop",
        "lab", "reading", "tutorial", "residency", "preparation",
    };

    private static readonly HashSet<string> QualifiersSet = new(StringComparer.Ordinal)
    {
        "lab", "recitation", "advanced", "intermediate", "elementary",
    };

    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.Ordinal)
    {
        ["laboratory"] = "lab",
    };

    private static readonly Dictionary<string, int> RomanNumerals = new(StringComparer.Ordinal)
    {
        ["i"] = 1, ["ii"] = 2, ["iii"] = 3, ["iv"] = 4, ["v"] = 5, ["vi"] = 6, ["vii"] = 7, ["viii"] = 8, ["ix"] = 9, ["x"] = 10,
    };
}
