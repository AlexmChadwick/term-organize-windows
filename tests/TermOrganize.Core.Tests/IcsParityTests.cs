// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;

namespace TermOrganize.Core.Tests;

public class IcsParityTests
{
    private static readonly Term Fall = new("202710", "Fall 2026 Semester");

    private static CourseSection Section(params Meeting[] meetings) => new()
    {
        Crn = "12345",
        Subject = "CS",
        Number = "2500",
        Title = "Fundamentals",
        ClassType = "Lecture",
        Meetings = [.. meetings],
    };

    private static Meeting Weekly(string? startDate, string? endDate) => new()
    {
        Days = DaySet.Monday | DaySet.Wednesday,
        Start = 10 * 60,
        End = 11 * 60,
        BuildingCode = "WVH",
        Room = "210",
        StartDate = startDate,
        EndDate = endDate,
    };

    private static string[] Lines(string ics) => ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void WritesVtimezoneAndMeetingDatesWithFirstMatchingWeekday()
    {
        // 2026-09-08 is a Tuesday, so the first Monday/Wednesday is Wednesday the 9th.
        var ics = IcsWriter.Write("Fall", Fall, [Section(Weekly("2026-09-08", "2026-12-04"))]);
        Assert.Contains("BEGIN:VTIMEZONE\r\nTZID:America/New_York", ics);
        Assert.Contains("END:VTIMEZONE", ics);
        Assert.Contains("DTSTART;TZID=America/New_York:20260909T100000", ics);
        Assert.Contains("UNTIL=20261205T045959Z", ics);
    }

    [Fact]
    public void TimedFinalExamIsASeparateZonedEvent()
    {
        var exam = new Meeting { Kind = "Final Exam", StartDate = "2026-12-10", Start = 8 * 60, End = 10 * 60 };
        var ics = IcsWriter.Write("Fall", Fall, [Section(Weekly("2026-09-02", "2026-12-04"), exam)]);
        Assert.Equal(2, Lines(ics).Count(l => l == "BEGIN:VEVENT"));
        Assert.Contains("SUMMARY:CS 2500 Final Exam", ics);
        Assert.Contains("DTSTART;TZID=America/New_York:20261210T080000", ics);
        Assert.Contains("DTEND;TZID=America/New_York:20261210T100000", ics);
        Assert.Single(Lines(ics), l => l.StartsWith("RRULE:FREQ=WEEKLY", StringComparison.Ordinal));
    }

    [Fact]
    public void UntimedFinalExamIsAllDayWithNextDayEnd()
    {
        var exam = new Meeting { Kind = "Final Exam", StartDate = "2026-12-31" };
        var ics = IcsWriter.Write("Fall", Fall, [Section(exam)]);
        Assert.Contains("DTSTART;VALUE=DATE:20261231", ics);
        Assert.Contains("DTEND;VALUE=DATE:20270101", ics);
        Assert.DoesNotContain("RRULE:FREQ=WEEKLY", ics);
    }

    [Fact]
    public void FoldsByOctetsNotCharacters()
    {
        var section = Section(Weekly("2026-09-02", "2026-12-04"));
        section.Title = string.Concat(Enumerable.Repeat("café 中", 40));
        var ics = IcsWriter.Write("Fall", Fall, [section]);
        Assert.Contains("\r\n ", ics);
        foreach (var line in Lines(ics))
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, line);
        var unfolded = ics.Replace("\r\n ", "", StringComparison.Ordinal);
        Assert.Contains(section.Title, unfolded.Replace("\\,", ",", StringComparison.Ordinal));
    }
}
