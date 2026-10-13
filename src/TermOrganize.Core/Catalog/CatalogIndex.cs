// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;

namespace TermOrganize.Core;

public sealed class CourseStats
{
    public int SectionCount { get; set; }
    public int OpenSectionCount { get; set; }
    public int SeatsRemaining { get; set; }
    public int SeatCapacity { get; set; }
    public int WaitlistOpenCount { get; set; }
    public HashSet<string> Campuses { get; set; } = [];
    public HashSet<string> ClassTypes { get; set; } = [];
    public bool HasHonors { get; set; }
    public bool HasOnline { get; set; }
    public bool HasInPerson { get; set; }
    public List<string> InstructorNames { get; set; } = [];
    public DaySet MeetingDays { get; set; }
    public bool IsFull => SectionCount > 0 && OpenSectionCount == 0;

    public string AvailabilityLabel
    {
        get
        {
            if (SectionCount == 0) return "No sections";
            if (SectionCount == 1) return OpenSectionCount == 1 ? "1 section · open" : "1 section · full";
            return $"{OpenSectionCount} of {SectionCount} sections open";
        }
    }
}

public sealed class InstructorSummary
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public string? Email { get; init; }
    public required IReadOnlyList<string> Crns { get; init; }
    public required IReadOnlyList<CourseKey> CourseKeys { get; init; }
    public string Id => Name;
}

public sealed class RoomBooking
{
    public required string Crn { get; init; }
    public required CourseKey Course { get; init; }
    public DaySet Days { get; init; }
    public int Start { get; init; }
    public int End { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }

    public bool OccursOn(DateOnly date)
    {
        var weekday = (int)date.DayOfWeek;
        if (!Days.Contains(weekday)) return false;
        return IsActiveOn(date);
    }

    public bool IsActiveOn(DateOnly date)
    {
        var iso = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (StartDate is not null && string.CompareOrdinal(iso, StartDate) < 0) return false;
        if (EndDate is not null && string.CompareOrdinal(iso, EndDate) > 0) return false;
        return true;
    }

    public bool IsShortSession
    {
        get
        {
            if (StartDate is null || EndDate is null) return false;
            if (!DateOnly.TryParse(StartDate, out var a) || !DateOnly.TryParse(EndDate, out var b)) return false;
            return b.DayNumber - a.DayNumber < 21;
        }
    }
}

public sealed class RoomSchedule
{
    public required string BuildingCode { get; init; }
    public required string BuildingName { get; init; }
    public required string Room { get; init; }
    public List<RoomBooking> Bookings { get; init; } = [];
    public string Id => $"{BuildingCode}|{Room}";
    public string Label => $"{BuildingCode} {Room}";
}

public sealed class BuildingSummary
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? CampusCode { get; init; }
    public required IReadOnlyList<RoomSchedule> Rooms { get; init; }
    public string Id => Code;
}

/// <summary>Read-only lookup structures for one term.</summary>
public sealed class CatalogIndex
{
    public TermCatalog Catalog { get; }
    public IReadOnlyDictionary<CourseKey, Course> CoursesByKey { get; }
    public IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>> SectionsByCourse { get; }
    public IReadOnlyDictionary<string, CourseSection> SectionsByCrn { get; }
    public IReadOnlyDictionary<CourseKey, CourseStats> StatsByCourse { get; }
    public IReadOnlyDictionary<string, Subject> SubjectsByCode { get; }
    public IReadOnlyDictionary<string, string> AttributeNames { get; }
    public IReadOnlyList<string> CampusNames { get; }
    public IReadOnlyList<string> ClassTypes { get; }
    public IReadOnlyList<Subject> ActiveSubjects { get; }
    public IReadOnlyDictionary<string, int> CourseCountBySubject { get; }
    public IReadOnlyList<InstructorSummary> Instructors { get; }
    public IReadOnlyDictionary<string, InstructorSummary> InstructorsByName { get; }
    public IReadOnlyList<BuildingSummary> Buildings { get; }
    public IReadOnlyList<SearchDocument> SearchDocuments { get; }

    public Term Term => Catalog.Term;
    public IReadOnlyList<Course> Courses => Catalog.Courses;

    private CatalogIndex(
        TermCatalog catalog,
        Dictionary<CourseKey, Course> coursesByKey,
        Dictionary<CourseKey, IReadOnlyList<CourseSection>> sectionsByCourse,
        Dictionary<string, CourseSection> sectionsByCrn,
        Dictionary<CourseKey, CourseStats> stats,
        Dictionary<string, Subject> subjectsByCode,
        Dictionary<string, string> attributeNames,
        List<string> campusNames,
        List<string> classTypes,
        List<Subject> activeSubjects,
        Dictionary<string, int> courseCountBySubject,
        List<InstructorSummary> instructors,
        Dictionary<string, InstructorSummary> instructorsByName,
        List<BuildingSummary> buildings,
        List<SearchDocument> documents)
    {
        Catalog = catalog;
        CoursesByKey = coursesByKey;
        SectionsByCourse = sectionsByCourse;
        SectionsByCrn = sectionsByCrn;
        StatsByCourse = stats;
        SubjectsByCode = subjectsByCode;
        AttributeNames = attributeNames;
        CampusNames = campusNames;
        ClassTypes = classTypes;
        ActiveSubjects = activeSubjects;
        CourseCountBySubject = courseCountBySubject;
        Instructors = instructors;
        InstructorsByName = instructorsByName;
        Buildings = buildings;
        SearchDocuments = documents;
    }

    public Course? Course(CourseKey key) => CoursesByKey.TryGetValue(key, out var course) ? course : null;
    public IReadOnlyList<CourseSection> Sections(CourseKey key) =>
        SectionsByCourse.TryGetValue(key, out var list) ? list : [];
    public CourseSection? Section(string crn) => SectionsByCrn.TryGetValue(crn, out var section) ? section : null;
    public CourseStats Stats(CourseKey key) => StatsByCourse.TryGetValue(key, out var stats) ? stats : new CourseStats();
    public string? SubjectName(string code) => SubjectsByCode.TryGetValue(code, out var subject) ? subject.Name : null;
    public InstructorSummary? Instructor(string name) => InstructorsByName.TryGetValue(name, out var instructor) ? instructor : null;
    public string AttributeName(string code) => AttributeNames.TryGetValue(code, out var name) ? name : code;

    public List<Course> CoursesInSubject(string code) =>
        Catalog.Courses.Where(c => c.Subject == code).OrderBy(c => c.Key).ToList();

    public List<Course> CoursesWith(NUPath path)
    {
        var code = path.AttributeCode();
        return Catalog.Courses.Where(c => c.Attributes.Contains(code)).OrderBy(c => c.Key).ToList();
    }

    public static CatalogIndex Build(TermCatalog input)
    {
        var catalog = NormalizedRequisites(input);
        var coursesByKey = new Dictionary<CourseKey, Course>(catalog.Courses.Count);
        foreach (var course in catalog.Courses) coursesByKey[course.Key] = course;

        var sectionsByCourse = new Dictionary<CourseKey, List<CourseSection>>();
        var sectionsByCrn = new Dictionary<string, CourseSection>(catalog.Sections.Count, StringComparer.Ordinal);
        var campusCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var typeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var rooms = new Dictionary<string, RoomSchedule>(StringComparer.Ordinal);
        var buildingNames = new Dictionary<string, (string Name, string? Campus)>(StringComparer.Ordinal);

        foreach (var section in catalog.Sections)
        {
            if (!sectionsByCourse.TryGetValue(section.CourseKey, out var list))
                sectionsByCourse[section.CourseKey] = list = [];
            list.Add(section);
            sectionsByCrn[section.Crn] = section;
            if (section.Campus.Length > 0)
                campusCounts[section.Campus] = campusCounts.GetValueOrDefault(section.Campus) + 1;
            typeCounts[section.ClassType] = typeCounts.GetValueOrDefault(section.ClassType) + 1;
            foreach (var meeting in section.WeeklyMeetings)
            {
                if (meeting.BuildingCode is null || meeting.Room is null || meeting.Start is not int start || meeting.End is not int end || meeting.IsOnline)
                    continue;
                var id = $"{meeting.BuildingCode}|{meeting.Room}";
                if (!rooms.TryGetValue(id, out var schedule))
                {
                    schedule = new RoomSchedule
                    {
                        BuildingCode = meeting.BuildingCode,
                        BuildingName = meeting.Building ?? meeting.BuildingCode,
                        Room = meeting.Room,
                    };
                    rooms[id] = schedule;
                }
                schedule.Bookings.Add(new RoomBooking
                {
                    Crn = section.Crn,
                    Course = section.CourseKey,
                    Days = meeting.Days,
                    Start = start,
                    End = end,
                    StartDate = meeting.StartDate,
                    EndDate = meeting.EndDate,
                });
                if (!buildingNames.ContainsKey(meeting.BuildingCode))
                    buildingNames[meeting.BuildingCode] = (meeting.Building ?? meeting.BuildingCode, meeting.CampusCode);
            }
        }

        var sortedSections = new Dictionary<CourseKey, IReadOnlyList<CourseSection>>(sectionsByCourse.Count);
        var stats = new Dictionary<CourseKey, CourseStats>(sectionsByCourse.Count);
        foreach (var (key, list) in sectionsByCourse)
        {
            var sorted = SortedSections(list);
            sortedSections[key] = sorted;
            stats[key] = MakeStats(sorted);
        }

        var subjectsByCode = new Dictionary<string, Subject>(StringComparer.Ordinal);
        foreach (var subject in catalog.Subjects)
            subjectsByCode.TryAdd(subject.Code, subject);
        var courseCount = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var course in catalog.Courses)
            courseCount[course.Subject] = courseCount.GetValueOrDefault(course.Subject) + 1;
        var active = courseCount.Keys.OrderBy(c => c, StringComparer.Ordinal).Select(code =>
            subjectsByCode.TryGetValue(code, out var subject) ? subject : new Subject { Code = code, Name = code }).ToList();

        var (instructors, instructorsByName) = MakeInstructorDirectory(catalog.Sections);
        var roomsByBuilding = new Dictionary<string, List<RoomSchedule>>(StringComparer.Ordinal);
        foreach (var room in rooms.Values)
        {
            if (!roomsByBuilding.TryGetValue(room.BuildingCode, out var list))
                roomsByBuilding[room.BuildingCode] = list = [];
            list.Add(room);
        }
        var titleOrder = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
        var buildings = roomsByBuilding.Select(pair =>
        {
            buildingNames.TryGetValue(pair.Key, out var meta);
            return new BuildingSummary
            {
                Code = pair.Key,
                Name = meta.Name ?? pair.Key,
                CampusCode = meta.Campus,
                Rooms = pair.Value.OrderBy(r => r.Room, titleOrder).ToList(),
            };
        }).OrderBy(b => b.Name, titleOrder).ToList();

        var attributeNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in catalog.Attributes)
            attributeNames.TryAdd(attribute.Code, attribute.Name);

        var documents = catalog.Courses.Select(course => MakeDocument(
            course,
            subjectsByCode.TryGetValue(course.Subject, out var subject) ? subject.Name : "",
            stats.TryGetValue(course.Key, out var courseStats) ? courseStats : null,
            sortedSections.TryGetValue(course.Key, out var courseSections) ? courseSections : [])).ToList();

        return new CatalogIndex(
            catalog, coursesByKey, sortedSections, sectionsByCrn, stats, subjectsByCode, attributeNames,
            campusCounts.OrderByDescending(p => p.Value).Select(p => p.Key).Where(c => !c.Contains("no campus", StringComparison.OrdinalIgnoreCase)).ToList(),
            typeCounts.OrderByDescending(p => p.Value).Select(p => p.Key).ToList(),
            active, courseCount, instructors, instructorsByName, buildings, documents);
    }

    public static TermCatalog NormalizedRequisites(TermCatalog catalog)
    {
        if (!catalog.Courses.Any(NeedsSubjectFix)) return catalog;
        var lookup = new BannerParsers.SubjectLookup(catalog.Subjects);
        var copy = CopyCatalog(catalog);
        copy.Courses = catalog.Courses.Select(course => NormalizedCourse(course, lookup)).ToList();
        copy.RecomputePostreqs();
        return copy;
    }

    public static Course NormalizedCourse(Course course, BannerParsers.SubjectLookup lookup)
    {
        if (!NeedsSubjectFix(course)) return course;
        CourseKey Fix(CourseKey key) =>
            key.Subject.Contains(' ') ? new CourseKey(lookup.Code(key.Subject), key.Number) : key;
        var clone = CloneCourse(course);
        clone.Prereqs = course.Prereqs?.MapCourses(Fix);
        clone.Coreqs = course.Coreqs?.MapCourses(Fix);
        return clone;
    }

    public static (List<InstructorSummary> List, Dictionary<string, InstructorSummary> ByName) MakeInstructorDirectory(IEnumerable<CourseSection> sections)
    {
        var entries = new Dictionary<string, (string Display, string? Email, List<string> Crns, List<CourseKey> Keys)>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            foreach (var instructor in section.Instructors ?? [])
            {
                if (!entries.TryGetValue(instructor.Name, out var entry))
                    entry = (instructor.DisplayName, instructor.Email, [], []);
                entry.Crns.Add(section.Crn);
                if (!entry.Keys.Contains(section.CourseKey)) entry.Keys.Add(section.CourseKey);
                entry.Email ??= instructor.Email;
                entries[instructor.Name] = entry;
            }
        }
        var order = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
        var list = entries.Select(pair => new InstructorSummary
        {
            Name = pair.Key,
            DisplayName = pair.Value.Display,
            Email = pair.Value.Email,
            Crns = pair.Value.Crns,
            CourseKeys = pair.Value.Keys.OrderBy(k => k).ToList(),
        }).OrderBy(i => i.DisplayName, order).ToList();
        var byName = new Dictionary<string, InstructorSummary>(StringComparer.Ordinal);
        foreach (var instructor in list) byName.TryAdd(instructor.Name, instructor);
        return (list, byName);
    }

    public static List<CourseSection> SortedSections(IReadOnlyList<CourseSection> list)
    {
        int Rank(string type) => type switch
        {
            "Lecture" => 0,
            "Lab" => 1,
            "Recitation/Discussion" => 2,
            "Seminar" => 3,
            _ => 9,
        };
        return list.OrderBy(s => Rank(s.ClassType)).ThenBy(s => s.ClassType, StringComparer.Ordinal).ThenBy(s => s.SectionNumber, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static CourseStats MakeStats(IReadOnlyList<CourseSection> list)
    {
        var stats = new CourseStats();
        var names = new List<string>();
        foreach (var section in list)
        {
            stats.SectionCount++;
            if (section.IsOpen) stats.OpenSectionCount++;
            stats.SeatsRemaining += Math.Max(0, section.SeatsRemaining);
            stats.SeatCapacity += section.SeatCapacity;
            if (section.WaitlistRemaining > 0) stats.WaitlistOpenCount++;
            if (section.Campus.Length > 0) stats.Campuses.Add(section.Campus);
            stats.ClassTypes.Add(section.ClassType);
            stats.HasHonors |= section.Honors;
            if (section.IsOnline) stats.HasOnline = true;
            else stats.HasInPerson = true;
            stats.MeetingDays = stats.MeetingDays.Union(section.MeetingDays);
            foreach (var instructor in (section.Instructors ?? []).OrderByDescending(i => i.Primary))
            {
                if (!names.Contains(instructor.DisplayName)) names.Add(instructor.DisplayName);
            }
        }
        stats.InstructorNames = names;
        return stats;
    }

    public static SearchDocument MakeDocument(Course course, string subjectName, CourseStats? stats, IReadOnlyList<CourseSection> sections) =>
        new(course, subjectName, stats?.InstructorNames ?? [], course.SpecialTopics ? sections.Select(s => s.Title).Distinct().ToArray() : []);

    public CatalogIndex ReplacingCourse(CourseKey key, Course? incoming, IReadOnlyList<CourseSection> newSections)
    {
        var lookup = new BannerParsers.SubjectLookup(Catalog.Subjects);
        var course = incoming is null ? null : NormalizedCourse(incoming, lookup);
        var catalog = CopyCatalog(Catalog);
        catalog.Sections = Catalog.Sections.Where(s => s.CourseKey != key).ToList();
        catalog.Sections.AddRange(newSections);
        var coursesByKey = new Dictionary<CourseKey, Course>(CoursesByKey);
        var documents = SearchDocuments.ToList();
        var sorted = SortedSections(newSections.ToList());
        var stats = new Dictionary<CourseKey, CourseStats>(StatsByCourse) { [key] = MakeStats(sorted) };
        if (course is not null)
        {
            var index = catalog.Courses.FindIndex(c => c.Key == key);
            if (index >= 0) catalog.Courses[index] = course;
            else
            {
                catalog.Courses.Add(course);
                catalog.Courses.Sort((a, b) => a.Key.CompareTo(b.Key));
            }
            coursesByKey[key] = course;
        }
        if (coursesByKey.TryGetValue(key, out var current))
        {
            var document = MakeDocument(current, SubjectsByCode.TryGetValue(key.Subject, out var subject) ? subject.Name : "", stats[key], sorted);
            var docIndex = documents.FindIndex(d => d.Key == key);
            if (docIndex >= 0) documents[docIndex] = document;
            else documents.Add(document);
        }
        var sectionsByCourse = SectionsByCourse.ToDictionary(p => p.Key, p => p.Value);
        var sectionsByCrn = new Dictionary<string, CourseSection>(SectionsByCrn, StringComparer.Ordinal);
        if (SectionsByCourse.TryGetValue(key, out var oldSections))
            foreach (var old in oldSections) sectionsByCrn.Remove(old.Crn);
        foreach (var section in sorted) sectionsByCrn[section.Crn] = section;
        sectionsByCourse[key] = sorted;
        var (instructors, instructorsByName) = MakeInstructorDirectory(catalog.Sections);
        return new CatalogIndex(
            catalog, coursesByKey, sectionsByCourse, sectionsByCrn, stats,
            new Dictionary<string, Subject>(SubjectsByCode, StringComparer.Ordinal),
            new Dictionary<string, string>(AttributeNames, StringComparer.Ordinal),
            CampusNames.ToList(), ClassTypes.ToList(), ActiveSubjects.ToList(),
            new Dictionary<string, int>(CourseCountBySubject, StringComparer.Ordinal),
            instructors, instructorsByName, Buildings.ToList(), documents);
    }

    private static bool NeedsSubjectFix(Course course) =>
        HasSpacedSubject(course.Prereqs) || HasSpacedSubject(course.Coreqs);

    private static bool HasSpacedSubject(Requisite? requisite) =>
        requisite?.ReferencedCourses().Any(key => key.Subject.Contains(' ')) ?? false;

    private static TermCatalog CopyCatalog(TermCatalog catalog) => new()
    {
        Version = catalog.Version,
        Term = catalog.Term,
        GeneratedAt = catalog.GeneratedAt,
        SectionsUpdatedAt = catalog.SectionsUpdatedAt,
        Courses = catalog.Courses.ToList(),
        Sections = catalog.Sections.ToList(),
        Subjects = catalog.Subjects.ToList(),
        Attributes = catalog.Attributes.ToList(),
        Campuses = catalog.Campuses.ToList(),
    };

    private static Course CloneCourse(Course course) => new()
    {
        Subject = course.Subject,
        Number = course.Number,
        Title = course.Title,
        Description = course.Description,
        MinCredits = course.MinCredits,
        MaxCredits = course.MaxCredits,
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
