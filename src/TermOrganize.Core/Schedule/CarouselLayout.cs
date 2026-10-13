// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Hour range and day columns shared by every page so the grid stays put while swiping.
// Built from the chosen sections. Generated schedules are not required.

namespace TermOrganize.Core;

public sealed class ScheduleCarouselLayout : IEquatable<ScheduleCarouselLayout>
{
    public int HourStart { get; set; } = 8;
    public int HourEnd { get; set; } = 17;
    public List<int> Days { get; set; } = [1, 2, 3, 4, 5];

    public ScheduleCarouselLayout() { }

    public ScheduleCarouselLayout(IEnumerable<CourseSection> sections, AppPreferences? preferences = null)
    {
        preferences ??= new AppPreferences();
        var earliest = int.MaxValue;
        var latest = 0;
        var weekdays = new HashSet<int>();
        foreach (var section in sections)
        {
            foreach (var meeting in section.WeeklyMeetings)
            {
                if (meeting.Start is int start) earliest = Math.Min(earliest, start);
                if (meeting.End is int end) latest = Math.Max(latest, end);
                foreach (var day in meeting.Days.Indices) weekdays.Add(day);
            }
        }
        if (earliest == int.MaxValue)
        {
            HourStart = Math.Max(0, preferences.CalendarStartHour);
            HourEnd = Math.Max(preferences.CalendarStartHour + 5, Math.Min(24, preferences.CalendarEndHour));
        }
        else
        {
            var lower = Math.Max(0, earliest / 60);
            var upper = Math.Min(24, Math.Max((int)Math.Ceiling(latest / 60.0), lower + 5));
            HourStart = lower;
            HourEnd = upper;
        }
        var columns = new List<int> { 1, 2, 3, 4, 5 };
        if (preferences.ShowWeekends || weekdays.Contains(6)) columns.Add(6);
        if (preferences.ShowWeekends || weekdays.Contains(0)) columns.Insert(0, 0);
        Days = columns;
    }

    public bool Equals(ScheduleCarouselLayout? other) =>
        other is not null && HourStart == other.HourStart && HourEnd == other.HourEnd && Days.SequenceEqual(other.Days);

    public override bool Equals(object? obj) => Equals(obj as ScheduleCarouselLayout);
    public override int GetHashCode() => HashCode.Combine(HourStart, HourEnd, Days.Count);
}
