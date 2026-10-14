// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TermOrganize.Core;

namespace TermOrganize;

/// <summary>
/// The Windows entry point. A seat check and the background-task COM server
/// run without opening the window. Everything else starts the WinUI app.
/// </summary>
public static class Program
{
    [ModuleInitializer]
    internal static void PrepareScreenshotSweep() => ScreenshotSweep.EnsureHooks();

    [STAThread]
    public static void Main(string[] args)
    {
        if (TrySelfTestDirectory(args, out var selfTestDirectory))
        {
            try
            {
                Environment.Exit(Task.Run(() => ReleaseSmoke.RunAsync(selfTestDirectory)).GetAwaiter().GetResult());
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(selfTestDirectory);
                    File.WriteAllText(Path.Combine(selfTestDirectory, "selftest.log"), ex.ToString());
                }
                catch (Exception)
                {
                    Console.Error.WriteLine(ex);
                }
                Environment.Exit(1);
            }
        }
        try
        {
            ScreenshotSweep.EnsureHooks();
            ScreenshotSweep.Configure(args);
            ScreenshotSweep.Log("process start");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            if (!ScreenshotSweep.Enabled && Has(args, "--seat-check"))
            {
                UnpackagedBootstrap.Try();
                SeatCheckHost.RunHeadless();
                return;
            }
            if (Has(args, "-RegisterForBGTaskServer"))
            {
                SeatBackgroundServer.RegisterAndPump();
                return;
            }
            if (Has(args, "-RegisterWidgetProvider"))
            {
                UnpackagedBootstrap.Try();
                UpNextWidgetHost.RegisterAndPump();
                return;
            }
            UnpackagedBootstrap.Try();
            ScreenshotSweep.Log("winui start");
            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
        catch (Exception ex)
        {
            ScreenshotSweep.WriteError(ex);
            if (ScreenshotSweep.Enabled) Environment.Exit(1);
            throw;
        }
    }

    private static bool Has(string[] args, string flag) =>
        args.Any(arg => arg.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static bool TrySelfTestDirectory(string[] args, out string directory)
    {
        directory = "";
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--selftest", StringComparison.OrdinalIgnoreCase))
            {
                directory = i + 1 < args.Length && !args[i + 1].StartsWith('-')
                    ? args[i + 1].Trim().Trim('"')
                    : Path.Combine(Path.GetTempPath(), "term-organize-selftest");
                return true;
            }
            const string prefix = "--selftest=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                directory = arg[prefix.Length..].Trim().Trim('"');
                if (directory.Length == 0)
                    directory = Path.Combine(Path.GetTempPath(), "term-organize-selftest");
                return true;
            }
        }
        return false;
    }
}
