// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using H.NotifyIcon;
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
using Windows.UI;

namespace TermOrganize;

public sealed partial class MainWindow : Window
{
    private string _area = "search";
    private bool _fillingTerms;
    private bool _exiting;
    private bool _mica;
    private bool _suppressNav;
    private bool _syncingChrome;
    private bool _compact;
    private double _appliedInset = -1;
    private int _captureWidth;
    private int _termGeneration;
    private CatalogIndex? _shownIndex;
    private string? _shownError;
    private readonly string _themePath = Path.Combine(Path.GetDirectoryName(UserDataStore.DefaultPath())!, "theme.txt");
    private readonly TextBlock _upNext = new() { Text = "No class on the calendar yet.", TextWrapping = TextWrapping.Wrap };
    private TaskbarIcon? _tray;

    public MainWindow()
    {
        InitializeComponent();
        ScreenshotSweep.Log("window xaml loaded");
        try
        {
            _tray = BuildTray();
        }
        catch (Exception ex)
        {
            // The tray is not part of the window parse. A failure here must not look like a XAML load error.
            ScreenshotSweep.Log("tray: " + ScreenshotSweep.Describe(ex));
        }
        try
        {
            SystemBackdrop = new MicaBackdrop();
            _mica = true;
        }
        catch (Exception)
        {
            // Some Windows SKUs have no Mica backdrop. SolidBackgroundFillColorBase covers the window.
        }
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        // Only the title text is the drag region. The term box and theme button stay real controls.
        SetTitleBar(DragRegion);
        Root.SizeChanged += (_, _) => SyncChromeMetrics();
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

    public void SetUpNext(string text) => _upNext.Text = text;

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
        if (_suppressNav) return;
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
        var next = Root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        ApplyTheme(next);
        Directory.CreateDirectory(Path.GetDirectoryName(_themePath)!);
        File.WriteAllText(_themePath, next.ToString());
    }

    private void ApplySavedTheme()
    {
        var theme = Root.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        if (File.Exists(_themePath)
            && Enum.TryParse<ElementTheme>(File.ReadAllText(_themePath).Trim(), out var saved)
            && saved is ElementTheme.Light or ElementTheme.Dark)
            theme = saved;
        ApplyTheme(theme);
    }

    /// <summary>
    /// Element.RequestedTheme on the root, the title bar, and the pane.
    /// Application.RequestedTheme cannot change after startup, and a Default element theme
    /// leaves the pane and caption controls on the wrong brushes.
    /// </summary>
    private void ApplyTheme(ElementTheme theme)
    {
        if (theme == ElementTheme.Default)
            theme = Root.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        // Brushes are assigned before the theme flip so the selected pill does not keep the previous theme.
        PaintChrome(theme);
        void Apply(FrameworkElement element)
        {
            element.RequestedTheme = ElementTheme.Default;
            element.RequestedTheme = theme;
        }
        Apply(Root);
        Apply(TitleBar);
        Apply(Nav);
        Apply(Commands);
        Apply(TermPicker);
        Apply(ThemeButton);
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
            Apply(item);
        if (Nav.SettingsItem is NavigationViewItem settings)
            Apply(settings);
        var selected = Nav.SelectedItem;
        _suppressNav = true;
        Nav.SelectedItem = null;
        Nav.SelectedItem = selected;
        _suppressNav = false;
    }

    private void PaintChrome(ElementTheme theme)
    {
        var dark = theme == ElementTheme.Dark;
        var page = Chrome.ThemedBrush(theme, "SolidBackgroundFillColorBaseBrush",
            dark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243));
        var fill = Chrome.ThemedBrush(theme, "ControlFillColorDefaultBrush",
            dark ? Color.FromArgb(255, 51, 51, 51) : Color.FromArgb(255, 255, 255, 255));
        var hover = Chrome.ThemedBrush(theme, "ControlFillColorSecondaryBrush",
            dark ? Color.FromArgb(255, 69, 69, 69) : Color.FromArgb(255, 246, 246, 246));
        var fg = Chrome.ThemedBrush(theme, "TextFillColorPrimaryBrush",
            dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 27, 27, 27));
        var stroke = Chrome.ThemedBrush(theme, "ControlStrokeColorDefaultBrush",
            dark ? Color.FromArgb(255, 96, 96, 96) : Color.FromArgb(255, 200, 200, 200));
        var muted = Chrome.ThemedBrush(theme, "TextFillColorSecondaryBrush",
            dark ? Color.FromArgb(255, 200, 200, 200) : Color.FromArgb(255, 96, 96, 96));
        // Mica shows through a transparent root. Screenshots and SKUs without Mica use the solid fill,
        // because RenderTargetBitmap records a transparent Mica surface as black.
        var showMica = _mica && !ScreenshotSweep.Enabled;
        Root.Background = showMica ? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) : page;
        TitleBar.Background = page;
        Commands.Background = page;
        Nav.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        // The pane template uses a transparent or acrylic brush, which captures as black.
        Nav.Resources["NavigationViewExpandedPaneBackground"] = page;
        Nav.Resources["NavigationViewDefaultPaneBackground"] = page;
        Nav.Resources["NavigationViewContentBackground"] = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        Nav.Resources["NavigationViewBorderThickness"] = new Thickness(0);
        Nav.Resources["NavigationViewPaneContentGridMargin"] = new Thickness(0);
        var ink = dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(0xE4, 0, 0, 0);
        var pill = dark ? Color.FromArgb(0x2A, 255, 255, 255) : Color.FromArgb(0x1A, 0, 0, 0);
        var hoverFill = dark ? Color.FromArgb(0x14, 255, 255, 255) : Color.FromArgb(0x0F, 0, 0, 0);
        var pressedFill = dark ? Color.FromArgb(0x1F, 255, 255, 255) : Color.FromArgb(0x18, 0, 0, 0);
        var selectedHover = dark ? Color.FromArgb(0x33, 255, 255, 255) : Color.FromArgb(0x26, 0, 0, 0);
        void NavBrush(string key, Color color) => Nav.Resources[key] = new SolidColorBrush(color);
        foreach (var key in new[]
        {
            "NavigationViewItemForeground",
            "NavigationViewItemForegroundPointerOver",
            "NavigationViewItemForegroundPressed",
            "NavigationViewItemForegroundSelected",
            "NavigationViewItemForegroundSelectedPointerOver",
            "NavigationViewItemForegroundSelectedPressed",
        })
            NavBrush(key, ink);
        NavBrush("NavigationViewItemBackground", Color.FromArgb(0, 0, 0, 0));
        NavBrush("NavigationViewItemBackgroundPointerOver", hoverFill);
        NavBrush("NavigationViewItemBackgroundPressed", pressedFill);
        NavBrush("NavigationViewItemBackgroundSelected", pill);
        NavBrush("NavigationViewItemBackgroundSelectedPointerOver", selectedHover);
        NavBrush("NavigationViewItemBackgroundSelectedPressed", selectedHover);
        PaintControl(TermPicker, fill, hover, fg, stroke, "ComboBox");
        TermPicker.Background = fill;
        TermPicker.Foreground = fg;
        TermPicker.BorderBrush = stroke;
        TermPicker.Resources["ComboBoxPlaceHolderForeground"] = muted;
        TermPicker.Resources["ComboBoxPlaceHolderForegroundPointerOver"] = muted;
        TermPicker.Resources["ComboBoxDropDownGlyphForeground"] = fg;
        TermPicker.Resources["ComboBoxForegroundFocused"] = fg;
        PaintControl(ThemeButton, fill, hover, fg, stroke, "Button");
        ThemeButton.Background = fill;
        ThemeButton.Foreground = fg;
        ThemeButton.BorderBrush = stroke;
        PaintCaptionButtons(dark);
    }

    private static void PaintControl(FrameworkElement element, Brush fill, Brush hover, Brush foreground, Brush stroke, string prefix)
    {
        element.Resources[prefix + "Background"] = fill;
        element.Resources[prefix + "BackgroundPointerOver"] = hover;
        element.Resources[prefix + "BackgroundPressed"] = hover;
        element.Resources[prefix + "Foreground"] = foreground;
        element.Resources[prefix + "ForegroundPointerOver"] = foreground;
        element.Resources[prefix + "ForegroundPressed"] = foreground;
        element.Resources[prefix + "BorderBrush"] = stroke;
        element.Resources[prefix + "BorderBrushPointerOver"] = stroke;
        element.Resources[prefix + "BorderBrushPressed"] = stroke;
    }

    private void PaintCaptionButtons(bool dark)
    {
        var fg = dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 27, 27, 27);
        var inactive = dark ? Color.FromArgb(255, 160, 160, 160) : Color.FromArgb(255, 110, 110, 110);
        var hover = dark ? Color.FromArgb(32, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
        var pressed = dark ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(36, 0, 0, 0);
        var clear = Color.FromArgb(0, 0, 0, 0);
        var bar = AppWindow.TitleBar;
        bar.BackgroundColor = clear;
        bar.InactiveBackgroundColor = clear;
        bar.ButtonBackgroundColor = clear;
        bar.ButtonInactiveBackgroundColor = clear;
        bar.ButtonForegroundColor = fg;
        bar.ButtonInactiveForegroundColor = inactive;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = fg;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = fg;
    }

    private void SyncChromeMetrics()
    {
        if (_syncingChrome) return;
        var width = Root.ActualWidth;
        if (width < 1) return;
        _syncingChrome = true;
        try
        {
            var inset = width >= 1008 ? 36.0 : 24.0;
            Chrome.UsePageWidth(width);
            Disclaimer.Margin = new Thickness(inset, 12, inset, 16);
            TitleBar.Padding = new Thickness(inset, 0, 0, 0);
            var compact = width < 1008;
            if (compact != _compact || Nav.PaneDisplayMode == NavigationViewPaneDisplayMode.Auto)
            {
                _compact = compact;
                Nav.PaneDisplayMode = compact ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
                Nav.IsPaneOpen = !compact;
            }
            if (ScreenshotSweep.Enabled || Math.Abs(inset - _appliedInset) < 0.1) return;
            _appliedInset = inset;
            if (_area.Length > 0) Show(_area);
        }
        finally
        {
            _syncingChrome = false;
        }
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
        _captureWidth = width;
        Chrome.UsePageWidth(width);
        AppWindow.Move(new Windows.Graphics.PointInt32(32, 32));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        SyncCaptionSpacer();
        SyncChromeMetrics();
        Chrome.UsePageWidth(width);
    }

    public async Task RunScreenshotSweepAsync()
    {
        try
        {
            await ScreenshotSweep.CaptureAsync(this);
            try { FinishSweep(); } catch (Exception ex) { ScreenshotSweep.Log("finish: " + ScreenshotSweep.Describe(ex)); }
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
        if (_captureWidth > 0) Chrome.UsePageWidth(_captureWidth);
        HideCaptureOverlay();
        switch (tag)
        {
            case "course":
                Select("search");
                if (Host.Children.FirstOrDefault() is SearchPage courseSearch)
                    courseSearch.Run(CourseQuery());
                break;
            case "generated":
                Select("schedules");
                if (Host.Children.FirstOrDefault() is SchedulesPage schedules)
                    schedules.GenerateForCapture();
                break;
            case "survey":
                Select("schedules");
                ShowSurveyOverlay();
                break;
            case "watch-items":
                SeedWatchItems();
                Select("watch");
                break;
            case "about":
                Select("settings");
                ShowAboutOverlay();
                break;
            case "dialog":
                Select("search");
                ShowDialogOverlay();
                break;
            default:
                Select(tag);
                if (tag == "search" && Host.Children.FirstOrDefault() is SearchPage search)
                    search.Run("CS");
                break;
        }
        ClearCaptureFocus();
    }

    public void ApplyCaptureTheme(ElementTheme theme) => ApplyTheme(theme);

    public void HideCaptureOverlay()
    {
        CaptureLayer.Children.Clear();
        CaptureLayer.Visibility = Visibility.Collapsed;
    }

    private void ClearCaptureFocus()
    {
        try
        {
            Nav.UseSystemFocusVisuals = false;
            TermPicker.UseSystemFocusVisuals = false;
            ThemeButton.UseSystemFocusVisuals = false;
            foreach (var item in Nav.MenuItems.OfType<Control>())
                item.UseSystemFocusVisuals = false;
            if (Nav.SettingsItem is Control settings)
                settings.UseSystemFocusVisuals = false;
            if (Host.Children.FirstOrDefault() is Control page)
            {
                page.UseSystemFocusVisuals = false;
                page.Focus(FocusState.Pointer);
            }
        }
        catch (Exception)
        {
            // A missing focus target should not fail the sweep.
        }
    }

    private static string CourseQuery()
    {
        var index = App.Session.Index;
        if (index is null) return "CS";
        var exact = index.Courses.FirstOrDefault(course => course.Subject == "CS" && course.Number == "1200");
        if (exact is not null) return exact.Code;
        var cs = index.Courses.FirstOrDefault(course => course.Subject == "CS" && index.Sections(course.Key).Count > 0);
        return cs?.Code ?? "CS";
    }

    private void SeedWatchItems()
    {
        var index = App.Session.Index;
        if (index is null) return;
        var term = index.Term.Code;
        foreach (var section in index.SectionsByCrn.Values.Where(section => section.Crn.Length > 0).Take(4))
        {
            var title = index.Course(section.CourseKey)?.Title ?? section.Title;
            App.Session.Store.Watch(section, title, term);
        }
    }

    private void ShowSurveyOverlay()
    {
        var campuses = App.Session.Index?.CampusNames.ToList() ?? [];
        var survey = new PreferencesSurvey(new SchedulePreferences(), campuses);
        ShowCaptureCard(new ScrollViewer
        {
            Content = survey,
            MaxHeight = 620,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
    }

    private void ShowAboutOverlay()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Chrome.Heading("About Term Organize"));
        panel.Children.Add(Chrome.Body(LegalText.UnofficialNotice));
        panel.Children.Add(Chrome.Body(LegalText.SearchNeuSummary));
        panel.Children.Add(Chrome.Body(LegalText.PrivacyNotice));
        panel.Children.Add(Chrome.Section("License"));
        var license = LegalText.LoadLicenseText() ?? LegalText.LicenseNotice;
        if (license.Length > 1800) license = license[..1800] + "…";
        panel.Children.Add(new TextBlock
        {
            Text = license,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            IsTextSelectionEnabled = true,
        });
        ShowCaptureCard(new ScrollViewer
        {
            Content = panel,
            MaxHeight = 620,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
    }

    private void ShowDialogOverlay()
    {
        var panel = new StackPanel { Spacing = 12, MaxWidth = 460 };
        panel.Children.Add(Chrome.Heading("Welcome to Term Organize"));
        panel.Children.Add(Chrome.Body("Northeastern courses, on your PC."));
        panel.Children.Add(Chrome.Body("Search every class, build a conflict-free schedule, import a degree audit, and get a toast when a seat opens."));
        panel.Children.Add(Chrome.Body("Everything stays on your PC. No accounts, no analytics, no tracking."));
        panel.Children.Add(Chrome.Body(LegalText.UnofficialNotice));
        var button = new Button { Content = "Continue", HorizontalAlignment = HorizontalAlignment.Right };
        if (Chrome.StyleOf("AccentButtonStyle") is Style accent) button.Style = accent;
        panel.Children.Add(button);
        ShowCaptureCard(panel);
    }

    private void ShowCaptureCard(UIElement content)
    {
        var theme = Root.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        var smoke = new Grid
        {
            RequestedTheme = theme,
            Background = new SolidColorBrush(Color.FromArgb(theme == ElementTheme.Dark ? (byte)160 : (byte)96, 0, 0, 0)),
        };
        var frame = new Border
        {
            RequestedTheme = theme,
            MaxWidth = 560,
            MaxHeight = 680,
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Chrome.ThemedBrush(theme, "SolidBackgroundFillColorBaseBrush",
                theme == ElementTheme.Dark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243)),
            BorderBrush = Chrome.ThemedBrush(theme, "CardStrokeColorDefaultBrush",
                theme == ElementTheme.Dark ? Color.FromArgb(255, 80, 80, 80) : Color.FromArgb(255, 220, 220, 220)),
            Child = content,
        };
        smoke.Children.Add(frame);
        CaptureLayer.Children.Clear();
        CaptureLayer.Children.Add(smoke);
        CaptureLayer.Visibility = Visibility.Visible;
    }

    public void FinishSweep()
    {
        _exiting = true;
        _tray?.Dispose();
        _tray = null;
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
        _tray?.Dispose();
        _tray = null;
        Close();
    }

    public void RefreshUpNext()
    {
        var session = App.Session;
        var line = UpNextLines.Current(session.Store, session.Index, DateTime.Now);
        _upNext.Text = line == "Nothing else today." ? "Nothing else today. The tray flyout is Up Next." : line;
    }

    private TaskbarIcon BuildTray()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Up Next", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(_upNext);
        var open = new Button { Content = "Open Term Organize", HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += ShowWindow;
        panel.Children.Add(open);
        var popup = new Border
        {
            Padding = new Thickness(16),
            Width = 280,
            Background = ThemeBrush("SolidBackgroundFillColorBaseBrush", Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush", Windows.UI.Color.FromArgb(255, 200, 200, 200)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = panel,
        };
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Show Term Organize", ShowWindow));
        menu.Items.Add(Item("Check seats now", CheckSeats));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Exit", ExitApp));
        var tray = new TaskbarIcon
        {
            ToolTipText = "Term Organize",
            IconSource = new GeneratedIconSource
            {
                Text = "T",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xC8, 0x10, 0x2E)),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            },
            TrayPopup = popup,
            ContextFlyout = menu,
        };
        return tray;

        static MenuFlyoutItem Item(string text, RoutedEventHandler click)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += click;
            return item;
        }
    }

    private static Brush ThemeBrush(string key, Windows.UI.Color fallback)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
                return brush;
        }
        catch (Exception)
        {
            // A missing theme brush should not take down the tray.
        }
        return new SolidColorBrush(fallback);
    }
}
