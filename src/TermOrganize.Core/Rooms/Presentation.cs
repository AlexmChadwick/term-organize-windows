// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// How the free-rooms screens describe availability: colors, symbols, wording and the
// class dates of a term. Colors are names; calendar blocks are data, not views.

using System.Globalization;

namespace TermOrganize.Core;

public static class RoomsPresentation
{
    public static string Tint(this RoomAvailability.Status status, RoomAvailability.Moment moment) => status switch
    {
        RoomAvailability.Status.Busy => "red",
        RoomAvailability.Status.Free when status.IsFreeOnlyBriefly(moment) => "orange",
        RoomAvailability.Status.Free => "green",
        _ => "green",
    };

    public static string Symbol(this RoomAvailability.Status status) =>
        status.IsFree ? "door.left.hand.open" : "door.left.hand.closed";
}

public static class RoomsText
{
    public const string Disclaimer = "Based on scheduled classes only — rooms may be locked or reserved.";

    public const string OutsideHours = "This is outside regular class hours (7 AM – 10 PM). Most buildings are locked then, so these rooms may not be open to you.";

    public static string ClassDatesMessage(string termName, RoomAvailability.ClassDates dates)
    {
        var format = CultureInfo.CurrentCulture;
        var start = dates.Start.ToString("MMM d", format);
        var end = dates.End.ToString("MMM d", format);
        return $"{termName} classes run {start} – {end}. Showing a typical week.";
    }

    public static string FreeSummary(int free, int total) =>
        $"{free} of {total} {(total == 1 ? "room" : "rooms")} free";

    /// <summary>What happens next: the following class for a free room, or when a busy room frees up.</summary>
    public static string? FollowUpNote(RoomAvailability.Status status, IReadOnlyList<RoomBooking> today) => status switch
    {
        RoomAvailability.Status.Free free => NextNote(free.Until, today),
        RoomAvailability.Status.Busy busy => busy.Occupancy.FreeUntil is int until
            ? $"Then free until {TimeOfDay.Label(until)}"
            : "Then free for the rest of the day",
        _ => null,
    };

    private static string NextNote(int? until, IReadOnlyList<RoomBooking> today)
    {
        if (until is int start)
        {
            var next = today.FirstOrDefault(booking => booking.Start == start);
            if (next is not null)
                return $"Next: {next.Course}, {TimeOfDay.RangeLabel(next.Start, next.End)}";
        }
        return today.Count == 0 ? "No classes are scheduled here today." : "No more classes today.";
    }
}

/// <summary>One block on a room's weekly calendar.</summary>
public sealed class RoomCalendarEvent
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Detail { get; init; }
    public int Day { get; init; }
    public int Start { get; init; }
    public int End { get; init; }
    public required CourseColor Color { get; init; }
    public bool IsGhost { get; init; }
    public required string Crn { get; init; }
}

public static class RoomCalendar
{
    /// <summary>
    /// The room's weekly bookings as calendar blocks. One-off sessions don't meet every week,
    /// so they are marked tentative.
    /// </summary>
    public static List<RoomCalendarEvent> CalendarEvents(this RoomSchedule room, CatalogIndex index)
    {
        var events = new List<RoomCalendarEvent>();
        for (var position = 0; position < room.Bookings.Count; position++)
        {
            var booking = room.Bookings[position];
            if (booking.End <= booking.Start) continue;
            var section = index.Section(booking.Crn);
            foreach (var day in booking.Days.Indices)
            {
                events.Add(new RoomCalendarEvent
                {
                    Id = $"{booking.Crn}-{day}-{booking.Start}-{position}",
                    Title = booking.Course.ToString(),
                    Detail = section?.ClassType,
                    Day = day,
                    Start = booking.Start,
                    End = booking.End,
                    Color = CourseColor.ForCourse(booking.Course),
                    IsGhost = booking.IsShortSession,
                    Crn = booking.Crn,
                });
            }
        }
        return events;
    }

    /// <summary>Boston for the code BOS, falling back to the code itself.</summary>
    public static string? CampusName(this CatalogIndex index, string? code)
    {
        if (code is null) return null;
        return index.Catalog.Campuses.FirstOrDefault(campus => campus.Code == code)?.Name ?? code;
    }

    /// <summary>Changes whenever the index is replaced by newer data.</summary>
    public static string Identity(this CatalogIndex index) =>
        $"{index.Term.Code}|{index.Catalog.SectionsUpdatedAt.ToUnixTimeMilliseconds() / 1000.0}";
}
