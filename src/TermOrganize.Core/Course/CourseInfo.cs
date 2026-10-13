// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// What Banner's course catalog says about a course, read for a course key and a term
/// without needing a section (a CRN).
/// A term's schedule only lists courses that have sections that term. The catalog is
/// effective-dated: it lists every course active in the term, offered or not.
/// </summary>
public sealed class CourseInfo : IEquatable<CourseInfo>
{
    public CourseKey Key { get; }
    public string Title { get; }
    /// <summary>Credit hours; null when the catalog entry doesn't say.</summary>
    public double? MinCredits { get; }
    public double? MaxCredits { get; }
    public Requisite? Prerequisites { get; }
    public Requisite? Corequisites { get; }
    /// <summary>
    /// Courses that count as this one: the ones Banner says credit can't be earned alongside
    /// (mutual exclusions). Sorted, and never the course itself.
    /// </summary>
    public IReadOnlyList<CourseKey> Equivalents { get; }
    /// <summary>The term whose catalog the entry was read from, for example 202630.</summary>
    public string SourceTerm { get; }
    /// <summary>
    /// True when only catalogs older than the newest term list it, false when the newest
    /// term's catalog does, null when that wasn't checked.
    /// </summary>
    public bool? IsRetired { get; }
    public DateTimeOffset FetchedAt { get; }

    public CourseInfo(
        CourseKey key,
        string title,
        double? minCredits = null,
        double? maxCredits = null,
        Requisite? prerequisites = null,
        Requisite? corequisites = null,
        IEnumerable<CourseKey>? equivalents = null,
        string sourceTerm = "",
        bool? isRetired = null,
        DateTimeOffset? fetchedAt = null)
    {
        Key = key;
        Title = title;
        MinCredits = minCredits;
        MaxCredits = maxCredits;
        Prerequisites = prerequisites;
        Corequisites = corequisites;
        Equivalents = (equivalents ?? []).Where(item => item != key).Distinct().OrderBy(item => item).ToList();
        SourceTerm = sourceTerm;
        IsRetired = isRetired;
        FetchedAt = fetchedAt ?? DateTimeOffset.UtcNow;
    }

    /// <summary>4 credits, 1–4 credits; null when the credit hours are unknown.</summary>
    public string? CreditsLabel
    {
        get
        {
            var low = MinCredits ?? MaxCredits;
            if (low is not double value) return null;
            return CreditFormatter.Label(value, MaxCredits ?? value);
        }
    }

    public bool Equals(CourseInfo? other) =>
        other is not null && Key == other.Key && Title == other.Title && MinCredits == other.MinCredits &&
        MaxCredits == other.MaxCredits && Equals(Prerequisites, other.Prerequisites) && Equals(Corequisites, other.Corequisites) &&
        Equivalents.SequenceEqual(other.Equivalents) && SourceTerm == other.SourceTerm && IsRetired == other.IsRetired &&
        FetchedAt == other.FetchedAt;

    public override bool Equals(object? obj) => Equals(obj as CourseInfo);
    public override int GetHashCode() => HashCode.Combine(Key, Title, SourceTerm, FetchedAt);
}
