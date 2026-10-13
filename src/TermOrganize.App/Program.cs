// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace TermOrganize;

/// <summary>
/// The Windows entry point. A seat check and the background-task COM server
/// run without opening the window. Everything else starts the WinUI app.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        ScreenshotSweep.Configure(args);
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
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    private static bool Has(string[] args, string flag) =>
        args.Any(arg => arg.Equals(flag, StringComparison.OrdinalIgnoreCase));
}
