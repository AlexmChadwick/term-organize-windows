// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermOrganize.Core;
using Windows.Storage.Pickers;

namespace TermOrganize;

public sealed class SettingsPage : UserControl
{
    private readonly AppSession _session;
    private readonly MainWindow _window;
    private bool _filling = true;

    public SettingsPage(AppSession session, MainWindow window)
    {
        _session = session;
        _window = window;
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(12) };
        Content = new ScrollViewer { Content = panel };
        panel.Children.Add(Chrome.Heading("Settings"));
        panel.Children.Add(Chrome.Body(LegalText.UnofficialNotice));
        panel.Children.Add(Chrome.Body(LegalText.PrivacyNotice));
        panel.Children.Add(Chrome.Body(LegalText.DataNotice));
        panel.Children.Add(Chrome.Body(LegalText.SourceOffer));

        var prefs = session.Store.Preferences;
        panel.Children.Add(Chrome.Heading("Search and courses"));
        var campus = new ComboBox { Header = "Preferred campus", MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
        campus.Items.Add("None");
        foreach (var name in session.Index?.CampusNames ?? []) campus.Items.Add(name);
        campus.SelectedItem = string.IsNullOrWhiteSpace(prefs.PreferredCampus) ? "None" : prefs.PreferredCampus;
        campus.SelectionChanged += (_, _) =>
        {
            if (_filling) return;
            var chosen = campus.SelectedItem as string;
            SavePrefs(p => p.PreferredCampus = chosen is null or "None" ? null : chosen);
        };
        panel.Children.Add(campus);
        panel.Children.Add(Chrome.Body("Searches and free rooms start with this campus. Clear it to see every campus."));

        var auto = new CheckBox { Content = "Auto-refresh seats", IsChecked = prefs.AutoRefreshSeats };
        auto.Checked += (_, _) => SaveSeat(p => p.AutoRefreshSeats = true);
        auto.Unchecked += (_, _) => SaveSeat(p => p.AutoRefreshSeats = false);
        panel.Children.Add(auto);
        panel.Children.Add(Chrome.Body("Opening a course fetches live seat counts from Northeastern."));

        panel.Children.Add(Chrome.Heading("Calendar"));
        var weekends = new CheckBox { Content = "Show weekends on the calendar", IsChecked = prefs.ShowWeekends };
        weekends.Checked += (_, _) => SavePrefs(p => p.ShowWeekends = true);
        weekends.Unchecked += (_, _) => SavePrefs(p => p.ShowWeekends = false);
        panel.Children.Add(weekends);
        var start = HourBox("Day starts at", prefs.CalendarStartHour, 0, 22);
        var end = HourBox("Day ends at", prefs.CalendarEndHour, 1, 24);
        start.SelectionChanged += (_, _) =>
        {
            if (_filling || start.SelectedItem is not HourChoice first) return;
            SavePrefs(p =>
            {
                p.CalendarStartHour = first.Hour;
                if (p.CalendarEndHour <= first.Hour) p.CalendarEndHour = Math.Min(24, first.Hour + 1);
            });
        };
        end.SelectionChanged += (_, _) =>
        {
            if (_filling || end.SelectedItem is not HourChoice last) return;
            SavePrefs(p => p.CalendarEndHour = Math.Max(p.CalendarStartHour + 1, last.Hour));
        };
        panel.Children.Add(start);
        panel.Children.Add(end);

        panel.Children.Add(Chrome.Heading("Seat alerts"));
        panel.Children.Add(Chrome.Body("You\u2019re notified on this PC when a seat or waitlist spot opens in a section you watch. No account or phone number is involved."));
        var background = new CheckBox { Content = "Check for open seats automatically", IsChecked = prefs.BackgroundRefreshEnabled };
        background.Checked += (_, _) => SaveSeat(p => p.BackgroundRefreshEnabled = true);
        background.Unchecked += (_, _) => SaveSeat(p => p.BackgroundRefreshEnabled = false);
        panel.Children.Add(background);
        var notify = new CheckBox { Content = "Show a toast when a seat opens", IsChecked = prefs.NotifyOnSeats };
        notify.Checked += (_, _) => SavePrefs(p => p.NotifyOnSeats = true);
        notify.Unchecked += (_, _) => SavePrefs(p => p.NotifyOnSeats = false);
        panel.Children.Add(notify);
        var minutes = new ComboBox { Header = "Check every", MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var choice in new[] { 5, 10, 15, 30, 60 })
            minutes.Items.Add(new MinuteChoice(choice));
        SelectMinutes(minutes, prefs.SeatCheckMinutes);
        minutes.SelectionChanged += (_, _) =>
        {
            if (_filling || minutes.SelectedItem is not MinuteChoice choice) return;
            SaveSeat(p => p.SeatCheckMinutes = choice.Minutes);
        };
        panel.Children.Add(minutes);
        DateTimeOffset? last = null;
        foreach (var item in session.Store.Watchlist)
        {
            if (item.LastCheckedAt is DateTimeOffset checkedAt && (last is null || checkedAt > last)) last = checkedAt;
        }
        panel.Children.Add(Chrome.Body(last is DateTimeOffset lastChecked
            ? "Last checked " + lastChecked.ToLocalTime().ToString("g") + "."
            : session.Store.Watchlist.Count == 0 ? "Nothing watched yet." : "Not checked yet."));
        var now = new Button { Content = "Check now", HorizontalAlignment = HorizontalAlignment.Left };
        now.Click += (_, _) => PlatformServices.CheckSeats(session, window);
        var test = new Button { Content = "Send a test notification", HorizontalAlignment = HorizontalAlignment.Left };
        test.Click += (_, _) =>
        {
            if (!PlatformServices.ShowToast("Term Organize", "Seat alerts look like this."))
                window.SetStatus("Windows didn\u2019t show the test notification. Alerts will still be saved for the next launch.");
        };
        panel.Children.Add(now);
        panel.Children.Add(test);
        panel.Children.Add(Chrome.Body("Checks run while Term Organize is open, including from the tray. With automatic checking on, a scheduled task keeps checking after Exit. Packaged installs also register a Windows timer task. Those background checks run at least every 15 minutes. Toasts that couldn\u2019t be shown appear the next time you open the app."));

        panel.Children.Add(Chrome.Heading("Your data"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var export = new Button { Content = "Export data" };
        export.Click += (_, _) =>
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "term-organize-data.json");
            session.Store.ExportTo(path);
            window.SetStatus("Exported your data to " + path);
        };
        var import = new Button { Content = "Import data\u2026" };
        import.Click += async (_, _) => await ImportAsync();
        buttons.Children.Add(export);
        buttons.Children.Add(import);
        panel.Children.Add(buttons);

        var license = new TextBlock
        {
            Text = LegalText.LoadLicenseText() ?? LegalText.LicenseNotice,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            IsTextSelectionEnabled = true,
        };
        panel.Children.Add(Chrome.Heading("License"));
        panel.Children.Add(license);
        _filling = false;
    }

    private void SavePrefs(Action<AppPreferences> change)
    {
        var prefs = _session.Store.Preferences;
        change(prefs);
        _session.Store.ReplacePreferences(prefs);
    }

    private void SaveSeat(Action<AppPreferences> change)
    {
        if (_filling) return;
        SavePrefs(change);
        PlatformServices.ApplyBackground(_session);
    }

    private static void SelectMinutes(ComboBox box, int minutes)
    {
        var best = 0;
        var distance = int.MaxValue;
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is not MinuteChoice choice) continue;
            var gap = Math.Abs(choice.Minutes - minutes);
            if (gap < distance)
            {
                distance = gap;
                best = i;
            }
        }
        box.SelectedIndex = best;
    }

    private static ComboBox HourBox(string header, int selected, int from, int to)
    {
        var box = new ComboBox { Header = header, MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left };
        var index = 0;
        for (var hour = from; hour <= to; hour++)
        {
            box.Items.Add(new HourChoice(hour));
            if (hour == selected) box.SelectedIndex = index;
            index++;
        }
        if (box.SelectedIndex < 0) box.SelectedIndex = 0;
        return box;
    }

    private async Task ImportAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        _session.Store.ImportReplacing(file.Path);
        _window.SetStatus("Imported " + file.Name);
    }

    private sealed record HourChoice(int Hour)
    {
        public override string ToString() => TimeOfDay.HourAxisLabel(Hour);
    }

    private sealed record MinuteChoice(int Minutes)
    {
        public override string ToString() => Minutes == 60 ? "Hour" : Minutes + " minutes";
    }
}
