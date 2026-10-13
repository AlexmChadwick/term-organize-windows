// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// The "a seat just opened" rules mirror SearchNEU's section updater
// (https://github.com/sandboxnu/searchneu, packages/scraper/src/update/main.ts,
// Copyright (C) Sandbox at Northeastern University and the SearchNEU contributors,
// licensed under the GNU Affero General Public License v3): a section raises a
// seat alert when it goes from no seats to some, a waitlist alert when a full
// waitlist gains room, and a stored CRN that Banner no longer lists is reported
// as missing.
// Modified 2026-10-13: ported from Swift as a pure function that feeds notifications.

namespace TermOrganize.Core;

/// <summary>A watched section gained a seat or a waitlist spot.</summary>
public sealed class WatchlistAlert : IEquatable<WatchlistAlert>
{
    public enum Kind
    {
        Seats,
        Waitlist,
    }

    public Kind AlertKind { get; init; }
    public required string TermCode { get; init; }
    public required string Crn { get; init; }
    public required CourseKey CourseKey { get; init; }
    public required string CourseTitle { get; init; }
    /// <summary>Lecture 02.</summary>
    public required string SectionName { get; init; }
    public int SeatsRemaining { get; init; }
    public int SeatCapacity { get; init; }
    public int WaitlistRemaining { get; init; }

    public string Title => AlertKind switch
    {
        Kind.Seats => $"Seats open in {CourseKey}",
        Kind.Waitlist => $"Waitlist spot open in {CourseKey}",
        _ => "",
    };

    public string Body => AlertKind switch
    {
        Kind.Seats => SeatsBody(),
        Kind.Waitlist => WaitlistBody(),
        _ => "",
    };

    /// <summary>One delivered notification per section and kind; a repeat replaces the earlier one.</summary>
    public string Identifier => $"watch-{KindName}-{TermCode}-{Crn}";

    public string ThreadIdentifier => $"course-{TermCode}-{CourseKey}";

    public Uri Url => CourseLinks.Section(TermCode, Crn);

    private string KindName => AlertKind == Kind.Waitlist ? "waitlist" : "seats";

    private string SeatsBody()
    {
        var seats = SeatCapacity > 0
            ? $"{SeatsRemaining} of {SeatCapacity} seats"
            : SeatsRemaining == 1 ? "1 seat" : $"{SeatsRemaining} seats";
        return $"{seats} in {SectionName} (CRN {Crn}). Register soon!";
    }

    private string WaitlistBody()
    {
        var spots = WaitlistRemaining == 1 ? "1 waitlist spot" : $"{WaitlistRemaining} waitlist spots";
        return $"{spots} open in {SectionName} (CRN {Crn}). Join soon!";
    }

    public bool Equals(WatchlistAlert? other) =>
        other is not null && AlertKind == other.AlertKind && TermCode == other.TermCode && Crn == other.Crn
        && CourseKey == other.CourseKey && CourseTitle == other.CourseTitle && SectionName == other.SectionName
        && SeatsRemaining == other.SeatsRemaining && SeatCapacity == other.SeatCapacity && WaitlistRemaining == other.WaitlistRemaining;

    public override bool Equals(object? obj) => Equals(obj as WatchlistAlert);
    public override int GetHashCode() => HashCode.Combine(AlertKind, TermCode, Crn, CourseKey);
}

public static class WatchlistComparison
{
    public sealed class Outcome
    {
        public required List<WatchItem> Items { get; init; }
        public required List<WatchlistAlert> Alerts { get; init; }
        public required HashSet<string> MissingIds { get; init; }
        public required HashSet<string> CheckedTermCodes { get; init; }
    }

    /// <summary>
    /// Compares the watchlist with freshly fetched sections. Terms absent from live
    /// (a failed request, for example) leave their items untouched.
    /// </summary>
    public static Outcome Compare(
        IReadOnlyList<WatchItem> items,
        IReadOnlyDictionary<string, IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>> live,
        DateTimeOffset? now = null)
    {
        var moment = now ?? DateTimeOffset.UtcNow;
        var updated = items.Select(Clone).ToList();
        var alerts = new List<WatchlistAlert>();
        var missing = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < updated.Count; index++)
        {
            var item = updated[index];
            if (!live.TryGetValue(item.TermCode, out var termSections)) continue;
            CourseSection? section = null;
            if (termSections.TryGetValue(item.CourseKey, out var courseSections))
                section = courseSections.FirstOrDefault(candidate => candidate.Crn == item.Crn);
            if (section is null)
            {
                missing.Add(item.Id);
                item.LastCheckedAt = moment;
                continue;
            }
            var seatsOpened = item.LastSeatsRemaining <= 0 && section.SeatsRemaining > 0;
            var waitlistOpened = item.NotifyOnWaitlist
                && !seatsOpened
                && section.SeatsRemaining <= 0
                && item.LastWaitlistRemaining <= 0
                && section.WaitlistRemaining > 0;
            item.LastSeatsRemaining = section.SeatsRemaining;
            item.LastWaitlistRemaining = section.WaitlistRemaining;
            item.SeatCapacity = section.SeatCapacity;
            item.LastCheckedAt = moment;
            if (seatsOpened || waitlistOpened)
            {
                item.LastNotifiedAt = moment;
                alerts.Add(new WatchlistAlert
                {
                    AlertKind = seatsOpened ? WatchlistAlert.Kind.Seats : WatchlistAlert.Kind.Waitlist,
                    TermCode = item.TermCode,
                    Crn = item.Crn,
                    CourseKey = item.CourseKey,
                    CourseTitle = item.CourseTitle,
                    SectionName = SectionName(section),
                    SeatsRemaining = section.SeatsRemaining,
                    SeatCapacity = section.SeatCapacity,
                    WaitlistRemaining = section.WaitlistRemaining,
                });
            }
        }
        return new Outcome
        {
            Items = updated,
            Alerts = alerts,
            MissingIds = missing,
            CheckedTermCodes = live.Keys.ToHashSet(StringComparer.Ordinal),
        };
    }

    /// <summary>Lecture 02, or just the class type when there is no section number.</summary>
    public static string SectionName(CourseSection section)
    {
        var number = section.SectionNumber.Trim();
        return number.Length == 0 ? section.ClassType : $"{section.ClassType} {number}";
    }

    private static WatchItem Clone(WatchItem item) => new()
    {
        TermCode = item.TermCode,
        Crn = item.Crn,
        CourseKey = item.CourseKey,
        CourseTitle = item.CourseTitle,
        SectionLabel = item.SectionLabel,
        AddedAt = item.AddedAt,
        LastSeatsRemaining = item.LastSeatsRemaining,
        LastWaitlistRemaining = item.LastWaitlistRemaining,
        SeatCapacity = item.SeatCapacity,
        LastCheckedAt = item.LastCheckedAt,
        NotifyOnWaitlist = item.NotifyOnWaitlist,
        LastNotifiedAt = item.LastNotifiedAt,
    };
}
