// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace TermOrganize;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }
    public static AppSession Session { get; private set; } = null!;

    public App()
    {
        UnhandledException += (_, e) =>
        {
            if (ScreenshotSweep.Enabled)
            {
                ScreenshotSweep.WriteError(e.Exception);
                e.Handled = true;
                Environment.Exit(1);
            }
            e.Handled = true;
        };
        InitializeComponent();
        ScreenshotSweep.Log("app constructed");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ScreenshotSweep.Log("launch");
        Session = new AppSession();
        Window = new MainWindow();
        Window.Activate();
        if (ScreenshotSweep.Enabled)
        {
            ScreenshotSweep.Log("sweep scheduled");
            _ = Window.RunScreenshotSweepAsync();
            return;
        }
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        Window.HandleActivation(activation);
        PlatformServices.Start(Window, Session);
    }
}
