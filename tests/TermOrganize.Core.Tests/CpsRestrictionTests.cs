// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class CpsRestrictionTests
{
    [Fact]
    public void OnlyCpsLevelsOrOnlyTheCollegeCount()
    {
        var onlyUc = SectionRestrictions.Parse("""
            <span>Must be enrolled in one of the following Levels:</span><br>
            CPS - Undergraduate Semester (UC)
            """);
        Assert.True(SectionRestrictions.CpsOnly(onlyUc));

        var mixed = SectionRestrictions.Parse("""
            <p>Must be enrolled in one of the following Levels:</p>
            Graduate<br>CPS - Undergraduate Semester<br>Undergraduate
            """);
        Assert.False(SectionRestrictions.CpsOnly(mixed));

        var college = SectionRestrictions.Parse("""
            Must be enrolled in one of the following Colleges:<br>
            College of Professional Studies
            """);
        Assert.True(SectionRestrictions.CpsOnly(college));

        var cannot = SectionRestrictions.Parse("""
            Cannot be enrolled in one of the following Levels:<br>
            CPS - Undergraduate Semester (UC)
            """);
        Assert.False(SectionRestrictions.CpsOnly(cannot));
        Assert.Equal(RestrictionKind.Cannot, cannot[0].Kind);
        Assert.Equal("Levels", cannot[0].Category);

        Assert.Null(SectionRestrictions.CpsOnly(null));
        Assert.False(SectionRestrictions.CpsOnly([]));
    }

    [Fact]
    public void CourseNoteIsAllSomeOrNothing()
    {
        var cps = Section("34087", true);
        var open = Section("34629", false);
        var unknown = Section("10001", null);

        Assert.Equal(CpsNotes.Restricted, CpsNotes.ForCourse([cps, Section("34090", true)]));
        Assert.Equal(CpsNotes.Some, CpsNotes.ForCourse([cps, open]));
        Assert.Null(CpsNotes.ForCourse([cps, unknown]));
        Assert.Null(CpsNotes.ForCourse([open, unknown]));
        Assert.Null(CpsNotes.ForCourse([unknown]));
        Assert.Equal(true, CpsNotes.Flag([cps, Section("34092", true)]));
        Assert.Equal(false, CpsNotes.Flag([cps, open]));
        Assert.Null(CpsNotes.Flag([cps, unknown]));
    }

    static CourseSection Section(string crn, bool? cps) => new()
    {
        Crn = crn,
        Subject = "ITC",
        Number = "1000",
        Restrictions = cps switch
        {
            null => null,
            true => [new SectionRestriction { Kind = RestrictionKind.Must, Category = "Levels", Values = ["CPS - Undergraduate Semester (UC)"] }],
            false => [new SectionRestriction { Kind = RestrictionKind.Must, Category = "Levels", Values = ["Undergraduate"] }],
        },
    };
}
