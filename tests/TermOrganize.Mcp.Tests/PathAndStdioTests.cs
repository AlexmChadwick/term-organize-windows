// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using ModelContextProtocol.Client;
using TermOrganize.Mcp;

namespace TermOrganize.Mcp.Tests;

public sealed class DataPathTests
{
    [Fact]
    public void PublisherIdMatchesTheWindowsAlgorithm()
    {
        Assert.Equal("8wekyb3d8bbwe", DataPaths.PublisherId("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"));
        Assert.Equal("wmazyzrpc95f0", DataPaths.PublisherId(DataPaths.Publisher));
        Assert.Equal("TermOrganize_wmazyzrpc95f0", DataPaths.PackageFamilyName);
    }

    [Fact]
    public void ResolverPrefersTheNewestExistingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "term-organize-paths-" + Guid.NewGuid().ToString("N"));
        var portable = Path.Combine(root, "TermOrganize", "UserData.json");
        var packaged = Path.Combine(root, "Packages", DataPaths.PackageFamilyName, "LocalCache", "Local", "TermOrganize", "UserData.json");
        var localState = Path.Combine(root, "Packages", DataPaths.PackageFamilyName, "LocalState", "UserData.json");
        Write(portable, "portable");
        Write(packaged, "packaged");
        File.SetLastWriteTimeUtc(portable, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(packaged, DateTime.UtcNow);
        Assert.Equal(packaged, DataPaths.Resolve(null, root));

        File.SetLastWriteTimeUtc(portable, DateTime.UtcNow.AddMinutes(5));
        Assert.Equal(portable, DataPaths.Resolve(null, root));

        File.Delete(portable);
        File.Delete(packaged);
        Write(localState, "state");
        Assert.Equal(localState, DataPaths.Resolve(null, root));

        File.Delete(localState);
        Assert.Equal(packaged, DataPaths.Resolve(null, root));

        Directory.Delete(Path.Combine(root, "Packages"), recursive: true);
        Assert.Equal(portable, DataPaths.Resolve(null, root));
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "override.json")), DataPaths.Resolve(Path.Combine(root, "override.json"), root));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void ReadOnlyFlagAndEnv()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var options = McpOptions.Parse(["--read-only", "--data-file", file], _ => null);
        Assert.True(options.ReadOnly);
        Assert.Equal(Path.GetFullPath(file), options.DataFile);
        var fromEnv = McpOptions.Parse([], name => name == "TERM_ORGANIZE_MCP_READONLY" ? "1" : name == "TERM_ORGANIZE_DATA_FILE" ? file : null);
        Assert.True(fromEnv.ReadOnly);
        Assert.Throws<ArgumentException>(() => McpOptions.Parse(["--nope"], _ => null));
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}

public sealed class StdioTests
{
    [Fact]
    public async Task ServerSpeaksThePinnedProtocolAndHidesWritesWhenReadOnly()
    {
        var directory = Path.Combine(Path.GetTempPath(), "term-organize-stdio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var data = Path.Combine(directory, "UserData.json");
        var dll = typeof(CatalogTools).Assembly.Location;
        await using (var client = await Connect(dll, data, readOnly: false))
        {
            Assert.Equal("Term Organize", client.ServerInfo.Name);
            Assert.Contains("not affiliated", client.ServerInstructions ?? "", StringComparison.OrdinalIgnoreCase);
            var version = client.GetType().GetProperty("NegotiatedProtocolVersion")?.GetValue(client) as string;
            Assert.Equal("2025-06-18", version);

            var tools = await client.ListToolsAsync();
            var suggest = tools.Single(tool => tool.Name == "suggest_schedule");
            Assert.Contains("no_classes_before", suggest.ProtocolTool.InputSchema.ToString());
            Assert.Contains("plan_id", tools.Single(tool => tool.Name == "add_course").ProtocolTool.InputSchema.ToString());
            Assert.True(tools.Single(tool => tool.Name == "list_terms").ProtocolTool.Annotations?.ReadOnlyHint);
            Assert.True(tools.Single(tool => tool.Name == "delete_plan").ProtocolTool.Annotations?.DestructiveHint);
            Assert.True(tools.Single(tool => tool.Name == "remove_course").ProtocolTool.Annotations?.DestructiveHint);
            Assert.True(tools.Single(tool => tool.Name == "apply_reorganize").ProtocolTool.Annotations?.DestructiveHint);

            var listed = await client.CallToolAsync("list_terms", new Dictionary<string, object?>());
            Assert.NotEqual(true, listed.IsError);
            var body = listed.StructuredContent?.ToString()
                ?? string.Join("\n", listed.Content.Select(block => block.ToString()));
            Assert.Contains("202730", body);
            Assert.Contains("\"term\"", body);
            Assert.DoesNotContain("\"code\"", body);

            var missingName = await client.CallToolAsync("create_plan", new Dictionary<string, object?>());
            Assert.True(missingName.IsError);
            Assert.Equal("invalid_argument", ErrorCode(missingName));
            Assert.False(string.IsNullOrWhiteSpace(ErrorMessage(missingName)));

            var unknown = await client.CallToolAsync("not_a_tool", new Dictionary<string, object?>());
            Assert.True(unknown.IsError);
            Assert.Equal("unknown_tool", ErrorCode(unknown));
            Assert.False(string.IsNullOrWhiteSpace(ErrorMessage(unknown)));
        }

        await using (var readOnly = await Connect(dll, data, readOnly: true))
        {
            var hidden = await readOnly.ListToolsAsync();
            Assert.Contains(hidden, tool => tool.Name == "get_plan");
            Assert.DoesNotContain(hidden, tool => tool.Name == "create_plan");
            Assert.DoesNotContain(hidden, tool => tool.Name == "delete_plan");
            var denied = await readOnly.CallToolAsync("create_plan", new Dictionary<string, object?> { ["name"] = "Nope" });
            Assert.True(denied.IsError);
            Assert.Equal("read_only", ErrorCode(denied));
            Assert.False(string.IsNullOrWhiteSpace(ErrorMessage(denied)));
        }
        Directory.Delete(directory, recursive: true);
    }

    private static async Task<McpClient> Connect(string dll, string dataFile, bool readOnly)
    {
        var arguments = new List<string> { "exec", dll, "--data-file", dataFile };
        if (readOnly) arguments.Add("--read-only");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = arguments,
            Name = "Term Organize",
        });
        return await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-06-18" });
    }

    private static string? ErrorCode(ModelContextProtocol.Protocol.CallToolResult result) =>
        result.StructuredContent?.GetProperty("error").GetProperty("code").GetString();

    private static string? ErrorMessage(ModelContextProtocol.Protocol.CallToolResult result) =>
        result.StructuredContent?.GetProperty("error").GetProperty("message").GetString();
}
