// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Portions of this file are derived from SearchNEU
// (https://github.com/sandboxnu/searchneu), packages/scraper/src/generate/endpoints.ts,
// fetch.ts (retry policy), steps/sections.ts (search sessions and cookie pool),
// steps/terms.ts and steps/campuses.ts, Copyright (C) Sandbox at Northeastern
// University and the SearchNEU contributors, licensed under the GNU Affero General
// Public License v3.
// Modified 2026-10-13: ported from Swift to C# (HttpClient) with a smaller session
// pool, plus per-course live refreshes, attribute lookups and course-detail merging.
//
// Talks directly to Northeastern's public Banner 9 class search. All requests are
// read-only lookups of public course information.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TermOrganize.Core;

public sealed class BannerException : Exception
{
    public int? StatusCode { get; }

    private BannerException(string message, int? statusCode = null, Exception? inner = null) : base(message, inner) =>
        StatusCode = statusCode;

    public static BannerException Http(int code) => new(
        code == 429
            ? "Northeastern's registration system is busy. Please try again shortly."
            : $"Northeastern's registration system returned an error (HTTP {code}).",
        code);

    public static BannerException InvalidResponse() =>
        new("Received an unexpected response from Northeastern's registration system.");

    public static BannerException Decoding(string detail) =>
        new($"Couldn't read the response from Northeastern's registration system. {detail}");

    public static BannerException NoSession() =>
        new("Couldn't start a search session with Northeastern's registration system.");
}

/// <summary>A Banner search session. Section searches require a cookie bound to a term.</summary>
public sealed class BannerSearchSession
{
    public BannerSearchSession(string term, string cookieHeader)
    {
        Term = term;
        CookieHeader = cookieHeader;
    }

    public string Term { get; }
    public string CookieHeader { get; }
}

/// <summary>Read-only client for Northeastern's public Banner 9 class search.</summary>
public sealed class BannerClient : IDisposable
{
    public static BannerClient Shared { get; } = new();

    public static readonly Uri BaseUri = new("https://nubanner.neu.edu/StudentRegistrationSsb/ssb/");

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly HttpClient _http;
    private readonly int _maxAttempts;
    private readonly TimeSpan _attemptTimeout;

    public BannerClient(TimeSpan? timeout = null, int maxAttempts = 4)
    {
        var requestTimeout = timeout ?? TimeSpan.FromSeconds(60);
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = 8,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        _http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = requestTimeout * 4,
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/html;q=0.9, */*;q=0.8");
        _attemptTimeout = requestTimeout;
        _maxAttempts = Math.Max(1, maxAttempts);
    }

    /// <summary>Most recent terms first.</summary>
    public async Task<List<Term>> FetchTermsAsync(int max = 40, string search = "", CancellationToken cancellationToken = default)
    {
        var url = Endpoint("classSearch/getTerms", new Dictionary<string, string>
        {
            ["offset"] = "1",
            ["max"] = max.ToString(CultureInfo.InvariantCulture),
            ["searchTerm"] = search,
        });
        var terms = await DecodeAsync<List<BannerTerm>>(url, cancellationToken).ConfigureAwait(false);
        return terms.Select(term => new Term(term.Code, HtmlText.DecodeEntities(term.Description))).ToList();
    }

    public async Task<List<Subject>> FetchSubjectsAsync(string term, CancellationToken cancellationToken = default)
    {
        var items = await DecodeAsync<List<BannerCodeDescription>>(ReferenceUrl("classSearch/get_subject", term, "900"), cancellationToken).ConfigureAwait(false);
        return items.Select(item => new Subject { Code = item.Code, Name = HtmlText.DecodeEntities(item.Description).Trim() }).ToList();
    }

    public async Task<List<Campus>> FetchCampusesAsync(string term, CancellationToken cancellationToken = default)
    {
        var items = await DecodeAsync<List<BannerCodeDescription>>(ReferenceUrl("classSearch/get_campus", term, "200"), cancellationToken).ConfigureAwait(false);
        return items.Select(item => new Campus { Code = item.Code, Name = HtmlText.DecodeEntities(item.Description).Trim() }).ToList();
    }

    public async Task<List<AttributeInfo>> FetchAttributesAsync(string term, CancellationToken cancellationToken = default)
    {
        var items = await DecodeAsync<List<BannerCodeDescription>>(ReferenceUrl("classSearch/get_attribute", term, "500"), cancellationToken).ConfigureAwait(false);
        return items.Select(item => new AttributeInfo { Code = item.Code, Name = HtmlText.DecodeEntities(item.Description).Trim() }).ToList();
    }

    /// <summary>Starts a search session for a term (POST term/search), returning its cookie.</summary>
    public async Task<BannerSearchSession> OpenSearchSessionAsync(string term, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint("term/search", new Dictionary<string, string> { ["mode"] = "search" }));
        request.Content = FormContent(new Dictionary<string, string>
        {
            ["term"] = term,
            ["studyPath"] = "",
            ["studyPathText"] = "",
            ["startDatepicker"] = "",
            ["endDatepicker"] = "",
        });
        using var response = await PerformAsync(request, cancellationToken).ConfigureAwait(false);
        var cookies = ReadCookies(response);
        if (cookies.Count == 0) throw BannerException.NoSession();
        return new BannerSearchSession(term, string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}")));
    }

    /// <summary>Clears the search criteria of a session so it can be reused.</summary>
    public async Task ResetSearchAsync(BannerSearchSession session, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint("classSearch/resetDataForm"));
        request.Headers.TryAddWithoutValidation("Cookie", session.CookieHeader);
        using var response = await PerformAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One page of section search results.</summary>
    public async Task<BannerSectionPage> SearchSectionsAsync(
        BannerSearchSession session,
        string? subject = null,
        string? courseNumber = null,
        int offset = 0,
        int pageSize = 500,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string>
        {
            ["txt_term"] = session.Term,
            ["pageOffset"] = offset.ToString(CultureInfo.InvariantCulture),
            ["pageMaxSize"] = pageSize.ToString(CultureInfo.InvariantCulture),
            ["sortColumn"] = "subjectDescription",
            ["sortDirection"] = "asc",
        };
        if (subject is not null) query["txt_subject"] = subject;
        if (courseNumber is not null) query["txt_courseNumber"] = courseNumber;
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint("searchResults/searchResults", query));
        request.Headers.TryAddWithoutValidation("Cookie", session.CookieHeader);
        return await DecodeAsync<BannerSectionPage>(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Every section in a term. Pages are fetched with independent sessions
    /// (SearchNEU uses a pool of cookies the same way), one request in flight per cookie.
    /// </summary>
    public async Task<List<BannerSection>> FetchAllSectionsAsync(
        string term,
        int parallelism = 4,
        Action<int, int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 500;
        var first = await OpenSearchSessionAsync(term, cancellationToken).ConfigureAwait(false);
        var probe = await SearchSectionsAsync(first, offset: 0, pageSize: 1, cancellationToken: cancellationToken).ConfigureAwait(false);
        var total = probe.TotalCount;
        if (total <= 0) return [];
        var pageCount = (int)Math.Ceiling(total / (double)pageSize);
        progress?.Invoke(0, total);

        var sessions = new List<BannerSearchSession> { first };
        var sessionCount = Math.Max(1, Math.Min(parallelism, pageCount));
        for (var i = 1; i < sessionCount; i++)
            sessions.Add(await OpenSearchSessionAsync(term, cancellationToken).ConfigureAwait(false));

        var fetched = 0;
        var nextPage = 0;
        var pages = new BannerSection[pageCount][];
        var workers = sessions.Select(session => Task.Run(async () =>
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = Interlocked.Increment(ref nextPage) - 1;
                if (page >= pageCount) return;
                var result = await SearchSectionsAsync(session, offset: page * pageSize, pageSize: pageSize, cancellationToken: cancellationToken).ConfigureAwait(false);
                var data = result.Data ?? [];
                pages[page] = data.ToArray();
                var count = Interlocked.Add(ref fetched, data.Count);
                progress?.Invoke(Math.Min(count, total), total);
            }
        }, cancellationToken)).ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);

        var all = new List<BannerSection>(total);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; page < pageCount; page++)
        {
            foreach (var section in pages[page] ?? [])
                if (seen.Add(section.CourseReferenceNumber)) all.Add(section);
        }
        return all;
    }

    /// <summary>
    /// Live sections for specific courses, using one session and resetting the search between courses.
    /// Banner treats the course number as a prefix; only exact matches are kept.
    /// </summary>
    public async Task<Dictionary<CourseKey, List<BannerSection>>> FetchSectionsAsync(
        string term,
        IReadOnlyList<CourseKey> courses,
        CancellationToken cancellationToken = default)
    {
        var output = new Dictionary<CourseKey, List<BannerSection>>();
        if (courses.Count == 0) return output;
        var session = await OpenSearchSessionAsync(term, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < courses.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i > 0) await ResetSearchAsync(session, cancellationToken).ConfigureAwait(false);
            var key = courses[i];
            var page = await SearchSectionsAsync(session, key.Subject, key.Number, pageSize: 500, cancellationToken: cancellationToken).ConfigureAwait(false);
            output[key] = (page.Data ?? []).Where(section => section.Subject == key.Subject && section.CourseNumber == key.Number).ToList();
        }
        return output;
    }

    public async Task<List<Instructor>> FetchInstructorsAsync(string term, string crn, CancellationToken cancellationToken = default)
    {
        var url = Endpoint("searchResults/getFacultyMeetingTimes", new Dictionary<string, string>
        {
            ["term"] = term,
            ["courseReferenceNumber"] = crn,
        });
        var result = await DecodeAsync<BannerFacultyMeetingTimes>(url, cancellationToken).ConfigureAwait(false);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var instructors = new List<Instructor>();
        foreach (var meeting in result.Fmt ?? [])
        {
            foreach (var faculty in meeting.Faculty ?? [])
            {
                var instructor = faculty.ToInstructor();
                if (instructor is null || !seen.Add(instructor.Name)) continue;
                instructors.Add(instructor);
            }
        }
        return instructors.OrderByDescending(instructor => instructor.Primary).ToList();
    }

    /// <summary>
    /// Banner search results omit faculty. This fills up to <paramref name="maxSections"/>
    /// sections from <see cref="FetchInstructorsAsync"/> so a course refresh can show names.
    /// A faculty miss leaves that section unchanged. The count is how many gained a name.
    /// </summary>
    public async Task<int> AttachFacultyAsync(
        string term,
        IList<BannerSection> sections,
        int maxSections = 12,
        CancellationToken cancellationToken = default)
    {
        var filled = 0;
        var tried = 0;
        foreach (var section in sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasFaculty(section)) continue;
            if (tried >= maxSections) break;
            tried++;
            var instructors = await FetchInstructorsAsync(term, section.CourseReferenceNumber, cancellationToken).ConfigureAwait(false);
            if (instructors.Count == 0) continue;
            ApplyFaculty(section, instructors);
            filled++;
        }
        return filled;
    }

    /// <summary>Writes instructors onto a section so <see cref="CatalogBuilder.MakeSections"/> keeps them.</summary>
    public static void ApplyFaculty(BannerSection section, IReadOnlyList<Instructor> instructors)
    {
        if (instructors.Count == 0) return;
        var faculty = instructors.Select(instructor => new BannerFaculty
        {
            DisplayName = instructor.Name,
            EmailAddress = instructor.Email,
            PrimaryIndicator = instructor.Primary,
        }).ToList();
        section.MeetingsFaculty ??= [];
        var host = section.MeetingsFaculty.FirstOrDefault();
        if (host is null)
        {
            section.MeetingsFaculty.Add(new BannerMeetingFaculty
            {
                CourseReferenceNumber = section.CourseReferenceNumber,
                Faculty = faculty,
            });
            return;
        }
        host.Faculty = faculty;
    }

    private static bool HasFaculty(BannerSection section) =>
        (section.MeetingsFaculty ?? []).Any(meeting => (meeting.Faculty ?? []).Any(faculty => faculty.ToInstructor() is not null));

    public Task<string> FetchDescriptionHtmlAsync(string term, string crn, CancellationToken cancellationToken = default) =>
        PostFormAsync("searchResults/getCourseDescription", term, crn, cancellationToken);

    public Task<string> FetchPrerequisitesHtmlAsync(string term, string crn, CancellationToken cancellationToken = default) =>
        PostFormAsync("searchResults/getSectionPrerequisites", term, crn, cancellationToken);

    public Task<string> FetchRestrictionsHtmlAsync(string term, string crn, CancellationToken cancellationToken = default) =>
        PostFormAsync("searchResults/getRestrictions", term, crn, cancellationToken);

    public Task<string> FetchCorequisitesHtmlAsync(string term, string crn, CancellationToken cancellationToken = default) =>
        PostFormAsync("searchResults/getCorequisites", term, crn, cancellationToken);

    public Task<string> FetchCatalogDetailsHtmlAsync(string term, string crn, CancellationToken cancellationToken = default) =>
        PostFormAsync("searchResults/getSectionCatalogDetails", term, crn, cancellationToken);

    /// <summary>Fetches description, requisites and catalog details for a course using one of its sections.</summary>
    public async Task<Course> FetchCourseDetailsAsync(
        string term,
        string crn,
        Course course,
        BannerParsers.SubjectLookup subjects,
        CancellationToken cancellationToken = default)
    {
        var descriptionTask = FetchDescriptionHtmlAsync(term, crn, cancellationToken);
        var prereqTask = FetchPrerequisitesHtmlAsync(term, crn, cancellationToken);
        var coreqTask = FetchCorequisitesHtmlAsync(term, crn, cancellationToken);
        var detailsTask = FetchCatalogDetailsHtmlAsync(term, crn, cancellationToken);
        await Task.WhenAll(descriptionTask, prereqTask, coreqTask, detailsTask).ConfigureAwait(false);
        var updated = Copy(course);
        updated.Description = BannerParsers.Description(descriptionTask.Result);
        updated.Prereqs = BannerParsers.Prerequisites(prereqTask.Result, subjects);
        updated.Coreqs = BannerParsers.Corequisites(coreqTask.Result, subjects);
        var details = BannerParsers.CatalogDetailsFrom(detailsTask.Result);
        if (details.Title.NilIfBlank() is string title && !course.SpecialTopics) updated.Title = title;
        updated.College = details.College ?? updated.College;
        updated.Department = details.Department ?? updated.Department;
        updated.DetailsLoaded = true;
        return updated;
    }

    /// <summary>Whether the term's course catalog lists the course. The typeahead matches by prefix, so the code is compared exactly.</summary>
    public async Task<bool> CatalogListsAsync(CourseKey course, string term, CancellationToken cancellationToken = default)
    {
        var url = Endpoint("courseSearch/get_subjectcoursecombo", new Dictionary<string, string>
        {
            ["searchTerm"] = $"{course.Subject} {course.Number}",
            ["term"] = term,
            ["offset"] = "1",
            ["max"] = "10",
        });
        var items = await DecodeAsync<List<BannerCodeDescription>>(url, cancellationToken).ConfigureAwait(false);
        return BannerParsers.CatalogCodes(items, course);
    }

    public Task<string> FetchCourseCatalogDetailsHtmlAsync(string term, CourseKey course, CancellationToken cancellationToken = default) =>
        PostCourseFormAsync("courseSearchResults/getCourseCatalogDetails", term, course, cancellationToken);

    public Task<string> FetchCoursePrerequisitesHtmlAsync(string term, CourseKey course, CancellationToken cancellationToken = default) =>
        PostCourseFormAsync("courseSearchResults/getPrerequisites", term, course, cancellationToken);

    public Task<string> FetchCourseCorequisitesHtmlAsync(string term, CourseKey course, CancellationToken cancellationToken = default) =>
        PostCourseFormAsync("courseSearchResults/getCorequisites", term, course, cancellationToken);

    public Task<string> FetchCourseMutualExclusionsHtmlAsync(string term, CourseKey course, CancellationToken cancellationToken = default) =>
        PostCourseFormAsync("courseSearchResults/getCourseMutuallyExclusions", term, course, cancellationToken);

    public Task<string> FetchCrossListedSectionsHtmlAsync(string term, string crn, CancellationToken cancellationToken = default) =>
        PostFormAsync("searchResults/getXlstSections", term, crn, cancellationToken);

    public async Task<List<BannerParsers.CrossListedSection>> FetchCrossListedSectionsAsync(
        string term,
        string crn,
        BannerParsers.SubjectLookup subjects,
        CancellationToken cancellationToken = default) =>
        BannerParsers.CrossListedSections(await FetchCrossListedSectionsHtmlAsync(term, crn, cancellationToken).ConfigureAwait(false), subjects);

    /// <summary>
    /// A course's catalog entry as of the term. Null when that term's catalog does not list the course.
    /// One request checks, then four run together when it does.
    /// </summary>
    public async Task<CourseInfo?> FetchCourseInfoAsync(
        CourseKey course,
        string term,
        Func<CancellationToken, Task<BannerParsers.SubjectLookup>> subjects,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CatalogListsAsync(course, term, cancellationToken).ConfigureAwait(false)) return null;
        var detailsTask = FetchCourseCatalogDetailsHtmlAsync(term, course, cancellationToken);
        var prerequisitesTask = FetchCoursePrerequisitesHtmlAsync(term, course, cancellationToken);
        var corequisitesTask = FetchCourseCorequisitesHtmlAsync(term, course, cancellationToken);
        var exclusionsTask = FetchCourseMutualExclusionsHtmlAsync(term, course, cancellationToken);
        var lookupTask = subjects(cancellationToken);
        await Task.WhenAll(detailsTask, prerequisitesTask, corequisitesTask, exclusionsTask, lookupTask).ConfigureAwait(false);
        var details = BannerParsers.CatalogDetailsFrom(detailsTask.Result);
        var lookup = lookupTask.Result;
        return new CourseInfo(
            course,
            details.Title ?? "",
            details.MinCredits,
            details.MaxCredits,
            BannerParsers.Prerequisites(prerequisitesTask.Result, lookup),
            BannerParsers.Corequisites(corequisitesTask.Result, lookup),
            BannerParsers.MutualExclusions(exclusionsTask.Result, lookup),
            term,
            null,
            now ?? DateTimeOffset.UtcNow);
    }

    /// <summary>Which of the courses had at least one section in the term.</summary>
    public async Task<HashSet<CourseKey>> FetchOfferedCoursesAsync(string term, IReadOnlyList<CourseKey> courses, CancellationToken cancellationToken = default)
    {
        var offered = new HashSet<CourseKey>();
        if (courses.Count == 0) return offered;
        var session = await OpenSearchSessionAsync(term, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < courses.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i > 0) await ResetSearchAsync(session, cancellationToken).ConfigureAwait(false);
            var key = courses[i];
            var page = await SearchSectionsAsync(session, key.Subject, key.Number, pageSize: 20, cancellationToken: cancellationToken).ConfigureAwait(false);
            if ((page.Data ?? []).Any(section => section.Subject == key.Subject && section.CourseNumber == key.Number))
                offered.Add(key);
        }
        return offered;
    }

    /// <summary>Public catalog page for a course on Northeastern's registration site.</summary>
    public static Uri? CatalogPageUrl(string term, CourseKey course)
    {
        var query = string.Join("&", new[]
        {
            ("cat_term_in", term),
            ("subj_code_in", course.Subject),
            ("crse_numb_in", course.Number),
        }.Select(pair => $"{Uri.EscapeDataString(pair.Item1)}={Uri.EscapeDataString(pair.Item2)}"));
        return new Uri($"https://bnrordsp.neu.edu/ssb-prod/bwckctlg.p_disp_course_detail?{query}");
    }

    /// <summary>Runs work for each item with at most <paramref name="limit"/> tasks in flight.</summary>
    public static async Task ForEachConcurrentlyAsync<T>(IReadOnlyList<T> items, int limit, Func<T, CancellationToken, Task> body, CancellationToken cancellationToken = default)
    {
        var next = 0;
        var workers = Enumerable.Range(0, Math.Max(1, Math.Min(limit, Math.Max(1, items.Count)))).Select(_ => Task.Run(async () =>
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = Interlocked.Increment(ref next) - 1;
                if (index >= items.Count) return;
                await body(items[index], cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken));
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();

    private Uri ReferenceUrl(string path, string term, string max) =>
        Endpoint(path, new Dictionary<string, string>
        {
            ["searchTerm"] = "",
            ["term"] = term,
            ["offset"] = "1",
            ["max"] = max,
        });

    private static Uri Endpoint(string path, IReadOnlyDictionary<string, string>? query = null)
    {
        var root = BaseUri.AbsoluteUri.TrimEnd('/');
        var url = root + "/" + path.TrimStart('/');
        if (query is { Count: > 0 })
        {
            var text = string.Join("&", query.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
            url += "?" + text;
        }
        return new Uri(url);
    }

    private static ByteArrayContent FormContent(IReadOnlyDictionary<string, string> fields)
    {
        var body = string.Join("&", fields.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={EncodeForm(pair.Value)}"));
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded; charset=UTF-8");
        return content;
    }

    private static string EncodeForm(string value)
    {
        // RFC 3986 unreserved characters, matching the Swift form encoder.
        return Uri.EscapeDataString(value);
    }

    private Task<string> PostFormAsync(string path, string term, string crn, CancellationToken cancellationToken) =>
        PostFormAsync(path, new Dictionary<string, string> { ["term"] = term, ["courseReferenceNumber"] = crn }, cancellationToken);

    private Task<string> PostCourseFormAsync(string path, string term, CourseKey course, CancellationToken cancellationToken) =>
        PostFormAsync(path, new Dictionary<string, string>
        {
            ["term"] = term,
            ["subjectCode"] = course.Subject,
            ["courseNumber"] = course.Number,
        }, cancellationToken);

    private async Task<string> PostFormAsync(string path, Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(path));
        request.Content = FormContent(fields);
        using var response = await PerformAsync(request, cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<T> DecodeAsync<T>(Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await DecodeAsync<T>(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> DecodeAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await PerformAsync(request, cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var value = JsonSerializer.Deserialize<T>(bytes, Json);
            if (value is null) throw BannerException.Decoding("Empty JSON.");
            return value;
        }
        catch (BannerException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var detail = ex.Message;
            if (detail.Length > 200) detail = detail[..200];
            throw BannerException.Decoding(detail);
        }
    }

    private async Task<HttpResponseMessage> PerformAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var delay = TimeSpan.FromSeconds(1);
        Exception last = BannerException.InvalidResponse();
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var attemptRequest = CopyRequest(request, body);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_attemptTimeout);
            try
            {
                var response = await _http.SendAsync(attemptRequest, timeout.Token).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 200 and < 300) return response;
                var code = (int)response.StatusCode;
                response.Dispose();
                last = BannerException.Http(code);
                if (code != 429 && code < 500) throw last;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                last = ex;
            }
            catch (HttpRequestException ex)
            {
                last = ex;
            }
            catch (BannerException ex) when (ex.StatusCode is 429 or >= 500)
            {
                last = ex;
            }
            if (attempt < _maxAttempts)
            {
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * 500);
                await Task.Delay(delay + jitter, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 20));
            }
        }
        throw last;
    }

    private static HttpRequestMessage CopyRequest(HttpRequestMessage request, byte[]? body)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (body is not null)
        {
            var content = new ByteArrayContent(body);
            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            copy.Content = content;
        }
        return copy;
    }

    private static List<(string Name, string Value)> ReadCookies(HttpResponseMessage response)
    {
        var cookies = new List<(string Name, string Value)>();
        IEnumerable<string> values = [];
        if (response.Headers.TryGetValues("Set-Cookie", out var headerValues))
            values = headerValues;
        else if (response.Content?.Headers.TryGetValues("Set-Cookie", out var contentValues) == true)
            values = contentValues;
        foreach (var header in values)
        {
            var pair = header.Split(';', 2)[0];
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var name = pair[..eq].Trim();
            var value = pair[(eq + 1)..].Trim();
            if (name.Length == 0) continue;
            cookies.Add((name, value));
        }
        return cookies;
    }

    private static Course Copy(Course course) => new()
    {
        Subject = course.Subject,
        Number = course.Number,
        Title = course.Title,
        Description = course.Description,
        MinCredits = course.MinCredits,
        MaxCredits = course.MaxCredits,
        DefaultCredits = course.DefaultCredits,
        Attributes = course.Attributes.ToList(),
        Prereqs = course.Prereqs,
        Coreqs = course.Coreqs,
        Postreqs = course.Postreqs.ToList(),
        SpecialTopics = course.SpecialTopics,
        College = course.College,
        Department = course.Department,
        DetailsLoaded = course.DetailsLoaded,
    };
}
