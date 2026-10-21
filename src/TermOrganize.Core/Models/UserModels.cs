// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TermOrganize.Core;

public sealed class SavedCourse
{
    public string TermCode { get; set; } = "";
    public CourseKey Key { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    [JsonIgnore] public string Id => $"{TermCode}|{Key}";
}

public sealed class RecentCourse
{
    public string TermCode { get; set; } = "";
    public CourseKey Key { get; set; }
    public DateTimeOffset ViewedAt { get; set; }
    [JsonIgnore] public string Id => $"{TermCode}|{Key}";
}

public sealed class ScheduleFilters
{
    public int? EarliestStart { get; set; }
    public int? LatestEnd { get; set; }
    public DaySet FreeDays { get; set; }
    public bool IncludeHonors { get; set; } = true;
    public bool IncludeOnline { get; set; } = true;
    public bool HideFullSections { get; set; }
    public int MinSeats { get; set; }
    public string? Campus { get; set; }
    public int? CourseCount { get; set; }

    [JsonIgnore]
    public int ActiveCount
    {
        get
        {
            var n = 0;
            if (EarliestStart is not null) n++;
            if (LatestEnd is not null) n++;
            if (!FreeDays.IsEmpty) n++;
            if (!IncludeHonors) n++;
            if (!IncludeOnline) n++;
            if (HideFullSections || MinSeats > 0) n++;
            if (Campus is not null) n++;
            if (CourseCount is not null) n++;
            return n;
        }
    }
}

public sealed class PlanCourse
{
    public CourseKey Key { get; set; }
    public bool IsRequired { get; set; } = true;
    public HashSet<string> ExcludedCRNs { get; set; } = [];
    public int ColorIndex { get; set; }
    [JsonIgnore] public CourseKey Id => Key;
}

public static class CourseColorPalette
{
    public const int Count = 10;
}

public sealed class SchedulePlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string TermCode { get; set; } = "";
    public List<PlanCourse> Courses { get; set; } = [];
    public List<string> SelectedCRNs { get; set; } = [];
    public ScheduleFilters Filters { get; set; } = new();
    public SchedulePreferences? Preferences { get; set; }
    public List<List<string>> FavoriteCombinations { get; set; } = [];
    public bool IsPrimary { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool Contains(CourseKey key) => Courses.Any(c => c.Key == key);
    public PlanCourse? Course(CourseKey key) => Courses.FirstOrDefault(c => c.Key == key);

    [JsonIgnore]
    public int NextColorIndex
    {
        get
        {
            var used = Courses.Select(c => c.ColorIndex).ToHashSet();
            for (var i = 0; i < CourseColorPalette.Count; i++)
                if (!used.Contains(i)) return i;
            return Courses.Count % CourseColorPalette.Count;
        }
    }
}

public sealed class WatchItem
{
    public string TermCode { get; set; } = "";
    public string Crn { get; set; } = "";
    public CourseKey CourseKey { get; set; }
    public string CourseTitle { get; set; } = "";
    public string SectionLabel { get; set; } = "";
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public int LastSeatsRemaining { get; set; }
    public int LastWaitlistRemaining { get; set; }
    public int SeatCapacity { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public bool NotifyOnWaitlist { get; set; }
    public DateTimeOffset? LastNotifiedAt { get; set; }
    [JsonIgnore] public string Id => $"{TermCode}|{Crn}";
}

public sealed class AppPreferences
{
    public string? SelectedTermCode { get; set; }
    public string? PreferredCampus { get; set; }
    public bool HasCompletedOnboarding { get; set; }
    public bool ShowWeekends { get; set; }
    public bool AutoRefreshSeats { get; set; } = true;
    public bool BackgroundRefreshEnabled { get; set; } = true;
    public int CalendarStartHour { get; set; } = 8;
    public int CalendarEndHour { get; set; } = 21;
    /// <summary>Minutes between seat checks while the app is open. The Mac app defaults to 15.</summary>
    public int SeatCheckMinutes { get; set; } = 15;
    public bool NotifyOnSeats { get; set; } = true;
}

public enum PlanSeason
{
    Winter,
    Spring,
    SummerA,
    SummerFull,
    SummerB,
    Fall,
}

public static class PlanSeasonInfo
{
    public static string ExportName(this PlanSeason season) => season switch
    {
        PlanSeason.Winter => "Winter",
        PlanSeason.Spring => "Spring",
        PlanSeason.SummerA => "Summer Session A",
        PlanSeason.SummerFull => "Summer Full",
        PlanSeason.SummerB => "Summer Session B",
        PlanSeason.Fall => "Fall",
        _ => "",
    };

    public static string ShortName(this PlanSeason season) => season switch
    {
        PlanSeason.SummerA => "Summer A",
        PlanSeason.SummerB => "Summer B",
        PlanSeason.SummerFull => "Summer",
        _ => season.ExportName(),
    };

    public static int StartMonth(this PlanSeason season) => season switch
    {
        PlanSeason.Winter or PlanSeason.Spring => 1,
        PlanSeason.SummerA or PlanSeason.SummerFull => 5,
        PlanSeason.SummerB => 7,
        PlanSeason.Fall => 9,
        _ => 1,
    };

    public static int OrderWithinMonth(this PlanSeason season) => season switch
    {
        PlanSeason.Winter => 0,
        PlanSeason.Spring => 1,
        PlanSeason.SummerA => 0,
        PlanSeason.SummerFull => 1,
        _ => 0,
    };

    public static PlanSeason? ParseExportName(string raw)
    {
        var s = string.Join(' ', raw.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return s switch
        {
            "fall" or "autumn" => PlanSeason.Fall,
            "spring" => PlanSeason.Spring,
            "winter" => PlanSeason.Winter,
            "summer session a" or "summer a" or "summer 1" or "summer i" or "summer session 1" => PlanSeason.SummerA,
            "summer session b" or "summer b" or "summer 2" or "summer ii" or "summer session 2" => PlanSeason.SummerB,
            "summer full" or "summer" or "full summer" or "summer session full" => PlanSeason.SummerFull,
            _ => null,
        };
    }

    public static PlanSeason FromTerm(TermSeason season) => season switch
    {
        TermSeason.Fall => PlanSeason.Fall,
        TermSeason.Winter => PlanSeason.Winter,
        TermSeason.Spring => PlanSeason.Spring,
        TermSeason.Summer1 => PlanSeason.SummerA,
        TermSeason.SummerFull => PlanSeason.SummerFull,
        TermSeason.Summer2 => PlanSeason.SummerB,
        _ => PlanSeason.Fall,
    };

    public static bool Matches(this PlanSeason season, TermSeason term) => (season, term) switch
    {
        (PlanSeason.Fall, TermSeason.Fall) => true,
        (PlanSeason.Spring, TermSeason.Spring) => true,
        (PlanSeason.Winter, TermSeason.Winter) => true,
        (PlanSeason.SummerA, TermSeason.Summer1 or TermSeason.SummerFull) => true,
        (PlanSeason.SummerB, TermSeason.Summer2 or TermSeason.SummerFull) => true,
        (PlanSeason.SummerFull, TermSeason.SummerFull or TermSeason.Summer1 or TermSeason.Summer2) => true,
        _ => false,
    };
}

public sealed class DegreePlanCourse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CourseKey? Key { get; set; }
    public string Label { get; set; } = "";
    public string Title { get; set; } = "";
    public double Credits { get; set; }
    /// <summary>Section chosen for this course, when the plan names one. The app keeps the field on load.</summary>
    public string? SelectedCrn { get; set; }

    public DegreePlanCourse() { }

    public DegreePlanCourse(CourseKey? key, string title, double credits, string? label = null, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Key = key;
        Label = label ?? key?.ToString() ?? "";
        Title = title;
        Credits = credits;
    }
}

public sealed class DegreePlanTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PlanSeason Season { get; set; }
    public int Year { get; set; }
    public List<DegreePlanCourse> Courses { get; set; } = [];

    [JsonIgnore] public string Name => $"{Season.ExportName()} {Year}";
    [JsonIgnore] public string ShortName => $"{Season.ShortName()} {Year}";
    [JsonIgnore] public double Credits => Courses.Sum(c => c.Credits);
    [JsonIgnore] public int MonthIndex => Year * 12 + Season.StartMonth() - 1;
    [JsonIgnore] public int SortKey => MonthIndex * 4 + Season.OrderWithinMonth();

    public bool Matches(Term term) => term.CalendarYear == Year && Season.Matches(term.Season);
}

public sealed class DegreePlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Starts at 1. Each change of this plan increments it. MCP clients send it back as expected_revision.</summary>
    public int Revision { get; set; }
    public string Name { get; set; } = "";
    public string? Program { get; set; }
    public string? StudentName { get; set; }
    public string? StudentID { get; set; }
    public List<DegreePlanCourse> Completed { get; set; } = [];
    public List<DegreePlanTerm> Terms { get; set; } = [];
    public bool IsPrimary { get; set; }
    public string? ImportedFrom { get; set; }
    public DateTimeOffset? PrintedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int? CatalogYear { get; set; }
    public string? CatalogProgramPath { get; set; }
    public string? CatalogProgramTitle { get; set; }
    public string? ConcentrationID { get; set; }
    public List<RequirementSubstitution> Substitutions { get; set; } = [];
    [JsonPropertyName("courseEquivalences")]
    public List<CourseEquivalence> Equivalences { get; set; } = [];

    [JsonIgnore] public double CompletedCredits => Completed.Sum(c => c.Credits);
    [JsonIgnore] public double PlannedCredits => Terms.Sum(t => t.Credits);
    [JsonIgnore] public HashSet<CourseKey> CompletedKeys => Completed.Select(c => c.Key).OfType<CourseKey>().ToHashSet();
    [JsonIgnore] public CourseEquivalences DeclaredEquivalences => CourseEquivalences.FromDeclared(Equivalences);

    public void SortTerms() => Terms.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));

    public DegreePlanTerm? TermMatching(Term term) => Terms.FirstOrDefault(t => t.Matches(term));

    public bool DeclareEquivalence(CourseKey course, CourseKey countsAs, string note = "")
    {
        if (course == countsAs || DeclaredEquivalences.AreEquivalent(course, countsAs)) return false;
        Equivalences.Add(new CourseEquivalence { Course = course, CountsAs = countsAs, Note = note });
        return true;
    }

    public void RemoveEquivalence(Guid id) => Equivalences.RemoveAll(e => e.Id == id);
}

public abstract class CourseNumberLimit : IEquatable<CourseNumberLimit>
{
    public static CourseNumberLimit SameAsRequirement { get; } = new Same();
    public static CourseNumberLimit AnyNumber { get; } = new Any();
    public static CourseNumberLimit AtLeast(int minimum) => new AtLeastLimit(minimum);

    public virtual string? Summary => null;
    public abstract bool Equals(CourseNumberLimit? other);
    public override bool Equals(object? obj) => Equals(obj as CourseNumberLimit);
    public override int GetHashCode() => Summary?.GetHashCode() ?? 0;

    private sealed class Same : CourseNumberLimit
    {
        public override bool Equals(CourseNumberLimit? other) => other is Same;
        public override int GetHashCode() => 1;
    }

    private sealed class Any : CourseNumberLimit
    {
        public override string? Summary => "any course number";
        public override bool Equals(CourseNumberLimit? other) => other is Any;
        public override int GetHashCode() => 2;
    }

    public sealed class AtLeastLimit : CourseNumberLimit
    {
        public int Minimum { get; }
        public AtLeastLimit(int minimum) => Minimum = minimum;
        public override string? Summary => $"numbered {Minimum} or higher";
        public override bool Equals(CourseNumberLimit? other) => other is AtLeastLimit a && a.Minimum == Minimum;
        public override int GetHashCode() => HashCode.Combine(3, Minimum);
    }
}

public sealed class RequirementReplacement
{
    public int? Count { get; set; }
    public double? Hours { get; set; }
    public string? LikeRuleID { get; set; }
    public string? LikeRuleTitle { get; set; }
    public List<CourseKey> Courses { get; set; } = [];
    public CourseNumberLimit? CourseNumbers { get; set; }

    [JsonIgnore]
    public string Summary
    {
        get
        {
            string amount;
            if (Count is int count) amount = count == 1 ? "1 course" : $"{count} courses";
            else if (Hours is double hours) amount = $"{hours.ToString("0.#", CultureInfo.InvariantCulture)} semester hours";
            else amount = "Courses";
            var sources = new List<string>();
            if (LikeRuleTitle is not null) sources.Add($"like {LikeRuleTitle}");
            if (Courses.Count > 0) sources.Add("from " + string.Join(", ", Courses.Select(c => c.ToString())));
            var text = string.Join(" ", new[] { amount }.Concat(sources));
            if (LikeRuleTitle is not null && CourseNumbers?.Summary is string numbers) text += ", " + numbers;
            return text;
        }
    }
}

[JsonConverter(typeof(SubstitutionChangeConverter))]
public abstract class SubstitutionChange : IEquatable<SubstitutionChange>
{
    public static SubstitutionChange CountCourse(CourseKey key) => new CountCourseChange(key);
    public static SubstitutionChange Replace(RequirementReplacement replacement) => new ReplaceChange(replacement);
    public static SubstitutionChange Waive { get; } = new WaiveChange();

    public abstract bool Equals(SubstitutionChange? other);
    public override bool Equals(object? obj) => Equals(obj as SubstitutionChange);
    public abstract override int GetHashCode();

    public sealed class CountCourseChange : SubstitutionChange
    {
        public CourseKey Course { get; }
        public CountCourseChange(CourseKey course) => Course = course;
        public override bool Equals(SubstitutionChange? other) => other is CountCourseChange c && c.Course == Course;
        public override int GetHashCode() => HashCode.Combine(1, Course);
    }

    public sealed class ReplaceChange : SubstitutionChange
    {
        public RequirementReplacement Replacement { get; }
        public ReplaceChange(RequirementReplacement replacement) => Replacement = replacement;
        public override bool Equals(SubstitutionChange? other) => other is ReplaceChange;
        public override int GetHashCode() => 2;
    }

    private sealed class WaiveChange : SubstitutionChange
    {
        public override bool Equals(SubstitutionChange? other) => other is WaiveChange;
        public override int GetHashCode() => 3;
    }
}

public sealed class RequirementSubstitution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RuleID { get; set; } = "";
    public string RuleTitle { get; set; } = "";
    public SubstitutionChange Change { get; set; } = SubstitutionChange.Waive;
    public string Note { get; set; } = "";
    public DateTimeOffset? ApprovedOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserData
{
    public const int SupportedVersion = 1;
    public int Version { get; set; } = 1;
    public AppPreferences Preferences { get; set; } = new();
    public List<SavedCourse> Saved { get; set; } = [];
    public List<RecentCourse> Recents { get; set; } = [];
    public List<SchedulePlan> Plans { get; set; } = [];
    public List<WatchItem> Watchlist { get; set; } = [];
    public List<string> SearchHistory { get; set; } = [];
    public List<DegreePlan> DegreePlans { get; set; } = [];
}

public sealed class CourseNumberLimitConverter : JsonConverter<CourseNumberLimit>
{
    public override CourseNumberLimit? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
        if (root.TryGetProperty("sameAsRequirement", out _)) return CourseNumberLimit.SameAsRequirement;
        if (root.TryGetProperty("anyNumber", out _)) return CourseNumberLimit.AnyNumber;
        if (root.TryGetProperty("atLeast", out var atLeast))
        {
            if (atLeast.TryGetProperty("_0", out var boxed) && boxed.TryGetInt32(out var n)) return CourseNumberLimit.AtLeast(n);
            if (atLeast.TryGetInt32(out var direct)) return CourseNumberLimit.AtLeast(direct);
        }
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, CourseNumberLimit value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case CourseNumberLimit.AtLeastLimit at:
                writer.WritePropertyName("atLeast");
                writer.WriteStartObject();
                writer.WriteNumber("_0", at.Minimum);
                writer.WriteEndObject();
                break;
            default:
                if (value.Equals(CourseNumberLimit.AnyNumber)) writer.WriteStartObject("anyNumber");
                else writer.WriteStartObject("sameAsRequirement");
                writer.WriteEndObject();
                break;
        }
        writer.WriteEndObject();
    }
}

public sealed class SubstitutionChangeConverter : JsonConverter<SubstitutionChange>
{
    public override SubstitutionChange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.TryGetProperty("waive", out _)) return SubstitutionChange.Waive;
        if (root.TryGetProperty("countCourse", out var course))
        {
            var key = Unwrap(course, options);
            return SubstitutionChange.CountCourse(key);
        }
        if (root.TryGetProperty("replace", out var replace))
        {
            var payload = replace.TryGetProperty("_0", out var boxed) ? boxed : replace;
            var replacement = payload.Deserialize<RequirementReplacement>(options) ?? new RequirementReplacement();
            return SubstitutionChange.Replace(replacement);
        }
        throw new JsonException("Unknown substitution.");
    }

    private static CourseKey Unwrap(JsonElement element, JsonSerializerOptions options)
    {
        if (element.TryGetProperty("_0", out var boxed)) element = boxed;
        return element.Deserialize<CourseKey>(options);
    }

    public override void Write(Utf8JsonWriter writer, SubstitutionChange value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case SubstitutionChange.CountCourseChange course:
                writer.WritePropertyName("countCourse");
                writer.WriteStartObject();
                writer.WritePropertyName("_0");
                JsonSerializer.Serialize(writer, course.Course, options);
                writer.WriteEndObject();
                break;
            case SubstitutionChange.ReplaceChange replace:
                writer.WritePropertyName("replace");
                writer.WriteStartObject();
                writer.WritePropertyName("_0");
                JsonSerializer.Serialize(writer, replace.Replacement, options);
                writer.WriteEndObject();
                break;
            default:
                writer.WriteStartObject("waive");
                writer.WriteEndObject();
                break;
        }
        writer.WriteEndObject();
    }
}
