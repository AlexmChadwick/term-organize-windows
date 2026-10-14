// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;

namespace TermOrganize.Core.Tests;

public class ReleaseFlowTests
{
    [Fact]
    public void IcsUntilIsUtcAndLongLinesFold()
    {
        var term = new Term("202710", "Fall 2026 Semester");
        var section = new CourseSection
        {
            Crn = "12345",
            Subject = "CS",
            Number = "2500",
            Title = new string('A', 120) + ", lab",
            ClassType = "Lecture",
            Meetings =
            [
                new Meeting
                {
                    Days = DaySet.Monday | DaySet.Wednesday,
                    Start = 10 * 60,
                    End = 11 * 60 + 5,
                    BuildingCode = "WVH",
                    Room = "210",
                },
            ],
        };
        var ics = IcsWriter.Write("Fall 2026", term, [section]);
        Assert.Contains("BEGIN:VEVENT", ics);
        Assert.Contains("DTSTART;TZID=America/New_York:20260902T100000", ics);
        Assert.Contains("DTEND;TZID=America/New_York:20260902T110500", ics);
        Assert.Contains("RRULE:FREQ=WEEKLY;BYDAY=MO,WE;UNTIL=20261224T045959Z", ics);
        Assert.Contains("\r\n ", ics);
        foreach (var line in ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, line);
    }

    [Fact]
    public void CampusClockUsesEasternWallTime()
    {
        var afternoon = new DateTimeOffset(2026, 10, 14, 16, 30, 0, TimeSpan.Zero);
        var now = TermCalendar.Now(afternoon);
        Assert.Equal(DateTimeKind.Unspecified, now.Kind);
        Assert.Equal(new DateTime(2026, 10, 14, 12, 30, 0), now);
        var moment = RoomAvailability.Moment.From(afternoon);
        Assert.Equal((int)DayOfWeek.Wednesday, moment.Weekday);
        Assert.Equal(12 * 60 + 30, moment.Minutes);

        var midnight = new DateTimeOffset(2026, 1, 15, 5, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTime(2026, 1, 15, 0, 0, 0), TermCalendar.Now(midnight));
    }

    [Fact]
    public void SurveyPlaceholderKeepsBothClubNights()
    {
        var result = PreferenceParser.Interpret(PreferenceParser.ExampleSentence);
        Assert.True(result.Succeeded);
        Assert.True(result.ChangedCount > 0);
        Assert.Equal(10 * 60, result.Preferences.EarliestStart!.Minutes);
        Assert.True(result.Preferences.DaysOff.Contains((int)DayOfWeek.Friday));
        Assert.NotNull(result.Preferences.Lunch);
        var block = Assert.Single(result.Preferences.BlockedTimes);
        Assert.Equal(DaySet.Tuesday | DaySet.Thursday, block.Days);
        Assert.Equal(18 * 60, block.Start);
        Assert.Equal(20 * 60, block.End);
    }

    [Fact]
    public void FacultyAttachReachesTheDirectory()
    {
        var banner = new BannerSection
        {
            CourseReferenceNumber = "12345",
            Subject = "CS",
            CourseNumber = "2500",
            CourseTitle = "Fundamentals of Computer Science 1",
            MeetingsFaculty =
            [
                new BannerMeetingFaculty
                {
                    MeetingTime = new BannerMeetingTime
                    {
                        Monday = true,
                        Wednesday = true,
                        BeginTime = "1000",
                        EndTime = "1140",
                        Building = "WVH",
                        BuildingDescription = "West Village H",
                        Room = "210",
                    },
                },
            ],
        };
        BannerClient.ApplyFaculty(banner, [new Instructor { Name = "Smith, Ada", Primary = true, Email = "ada@example.invalid" }]);
        var catalog = CatalogBuilder.Merge(
            null,
            [banner],
            new Term("202710", "Fall 2026 Semester"),
            [new Subject { Code = "CS", Name = "Computer Science" }],
            null,
            null);
        var index = CatalogIndex.Build(catalog);
        var section = Assert.Single(index.Sections(new CourseKey("CS", "2500")));
        Assert.Equal("Smith, Ada", Assert.Single(section.Instructors!).Name);
        Assert.Contains(ProfessorDirectoryEntry.Build(index), entry => entry.DisplayName == "Ada Smith");
        Assert.NotEmpty(index.Buildings);
    }

    [Fact]
    public void CourseDetailsFillABlankDescription()
    {
        var target = new Course { Subject = "CS", Number = "2500", Title = "Stub", Description = "" };
        var detailed = new Course
        {
            Subject = "CS",
            Number = "2500",
            Title = "Fundamentals of Computer Science 1",
            Description = "Discrete structures, programs, and the memory model.",
            College = "Khoury",
        };
        CatalogBuilder.ApplyCourseDetails(target, detailed);
        Assert.Equal("Discrete structures, programs, and the memory model.", target.Description);
        Assert.Equal("Fundamentals of Computer Science 1", target.Title);
        Assert.Equal("Khoury", target.College);
        Assert.True(target.DetailsLoaded);

        var topics = new Course { Subject = "CS", Number = "4990", Title = "Special Topics", SpecialTopics = true, Description = "" };
        CatalogBuilder.ApplyCourseDetails(topics, detailed);
        Assert.Equal("Special Topics", topics.Title);
        Assert.Equal(detailed.Description, topics.Description);
    }

    [Fact]
    public void DegreePlanPdfFileRoundTripsTheCourse()
    {
        var key = new CourseKey("CS", "2500");
        var plan = new DegreePlan
        {
            Name = "Self-test",
            Terms =
            [
                new DegreePlanTerm
                {
                    Season = PlanSeason.Fall,
                    Year = 2026,
                    Courses = [new DegreePlanCourse(key, "Fundamentals of Computer Science 1", 4)],
                },
            ],
        };
        var path = Path.Combine(Path.GetTempPath(), "term-organize-degree-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            File.WriteAllBytes(path, DegreePlanPdf.Render(plan));
            var imported = DegreePlanImporter.Import(path);
            Assert.Contains(imported.Plan.Terms.SelectMany(term => term.Courses), course => course.Key == key);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
