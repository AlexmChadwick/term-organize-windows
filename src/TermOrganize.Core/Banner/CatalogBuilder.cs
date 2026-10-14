// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Portions of this file are derived from SearchNEU
// (https://github.com/sandboxnu/searchneu), packages/scraper/src/generate/marshall.ts,
// Copyright (C) Sandbox at Northeastern University and the SearchNEU contributors,
// licensed under the GNU Affero General Public License v3.
// Modified 2026-10-13: ported from Swift to C#, including the merge used for on-device refreshes.

namespace TermOrganize.Core;

public static class CatalogBuilder
{
    /// <summary>
    /// Converts raw Banner sections into app sections, resolving cross-lists.
    /// Seat counts use cross-list totals when present, as SearchNEU does.
    /// </summary>
    public static List<CourseSection> MakeSections(IReadOnlyList<BannerSection> banner, IReadOnlyDictionary<string, CourseSection>? previous = null)
    {
        var crossLists = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var section in banner)
        {
            var id = section.CrossList.NilIfBlank();
            if (id is null) continue;
            if (!crossLists.TryGetValue(id, out var list)) crossLists[id] = list = [];
            list.Add(section.CourseReferenceNumber);
        }
        var result = new List<CourseSection>(banner.Count);
        foreach (var section in banner)
        {
            var attributes = section.SectionAttributes ?? [];
            var honors = attributes.Any(attribute =>
                attribute.Code == BannerAttributes.Honors ||
                attribute.Description?.Trim() == "Honors");
            var meetings = (section.MeetingsFaculty ?? []).Select(meeting => meeting.MeetingTime?.ToMeeting()).OfType<Meeting>().ToList();
            var bannerInstructors = (section.MeetingsFaculty ?? []).SelectMany(meeting => meeting.Faculty ?? []).Select(faculty => faculty.ToInstructor()).OfType<Instructor>().ToList();
            CourseSection? prior = null;
            previous?.TryGetValue(section.CourseReferenceNumber, out prior);
            var crossId = section.CrossList.NilIfBlank();
            var crossList = crossId is not null && crossLists.TryGetValue(crossId, out var group)
                ? group.Where(crn => crn != section.CourseReferenceNumber).ToList()
                : [];
            result.Add(new CourseSection
            {
                Crn = section.CourseReferenceNumber,
                Subject = section.Subject,
                Number = section.CourseNumber,
                Title = HtmlText.DecodeEntities(HtmlText.DecodeEntities(section.CourseTitle ?? "")),
                SectionNumber = section.SequenceNumber ?? "",
                PartOfTerm = section.PartOfTerm ?? "1",
                SeatCapacity = section.CrossListCapacity ?? section.MaximumEnrollment ?? 0,
                SeatsRemaining = section.CrossListAvailable ?? section.SeatsAvailable ?? 0,
                Enrollment = section.CrossListCount ?? section.Enrollment ?? 0,
                WaitlistCapacity = section.WaitCapacity ?? 0,
                WaitlistCount = section.WaitCount ?? 0,
                WaitlistRemaining = section.WaitAvailable ?? 0,
                ClassType = HtmlText.DecodeEntities(section.ScheduleTypeDescription ?? "Lecture"),
                Honors = honors,
                Campus = HtmlText.DecodeEntities(section.CampusDescription ?? ""),
                InstructionMethod = section.InstructionalMethodDescription is null ? null : HtmlText.DecodeEntities(section.InstructionalMethodDescription),
                Meetings = meetings,
                // Banner's search results currently hide faculty; keep what we knew.
                Instructors = bannerInstructors.Count == 0 ? prior?.Instructors : bannerInstructors,
                CrossListCRNs = crossList,
                Attributes = attributes.Select(attribute => attribute.Code).ToList(),
                Description = prior?.Description,
                Prereqs = prior?.Prereqs,
                Coreqs = prior?.Coreqs,
            });
        }
        return result;
    }

    /// <summary>
    /// Builds course stubs from sections (port of SearchNEU's arrangeCourses).
    /// Courses whose title contains "Special Topics" or that carry the TOPC attribute are marked as special topics.
    /// </summary>
    public static (List<Course> Courses, List<AttributeInfo> Attributes) StubCourses(IReadOnlyList<BannerSection> banner)
    {
        var courses = new Dictionary<CourseKey, Course>();
        var order = new List<CourseKey>();
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in banner)
        {
            var key = new CourseKey(section.Subject, section.CourseNumber);
            var title = HtmlText.DecodeEntities(HtmlText.DecodeEntities(section.CourseTitle ?? ""));
            var sectionAttributes = section.SectionAttributes ?? [];
            foreach (var attribute in sectionAttributes)
                attributes[attribute.Code] = HtmlText.DecodeEntities(attribute.Description ?? attribute.Code).Trim();
            var isTopics = sectionAttributes.Any(attribute => attribute.Code == "TOPC");
            if (courses.TryGetValue(key, out var existing))
            {
                existing.Attributes = existing.Attributes
                    .Concat(sectionAttributes.Select(attribute => attribute.Code).Where(code => code != BannerAttributes.Honors))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(code => code, StringComparer.Ordinal)
                    .ToList();
                if (!existing.SpecialTopics && isTopics)
                {
                    existing.SpecialTopics = true;
                    existing.Title = "Special Topics";
                }
            }
            else
            {
                var special = title.Contains("Special Topics", StringComparison.OrdinalIgnoreCase) || isTopics;
                var low = section.CreditHourLow ?? section.CreditHours ?? 0;
                courses[key] = new Course
                {
                    Subject = section.Subject,
                    Number = section.CourseNumber,
                    Title = special ? "Special Topics" : title,
                    Description = "",
                    MinCredits = low,
                    MaxCredits = section.CreditHourHigh ?? low,
                    Attributes = sectionAttributes.Select(attribute => attribute.Code).Where(code => code != BannerAttributes.Honors).OrderBy(code => code, StringComparer.Ordinal).ToList(),
                    Postreqs = [],
                    SpecialTopics = special,
                    DetailsLoaded = false,
                };
                order.Add(key);
            }
        }
        var attributeList = attributes
            .Select(pair => new AttributeInfo { Code = pair.Key, Name = pair.Value })
            .OrderBy(attribute => attribute.Code, StringComparer.Ordinal)
            .ToList();
        return (order.Select(key => courses[key]).ToList(), attributeList);
    }

    /// <summary>
    /// Merges a fresh section scrape into an existing catalog.
    /// Seats, times and locations come from the fresh scrape. Instructors and special-topics
    /// details are preserved by CRN. Course descriptions and requisites are preserved, or borrowed
    /// from courses known from other terms, otherwise left as stubs.
    /// </summary>
    public static TermCatalog Merge(
        TermCatalog? existing,
        IReadOnlyList<BannerSection> fresh,
        Term term,
        IReadOnlyList<Subject>? subjects,
        IReadOnlyList<Campus>? campuses,
        IReadOnlyList<AttributeInfo>? attributes,
        IReadOnlyDictionary<CourseKey, Course>? knowledge = null,
        DateTimeOffset? now = null)
    {
        var moment = now ?? DateTimeOffset.UtcNow;
        var previousSections = new Dictionary<string, CourseSection>(StringComparer.Ordinal);
        foreach (var section in existing?.Sections ?? [])
            previousSections.TryAdd(section.Crn, section);
        var previousCourses = new Dictionary<CourseKey, Course>();
        foreach (var course in existing?.Courses ?? [])
            previousCourses.TryAdd(course.Key, course);

        var sections = MakeSections(fresh, previousSections);
        var (stubs, freshAttributes) = StubCourses(fresh);
        var courses = new List<Course>(stubs.Count);
        foreach (var stub in stubs)
        {
            if (previousCourses.TryGetValue(stub.Key, out var known))
            {
                known.MinCredits = stub.MinCredits;
                known.MaxCredits = stub.MaxCredits;
                known.Attributes = stub.Attributes;
                known.SpecialTopics = known.SpecialTopics || stub.SpecialTopics;
                courses.Add(known);
                continue;
            }
            if (knowledge is not null && knowledge.TryGetValue(stub.Key, out var borrowed))
            {
                borrowed.MinCredits = stub.MinCredits;
                borrowed.MaxCredits = stub.MaxCredits;
                borrowed.Attributes = stub.Attributes;
                borrowed.SpecialTopics = borrowed.SpecialTopics || stub.SpecialTopics;
                borrowed.Postreqs = [];
                courses.Add(borrowed);
                continue;
            }
            courses.Add(stub);
        }

        var mergedAttributes = new Dictionary<string, AttributeInfo>(StringComparer.Ordinal);
        foreach (var attribute in existing?.Attributes ?? [])
            mergedAttributes.TryAdd(attribute.Code, attribute);
        foreach (var attribute in (attributes ?? []).Concat(freshAttributes))
            mergedAttributes[attribute.Code] = attribute;

        var catalog = new TermCatalog
        {
            Term = term,
            GeneratedAt = existing?.GeneratedAt ?? moment,
            SectionsUpdatedAt = moment,
            Courses = courses.OrderBy(course => course.Key).ToList(),
            Sections = sections.OrderBy(section => section.CourseKey).ThenBy(section => section.SectionNumber, StringComparer.Ordinal).ToList(),
            Subjects = subjects?.ToList() ?? existing?.Subjects ?? [],
            Attributes = mergedAttributes.Values.OrderBy(attribute => attribute.Code, StringComparer.Ordinal).ToList(),
            Campuses = campuses?.ToList() ?? existing?.Campuses ?? [],
        };
        catalog.RecomputePostreqs();
        return catalog;
    }

    /// <summary>Replaces the sections of one course after a per-course live refresh.</summary>
    public static void ApplyCourseRefresh(TermCatalog catalog, CourseKey course, IReadOnlyList<BannerSection> banner)
    {
        var previous = new Dictionary<string, CourseSection>(StringComparer.Ordinal);
        foreach (var section in catalog.Sections.Where(section => section.CourseKey == course))
            previous.TryAdd(section.Crn, section);
        var fresh = MakeSections(banner, previous);
        catalog.Sections.RemoveAll(section => section.CourseKey == course);
        catalog.Sections.AddRange(fresh);
        catalog.Sections.Sort((a, b) =>
        {
            var key = a.CourseKey.CompareTo(b.CourseKey);
            return key != 0 ? key : string.CompareOrdinal(a.SectionNumber, b.SectionNumber);
        });
        if (!catalog.Courses.Any(item => item.Key == course) && banner.Count > 0)
        {
            var (stubs, _) = StubCourses(banner);
            catalog.Courses.AddRange(stubs);
            catalog.Courses.Sort((a, b) => a.Key.CompareTo(b.Key));
        }
    }

    /// <summary>Copies a live description and requisites onto the catalog course the page is showing.</summary>
    public static void ApplyCourseDetails(Course target, Course detailed)
    {
        target.Description = detailed.Description;
        target.Prereqs = detailed.Prereqs;
        target.Coreqs = detailed.Coreqs;
        if (!target.SpecialTopics && !string.IsNullOrWhiteSpace(detailed.Title)) target.Title = detailed.Title;
        target.College = detailed.College ?? target.College;
        target.Department = detailed.Department ?? target.Department;
        target.DetailsLoaded = true;
    }
}
