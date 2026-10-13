// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// The "Leads to" list: courses that mention this one among their prerequisites, and whether
// each of them needs it. Being mentioned is not the same as being required.

namespace TermOrganize.Core;

/// <summary>A course whose prerequisites mention another course.</summary>
public sealed class CourseDependent : IEquatable<CourseDependent>
{
    public enum Role
    {
        /// <summary>The prerequisites can't be met without it.</summary>
        Required,
        /// <summary>One of several ways to meet them.</summary>
        Option,
    }

    public required CourseKey Key { get; init; }
    /// <summary>Null when the prerequisites aren't known, or don't mention the other course after all.</summary>
    public Role? Relation { get; init; }
    /// <summary>Whether this term's catalog lists the course, so it has a page to open.</summary>
    public bool IsOffered { get; init; }
    public CourseKey Id => Key;

    public static string Caption(Role role) => role switch
    {
        Role.Required => "Required",
        Role.Option => "One option",
        _ => "",
    };

    public static string SpokenNote(Role role) => role switch
    {
        Role.Required => "required prerequisite",
        Role.Option => "one option among its prerequisites",
        _ => "",
    };

    public bool Equals(CourseDependent? other) =>
        other is not null && Key == other.Key && Relation == other.Relation && IsOffered == other.IsOffered;

    public override bool Equals(object? obj) => Equals(obj as CourseDependent);
    public override int GetHashCode() => HashCode.Combine(Key, Relation, IsOffered);

    /// <summary>
    /// Courses in keys (those that mention the course), required first, then options, then any
    /// whose prerequisites can't be read. Offered courses come before the rest within each group.
    /// </summary>
    public static List<CourseDependent> Arranged(
        IReadOnlyList<CourseKey> keys,
        CourseKey course,
        Func<CourseKey, Requisite?> prerequisites,
        Func<CourseKey, bool> isOffered)
    {
        static int Rank(CourseDependent dependent)
        {
            var roleRank = dependent.Relation switch
            {
                Role.Required => 0,
                Role.Option => 1,
                _ => 2,
            };
            return roleRank * 2 + (dependent.IsOffered ? 0 : 1);
        }
        return keys
            .Select((key, index) => (Item: new CourseDependent
            {
                Key = key,
                Relation = prerequisites(key)?.RoleOf(course),
                IsOffered = isOffered(key),
            }, Index: index))
            .OrderBy(pair => Rank(pair.Item))
            .ThenBy(pair => pair.Index)
            .Select(pair => pair.Item)
            .ToList();
    }
}

public static class CourseDependentRules
{
    /// <summary>
    /// How the course figures in this requisite. Required when it can't be met without the course,
    /// even if every other course it mentions has been taken. Option when some other way meets it.
    /// Null when it isn't mentioned. Placement tests count as met.
    /// </summary>
    public static CourseDependent.Role? RoleOf(this Requisite requisite, CourseKey course)
    {
        var tree = requisite.NormalizedForDisplay();
        if (tree is null) return null;
        var others = tree.ReferencedCourses().ToHashSet();
        if (!others.Remove(course)) return null;
        return tree.IsSatisfied(others, assumeTestsMet: true) ? CourseDependent.Role.Option : CourseDependent.Role.Required;
    }
}
