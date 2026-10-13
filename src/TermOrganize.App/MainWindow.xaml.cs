// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.AppLifecycle;
using TermOrganize.Core;
using Windows.ApplicationModel.Activation;
using Windows.Storage.Pickers;
using Windows.System;

namespace TermOrganize;

public sealed partial class MainWindow : Window
{
    private string _area = "search";
    private bool _fillingTerms;
    private bool _exiting;
    private readonly string _themePath = Path.Combine(Path.GetDirectoryName(UserDataStore.DefaultPath())!, "theme.txt");

    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        AppWindow.Closing += (_, args) =>
        {
            if (_exiting) return;
            args.Cancel = true;
            AppWindow.Hide();
        };
        SetTitleBar(DragRegion);
        Disclaimer.Text = LegalText.UnofficialNotice + "  Course information is © Northeastern University.";
        App.Session.Changed += () => DispatcherQueue.TryEnqueue(RefreshChrome);
        ApplySavedTheme();
        WireKeys();
        Nav.SelectedItem = Nav.MenuItems[0];
        Show("search");
        _ = LoadTermsAsync();
    }

    public void HandleActivation(AppActivationArguments args)
    {
        if (args.Kind == ExtendedActivationKind.Protocol && args.Data is IProtocolActivatedEventArgs protocol)
            OpenLink(protocol.Uri);
        else if (args.Kind == ExtendedActivationKind.File && args.Data is IFileActivatedEventArgs files)
        {
            foreach (var file in files.Files)
                if (file.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    ImportDegreePdf(file.Path);
        }
        foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
        {
            if (arg.StartsWith("go:", StringComparison.OrdinalIgnoreCase)) Select(arg[3..]);
            else if (Uri.TryCreate(arg, UriKind.Absolute, out var uri) && (AppLink.IsAppLink(uri) || uri.Scheme == "file"))
            {
                if (uri.IsFile && uri.LocalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) ImportDegreePdf(uri.LocalPath);
                else OpenLink(uri);
            }
            else if (arg.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && File.Exists(arg)) ImportDegreePdf(arg);
        }
    }

    public void OpenLink(Uri uri)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(uri.Host)) parts.Add(uri.Host);
        parts.AddRange(uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (parts.Count >= 4 && parts[0].Equals("course", StringComparison.OrdinalIgnoreCase))
        {
            Select("search");
            if (Host.Children.FirstOrDefault() is SearchPage search)
                search.ShowCourse(new CourseKey(parts[2].ToUpperInvariant(), parts[3].ToUpperInvariant()));
            return;
        }
        var query = QueryValue(uri, "q");
        if (parts.Contains("search") || query is not null)
        {
            Select("search");
            if (query is not null && Host.Children.FirstOrDefault() is SearchPage search) search.Run(query);
            return;
        }
        var area = parts.FirstOrDefault()?.ToLowerInvariant();
        if (area is "subjects" or "saved" or "professors" or "rooms" or "schedules" or "degree" or "watch") Select(area);
    }

    private static string? QueryValue(Uri uri, string key)
    {
        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var name = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]);
            if (!name.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            return eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
        }
        return null;
    }

    public void Select(string tag)
    {
        _area = tag;
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
            if ((item.Tag as string) == tag) Nav.SelectedItem = item;
        if (tag == "settings") Nav.SelectedItem = Nav.SettingsItem;
        Show(tag);
    }

    public void SetUpNext(string text) => UpNextText.Text = text;

    public void SetStatus(string text) => Disclaimer.Text = text;

    private void Show(string tag)
    {
        _area = tag;
        var session = App.Session;
        UIElement page = tag switch
        {
            "subjects" => new SubjectsPage(session),
            "saved" => new SavedPage(session),
            "professors" => new ProfessorsPage(session),
            "rooms" => new RoomsPage(session),
            "schedules" => new SchedulesPage(session, this),
            "degree" => new DegreePage(session, this),
            "watch" => new WatchPage(session),
            "settings" => new SettingsPage(session, this),
            _ => new SearchPage(session),
        };
        Host.Children.Clear();
        Host.Children.Add(page);
        RefreshUpNext();
    }

    private void NavChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected) { Show("settings"); return; }
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag) Show(tag);
    }

    private void RefreshChrome()
    {
        var session = App.Session;
        Title = session.Term is null ? "Term Organize" : $"Term Organize — {session.Term.Name}";
        if (session.Index is not null && TermPicker.Items.Count == 0)
            TermPicker.Items.Add(session.Index.Term);
        RefreshUpNext();
    }

    private async Task LoadTermsAsync()
    {
        IReadOnlyList<Term> live = [];
        try { live = await BannerTermList.FetchAsync(); }
        catch (Exception) { /* offline: the bundled term still works */ }
        DispatcherQueue.TryEnqueue(() =>
        {
            _fillingTerms = true;
            TermPicker.Items.Clear();
            if (App.Session.Index is not null) TermPicker.Items.Add(App.Session.Index.Term);
            foreach (var term in live.Where(t => t.Code != App.Session.Index?.Term.Code).Take(12))
                TermPicker.Items.Add(term);
            TermPicker.SelectedIndex = 0;
            _fillingTerms = false;
        });
    }

    private void TermChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingTerms || TermPicker.SelectedItem is not Term term) return;
        App.Session.Select(term);
        Show(_area);
    }

    private void ToggleTheme(object sender, RoutedEventArgs e)
    {
        var root = (FrameworkElement)Content;
        root.RequestedTheme = root.RequestedTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        Directory.CreateDirectory(Path.GetDirectoryName(_themePath)!);
        File.WriteAllText(_themePath, root.RequestedTheme.ToString());
    }

    private void ApplySavedTheme()
    {
        if (!File.Exists(_themePath)) return;
        var name = File.ReadAllText(_themePath).Trim();
        if (Enum.TryParse<ElementTheme>(name, out var theme))
            ((FrameworkElement)Content).RequestedTheme = theme;
    }

    private void WireKeys()
    {
        var root = (FrameworkElement)Content;
        void Go(VirtualKey key, string tag, VirtualKeyModifiers mods = VirtualKeyModifiers.Control)
        {
            var accel = new KeyboardAccelerator { Key = key, Modifiers = mods };
            accel.Invoked += (_, args) => { args.Handled = true; Select(tag); };
            root.KeyboardAccelerators.Add(accel);
        }
        Go(VirtualKey.Number1, "search");
        Go(VirtualKey.Number2, "subjects");
        Go(VirtualKey.Number3, "saved");
        Go(VirtualKey.Number4, "professors");
        Go(VirtualKey.Number5, "rooms");
        Go(VirtualKey.Number6, "schedules");
        Go(VirtualKey.Number7, "degree");
        Go(VirtualKey.Number8, "watch");
        var comma = new KeyboardAccelerator { Key = VirtualKey.OemComma, Modifiers = VirtualKeyModifiers.Control };
        comma.Invoked += (_, args) => { args.Handled = true; Select("settings"); };
        root.KeyboardAccelerators.Add(comma);
        Command(VirtualKey.N, NewSchedule);
        Command(VirtualKey.N, NewDegree, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift);
        Command(VirtualKey.O, () => _ = ImportDegreeAsync());
        Command(VirtualKey.E, Export);
        Command(VirtualKey.P, Print);
        Command(VirtualKey.R, () => Show(_area));
        var pane = new KeyboardAccelerator { Key = VirtualKey.Number0, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu };
        pane.Invoked += (_, args) => { args.Handled = true; Nav.IsPaneOpen = !Nav.IsPaneOpen; };
        root.KeyboardAccelerators.Add(pane);
    }

    private void Command(VirtualKey key, Action action, VirtualKeyModifiers mods = VirtualKeyModifiers.Control)
    {
        var accel = new KeyboardAccelerator { Key = key, Modifiers = mods };
        accel.Invoked += (_, args) => { args.Handled = true; action(); };
        ((FrameworkElement)Content).KeyboardAccelerators.Add(accel);
    }

    private void MenuNewSchedule(object sender, RoutedEventArgs e) => NewSchedule();
    private void MenuNewDegree(object sender, RoutedEventArgs e) => NewDegree();
    private void MenuImport(object sender, RoutedEventArgs e) => _ = ImportDegreeAsync();
    private void MenuExport(object sender, RoutedEventArgs e) => Export();
    private void MenuPrint(object sender, RoutedEventArgs e) => Print();
    private void MenuSearch(object sender, RoutedEventArgs e) => Select("search");
    private void MenuSubjects(object sender, RoutedEventArgs e) => Select("subjects");
    private void MenuSaved(object sender, RoutedEventArgs e) => Select("saved");
    private void MenuProfessors(object sender, RoutedEventArgs e) => Select("professors");
    private void MenuRooms(object sender, RoutedEventArgs e) => Select("rooms");
    private void MenuSchedules(object sender, RoutedEventArgs e) => Select("schedules");
    private void MenuDegree(object sender, RoutedEventArgs e) => Select("degree");
    private void MenuWatch(object sender, RoutedEventArgs e) => Select("watch");
    private void MenuSettings(object sender, RoutedEventArgs e) => Select("settings");
    private void MenuReload(object sender, RoutedEventArgs e) => Show(_area);
    private void MenuPane(object sender, RoutedEventArgs e) => Nav.IsPaneOpen = !Nav.IsPaneOpen;

    private void NewSchedule()
    {
        var term = App.Session.Term?.Code ?? BundledCatalog.DefaultTerm;
        App.Session.Store.CreatePlan("Schedule", term);
        Select("schedules");
    }

    private void NewDegree()
    {
        App.Session.Store.AddDegreePlan(new DegreePlan { Name = "Degree Plan", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        Select("degree");
    }

    public async Task ImportDegreeAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".pdf");
        picker.FileTypeFilter.Add(".txt");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) ImportDegreePdf(file.Path);
    }

    public void ImportDegreePdf(string path)
    {
        try
        {
            var result = DegreePlanImporter.Import(path);
            App.Session.Store.AddDegreePlan(result.Plan);
            Select("degree");
        }
        catch (Exception ex)
        {
            Disclaimer.Text = ex.Message;
        }
    }

    private void Export()
    {
        if (_area == "schedules" && Host.Children.FirstOrDefault() is SchedulesPage schedules)
            schedules.ExportIcs();
        else if (_area == "degree" && Host.Children.FirstOrDefault() is DegreePage degree)
            degree.ExportPdf();
        else
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "term-organize-data.json");
            App.Session.Store.ExportTo(path);
            Disclaimer.Text = "Exported your data to " + path;
        }
    }

    private void Print()
    {
        if (Host.Children.FirstOrDefault() is DegreePage degree) degree.ExportPdf(open: true);
        else Disclaimer.Text = "Open a degree plan and press Ctrl+P to print its View Plan PDF.";
    }

    private void ShowWindow(object sender, RoutedEventArgs e)
    {
        AppWindow.Show();
        Activate();
    }

    private void CheckSeats(object sender, RoutedEventArgs e) => PlatformServices.CheckSeats(App.Session, this);

    private void ExitApp(object sender, RoutedEventArgs e)
    {
        _exiting = true;
        Tray.Dispose();
        Close();
    }

    public void RefreshUpNext()
    {
        var session = App.Session;
        var term = session.Term;
        var plan = term is null ? null : session.Store.PrimaryPlan(term.Code);
        if (session.Index is null || plan is null || term is null)
        {
            UpNextText.Text = "No class on the calendar yet. Pick sections in a schedule.";
            return;
        }
        var now = DateTime.Now;
        var minutes = now.Hour * 60 + now.Minute;
        var today = (int)now.DayOfWeek;
        string? next = null;
        var best = int.MaxValue;
        foreach (var crn in plan.SelectedCRNs)
        {
            var section = session.Index.Section(crn);
            if (section is null) continue;
            foreach (var meeting in section.WeeklyMeetings)
            {
                if (meeting.Start is not int start || !meeting.Days.Contains(today)) continue;
                var delta = start >= minutes - 60 ? start - minutes : start + 24 * 60 - minutes;
                if (delta < best)
                {
                    best = delta;
                    next = $"{section.CourseKey} · {meeting.Days.CompactLabel} {meeting.TimeLabel} · {meeting.LocationLabel}";
                }
            }
        }
        UpNextText.Text = next ?? "Nothing else today. The tray flyout is Up Next.";
    }
}
