// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Portions of this file are derived from SearchNEU
// (https://github.com/sandboxnu/searchneu): the wire-format fields follow its zod
// schemas in packages/scraper/src/schemas/banner/*.ts, and
// BannerMeetingTime.ToMeeting() follows parseMeetingTimes in
// packages/scraper/src/generate/marshall.ts, Copyright (C) Sandbox at Northeastern
// University and the SearchNEU contributors, licensed under the GNU Affero General
// Public License v3.
// Modified 2026-10-13: ported from Swift to C# as lenient models (most fields
// optional), with meeting times converted to minutes after midnight and dates to
// ISO format.
//
// Wire formats for Northeastern's public Banner 9 class search API
// (https://nubanner.neu.edu/StudentRegistrationSsb). Decoding is intentionally
// lenient so that new or missing fields do not break the app.

using System.Globalization;
using System.Text.Json.Serialization;

namespace TermOrganize.Core;

public sealed class BannerTerm
{
    public required string Code { get; set; }
    public required string Description { get; set; }
}

public sealed class BannerCodeDescription
{
    public required string Code { get; set; }
    public required string Description { get; set; }
}

public sealed class BannerSectionPage
{
    public bool? Success { get; set; }
    public int TotalCount { get; set; }
    public List<BannerSection>? Data { get; set; }
}

public sealed class BannerSection
{
    public string? Term { get; set; }
    public required string CourseReferenceNumber { get; set; }
    public string? PartOfTerm { get; set; }
    public required string CourseNumber { get; set; }
    public required string Subject { get; set; }
    public string? SubjectDescription { get; set; }
    public string? SequenceNumber { get; set; }
    public string? CampusDescription { get; set; }
    public string? ScheduleTypeDescription { get; set; }
    public string? CourseTitle { get; set; }
    public double? CreditHours { get; set; }
    public string? CreditHourIndicator { get; set; }
    public int? MaximumEnrollment { get; set; }
    public int? Enrollment { get; set; }
    public int? SeatsAvailable { get; set; }
    public int? WaitCapacity { get; set; }
    public int? WaitCount { get; set; }
    public int? WaitAvailable { get; set; }
    public string? CrossList { get; set; }
    public int? CrossListCapacity { get; set; }
    public int? CrossListCount { get; set; }
    public int? CrossListAvailable { get; set; }
    public double? CreditHourHigh { get; set; }
    public double? CreditHourLow { get; set; }
    public bool? OpenSection { get; set; }
    public string? SubjectCourse { get; set; }
    public List<BannerMeetingFaculty>? MeetingsFaculty { get; set; }
    public List<BannerSectionAttribute>? SectionAttributes { get; set; }
    public string? InstructionalMethod { get; set; }
    public string? InstructionalMethodDescription { get; set; }
}

public sealed class BannerSectionAttribute
{
    public required string Code { get; set; }
    public string? Description { get; set; }
}

public sealed class BannerMeetingFaculty
{
    public string? CourseReferenceNumber { get; set; }
    public List<BannerFaculty>? Faculty { get; set; }
    public BannerMeetingTime? MeetingTime { get; set; }
}

public sealed class BannerFaculty
{
    public string? DisplayName { get; set; }
    public string? EmailAddress { get; set; }
    public bool? PrimaryIndicator { get; set; }

    public Instructor? ToInstructor()
    {
        var name = DisplayName is null ? null : HtmlText.DecodeEntities(HtmlText.DecodeEntities(DisplayName)).NilIfBlank();
        if (name is null) return null;
        return new Instructor { Name = name, Primary = PrimaryIndicator ?? false, Email = EmailAddress.NilIfBlank() };
    }
}

public sealed class BannerMeetingTime
{
    public string? BeginTime { get; set; }
    public string? EndTime { get; set; }
    public string? Building { get; set; }
    public string? BuildingDescription { get; set; }
    public string? Room { get; set; }
    public string? Campus { get; set; }
    public string? CampusDescription { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? MeetingType { get; set; }
    public string? MeetingTypeDescription { get; set; }
    public bool? Monday { get; set; }
    public bool? Tuesday { get; set; }
    public bool? Wednesday { get; set; }
    public bool? Thursday { get; set; }
    public bool? Friday { get; set; }
    public bool? Saturday { get; set; }
    public bool? Sunday { get; set; }

    [JsonIgnore]
    public DaySet DaySet
    {
        get
        {
            var days = DaySet.None;
            if (Sunday == true) days |= DaySet.Sunday;
            if (Monday == true) days |= DaySet.Monday;
            if (Tuesday == true) days |= DaySet.Tuesday;
            if (Wednesday == true) days |= DaySet.Wednesday;
            if (Thursday == true) days |= DaySet.Thursday;
            if (Friday == true) days |= DaySet.Friday;
            if (Saturday == true) days |= DaySet.Saturday;
            return days;
        }
    }

    /// <summary>
    /// Converts to the app model. Mirrors SearchNEU's parseMeetingTimes:
    /// placeholder rooms named ROOM are dropped and HHMM strings become minutes after midnight.
    /// </summary>
    public Meeting ToMeeting()
    {
        int? start = int.TryParse(BeginTime, NumberStyles.None, CultureInfo.InvariantCulture, out var begin) ? TimeOfDay.MinutesFromHhmm(begin) : null;
        int? end = int.TryParse(EndTime, NumberStyles.None, CultureInfo.InvariantCulture, out var finish) ? TimeOfDay.MinutesFromHhmm(finish) : null;
        var room = Room?.Trim();
        if (string.IsNullOrEmpty(room) || room.Equals("ROOM", StringComparison.OrdinalIgnoreCase)) room = null;
        var kind = MeetingType == "FNEX" ? "Final Exam" : HtmlText.DecodeEntities(MeetingTypeDescription ?? "Class");
        return new Meeting
        {
            Days = DaySet,
            Start = start,
            End = end,
            BuildingCode = Building.NilIfBlank(),
            Building = BuildingDescription is null ? null : HtmlText.DecodeEntities(BuildingDescription).NilIfBlank(),
            Room = room,
            CampusCode = Campus.NilIfBlank(),
            Kind = kind,
            StartDate = StartDate is null ? null : BannerDate.Iso(StartDate),
            EndDate = EndDate is null ? null : BannerDate.Iso(EndDate),
        };
    }
}

public sealed class BannerFacultyMeetingTimes
{
    public List<BannerMeetingFaculty>? Fmt { get; set; }
}

public static class BannerDate
{
    /// <summary>Converts Banner's MM/dd/yyyy to ISO yyyy-MM-dd.</summary>
    public static string? Iso(string banner)
    {
        var parts = banner.Split('/');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var month)) return null;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var day)) return null;
        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var year)) return null;
        return string.Create(CultureInfo.InvariantCulture, $"{year:0000}-{month:00}-{day:00}");
    }
}

public static class BannerText
{
    public static string? NilIfBlank(this string? text)
    {
        if (text is null) return null;
        var trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
