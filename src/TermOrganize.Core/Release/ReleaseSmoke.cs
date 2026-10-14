// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;

namespace TermOrganize.Core;

/// <summary>
/// Headless check of the live Northeastern flows. The Windows app runs this for
/// <c>--selftest</c> before it opens a window. An empty result or a thrown
/// exception is a failure. This does not run as part of <c>dotnet test</c>.
/// </summary>
public static class ReleaseSmoke
{
    public const string OkMarker = "SELFTEST OK";

    public static async Task<int> RunAsync(string directory, CancellationToken cancellationToken = default)
    {
        var log = new StringBuilder();
        var logPath = Path.Combine(directory, "selftest.log");
        void Step(string line)
        {
            log.Append(line).Append('\n');
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(logPath, log.ToString());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(4));
        try
        {
            Directory.CreateDirectory(directory);
            using var banner = new BannerClient(TimeSpan.FromSeconds(25), maxAttempts: 3);
            await RunAsync(banner, directory, Step, timeout.Token).ConfigureAwait(false);
            Step(OkMarker);
            return 0;
        }
        catch (Exception ex)
        {
            Step("FAIL");
            Step(ex.ToString());
            return 1;
        }
    }

    private static async Task RunAsync(BannerClient banner, string directory, Action<string> step, CancellationToken cancellationToken)
    {
        var terms = await banner.FetchTermsAsync(40, cancellationToken: cancellationToken).ConfigureAwait(false);
        Require(terms.Count > 0, "term list was empty");
        var today = DateOnly.FromDateTime(TermCalendar.Now());
        var term = terms.FirstOrDefault(candidate => IsNeuTerm(candidate) && TermCalendar.Contains(candidate, today))
            ?? terms.FirstOrDefault(IsNeuTerm)
            ?? throw new InvalidOperationException("no 6-digit Northeastern term");
        step($"term {term.Code} {term.Name}");

        var subjectsTask = banner.FetchSubjectsAsync(term.Code, cancellationToken);
        var campusesTask = banner.FetchCampusesAsync(term.Code, cancellationToken);
        await Task.WhenAll(subjectsTask, campusesTask).ConfigureAwait(false);
        var subjects = await subjectsTask.ConfigureAwait(false);
        var campuses = await campusesTask.ConfigureAwait(false);
        Require(subjects.Count > 0, "subject list was empty");
        Require(campuses.Count > 0, "campus list was empty");
        Require(subjects.Any(subject => subject.Code.Length > 0 && subject.Name.Length > 0), "subject list had no names");
        step($"subjects {subjects.Count}");
        step($"campuses {campuses.Count}");

        CourseKey[] wanted =
        [
            new("CS", "2500"),
            new("CS", "1800"),
            new("CS", "2510"),
            new("MATH", "1341"),
            new("ENGW", "1111"),
        ];
        var found = await banner.FetchSectionsAsync(term.Code, wanted, cancellationToken).ConfigureAwait(false);
        CourseKey? chosen = null;
        var pool = new List<BannerSection>();
        foreach (var key in wanted)
        {
            if (!found.TryGetValue(key, out var list) || list.Count == 0) continue;
            chosen = key;
            pool.AddRange(list);
            break;
        }
        if (chosen is null)
        {
            var session = await banner.OpenSearchSessionAsync(term.Code, cancellationToken).ConfigureAwait(false);
            var page = await banner.SearchSectionsAsync(session, "CS", pageSize: 40, cancellationToken: cancellationToken).ConfigureAwait(false);
            var data = page.Data ?? [];
            Require(data.Count > 0, "section search was empty");
            var first = data[0];
            chosen = new CourseKey(first.Subject, first.CourseNumber);
            pool.AddRange(data.Where(section => section.Subject == chosen.Value.Subject && section.CourseNumber == chosen.Value.Number));
            Require(pool.Count > 0, "exact course filter was empty");
        }
        var courseKey = chosen.Value;
        Require(pool.Count > 0, "section search was empty");
        step($"course {courseKey} sections {pool.Count}");

        if (!pool.Any(HasWeeklyMeeting) || !pool.Any(HasRoom))
        {
            var session = await banner.OpenSearchSessionAsync(term.Code, cancellationToken).ConfigureAwait(false);
            var page = await banner.SearchSectionsAsync(session, "CS", pageSize: 40, cancellationToken: cancellationToken).ConfigureAwait(false);
            var seen = pool.Select(section => section.CourseReferenceNumber).ToHashSet(StringComparer.Ordinal);
            foreach (var section in page.Data ?? [])
            {
                if (!seen.Add(section.CourseReferenceNumber)) continue;
                pool.Add(section);
            }
        }
        Require(pool.Any(HasWeeklyMeeting), "no weekly meeting to export");
        Require(pool.Any(HasRoom), "rooms search was empty");

        var named = new List<Instructor>();
        var tried = 0;
        foreach (var section in pool)
        {
            if (named.Count > 0 || tried >= 8) break;
            tried++;
            var instructors = await banner.FetchInstructorsAsync(term.Code, section.CourseReferenceNumber, cancellationToken).ConfigureAwait(false);
            if (instructors.Count == 0) continue;
            BannerClient.ApplyFaculty(section, instructors);
            named.AddRange(instructors);
        }
        Require(named.Count > 0, "faculty lookup was empty for every CRN");
        step($"professor {named[0].DisplayName}");

        var catalog = CatalogBuilder.Merge(null, pool, term, subjects, campuses, null);
        var index = CatalogIndex.Build(catalog);
        var search = SearchEngine.Search(courseKey.ToString(), index);
        Require(search.Results.Count > 0, "search returned nothing for " + courseKey);
        Require(search.Results.Any(result => result.Course.Key == courseKey), "search did not return " + courseKey);
        step($"search {search.Results.Count}");

        var course = index.Course(courseKey) ?? throw new InvalidOperationException("catalog is missing " + courseKey);
        var description = "";
        foreach (var section in pool.Where(section => section.Subject == courseKey.Subject && section.CourseNumber == courseKey.Number).Take(3))
        {
            var detailed = await banner.FetchCourseDetailsAsync(
                term.Code,
                section.CourseReferenceNumber,
                course,
                new BannerParsers.SubjectLookup(subjects),
                cancellationToken).ConfigureAwait(false);
            description = (detailed.Description ?? "").Trim();
            if (description.Length >= 20)
            {
                course = detailed;
                break;
            }
        }
        Require(description.Length >= 20, "course description was empty");
        step($"description {description.Length}");

        var storePath = Path.Combine(directory, "user-data.json");
        var store = new UserDataStore(storePath);
        store.ToggleSaved(courseKey, term.Code);
        store.RecordView(courseKey, term.Code);
        Require(store.IsSaved(courseKey, term.Code), "save did not stick");
        var plan = store.CreatePlan("Self-test", term.Code);
        Require(store.AddCourse(courseKey, plan.Id), "schedule plan did not take the course");

        var courseSections = index.Sections(courseKey).ToList();
        Require(courseSections.Count > 0, "catalog sections were empty");
        var generated = ScheduleGenerator.Generate(
            [new ScheduleCourseInput(courseKey, true, courseSections)],
            new ScheduleFilters());
        Require(generated.Schedules.Count > 0 || generated.TotalFound > 0, "schedule generation returned nothing");
        step($"schedules {generated.Schedules.Count} found {generated.TotalFound}");

        var survey = PreferenceParser.Interpret(PreferenceParser.ExampleSentence);
        Require(survey.Succeeded && survey.ChangedCount > 0, survey.Message ?? "survey parsed nothing");
        var surveyed = ScheduleGenerator.Generate(
            [new ScheduleCourseInput(courseKey, true, courseSections)],
            new ScheduleFilters(),
            survey.Preferences);
        step($"survey {survey.ChangedCount} schedules {surveyed.Schedules.Count}");

        var weekly = courseSections.FirstOrDefault(section => section.WeeklyMeetings.Any(meeting => meeting.Start is int && meeting.End is int))
            ?? index.Catalog.Sections.FirstOrDefault(section => section.WeeklyMeetings.Any(meeting => meeting.Start is int && meeting.End is int));
        if (weekly is not CourseSection meetingSection)
            throw new InvalidOperationException("no weekly meeting to export");
        var ics = IcsWriter.Write(courseKey.ToString(), term, [meetingSection]);
        File.WriteAllText(Path.Combine(directory, "schedule.ics"), ics);
        Require(ics.Contains("BEGIN:VEVENT", StringComparison.Ordinal), "ics export had no event");
        Require(ics.Contains("DTSTART", StringComparison.Ordinal), "ics export had no start");
        Require(System.Text.RegularExpressions.Regex.IsMatch(ics, @"UNTIL=\d{8}T\d{6}Z"), "ics UNTIL is not UTC");
        step("ics " + System.Text.RegularExpressions.Regex.Match(ics, @"UNTIL=\d{8}T\d{6}Z").Value);

        var degree = new DegreePlan
        {
            Name = "Self-test",
            Terms =
            [
                new DegreePlanTerm
                {
                    Season = PlanSeasonInfo.FromTerm(term.Season),
                    Year = term.CalendarYear,
                    Courses = [new DegreePlanCourse(courseKey, course.Title, course.MinCredits > 0 ? course.MinCredits : 4)],
                },
            ],
        };
        var pdfPath = Path.Combine(directory, "degree.pdf");
        File.WriteAllBytes(pdfPath, DegreePlanPdf.Render(degree));
        var imported = DegreePlanImporter.Import(pdfPath);
        var importedKeys = imported.Plan.Terms.SelectMany(item => item.Courses).Select(item => item.Key)
            .Concat(imported.Plan.Completed.Select(item => item.Key));
        Require(importedKeys.Contains(courseKey), "degree audit import is missing " + courseKey);
        step($"degree {courseKey}");

        Require(index.Buildings.Count > 0, "rooms search was empty");
        var moment = RoomAvailability.Moment.From(TermCalendar.Now());
        var rooms = RoomAvailability.Of(index.Buildings, moment);
        Require(rooms.Count > 0 && rooms.Any(building => building.RoomCount > 0), "rooms search was empty");
        step($"rooms {rooms.Sum(building => building.RoomCount)}");

        var directoryEntries = ProfessorDirectoryEntry.Build(index);
        Require(directoryEntries.Count > 0, "professor directory was empty");
        var display = named[0].DisplayName;
        Require(
            directoryEntries.Any(entry =>
                entry.DisplayName.Equals(display, StringComparison.OrdinalIgnoreCase)
                || entry.Id.Equals(named[0].Name, StringComparison.OrdinalIgnoreCase)),
            "professor directory is missing " + display);

        var watched = courseSections[0];
        store.Watch(watched, course.Title, term.Code);
        var preferences = store.Preferences;
        preferences.PreferredCampus = campuses[0].Name;
        store.ReplacePreferences(preferences);

        var reloaded = new UserDataStore(storePath);
        Require(reloaded.IsSaved(courseKey, term.Code), "saved course did not reload");
        Require(reloaded.IsWatching(watched.Crn, term.Code), "watchlist did not reload");
        Require(reloaded.Watchlist.Count > 0, "watchlist was empty");
        var live = new Dictionary<string, IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>>
        {
            [term.Code] = new Dictionary<CourseKey, IReadOnlyList<CourseSection>>
            {
                [courseKey] = courseSections,
            },
        };
        var outcome = WatchlistCheck.Compare(reloaded.Watchlist, live);
        Require(outcome.Items.Count > 0, "watchlist compare returned nothing");
        step($"watch {outcome.Items.Count} alerts {outcome.Alerts.Count}");

        Require(reloaded.Preferences.PreferredCampus == campuses[0].Name, "preferred campus did not persist");
        Require(!string.IsNullOrWhiteSpace(LegalText.UnofficialNotice), "disclaimer was empty");
        Require(!string.IsNullOrWhiteSpace(LegalText.SourceOffer), "AGPL source offer was empty");
        Require(!string.IsNullOrWhiteSpace(LegalText.SearchNeuSummary), "SearchNEU credit was empty");
        step($"campus {campuses[0].Name}");
    }

    private static bool IsNeuTerm(Term term) =>
        term.Code.Length == 6 && term.Code.All(char.IsDigit) && term.College == TermCollege.Neu;

    private static bool HasWeeklyMeeting(BannerSection section) =>
        (section.MeetingsFaculty ?? []).Any(meeting =>
        {
            var time = meeting.MeetingTime;
            return time?.BeginTime is { Length: > 0 } && time.EndTime is { Length: > 0 } && time.DaySet.Raw != 0;
        });

    private static bool HasRoom(BannerSection section) =>
        (section.MeetingsFaculty ?? []).Any(meeting =>
        {
            var time = meeting.MeetingTime;
            if (time is null || string.IsNullOrWhiteSpace(time.Building)) return false;
            var room = time.Room?.Trim();
            return !string.IsNullOrEmpty(room) && !room.Equals("ROOM", StringComparison.OrdinalIgnoreCase);
        });

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
