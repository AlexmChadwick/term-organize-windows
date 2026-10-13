// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Degree requirements are read from Northeastern's public academic catalog
// (catalog.northeastern.edu). Term Organize is not affiliated with, endorsed by,
// or sponsored by Northeastern University.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TermOrganize.Core;

/// <summary>
/// An edition of Northeastern's academic catalog (catalog.northeastern.edu), for example 2026–2027.
/// Students follow the degree requirements of the edition for the year they entered their program.
/// </summary>
public readonly record struct CatalogYear(int StartYear) : IComparable<CatalogYear>
{
    public int Id => StartYear;

    /// <summary>2026-2027, as in the catalog's archive URLs.</summary>
    public string Slug => $"{StartYear}-{StartYear + 1}";

    /// <summary>2026–2027.</summary>
    public override string ToString() => $"{StartYear}\u2013{StartYear + 1}";

    public int CompareTo(CatalogYear other) => StartYear.CompareTo(other.StartYear);

    public static bool operator <(CatalogYear left, CatalogYear right) => left.StartYear < right.StartYear;
    public static bool operator >(CatalogYear left, CatalogYear right) => left.StartYear > right.StartYear;

    /// <summary>Parses 2026-2027, 2026–2027, or 2026-27.</summary>
    public static CatalogYear? Parse(string text)
    {
        var match = YearPattern().Match(text);
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var start)) return null;
        if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var end)) return null;
        if (end != start + 1 && end != (start + 1) % 100) return null;
        return new CatalogYear(start);
    }

    /// <summary>
    /// The edition in effect on <paramref name="date"/>. New editions are published over the summer;
    /// July is the turnover. Only a fallback — the catalog site says which edition is current.
    /// </summary>
    public static CatalogYear Containing(DateTimeOffset date, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(date, zone);
        return new CatalogYear(local.Month >= 7 ? local.Year : local.Year - 1);
    }

    private static Regex YearPattern() => new(@"(\d{4})\s*[-\u2013]\s*(\d{2,4})", RegexOptions.CultureInvariant);
}

/// <summary>
/// The editions the catalog site offers: the current one, served from the site root, and archived
/// ones, served under /archive/&lt;year&gt;/. Read from the site, so a new edition appears without an app update.
/// </summary>
public sealed class CatalogEditions
{
    public static readonly Uri SiteUrl = new("https://catalog.northeastern.edu/");

    public CatalogYear Current { get; set; }

    /// <summary>Newest first.</summary>
    public List<CatalogYear> Archived { get; set; } = [];

    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>True when made up offline (no copy from the site yet).</summary>
    public bool IsEstimated { get; set; }

    public CatalogEditions()
    {
    }

    public CatalogEditions(CatalogYear current, IEnumerable<CatalogYear> archived, DateTimeOffset fetchedAt, bool isEstimated = false)
    {
        Current = current;
        Archived = archived.ToList();
        FetchedAt = fetchedAt;
        IsEstimated = isEstimated;
    }

    /// <summary>Every edition, newest first.</summary>
    [JsonIgnore]
    public List<CatalogYear> All => new HashSet<CatalogYear>(Archived.Append(Current)).OrderByDescending(year => year.StartYear).ToList();

    public bool Contains(CatalogYear year) => year == Current || Archived.Contains(year);

    /// <summary>The root of an edition: the site root for the current edition, else its archive.</summary>
    public Uri BaseUrl(CatalogYear year) =>
        year == Current ? SiteUrl : new Uri($"https://catalog.northeastern.edu/archive/{year.Slug}/");

    /// <summary>A page of an edition from its edition-relative path.</summary>
    public Uri Url(CatalogYear year, string path)
    {
        var trimmed = path.StartsWith('/') ? path[1..] : path;
        return new Uri(BaseUrl(year), trimmed);
    }

    /// <summary>
    /// A guess used before the site has been reached: the edition for today, and every edition
    /// back to 2016–2017 (the oldest archived online).
    /// </summary>
    public static CatalogEditions Estimated(DateTimeOffset? now = null, TimeZoneInfo? timeZone = null)
    {
        var current = CatalogYear.Containing(now ?? DateTimeOffset.Now, timeZone);
        var archived = new List<CatalogYear>();
        for (var year = current.StartYear - 1; year >= 2016; year--) archived.Add(new CatalogYear(year));
        return new CatalogEditions(current, archived, CatalogDates.DistantPast, isEstimated: true);
    }
}

public static class CatalogDates
{
    public static readonly DateTimeOffset DistantPast = new(1, 1, 1, 0, 0, 0, TimeSpan.Zero);
}

/// <summary>A degree program page in a catalog edition.</summary>
public sealed class CatalogProgram
{
    public enum LevelKind
    {
        Undergraduate,
        Graduate,
        Other,
    }

    /// <summary>Computer Science, BSCS (Boston).</summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// The page's path within its edition, without any archive prefix:
    /// undergraduate/computer-information-science/computer-science/bscs/.
    /// </summary>
    public string Path { get; set; } = "";

    public CatalogProgram()
    {
    }

    public CatalogProgram(string title, string path)
    {
        Title = title;
        Path = path;
    }

    [JsonIgnore] public string Id => Path;

    [JsonIgnore]
    public LevelKind Level
    {
        get
        {
            if (Path.StartsWith("undergraduate/", StringComparison.Ordinal)) return LevelKind.Undergraduate;
            if (Path.StartsWith("graduate/", StringComparison.Ordinal)) return LevelKind.Graduate;
            return LevelKind.Other;
        }
    }

    /// <summary>Computer Science.</summary>
    [JsonIgnore] public string Name => TitleParts().Name;

    /// <summary>BSCS.</summary>
    [JsonIgnore] public string? Degree => TitleParts().Degree;

    /// <summary>Boston, for editions that list programs per campus.</summary>
    [JsonIgnore] public string? Campus => TitleParts().Campus;

    /// <summary>The title without the campus: Computer Science, BSCS.</summary>
    [JsonIgnore] public string TitleWithoutCampus => Degree is string degree ? $"{Name}, {degree}" : Title;

    private (string Name, string? Degree, string? Campus) TitleParts()
    {
        var match = TitlePattern().Match(Title);
        if (!match.Success) return (Title, null, null);
        var campus = match.Groups[3].Success ? match.Groups[3].Value : null;
        return (match.Groups[1].Value, match.Groups[2].Value, campus);
    }

    private static Regex TitlePattern() => new(
        @"^(.+?),\s*([A-Za-z][A-Za-z.]{1,9})(?:\s*\(([^)]+)\))?\s*$",
        RegexOptions.CultureInvariant);
}

/// <summary>
/// The minimums every bachelor's degree of a catalog edition has, from its Universitywide
/// Requirements page.
/// </summary>
public sealed class UniversitywideRequirements
{
    public double? TotalHours { get; set; }
    public double? Gpa { get; set; }

    /// <summary>Semester hours that must be earned at Northeastern (residency).</summary>
    public double? ResidencyHours { get; set; }

    /// <summary>Including the experiential learning graduation requirement (newer editions).</summary>
    public bool RequiresExperientialLearning { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>The Program Requirements of a program page.</summary>
public sealed class ProgramRequirements
{
    public CatalogYear Year { get; set; }
    public CatalogProgram Program { get; set; } = new();

    /// <summary>Sections in page order; concentrations are marked with <see cref="RequirementSection.IsConcentration"/>.</summary>
    public List<RequirementSection> Sections { get; set; } = [];

    /// <summary>134 from "134 total semester hours required".</summary>
    public double? TotalHours { get; set; }

    public DateTimeOffset FetchedAt { get; set; } = CatalogDates.DistantPast;

    /// <summary>The catalog's titles for the courses it lists (CS 3000 → Algorithms and Data).</summary>
    [JsonConverter(typeof(CourseTitleMapConverter))]
    public Dictionary<CourseKey, string> CourseTitles { get; set; } = new();

    /// <summary>
    /// The edition's universitywide minimums. Null when that page couldn't be read.
    /// </summary>
    public UniversitywideRequirements? Universitywide { get; set; }

    [JsonIgnore]
    public List<RequirementSection> Concentrations => Sections.Where(section => section.IsConcentration).ToList();

    /// <summary>The sections that apply: everything but the concentrations, plus the chosen one.</summary>
    public List<RequirementSection> ApplicableSections(string? concentrationId) =>
        Sections.Where(section => !section.IsConcentration || section.Id == concentrationId).ToList();

    [JsonIgnore]
    public List<RequirementRule> AllRules => Sections.SelectMany(section => section.Groups.SelectMany(group => group.Rules)).ToList();

    public RequirementRule? Rule(string id) => AllRules.FirstOrDefault(rule => rule.Id == id);

    /// <summary>
    /// A readable name for a rule: its group's title when the group has only that rule
    /// (Science Requirement), else the rule's own label.
    /// </summary>
    public string? Title(string id)
    {
        foreach (var section in Sections)
        {
            foreach (var group in section.Groups)
            {
                var rule = group.Rules.FirstOrDefault(item => item.Id == id);
                if (rule is null) continue;
                if (group.Rules.Count == 1)
                {
                    var title = group.Title ?? (section.IsConcentration ? section.Title : null);
                    if (title is not null) return title;
                }
                return rule.DisplayLabel;
            }
        }
        return null;
    }
}

public sealed class RequirementSection
{
    public string Id { get; set; } = "";

    /// <summary>For example Computer Science Requirements, or Concentration in Software.</summary>
    public string Title { get; set; } = "";

    /// <summary>One of several concentrations; a student completes one.</summary>
    public bool IsConcentration { get; set; }

    public List<RequirementGroup> Groups { get; set; } = [];

    /// <summary>Paragraphs under the heading.</summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>Software from Concentration in Software, Concentration: Software, or Software Concentration.</summary>
    [JsonIgnore]
    public string ConcentrationName =>
        ConcentrationSuffix().Replace(ConcentrationPrefix().Replace(Title, ""), "")
            .Trim(' ', '*');

    private static Regex ConcentrationPrefix() => new(
        @"^(Concentration|Track|Option|Pathway)(\s+in\s+|\s*:\s*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex ConcentrationSuffix() => new(
        @"\s+(Concentration|Track|Option|Pathway)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

public sealed class RequirementGroup
{
    public string Id { get; set; } = "";

    /// <summary>The area header, for example Computer Science Required Courses. Null when the table has none.</summary>
    public string? Title { get; set; }

    public List<RequirementRule> Rules { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

/// <summary>One row of a program's requirements.</summary>
public sealed class RequirementRule
{
    public string Id { get; set; } = "";
    public RequirementKind Kind { get; set; } = new RequirementKind.Choose(null, null, [], []);

    /// <summary>The catalog's wording.</summary>
    public string Label { get; set; } = "";

    /// <summary>The hours column (4; 8-9 is read as 8).</summary>
    public double? Hours { get; set; }

    /// <summary>Notes under the rule, including category subheaders (Biology, Chemistry).</summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>
    /// For "complete N of the following" lists with category subheaders: the category of each
    /// option, in option order. Null when the list has none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string?>? OptionCategories { get; set; }

    /// <summary>The label without a trailing colon.</summary>
    [JsonIgnore]
    public string DisplayLabel => Label.EndsWith(':') ? Label[..^1] : Label;

    /// <summary>Every course the rule names (not the ones its ranges cover).</summary>
    [JsonIgnore]
    public List<CourseKey> ListedCourses => Kind switch
    {
        RequirementKind.Required required => required.Option.AllCourses,
        RequirementKind.Choose choose => choose.Options.SelectMany(option => option.AllCourses).ToList(),
        _ => [],
    };
}

/// <summary>What a requirement asks for, after the catalog's wording is read.</summary>
[JsonConverter(typeof(RequirementKindConverter))]
public abstract class RequirementKind : IEquatable<RequirementKind>
{
    private RequirementKind()
    {
    }

    /// <summary>One option: a course, a course set taken together, or alternatives.</summary>
    public sealed class Required : RequirementKind
    {
        public CourseOption Option { get; }
        public Required(CourseOption option) => Option = option;
        public override bool Equals(RequirementKind? other) => other is Required required && Option.Equals(required.Option);
        public override int GetHashCode() => HashCode.Combine(1, Option);
    }

    /// <summary>Count options, or hours semester hours, from options and ranges.</summary>
    public sealed class Choose : RequirementKind
    {
        public int? Count { get; }
        public double? Hours { get; }
        public IReadOnlyList<CourseOption> Options { get; }
        public IReadOnlyList<CourseRange> Ranges { get; }

        public Choose(int? count, double? hours, IReadOnlyList<CourseOption> options, IReadOnlyList<CourseRange> ranges)
        {
            Count = count;
            Hours = hours;
            Options = options;
            Ranges = ranges;
        }

        public override bool Equals(RequirementKind? other) =>
            other is Choose choose && Count == choose.Count && Hours == choose.Hours &&
            Options.SequenceEqual(choose.Options) && Ranges.SequenceEqual(choose.Ranges);

        public override int GetHashCode() => HashCode.Combine(2, Count, Hours, Options.Count, Ranges.Count);
    }

    /// <summary>Any courses not used for another requirement.</summary>
    public sealed class GeneralElectives : RequirementKind
    {
        public double Hours { get; }
        public GeneralElectives(double hours) => Hours = hours;
        public override bool Equals(RequirementKind? other) => other is GeneralElectives general && general.Hours == Hours;
        public override int GetHashCode() => HashCode.Combine(3, Hours);
    }

    public abstract bool Equals(RequirementKind? other);
    public override bool Equals(object? obj) => Equals(obj as RequirementKind);
    public abstract override int GetHashCode();
}

/// <summary>
/// An item that can be satisfied one of several ways. Each way is a set of courses taken together.
/// </summary>
public sealed class CourseOption : IEquatable<CourseOption>
{
    public List<List<CourseKey>> Alternatives { get; set; } = [];

    public CourseOption()
    {
    }

    public CourseOption(IEnumerable<IEnumerable<CourseKey>> alternatives)
    {
        Alternatives = alternatives.Select(alternative => alternative.ToList()).ToList();
    }

    [JsonIgnore]
    public List<CourseKey> AllCourses
    {
        get
        {
            var seen = new HashSet<CourseKey>();
            var result = new List<CourseKey>();
            foreach (var key in Alternatives.SelectMany(alternative => alternative))
                if (seen.Add(key)) result.Add(key);
            return result;
        }
    }

    /// <summary>CS 1800 and CS 1802, or CS 4500 or CS 4530.</summary>
    [JsonIgnore]
    public string Label => string.Join(" or ", Alternatives.Select(alternative => string.Join(" and ", alternative.Select(key => key.ToString()))));

    public bool Equals(CourseOption? other)
    {
        if (other is null || Alternatives.Count != other.Alternatives.Count) return false;
        for (var i = 0; i < Alternatives.Count; i++)
            if (!Alternatives[i].SequenceEqual(other.Alternatives[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as CourseOption);
    public override int GetHashCode() => Alternatives.Count;
}

/// <summary>A range of course numbers: CS 2500 or higher, except CS 5010.</summary>
public sealed class CourseRange : IEquatable<CourseRange>
{
    public string Subject { get; set; } = "";
    public int Lowest { get; set; }
    public int? Highest { get; set; }
    public List<CourseKey> Except { get; set; } = [];

    /// <summary>The catalog's wording.</summary>
    public string Label { get; set; } = "";

    public CourseRange()
    {
    }

    public CourseRange(string subject, int lowest, int? highest, IEnumerable<CourseKey>? except, string label)
    {
        Subject = subject;
        Lowest = lowest;
        Highest = highest;
        Except = except?.ToList() ?? [];
        Label = label;
    }

    public bool Contains(CourseKey key)
    {
        if (key.Subject != Subject || Except.Contains(key)) return false;
        var digits = 0;
        while (digits < key.Number.Length && char.IsDigit(key.Number[digits])) digits++;
        if (digits != 4) return false;
        var number = int.Parse(key.Number[..digits], CultureInfo.InvariantCulture);
        return number >= Lowest && (Highest is not int highest || number <= highest);
    }

    public CourseRange Copy(int? lowest = null, string? label = null) =>
        new(Subject, lowest ?? Lowest, Highest, Except, label ?? Label);

    /// <summary>
    /// The range with a petition's course-number limit: unchanged, without its minimum, or with another minimum.
    /// </summary>
    public CourseRange Applying(CourseNumberLimit? limit)
    {
        if (limit is null || limit.Equals(CourseNumberLimit.SameAsRequirement)) return this;
        var lowest = limit.Equals(CourseNumberLimit.AnyNumber) ? 0
            : limit is CourseNumberLimit.AtLeastLimit atLeast ? atLeast.Minimum
            : Lowest;
        var copy = new CourseRange(Subject, lowest, Highest, Except, "");
        copy.Label = copy.DescribedLabel();
        return copy;
    }

    /// <summary>CS 2000 or higher, except CS 5010; Any CS course.</summary>
    public string DescribedLabel()
    {
        string text = (Lowest > 0, Highest) switch
        {
            (true, int highest) => $"{Subject} {Lowest} to {Subject} {highest}",
            (true, null) => $"{Subject} {Lowest} or higher",
            (false, int highest) => $"{Subject} courses up to {Subject} {highest}",
            _ => $"Any {Subject} course",
        };
        if (Except.Count > 0) text += ", except " + string.Join(", ", Except.Select(key => key.ToString()));
        return text;
    }

    /// <summary>Why a course in this subject is outside the range.</summary>
    public string? ReasonExcluding(CourseKey key)
    {
        if (key.Subject != Subject || Contains(key)) return null;
        if (Except.Contains(key)) return $"{key} is excluded";
        var digits = 0;
        while (digits < key.Number.Length && char.IsDigit(key.Number[digits])) digits++;
        if (digits == 0 || !int.TryParse(key.Number[..digits], NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return null;
        if (number < Lowest) return $"Below {Subject} {Lowest}";
        if (Highest is int highest && number > highest) return $"Above {Subject} {highest}";
        return null;
    }

    public bool Equals(CourseRange? other) =>
        other is not null && Subject == other.Subject && Lowest == other.Lowest && Highest == other.Highest &&
        Label == other.Label && Except.SequenceEqual(other.Except);

    public override bool Equals(object? obj) => Equals(obj as CourseRange);
    public override int GetHashCode() => HashCode.Combine(Subject, Lowest, Highest, Label);
}

public sealed class RequirementKindConverter : JsonConverter<RequirementKind>
{
    public override RequirementKind? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.TryGetProperty("required", out var required))
        {
            var payload = required.TryGetProperty("_0", out var boxed) ? boxed : required;
            var option = payload.Deserialize<CourseOption>(options) ?? new CourseOption();
            return new RequirementKind.Required(option);
        }
        if (root.TryGetProperty("generalElectives", out var general))
        {
            var payload = general.TryGetProperty("hours", out var hours) ? hours : general;
            return new RequirementKind.GeneralElectives(payload.ValueKind == JsonValueKind.Number ? payload.GetDouble() : 0);
        }
        if (root.TryGetProperty("choose", out var choose))
        {
            int? count = choose.TryGetProperty("count", out var countEl) && countEl.ValueKind == JsonValueKind.Number ? countEl.GetInt32() : null;
            double? hours = choose.TryGetProperty("hours", out var hoursEl) && hoursEl.ValueKind == JsonValueKind.Number ? hoursEl.GetDouble() : null;
            var optionsList = choose.TryGetProperty("options", out var optionsEl)
                ? optionsEl.Deserialize<List<CourseOption>>(options) ?? []
                : [];
            var ranges = choose.TryGetProperty("ranges", out var rangesEl)
                ? rangesEl.Deserialize<List<CourseRange>>(options) ?? []
                : [];
            return new RequirementKind.Choose(count, hours, optionsList, ranges);
        }
        throw new JsonException("Unknown requirement kind.");
    }

    public override void Write(Utf8JsonWriter writer, RequirementKind value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case RequirementKind.Required required:
                writer.WritePropertyName("required");
                writer.WriteStartObject();
                writer.WritePropertyName("_0");
                JsonSerializer.Serialize(writer, required.Option, options);
                writer.WriteEndObject();
                break;
            case RequirementKind.Choose choose:
                writer.WritePropertyName("choose");
                writer.WriteStartObject();
                if (choose.Count is int count) writer.WriteNumber("count", count);
                if (choose.Hours is double hours) writer.WriteNumber("hours", hours);
                writer.WritePropertyName("options");
                JsonSerializer.Serialize(writer, choose.Options, options);
                writer.WritePropertyName("ranges");
                JsonSerializer.Serialize(writer, choose.Ranges, options);
                writer.WriteEndObject();
                break;
            case RequirementKind.GeneralElectives general:
                writer.WritePropertyName("generalElectives");
                writer.WriteStartObject();
                writer.WriteNumber("hours", general.Hours);
                writer.WriteEndObject();
                break;
        }
        writer.WriteEndObject();
    }
}

/// <summary>
/// Swift encodes <c>[CourseKey: String]</c> as an array of pairs. Object maps keyed by "CS 3000" are accepted too.
/// A value that cannot be read becomes an empty map, so an older copy still loads.
/// </summary>
public sealed class CourseTitleMapConverter : JsonConverter<Dictionary<CourseKey, string>>
{
    public override Dictionary<CourseKey, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return ReadElement(doc.RootElement, options);
        }
        catch (JsonException)
        {
            return new Dictionary<CourseKey, string>();
        }
    }

    public static Dictionary<CourseKey, string> ReadElement(JsonElement element, JsonSerializerOptions options)
    {
        var result = new Dictionary<CourseKey, string>();
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (CourseKey.Parse(property.Name) is CourseKey key && property.Value.ValueKind == JsonValueKind.String)
                    result[key] = property.Value.GetString() ?? "";
            }
            return result;
        }
        if (element.ValueKind != JsonValueKind.Array) return result;
        foreach (var pair in element.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array) continue;
            var items = pair.EnumerateArray().ToList();
            if (items.Count != 2) continue;
            var key = items[0].Deserialize<CourseKey>(options);
            if (items[1].ValueKind != JsonValueKind.String) continue;
            result[key] = items[1].GetString() ?? "";
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<CourseKey, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var pair in value.OrderBy(pair => pair.Key))
        {
            writer.WriteStartArray();
            JsonSerializer.Serialize(writer, pair.Key, options);
            writer.WriteStringValue(pair.Value);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
    }
}
