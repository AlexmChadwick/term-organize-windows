// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Banner lists some sections as dozens of single-day meeting rows. These
// helpers fold rows with the same days, times and room into one pattern.

namespace TermOrganize.Core;

/// <summary>A distinct weekly meeting pattern of a section.</summary>
public sealed class CourseMeetingPattern : IEquatable<CourseMeetingPattern>
{
    public DaySet Days { get; private set; }
    public int? Start { get; private set; }
    public int? End { get; private set; }
    public string? BuildingCode { get; private set; }
    public string? Building { get; private set; }
    public string? Room { get; private set; }
    public string? CampusCode { get; private set; }
    /// <summary>Banner meeting type, e.g. Class, Lab, Final Exam.</summary>
    public string Kind { get; private set; }
    public string? StartDate { get; private set; }
    public string? EndDate { get; private set; }
    /// <summary>Number of Banner meeting rows folded into this pattern.</summary>
    public int Occurrences { get; private set; }
    /// <summary>True when every folded row was a single dated session (an irregular schedule).</summary>
    public bool IsDatedList { get; private set; }

    public string Id => $"{Days.Raw}|{Start ?? -1}|{End ?? -1}|{BuildingCode ?? ""}|{Room ?? ""}|{Kind}";

    public CourseMeetingPattern(Meeting meeting)
    {
        Days = meeting.Days;
        Start = meeting.Start;
        End = meeting.End;
        BuildingCode = meeting.BuildingCode;
        Building = meeting.Building;
        Room = meeting.Room;
        CampusCode = meeting.CampusCode;
        Kind = meeting.Kind;
        StartDate = meeting.StartDate;
        EndDate = meeting.EndDate;
        Occurrences = 1;
        IsDatedList = meeting.StartDate is not null && meeting.StartDate == meeting.EndDate;
    }

    public void Absorb(Meeting meeting)
    {
        Occurrences++;
        IsDatedList = IsDatedList && meeting.StartDate is not null && meeting.StartDate == meeting.EndDate;
        if (meeting.StartDate is string start && (StartDate is null || string.CompareOrdinal(start, StartDate) < 0))
            StartDate = start;
        if (meeting.EndDate is string end && (EndDate is null || string.CompareOrdinal(end, EndDate) > 0))
            EndDate = end;
    }

    public Meeting Meeting => new()
    {
        Days = Days,
        Start = Start,
        End = End,
        BuildingCode = BuildingCode,
        Building = Building,
        Room = Room,
        CampusCode = CampusCode,
        Kind = Kind,
        StartDate = StartDate,
        EndDate = EndDate,
    };

    public bool HasTime => Start is not null && End is not null && !Days.IsEmpty;
    public bool IsOnline => Meeting.IsOnline;
    public bool IsFinalExam => Meeting.IsFinalExam;
    public string TimeLabel => Meeting.TimeLabel;

    /// <summary>Cargill Hall 097 (or the short code), Online, or Location TBA.</summary>
    public string LocationLabel(bool longForm)
    {
        if (IsOnline) return "Online";
        return longForm ? Meeting.LongLocationLabel : Meeting.LocationLabel;
    }

    /// <summary>Sep 9 – Dec 13, or 6 meetings · Sep 14 – Oct 19 for irregular schedules.</summary>
    public string? DatesLabel
    {
        get
        {
            var range = CourseFormat.DateRange(StartDate, EndDate);
            if (!IsDatedList) return range;
            var parts = new List<string>();
            if (Occurrences > 0) parts.Add(CourseFormat.Count(Occurrences, "meeting"));
            if (range is not null) parts.Add(range);
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// The building code and room when both are known and the meeting is not online.
    /// Null for online, to-be-announced or building-only locations.
    /// </summary>
    public (string BuildingCode, string Room)? RoomReference
    {
        get
        {
            if (IsOnline) return null;
            if (string.IsNullOrWhiteSpace(BuildingCode) || string.IsNullOrWhiteSpace(Room)) return null;
            return (BuildingCode, Room);
        }
    }

    /// <summary>Monday, Wednesday, Thursday, 10:30 AM to 11:35 AM, Lab (no location).</summary>
    public string SpokenSchedule
    {
        get
        {
            var parts = new List<string>();
            if (!Days.IsEmpty) parts.Add(string.Join(", ", Days.Indices.Select(index => DaySet.FullNames[index])));
            if (Start is int start && End is int end) parts.Add($"{TimeOfDay.Label(start)} to {TimeOfDay.Label(end)}");
            else parts.Add("time to be announced");
            if (Kind.Length > 0 && !Kind.Equals("Class", StringComparison.OrdinalIgnoreCase)) parts.Add(Kind);
            return string.Join(", ", parts);
        }
    }

    /// <summary>Monday, Wednesday, Thursday, 10:30 AM to 11:35 AM, Cargill Hall 097.</summary>
    public string SpokenDescription(bool longLocation = true) => $"{SpokenSchedule}, {LocationLabel(longLocation)}";

    public bool Equals(CourseMeetingPattern? other) => other is not null && Id == other.Id && Occurrences == other.Occurrences
        && StartDate == other.StartDate && EndDate == other.EndDate && IsDatedList == other.IsDatedList;

    public override bool Equals(object? obj) => Equals(obj as CourseMeetingPattern);
    public override int GetHashCode() => HashCode.Combine(Id, Occurrences, StartDate, EndDate, IsDatedList);
}

public static class CourseMeetingPatterns
{
    /// <summary>Weekly (non-final) meeting patterns, folded and sorted Monday first.</summary>
    public static List<CourseMeetingPattern> CourseMeetingPatternsOf(this CourseSection section)
    {
        var order = new List<string>();
        var patterns = new Dictionary<string, CourseMeetingPattern>(StringComparer.Ordinal);
        foreach (var meeting in section.Meetings)
        {
            if (meeting.IsFinalExam || (meeting.Days.IsEmpty && meeting.Start is null)) continue;
            var pattern = new CourseMeetingPattern(meeting);
            if (patterns.TryGetValue(pattern.Id, out var existing)) existing.Absorb(meeting);
            else
            {
                patterns[pattern.Id] = pattern;
                order.Add(pattern.Id);
            }
        }
        return order.Select(id => patterns[id]).OrderBy(pattern =>
        {
            var first = pattern.Days.Indices.Select(index => (index + 6) % 7).DefaultIfEmpty(9).Min();
            return (first, pattern.Start ?? int.MaxValue);
        }).ToList();
    }

    /// <summary>One line for list rows, or why there is no time.</summary>
    public static string CourseScheduleLine(this CourseSection section)
    {
        var timed = section.CourseMeetingPatternsOf().Where(pattern => pattern.HasTime).ToList();
        if (timed.Count == 0)
        {
            if (section.IsOnline) return "Online · asynchronous";
            return section.Meetings.Count == 0 ? "No scheduled meetings" : "Times to be announced";
        }
        return string.Join(" · ", timed.Select(pattern => $"{pattern.Days.CompactLabel} {pattern.TimeLabel}"));
    }

    /// <summary>The schedule line as it should be read (day names, "to" between times).</summary>
    public static string CourseSpokenScheduleLine(this CourseSection section)
    {
        var timed = section.CourseMeetingPatternsOf().Where(pattern => pattern.HasTime).ToList();
        if (timed.Count == 0) return section.CourseScheduleLine();
        return string.Join("; ", timed.Select(pattern => pattern.SpokenSchedule));
    }

    /// <summary>The final exam, when Banner lists one.</summary>
    public static CourseMeetingPattern? CourseFinalExamPattern(this CourseSection section) =>
        section.FinalExam is Meeting exam ? new CourseMeetingPattern(exam) : null;
}
