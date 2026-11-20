// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// Writes chosen sections as .ics text. Events are built here, then written by
/// <see cref="ScheduleExporter.IcsString"/> so both export paths share one writer
/// (VTIMEZONE, final exams, UNTIL value types, 75-octet folding).
/// </summary>
public static class IcsWriter
{
    /// <summary>
    /// Writes a schedule plan as .ics text using the plan/index event builder (<see cref="ScheduleExporter.Events"/>).
    /// Both this method and the plan exporter use the resolved catalog course title with Swift's fallback,
    /// and format the calendar name as <c>{plan.Name} – {index.Term.Name}</c>.
    /// </summary>
    public static string Write(SchedulePlan plan, CatalogIndex index, DateTimeOffset? now = null)
    {
        var (events, _) = ScheduleExporter.Events(plan, index);
        var name = $"{plan.Name} \u2013 {index.Term.Name}";
        return ScheduleExporter.IcsString(events, name, now);
    }

    /// <summary>
    /// Writes chosen sections as .ics text.
    /// <para>
    /// UID contract:
    /// When <paramref name="planId"/> is specified, UIDs use the plan GUID:
    /// <c>{planId:D}-{crn}-{index}@neuclasssearch</c>, matching Swift and <see cref="ScheduleExporter"/>.
    /// When <paramref name="planId"/> is omitted (section-only fallback), UIDs fall back to scoping by
    /// <paramref name="term"/>.Code: <c>{term.Code}-{crn}-{index}@neuclasssearch</c>.
    /// Callers exporting a schedule plan should always provide <paramref name="planId"/> (or call
    /// <see cref="Write(SchedulePlan, CatalogIndex, DateTimeOffset?)"/>) so two plans in the same term
    /// cannot produce colliding UIDs.
    /// </para>
    /// <para>
    /// Course title resolution:
    /// If <paramref name="titleOf"/> is provided, it is invoked with each section's course key; if it returns
    /// a non-empty catalog title, that title is used. Otherwise, it falls back to <see cref="CourseSection.Title"/>,
    /// matching Swift's fallback behavior.
    /// </para>
    /// </summary>
    public static string Write(
        string calendarName,
        Term term,
        IEnumerable<CourseSection> sections,
        DateTimeOffset? now = null,
        Guid? planId = null,
        Func<CourseKey, string?>? titleOf = null)
    {
        // CRNs repeat across terms, so UIDs lead with the plan id (or the Banner term code) like the Swift export.
        var scope = planId?.ToString("D") ?? term.Code;
        var events = new List<ScheduleExportEvent>();
        foreach (var section in sections)
        {
            var code = section.CourseKey.ToString();
            var catalogTitle = titleOf?.Invoke(section.CourseKey);
            var title = string.IsNullOrEmpty(catalogTitle) ? section.Title : catalogTitle;
            var notes = new List<string>
            {
                title,
                $"CRN {section.Crn} · {ScheduleFormat.SectionTitle(section)}",
            };
            if (ScheduleFormat.Instructors(section) is { } instructors) notes.Add("Instructor: " + instructors);
            notes.Add("Added from Term Organize");

            var number = 0;
            foreach (var meeting in section.WeeklyMeetings)
            {
                var index = number++;
                if (meeting.Start is not int start || meeting.End is not int end) continue;
                // No known end date: skip rather than emit an unbounded weekly rule.
                if (DayOf(meeting.StartDate) is not { } startDay || DayOf(meeting.EndDate) is not { } endDay) continue;
                var weekdays = meeting.Days.Indices.ToList();
                if (FirstDate(startDay, weekdays) is not { } first || first > endDay) continue;
                events.Add(new ScheduleExportEvent
                {
                    Uid = $"{scope}-{section.Crn}-{index}@neuclasssearch",
                    Title = $"{code} {section.ClassType}",
                    Location = LocationOf(meeting, section),
                    Notes = string.Join('\n', notes),
                    FirstDate = first,
                    StartMinute = start,
                    EndMinute = end,
                    Weekdays = weekdays,
                    LastDate = endDay,
                });
            }

            if (section.FinalExam is Meeting exam && DayOf(exam.StartDate) is { } examDay)
            {
                var timed = exam.Start is not null && exam.End is not null;
                events.Add(new ScheduleExportEvent
                {
                    Uid = $"{scope}-{section.Crn}-final@neuclasssearch",
                    Title = $"{code} Final Exam",
                    Location = LocationOf(exam, section),
                    Notes = string.Join('\n', new[] { "Final exam" }.Concat(notes)),
                    FirstDate = examDay,
                    StartMinute = timed ? exam.Start : null,
                    EndMinute = timed ? exam.End : null,
                    IsFinalExam = true,
                });
            }
        }
        return ScheduleExporter.IcsString(events, calendarName, now);
    }

    private static DateOnly? DayOf(string? iso)
    {
        if (!ScheduleDates.TryComponents(iso, out var year, out var month, out var day)) return null;
        try { return new DateOnly(year, month, day); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static DateOnly? FirstDate(DateOnly start, IReadOnlyList<int> weekdays)
    {
        if (weekdays.Count == 0) return null;
        for (var i = 0; i < 7; i++)
        {
            var day = start.AddDays(i);
            if (weekdays.Contains((int)day.DayOfWeek)) return day;
        }
        return null;
    }

    private static string? LocationOf(Meeting meeting, CourseSection section)
    {
        if (meeting.IsOnline || section.IsOnline) return "Online";
        var label = meeting.LongLocationLabel;
        return label == "Location TBA" ? null : label;
    }
}
