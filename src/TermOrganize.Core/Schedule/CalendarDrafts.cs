// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// One class meeting, ready for Windows Calendar. A weekly meeting becomes one recurring event
/// whose first occurrence is a day the section actually meets, inside the term (or the section's own dates).
/// </summary>
public sealed class CalendarEventDraft
{
    public required string Subject { get; init; }
    public required string Location { get; init; }
    public required string Details { get; init; }
    public required DateTimeOffset Start { get; init; }
    public required TimeSpan Duration { get; init; }
    public DateTimeOffset? Until { get; init; }
    public DaySet Days { get; init; }
    public required string Crn { get; init; }
}

public static class CalendarEventDrafts
{
    public static List<CalendarEventDraft> FromSections(
        Term term,
        IEnumerable<CourseSection> sections,
        Func<CourseKey, string?>? titleOf = null)
    {
        var (windowStart, windowEnd) = TermCalendar.Window(term);
        var drafts = new List<CalendarEventDraft>();
        foreach (var section in sections)
        {
            foreach (var meeting in section.WeeklyMeetings)
            {
                if (meeting.Start is not int start || meeting.End is not int end || end <= start || meeting.Days.IsEmpty) continue;
                var rangeStart = ParseDate(meeting.StartDate) ?? windowStart;
                var rangeEnd = ParseDate(meeting.EndDate) ?? windowEnd;
                if (rangeStart < windowStart) rangeStart = windowStart;
                if (rangeEnd > windowEnd) rangeEnd = windowEnd;
                var first = FirstDay(rangeStart, rangeEnd, meeting.Days);
                if (first is null) continue;
                var local = new DateTime(first.Value.Year, first.Value.Month, first.Value.Day, start / 60, start % 60, 0, DateTimeKind.Unspecified);
                var offset = TermCalendar.NewYork.GetUtcOffset(local);
                var untilLocal = new DateTime(rangeEnd.Year, rangeEnd.Month, rangeEnd.Day, 23, 59, 0, DateTimeKind.Unspecified);
                var title = titleOf?.Invoke(section.CourseKey);
                var subject = string.IsNullOrWhiteSpace(title) ? section.CourseKey.ToString() : $"{section.CourseKey} {title}";
                drafts.Add(new CalendarEventDraft
                {
                    Subject = subject,
                    Location = meeting.LocationLabel,
                    Details = $"CRN {section.Crn} · {section.ClassType} {section.SectionNumber}".Trim(),
                    Start = new DateTimeOffset(local, offset),
                    Duration = TimeSpan.FromMinutes(end - start),
                    Until = new DateTimeOffset(untilLocal, TermCalendar.NewYork.GetUtcOffset(untilLocal)),
                    Days = meeting.Days,
                    Crn = section.Crn,
                });
            }
        }
        return drafts;
    }

    private static DateOnly? ParseDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso) || iso.Length < 10) return null;
        return DateOnly.TryParse(iso[..10], out var day) ? day : null;
    }

    private static DateOnly? FirstDay(DateOnly start, DateOnly end, DaySet days)
    {
        for (var day = start; day <= end && day <= start.AddDays(6); day = day.AddDays(1))
        {
            if (days.Contains((int)day.DayOfWeek)) return day;
        }
        return null;
    }
}
