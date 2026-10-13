// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TermOrganize.Core;
using Windows.UI.StartScreen;

namespace TermOrganize;

public static class PlatformServices
{
    private static DispatcherQueueTimer? _timer;
    private static bool _toastsRegistered;

    public static void Start(MainWindow window, AppSession session)
    {
        RegisterProtocols();
        RegisterPdfOpenWith();
        _ = JumpListAsync();
        _timer = window.DispatcherQueue.CreateTimer();
        _timer.Tick += (_, _) => CheckSeats(session, window);
        ApplyBackground(session);
        ReplayAlerts(window);
    }

    /// <summary>Applies the seat-check interval, the in-app timer, and the checks that survive Exit.</summary>
    public static void ApplyBackground(AppSession session)
    {
        var preferences = session.Store.Preferences;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMinutes(Math.Max(5, preferences.SeatCheckMinutes));
            if (preferences.BackgroundRefreshEnabled) _timer.Start();
        }
        if (preferences.BackgroundRefreshEnabled)
        {
            SeatScheduler.Register(preferences);
            _ = SeatBackgroundRegistration.RegisterAsync(preferences);
        }
        else
        {
            SeatScheduler.Remove();
            SeatBackgroundRegistration.Remove();
        }
    }

    public static void CheckSeats(AppSession session, MainWindow window) => _ = CheckSeatsAsync(session, window);

    public static bool ShowToast(string title, string body)
    {
        try
        {
            if (!_toastsRegistered)
            {
                AppNotificationManager.Default.Register();
                _toastsRegistered = true;
            }
            var notification = new AppNotificationBuilder().AddText(title).AddText(body).BuildNotification();
            AppNotificationManager.Default.Show(notification);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task CheckSeatsAsync(AppSession session, MainWindow window)
    {
        try
        {
            var outcome = await SeatCheckHost.RunAsync(session.Store);
            var alert = outcome.Alerts.FirstOrDefault();
            if (alert is null) return;
            window.DispatcherQueue.TryEnqueue(() => window.SetUpNext(alert.Title + " \u2014 " + alert.Body));
        }
        catch (Exception)
        {
            // The last stored counts stay. The next check tries again.
        }
    }

    private static void ReplayAlerts(MainWindow window)
    {
        foreach (var (title, body) in SeatAlertLog.Take())
        {
            if (!ShowToast(title, body))
                window.SetStatus(title + " \u2014 " + body);
        }
    }

    private static async Task JumpListAsync()
    {
        try
        {
            var list = await JumpList.LoadCurrentAsync();
            list.SystemGroupKind = JumpListSystemGroupKind.None;
            list.Items.Clear();
            void Add(string title, string area)
            {
                var item = JumpListItem.CreateWithArguments("go:" + area, title);
                item.Description = title;
                list.Items.Add(item);
            }
            Add("Search", "search");
            Add("Schedules", "schedules");
            Add("Degree plans", "degree");
            Add("Watchlist", "watch");
            Add("Free rooms", "rooms");
            await list.SaveAsync();
        }
        catch (Exception)
        {
            // Unpackaged runs may not have a jump-list identity. The menu and shortcuts still work.
        }
    }

    private static bool IsPackaged()
    {
        try { return Windows.ApplicationModel.Package.Current is not null; }
        catch (Exception) { return false; }
    }

    private static void RegisterProtocols()
    {
        if (IsPackaged()) return;
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        foreach (var scheme in new[] { "termorganize", "neuclasssearch" })
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + scheme);
            key?.SetValue("", "URL:Term Organize");
            key?.SetValue("URL Protocol", "");
            using var command = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + scheme + @"\shell\open\command");
            command?.SetValue("", $"\"{exe}\" \"%1\"");
        }
    }

    private static void RegisterPdfOpenWith()
    {
        if (IsPackaged()) return;
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        using var prog = Registry.CurrentUser.CreateSubKey(@"Software\Classes\TermOrganize.DegreeAudit");
        prog?.SetValue("", "Degree audit PDF");
        using var command = Registry.CurrentUser.CreateSubKey(@"Software\Classes\TermOrganize.DegreeAudit\shell\open\command");
        command?.SetValue("", $"\"{exe}\" \"%1\"");
        using var openWith = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.pdf\OpenWithProgids");
        openWith?.SetValue("TermOrganize.DegreeAudit", "");
    }
}
