// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// Portions derived from SearchNEU where the Swift file says so. Modified 2026-10-13: ported from Swift to C#.

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class ScheduleGeneratorTests
{
    private static CourseSection Section(string crn, string subject, string number, int[] days, double start, double end, int seats = 10, bool honors = false, string campus = "Boston") => new()
    {
        Crn = crn,
        Subject = subject,
        Number = number,
        Title = $"{subject} {number}",
        SectionNumber = "01",
        PartOfTerm = "1",
        SeatCapacity = 40,
        SeatsRemaining = seats,
        Enrollment = 40 - seats,
        ClassType = "Lecture",
        Honors = honors,
        Campus = campus,
        InstructionMethod = "Traditional",
        Meetings =
        [
            new Meeting
            {
                Days = DaySet.FromIndices(days),
                Start = (int)(start * 60),
                End = (int)(end * 60),
                BuildingCode = "WVH",
                Building = "West Village H",
                Room = "210",
                CampusCode = "BOS",
                Kind = "Class",
                StartDate = "2026-09-09",
                EndDate = "2026-12-13",
            },
        ],
    };

    private static ScheduleCourseInput Course(string subject, string number, bool required, params CourseSection[] sections) => new()
    {
        Key = new CourseKey(subject, number),
        IsRequired = required,
        Sections = sections,
    };

    [Fact]
    public void SlotMasksDetectOverlaps()
    {
        var a = new ScheduleSlotMask(Section("1", "CS", "1", [1, 3], 10, 11));
        var b = new ScheduleSlotMask(Section("2", "CS", "2", [3], 10.5, 12));
        var c = new ScheduleSlotMask(Section("3", "CS", "3", [1, 3], 11, 12));
        var otherDays = new ScheduleSlotMask(Section("4", "CS", "4", [2, 4], 10, 11));
        Assert.True(a.Conflicts(b));
        Assert.False(a.Conflicts(c));
        Assert.False(a.Conflicts(otherDays));
    }

    [Fact]
    public void GeneratesOnlyConflictFreeCombinations()
    {
        var courses = new[]
        {
            Course("CS", "3000", true,
                Section("A1", "CS", "3000", [1, 3, 4], 9, 10),
                Section("A2", "CS", "3000", [1, 3, 4], 13, 14)),
            Course("MATH", "2331", true,
                Section("B1", "MATH", "2331", [1, 3, 4], 9.5, 10.5),
                Section("B2", "MATH", "2331", [2, 5], 9, 10.5)),
        };
        var result = ScheduleGenerator.Generate(courses, new ScheduleFilters());
        Assert.Equal(3, result.TotalFound);
        Assert.DoesNotContain(result.Schedules, schedule => schedule.Crns.ToHashSet().SetEquals(["A1", "B1"]));
        foreach (var schedule in result.Schedules)
        {
            var masks = schedule.Sections.Select(section => new ScheduleSlotMask(section)).ToList();
            for (var i = 0; i < masks.Count; i++)
                for (var j = i + 1; j < masks.Count; j++)
                    Assert.False(masks[i].Conflicts(masks[j]));
        }
    }

    [Fact]
    public void RespectsFilters()
    {
        var courses = new[]
        {
            Course("CS", "3000", true,
                Section("EARLY", "CS", "3000", [1, 3], 8, 9),
                Section("FRI", "CS", "3000", [5], 12, 15),
                Section("OK", "CS", "3000", [2, 4], 11, 12)),
        };
        var filters = new ScheduleFilters { EarliestStart = 9 * 60, FreeDays = DaySet.Friday };
        var result = ScheduleGenerator.Generate(courses, filters);
        Assert.Equal(["OK"], result.Schedules.SelectMany(schedule => schedule.Crns).ToArray());
    }

    [Fact]
    public void OptionalCoursesFillInWhenTheyFit()
    {
        var courses = new[]
        {
            Course("CS", "3000", true, Section("R", "CS", "3000", [1, 3], 10, 11)),
            Course("ARTF", "1220", false,
                Section("O1", "ARTF", "1220", [1], 10.5, 12),
                Section("O2", "ARTF", "1220", [2], 10, 12)),
        };
        var result = ScheduleGenerator.Generate(courses, new ScheduleFilters());
        Assert.Equal(new[] { "O2", "R" }.OrderBy(crn => crn), result.Schedules.Single().Crns.OrderBy(crn => crn));

        var single = ScheduleGenerator.Generate(courses, new ScheduleFilters { CourseCount = 1 });
        Assert.Equal(["R"], single.Schedules.Single().Crns);
    }

    [Fact]
    public void HalfTermSectionsAtTheSameClockTimeDoNotConflict()
    {
        var first = Section("H1", "CS", "2500", [1], 10, 11);
        first.Meetings[0].EndDate = "2026-10-15";
        var second = Section("H2", "MATH", "1341", [1], 10, 11);
        second.Meetings[0].StartDate = "2026-10-20";
        var result = ScheduleGenerator.Generate(
            [Course("CS", "2500", true, first), Course("MATH", "1341", true, second)],
            new ScheduleFilters());
        Assert.Equal(1, result.TotalFound);
    }

    [Fact]
    public void RealCatalogPlanGenerates()
    {
        var index = CatalogIndex.Build(BundledCatalog.Load());
        CourseKey[] keys = [new("CS", "3000"), new("CS", "3001"), new("MATH", "2331")];
        var courses = keys.Select(key => new ScheduleCourseInput
        {
            Key = key,
            IsRequired = true,
            Sections = index.Sections(key),
        }).ToList();
        var result = ScheduleGenerator.Generate(courses, new ScheduleFilters());
        Assert.True(result.TotalFound > 0);
        Assert.All(result.Schedules, schedule => Assert.Equal(3, schedule.Crns.Count));
        Assert.True(result.Elapsed < 10);
    }
}
