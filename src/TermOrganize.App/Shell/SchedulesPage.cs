// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermOrganize.Core;
using Windows.ApplicationModel.DataTransfer;

namespace TermOrganize;

public sealed class SchedulesPage : UserControl
{
    private readonly AppSession _session;
    private readonly MainWindow _window;
    private readonly ComboBox _plans = new() { MinWidth = 220 };
    private readonly StackPanel _courses = new() { Spacing = 8 };
    private readonly StackPanel _results = new() { Spacing = 8 };
    private readonly WeekCalendarView _week = new();
    private readonly TextBlock _conflicts = new() { TextWrapping = TextWrapping.Wrap };
    private CheckBox? _hideFull;
    private CheckBox? _includeHonors;
    private CheckBox? _includeOnline;
    private NumberBox? _minSeats;
    private ComboBox? _campus;
    private ComboBox? _freeDay;
    private SchedulePlan? _plan;
    private bool _filling = true;

    public SchedulesPage(AppSession session, MainWindow window)
    {
        _session = session;
        _window = window;
        var bar = new WrapLayout { Spacing = 8 };
        bar.Children.Add(_plans);
        bar.Children.Add(Button("Generate", (_, _) => Generate()));
        bar.Children.Add(Button("Ideal week\u2026", (_, _) => _ = SurveyAsync()));
        bar.Children.Add(Button("Copy CRNs", (_, _) => CopyCrns()));
        bar.Children.Add(Button("Export .ics", (_, _) => ExportIcs()));
        bar.Children.Add(Button("Print", (_, _) => Print(WinRT.Interop.WindowNative.GetWindowHandle(_window))));
        bar.Children.Add(Button("Export image", (_, _) => ExportImage()));
        bar.Children.Add(Button("Add to Windows Calendar", (_, _) => _ = AddToCalendarAsync()));
        var left = new StackPanel { Spacing = 12 };
        left.Children.Add(Chrome.Heading("Schedules"));
        left.Children.Add(bar);
        left.Children.Add(Filters());
        left.Children.Add(_courses);
        left.Children.Add(_conflicts);
        left.Children.Add(_results);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var scroll = new ScrollViewer { Content = left };
        root.Children.Add(scroll);
        Grid.SetRow(_week, 1);
        root.Children.Add(_week);
        Content = root;
        Padding = Chrome.PagePadding;
        _plans.SelectionChanged += (_, _) => { if (!_filling) ShowPlan(); };
        ReloadPlans();
    }

    public void Print(nint owner)
    {
        if (_plan is null)
        {
            _window.SetStatus("Open a schedule, then press Ctrl+P.");
            return;
        }
        var lines = new List<string> { _plan.Name };
        if (_session.Term is not null) lines.Add(_session.Term.Name);
        foreach (var section in SelectedSections())
            lines.Add($"{section.CourseKey} {section.SectionNumber} \u00b7 CRN {section.Crn} \u00b7 {section.ScheduleSummary} \u00b7 {section.SeatsLabel}");
        if (lines.Count <= 2) lines.Add("No sections are on the calendar.");
        if (NativePrint.Show(owner, _plan.Name, lines)) _window.SetStatus("Sent the schedule to the printer.");
        else _window.SetStatus("Print was canceled.");
    }

    public void ExportIcs()
    {
        if (_plan is null || _session.Term is null) return;
        var sections = SelectedSections().ToList();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Sanitize(_plan.Name) + ".ics");
        File.WriteAllText(path, IcsWriter.Write(_plan.Name, _session.Term, sections));
        _window.SetStatus("Wrote " + path);
    }

    private async Task AddToCalendarAsync()
    {
        if (_plan is null || _session.Term is null || _session.Index is null) return;
        var term = _session.Term;
        var sections = SelectedSections().ToList();
        var result = await WindowsCalendar.AddAsync(term, sections, key => _session.Index?.Course(key)?.Title);
        if (result.UsedIcsFallback) ExportIcs();
        _window.SetStatus(result.Message);
    }

    private async Task SurveyAsync()
    {
        if (_plan is null) return;
        var campuses = _session.Index?.CampusNames.ToList() ?? [];
        var survey = new PreferencesSurvey(_plan.Preferences ?? new SchedulePreferences(), campuses);
        var windowHeight = XamlRoot?.Size.Height ?? _window.Content.XamlRoot?.Size.Height ?? 0;
        var dialog = new ContentDialog
        {
            Title = "Your ideal week",
            PrimaryButtonText = "Done",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot ?? _window.Content.XamlRoot,
            Content = Chrome.ConstrainedScroll(survey, Chrome.DialogViewport(windowHeight)),
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var preferences = survey.Read();
        _session.Store.UpdatePlan(_plan.Id, plan => plan.Preferences = preferences.IsDefault ? null : preferences);
        ShowPlan();
    }

    private WrapLayout Filters()
    {
        var row = new WrapLayout { Spacing = 8 };
        var full = _hideFull = new CheckBox { Content = "Hide full sections" };
        var honors = _includeHonors = new CheckBox { Content = "Include honors" };
        var online = _includeOnline = new CheckBox { Content = "Include online" };
        var seats = _minSeats = new NumberBox { Header = "Min seats", Minimum = 0, Maximum = 50, Width = 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var campus = _campus = new ComboBox { Header = "Campus", MinWidth = 140 };
        campus.Items.Add("Any campus");
        foreach (var name in _session.Index?.CampusNames ?? []) campus.Items.Add(name);
        var free = _freeDay = new ComboBox { Header = "Free day", MinWidth = 120 };
        free.Items.Add(new DayChoice(null, "No free day"));
        free.Items.Add(new DayChoice(DaySet.Friday, "Friday"));
        free.Items.Add(new DayChoice(DaySet.Monday, "Monday"));
        free.Items.Add(new DayChoice(DaySet.Thursday | DaySet.Friday, "Thu and Fri"));
        void Save()
        {
            if (_filling || _plan is null) return;
            _session.Store.UpdatePlan(_plan.Id, plan =>
            {
                plan.Filters.HideFullSections = full.IsChecked == true;
                plan.Filters.IncludeHonors = honors.IsChecked == true;
                plan.Filters.IncludeOnline = online.IsChecked == true;
                plan.Filters.MinSeats = double.IsNaN(seats.Value) ? 0 : (int)seats.Value;
                plan.Filters.Campus = campus.SelectedItem as string == "Any campus" ? null : campus.SelectedItem as string;
                plan.Filters.FreeDays = (free.SelectedItem as DayChoice)?.Days ?? DaySet.None;
            });
        }
        full.Checked += (_, _) => Save();
        full.Unchecked += (_, _) => Save();
        honors.Checked += (_, _) => Save();
        honors.Unchecked += (_, _) => Save();
        online.Checked += (_, _) => Save();
        online.Unchecked += (_, _) => Save();
        seats.ValueChanged += (_, _) => Save();
        campus.SelectionChanged += (_, _) => Save();
        free.SelectionChanged += (_, _) => Save();
        row.Children.Add(full);
        row.Children.Add(honors);
        row.Children.Add(online);
        row.Children.Add(seats);
        row.Children.Add(campus);
        row.Children.Add(free);
        return row;
    }

    private void ReloadPlans()
    {
        var term = _session.Term?.Code ?? _session.Index?.Term.Code ?? BundledCatalog.DefaultTerm;
        _filling = true;
        _plans.Items.Clear();
        foreach (var plan in _session.Store.PlansIn(term))
            _plans.Items.Add(new PlanChoice(plan));
        if (_plans.Items.Count == 0)
            _plans.Items.Add(new PlanChoice(_session.Store.CreatePlan("Schedule", term)));
        _plans.SelectedIndex = 0;
        _filling = false;
        ShowPlan();
    }

    private void ShowPlan()
    {
        _plan = (_plans.SelectedItem as PlanChoice)?.Plan;
        _courses.Children.Clear();
        _results.Children.Clear();
        if (_plan is null || _session.Index is null)
        {
            _courses.Children.Add(Chrome.Body(_session.Error ?? "The catalog is still loading."));
            return;
        }
        PaintFilters();
        _courses.Children.Add(Chrome.Body(_plan.Preferences?.Summary(8) ?? "No ideal week yet. Describe one, or set it in the survey."));
        foreach (var course in _plan.Courses)
            _courses.Children.Add(CourseBlock(course));
        var selected = SelectedSections().ToList();
        var conflicts = ScheduleConflicts.Find(selected);
        _conflicts.Text = conflicts.Count == 0 ? "" : string.Join("\n", conflicts.Select(conflict => conflict.Summary));
        _week.Show(selected, key => _plan.Course(key)?.ColorIndex ?? 0, _session.Store.Preferences.CalendarStartHour, _session.Store.Preferences.CalendarEndHour, _session.Store.Preferences.ShowWeekends);
    }

    private void PaintFilters()
    {
        if (_plan is null || _hideFull is null || _includeHonors is null || _includeOnline is null || _minSeats is null || _campus is null || _freeDay is null) return;
        _filling = true;
        _hideFull.IsChecked = _plan.Filters.HideFullSections;
        _includeHonors.IsChecked = _plan.Filters.IncludeHonors;
        _includeOnline.IsChecked = _plan.Filters.IncludeOnline;
        _minSeats.Value = _plan.Filters.MinSeats;
        _campus.SelectedItem = _plan.Filters.Campus ?? "Any campus";
        _freeDay.SelectedIndex = 0;
        for (var i = 0; i < _freeDay.Items.Count; i++)
            if (_freeDay.Items[i] is DayChoice choice && choice.Days is DaySet days && days == _plan.Filters.FreeDays) _freeDay.SelectedIndex = i;
        _filling = false;
    }

    private StackPanel CourseBlock(PlanCourse course)
    {
        var index = _session.Index!;
        var title = index.Course(course.Key)?.Title ?? "";
        var panel = new StackPanel { Spacing = 4 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock { Text = $"{course.Key}  {title}", VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var required = new CheckBox { Content = "Required", IsChecked = course.IsRequired, VerticalAlignment = VerticalAlignment.Center };
        var key = course.Key;
        required.Checked += (_, _) => _session.Store.UpdatePlan(_plan!.Id, plan => PlanEditing.SetRequired(true, key, plan));
        required.Unchecked += (_, _) => _session.Store.UpdatePlan(_plan!.Id, plan => PlanEditing.SetRequired(false, key, plan));
        var colors = new ComboBox { MinWidth = 90 };
        for (var i = 0; i < CourseColorPalette.Count; i++) colors.Items.Add(new ColorChoice(i));
        colors.SelectedIndex = Math.Clamp(course.ColorIndex, 0, CourseColorPalette.Count - 1);
        colors.SelectionChanged += (_, _) =>
        {
            if (colors.SelectedItem is ColorChoice color)
                _session.Store.UpdatePlan(_plan!.Id, plan =>
                {
                    var item = plan.Course(key);
                    if (item is not null) item.ColorIndex = color.Index;
                });
            ShowPlan();
        };
        var remove = new Button { Content = "Remove" };
        remove.Click += (_, _) =>
        {
            _session.Store.RemoveCourse(key, _plan!.Id, index.Sections(key).Select(section => section.Crn));
            ReloadPlans();
        };
        header.Children.Add(required);
        header.Children.Add(colors);
        header.Children.Add(remove);
        panel.Children.Add(header);
        var crns = index.Sections(key).Select(section => section.Crn).ToHashSet(StringComparer.Ordinal);
        foreach (var section in index.Sections(key))
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 0, 0, 0) };
            var consider = new CheckBox { Content = "Consider", IsChecked = !course.ExcludedCRNs.Contains(section.Crn) };
            var crn = section.Crn;
            consider.Checked += (_, _) => _session.Store.UpdatePlan(_plan!.Id, plan => PlanEditing.SetExcluded(false, crn, key, plan));
            consider.Unchecked += (_, _) => _session.Store.UpdatePlan(_plan!.Id, plan => PlanEditing.SetExcluded(true, crn, key, plan));
            var onCalendar = new CheckBox { Content = "On the calendar", IsChecked = _plan!.SelectedCRNs.Contains(section.Crn) };
            onCalendar.Checked += (_, _) =>
            {
                _session.Store.UpdatePlan(_plan!.Id, plan => PlanEditing.SetSection(crn, crns, plan));
                ShowPlan();
            };
            onCalendar.Unchecked += (_, _) =>
            {
                _session.Store.UpdatePlan(_plan!.Id, plan =>
                {
                    if (plan.SelectedCRNs.Contains(crn)) PlanEditing.SetSection(null, crns, plan);
                });
                ShowPlan();
            };
            line.Children.Add(consider);
            line.Children.Add(onCalendar);
            line.Children.Add(new TextBlock { Text = $"{section.ClassType} {section.SectionNumber} \u00b7 CRN {section.Crn} \u00b7 {section.ScheduleSummary} \u00b7 {section.SeatsLabel}", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(line);
        }
        return panel;
    }

    public void GenerateForCapture()
    {
        if (_session.Index is null) return;
        var term = _session.Term?.Code ?? _session.Index.Term.Code;
        var plan = _session.Store.PrimaryPlan(term) ?? _session.Store.CreatePlan("Schedule", term);
        var picks = _session.Index.Courses
            .Select(course => (course.Key, Count: _session.Index.Sections(course.Key).Count))
            .Where(item => item.Count is > 0 and <= 8)
            .Take(3)
            .Select(item => item.Key)
            .ToList();
        if (picks.Count == 0)
        {
            picks = _session.Index.Courses
                .Where(course => _session.Index.Sections(course.Key).Count > 0)
                .Take(2)
                .Select(course => course.Key)
                .ToList();
        }
        foreach (var key in picks)
            _session.Store.AddCourse(key, plan.Id);
        ReloadPlans();
        Generate(applyFirst: true);
    }

    private void Generate(bool applyFirst = false)
    {
        if (_plan is null || _session.Index is null) return;
        var inputs = _plan.Courses.Select(course => new ScheduleCourseInput
        {
            Key = course.Key,
            IsRequired = course.IsRequired,
            Sections = _session.Index.Sections(course.Key),
            ExcludedCrns = course.ExcludedCRNs.ToList(),
            MinCredits = _session.Index.Course(course.Key)?.MinCredits,
            MaxCredits = _session.Index.Course(course.Key)?.MaxCredits,
        }).ToList();
        var result = ScheduleGenerator.Generate(inputs, _plan.Filters, _plan.Preferences);
        _results.Children.Clear();
        _results.Children.Add(Chrome.Heading(result.TotalFound == 0 ? "No schedules" : $"{result.TotalFound} schedules"));
        foreach (var suggestion in result.Suggestions) _results.Children.Add(Chrome.Body(suggestion.ToString() ?? ""));
        foreach (var schedule in result.Schedules.Take(12))
        {
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = string.Join(" \u00b7 ", schedule.Sections.Select(section => $"{section.CourseKey} {section.Crn}")) + $"  \u00b7  {schedule.Score:0}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (schedule.Reasons.Count > 0) panel.Children.Add(Chrome.Body(string.Join(" \u00b7 ", schedule.Reasons)));
            var use = new Button { Content = "Use these sections", HorizontalAlignment = HorizontalAlignment.Left };
            var crns = schedule.Crns.ToList();
            use.Click += (_, _) =>
            {
                _session.Store.UpdatePlan(_plan.Id, plan => plan.SelectedCRNs = crns);
                _window.RefreshUpNext();
                ShowPlan();
            };
            panel.Children.Add(use);
            _results.Children.Add(Chrome.Card(panel));
        }
        if (applyFirst && result.Schedules.Count > 0)
        {
            var crns = result.Schedules[0].Crns.ToList();
            _session.Store.UpdatePlan(_plan.Id, plan => plan.SelectedCRNs = crns);
            var selected = SelectedSections().ToList();
            _week.Show(selected, key => _plan.Course(key)?.ColorIndex ?? 0, _session.Store.Preferences.CalendarStartHour, _session.Store.Preferences.CalendarEndHour, _session.Store.Preferences.ShowWeekends);
        }
    }

    private void CopyCrns()
    {
        var crns = SelectedSections().Select(section => section.Crn).ToList();
        var package = new DataPackage();
        package.SetText(crns.Count == 0 ? "" : string.Join(", ", crns));
        Clipboard.SetContent(package);
        _window.SetStatus(crns.Count == 0 ? "Pick sections first." : "Copied " + string.Join(", ", crns));
    }

    private void ExportImage()
    {
        if (_plan is null || _session.Index is null) return;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Sanitize(_plan.Name) + ".png");
        File.WriteAllBytes(path, ScheduleExporter.PngBytes(_plan, _session.Index));
        _window.SetStatus("Wrote " + path);
    }

    private IEnumerable<CourseSection> SelectedSections()
    {
        if (_plan is null || _session.Index is null) yield break;
        foreach (var crn in _plan.SelectedCRNs)
        {
            var section = _session.Index.Section(crn);
            if (section is not null) yield return section;
        }
    }

    private static Button Button(string label, RoutedEventHandler click)
    {
        var button = new Button { Content = label };
        button.Click += click;
        return button;
    }

    private sealed class PlanChoice(SchedulePlan plan)
    {
        public SchedulePlan Plan { get; } = plan;
        public override string ToString() => Plan.IsPrimary ? Plan.Name + " \u00b7 Primary" : Plan.Name;
    }

    private sealed record DayChoice(DaySet? Days, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ColorChoice(int Index)
    {
        public override string ToString() => CourseColor.At(Index).Name;
    }

    private static string Sanitize(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '-');
        return string.IsNullOrWhiteSpace(name) ? "schedule" : name;
    }
}
