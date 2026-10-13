// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermOrganize.Core;
using Windows.Storage.Pickers;

namespace TermOrganize;

public sealed class WatchPage : UserControl
{
    public WatchPage(AppSession session)
    {
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(12) };
        Content = new ScrollViewer { Content = panel };
        panel.Children.Add(Chrome.Heading("Watchlist"));
        panel.Children.Add(Chrome.Body("A seat alert fires when a full section you are watching gains a seat, or a full waitlist gains a spot."));
        if (session.Store.Watchlist.Count == 0)
        {
            panel.Children.Add(Chrome.Body("Watch a section from its course page. Checks run while the app is open and from the tray."));
            return;
        }
        foreach (var item in session.Store.Watchlist)
        {
            var text = $"{item.CourseKey} · CRN {item.Crn} · {item.LastSeatsRemaining} seats";
            if (item.LastCheckedAt is DateTimeOffset at) text += " · checked " + at.ToLocalTime().ToString("g");
            panel.Children.Add(Chrome.Card(Chrome.Body(text)));
        }
    }
}

public sealed class SettingsPage : UserControl
{
    private readonly AppSession _session;
    private readonly MainWindow _window;

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

        var weekends = new CheckBox { Content = "Show weekends on the calendar", IsChecked = session.Store.Preferences.ShowWeekends };
        weekends.Checked += (_, _) => SavePrefs(p => p.ShowWeekends = true);
        weekends.Unchecked += (_, _) => SavePrefs(p => p.ShowWeekends = false);
        panel.Children.Add(weekends);

        var campus = new TextBox { Header = "Preferred campus", Text = session.Store.Preferences.PreferredCampus ?? "" };
        campus.LostFocus += (_, _) => SavePrefs(p => p.PreferredCampus = string.IsNullOrWhiteSpace(campus.Text) ? null : campus.Text.Trim());
        panel.Children.Add(campus);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var export = new Button { Content = "Export data" };
        export.Click += (_, _) =>
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "term-organize-data.json");
            session.Store.ExportTo(path);
            window.SetStatus("Exported your data to " + path);
        };
        var import = new Button { Content = "Import data…" };
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
    }

    private void SavePrefs(Action<AppPreferences> change)
    {
        var prefs = _session.Store.Preferences;
        change(prefs);
        _session.Store.ReplacePreferences(prefs);
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
}
