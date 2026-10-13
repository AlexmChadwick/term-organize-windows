// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// Portions derived from SearchNEU where the Swift file says so. Modified 2026-10-13: ported from Swift to C#.

namespace TermOrganize.Core;

/// <summary>Why a schedule suits the student's wishes, or where it falls short.</summary>
public sealed class ScheduleReason : IEquatable<ScheduleReason>
{
    public enum Kind { Benefit, Caveat }

    public Kind ReasonKind { get; }
    public string Text { get; }
    public string Symbol { get; }

    public ScheduleReason(Kind kind, string text, string symbol)
    {
        ReasonKind = kind;
        Text = text;
        Symbol = symbol;
    }

    public string Id => (ReasonKind == Kind.Benefit ? "+" : "-") + Text;
    public bool IsCaveat => ReasonKind == Kind.Caveat;
    public override string ToString() => Text;

    public bool Equals(ScheduleReason? other) => other is not null && ReasonKind == other.ReasonKind && Text == other.Text && Symbol == other.Symbol;
    public override bool Equals(object? obj) => Equals(obj as ScheduleReason);
    public override int GetHashCode() => HashCode.Combine(ReasonKind, Text, Symbol);
}

/// <summary>
/// Preference rules applied to single sections, plus the short explanations on result cards.
/// </summary>
public static class SchedulePreferenceRules
{
    public static uint RejectionReasons(CourseSection section, SchedulePreferences preferences)
    {
        uint reasons = 0;
        var weekly = section.WeeklyMeetings.ToList();

        if (preferences.EarliestStart is { Strength: PreferenceStrength.Required } early &&
            weekly.Any(m => (m.Start ?? int.MaxValue) < early.Minutes))
            reasons |= ScheduleFilterReason.PrefStartsTooEarly.Bit();
        if (preferences.LatestEnd is { Strength: PreferenceStrength.Required } late &&
            weekly.Any(m => (m.End ?? 0) > late.Minutes))
            reasons |= ScheduleFilterReason.PrefEndsTooLate.Bit();
        if (preferences.DaysOffStrength == PreferenceStrength.Required && !preferences.DaysOff.IsEmpty &&
            weekly.Any(m => !m.Days.IsDisjoint(preferences.DaysOff)))
            reasons |= ScheduleFilterReason.PrefDayOff.Bit();
        foreach (var block in preferences.BlockedTimes)
        {
            if (!block.IsActive || block.Strength != PreferenceStrength.Required) continue;
            if (Overlaps(block, weekly))
            {
                reasons |= ScheduleFilterReason.BlockedTime.Bit();
                break;
            }
        }
        if (preferences.Format != FormatPreference.NoPreference && preferences.FormatStrength == PreferenceStrength.Required &&
            SchedulePreferences.FormatOf(section).Mismatch(preferences.Format) > 0)
            reasons |= ScheduleFilterReason.PrefFormat.Bit();
        if (preferences.Campus is string campus && preferences.CampusStrength == PreferenceStrength.Required &&
            !section.IsOnline && !ScheduleGenerator.HasNoCampus(section) && section.Campus != campus)
            reasons |= ScheduleFilterReason.PrefCampus.Bit();
        switch (preferences.Seats)
        {
            case SeatPolicy.OpenOnly when IsFull(section):
                reasons |= ScheduleFilterReason.PrefSeats.Bit();
                break;
            case SeatPolicy.WaitlistOK when IsFull(section) && section.WaitlistRemaining <= 0:
                reasons |= ScheduleFilterReason.PrefSeats.Bit();
                break;
        }
        if (preferences.MaxMinutesPerDay is int cap)
        {
            Span<int> perDay = stackalloc int[7];
            foreach (var interval in ScheduleInterval.IntervalsFor(section))
                perDay[interval.Day] += interval.End - interval.Start;
            for (var day = 0; day < 7; day++)
            {
                if (perDay[day] > cap)
                {
                    reasons |= ScheduleFilterReason.PrefWorkload.Bit();
                    break;
                }
            }
        }
        return reasons;
    }

    /// <summary>A section with a seat limit and no seats left. Sections without a limit are never full.</summary>
    public static bool IsFull(CourseSection section) => section.SeatCapacity > 0 && section.SeatsRemaining <= 0;

    /// <summary>
    /// In-person meetings in different (or unknown) buildings, same weeks, with fewer than <paramref name="minutes"/> between them.
    /// Overlaps are conflicts, not close calls.
    /// </summary>
    public static bool TooClose(CourseSection a, IReadOnlyList<Meeting> aMeetings, CourseSection b, IReadOnlyList<Meeting> bMeetings, int minutes)
    {
        if (minutes <= 0 || a.IsOnline || b.IsOnline) return false;
        foreach (var x in aMeetings)
        {
            if (x.IsOnline || x.Start is not int xs || x.End is not int xe) continue;
            foreach (var y in bMeetings)
            {
                if (y.IsOnline || y.Start is not int ys || y.End is not int ye || x.Days.IsDisjoint(y.Days)) continue;
                var gap = ys >= xe ? ys - xe : xs >= ye ? xs - ye : -1;
                if (gap < 0 || gap >= minutes) continue;
                if (!Meeting.DateWindowsOverlap(x.StartDate, x.EndDate, y.StartDate, y.EndDate)) continue;
                var bx = x.BuildingCode ?? x.Building;
                var by = y.BuildingCode ?? y.Building;
                if (bx is not null && by is not null && bx == by) continue;
                return true;
            }
        }
        return false;
    }

    public static double SectionScore(CourseSection section, IReadOnlyList<ScheduleInterval> intervals, SchedulePreferences preferences)
    {
        var score = 0.0;
        if (preferences.HasTimeOfDayPreference)
        {
            foreach (var interval in intervals)
            {
                foreach (var band in Enum.GetValues<TimeBand>())
                {
                    var preference = preferences[band];
                    if (preference == TimePreference.Okay) continue;
                    var minutes = band.Overlap(interval.Start, interval.End);
                    if (minutes <= 0) continue;
                    var perHour = preference == TimePreference.Prefer ? ScheduleScoring.PreferredBandPerHour : ScheduleScoring.AvoidedBandPerHour;
                    score += perHour * minutes / 60.0;
                }
            }
        }
        if (preferences.EarliestStart is { Strength: PreferenceStrength.Preferred } early)
        {
            foreach (var interval in intervals)
                if (interval.Start < early.Minutes)
                    score -= SoftLimitPenalty(early.Minutes - interval.Start);
        }
        if (preferences.LatestEnd is { Strength: PreferenceStrength.Preferred } late)
        {
            foreach (var interval in intervals)
                if (interval.End > late.Minutes)
                    score -= SoftLimitPenalty(interval.End - late.Minutes);
        }
        foreach (var block in preferences.BlockedTimes)
        {
            if (!block.IsActive || block.Strength != PreferenceStrength.Preferred) continue;
            foreach (var interval in intervals)
                if (block.Overlaps(DaySet.Day(interval.Day), interval.Start, interval.End))
                    score -= ScheduleScoring.SoftBlockedPerMeeting;
        }
        if (preferences.Format != FormatPreference.NoPreference)
            score -= ScheduleScoring.FormatMismatchPerSection * SchedulePreferences.FormatOf(section).Mismatch(preferences.Format);
        if (preferences.Campus is string campus && preferences.CampusStrength == PreferenceStrength.Preferred &&
            !section.IsOnline && !ScheduleGenerator.HasNoCampus(section) && section.Campus != campus)
            score -= ScheduleScoring.CampusMismatchPerSection;
        if (preferences.PreferredInstructors.Count > 0 || preferences.AvoidedInstructors.Count > 0)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (section.Instructors is not null)
                foreach (var instructor in section.Instructors)
                    names.Add(instructor.Name.ToLowerInvariant());
            if (names.Count > 0)
            {
                if (preferences.PreferredInstructors.Any(n => names.Contains(n.ToLowerInvariant())))
                    score += ScheduleScoring.PreferredInstructorPerSection;
                if (preferences.AvoidedInstructors.Any(n => names.Contains(n.ToLowerInvariant())))
                    score += ScheduleScoring.AvoidedInstructorPerSection;
            }
        }
        return score;
    }

    public static List<ScheduleReason> Reasons(
        IReadOnlyList<CourseSection> sections, SchedulePreferences preferences, int maxBenefits = 4, int maxCaveats = 2)
    {
        if (preferences.IsDefault || sections.Count == 0) return [];
        var table = new ScheduleBuildingTable();
        var intervals = new List<ScheduleInterval>();
        foreach (var section in sections)
            intervals.AddRange(ScheduleInterval.IntervalsFor(section, table));
        var options = new ScheduleMeasureOptions();
        if (preferences.Breaks == BreakPreference.AvoidBackToBack)
            options.BackToBackBelow = SchedulePreferences.BackToBackMinutes;
        if (preferences.Breaks == BreakPreference.AtLeast)
            options.TravelMinutes = preferences.MinimumBreakMinutes;
        options.Lunch = preferences.Lunch;
        var measure = ScheduleMeasure.Measure(intervals, options);
        var hasClasses = measure.ClassDays > 0;
        var good = new List<ScheduleReason>();
        var bad = new List<ScheduleReason>();
        void Pro(string text, string symbol) => good.Add(new ScheduleReason(ScheduleReason.Kind.Benefit, text, symbol));
        void Con(string text, string symbol) => bad.Add(new ScheduleReason(ScheduleReason.Kind.Caveat, text, symbol));

        if (hasClasses && !preferences.DaysOff.IsEmpty)
        {
            var used = new DaySet((byte)(measure.ClassDayMask & preferences.DaysOff.Raw));
            var free = preferences.DaysOff.Without(used);
            if (!free.IsEmpty) Pro(SchedulePreferences.DaysOffPhrase(free), "calendar.badge.minus");
            if (!used.IsEmpty && preferences.DaysOffStrength == PreferenceStrength.Preferred)
                Con("Class on " + string.Join(", ", used.Indices.Select(i => DaySet.ShortNames[i])), "calendar.badge.exclamationmark");
        }

        if (hasClasses && preferences.EarliestStart is TimeLimit earlyLimit)
        {
            if (measure.EarliestStart >= earlyLimit.Minutes)
                Pro($"No classes before {SchedulePreferences.ShortTime(earlyLimit.Minutes)}", "sunrise");
            else if (earlyLimit.Strength == PreferenceStrength.Preferred)
                Con($"Starts {TimeOfDay.Label(measure.EarliestStart)}", "sunrise");
        }
        if (hasClasses && preferences.LatestEnd is TimeLimit lateLimit)
        {
            if (measure.LatestEnd <= lateLimit.Minutes)
                Pro($"Done by {SchedulePreferences.ShortTime(lateLimit.Minutes)}", "sunset");
            else if (lateLimit.Strength == PreferenceStrength.Preferred)
                Con($"Ends {TimeOfDay.Label(measure.LatestEnd)}", "sunset");
        }

        if (hasClasses)
        {
            switch (preferences.DayLayout)
            {
                case DayLayout.FewerDays:
                    var days = measure.CampusDays > 0 ? measure.CampusDays : measure.ClassDays;
                    if (days <= 3) Pro(days == 1 ? "1 day on campus" : $"{days} days on campus", "calendar");
                    break;
                case DayLayout.SpreadOut:
                    if (measure.ClassDays >= 4) Pro($"Spread over {measure.ClassDays} days", "calendar");
                    break;
            }
            if (preferences.Gaps == GapPreference.Compact && measure.GapMinutes <= 30)
                Pro("Short gaps", "rectangle.compress.vertical");
        }

        if (hasClasses && measure.TotalMinutes > 0)
        {
            int MinutesIn(IReadOnlyList<TimeBand> bands)
            {
                var total = 0;
                foreach (var interval in intervals)
                    foreach (var band in bands)
                        total += band.Overlap(interval.Start, interval.End);
                return total;
            }
            var preferred = preferences.PreferredBands;
            if (preferred.Count > 0)
            {
                var share = (double)MinutesIn(preferred) / measure.TotalMinutes;
                var name = SchedulePreferences.BandPhrase(preferred);
                var symbol = Symbol(preferred[0]);
                if (share >= 0.999) Pro($"All {name}", symbol);
                else if (share >= 0.6) Pro($"Mostly {name}", symbol);
            }
            var avoided = preferences.AvoidedBands;
            if (avoided.Count > 0)
            {
                var name = SchedulePreferences.BandPhrase(avoided);
                if (MinutesIn(avoided) == 0) Pro($"No {name}", Symbol(avoided[0]));
                else Con($"Includes {name}", Symbol(avoided[0]));
            }
        }

        if (hasClasses && preferences.Lunch is not null)
        {
            var free = measure.ClassDays - measure.LunchMisses;
            if (measure.LunchMisses == 0) Pro("Lunch free every day", "fork.knife");
            else if (free * 2 >= measure.ClassDays) Pro("Lunch free most days", "fork.knife");
            else Con("Lunch often busy", "fork.knife");
        }
        if (hasClasses)
        {
            switch (preferences.Breaks)
            {
                case BreakPreference.AvoidBackToBack:
                    if (measure.BackToBack == 0) Pro("No back-to-back classes", "figure.walk");
                    else Con(measure.BackToBack == 1 ? "1 back-to-back" : $"{measure.BackToBack} back-to-back", "figure.walk");
                    break;
                case BreakPreference.AtLeast:
                    if (measure.TightTransitions == 0) Pro("Time to walk between classes", "figure.walk");
                    else Con("Tight between buildings", "figure.walk");
                    break;
            }
        }

        if (preferences.PreferredInstructors.Count > 0 || preferences.AvoidedInstructors.Count > 0)
        {
            List<string> Names(IReadOnlyList<string> wanted)
            {
                var set = wanted.Select(n => n.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
                var result = new List<string>();
                foreach (var section in sections)
                {
                    if (section.Instructors is null) continue;
                    foreach (var instructor in section.Instructors)
                        if (set.Contains(instructor.Name.ToLowerInvariant()) && !result.Contains(instructor.DisplayName))
                            result.Add(instructor.DisplayName);
                }
                return result;
            }
            var liked = Names(preferences.PreferredInstructors);
            if (liked.Count == 1) Pro($"With {liked[0]}", "person.fill");
            else if (liked.Count > 1) Pro($"With {liked.Count} preferred instructors", "person.2.fill");
            var disliked = Names(preferences.AvoidedInstructors);
            if (disliked.Count > 0)
                Con(disliked.Count == 1 ? $"With {disliked[0]}" : $"With {disliked.Count} avoided instructors", "person.fill.xmark");
        }

        if (preferences.Format != FormatPreference.NoPreference)
        {
            var formats = sections.Select(SchedulePreferences.FormatOf).ToList();
            var deliveries = formats.Where(f => f != SectionFormat.Other).ToList();
            if (deliveries.Count > 0)
            {
                var title = preferences.Format.Title().ToLowerInvariant();
                if (deliveries.All(f => f.Mismatch(preferences.Format) == 0))
                    Pro($"All {title}", preferences.Format == FormatPreference.Online ? "wifi" : "building.2");
                else if (preferences.FormatStrength == PreferenceStrength.Preferred)
                    Con($"Not all {title}", "wifi");
            }
        }
        if (preferences.Campus is string wantedCampus)
        {
            var onCampus = sections.Where(s => !s.IsOnline && !ScheduleGenerator.HasNoCampus(s)).ToList();
            if (onCampus.Count > 0)
            {
                if (onCampus.All(s => s.Campus == wantedCampus)) Pro($"{wantedCampus} campus", "mappin.and.ellipse");
                else if (preferences.CampusStrength == PreferenceStrength.Preferred) Con("Includes another campus", "mappin.and.ellipse");
            }
        }

        switch (preferences.Seats)
        {
            case SeatPolicy.OpenOnly:
                Pro("All sections open", "chair.lounge");
                break;
            case SeatPolicy.WaitlistOK:
                var waitlisted = sections.Count(IsFull);
                if (waitlisted == 0) Pro("All sections open", "chair.lounge");
                else Con(waitlisted == 1 ? "1 waitlisted section" : $"{waitlisted} waitlisted sections", "chair.lounge");
                break;
        }

        foreach (var block in preferences.BlockedTimes)
        {
            if (!block.IsActive || block.Strength != PreferenceStrength.Preferred) continue;
            var touches = intervals.Any(interval => block.Overlaps(DaySet.Day(interval.Day), interval.Start, interval.End));
            if (touches) Con($"Overlaps {block.Label}", "calendar.badge.exclamationmark");
        }

        return Unique(good).Take(maxBenefits).Concat(Unique(bad).Take(maxCaveats)).ToList();
    }

    /// <summary>SF Symbol names from the Swift time-of-day bands. The C# band type does not carry them.</summary>
    public static string Symbol(TimeBand band) => band switch
    {
        TimeBand.EarlyMorning => "sunrise",
        TimeBand.Morning => "sun.horizon",
        TimeBand.Midday => "sun.max",
        TimeBand.Afternoon => "sun.haze",
        TimeBand.Evening => "sunset",
        TimeBand.Night => "moon.stars",
        _ => "sun.max",
    };

    private static bool Overlaps(BlockedTime block, IReadOnlyList<Meeting> meetings) =>
        meetings.Any(meeting => meeting.Start is int start && meeting.End is int end && block.Overlaps(meeting.Days, start, end));

    private static double SoftLimitPenalty(int minutesOutside) =>
        Math.Min(ScheduleScoring.SoftLimitCap, ScheduleScoring.SoftLimitBase + minutesOutside / 10.0);

    private static List<ScheduleReason> Unique(List<ScheduleReason> reasons)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return reasons.Where(reason => seen.Add(reason.Id)).ToList();
    }
}
