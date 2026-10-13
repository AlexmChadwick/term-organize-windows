// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Net;
using System.Text;

namespace TermOrganize.Core.Tests;

public class ParityGapTests
{
    [Fact]
    public async Task TermListPagesUntilAShortPageAndSkipsDuplicates()
    {
        var handler = new PageHandler();
        using var http = new HttpClient(handler);
        var terms = await BannerTermList.FetchAsync(http);
        Assert.Equal(2, handler.Requests);
        Assert.Equal(BannerTermList.PageSize + 1, terms.Count);
        Assert.Equal("202710", terms[0].Code);
        Assert.Equal("202630", terms[1].Code);
        Assert.Equal("202610", terms[^1].Code);
    }

    [Fact]
    public void CatalogCacheRoundTripsADownloadedTerm()
    {
        var root = Temp();
        var previous = TermCatalogCache.Root;
        TermCatalogCache.Root = root;
        try
        {
            var catalog = new TermCatalog
            {
                Term = new Term("202630", "Spring 2026 Semester"),
                Courses = [new Course { Subject = "CS", Number = "3000", Title = "Algorithms and Data", MinCredits = 4, MaxCredits = 4 }],
                Sections =
                [
                    new CourseSection
                    {
                        Crn = "12345",
                        Subject = "CS",
                        Number = "3000",
                        SectionNumber = "01",
                        ClassType = "Lecture",
                        SeatCapacity = 40,
                        SeatsRemaining = 3,
                    },
                ],
            };
            TermCatalogCache.Save(catalog);
            var loaded = TermCatalogCache.TryLoad("202630");
            Assert.NotNull(loaded);
            Assert.Equal("Algorithms and Data", loaded!.Courses[0].Title);
            Assert.Equal(3, loaded.Sections[0].SeatsRemaining);
        }
        finally
        {
            TermCatalogCache.Root = previous;
        }
    }

    [Fact]
    public void OfferingHistoryFromCatalogsAndTheCache()
    {
        var fall = Catalog("202710", "CS", "3000");
        var spring = Catalog("202630", "CS", "3500");
        var history = OfferingHistoryBuilder.FromCatalogs([fall, spring], [new CourseKey("CS", "3000"), new CourseKey("CS", "3500")]);
        Assert.True(history.IsLikelyOffered(new CourseKey("CS", "3000"), PlanSeason.Fall));
        Assert.False(history.IsLikelyOffered(new CourseKey("CS", "3000"), PlanSeason.Spring));
        Assert.Contains("Fall", history.Summary(new CourseKey("CS", "3000"), PlanSeason.Fall), StringComparison.Ordinal);

        var path = Path.Combine(Temp(), "history.json");
        OfferingHistoryBuilder.Save(path, history);
        var loaded = OfferingHistoryBuilder.TryLoad(path, [new CourseKey("CS", "3000"), new CourseKey("CS", "3500")]);
        Assert.NotNull(loaded);
        Assert.True(loaded!.IsLikelyOffered(new CourseKey("CS", "3500"), PlanSeason.Spring));
        Assert.Null(OfferingHistoryBuilder.TryLoad(path, [new CourseKey("CS", "9999")]));
    }

    [Fact]
    public async Task OfferingHistoryFetchRecordsTheTermsThatListedTheCourse()
    {
        var terms = new[] { new Term("202710", "Fall 2026"), new Term("202630", "Spring 2026") };
        var keys = new[] { new CourseKey("CS", "3000") };
        var history = await OfferingHistoryBuilder.FetchAsync(terms, keys, (code, _, _) =>
            Task.FromResult<IReadOnlySet<CourseKey>>(code == "202710" ? keys.ToHashSet() : new HashSet<CourseKey>()));
        Assert.Equal("202710", Assert.Single(history.OfferedTerms(keys[0])));
        Assert.False(history.IsLikelyOffered(keys[0], PlanSeason.Spring));
    }

    [Fact]
    public void CalendarDraftStartsOnAMeetingDayInsideTheTerm()
    {
        var section = new CourseSection
        {
            Crn = "40404",
            Subject = "CS",
            Number = "3000",
            SectionNumber = "02",
            ClassType = "Lecture",
            Meetings =
            [
                new Meeting
                {
                    Days = DaySet.Monday.Union(DaySet.Wednesday),
                    Start = 10 * 60,
                    End = 11 * 60 + 40,
                    Building = "Snell Engineering",
                    Room = "108",
                },
            ],
        };
        var draft = Assert.Single(CalendarEventDrafts.FromSections(new Term("202710", "Fall 2026 Semester"), [section], _ => "Algorithms and Data"));
        Assert.Equal("CS 3000 Algorithms and Data", draft.Subject);
        Assert.Equal(TimeSpan.FromMinutes(100), draft.Duration);
        Assert.Equal(DayOfWeek.Wednesday, draft.Start.DayOfWeek);
        Assert.Equal(10, draft.Start.Hour);
        Assert.Contains("Snell", draft.Location, StringComparison.Ordinal);
        Assert.True(draft.Until > draft.Start);
    }

    [Fact]
    public async Task SeatCheckStoresTheNewCountAndRaisesAnAlert()
    {
        var path = Path.Combine(Temp(), "user-data.json");
        var store = new UserDataStore(path);
        store.Watch(new CourseSection
        {
            Crn = "55555",
            Subject = "CS",
            Number = "3000",
            SectionNumber = "01",
            ClassType = "Lecture",
            SeatCapacity = 20,
            SeatsRemaining = 0,
        }, "Algorithms and Data", "202710");

        var outcome = await SeatCheckService.RunAsync(store, (_, _, _) =>
            Task.FromResult<IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>>(new Dictionary<CourseKey, IReadOnlyList<CourseSection>>
            {
                [new CourseKey("CS", "3000")] =
                [
                    new CourseSection
                    {
                        Crn = "55555",
                        Subject = "CS",
                        Number = "3000",
                        SectionNumber = "01",
                        ClassType = "Lecture",
                        SeatCapacity = 20,
                        SeatsRemaining = 2,
                    },
                ],
            }));

        var alert = Assert.Single(outcome.Alerts);
        Assert.Equal(WatchlistAlert.Kind.Seats, alert.AlertKind);
        Assert.Equal(2, store.Watchlist[0].LastSeatsRemaining);

        var log = Path.Combine(Temp(), "alerts.txt");
        SeatAlertLog.Append(outcome.Alerts, log);
        var pending = SeatAlertLog.Take(log);
        Assert.Contains("Seats open", pending[0].Title, StringComparison.Ordinal);
        Assert.Empty(SeatAlertLog.Take(log));
    }

    [Fact]
    public void ProgramRequirementsCacheRoundTrips()
    {
        var root = Temp();
        var previous = ProgramCatalogClient.Root;
        ProgramCatalogClient.Root = root;
        try
        {
            var requirements = new ProgramRequirements
            {
                Year = new CatalogYear(2025),
                Program = new CatalogProgram("Computer Science, BSCS", "undergraduate/computer-information-science/computer-science/bscs/"),
                TotalHours = 134,
                FetchedAt = DateTimeOffset.UnixEpoch,
                Sections =
                [
                    new RequirementSection
                    {
                        Id = "major",
                        Title = "Computer Science Requirements",
                        Groups =
                        [
                            new RequirementGroup
                            {
                                Id = "required",
                                Title = "Required Courses",
                                Rules =
                                [
                                    new RequirementRule
                                    {
                                        Id = "major/cs-3000",
                                        Label = "CS 3000",
                                        Hours = 4,
                                        Kind = new RequirementKind.Required(new CourseOption([[new CourseKey("CS", "3000")]])),
                                    },
                                ],
                            },
                        ],
                    },
                ],
            };
            ProgramCatalogClient.SaveRequirements(requirements);
            var loaded = ProgramCatalogClient.LoadRequirements(requirements.Year, requirements.Program.Path);
            Assert.NotNull(loaded);
            Assert.Equal(134, loaded!.TotalHours);
            Assert.Equal("CS 3000", loaded.Rule("major/cs-3000")?.DisplayLabel);
            var audit = DegreeAudit.Run(new DegreePlan
            {
                Completed = [new DegreePlanCourse(new CourseKey("CS", "3000"), "Algorithms and Data", 4)],
            }, loaded);
            Assert.Equal(DegreeAudit.Status.Complete, audit.Result("major/cs-3000")?.Status);
        }
        finally
        {
            ProgramCatalogClient.Root = previous;
        }
    }

    private static TermCatalog Catalog(string code, string subject, string number) => new()
    {
        Term = new Term(code, code),
        Sections = [new CourseSection { Crn = code, Subject = subject, Number = number, SectionNumber = "01" }],
    };

    private static string Temp() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nto-" + Guid.NewGuid().ToString("N"))).FullName;

    private sealed class PageHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var offset = int.Parse(Query(request, "offset") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
            string json;
            if (offset == 1)
            {
                var rows = new List<string> { Row("202710", "Fall 2026 Semester"), Row("202630", "Spring 2026 Semester") };
                for (var i = 2; i < BannerTermList.PageSize; i++) rows.Add(Row((300000 + i).ToString(), "Pad"));
                json = "[" + string.Join(",", rows) + "]";
            }
            else if (offset == BannerTermList.PageSize + 1)
            {
                json = "[" + Row("202630", "Spring 2026 Semester") + "," + Row("202610", "Fall 2025 Semester") + "]";
            }
            else json = "[]";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }

        private static string Row(string code, string description) =>
            "{\"code\":\"" + code + "\",\"description\":\"" + description + "\"}";

        private static string? Query(HttpRequestMessage request, string key)
        {
            var query = request.RequestUri?.Query.TrimStart('?') ?? "";
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq > 0 && part[..eq] == key) return part[(eq + 1)..];
            }
            return null;
        }
    }
}
