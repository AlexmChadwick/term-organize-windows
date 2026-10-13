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
        InitializeComponent();
        UnhandledException += (_, e) => e.Handled = true;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Session = new AppSession();
        Window = new MainWindow();
        Window.Activate();
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        Window.HandleActivation(activation);
        PlatformServices.Start(Window, Session);
    }
}
