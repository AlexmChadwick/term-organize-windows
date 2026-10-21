// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TermOrganize.Mcp;

public sealed class CourseRef
{
    public string Subject { get; set; } = "";
    public string Number { get; set; } = "";

    /// <summary>When set, this listing is that section. A repeated subject and number is valid only when every listing has a different CRN.</summary>
    public string? Crn { get; set; }
}

public sealed class ScheduleConstraints
{
    public JsonElement? NoClassesBefore { get; set; }
    public JsonElement? NoClassesAfter { get; set; }
    public List<string>? DaysOff { get; set; }
}

public sealed class PlanTermInput
{
    public string? Term { get; set; }
    public string? Code { get; set; }
    public List<CourseRef> Courses { get; set; } = [];
}

public sealed class PlanInput
{
    public List<PlanTermInput> Terms { get; set; } = [];
}

public static class McpJson
{
    public static JsonSerializerOptions Tools { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
}
