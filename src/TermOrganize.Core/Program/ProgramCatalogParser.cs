// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Reads Northeastern's public academic catalog (catalog.northeastern.edu, a CourseLeaf site):
// which editions exist, the degree programs of an edition, and a program's requirements.
// Term Organize is not affiliated with, endorsed by, or sponsored by Northeastern University.
// Nothing here depends on a particular year. Requirement tables have had the same markup
// in every edition online (2016–2017 on).

using System.Globalization;
using System.Text.RegularExpressions;

namespace TermOrganize.Core;

/// <summary>
/// Reads Northeastern's academic catalog: editions, the degree programs of an edition, and a program's requirements.
/// </summary>
public static partial class ProgramCatalogParser
{
    /// <summary>The edition the site serves at its root, from any page's header.</summary>
    public static CatalogYear? CurrentEdition(string html)
    {
        var text = HtmlText.DecodeEntities(html);
        foreach (var pattern in CurrentEditionPatterns())
        {
            var match = pattern.Match(text);
            if (match.Success && CatalogYear.Parse(match.Value) is CatalogYear year) return year;
        }
        return null;
    }

    /// <summary>Archived editions linked from a page, newest first.</summary>
    public static List<CatalogYear> ArchivedEditions(string html)
    {
        var years = new HashSet<CatalogYear>();
        foreach (Match match in ArchiveYearPattern().Matches(html))
        {
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var start)) continue;
            if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var end)) continue;
            if (end != start + 1) continue;
            years.Add(new CatalogYear(start));
        }
        return years.OrderByDescending(year => year.StartYear).ToList();
    }

    /// <summary>Degree programs from an edition's A–Z index. The first link for a path wins.</summary>
    public static List<CatalogProgram> Programs(string html)
    {
        var byPath = new Dictionary<string, CatalogProgram>(StringComparer.Ordinal);
        foreach (var (href, label) in Links(html))
        {
            var path = EditionRelativePath(href);
            if (!(path.StartsWith("undergraduate/", StringComparison.Ordinal) || path.StartsWith("graduate/", StringComparison.Ordinal))
                || !path.EndsWith('/')) continue;
            if (byPath.ContainsKey(path)) continue;
            var program = new CatalogProgram(HtmlText.PlainText(label), path);
            if (program.Degree is not string degree || degree.Length == 0 || !char.IsUpper(degree[0])) continue;
            if (degree.Count(char.IsUpper) < 2) continue;
            byPath[path] = program;
        }
        return byPath.Values.OrderBy(program => program.Title, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// <c>/archive/2023-2024/undergraduate/x/</c> or a site URL → <c>undergraduate/x/</c>.
    /// </summary>
    public static string EditionRelativePath(string href)
    {
        var path = href;
        if (Uri.TryCreate(href, UriKind.Absolute, out var url) && !string.IsNullOrEmpty(url.Host))
        {
            path = url.AbsolutePath;
            if (href.EndsWith('/') && !path.EndsWith('/')) path += "/";
        }
        path = ArchivePrefixPattern().Replace(path, "");
        if (path.StartsWith('/')) path = path[1..];
        return path;
    }

    /// <summary>The requirements on a program page; null when the page has no requirement tables.</summary>
    public static ProgramRequirements? Requirements(string html, CatalogYear year, CatalogProgram program, DateTimeOffset? fetchedAt = null)
    {
        var builder = new RequirementsBuilder();
        foreach (var token in Tokens(RequirementsContainer(html)))
        {
            switch (token)
            {
                case HeadingToken heading: builder.StartSection(heading.Text); break;
                case ParagraphToken paragraph: builder.AddParagraph(paragraph.Text); break;
                case TableToken table: builder.AddTable(table.Html); break;
            }
        }
        var sections = builder.Finish();
        if (!sections.Any(section => section.Groups.Count > 0)) return null;
        return new ProgramRequirements
        {
            Year = year,
            Program = program,
            Sections = sections,
            TotalHours = builder.TotalHours,
            FetchedAt = fetchedAt ?? DateTimeOffset.UtcNow,
            CourseTitles = builder.CourseTitles,
        };
    }

    /// <summary>The edition-relative path of the universitywide requirements page a program page links, if any.</summary>
    public static string? UniversitywideLink(string html)
    {
        foreach (var (href, _) in Links(html))
        {
            if (!UniversitywideHrefPattern().IsMatch(href)) continue;
            var hash = href.IndexOf('#');
            var path = EditionRelativePath(hash >= 0 ? href[..hash] : href);
            if (path.Length == 0) continue;
            return path.EndsWith('/') ? path : path + "/";
        }
        return null;
    }

    /// <summary>
    /// The minimums on a Universitywide Requirements page. Null when the page states none of them.
    /// </summary>
    public static UniversitywideRequirements? UniversitywideRequirements(string html, DateTimeOffset? fetchedAt = null)
    {
        var text = HtmlText.PlainText(RequirementsContainer(html));
        var requirements = new UniversitywideRequirements
        {
            TotalHours = FirstNumber(MinimumTotalHoursPattern(), text),
            Gpa = FirstNumber(MinimumGpaPattern(), text),
            ResidencyHours = FirstNumber(MinimumResidencyPattern(), text),
            RequiresExperientialLearning = ExperientialPattern().IsMatch(text),
            FetchedAt = fetchedAt ?? DateTimeOffset.UtcNow,
        };
        if (requirements.TotalHours is null && requirements.Gpa is null && requirements.ResidencyHours is null
            && !requirements.RequiresExperientialLearning) return null;
        return requirements;
    }

    /// <summary>The Program Requirements tab (pages without tabs: the main text).</summary>
    public static string RequirementsContainer(string html)
    {
        var start = RequirementsContainerPattern().Match(html);
        if (!start.Success) start = TextContainerPattern().Match(html);
        if (!start.Success) return html;
        var rest = html[(start.Index + start.Length)..];
        var next = NextContainerPattern().Match(rest);
        if (next.Success) return rest[..next.Index];
        var footer = FooterPattern().Match(rest);
        if (footer.Success) return rest[..footer.Index];
        return rest;
    }

    /// <summary>One row of a course-list table.</summary>
    public sealed class Row
    {
        public enum Kind { Skip, AreaHeader, Subheader, Comment, Courses }

        public Kind RowKind { get; init; }
        public string Text { get; init; } = "";
        public List<List<CourseKey>> Sets { get; init; } = [];
        public bool IsAlternative { get; init; }
        public double? Hours { get; init; }
        public List<string> Titles { get; init; } = [];
    }

    public static List<Row> Rows(string tableHtml)
    {
        var rows = new List<Row>();
        foreach (Match match in RowPattern().Matches(tableHtml))
            rows.Add(ParseRow(match.Groups[1].Value, match.Groups[2].Value));
        return rows;
    }

    /// <summary><c>CS 1800 and CS 1802</c> → one set; <c>CS 4500 or CS 4530</c> → two.</summary>
    public static List<List<CourseKey>> CourseSets(string text)
    {
        var cleaned = text.Replace('\u00A0', ' ');
        if (cleaned.StartsWith("or ", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned[3..];
        var sets = new List<List<CourseKey>>();
        foreach (var part in cleaned.Split(" or ", StringSplitOptions.None))
        {
            var keys = CourseKeys(part);
            if (keys.Count > 0) sets.Add(keys);
        }
        return sets;
    }

    public static List<CourseKey> CourseKeys(string text)
    {
        var keys = new List<CourseKey>();
        foreach (Match match in CourseKeyPattern().Matches(text))
            keys.Add(new CourseKey(match.Groups[1].Value, match.Groups[2].Value));
        return keys;
    }

    /// <summary><c>4</c> → 4, <c>8-9</c> → 8, blank → null.</summary>
    public static double? HoursValue(string text)
    {
        var match = NumberPattern().Match(text);
        if (!match.Success) return null;
        return double.Parse(match.Value, CultureInfo.InvariantCulture);
    }

    /// <summary><c>CS 2500 or higher, except CS 5010</c>, <c>DS 2500 to DS 4999 but not DS 4900</c>.</summary>
    public static CourseRange? CourseRange(string text)
    {
        var cleaned = text.Replace('\u00A0', ' ').Trim();
        var higher = HigherRangePattern().Match(cleaned);
        if (higher.Success && int.TryParse(higher.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var lowest))
        {
            var except = higher.Groups[3].Success ? CourseKeys(higher.Groups[3].Value) : [];
            return new CourseRange(higher.Groups[1].Value, lowest, null, except, cleaned);
        }
        var span = SpanRangePattern().Match(cleaned);
        if (span.Success
            && int.TryParse(span.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var low)
            && int.TryParse(span.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var high)
            && (!span.Groups[3].Success || span.Groups[3].Value == span.Groups[1].Value))
        {
            var except = span.Groups[5].Success ? CourseKeys(span.Groups[5].Value) : [];
            return new CourseRange(span.Groups[1].Value, low, high, except, cleaned);
        }
        return null;
    }

    /// <summary>What a "Complete …" line asks for: a number of options, or of semester hours.</summary>
    public static (int? Count, double? Hours)? ChooseAmount(string text)
    {
        var lower = text.ToLowerInvariant();
        if (!lower.StartsWith("complete", StringComparison.Ordinal) && !lower.StartsWith("choose", StringComparison.Ordinal)
            && !lower.StartsWith("select", StringComparison.Ordinal) && !lower.StartsWith("take", StringComparison.Ordinal))
            return null;
        var hours = HoursAmountPattern().Match(lower);
        if (hours.Success && double.TryParse(hours.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hourValue))
            return (null, hourValue);
        var count = CountAmountPattern().Match(lower);
        if (!count.Success) return null;
        var token = count.Groups[1].Value;
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return (number, null);
        if (CountWords.TryGetValue(token, out var word)) return (word, null);
        return null;
    }

    private static readonly Dictionary<string, int> CountWords = new(StringComparer.Ordinal)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12,
    };

    private abstract record Token;
    private sealed record HeadingToken(string Text) : Token;
    private sealed record ParagraphToken(string Text) : Token;
    private sealed record TableToken(string Html) : Token;

    private static List<Token> Tokens(string html)
    {
        var tokens = new List<Token>();
        foreach (Match match in TokenPattern().Matches(html))
        {
            if (match.Groups[2].Success) tokens.Add(new HeadingToken(HtmlText.PlainText(match.Groups[2].Value)));
            else if (match.Groups[3].Success) tokens.Add(new TableToken(match.Groups[3].Value));
            else if (match.Groups[4].Success)
            {
                var text = HtmlText.PlainText(match.Groups[4].Value);
                if (text.Length > 0) tokens.Add(new ParagraphToken(text));
            }
        }
        return tokens;
    }

    private static Row ParseRow(string attributes, string body)
    {
        var rowClass = ClassPattern().Match(attributes) is { Success: true } rowMatch ? rowMatch.Groups[1].Value : "";
        var cells = new List<(bool IsHeader, string Class, string Html)>();
        foreach (Match match in CellPattern().Matches(body))
        {
            var attrs = match.Groups[2].Value;
            var cls = ClassPattern().Match(attrs) is { Success: true } cellMatch ? cellMatch.Groups[1].Value : "";
            cells.Add((match.Groups[1].Value.Equals("h", StringComparison.OrdinalIgnoreCase), cls, match.Groups[3].Value));
        }
        double? hours = null;
        foreach (var cell in cells)
        {
            if (!cell.Class.Contains("hourscol", StringComparison.Ordinal)) continue;
            hours = HoursValue(HtmlText.PlainText(cell.Html));
            break;
        }
        if (rowClass.Contains("hidden", StringComparison.Ordinal) || cells.Any(cell => cell.IsHeader))
            return new Row { RowKind = Row.Kind.Skip };
        if (rowClass.Contains("areasubheader", StringComparison.Ordinal) || body.Contains("areasubheader", StringComparison.Ordinal))
            return new Row { RowKind = Row.Kind.Subheader, Text = HtmlText.PlainText(body), Hours = hours };
        if (rowClass.Contains("areaheader", StringComparison.Ordinal) || body.Contains("courselistcomment areaheader", StringComparison.Ordinal))
            return new Row { RowKind = Row.Kind.AreaHeader, Text = HtmlText.PlainText(body), Hours = hours };
        var code = cells.FirstOrDefault(cell => cell.Class.Contains("codecol", StringComparison.Ordinal));
        if (code.Html is not null)
        {
            var text = SpacedText(code.Html);
            var isAlternative = rowClass.Contains("orclass", StringComparison.Ordinal)
                || code.Class.Contains("orclass", StringComparison.Ordinal)
                || text.StartsWith("or ", StringComparison.OrdinalIgnoreCase);
            var sets = CourseSets(text);
            if (sets.Count > 0)
            {
                var titleCell = cells.FirstOrDefault(cell =>
                    !cell.Class.Contains("codecol", StringComparison.Ordinal)
                    && !cell.Class.Contains("hourscol", StringComparison.Ordinal)
                    && !cell.IsHeader);
                var titles = new List<string>();
                if (titleCell.Html is not null)
                {
                    foreach (var line in HtmlText.PlainText(titleCell.Html).Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("and ", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[4..];
                        if (trimmed.Length > 0) titles.Add(trimmed);
                    }
                }
                return new Row { RowKind = Row.Kind.Courses, Sets = sets, IsAlternative = isAlternative, Hours = hours, Titles = titles };
            }
        }
        if (body.Contains("courselistcomment", StringComparison.Ordinal))
        {
            var text = SpacedText(cells.Count > 0 ? cells[0].Html : body);
            return text.Length == 0
                ? new Row { RowKind = Row.Kind.Skip }
                : new Row { RowKind = Row.Kind.Comment, Text = text, Hours = hours };
        }
        return new Row { RowKind = Row.Kind.Skip };
    }

    /// <summary>Plain text that keeps neighboring elements apart.</summary>
    private static string SpacedText(string html) =>
        SpaceRunPattern().Replace(
            PunctuationSpacePattern().Replace(HtmlText.PlainText(html.Replace("<", " <", StringComparison.Ordinal)), "$1"),
            " ").Trim();

    private static double? FirstNumber(Regex pattern, string text)
    {
        var match = pattern.Match(text);
        if (!match.Success) return null;
        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private enum DraftKind { Required, Choose, General }

    private sealed class RuleDraft
    {
        public DraftKind Kind;
        public List<List<CourseKey>> Required = [];
        public int? Count;
        public double? ChooseHours;
        public List<List<List<CourseKey>>> Options = [];
        public List<CourseRange> Ranges = [];
        public double? GeneralHours;
        public string Label = "";
        public double? Hours;
        public List<string> Notes = [];
        public List<string?> OptionCategories = [];
        public string? CurrentCategory;
    }

    private sealed class GroupDraft
    {
        public string? Title;
        public List<RuleDraft> Rules = [];
        public List<string> Notes = [];
    }

    private sealed class SectionDraft
    {
        public string Title = "";
        public List<GroupDraft> Groups = [];
        public List<string> Notes = [];
    }

    private sealed class RequirementsBuilder
    {
        public List<SectionDraft> Sections = [];
        public double? TotalHours;
        public Dictionary<CourseKey, string> CourseTitles = new();
        private int? _chooseIndex;

        public void StartSection(string title)
        {
            Sections.Add(new SectionDraft { Title = title.Trim() });
            _chooseIndex = null;
        }

        public void AddParagraph(string text)
        {
            var match = TotalSemesterHoursPattern().Match(text);
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
                TotalHours = Math.Max(TotalHours ?? 0, hours);
            if (Sections.Count == 0) return;
            Sections[^1].Notes.Add(text);
        }

        public void AddTable(string html)
        {
            if (Sections.Count == 0) StartSection("Program Requirements");
            _chooseIndex = null;
            var startedGroup = false;
            foreach (var row in Rows(html))
            {
                switch (row.RowKind)
                {
                    case Row.Kind.Skip:
                        break;
                    case Row.Kind.AreaHeader:
                        AppendGroup(row.Text);
                        startedGroup = true;
                        break;
                    case Row.Kind.Subheader:
                        if (_chooseIndex is int choose)
                        {
                            Current.Rules[choose].Notes.Add(row.Text);
                            Current.Rules[choose].CurrentCategory = row.Text;
                        }
                        else Current.Notes.Add(row.Text);
                        break;
                    case Row.Kind.Comment:
                        if (!startedGroup) { AppendGroup(null); startedGroup = true; }
                        AddComment(row.Text, row.Hours);
                        break;
                    case Row.Kind.Courses:
                        if (!startedGroup) { AppendGroup(null); startedGroup = true; }
                        AddCourses(row.Sets, row.IsAlternative, row.Hours);
                        foreach (var (key, title) in row.Sets.SelectMany(set => set).Zip(row.Titles))
                            CourseTitles.TryAdd(key, title);
                        break;
                }
            }
        }

        private void AppendGroup(string? title)
        {
            Sections[^1].Groups.Add(new GroupDraft { Title = title });
            _chooseIndex = null;
        }

        private GroupDraft Current => Sections[^1].Groups[^1];

        private void AddComment(string text, double? hours)
        {
            var lower = text.ToLowerInvariant();
            if (lower.Contains("general elective", StringComparison.Ordinal))
            {
                var amount = hours ?? ChooseAmount(text)?.Hours;
                if (amount is double general)
                {
                    Current.Rules.Add(new RuleDraft { Kind = DraftKind.General, GeneralHours = general, Label = text, Hours = general });
                    _chooseIndex = null;
                    return;
                }
            }
            if (CourseRange(text) is CourseRange range)
            {
                if (_chooseIndex is int choose && Current.Rules[choose].Kind == DraftKind.Choose)
                    Current.Rules[choose].Ranges.Add(range);
                else Current.Notes.Add(text);
                return;
            }
            if (ChooseAmount(text) is { } chosen)
            {
                var count = chosen.Count;
                var chooseHours = chosen.Hours;
                Current.Rules.Add(new RuleDraft
                {
                    Kind = DraftKind.Choose,
                    Count = count,
                    ChooseHours = chooseHours,
                    Label = text,
                    Hours = hours ?? chooseHours,
                });
                _chooseIndex = Current.Rules.Count - 1;
                return;
            }
            if (_chooseIndex is int noteIndex) Current.Rules[noteIndex].Notes.Add(text);
            else Current.Notes.Add(text);
        }

        private void AddCourses(List<List<CourseKey>> sets, bool isAlternative, double? hours)
        {
            if (isAlternative)
            {
                if (_chooseIndex is int choose && Current.Rules[choose].Kind == DraftKind.Choose && Current.Rules[choose].Options.Count > 0)
                {
                    Current.Rules[choose].Options[^1].AddRange(sets);
                    return;
                }
                if (Current.Rules.Count > 0 && Current.Rules[^1].Kind == DraftKind.Required)
                {
                    Current.Rules[^1].Required.AddRange(sets);
                    return;
                }
            }
            if (_chooseIndex is int index && hours is null && Current.Rules[index].Kind == DraftKind.Choose)
            {
                Current.Rules[index].Options.Add(sets.Select(set => set.ToList()).ToList());
                Current.Rules[index].OptionCategories.Add(Current.Rules[index].CurrentCategory);
                return;
            }
            Current.Rules.Add(new RuleDraft { Kind = DraftKind.Required, Required = sets.Select(set => set.ToList()).ToList(), Hours = hours });
            _chooseIndex = null;
        }

        public List<RequirementSection> Finish()
        {
            var sectionIds = new HashSet<string>(StringComparer.Ordinal);
            var sections = new List<RequirementSection>();
            foreach (var draft in Sections)
            {
                var sectionId = Unique(Slug(draft.Title), sectionIds);
                var groupIds = new HashSet<string>(StringComparer.Ordinal);
                var groups = new List<RequirementGroup>();
                foreach (var group in draft.Groups)
                {
                    var groupId = Unique($"{sectionId}/{Slug(group.Title ?? "courses")}", groupIds);
                    var ruleIds = new HashSet<string>(StringComparer.Ordinal);
                    var rules = new List<RequirementRule>();
                    foreach (var rule in group.Rules)
                    {
                        RequirementKind kind;
                        var label = rule.Label;
                        switch (rule.Kind)
                        {
                            case DraftKind.Required:
                                if (rule.Required.Count == 0) continue;
                                var option = new CourseOption(rule.Required);
                                kind = new RequirementKind.Required(option);
                                label = option.Label;
                                break;
                            case DraftKind.Choose:
                                kind = new RequirementKind.Choose(
                                    rule.Count,
                                    rule.ChooseHours,
                                    rule.Options.Select(alternatives => new CourseOption(alternatives)).ToList(),
                                    rule.Ranges);
                                break;
                            default:
                                kind = new RequirementKind.GeneralElectives(rule.GeneralHours ?? 0);
                                break;
                        }
                        var id = Unique($"{groupId}/{Slug(label)}", ruleIds);
                        var categories = rule.OptionCategories.Any(category => category is not null) ? rule.OptionCategories : null;
                        rules.Add(new RequirementRule
                        {
                            Id = id,
                            Kind = kind,
                            Label = label,
                            Hours = rule.Hours,
                            Notes = rule.Notes,
                            OptionCategories = categories,
                        });
                    }
                    if (rules.Count == 0 && group.Notes.Count == 0) continue;
                    groups.Add(new RequirementGroup { Id = groupId, Title = group.Title, Rules = rules, Notes = group.Notes });
                }
                var title = draft.Title.Trim('*', ' ');
                sections.Add(new RequirementSection
                {
                    Id = sectionId,
                    Title = title,
                    IsConcentration = groups.Count > 0 && ChoosableSectionPattern().IsMatch(title),
                    Groups = groups,
                    Notes = draft.Notes,
                });
            }
            return sections;
        }

        private static string Slug(string text)
        {
            var lowered = NonSlugPattern().Replace(text.ToLowerInvariant(), "-").Trim('-');
            if (lowered.Length > 60) lowered = lowered[..60].Trim('-');
            return lowered;
        }

        private static string Unique(string id, HashSet<string> used)
        {
            var candidate = id.Length == 0 ? "item" : id;
            var n = 2;
            while (!used.Add(candidate))
            {
                candidate = $"{id}-{n}";
                n++;
            }
            return candidate;
        }
    }

    private static List<(string Href, string Label)> Links(string html)
    {
        var links = new List<(string, string)>();
        foreach (Match match in LinkPattern().Matches(html))
            links.Add((match.Groups[1].Value, match.Groups[2].Value));
        return links;
    }

    private static IEnumerable<Regex> CurrentEditionPatterns()
    {
        yield return AcademicCatalogPattern();
        yield return EditionWordPattern();
    }

    [GeneratedRegex(@"Academic Catalog\s+\d{4}\s*[-\u2013]\s*\d{2,4}", RegexOptions.CultureInvariant)]
    private static partial Regex AcademicCatalogPattern();

    [GeneratedRegex(@"\d{4}\s*[-\u2013]\s*\d{2,4}\s+Edition", RegexOptions.CultureInvariant)]
    private static partial Regex EditionWordPattern();

    [GeneratedRegex(@"archive/(\d{4})-(\d{4})", RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveYearPattern();

    [GeneratedRegex(@"^/?archive/\d{4}-\d{4}/?", RegexOptions.CultureInvariant)]
    private static partial Regex ArchivePrefixPattern();

    [GeneratedRegex(@"(?i)university-?wide-?requirements", RegexOptions.CultureInvariant)]
    private static partial Regex UniversitywideHrefPattern();

    [GeneratedRegex(@"(?i)minimum\s+(?:of\s+)?(\d+(?:\.\d+)?)\s+total\s+semester\s+hours", RegexOptions.CultureInvariant)]
    private static partial Regex MinimumTotalHoursPattern();

    [GeneratedRegex(@"(?i)minimum\s+(?:cumulative\s+)?(\d\.\d+)\s+GPA", RegexOptions.CultureInvariant)]
    private static partial Regex MinimumGpaPattern();

    [GeneratedRegex(@"(?i)minimum\s+of\s+(\d+(?:\.\d+)?)\s+Northeastern\s+University\s+semester\s+hours", RegexOptions.CultureInvariant)]
    private static partial Regex MinimumResidencyPattern();

    [GeneratedRegex(@"(?i)experiential\s+learning\s+graduation\s+requirement", RegexOptions.CultureInvariant)]
    private static partial Regex ExperientialPattern();

    [GeneratedRegex(@"<div[^>]*id=""programrequirementstextcontainer""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequirementsContainerPattern();

    [GeneratedRegex(@"<div[^>]*id=""textcontainer""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TextContainerPattern();

    [GeneratedRegex(@"<div[^>]*id=""[a-z]+textcontainer""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NextContainerPattern();

    [GeneratedRegex("<footer", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FooterPattern();

    [GeneratedRegex(@"<h([2-4])[^>]*>([\s\S]*?)</h\1>|<table[^>]*class=""[^""]*sc_courselist[^""]*""[^>]*>([\s\S]*?)</table>|<p[^>]*>([\s\S]*?)</p>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"<tr([^>]*)>([\s\S]*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RowPattern();

    [GeneratedRegex(@"<t([dh])([^>]*)>([\s\S]*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CellPattern();

    [GeneratedRegex(@"class=""([^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex ClassPattern();

    [GeneratedRegex(@"\b([A-Z]{2,5})\s+(\d{4}[A-Z]?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex CourseKeyPattern();

    [GeneratedRegex(@"\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^([A-Z]{2,5}) (\d{4}) or (?:higher|above)(?:,? (?:except|excluding|but not) (.+?))?\.?$", RegexOptions.CultureInvariant)]
    private static partial Regex HigherRangePattern();

    [GeneratedRegex(@"^([A-Z]{2,5}) (\d{4}) (?:to|through|-|\u2013) (?:([A-Z]{2,5}) )?(\d{4})(?:,? (?:except|excluding|but not) (.+?))?\.?$", RegexOptions.CultureInvariant)]
    private static partial Regex SpanRangePattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)(?:\s*[-\u2013]\s*\d+)?\s+(?:semester hours?|credits?|credit hours?|sh)\b", RegexOptions.CultureInvariant)]
    private static partial Regex HoursAmountPattern();

    [GeneratedRegex(@"^(?:complete|choose|select|take)\s+(?:any\s+)?(\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\b", RegexOptions.CultureInvariant)]
    private static partial Regex CountAmountPattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s+total semester hours", RegexOptions.CultureInvariant)]
    private static partial Regex TotalSemesterHoursPattern();

    [GeneratedRegex(@"^(concentration|track|option|pathway)( in\b|\s*:)|\b(concentration|track|option|pathway)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChoosableSectionPattern();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlugPattern();

    [GeneratedRegex(@"<a\s[^>]*href=""([^""#]+)""[^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\s+([,.:;])", RegexOptions.CultureInvariant)]
    private static partial Regex PunctuationSpacePattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceRunPattern();
}
