// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text.Json.Serialization;

namespace TermOrganize.Core;

public enum PreferenceStrength
{
    Preferred,
    Required,
}

public static class PreferenceStrengthInfo
{
    public static string Title(this PreferenceStrength strength) => strength switch
    {
        PreferenceStrength.Preferred => "If possible",
        PreferenceStrength.Required => "Always",
        _ => "",
    };
}

public enum TimeBand
{
    EarlyMorning,
    Morning,
    Midday,
    Afternoon,
    Evening,
    Night,
}

public static class TimeBandInfo
{
    public static (int Start, int End) Minutes(this TimeBand band) => band switch
    {
        TimeBand.EarlyMorning => (0, 9 * 60),
        TimeBand.Morning => (9 * 60, 12 * 60),
        TimeBand.Midday => (12 * 60, 14 * 60),
        TimeBand.Afternoon => (14 * 60, 17 * 60),
        TimeBand.Evening => (17 * 60, 19 * 60),
        TimeBand.Night => (19 * 60, 24 * 60),
        _ => (0, 0),
    };

    public static string Title(this TimeBand band) => band switch
    {
        TimeBand.EarlyMorning => "Early morning",
        TimeBand.Morning => "Morning",
        TimeBand.Midday => "Midday",
        TimeBand.Afternoon => "Afternoon",
        TimeBand.Evening => "Evening",
        TimeBand.Night => "Night",
        _ => "",
    };

    public static string Detail(this TimeBand band) => band switch
    {
        TimeBand.EarlyMorning => "Before 9 AM",
        TimeBand.Morning => "9 AM – noon",
        TimeBand.Midday => "Noon – 2 PM",
        TimeBand.Afternoon => "2 – 5 PM",
        TimeBand.Evening => "5 – 7 PM",
        TimeBand.Night => "After 7 PM",
        _ => "",
    };

    public static int Overlap(this TimeBand band, int start, int end)
    {
        var (lo, hi) = band.Minutes();
        return Math.Max(0, Math.Min(end, hi) - Math.Max(start, lo));
    }
}

public enum TimePreference
{
    Avoid,
    Okay,
    Prefer,
}

public static class TimePreferenceInfo
{
    public static string Title(this TimePreference preference) => preference switch
    {
        TimePreference.Avoid => "Avoid",
        TimePreference.Okay => "OK",
        TimePreference.Prefer => "Prefer",
        _ => "",
    };
}

public sealed class TimeLimit : IEquatable<TimeLimit>
{
    public int Minutes { get; set; }
    public PreferenceStrength Strength { get; set; } = PreferenceStrength.Required;
    public TimeLimit() { }
    public TimeLimit(int minutes, PreferenceStrength strength = PreferenceStrength.Required)
    {
        Minutes = minutes;
        Strength = strength;
    }
    public bool Equals(TimeLimit? other) => other is not null && Minutes == other.Minutes && Strength == other.Strength;
    public override bool Equals(object? obj) => Equals(obj as TimeLimit);
    public override int GetHashCode() => HashCode.Combine(Minutes, Strength);
}

public enum DayLayout { NoPreference, FewerDays, SpreadOut }
public enum GapPreference { Compact, NoPreference, Fine }
public enum BreakPreference { Fine, AvoidBackToBack, AtLeast }

public static class DayLayoutInfo
{
    public static string Title(this DayLayout layout) => layout switch
    {
        DayLayout.NoPreference => "No preference",
        DayLayout.FewerDays => "Fewer days on campus",
        DayLayout.SpreadOut => "Spread out evenly",
        _ => "",
    };
}

public static class GapPreferenceInfo
{
    public static string Title(this GapPreference gaps) => gaps switch
    {
        GapPreference.Compact => "Keep short",
        GapPreference.NoPreference => "No preference",
        GapPreference.Fine => "Fine",
        _ => "",
    };
}

public static class BreakPreferenceInfo
{
    public static string Title(this BreakPreference breaks) => breaks switch
    {
        BreakPreference.Fine => "Fine",
        BreakPreference.AvoidBackToBack => "Avoid",
        BreakPreference.AtLeast => "Time to walk",
        _ => "",
    };
}

public sealed class LunchBreak : IEquatable<LunchBreak>
{
    public int Start { get; set; } = 11 * 60 + 30;
    public int End { get; set; } = 14 * 60;
    public int Minutes { get; set; } = 30;
    public bool Equals(LunchBreak? other) => other is not null && Start == other.Start && End == other.End && Minutes == other.Minutes;
    public override bool Equals(object? obj) => Equals(obj as LunchBreak);
    public override int GetHashCode() => HashCode.Combine(Start, End, Minutes);
}

public sealed class BlockedTime : IEquatable<BlockedTime>
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public DaySet Days { get; set; }
    public int Start { get; set; }
    public int End { get; set; }
    public PreferenceStrength Strength { get; set; } = PreferenceStrength.Required;
    public bool IsEnabled { get; set; } = true;

    [JsonIgnore] public bool IsValid => !Days.IsEmpty && End > Start && Start >= 0 && End <= 24 * 60;
    [JsonIgnore] public bool IsActive => IsEnabled && IsValid;
    [JsonIgnore] public string Summary => $"{Days.ShortLabel} {TimeOfDay.RangeLabel(Start, End)}";
    [JsonIgnore]
    public string ListDetail
    {
        get
        {
            if (!IsValid) return Days.IsEmpty ? "Choose the days" : "Check the times";
            var text = Summary;
            if (!IsEnabled) text += " · Off";
            else if (Strength == PreferenceStrength.Preferred) text += " · If I must";
            return text;
        }
    }

    public bool Overlaps(DaySet meetingDays, int meetingStart, int meetingEnd) =>
        !Days.IsDisjoint(meetingDays) && meetingStart < End && Start < meetingEnd;

    public bool Equals(BlockedTime? other) => other is not null && Id == other.Id && Label == other.Label &&
        Days == other.Days && Start == other.Start && End == other.End && Strength == other.Strength && IsEnabled == other.IsEnabled;
    public override bool Equals(object? obj) => Equals(obj as BlockedTime);
    public override int GetHashCode() => HashCode.Combine(Id, Label, Days, Start, End, Strength, IsEnabled);
}

public enum FormatPreference { NoPreference, InPerson, Hybrid, Online }
public enum SeatPolicy { Any, WaitlistOK, OpenOnly }

public static class FormatPreferenceInfo
{
    public static string Title(this FormatPreference format) => format switch
    {
        FormatPreference.NoPreference => "No preference",
        FormatPreference.InPerson => "In person",
        FormatPreference.Hybrid => "Hybrid",
        FormatPreference.Online => "Online",
        _ => "",
    };
}

public static class SeatPolicyInfo
{
    public static string Title(this SeatPolicy seats) => seats switch
    {
        SeatPolicy.Any => "Don't filter",
        SeatPolicy.WaitlistOK => "Open or waitlist",
        SeatPolicy.OpenOnly => "Open seats only",
        _ => "",
    };
}

public sealed class CreditTarget : IEquatable<CreditTarget>
{
    public int Lower { get; set; }
    public int Upper { get; set; }
    public CreditTarget() { }
    public CreditTarget(int lower, int upper) { Lower = lower; Upper = Math.Max(lower, upper); }
    [JsonIgnore] public string Label => Lower == Upper ? $"{Lower} credits" : $"{Lower}\u2013{Upper} credits";
    public bool Equals(CreditTarget? other) => other is not null && Lower == other.Lower && Upper == other.Upper;
    public override bool Equals(object? obj) => Equals(obj as CreditTarget);
    public override int GetHashCode() => HashCode.Combine(Lower, Upper);
}

public enum SectionFormat { InPerson, Hybrid, Online, Other }

public static class SectionFormatInfo
{
    public static double Mismatch(this SectionFormat format, FormatPreference preference) => (preference, format) switch
    {
        (FormatPreference.NoPreference, _) or (_, SectionFormat.Other) => 0,
        (FormatPreference.InPerson, SectionFormat.InPerson) or (FormatPreference.Online, SectionFormat.Online) or (FormatPreference.Hybrid, SectionFormat.Hybrid) => 0,
        (FormatPreference.InPerson, SectionFormat.Hybrid) or (FormatPreference.Online, SectionFormat.Hybrid) => 0.5,
        (FormatPreference.Hybrid, SectionFormat.InPerson) or (FormatPreference.Hybrid, SectionFormat.Online) => 0.5,
        (FormatPreference.InPerson, SectionFormat.Online) or (FormatPreference.Online, SectionFormat.InPerson) => 1,
        _ => 0,
    };
}

/// <summary>"Your ideal week." An empty value has no opinions.</summary>
public sealed class SchedulePreferences : IEquatable<SchedulePreferences>
{
    public const int BackToBackMinutes = 30;

    public Dictionary<TimeBand, TimePreference> TimeOfDay { get; set; } = [];
    public TimeLimit? EarliestStart { get; set; }
    public TimeLimit? LatestEnd { get; set; }
    public DaySet DaysOff { get; set; }
    public PreferenceStrength DaysOffStrength { get; set; } = PreferenceStrength.Preferred;
    public DayLayout DayLayout { get; set; } = DayLayout.NoPreference;
    public int? MaxClassesPerDay { get; set; }
    public int? MaxMinutesPerDay { get; set; }
    public GapPreference Gaps { get; set; } = GapPreference.NoPreference;
    public BreakPreference Breaks { get; set; } = BreakPreference.Fine;
    public int MinimumBreakMinutes { get; set; } = 15;
    public PreferenceStrength MinimumBreakStrength { get; set; } = PreferenceStrength.Preferred;
    public LunchBreak? Lunch { get; set; }
    public List<BlockedTime> BlockedTimes { get; set; } = [];
    public FormatPreference Format { get; set; } = FormatPreference.NoPreference;
    public PreferenceStrength FormatStrength { get; set; } = PreferenceStrength.Preferred;
    public string? Campus { get; set; }
    public PreferenceStrength CampusStrength { get; set; } = PreferenceStrength.Preferred;
    public SeatPolicy Seats { get; set; } = SeatPolicy.Any;
    public List<string> PreferredInstructors { get; set; } = [];
    public List<string> AvoidedInstructors { get; set; } = [];
    public CreditTarget? CreditTarget { get; set; }

    public TimePreference this[TimeBand band]
    {
        get => TimeOfDay.TryGetValue(band, out var value) ? value : TimePreference.Okay;
        set
        {
            if (value == TimePreference.Okay) TimeOfDay.Remove(band);
            else TimeOfDay[band] = value;
        }
    }

    [JsonIgnore] public bool IsDefault => Equals(new SchedulePreferences());
    [JsonIgnore] public List<TimeBand> PreferredBands => Enum.GetValues<TimeBand>().Where(b => this[b] == TimePreference.Prefer).ToList();
    [JsonIgnore] public List<TimeBand> AvoidedBands => Enum.GetValues<TimeBand>().Where(b => this[b] == TimePreference.Avoid).ToList();
    [JsonIgnore] public bool HasTimeOfDayPreference => TimeOfDay.Count > 0;
    [JsonIgnore] public List<BlockedTime> ActiveBlockedTimes => BlockedTimes.Where(b => b.IsActive).ToList();
    [JsonIgnore] public bool HasWishes => ActiveCount > 0;

    [JsonIgnore]
    public int ActiveCount
    {
        get
        {
            var n = 0;
            if (HasTimeOfDayPreference) n++;
            if (EarliestStart is not null) n++;
            if (LatestEnd is not null) n++;
            if (!DaysOff.IsEmpty) n++;
            if (DayLayout != DayLayout.NoPreference) n++;
            if (MaxClassesPerDay is not null || MaxMinutesPerDay is not null) n++;
            if (Gaps != GapPreference.NoPreference) n++;
            if (Breaks != BreakPreference.Fine) n++;
            if (Lunch is not null) n++;
            n += ActiveBlockedTimes.Count;
            if (Format != FormatPreference.NoPreference) n++;
            if (Campus is not null) n++;
            if (Seats != SeatPolicy.Any) n++;
            if (PreferredInstructors.Count > 0 || AvoidedInstructors.Count > 0) n++;
            if (CreditTarget is not null) n++;
            return n;
        }
    }

    public static SectionFormat FormatOf(CourseSection section)
    {
        if (section.IsOnline) return SectionFormat.Online;
        var method = (section.InstructionMethod ?? "").ToLowerInvariant();
        if (method.Contains("hybrid")) return SectionFormat.Hybrid;
        if (method.Contains("live cast") || method.Contains("video") || method.Contains("remote")) return SectionFormat.Online;
        if (method.Contains("traditional")) return SectionFormat.InPerson;
        return SectionFormat.Other;
    }

    public SchedulePreferences Normalized()
    {
        var p = Clone();
        p.EarliestStart = ClampTime(p.EarliestStart, 0, 23 * 60 + 45);
        p.LatestEnd = ClampTime(p.LatestEnd, 30, 24 * 60);
        p.MaxClassesPerDay = p.MaxClassesPerDay is int classes && classes is >= 1 and <= 8 ? classes : null;
        p.MaxMinutesPerDay = p.MaxMinutesPerDay is int minutes && minutes is >= 60 and <= 12 * 60 ? minutes : null;
        p.MinimumBreakMinutes = Math.Clamp(p.MinimumBreakMinutes, 5, 90);
        if (p.Lunch is LunchBreak lunch)
        {
            lunch.Start = Math.Clamp(lunch.Start, 9 * 60, 15 * 60);
            lunch.End = Math.Clamp(lunch.End, lunch.Start + 30, 17 * 60);
            lunch.Minutes = Math.Clamp(lunch.Minutes, 15, lunch.End - lunch.Start);
        }
        var seen = new HashSet<string>();
        p.BlockedTimes = p.BlockedTimes.Select(block =>
        {
            block.Label = block.Label.Trim();
            if (block.Label.Length == 0) block.Label = "Blocked time";
            block.Start = Math.Clamp(block.Start, 0, 24 * 60);
            block.End = Math.Clamp(block.End, 0, 24 * 60);
            return block;
        }).Where(block => block.IsValid).Where(block =>
        {
            var key = $"{block.Label.ToLowerInvariant()}|{block.Days.Raw}|{block.Start}|{block.End}";
            return seen.Add(key);
        }).Take(20).ToList();
        if (p.Campus is string campus)
        {
            campus = campus.Trim();
            p.Campus = campus.Length == 0 ? null : campus;
        }
        List<string> Clean(List<string> names)
        {
            var unique = new HashSet<string>();
            return names.Select(n => n.Trim()).Where(n => n.Length > 0 && unique.Add(n.ToLowerInvariant())).Take(30).ToList();
        }
        p.PreferredInstructors = Clean(p.PreferredInstructors);
        var preferred = p.PreferredInstructors.Select(n => n.ToLowerInvariant()).ToHashSet();
        p.AvoidedInstructors = Clean(p.AvoidedInstructors).Where(n => !preferred.Contains(n.ToLowerInvariant())).ToList();
        if (p.CreditTarget is CreditTarget credits)
        {
            credits.Lower = Math.Clamp(credits.Lower, 1, 30);
            credits.Upper = Math.Clamp(credits.Upper, credits.Lower, 30);
        }
        return p;
    }

    private static TimeLimit? ClampTime(TimeLimit? limit, int lo, int hi)
    {
        if (limit is null) return null;
        limit.Minutes = Math.Clamp(limit.Minutes, lo, hi);
        return limit;
    }

    public SchedulePreferences Clone()
    {
        var copy = (SchedulePreferences)MemberwiseClone();
        copy.TimeOfDay = new Dictionary<TimeBand, TimePreference>(TimeOfDay);
        copy.BlockedTimes = BlockedTimes.Select(b => new BlockedTime
        {
            Id = b.Id, Label = b.Label, Days = b.Days, Start = b.Start, End = b.End, Strength = b.Strength, IsEnabled = b.IsEnabled,
        }).ToList();
        copy.PreferredInstructors = [.. PreferredInstructors];
        copy.AvoidedInstructors = [.. AvoidedInstructors];
        if (EarliestStart is not null) copy.EarliestStart = new TimeLimit(EarliestStart.Minutes, EarliestStart.Strength);
        if (LatestEnd is not null) copy.LatestEnd = new TimeLimit(LatestEnd.Minutes, LatestEnd.Strength);
        if (Lunch is not null) copy.Lunch = new LunchBreak { Start = Lunch.Start, End = Lunch.End, Minutes = Lunch.Minutes };
        if (CreditTarget is not null) copy.CreditTarget = new CreditTarget(CreditTarget.Lower, CreditTarget.Upper);
        return copy;
    }

    public List<string> SummaryParts()
    {
        var parts = new List<string>();
        var phrase = TimeOfDayPhrase(PreferredBands, AvoidedBands);
        if (phrase is not null) parts.Add(phrase);
        if (EarliestStart is TimeLimit early)
            parts.Add(early.Strength == PreferenceStrength.Required ? $"Nothing before {ShortTime(early.Minutes)}" : $"Prefer after {ShortTime(early.Minutes)}");
        if (LatestEnd is TimeLimit late)
            parts.Add(late.Strength == PreferenceStrength.Required ? $"Done by {ShortTime(late.Minutes)}" : $"Prefer done by {ShortTime(late.Minutes)}");
        if (!DaysOff.IsEmpty) parts.Add(DaysOffPhrase(DaysOff));
        switch (DayLayout)
        {
            case DayLayout.FewerDays: parts.Add("Fewer days"); break;
            case DayLayout.SpreadOut: parts.Add("Spread out"); break;
        }
        var blocked = ActiveBlockedTimes.Count;
        if (blocked > 0) parts.Add(blocked == 1 ? "1 blocked time" : $"{blocked} blocked times");
        switch (Gaps)
        {
            case GapPreference.Compact: parts.Add("Short gaps"); break;
            case GapPreference.Fine: parts.Add("Gaps are fine"); break;
        }
        switch (Breaks)
        {
            case BreakPreference.AvoidBackToBack: parts.Add("No back-to-back"); break;
            case BreakPreference.AtLeast: parts.Add($"{MinimumBreakMinutes} min to walk"); break;
        }
        if (Lunch is not null) parts.Add("Lunch break");
        if (MaxClassesPerDay is int maxClasses) parts.Add($"Up to {maxClasses} a day");
        if (MaxMinutesPerDay is int maxMinutes) parts.Add($"Up to {HoursLabel(maxMinutes)} a day");
        if (Format != FormatPreference.NoPreference) parts.Add(Format.Title());
        if (Campus is not null) parts.Add(Campus);
        switch (Seats)
        {
            case SeatPolicy.WaitlistOK: parts.Add("Waitlists OK"); break;
            case SeatPolicy.OpenOnly: parts.Add("Open seats"); break;
        }
        var instructors = PreferredInstructors.Count + AvoidedInstructors.Count;
        if (instructors > 0) parts.Add(instructors == 1 ? "1 instructor" : $"{instructors} instructors");
        if (CreditTarget is not null) parts.Add(CreditTarget.Label);
        return parts;
    }

    public string Summary(int limit = 3)
    {
        var parts = SummaryParts();
        if (parts.Count == 0) return "No preferences yet";
        if (parts.Count <= limit) return string.Join(" · ", parts);
        return string.Join(" · ", parts.Take(limit).Append($"+{parts.Count - limit}"));
    }

    public static string? TimeOfDayPhrase(IReadOnlyList<TimeBand> preferred, IReadOnlyList<TimeBand> avoided)
    {
        if (preferred.Count > 0)
        {
            var text = BandPhrase(preferred);
            return text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
        }
        if (avoided.Count > 0) return "No " + BandPhrase(avoided);
        return null;
    }

    public static string BandPhrase(IReadOnlyList<TimeBand> bands)
    {
        var set = bands.ToHashSet();
        var items = new List<string>();
        if (set.Contains(TimeBand.Morning)) items.Add("mornings");
        else if (set.Contains(TimeBand.EarlyMorning)) items.Add("early mornings");
        if (set.Contains(TimeBand.Midday)) items.Add("midday");
        if (set.Contains(TimeBand.Afternoon)) items.Add("afternoons");
        if (set.Contains(TimeBand.Evening)) items.Add("evenings");
        else if (set.Contains(TimeBand.Night)) items.Add("nights");
        return items.Count switch
        {
            0 => "",
            1 => items[0],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " & " + items[^1],
        };
    }

    public static string DaysOffPhrase(DaySet days)
    {
        var indices = days.Indices;
        return indices.Count switch
        {
            0 => "",
            1 => $"{DaySet.FullNames[indices[0]]}s off",
            _ => string.Join(", ", indices.Take(indices.Count - 1).Select(i => DaySet.ShortNames[i])) + " & " + DaySet.ShortNames[indices[^1]] + " off",
        };
    }

    public static string ShortTime(int minutes) => TermOrganize.Core.TimeOfDay.Label(minutes).Replace(":00", "", StringComparison.Ordinal);
    public static string HoursLabel(int minutes)
    {
        var hours = minutes / 60.0;
        return hours % 1 == 0 ? $"{(int)hours} h" : hours.ToString("0.0", CultureInfo.InvariantCulture) + " h";
    }

    public HashSet<PreferenceField> ChangedFields(SchedulePreferences old)
    {
        var changed = new HashSet<PreferenceField>();
        foreach (var band in Enum.GetValues<TimeBand>())
            if (this[band] != old[band]) changed.Add(new PreferenceField.TimeOfDayField(band));
        if (!Equals(EarliestStart, old.EarliestStart)) changed.Add(PreferenceField.EarliestStart);
        if (!Equals(LatestEnd, old.LatestEnd)) changed.Add(PreferenceField.LatestEnd);
        if (DaysOff != old.DaysOff || (!DaysOff.IsEmpty && DaysOffStrength != old.DaysOffStrength)) changed.Add(PreferenceField.DaysOff);
        if (DayLayout != old.DayLayout) changed.Add(PreferenceField.DayLayout);
        if (MaxClassesPerDay != old.MaxClassesPerDay || MaxMinutesPerDay != old.MaxMinutesPerDay) changed.Add(PreferenceField.Workload);
        if (Gaps != old.Gaps) changed.Add(PreferenceField.Gaps);
        if (Breaks != old.Breaks || (Breaks == BreakPreference.AtLeast && (MinimumBreakMinutes != old.MinimumBreakMinutes || MinimumBreakStrength != old.MinimumBreakStrength)))
            changed.Add(PreferenceField.Breaks);
        if (!Equals(Lunch, old.Lunch)) changed.Add(PreferenceField.Lunch);
        var oldBlocks = old.BlockedTimes.GroupBy(b => b.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var block in BlockedTimes)
            if (!oldBlocks.TryGetValue(block.Id, out var prior) || !block.Equals(prior))
                changed.Add(new PreferenceField.BlockedTimeField(block.Id));
        if (Format != old.Format || (Format != FormatPreference.NoPreference && FormatStrength != old.FormatStrength)) changed.Add(PreferenceField.Format);
        if (Campus != old.Campus || (Campus is not null && CampusStrength != old.CampusStrength)) changed.Add(PreferenceField.Campus);
        if (Seats != old.Seats) changed.Add(PreferenceField.Seats);
        if (!PreferredInstructors.SequenceEqual(old.PreferredInstructors) || !AvoidedInstructors.SequenceEqual(old.AvoidedInstructors))
            changed.Add(PreferenceField.Instructors);
        if (!Equals(CreditTarget, old.CreditTarget)) changed.Add(PreferenceField.Credits);
        return changed;
    }

    public bool Equals(SchedulePreferences? other)
    {
        if (other is null) return false;
        return TimeOfDay.Count == other.TimeOfDay.Count && TimeOfDay.All(p => other.TimeOfDay.TryGetValue(p.Key, out var v) && v == p.Value)
            && Equals(EarliestStart, other.EarliestStart) && Equals(LatestEnd, other.LatestEnd)
            && DaysOff == other.DaysOff && DaysOffStrength == other.DaysOffStrength && DayLayout == other.DayLayout
            && MaxClassesPerDay == other.MaxClassesPerDay && MaxMinutesPerDay == other.MaxMinutesPerDay
            && Gaps == other.Gaps && Breaks == other.Breaks && MinimumBreakMinutes == other.MinimumBreakMinutes
            && MinimumBreakStrength == other.MinimumBreakStrength && Equals(Lunch, other.Lunch)
            && BlockedTimes.SequenceEqual(other.BlockedTimes)
            && Format == other.Format && FormatStrength == other.FormatStrength
            && Campus == other.Campus && CampusStrength == other.CampusStrength && Seats == other.Seats
            && PreferredInstructors.SequenceEqual(other.PreferredInstructors)
            && AvoidedInstructors.SequenceEqual(other.AvoidedInstructors)
            && Equals(CreditTarget, other.CreditTarget);
    }

    public override bool Equals(object? obj) => Equals(obj as SchedulePreferences);
    public override int GetHashCode() => HashCode.Combine(ActiveCount, DaysOff, DayLayout, Format, Seats);
}

public abstract record PreferenceField
{
    public static PreferenceField EarliestStart { get; } = new Simple("earliest");
    public static PreferenceField LatestEnd { get; } = new Simple("latest");
    public static PreferenceField DaysOff { get; } = new Simple("days");
    public static PreferenceField DayLayout { get; } = new Simple("layout");
    public static PreferenceField Workload { get; } = new Simple("workload");
    public static PreferenceField Gaps { get; } = new Simple("gaps");
    public static PreferenceField Breaks { get; } = new Simple("breaks");
    public static PreferenceField Lunch { get; } = new Simple("lunch");
    public static PreferenceField Format { get; } = new Simple("format");
    public static PreferenceField Campus { get; } = new Simple("campus");
    public static PreferenceField Seats { get; } = new Simple("seats");
    public static PreferenceField Instructors { get; } = new Simple("instructors");
    public static PreferenceField Credits { get; } = new Simple("credits");
    public sealed record TimeOfDayField(TimeBand Band) : PreferenceField;
    public sealed record BlockedTimeField(Guid Id) : PreferenceField;
    private sealed record Simple(string Name) : PreferenceField;
}

public enum SchedulePreset
{
    MorningPerson,
    NightOwl,
    LongWeekends,
    CompactDays,
}

public static class SchedulePresetInfo
{
    public static string Title(this SchedulePreset preset) => preset switch
    {
        SchedulePreset.MorningPerson => "Morning person",
        SchedulePreset.NightOwl => "Night owl",
        SchedulePreset.LongWeekends => "Long weekends",
        SchedulePreset.CompactDays => "Compact days",
        _ => "",
    };

    public static string Detail(this SchedulePreset preset) => preset switch
    {
        SchedulePreset.MorningPerson => "Prefer classes before noon and avoid evenings.",
        SchedulePreset.NightOwl => "Avoid mornings and prefer afternoons and evenings.",
        SchedulePreset.LongWeekends => "Prefer Fridays off and as few days on campus as possible.",
        SchedulePreset.CompactDays => "Prefer few days on campus and short gaps between classes.",
        _ => "",
    };

    public static bool IsApplied(this SchedulePreset preset, SchedulePreferences p) => preset switch
    {
        SchedulePreset.MorningPerson or SchedulePreset.NightOwl => Bands(preset).All(pair => p[pair.Key] == pair.Value) && p.TimeOfDay.Count == Bands(preset).Count(),
        SchedulePreset.LongWeekends => p.DaysOff.Contains(5) && p.DayLayout == DayLayout.FewerDays,
        SchedulePreset.CompactDays => p.DayLayout == DayLayout.FewerDays && p.Gaps == GapPreference.Compact,
        _ => false,
    };

    public static SchedulePreferences Toggled(this SchedulePreset preset, SchedulePreferences p)
    {
        var copy = p.Clone();
        if (preset.IsApplied(p)) preset.Clear(copy);
        else preset.Apply(copy);
        return copy;
    }

    public static void Apply(this SchedulePreset preset, SchedulePreferences p)
    {
        switch (preset)
        {
            case SchedulePreset.MorningPerson:
            case SchedulePreset.NightOwl:
                p.TimeOfDay.Clear();
                foreach (var pair in Bands(preset)) p[pair.Key] = pair.Value;
                break;
            case SchedulePreset.LongWeekends:
                p.DaysOff = p.DaysOff.Union(DaySet.Friday);
                p.DayLayout = DayLayout.FewerDays;
                break;
            case SchedulePreset.CompactDays:
                p.DayLayout = DayLayout.FewerDays;
                p.Gaps = GapPreference.Compact;
                break;
        }
    }

    public static void Clear(this SchedulePreset preset, SchedulePreferences p)
    {
        switch (preset)
        {
            case SchedulePreset.MorningPerson:
            case SchedulePreset.NightOwl:
                p.TimeOfDay.Clear();
                break;
            case SchedulePreset.LongWeekends:
                p.DaysOff = p.DaysOff.Without(DaySet.Friday);
                if (p.Gaps != GapPreference.Compact) p.DayLayout = DayLayout.NoPreference;
                break;
            case SchedulePreset.CompactDays:
                p.Gaps = GapPreference.NoPreference;
                if (!p.DaysOff.Contains(5)) p.DayLayout = DayLayout.NoPreference;
                break;
        }
    }

    private static IEnumerable<KeyValuePair<TimeBand, TimePreference>> Bands(SchedulePreset preset) => preset switch
    {
        SchedulePreset.MorningPerson =>
        [
            new(TimeBand.EarlyMorning, TimePreference.Prefer),
            new(TimeBand.Morning, TimePreference.Prefer),
            new(TimeBand.Evening, TimePreference.Avoid),
            new(TimeBand.Night, TimePreference.Avoid),
        ],
        SchedulePreset.NightOwl =>
        [
            new(TimeBand.EarlyMorning, TimePreference.Avoid),
            new(TimeBand.Morning, TimePreference.Avoid),
            new(TimeBand.Afternoon, TimePreference.Prefer),
            new(TimeBand.Evening, TimePreference.Prefer),
            new(TimeBand.Night, TimePreference.Prefer),
        ],
        _ => [],
    };
}
