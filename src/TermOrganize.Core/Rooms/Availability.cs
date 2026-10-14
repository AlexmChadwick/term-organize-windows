// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// The "find a free classroom" idea follows SearchNEU's rooms page
// (https://github.com/sandboxnu/searchneu). The availability rules below are
// written from scratch for this app, ported from the Swift helpers.
//
// A booking occupies [start, end): a class ending at 10:05 leaves the room
// free at 10:05, a class starting at 10:05 makes it busy at 10:05.

using System.Globalization;
using System.Text;

namespace TermOrganize.Core;

public static class RoomAvailability
{
    public const int FirstClassMinute = 7 * 60;
    public const int LastClassMinute = 22 * 60;

    /// <summary>A point in a typical week.</summary>
    public readonly record struct Moment(int Weekday, int Minutes, DateOnly? Date)
    {
        public Moment(int weekday, int minutes) : this(NormalizeWeekday(weekday), Math.Clamp(minutes, 0, 24 * 60 - 1), null) { }

        public static Moment From(DateTime date) =>
            new((int)date.DayOfWeek, date.Hour * 60 + date.Minute, DateOnly.FromDateTime(date));

        public static Moment From(DateTimeOffset date) => From(TermCalendar.Now(date));

        /// <summary>Classes are scheduled between 7 AM and 10 PM.</summary>
        public bool IsWithinClassHours => Minutes >= FirstClassMinute && Minutes < LastClassMinute;

        public string Label => $"{DaySet.FullNames[Weekday]} at {TimeOfDay.Label(Minutes)}";

        private static int NormalizeWeekday(int weekday) => ((weekday % 7) + 7) % 7;
    }

    public sealed class Occupancy : IEquatable<Occupancy>
    {
        public required RoomBooking Booking { get; init; }
        /// <summary>When the room empties. Back-to-back and overlapping classes are merged.</summary>
        public int Until { get; init; }
        /// <summary>When the next class after that starts. Null means free for the rest of the day.</summary>
        public int? FreeUntil { get; init; }

        public bool Equals(Occupancy? other) =>
            other is not null && Booking == other.Booking && Until == other.Until && FreeUntil == other.FreeUntil;

        public override bool Equals(object? obj) => Equals(obj as Occupancy);
        public override int GetHashCode() => HashCode.Combine(Booking, Until, FreeUntil);
    }

    public abstract record Status
    {
        public sealed record Free(int? Until) : Status;
        public sealed record Busy(Occupancy Occupancy) : Status;

        public bool IsFree => this is Free;

        public int MinutesRemaining(Moment moment) => this switch
        {
            Free free => (free.Until ?? 24 * 60) - moment.Minutes,
            Busy busy => busy.Occupancy.Until - moment.Minutes,
            _ => 0,
        };

        /// <summary>A free room that will be taken within half an hour isn't much use.</summary>
        public bool IsFreeOnlyBriefly(Moment moment) => IsFree && MinutesRemaining(moment) < 30;

        public string Summary => this switch
        {
            Free { Until: int until } => $"Free until {TimeOfDay.Label(until)}",
            Free => "Free for the rest of the day",
            Busy busy => $"In use until {TimeOfDay.Label(busy.Occupancy.Until)}",
            _ => "",
        };

        public string DetailedSummary => this switch
        {
            Free => Summary,
            Busy busy => $"In use: {busy.Occupancy.Booking.Course} until {TimeOfDay.Label(busy.Occupancy.Until)}",
            _ => "",
        };
    }

    public sealed class Entry
    {
        public required RoomSchedule Room { get; init; }
        public required Status Availability { get; init; }
        public string Id => Room.Id;
    }

    /// <summary>
    /// Bookings that fall on the weekday, ordered by start time. Zero-length bookings are ignored.
    /// With a concrete date, only bookings whose date window covers it count; otherwise
    /// short one-off sessions are left out.
    /// </summary>
    public static List<RoomBooking> Bookings(RoomSchedule room, int weekday, DateOnly? date = null) =>
        room.Bookings
            .Where(booking => booking.Days.Contains(weekday) && booking.End > booking.Start)
            .Where(booking => date is DateOnly day ? booking.IsActiveOn(day) : !booking.IsShortSession)
            .OrderBy(booking => booking.Start)
            .ThenBy(booking => booking.End)
            .ToList();

    public static Status At(RoomSchedule room, Moment moment) =>
        At(Bookings(room, moment.Weekday, moment.Date), moment.Minutes);

    public static Status At(IReadOnlyList<RoomBooking> day, int minutes)
    {
        var current = day.FirstOrDefault(booking => booking.Start <= minutes && minutes < booking.End);
        if (current is null) return new Status.Free(day.FirstOrDefault(booking => booking.Start > minutes)?.Start);
        var until = current.End;
        foreach (var booking in day)
        {
            if (booking.Start <= until && booking.End > until) until = booking.End;
        }
        return new Status.Busy(new Occupancy
        {
            Booking = current,
            Until = until,
            FreeUntil = day.FirstOrDefault(booking => booking.Start >= until)?.Start,
        });
    }

    public readonly record struct Stretch(int Start, int End);

    /// <summary>
    /// Stretches of a day without a class between start and end, half-open.
    /// Back-to-back and overlapping classes leave no gap.
    /// </summary>
    public static List<Stretch> FreeStretches(IReadOnlyList<RoomBooking> day, int start, int end)
    {
        var stretches = new List<Stretch>();
        if (start >= end) return stretches;
        var cursor = start;
        foreach (var booking in day.OrderBy(item => item.Start).ThenBy(item => item.End))
        {
            if (booking.Start >= end) break;
            if (booking.Start > cursor) stretches.Add(new Stretch(cursor, booking.Start));
            cursor = Math.Max(cursor, booking.End);
            if (cursor >= end) return stretches;
        }
        if (cursor < end) stretches.Add(new Stretch(cursor, end));
        return stretches;
    }

    /// <summary>
    /// How many rooms have no class during each whole hour from 7 AM to 10 PM.
    /// Index 0 is 7–8 AM.
    /// </summary>
    public static int[] FreeRoomCounts(IReadOnlyList<RoomSchedule> rooms, int weekday, DateOnly? date = null)
    {
        var hours = (LastClassMinute - FirstClassMinute) / 60;
        var counts = new int[hours];
        foreach (var room in rooms)
        {
            var day = Bookings(room, weekday, date);
            for (var hour = 0; hour < hours; hour++)
            {
                var start = FirstClassMinute + hour * 60;
                var end = start + 60;
                if (!day.Any(booking => booking.Start < end && booking.End > start)) counts[hour]++;
            }
        }
        return counts;
    }

    public sealed class BuildingAvailability
    {
        public required BuildingSummary Building { get; init; }
        public List<Entry> Free { get; set; } = [];
        public List<Entry> Busy { get; set; } = [];
        public int FreeCount { get; init; }
        public int RoomCount { get; init; }
        public int BusyCount => RoomCount - FreeCount;
        public string Id => Building.Id;
    }

    public static BuildingAvailability Of(BuildingSummary building, Moment moment)
    {
        var free = new List<(Entry Entry, int Order)>();
        var busy = new List<(Entry Entry, int Order)>();
        for (var order = 0; order < building.Rooms.Count; order++)
        {
            var room = building.Rooms[order];
            var entry = new Entry { Room = room, Availability = At(room, moment) };
            if (entry.Availability.IsFree) free.Add((entry, order));
            else busy.Add((entry, order));
        }
        free.Sort((left, right) =>
        {
            var remaining = right.Entry.Availability.MinutesRemaining(moment).CompareTo(left.Entry.Availability.MinutesRemaining(moment));
            return remaining != 0 ? remaining : left.Order.CompareTo(right.Order);
        });
        busy.Sort((left, right) =>
        {
            var remaining = left.Entry.Availability.MinutesRemaining(moment).CompareTo(right.Entry.Availability.MinutesRemaining(moment));
            return remaining != 0 ? remaining : left.Order.CompareTo(right.Order);
        });
        return new BuildingAvailability
        {
            Building = building,
            Free = free.Select(item => item.Entry).ToList(),
            Busy = busy.Select(item => item.Entry).ToList(),
            FreeCount = free.Count,
            RoomCount = building.Rooms.Count,
        };
    }

    public static List<BuildingAvailability> Of(IEnumerable<BuildingSummary> buildings, Moment moment) =>
        buildings.Select(building => Of(building, moment)).ToList();

    public enum BuildingSort
    {
        MostFree,
        Name,
    }

    public static string Title(this BuildingSort sort) => sort switch
    {
        BuildingSort.MostFree => "Most Free Rooms",
        BuildingSort.Name => "Name",
        _ => "",
    };

    public static List<BuildingAvailability> Sorted(IEnumerable<BuildingAvailability> items, BuildingSort sort)
    {
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
        return items.OrderByDescending(item => sort == BuildingSort.MostFree ? item.FreeCount : 0)
            .ThenBy(item => item.Building.Name, comparer)
            .ToList();
    }

    /// <summary>
    /// Keeps buildings that match every word through their name or code, or that contain rooms
    /// matching the remaining words. When a building matches on its own, all of its rooms stay.
    /// Free counts are the whole building, not the filtered lists.
    /// </summary>
    public static List<BuildingAvailability> Filter(IEnumerable<BuildingAvailability> items, string query)
    {
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Fold).Where(token => token.Length > 0).ToArray();
        if (tokens.Length == 0) return items.ToList();
        var matched = new List<BuildingAvailability>();
        foreach (var item in items)
        {
            var name = Fold(item.Building.Name);
            var code = Fold(item.Building.Code);
            var nameWords = SplitWords(name);
            var buildingHits = tokens.Select(token =>
                code.StartsWith(token, StringComparison.Ordinal)
                || nameWords.Any(word => word.StartsWith(token, StringComparison.Ordinal))
                || (token.Length >= 3 && name.Contains(token, StringComparison.Ordinal))).ToArray();
            if (buildingHits.All(hit => hit))
            {
                matched.Add(item);
                continue;
            }
            bool Matches(Entry entry)
            {
                var number = Fold(entry.Room.Room);
                for (var i = 0; i < tokens.Length; i++)
                {
                    if (buildingHits[i]) continue;
                    if (number.StartsWith(tokens[i], StringComparison.Ordinal)) continue;
                    if (tokens[i].Length >= 2 && number.Contains(tokens[i], StringComparison.Ordinal)) continue;
                    return false;
                }
                return true;
            }
            var free = item.Free.Where(Matches).ToList();
            var busy = item.Busy.Where(Matches).ToList();
            if (free.Count == 0 && busy.Count == 0) continue;
            matched.Add(new BuildingAvailability
            {
                Building = item.Building,
                Free = free,
                Busy = busy,
                FreeCount = item.FreeCount,
                RoomCount = item.RoomCount,
            });
        }
        return matched;
    }

    public readonly record struct ClassDates(DateOnly Start, DateOnly End)
    {
        /// <summary>
        /// The dates most recurring in-person weekly meetings begin and end on.
        /// Ties go to the lexicographically smaller ISO date.
        /// </summary>
        public static ClassDates? Derive(IEnumerable<CourseSection> sections)
        {
            var starts = new Dictionary<string, int>(StringComparer.Ordinal);
            var ends = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var section in sections)
            {
                foreach (var meeting in section.WeeklyMeetings)
                {
                    if (meeting.IsOnline) continue;
                    if (meeting.StartDate is not string first || meeting.EndDate is not string last) continue;
                    if (string.CompareOrdinal(first, last) >= 0) continue;
                    starts[first] = starts.GetValueOrDefault(first) + 1;
                    ends[last] = ends.GetValueOrDefault(last) + 1;
                }
            }
            var startText = MostCommon(starts);
            var endText = MostCommon(ends);
            if (startText is null || endText is null) return null;
            if (!DateOnly.TryParse(startText, CultureInfo.InvariantCulture, out var start)) return null;
            if (!DateOnly.TryParse(endText, CultureInfo.InvariantCulture, out var end)) return null;
            if (start > end) return null;
            return new ClassDates(start, end);
        }

        public bool Contains(DateOnly date) => date >= Start && date <= End;

        private static string? MostCommon(Dictionary<string, int> counts)
        {
            string? best = null;
            var bestCount = int.MinValue;
            foreach (var pair in counts)
            {
                if (pair.Value > bestCount || (pair.Value == bestCount && (best is null || string.CompareOrdinal(pair.Key, best) < 0)))
                {
                    best = pair.Key;
                    bestCount = pair.Value;
                }
            }
            return best;
        }
    }

    public static string Fold(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(character);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    private static List<string> SplitWords(string folded)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var character in folded)
        {
            if (char.IsLetterOrDigit(character)) current.Append(character);
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0) words.Add(current.ToString());
        return words;
    }
}
