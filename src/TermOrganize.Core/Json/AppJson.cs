// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TermOrganize.Core;

/// <summary>
/// JSON settings that match the Mac app. Catalog files use Unix timestamps;
/// user data uses ISO-8601, the same split as the Swift encoders.
/// </summary>
public static class AppJson
{
    public static JsonSerializerOptions Catalog { get; } = Create(unixDates: true);

    public static JsonSerializerOptions UserData { get; } = Create(unixDates: false);

    public static string Serialize<T>(T value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(value, options);

    public static T? Deserialize<T>(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<T>(json, options);

    public static byte[] SerializeBytes<T>(T value, JsonSerializerOptions options) =>
        JsonSerializer.SerializeToUtf8Bytes(value, options);

    private static JsonSerializerOptions Create(bool unixDates)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new DaySetConverter());
        options.Converters.Add(new RequisiteConverter());
        options.Converters.Add(new UpperGuidConverter());
        options.Converters.Add(new CourseEquivalencesConverter());
        options.Converters.Add(new CourseNumberLimitConverter());
        options.Converters.Add(new SubstitutionChangeConverter());
        options.Converters.Add(new SchedulePreferencesConverter());
        options.Converters.Add(unixDates ? new UnixDateConverter() : new IsoDateConverter());
        options.Converters.Add(new UserDataConverter());
        options.Converters.Add(new DegreePlanConverter());
        return options;
    }
}

public sealed class UnixDateConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed;
        var seconds = reader.GetDouble();
        var whole = (long)Math.Floor(seconds);
        var frac = seconds - whole;
        return DateTimeOffset.FromUnixTimeSeconds(whole).AddTicks((long)Math.Round(frac * TimeSpan.TicksPerSecond));
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        var seconds = value.ToUnixTimeMilliseconds() / 1000.0;
        writer.WriteNumberValue(seconds);
    }
}

public sealed class IsoDateConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new UnixDateConverter().Read(ref reader, typeToConvert, options);
        var text = reader.GetString() ?? "";
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        var utc = value.ToUniversalTime();
        writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture).Replace(".Z", "Z").Replace(".0000000Z", "Z"));
    }
}

public sealed class UpperGuidConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? Guid.Parse(reader.GetString()!) : throw new JsonException("Expected a UUID string.");

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("D").ToUpperInvariant());
}

public sealed class DaySetConverter : JsonConverter<DaySet>
{
    public override DaySet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number) throw new JsonException("Day set must be a bit mask.");
        if (!reader.TryGetInt32(out var value) || value < 0 || value > 255) throw new JsonException("Day set out of range.");
        return new DaySet((byte)value);
    }

    public override void Write(Utf8JsonWriter writer, DaySet value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Raw);
}
