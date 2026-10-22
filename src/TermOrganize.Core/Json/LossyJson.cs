// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TermOrganize.Core;

public sealed class DegreePlanConverter : JsonConverter<DegreePlan>
{
    public override DegreePlan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Degree plan must be an object.");
        var inner = Without<DegreePlanConverter>(options);
        var plan = new DegreePlan
        {
            Id = GuidOrNew(root, "id"),
            Revision = IntOr(root, "revision"),
            Name = StringOr(root, "name", "Degree Plan"),
            Program = StringOrNull(root, "program"),
            StudentName = StringOrNull(root, "studentName"),
            StudentID = StringOrNull(root, "studentID"),
            IsPrimary = BoolOr(root, "isPrimary", false),
            ImportedFrom = StringOrNull(root, "importedFrom"),
            CatalogYear = IntOrNull(root, "catalogYear"),
            CatalogProgramPath = StringOrNull(root, "catalogProgramPath"),
            CatalogProgramTitle = StringOrNull(root, "catalogProgramTitle"),
            ConcentrationID = StringOrNull(root, "concentrationID"),
        };
        if (root.TryGetProperty("completed", out var completed))
            plan.Completed = completed.Deserialize<List<DegreePlanCourse>>(inner) ?? [];
        if (root.TryGetProperty("terms", out var terms))
            plan.Terms = terms.Deserialize<List<DegreePlanTerm>>(inner) ?? [];
        plan.PrintedAt = DateOrNull(root, "printedAt", inner);
        plan.CreatedAt = DateOr(root, "createdAt", inner, DateTimeOffset.UtcNow);
        plan.UpdatedAt = DateOr(root, "updatedAt", inner, plan.CreatedAt);
        plan.Substitutions = TryList<RequirementSubstitution>(root, "substitutions", inner);
        plan.Equivalences = TryList<CourseEquivalence>(root, "courseEquivalences", inner);
        return plan;
    }

    public override void Write(Utf8JsonWriter writer, DegreePlan value, JsonSerializerOptions options)
    {
        var inner = Without<DegreePlanConverter>(options);
        JsonSerializer.Serialize(writer, value, inner);
    }

    internal static JsonSerializerOptions Without<TConverter>(JsonSerializerOptions options) where TConverter : JsonConverter
    {
        var copy = new JsonSerializerOptions(options);
        for (var i = copy.Converters.Count - 1; i >= 0; i--)
            if (copy.Converters[i] is TConverter) copy.Converters.RemoveAt(i);
        return copy;
    }

    private static Guid GuidOrNew(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String && Guid.TryParse(el.GetString(), out var id))
            return id;
        return Guid.NewGuid();
    }

    private static string StringOr(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? fallback : fallback;

    private static string? StringOrNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static bool BoolOr(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False ? el.GetBoolean() : fallback;

    private static int IntOr(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.TryGetInt32(out var n) ? n : 0;

    private static int? IntOrNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.TryGetInt32(out var n) ? n : null;

    private static DateTimeOffset? DateOrNull(JsonElement root, string name, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind is JsonValueKind.Null) return null;
        try { return el.Deserialize<DateTimeOffset>(options); }
        catch (JsonException) { return null; }
    }

    private static DateTimeOffset DateOr(JsonElement root, string name, JsonSerializerOptions options, DateTimeOffset fallback) =>
        DateOrNull(root, name, options) ?? fallback;

    private static List<T> TryList<T>(JsonElement root, string name, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(name, out var el)) return [];
        try { return el.Deserialize<List<T>>(options) ?? []; }
        catch (JsonException) { return []; }
    }
}

public sealed class UserDataConverter : JsonConverter<UserData>
{
    public override UserData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var inner = DegreePlanConverter.Without<UserDataConverter>(options);
        var data = new UserData();
        if (root.ValueKind != JsonValueKind.Object) return data;
        if (root.TryGetProperty("version", out var version) && version.TryGetInt32(out var v)) data.Version = v;
        if (root.TryGetProperty("preferences", out var prefs))
        {
            try { data.Preferences = prefs.Deserialize<AppPreferences>(inner) ?? new AppPreferences(); }
            catch (JsonException) { data.Preferences = new AppPreferences(); }
        }
        data.Saved = Each<SavedCourse>(root, "saved", inner);
        data.Recents = Each<RecentCourse>(root, "recents", inner);
        data.Plans = Each<SchedulePlan>(root, "plans", inner);
        data.Watchlist = Each<WatchItem>(root, "watchlist", inner);
        data.SearchHistory = Each<string>(root, "searchHistory", inner);
        data.DegreePlans = Each<DegreePlan>(root, "degreePlans", inner);
        return data;
    }

    public override void Write(Utf8JsonWriter writer, UserData value, JsonSerializerOptions options)
    {
        var inner = DegreePlanConverter.Without<UserDataConverter>(options);
        JsonSerializer.Serialize(writer, value, inner);
    }

    private static List<T> Each<T>(JsonElement root, string name, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return [];
        var list = new List<T>();
        foreach (var item in el.EnumerateArray())
        {
            try
            {
                var value = item.Deserialize<T>(options);
                if (value is not null) list.Add(value);
            }
            catch (JsonException) { }
        }
        return list;
    }
}
