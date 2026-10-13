// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Share-image layout, ICS text, and PNG bytes from ScheduleExporter.swift.
// Adding events to the system calendar is not available on this platform.

using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace TermOrganize.Core;

/// <summary>Registration CRNs, calendar text, and the shared week picture for one schedule.</summary>
public static partial class ScheduleExporter
{
    /// <summary>Selected CRNs in plan-course order, one per line, ready to paste into registration.</summary>
    public static string CrnText(SchedulePlan plan, CatalogIndex? index) =>
        string.Join('\n', new ResolvedPlan(plan, index).CrnsForRegistration);

    /// <summary>The same list when the caller already has the CRNs in registration order.</summary>
    public static string CrnText(IEnumerable<string> crns) => string.Join('\n', crns);
}

/// <summary>Cross-platform description of the "Share as Image" week picture.</summary>
public static class ScheduleImage
{
    public const int Width = 460;
    public const int Padding = 20;
    public const int StackSpacing = 16;
    public const int TitleSpacing = 2;
    public const int HourHeight = 40;
    public const int AxisWidth = 34;
    public const int HeaderBottomPadding = 6;
    public const int LegendSpacing = 10;
    public const int ColorBarWidth = 4;
    public const double FillOpacity = 0.18;
    public const string Background = "#FAFAFC";

    /// <summary>Light-mode SwiftUI system colors, in <see cref="CourseColor"/> palette order.</summary>
    private static readonly string[] StrokeHex =
    [
        "#007AFF", "#FF9500", "#34C759", "#AF52DE", "#FF2D55",
        "#30B0C7", "#5856D6", "#A2845E", "#00C7BE", "#FF3B30",
    ];

    private const string Conflict = "#FF3B30";
    private const string Ink = "#141414";
    private const string Secondary = "#6E6E73";
    private const string Tertiary = "#A0A0A5";
    private const string Separator = "#D2D2D6";

    /// <summary>
    /// The week grid, event blocks, legend, and footer for <paramref name="plan"/>.
    /// Coordinates are logical points in a 460-wide picture. The now indicator is omitted.
    /// </summary>
    public static ScheduleImageLayout Layout(SchedulePlan plan, CatalogIndex? index, string? termName = null)
    {
        var resolved = new ResolvedPlan(plan, index);
        var showsWeekend = resolved.Events.Any(item => item.Day is 0 or 6);
        var (hourStart, hourEnd) = PlanCalendarSupport.HourRange(resolved.Events, null, null, compact: false);
        var days = PlanCalendarSupport.VisibleDays(resolved.Events, showsWeekend);
        var legend = new List<(ResolvedPlan.Entry Entry, CourseSection Section)>();
        foreach (var entry in resolved.Entries)
        {
            if (entry.Selected.Count == 0) continue;
            foreach (var section in entry.Selected) legend.Add((entry, section));
        }

        const int titleHeight = 16;
        const int subtitleHeight = 9;
        const int headerText = 9;
        var contentWidth = Width - Padding * 2;
        var columnWidth = (contentWidth - AxisWidth) / (double)Math.Max(1, days.Count);
        var y = (double)Padding;
        var titleY = y;
        y += titleHeight + TitleSpacing;
        var subtitleY = y;
        y += subtitleHeight + StackSpacing;
        var headerY = y;
        y += headerText + HeaderBottomPadding;
        var gridTop = y;
        var gridHeight = Math.Max(HourHeight, (hourEnd - hourStart) * HourHeight);
        y += gridHeight + StackSpacing;
        var legendTop = y;
        var legendHeights = legend.Select(LegendHeight).ToList();
        var legendHeight = 0;
        for (var i = 0; i < legendHeights.Count; i++)
        {
            legendHeight += legendHeights[i];
            if (i + 1 < legendHeights.Count) legendHeight += LegendSpacing;
        }
        y += legendHeight;
        if (legend.Count > 0) y += StackSpacing;
        var footerY = y;
        y += subtitleHeight + Padding;

        var rects = new List<ScheduleImageRect>
        {
            new() { X = 0, Y = 0, Width = Width, Height = y, Fill = Background, Role = "background" },
        };
        var labels = new List<ScheduleImageLabel>
        {
            new() { X = Padding, Y = titleY, Text = plan.Name, Color = Ink, Role = "title" },
            new()
            {
                X = Padding,
                Y = subtitleY,
                Text = $"{termName ?? index?.Term.Name ?? ""} · {resolved.CreditsLabel}".Trim(' ', '·'),
                Color = Secondary,
                Role = "subtitle",
            },
        };

        var gridLeft = Padding + AxisWidth;
        var gridRight = Width - Padding;
        for (var column = 0; column < days.Count; column++)
        {
            labels.Add(new ScheduleImageLabel
            {
                X = gridLeft + column * columnWidth,
                Y = headerY,
                Text = DaySet.ShortNames[days[column]],
                Color = Secondary,
                Role = "weekday",
            });
        }
        for (var hour = hourStart; hour <= hourEnd; hour++)
        {
            var line = gridTop + (hour - hourStart) * HourHeight;
            rects.Add(new ScheduleImageRect
            {
                X = gridLeft, Y = line, Width = gridRight - gridLeft, Height = 1, Fill = Separator, Role = "grid",
            });
            if (hour < hourEnd)
            {
                labels.Add(new ScheduleImageLabel
                {
                    X = Padding, Y = line - 4, Text = TimeOfDay.HourAxisLabel(hour), Color = Tertiary, Role = "hour",
                });
            }
        }
        for (var column = 0; column <= days.Count; column++)
        {
            var x = gridLeft + column * columnWidth;
            rects.Add(new ScheduleImageRect
            {
                X = x, Y = gridTop, Width = 1, Height = gridHeight, Fill = Separator, Role = "grid",
            });
        }

        var lanes = CalendarLayout.Lanes(resolved.Events);
        foreach (var calendarEvent in resolved.Events)
        {
            var column = days.IndexOf(calendarEvent.Day);
            if (column < 0) continue;
            var lane = lanes.TryGetValue(calendarEvent.Id, out var assigned) ? assigned : (Index: 0, Count: 1);
            var laneWidth = (columnWidth - 3) / Math.Max(1, lane.Count);
            var x = Padding + AxisWidth + column * columnWidth + 1.5 + lane.Index * laneWidth;
            var blockY = gridTop + (calendarEvent.Start - hourStart * 60) / 60.0 * HourHeight + 1;
            var blockH = Math.Max(18, (calendarEvent.End - calendarEvent.Start) / 60.0 * HourHeight - 2);
            var blockW = Math.Max(2, laneWidth - 2);
            var stroke = calendarEvent.IsConflict ? Conflict : Stroke(calendarEvent.Color);
            var fill = Blend(stroke, FillOpacity);
            rects.Add(new ScheduleImageRect
            {
                X = x, Y = blockY, Width = blockW, Height = blockH, Fill = fill, Stroke = stroke, Role = "event",
            });
            rects.Add(new ScheduleImageRect
            {
                X = x, Y = blockY, Width = 3, Height = blockH, Fill = stroke, Role = "eventBar",
            });
            labels.Add(new ScheduleImageLabel
            {
                X = x + 5,
                Y = blockY + 2,
                Text = calendarEvent.Title,
                Color = stroke,
                Role = "event",
            });
        }

        var legendY = legendTop;
        for (var i = 0; i < legend.Count; i++)
        {
            var (entry, section) = legend[i];
            var rowH = legendHeights[i];
            var stroke = Stroke(entry.Color);
            rects.Add(new ScheduleImageRect
            {
                X = Padding, Y = legendY, Width = ColorBarWidth, Height = rowH, Fill = stroke, Role = "legendBar",
            });
            var textX = Padding + ColorBarWidth + LegendSpacing;
            labels.Add(new ScheduleImageLabel { X = textX, Y = legendY, Text = section.CourseKey.ToString(), Color = Ink, Role = "legendCode" });
            labels.Add(new ScheduleImageLabel { X = textX, Y = legendY, Text = entry.Title, Color = Ink, Role = "legendTitle" });
            labels.Add(new ScheduleImageLabel
            {
                X = textX,
                Y = legendY + 10,
                Text = $"{ScheduleFormat.SectionTitle(section)} · CRN {section.Crn}",
                Color = Secondary,
                Role = "legendSection",
            });
            var place = ScheduleFormat.Location(section);
            labels.Add(new ScheduleImageLabel
            {
                X = textX,
                Y = legendY + 20,
                Text = section.ScheduleSummary + (place is null ? "" : " · " + place),
                Color = Secondary,
                Role = "legendWhen",
            });
            var instructors = ScheduleFormat.Instructors(section);
            if (instructors is not null)
            {
                labels.Add(new ScheduleImageLabel
                {
                    X = textX, Y = legendY + 30, Text = instructors, Color = Secondary, Role = "legendInstructor",
                });
            }
            legendY += rowH + LegendSpacing;
        }

        labels.Add(new ScheduleImageLabel
        {
            X = Padding, Y = footerY, Text = "Made with Term Organize", Color = Tertiary, Role = "footer",
        });

        return new ScheduleImageLayout
        {
            Width = Width,
            Height = y,
            Background = Background,
            HourStart = hourStart,
            HourEnd = hourEnd,
            Days = days,
            Rects = rects,
            Labels = labels,
        };
    }

    private static int LegendHeight((ResolvedPlan.Entry Entry, CourseSection Section) item) =>
        ScheduleFormat.Instructors(item.Section) is null ? 32 : 42;

    private static string Stroke(CourseColor color)
    {
        var index = ((color.Index % StrokeHex.Length) + StrokeHex.Length) % StrokeHex.Length;
        return StrokeHex[index];
    }

    private static string Blend(string hex, double opacity)
    {
        var (fr, fg, fb) = Parse(hex);
        var (br, bg, bb) = Parse(Background);
        byte Mix(byte back, byte front) => (byte)Math.Clamp((int)Math.Round(back * (1 - opacity) + front * opacity), 0, 255);
        return Format(Mix(br, fr), Mix(bg, fg), Mix(bb, fb));
    }

    private static (byte R, byte G, byte B) Parse(string hex)
    {
        var text = hex.TrimStart('#');
        return (
            Convert.ToByte(text[..2], 16),
            Convert.ToByte(text[2..4], 16),
            Convert.ToByte(text[4..6], 16));
    }

    private static string Format(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
}

/// <summary>One rendered week picture: size, rectangles, and labels.</summary>
public sealed class ScheduleImageLayout
{
    public double Width { get; init; }
    public double Height { get; init; }
    public string Background { get; init; } = ScheduleImage.Background;
    public int HourStart { get; init; }
    public int HourEnd { get; init; }
    public List<int> Days { get; init; } = [];
    public List<ScheduleImageRect> Rects { get; init; } = [];
    public List<ScheduleImageLabel> Labels { get; init; } = [];
}

/// <summary>A filled rectangle in the week picture. Coordinates are logical points.</summary>
public sealed class ScheduleImageRect
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string Fill { get; init; } = ScheduleImage.Background;
    public string? Stroke { get; init; }
    public string Role { get; init; } = "";
}

/// <summary>A text run in the week picture.</summary>
public sealed class ScheduleImageLabel
{
    public double X { get; init; }
    public double Y { get; init; }
    public string Text { get; init; } = "";
    public string Color { get; init; } = "#141414";
    public string Role { get; init; } = "";
}

/// <summary>A class meeting or final exam ready to be written to a calendar.</summary>
public sealed class ScheduleExportEvent : IEquatable<ScheduleExportEvent>
{
    public string Uid { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Location { get; set; }
    public string Notes { get; set; } = "";
    /// <summary>The first day the event happens, in America/New_York.</summary>
    public DateOnly FirstDate { get; set; }
    /// <summary>Minutes after midnight. Null together with <see cref="EndMinute"/> makes an all-day event.</summary>
    public int? StartMinute { get; set; }
    public int? EndMinute { get; set; }
    /// <summary>Weekdays (0 = Sunday) the event repeats on. Empty for a single event.</summary>
    public List<int> Weekdays { get; set; } = [];
    /// <summary>The last day of the repetition, inclusive.</summary>
    public DateOnly? LastDate { get; set; }
    public bool IsFinalExam { get; set; }
    public bool IsRecurring => Weekdays.Count > 0 && LastDate is not null;

    public bool Equals(ScheduleExportEvent? other) =>
        other is not null && Uid == other.Uid && Title == other.Title && Location == other.Location && Notes == other.Notes
        && FirstDate == other.FirstDate && StartMinute == other.StartMinute && EndMinute == other.EndMinute
        && Weekdays.SequenceEqual(other.Weekdays) && LastDate == other.LastDate && IsFinalExam == other.IsFinalExam;

    public override bool Equals(object? obj) => Equals(obj as ScheduleExportEvent);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Uid);
}

/// <summary>What an export contains, for display before the user commits.</summary>
public sealed class ScheduleExportSummary
{
    public int ClassCount { get; set; }
    public int WeeklyMeetings { get; set; }
    public int FinalExams { get; set; }
    public int SkippedMeetings { get; set; }
    /// <summary>Midnight in New York of the last day anything repeats or happens.</summary>
    public DateTimeOffset? LastClassDay { get; set; }
}

/// <summary>There are no scheduled classes to export yet.</summary>
public sealed class ScheduleExportException : Exception
{
    public ScheduleExportException(string message) : base(message) { }
    public static ScheduleExportException NothingToExport { get; } = new("There are no scheduled classes to export yet.");
}

/// <summary>Outcome of adding a schedule to the system calendar.</summary>
public abstract record ScheduleCalendarOutcome
{
    public sealed record Added(int Count) : ScheduleCalendarOutcome;
    public sealed record NothingToAdd : ScheduleCalendarOutcome;
    public sealed record Denied : ScheduleCalendarOutcome;
    public sealed record Failed(string Message) : ScheduleCalendarOutcome;
}

public static partial class ScheduleExporter
{
    public const int LogicalWidth = 460;
    public const int Padding = 20;
    public const int StackSpacing = 16;
    public const int TitleSpacing = 2;
    public const int HourHeight = 40;
    public const int AxisWidth = 34;
    public const int HeaderBottomPadding = 6;
    public const int LegendSpacing = 10;
    public const int ColorBarWidth = 4;
    public const int Scale = 3;

    private static readonly string[] WeekdayCodes = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    /// <summary>One event per weekly meeting of every chosen section, plus final exams.</summary>
    public static (List<ScheduleExportEvent> Events, ScheduleExportSummary Summary) Events(SchedulePlan plan, CatalogIndex index)
    {
        var resolved = new ResolvedPlan(plan, index);
        var events = new List<ScheduleExportEvent>();
        var summary = new ScheduleExportSummary();
        DateOnly? lastDay = null;
        var planId = plan.Id.ToString("D");
        foreach (var entry in resolved.Entries)
        {
            foreach (var section in entry.Selected)
            {
                summary.ClassCount++;
                var code = section.CourseKey.ToString();
                var instructors = ScheduleFormat.Instructors(section);
                var notes = new List<string>
                {
                    string.IsNullOrEmpty(entry.Title) ? section.Title : entry.Title,
                    $"CRN {section.Crn} \u00b7 {ScheduleFormat.SectionTitle(section)}",
                };
                if (instructors is not null) notes.Add("Instructor: " + instructors);
                notes.Add("Added from Term Organize");
                var noteText = string.Join('\n', notes);
                var weekly = section.WeeklyMeetings.ToList();
                for (var number = 0; number < weekly.Count; number++)
                {
                    var meeting = weekly[number];
                    if (meeting.Start is not int start || meeting.End is not int end
                        || !ScheduleDates.TryComponents(meeting.StartDate, out var sy, out var sm, out var sd)
                        || !ScheduleDates.TryComponents(meeting.EndDate, out var ey, out var em, out var ed)
                        || !TryDate(sy, sm, sd, out var startDay) || !TryDate(ey, em, ed, out var endDay))
                    {
                        summary.SkippedMeetings++;
                        continue;
                    }
                    var weekdays = meeting.Days.Indices.ToList();
                    var first = FirstDay(startDay, weekdays);
                    if (first is null || first.Value > endDay)
                    {
                        summary.SkippedMeetings++;
                        continue;
                    }
                    summary.WeeklyMeetings++;
                    if (lastDay is null || endDay > lastDay) lastDay = endDay;
                    events.Add(new ScheduleExportEvent
                    {
                        Uid = $"{planId}-{section.Crn}-{number}@neuclasssearch",
                        Title = $"{code} {section.ClassType}",
                        Location = LocationOf(meeting, section),
                        Notes = noteText,
                        FirstDate = first.Value,
                        StartMinute = start,
                        EndMinute = end,
                        Weekdays = weekdays,
                        LastDate = endDay,
                    });
                }
                if (section.FinalExam is Meeting exam && ScheduleDates.TryComponents(exam.StartDate, out var fy, out var fm, out var fd) && TryDate(fy, fm, fd, out var examDay))
                {
                    summary.FinalExams++;
                    var examNotes = new List<string> { "Final exam" };
                    examNotes.AddRange(notes);
                    var timed = exam.Start is not null && exam.End is not null;
                    events.Add(new ScheduleExportEvent
                    {
                        Uid = $"{planId}-{section.Crn}-final@neuclasssearch",
                        Title = $"{code} Final Exam",
                        Location = LocationOf(exam, section),
                        Notes = string.Join('\n', examNotes),
                        FirstDate = examDay,
                        StartMinute = timed ? exam.Start : null,
                        EndMinute = timed ? exam.End : null,
                        IsFinalExam = true,
                    });
                    if (lastDay is null || examDay > lastDay) lastDay = examDay;
                }
            }
        }
        summary.LastClassDay = lastDay is DateOnly day ? Midnight(day) : null;
        return (events, summary);
    }

    /// <summary>An RFC 5545 calendar with a VTIMEZONE for America/New_York.</summary>
    public static string IcsString(IReadOnlyList<ScheduleExportEvent> events, string calendarName, DateTimeOffset? now = null)
    {
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Term Organize//Schedule//EN", "CALSCALE:GREGORIAN", "METHOD:PUBLISH",
            "X-WR-CALNAME:" + Escape(calendarName), "X-WR-TIMEZONE:America/New_York",
            "BEGIN:VTIMEZONE", "TZID:America/New_York", "X-LIC-LOCATION:America/New_York",
            "BEGIN:DAYLIGHT", "TZOFFSETFROM:-0500", "TZOFFSETTO:-0400", "TZNAME:EDT", "DTSTART:19700308T020000",
            "RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU", "END:DAYLIGHT",
            "BEGIN:STANDARD", "TZOFFSETFROM:-0400", "TZOFFSETTO:-0500", "TZNAME:EST", "DTSTART:19701101T020000",
            "RRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU", "END:STANDARD", "END:VTIMEZONE",
        };
        var stamp = UtcStamp((now ?? DateTimeOffset.UtcNow).UtcDateTime);
        foreach (var calendarEvent in events)
        {
            lines.Add("BEGIN:VEVENT");
            lines.Add("UID:" + calendarEvent.Uid);
            lines.Add("DTSTAMP:" + stamp);
            if (calendarEvent.StartMinute is int start && calendarEvent.EndMinute is int end)
            {
                lines.Add("DTSTART;TZID=America/New_York:" + LocalStamp(calendarEvent.FirstDate, start));
                lines.Add("DTEND;TZID=America/New_York:" + LocalStamp(calendarEvent.FirstDate, end));
            }
            else
            {
                lines.Add("DTSTART;VALUE=DATE:" + DateStamp(calendarEvent.FirstDate));
                lines.Add("DTEND;VALUE=DATE:" + DateStamp(calendarEvent.FirstDate.AddDays(1)));
            }
            if (calendarEvent.IsRecurring && calendarEvent.LastDate is DateOnly last)
            {
                var days = string.Join(',', calendarEvent.Weekdays.Select(day => WeekdayCodes[day]));
                lines.Add($"RRULE:FREQ=WEEKLY;BYDAY={days};UNTIL={UntilStamp(last)}");
            }
            lines.Add("SUMMARY:" + Escape(calendarEvent.Title));
            if (calendarEvent.Location is not null) lines.Add("LOCATION:" + Escape(calendarEvent.Location));
            lines.Add("DESCRIPTION:" + Escape(calendarEvent.Notes));
            lines.Add("TRANSP:OPAQUE");
            lines.Add("END:VEVENT");
        }
        lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines.Select(Fold)) + "\r\n";
    }

    /// <summary>Writes the plan's calendar to a temporary .ics file.</summary>
    public static string WriteIcs(SchedulePlan plan, CatalogIndex index)
    {
        var (events, _) = Events(plan, index);
        if (events.Count == 0) throw ScheduleExportException.NothingToExport;
        var name = $"{plan.Name} \u2013 {index.Term.Name}";
        var text = IcsString(events, name);
        var path = Path.Combine(Path.GetTempPath(), FileName(name, "ics"));
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public static string FileName(string name, string extension)
    {
        var cleaned = new StringBuilder(name.Length);
        foreach (var character in name)
            cleaned.Append("/\\:*?\"<>|".Contains(character) ? '-' : character);
        var text = cleaned.ToString().Trim();
        return (text.Length == 0 ? "Schedule" : text) + "." + extension;
    }

    /// <summary>EventKit is not available. An empty list adds nothing; otherwise the call fails closed.</summary>
    public static ScheduleCalendarOutcome AddToCalendar(IReadOnlyList<ScheduleExportEvent> events) =>
        events.Count == 0
            ? new ScheduleCalendarOutcome.NothingToAdd()
            : new ScheduleCalendarOutcome.Failed("Adding events to the system calendar isn\u2019t available on this platform.");

    /// <summary>The week picture at 3x as PNG bytes. Layout constants match the Swift share image.</summary>
    public static byte[] PngBytes(SchedulePlan plan, CatalogIndex index)
    {
        var resolved = new ResolvedPlan(plan, index);
        var showsWeekend = resolved.Events.Any(item => item.Day is 0 or 6);
        var (hourStart, hourEnd) = PlanCalendarSupport.HourRange(resolved.Events, null, null, compact: false);
        var days = PlanCalendarSupport.VisibleDays(resolved.Events, showsWeekend);
        var legend = new List<(ResolvedPlan.Entry Entry, CourseSection Section)>();
        foreach (var entry in resolved.Entries)
            foreach (var section in entry.Selected)
                legend.Add((entry, section));
        const int titleHeight = 16;
        const int lineHeight = 9;
        var columnWidth = (LogicalWidth - Padding * 2 - AxisWidth) / (double)Math.Max(1, days.Count);
        var y = Padding;
        var titleY = y;
        y += titleHeight + TitleSpacing;
        var subtitleY = y;
        y += lineHeight + StackSpacing;
        var headerY = y;
        y += lineHeight + HeaderBottomPadding;
        var gridTop = y;
        var gridHeight = Math.Max(HourHeight, (hourEnd - hourStart) * HourHeight);
        y += gridHeight + StackSpacing;
        var legendTop = y;
        var legendHeights = legend.Select(item => ScheduleFormat.Instructors(item.Section) is null ? 32 : 42).ToList();
        for (var i = 0; i < legendHeights.Count; i++)
        {
            y += legendHeights[i];
            if (i + 1 < legendHeights.Count) y += LegendSpacing;
        }
        if (legend.Count > 0) y += StackSpacing;
        var footerY = y;
        y += lineHeight + Padding;
        var width = LogicalWidth * Scale;
        var height = y * Scale;
        var pixels = new byte[width * height * 3];
        Fill(pixels, width, height, 0, 0, LogicalWidth, y, 250, 250, 252);
        DrawText(pixels, width, height, plan.Name, Padding, titleY, 2, 20, 20, 20, true);
        DrawText(pixels, width, height, $"{index.Term.Name} \u00b7 {resolved.CreditsLabel}", Padding, subtitleY, 1, 110, 110, 115, false);
        for (var column = 0; column < days.Count; column++)
            DrawText(pixels, width, height, DaySet.ShortNames[days[column]], Padding + AxisWidth + (int)(column * columnWidth), headerY, 1, 120, 120, 125, false);
        var gridLeft = Padding + AxisWidth;
        var gridRight = LogicalWidth - Padding;
        for (var hour = hourStart; hour <= hourEnd; hour++)
        {
            var line = gridTop + (hour - hourStart) * HourHeight;
            HLine(pixels, width, height, gridLeft, gridRight, line, 210, 210, 214);
            if (hour < hourEnd) DrawText(pixels, width, height, TimeOfDay.HourAxisLabel(hour), Padding, line - 4, 1, 150, 150, 155, false);
        }
        for (var column = 0; column <= days.Count; column++)
            VLine(pixels, width, height, gridLeft + (int)Math.Round(column * columnWidth), gridTop, gridTop + gridHeight, 210, 210, 214);
        var lanes = CalendarLayout.Lanes(resolved.Events);
        foreach (var calendarEvent in resolved.Events)
        {
            var column = days.IndexOf(calendarEvent.Day);
            if (column < 0) continue;
            var lane = lanes.TryGetValue(calendarEvent.Id, out var assigned) ? assigned : (Index: 0, Count: 1);
            var laneWidth = (columnWidth - 3) / Math.Max(1, lane.Count);
            var x = Padding + AxisWidth + column * columnWidth + 1.5 + lane.Index * laneWidth;
            var blockY = gridTop + (calendarEvent.Start - hourStart * 60) / 60.0 * HourHeight + 1;
            var blockH = Math.Max(18, (calendarEvent.End - calendarEvent.Start) / 60.0 * HourHeight - 2);
            var (sr, sg, sb) = StrokeBytes(calendarEvent.Color, calendarEvent.IsConflict);
            var fill = Blend(sr, sg, sb);
            Fill(pixels, width, height, (int)x, (int)blockY, Math.Max(2, (int)laneWidth - 2), (int)blockH, fill.R, fill.G, fill.B);
            Fill(pixels, width, height, (int)x, (int)blockY, 3, (int)blockH, sr, sg, sb);
            DrawText(pixels, width, height, calendarEvent.Title, (int)x + 5, (int)blockY + 2, 1, sr, sg, sb, true);
        }
        var legendY = legendTop;
        for (var i = 0; i < legend.Count; i++)
        {
            var (entry, section) = legend[i];
            var (barR, barG, barB) = StrokeBytes(entry.Color, conflict: false);
            Fill(pixels, width, height, Padding, legendY, ColorBarWidth, legendHeights[i], barR, barG, barB);
            var textX = Padding + ColorBarWidth + LegendSpacing;
            DrawText(pixels, width, height, section.CourseKey.ToString(), textX, legendY, 1, 20, 20, 20, true);
            DrawText(pixels, width, height, entry.Title, textX + 70, legendY, 1, 40, 40, 45, false);
            DrawText(pixels, width, height, $"{ScheduleFormat.SectionTitle(section)} \u00b7 CRN {section.Crn}", textX, legendY + 10, 1, 110, 110, 115, false);
            var place = ScheduleFormat.Location(section);
            DrawText(pixels, width, height, section.ScheduleSummary + (place is null ? "" : " \u00b7 " + place), textX, legendY + 20, 1, 110, 110, 115, false);
            var instructors = ScheduleFormat.Instructors(section);
            if (instructors is not null) DrawText(pixels, width, height, instructors, textX, legendY + 30, 1, 110, 110, 115, false);
            legendY += legendHeights[i] + LegendSpacing;
        }
        const string footer = "Made with Term Organize";
        DrawText(pixels, width, height, footer, Math.Max(Padding, (LogicalWidth - footer.Length * 6) / 2), footerY, 1, 160, 160, 165, false);
        return EncodePng(pixels, width, height);
    }

    private static string? LocationOf(Meeting meeting, CourseSection section)
    {
        if (meeting.IsOnline || section.IsOnline) return "Online";
        var label = meeting.LongLocationLabel;
        return label == "Location TBA" ? null : label;
    }

    private static DateOnly? FirstDay(DateOnly start, IReadOnlyList<int> weekdays)
    {
        if (weekdays.Count == 0) return null;
        var day = start;
        for (var i = 0; i < 7; i++)
        {
            if (weekdays.Contains((int)day.DayOfWeek)) return day;
            day = day.AddDays(1);
        }
        return null;
    }

    private static bool TryDate(int year, int month, int day, out DateOnly date)
    {
        try { date = new DateOnly(year, month, day); return true; }
        catch (ArgumentOutOfRangeException) { date = default; return false; }
    }

    private static DateTimeOffset Midnight(DateOnly day)
    {
        var local = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TermCalendar.NewYork.GetUtcOffset(local));
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal).Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) return line;
        var result = new StringBuilder();
        var current = new StringBuilder();
        var bytes = 0;
        var limit = 75;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (bytes + size > limit)
            {
                result.Append(current).Append("\r\n ");
                current.Clear();
                bytes = 0;
                limit = 74;
            }
            current.Append(rune.ToString());
            bytes += size;
        }
        result.Append(current);
        return result.ToString();
    }

    private static string DateStamp(DateOnly day) => day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    private static string LocalStamp(DateOnly day, int minute) => $"{DateStamp(day)}T{minute / 60:00}{minute % 60:00}00";
    private static string UtcStamp(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string UntilStamp(DateOnly lastDay)
    {
        var local = new DateTime(lastDay.Year, lastDay.Month, lastDay.Day, 23, 59, 59, DateTimeKind.Unspecified);
        try { return UtcStamp(TimeZoneInfo.ConvertTimeToUtc(local, TermCalendar.NewYork)); }
        catch (ArgumentException) { return UtcStamp(DateTime.UtcNow); }
    }

    private readonly record struct Rgb(byte R, byte G, byte B);

    private static readonly byte[][] PaletteRgb =
    [
        [0x00, 0x7A, 0xFF], [0xFF, 0x95, 0x00], [0x34, 0xC7, 0x59], [0xAF, 0x52, 0xDE], [0xFF, 0x2D, 0x55],
        [0x30, 0xB0, 0xC7], [0x58, 0x56, 0xD6], [0xA2, 0x84, 0x5E], [0x00, 0xC7, 0xBE], [0xFF, 0x3B, 0x30],
    ];

    private static (byte R, byte G, byte B) StrokeBytes(CourseColor color, bool conflict)
    {
        if (conflict) return (0xFF, 0x3B, 0x30);
        var index = ((color.Index % PaletteRgb.Length) + PaletteRgb.Length) % PaletteRgb.Length;
        var row = PaletteRgb[index];
        return (row[0], row[1], row[2]);
    }

    private static Rgb Blend(byte r, byte g, byte b)
    {
        const double opacity = 0.18;
        byte Mix(byte back, byte front) => (byte)Math.Clamp((int)Math.Round(back * (1 - opacity) + front * opacity), 0, 255);
        return new Rgb(Mix(250, r), Mix(250, g), Mix(252, b));
    }

    private static void Fill(byte[] pixels, int width, int height, int x, int y, int w, int h, byte r, byte g, byte b)
    {
        var logicalHeight = height / Scale;
        var x0 = Math.Clamp(x, 0, LogicalWidth);
        var y0 = Math.Clamp(y, 0, logicalHeight);
        var x1 = Math.Clamp(x + Math.Max(0, w), 0, LogicalWidth);
        var y1 = Math.Clamp(y + Math.Max(0, h), 0, logicalHeight);
        for (var ly = y0; ly < y1; ly++)
            for (var lx = x0; lx < x1; lx++)
                Plot(pixels, width, height, lx, ly, r, g, b);
    }

    private static void HLine(byte[] pixels, int width, int height, int x0, int x1, int y, byte r, byte g, byte b)
    {
        if ((uint)y >= (uint)(height / Scale)) return;
        for (var x = Math.Max(0, x0); x < Math.Min(LogicalWidth, x1); x++) Plot(pixels, width, height, x, y, r, g, b);
    }

    private static void VLine(byte[] pixels, int width, int height, int x, int y0, int y1, byte r, byte g, byte b)
    {
        if ((uint)x >= (uint)LogicalWidth) return;
        var limit = height / Scale;
        for (var y = Math.Max(0, y0); y < Math.Min(limit, y1); y++) Plot(pixels, width, height, x, y, r, g, b);
    }

    private static void Plot(byte[] pixels, int width, int height, int logicalX, int logicalY, byte r, byte g, byte b)
    {
        var x = logicalX * Scale;
        var y = logicalY * Scale;
        for (var dy = 0; dy < Scale; dy++)
        {
            var row = y + dy;
            if ((uint)row >= (uint)height) continue;
            for (var dx = 0; dx < Scale; dx++)
            {
                var column = x + dx;
                if ((uint)column >= (uint)width) continue;
                var index = (row * width + column) * 3;
                pixels[index] = r;
                pixels[index + 1] = g;
                pixels[index + 2] = b;
            }
        }
    }

    private static void DrawText(byte[] pixels, int width, int height, string text, int x, int y, int glyphScale, byte r, byte g, byte b, bool bold)
    {
        var cursor = x;
        foreach (var character in text)
        {
            if (character == ' ') { cursor += 4 * glyphScale; continue; }
            var rows = Glyph(character);
            for (var row = 0; row < 7; row++)
            {
                var bits = rows[row];
                for (var col = 0; col < 5; col++)
                {
                    if ((bits & (1 << (4 - col))) == 0) continue;
                    Fill(pixels, width, height, cursor + col * glyphScale, y + row * glyphScale, glyphScale, glyphScale, r, g, b);
                    if (bold) Fill(pixels, width, height, cursor + col * glyphScale + 1, y + row * glyphScale, 1, glyphScale, r, g, b);
                }
            }
            cursor += (bold ? 7 : 6) * glyphScale;
        }
    }

    private static byte[] Glyph(char character) => Glyphs.TryGetValue(char.ToUpperInvariant(character), out var rows) ? rows : Glyphs['?'];

    private static readonly Dictionary<char, byte[]> Glyphs = BuildGlyphs();

    private static Dictionary<char, byte[]> BuildGlyphs()
    {
        var map = new Dictionary<char, byte[]>();
        void Add(char name, string rows)
        {
            var bytes = new byte[7];
            var lines = rows.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < Math.Min(7, lines.Length); i++)
            {
                byte value = 0;
                for (var bit = 0; bit < Math.Min(5, lines[i].Length); bit++)
                    if (lines[i][bit] == '#') value |= (byte)(1 << (4 - bit));
                bytes[i] = value;
            }
            map[name] = bytes;
        }
        Add('?', "..#.. .#.#. ..#.. ..#.. ..#.. ..... ..#..");
        Add('0', ".###. #...# #..## #.#.# ##..# #...# .###.");
        Add('1', "..#.. .##.. ..#.. ..#.. ..#.. ..#.. .###.");
        Add('2', ".###. #...# ....# ..##. .#... #.... #####");
        Add('3', "##### ....# ...#. ..##. ....# #...# .###.");
        Add('4', "...#. ..##. .#.#. #..#. ##### ..#.. ..#..");
        Add('5', "##### #.... ####. ....# ....# #...# .###.");
        Add('6', ".###. #.... #.... ####. #...# #...# .###.");
        Add('7', "##### ....# ...#. ..#.. .#... .#... .#...");
        Add('8', ".###. #...# #...# .###. #...# #...# .###.");
        Add('9', ".###. #...# #...# .#### ....# ....# .###.");
        Add('A', ".###. #...# #...# ##### #...# #...# #...#");
        Add('B', "####. #...# #...# ####. #...# #...# ####.");
        Add('C', ".###. #...# #.... #.... #.... #...# .###.");
        Add('D', "####. #...# #...# #...# #...# #...# ####.");
        Add('E', "##### #.... #.... ####. #.... #.... #####");
        Add('F', "##### #.... #.... ####. #.... #.... #....");
        Add('G', ".###. #...# #.... #.### #...# #...# .###.");
        Add('H', "#...# #...# #...# ##### #...# #...# #...#");
        Add('I', ".###. ..#.. ..#.. ..#.. ..#.. ..#.. .###.");
        Add('J', "..### ...#. ...#. ...#. #..#. #..#. .##..");
        Add('K', "#...# #..#. #.#.. ##... #.#.. #..#. #...#");
        Add('L', "#.... #.... #.... #.... #.... #.... #####");
        Add('M', "#...# ##.## #.#.# #.#.# #...# #...# #...#");
        Add('N', "#...# ##..# #.#.# #.#.# #.#.# #..## #...#");
        Add('O', ".###. #...# #...# #...# #...# #...# .###.");
        Add('P', "####. #...# #...# ####. #.... #.... #....");
        Add('Q', ".###. #...# #...# #...# #.#.# #..#. .##.#");
        Add('R', "####. #...# #...# ####. #.#.. #..#. #...#");
        Add('S', ".#### #.... #.... .###. ....# ....# ####.");
        Add('T', "##### ..#.. ..#.. ..#.. ..#.. ..#.. ..#..");
        Add('U', "#...# #...# #...# #...# #...# #...# .###.");
        Add('V', "#...# #...# #...# #...# #...# .#.#. ..#..");
        Add('W', "#...# #...# #...# #.#.# #.#.# ##.## #...#");
        Add('X', "#...# #...# .#.#. ..#.. .#.#. #...# #...#");
        Add('Y', "#...# #...# .#.#. ..#.. ..#.. ..#.. ..#..");
        Add('Z', "##### ....# ...#. ..#.. .#... #.... #####");
        Add('-', "..... ..... ..... .###. ..... ..... .....");
        Add('.', "..... ..... ..... ..... ..... ..#.. ..#..");
        Add(',', "..... ..... ..... ..... ..#.. ..#.. .#...");
        Add(':', "..... ..#.. ..#.. ..... ..#.. ..#.. .....");
        Add('/', "....# ....# ...#. ..#.. .#... #.... #....");
        Add('(', "..#.. .#... #.... #.... #.... .#... ..#..");
        Add(')', "..#.. ...#. ....# ....# ....# ...#. ..#..");
        Add('\u00B7', "..... ..... ..#.. ..#.. ..#.. ..... .....");
        Add('\u2013', "..... ..... ..... .###. ..... ..... .....");
        return map;
    }

    private static byte[] EncodePng(byte[] rgb, int width, int height)
    {
        var stride = width * 3;
        var raw = new byte[height * (stride + 1)];
        for (var row = 0; row < height; row++)
        {
            raw[row * (stride + 1)] = 0;
            Buffer.BlockCopy(rgb, row * stride, raw, row * (stride + 1) + 1, stride);
        }
        using var deflated = new MemoryStream();
        using (var compressor = new DeflateStream(deflated, CompressionLevel.Fastest, leaveOpen: true))
            compressor.Write(raw);
        var payload = deflated.ToArray();
        var zlib = new byte[payload.Length + 6];
        zlib[0] = 0x78;
        zlib[1] = 0x9C;
        Buffer.BlockCopy(payload, 0, zlib, 2, payload.Length);
        var adler = Adler32(raw);
        var end = zlib.Length;
        zlib[end - 4] = (byte)(adler >> 24);
        zlib[end - 3] = (byte)(adler >> 16);
        zlib[end - 2] = (byte)(adler >> 8);
        zlib[end - 1] = (byte)adler;
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteInt(ihdr, 0, width);
        WriteInt(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        Chunk(png, "IHDR"u8, ihdr);
        Chunk(png, "IDAT"u8, zlib);
        Chunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void Chunk(Stream stream, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteInt(length, 0, data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);
        var crcSource = new byte[type.Length + data.Length];
        type.CopyTo(crcSource);
        data.CopyTo(crcSource.AsSpan(type.Length));
        Span<byte> crc = stackalloc byte[4];
        WriteInt(crc, 0, unchecked((int)Crc32(crcSource)));
        stream.Write(crc);
    }

    private static void WriteInt(Span<byte> target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
