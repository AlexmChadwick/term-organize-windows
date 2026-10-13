// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

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

    public static void Start(Window window, AppSession session)
    {
        RegisterProtocols();
        RegisterPdfOpenWith();
        _ = JumpListAsync();
        _timer = window.DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(Math.Max(5, session.Store.Preferences.SeatCheckMinutes));
        _timer.Tick += (_, _) => CheckSeats(session, window);
        if (session.Store.Preferences.AutoRefreshSeats) _timer.Start();
    }

    public static void CheckSeats(AppSession session, Window window) => _ = CheckSeatsAsync(session, window);

    private static async Task CheckSeatsAsync(AppSession session, Window window)
    {
        var items = session.Store.Watchlist.ToList();
        if (items.Count == 0) return;
        var live = new Dictionary<string, IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>>(StringComparer.Ordinal);
        foreach (var group in items.GroupBy(item => item.TermCode))
        {
            try
            {
                var keys = group.Select(item => item.CourseKey).Distinct().ToList();
                var banner = await BannerClient.Shared.FetchSectionsAsync(group.Key, keys);
                var map = new Dictionary<CourseKey, IReadOnlyList<CourseSection>>();
                foreach (var pair in banner)
                    map[pair.Key] = CatalogBuilder.MakeSections(pair.Value);
                live[group.Key] = map;
            }
            catch (Exception)
            {
                if (session.Index?.Term.Code == group.Key)
                {
                    var map = new Dictionary<CourseKey, IReadOnlyList<CourseSection>>();
                    foreach (var key in group.Select(item => item.CourseKey).Distinct())
                        map[key] = session.Index.Sections(key);
                    live[group.Key] = map;
                }
            }
        }
        var outcome = WatchlistComparison.Compare(session.Store.Watchlist, live);
        foreach (var item in outcome.Items)
            session.Store.UpdateWatchItem(item);
        foreach (var alert in outcome.Alerts)
        {
            if (!Toast(alert.Title, alert.Body))
                window.DispatcherQueue.TryEnqueue(() => window.SetUpNext(alert.Title + " — " + alert.Body));
        }
    }

    private static bool Toast(string title, string body)
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
