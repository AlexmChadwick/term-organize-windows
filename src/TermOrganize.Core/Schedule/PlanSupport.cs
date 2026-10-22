// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Editing helpers, conflict detection, a plan resolved against a catalog, calendar blocks,
// and the wording of a chosen section. Metrics lines that describe a GeneratedSchedule
// stay with the generator.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TermOrganize.Core;

public static partial class PlanEditing
{
    /// <summary>"Plan A", "Plan B", … skipping names that are already used.</summary>
    public static string NextPlanName(IEnumerable<string> existing)
    {
        var used = existing.Select(name => name.Trim().ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        foreach (var letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            var name = $"Plan {letter}";
            if (!used.Contains(name.ToLowerInvariant())) return name;
        }
        var n = 27;
        while (used.Contains($"plan {n}")) n++;
        return $"Plan {n}";
    }

    /// <summary>
    /// Chooses <paramref name="crn"/> for a course. By default this replaces the course's other sections.
    /// Special topics pass <paramref name="replaceOtherSections"/> false so a second CRN stays selected.
    /// Null clears every section in <paramref name="courseCrns"/> when replacing, and does nothing otherwise.
    /// </summary>
    public static void SetSection(string? crn, ISet<string> courseCrns, SchedulePlan plan, bool replaceOtherSections = true, CourseKey? courseKey = null)
    {
        if (replaceOtherSections) plan.SelectedCRNs.RemoveAll(courseCrns.Contains);
        else if (crn is null) return;
        if (crn is not null && !plan.SelectedCRNs.Contains(crn)) plan.SelectedCRNs.Add(crn);
        if (courseKey is CourseKey key && plan.Course(key) is PlanCourse row)
        {
            if (replaceOtherSections) row.PinnedCrn = string.IsNullOrWhiteSpace(crn) ? null : crn.Trim();
            else if (string.IsNullOrWhiteSpace(row.PinnedCrn)) row.PinnedCrn = crn!.Trim();
        }
    }

    /// <summary>Removes one selected CRN and leaves every other pin in place.</summary>
    public static void ClearCrn(string crn, SchedulePlan plan)
    {
        plan.SelectedCRNs.RemoveAll(item => string.Equals(item, crn, StringComparison.Ordinal));
        foreach (var course in plan.Courses)
            if (string.Equals(course.PinnedCrn, crn, StringComparison.Ordinal)) course.PinnedCrn = null;
    }

    /// <summary>
    /// Keeps every CRN the user already chose. A generated section is added only for a course
    /// that has no pin. A generated alternate of a pinned course is ignored. A CRN the catalog
    /// does not know stays.
    /// </summary>
    public static void ApplyGeneratedSchedule(IReadOnlyList<string> generatedCrns, CatalogIndex? index, SchedulePlan plan)
    {
        if (index is null) return;
        var kept = new List<string>(plan.SelectedCRNs);
        var pinnedKeys = new HashSet<CourseKey>();
        foreach (var course in plan.Courses)
            if (!string.IsNullOrWhiteSpace(course.PinnedCrn)) pinnedKeys.Add(course.Key);
        foreach (var crn in plan.SelectedCRNs)
        {
            if (index.Section(crn) is CourseSection section) pinnedKeys.Add(section.CourseKey);
        }
        foreach (var crn in generatedCrns)
        {
            if (kept.Contains(crn)) continue;
            var section = index.Section(crn);
            if (section is null) continue;
            if (pinnedKeys.Contains(section.CourseKey)) continue;
            if (!plan.Courses.Any(course => course.Key == section.CourseKey)) continue;
            kept.Add(crn);
            pinnedKeys.Add(section.CourseKey);
        }
        plan.SelectedCRNs = kept;
    }

    /// <summary>
    /// Selected CRNs after a catalog refresh. Identity is the CRN itself: a missing section
    /// is not replaced by another section of the same course number.
    /// </summary>
    public static List<string> PinsAfterRefresh(IReadOnlyList<string> selected, CatalogIndex index) =>
        selected.Select(crn => index.Section(crn)?.Crn ?? crn).ToList();

    public static void SetRequired(bool isRequired, CourseKey key, SchedulePlan plan)
    {
        var course = plan.Courses.FirstOrDefault(item => item.Key == key);
        if (course is not null) course.IsRequired = isRequired;
    }

    public static void SetExcluded(bool excluded, string crn, CourseKey key, SchedulePlan plan)
    {
        var course = plan.Courses.FirstOrDefault(item => item.Key == key);
        if (course is null) return;
        if (excluded) course.ExcludedCRNs.Add(crn);
        else course.ExcludedCRNs.Remove(crn);
    }

    public static bool IsFavorite(SchedulePlan plan, IReadOnlyList<string> sortedCrns) =>
        plan.FavoriteCombinations.Any(combination => combination.SequenceEqual(sortedCrns));

    public static void SetFavorite(bool favorite, IReadOnlyList<string> sortedCrns, SchedulePlan plan)
    {
        plan.FavoriteCombinations.RemoveAll(combination => combination.SequenceEqual(sortedCrns));
        if (favorite) plan.FavoriteCombinations.Add(sortedCrns.ToList());
    }

    public static void RemoveStaleCrns(IEnumerable<string> crns, SchedulePlan plan)
    {
        var stale = crns.ToHashSet(StringComparer.Ordinal);
        plan.SelectedCRNs.RemoveAll(stale.Contains);
    }

    /// <summary>
    /// Drops selected sections and favorite combinations whose course is no longer in the plan.
    /// CRNs the catalog doesn't know are left alone.
    /// </summary>
    public static (List<string> Selected, List<List<string>> Favorites) PruneOrphans(CatalogIndex index, SchedulePlan plan)
    {
        var keys = plan.Courses.Select(course => course.Key).ToHashSet();
        bool IsOrphan(string crn)
        {
            var section = index.Section(crn);
            return section is not null && !keys.Contains(section.CourseKey);
        }
        var selected = plan.SelectedCRNs.Where(IsOrphan).ToList();
        var favorites = plan.FavoriteCombinations.Where(combination => combination.Any(IsOrphan)).ToList();
        plan.SelectedCRNs.RemoveAll(IsOrphan);
        plan.FavoriteCombinations.RemoveAll(combination => combination.Any(IsOrphan));
        return (selected, favorites);
    }

    /// <summary>Puts back exactly the course that was removed, at its old position.</summary>
    public static void Restore(PlanCourseRemoval removal, SchedulePlan plan)
    {
        if (plan.Contains(removal.Course.Key)) return;
        var index = Math.Clamp(removal.Index, 0, plan.Courses.Count);
        plan.Courses.Insert(index, removal.Course);
        foreach (var crn in removal.SelectedCRNs)
            if (!plan.SelectedCRNs.Contains(crn)) plan.SelectedCRNs.Add(crn);
        foreach (var combination in removal.FavoriteCombinations)
            if (!plan.FavoriteCombinations.Any(existing => existing.SequenceEqual(combination)))
                plan.FavoriteCombinations.Add(combination);
    }
}

/// <summary>A course taken out of a plan, kept so undo can put back just that course.</summary>
public sealed class PlanCourseRemoval
{
    public PlanCourse Course { get; }
    public int Index { get; }
    public List<string> SelectedCRNs { get; set; }
    public List<List<string>> FavoriteCombinations { get; set; }

    public PlanCourseRemoval(PlanCourse course, int index, IEnumerable<string> selectedCrns, IEnumerable<IEnumerable<string>> favoriteCombinations)
    {
        Course = course;
        Index = index;
        SelectedCRNs = selectedCrns.ToList();
        FavoriteCombinations = favoriteCombinations.Select(combination => combination.ToList()).ToList();
    }
}

/// <summary>Two selected sections whose weekly meetings overlap.</summary>
public sealed class ScheduleConflict
{
    public required CourseSection First { get; init; }
    public required CourseSection Second { get; init; }
    public DaySet Days { get; init; }
    public int Start { get; init; }
    public int End { get; init; }
    public string Id => $"{First.Crn}|{Second.Crn}";

    public string Summary =>
        $"{First.CourseKey} overlaps {Second.CourseKey} · {Days.ShortLabel} {TimeOfDay.RangeLabel(Start, End)}";
}

public static class ScheduleConflicts
{
    public static (DaySet Days, int Start, int End)? Overlap(CourseSection a, CourseSection b)
    {
        var days = DaySet.None;
        (int Start, int End)? window = null;
        foreach (var first in a.WeeklyMeetings)
        {
            foreach (var second in b.WeeklyMeetings)
            {
                if (!first.Overlaps(second)) continue;
                if (first.Start is not int s1 || first.End is not int e1 || second.Start is not int s2 || second.End is not int e2) continue;
                days |= first.Days.Intersection(second.Days);
                var start = Math.Max(s1, s2);
                var end = Math.Min(e1, e2);
                if (window is null || start < window.Value.Start) window = (start, end);
            }
        }
        return window is null ? null : (days, window.Value.Start, window.Value.End);
    }

    public static List<ScheduleConflict> Find(IReadOnlyList<CourseSection> sections)
    {
        var result = new List<ScheduleConflict>();
        for (var i = 0; i < sections.Count; i++)
        {
            for (var j = i + 1; j < sections.Count; j++)
            {
                if (Overlap(sections[i], sections[j]) is not { } overlap) continue;
                result.Add(new ScheduleConflict
                {
                    First = sections[i],
                    Second = sections[j],
                    Days = overlap.Days,
                    Start = overlap.Start,
                    End = overlap.End,
                });
            }
        }
        return result;
    }

    public static List<CourseSection> SectionsOverlapping(CourseSection section, IEnumerable<CourseSection> others) =>
        others.Where(other => other.Crn != section.Crn && Overlap(section, other) is not null).ToList();
}

/// <summary>One block on a week calendar.</summary>
public sealed class CalendarEvent
{
    public required string Id { get; init; }
    public required string Title { get; set; }
    public string? Subtitle { get; init; }
    public string? Detail { get; init; }
    public int Day { get; init; }
    public int Start { get; init; }
    public int End { get; init; }
    public required CourseColor Color { get; init; }
    public bool IsConflict { get; set; }
    public bool IsGhost { get; init; }
    public string? Payload { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }

    public static List<CalendarEvent> Events(CourseSection section, CourseColor color, bool isGhost = false, string? title = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var events = new List<CalendarEvent>();
        var offset = 0;
        foreach (var meeting in section.WeeklyMeetings)
        {
            if (meeting.Start is int start && meeting.End is int end)
            {
                foreach (var day in meeting.Days.Indices)
                {
                    if (!seen.Add($"{day}-{start}-{end}-{meeting.LocationLabel}")) continue;
                    events.Add(new CalendarEvent
                    {
                        Id = $"{section.Crn}-{offset}-{day}-{start}",
                        Title = title ?? section.CourseKey.ToString(),
                        Subtitle = meeting.IsOnline ? "Online" : meeting.LocationLabel,
                        Detail = section.ClassType,
                        Day = day,
                        Start = start,
                        End = end,
                        Color = color,
                        IsGhost = isGhost,
                        Payload = section.Crn,
                        StartDate = meeting.StartDate,
                        EndDate = meeting.EndDate,
                    });
                }
            }
            offset++;
        }
        return events;
    }

    public static List<CalendarEvent> MarkingConflicts(IReadOnlyList<CalendarEvent> events)
    {
        var result = events.Select(Copy).ToList();
        for (var i = 0; i < result.Count; i++)
        {
            for (var j = 0; j < result.Count; j++)
            {
                if (i == j || result[i].IsGhost || result[j].IsGhost) continue;
                var a = result[i];
                var b = result[j];
                if (a.Day == b.Day && a.Start < b.End && b.Start < a.End && a.Payload != b.Payload
                    && Meeting.DateWindowsOverlap(a.StartDate, a.EndDate, b.StartDate, b.EndDate))
                    result[i].IsConflict = true;
            }
        }
        return result;
    }

    private static CalendarEvent Copy(CalendarEvent source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        Subtitle = source.Subtitle,
        Detail = source.Detail,
        Day = source.Day,
        Start = source.Start,
        End = source.End,
        Color = source.Color,
        IsConflict = source.IsConflict,
        IsGhost = source.IsGhost,
        Payload = source.Payload,
        StartDate = source.StartDate,
        EndDate = source.EndDate,
    };
}

public static class CalendarLayout
{
    /// <summary>Lane index and lane count for each event id. Overlapping events sit side by side.</summary>
    public static Dictionary<string, (int Index, int Count)> Lanes(IReadOnlyList<CalendarEvent> events)
    {
        var result = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        var group = new List<(CalendarEvent Event, int Lane)>();
        var laneEnds = new List<int>();
        var groupEnd = int.MinValue;
        void Flush()
        {
            var count = Math.Max(1, laneEnds.Count);
            foreach (var item in group) result[item.Event.Id] = (item.Lane, count);
            group.Clear();
            laneEnds.Clear();
        }
        foreach (var calendarEvent in events.OrderBy(item => item.Start).ThenBy(item => item.End))
        {
            if (calendarEvent.Start >= groupEnd) Flush();
            var lane = laneEnds.FindIndex(end => end <= calendarEvent.Start);
            if (lane >= 0)
            {
                laneEnds[lane] = calendarEvent.End;
                group.Add((calendarEvent, lane));
            }
            else
            {
                laneEnds.Add(calendarEvent.End);
                group.Add((calendarEvent, laneEnds.Count - 1));
            }
            groupEnd = Math.Max(groupEnd, calendarEvent.End);
        }
        Flush();
        return result;
    }
}

/// <summary>A plan resolved against a term's catalog.</summary>
public sealed class ResolvedPlan
{
    public sealed class Entry
    {
        public required PlanCourse PlanCourse { get; init; }
        public Course? Course { get; init; }
        public required IReadOnlyList<CourseSection> AllSections { get; init; }
        public required IReadOnlyList<CourseSection> Selected { get; init; }
        public bool CatalogAvailable { get; init; }

        public CourseKey Id => PlanCourse.Key;
        public CourseKey Key => PlanCourse.Key;
        public CourseColor Color => CourseColor.At(PlanCourse.ColorIndex);
        public CourseSection? Chosen => Selected.FirstOrDefault();
        public bool IsMissing => CatalogAvailable && Course is null && AllSections.Count == 0;
        public bool HasNoSections => CatalogAvailable && Course is not null && AllSections.Count == 0;
        public string Title => Course?.Title ?? AllSections.FirstOrDefault()?.Title ?? "";
        public List<CourseSection> AvailableSections => AllSections.Where(section => !PlanCourse.ExcludedCRNs.Contains(section.Crn)).ToList();
        public HashSet<string> CourseCrns => AllSections.Select(section => section.Crn).ToHashSet(StringComparer.Ordinal);
        public bool CountsTowardsCredits => PlanCourse.IsRequired || Selected.Count > 0;
    }

    public SchedulePlan Plan { get; }
    public bool HasCatalog { get; }
    public List<Entry> Entries { get; }
    public List<string> UnlistedCRNs { get; }
    public List<string> OrphanedCRNs { get; }
    public List<CourseSection> SelectedSections { get; }
    public List<CalendarEvent> Events { get; }
    public List<ScheduleConflict> Conflicts { get; }
    public List<(Entry Entry, CourseSection Section)> Untimed { get; }
    public List<(Entry Entry, CourseSection Section)> FullSections { get; }
    public List<CourseKey> MissingCourses { get; }
    public (double Min, double Max) Credits { get; }

    public ResolvedPlan(SchedulePlan plan, CatalogIndex? index)
    {
        Plan = plan;
        HasCatalog = index is not null;
        var bySection = new Dictionary<string, CourseSection>(StringComparer.Ordinal);
        if (index is not null)
        {
            foreach (var crn in plan.SelectedCRNs)
                if (index.Section(crn) is CourseSection section) bySection[crn] = section;
        }

        var entries = new List<Entry>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var planCourse in plan.Courses)
        {
            var all = index?.Sections(planCourse.Key).ToList() ?? [];
            var selected = new List<CourseSection>();
            foreach (var crn in plan.SelectedCRNs)
            {
                if (!bySection.TryGetValue(crn, out var section) || section.CourseKey != planCourse.Key) continue;
                claimed.Add(crn);
                selected.Add(section);
            }
            entries.Add(new Entry
            {
                PlanCourse = planCourse,
                Course = index?.Course(planCourse.Key),
                AllSections = all,
                Selected = selected,
                CatalogAvailable = index is not null,
            });
        }
        Entries = entries;
        var unclaimed = index is null ? [] : plan.SelectedCRNs.Where(crn => !claimed.Contains(crn)).ToList();
        UnlistedCRNs = unclaimed.Where(crn => !bySection.ContainsKey(crn)).ToList();
        OrphanedCRNs = unclaimed.Where(bySection.ContainsKey).ToList();

        var sections = entries.SelectMany(entry => entry.Selected).ToList();
        SelectedSections = sections;
        var events = new List<CalendarEvent>();
        var untimed = new List<(Entry, CourseSection)>();
        var full = new List<(Entry, CourseSection)>();
        foreach (var entry in entries)
        {
            foreach (var section in entry.Selected)
            {
                var blocks = CalendarEvent.Events(section, entry.Color);
                if (blocks.Count == 0) untimed.Add((entry, section));
                events.AddRange(blocks);
                if (section.IsFull) full.Add((entry, section));
            }
        }
        Events = CalendarEvent.MarkingConflicts(events);
        Untimed = untimed;
        FullSections = full;
        Conflicts = ScheduleConflicts.Find(sections);
        MissingCourses = entries.Where(entry => entry.IsMissing || entry.HasNoSections).Select(entry => entry.Key).ToList();

        double low = 0, high = 0;
        foreach (var entry in entries.Where(entry => entry.CountsTowardsCredits))
        {
            if (entry.Course is null) continue;
            if (entry.Course.IsVariableCredit)
            {
                var chosen = VariableCredits.Exact(entry.Course, entry.PlanCourse.Credits) ?? entry.Course.StartingCredits;
                low += chosen;
                high += chosen;
                continue;
            }
            var copies = Math.Max(1, entry.Selected.Count);
            low += entry.Course.MinCredits * copies;
            high += entry.Course.MaxCredits * copies;
        }
        Credits = (low, high);
    }

    public List<string> StaleCRNs => UnlistedCRNs.Concat(OrphanedCRNs).ToList();
    public string CreditsLabel => CreditFormatter.Label(Credits.Min, Credits.Max);
    public bool IsOverCreditLimit => Credits.Min > 19;
    public bool IsEmpty => Entries.Count == 0;
    public bool HasSelection => SelectedSections.Count > 0;
    public List<string> CrnsForRegistration => SelectedSections.Select(section => section.Crn).ToList();
    public Entry? EntryFor(CourseKey key) => Entries.FirstOrDefault(entry => entry.Key == key);

    public (Entry Entry, CourseSection Section)? EntryForCrn(string crn)
    {
        foreach (var entry in Entries)
        {
            var section = entry.Selected.FirstOrDefault(item => item.Crn == crn);
            if (section is not null) return (entry, section);
        }
        return null;
    }

    public List<Entry> CoursesWithoutSection =>
        Entries.Where(entry => entry.Selected.Count == 0 && !entry.IsMissing && !entry.HasNoSections).ToList();
}

public static class ScheduleFormat
{
    public static string SectionTitle(CourseSection section) =>
        string.IsNullOrEmpty(section.SectionNumber) ? section.ClassType : $"{section.ClassType} {section.SectionNumber}";

    public static string SectionTitleWithCrn(CourseSection section) => $"{SectionTitle(section)} · CRN {section.Crn}";

    public static string? Location(CourseSection section)
    {
        var meetings = section.WeeklyMeetings.ToList();
        var inPerson = meetings.FirstOrDefault(meeting => !meeting.IsOnline && (meeting.BuildingCode ?? meeting.Building) is not null);
        if (inPerson is not null) return inPerson.LocationLabel;
        if (section.IsOnline || meetings.Any(meeting => meeting.IsOnline)) return "Online";
        return meetings.Count == 0 ? null : "Location TBA";
    }

    public static string? Instructors(CourseSection section)
    {
        if (section.Instructors is not { Count: > 0 } instructors) return null;
        return string.Join(", ", instructors.OrderByDescending(instructor => instructor.Primary).Select(instructor => instructor.DisplayName));
    }

    public static string SectionSummary(CourseSection section) => $"{SectionTitle(section)} · {section.ScheduleSummary}";

    public static string Duration(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;
        return (hours, rest) switch
        {
            (0, _) => $"{rest}m",
            (_, 0) => $"{hours}h",
            _ => $"{hours}h {rest}m",
        };
    }

    public static DaySet CampusDays(IEnumerable<CourseSection> sections)
    {
        var days = DaySet.None;
        foreach (var section in sections.Where(section => !section.IsOnline))
            foreach (var meeting in section.WeeklyMeetings.Where(meeting => !meeting.IsOnline))
                days |= meeting.Days;
        return days;
    }

    public static DaySet ClassDays(IEnumerable<CourseSection> sections)
    {
        var days = DaySet.None;
        foreach (var section in sections) days |= section.MeetingDays;
        return days;
    }

    public static string SeatsText(CourseSection section)
    {
        if (section.SeatsRemaining > 0) return $"{section.SeatsRemaining} of {section.SeatCapacity} open";
        if (section.WaitlistCapacity > 0) return $"Full · {section.WaitlistRemaining} waitlist";
        return "Full";
    }

    public static string FullLabel(CourseSection section) =>
        $"{section.CourseKey} · {SectionTitle(section)} · CRN {section.Crn}";
}

public static class PlanCalendarSupport
{
    public static (int Start, int End) HourRange(IReadOnlyList<CalendarEvent> events, int? startHour, int? endHour, bool compact)
    {
        var defaultStart = startHour ?? 8;
        var defaultEnd = endHour ?? 18;
        if (events.Count == 0) return (defaultStart, Math.Max(defaultStart + 1, defaultEnd));
        var minStart = events.Min(item => item.Start) / 60;
        var maxEnd = (int)Math.Ceiling(events.Max(item => item.End) / 60.0);
        var lower = Math.Min(startHour ?? minStart, minStart);
        var upper = Math.Max(endHour ?? maxEnd, Math.Max(maxEnd, lower + (compact ? 1 : 4)));
        return (Math.Max(0, lower), Math.Min(24, upper));
    }

    public static double HourHeight(double height, int start, int end) =>
        height / Math.Max(1, end - start);

    public static List<int> VisibleDays(IReadOnlyList<CalendarEvent> events, bool showWeekends)
    {
        var days = new List<int> { 1, 2, 3, 4, 5 };
        var eventDays = events.Select(item => item.Day).ToHashSet();
        if (showWeekends || eventDays.Contains(6)) days.Add(6);
        if (showWeekends || eventDays.Contains(0)) days.Insert(0, 0);
        return days;
    }
}

public static class ScheduleDates
{
    public static TimeZoneInfo NewYork => TermCalendar.NewYork;

    public static (int Year, int Month, int Day)? Components(string? iso)
    {
        if (iso is null) return null;
        var parts = iso.Split('-');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)) return null;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month) || month is < 1 or > 12) return null;
        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day) || day is < 1 or > 31) return null;
        return (year, month, day);
    }

    public static DateTime? Date(string? iso)
    {
        if (Components(iso) is not { } parts) return null;
        return new DateTime(parts.Year, parts.Month, parts.Day);
    }

    public static bool TryComponents(string? iso, out int year, out int month, out int day)
    {
        if (Components(iso) is not { } parts)
        {
            year = month = day = 0;
            return false;
        }
        year = parts.Year;
        month = parts.Month;
        day = parts.Day;
        return true;
    }
}

/// <summary>
/// The primary schedule flattened into weekly meetings for widgets and Up Next.
/// The file is written only when <c>TERM_ORGANIZE_APP_GROUP</c> names a directory.
/// </summary>
public sealed class WidgetScheduleData
{
    public const string AppGroupIdentifier = "group.com.alexchadwick.neuclasssearch";
    public const string FileName = "WidgetSchedule.json";

    public sealed class Meeting
    {
        public required string Id { get; set; }
        public required string Crn { get; set; }
        public required string CourseCode { get; set; }
        public required string Title { get; set; }
        public required string Location { get; set; }
        public required string ClassType { get; set; }
        /// <summary>0 = Sunday … 6 = Saturday.</summary>
        public int Weekday { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public int ColorIndex { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }

        public bool IsActive(DateTime date)
        {
            if (StartDate is null && EndDate is null) return true;
            var iso = WidgetScheduleData.IsoDay(date);
            if (StartDate is not null && string.CompareOrdinal(iso, StartDate) < 0) return false;
            if (EndDate is not null && string.CompareOrdinal(iso, EndDate) > 0) return false;
            return true;
        }
    }

    public sealed class Occurrence
    {
        public required Meeting Meeting { get; init; }
        public DateTime Start { get; init; }
        public DateTime End { get; init; }
        public string Id => $"{Meeting.Id}-{new DateTimeOffset(DateTime.SpecifyKind(Start, DateTimeKind.Local)).ToUnixTimeSeconds()}";
    }

    public DateTime GeneratedAt { get; set; }
    public string TermCode { get; set; } = "";
    public string TermName { get; set; } = "";
    public string PlanName { get; set; } = "";
    public DateTime? ClassesStart { get; set; }
    public DateTime? ClassesEnd { get; set; }
    public List<Meeting> Meetings { get; set; } = [];

    public static string IsoDay(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string? FilePath
    {
        get
        {
            var root = Environment.GetEnvironmentVariable("TERM_ORGANIZE_APP_GROUP");
            return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, FileName);
        }
    }

    public static WidgetScheduleData? Load()
    {
        var path = FilePath;
        if (path is null || !File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<WidgetScheduleData>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Save()
    {
        var path = FilePath;
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }

    public static void Clear()
    {
        var path = FilePath;
        if (path is null || !File.Exists(path)) return;
        File.Delete(path);
    }

    public List<Occurrence> Occurrences(DateTime date, int limit = 10)
    {
        var results = new List<Occurrence>();
        if (Meetings.Count == 0) return results;
        var startOfToday = date.Date;
        for (var dayOffset = 0; dayOffset < 14; dayOffset++)
        {
            var day = startOfToday.AddDays(dayOffset);
            if (ClassesEnd is DateTime end && day.Date > end.Date) break;
            if (ClassesStart is DateTime start && day.Date < start.Date) continue;
            var weekday = (int)day.DayOfWeek;
            foreach (var meeting in Meetings.Where(item => item.Weekday == weekday && item.IsActive(day)).OrderBy(item => item.Start))
            {
                var startAt = day.AddMinutes(meeting.Start);
                var endAt = day.AddMinutes(meeting.End);
                if (endAt <= date) continue;
                results.Add(new Occurrence { Meeting = meeting, Start = startAt, End = endAt });
                if (results.Count >= limit) return results;
            }
        }
        return results;
    }

    public List<Occurrence> Classes(DateTime date)
    {
        var day = date.Date;
        if (ClassesStart is DateTime start && day < start.Date) return [];
        if (ClassesEnd is DateTime end && day > end.Date) return [];
        var weekday = (int)day.DayOfWeek;
        return Meetings.Where(item => item.Weekday == weekday && item.IsActive(day)).OrderBy(item => item.Start)
            .Select(meeting => new Occurrence
            {
                Meeting = meeting,
                Start = day.AddMinutes(meeting.Start),
                End = day.AddMinutes(meeting.End),
            })
            .ToList();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Flattens a plan into the weekly meetings shared with widgets and Up Next.</summary>
public static class ScheduleWidgetBuilder
{
    public static WidgetScheduleData Make(SchedulePlan plan, CatalogIndex index, DateTime? now = null)
    {
        var meetings = new List<WidgetScheduleData.Meeting>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        DateTime? firstDay = null;
        DateTime? lastDay = null;
        foreach (var crn in plan.SelectedCRNs)
        {
            var section = index.Section(crn);
            if (section is null) continue;
            var planCourse = plan.Course(section.CourseKey);
            if (planCourse is null) continue;
            var title = index.Course(section.CourseKey)?.Title ?? section.Title;
            foreach (var meeting in section.WeeklyMeetings)
            {
                if (meeting.Start is not int start || meeting.End is not int end) continue;
                var location = meeting.IsOnline || section.IsOnline ? "Online" : meeting.LocationLabel;
                foreach (var day in meeting.Days.Indices)
                {
                    var key = $"{section.Crn}|{day}|{start}|{end}|{meeting.StartDate}|{meeting.EndDate}";
                    if (!seen.Add(key)) continue;
                    var baseId = $"{section.Crn}-{day}-{start}";
                    var id = baseId;
                    var suffix = 2;
                    while (!usedIds.Add(id))
                    {
                        id = $"{baseId}-{suffix}";
                        suffix++;
                    }
                    meetings.Add(new WidgetScheduleData.Meeting
                    {
                        Id = id,
                        Crn = section.Crn,
                        CourseCode = section.CourseKey.ToString(),
                        Title = title,
                        Location = location,
                        ClassType = section.ClassType,
                        Weekday = day,
                        Start = start,
                        End = end,
                        ColorIndex = planCourse.ColorIndex,
                        StartDate = meeting.StartDate,
                        EndDate = meeting.EndDate,
                    });
                }
                if (ScheduleDates.Date(meeting.StartDate) is DateTime startDay)
                    firstDay = firstDay is null || startDay < firstDay ? startDay : firstDay;
                if (ScheduleDates.Date(meeting.EndDate) is DateTime endDay)
                    lastDay = lastDay is null || endDay > lastDay ? endDay : lastDay;
            }
        }
        meetings.Sort((a, b) =>
        {
            var day = a.Weekday.CompareTo(b.Weekday);
            if (day != 0) return day;
            var clock = a.Start.CompareTo(b.Start);
            return clock != 0 ? clock : string.CompareOrdinal(a.Id, b.Id);
        });
        return new WidgetScheduleData
        {
            GeneratedAt = now ?? DateTime.Now,
            TermCode = plan.TermCode,
            TermName = index.Term.Name,
            PlanName = plan.Name,
            ClassesStart = firstDay,
            ClassesEnd = lastDay,
            Meetings = meetings,
        };
    }
}
