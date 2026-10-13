// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Deterministic stand-in for ScheduleAssistant.swift. The Mac app asks Apple
// Intelligence to fill a SchedulePreferencesDraft; Windows parses the same
// wishes from the sentence itself and merges them with applied(to:).

using System.Globalization;
using System.Text.RegularExpressions;

namespace TermOrganize.Core;

/// <summary>
/// Reads a description of an ideal week and merges the wishes it can defend
/// into a <see cref="SchedulePreferences"/> survey. Nothing is guessed when the
/// sentence does not mention that subject.
/// </summary>
public static partial class PreferenceParser
{
    public const int MaxDescriptionLength = 1500;

    public sealed class Interpretation
    {
        public required SchedulePreferences Preferences { get; init; }
        public required int ChangedCount { get; init; }
        public string? Message { get; init; }
        public bool Succeeded => Message is null && ChangedCount > 0;
    }

    public static Interpretation Interpret(string? text, SchedulePreferences? basePreferences = null)
    {
        var description = (text ?? "").Trim();
        var baseline = (basePreferences ?? new SchedulePreferences()).Clone();
        if (description.Length == 0)
            return Fail(baseline, "Describe the schedule you want first.");
        if (description.Length > MaxDescriptionLength)
            return Fail(baseline, "That's a lot to read at once. Try describing the most important things in a few sentences.");

        var next = baseline.Clone();
        foreach (var clause in Clauses(description))
            ApplyClause(next, clause);

        next = next.Normalized();
        var changed = next.ChangedFields(baseline).Count;
        if (changed == 0)
        {
            return new Interpretation
            {
                Preferences = baseline,
                ChangedCount = 0,
                Message = "Couldn't find any schedule wishes in that. Try mentioning times, days or commitments, like \"no classes before 10\" or \"work Tuesdays 1-5\".",
            };
        }
        return new Interpretation { Preferences = next, ChangedCount = changed };
    }

    private static Interpretation Fail(SchedulePreferences baseline, string message) =>
        new() { Preferences = baseline, ChangedCount = 0, Message = message };

    private static void ApplyClause(SchedulePreferences preferences, string clause)
    {
        var soft = IsSoft(clause) && !IsFirm(clause);
        var firm = IsFirm(clause);
        ApplyTimeLimit(preferences, clause, soft);
        ApplyDaysOff(preferences, clause, firm);
        ApplyBands(preferences, clause);
        ApplyFormat(preferences, clause);
        ApplySeats(preferences, clause);
        ApplyBreaks(preferences, clause);
        ApplyLunch(preferences, clause);
        ApplyLayout(preferences, clause);
        ApplyWorkload(preferences, clause);
        ApplyCredits(preferences, clause);
        ApplyCampus(preferences, clause);
        ApplyBlocked(preferences, clause);
    }

    private static void ApplyTimeLimit(SchedulePreferences preferences, string clause, bool soft)
    {
        var strength = soft ? PreferenceStrength.Preferred : PreferenceStrength.Required;
        var before = MatchClock(clause, @"\b(?:no classes before|nothing before|not before|earlier than|before)\s+(.+)$");
        if (before is int early)
            preferences.EarliestStart = new TimeLimit(early, strength);

        var after = MatchClock(clause, @"\b(?:no classes after|nothing after|starting after|start after|after)\s+(.+)$", afternoonBias: false);
        if (after is int startAfter && preferences.EarliestStart is null && before is null && clause.Contains("after", StringComparison.Ordinal))
        {
            // "after 10" is a start limit. "done after" is not.
            if (!ContainsAny(clause, "done", "finish", "out by", "end by"))
                preferences.EarliestStart = new TimeLimit(startAfter, strength);
        }

        var done = MatchClock(clause, @"\b(?:done by|end by|out by|finished by|finish by|no later than|later than)\s+(.+)$", afternoonBias: true);
        if (done is int latest && latest > (preferences.EarliestStart?.Minutes ?? 0))
            preferences.LatestEnd = new TimeLimit(latest, strength);
    }

    private static void ApplyDaysOff(SchedulePreferences preferences, string clause, bool firm)
    {
        if (TryBlockedRange(clause, out _, out _, out _, out _)) return;
        var days = DaySet.None;
        if (ContainsAny(clause, "weekend")) days |= DaySet.Saturday | DaySet.Sunday;
        foreach (var (name, day) in Weekdays)
        {
            if (!clause.Contains(name, StringComparison.Ordinal)) continue;
            if (ContainsAny(clause, " off", "free", "no class", "without class", "nothing on", "skip")
                || (firm && ContainsAny(clause, "work", "job", "shift")))
                days |= day;
        }
        if (days.IsEmpty) return;
        preferences.DaysOff = preferences.DaysOff.Union(days);
        if (firm || ContainsAny(clause, "no class on", "no classes on", "nothing on"))
            preferences.DaysOffStrength = PreferenceStrength.Required;
    }

    private static void ApplyBands(SchedulePreferences preferences, string clause)
    {
        var avoid = ContainsAny(clause, "avoid", "no ", "not ", "don't", "do not", "without");
        var prefer = !avoid || ContainsAny(clause, "prefer", "like", "want");
        foreach (var (cue, band) in Bands)
        {
            if (!clause.Contains(cue, StringComparison.Ordinal)) continue;
            preferences[band] = avoid && !ContainsAny(clause, "prefer " + cue, "like " + cue) ? TimePreference.Avoid : TimePreference.Prefer;
            if (!prefer && avoid) preferences[band] = TimePreference.Avoid;
        }
    }

    private static void ApplyFormat(SchedulePreferences preferences, string clause)
    {
        if (ContainsAny(clause, "hybrid")) preferences.Format = FormatPreference.Hybrid;
        else if (ContainsAny(clause, "online", "remote", "virtual", "zoom")) preferences.Format = FormatPreference.Online;
        else if (ContainsAny(clause, "in person", "in-person", "on campus", "on-campus", "face to face", "face-to-face"))
            preferences.Format = FormatPreference.InPerson;
    }

    private static void ApplySeats(SchedulePreferences preferences, string clause)
    {
        if (ContainsAny(clause, "waitlist", "wait list", "wait-list")) preferences.Seats = SeatPolicy.WaitlistOK;
        else if (ContainsAny(clause, "open seat", "open section", "open class", "seats open")) preferences.Seats = SeatPolicy.OpenOnly;
    }

    private static void ApplyBreaks(SchedulePreferences preferences, string clause)
    {
        if (ContainsAny(clause, "back to back", "back-to-back")) preferences.Breaks = BreakPreference.AvoidBackToBack;
        else if (ContainsAny(clause, "time to walk", "between class", "between building", "time to get"))
        {
            preferences.Breaks = BreakPreference.AtLeast;
            preferences.MinimumBreakMinutes = 15;
            preferences.MinimumBreakStrength = PreferenceStrength.Preferred;
        }
    }

    private static void ApplyLunch(SchedulePreferences preferences, string clause)
    {
        if (preferences.Lunch is null && ContainsAny(clause, "lunch", "meal"))
            preferences.Lunch = new LunchBreak();
    }

    private static void ApplyLayout(SchedulePreferences preferences, string clause)
    {
        if (ContainsAny(clause, "spread", "evenly")) preferences.DayLayout = DayLayout.SpreadOut;
        else if (ContainsAny(clause, "fewer days", "fewest days", "few days", "long weekend", "compact", "cram", "consolidat"))
            preferences.DayLayout = DayLayout.FewerDays;
    }

    private static void ApplyWorkload(SchedulePreferences preferences, string clause)
    {
        if (!ContainsAny(clause, "a day", "per day", "each day", "every day", "daily")) return;
        var number = FirstNumber(clause);
        if (number is null) return;
        if (clause.Contains("class", StringComparison.Ordinal) && number is >= 1 and <= 8)
            preferences.MaxClassesPerDay = number;
        if (clause.Contains("hour", StringComparison.Ordinal) && number is >= 1 and <= 12)
            preferences.MaxMinutesPerDay = number * 60;
    }

    private static void ApplyCredits(SchedulePreferences preferences, string clause)
    {
        if (!clause.Contains("credit", StringComparison.Ordinal)) return;
        var numbers = Numbers(clause).ToList();
        if (numbers.Count == 0) return;
        var lower = numbers[0];
        var upper = numbers.Count > 1 ? numbers[1] : numbers[0];
        if (lower is < 1 or > 30 || upper is < 1 or > 30) return;
        preferences.CreditTarget = new CreditTarget(Math.Min(lower, upper), Math.Max(lower, upper));
    }

    private static void ApplyCampus(SchedulePreferences preferences, string clause)
    {
        foreach (var campus in new[] { "boston", "burlington", "nahant", "oakland", "portland", "seattle", "silicon valley", "toronto", "vancouver", "online" })
        {
            if (!clause.Contains(campus, StringComparison.Ordinal)) continue;
            if (campus == "online" && preferences.Format == FormatPreference.Online) continue;
            preferences.Campus = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(campus);
            return;
        }
    }

    private static void ApplyBlocked(SchedulePreferences preferences, string clause)
    {
        if (!TryBlockedRange(clause, out var label, out var days, out var start, out var end)) return;
        if (end <= start && end < 12 * 60 && end + 12 * 60 <= 24 * 60) end += 12 * 60;
        var block = new BlockedTime { Label = label, Days = days, Start = start, End = end };
        if (!block.IsValid) return;
        var duplicate = preferences.BlockedTimes.Any(existing =>
            existing.Label.Equals(block.Label, StringComparison.OrdinalIgnoreCase)
            && existing.Days == block.Days && existing.Start == block.Start && existing.End == block.End);
        if (!duplicate) preferences.BlockedTimes.Add(block);
    }

    private static bool TryBlockedRange(string clause, out string label, out DaySet days, out int start, out int end)
    {
        label = "";
        days = DaySet.None;
        start = 0;
        end = 0;
        var match = BlockedPattern().Match(clause);
        if (!match.Success) return false;
        days = DaysIn(match.Groups["days"].Value);
        if (days.IsEmpty) return false;
        var startToken = match.Groups["start"].Value;
        var endToken = match.Groups["end"].Value;
        if (ParseClockToken(startToken, afternoonBias: false) is not int from) return false;
        if (ParseClockToken(endToken, afternoonBias: false) is not int to) return false;
        var startMeridian = startToken.Contains("am", StringComparison.Ordinal) || startToken.Contains("pm", StringComparison.Ordinal);
        var endMeridian = endToken.Contains("am", StringComparison.Ordinal) || endToken.Contains("pm", StringComparison.Ordinal);
        if (!startMeridian && !endMeridian && from <= 7 * 60 && to <= 11 * 60)
        {
            from += 12 * 60;
            to += 12 * 60;
        }
        else if (!startMeridian && endToken.Contains("pm", StringComparison.Ordinal) && from < 12 * 60)
            from += 12 * 60;
        start = from;
        end = to;
        var rawLabel = match.Groups["label"].Value.Trim();
        if (rawLabel.Length == 0 || StopLabels.Contains(rawLabel)) rawLabel = "Blocked time";
        label = char.ToUpper(rawLabel[0], CultureInfo.InvariantCulture) + rawLabel[1..];
        return true;
    }

    private static DaySet DaysIn(string text)
    {
        var days = DaySet.None;
        if (text.Contains("weekday", StringComparison.Ordinal)) days |= DaySet.Weekdays;
        if (text.Contains("weekend", StringComparison.Ordinal)) days |= DaySet.Saturday | DaySet.Sunday;
        foreach (var (name, day) in Weekdays)
            if (text.Contains(name, StringComparison.Ordinal)) days |= day;
        return days;
    }

    private static int? MatchClock(string clause, string pattern, bool afternoonBias = false)
    {
        var match = Regex.Match(clause, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        return ParseClockToken(match.Groups[1].Value.Trim(), afternoonBias);
    }

    /// <summary>Reads 10, 10am, 10:30, 18:00. A bare 1–7 with <paramref name="afternoonBias"/> is pm.</summary>
    public static int? ParseClockToken(string token, bool afternoonBias)
    {
        var match = ClockPattern().Match(token.Trim());
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups["h"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hour)) return null;
        var minute = 0;
        if (match.Groups["m"].Success && !int.TryParse(match.Groups["m"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minute))
            return null;
        if (hour is < 0 or > 24 || minute is < 0 or > 59) return null;
        var meridian = match.Groups["mer"].Value;
        if (meridian.StartsWith('p') && hour < 12) hour += 12;
        else if (meridian.StartsWith('a') && hour == 12) hour = 0;
        else if (meridian.Length == 0 && afternoonBias && hour is >= 1 and <= 7) hour += 12;
        if (hour == 24 && minute == 0) return 24 * 60;
        if (hour >= 24) return null;
        return hour * 60 + minute;
    }

    private static bool IsFirm(string clause) =>
        ContainsAny(clause, "must", "have to", "has to", "need to", "i need", "can't", "cannot", "can not", "never", "required", "i work", "my job", "my shift", "only available");

    private static bool IsSoft(string clause) =>
        ContainsAny(clause, "ideally", "prefer", "if possible", "would like", "i'd like", "id like", "rather", "hopefully", "nice if", "would be nice");

    private static bool ContainsAny(string clause, params string[] cues) =>
        cues.Any(cue => clause.Contains(cue, StringComparison.Ordinal));

    private static int? FirstNumber(string clause)
    {
        var digit = Regex.Match(clause, @"\d+");
        if (digit.Success && int.TryParse(digit.Value, out var number)) return number;
        foreach (var (word, value) in NumberWords)
            if (clause.Contains(word, StringComparison.Ordinal)) return value;
        return null;
    }

    private static IEnumerable<int> Numbers(string clause)
    {
        foreach (Match match in Regex.Matches(clause, @"\d+"))
            if (int.TryParse(match.Value, out var number)) yield return number;
    }

    private static List<string> Clauses(string text)
    {
        var lowered = " " + text.ToLowerInvariant() + " ";
        var parts = new List<string> { lowered };
        foreach (var separator in new[] { ",", ";", ".", "\n", " and ", " but " })
        {
            parts = parts.SelectMany(part => part.Split(separator, StringSplitOptions.None)).ToList();
        }
        return parts.Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
    }

    private static readonly (string Name, DaySet Day)[] Weekdays =
    [
        ("monday", DaySet.Monday), ("tuesday", DaySet.Tuesday), ("wednesday", DaySet.Wednesday),
        ("thursday", DaySet.Thursday), ("friday", DaySet.Friday), ("saturday", DaySet.Saturday), ("sunday", DaySet.Sunday),
        ("mon", DaySet.Monday), ("tue", DaySet.Tuesday), ("wed", DaySet.Wednesday),
        ("thu", DaySet.Thursday), ("fri", DaySet.Friday), ("sat", DaySet.Saturday), ("sun", DaySet.Sunday),
    ];

    private static readonly (string Cue, TimeBand Band)[] Bands =
    [
        ("early morning", TimeBand.EarlyMorning), ("midday", TimeBand.Midday), ("noon", TimeBand.Midday),
        ("afternoon", TimeBand.Afternoon), ("evening", TimeBand.Evening), ("night", TimeBand.Night),
        ("morning", TimeBand.Morning),
    ];

    private static readonly HashSet<string> StopLabels = new(StringComparer.Ordinal)
    {
        "no", "classes", "class", "nothing", "prefer", "i", "on", "every", "my",
    };

    private static readonly (string Word, int Value)[] NumberWords =
    [
        ("one", 1), ("two", 2), ("three", 3), ("four", 4), ("five", 5),
        ("six", 6), ("seven", 7), ("eight", 8),
    ];

    [GeneratedRegex(@"^(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<mer>am|pm)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClockPattern();

    [GeneratedRegex(@"\b(?:(?<label>[a-z][a-z ]{1,40}?)??\s+)?(?<days>(?:(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday|weekdays|weekends|mon|tue|wed|thu|fri|sat|sun)s?)(?:\s*(?:,|and)\s*(?:(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday|weekdays|weekends|mon|tue|wed|thu|fri|sat|sun)s?))*)\s+(?<start>\d{1,2}(?::\d{2})?\s*(?:am|pm)?)\s*(?:-|–|to)\s*(?<end>\d{1,2}(?::\d{2})?\s*(?:am|pm)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockedPattern();
}
