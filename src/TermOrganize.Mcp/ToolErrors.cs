// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace TermOrganize.Mcp;

public sealed class ToolFailure : Exception
{
    public string Code { get; }

    public ToolFailure(string code, string message)
        : base(string.IsNullOrWhiteSpace(message) ? "The tool failed." : message.Trim())
    {
        Code = code;
    }
}

public static class ToolErrors
{
    public static readonly HashSet<string> WriteTools = new(StringComparer.Ordinal)
    {
        "create_plan",
        "rename_plan",
        "add_course",
        "remove_course",
        "move_course",
        "set_section",
        "apply_reorganize",
        "delete_plan",
    };

    public static CallToolResult Result(string code, string? message)
    {
        var text = string.IsNullOrWhiteSpace(message) ? "The tool failed." : message.Trim();
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["error"] = new Dictionary<string, object?>
                {
                    ["code"] = code,
                    ["message"] = text,
                },
            }),
        };
    }

    public static int RequireRevision(JsonElement? element)
    {
        if (element is not JsonElement value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new ToolFailure("invalid_argument", "expected_revision is required.");
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var revision) && revision >= 1)
            return revision;
        throw new ToolFailure("invalid_argument", "expected_revision must be an integer.");
    }

    public static CallToolResult FromException(Exception exception, string? toolName)
    {
        var ex = exception;
        while (ex is TargetInvocationException or AggregateException && ex.InnerException is not null)
            ex = ex.InnerException;
        if (ex is ToolFailure failure)
            return Result(failure.Code, failure.Message);
        var name = string.IsNullOrWhiteSpace(toolName) ? "tool" : toolName;
        if (ex is McpProtocolException protocol && protocol.Message.Contains("Unknown tool", StringComparison.OrdinalIgnoreCase))
        {
            var unknown = string.IsNullOrWhiteSpace(protocol.Message) ? $"Unknown tool '{name}'." : protocol.Message;
            return Result("unknown_tool", unknown);
        }
        var message = ex.Message?.Trim() ?? "";
        if (message.Length == 0 || message.StartsWith("An error occurred invoking", StringComparison.Ordinal))
            message = $"{name} could not run. Check the arguments.";
        var code = ex is McpProtocolException ? "unknown_tool" : "invalid_argument";
        return Result(code, message);
    }
}
