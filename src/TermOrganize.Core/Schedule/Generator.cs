// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// Portions derived from SearchNEU where the Swift file says so. Modified 2026-10-13: ported from Swift to C#.

using System.Diagnostics;
using System.Numerics;

namespace TermOrganize.Core;

public enum ScheduleSortOption
{
    Best,
    FewestDays,
    LatestStart,
    EarliestFinish,
    FewestGaps,
    MostSeats,
}

public static class ScheduleSortOptionInfo
{
    public static string Label(this ScheduleSortOption sort) => sort switch
    {
        ScheduleSortOption.Best => "Best overall",
        ScheduleSortOption.FewestDays => "Fewest days on campus",
        ScheduleSortOption.LatestStart => "Latest start",
        ScheduleSortOption.EarliestFinish => "Earliest finish",
        ScheduleSortOption.FewestGaps => "Fewest gaps",
        ScheduleSortOption.MostSeats => "Most open seats",
        _ => "",
    };

    public static string ShortLabel(this ScheduleSortOption sort) => sort switch
    {
        ScheduleSortOption.Best => "Best overall",
        ScheduleSortOption.FewestDays => "Fewest days",
        ScheduleSortOption.LatestStart => "Latest start",
        ScheduleSortOption.EarliestFinish => "Earliest finish",
        ScheduleSortOption.FewestGaps => "Fewest gaps",
        ScheduleSortOption.MostSeats => "Most seats",
        _ => "",
    };

    public static string Symbol(this ScheduleSortOption sort) => sort switch
    {
        ScheduleSortOption.Best => "sparkles",
        ScheduleSortOption.FewestDays => "calendar.badge.minus",
        ScheduleSortOption.LatestStart => "sunrise",
        ScheduleSortOption.EarliestFinish => "sunset",
        ScheduleSortOption.FewestGaps => "rectangle.compress.vertical",
        ScheduleSortOption.MostSeats => "chair.lounge",
        _ => "",
    };
}

public enum ScheduleFilterReason
{
    ExcludedByUser,
    StartsTooEarly,
    EndsTooLate,
    FreeDay,
    Honors,
    Online,
    Seats,
    Campus,
    BlockedTime,
    PrefStartsTooEarly,
    PrefEndsTooLate,
    PrefDayOff,
    PrefFormat,
    PrefSeats,
    PrefCampus,
    PrefWorkload,
}

public static class ScheduleFilterReasonInfo
{
    public static uint Bit(this ScheduleFilterReason reason) => 1u << (int)reason;
}

public sealed class ScheduleCourseInput
{
    public CourseKey Key { get; set; }
    public bool IsRequired { get; set; }
    public IReadOnlyList<CourseSection> Sections { get; set; } = [];
    public IReadOnlyList<string> ExcludedCRNs { get; set; } = [];

    /// <summary>Same list as <see cref="ExcludedCRNs"/>. The shell sets this name.</summary>
    public IReadOnlyList<string> ExcludedCrns
    {
        get => ExcludedCRNs;
        set => ExcludedCRNs = value ?? [];
    }

    public double? MinCredits { get; set; }
    public double? MaxCredits { get; set; }
    public CourseKey Id => Key;

    /// <summary>Whole credits the search uses. Null credits count as 0; a fractional credit rounds up.</summary>
    public int WholeMinCredits => (int)Math.Ceiling(MinCredits ?? 0);

    /// <summary>At least <see cref="WholeMinCredits"/>. A null maximum does not shrink the course.</summary>
    public int WholeMaxCredits => Math.Max(WholeMinCredits, (int)Math.Ceiling(MaxCredits ?? 0));

    public ScheduleCourseInput() { }

    public ScheduleCourseInput(CourseKey key, bool isRequired, IReadOnlyList<CourseSection> sections, IEnumerable<string>? excludedCRNs = null, double? minCredits = null, double? maxCredits = null)
    {
        Key = key;
        IsRequired = isRequired;
        Sections = sections;
        ExcludedCRNs = excludedCRNs?.ToList() ?? [];
        MinCredits = minCredits;
        MaxCredits = maxCredits;
    }

    public ScheduleCourseInput Clone() => new()
    {
        Key = Key,
        IsRequired = IsRequired,
        Sections = Sections,
        ExcludedCRNs = ExcludedCRNs.ToList(),
        MinCredits = MinCredits,
        MaxCredits = MaxCredits,
    };
}

public sealed class GeneratedSchedule : IEquatable<GeneratedSchedule>
{
    public List<string> Crns { get; }
    public List<CourseSection> Sections { get; }
    public int DaysOnCampus { get; }
    public int ClassDays { get; }
    public int? EarliestStart { get; }
    public int? LatestEnd { get; }
    public int TotalGapMinutes { get; }
    public int MinSeats { get; }
    public int TotalSeats { get; }
    public int FullSections { get; }
    public double Score { get; }
    public List<ScheduleReason> Reasons { get; }
    public List<string> SortedCRNs { get; }
    public string Id { get; }

    public GeneratedSchedule(
        IReadOnlyList<string> crns, IReadOnlyList<CourseSection> sections,
        int daysOnCampus, int classDays, int? earliestStart, int? latestEnd,
        int totalGapMinutes, int minSeats, int totalSeats, int fullSections, double score,
        IReadOnlyList<ScheduleReason>? reasons = null)
    {
        Crns = crns.ToList();
        Sections = sections.ToList();
        DaysOnCampus = daysOnCampus;
        ClassDays = classDays;
        EarliestStart = earliestStart;
        LatestEnd = latestEnd;
        TotalGapMinutes = totalGapMinutes;
        MinSeats = minSeats;
        TotalSeats = totalSeats;
        FullSections = fullSections;
        Score = score;
        Reasons = reasons?.ToList() ?? [];
        SortedCRNs = Crns.OrderBy(c => c, StringComparer.Ordinal).ToList();
        Id = string.Join("-", SortedCRNs);
    }

    public GeneratedSchedule(IReadOnlyList<CourseSection> sections, SchedulePreferences? preferences = null)
    {
        preferences ??= new SchedulePreferences();
        var buildings = new ScheduleBuildingTable();
        var intervals = new List<ScheduleInterval>();
        var minSeats = int.MaxValue;
        var totalSeats = 0;
        var full = 0;
        var points = 0.0;
        var hasPreferences = !preferences.IsDefault;
        foreach (var section in sections)
        {
            var own = ScheduleInterval.IntervalsFor(section, buildings);
            intervals.AddRange(own);
            if (hasPreferences) points += SchedulePreferenceRules.SectionScore(section, own, preferences);
            var seats = Math.Max(0, section.SeatsRemaining);
            minSeats = Math.Min(minSeats, seats);
            totalSeats += seats;
            if (seats == 0) full++;
        }
        var raw = ScheduleMeasure.Measure(intervals, ScheduleScoring.MeasureOptions(preferences));
        var weights = new ScheduleScoring.Weights(preferences);
        var floor = minSeats == int.MaxValue ? 0 : minSeats;
        var crns = sections.Select(s => s.Crn).ToList();
        Crns = crns;
        Sections = sections.ToList();
        DaysOnCampus = raw.CampusDays;
        ClassDays = raw.ClassDays;
        EarliestStart = raw.EarliestStart == int.MaxValue ? null : raw.EarliestStart;
        LatestEnd = raw.LatestEnd == int.MinValue ? null : raw.LatestEnd;
        TotalGapMinutes = raw.GapMinutes;
        MinSeats = floor;
        TotalSeats = totalSeats;
        FullSections = full;
        Score = weights.Score(raw, full, floor) + points;
        Reasons = SchedulePreferenceRules.Reasons(Sections, preferences);
        SortedCRNs = Crns.OrderBy(c => c, StringComparer.Ordinal).ToList();
        Id = string.Join("-", SortedCRNs);
    }

    public bool Equals(GeneratedSchedule? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as GeneratedSchedule);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);
}

public sealed class ScheduleGenerationResult
{
    public List<GeneratedSchedule> Schedules { get; set; } = [];
    public int TotalFound { get; set; }
    public bool IsExhaustive { get; set; } = true;
    public bool WasCancelled { get; set; }
    public ScheduleDiagnostics Diagnostics { get; set; } = new();
    public double Elapsed { get; set; }
    public bool WasSampled { get; set; }
    public List<ScheduleSuggestion> Suggestions { get; set; } = [];
    public bool IsTruncated => !IsExhaustive || Schedules.Count < TotalFound;
}

public sealed class ScheduleDiagnostics
{
    public sealed class CourseReport : IEquatable<CourseReport>
    {
        public CourseKey Key { get; set; }
        public bool IsRequired { get; set; }
        public int TotalSections { get; set; }
        public int RemainingSections { get; set; }
        public Dictionary<ScheduleFilterReason, int> Rejections { get; set; } = [];
        public CourseKey Id => Key;
        public bool IsBlocked => RemainingSections == 0;

        public bool Equals(CourseReport? other) =>
            other is not null && Key == other.Key && IsRequired == other.IsRequired &&
            TotalSections == other.TotalSections && RemainingSections == other.RemainingSections &&
            Rejections.Count == other.Rejections.Count && Rejections.All(p => other.Rejections.TryGetValue(p.Key, out var n) && n == p.Value);

        public override bool Equals(object? obj) => Equals(obj as CourseReport);
        public override int GetHashCode() => HashCode.Combine(Key, IsRequired, TotalSections, RemainingSections);
    }

    public enum ConflictCause { Overlap, TravelTime }

    public sealed class ConflictPair : IEquatable<ConflictPair>
    {
        public CourseKey First { get; set; }
        public CourseKey Second { get; set; }
        public ConflictCause Reason { get; set; } = ConflictCause.Overlap;

        public bool Equals(ConflictPair? other) =>
            other is not null && First == other.First && Second == other.Second && Reason == other.Reason;

        public override bool Equals(object? obj) => Equals(obj as ConflictPair);
        public override int GetHashCode() => HashCode.Combine(First, Second, Reason);
    }

    public abstract record CountProblem
    {
        public sealed record TooManyRequired(int Required, int Wanted) : CountProblem;
        public sealed record TooFewCourses(int Available, int Wanted) : CountProblem;
        public sealed record CreditsOverTarget(int Required, int Limit) : CountProblem;
        public sealed record CreditsUnderTarget(int Available, int Wanted) : CountProblem;
    }

    public ScheduleFilters Filters { get; set; } = new();
    public SchedulePreferences Preferences { get; set; } = new();
    public List<CourseReport> Courses { get; set; } = [];
    public List<ConflictPair> ConflictingPairs { get; set; } = [];
    public CountProblem? CountProblemKind { get; set; }
    public bool HasNoCourses => Courses.Count == 0;
    public List<CourseReport> BlockedRequiredCourses => Courses.Where(c => c.IsRequired && c.IsBlocked).ToList();
}

public abstract record ScheduleRelaxation
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract string Symbol { get; }
    public virtual void Apply(ScheduleFilters filters) { }
    public virtual void Apply(SchedulePreferences preferences) { }

    public sealed record ClearEarliestStart : ScheduleRelaxation
    {
        public override string Id => "clearEarliestStart";
        public override string Title => "Remove start time limit";
        public override string Symbol => "sunrise";
        public override void Apply(ScheduleFilters filters) => filters.EarliestStart = null;
    }

    public sealed record ClearLatestEnd : ScheduleRelaxation
    {
        public override string Id => "clearLatestEnd";
        public override string Title => "Remove end time limit";
        public override string Symbol => "sunset";
        public override void Apply(ScheduleFilters filters) => filters.LatestEnd = null;
    }

    public sealed record ClearFreeDays : ScheduleRelaxation
    {
        public override string Id => "clearFreeDays";
        public override string Title => "Allow classes on every day";
        public override string Symbol => "calendar";
        public override void Apply(ScheduleFilters filters) => filters.FreeDays = DaySet.None;
    }

    public sealed record AllowHonors : ScheduleRelaxation
    {
        public override string Id => "allowHonors";
        public override string Title => "Include honors sections";
        public override string Symbol => "star";
        public override void Apply(ScheduleFilters filters) => filters.IncludeHonors = true;
    }

    public sealed record AllowOnline : ScheduleRelaxation
    {
        public override string Id => "allowOnline";
        public override string Title => "Include online sections";
        public override string Symbol => "wifi";
        public override void Apply(ScheduleFilters filters) => filters.IncludeOnline = true;
    }

    public sealed record AllowFullSections : ScheduleRelaxation
    {
        public override string Id => "allowFullSections";
        public override string Title => "Include full sections";
        public override string Symbol => "chair.lounge";
        public override void Apply(ScheduleFilters filters)
        {
            filters.HideFullSections = false;
            filters.MinSeats = 0;
        }
    }

    public sealed record ClearCampus : ScheduleRelaxation
    {
        public override string Id => "clearCampus";
        public override string Title => "Allow any campus";
        public override string Symbol => "building.2";
        public override void Apply(ScheduleFilters filters) => filters.Campus = null;
    }

    public sealed record ClearCourseCount : ScheduleRelaxation
    {
        public override string Id => "clearCourseCount";
        public override string Title => "Use any number of courses";
        public override string Symbol => "number";
        public override void Apply(ScheduleFilters filters) => filters.CourseCount = null;
    }

    public sealed record MakeOptional(CourseKey Key) : ScheduleRelaxation
    {
        public override string Id => $"optional-{Key}";
        public override string Title => $"Make {Key} optional";
        public override string Symbol => "questionmark.circle";
    }

    public sealed record PreferenceChange(PreferenceRelaxation Relaxation) : ScheduleRelaxation
    {
        public override string Id => $"preference-{Relaxation.Id}";
        public override string Title => Relaxation.Title;
        public override string Symbol => Relaxation.Symbol;
        public override void Apply(SchedulePreferences preferences) => Relaxation.Apply(preferences);
    }
}

public abstract record PreferenceRelaxation
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract string Symbol { get; }
    public abstract void Apply(SchedulePreferences preferences);

    public sealed record SoftenEarliestStart : PreferenceRelaxation
    {
        public override string Id => "softenEarliestStart";
        public override string Title => "Allow earlier classes if needed";
        public override string Symbol => "sunrise";
        public override void Apply(SchedulePreferences preferences)
        {
            if (preferences.EarliestStart is not null) preferences.EarliestStart.Strength = PreferenceStrength.Preferred;
        }
    }

    public sealed record SoftenLatestEnd : PreferenceRelaxation
    {
        public override string Id => "softenLatestEnd";
        public override string Title => "Allow later classes if needed";
        public override string Symbol => "sunset";
        public override void Apply(SchedulePreferences preferences)
        {
            if (preferences.LatestEnd is not null) preferences.LatestEnd.Strength = PreferenceStrength.Preferred;
        }
    }

    public sealed record SoftenDaysOff : PreferenceRelaxation
    {
        public override string Id => "softenDaysOff";
        public override string Title => "Allow classes on your days off if needed";
        public override string Symbol => "calendar";
        public override void Apply(SchedulePreferences preferences) => preferences.DaysOffStrength = PreferenceStrength.Preferred;
    }

    public sealed record SoftenBlockedTime(Guid BlockId, string Label) : PreferenceRelaxation
    {
        public override string Id => $"softenBlockedTime-{BlockId:D}";
        public override string Title => $"Allow classes during \u201C{Label}\u201D if needed";
        public override string Symbol => "calendar.badge.clock";
        public override void Apply(SchedulePreferences preferences)
        {
            var index = preferences.BlockedTimes.FindIndex(b => b.Id == BlockId);
            if (index >= 0) preferences.BlockedTimes[index].Strength = PreferenceStrength.Preferred;
        }
    }

    public sealed record SoftenFormat : PreferenceRelaxation
    {
        public override string Id => "softenFormat";
        public override string Title => "Allow other formats if needed";
        public override string Symbol => "wifi";
        public override void Apply(SchedulePreferences preferences) => preferences.FormatStrength = PreferenceStrength.Preferred;
    }

    public sealed record SoftenCampus : PreferenceRelaxation
    {
        public override string Id => "softenCampus";
        public override string Title => "Allow other campuses if needed";
        public override string Symbol => "building.2";
        public override void Apply(SchedulePreferences preferences) => preferences.CampusStrength = PreferenceStrength.Preferred;
    }

    public sealed record AllowFullSections : PreferenceRelaxation
    {
        public override string Id => "allowFullSections";
        public override string Title => "Include full sections";
        public override string Symbol => "chair.lounge";
        public override void Apply(SchedulePreferences preferences) => preferences.Seats = SeatPolicy.Any;
    }

    public sealed record ClearMaxClassesPerDay : PreferenceRelaxation
    {
        public override string Id => "clearMaxClassesPerDay";
        public override string Title => "Remove the classes-a-day limit";
        public override string Symbol => "gauge.with.dots.needle.33percent";
        public override void Apply(SchedulePreferences preferences) => preferences.MaxClassesPerDay = null;
    }

    public sealed record ClearMaxMinutesPerDay : PreferenceRelaxation
    {
        public override string Id => "clearMaxMinutesPerDay";
        public override string Title => "Remove the hours-a-day limit";
        public override string Symbol => "gauge.with.dots.needle.33percent";
        public override void Apply(SchedulePreferences preferences) => preferences.MaxMinutesPerDay = null;
    }

    public sealed record SoftenMinimumBreak : PreferenceRelaxation
    {
        public override string Id => "softenMinimumBreak";
        public override string Title => "Don\u2019t require time to walk between classes";
        public override string Symbol => "figure.walk";
        public override void Apply(SchedulePreferences preferences) => preferences.MinimumBreakStrength = PreferenceStrength.Preferred;
    }

    public sealed record ClearCreditTarget : PreferenceRelaxation
    {
        public override string Id => "clearCreditTarget";
        public override string Title => "Remove the credit target";
        public override string Symbol => "number";
        public override void Apply(SchedulePreferences preferences) => preferences.CreditTarget = null;
    }

    public static List<PreferenceRelaxation> Available(SchedulePreferences preferences)
    {
        var result = new List<PreferenceRelaxation>();
        if (preferences.EarliestStart?.Strength == PreferenceStrength.Required) result.Add(new SoftenEarliestStart());
        if (preferences.LatestEnd?.Strength == PreferenceStrength.Required) result.Add(new SoftenLatestEnd());
        if (!preferences.DaysOff.IsEmpty && preferences.DaysOffStrength == PreferenceStrength.Required) result.Add(new SoftenDaysOff());
        foreach (var block in preferences.ActiveBlockedTimes)
            if (block.Strength == PreferenceStrength.Required)
                result.Add(new SoftenBlockedTime(block.Id, block.Label));
        if (preferences.Format != FormatPreference.NoPreference && preferences.FormatStrength == PreferenceStrength.Required)
            result.Add(new SoftenFormat());
        if (preferences.Campus is not null && preferences.CampusStrength == PreferenceStrength.Required)
            result.Add(new SoftenCampus());
        if (preferences.Seats != SeatPolicy.Any) result.Add(new AllowFullSections());
        if (preferences.MaxClassesPerDay is not null) result.Add(new ClearMaxClassesPerDay());
        if (preferences.MaxMinutesPerDay is not null) result.Add(new ClearMaxMinutesPerDay());
        if (preferences.Breaks == BreakPreference.AtLeast && preferences.MinimumBreakStrength == PreferenceStrength.Required)
            result.Add(new SoftenMinimumBreak());
        if (preferences.CreditTarget is not null) result.Add(new ClearCreditTarget());
        return result;
    }
}

public sealed record ScheduleSuggestion(ScheduleRelaxation Relaxation, int ScheduleCount, bool IsLowerBound)
{
    public string Id => Relaxation.Id;
    public override string ToString() => Relaxation.Title;
    public static implicit operator string(ScheduleSuggestion suggestion) => suggestion.Relaxation.Title;
}

/// <summary>How far a search is allowed to look. <see cref="Standard"/> is what the schedule screen uses.</summary>
public sealed class ScheduleLimits
{
    public int MaxResults { get; set; } = 5_000;
    public int MaxExhaustiveLeaves { get; set; } = 2_500_000;
    public int MaxNodes { get; set; } = 12_000_000;
    public double TimeBudget { get; set; } = 0.9;
    public int SampleLeaves { get; set; } = 150_000;
    public double SampleTimeBudget { get; set; } = 0.35;
    public bool AllowSampling { get; set; } = true;

    public static ScheduleLimits Standard { get; } = new();

    public static ScheduleLimits Probe { get; } = new()
    {
        MaxResults = 1,
        MaxExhaustiveLeaves = 300,
        MaxNodes = 300_000,
        TimeBudget = 0.25,
        SampleLeaves = 0,
        SampleTimeBudget = 0,
        AllowSampling = false,
    };

    public static ScheduleLimits BlockerProbe { get; } = new()
    {
        MaxResults = 1,
        MaxExhaustiveLeaves = 1_000,
        MaxNodes = 250_000,
        TimeBudget = 0.15,
        SampleLeaves = 0,
        SampleTimeBudget = 0,
        AllowSampling = false,
    };
}

public static partial class ScheduleGenerator
{
    public static uint RejectionReasons(CourseSection section, ScheduleFilters filters, SchedulePreferences? preferences = null, IEnumerable<string>? excluded = null)
    {
        preferences ??= new SchedulePreferences();
        excluded ??= [];
        uint reasons = 0;
        if (excluded.Contains(section.Crn)) reasons |= ScheduleFilterReason.ExcludedByUser.Bit();
        var weekly = section.WeeklyMeetings.ToList();
        if (filters.EarliestStart is int earliest && weekly.Any(m => (m.Start ?? int.MaxValue) < earliest))
            reasons |= ScheduleFilterReason.StartsTooEarly.Bit();
        if (filters.LatestEnd is int latest && weekly.Any(m => (m.End ?? 0) > latest))
            reasons |= ScheduleFilterReason.EndsTooLate.Bit();
        if (!filters.FreeDays.IsEmpty && weekly.Any(m => !m.Days.IsDisjoint(filters.FreeDays)))
            reasons |= ScheduleFilterReason.FreeDay.Bit();
        if (!filters.IncludeHonors && section.Honors) reasons |= ScheduleFilterReason.Honors.Bit();
        if (!filters.IncludeOnline && section.IsOnline) reasons |= ScheduleFilterReason.Online.Bit();
        if (filters.HideFullSections && section.SeatsRemaining <= 0) reasons |= ScheduleFilterReason.Seats.Bit();
        if (filters.MinSeats > 0 && section.SeatsRemaining < filters.MinSeats) reasons |= ScheduleFilterReason.Seats.Bit();
        if (filters.Campus is string campus && !section.IsOnline && !HasNoCampus(section) && section.Campus != campus)
            reasons |= ScheduleFilterReason.Campus.Bit();
        if (!preferences.IsDefault)
            reasons |= SchedulePreferenceRules.RejectionReasons(section, preferences);
        return reasons;
    }

    public static bool HasNoCampus(CourseSection section) =>
        section.Campus.Contains("no campus", StringComparison.OrdinalIgnoreCase);

    public static bool PassesFilters(CourseSection section, ScheduleFilters filters) =>
        RejectionReasons(section, filters) == 0;

    public static ScheduleGenerationResult Generate(
        IReadOnlyList<ScheduleCourseInput> courses,
        ScheduleFilters filters,
        SchedulePreferences? preferences = null,
        ScheduleSortOption sort = ScheduleSortOption.Best,
        ScheduleLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var result = Run(courses, filters, (preferences ?? new SchedulePreferences()).Normalized(), sort, limits ?? ScheduleLimits.Standard, cancellationToken);
        result.Elapsed = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
        return result;
    }

    public static List<ScheduleSuggestion> SuggestRelaxations(
        IReadOnlyList<ScheduleCourseInput> courses,
        ScheduleFilters filters,
        SchedulePreferences? preferences = null,
        bool includeCourseRelaxations = true,
        CancellationToken cancellationToken = default)
    {
        preferences ??= new SchedulePreferences();
        var candidates = new List<(ScheduleRelaxation Relaxation, ScheduleFilters Filters, SchedulePreferences Preferences, IReadOnlyList<ScheduleCourseInput> Courses)>();
        void AddFilter(ScheduleRelaxation relaxation, bool active)
        {
            if (!active) return;
            var relaxed = CopyFilters(filters);
            relaxation.Apply(relaxed);
            candidates.Add((relaxation, relaxed, preferences, courses));
        }
        AddFilter(new ScheduleRelaxation.ClearEarliestStart(), filters.EarliestStart is not null);
        AddFilter(new ScheduleRelaxation.ClearLatestEnd(), filters.LatestEnd is not null);
        AddFilter(new ScheduleRelaxation.ClearFreeDays(), !filters.FreeDays.IsEmpty);
        AddFilter(new ScheduleRelaxation.AllowHonors(), !filters.IncludeHonors);
        AddFilter(new ScheduleRelaxation.AllowOnline(), !filters.IncludeOnline);
        AddFilter(new ScheduleRelaxation.AllowFullSections(), filters.HideFullSections || filters.MinSeats > 0);
        AddFilter(new ScheduleRelaxation.ClearCampus(), filters.Campus is not null);
        AddFilter(new ScheduleRelaxation.ClearCourseCount(), filters.CourseCount is not null);
        foreach (var relaxation in PreferenceRelaxation.Available(preferences))
        {
            var relaxed = preferences.Clone();
            relaxation.Apply(relaxed);
            candidates.Add((new ScheduleRelaxation.PreferenceChange(relaxation), filters, relaxed, courses));
        }
        if (includeCourseRelaxations)
        {
            for (var index = 0; index < courses.Count; index++)
            {
                var course = courses[index];
                if (!course.IsRequired || courses.Count <= 1) continue;
                var relaxedCourses = courses.Select(c => c.Clone()).ToList();
                relaxedCourses[index].IsRequired = false;
                candidates.Add((new ScheduleRelaxation.MakeOptional(course.Key), filters, preferences, relaxedCourses));
            }
        }

        var suggestions = new List<ScheduleSuggestion>();
        foreach (var (relaxation, relaxedFilters, relaxedPreferences, relaxedCourses) in candidates)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var probe = Generate(relaxedCourses, relaxedFilters, relaxedPreferences, ScheduleSortOption.Best, ScheduleLimits.Probe, cancellationToken);
            if (probe.TotalFound <= 0) continue;
            suggestions.Add(new ScheduleSuggestion(relaxation, probe.TotalFound, !probe.IsExhaustive));
        }
        return suggestions.OrderByDescending(s => s.ScheduleCount).ToList();
    }

    private static ScheduleGenerationResult Run(
        IReadOnlyList<ScheduleCourseInput> courses,
        ScheduleFilters filters,
        SchedulePreferences preferences,
        ScheduleSortOption sort,
        ScheduleLimits limits,
        CancellationToken cancellationToken)
    {
        var result = new ScheduleGenerationResult();
        var hasPreferences = !preferences.IsDefault;
        var kept = new List<List<CourseSection>>(courses.Count);
        var reports = new List<ScheduleDiagnostics.CourseReport>(courses.Count);
        foreach (var course in courses)
        {
            var remaining = new List<CourseSection>();
            var rejections = new Dictionary<ScheduleFilterReason, int>();
            foreach (var section in course.Sections)
            {
                var reasons = RejectionReasons(section, filters, preferences, course.ExcludedCRNs);
                if (reasons == 0)
                {
                    remaining.Add(section);
                }
                else
                {
                    foreach (var reason in Enum.GetValues<ScheduleFilterReason>())
                    {
                        if ((reasons & reason.Bit()) == 0) continue;
                        rejections[reason] = rejections.GetValueOrDefault(reason) + 1;
                    }
                }
            }
            kept.Add(remaining);
            reports.Add(new ScheduleDiagnostics.CourseReport
            {
                Key = course.Key,
                IsRequired = course.IsRequired,
                TotalSections = course.Sections.Count,
                RemainingSections = remaining.Count,
                Rejections = rejections,
            });
        }
        result.Diagnostics = new ScheduleDiagnostics
        {
            Filters = CopyFilters(filters),
            Preferences = preferences,
            Courses = reports,
        };

        var requiredIndexes = new List<int>();
        var optionalIndexes = new List<int>();
        for (var i = 0; i < courses.Count; i++)
        {
            if (courses[i].IsRequired) requiredIndexes.Add(i);
            else if (kept[i].Count > 0) optionalIndexes.Add(i);
        }
        int? target = filters.CourseCount is int count && count > 0 ? count : null;
        (int Lower, int Upper)? creditWindow = preferences.CreditTarget is CreditTarget credits ? (credits.Lower, credits.Upper) : null;

        if (courses.Count == 0) return result;
        if (requiredIndexes.Any(i => kept[i].Count == 0)) return result;
        if (target is int wanted)
        {
            if (requiredIndexes.Count > wanted)
            {
                result.Diagnostics.CountProblemKind = new ScheduleDiagnostics.CountProblem.TooManyRequired(requiredIndexes.Count, wanted);
                return result;
            }
            if (requiredIndexes.Count + optionalIndexes.Count < wanted)
            {
                result.Diagnostics.CountProblemKind = new ScheduleDiagnostics.CountProblem.TooFewCourses(requiredIndexes.Count + optionalIndexes.Count, wanted);
                return result;
            }
        }
        if (creditWindow is { } window)
        {
            var requiredCredits = requiredIndexes.Sum(i => courses[i].WholeMinCredits);
            if (requiredCredits > window.Upper)
            {
                result.Diagnostics.CountProblemKind = new ScheduleDiagnostics.CountProblem.CreditsOverTarget(requiredCredits, window.Upper);
                return result;
            }
            var available = requiredIndexes.Sum(i => courses[i].WholeMaxCredits) + optionalIndexes.Sum(i => courses[i].WholeMaxCredits);
            if (available < window.Lower)
            {
                result.Diagnostics.CountProblemKind = new ScheduleDiagnostics.CountProblem.CreditsUnderTarget(available, window.Lower);
                return result;
            }
        }

        List<int> Ordered(List<int> indexes) =>
            indexes.OrderBy(i => kept[i].Count).ThenBy(i => i).ToList();

        var searchOrder = Ordered(requiredIndexes).Concat(Ordered(optionalIndexes)).ToList();
        if (searchOrder.Count == 0) return result;

        var sections = new List<CourseSection>();
        var masks = new List<ScheduleSlotMask>();
        var weeklyMeetings = new List<Meeting[]>();
        var intervalStart = new List<int>();
        var intervals = new List<ScheduleInterval>();
        var sectionPoints = new List<double>();
        var rules = new ScheduleSearchRules();
        var buildings = new ScheduleBuildingTable();
        var slots = new List<ScheduleSearch.Slot>();
        foreach (var courseIndex in searchOrder)
        {
            var first = sections.Count;
            var candidates = new List<(CourseSection Section, List<ScheduleInterval> Intervals, double Points)>();
            foreach (var section in kept[courseIndex])
            {
                var own = ScheduleInterval.IntervalsFor(section, buildings);
                var points = hasPreferences ? SchedulePreferenceRules.SectionScore(section, own, preferences) : 0;
                candidates.Add((section, own, points));
            }
            if (hasPreferences)
            {
                candidates = candidates
                    .Select((candidate, offset) => (candidate, offset))
                    .OrderByDescending(item => item.candidate.Points)
                    .ThenBy(item => item.offset)
                    .Select(item => item.candidate)
                    .ToList();
            }
            foreach (var candidate in candidates)
            {
                sections.Add(candidate.Section);
                var weekly = candidate.Section.WeeklyMeetings.ToArray();
                weeklyMeetings.Add(weekly);
                masks.Add(new ScheduleSlotMask(weekly));
                intervalStart.Add(intervals.Count);
                intervals.AddRange(candidate.Intervals);
                sectionPoints.Add(candidate.Points);
                rules.Append(candidate.Intervals);
            }
            slots.Add(new ScheduleSearch.Slot(courseIndex, courses[courseIndex].IsRequired, first, sections.Count - first));
            rules.SlotMinCredits.Add(courses[courseIndex].WholeMinCredits);
            rules.SlotMaxCredits.Add(courses[courseIndex].WholeMaxCredits);
        }
        intervalStart.Add(intervals.Count);
        rules.MaxClassesPerDay = preferences.MaxClassesPerDay ?? int.MaxValue;
        rules.MaxMinutesPerDay = preferences.MaxMinutesPerDay ?? int.MaxValue;
        if (preferences.Breaks == BreakPreference.AtLeast && preferences.MinimumBreakStrength == PreferenceStrength.Required)
            rules.TravelMinutes = preferences.MinimumBreakMinutes;
        rules.CreditWindow = creditWindow;

        var collector = new ScheduleCollector(
            sort,
            Math.Max(1, limits.MaxResults),
            slots.Select(s => s.Start).ToArray(),
            intervalStart.ToArray(),
            intervals.ToArray(),
            sections.Select(s => Math.Max(0, s.SeatsRemaining)).ToArray(),
            sectionPoints.ToArray(),
            new ScheduleScoring.Weights(preferences),
            ScheduleScoring.MeasureOptions(preferences));
        var search = new ScheduleSearch(
            slots.ToArray(), masks.ToArray(), weeklyMeetings.ToArray(), sections.ToArray(),
            target, rules, limits, collector, Seed(sections), cancellationToken);

        search.RunExhaustive();
        if (search.Cancelled)
        {
            result.WasCancelled = true;
            return result;
        }
        var totalFound = search.LeafCount;
        var exhaustive = !search.HitLimit;
        if (search.HitLimit)
        {
            exhaustive = false;
            if (limits.AllowSampling && limits.SampleLeaves > 0)
            {
                result.WasSampled = true;
                search.RunSampled();
                if (search.Cancelled)
                {
                    result.WasCancelled = true;
                    return result;
                }
                totalFound = Math.Max(totalFound, search.LeafCount);
            }
        }
        result.TotalFound = totalFound;
        result.IsExhaustive = exhaustive;
        if (totalFound == 0)
        {
            if (exhaustive) result.Diagnostics.ConflictingPairs = ConflictingPairs(search, slots, courses);
            return result;
        }

        var schedules = new List<GeneratedSchedule>(collector.Count);
        foreach (var ranked in collector.Ranked())
        {
            var picked = new List<(int Course, int Id)>();
            for (var s = 0; s < slots.Count; s++)
            {
                var pick = ranked.Picks[s];
                if (pick == ScheduleSearch.SkipMarker) continue;
                picked.Add((slots[s].CourseIndex, slots[s].Start + pick));
            }
            picked.Sort((a, b) => a.Course.CompareTo(b.Course));
            var chosen = picked.Select(p => sections[p.Id]).ToList();
            var metrics = ranked.Metrics;
            schedules.Add(new GeneratedSchedule(
                chosen.Select(s => s.Crn).ToList(),
                chosen,
                metrics.CampusDays,
                metrics.ClassDays,
                metrics.Earliest == int.MaxValue ? null : metrics.Earliest,
                metrics.ClassDays == 0 ? null : metrics.Latest,
                metrics.Gap,
                metrics.MinSeats,
                metrics.TotalSeats,
                metrics.Full,
                metrics.Score,
                hasPreferences ? SchedulePreferenceRules.Reasons(chosen, preferences) : []));
        }
        result.Schedules = schedules;
        return result;
    }

    private static List<ScheduleDiagnostics.ConflictPair> ConflictingPairs(
        ScheduleSearch search, List<ScheduleSearch.Slot> slots, IReadOnlyList<ScheduleCourseInput> courses)
    {
        var pairs = new List<ScheduleDiagnostics.ConflictPair>();
        for (var a = 0; a < slots.Count; a++)
        {
            for (var b = a + 1; b < slots.Count; b++)
            {
                if (!slots[a].IsRequired || !slots[b].IsRequired) continue;
                if (search.HasCompatiblePair(a, b)) continue;
                pairs.Add(new ScheduleDiagnostics.ConflictPair
                {
                    First = courses[slots[a].CourseIndex].Key,
                    Second = courses[slots[b].CourseIndex].Key,
                    Reason = search.HasOverlapFreePair(a, b)
                        ? ScheduleDiagnostics.ConflictCause.TravelTime
                        : ScheduleDiagnostics.ConflictCause.Overlap,
                });
            }
        }
        return pairs;
    }

    internal static ScheduleFilters CopyFilters(ScheduleFilters filters) => new()
    {
        EarliestStart = filters.EarliestStart,
        LatestEnd = filters.LatestEnd,
        FreeDays = filters.FreeDays,
        IncludeHonors = filters.IncludeHonors,
        IncludeOnline = filters.IncludeOnline,
        HideFullSections = filters.HideFullSections,
        MinSeats = filters.MinSeats,
        Campus = filters.Campus,
        CourseCount = filters.CourseCount,
    };

    private static ulong Seed(List<CourseSection> sections)
    {
        ulong hash = 0xcbf29ce484222325;
        foreach (var section in sections)
        {
            foreach (var b in System.Text.Encoding.UTF8.GetBytes(section.Crn))
            {
                hash ^= b;
                hash *= 0x100000001b3;
            }
        }
        return hash;
    }

    private struct LeafMetrics
    {
        public int ClassDays;
        public int CampusDays;
        public int Earliest;
        public int Latest;
        public int Gap;
        public int MinSeats;
        public int TotalSeats;
        public int Full;
        public double Score;
    }

    private static bool Precedes(ScheduleSortOption sort, LeafMetrics a, LeafMetrics b, bool tieBreak)
    {
        switch (sort)
        {
            case ScheduleSortOption.FewestDays:
                if (a.CampusDays != b.CampusDays) return a.CampusDays < b.CampusDays;
                if (a.ClassDays != b.ClassDays) return a.ClassDays < b.ClassDays;
                if (a.Gap != b.Gap) return a.Gap < b.Gap;
                break;
            case ScheduleSortOption.LatestStart:
                if (a.Earliest != b.Earliest) return a.Earliest > b.Earliest;
                if (a.CampusDays != b.CampusDays) return a.CampusDays < b.CampusDays;
                break;
            case ScheduleSortOption.EarliestFinish:
                if (a.Latest != b.Latest) return a.Latest < b.Latest;
                if (a.CampusDays != b.CampusDays) return a.CampusDays < b.CampusDays;
                break;
            case ScheduleSortOption.FewestGaps:
                if (a.Gap != b.Gap) return a.Gap < b.Gap;
                if (a.CampusDays != b.CampusDays) return a.CampusDays < b.CampusDays;
                break;
            case ScheduleSortOption.MostSeats:
                if (a.MinSeats != b.MinSeats) return a.MinSeats > b.MinSeats;
                if (a.TotalSeats != b.TotalSeats) return a.TotalSeats > b.TotalSeats;
                break;
        }
        if (a.Score != b.Score) return a.Score > b.Score;
        return tieBreak;
    }

    private sealed class ScheduleCollector
    {
        private struct Entry
        {
            public LeafMetrics Metrics;
            public int Seq;
            public ulong Hash;
            public int Base;
        }

        private readonly ScheduleSortOption _sort;
        private readonly int _capacity;
        private readonly int _slotCount;
        private readonly int[] _slotStart;
        private readonly int[] _intervalStart;
        private readonly ScheduleInterval[] _intervals;
        private readonly int[] _seatsRemaining;
        private readonly double[] _sectionPoints;
        private readonly ScheduleScoring.Weights _weights;
        private readonly ScheduleMeasureOptions _options;
        private readonly ScheduleInterval[] _scratch;
        private readonly List<Entry> _entries = [];
        private readonly List<ushort> _pickStore = [];
        private readonly HashSet<ulong> _members = [];
        private int _nextSeq;

        public ScheduleCollector(
            ScheduleSortOption sort, int capacity, int[] slotStart, int[] intervalStart, ScheduleInterval[] intervals,
            int[] seatsRemaining, double[] sectionPoints, ScheduleScoring.Weights weights, ScheduleMeasureOptions options)
        {
            _sort = sort;
            _capacity = capacity;
            _slotStart = slotStart;
            _slotCount = slotStart.Length;
            _intervalStart = intervalStart;
            _intervals = intervals;
            _seatsRemaining = seatsRemaining;
            _sectionPoints = sectionPoints;
            _weights = weights;
            _options = options;
            var widest = 0;
            for (var s = 0; s < slotStart.Length; s++)
            {
                var lower = slotStart[s];
                var upper = s + 1 < slotStart.Length ? slotStart[s + 1] : seatsRemaining.Length;
                var slotWidest = 0;
                for (var g = lower; g < upper; g++)
                    slotWidest = Math.Max(slotWidest, intervalStart[g + 1] - intervalStart[g]);
                widest += slotWidest;
            }
            _scratch = new ScheduleInterval[Math.Max(1, widest)];
        }

        public int Count => _entries.Count;

        public void Offer(int[] picks)
        {
            var n = 0;
            var minSeats = int.MaxValue;
            var totalSeats = 0;
            var full = 0;
            var points = 0.0;
            for (var s = 0; s < _slotCount; s++)
            {
                var pick = picks[s];
                if (pick < 0) continue;
                var g = _slotStart[s] + pick;
                for (var k = _intervalStart[g]; k < _intervalStart[g + 1]; k++)
                    _scratch[n++] = _intervals[k];
                var seats = _seatsRemaining[g];
                minSeats = Math.Min(minSeats, seats);
                totalSeats += seats;
                if (seats == 0) full++;
                points += _sectionPoints[g];
            }
            var raw = ScheduleMeasure.Measure(_scratch, n, _options);
            var floor = minSeats == int.MaxValue ? 0 : minSeats;
            var metrics = new LeafMetrics
            {
                ClassDays = raw.ClassDays,
                CampusDays = raw.CampusDays,
                Earliest = raw.EarliestStart,
                Latest = raw.LatestEnd == int.MinValue ? 0 : raw.LatestEnd,
                Gap = raw.GapMinutes,
                MinSeats = floor,
                TotalSeats = totalSeats,
                Full = full,
                Score = _weights.Score(raw, full, floor) + points,
            };
            var seq = _nextSeq++;
            if (_entries.Count == _capacity && !Precedes(_sort, metrics, _entries[0].Metrics, false)) return;

            ulong hash = 0xcbf29ce484222325;
            for (var s = 0; s < _slotCount; s++)
            {
                hash ^= (ulong)(picks[s] + 1);
                hash *= 0x100000001b3;
            }
            if (!_members.Add(hash)) return;

            if (_entries.Count < _capacity)
            {
                var entryBase = _entries.Count * _slotCount;
                for (var s = 0; s < _slotCount; s++) _pickStore.Add(0);
                Store(picks, entryBase);
                _entries.Add(new Entry { Metrics = metrics, Seq = seq, Hash = hash, Base = entryBase });
                SiftUp(_entries.Count - 1);
            }
            else
            {
                var entryBase = _entries[0].Base;
                _members.Remove(_entries[0].Hash);
                Store(picks, entryBase);
                _entries[0] = new Entry { Metrics = metrics, Seq = seq, Hash = hash, Base = entryBase };
                SiftDown(0);
            }
        }

        public List<(LeafMetrics Metrics, ushort[] Picks)> Ranked()
        {
            var sorted = _entries
                .Select((entry, index) => (entry, index))
                .OrderBy(item => item, Comparer<(Entry Entry, int Index)>.Create((a, b) =>
                    Precedes(_sort, a.Entry.Metrics, b.Entry.Metrics, a.Entry.Seq < b.Entry.Seq) ? -1 :
                    Precedes(_sort, b.Entry.Metrics, a.Entry.Metrics, b.Entry.Seq < a.Entry.Seq) ? 1 : 0))
                .Select(item => item.entry)
                .ToList();
            var result = new List<(LeafMetrics, ushort[])>(sorted.Count);
            foreach (var entry in sorted)
            {
                var picks = new ushort[_slotCount];
                for (var s = 0; s < _slotCount; s++) picks[s] = _pickStore[entry.Base + s];
                result.Add((entry.Metrics, picks));
            }
            return result;
        }

        private void Store(int[] picks, int entryBase)
        {
            for (var s = 0; s < _slotCount; s++)
                _pickStore[entryBase + s] = picks[s] < 0 ? ScheduleSearch.SkipMarker : (ushort)picks[s];
        }

        private bool IsWorse(Entry a, Entry b) => Precedes(_sort, b.Metrics, a.Metrics, b.Seq < a.Seq);

        private void SiftUp(int index)
        {
            var child = index;
            while (child > 0)
            {
                var parent = (child - 1) / 2;
                if (!IsWorse(_entries[child], _entries[parent])) return;
                (_entries[child], _entries[parent]) = (_entries[parent], _entries[child]);
                child = parent;
            }
        }

        private void SiftDown(int index)
        {
            var parent = index;
            var n = _entries.Count;
            while (true)
            {
                var left = 2 * parent + 1;
                var right = left + 1;
                var worst = parent;
                if (left < n && IsWorse(_entries[left], _entries[worst])) worst = left;
                if (right < n && IsWorse(_entries[right], _entries[worst])) worst = right;
                if (worst == parent) return;
                (_entries[parent], _entries[worst]) = (_entries[worst], _entries[parent]);
                parent = worst;
            }
        }
    }

    private sealed class ScheduleSearchRules
    {
        public int MaxClassesPerDay = int.MaxValue;
        public int MaxMinutesPerDay = int.MaxValue;
        public int[] DayClasses = [];
        public int[] DayMinutes = [];
        public int TravelMinutes;
        public (int Lower, int Upper)? CreditWindow;
        public List<int> SlotMinCredits = [];
        public List<int> SlotMaxCredits = [];
        private readonly List<int> _dayClasses = [];
        private readonly List<int> _dayMinutes = [];

        public bool LimitsDays => MaxClassesPerDay != int.MaxValue || MaxMinutesPerDay != int.MaxValue;

        public void Append(List<ScheduleInterval> own)
        {
            Span<int> classes = stackalloc int[8];
            Span<int> minutes = stackalloc int[8];
            foreach (var interval in own)
            {
                classes[interval.Day] = 1;
                minutes[interval.Day] += interval.End - interval.Start;
            }
            for (var day = 0; day < 8; day++)
            {
                _dayClasses.Add(classes[day]);
                _dayMinutes.Add(minutes[day]);
            }
            DayClasses = _dayClasses.ToArray();
            DayMinutes = _dayMinutes.ToArray();
        }
    }

    private sealed class ScheduleSearch
    {
        public readonly struct Slot(int courseIndex, bool isRequired, int start, int size)
        {
            public int CourseIndex { get; } = courseIndex;
            public bool IsRequired { get; } = isRequired;
            public int Start { get; } = start;
            public int Size { get; } = size;
        }

        public const int Skip = -1;
        public const ushort SkipMarker = ushort.MaxValue;

        private readonly Slot[] _slots;
        private readonly int _slotCount;
        private readonly int[] _slotWords;
        private readonly int[] _domainOffset;
        private readonly int _domainTotal;
        private readonly int[] _compatOffset;
        private readonly ulong[] _compat;
        private readonly ulong[] _domains;
        private readonly int? _target;
        private readonly ScheduleSearchRules _rules;
        private readonly ScheduleSlotMask[] _masks;
        private readonly Meeting[][] _weekly;
        private readonly CourseSection[] _sections;
        private readonly ScheduleLimits _limits;
        private readonly ScheduleCollector _collector;
        private readonly bool _limitsDays;
        private readonly int[] _loadClasses = new int[8];
        private readonly int[] _loadMinutes = new int[8];
        private readonly CancellationToken _cancellation;
        private int _creditsMin;
        private int _creditsMax;
        private int _maxLeaves;
        private long _deadline;
        private readonly int[] _picks;
        private int _chosen;
        private int _nodes;
        private SplitMix64 _rng;

        public int LeafCount { get; private set; }
        public bool HitLimit { get; private set; }
        public bool Cancelled { get; private set; }
        private bool _aborted;

        public ScheduleSearch(
            Slot[] slots, ScheduleSlotMask[] masks, Meeting[][] weekly, CourseSection[] sections,
            int? target, ScheduleSearchRules rules, ScheduleLimits limits, ScheduleCollector collector, ulong seed,
            CancellationToken cancellationToken)
        {
            _slots = slots;
            _slotCount = slots.Length;
            _target = target;
            _rules = rules;
            _masks = masks;
            _weekly = weekly;
            _sections = sections;
            _limitsDays = rules.LimitsDays;
            _limits = limits;
            _collector = collector;
            _cancellation = cancellationToken;
            _rng = new SplitMix64(seed);
            _picks = new int[_slotCount];
            Array.Fill(_picks, Skip);

            _slotWords = slots.Select(slot => (slot.Size + 63) / 64).ToArray();
            _domainOffset = new int[_slotCount];
            var total = 0;
            for (var s = 0; s < _slotCount; s++)
            {
                _domainOffset[s] = total;
                total += _slotWords[s];
            }
            _domainTotal = Math.Max(1, total);

            var sectionTotal = masks.Length;
            _compatOffset = new int[Math.Max(1, sectionTotal * Math.Max(1, _slotCount))];
            var cursor = 0;
            for (var g = 0; g < sectionTotal; g++)
            {
                for (var s = 0; s < _slotCount; s++)
                {
                    _compatOffset[g * _slotCount + s] = cursor;
                    cursor += _slotWords[s];
                }
            }
            _compat = new ulong[Math.Max(1, cursor)];
            var slotOf = new int[sectionTotal];
            for (var s = 0; s < _slotCount; s++)
                for (var g = slots[s].Start; g < slots[s].Start + slots[s].Size; g++)
                    slotOf[g] = s;
            for (var g = 0; g < sectionTotal; g++)
            {
                var own = slotOf[g];
                for (var s = 0; s < _slotCount; s++)
                {
                    if (s == own) continue;
                    var compatBase = _compatOffset[g * _slotCount + s];
                    for (var j = 0; j < slots[s].Size; j++)
                    {
                        var h = slots[s].Start + j;
                        if (_masks[g].Conflicts(_masks[h]) && MeetingsConflict(_weekly[g], _weekly[h])) continue;
                        if (rules.TravelMinutes > 0 &&
                            SchedulePreferenceRules.TooClose(_sections[g], _weekly[g], _sections[h], _weekly[h], rules.TravelMinutes))
                            continue;
                        _compat[compatBase + (j >> 6)] |= 1UL << (j & 63);
                    }
                }
            }
            _domains = new ulong[(_slotCount + 1) * _domainTotal];
        }

        public bool HasCompatiblePair(int slotA, int slotB)
        {
            for (var j = 0; j < _slots[slotA].Size; j++)
            {
                var g = _slots[slotA].Start + j;
                var compatBase = _compatOffset[g * _slotCount + slotB];
                for (var k = 0; k < _slotWords[slotB]; k++)
                    if (_compat[compatBase + k] != 0) return true;
            }
            return false;
        }

        public bool HasOverlapFreePair(int slotA, int slotB)
        {
            if (_rules.TravelMinutes <= 0) return HasCompatiblePair(slotA, slotB);
            for (var i = 0; i < _slots[slotA].Size; i++)
            {
                for (var j = 0; j < _slots[slotB].Size; j++)
                {
                    var g = _slots[slotA].Start + i;
                    var h = _slots[slotB].Start + j;
                    if (!(_masks[g].Conflicts(_masks[h]) && MeetingsConflict(_weekly[g], _weekly[h]))) return true;
                }
            }
            return false;
        }

        public void RunExhaustive()
        {
            Reset(Math.Max(1, _limits.MaxExhaustiveLeaves), _limits.TimeBudget);
            DescendExhaustive(0);
            if (Cancelled) _aborted = true;
        }

        public void RunSampled()
        {
            Reset(Math.Max(1, _limits.SampleLeaves), _limits.SampleTimeBudget);
            DescendSampled(0, _maxLeaves);
            if (Cancelled) _aborted = true;
        }

        private void Reset(int maxLeaves, double timeBudget)
        {
            _maxLeaves = Math.Max(1, maxLeaves);
            LeafCount = 0;
            HitLimit = false;
            _aborted = false;
            _nodes = 0;
            _chosen = 0;
            Array.Clear(_loadClasses);
            Array.Clear(_loadMinutes);
            _creditsMin = 0;
            _creditsMax = 0;
            Array.Fill(_picks, Skip);
            _deadline = Stopwatch.GetTimestamp() + (long)(Math.Max(0.05, timeBudget) * Stopwatch.Frequency);
            Array.Clear(_domains);
            for (var s = 0; s < _slotCount; s++)
            {
                var off = _domainOffset[s];
                var size = _slots[s].Size;
                for (var w = 0; w < _slotWords[s]; w++)
                {
                    var remaining = size - w * 64;
                    _domains[off + w] = remaining >= 64 ? ulong.MaxValue : (1UL << remaining) - 1;
                }
            }
        }

        private void CheckBudget()
        {
            if (_cancellation.IsCancellationRequested)
            {
                Cancelled = true;
                _aborted = true;
            }
            else if (_nodes >= _limits.MaxNodes || Stopwatch.GetTimestamp() >= _deadline)
            {
                HitLimit = true;
                _aborted = true;
            }
        }

        private bool Choose(int depth, int local)
        {
            var g = _slots[depth].Start + local;
            var src = depth * _domainTotal;
            var dst = (depth + 1) * _domainTotal;
            for (var s = 0; s < _slotCount; s++)
            {
                var off = _domainOffset[s];
                var words = _slotWords[s];
                if (s == depth)
                {
                    for (var k = 0; k < words; k++) _domains[dst + off + k] = _domains[src + off + k];
                    continue;
                }
                var compatBase = _compatOffset[g * _slotCount + s];
                ulong any = 0;
                for (var k = 0; k < words; k++)
                {
                    var value = _domains[src + off + k] & _compat[compatBase + k];
                    _domains[dst + off + k] = value;
                    any |= value;
                }
                if (any == 0 && s > depth && _slots[s].IsRequired) return false;
            }
            return true;
        }

        private void CopyDomain(int depth) =>
            Array.Copy(_domains, depth * _domainTotal, _domains, (depth + 1) * _domainTotal, _domainTotal);

        private bool CanAdd(int g, int slot)
        {
            if (_limitsDays)
            {
                var row = g * 8;
                for (var day = 0; day < 7; day++)
                {
                    if (_loadClasses[day] + _rules.DayClasses[row + day] > _rules.MaxClassesPerDay) return false;
                    if (_loadMinutes[day] + _rules.DayMinutes[row + day] > _rules.MaxMinutesPerDay) return false;
                }
            }
            if (_rules.CreditWindow is { } window && _creditsMin + _rules.SlotMinCredits[slot] > window.Upper) return false;
            return true;
        }

        private void Take(int g, int slot)
        {
            if (_limitsDays)
            {
                var row = g * 8;
                for (var day = 0; day < 8; day++)
                {
                    _loadClasses[day] += _rules.DayClasses[row + day];
                    _loadMinutes[day] += _rules.DayMinutes[row + day];
                }
            }
            _creditsMin += _rules.SlotMinCredits[slot];
            _creditsMax += _rules.SlotMaxCredits[slot];
        }

        private void Release(int g, int slot)
        {
            if (_limitsDays)
            {
                var row = g * 8;
                for (var day = 0; day < 8; day++)
                {
                    _loadClasses[day] -= _rules.DayClasses[row + day];
                    _loadMinutes[day] -= _rules.DayMinutes[row + day];
                }
            }
            _creditsMin -= _rules.SlotMinCredits[slot];
            _creditsMax -= _rules.SlotMaxCredits[slot];
        }

        private bool CanStillAdd(int depth, int slot)
        {
            var dom = depth * _domainTotal;
            var off = _domainOffset[slot];
            var simple = !_limitsDays && _rules.CreditWindow is null;
            for (var k = 0; k < _slotWords[slot]; k++)
            {
                var bits = _domains[dom + off + k];
                if (simple)
                {
                    if (bits != 0) return true;
                    continue;
                }
                while (bits != 0)
                {
                    var local = k * 64 + BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    if (CanAdd(_slots[slot].Start + local, slot)) return true;
                }
            }
            return false;
        }

        private bool IsFeasible(int depth, int first)
        {
            if (_target is null && _rules.CreditWindow is null) return true;
            var reachable = _chosen;
            var most = _creditsMax;
            var least = _creditsMin;
            for (var s = first; s < _slotCount; s++)
            {
                var required = _slots[s].IsRequired;
                if (required || CanStillAdd(depth, s))
                {
                    reachable++;
                    most += _rules.SlotMaxCredits[s];
                }
                if (required) least += _rules.SlotMinCredits[s];
            }
            if (_target is int wanted && reachable < wanted) return false;
            if (_rules.CreditWindow is { } window && (most < window.Lower || least > window.Upper)) return false;
            return true;
        }

        private bool RecordLeaf(int depth)
        {
            if (_chosen == 0) return false;
            if (_target is int wanted && _chosen != wanted) return false;
            if (_rules.CreditWindow is { } window && (_creditsMin > window.Upper || _creditsMax < window.Lower)) return false;
            if (_target is null && _rules.CreditWindow is null)
            {
                for (var s = 0; s < _slotCount; s++)
                    if (!_slots[s].IsRequired && _picks[s] == Skip && CanStillAdd(depth, s))
                        return false;
            }
            _collector.Offer(_picks);
            LeafCount++;
            return true;
        }

        private void DescendExhaustive(int depth)
        {
            if (_aborted) return;
            _nodes++;
            if ((_nodes & 0x3FF) == 0)
            {
                CheckBudget();
                if (_aborted) return;
            }
            if (depth == _slotCount)
            {
                if (RecordLeaf(depth) && LeafCount >= _maxLeaves)
                {
                    HitLimit = true;
                    _aborted = true;
                }
                return;
            }
            if (_target is int filled && _chosen == filled)
            {
                for (var s = depth; s < _slotCount; s++) _picks[s] = Skip;
                if (RecordLeaf(depth) && LeafCount >= _maxLeaves)
                {
                    HitLimit = true;
                    _aborted = true;
                }
                return;
            }
            if (!IsFeasible(depth, depth)) return;
            var slot = _slots[depth];
            if (!slot.IsRequired && IsFeasible(depth, depth + 1))
            {
                _picks[depth] = Skip;
                CopyDomain(depth);
                DescendExhaustive(depth + 1);
                if (_aborted) return;
            }
            var dom = depth * _domainTotal;
            var off = _domainOffset[depth];
            for (var word = 0; word < _slotWords[depth]; word++)
            {
                var bits = _domains[dom + off + word];
                while (bits != 0)
                {
                    var local = word * 64 + BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    var g = slot.Start + local;
                    if (!CanAdd(g, depth) || !Choose(depth, local)) continue;
                    Take(g, depth);
                    _picks[depth] = local;
                    _chosen++;
                    DescendExhaustive(depth + 1);
                    _chosen--;
                    _picks[depth] = Skip;
                    Release(g, depth);
                    if (_aborted) return;
                }
            }
        }

        private int DescendSampled(int depth, int budget)
        {
            if (_aborted) return 0;
            _nodes++;
            if ((_nodes & 0x3FF) == 0)
            {
                CheckBudget();
                if (_aborted) return 0;
            }
            if (depth == _slotCount) return RecordLeaf(depth) ? 1 : 0;
            if (_target is int filled && _chosen == filled)
            {
                for (var s = depth; s < _slotCount; s++) _picks[s] = Skip;
                return RecordLeaf(depth) ? 1 : 0;
            }
            if (!IsFeasible(depth, depth)) return 0;

            var slot = _slots[depth];
            var options = new List<int>();
            if (!slot.IsRequired && IsFeasible(depth, depth + 1)) options.Add(Skip);
            var dom = depth * _domainTotal;
            var off = _domainOffset[depth];
            for (var word = 0; word < _slotWords[depth]; word++)
            {
                var bits = _domains[dom + off + word];
                while (bits != 0)
                {
                    var local = word * 64 + BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    if (CanAdd(slot.Start + local, depth)) options.Add(local);
                }
            }
            if (options.Count == 0) return 0;
            Shuffle(options, ref _rng);

            var remaining = budget;
            var left = options.Count;
            var emitted = 0;
            foreach (var option in options)
            {
                if (_aborted || remaining <= 0) break;
                var share = Math.Max(1, (remaining + left - 1) / left);
                left--;
                var got = 0;
                if (option == Skip)
                {
                    _picks[depth] = Skip;
                    CopyDomain(depth);
                    got = DescendSampled(depth + 1, share);
                }
                else if (Choose(depth, option))
                {
                    var g = slot.Start + option;
                    Take(g, depth);
                    _picks[depth] = option;
                    _chosen++;
                    got = DescendSampled(depth + 1, share);
                    _chosen--;
                    _picks[depth] = Skip;
                    Release(g, depth);
                }
                emitted += got;
                remaining -= got;
            }
            return emitted;
        }

        private static bool MeetingsConflict(Meeting[] a, Meeting[] b)
        {
            foreach (var first in a)
                foreach (var second in b)
                    if (first.Overlaps(second)) return true;
            return false;
        }

        private static void Shuffle(List<int> values, ref SplitMix64 rng)
        {
            var amount = values.Count;
            var current = 0;
            while (amount > 1)
            {
                var random = rng.NextInt(amount);
                amount--;
                (values[current], values[current + random]) = (values[current + random], values[current]);
                current++;
            }
        }
    }

    private struct SplitMix64(ulong state)
    {
        public ulong State = state;

        public ulong Next()
        {
            State += 0x9E3779B97F4A7C15;
            var z = State;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }

        public int NextInt(int upperBound)
        {
            if (upperBound <= 1) return 0;
            var bound = (ulong)upperBound;
            while (true)
            {
                var random = Next();
                var high = Math.BigMul(random, bound, out var low);
                if (low < bound)
                {
                    var threshold = (0UL - bound) % bound;
                    if (low < threshold) continue;
                }
                return (int)high;
            }
        }
    }
}
