// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// On-device search in the style of SearchNEU's MiniSearch setup: course codes
// outrank titles, word prefixes match, small typos are tolerated, and every
// query word has to match. Instructors and descriptions are searched too.

using System.Globalization;
using System.Text;

namespace TermOrganize.Core;

public enum SearchSort
{
    Relevance,
    CourseNumber,
    Title,
    OpenSeats,
}

public static class SearchSortInfo
{
    public static string Label(this SearchSort sort) => sort switch
    {
        SearchSort.Relevance => "Best Match",
        SearchSort.CourseNumber => "Course Number",
        SearchSort.Title => "Title",
        SearchSort.OpenSeats => "Most Open Seats",
        _ => sort.ToString(),
    };
}

public enum InstructionFilter
{
    Any,
    InPerson,
    Online,
}

public static class InstructionFilterInfo
{
    public static string Label(this InstructionFilter filter) => filter switch
    {
        InstructionFilter.Any => "Any",
        InstructionFilter.InPerson => "In Person",
        InstructionFilter.Online => "Online",
        _ => filter.ToString(),
    };
}

/// <summary>
/// Course-level filters apply to the course. The rest require at least one section
/// that satisfies all of them.
/// </summary>
public sealed class SearchFilters
{
    public HashSet<string> Subjects { get; set; } = [];
    public HashSet<NUPath> Nupaths { get; set; } = [];
    public HashSet<int> Levels { get; set; } = [];
    public double? MinCredits { get; set; }
    public double? MaxCredits { get; set; }
    public HashSet<string> Campuses { get; set; } = [];
    public HashSet<string> ClassTypes { get; set; } = [];
    public bool HonorsOnly { get; set; }
    public bool OpenSeatsOnly { get; set; }
    public InstructionFilter Instruction { get; set; } = InstructionFilter.Any;
    public DaySet? AllowedDays { get; set; }
    public int? StartsAfter { get; set; }
    public int? EndsBefore { get; set; }

    public bool IsEmpty => ActiveCount == 0;

    public int ActiveCount
    {
        get
        {
            var n = 0;
            if (Subjects.Count > 0) n++;
            if (Nupaths.Count > 0) n++;
            if (Levels.Count > 0) n++;
            if (MinCredits is not null || MaxCredits is not null) n++;
            if (Campuses.Count > 0) n++;
            if (ClassTypes.Count > 0) n++;
            if (HonorsOnly) n++;
            if (OpenSeatsOnly) n++;
            if (Instruction != InstructionFilter.Any) n++;
            if (AllowedDays is not null) n++;
            if (StartsAfter is not null || EndsBefore is not null) n++;
            return n;
        }
    }

    public bool HasSectionFilters =>
        Campuses.Count > 0 || ClassTypes.Count > 0 || HonorsOnly || OpenSeatsOnly || Instruction != InstructionFilter.Any
        || AllowedDays is not null || StartsAfter is not null || EndsBefore is not null;

    public bool Matches(Course course)
    {
        if (Subjects.Count > 0 && !Subjects.Contains(course.Subject)) return false;
        if (Nupaths.Count > 0)
        {
            var attrs = course.Attributes.ToHashSet(StringComparer.Ordinal);
            if (!Nupaths.All(path => attrs.Contains(path.AttributeCode()))) return false;
        }
        if (Levels.Count > 0)
        {
            if (course.Key.Level is not int level || !Levels.Contains(level)) return false;
        }
        if (MinCredits is double min && course.MaxCredits < min) return false;
        if (MaxCredits is double max && course.MinCredits > max) return false;
        return true;
    }

    public bool Matches(CourseSection section)
    {
        if (Campuses.Count > 0 && !Campuses.Contains(section.Campus)) return false;
        if (ClassTypes.Count > 0 && !ClassTypes.Contains(section.ClassType)) return false;
        if (HonorsOnly && !section.Honors) return false;
        if (OpenSeatsOnly && !section.IsOpen) return false;
        switch (Instruction)
        {
            case InstructionFilter.Online when !section.IsOnline:
            case InstructionFilter.InPerson when section.IsOnline:
                return false;
        }
        var weekly = section.WeeklyMeetings;
        if (AllowedDays is DaySet allowed)
        {
            foreach (var meeting in weekly)
                if (!meeting.Days.IsSubsetOf(allowed)) return false;
        }
        if (StartsAfter is int after)
        {
            foreach (var meeting in weekly)
                if ((meeting.Start ?? 0) < after) return false;
        }
        if (EndsBefore is int before)
        {
            foreach (var meeting in weekly)
                if ((meeting.End ?? 0) > before) return false;
        }
        return true;
    }
}

public sealed class SearchQuery
{
    public string Text { get; set; } = "";
    public SearchFilters Filters { get; set; } = new();
    public SearchSort Sort { get; set; } = SearchSort.Relevance;
}

public sealed class SearchResult
{
    public required Course Course { get; init; }
    public required CourseStats Stats { get; init; }
    public double Score { get; init; }
    public string? MatchNote { get; init; }
    public CourseKey Id => Course.Key;
}

public sealed class SearchResponse
{
    public List<SearchResult> Results { get; set; } = [];
    public CourseSection? CrnMatch { get; set; }
    public List<InstructorSummary> Instructors { get; set; } = [];
    public List<Subject> Subjects { get; set; } = [];
    public int TotalCount => Results.Count;
}

public sealed class SearchDocument
{
    public CourseKey Key { get; }
    public string SubjectLower { get; }
    public string NumberLower { get; }
    public string TitleNormalized { get; }
    public string[] TitleWords { get; }
    public string[] SubjectWords { get; }
    public string[] InstructorWords { get; }
    public string[] InstructorNames { get; }
    public HashSet<string> DescriptionWords { get; }
    public string[] TopicWords { get; }
    public HashSet<string> NupathShorts { get; }

    public SearchDocument(Course course, string subjectName, IReadOnlyList<string> instructorNames, IReadOnlyList<string> sectionTitles)
    {
        Key = course.Key;
        SubjectLower = course.Subject.ToLowerInvariant();
        NumberLower = course.Number.ToLowerInvariant();
        TitleNormalized = SearchText.Normalize(course.Title);
        TitleWords = SearchText.Words(course.Title);
        SubjectWords = SearchText.Words(subjectName);
        InstructorNames = instructorNames.ToArray();
        InstructorWords = instructorNames.SelectMany(SearchText.Words).ToArray();
        DescriptionWords = SearchText.Words(course.Description).Where(w => w.Length >= 3).ToHashSet(StringComparer.Ordinal);
        TopicWords = sectionTitles.SelectMany(SearchText.Words).ToArray();
        NupathShorts = course.Nupaths.Select(p => p.Short().ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
    }
}

public static class SearchText
{
    public static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "and", "of", "the", "to", "in", "for", "a", "an", "with", "on", "at", "intro", "introduction",
    };

    public static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["orgo"] = "organic chemistry", ["ochem"] = "organic chemistry", ["linalg"] = "linear algebra",
        ["diffeq"] = "differential equations", ["diffeqs"] = "differential equations", ["stats"] = "statistics",
        ["stat"] = "statistics", ["econ"] = "economics", ["psych"] = "psychology", ["chem"] = "chemistry",
        ["ml"] = "machine learning", ["ai"] = "artificial intelligence", ["hci"] = "human computer interaction",
        ["ood"] = "object oriented", ["algo"] = "algorithms", ["algos"] = "algorithms", ["swe"] = "software engineering",
        ["nlp"] = "natural language processing", ["db"] = "databases", ["dbs"] = "databases", ["fundies"] = "program design",
        ["compsci"] = "computer science", ["cs1"] = "program design implementation 1", ["discrete"] = "discrete",
        ["theory"] = "theory", ["calc"] = "calculus", ["bio"] = "biology", ["phys"] = "physics", ["intl"] = "international",
        ["mgmt"] = "management", ["acct"] = "accounting", ["mktg"] = "marketing", ["philo"] = "philosophy", ["poli"] = "political",
        ["polisci"] = "political science", ["comm"] = "communication", ["comms"] = "communication", ["lab"] = "lab",
    };

    public static string Normalize(string text)
    {
        var folded = Fold(text);
        var builder = new StringBuilder(folded.Length);
        var lastWasSpace = true;
        foreach (var ch in folded)
        {
            if (char.IsLetter(ch) || char.IsNumber(ch))
            {
                builder.Append(ch);
                lastWasSpace = false;
            }
            else if (ch == '&')
            {
                if (!lastWasSpace) builder.Append(' ');
                builder.Append("and ");
                lastWasSpace = true;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }
        return builder.ToString().Trim();
    }

    public static string ExpandAliases(string normalized, ISet<string> subjectCodes)
    {
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word =>
        {
            if (subjectCodes.Contains(word.ToUpperInvariant())) return word;
            return Aliases.TryGetValue(word, out var expanded) ? expanded : word;
        }));
    }

    public static string[] Words(string text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Damerau-free Levenshtein distance capped at <paramref name="limit"/> (returns limit + 1 when exceeded).</summary>
    public static int EditDistance(string a, string b, int limit)
    {
        if (a.Length == 0 || b.Length == 0) return Math.Max(a.Length, b.Length);
        if (Math.Abs(a.Length - b.Length) > limit) return limit + 1;
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            var rowMin = curr[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, curr[j]);
            }
            if (rowMin > limit) return limit + 1;
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    private static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }
}

public static class SearchEngine
{
    private static readonly StringComparer TitleOrder = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    public static SearchResponse Search(SearchQuery query, CatalogIndex index)
    {
        var response = new SearchResponse();
        var text = query.Text.Trim();
        var filters = query.Filters;

        bool Passes(Course course)
        {
            if (!filters.Matches(course)) return false;
            if (!filters.HasSectionFilters) return true;
            return index.Sections(course.Key).Any(filters.Matches);
        }

        CourseStats StatsFor(Course course)
        {
            if (!filters.HasSectionFilters) return index.Stats(course.Key);
            return CatalogIndex.MakeStats(index.Sections(course.Key).Where(filters.Matches).ToList());
        }

        if (text.Length == 5 && text.All(char.IsDigit) && index.Section(text) is CourseSection section)
            response.CrnMatch = section;

        if (text.Length == 0)
        {
            var all = new List<SearchResult>();
            foreach (var course in index.Courses)
            {
                if (!Passes(course)) continue;
                all.Add(new SearchResult { Course = course, Stats = StatsFor(course), Score = 0 });
            }
            response.Results = Sort(all, query.Sort == SearchSort.Relevance ? SearchSort.CourseNumber : query.Sort);
            return response;
        }

        var rawNormalized = SearchText.Normalize(text);
        var subjectCodes = index.SubjectsByCode.Keys.ToHashSet(StringComparer.Ordinal);
        var normalized = SearchText.ExpandAliases(rawNormalized, subjectCodes);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return response;

        response.Subjects = index.ActiveSubjects.Where(subject =>
        {
            var name = SearchText.Normalize(subject.Name);
            return subject.Code.ToLowerInvariant() == rawNormalized || name == rawNormalized || name == normalized
                || (rawNormalized.Length >= 4 && name.StartsWith(rawNormalized, StringComparison.Ordinal));
        }).ToList();

        var rawTokens = rawNormalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (rawTokens.Length > 0 && rawTokens.All(t => t.Length >= 2) && !rawTokens.All(t => t.All(char.IsDigit)))
        {
            response.Instructors = index.Instructors.Where(instructor =>
            {
                var words = SearchText.Words(instructor.DisplayName);
                return rawTokens.All(token => words.Any(word => word.StartsWith(token, StringComparison.Ordinal)));
            }).Take(8).ToList();
        }

        var code = ParseCourseCode(rawNormalized, index.SubjectsByCode);
        var scored = new List<SearchResult>();
        foreach (var doc in index.SearchDocuments)
        {
            if (index.Course(doc.Key) is not Course course) continue;
            double score;
            string? note = null;
            if (code is { } parsed)
            {
                if (doc.SubjectLower != parsed.Subject) continue;
                if (parsed.Number.Length == 0) score = 600 - doc.Key.NumericValue / 100.0;
                else if (doc.NumberLower == parsed.Number) score = 2000;
                else if (doc.NumberLower.StartsWith(parsed.Number, StringComparison.Ordinal))
                    score = 1200 - (doc.NumberLower.Length - parsed.Number.Length);
                else continue;
            }
            else
            {
                var match = ScoreTokens(tokens, normalized, doc);
                if (match is null) continue;
                score = match.Value.Score;
                note = match.Value.Note;
            }
            if (!Passes(course)) continue;
            scored.Add(new SearchResult { Course = course, Stats = StatsFor(course), Score = score, MatchNote = note });
        }
        response.Results = Sort(scored, query.Sort);
        return response;
    }

    public static SearchResponse Search(string text, CatalogIndex index, SearchFilters? filters = null, SearchSort sort = SearchSort.Relevance) =>
        Search(new SearchQuery { Text = text, Filters = filters ?? new SearchFilters(), Sort = sort }, index);

    private static (string Subject, string Number)? ParseCourseCode(string normalized, IReadOnlyDictionary<string, Subject> subjects)
    {
        var compact = normalized.Replace(" ", "", StringComparison.Ordinal);
        var letters = 0;
        while (letters < compact.Length && char.IsLetter(compact[letters])) letters++;
        if (letters is < 2 or > 5) return null;
        var subject = compact[..letters].ToUpperInvariant();
        if (!subjects.ContainsKey(subject)) return null;
        var rest = compact[letters..];
        if (rest.Length > 4 || !rest.All(ch => char.IsNumber(ch) || char.IsLetter(ch))) return null;
        if (rest.Length == 0) return normalized == compact[..letters] ? (compact[..letters], "") : null;
        if (!char.IsNumber(rest[0])) return null;
        return (compact[..letters], rest);
    }

    private static (double Score, string? Note)? ScoreTokens(string[] tokens, string phrase, SearchDocument doc)
    {
        double total = 0;
        var titleHits = 0;
        string? note = null;
        var meaningful = tokens.Where(t => !SearchText.StopWords.Contains(t)).ToArray();
        var wantsIntro = tokens.Any(t => t is "intro" or "introduction" or "introductory");
        foreach (var token in meaningful.Length == 0 ? tokens : meaningful)
        {
            double best = 0;
            string? bestNote = null;
            if (token == doc.SubjectLower) best = Math.Max(best, 50);
            if (token == doc.NumberLower) best = Math.Max(best, 60);
            else if (token.All(char.IsNumber) && doc.NumberLower.StartsWith(token, StringComparison.Ordinal)) best = Math.Max(best, 30);
            if (doc.TitleWords.Contains(token))
            {
                best = Math.Max(best, 40);
                titleHits++;
            }
            else if (token.Length >= 2 && doc.TitleWords.Any(word => word.StartsWith(token, StringComparison.Ordinal)))
            {
                best = Math.Max(best, 28);
                titleHits++;
            }
            if (doc.SubjectWords.Any(word => word.StartsWith(token, StringComparison.Ordinal)))
                best = Math.Max(best, token.Length >= 3 ? 20 : 8);
            if (doc.NupathShorts.Contains(token)) best = Math.Max(best, 15);
            if (best == 0 && token.Length >= 2 && doc.TopicWords.Any(word => word.StartsWith(token, StringComparison.Ordinal)))
            {
                best = 24;
                bestNote = "Topic match";
            }
            if (best == 0 && token.Length >= 2 && doc.InstructorWords.Any(word => word.StartsWith(token, StringComparison.Ordinal)))
            {
                best = 22;
                var name = doc.InstructorNames.FirstOrDefault(n => SearchText.Words(n).Any(w => w.StartsWith(token, StringComparison.Ordinal)));
                if (name is not null) bestNote = $"Taught by {name}";
            }
            if (best == 0 && token.Length >= 3 && (doc.DescriptionWords.Contains(token) || (token.Length >= 5 && doc.DescriptionWords.Any(w => w.StartsWith(token, StringComparison.Ordinal)))))
            {
                best = 6;
                bestNote = $"Description mentions \u201C{token}\u201D";
            }
            if (best == 0 && token.Length >= 4)
            {
                var limit = token.Length >= 7 ? 2 : 1;
                if (doc.TitleWords.Any(word => SearchText.EditDistance(word, token, limit) <= limit
                    || (word.Length > token.Length && SearchText.EditDistance(word[..token.Length], token, 1) <= 1)))
                {
                    best = 12;
                    titleHits++;
                }
            }
            if (best == 0) return null;
            total += best;
            note ??= bestNote;
        }

        if (doc.TitleNormalized == phrase) total += 150;
        else if (doc.TitleNormalized.StartsWith(phrase, StringComparison.Ordinal)) total += 80;
        else if (phrase.Length >= 4 && doc.TitleNormalized.Contains(phrase, StringComparison.Ordinal)) total += 50;
        if (titleHits >= Math.Max(1, meaningful.Length))
        {
            total += 25;
            note = null;
        }
        if (wantsIntro && doc.Key.Level is int level)
            total += level == 1 ? 15 : level == 2 ? 6 : 0;
        total -= doc.Key.NumericValue / 10_000.0;
        return (total, note);
    }

    public static List<SearchResult> Sort(List<SearchResult> results, SearchSort sort)
    {
        results.Sort((a, b) => sort switch
        {
            SearchSort.CourseNumber => a.Course.Key.CompareTo(b.Course.Key),
            SearchSort.Title => TitleOrder.Compare(a.Course.Title, b.Course.Title),
            SearchSort.OpenSeats => b.Stats.SeatsRemaining != a.Stats.SeatsRemaining
                ? b.Stats.SeatsRemaining.CompareTo(a.Stats.SeatsRemaining)
                : a.Course.Key.CompareTo(b.Course.Key),
            _ => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Course.Key.CompareTo(b.Course.Key),
        });
        return results;
    }
}
