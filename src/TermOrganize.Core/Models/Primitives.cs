// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text.Json.Serialization;

namespace TermOrganize.Core;

/// <summary>A Banner term such as 202710 ("Fall 2026 Semester").</summary>
public sealed class Term : IComparable<Term>
{
    public string Code { get; set; } = "";
    public string BannerDescription { get; set; } = "";

    public Term() { }

    public Term(string code, string bannerDescription)
    {
        Code = code;
        BannerDescription = bannerDescription;
    }

    [JsonIgnore]
    public string Id => Code;

    [JsonIgnore]
    public string Name
    {
        get
        {
            var s = BannerDescription.Replace("(View Only)", "", StringComparison.OrdinalIgnoreCase).Trim();
            switch (College)
            {
                case TermCollege.Neu:
                    s = s.Replace(" Semester", "", StringComparison.Ordinal);
                    break;
                case TermCollege.Cps:
                case TermCollege.Law:
                    foreach (var marker in new[] { " CPS ", " Law " })
                    {
                        var index = s.IndexOf(marker, StringComparison.Ordinal);
                        if (index >= 0)
                        {
                            var head = s[..index];
                            var tail = s[index..].Trim();
                            s = $"{head} · {tail}";
                            break;
                        }
                    }
                    break;
            }
            return s.Trim();
        }
    }

    [JsonIgnore]
    public string ShortName => Name.Split(" · ")[0];

    [JsonIgnore]
    public bool IsViewOnly => BannerDescription.Contains("view only", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public TermCollege College
    {
        get
        {
            if (Code.Length == 0) return TermCollege.Neu;
            return Code[^1] switch
            {
                '4' or '5' => TermCollege.Cps,
                '2' or '8' => TermCollege.Law,
                _ => TermCollege.Neu,
            };
        }
    }

    [JsonIgnore]
    public TermSeason Season
    {
        get
        {
            if (Code.Length != 6) return TermSeason.Fall;
            return Code[4] switch
            {
                '1' => TermSeason.Fall,
                '2' => TermSeason.Winter,
                '3' => TermSeason.Spring,
                '4' => TermSeason.Summer1,
                '6' => TermSeason.Summer2,
                _ => TermSeason.SummerFull,
            };
        }
    }

    /// <summary>The calendar year the term takes place (Fall 2026 has code 2027xx).</summary>
    [JsonIgnore]
    public int CalendarYear
    {
        get
        {
            var year = int.TryParse(Code.Length >= 4 ? Code[..4] : Code, out var y) ? y : 0;
            return Season == TermSeason.Fall ? year - 1 : year;
        }
    }

    public int CompareTo(Term? other) => string.CompareOrdinal(Code, other?.Code);
}

public enum TermCollege
{
    Neu,
    Cps,
    Law,
}

public enum TermSeason
{
    Fall,
    Winter,
    Spring,
    Summer1,
    SummerFull,
    Summer2,
}

/// <summary>Identifies a course independent of term, e.g. CS 3000.</summary>
public readonly record struct CourseKey(string Subject, string Number) : IComparable<CourseKey>
{
    public static CourseKey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var cleaned = text.ToUpperInvariant().Replace('-', ' ');
        var i = 0;
        while (i < cleaned.Length && char.IsLetter(cleaned[i])) i++;
        if (i == 0) return null;
        var letters = cleaned[..i];
        var rest = cleaned[i..].Trim();
        if (rest.Length == 0 || !rest.All(c => char.IsLetter(c) || char.IsNumber(c))) return null;
        return new CourseKey(letters, rest);
    }

    public int? Level => Number.Length > 0 && char.IsDigit(Number[0]) ? Number[0] - '0' : null;

    public int NumericValue
    {
        get
        {
            var i = 0;
            while (i < Number.Length && char.IsDigit(Number[i])) i++;
            return i == 0 ? 0 : int.Parse(Number[..i], CultureInfo.InvariantCulture);
        }
    }

    public override string ToString() => $"{Subject} {Number}";

    public int CompareTo(CourseKey other)
    {
        var subject = string.CompareOrdinal(Subject, other.Subject);
        if (subject != 0) return subject;
        var number = NumericValue.CompareTo(other.NumericValue);
        return number != 0 ? number : string.CompareOrdinal(Number, other.Number);
    }

    public static bool operator <(CourseKey a, CourseKey b) => a.CompareTo(b) < 0;
    public static bool operator >(CourseKey a, CourseKey b) => a.CompareTo(b) > 0;
}

public static class CreditFormatter
{
    public static string Number(double value) =>
        value % 1 == 0 ? ((int)value).ToString(CultureInfo.InvariantCulture) : value.ToString("0.0", CultureInfo.InvariantCulture);

    public static string Label(double min, double max)
    {
        var text = min == max ? Number(min) : $"{Number(min)}\u2013{Number(max)}";
        return $"{text} {(max == 1 && min == 1 ? "credit" : "credits")}";
    }
}

public sealed class Subject
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonIgnore] public string Id => Code;
}

public sealed class AttributeInfo
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonIgnore] public string Id => Code;
}

public sealed class Campus
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonIgnore] public string Id => Code;
    [JsonIgnore] public bool IsOnline => Name.Contains("online", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Northeastern's NUpath general-education attributes.</summary>
[JsonConverter(typeof(NuPathConverter))]
public enum NUPath
{
    Nd, Ei, Ic, Fq, Si, Ad, Dd, Er, Wf, Wi, Wd, Ex, Ce,
}

public static class NUPathInfo
{
    public static string AttributeCode(this NUPath path) => path switch
    {
        NUPath.Nd => "NCND",
        NUPath.Ei => "NCEI",
        NUPath.Ic => "NCIC",
        NUPath.Fq => "NCFQ",
        NUPath.Si => "NCSI",
        NUPath.Ad => "NCAD",
        NUPath.Dd => "NCDD",
        NUPath.Er => "NCER",
        NUPath.Wf => "NCW1",
        NUPath.Wi => "NCWI",
        NUPath.Wd => "NCW3",
        NUPath.Ex => "NCEX",
        NUPath.Ce => "NCCE",
        _ => "",
    };

    public static string Short(this NUPath path) => path switch
    {
        NUPath.Nd => "ND",
        NUPath.Ei => "EI",
        NUPath.Ic => "IC",
        NUPath.Fq => "FQ",
        NUPath.Si => "SI",
        NUPath.Ad => "AD",
        NUPath.Dd => "DD",
        NUPath.Er => "ER",
        NUPath.Wf => "WF",
        NUPath.Wi => "WI",
        NUPath.Wd => "WD",
        NUPath.Ex => "EX",
        NUPath.Ce => "CE",
        _ => "",
    };

    public static string Name(this NUPath path) => path switch
    {
        NUPath.Nd => "Natural & Designed World",
        NUPath.Ei => "Creative Expression & Innovation",
        NUPath.Ic => "Interpreting Culture",
        NUPath.Fq => "Formal & Quantitative Reasoning",
        NUPath.Si => "Societies & Institutions",
        NUPath.Ad => "Analyzing & Using Data",
        NUPath.Dd => "Difference & Diversity",
        NUPath.Er => "Ethical Reasoning",
        NUPath.Wf => "First-Year Writing",
        NUPath.Wi => "Writing Intensive",
        NUPath.Wd => "Advanced Writing in the Disciplines",
        NUPath.Ex => "Integration Experience",
        NUPath.Ce => "Capstone Experience",
        _ => "",
    };

    public static IReadOnlyList<NUPath> FromAttributeCodes(IEnumerable<string> codes)
    {
        var set = codes as ISet<string> ?? codes.ToHashSet(StringComparer.Ordinal);
        return Enum.GetValues<NUPath>().Where(p => set.Contains(p.AttributeCode())).ToArray();
    }

    public static NUPath? FromShort(string shortName) =>
        Enum.GetValues<NUPath>().FirstOrDefault(p => p.Short().Equals(shortName, StringComparison.OrdinalIgnoreCase)) is var path
        && path.Short().Equals(shortName, StringComparison.OrdinalIgnoreCase) ? path : null;
}

public sealed class NuPathConverter : System.Text.Json.Serialization.JsonConverter<NUPath>
{
    public override NUPath Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? "";
        foreach (var path in Enum.GetValues<NUPath>())
            if (path.AttributeCode().Equals(text, StringComparison.OrdinalIgnoreCase) || path.ToString().Equals(text, StringComparison.OrdinalIgnoreCase))
                return path;
        throw new System.Text.Json.JsonException($"Unknown NUpath '{text}'.");
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, NUPath value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(value.AttributeCode());
}

public static class BannerAttributes
{
    public const string Honors = "GNHN";
}

/// <summary>Weekdays. Bit 0 is Sunday through bit 6 Saturday.</summary>
[JsonConverter(typeof(DaySetConverter))]
public readonly record struct DaySet
{
    public byte Raw { get; }
    public DaySet(byte raw) => Raw = (byte)(raw & 0x7F);

    public static DaySet Sunday => new(1 << 0);
    public static DaySet Monday => new(1 << 1);
    public static DaySet Tuesday => new(1 << 2);
    public static DaySet Wednesday => new(1 << 3);
    public static DaySet Thursday => new(1 << 4);
    public static DaySet Friday => new(1 << 5);
    public static DaySet Saturday => new(1 << 6);
    public static DaySet Weekdays => new(0b0111110);
    public static DaySet All => new(0x7F);
    public static DaySet None => new(0);

    public static readonly string[] Letters = ["U", "M", "T", "W", "R", "F", "S"];
    public static readonly string[] ShortNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    public static readonly string[] FullNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    public static DaySet FromIndices(IEnumerable<int> indices)
    {
        byte raw = 0;
        foreach (var i in indices)
            if (i is >= 0 and <= 6) raw |= (byte)(1 << i);
        return new DaySet(raw);
    }

    public static DaySet Day(int index) => FromIndices([index]);

    public bool Contains(int index) => index is >= 0 and <= 6 && (Raw & (1 << index)) != 0;
    public IReadOnlyList<int> Indices => Enumerable.Range(0, 7).Where(Contains).ToArray();
    public int Count => System.Numerics.BitOperations.PopCount(Raw);
    public bool IsEmpty => Raw == 0;
    public string CompactLabel => string.Concat(Indices.Select(i => Letters[i]));
    public string ShortLabel => string.Join(", ", Indices.Select(i => ShortNames[i]));
    public bool IsSubsetOf(DaySet other) => (Raw & other.Raw) == Raw;
    public bool IsDisjoint(DaySet other) => (Raw & other.Raw) == 0;
    public DaySet Union(DaySet other) => new((byte)(Raw | other.Raw));
    public DaySet Intersection(DaySet other) => new((byte)(Raw & other.Raw));
    public DaySet Without(DaySet other) => new((byte)(Raw & ~other.Raw));
    public static DaySet operator |(DaySet a, DaySet b) => a.Union(b);
    public static DaySet operator &(DaySet a, DaySet b) => a.Intersection(b);
}

public static class TimeOfDay
{
    public static int MinutesFromHhmm(int value) => (value / 100) * 60 + value % 100;
    public static int HhmmFromMinutes(int minutes) => (minutes / 60) * 100 + minutes % 60;

    public static string Label(int minutes, bool includePeriod = true)
    {
        var h24 = (minutes / 60) % 24;
        var m = minutes % 60;
        var h12 = h24 % 12 == 0 ? 12 : h24 % 12;
        var period = h24 < 12 ? "AM" : "PM";
        var baseText = $"{h12}:{m:00}";
        return includePeriod ? $"{baseText} {period}" : baseText;
    }

    public static string RangeLabel(int start, int end)
    {
        var samePeriod = (start / 60 < 12) == (end / 60 < 12);
        return $"{Label(start, !samePeriod)} \u2013 {Label(end)}";
    }

    public static string HourAxisLabel(int hour)
    {
        var h12 = hour % 12 == 0 ? 12 : hour % 12;
        return $"{h12}{(hour < 12 || hour == 24 ? "a" : "p")}";
    }
}

public static class AppLink
{
    public const string Scheme = "termorganize";
    public static readonly HashSet<string> LegacySchemes = new(StringComparer.OrdinalIgnoreCase) { "neuclasssearch" };

    public static bool IsAppLink(Uri url) =>
        url.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase) || LegacySchemes.Contains(url.Scheme);

    public static Uri Url(string path) => new($"{Scheme}://{path}");
}
