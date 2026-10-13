// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Portions derived from SearchNEU (packages/scraper/src/types.ts and reqs.ts),
// Copyright (C) Sandbox at Northeastern University and the SearchNEU contributors,
// AGPL-3.0. Modified 2026-10-13: ported from the Swift requisite tree to C#.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TermOrganize.Core;

[JsonConverter(typeof(RequisiteConverter))]
public abstract class Requisite : IEquatable<Requisite>
{
    public static Requisite Course(CourseKey key) => new CourseNode(key);
    public static Requisite Test(string name, double? score = null) => new TestNode(name, score);
    public static Requisite And(IEnumerable<Requisite> items) => new AndNode(items.ToList());
    public static Requisite And(params Requisite[] items) => new AndNode(items.ToList());
    public static Requisite Or(IEnumerable<Requisite> items) => new OrNode(items.ToList());
    public static Requisite Or(params Requisite[] items) => new OrNode(items.ToList());

    public abstract IEnumerable<CourseKey> ReferencedCourses();
    public abstract bool IsSatisfied(HashSet<CourseKey> completed, bool assumeTestsMet = false);
    public bool IsSatisfied(IEnumerable<CourseKey> completed, bool assumeTestsMet = false) =>
        IsSatisfied(completed as HashSet<CourseKey> ?? completed.ToHashSet(), assumeTestsMet);
    public abstract Requisite MapCourses(Func<CourseKey, CourseKey> transform);
    public abstract Requisite? Simplified();
    public abstract string Summary { get; }
    public virtual bool IsCondition => false;

    /// <summary>
    /// The requisite as the course page reads it. A group of one becomes that one,
    /// matching groups merge, and a repeated alternative is kept once. Null when nothing is left.
    /// </summary>
    public Requisite? NormalizedForDisplay() => this switch
    {
        CourseNode or TestNode => this,
        AndNode and => Merging(and.Items, asAnd: true),
        OrNode or => Merging(or.Items, asAnd: false),
        _ => this,
    };

    private static Requisite? Merging(IReadOnlyList<Requisite> items, bool asAnd)
    {
        var members = new List<Requisite>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(Requisite member)
        {
            if (seen.Add(DisplayKey(member))) members.Add(member);
        }
        foreach (var item in items)
        {
            var member = item.NormalizedForDisplay();
            if (member is null) continue;
            if (asAnd && member is AndNode innerAnd)
                foreach (var child in innerAnd.Items) Add(child);
            else if (!asAnd && member is OrNode innerOr)
                foreach (var child in innerOr.Items) Add(child);
            else
                Add(member);
        }
        return members.Count switch
        {
            0 => null,
            1 => members[0],
            _ => asAnd ? And(members) : Or(members),
        };
    }

    private static string DisplayKey(Requisite requisite) => requisite switch
    {
        CourseNode course => "c:" + course.Key,
        TestNode test => "t:" + test.Name + ":" + (test.Score?.ToString("0.################", CultureInfo.InvariantCulture) ?? ""),
        AndNode and => "and(" + string.Join(',', and.Items.Select(DisplayKey).OrderBy(k => k, StringComparer.Ordinal)) + ")",
        OrNode or => "or(" + string.Join(',', or.Items.Select(DisplayKey).OrderBy(k => k, StringComparer.Ordinal)) + ")",
        _ => requisite.Summary,
    };

    public abstract bool Equals(Requisite? other);
    public override bool Equals(object? obj) => Equals(obj as Requisite);
    public override int GetHashCode() => StructuralHash();
    protected abstract int StructuralHash();

    public sealed class CourseNode : Requisite
    {
        public CourseKey Key { get; }
        public CourseNode(CourseKey key) => Key = key;
        public override IEnumerable<CourseKey> ReferencedCourses() => [Key];
        public override bool IsSatisfied(HashSet<CourseKey> completed, bool assumeTestsMet = false) => completed.Contains(Key);
        public override Requisite MapCourses(Func<CourseKey, CourseKey> transform) => Course(transform(Key));
        public override Requisite? Simplified() => this;
        public override string Summary => Key.ToString();
        public override bool Equals(Requisite? other) => other is CourseNode c && c.Key == Key;
        protected override int StructuralHash() => HashCode.Combine(1, Key);
    }

    public sealed class TestNode : Requisite
    {
        public string Name { get; }
        public double? Score { get; }
        public TestNode(string name, double? score) { Name = name; Score = score; }
        public override IEnumerable<CourseKey> ReferencedCourses() => [];
        public override bool IsSatisfied(HashSet<CourseKey> completed, bool assumeTestsMet = false) => assumeTestsMet;
        public override Requisite MapCourses(Func<CourseKey, CourseKey> transform) => this;
        public override Requisite? Simplified() => this;
        public override string Summary => Score is double score
            ? $"{Name} ({score.ToString("0.################", CultureInfo.InvariantCulture)})"
            : Name;
        public override bool Equals(Requisite? other) => other is TestNode t && t.Name == Name && t.Score == Score;
        protected override int StructuralHash() => HashCode.Combine(2, Name, Score);
    }

    public sealed class AndNode : Requisite
    {
        public IReadOnlyList<Requisite> Items { get; }
        public AndNode(List<Requisite> items) => Items = items;
        public override bool IsCondition => true;
        public override IEnumerable<CourseKey> ReferencedCourses() => UniqueCourses(Items);
        public override bool IsSatisfied(HashSet<CourseKey> completed, bool assumeTestsMet = false) =>
            Items.All(i => i.IsSatisfied(completed, assumeTestsMet));
        public override Requisite MapCourses(Func<CourseKey, CourseKey> transform) => And(Items.Select(i => i.MapCourses(transform)));
        public override Requisite? Simplified() => Flatten(Items.Select(i => i.Simplified()), inner => inner is AndNode a ? a.Items : null, And);
        public override string Summary => string.Join(" and ", Items.Select(i => i.IsCondition ? $"({i.Summary})" : i.Summary));
        public override bool Equals(Requisite? other) => other is AndNode a && Items.SequenceEqual(a.Items);
        protected override int StructuralHash() => HashItems(3, Items);
    }

    public sealed class OrNode : Requisite
    {
        public IReadOnlyList<Requisite> Items { get; }
        public OrNode(List<Requisite> items) => Items = items;
        public override bool IsCondition => true;
        public override IEnumerable<CourseKey> ReferencedCourses() => UniqueCourses(Items);
        public override bool IsSatisfied(HashSet<CourseKey> completed, bool assumeTestsMet = false) =>
            Items.Any(i => i.IsSatisfied(completed, assumeTestsMet));
        public override Requisite MapCourses(Func<CourseKey, CourseKey> transform) => Or(Items.Select(i => i.MapCourses(transform)));
        public override Requisite? Simplified() => Flatten(Items.Select(i => i.Simplified()), inner => inner is OrNode o ? o.Items : null, Or);
        public override string Summary => string.Join(" or ", Items.Select(i => i.IsCondition ? $"({i.Summary})" : i.Summary));
        public override bool Equals(Requisite? other) => other is OrNode o && Items.SequenceEqual(o.Items);
        protected override int StructuralHash() => HashItems(4, Items);
    }

    private static IEnumerable<CourseKey> UniqueCourses(IEnumerable<Requisite> items)
    {
        var seen = new HashSet<CourseKey>();
        foreach (var item in items)
            foreach (var key in item.ReferencedCourses())
                if (seen.Add(key)) yield return key;
    }

    private static Requisite? Flatten(IEnumerable<Requisite?> simplified, Func<Requisite, IReadOnlyList<Requisite>?> inner, Func<IEnumerable<Requisite>, Requisite> wrap)
    {
        var flat = new List<Requisite>();
        foreach (var child in simplified)
        {
            if (child is null) continue;
            var nested = inner(child);
            if (nested is not null) flat.AddRange(nested);
            else flat.Add(child);
        }
        return flat.Count switch
        {
            0 => null,
            1 => flat[0],
            _ => wrap(flat),
        };
    }

    private static int HashItems(int tag, IReadOnlyList<Requisite> items)
    {
        var hash = new HashCode();
        hash.Add(tag);
        foreach (var item in items) hash.Add(item);
        return hash.ToHashCode();
    }
}

public sealed class RequisiteConverter : JsonConverter<Requisite>
{
    public override Requisite? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return ReadElement(doc.RootElement);
    }

    public static Requisite? ReadElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (element.TryGetProperty("and", out var and) && and.ValueKind == JsonValueKind.Array)
            return Requisite.And(and.EnumerateArray().Select(ReadElement).OfType<Requisite>());
        if (element.TryGetProperty("or", out var or) && or.ValueKind == JsonValueKind.Array)
            return Requisite.Or(or.EnumerateArray().Select(ReadElement).OfType<Requisite>());
        if (element.TryGetProperty("test", out var test) && test.ValueKind == JsonValueKind.String)
        {
            double? score = element.TryGetProperty("score", out var scoreEl) && scoreEl.ValueKind == JsonValueKind.Number
                ? scoreEl.GetDouble() : null;
            return Requisite.Test(test.GetString() ?? "", score);
        }
        var subject = element.TryGetProperty("s", out var s) ? s.GetString() : null;
        var number = element.TryGetProperty("n", out var n) ? n.GetString() : null;
        if (subject is null || number is null) return null;
        return Requisite.Course(new CourseKey(subject, number));
    }

    public override void Write(Utf8JsonWriter writer, Requisite value, JsonSerializerOptions options) =>
        WriteReq(writer, value);

    public static void WriteReq(Utf8JsonWriter writer, Requisite value)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case Requisite.CourseNode course:
                writer.WriteString("s", course.Key.Subject);
                writer.WriteString("n", course.Key.Number);
                break;
            case Requisite.TestNode test:
                writer.WriteString("test", test.Name);
                if (test.Score is double score) writer.WriteNumber("score", score);
                break;
            case Requisite.AndNode and:
                writer.WritePropertyName("and");
                writer.WriteStartArray();
                foreach (var item in and.Items) WriteReq(writer, item);
                writer.WriteEndArray();
                break;
            case Requisite.OrNode or:
                writer.WritePropertyName("or");
                writer.WriteStartArray();
                foreach (var item in or.Items) WriteReq(writer, item);
                writer.WriteEndArray();
                break;
        }
        writer.WriteEndObject();
    }
}
