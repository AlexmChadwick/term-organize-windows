// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Deterministic stand-in for ScheduleAssistant.swift and ScheduleAssistantModel.swift.
// The Mac app fills a SchedulePreferencesDraft with Apple Intelligence. This port reads the
// same draft, applies the same grounding cues, and parses the sentence itself. Nothing is
// sent to a model or over the network. Apple Intelligence availability, SystemLanguageModel,
// and the streaming session are not used; their user-facing notes are kept so the survey
// can explain why a description field is absent.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TermOrganize.Core;

/// <summary>Whether a description field can be offered. This port has no on-device model.</summary>
public enum ScheduleAssistantAvailability
{
    Available,
    /// <summary>The device can't run Apple Intelligence.</summary>
    DeviceNotEligible,
    /// <summary>Apple Intelligence is turned off in Settings.</summary>
    NotEnabled,
    /// <summary>The model is still downloading or getting ready.</summary>
    NotReady,
    /// <summary>Apple Intelligence doesn't support the current language yet.</summary>
    UnsupportedLanguage,
    /// <summary>Unavailable for a reason this version doesn't know.</summary>
    Unavailable,
}

public static class ScheduleAssistantAvailabilityInfo
{
    public static bool IsAvailable(this ScheduleAssistantAvailability availability) =>
        availability == ScheduleAssistantAvailability.Available;

    /// <summary>One line for the survey when the description field can't be offered; null when it can.</summary>
    public static string? Note(this ScheduleAssistantAvailability availability) => availability switch
    {
        ScheduleAssistantAvailability.Available => null,
        ScheduleAssistantAvailability.DeviceNotEligible =>
            "Apple Intelligence isn\u2019t available on this device, but the survey below still works.",
        ScheduleAssistantAvailability.NotEnabled =>
            "Turn on Apple Intelligence in Settings to describe your week in your own words. The survey below works without it.",
        ScheduleAssistantAvailability.NotReady =>
            "Apple Intelligence isn\u2019t ready yet. Try again later; the survey below works in the meantime.",
        ScheduleAssistantAvailability.UnsupportedLanguage =>
            "Apple Intelligence doesn\u2019t support your language yet, but the survey below still works.",
        _ => "Apple Intelligence isn\u2019t available right now, but the survey below still works.",
    };
}

public enum ScheduleDraftWeekday
{
    Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday,
}

public static class ScheduleDraftWeekdayInfo
{
    public static DaySet Day(this ScheduleDraftWeekday weekday) => weekday switch
    {
        ScheduleDraftWeekday.Sunday => DaySet.Sunday,
        ScheduleDraftWeekday.Monday => DaySet.Monday,
        ScheduleDraftWeekday.Tuesday => DaySet.Tuesday,
        ScheduleDraftWeekday.Wednesday => DaySet.Wednesday,
        ScheduleDraftWeekday.Thursday => DaySet.Thursday,
        ScheduleDraftWeekday.Friday => DaySet.Friday,
        _ => DaySet.Saturday,
    };

    public static ScheduleDraftWeekday? FromIndex(int index) => index switch
    {
        0 => ScheduleDraftWeekday.Sunday,
        1 => ScheduleDraftWeekday.Monday,
        2 => ScheduleDraftWeekday.Tuesday,
        3 => ScheduleDraftWeekday.Wednesday,
        4 => ScheduleDraftWeekday.Thursday,
        5 => ScheduleDraftWeekday.Friday,
        6 => ScheduleDraftWeekday.Saturday,
        _ => null,
    };
}

public enum ScheduleDraftTimeOfDay
{
    EarlyMorning, Morning, Midday, Afternoon, Evening, Night,
}

public static class ScheduleDraftTimeOfDayInfo
{
    public static TimeBand Band(this ScheduleDraftTimeOfDay time) => time switch
    {
        ScheduleDraftTimeOfDay.EarlyMorning => TimeBand.EarlyMorning,
        ScheduleDraftTimeOfDay.Morning => TimeBand.Morning,
        ScheduleDraftTimeOfDay.Midday => TimeBand.Midday,
        ScheduleDraftTimeOfDay.Afternoon => TimeBand.Afternoon,
        ScheduleDraftTimeOfDay.Evening => TimeBand.Evening,
        _ => TimeBand.Night,
    };
}

public enum ScheduleDraftLayout { FewerDays, SpreadOut }
public enum ScheduleDraftBreaks { AvoidBackToBack, TimeToWalk }
public enum ScheduleDraftFormat { InPerson, Online, Hybrid }
public enum ScheduleDraftSeats { OpenSeatsOnly, WaitlistOK }

/// <summary>A weekly commitment classes can't overlap.</summary>
public sealed class ScheduleDraftBlockedTime : IEquatable<ScheduleDraftBlockedTime>
{
    public string Label { get; set; } = "";
    public List<ScheduleDraftWeekday> Days { get; set; } = [];
    public string Start { get; set; } = "";
    public string End { get; set; } = "";

    public bool Equals(ScheduleDraftBlockedTime? other) =>
        other is not null && Label == other.Label && Start == other.Start && End == other.End && Days.SequenceEqual(other.Days);

    public override bool Equals(object? obj) => Equals(obj as ScheduleDraftBlockedTime);
    public override int GetHashCode() => HashCode.Combine(Label, Start, End, Days.Count);
}

/// <summary>
/// What was understood from a description of an ideal schedule. Every field is optional:
/// only what was said comes back, and <see cref="Applied"/> merges it into the survey.
/// </summary>
public sealed class SchedulePreferencesDraft : IEquatable<SchedulePreferencesDraft>
{
    public const int MaxBlockedTimes = 8;

    public List<ScheduleDraftTimeOfDay>? PreferredTimes { get; set; }
    public List<ScheduleDraftTimeOfDay>? AvoidedTimes { get; set; }
    public string? EarliestStart { get; set; }
    public string? LatestEnd { get; set; }
    public bool? TimeLimitsAreFlexible { get; set; }
    public List<ScheduleDraftWeekday>? DaysOff { get; set; }
    public bool? DaysOffAreRequired { get; set; }
    public ScheduleDraftLayout? Layout { get; set; }
    public int? MaxClassesPerDay { get; set; }
    public int? MaxHoursPerDay { get; set; }
    public ScheduleDraftBreaks? Breaks { get; set; }
    public bool? WantsLunchBreak { get; set; }
    public List<ScheduleDraftBlockedTime>? BlockedTimes { get; set; }
    public ScheduleDraftFormat? Format { get; set; }
    public ScheduleDraftSeats? Seats { get; set; }

    public bool IsEmpty =>
        (PreferredTimes ?? []).Count == 0 && (AvoidedTimes ?? []).Count == 0
        && EarliestStart is null && LatestEnd is null
        && (DaysOff ?? []).Count == 0 && Layout is null
        && MaxClassesPerDay is null && MaxHoursPerDay is null
        && Breaks is null && WantsLunchBreak != true
        && (BlockedTimes ?? []).Count == 0 && Format is null && Seats is null;

    /// <summary>How many settings the draft fills in.</summary>
    public int FilledCount
    {
        get
        {
            var n = 0;
            if ((PreferredTimes ?? []).Count > 0) n++;
            if ((AvoidedTimes ?? []).Count > 0) n++;
            if (EarliestStart is not null) n++;
            if (LatestEnd is not null) n++;
            if ((DaysOff ?? []).Count > 0) n++;
            if (Layout is not null) n++;
            if (MaxClassesPerDay is not null || MaxHoursPerDay is not null) n++;
            if (Breaks is not null) n++;
            if (WantsLunchBreak == true) n++;
            n += (BlockedTimes ?? []).Count;
            if (Format is not null) n++;
            if (Seats is not null) n++;
            return n;
        }
    }

    /// <summary>
    /// <paramref name="base"/> with this draft's wishes applied. Fields the draft leaves out keep
    /// their values. Times that can't be read, blocks without days, and values out of range are dropped.
    /// </summary>
    public SchedulePreferences Applied(SchedulePreferences? basePreferences = null)
    {
        var preferences = (basePreferences ?? new SchedulePreferences()).Clone();
        foreach (var time in PreferredTimes ?? []) preferences[time.Band()] = TimePreference.Prefer;
        foreach (var time in AvoidedTimes ?? []) preferences[time.Band()] = TimePreference.Avoid;

        var strength = TimeLimitsAreFlexible == true ? PreferenceStrength.Preferred : PreferenceStrength.Required;
        if (ScheduleAssistant.Minutes(EarliestStart) is int earliest && earliest < 24 * 60)
            preferences.EarliestStart = new TimeLimit(earliest, strength);
        if (ScheduleAssistant.Minutes(LatestEnd) is int latest && latest > (preferences.EarliestStart?.Minutes ?? 0))
            preferences.LatestEnd = new TimeLimit(latest, strength);

        if (DaysOff is { Count: > 0 } wanted)
        {
            var days = DaySet.None;
            foreach (var day in wanted) days |= day.Day();
            preferences.DaysOff = days;
            preferences.DaysOffStrength = DaysOffAreRequired == true ? PreferenceStrength.Required : PreferenceStrength.Preferred;
        }
        preferences.DayLayout = Layout switch
        {
            ScheduleDraftLayout.FewerDays => DayLayout.FewerDays,
            ScheduleDraftLayout.SpreadOut => DayLayout.SpreadOut,
            _ => preferences.DayLayout,
        };
        if (MaxClassesPerDay is int classes && classes is >= 1 and <= 8) preferences.MaxClassesPerDay = classes;
        if (MaxHoursPerDay is int hours && hours is >= 1 and <= 12) preferences.MaxMinutesPerDay = hours * 60;
        switch (Breaks)
        {
            case ScheduleDraftBreaks.AvoidBackToBack:
                preferences.Breaks = BreakPreference.AvoidBackToBack;
                break;
            case ScheduleDraftBreaks.TimeToWalk:
                preferences.Breaks = BreakPreference.AtLeast;
                preferences.MinimumBreakMinutes = 15;
                preferences.MinimumBreakStrength = PreferenceStrength.Preferred;
                break;
        }
        if (WantsLunchBreak == true && preferences.Lunch is null) preferences.Lunch = new LunchBreak();

        foreach (var block in (BlockedTimes ?? []).Take(MaxBlockedTimes))
        {
            if (ScheduleAssistant.Minutes(block.Start) is not int start) continue;
            if (ScheduleAssistant.Minutes(block.End) is not int end) continue;
            if (end <= start && end < 12 * 60 && end + 12 * 60 <= 24 * 60) end += 12 * 60;
            var days = DaySet.None;
            foreach (var day in block.Days) days |= day.Day();
            var candidate = new BlockedTime { Label = block.Label, Days = days, Start = start, End = end };
            if (!candidate.IsValid) continue;
            var label = candidate.Label.Trim().ToLowerInvariant();
            var duplicate = preferences.BlockedTimes.Any(existing =>
                existing.Label.ToLowerInvariant() == label && existing.Days == candidate.Days
                && existing.Start == candidate.Start && existing.End == candidate.End);
            if (!duplicate) preferences.BlockedTimes.Add(candidate);
        }

        preferences.Format = Format switch
        {
            ScheduleDraftFormat.InPerson => FormatPreference.InPerson,
            ScheduleDraftFormat.Online => FormatPreference.Online,
            ScheduleDraftFormat.Hybrid => FormatPreference.Hybrid,
            _ => preferences.Format,
        };
        preferences.Seats = Seats switch
        {
            ScheduleDraftSeats.OpenSeatsOnly => SeatPolicy.OpenOnly,
            ScheduleDraftSeats.WaitlistOK => SeatPolicy.WaitlistOK,
            _ => preferences.Seats,
        };
        return preferences.Normalized();
    }

    /// <summary>
    /// Drops what the student's own words give no sign of. Firmness of a time limit or a day off
    /// follows the wording of its own clause. Descriptions that aren't in English are left alone.
    /// </summary>
    public SchedulePreferencesDraft Grounded(string description)
    {
        if (!ScheduleAssistant.IsEnglish(description)) return this;
        var text = " " + description.ToLowerInvariant() + " ";
        bool Mentions(string[] cues) => cues.Any(text.Contains);

        var copy = Copy();
        if (!Mentions(Cues.Format)) copy.Format = null;
        if (!Mentions(Cues.Seats)) copy.Seats = null;
        if (!Mentions(Cues.Breaks)) copy.Breaks = null;
        if (!Mentions(Cues.Lunch)) copy.WantsLunchBreak = null;
        if (!Mentions(Cues.Layout)) copy.Layout = null;
        var perDay = Mentions(Cues.PerDay) && HasQuantity(text);
        if (!(perDay && text.Contains("class", StringComparison.Ordinal))) copy.MaxClassesPerDay = null;
        if (!(perDay && text.Contains("hour", StringComparison.Ordinal))) copy.MaxHoursPerDay = null;

        var clauses = Clauses(text);
        bool Has(string[] cues, string clause) => cues.Any(clause.Contains);
        var limitClauses = clauses.Where(clause => Has(Cues.TimeLimit, clause)).ToList();
        copy.TimeLimitsAreFlexible = limitClauses.Count > 0 && limitClauses.All(clause => Has(Cues.Soft, clause) && !Has(Cues.Firm, clause));
        var offClauses = clauses.Where(clause => Has(Cues.Weekday, clause) && Has(Cues.Off, clause)).ToList();
        copy.DaysOffAreRequired = offClauses.Any(clause => Has(Cues.Firm, clause) || Has(Cues.NoClassesOn, clause));
        return copy;
    }

    public bool Equals(SchedulePreferencesDraft? other)
    {
        if (other is null) return false;
        return Sequence(PreferredTimes, other.PreferredTimes) && Sequence(AvoidedTimes, other.AvoidedTimes)
            && EarliestStart == other.EarliestStart && LatestEnd == other.LatestEnd
            && TimeLimitsAreFlexible == other.TimeLimitsAreFlexible
            && Sequence(DaysOff, other.DaysOff) && DaysOffAreRequired == other.DaysOffAreRequired
            && Layout == other.Layout && MaxClassesPerDay == other.MaxClassesPerDay && MaxHoursPerDay == other.MaxHoursPerDay
            && Breaks == other.Breaks && WantsLunchBreak == other.WantsLunchBreak
            && Sequence(BlockedTimes, other.BlockedTimes) && Format == other.Format && Seats == other.Seats;
    }

    public override bool Equals(object? obj) => Equals(obj as SchedulePreferencesDraft);
    public override int GetHashCode() => HashCode.Combine(EarliestStart, LatestEnd, WantsLunchBreak, Format, Seats);

    internal static List<string> Clauses(string text)
    {
        var parts = new List<string> { text };
        foreach (var separator in new[] { ",", ";", ".", "\n", " and ", " but " })
        {
            var next = new List<string>();
            foreach (var part in parts)
                next.AddRange(part.Split(separator, StringSplitOptions.None));
            parts = next;
        }
        return parts.Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
    }

    private static bool HasQuantity(string text) =>
        text.Any(char.IsNumber) || NumberWords.Any(text.Contains);

    private static bool Sequence<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
    {
        var a = left ?? [];
        var b = right ?? [];
        return a.SequenceEqual(b);
    }

    private SchedulePreferencesDraft Copy() => new()
    {
        PreferredTimes = PreferredTimes?.ToList(),
        AvoidedTimes = AvoidedTimes?.ToList(),
        EarliestStart = EarliestStart,
        LatestEnd = LatestEnd,
        TimeLimitsAreFlexible = TimeLimitsAreFlexible,
        DaysOff = DaysOff?.ToList(),
        DaysOffAreRequired = DaysOffAreRequired,
        Layout = Layout,
        MaxClassesPerDay = MaxClassesPerDay,
        MaxHoursPerDay = MaxHoursPerDay,
        Breaks = Breaks,
        WantsLunchBreak = WantsLunchBreak,
        BlockedTimes = BlockedTimes?.Select(block => new ScheduleDraftBlockedTime
        {
            Label = block.Label,
            Days = block.Days.ToList(),
            Start = block.Start,
            End = block.End,
        }).ToList(),
        Format = Format,
        Seats = Seats,
    };

    private static readonly string[] NumberWords =
        ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "twelve", "couple", "no more than", "at most", "max"];

    private static class Cues
    {
        public static readonly string[] Format = ["online", "remote", "virtual", "zoom", "in person", "in-person", "on campus", "on-campus", "hybrid", "face to face", "face-to-face"];
        public static readonly string[] Seats = ["seat", "waitlist", "wait list", "wait-list", "capacity", "full class", "full section", "open section", "open class", "closed class"];
        public static readonly string[] Breaks = ["back to back", "back-to-back", "walk", "between class", "between building", "break between", "gap", "rush", "far apart", "time to get", "travel"];
        public static readonly string[] Lunch = ["lunch", "meal", " eat "];
        public static readonly string[] PerDay = ["per day", "a day", "each day", "every day", "in a day", "in one day", "daily", "same day"];
        public static readonly string[] Layout = ["few days", "fewer days", "fewest days", "spread", "evenly", "compact", "long weekend", "bunch", "pack ", "cram", "consolidat", "every day", "each day", "all week", "days a week", "days per week", "days on campus"];
        public static readonly string[] TimeLimit = ["before", "after", "earlier than", "later than", "start", "finish", "end by", "done by", "out by", "no classes", "nothing"];
        public static readonly string[] Weekday = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];
        public static readonly string[] Off = [" off", "free", "no class", "without class", "skip", "nothing on"];
        public static readonly string[] NoClassesOn = ["no class on", "no classes on", "nothing on"];
        public static readonly string[] Firm = ["must", "have to", "has to", "need to", "i need", "can't", "can\u2019t", "cannot", "can not", "no way", "never", "always", "required", "i work", "my job", "my shift", "shifts", "only available"];
        public static readonly string[] Soft = ["ideally", "prefer", "if possible", "if i can", "if it's possible", "would like", "i'd like", "i\u2019d like", "id like", "rather", "hopefully", "try to", "nice if", "would be nice", "wish"];
    }
}

/// <summary>
/// Reads a description of an ideal schedule and turns it into survey answers.
/// The grammar is the draft in ScheduleAssistant.swift; the reading is deterministic.
/// </summary>
public static partial class ScheduleAssistant
{
    public sealed class Interpretation
    {
        public required SchedulePreferencesDraft Draft { get; init; }

        public (SchedulePreferences Preferences, HashSet<PreferenceField> ChangedFields) Merged(SchedulePreferences? basePreferences = null)
        {
            var baseline = basePreferences ?? new SchedulePreferences();
            var merged = Draft.Applied(baseline);
            return (merged, merged.ChangedFields(baseline));
        }
    }

    /// <summary>Why a description couldn't be used. The message is what the survey shows.</summary>
    public sealed class Failure : Exception
    {
        private Failure(string message) : base(message) { }

        public static Failure Unavailable(ScheduleAssistantAvailability availability) =>
            new(availability.Note() ?? "Apple Intelligence isn\u2019t available right now.");

        public static Failure EmptyDescription { get; } = new("Describe the schedule you want first.");
        public static Failure DescriptionTooLong { get; } = new("That\u2019s a lot to read at once. Try describing the most important things in a few sentences.");
        public static Failure Guardrail { get; } = new("Apple Intelligence couldn\u2019t use that description. Try rephrasing it, or fill in the survey yourself.");
        public static Failure ContextTooLarge { get; } = new("That description is too long for Apple Intelligence. Shorten it, or fill in the survey yourself.");
        public static Failure UnsupportedLanguage { get; } = new("Apple Intelligence can\u2019t read that language yet. Try English, or fill in the survey yourself.");
        public static Failure Busy { get; } = new("Apple Intelligence is busy right now. Try again in a moment.");
        public static Failure TimedOut { get; } = new("Apple Intelligence took too long. Try a shorter description, or fill in the survey yourself.");
        public static Failure NothingFound { get; } = new("Couldn\u2019t find any schedule wishes in that. Try mentioning times, days or commitments, like \u201cno classes before 10\u201d or \u201cwork Tuesdays 1\u20135\u201d.");
        public static Failure Failed(string? _) => new("Something went wrong reading that. Try again, or fill in the survey yourself.");
    }

    public const int MaxDescriptionLength = 1_500;

    /// <summary>Kept for parity with the on-device instructions. Interpretation does not call a model.</summary>
    public const string Instructions = """
        You turn a college student's description of their ideal class schedule into structured preferences.
        Only fill in a field when the student clearly says it. Leave everything else out. Never guess or invent.
        Times use the 24-hour clock: 6pm is 18:00, 10am is 10:00. "No classes before 10" is earliestStart 10:00. "Done by 5" is latestEnd 17:00.
        Clubs, jobs, practices and commutes are blocked times with the weekdays and hours they repeat.
        Example: "No classes before 10, choir Tuesdays 6-8pm, Fridays off" gives earliestStart 10:00, a blocked time Choir on Tuesday from 18:00 to 20:00, and Friday in daysOff.
        """;

    /// <summary>How long a request may take before it is abandoned. Parsing here is local, so this is unused.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The description field is offered. Parsing is local; there is no on-device model to wait for.
    /// </summary>
    public static ScheduleAssistantAvailability Availability => ScheduleAssistantAvailability.Available;

    /// <summary>No model to warm. Kept so callers match the Swift assistant.</summary>
    public static void Prewarm() { }

    /// <summary>
    /// Whether <paramref name="text"/> is written in English, the language the grounding cues are in.
    /// A small lexicon stands in for NLLanguageRecognizer: English at 0.4 or better counts.
    /// </summary>
    public static bool IsEnglish(string text)
    {
        var folded = Fold(text);
        var english = 0;
        var spanish = 0;
        foreach (Match match in WordPattern().Matches(folded))
        {
            var word = match.Value;
            if (SpanishWords.Contains(word)) spanish++;
            else if (EnglishWords.Contains(word)) english++;
        }
        if (english + spanish == 0) return folded.All(c => c < 128);
        return (double)english / (english + spanish) >= 0.4;
    }

    /// <summary>Reads <paramref name="text"/> and returns the wishes it states.</summary>
    public static Interpretation Interpret(string? text)
    {
        var description = (text ?? "").Trim();
        if (description.Length == 0) throw Failure.EmptyDescription;
        if (description.Length > MaxDescriptionLength) throw Failure.DescriptionTooLong;
        var draft = Parse(description).Grounded(description);
        if (draft.IsEmpty) throw Failure.NothingFound;
        return new Interpretation { Draft = draft };
    }

    /// <summary>
    /// Reads a time as the model writes it (<c>18:00</c>, <c>6:30 pm</c>, <c>6pm</c>, <c>1800</c>, <c>noon</c>)
    /// into minutes after midnight. <c>24:00</c> and <c>midnight</c> are the end of the day (1440).
    /// </summary>
    public static int? Minutes(string? text)
    {
        if (text is null) return null;
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsWhiteSpace(character) || character == '.') continue;
            builder.Append(character);
        }
        var s = builder.ToString();
        switch (s)
        {
            case "noon": return 12 * 60;
            case "midnight": return 24 * 60;
        }
        bool? meridiem = null;
        if (s.EndsWith("am", StringComparison.Ordinal)) { meridiem = false; s = s[..^2]; }
        else if (s.EndsWith("pm", StringComparison.Ordinal)) { meridiem = true; s = s[..^2]; }
        if (s.Length == 0) return null;

        int hour;
        var minute = 0;
        var colon = s.IndexOf(':');
        if (colon >= 0)
        {
            if (!int.TryParse(s[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out hour)) return null;
            if (!int.TryParse(s[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out minute)) return null;
        }
        else if (s.Length is >= 3 and <= 4 && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var packed))
        {
            hour = packed / 100;
            minute = packed % 100;
        }
        else if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out hour))
        {
            return null;
        }
        if (hour is < 0 or > 24 || minute is < 0 or > 59) return null;
        if (meridiem is bool isPm)
        {
            if (hour is < 1 or > 12) return null;
            if (isPm && hour < 12) hour += 12;
            if (!isPm && hour == 12) hour = 0;
        }
        var total = hour * 60 + minute;
        return total <= 24 * 60 ? total : null;
    }

    private static SchedulePreferencesDraft Parse(string description)
    {
        var normalized = description.Replace('\u2013', '-').Replace('\u2014', '-').Replace('\u2212', '-');
        var lower = normalized.ToLowerInvariant();
        var draft = new SchedulePreferencesDraft();

        foreach (var clause in lower.Split([',', ';', '.', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ClockAfter(clause, ["no classes before", "nothing before", "not before", "earlier than", "classes before", "before"]) is string early)
                draft.EarliestStart ??= Canonical(early);
            if (ClockAfter(clause, ["done by", "end by", "out by", "finished by", "finish by", "no later than", "later than"]) is string late)
                draft.LatestEnd ??= Canonical(late, afternoonBias: true);
            ApplyBands(draft, clause);
            ApplyFormat(draft, clause);
            ApplySeats(draft, clause);
            ApplyBreaks(draft, clause);
            ApplyLayout(draft, clause);
            ApplyWorkload(draft, clause);
            if (clause.Contains("lunch", StringComparison.Ordinal) || clause.Contains("meal", StringComparison.Ordinal))
                draft.WantsLunchBreak = true;
        }

        draft.BlockedTimes = BlockedTimes(lower);
        if (draft.BlockedTimes.Count == 0) draft.BlockedTimes = null;
        var daysOff = DaysOff(lower);
        if (daysOff.Count > 0) draft.DaysOff = daysOff;
        return draft;
    }

    private static List<ScheduleDraftBlockedTime> BlockedTimes(string lower)
    {
        var blocks = new List<ScheduleDraftBlockedTime>();
        foreach (Match match in RangePattern().Matches(lower))
        {
            var prefixStart = 0;
            for (var i = match.Index - 1; i >= 0; i--)
            {
                if (lower[i] is ',' or ';' or '.' or '\n') { prefixStart = i + 1; break; }
            }
            var prefix = lower[prefixStart..match.Index];
            var days = WeekdaysIn(prefix);
            if (days.Count == 0) continue;
            var afternoon = ContainsAny(prefix, "work", "job", "shift", "practice");
            var (start, end) = RangeClock(match.Groups[1].Value, match.Groups[2].Value, afternoon);
            if (start is null || end is null) continue;
            blocks.Add(new ScheduleDraftBlockedTime
            {
                Label = LabelFrom(prefix),
                Days = days,
                Start = start,
                End = end,
            });
        }
        return blocks;
    }

    private static List<ScheduleDraftWeekday> DaysOff(string lower)
    {
        var days = new List<ScheduleDraftWeekday>();
        foreach (var clause in lower.Split([',', ';', '.', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (RangePattern().IsMatch(clause)) continue;
            var found = WeekdaysIn(clause);
            if (found.Count == 0) continue;
            var off = ContainsAny(clause, " off", "free", "no class", "without class", "skip", "nothing on");
            var commitment = ContainsAny(clause, "i work", "my job", "my shift", "work ", " shift");
            if (!off && !commitment) continue;
            foreach (var day in found)
                if (!days.Contains(day)) days.Add(day);
        }
        return days;
    }

    private static void ApplyBands(SchedulePreferencesDraft draft, string clause)
    {
        var text = clause.Replace("early morning", "\u0001", StringComparison.Ordinal);
        var avoid = ContainsAny(text, "avoid", "no ", "not ", "don't", "don\u2019t", "do not", "without");
        var prefer = ContainsAny(text, "prefer", "like", "want");
        void Take(string cue, ScheduleDraftTimeOfDay band, string source)
        {
            if (!source.Contains(cue, StringComparison.Ordinal)) return;
            var list = avoid && !source.Contains("prefer " + cue, StringComparison.Ordinal)
                ? draft.AvoidedTimes ??= []
                : prefer ? draft.PreferredTimes ??= [] : null;
            if (list is null) return;
            if (!list.Contains(band)) list.Add(band);
        }
        if (clause.Contains("early morning", StringComparison.Ordinal))
            Take("early morning", ScheduleDraftTimeOfDay.EarlyMorning, clause);
        Take("morning", ScheduleDraftTimeOfDay.Morning, text);
        Take("midday", ScheduleDraftTimeOfDay.Midday, text);
        Take("noon", ScheduleDraftTimeOfDay.Midday, text);
        Take("afternoon", ScheduleDraftTimeOfDay.Afternoon, text);
        Take("evening", ScheduleDraftTimeOfDay.Evening, text);
        Take("night", ScheduleDraftTimeOfDay.Night, text);
    }

    private static void ApplyFormat(SchedulePreferencesDraft draft, string clause)
    {
        if (draft.Format is not null) return;
        if (clause.Contains("hybrid", StringComparison.Ordinal)) draft.Format = ScheduleDraftFormat.Hybrid;
        else if (ContainsAny(clause, "online", "remote", "virtual", "zoom")) draft.Format = ScheduleDraftFormat.Online;
        else if (ContainsAny(clause, "in person", "in-person", "on campus", "on-campus", "face to face", "face-to-face"))
            draft.Format = ScheduleDraftFormat.InPerson;
    }

    private static void ApplySeats(SchedulePreferencesDraft draft, string clause)
    {
        if (draft.Seats is not null) return;
        if (ContainsAny(clause, "waitlist", "wait list", "wait-list")) draft.Seats = ScheduleDraftSeats.WaitlistOK;
        else if (ContainsAny(clause, "open seat", "open section", "open class", "seats open")) draft.Seats = ScheduleDraftSeats.OpenSeatsOnly;
    }

    private static void ApplyBreaks(SchedulePreferencesDraft draft, string clause)
    {
        if (ContainsAny(clause, "back to back", "back-to-back")) draft.Breaks = ScheduleDraftBreaks.AvoidBackToBack;
        else if (ContainsAny(clause, "time to walk", "between class", "between building", "time to get", " walk"))
            draft.Breaks = ScheduleDraftBreaks.TimeToWalk;
    }

    private static void ApplyLayout(SchedulePreferencesDraft draft, string clause)
    {
        if (draft.Layout is not null) return;
        if (ContainsAny(clause, "spread", "evenly")) draft.Layout = ScheduleDraftLayout.SpreadOut;
        else if (ContainsAny(clause, "fewer days", "fewest days", "few days", "long weekend", "compact", "cram", "consolidat"))
            draft.Layout = ScheduleDraftLayout.FewerDays;
    }

    private static void ApplyWorkload(SchedulePreferencesDraft draft, string clause)
    {
        if (!ContainsAny(clause, "a day", "per day", "each day", "every day", "daily")) return;
        var number = FirstNumber(clause);
        if (number is null) return;
        if (clause.Contains("class", StringComparison.Ordinal)) draft.MaxClassesPerDay ??= number;
        if (clause.Contains("hour", StringComparison.Ordinal)) draft.MaxHoursPerDay ??= number;
    }

    private static string? ClockAfter(string clause, string[] phrases)
    {
        foreach (var phrase in phrases)
        {
            var at = clause.IndexOf(phrase, StringComparison.Ordinal);
            if (at < 0) continue;
            var rest = clause[(at + phrase.Length)..].Trim();
            var match = ClockToken().Match(rest);
            if (match.Success) return match.Value;
        }
        return null;
    }

    private static (string? Start, string? End) RangeClock(string startText, string endText, bool afternoonBias)
    {
        var startMeridiem = Meridiem(startText);
        var endMeridiem = Meridiem(endText);
        var start = startText;
        var end = endText;
        if (startMeridiem is null && endMeridiem is not null) start += endMeridiem;
        else if (endMeridiem is null && startMeridiem is not null) end += startMeridiem;
        else if (startMeridiem is null && endMeridiem is null && afternoonBias)
        {
            if (BareHour(start) is int sh && sh is >= 1 and <= 11) start += "pm";
            if (BareHour(end) is int eh && eh is >= 1 and <= 11) end += "pm";
        }
        return (Canonical(start), Canonical(end));
    }

    private static string? Meridiem(string text)
    {
        var folded = text.Replace(".", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (folded.EndsWith("pm", StringComparison.Ordinal)) return "pm";
        if (folded.EndsWith("am", StringComparison.Ordinal)) return "am";
        return null;
    }

    private static int? BareHour(string text)
    {
        var digits = new string(text.TakeWhile(c => char.IsDigit(c) || c == ':').ToArray());
        if (digits.Length == 0) return null;
        var hourText = digits.Split(':')[0];
        return int.TryParse(hourText, NumberStyles.None, CultureInfo.InvariantCulture, out var hour) ? hour : null;
    }

    private static string? Canonical(string token, bool afternoonBias = false)
    {
        if (Minutes(token) is not int minutes) return null;
        if (afternoonBias && !HasMeridiem(token))
        {
            var hour = minutes / 60;
            if (hour is >= 1 and <= 7) minutes += 12 * 60;
        }
        if (minutes == 24 * 60) return "24:00";
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    private static bool HasMeridiem(string token)
    {
        var folded = token.ToLowerInvariant().Replace(".", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        return folded.Contains("am", StringComparison.Ordinal) || folded.Contains("pm", StringComparison.Ordinal);
    }

    private static string LabelFrom(string prefix)
    {
        var text = WeekdayWords().Replace(prefix, " ");
        text = ConnectorWords().Replace(text, " ");
        text = text.Trim(' ', ',', ';', ':', '-');
        var fillers = new[] { "i have", "i've got", "i\u2019ve got", "ive got", "i got", "my", "a", "an", "the" };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var filler in fillers)
            {
                if (!text.StartsWith(filler + " ", StringComparison.Ordinal) && text != filler) continue;
                text = text.Length <= filler.Length ? "" : text[filler.Length..].Trim();
                changed = true;
            }
        }
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length == 0) return "Blocked";
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static List<ScheduleDraftWeekday> WeekdaysIn(string text)
    {
        var days = new List<ScheduleDraftWeekday>();
        foreach (Match match in WeekdayWords().Matches(text))
        {
            var word = match.Value;
            if (word.EndsWith('s')) word = word[..^1];
            var day = word switch
            {
                "sunday" or "sun" => ScheduleDraftWeekday.Sunday,
                "monday" or "mon" => ScheduleDraftWeekday.Monday,
                "tuesday" or "tues" or "tue" => ScheduleDraftWeekday.Tuesday,
                "wednesday" or "wed" => ScheduleDraftWeekday.Wednesday,
                "thursday" or "thurs" or "thur" or "thu" => ScheduleDraftWeekday.Thursday,
                "friday" or "fri" => ScheduleDraftWeekday.Friday,
                "saturday" or "sat" => ScheduleDraftWeekday.Saturday,
                _ => (ScheduleDraftWeekday?)null,
            };
            if (day is not ScheduleDraftWeekday weekday) continue;
            if (!days.Contains(weekday)) days.Add(weekday);
        }
        return days;
    }

    private static int? FirstNumber(string clause)
    {
        var digit = Regex.Match(clause, @"\d+");
        if (digit.Success && int.TryParse(digit.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return number;
        foreach (var (word, value) in NumberWords)
            if (clause.Contains(word, StringComparison.Ordinal)) return value;
        return null;
    }

    private static bool ContainsAny(string text, params string[] cues) =>
        cues.Any(cue => text.Contains(cue, StringComparison.Ordinal));

    private static string Fold(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(character);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static readonly (string Word, int Value)[] NumberWords =
    [
        ("twelve", 12), ("eleven", 11), ("ten", 10), ("nine", 9), ("eight", 8), ("seven", 7),
        ("six", 6), ("five", 5), ("four", 4), ("three", 3), ("two", 2), ("one", 1), ("couple", 2),
    ];

    private static readonly HashSet<string> EnglishWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "i", "my", "me", "no", "not", "before", "after", "classes", "class", "off", "and", "but",
        "like", "have", "has", "want", "work", "works", "friday", "fridays", "monday", "mondays", "tuesday", "tuesdays",
        "wednesday", "wednesdays", "thursday", "thursdays", "saturday", "saturdays", "sunday", "sundays", "lunch",
        "break", "nothing", "done", "by", "on", "with", "for", "to", "of", "in", "at", "from", "would", "ideally",
        "prefer", "preferred", "please", "need", "them", "so", "do", "can", "cant", "morning", "mornings", "night",
        "hours", "hour", "day", "each", "every", "more", "than", "three", "too", "many", "robotics", "club", "choir",
        "online", "hybrid", "open", "seats", "back", "most", "up", "id", "be", "great", "without", "free", "skip",
    };

    private static readonly HashSet<string> SpanishWords = new(StringComparer.Ordinal)
    {
        "quiero", "las", "los", "el", "la", "porque", "sin", "clase", "mananas", "tardes", "trabajo", "por",
        "viernes", "libres", "una", "un", "de", "en", "con", "para", "y", "que", "mi", "mis", "clases", "libre",
    };

    [GeneratedRegex(@"\b(?:sunday|monday|tuesday|wednesday|thursday|friday|saturday|tues|thurs|thur|sun|mon|tue|wed|thu|fri|sat)s?\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeekdayWords();

    [GeneratedRegex(@"\b(?:and|or|on|at|from|every)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectorWords();

    [GeneratedRegex(@"(\d{1,2}(?::\d{2})?(?:\s*[ap]\.?m\.?)?)\s*(?:-|to)\s*(\d{1,2}(?::\d{2})?(?:\s*[ap]\.?m\.?)?)", RegexOptions.IgnoreCase)]
    private static partial Regex RangePattern();

    [GeneratedRegex(@"\d{1,2}(?::\d{2})?\s*[ap]\.?m\.?|\d{1,4}|noon|midnight", RegexOptions.IgnoreCase)]
    private static partial Regex ClockToken();

    [GeneratedRegex(@"[a-z']+")]
    private static partial Regex WordPattern();
}

/// <summary>The state of one "describe your ideal schedule" request. Parsing is synchronous.</summary>
public sealed class ScheduleAssistantModel
{
    public abstract record State
    {
        public sealed record Idle : State;
        public sealed record Working(int Found) : State;
        public sealed record Filled(int Count) : State;
        public sealed record Failed(string Message) : State;
    }

    public State Current { get; private set; }

    public ScheduleAssistantModel(State? state = null) => Current = state ?? new State.Idle();

    public bool IsWorking => Current is State.Working;

    /// <summary>
    /// Parses <paramref name="text"/>. <paramref name="onUnderstood"/> merges the draft into the survey
    /// and returns how many settings that changed.
    /// </summary>
    public void Start(string text, Func<ScheduleAssistant.Interpretation, int> onUnderstood)
    {
        Current = new State.Working(0);
        try
        {
            var interpretation = ScheduleAssistant.Interpret(text);
            if (Current is State.Working) Current = new State.Working(interpretation.Draft.FilledCount);
            var changed = onUnderstood(interpretation);
            Current = changed > 0
                ? new State.Filled(changed)
                : new State.Failed("Everything in that is already in your survey.");
        }
        catch (ScheduleAssistant.Failure failure)
        {
            Current = new State.Failed(failure.Message);
        }
    }

    public void Cancel()
    {
        if (IsWorking) Current = new State.Idle();
    }

    public void Reset() => Current = new State.Idle();
}
