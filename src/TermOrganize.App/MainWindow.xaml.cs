// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
    private int _termGeneration;
    private CatalogIndex? _shownIndex;
    private string? _shownError;
    private readonly string _themePath = Path.Combine(Path.GetDirectoryName(UserDataStore.DefaultPath())!, "theme.txt");

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch (Exception)
        {
            // Some Windows SKUs have no Mica backdrop. The Fluent theme still applies.
        }
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SetTitleBar(TitleBar);
        Activated += (_, _) => SyncCaptionSpacer();
        AppWindow.Closing += (_, args) =>
        {
            if (_exiting) return;
            args.Cancel = true;
            AppWindow.Hide();
        };
        Disclaimer.Text = LegalText.UnofficialNotice + "  Course information is © Northeastern University.";
        App.Session.Changed += () => DispatcherQueue.TryEnqueue(RefreshChrome);
        ApplySavedTheme();
        WireKeys();
        Nav.SelectedItem = Nav.MenuItems[0];
        Show("search");
        if (App.Session.Index is not null || App.Session.Error is not null) Show("search");
        _ = LoadTermsAsync();
        ScheduleWelcome();
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

    public void SetStatus(string text) =>
        Disclaimer.Text = string.IsNullOrWhiteSpace(text)
            ? LegalText.UnofficialNotice + "  Course information is \u00a9 Northeastern University."
            : text + "  " + LegalText.UnofficialNotice;

    private void Show(string tag)
    {
        _area = tag;
        _shownIndex = App.Session.Index;
        _shownError = App.Session.Error;
        var session = App.Session;
        UIElement page = tag switch
        {
            "subjects" => new SubjectsPage(session),
            "saved" => new SavedPage(session),
            "professors" => new ProfessorsPage(session),
            "rooms" => new RoomsPage(session),
            "schedules" => new SchedulesPage(session, this),
            "degree" => new DegreePage(session, this),
            "watch" => new WatchPage(session, this),
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
            TermPicker.Items.Add(new TermChoice(session.Index.Term));
        if (!ReferenceEquals(_shownIndex, session.Index) || _shownError != session.Error)
            Show(_area);
        else
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
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Add(Term term)
            {
                if (term.Code.Length == 0 || !seen.Add(term.Code)) return;
                TermPicker.Items.Add(new TermChoice(term));
            }
            if (App.Session.Index is not null) Add(App.Session.Index.Term);
            foreach (var term in live) Add(term);
            App.Session.RememberTerms(seen.Count == 0 ? [] : TermPicker.Items.OfType<TermChoice>().Select(choice => choice.Term).ToList());
            var selected = App.Session.Term?.Code ?? App.Session.Store.Preferences.SelectedTermCode;
            TermPicker.SelectedIndex = 0;
            for (var i = 0; i < TermPicker.Items.Count; i++)
            {
                if (TermPicker.Items[i] is TermChoice choice && choice.Term.Code == selected)
                    TermPicker.SelectedIndex = i;
            }
            _fillingTerms = false;
        });
    }

    private async void TermChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingTerms || TermPicker.SelectedItem is not TermChoice choice) return;
        var generation = ++_termGeneration;
        App.Session.Store.ReplacePreferences(CopyPrefs(prefs => prefs.SelectedTermCode = choice.Term.Code));
        await App.Session.SelectAsync(choice.Term, progress: text => DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == _termGeneration) SetStatus(text);
        }));
        if (generation != _termGeneration) return;
        Show(_area);
    }

    private async Task ReloadTermAsync()
    {
        if (App.Session.Term is not Term term)
        {
            Show(_area);
            return;
        }
        var generation = ++_termGeneration;
        await App.Session.SelectAsync(term, force: true, progress: text => DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == _termGeneration) SetStatus(text);
        }));
        if (generation == _termGeneration) Show(_area);
    }

    private AppPreferences CopyPrefs(Action<AppPreferences> change)
    {
        var prefs = App.Session.Store.Preferences;
        change(prefs);
        return prefs;
    }

    private sealed class TermChoice(Term term)
    {
        public Term Term { get; } = term;
        public override string ToString() => string.IsNullOrWhiteSpace(Term.Name) ? Term.Code : Term.Name;
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
        // VK_OEM_COMMA. Windows.System.VirtualKey has no OemComma member.
        var comma = new KeyboardAccelerator { Key = (VirtualKey)188, Modifiers = VirtualKeyModifiers.Control };
        comma.Invoked += (_, args) => { args.Handled = true; Select("settings"); };
        root.KeyboardAccelerators.Add(comma);
        Command(VirtualKey.N, NewSchedule);
        Command(VirtualKey.N, NewDegree, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift);
        Command(VirtualKey.O, () => _ = ImportDegreeAsync());
        Command(VirtualKey.E, Export);
        Command(VirtualKey.P, Print);
        Command(VirtualKey.R, () => _ = ReloadTermAsync());
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
    private void MenuReload(object sender, RoutedEventArgs e) => _ = ReloadTermAsync();
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
            SetStatus(ex.Message);
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
            SetStatus("Exported your data to " + path);
        }
    }

    private void Print()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        switch (Host.Children.FirstOrDefault())
        {
            case DegreePage degree:
                degree.Print(hwnd);
                break;
            case WatchPage watch:
                watch.Print(hwnd);
                break;
            case SchedulesPage schedules:
                schedules.Print(hwnd);
                break;
            default:
                SetStatus("Open a degree plan, a schedule, or the watchlist, then press Ctrl+P.");
                break;
        }
    }

    private bool _welcomeOpen;

    private void SyncCaptionSpacer()
    {
        var inset = AppWindow.TitleBar.RightInset;
        if (inset > 1) CaptionSpacer.Width = new GridLength(inset);
    }

    public void ResizeForCapture(int width, int height)
    {
        AppWindow.Move(new Windows.Graphics.PointInt32(32, 32));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        SyncCaptionSpacer();
    }

    public async Task RunScreenshotSweepAsync()
    {
        try
        {
            await ScreenshotSweep.CaptureAsync(this);
            try { FinishSweep(); } catch (Exception ex) { ScreenshotSweep.Log("finish: " + ex); }
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            ScreenshotSweep.WriteError(ex);
            try { FinishSweep(); } catch (Exception) { }
            Environment.Exit(1);
        }
    }

    public void PrepareCapture(string tag)
    {
        Select(tag);
        if (tag == "search" && Host.Children.FirstOrDefault() is SearchPage search)
            search.Run("CS");
    }

    public void ApplyCaptureTheme(ElementTheme theme) =>
        ((FrameworkElement)Content).RequestedTheme = theme;

    public void FinishSweep()
    {
        _exiting = true;
        Tray.Dispose();
    }

    private void ScheduleWelcome()
    {
        if (ScreenshotSweep.Enabled) return;
        if (App.Session.Store.Preferences.HasCompletedOnboarding) return;
        if (Content is not FrameworkElement root)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ScheduleWelcome);
            return;
        }
        if (root.XamlRoot is null)
            root.Loaded += WelcomeLoaded;
        else
            _ = ShowWelcomeAsync();
    }

    private void WelcomeLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement root) root.Loaded -= WelcomeLoaded;
        if (Content is FrameworkElement element && element.XamlRoot is null)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _ = ShowWelcomeAsync());
            return;
        }
        _ = ShowWelcomeAsync();
    }

    private async Task ShowWelcomeAsync()
    {
        if (_welcomeOpen || App.Session.Store.Preferences.HasCompletedOnboarding) return;
        if (Content is not FrameworkElement root || root.XamlRoot is null) return;
        _welcomeOpen = true;
        var panel = new StackPanel { Spacing = 14, MaxWidth = 460 };
        panel.Children.Add(new TextBlock
        {
            Text = "Northeastern courses, on your PC.",
            FontSize = 16,
            Opacity = 0.8,
        });
        panel.Children.Add(WelcomeFeature("Search every class", "Find courses by code, title, professor, or CRN, with live seats."));
        panel.Children.Add(WelcomeFeature("Build your schedule", "Generate conflict-free schedules and export them to Calendar."));
        panel.Children.Add(WelcomeFeature("Plan your degree", "Import your degree audit plan and check it against the requirements."));
        panel.Children.Add(WelcomeFeature("Get seat alerts", "Watch full sections and get notified when seats open."));
        panel.Children.Add(WelcomeFeature("Find a free room", "See which classrooms are empty right now."));
        ComboBox? campus = null;
        var names = App.Session.Index?.CampusNames ?? [];
        if (names.Count > 1)
        {
            campus = new ComboBox { Header = "Where do you take classes?", HorizontalAlignment = HorizontalAlignment.Stretch };
            campus.Items.Add("No preference");
            foreach (var name in names.Take(10)) campus.Items.Add(name);
            var saved = App.Session.Store.Preferences.PreferredCampus;
            campus.SelectedItem = saved is not null && campus.Items.Contains(saved) ? saved : "No preference";
            panel.Children.Add(campus);
            panel.Children.Add(Chrome.Body("Searches start with this campus selected. You can change it any time in Settings."));
        }
        panel.Children.Add(Chrome.Body("Everything stays on your PC. No accounts, no analytics, no tracking."));
        panel.Children.Add(Chrome.Body("Unofficial: not affiliated with Northeastern University or Sandbox. Based on SearchNEU by Sandbox at Northeastern University, free software under the " + LegalText.LicenseName + "."));
        var about = new Button { Content = "About & Licenses\u2026", HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(about);
        var dialog = new ContentDialog
        {
            Title = "Welcome to Term Organize",
            PrimaryButtonText = "Continue",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root.XamlRoot,
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
        };
        about.Click += (_, _) =>
        {
            dialog.Hide();
            Select("settings");
        };
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception)
        {
            _welcomeOpen = false;
            return;
        }
        var prefs = App.Session.Store.Preferences;
        prefs.HasCompletedOnboarding = true;
        if (campus?.SelectedItem is string chosen && chosen != "No preference")
            prefs.PreferredCampus = chosen;
        App.Session.Store.ReplacePreferences(prefs);
    }

    private static StackPanel WelcomeFeature(string title, string message)
    {
        var block = new StackPanel { Spacing = 2 };
        block.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        block.Children.Add(Chrome.Body(message));
        return block;
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
        var line = UpNextLines.Current(session.Store, session.Index, DateTime.Now);
        UpNextText.Text = line == "Nothing else today." ? "Nothing else today. The tray flyout is Up Next." : line;
    }
}
