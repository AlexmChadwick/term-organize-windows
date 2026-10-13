// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Lays a program's requirements out the way Northeastern's public academic catalog lists them.
// Term Organize is not affiliated with, endorsed by, or sponsored by Northeastern University.

using System.Text;

namespace TermOrganize.Core;

/// <summary>Which requirements the page shows.</summary>
public enum DegreeRequirementsFilter
{
    All,
    Remaining,
    Done,
}

public static class DegreeRequirementsFilterText
{
    public static string Title(this DegreeRequirementsFilter filter) => filter switch
    {
        DegreeRequirementsFilter.All => "All",
        DegreeRequirementsFilter.Remaining => "Remaining",
        DegreeRequirementsFilter.Done => "Done",
        _ => "All",
    };

    /// <summary>Remaining: not complete yet. Done: complete or waived.</summary>
    public static bool Includes(this DegreeRequirementsFilter filter, DegreeAudit.Status status) => filter switch
    {
        DegreeRequirementsFilter.All => true,
        DegreeRequirementsFilter.Remaining => status <= DegreeAudit.Status.Planned,
        DegreeRequirementsFilter.Done => status >= DegreeAudit.Status.Complete,
        _ => true,
    };
}

/// <summary>
/// One item of a requirement's list, in catalog order: an option of a "complete N of the
/// following" list, or one alternative of a required course.
/// </summary>
public sealed class DegreeListEntry
{
    public DegreeListEntry(int id, IEnumerable<IEnumerable<CourseKey>> alternatives, string? category = null)
    {
        Id = id;
        Alternatives = alternatives.Select(alternative => alternative.ToList()).ToList();
        Category = category;
    }

    /// <summary>Position in the catalog's list.</summary>
    public int Id { get; }
    /// <summary>Courses taken together, for each way to satisfy the item.</summary>
    public List<List<CourseKey>> Alternatives { get; }
    /// <summary>The category subheader that starts here (<c>Biology</c>).</summary>
    public string? Category { get; }

    public List<CourseKey> Courses => Alternatives.SelectMany(alternative => alternative).ToList();
}

/// <summary>
/// How a requirement is laid out like the academic catalog's page: which items of its list show,
/// where its category subheaders go, and how each course row stands.
/// </summary>
public static class DegreeRequirementsListing
{
    /// <summary>Lists longer than this show only the options that matter until expanded.</summary>
    public const int CollapseThreshold = 6;
    /// <summary>How many other options a collapsed list of an open requirement still shows.</summary>
    public const int ExtraOptionsWhenCollapsed = 3;

    /// <summary>
    /// Lists of options, hour requirements, petitions and waivers get a heading row; required
    /// courses are just their course rows, as in the catalog.
    /// </summary>
    public static bool HasHeaderRow(DegreeAudit.RuleResult result) => result.EffectiveKind switch
    {
        null or RequirementKind.Choose or RequirementKind.GeneralElectives => true,
        RequirementKind.Required => IsReplaced(result),
        _ => true,
    };

    /// <summary>The requirement was replaced by a petition.</summary>
    public static bool IsReplaced(DegreeAudit.RuleResult result) =>
        result.Substitutions.Any(substitution => substitution.Change is SubstitutionChange.ReplaceChange);

    /// <summary>Whether a counted course already has a row: its own, or the row of the listed course it counts as.</summary>
    public static bool IsShown(DegreeAudit.CourseUse use, ISet<CourseKey> listed) =>
        listed.Contains(use.Key) || (use.Satisfies is CourseKey named && listed.Contains(named));

    /// <summary>
    /// The items of a list to show. A long list shows the options in the plan (or counted, or with
    /// a suggested equivalence) and, while the requirement is open, a few more, until expanded.
    /// </summary>
    public static (List<DegreeListEntry> Shown, bool IsLong, bool ShowsCategories) Visible(
        IReadOnlyList<DegreeListEntry> entries,
        DegreeAudit.RuleResult result,
        bool isExpanded,
        DegreeRequirementsLookup lookup,
        DegreeEquivalencePrompts prompts)
    {
        var isLong = entries.Count > CollapseThreshold;
        var shown = isLong && !isExpanded
            ? Collapsed(entries, result.Status.IsSatisfied() ? 0 : ExtraOptionsWhenCollapsed, entry => IsRelevant(entry, result, lookup, prompts))
            : entries.ToList();
        return (shown, isLong, !isLong || isExpanded);
    }

    /// <summary>An entry that a collapsed list keeps: a course of it is in the plan, counts here, or has a suggested equivalence.</summary>
    public static bool IsRelevant(
        DegreeListEntry entry,
        DegreeAudit.RuleResult result,
        DegreeRequirementsLookup lookup,
        DegreeEquivalencePrompts prompts) =>
        entry.Courses.Any(key =>
        {
            var info = lookup.RowInfo(key, result);
            return info.IsCountedHere || info.Status is not DegreeCourseStatus.NotInPlan
                || (!result.Status.IsSatisfied() && prompts.ForListed(key) is not null);
        });

    /// <summary>The relevant entries and the first <paramref name="extra"/> others, in catalog order.</summary>
    public static List<DegreeListEntry> Collapsed(IReadOnlyList<DegreeListEntry> entries, int extra, Func<DegreeListEntry, bool> isRelevant)
    {
        var others = 0;
        var shown = new List<DegreeListEntry>();
        foreach (var entry in entries)
        {
            if (isRelevant(entry))
            {
                shown.Add(entry);
                continue;
            }
            if (others >= extra) continue;
            others++;
            shown.Add(entry);
        }
        return shown;
    }

    /// <summary>
    /// A "complete N of the following" list's options, each category subheader before the first
    /// option of its category.
    /// </summary>
    public static List<DegreeListEntry> Entries(IReadOnlyList<CourseOption> options, RequirementRule rule, Func<string, string?> subjectName)
    {
        Dictionary<int, string> starts;
        if (rule.Kind is RequirementKind.Choose choose && choose.Options.SequenceEqual(options)
            && rule.OptionCategories is { } categories && categories.Count == options.Count)
        {
            starts = [];
            for (var index = 0; index < categories.Count; index++)
            {
                var category = categories[index];
                if (category is null || (index > 0 && categories[index - 1] == category)) continue;
                starts[index] = category;
            }
        }
        else
        {
            starts = CategoryStarts(options, rule.DegreeCategories(), subjectName);
        }
        return options.Select((option, index) =>
            new DegreeListEntry(index, option.Alternatives, starts.TryGetValue(index, out var category) ? category : null)).ToList();
    }

    /// <summary>A required course's alternatives, one entry each.</summary>
    public static List<DegreeListEntry> Entries(CourseOption option) =>
        option.Alternatives.Select((alternative, index) => new DegreeListEntry(index, [alternative])).ToList();

    /// <summary>
    /// Where each category subheader goes, by option index. Empty when the categories can't be placed.
    /// </summary>
    public static Dictionary<int, string> CategoryStarts(
        IReadOnlyList<CourseOption> options,
        IReadOnlyList<string> categories,
        Func<string, string?> subjectName)
    {
        if (categories.Count == 0 || options.Count == 0) return [];
        var subjects = options.Select(option => option.AllCourses.Count == 0 ? "" : option.AllCourses[0].Subject).ToList();
        var runs = new List<int>();
        for (var index = 0; index < subjects.Count; index++)
        {
            if (index == 0 || subjects[index] != subjects[index - 1]) runs.Add(index);
        }
        if (runs.Count == categories.Count)
            return runs.Zip(categories, (run, category) => (run, category)).ToDictionary(pair => pair.run, pair => pair.category);
        var starts = new Dictionary<int, string> { [0] = categories[0] };
        var current = 0;
        foreach (var start in runs.Skip(1))
        {
            if (current + 1 >= categories.Count) break;
            var name = subjectName(subjects[start]) ?? subjects[start];
            if (!SharesWord(name, categories[current + 1])) continue;
            current++;
            starts[start] = categories[current];
        }
        return current == categories.Count - 1 ? starts : [];
    }

    /// <summary>Whether two names share a word (or one's start: <c>BIOL</c> and Biology), ignoring short ones.</summary>
    public static bool SharesWord(string a, string b)
    {
        static List<string> Words(string text)
        {
            var words = new List<string>();
            var current = new StringBuilder();
            foreach (var ch in text.ToLowerInvariant())
            {
                if (char.IsLetter(ch))
                {
                    current.Append(ch);
                    continue;
                }
                if (current.Length >= 4) words.Add(current.ToString());
                current.Clear();
            }
            if (current.Length >= 4) words.Add(current.ToString());
            return words;
        }

        var right = Words(b);
        return Words(a).Any(left => right.Any(word =>
            left.StartsWith(word, StringComparison.Ordinal) || word.StartsWith(left, StringComparison.Ordinal)));
    }

    /// <summary>The options and ranges a rule lists in the catalog.</summary>
    public static (List<CourseOption> Options, List<CourseRange> Ranges) Listing(RequirementRule rule) => rule.Kind switch
    {
        RequirementKind.Required required => ([required.Option], []),
        RequirementKind.Choose choose => (choose.Options.ToList(), choose.Ranges.ToList()),
        _ => ([], []),
    };

    /// <summary>How a course row of a requirement stands.</summary>
    public sealed class CourseRow
    {
        public required CourseKey Key { get; init; }
        public required DegreeCourseRowInfo Info { get; init; }
        /// <summary>The caption to show: the substitution's note for a course counted by substitution, else the lookup's.</summary>
        public string? Caption { get; init; }
        public bool IsWaived { get; init; }
        /// <summary>The requirement is still open, so the course can be added, or another one counted.</summary>
        public bool CanAct { get; init; }
        /// <summary>Open and the plan doesn't have the course.</summary>
        public bool IsMissing { get; init; }
    }

    /// <param name="counted">A course the requirement counts that it doesn't list.</param>
    public static CourseRow Row(
        CourseKey key,
        DegreeAudit.RuleResult result,
        DegreeRequirementsLookup lookup,
        DegreeAudit.CourseUse? counted = null)
    {
        var info = lookup.RowInfo(key, result, counted);
        var name = lookup.Requirements.DegreeShortName(result.Rule.Id) ?? result.Rule.DisplayLabel;
        string? substitutionNote = null;
        if (info.BySubstitution)
        {
            var countedKey = counted?.Key ?? key;
            var caption = result.Substitutions
                .FirstOrDefault(substitution => substitution.Change is SubstitutionChange.CountCourseChange count && count.Course == countedKey)
                is RequirementSubstitution found
                ? DegreeRequirementText.SubstitutionCaption(found)
                : null;
            var parts = new List<string> { $"Counts for {name} by substitution" };
            if (caption is not null) parts.Add(caption);
            substitutionNote = string.Join(" \u00b7 ", parts);
        }
        var canAct = !result.Status.IsSatisfied();
        return new CourseRow
        {
            Key = key,
            Info = info,
            Caption = info.BySubstitution ? substitutionNote : info.Caption,
            IsWaived = result.Status == DegreeAudit.Status.Waived,
            CanAct = canAct,
            IsMissing = canAct && info.Status is DegreeCourseStatus.NotInPlan,
        };
    }
}
