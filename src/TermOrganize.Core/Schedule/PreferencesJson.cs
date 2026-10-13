// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TermOrganize.Core;

/// <summary>
/// Preferences never take a saved schedule down. A missing field, a damaged field,
/// or a value that is not an object at all decodes as the defaults.
/// </summary>
public sealed class SchedulePreferencesConverter : JsonConverter<SchedulePreferences>
{
    public override SchedulePreferences Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return new SchedulePreferences();
        }
        using var doc = JsonDocument.ParseValue(ref reader);
        return ReadObject(doc.RootElement, options);
    }

    public static SchedulePreferences ReadObject(JsonElement root, JsonSerializerOptions options)
    {
        var prefs = new SchedulePreferences();
        if (root.ValueKind != JsonValueKind.Object) return prefs;
        if (root.TryGetProperty("timeOfDay", out var bands) && bands.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in bands.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String) continue;
                if (!Enum.TryParse<TimeBand>(entry.Name, true, out var band)) continue;
                if (!Enum.TryParse<TimePreference>(entry.Value.GetString(), true, out var preference)) continue;
                prefs[band] = preference;
            }
        }
        prefs.EarliestStart = ReadLimit(root, "earliestStart");
        prefs.LatestEnd = ReadLimit(root, "latestEnd");
        if (Try<DaySet>(root, "daysOff", options, out var days)) prefs.DaysOff = days;
        if (TryEnum<PreferenceStrength>(root, "daysOffStrength", out var daysStrength)) prefs.DaysOffStrength = daysStrength;
        if (TryEnum<DayLayout>(root, "dayLayout", out var layout)) prefs.DayLayout = layout;
        if (TryInt(root, "maxClassesPerDay", out var classes)) prefs.MaxClassesPerDay = classes;
        if (TryInt(root, "maxMinutesPerDay", out var minutes)) prefs.MaxMinutesPerDay = minutes;
        if (TryEnum<GapPreference>(root, "gaps", out var gaps)) prefs.Gaps = gaps;
        if (TryEnum<BreakPreference>(root, "breaks", out var breaks)) prefs.Breaks = breaks;
        if (TryInt(root, "minimumBreakMinutes", out var breakMinutes)) prefs.MinimumBreakMinutes = breakMinutes;
        if (TryEnum<PreferenceStrength>(root, "minimumBreakStrength", out var breakStrength)) prefs.MinimumBreakStrength = breakStrength;
        prefs.Lunch = ReadLunch(root);
        prefs.BlockedTimes = ReadBlocks(root, options);
        if (TryEnum<FormatPreference>(root, "format", out var format)) prefs.Format = format;
        if (TryEnum<PreferenceStrength>(root, "formatStrength", out var formatStrength)) prefs.FormatStrength = formatStrength;
        if (root.TryGetProperty("campus", out var campus) && campus.ValueKind == JsonValueKind.String)
            prefs.Campus = campus.GetString();
        if (TryEnum<PreferenceStrength>(root, "campusStrength", out var campusStrength)) prefs.CampusStrength = campusStrength;
        if (TryEnum<SeatPolicy>(root, "seats", out var seats)) prefs.Seats = seats;
        prefs.PreferredInstructors = ReadStrings(root, "preferredInstructors");
        prefs.AvoidedInstructors = ReadStrings(root, "avoidedInstructors");
        prefs.CreditTarget = ReadCredits(root);
        return prefs;
    }

    public override void Write(Utf8JsonWriter writer, SchedulePreferences value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.TimeOfDay.Count > 0)
        {
            writer.WritePropertyName("timeOfDay");
            writer.WriteStartObject();
            foreach (var (band, preference) in value.TimeOfDay)
            {
                writer.WriteString(JsonNamingPolicy.CamelCase.ConvertName(band.ToString()), JsonNamingPolicy.CamelCase.ConvertName(preference.ToString()));
            }
            writer.WriteEndObject();
        }
        WriteIf(writer, options, "earliestStart", value.EarliestStart);
        WriteIf(writer, options, "latestEnd", value.LatestEnd);
        writer.WritePropertyName("daysOff");
        JsonSerializer.Serialize(writer, value.DaysOff, options);
        WriteEnum(writer, "daysOffStrength", value.DaysOffStrength);
        WriteEnum(writer, "dayLayout", value.DayLayout);
        if (value.MaxClassesPerDay is int maxClasses) writer.WriteNumber("maxClassesPerDay", maxClasses);
        if (value.MaxMinutesPerDay is int maxMinutes) writer.WriteNumber("maxMinutesPerDay", maxMinutes);
        WriteEnum(writer, "gaps", value.Gaps);
        WriteEnum(writer, "breaks", value.Breaks);
        writer.WriteNumber("minimumBreakMinutes", value.MinimumBreakMinutes);
        WriteEnum(writer, "minimumBreakStrength", value.MinimumBreakStrength);
        WriteIf(writer, options, "lunch", value.Lunch);
        writer.WritePropertyName("blockedTimes");
        JsonSerializer.Serialize(writer, value.BlockedTimes, options);
        WriteEnum(writer, "format", value.Format);
        WriteEnum(writer, "formatStrength", value.FormatStrength);
        if (value.Campus is not null) writer.WriteString("campus", value.Campus);
        WriteEnum(writer, "campusStrength", value.CampusStrength);
        WriteEnum(writer, "seats", value.Seats);
        WriteStrings(writer, "preferredInstructors", value.PreferredInstructors);
        WriteStrings(writer, "avoidedInstructors", value.AvoidedInstructors);
        WriteIf(writer, options, "creditTarget", value.CreditTarget);
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, List<string> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static void WriteIf<T>(Utf8JsonWriter writer, JsonSerializerOptions options, string name, T? value)
    {
        if (value is null) return;
        writer.WritePropertyName(name);
        JsonSerializer.Serialize(writer, value, options);
    }

    private static void WriteEnum<T>(Utf8JsonWriter writer, string name, T value) where T : struct, Enum =>
        writer.WriteString(name, JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));

    private static TimeLimit? ReadLimit(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object) return null;
        if (!TryInt(el, "minutes", out var minutes)) return null;
        var strength = PreferenceStrength.Required;
        if (TryEnum<PreferenceStrength>(el, "strength", out var parsed)) strength = parsed;
        return new TimeLimit(minutes, strength);
    }

    private static LunchBreak? ReadLunch(JsonElement root)
    {
        if (!root.TryGetProperty("lunch", out var el) || el.ValueKind != JsonValueKind.Object) return null;
        var lunch = new LunchBreak();
        if (TryInt(el, "start", out var start)) lunch.Start = start;
        if (TryInt(el, "end", out var end)) lunch.End = end;
        if (TryInt(el, "minutes", out var minutes)) lunch.Minutes = minutes;
        return lunch;
    }

    private static CreditTarget? ReadCredits(JsonElement root)
    {
        if (!root.TryGetProperty("creditTarget", out var el) || el.ValueKind != JsonValueKind.Object) return null;
        if (!TryInt(el, "lower", out var lower) || !TryInt(el, "upper", out var upper)) return null;
        return new CreditTarget(lower, upper);
    }

    private static List<BlockedTime> ReadBlocks(JsonElement root, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty("blockedTimes", out var el) || el.ValueKind != JsonValueKind.Array) return [];
        var list = new List<BlockedTime>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var block = new BlockedTime();
            if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String && Guid.TryParse(idEl.GetString(), out var id))
                block.Id = id;
            if (item.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String)
                block.Label = label.GetString() ?? "";
            if (Try<DaySet>(item, "days", options, out var days)) block.Days = days;
            if (TryInt(item, "start", out var start)) block.Start = start;
            if (TryInt(item, "end", out var end)) block.End = end;
            if (TryEnum<PreferenceStrength>(item, "strength", out var strength)) block.Strength = strength;
            if (item.TryGetProperty("isEnabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                block.IsEnabled = enabled.GetBoolean();
            list.Add(block);
        }
        return list;
    }

    private static List<string> ReadStrings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return [];
        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return [];
            list.Add(item.GetString() ?? "");
        }
        return list;
    }

    private static bool Try<T>(JsonElement root, string name, JsonSerializerOptions options, out T value)
    {
        value = default!;
        if (!root.TryGetProperty(name, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return false;
        try
        {
            var parsed = el.Deserialize<T>(options);
            if (parsed is null) return false;
            value = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryEnum<T>(JsonElement root, string name, out T value) where T : struct, Enum
    {
        value = default;
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return false;
        return Enum.TryParse(el.GetString(), true, out value);
    }

    private static bool TryInt(JsonElement root, string name, out int value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var el)) return false;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value)) return true;
        return el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out value);
    }
}
