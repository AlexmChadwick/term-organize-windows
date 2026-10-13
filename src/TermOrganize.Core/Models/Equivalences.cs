// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TermOrganize.Core;

/// <summary>
/// Courses that count as one another. Groups are symmetric and transitive.
/// </summary>
[JsonConverter(typeof(CourseEquivalencesConverter))]
public sealed class CourseEquivalences : IEquatable<CourseEquivalences>
{
    public static CourseEquivalences None { get; } = new();

    public IReadOnlyList<IReadOnlyList<CourseKey>> Groups { get; }
    private readonly Dictionary<CourseKey, int> _index;

    public CourseEquivalences()
    {
        Groups = [];
        _index = [];
    }

    public CourseEquivalences(IEnumerable<IEnumerable<CourseKey>> groups)
    {
        var sets = new DisjointSets();
        foreach (var group in groups)
        {
            CourseKey? first = null;
            foreach (var key in group)
            {
                if (first is null) { first = key; sets.Add(key); }
                else sets.Union(first.Value, key);
            }
        }
        var buckets = new Dictionary<CourseKey, List<CourseKey>>();
        foreach (var key in sets.Keys)
        {
            var root = sets.Find(key);
            if (!buckets.TryGetValue(root, out var list)) buckets[root] = list = [];
            list.Add(key);
        }
        var built = buckets.Values
            .Where(g => g.Count > 1)
            .Select(g => (IReadOnlyList<CourseKey>)g.OrderBy(k => k).ToList())
            .OrderBy(g => g[0])
            .ToList();
        var index = new Dictionary<CourseKey, int>();
        for (var i = 0; i < built.Count; i++)
            foreach (var key in built[i]) index[key] = i;
        Groups = built;
        _index = index;
    }

    public CourseEquivalences(IEnumerable<(CourseKey, CourseKey)> pairs)
        : this(pairs.Select(p => new[] { p.Item1, p.Item2 })) { }

    public static CourseEquivalences FromDeclared(IEnumerable<CourseEquivalence> declared) =>
        new(declared.Select(e => (e.Course, e.CountsAs)));

    public bool IsEmpty => Groups.Count == 0;
    public IReadOnlyCollection<CourseKey> Courses => _index.Keys;

    public HashSet<CourseKey> EquivalentsOf(CourseKey key)
    {
        if (!_index.TryGetValue(key, out var i)) return [];
        var members = Groups[i].ToHashSet();
        members.Remove(key);
        return members;
    }

    public IReadOnlyList<CourseKey> GroupOf(CourseKey key) =>
        _index.TryGetValue(key, out var i) ? Groups[i] : [key];

    public bool AreEquivalent(CourseKey a, CourseKey b)
    {
        if (a == b) return true;
        return _index.TryGetValue(a, out var i) && _index.TryGetValue(b, out var j) && i == j;
    }

    public HashSet<CourseKey> Expanding(IEnumerable<CourseKey> taken)
    {
        var result = taken.ToHashSet();
        if (IsEmpty) return result;
        var seen = new HashSet<int>();
        foreach (var key in taken)
        {
            if (!_index.TryGetValue(key, out var i) || !seen.Add(i)) continue;
            foreach (var member in Groups[i]) result.Add(member);
        }
        return result;
    }

    public List<CourseKey> Satisfiers(CourseKey required, HashSet<CourseKey> taken)
    {
        var result = new List<CourseKey>();
        if (taken.Contains(required)) result.Add(required);
        if (_index.TryGetValue(required, out var i))
            result.AddRange(Groups[i].Where(k => k != required && taken.Contains(k)));
        return result;
    }

    public CourseKey? Satisfier(CourseKey required, HashSet<CourseKey> taken)
    {
        if (taken.Contains(required)) return required;
        if (!_index.TryGetValue(required, out var i)) return null;
        foreach (var key in Groups[i])
            if (taken.Contains(key)) return key;
        return null;
    }

    public CourseEquivalences Merging(CourseEquivalences other)
    {
        if (other.IsEmpty) return this;
        if (IsEmpty) return other;
        return new CourseEquivalences(Groups.Concat(other.Groups));
    }

    public CourseEquivalences Adding(CourseKey a, CourseKey b) =>
        Merging(new CourseEquivalences([(a, b)]));

    public bool Equals(CourseEquivalences? other)
    {
        if (other is null || Groups.Count != other.Groups.Count) return false;
        for (var i = 0; i < Groups.Count; i++)
            if (!Groups[i].SequenceEqual(other.Groups[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as CourseEquivalences);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var group in Groups)
            foreach (var key in group) hash.Add(key);
        return hash.ToHashCode();
    }

    private sealed class DisjointSets
    {
        private readonly Dictionary<CourseKey, CourseKey> _parent = [];
        public IEnumerable<CourseKey> Keys => _parent.Keys;
        public void Add(CourseKey key) { if (!_parent.ContainsKey(key)) _parent[key] = key; }
        public CourseKey Find(CourseKey key)
        {
            Add(key);
            var root = key;
            while (_parent[root] != root) root = _parent[root];
            var node = key;
            while (_parent[node] != root)
            {
                var next = _parent[node];
                _parent[node] = root;
                node = next;
            }
            return root;
        }
        public void Union(CourseKey a, CourseKey b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA == rootB) return;
            if (rootA.CompareTo(rootB) < 0) _parent[rootB] = rootA;
            else _parent[rootA] = rootB;
        }
    }
}

public sealed class CourseEquivalencesConverter : JsonConverter<CourseEquivalences>
{
    public override CourseEquivalences? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var groups = JsonSerializer.Deserialize<List<List<CourseKey>>>(ref reader, options) ?? [];
        return new CourseEquivalences(groups);
    }

    public override void Write(Utf8JsonWriter writer, CourseEquivalences value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var group in value.Groups)
        {
            writer.WriteStartArray();
            foreach (var key in group)
                JsonSerializer.Serialize(writer, key, options);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
    }
}

public sealed class CourseEquivalence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CourseKey Course { get; set; }
    public CourseKey CountsAs { get; set; }
    public string Note { get; set; } = "";
}
