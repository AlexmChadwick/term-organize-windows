// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;

namespace TermOrganize.Core;

public sealed class Course
{
    public string Subject { get; set; } = "";
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public double MinCredits { get; set; }
    public double MaxCredits { get; set; }
    public List<string> Attributes { get; set; } = [];
    public Requisite? Prereqs { get; set; }
    public Requisite? Coreqs { get; set; }
    public List<CourseKey> Postreqs { get; set; } = [];
    public bool SpecialTopics { get; set; }
    public string? College { get; set; }
    public string? Department { get; set; }
    public bool DetailsLoaded { get; set; }

    [JsonIgnore] public CourseKey Key => new(Subject, Number);
    [JsonIgnore] public string Code => Key.ToString();
    [JsonIgnore] public string CreditsLabel => CreditFormatter.Label(MinCredits, MaxCredits);
    [JsonIgnore] public IReadOnlyList<NUPath> Nupaths => NUPathInfo.FromAttributeCodes(Attributes);
}

public sealed class Meeting
{
    public DaySet Days { get; set; }
    public int? Start { get; set; }
    public int? End { get; set; }
    public string? BuildingCode { get; set; }
    public string? Building { get; set; }
    public string? Room { get; set; }
    public string? CampusCode { get; set; }
    public string Kind { get; set; } = "Class";
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }

    [JsonIgnore] public bool IsFinalExam => Kind.Contains("final", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool HasTime => Start is not null && End is not null && !Days.IsEmpty;

    [JsonIgnore]
    public string TimeLabel => Start is int start && End is int end ? TimeOfDay.RangeLabel(start, end) : "Time TBA";

    [JsonIgnore]
    public string LocationLabel
    {
        get
        {
            var b = BuildingCode ?? Building;
            return (b, Room) switch
            {
                (not null, not null) => $"{b} {Room}",
                (not null, null) => b,
                _ => "Location TBA",
            };
        }
    }

    [JsonIgnore]
    public string LongLocationLabel
    {
        get
        {
            var b = Building ?? BuildingCode;
            return (b, Room) switch
            {
                (not null, not null) => $"{b} {Room}",
                (not null, null) => b,
                _ => "Location TBA",
            };
        }
    }

    [JsonIgnore]
    public bool IsOnline
    {
        get
        {
            var b = (Building ?? "") + (BuildingCode ?? "");
            return b.Contains("online", StringComparison.OrdinalIgnoreCase) || CampusCode == "VTL";
        }
    }

    [JsonIgnore] public int? Duration => Start is int s && End is int e ? e - s : null;

    public bool Overlaps(Meeting other)
    {
        if (Start is not int start || End is not int end || other.Start is not int otherStart || other.End is not int otherEnd)
            return false;
        if (Days.IsDisjoint(other.Days) || !(start < otherEnd && otherStart < end)) return false;
        return DateWindowsOverlap(StartDate, EndDate, other.StartDate, other.EndDate);
    }

    public static bool DateWindowsOverlap(string? aStart, string? aEnd, string? bStart, string? bEnd) =>
        string.CompareOrdinal(aStart ?? "0000-00-00", bEnd ?? "9999-99-99") <= 0 &&
        string.CompareOrdinal(bStart ?? "0000-00-00", aEnd ?? "9999-99-99") <= 0;
}

public sealed class Instructor
{
    public string Name { get; set; } = "";
    public bool Primary { get; set; }
    public string? Email { get; set; }

    [JsonIgnore]
    public string DisplayName => DisplayNameOf(Name);

    public static string DisplayNameOf(string raw)
    {
        var parts = raw.Split(',', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && parts[1].Length > 0) return $"{parts[1]} {parts[0]}";
        return raw.Trim();
    }
}

public sealed class CourseSection
{
    public string Crn { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string SectionNumber { get; set; } = "";
    public string PartOfTerm { get; set; } = "";
    public int SeatCapacity { get; set; }
    public int SeatsRemaining { get; set; }
    public int Enrollment { get; set; }
    public int WaitlistCapacity { get; set; }
    public int WaitlistCount { get; set; }
    public int WaitlistRemaining { get; set; }
    public string ClassType { get; set; } = "";
    public bool Honors { get; set; }
    public string Campus { get; set; } = "";
    public string? InstructionMethod { get; set; }
    public List<Meeting> Meetings { get; set; } = [];
    public List<Instructor>? Instructors { get; set; }
    public List<string> CrossListCRNs { get; set; } = [];
    public List<string> Attributes { get; set; } = [];
    public string? Description { get; set; }
    public Requisite? Prereqs { get; set; }
    public Requisite? Coreqs { get; set; }

    [JsonIgnore] public string Id => Crn;
    [JsonIgnore] public CourseKey CourseKey => new(Subject, Number);
    [JsonIgnore] public bool IsOpen => SeatsRemaining > 0;
    [JsonIgnore] public bool HasWaitlist => WaitlistCapacity > 0;
    [JsonIgnore] public bool IsFull => SeatsRemaining <= 0;

    [JsonIgnore]
    public double FillFraction => SeatCapacity <= 0 ? 1 : Math.Min(1, Math.Max(0, (double)(SeatCapacity - SeatsRemaining) / SeatCapacity));

    [JsonIgnore]
    public bool IsOnline =>
        Campus.Contains("online", StringComparison.OrdinalIgnoreCase) ||
        (InstructionMethod?.Contains("online", StringComparison.OrdinalIgnoreCase) ?? false);

    [JsonIgnore] public IEnumerable<Meeting> WeeklyMeetings => Meetings.Where(m => !m.IsFinalExam && m.HasTime);
    [JsonIgnore] public Meeting? FinalExam => Meetings.FirstOrDefault(m => m.IsFinalExam);

    [JsonIgnore]
    public DaySet MeetingDays
    {
        get
        {
            var days = DaySet.None;
            foreach (var meeting in WeeklyMeetings) days = days.Union(meeting.Days);
            return days;
        }
    }

    [JsonIgnore]
    public string InstructorLabel
    {
        get
        {
            if (Instructors is null) return "Instructor unavailable";
            if (Instructors.Count == 0) return "TBA";
            return string.Join(", ", Instructors.OrderByDescending(i => i.Primary).Select(i => i.DisplayName));
        }
    }

    [JsonIgnore]
    public string ScheduleSummary
    {
        get
        {
            var weekly = WeeklyMeetings.ToList();
            if (weekly.Count == 0) return IsOnline ? "Asynchronous / online" : "Times TBA";
            return string.Join(" · ", weekly.Select(m => $"{m.Days.CompactLabel} {m.TimeLabel}"));
        }
    }

    [JsonIgnore] public string SeatsLabel => $"{Math.Max(0, SeatsRemaining)} of {SeatCapacity} seats";

    public bool ConflictsWith(CourseSection other)
    {
        var mine = WeeklyMeetings.ToList();
        var theirs = other.WeeklyMeetings.ToList();
        return mine.Any(a => theirs.Any(a.Overlaps));
    }
}

public sealed class TermCatalog
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public Term Term { get; set; } = new();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UnixEpoch;
    public DateTimeOffset SectionsUpdatedAt { get; set; } = DateTimeOffset.UnixEpoch;
    public List<Course> Courses { get; set; } = [];
    public List<CourseSection> Sections { get; set; } = [];
    public List<Subject> Subjects { get; set; } = [];
    public List<AttributeInfo> Attributes { get; set; } = [];
    public List<Campus> Campuses { get; set; } = [];

    public void RecomputePostreqs()
    {
        var map = new Dictionary<CourseKey, HashSet<CourseKey>>();
        foreach (var course in Courses)
        {
            if (course.Prereqs is null) continue;
            foreach (var key in course.Prereqs.ReferencedCourses())
            {
                if (!map.TryGetValue(key, out var set)) map[key] = set = [];
                set.Add(course.Key);
            }
        }
        foreach (var course in Courses)
        {
            course.Postreqs = map.TryGetValue(course.Key, out var set)
                ? set.OrderBy(k => k).ToList()
                : [];
        }
    }
}
