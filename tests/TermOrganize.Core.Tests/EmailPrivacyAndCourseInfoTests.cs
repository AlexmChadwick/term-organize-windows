// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TermOrganize.Core.Tests;

public class EmailPrivacyAndCourseInfoTests
{
    [Theory]
    [InlineData("a@neu.edué.com")]
    [InlineData("a@neu.edu_evil.com")]
    [InlineData("éa@neu.edu")]
    [InlineData("a@neu.edu@evil.com")]
    [InlineData("a@neu.edu.")]
    [InlineData("a@neu.edu.evil.com")]
    [InlineData("a@evil.com.neu.edu.")]
    [InlineData("a@evil.neu.edu")]
    [InlineData("a@husky.neu.edu")]
    [InlineData("a@neu.edu​")]
    [InlineData("​a@neu.edu")]
    [InlineData("a@ｎeu.edu")]
    [InlineData("a@neu.eduı")]
    [InlineData("mailto:a@neu.edu")]
    [InlineData("\"a b\"@neu.edu")]
    [InlineData("a@@neu.edu")]
    [InlineData("@neu.edu")]
    [InlineData("a@.neu.edu")]
    public void MalformedOrForeignAddressesAreDroppedNotTruncated(string raw)
    {
        Assert.Null(InstructorEmail.Institutional(raw));
        Assert.False(InstructorEmail.IsAllowedAddress(raw));
    }

    [Fact]
    public void AllowedAddressesKeepTheirExactText()
    {
        Assert.Equal("Ada@NEU.edu", InstructorEmail.Institutional("Ada@NEU.edu"));
        Assert.Equal("a.b+c@northeastern.edu", InstructorEmail.Institutional("a.b+c@northeastern.edu"));
        Assert.Equal("a@neu.edu b@northeastern.edu", InstructorEmail.Institutional("a@neu.edu,b@northeastern.edu"));
        Assert.Equal("a@neu.edu b@northeastern.edu", InstructorEmail.Institutional("a@neu.edu; b@northeastern.edu"));
        Assert.Equal("b@neu.edu", InstructorEmail.Institutional("a@husky.neu.edu\nb@neu.edu"));
        Assert.Equal("b@neu.edu", InstructorEmail.Institutional("A@NEU.EDU.EVIL.COM b@neu.edu"));
        Assert.Equal("a@neu.edu", InstructorEmail.Institutional("student@husky.neu.edu a@neu.edu"));
        Assert.True(InstructorEmail.IsAllowedAddress("a@NEU.EDU"));
        Assert.False(InstructorEmail.IsAllowedAddress(null));
        Assert.False(InstructorEmail.IsAllowedAddress(" a@neu.edu"));
    }

    [Fact]
    public void ApplyFacultyNeverCopiesADisallowedAddress()
    {
        var section = new BannerSection { CourseReferenceNumber = "10001", Subject = "CS", CourseNumber = "3000" };
        BannerClient.ApplyFaculty(section, [
            new Instructor { Name = "Good, Prof", Primary = true, Email = "prof@northeastern.edu" },
            new Instructor { Name = "Student, TA", Email = "ta@husky.neu.edu" },
            new Instructor { Name = "Dept, Prof", Email = "p@coe.neu.edu" },
        ]);
        var faculty = section.MeetingsFaculty![0].Faculty!;
        Assert.Equal("prof@northeastern.edu", faculty[0].EmailAddress);
        Assert.Null(faculty[1].EmailAddress);
        Assert.Null(faculty[2].EmailAddress);

        // The same rule applies when a meeting already hosts faculty.
        BannerClient.ApplyFaculty(section, [new Instructor { Name = "Student, TA", Email = "ta@husky.neu.edu" }]);
        Assert.Null(section.MeetingsFaculty![0].Faculty![0].EmailAddress);
    }

    [Fact]
    public void ArchiveReadScrubsDisallowedAddressesFromARawFile()
    {
        var root = Temp();
        var raw = RawCatalog();
        var gz = Path.Combine(root, "term-202630.json.gz");
        File.WriteAllBytes(gz, Gzip(raw));
        var json = Path.Combine(root, "term-202631.json");
        File.WriteAllBytes(json, raw);

        foreach (var path in new[] { gz, json })
        {
            var loaded = CatalogArchive.Read(path);
            var instructors = loaded.Sections[0].Instructors!;
            Assert.Equal("prof@northeastern.edu", instructors[0].Email);
            Assert.Null(instructors[1].Email);
            Assert.Null(instructors[2].Email);
            Assert.Null(instructors[3].Email);
            Assert.Equal("ok@neu.edu", instructors[4].Email);
            Assert.Equal("Student, TA", instructors[1].Name);
        }
    }

    [Fact]
    public void ArchiveWriteAndTheUserCacheNeverPersistADisallowedAddress()
    {
        var catalog = CatalogArchive.Decode(RawCatalog());
        // Put the raw addresses back so Write has to do the scrubbing itself.
        catalog.Sections[0].Instructors![1].Email = "ta@husky.neu.edu";
        catalog.Sections[0].Instructors![2].Email = "p@coe.neu.edu";
        catalog.Sections[0].Instructors![3].Email = "x@ece.northeastern.edu";

        var root = Temp();
        var path = Path.Combine(root, "out", "term-202630.json.gz");
        CatalogArchive.Write(catalog, path);
        AssertNoForeignAddresses(Gunzip(File.ReadAllBytes(path)));

        catalog.Sections[0].Instructors![1].Email = "ta@husky.neu.edu";
        var previous = TermCatalogCache.Root;
        TermCatalogCache.Root = Path.Combine(root, "cache");
        try
        {
            TermCatalogCache.Save(catalog);
            AssertNoForeignAddresses(Gunzip(File.ReadAllBytes(TermCatalogCache.PathFor("202630"))));

            // An older cache file written before the filter existed still loads clean.
            File.WriteAllBytes(TermCatalogCache.PathFor("202630"), Gzip(RawCatalog()));
            var loaded = TermCatalogCache.TryLoad("202630");
            Assert.NotNull(loaded);
            Assert.All(loaded!.Sections[0].Instructors!, instructor =>
                Assert.True(instructor.Email is null || InstructorEmail.IsAllowedAddress(instructor.Email), instructor.Email));
            Assert.Equal("prof@northeastern.edu", loaded.Sections[0].Instructors![0].Email);
        }
        finally
        {
            TermCatalogCache.Root = previous;
        }
    }

    [Theory]
    [InlineData("term-202710.json.gz")]
    [InlineData("term-202730.json.gz")]
    public void BundledSnapshotsShipOnlyExactNortheasternAddresses(string file)
    {
        var path = BundledCatalog.Resolve(file);
        Assert.NotNull(path);
        // Parse the file as shipped, without CatalogArchive.Read's scrub.
        using var document = JsonDocument.Parse(Gunzip(File.ReadAllBytes(path)));
        var seen = 0;
        foreach (var section in document.RootElement.GetProperty("sections").EnumerateArray())
        {
            if (!section.TryGetProperty("instructors", out var instructors)) continue;
            foreach (var instructor in instructors.EnumerateArray())
            {
                if (!instructor.TryGetProperty("email", out var email)) continue;
                var text = email.GetString();
                Assert.Equal(text, InstructorEmail.Institutional(text));
                seen++;
            }
        }
        Assert.True(seen > 100);
    }

    [Fact]
    public void BundledMacCatalogAlsoLoadsClean()
    {
        var path = BundledCatalog.Resolve("term-202710.catalog");
        Assert.NotNull(path);
        var catalog = CatalogArchive.Read(path);
        foreach (var instructor in catalog.Sections.SelectMany(section => section.Instructors ?? []))
            Assert.True(instructor.Email is null || InstructorEmail.IsAllowedAddress(instructor.Email), instructor.Email);
    }

    [Fact]
    public async Task SectionDetailsKeepTitleAndRequisitesWhenOnlyTheDescriptionFails()
    {
        using var client = new BannerClient(new CourseHandler(descriptionStatus: HttpStatusCode.InternalServerError), maxAttempts: 1);
        var course = new Course { Subject = "CS", Number = "3000", Title = "Original title" };
        var updated = await client.FetchCourseDetailsAsync("202710", "99999", course, await Subjects(default));

        Assert.True(updated.DetailsLoaded);
        Assert.False(updated.DescriptionLoaded);
        Assert.Equal("Algorithms and Data", updated.Title);
        Assert.Equal(Requisite.Course(new CourseKey("CS", "2510")), updated.Prereqs);
        Assert.Equal(Requisite.Course(new CourseKey("CS", "3001")), updated.Coreqs);
        Assert.NotNull(updated.Description);
        Assert.Equal("", updated.Description);
    }

    [Fact]
    public async Task SectionDetailsKeepThePreviousDescriptionWhenTheFetchFails()
    {
        using var client = new BannerClient(new CourseHandler(descriptionStatus: HttpStatusCode.InternalServerError), maxAttempts: 1);
        var course = new Course { Subject = "CS", Number = "3000", Title = "Original title", Description = "Earlier description." };
        var updated = await client.FetchCourseDetailsAsync("202710", "99999", course, await Subjects(default));

        Assert.True(updated.DetailsLoaded);
        Assert.False(updated.DescriptionLoaded);
        Assert.Equal("Earlier description.", updated.Description);
        Assert.Equal("Algorithms and Data", updated.Title);
    }

    [Fact]
    public async Task SectionDetailsKeepThePreviousDescriptionWhenTheFetchReturnsBlank()
    {
        using var client = new BannerClient(new CourseHandler(descriptionBody: ""), maxAttempts: 1);
        var course = new Course { Subject = "CS", Number = "3000", Title = "Original title", Description = "Earlier description." };
        var updated = await client.FetchCourseDetailsAsync("202710", "99999", course, await Subjects(default));

        Assert.True(updated.DetailsLoaded);
        Assert.True(updated.DescriptionLoaded);
        Assert.Equal("Earlier description.", updated.Description);
        Assert.Equal("Algorithms and Data", updated.Title);
    }

    [Fact]
    public async Task SectionDetailsKeepThePreviousDescriptionWhenTheFetchReturnsWhitespace()
    {
        using var client = new BannerClient(new CourseHandler(descriptionBody: "<section>   \r\n\t  </section>"), maxAttempts: 1);
        var course = new Course { Subject = "CS", Number = "3000", Title = "Original title", Description = "Earlier description." };
        var updated = await client.FetchCourseDetailsAsync("202710", "99999", course, await Subjects(default));

        Assert.True(updated.DetailsLoaded);
        Assert.True(updated.DescriptionLoaded);
        Assert.Equal("Earlier description.", updated.Description);
        Assert.Equal("Algorithms and Data", updated.Title);
    }

    [Fact]
    public async Task FailedFetchLeavesDescriptionLoadedFalseAndLaterSuccessfulFetchFillsIt()
    {
        using var failingClient = new BannerClient(new CourseHandler(descriptionStatus: HttpStatusCode.InternalServerError), maxAttempts: 1);
        var target = new Course { Subject = "CS", Number = "3000", Title = "Original title", Description = "Borrowed from 202630" };
        var failed = await failingClient.FetchCourseDetailsAsync("202710", "99999", target, await Subjects(default));

        Assert.True(failed.DetailsLoaded);
        Assert.False(failed.DescriptionLoaded);
        Assert.Equal("Borrowed from 202630", failed.Description);
        CatalogBuilder.ApplyCourseDetails(target, failed);
        Assert.True(target.DetailsLoaded);
        Assert.False(target.DescriptionLoaded);
        Assert.Equal("Borrowed from 202630", target.Description);

        using var succeedingClient = new BannerClient(new CourseHandler(descriptionStatus: HttpStatusCode.OK), maxAttempts: 1);
        var filled = await succeedingClient.FetchCourseDetailsAsync("202710", "99999", target, await Subjects(default));

        Assert.True(filled.DetailsLoaded);
        Assert.True(filled.DescriptionLoaded);
        Assert.Equal("Design and analysis of algorithms.", filled.Description);
        CatalogBuilder.ApplyCourseDetails(target, filled);
        Assert.True(target.DetailsLoaded);
        Assert.True(target.DescriptionLoaded);
        Assert.Equal("Design and analysis of algorithms.", target.Description);
    }

    [Fact]
    public async Task FetchCourseDetailsKeepsPreviousDescriptionWhenDescriptionTimesOut()
    {
        using var client = new BannerClient(new CourseHandler(descriptionException: new TaskCanceledException("Timeout")), maxAttempts: 1);
        var course = new Course { Subject = "CS", Number = "3000", Title = "Original title", Description = "Earlier description." };
        var updated = await client.FetchCourseDetailsAsync("202710", "99999", course, await Subjects(default));

        Assert.True(updated.DetailsLoaded);
        Assert.False(updated.DescriptionLoaded);
        Assert.Equal("Earlier description.", updated.Description);
        Assert.Equal("Algorithms and Data", updated.Title);
    }

    private sealed class CaptureListener : System.Diagnostics.TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (message is not null) Messages.Add(message); }
        public override void WriteLine(string? message) { if (message is not null) Messages.Add(message); }
    }

    [Fact]
    public async Task DescriptionFetchFailureIsTracedSoReleaseBuildsKeepTheDiagnostic()
    {
        var listener = new CaptureListener();
        System.Diagnostics.Trace.Listeners.Add(listener);
        try
        {
            using var client = new BannerClient(new CourseHandler(descriptionStatus: HttpStatusCode.InternalServerError), maxAttempts: 1);
            await client.FetchCourseDetailsAsync("202710", "99999", new Course { Subject = "CS", Number = "3000", Title = "T" }, await Subjects(default));
        }
        finally { System.Diagnostics.Trace.Listeners.Remove(listener); }

        Assert.Contains(listener.Messages, message => message.Contains("Failed to fetch course description") && message.Contains("99999"));
    }

    [Fact]
    public async Task DescriptionFailureKeepsDetailsLoadedTrueSoPrereqGatesRecognizeRequisites()
    {
        using var client = new BannerClient(new CourseHandler(descriptionStatus: HttpStatusCode.InternalServerError), maxAttempts: 1);
        var target = new Course { Subject = "CS", Number = "3000", Title = "Original title" };
        var detailed = await client.FetchCourseDetailsAsync("202710", "99999", target, await Subjects(default));
        CatalogBuilder.ApplyCourseDetails(target, detailed);

        Assert.True(target.DetailsLoaded);
        Assert.False(target.DescriptionLoaded);
        Assert.False(target.HasNoPrerequisites);
        Assert.False(target.HasNoCorequisites);

        // Prereq gates depend on DetailsLoaded:
        // 1. Degree Analysis Status sees known requisites (Missing CS 2510, since not taken) rather than Unknown
        var plan = new DegreePlan();
        var catalog = CatalogIndex.Build(new TermCatalog { Term = new Term("202710", "Fall 2026"), Courses = [target] });
        var status = DegreePlanAnalysis.ForCourse(new CourseKey("CS", "3000"), 0, plan, catalog);
        Assert.NotEqual(DegreePlanAnalysis.PrerequisiteStatus.Unknown, status);
        Assert.True(status.IsMissing);

        // 2. DegreePlanCatalogSnapshot IndexContaining finds the index with loaded requisites
        var snapshot = new DegreePlanCatalogSnapshot([catalog], [catalog.Term], "202710");
        Assert.Same(catalog, snapshot.IndexContaining(new CourseKey("CS", "3000")));
    }

    [Fact]
    public async Task FetchCourseDetailsPropagatesCallerCancellation()
    {
        using var client = new BannerClient(new CourseHandler(descriptionStatus: HttpStatusCode.OK), maxAttempts: 1);
        var course = new Course { Subject = "CS", Number = "3000", Title = "Original title", Description = "Earlier description." };
        var subjects = await Subjects(default);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.FetchCourseDetailsAsync("202710", "99999", course, subjects, cts.Token));
    }

    [Fact]
    public async Task FetchCourseDetailsPropagatesCallerCancellationWhenOnlyDescriptionIsCancelled()
    {
        using var cts = new CancellationTokenSource();
        using var client = new BannerClient(new CourseHandler(
            onDescriptionRequest: () => cts.Cancel(),
            cancelOnlyDescription: true), maxAttempts: 1);
        var course = new Course { Subject = "CS", Number = "3000", Title = "Original title", Description = "Earlier description." };
        var subjects = await Subjects(default);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.FetchCourseDetailsAsync("202710", "99999", course, subjects, cts.Token));
    }

    [Fact]
    public async Task CourseInfoLookupKeepsTitleAndRequisitesWhenOnlyTheDescriptionFails()
    {
        using var client = new BannerClient(new CourseHandler(descriptionStatus: HttpStatusCode.InternalServerError), maxAttempts: 1);
        var info = await client.FetchCourseInfoAsync(new CourseKey("CS", "3000"), "202710", Subjects);
        Assert.NotNull(info);
        Assert.Equal("Algorithms and Data", info!.Title);
        Assert.Equal(4.0, info.MinCredits);
        Assert.Equal(Requisite.Course(new CourseKey("CS", "2510")), info.Prerequisites);
        Assert.Equal(Requisite.Course(new CourseKey("CS", "3001")), info.Corequisites);
        Assert.Null(info.Description);
    }

    [Fact]
    public async Task CourseInfoLookupIncludesTheDescriptionWhenItArrives()
    {
        using var client = new BannerClient(new CourseHandler(descriptionStatus: HttpStatusCode.OK), maxAttempts: 1);
        var info = await client.FetchCourseInfoAsync(new CourseKey("CS", "3000"), "202710", Subjects);
        Assert.NotNull(info);
        Assert.Equal("Design and analysis of algorithms.", info!.Description);
        Assert.Equal("Algorithms and Data", info.Title);
    }

    [Fact]
    public async Task CourseInfoLookupStillFailsWhenARequiredRequestFails()
    {
        using var client = new BannerClient(new CourseHandler(descriptionStatus: HttpStatusCode.OK, prerequisitesStatus: HttpStatusCode.InternalServerError), maxAttempts: 1);
        await Assert.ThrowsAnyAsync<Exception>(() => client.FetchCourseInfoAsync(new CourseKey("CS", "3000"), "202710", Subjects));
    }

    [Fact]
    public async Task DownloadPassesLocalKnowledgeIntoTheMergedCatalog()
    {
        var previous = TermCatalogCache.Root;
        TermCatalogCache.Root = Path.Combine(Temp(), "cache");
        try
        {
            using var client = new BannerClient(new CourseHandler(descriptionStatus: HttpStatusCode.OK), maxAttempts: 1);
            var knowledge = new Dictionary<CourseKey, Course>
            {
                [new CourseKey("CS", "3000")] = new Course
                {
                    Subject = "CS",
                    Number = "3000",
                    Title = "Algorithms and Data",
                    Description = "Borrowed from an older term.",
                    Prereqs = Requisite.Course(new CourseKey("CS", "2510")),
                    DetailsLoaded = true,
                },
            };
            var catalog = await TermCatalogCache.DownloadAsync(new Term("202750", "Summer 2027"), client, knowledge: knowledge);
            var course = Assert.Single(catalog.Courses);
            Assert.Equal("Borrowed from an older term.", course.Description);
            Assert.Equal(Requisite.Course(new CourseKey("CS", "2510")), course.Prereqs);
            Assert.True(course.DetailsLoaded);
            Assert.Equal("Borrowed from an older term.", TermCatalogCache.TryLoad("202750")!.Courses[0].Description);
        }
        finally
        {
            TermCatalogCache.Root = previous;
        }
    }

    private static Task<BannerParsers.SubjectLookup> Subjects(CancellationToken _) =>
        Task.FromResult(new BannerParsers.SubjectLookup([new Subject { Code = "CS", Name = "Computer Science" }]));

    /// <summary>A catalog serialized without the archive's scrub, as an older build would have written it.</summary>
    private static byte[] RawCatalog()
    {
        var catalog = new TermCatalog
        {
            Term = new Term("202630", "Spring 2026 Semester"),
            Sections =
            [
                new CourseSection
                {
                    Crn = "12345",
                    Subject = "CS",
                    Number = "3000",
                    SectionNumber = "01",
                    ClassType = "Lecture",
                    Instructors =
                    [
                        new Instructor { Name = "Good, Prof", Primary = true, Email = "prof@northeastern.edu" },
                        new Instructor { Name = "Student, TA", Email = "ta@husky.neu.edu" },
                        new Instructor { Name = "Dept, Prof", Email = "p@coe.neu.edu" },
                        new Instructor { Name = "Far, Prof", Email = "x@ece.northeastern.edu" },
                        new Instructor { Name = "Ok, Prof", Email = "ok@neu.edu" },
                    ],
                },
            ],
        };
        return JsonSerializer.SerializeToUtf8Bytes(catalog, AppJson.Catalog);
    }

    private static byte[] Gzip(byte[] data)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(data);
        return stream.ToArray();
    }

    private static byte[] Gunzip(byte[] data)
    {
        using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static void AssertNoForeignAddresses(byte[] json)
    {
        var text = Encoding.UTF8.GetString(json);
        Assert.DoesNotContain("husky", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coe.neu.edu", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ece.northeastern.edu", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prof@northeastern.edu", text, StringComparison.Ordinal);
    }

    private static string Temp() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nto-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>Answers Banner's course-catalog and search endpoints from memory.</summary>
    private sealed class CourseHandler(
        HttpStatusCode descriptionStatus = HttpStatusCode.OK,
        HttpStatusCode prerequisitesStatus = HttpStatusCode.OK,
        string descriptionBody = "<section>Design and analysis of algorithms.</section>",
        Exception? descriptionException = null,
        Action? onDescriptionRequest = null,
        bool cancelOnlyDescription = false) : HttpMessageHandler
    {
        private int _otherRequestsCompleted;
        private readonly TaskCompletionSource _othersDone = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("getCourseDescription", StringComparison.Ordinal))
            {
                if (cancelOnlyDescription)
                {
                    await _othersDone.Task.ConfigureAwait(false);
                }
                onDescriptionRequest?.Invoke();
                if (cancellationToken.IsCancellationRequested)
                    return await Task.FromCanceled<HttpResponseMessage>(cancellationToken).ConfigureAwait(false);
                if (descriptionException is not null)
                    return await Task.FromException<HttpResponseMessage>(descriptionException).ConfigureAwait(false);
                return Reply(descriptionStatus, descriptionBody);
            }
            if (!cancelOnlyDescription && cancellationToken.IsCancellationRequested)
                return await Task.FromCanceled<HttpResponseMessage>(cancellationToken).ConfigureAwait(false);

            HttpResponseMessage response;
            if (path.EndsWith("/getPrerequisites", StringComparison.Ordinal)
                || path.EndsWith("/getSectionPrerequisites", StringComparison.Ordinal))
                response = Reply(prerequisitesStatus,
                    "<section><table class=\"basePreqTable\"><thead><tr><th>And/Or</th></tr></thead><tbody>"
                    + "<tr><td></td><td></td><td></td><td></td><td>Computer Science</td><td>2510</td><td>Undergraduate</td><td>D-</td><td></td></tr>"
                    + "</tbody></table></section>");
            else if (path.EndsWith("getCorequisites", StringComparison.Ordinal))
                response = Reply(HttpStatusCode.OK,
                    "<section><table class=\"basePreqTable\"><thead><tr><th>Subject</th></tr></thead><tbody>"
                    + "<tr><td>Computer Science</td><td>3001</td><td>Recitation</td></tr></tbody></table></section>");
            else if (path.EndsWith("getCourseMutuallyExclusions", StringComparison.Ordinal))
                response = Reply(HttpStatusCode.OK, "<section>No Mutual Exclusion information available.</section>");
            else if (path.EndsWith("/getCourseCatalogDetails", StringComparison.Ordinal)
                || path.EndsWith("/getSectionCatalogDetails", StringComparison.Ordinal))
                response = Reply(HttpStatusCode.OK,
                    "<section><span class=\"status-bold\">Title:</span>Algorithms and Data<br/>"
                    + "<span class=\"status-bold\">Credit Hours:</span> 4.000<br/></section>");
            else if (path.EndsWith("get_subjectcoursecombo", StringComparison.Ordinal))
                response = Reply(HttpStatusCode.OK, "[{\"code\":\"CS3000\",\"description\":\"Algorithms and Data\"}]");
            else if (path.EndsWith("term/search", StringComparison.Ordinal))
            {
                response = Reply(HttpStatusCode.OK, "{}");
                response.Headers.TryAddWithoutValidation("Set-Cookie", "JSESSIONID=abc123; Path=/; HttpOnly");
            }
            else if (path.EndsWith("searchResults/searchResults", StringComparison.Ordinal))
                response = Reply(HttpStatusCode.OK,
                    "{\"success\":true,\"totalCount\":1,\"data\":[{\"courseReferenceNumber\":\"99999\",\"subject\":\"CS\",\"courseNumber\":\"3000\","
                    + "\"sequenceNumber\":\"01\",\"courseTitle\":\"Algorithms and Data\",\"creditHours\":4}]}");
            else if (path.Contains("get_", StringComparison.Ordinal))
                response = Reply(HttpStatusCode.OK, "[]");
            else
                response = Reply(HttpStatusCode.NotFound, "");

            if (cancelOnlyDescription && Interlocked.Increment(ref _otherRequestsCompleted) == 3)
            {
                _othersDone.TrySetResult();
            }
            return response;
        }

        private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
