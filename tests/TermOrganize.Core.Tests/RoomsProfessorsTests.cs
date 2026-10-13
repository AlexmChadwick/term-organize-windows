// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class RoomsProfessorsTests
{
    [Fact]
    public void ProfessorPageTextsAndRateMyProfessorsLinks()
    {
        var a = new CourseKey("CS", "3000");
        var b = new CourseKey("CS", "3001");
        CourseSection Section(CourseKey key, string number) => new()
        {
            Crn = key.Number + number,
            Subject = key.Subject,
            Number = key.Number,
            Title = "T",
            SectionNumber = number,
            ClassType = "Lecture",
        };
        var sections = new[] { Section(a, "01"), Section(a, "04"), Section(b, "02") };
        Assert.Equal("Teaches 3 sections across 2 courses", ProfessorPage.SummaryText(sections));
        Assert.Equal("Teaches 1 section across 1 course", ProfessorPage.SummaryText([sections[2]]));
        Assert.Equal("Sections 01, 04", ProfessorPage.SectionNote(a, sections));
        Assert.Equal("Section 02", ProfessorPage.SectionNote(b, sections));
        Assert.Equal(
            "https://www.ratemyprofessors.com/search/professors/696?q=Jane%20Doe",
            ProfessorPage.RateMyProfessorsUrl("Jane Doe").AbsoluteUri);
        Assert.Contains("q=Jos%C3%A9%20O%27Neil-Smith", ProfessorPage.RateMyProfessorsUrl("José O'Neil-Smith").AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void CourseColorsAreStableAndDistinctForAShortList()
    {
        var keys = new[] { new CourseKey("CS", "3000"), new CourseKey("CS", "2500"), new CourseKey("MATH", "1341") };
        var colors = ProfessorPage.CourseColors(keys);
        Assert.Equal(3, colors.Count);
        Assert.Equal(3, colors.Values.Distinct().Count());
        Assert.Equal(colors, ProfessorPage.CourseColors(keys));
        Assert.Equal(CourseColor.ForCode("CS 3000"), CourseColor.ForCourse(keys[0]));
    }

    [Fact]
    public void OutsideClassHoursHasAWarning()
    {
        Assert.Contains("7 AM", RoomsText.OutsideHours, StringComparison.Ordinal);
        Assert.Contains("locked or reserved", RoomsText.Disclaimer, StringComparison.Ordinal);
    }
}
