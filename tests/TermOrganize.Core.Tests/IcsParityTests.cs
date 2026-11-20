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

    private static List<List<string>> EventBlocks(string ics)
    {
        var blocks = new List<List<string>>();
        List<string>? current = null;
        foreach (var line in Lines(ics))
        {
            if (line == "BEGIN:VEVENT") current = [line];
            else if (line == "END:VEVENT" && current is not null)
            {
                current.Add(line);
                blocks.Add(current);
                current = null;
            }
            else current?.Add(line);
        }
        return blocks;
    }

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

    [Fact]
    public void EveryEventHasUtcDtstamp()
    {
        var exam = new Meeting { Kind = "Final Exam", StartDate = "2026-12-10" };
        var now = new DateTimeOffset(2026, 11, 19, 15, 30, 0, TimeSpan.FromHours(-5));
        var ics = IcsWriter.Write("Fall", Fall, [Section(Weekly("2026-09-02", "2026-12-04"), exam)], now);
        var blocks = EventBlocks(ics);
        Assert.Equal(2, blocks.Count);
        foreach (var block in blocks)
        {
            var stamps = block.Where(l => l.StartsWith("DTSTAMP:", StringComparison.Ordinal)).ToList();
            Assert.Single(stamps);
            Assert.Equal("DTSTAMP:20261119T203000Z", stamps[0]);
        }
    }

    [Fact]
    public void UidsAreScopedByTermOrPlanAndIndexed()
    {
        var exam = new Meeting { Kind = "Final Exam", StartDate = "2026-12-10" };
        var section = Section(Weekly("2026-09-02", "2026-12-04"), Weekly("2026-09-02", "2026-12-04"), exam);
        var uids = Lines(IcsWriter.Write("Fall", Fall, [section])).Where(l => l.StartsWith("UID:", StringComparison.Ordinal)).ToList();
        Assert.Equal(["UID:202710-12345-0@neuclasssearch", "UID:202710-12345-1@neuclasssearch", "UID:202710-12345-final@neuclasssearch"], uids);
        var spring = new Term("202730", "Spring 2027 Semester");
        var other = Lines(IcsWriter.Write("Spring", spring, [section])).Where(l => l.StartsWith("UID:", StringComparison.Ordinal));
        Assert.Empty(uids.Intersect(other));
        var plan = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Contains($"UID:{plan:D}-12345-0@neuclasssearch", IcsWriter.Write("Fall", Fall, [section], planId: plan));
    }

    [Fact]
    public void WeeklyMeetingWithoutKnownDatesIsSkipped()
    {
        foreach (var (start, end) in new (string?, string?)[] { ("2026-09-02", null), (null, "2026-12-04"), (null, null) })
        {
            var ics = IcsWriter.Write("Fall", Fall, [Section(Weekly(start, end))]);
            Assert.DoesNotContain("BEGIN:VEVENT", ics);
            Assert.DoesNotContain("RRULE:FREQ=WEEKLY", ics);
        }
    }

    [Fact]
    public void RecurringEventWithoutLastDateIsNeverWritten()
    {
        var open = new ScheduleExportEvent
        {
            Uid = "x", Title = "t", Notes = "n", FirstDate = new DateOnly(2026, 9, 2),
            StartMinute = 600, EndMinute = 660, Weekdays = [1, 3],
        };
        var ics = ScheduleExporter.IcsString([open], "Fall");
        Assert.DoesNotContain("BEGIN:VEVENT", ics);
        Assert.DoesNotContain("RRULE:FREQ=WEEKLY", ics);
    }

    [Fact]
    public void UntilIsUtcDateTimeForTimedDtstart()
    {
        var ics = IcsWriter.Write("Fall", Fall, [Section(Weekly("2026-09-02", "2026-12-04"))]);
        Assert.Matches(@"RRULE:FREQ=WEEKLY;BYDAY=MO,WE;UNTIL=\d{8}T\d{6}Z", ics);

        // 2026 Spring DST transition (2026-03-08): 23:59:59 EDT is UTC-4 -> 2026-03-09 03:59:59Z
        var springTransition = IcsWriter.Write("Spring", Fall, [Section(Weekly("2026-01-05", "2026-03-08"))]);
        Assert.Contains("UNTIL=20260309T035959Z", springTransition);

        // 2026 Fall DST transition (2026-11-01): 23:59:59 EST is UTC-5 -> 2026-11-02 04:59:59Z
        var fallTransition = IcsWriter.Write("Fall", Fall, [Section(Weekly("2026-09-02", "2026-11-01"))]);
        Assert.Contains("UNTIL=20261102T045959Z", fallTransition);
    }

    [Fact]
    public void AllDayRecurringEventWritesDateValuedUntil()
    {
        var allDay = new ScheduleExportEvent
        {
            Uid = "all-day-recurring@neuclasssearch",
            Title = "All-Day Workshop",
            Notes = "Recurring notes",
            FirstDate = new DateOnly(2026, 9, 2),
            StartMinute = null,
            EndMinute = null,
            Weekdays = [1, 3],
            LastDate = new DateOnly(2026, 12, 4),
        };
        var ics = ScheduleExporter.IcsString([allDay], "Calendar");
        Assert.Contains("DTSTART;VALUE=DATE:20260902", ics);
        Assert.Contains("DTEND;VALUE=DATE:20260903", ics);
        Assert.Contains("RRULE:FREQ=WEEKLY;BYDAY=MO,WE;UNTIL=20261204", ics);
        Assert.DoesNotMatch(@"RRULE:.*UNTIL=\d{8}T", ics);
    }

    [Fact]
    public void NormalizesLineEndingsWithoutBareCrOrBareLf()
    {
        var ev = new ScheduleExportEvent
        {
            Uid = "raw-bytes-test@neuclasssearch",
            Title = "CR\rLF\nCRLF\r\nTitle",
            Notes = "First\rSecond\nThird\r\nFourth",
            FirstDate = new DateOnly(2026, 9, 2),
            StartMinute = 600,
            EndMinute = 660,
        };
        var ics = ScheduleExporter.IcsString([ev], "Calendar\r\nName\rLoneCR");
        var bytes = Encoding.UTF8.GetBytes(ics);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\r')
            {
                Assert.True(i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n', $"Bare CR at byte {i}");
            }
            if (bytes[i] == (byte)'\n')
            {
                Assert.True(i > 0 && bytes[i - 1] == (byte)'\r', $"Bare LF at byte {i}");
            }
        }
        Assert.Contains("SUMMARY:CR\\nLF\\nCRLF\\nTitle", ics);
        Assert.Contains("DESCRIPTION:First\\nSecond\\nThird\\nFourth", ics);
    }

    [Fact]
    public void CourseTitleFallbackUsesCatalogTitleThenSectionTitle()
    {
        var plan = new SchedulePlan { Name = "My Plan", TermCode = "202710" };
        var courseKey = new CourseKey("CS", "2500");
        plan.Courses.Add(new PlanCourse { Key = courseKey });
        plan.SelectedCRNs.Add("12345");

        var course = new Course
        {
            Key = courseKey,
            Title = "Catalog Fundamentals",
            Subject = "CS",
            Number = "2500",
            MinCredits = 4,
            MaxCredits = 4,
            DetailsLoaded = true,
        };
        var section = Section(Weekly("2026-09-02", "2026-12-04"));
        section.Title = "Raw Section Title";
        var index = CatalogIndex.Build(new TermCatalog
        {
            Term = Fall,
            Courses = [course],
            Sections = [section],
        });

        var (events, _) = ScheduleExporter.Events(plan, index);
        Assert.Single(events);
        Assert.Contains("Catalog Fundamentals", events[0].Notes);
        Assert.DoesNotContain("Raw Section Title", events[0].Notes);

        var emptyCourse = new Course
        {
            Key = courseKey,
            Title = "",
            Subject = "CS",
            Number = "2500",
            MinCredits = 4,
            MaxCredits = 4,
            DetailsLoaded = true,
        };
        var emptyIndex = CatalogIndex.Build(new TermCatalog
        {
            Term = Fall,
            Courses = [emptyCourse],
            Sections = [section],
        });
        var (fallbackEvents, _) = ScheduleExporter.Events(plan, emptyIndex);
        Assert.Single(fallbackEvents);
        Assert.Contains("Raw Section Title", fallbackEvents[0].Notes);

        var icsWithTitle = IcsWriter.Write("Fall", Fall, [section], titleOf: _ => "Catalog Fundamentals");
        Assert.Contains("Catalog Fundamentals", icsWithTitle);
        Assert.DoesNotContain("Raw Section Title", icsWithTitle);

        var icsFallback = IcsWriter.Write("Fall", Fall, [section]);
        Assert.Contains("Raw Section Title", icsFallback);
    }
}
