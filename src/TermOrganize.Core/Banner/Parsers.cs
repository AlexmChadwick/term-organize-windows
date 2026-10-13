// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Portions of this file are derived from SearchNEU
// (https://github.com/sandboxnu/searchneu), packages/scraper/src/generate/steps/reqs.ts
// and courseNames.ts, Copyright (C) Sandbox at Northeastern University and the
// SearchNEU contributors, licensed under the GNU Affero General Public License v3.
// Modified 2026-10-13: ported from Swift to C#, adapted to the app's Requisite
// model, and made tolerant of unknown subjects and score-less tests.

using System.Globalization;
using System.Text.RegularExpressions;

namespace TermOrganize.Core;

public static partial class BannerParsers
{
    private sealed class ConditionNode
    {
        public bool IsAnd;
        public List<Item> Items { get; } = [];

        public abstract class Item
        {
            public sealed class Leaf : Item
            {
                public Requisite Requisite { get; }
                public Leaf(Requisite requisite) => Requisite = requisite;
            }

            public sealed class Node : Item
            {
                public ConditionNode Condition { get; }
                public Node(ConditionNode condition) => Condition = condition;
            }
        }

        public Requisite? Build()
        {
            var built = new List<Requisite>();
            foreach (var item in Items)
            {
                switch (item)
                {
                    case Item.Leaf leaf:
                        built.Add(leaf.Requisite);
                        break;
                    case Item.Node node:
                        var child = node.Condition.Build();
                        if (child is not null) built.Add(child);
                        break;
                }
            }
            if (built.Count == 0) return null;
            return IsAnd ? Requisite.And(built) : Requisite.Or(built);
        }
    }

    /// <summary>
    /// Parses Banner's prerequisite table. Columns are
    /// And/Or | ( | Test | Score | Subject | Course Number | Level | Grade | ).
    /// Parentheses open and close nested conditions; the And/Or column sets the
    /// type of the condition currently being built (port of SearchNEU's parsePrereqs).
    /// </summary>
    public static Requisite? Prerequisites(string html, SubjectLookup subjects)
    {
        var rows = HtmlText.TableRows(html);
        if (rows is null || rows.Count == 0) return null;
        var root = new ConditionNode();
        var stack = new List<ConditionNode> { root };
        var current = root;
        foreach (var cells in rows)
        {
            if (cells.Count < 6) continue;
            string Cell(int i) => i < cells.Count ? cells[i].Trim() : "";
            var andOr = Cell(0).ToLowerInvariant();
            if (andOr == "and") current.IsAnd = true;
            else if (andOr == "or") current.IsAnd = false;
            if (Cell(1).Length > 0)
            {
                var child = new ConditionNode();
                current.Items.Add(new ConditionNode.Item.Node(child));
                stack.Add(child);
                current = child;
            }
            var testName = Cell(2);
            if (testName.Length > 0)
            {
                double? score = double.TryParse(Cell(3), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
                current.Items.Add(new ConditionNode.Item.Leaf(Requisite.Test(testName, score)));
            }
            var subjectName = Cell(4);
            var number = Cell(5);
            if (subjectName.Length > 0 && number.Length > 0)
                current.Items.Add(new ConditionNode.Item.Leaf(Requisite.Course(new CourseKey(subjects.Code(subjectName), number))));
            if (Cell(8).Length > 0 && stack.Count > 1)
            {
                stack.RemoveAt(stack.Count - 1);
                current = stack[^1];
            }
        }
        return root.Build()?.Simplified();
    }

    /// <summary>
    /// Parses Banner's corequisite table (Subject | Course Number | Title).
    /// Multiple corequisites are all required (port of SearchNEU's parseCoreqs).
    /// </summary>
    public static Requisite? Corequisites(string html, SubjectLookup subjects)
    {
        var rows = HtmlText.TableRows(html);
        if (rows is null) return null;
        var items = new List<Requisite>();
        foreach (var cells in rows)
        {
            string subjectName, number;
            if (cells.Count == 3)
            {
                subjectName = cells[0];
                number = cells[1];
            }
            else if (cells.Count >= 4)
            {
                subjectName = cells[1];
                number = cells[2];
            }
            else continue;
            if (subjectName.Length == 0 || number.Length == 0) continue;
            items.Add(Requisite.Course(new CourseKey(subjects.Code(subjectName), number)));
        }
        return items.Count switch
        {
            0 => null,
            1 => items[0],
            _ => Requisite.And(items),
        };
    }

    /// <summary>Extracts the plain-text course description.</summary>
    public static string Description(string html)
    {
        var text = HtmlText.PlainText(html);
        var lowered = text.ToLowerInvariant();
        if (lowered.StartsWith("no course description", StringComparison.Ordinal) || lowered.Contains("no information available", StringComparison.Ordinal))
            return "";
        return text;
    }

    public sealed class CatalogDetails
    {
        public string? Title { get; init; }
        public string? College { get; init; }
        public string? Department { get; init; }
        /// <summary>Credit hours: 4 is 4..4, 1 TO 4 is 1..4.</summary>
        public double? MinCredits { get; init; }
        public double? MaxCredits { get; init; }
    }

    /// <summary>
    /// Parses the catalog-details fragment of a section or a course: title, college, department and credit hours.
    /// </summary>
    public static CatalogDetails CatalogDetailsFrom(string html)
    {
        var lines = HtmlText.PlainText(html)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        string? Value(string label)
        {
            var i = lines.FindIndex(line => line.StartsWith(label, StringComparison.Ordinal));
            if (i < 0) return null;
            var inline = lines[i][label.Length..].Trim();
            if (inline.Length > 0) return inline;
            if (i + 1 >= lines.Count || lines[i + 1].EndsWith(':')) return null;
            return lines[i + 1];
        }

        var credits = Value("Credit Hours:") is string hours ? CreditRange(hours) : null;
        return new CatalogDetails
        {
            Title = Value("Title:"),
            College = ExpandCollegeName(StripTrailingCode(Value("College:"))),
            Department = ExpandCollegeName(StripTrailingCode(Value("Department:"))),
            MinCredits = credits?.Low,
            MaxCredits = credits?.High,
        };
    }

    /// <summary>Reads credit hours written as 4, 4.000, 1 TO 4 or 1.000 TO 4.000.</summary>
    public static (double Low, double High)? CreditRange(string text)
    {
        var numbers = CreditNumberPattern().Matches(text)
            .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToList();
        if (numbers.Count == 0) return null;
        var second = numbers.Count > 1 ? numbers[1] : numbers[0];
        return (Math.Min(numbers[0], second), Math.Max(numbers[0], second));
    }

    /// <summary>Expands Banner's abbreviated college names for display.</summary>
    public static string? ExpandCollegeName(string? name)
    {
        if (name is null) return null;
        var output = $" {name} ";
        (string From, string To)[] replacements =
        [
            (" Coll of ", " College of "), (" Col of ", " College of "), (" Coll ", " College "),
            (" Comp Sciences ", " Computer Sciences "), (" Hlth ", " Health "), (" Sci ", " Science "),
            (" Engr ", " Engineering "), (" Prof ", " Professional "), (" School Business ", " School of Business "),
            (" Soc Sci ", " Social Sciences "), (" Humanities ", " Humanities "),
        ];
        foreach (var (from, to) in replacements)
            output = output.Replace(from, to, StringComparison.Ordinal);
        return output.Trim();
    }

    /// <summary>
    /// Whether the course codes Banner's catalog typeahead returned (for example CS3386) include the course.
    /// The endpoint matches by prefix, so the code has to match exactly.
    /// </summary>
    public static bool CatalogCodes(IEnumerable<BannerCodeDescription> items, CourseKey course)
    {
        var wanted = NormalizedCode($"{course.Subject}{course.Number}");
        return items.Any(item => NormalizedCode(item.Code) == wanted);
    }

    /// <summary>
    /// Courses credit can't be earned for as well as this one. Banner answers
    /// "No Mutual Exclusion information available." when a course has none.
    /// </summary>
    public static List<CourseKey> MutualExclusions(string html, SubjectLookup subjects)
    {
        var text = HtmlText.PlainText(html);
        if (text.Contains("no mutual exclusion", StringComparison.OrdinalIgnoreCase)) return [];
        var keys = new List<CourseKey>();
        void Add(CourseKey key)
        {
            if (!keys.Contains(key)) keys.Add(key);
        }
        foreach (var raw in HtmlText.TableRows(html) ?? [])
        {
            var cells = raw.Select(cell => cell.Trim()).ToList();
            for (var i = 0; i < cells.Count - 1; i++)
            {
                if (!IsSubjectText(cells[i]) || !IsCourseNumber(cells[i + 1])) continue;
                Add(new CourseKey(subjects.Code(cells[i]), cells[i + 1]));
                break;
            }
        }
        if (keys.Count == 0)
        {
            foreach (Match match in CourseCodePattern().Matches(text))
                Add(new CourseKey(match.Groups[1].Value, match.Groups[2].Value));
        }
        return keys;
    }

    /// <summary>A section cross-listed with another (searchResults/getXlstSections).</summary>
    public sealed class CrossListedSection : IEquatable<CrossListedSection>
    {
        public required string Crn { get; init; }
        public required CourseKey Course { get; init; }
        public required string Title { get; init; }
        public required string Section { get; init; }

        public bool Equals(CrossListedSection? other) =>
            other is not null && Crn == other.Crn && Course == other.Course && Title == other.Title && Section == other.Section;
        public override bool Equals(object? obj) => Equals(obj as CrossListedSection);
        public override int GetHashCode() => HashCode.Combine(Crn, Course, Title, Section);
    }

    /// <summary>
    /// Sections cross-listed with a section. The table lists the other sections:
    /// CRN | Subject | Course Number | Title | Section.
    /// </summary>
    public static List<CrossListedSection> CrossListedSections(string html, SubjectLookup subjects)
    {
        var rows = HtmlText.TableRows(html);
        if (rows is null) return [];
        var result = new List<CrossListedSection>();
        foreach (var cells in rows)
        {
            if (cells.Count < 4) continue;
            var crn = cells[0].Trim();
            var subject = cells[1].Trim();
            var number = cells[2].Trim();
            if (crn.Length == 0 || !crn.All(char.IsDigit) || subject.Length == 0 || number.Length == 0) continue;
            result.Add(new CrossListedSection
            {
                Crn = crn,
                Course = new CourseKey(subjects.Code(subject), number),
                Title = cells[3].Trim(),
                Section = cells.Count > 4 ? cells[4].Trim() : "",
            });
        }
        return result;
    }

    private static string NormalizedCode(string text) =>
        string.Concat(text.Where(ch => !char.IsWhiteSpace(ch))).ToUpperInvariant();

    /// <summary>Banner appends the org code, e.g. "Computer Science CS"; drop it.</summary>
    private static string? StripTrailingCode(string? text)
    {
        if (text is null) return null;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 1)
        {
            var last = parts[^1];
            if (last.Length <= 4 && last == last.ToUpperInvariant() && last.All(ch => char.IsLetter(ch) || char.IsNumber(ch)))
                parts.RemoveAt(parts.Count - 1);
        }
        return string.Join(' ', parts);
    }

    private static bool IsSubjectText(string text) => text.Any(char.IsLetter) && !IsCourseNumber(text);

    /// <summary>2100, 201M, 21EM: three to five letters or digits, starting with a digit.</summary>
    private static bool IsCourseNumber(string text) =>
        text.Length is >= 3 and <= 5 && text.Length > 0 && char.IsDigit(text[0]) && text.All(ch => char.IsLetter(ch) || char.IsNumber(ch));

    [GeneratedRegex(@"\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex CreditNumberPattern();

    [GeneratedRegex(@"\b([A-Z]{2,6})\s?(\d{3,4}[A-Z]?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex CourseCodePattern();
}
