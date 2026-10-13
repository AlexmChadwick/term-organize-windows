// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// Portions derived from SearchNEU where the Swift file says so. Modified 2026-10-13: ported from Swift to C#.

namespace TermOrganize.Core;

/// <summary>Maps building codes to small numbers so intervals can say "same building" cheaply.</summary>
public sealed class ScheduleBuildingTable
{
    private readonly Dictionary<string, ushort> _ids = new(StringComparer.Ordinal);

    /// <summary>0 means unknown (no building listed).</summary>
    public ushort IdFor(Meeting meeting)
    {
        var code = meeting.BuildingCode ?? meeting.Building;
        if (string.IsNullOrEmpty(code)) return 0;
        if (_ids.TryGetValue(code, out var known)) return known;
        var next = (ushort)(_ids.Count + 1);
        _ids[code] = next;
        return next;
    }
}

/// <summary>A weekly time block of a section on a single day.</summary>
public readonly record struct ScheduleInterval(byte Day, bool InPerson, short Start, short End, ushort Building = 0)
{
    public static List<ScheduleInterval> IntervalsFor(CourseSection section) =>
        IntervalsFor(section, null);

    public static List<ScheduleInterval> IntervalsFor(CourseSection section, ScheduleBuildingTable? buildings)
    {
        var result = new List<ScheduleInterval>();
        var seen = new HashSet<ScheduleInterval>();
        foreach (var meeting in section.WeeklyMeetings)
        {
            if (meeting.Start is not int start || meeting.End is not int end) continue;
            var inPerson = !(section.IsOnline || meeting.IsOnline);
            var place = inPerson && buildings is not null ? buildings.IdFor(meeting) : (ushort)0;
            foreach (var day in meeting.Days.Indices)
            {
                var interval = new ScheduleInterval((byte)day, inPerson, Clamp(start), Clamp(end), place);
                if (seen.Add(interval)) result.Add(interval);
            }
        }
        return result;
    }

    private static short Clamp(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);
}

/// <summary>What to count besides the basics when measuring a schedule. The defaults count nothing extra.</summary>
public struct ScheduleMeasureOptions : IEquatable<ScheduleMeasureOptions>
{
    /// <summary>Count classes with less than this many minutes between them (0 = don't).</summary>
    public int BackToBackBelow;

    /// <summary>Count in-person classes in different buildings with less than this many minutes between them (0 = don't).</summary>
    public int TravelMinutes;

    /// <summary>Count class days on which the window has no free stretch of <see cref="LunchBreak.Minutes"/>.</summary>
    public LunchBreak? Lunch;

    public static ScheduleMeasureOptions None => default;

    public bool Equals(ScheduleMeasureOptions other) =>
        BackToBackBelow == other.BackToBackBelow && TravelMinutes == other.TravelMinutes && Equals(Lunch, other.Lunch);

    public override bool Equals(object? obj) => obj is ScheduleMeasureOptions other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(BackToBackBelow, TravelMinutes, Lunch);
}

/// <summary>The raw numbers measured from a set of weekly intervals.</summary>
public sealed class ScheduleMeasure
{
    public int ClassDays { get; set; }
    public int CampusDays { get; set; }
    public int EarliestStart { get; set; } = int.MaxValue;
    public int LatestEnd { get; set; } = int.MinValue;
    public int GapMinutes { get; set; }
    /// <summary>Bit 0 is Sunday: the days with any timed class.</summary>
    public byte ClassDayMask { get; set; }
    public int MaxDayMinutes { get; set; }
    public int TotalMinutes { get; set; }
    public int BackToBack { get; set; }
    public int TightTransitions { get; set; }
    public int LunchMisses { get; set; }

    /// <summary>Breaks shorter than this are passing time, not gaps.</summary>
    public const int GapThresholdMinutes = 20;

    public static ScheduleMeasure Measure(ScheduleInterval[] intervals, int count, ScheduleMeasureOptions options = default)
    {
        var n = count;
        for (var i = 1; i < n; i++)
        {
            var x = intervals[i];
            var j = i - 1;
            while (j >= 0 && (intervals[j].Day > x.Day || (intervals[j].Day == x.Day && intervals[j].Start > x.Start)))
            {
                intervals[j + 1] = intervals[j];
                j--;
            }
            intervals[j + 1] = x;
        }

        var result = new ScheduleMeasure();
        var k = 0;
        while (k < n)
        {
            var day = intervals[k].Day;
            var runEnd = (int)intervals[k].End;
            var onCampus = intervals[k].InPerson;
            var dayMinutes = intervals[k].End - intervals[k].Start;
            var previous = intervals[k];
            result.EarliestStart = Math.Min(result.EarliestStart, intervals[k].Start);
            result.LatestEnd = Math.Max(result.LatestEnd, intervals[k].End);
            var m = k + 1;
            while (m < n && intervals[m].Day == day)
            {
                var current = intervals[m];
                var start = (int)current.Start;
                var gap = start - runEnd;
                if (gap >= GapThresholdMinutes) result.GapMinutes += gap;
                if (gap >= 0)
                {
                    if (gap < options.BackToBackBelow) result.BackToBack++;
                    if (gap < options.TravelMinutes && previous.InPerson && current.InPerson &&
                        (previous.Building == 0 || current.Building == 0 || previous.Building != current.Building))
                        result.TightTransitions++;
                }
                runEnd = Math.Max(runEnd, current.End);
                dayMinutes += current.End - start;
                result.EarliestStart = Math.Min(result.EarliestStart, start);
                result.LatestEnd = Math.Max(result.LatestEnd, current.End);
                if (current.InPerson) onCampus = true;
                previous = current;
                m++;
            }
            if (options.Lunch is LunchBreak lunch && !HasFreeStretch(intervals, k, m - k, lunch)) result.LunchMisses++;
            result.ClassDays++;
            result.ClassDayMask |= (byte)(1 << day);
            if (onCampus) result.CampusDays++;
            result.MaxDayMinutes = Math.Max(result.MaxDayMinutes, dayMinutes);
            result.TotalMinutes += dayMinutes;
            k = m;
        }
        return result;
    }

    public static ScheduleMeasure Measure(IReadOnlyList<ScheduleInterval> intervals, ScheduleMeasureOptions options = default)
    {
        var copy = new ScheduleInterval[intervals.Count];
        for (var i = 0; i < intervals.Count; i++) copy[i] = intervals[i];
        return Measure(copy, copy.Length, options);
    }

    private static bool HasFreeStretch(ScheduleInterval[] intervals, int offset, int count, LunchBreak lunch)
    {
        var cursor = lunch.Start;
        for (var i = 0; i < count; i++)
        {
            var start = (int)intervals[offset + i].Start;
            var end = (int)intervals[offset + i].End;
            if (!(end > lunch.Start && start < lunch.End)) continue;
            if (start > cursor && Math.Min(start, lunch.End) - cursor >= lunch.Minutes) return true;
            cursor = Math.Max(cursor, Math.Min(end, lunch.End));
        }
        return lunch.End - cursor >= lunch.Minutes;
    }
}

/// <summary>Turns measurements into one number; higher is better. The score starts at 100.</summary>
public static class ScheduleScoring
{
    public sealed class Weights : IEquatable<Weights>
    {
        public double Base = 100;
        public double PerCampusDay = 8;
        public double PerOnlineDay = 2;
        public double GapDivisor = 10;
        public bool ComfortHours = true;
        public double PerFullSection = 6;
        public double LowSeatsPenalty = 2;
        public double PerClassDayBonus;
        public double PerImbalanceMinute;
        public double PerDayOffMissed;
        public DaySet DaysOff;
        public double PerBackToBack;
        public double PerTightTransition;
        public double PerLunchMiss;

        public Weights() { }

        public Weights(SchedulePreferences preferences)
        {
            if (preferences.HasTimeOfDayPreference || preferences.EarliestStart is not null || preferences.LatestEnd is not null)
                ComfortHours = false;
            switch (preferences.DayLayout)
            {
                case DayLayout.FewerDays:
                    PerCampusDay = 14;
                    break;
                case DayLayout.SpreadOut:
                    PerCampusDay = 0;
                    PerOnlineDay = 0;
                    PerClassDayBonus = 6;
                    PerImbalanceMinute = 0.1;
                    break;
            }
            switch (preferences.Gaps)
            {
                case GapPreference.NoPreference:
                    if (preferences.Lunch is not null) GapDivisor = 20;
                    break;
                case GapPreference.Compact:
                    GapDivisor = 10.0 / 3.0;
                    break;
                case GapPreference.Fine:
                    GapDivisor = double.PositiveInfinity;
                    break;
            }
            if (preferences.Seats == SeatPolicy.WaitlistOK) PerFullSection = 3;
            if (!preferences.DaysOff.IsEmpty && preferences.DaysOffStrength == PreferenceStrength.Preferred)
            {
                DaysOff = preferences.DaysOff;
                PerDayOffMissed = 12;
            }
            switch (preferences.Breaks)
            {
                case BreakPreference.AvoidBackToBack:
                    PerBackToBack = 10;
                    break;
                case BreakPreference.AtLeast:
                    if (preferences.MinimumBreakStrength == PreferenceStrength.Preferred) PerTightTransition = 10;
                    break;
            }
            if (preferences.Lunch is not null) PerLunchMiss = 8;
        }

        public double Score(ScheduleMeasure measure, int fullSections, int minSeats)
        {
            var score = Base;
            score -= PerCampusDay * measure.CampusDays;
            score -= PerOnlineDay * Math.Max(0, measure.ClassDays - measure.CampusDays);
            score -= measure.GapMinutes / GapDivisor;
            if (ComfortHours)
            {
                if (measure.EarliestStart != int.MaxValue && measure.EarliestStart < 9 * 60)
                    score -= (9 * 60 - measure.EarliestStart) / 15.0;
                if (measure.LatestEnd != int.MinValue && measure.LatestEnd > 17 * 60)
                    score -= (measure.LatestEnd - 17 * 60) / 20.0;
            }
            score -= PerFullSection * fullSections;
            if (fullSections == 0 && minSeats <= 3) score -= LowSeatsPenalty;

            if (PerClassDayBonus != 0) score += PerClassDayBonus * measure.ClassDays;
            if (PerImbalanceMinute != 0 && measure.ClassDays > 0)
            {
                var average = (double)measure.TotalMinutes / measure.ClassDays;
                score -= PerImbalanceMinute * Math.Max(0, measure.MaxDayMinutes - average);
            }
            if (PerDayOffMissed != 0)
                score -= PerDayOffMissed * System.Numerics.BitOperations.PopCount((uint)(measure.ClassDayMask & DaysOff.Raw));
            if (PerBackToBack != 0) score -= PerBackToBack * measure.BackToBack;
            if (PerTightTransition != 0) score -= PerTightTransition * measure.TightTransitions;
            if (PerLunchMiss != 0) score -= PerLunchMiss * measure.LunchMisses;
            return score;
        }

        public bool Equals(Weights? other) =>
            other is not null && Base == other.Base && PerCampusDay == other.PerCampusDay && PerOnlineDay == other.PerOnlineDay &&
            GapDivisor == other.GapDivisor && ComfortHours == other.ComfortHours && PerFullSection == other.PerFullSection &&
            LowSeatsPenalty == other.LowSeatsPenalty && PerClassDayBonus == other.PerClassDayBonus &&
            PerImbalanceMinute == other.PerImbalanceMinute && PerDayOffMissed == other.PerDayOffMissed && DaysOff == other.DaysOff &&
            PerBackToBack == other.PerBackToBack && PerTightTransition == other.PerTightTransition && PerLunchMiss == other.PerLunchMiss;

        public override bool Equals(object? obj) => Equals(obj as Weights);
        public override int GetHashCode() => HashCode.Combine(Base, PerCampusDay, GapDivisor, ComfortHours, PerFullSection);
    }

    public static double Score(ScheduleMeasure measure, int fullSections, int minSeats) =>
        new Weights().Score(measure, fullSections, minSeats);

    public static ScheduleMeasureOptions MeasureOptions(SchedulePreferences preferences)
    {
        var options = new ScheduleMeasureOptions();
        switch (preferences.Breaks)
        {
            case BreakPreference.AvoidBackToBack:
                options.BackToBackBelow = SchedulePreferences.BackToBackMinutes;
                break;
            case BreakPreference.AtLeast:
                if (preferences.MinimumBreakStrength == PreferenceStrength.Preferred)
                    options.TravelMinutes = preferences.MinimumBreakMinutes;
                break;
        }
        options.Lunch = preferences.Lunch;
        return options;
    }

    public const double PreferredBandPerHour = 3;
    public const double AvoidedBandPerHour = -6;
    public const double SoftLimitBase = 4;
    public const double SoftLimitCap = 20;
    public const double SoftBlockedPerMeeting = 15;
    public const double FormatMismatchPerSection = 8;
    public const double CampusMismatchPerSection = 10;
    public const double PreferredInstructorPerSection = 5;
    public const double AvoidedInstructorPerSection = -15;
}
