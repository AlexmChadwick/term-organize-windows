// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace TermOrganize.Mcp;

public sealed class McpOptions
{
    public bool ReadOnly { get; init; }
    public string DataFile { get; init; } = "";
    public bool Help { get; init; }

    public static McpOptions Parse(string[] args, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        var readOnly = Truthy(env("TERM_ORGANIZE_MCP_READONLY"));
        string? dataFile = env("TERM_ORGANIZE_DATA_FILE");
        var help = false;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    help = true;
                    break;
                case "--read-only":
                    readOnly = true;
                    break;
                case "--data-file":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--data-file needs a path.");
                    dataFile = args[++i];
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }
        return new McpOptions
        {
            ReadOnly = readOnly,
            DataFile = DataPaths.Resolve(dataFile),
            Help = help,
        };
    }

    public static bool Truthy(string? value) =>
        value is "1" or "true" or "TRUE" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}

public static class Program
{
    public const string Instructions =
        "Term Organize is an unofficial course planner and is not affiliated with, endorsed by, or sponsored by Northeastern University. " +
        "This server reads only the bundled Fall 2026 (202710) and Spring 2027 (202730) catalogs. " +
        "It does not use the network and does not log in. " +
        "Saved plans stay in the local UserData.json file the desktop app reads. " +
        "Writes need the revision from the last read. " +
        "Pass --read-only or set TERM_ORGANIZE_MCP_READONLY=1 to hide write tools.";

    public static async Task<int> Main(string[] args)
    {
        McpOptions options;
        try
        {
            options = McpOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(HelpText);
            return 2;
        }
        if (options.Help)
        {
            Console.Error.WriteLine(HelpText);
            return 0;
        }

        var library = new CatalogLibrary();
        var plans = new PlanService(options.DataFile, library);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(logging => logging.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(library);
        builder.Services.AddSingleton(plans);
        builder.Services
            .AddMcpServer(server =>
            {
                server.ServerInfo = new Implementation { Name = "Term Organize", Version = "1.1.0" };
                server.ServerInstructions = Instructions;
                server.ProtocolVersion = "2025-06-18";
            })
            .WithStdioServerTransport()
            .WithTools(new CatalogTools(library, plans), McpJson.Tools)
            .WithTools(new PlanReadTools(plans), McpJson.Tools)
            .WithTools(new PlanWriteTools(plans), McpJson.Tools)
            .WithRequestFilters(filters =>
            {
                filters.AddCallToolFilter(next => async (context, cancellationToken) =>
                {
                    var name = context.Params?.Name ?? "";
                    if (options.ReadOnly && ToolErrors.WriteTools.Contains(name))
                        return ToolErrors.Result("read_only", $"The server is read-only, so {name} is unavailable.");
                    try
                    {
                        return await next(context, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        return ToolErrors.FromException(ex, name);
                    }
                });
                if (options.ReadOnly)
                {
                    filters.AddListToolsFilter(next => async (context, cancellationToken) =>
                    {
                        var listed = await next(context, cancellationToken).ConfigureAwait(false);
                        for (var i = listed.Tools.Count - 1; i >= 0; i--)
                        {
                            if (ToolErrors.WriteTools.Contains(listed.Tools[i].Name))
                                listed.Tools.RemoveAt(i);
                        }
                        return listed;
                    });
                }
            });
        await builder.Build().RunAsync().ConfigureAwait(false);
        return 0;
    }

    public const string HelpText =
        """
        Term Organize MCP server (stdio, protocol 2025-06-18).

        Unofficial and not affiliated with Northeastern University.
        Reads bundled catalogs only. Does not use the network.

          --data-file <path>   UserData.json to read and write.
                               Overrides TERM_ORGANIZE_DATA_FILE.
          --read-only          Hide write tools.
                               Also enabled by TERM_ORGANIZE_MCP_READONLY=1.
          --help               Show this text.

        With no data file, the server uses the same path as the desktop app:
        the portable file %LOCALAPPDATA%\TermOrganize\UserData.json, or the
        newer of that file and the MSIX virtualized copy under
        %LOCALAPPDATA%\Packages\TermOrganize_<publisherId>\LocalCache\Local\TermOrganize\UserData.json.
        """;
}
