// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// Outer optional of Swift's <c>Requisite??</c>.
/// <see cref="IsKnown"/> is false when prerequisites were not looked up.
/// When known, a null <see cref="Requisite"/> means the course has none.
/// </summary>
public readonly record struct RequisiteAnswer(bool IsKnown, Requisite? Requisite)
{
    public static RequisiteAnswer Unknown => new(false, null);
    public static RequisiteAnswer None => new(true, null);
    public static RequisiteAnswer Known(Requisite requisite) => new(true, requisite);
}

/// <summary>Prerequisite checks for courses in a degree plan.</summary>
public static class DegreePlanAnalysis
{
    public abstract class PrerequisiteStatus : IEquatable<PrerequisiteStatus>
    {
        public static PrerequisiteStatus Met { get; } = new MetStatus();
        public static PrerequisiteStatus Unknown { get; } = new UnknownStatus();
        public static PrerequisiteStatus Missing(string summary) => new MissingStatus(summary);
        public bool IsMissing => this is MissingStatus;
        public string? MissingText => this is MissingStatus missing ? missing.Text : null;

        public abstract bool Equals(PrerequisiteStatus? other);
        public override bool Equals(object? obj) => Equals(obj as PrerequisiteStatus);
        public abstract override int GetHashCode();
        public static bool operator ==(PrerequisiteStatus? left, PrerequisiteStatus? right) =>
            left is null ? right is null : left.Equals(right);
        public static bool operator !=(PrerequisiteStatus? left, PrerequisiteStatus? right) => !(left == right);

        private sealed class MetStatus : PrerequisiteStatus
        {
            public override bool Equals(PrerequisiteStatus? other) => other is MetStatus;
            public override int GetHashCode() => 1;
            public override string ToString() => "met";
        }

        private sealed class UnknownStatus : PrerequisiteStatus
        {
            public override bool Equals(PrerequisiteStatus? other) => other is UnknownStatus;
            public override int GetHashCode() => 3;
            public override string ToString() => "unknown";
        }

        private sealed class MissingStatus : PrerequisiteStatus
        {
            public string Text { get; }
            public MissingStatus(string text) => Text = text;
            public override bool Equals(PrerequisiteStatus? other) => other is MissingStatus missing && missing.Text == Text;
            public override int GetHashCode() => HashCode.Combine(2, Text);
            public override string ToString() => Text;
        }
    }

    public static HashSet<CourseKey> CoursesBefore(int termIndex, DegreePlan plan)
    {
        var keys = new HashSet<CourseKey>(plan.CompletedKeys);
        var limit = Math.Max(0, termIndex);
        for (var i = 0; i < plan.Terms.Count && i < limit; i++)
            foreach (var course in plan.Terms[i].Courses)
                if (course.Key is CourseKey key) keys.Add(key);
        return keys;
    }

    /// <summary>
    /// Prerequisites of a planned course. A catalog entry is used only when its details are loaded.
    /// <paramref name="lookup"/> answers <see cref="RequisiteAnswer.Unknown"/> when the course is unknown
    /// and <see cref="RequisiteAnswer.None"/> when it has no prerequisites.
    /// </summary>
    public static PrerequisiteStatus ForCourse(
        CourseKey key,
        int termIndex,
        DegreePlan plan,
        CatalogIndex? catalog,
        Func<CourseKey, RequisiteAnswer>? lookup = null,
        CourseEquivalences? equivalences = null)
    {
        var taken = CoursesBefore(termIndex, plan);
        var counting = (equivalences ?? CourseEquivalences.None).Merging(plan.DeclaredEquivalences);
        var listed = catalog?.Course(key);
        if (listed is { DetailsLoaded: true }) return Status(listed.Prereqs, taken, counting);
        if (lookup is not null)
        {
            var known = lookup(key);
            if (known.IsKnown) return Status(known.Requisite, taken, counting);
        }
        return PrerequisiteStatus.Unknown;
    }

    public static PrerequisiteStatus Status(Requisite? requisite, IEnumerable<CourseKey> satisfied, CourseEquivalences? equivalences = null)
    {
        if (requisite is null) return PrerequisiteStatus.Met;
        var missing = UnmetSummary(requisite, satisfied, equivalences);
        return missing is null ? PrerequisiteStatus.Met : PrerequisiteStatus.Missing(missing);
    }

    public static Dictionary<CourseKey, CourseKey> CoveringEquivalents(Requisite requisite, IEnumerable<CourseKey> taken, CourseEquivalences equivalences)
    {
        var set = taken as HashSet<CourseKey> ?? taken.ToHashSet();
        var covering = new Dictionary<CourseKey, CourseKey>();
        foreach (var key in requisite.ReferencedCourses())
        {
            if (set.Contains(key)) continue;
            if (equivalences.Satisfier(key, set) is CourseKey equivalent) covering[key] = equivalent;
        }
        return covering;
    }

    /// <summary>
    /// The unmet part of a requisite, kept short. Placement tests are treated as met.
    /// Equivalent courses expand the taken set.
    /// </summary>
    public static string? UnmetSummary(Requisite requisite, IEnumerable<CourseKey> satisfied, CourseEquivalences? equivalences = null) =>
        Unmet(requisite, (equivalences ?? CourseEquivalences.None).Expanding(satisfied));

    public static int MissingCount(Requisite requisite, IEnumerable<CourseKey> satisfied, CourseEquivalences? equivalences = null) =>
        Count(requisite, (equivalences ?? CourseEquivalences.None).Expanding(satisfied));

    public static HashSet<CourseKey> DuplicateKeys(DegreePlan plan)
    {
        var planned = plan.Terms.SelectMany(term => term.Courses).ToList();
        var duplicates = new HashSet<CourseKey>();
        for (var index = 0; index < planned.Count; index++)
        {
            var course = planned[index];
            if (course.Key is not CourseKey key) continue;
            var repeatsPlanned = false;
            for (var earlier = 0; earlier < index && !repeatsPlanned; earlier++)
                repeatsPlanned = planned[earlier].Key == key && !CountsSeparately(planned[earlier], course);
            var repeatsCompleted = plan.Completed.Any(done => done.Key == key && !CountsSeparately(done, course));
            if (repeatsPlanned || repeatsCompleted) duplicates.Add(key);
        }
        var completed = plan.Completed;
        for (var index = 0; index < completed.Count; index++)
        {
            var course = completed[index];
            if (course.Key is not CourseKey key) continue;
            var repeatsEarlier = false;
            for (var earlier = 0; earlier < index && !repeatsEarlier; earlier++)
                repeatsEarlier = completed[earlier].Key == key && !CountsSeparately(completed[earlier], course);
            if (repeatsEarlier) duplicates.Add(key);
        }
        return duplicates;
    }

    private static string? Unmet(Requisite requisite, HashSet<CourseKey> satisfied)
    {
        switch (requisite)
        {
            case Requisite.CourseNode course:
                return satisfied.Contains(course.Key) ? null : course.Key.ToString();
            case Requisite.TestNode:
                return null;
            case Requisite.AndNode and:
                var parts = Unique(and.Items.Select(item => Unmet(item, satisfied)).OfType<string>());
                return parts.Count switch
                {
                    0 or 1 => parts.FirstOrDefault(),
                    2 or 3 => string.Join(" and ", parts.Select(ParenthesizedIfCompound)),
                    _ => string.Join(", ", parts.Take(2).Select(ParenthesizedIfCompound)) + $" and {parts.Count - 2} more",
                };
            case Requisite.OrNode or:
                if (or.Items.Any(item => Unmet(item, satisfied) is null)) return null;
                var courses = new List<CourseKey>();
                var allCourses = true;
                foreach (var item in or.Items)
                {
                    if (item is Requisite.CourseNode course) courses.Add(course.Key);
                    else allCourses = false;
                }
                if (allCourses)
                {
                    var names = Unique(courses.Select(key => key.ToString()));
                    return names.Count <= 3
                        ? string.Join(" or ", names)
                        : $"{names[0]}, {names[1]} or {names.Count - 2} others";
                }
                Requisite? best = null;
                var bestCount = int.MaxValue;
                foreach (var item in or.Items)
                {
                    var count = Count(item, satisfied);
                    if (count < bestCount)
                    {
                        bestCount = count;
                        best = item;
                    }
                }
                if (best is null || Unmet(best, satisfied) is not string text) return requisite.Summary;
                return ParenthesizedIfCompound(text) + " or another option";
            default:
                return requisite.Summary;
        }
    }

    private static int Count(Requisite requisite, HashSet<CourseKey> satisfied) => requisite switch
    {
        Requisite.CourseNode course => satisfied.Contains(course.Key) ? 0 : 1,
        Requisite.TestNode => 0,
        Requisite.AndNode and => and.Items.Sum(item => Count(item, satisfied)),
        Requisite.OrNode or => or.Items.Count == 0 ? 0 : or.Items.Min(item => Count(item, satisfied)),
        _ => 1,
    };

    private static string ParenthesizedIfCompound(string text) =>
        text.Contains(" or ", StringComparison.Ordinal) || text.Contains(" and ", StringComparison.Ordinal) || text.EndsWith(" more", StringComparison.Ordinal)
            ? $"({text})"
            : text;

    private static List<string> Unique(IEnumerable<string> texts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var text in texts)
            if (seen.Add(text)) result.Add(text);
        return result;
    }

    /// <summary>Same rule as <c>DegreeCourseRepeats</c>: another topic, or another manual CRN, is not a repeat.</summary>
    private static bool CountsSeparately(DegreePlanCourse course, DegreePlanCourse other) =>
        DegreeCourseRepeats.CountsSeparately(course, other);
}
