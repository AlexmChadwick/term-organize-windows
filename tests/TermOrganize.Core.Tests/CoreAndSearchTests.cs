// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TermOrganize.Core.Tests;

public class CoreModelTests
{
    [Fact]
    public void TermNames()
    {
        Assert.Equal("Fall 2026", new Term("202710", "Fall 2026 Semester").Name);
        Assert.Equal("Spring 2026", new Term("202630", "Spring 2026 Semester (View Only)").Name);
        var cps = new Term("202635", "Spring 2026 CPS Quarter (View Only)");
        Assert.Equal("Spring 2026 · CPS Quarter", cps.Name);
        Assert.Equal("Spring 2026", cps.ShortName);
        Assert.Equal(TermCollege.Cps, cps.College);
        Assert.True(cps.IsViewOnly);
        Assert.Equal(TermSeason.Fall, new Term("202710", "").Season);
        Assert.Equal(2026, new Term("202710", "").CalendarYear);
        Assert.Equal(2026, new Term("202630", "").CalendarYear);
    }

    [Fact]
    public void CourseKeyParsing()
    {
        Assert.Equal(new CourseKey("CS", "3000"), CourseKey.Parse("CS 3000"));
        Assert.Equal(new CourseKey("CS", "3000"), CourseKey.Parse("cs3000"));
        Assert.Equal(new CourseKey("MATH", "209M"), CourseKey.Parse("math-209m"));
        Assert.Null(CourseKey.Parse("3000"));
        Assert.Equal(3, new CourseKey("CS", "3000").Level);
        Assert.True(new CourseKey("CS", "2100") < new CourseKey("CS", "3000"));
        Assert.True(new CourseKey("CS", "900") < new CourseKey("CS", "1800"));
    }

    [Fact]
    public void DaySets()
    {
        var mwr = DaySet.FromIndices([1, 3, 4]);
        Assert.Equal("MWR", mwr.CompactLabel);
        Assert.True(mwr.Contains(3));
        Assert.False(mwr.Contains(2));
        Assert.Equal(3, mwr.Count);
        Assert.True(mwr.IsSubsetOf(DaySet.Weekdays));
        Assert.False(DaySet.Saturday.IsSubsetOf(DaySet.Weekdays));
    }

    [Fact]
    public void TimeFormatting()
    {
        Assert.Equal(875, TimeOfDay.MinutesFromHhmm(1435));
        Assert.Equal("2:35 PM", TimeOfDay.Label(875));
        Assert.Equal("12:00 AM", TimeOfDay.Label(0));
        Assert.Equal("10:30 \u2013 11:35 AM", TimeOfDay.RangeLabel(630, 695));
        Assert.Equal("11:45 AM \u2013 1:25 PM", TimeOfDay.RangeLabel(705, 805));
        Assert.Equal("4 credits", CreditFormatter.Label(4, 4));
        Assert.Equal("1 credit", CreditFormatter.Label(1, 1));
        Assert.Equal("1\u20134 credits", CreditFormatter.Label(1, 4));
    }

    [Fact]
    public void RequisiteHelpers()
    {
        var a = new CourseKey("CS", "2100");
        var b = new CourseKey("CS", "2510");
        var c = new CourseKey("CS", "1800");
        var nested = Requisite.And(Requisite.Or(Requisite.Course(a), Requisite.Or(Requisite.Course(b))), Requisite.And(Requisite.Course(c)));
        var simplified = nested.Simplified();
        Assert.Equal(Requisite.And(Requisite.Or(Requisite.Course(a), Requisite.Course(b)), Requisite.Course(c)), simplified);
        Assert.Equal([a, b, c], nested.ReferencedCourses().ToArray());
        Assert.Equal("(CS 2100 or CS 2510) and CS 1800", simplified?.Summary);
        Assert.True(simplified!.IsSatisfied([a, c]));
        Assert.False(simplified.IsSatisfied([a]));
    }

    [Fact]
    public void InstructorNames()
    {
        Assert.Equal("Natalia Ptitsyna", new Instructor { Name = "Ptitsyna, Natalia", Primary = true }.DisplayName);
        Assert.Equal("Staff", new Instructor { Name = "Staff" }.DisplayName);
    }

    [Fact]
    public void CatalogArchiveRoundTrip()
    {
        var catalog = new TermCatalog
        {
            Term = new Term("202710", "Fall 2026 Semester"),
            Courses =
            [
                new Course
                {
                    Subject = "CS", Number = "3000", Title = "Algorithms and Data", Description = "Introduces…",
                    MinCredits = 4, MaxCredits = 4, Attributes = ["NCFQ"],
                    Prereqs = Requisite.Course(new CourseKey("CS", "2100")), DetailsLoaded = true,
                },
            ],
        };
        catalog.RecomputePostreqs();
        var decoded = CatalogArchive.Decode(CatalogArchive.Encode(catalog));
        Assert.Equal("Algorithms and Data", decoded.Courses[0].Title);
        Assert.Equal([NUPath.Fq], decoded.Courses[0].Nupaths.ToArray());
        Assert.Equal(Requisite.Course(new CourseKey("CS", "2100")), decoded.Courses[0].Prereqs);
        Assert.Equal("202710", CatalogArchive.TermCode("term-202710.catalog"));
    }

    [Fact]
    public void EditDistanceIsCappedLevenshtein()
    {
        Assert.Equal(1, SearchText.EditDistance("algorithms", "algoritms", 2));
        Assert.Equal(3, SearchText.EditDistance("kitten", "sitting", 2));
    }
}

public class SearchEngineTests
{
    private static readonly Lazy<CatalogIndex> Index = new(() => CatalogIndex.Build(BundledCatalog.Load()));

    private static IReadOnlyList<string> Top(string text, SearchFilters? filters = null) =>
        SearchEngine.Search(text, Index.Value, filters).Results.Take(5).Select(r => r.Course.Code).ToList();

    [Fact]
    public void BundledCatalogIsComplete()
    {
        var index = Index.Value;
        Assert.True(index.Courses.Count > 2_500);
        Assert.True(index.Catalog.Sections.Count > 7_000);
        Assert.All(index.Courses, course => Assert.True(course.DetailsLoaded));
        Assert.NotEmpty(index.Instructors);
        Assert.NotEmpty(index.Buildings);
    }

    [Fact]
    public void LzfseCatalogMatchesGzip()
    {
        var path = BundledCatalog.Resolve("term-202710.catalog");
        Assert.NotNull(path);
        var fromLzfse = CatalogArchive.Read(path);
        Assert.Equal(Index.Value.Courses.Count, fromLzfse.Courses.Count);
        Assert.Equal(Index.Value.Catalog.Sections.Count, fromLzfse.Sections.Count);
    }

    [Fact]
    public void ExactCourseCodes()
    {
        Assert.Equal("CS 3000", Top("CS 3000")[0]);
        Assert.Equal("CS 3000", Top("cs3000")[0]);
        Assert.Equal("CS 3000", Top("cs 30")[0]);
    }

    [Fact]
    public void TitlesAndShorthand()
    {
        Assert.Equal("CS 3000", Top("algorithms and data")[0]);
        Assert.Equal("MATH 2331", Top("linear algebra")[0]);
        Assert.All(Top("orgo"), code => Assert.True(code.StartsWith("CHM") || code.StartsWith("CHEM"), code));
        Assert.Equal("MATH 2331", Top("linalg")[0]);
        Assert.Contains("CS 3000", Top("algoritms"));
    }

    [Fact]
    public void CrnLookup()
    {
        var section = Index.Value.Catalog.Sections[0];
        var response = SearchEngine.Search(section.Crn, Index.Value);
        Assert.Equal(section.Crn, response.CrnMatch?.Crn);
    }

    [Fact]
    public void SubjectAndProfessorMatches()
    {
        Assert.Equal(["CS"], SearchEngine.Search("cs", Index.Value).Subjects.Select(s => s.Code).ToArray());
        var instructor = Index.Value.Instructors.First(i => i.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 2);
        var response = SearchEngine.Search(instructor.DisplayName, Index.Value);
        Assert.Contains(response.Instructors, i => i.Name == instructor.Name);
    }

    [Fact]
    public void FiltersRequireAMatchingSection()
    {
        var filters = new SearchFilters
        {
            Subjects = ["CS"],
            OpenSeatsOnly = true,
            AllowedDays = DaySet.Monday | DaySet.Wednesday | DaySet.Thursday,
        };
        var results = SearchEngine.Search("", Index.Value, filters).Results;
        Assert.NotEmpty(results);
        foreach (var result in results)
        {
            Assert.Equal("CS", result.Course.Subject);
            Assert.Contains(Index.Value.Sections(result.Course.Key), filters.Matches);
        }
    }
}

public class SchedulePreferencesTests
{
    [Fact]
    public void DefaultsHaveNoOpinions()
    {
        var p = new SchedulePreferences();
        Assert.True(p.IsDefault);
        Assert.Equal(0, p.ActiveCount);
        Assert.Empty(p.SummaryParts());
        Assert.Equal("No preferences yet", p.Summary());
    }

    [Fact]
    public void SettingABandBackToOkayRestoresTheDefault()
    {
        var p = new SchedulePreferences { [TimeBand.Morning] = TimePreference.Prefer };
        Assert.False(p.IsDefault);
        p[TimeBand.Morning] = TimePreference.Okay;
        Assert.True(p.IsDefault);
    }

    [Fact]
    public void SummaryReadsLikeTheBrief()
    {
        var p = new SchedulePreferences
        {
            [TimeBand.EarlyMorning] = TimePreference.Prefer,
            [TimeBand.Morning] = TimePreference.Prefer,
            DaysOff = DaySet.Friday,
            BlockedTimes =
            [
                new BlockedTime { Label = "Club", Days = DaySet.Tuesday | DaySet.Thursday, Start = 18 * 60, End = 20 * 60 },
                new BlockedTime { Label = "Work", Days = DaySet.Monday, Start = 13 * 60, End = 17 * 60 },
            ],
        };
        Assert.Equal(["Mornings", "Fridays off", "2 blocked times"], p.SummaryParts());
        Assert.Equal("Mornings · Fridays off · 2 blocked times", p.Summary());
        p.DayLayout = DayLayout.FewerDays;
        Assert.Equal("Mornings · Fridays off · Fewer days · +1", p.Summary(3));
    }

    [Fact]
    public void SummaryNamesTheDaysAndTimes()
    {
        var p = new SchedulePreferences
        {
            DaysOff = DaySet.Monday | DaySet.Friday,
            EarliestStart = new TimeLimit(10 * 60),
            LatestEnd = new TimeLimit(17 * 60 + 30, PreferenceStrength.Preferred),
        };
        Assert.Equal(["Nothing before 10 AM", "Prefer done by 5:30 PM", "Mon & Fri off"], p.SummaryParts());
        p[TimeBand.Evening] = TimePreference.Avoid;
        p[TimeBand.Night] = TimePreference.Avoid;
        Assert.Equal("No evenings", p.SummaryParts()[0]);
    }

    [Fact]
    public void DisabledBlocksDontCount()
    {
        var p = new SchedulePreferences
        {
            BlockedTimes = [new BlockedTime { Label = "Work", Days = DaySet.Monday, Start = 600, End = 700, IsEnabled = false }],
        };
        Assert.Empty(p.ActiveBlockedTimes);
        Assert.Equal(0, p.ActiveCount);
        Assert.Empty(p.SummaryParts());
        Assert.False(p.IsDefault);
    }

    [Fact]
    public void PresetsToggleAndCombine()
    {
        var p = SchedulePreset.MorningPerson.Toggled(new SchedulePreferences());
        Assert.True(SchedulePreset.MorningPerson.IsApplied(p));
        Assert.Equal(TimePreference.Prefer, p[TimeBand.Morning]);
        Assert.Equal(TimePreference.Avoid, p[TimeBand.Night]);

        p = SchedulePreset.LongWeekends.Toggled(p);
        Assert.True(SchedulePreset.LongWeekends.IsApplied(p));
        Assert.True(SchedulePreset.MorningPerson.IsApplied(p));
        Assert.True(p.DaysOff.Contains(5));
        Assert.Equal(DayLayout.FewerDays, p.DayLayout);

        p = SchedulePreset.NightOwl.Toggled(p);
        Assert.True(SchedulePreset.NightOwl.IsApplied(p));
        Assert.False(SchedulePreset.MorningPerson.IsApplied(p));
        Assert.Equal(TimePreference.Avoid, p[TimeBand.Morning]);
        Assert.Equal(TimePreference.Prefer, p[TimeBand.Evening]);

        p = SchedulePreset.NightOwl.Toggled(p);
        p = SchedulePreset.LongWeekends.Toggled(p);
        Assert.True(p.IsDefault);
    }

    [Fact]
    public void NormalizingDropsWhatCantBeApplied()
    {
        var p = new SchedulePreferences
        {
            EarliestStart = new TimeLimit(600),
            LatestEnd = new TimeLimit(540),
            MaxClassesPerDay = 0,
            MaxMinutesPerDay = 10,
            MinimumBreakMinutes = 1_000,
            Lunch = new LunchBreak { Start = 700, End = 705, Minutes = 600 },
            BlockedTimes =
            [
                new BlockedTime { Label = "  ", Days = DaySet.Monday, Start = 600, End = 700 },
                new BlockedTime { Label = "No days", Days = DaySet.None, Start = 600, End = 700 },
                new BlockedTime { Label = "Backwards", Days = DaySet.Monday, Start = 700, End = 600 },
                new BlockedTime { Label = "Club", Days = DaySet.Tuesday, Start = 600, End = 700 },
                new BlockedTime { Label = "club", Days = DaySet.Tuesday, Start = 600, End = 700 },
            ],
            PreferredInstructors = ["Smith, Alex", " smith, alex ", ""],
            AvoidedInstructors = ["Smith, Alex", "Jones, Sam"],
            CreditTarget = new CreditTarget(40, 50),
        };
        var n = p.Normalized();
        Assert.Equal(new TimeLimit(540), n.LatestEnd);
        Assert.Null(n.MaxClassesPerDay);
        Assert.Null(n.MaxMinutesPerDay);
        Assert.Equal(90, n.MinimumBreakMinutes);
        Assert.True(n.Lunch!.End - n.Lunch.Start >= n.Lunch.Minutes);
        Assert.Equal(["Blocked time", "Club"], n.BlockedTimes.Select(b => b.Label).ToArray());
        Assert.Equal(["Smith, Alex"], n.PreferredInstructors);
        Assert.Equal(["Jones, Sam"], n.AvoidedInstructors);
        Assert.Equal(30, n.CreditTarget?.Upper);
    }

    [Fact]
    public void RoundTrips()
    {
        var original = FullyLoaded();
        var json = JsonSerializer.Serialize(original, AppJson.UserData);
        var decoded = JsonSerializer.Deserialize<SchedulePreferences>(json, AppJson.UserData);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void AnEmptyObjectDecodesToTheDefaults()
    {
        var decoded = JsonSerializer.Deserialize<SchedulePreferences>("{}", AppJson.UserData);
        Assert.NotNull(decoded);
        Assert.True(decoded.IsDefault);
    }

    [Fact]
    public void UnknownAndDamagedValuesFallBack()
    {
        const string json = """
        {
          "timeOfDay": {"morning": "prefer", "brunch": "prefer", "night": "sometimes"},
          "dayLayout": "teleport",
          "earliestStart": {"minutes": "soon"},
          "daysOff": 32,
          "maxClassesPerDay": 3,
          "blockedTimes": [
            {"label": "Club", "days": 20, "start": 1080, "end": 1200},
            "garbage",
            {"label": 7}
          ],
          "futureField": [1, 2, 3]
        }
        """;
        var decoded = JsonSerializer.Deserialize<SchedulePreferences>(json, AppJson.UserData)!;
        Assert.Equal(TimePreference.Prefer, decoded[TimeBand.Morning]);
        Assert.Equal(TimePreference.Okay, decoded[TimeBand.Night]);
        Assert.Equal(DayLayout.NoPreference, decoded.DayLayout);
        Assert.Null(decoded.EarliestStart);
        Assert.Equal(DaySet.Friday, decoded.DaysOff);
        Assert.Equal(3, decoded.MaxClassesPerDay);
        Assert.Equal((byte)20, decoded.BlockedTimes.First(b => b.Label == "Club").Days.Raw);
        Assert.Equal(["Club"], decoded.ActiveBlockedTimes.Select(b => b.Label).ToArray());
    }

    [Fact]
    public void PlansSavedBeforePreferencesExistStillLoad()
    {
        var plan = new SchedulePlan
        {
            Name = "Plan A",
            TermCode = "202730",
            Courses = [new PlanCourse { Key = new CourseKey("CS", "3000"), ColorIndex = 0 }],
        };
        var json = JsonSerializer.Serialize(plan, AppJson.UserData);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("preferences", out _));
        var decoded = JsonSerializer.Deserialize<SchedulePlan>(json, AppJson.UserData);
        Assert.Equal("Plan A", decoded!.Name);
        Assert.Null(decoded.Preferences);
        Assert.Single(decoded.Courses);
    }

    [Fact]
    public void PreferencesOfAnyShapeNeverTakeTheScheduleDown()
    {
        var plan = new SchedulePlan { Name = "Plan D", TermCode = "202730" };
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(plan, AppJson.UserData));
        foreach (var garbage in new[] { "7", "\"text\"", "[1,2,3]", "true" })
        {
            var damaged = doc.RootElement.GetRawText().TrimEnd('}') + $",\"preferences\":{garbage}}}";
            var decoded = JsonSerializer.Deserialize<SchedulePlan>(damaged, AppJson.UserData);
            Assert.Equal("Plan D", decoded!.Name);
            Assert.True(decoded.Preferences?.IsDefault ?? true);
        }
    }

    [Fact]
    public void DamagedPreferencesNeverTakeTheScheduleDown()
    {
        var plan = new SchedulePlan { Name = "Plan C", TermCode = "202730" };
        var raw = JsonSerializer.Serialize(plan, AppJson.UserData).TrimEnd('}');
        var damaged = raw + ",\"preferences\":{\"timeOfDay\":7,\"blockedTimes\":\"nope\",\"campus\":3}}";
        var decoded = JsonSerializer.Deserialize<SchedulePlan>(damaged, AppJson.UserData);
        Assert.Equal("Plan C", decoded!.Name);
        Assert.True(decoded.Preferences?.IsDefault);
    }

    [Fact]
    public void DegreePlanTextAndPdfRoundTripSixtyCourses()
    {
        var plan = new DegreePlan
        {
            Name = "New Plan",
            Program = "CS-B-CSCI-BSCS",
            StudentName = "Ada Lovelace",
            StudentID = "001234567",
        };
        for (var i = 0; i < 60; i++)
        {
            plan.Completed.Add(new DegreePlanCourse(new CourseKey("CS", (1800 + i).ToString(CultureInfo.InvariantCulture)), "Course " + i, i == 0 ? 1 : 4));
        }
        plan.Terms.Add(new DegreePlanTerm
        {
            Season = PlanSeason.Spring,
            Year = 2027,
            Courses = [new DegreePlanCourse(new CourseKey("CS", "3484"), "GUI Programming", 4)],
        });
        plan.Terms.Add(new DegreePlanTerm { Season = PlanSeason.SummerA, Year = 2027 });
        var printed = new DateTimeOffset(2026, 10, 3, 16, 42, 0, TimeSpan.Zero);
        var text = DegreePlanFormat.Text(plan, printed, includeStudent: true);
        Assert.Contains("Printed:  ", text);
        Assert.Contains("CS  1800 (1.0 Hour) Course 0", text);
        Assert.Contains("Spring  2027 (4.0 Hours)", text);
        Assert.Contains("Summer Session A 2027 (0.0 Hours)", text);
        Assert.Contains(new string('-', 115), text);
        var parsed = DegreePlanFormat.Parse(text, "view-plan.pdf");
        Assert.Equal(60, parsed.Plan.Completed.Count);
        Assert.Equal("New Plan", parsed.Plan.Name);
        Assert.Equal("CS-B-CSCI-BSCS", parsed.Plan.Program);
        Assert.Equal(new CourseKey("CS", "3484"), parsed.Plan.Terms[0].Courses[0].Key);
        var pdf = DegreePlanPdf.Render(plan, printed, includeStudent: true);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf[..5]));
        var again = DegreePlanImporter.Import(pdf, "view-plan.pdf", pdf: true);
        Assert.Equal(60, again.Plan.Completed.Count);
        Assert.Equal("New Plan", again.Plan.Name);
        Assert.Contains("GUI Programming", again.Plan.Terms.SelectMany(t => t.Courses).Select(c => c.Title));
    }

    private static SchedulePreferences FullyLoaded() => new()
    {
        [TimeBand.EarlyMorning] = TimePreference.Avoid,
        [TimeBand.Afternoon] = TimePreference.Prefer,
        EarliestStart = new TimeLimit(600, PreferenceStrength.Preferred),
        LatestEnd = new TimeLimit(1_020),
        DaysOff = DaySet.Friday | DaySet.Monday,
        DaysOffStrength = PreferenceStrength.Required,
        DayLayout = DayLayout.SpreadOut,
        MaxClassesPerDay = 3,
        MaxMinutesPerDay = 300,
        Gaps = GapPreference.Compact,
        Breaks = BreakPreference.AtLeast,
        MinimumBreakMinutes = 20,
        MinimumBreakStrength = PreferenceStrength.Required,
        Lunch = new LunchBreak { Start = 690, End = 840, Minutes = 45 },
        BlockedTimes =
        [
            new BlockedTime { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Label = "Robotics club", Days = DaySet.Tuesday | DaySet.Thursday, Start = 18 * 60, End = 20 * 60 },
            new BlockedTime { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Label = "Work", Days = DaySet.Monday, Start = 13 * 60, End = 17 * 60, Strength = PreferenceStrength.Preferred, IsEnabled = false },
        ],
        Format = FormatPreference.Hybrid,
        FormatStrength = PreferenceStrength.Required,
        Campus = "Boston",
        CampusStrength = PreferenceStrength.Required,
        Seats = SeatPolicy.WaitlistOK,
        PreferredInstructors = ["Smith, Alex"],
        AvoidedInstructors = ["Jones, Sam"],
        CreditTarget = new CreditTarget(16, 18),
    };
}
