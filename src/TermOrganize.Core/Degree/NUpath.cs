// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Northeastern's NUpath requirements, checked from the NUpath attributes Banner gives a plan's
// courses. Term Organize is not affiliated with Northeastern University.

namespace TermOrganize.Core;

/// <summary>
/// The NUpath attributes of courses, as Banner gives their sections, for checking a plan's NUpath requirements.
/// </summary>
public sealed class DegreeCourseAttributes : IEquatable<DegreeCourseAttributes>
{
    /// <summary>By course, the NUpath attributes its sections carry (empty when none do).</summary>
    public Dictionary<CourseKey, HashSet<NUPath>> Nupaths { get; }

    /// <summary>Courses still being looked up.</summary>
    public HashSet<CourseKey> Loading { get; }

    public static DegreeCourseAttributes None { get; } = new();

    public DegreeCourseAttributes()
    {
        Nupaths = [];
        Loading = [];
    }

    public DegreeCourseAttributes(IEnumerable<KeyValuePair<CourseKey, IEnumerable<NUPath>>> nupaths, IEnumerable<CourseKey>? loading = null)
    {
        Nupaths = nupaths.ToDictionary(pair => pair.Key, pair => pair.Value.ToHashSet());
        Loading = loading?.ToHashSet() ?? [];
    }

    /// <summary>Nothing was looked up, so NUpath isn't checked at all.</summary>
    public bool IsEmpty => Nupaths.Count == 0 && Loading.Count == 0;

    public bool Equals(DegreeCourseAttributes? other)
    {
        if (other is null || Nupaths.Count != other.Nupaths.Count || Loading.Count != other.Loading.Count) return false;
        if (!Loading.SetEquals(other.Loading)) return false;
        foreach (var (key, paths) in Nupaths)
        {
            if (!other.Nupaths.TryGetValue(key, out var theirs) || !paths.SetEquals(theirs)) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as DegreeCourseAttributes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var key in Nupaths.Keys.OrderBy(key => key))
        {
            hash.Add(key);
            foreach (var path in Nupaths[key].OrderBy(path => path)) hash.Add(path);
        }
        foreach (var key in Loading.OrderBy(key => key)) hash.Add(key);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Northeastern's NUpath requirements, as the catalog states them for students who entered in fall 2016 or later.
/// One course each for eight areas, where a single course can count for up to two of them; writing; experience; and a capstone.
/// Writing-intensive, capstone, and experience are not limited.
/// </summary>
public sealed class NUpathRequirement : IEquatable<NUpathRequirement>
{
    public NUPath Path { get; }
    /// <summary>Courses needed (two writing-intensive courses, one of everything else).</summary>
    public int Count { get; }
    /// <summary>One of the eight areas a course counts for at most two of.</summary>
    public bool IsLimited { get; }

    public NUPath Id => Path;

    public NUpathRequirement(NUPath path, int count, bool isLimited)
    {
        Path = path;
        Count = count;
        IsLimited = isLimited;
    }

    /// <summary>In catalog order.</summary>
    public static IReadOnlyList<NUpathRequirement> All { get; } =
    [
        new(NUPath.Nd, 1, true), new(NUPath.Ei, 1, true), new(NUPath.Ic, 1, true), new(NUPath.Fq, 1, true),
        new(NUPath.Si, 1, true), new(NUPath.Ad, 1, true), new(NUPath.Dd, 1, true), new(NUPath.Er, 1, true),
        new(NUPath.Wf, 1, false), new(NUPath.Wi, 2, false), new(NUPath.Wd, 1, false),
        new(NUPath.Ex, 1, false), new(NUPath.Ce, 1, false),
    ];

    /// <summary>How many of the limited areas one course can count for.</summary>
    public const int AreasPerCourse = 2;

    /// <summary><c>Interpreting Culture (IC)</c>.</summary>
    public string Title => $"{Path.Name()} ({Path.Short()})";

    /// <summary>
    /// The requirement a catalog sentence says is satisfied through co-op, by its catalog name or code.
    /// </summary>
    public static NUPath? SatisfiedThroughCoop(string note)
    {
        if (!DegreeAudit.IsCoopNote(note)) return null;
        (NUPath Path, string[] Names)[] names =
        [
            (NUPath.Ex, ["Integrating Knowledge and Skills Through Experience", "Integration Experience", "Integration of Experience"]),
            (NUPath.Ce, ["Demonstrating Thought and Action in a Capstone", "Capstone Experience"]),
        ];
        foreach (var (path, candidates) in names)
        {
            if (candidates.Any(candidate => note.Contains(candidate, StringComparison.OrdinalIgnoreCase))) return path;
        }
        foreach (var path in Enum.GetValues<NUPath>())
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(note, $@"\b{path.Short()}\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                return path;
        }
        return null;
    }

    public bool Equals(NUpathRequirement? other) => other is not null && Path == other.Path && Count == other.Count && IsLimited == other.IsLimited;
    public override bool Equals(object? obj) => Equals(obj as NUpathRequirement);
    public override int GetHashCode() => HashCode.Combine(Path, Count, IsLimited);
}

/// <summary>
/// Which plan courses meet which NUpath requirements: every requirement that can be met is, with completed courses wherever they can be used.
/// The eight limited areas are matched so no assignment meets more areas, and none that meets as many uses more completed courses.
/// </summary>
public sealed class NUpathMatching
{
    public sealed class Course
    {
        public Course(string id, IEnumerable<NUPath> paths, bool isCompleted)
        {
            Id = id;
            Paths = paths.ToHashSet();
            IsCompleted = isCompleted;
        }

        public string Id { get; }
        public HashSet<NUPath> Paths { get; }
        public bool IsCompleted { get; }
    }

    private readonly Dictionary<NUPath, List<string>> _assigned = [];

    /// <param name="courses">The plan's courses in plan order (completed first).</param>
    /// <param name="coopCounts">Requirements a co-op satisfies, with the plan's co-ops.</param>
    public NUpathMatching(
        IReadOnlyList<Course> courses,
        IReadOnlyList<NUpathRequirement>? requirements = null,
        IReadOnlyDictionary<NUPath, IReadOnlyList<Course>>? coopCounts = null)
    {
        requirements ??= NUpathRequirement.All;
        coopCounts ??= new Dictionary<NUPath, IReadOnlyList<Course>>();
        var limited = requirements.Where(requirement => requirement.IsLimited).Select(requirement => requirement.Path).ToList();
        foreach (var (path, ids) in MatchLimited(limited, courses)) _assigned[path] = ids;
        foreach (var requirement in requirements.Where(requirement => !requirement.IsLimited))
        {
            var candidates = courses.Where(course => course.Paths.Contains(requirement.Path)).ToList();
            if (coopCounts.TryGetValue(requirement.Path, out var coops)) candidates.AddRange(coops);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<Course>();
            foreach (var course in candidates.Where(course => course.IsCompleted).Concat(candidates.Where(course => !course.IsCompleted)))
            {
                if (seen.Add(course.Id)) ordered.Add(course);
            }
            _assigned[requirement.Path] = ordered.Take(requirement.Count).Select(course => course.Id).ToList();
        }
    }

    public IReadOnlyList<string> Courses(NUPath path) =>
        _assigned.TryGetValue(path, out var courses) ? courses : [];

    /// <summary>Kuhn's augmenting paths from areas to course places (two per course).</summary>
    private static Dictionary<NUPath, List<string>> MatchLimited(IReadOnlyList<NUPath> areas, IReadOnlyList<Course> courses)
    {
        var load = Enumerable.Range(0, courses.Count).Select(_ => new List<NUPath>()).ToArray();
        var owner = new Dictionary<NUPath, int>();

        bool Augment(NUPath area, Func<int, bool> allowed, HashSet<int> visited)
        {
            for (var index = 0; index < courses.Count; index++)
            {
                if (!allowed(index) || !courses[index].Paths.Contains(area) || !visited.Add(index)) continue;
                if (load[index].Count < NUpathRequirement.AreasPerCourse)
                {
                    load[index].Add(area);
                    owner[area] = index;
                    return true;
                }
                foreach (var other in load[index].ToList())
                {
                    if (!Augment(other, allowed, visited)) continue;
                    load[index].RemoveAll(path => path == other);
                    load[index].Add(area);
                    owner[area] = index;
                    return true;
                }
            }
            return false;
        }

        Func<int, bool>[] passes = [index => courses[index].IsCompleted, _ => true];
        foreach (var allowed in passes)
        {
            foreach (var area in areas)
            {
                if (owner.ContainsKey(area)) continue;
                Augment(area, allowed, []);
            }
        }
        return owner.ToDictionary(pair => pair.Key, pair => new List<string> { courses[pair.Value].Id });
    }
}
