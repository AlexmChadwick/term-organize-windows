// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermOrganize.Core;

namespace TermOrganize;

public sealed class SearchPage : UserControl
{
    private readonly AppSession _session;
    private readonly TextBox _query = new() { PlaceholderText = "Search courses, professors, or a 5-digit CRN" };
    private readonly CheckBox _open = new() { Content = "Open seats" };
    private readonly CheckBox _honors = new() { Content = "Honors" };
    private readonly Button _subjects = new() { Content = "Subject" };
    private readonly Button _nupath = new() { Content = "NUpath" };
    private readonly Button _level = new() { Content = "Level" };
    private readonly ComboBox _days = new() { MinWidth = 140, PlaceholderText = "Meeting days" };
    private readonly Button _campus = new() { Content = "Campus" };
    private readonly Button _all = new() { Content = "All filters" };
    private readonly Button _clear = new() { Content = "Clear", Visibility = Visibility.Collapsed };
    private readonly SortableTable _results = new();
    private readonly StackPanel _detail = new() { Spacing = 8 };
    private readonly SearchFilters _filters = new();
    private bool _filling;
    private SearchResponse? _response;

    public SearchPage(AppSession session)
    {
        _session = session;
        var filters = new WrapLayout { Spacing = 8 };
        filters.Children.Add(_open);
        filters.Children.Add(_honors);
        filters.Children.Add(_subjects);
        filters.Children.Add(_nupath);
        filters.Children.Add(_level);
        filters.Children.Add(_days);
        filters.Children.Add(_campus);
        filters.Children.Add(_all);
        filters.Children.Add(_clear);
        var left = new Grid { RowSpacing = 12 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.Children.Add(_query);
        Grid.SetRow(filters, 1);
        left.Children.Add(filters);
        Grid.SetRow(_results, 2);
        left.Children.Add(_results);
        var scroll = Chrome.Details(_detail);
        Content = new PaneSplit(left, scroll, 420) { Padding = Chrome.PagePadding };
        Chrome.ClipToBounds(this);

        _query.TextChanged += (_, _) => { if (!_filling) Run(_query.Text); };
        _open.Checked += (_, _) => Apply();
        _open.Unchecked += (_, _) => Apply();
        _honors.Checked += (_, _) => Apply();
        _honors.Unchecked += (_, _) => Apply();
        _days.SelectionChanged += (_, _) => { if (!_filling) Apply(); };
        _results.RowChosen += tag => { if (tag is CourseKey key) ShowCourse(key); };
        _results.RowOpened += tag => { if (tag is CourseKey key) CourseWindow.Open(_session, key); };
        if (_session.Store.Preferences.PreferredCampus is string campus && campus.Length > 0)
            _filters.Campuses.Add(campus);
        BuildMenus();
        ShowEmpty();
    }

    public void Run(string text)
    {
        _filling = true;
        _query.Text = text;
        _filling = false;
        var index = _session.Index;
        if (index is null)
        {
            Chrome.Fill(_detail, "Catalog", _session.Error ?? "Loading the catalog\u2026");
            return;
        }
        ReadQuickFilters();
        _response = SearchEngine.Search(text, index, _filters);
        var savedAt = new Dictionary<CourseKey, DateTimeOffset>();
        foreach (var item in _session.Store.SavedCourses(index.Term.Code)) savedAt[item.Key] = item.SavedAt;
        var rows = new List<SortableTable.Row>();
        foreach (var result in _response.Results.Take(200))
        {
            var saved = savedAt.TryGetValue(result.Course.Key, out var when);
            rows.Add(CourseTable.Make(result, saved, saved ? when : null));
        }
        _results.Show(CourseTable.Columns, rows);
        _clear.Visibility = _filters.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (_response.CrnMatch is CourseSection section)
            ShowCourse(section.CourseKey);
        else if (rows.Count > 0)
            ShowCourse((CourseKey)rows[0].Tag);
        else if (text.Trim().Length > 0)
            Chrome.Fill(_detail, "No matches", "Try a course code, a nickname like orgo or linalg, or a professor\u2019s last name.");
        else
            ShowEmpty();
        if (text.Trim().Length >= 2) _session.Store.RecordSearch(text);
    }

    public void ShowCourse(CourseKey key) => CourseDetail.Show(_detail, _session, key, inWindow: false);

    private void Apply() => Run(_query.Text);

    private void ReadQuickFilters()
    {
        _filters.OpenSeatsOnly = _open.IsChecked == true;
        _filters.HonorsOnly = _honors.IsChecked == true;
        _filters.AllowedDays = (_days.SelectedItem as DayChoice)?.Days;
    }

    private void BuildMenus(bool resetDays = false)
    {
        var kept = resetDays ? null : (_days.SelectedItem as DayChoice)?.Days;
        _filling = true;
        _days.Items.Clear();
        _days.Items.Add(new DayChoice(null, "Any days"));
        _days.Items.Add(new DayChoice(DaySet.Weekdays, "Weekdays"));
        _days.Items.Add(new DayChoice(DaySet.Monday | DaySet.Wednesday | DaySet.Friday, "Mon, Wed, Fri"));
        _days.Items.Add(new DayChoice(DaySet.Tuesday | DaySet.Thursday, "Tue, Thu"));
        _days.SelectedIndex = 0;
        if (kept is DaySet wanted)
        {
            for (var i = 0; i < _days.Items.Count; i++)
            {
                if (_days.Items[i] is DayChoice choice && choice.Days is DaySet days && days == wanted)
                    _days.SelectedIndex = i;
            }
        }
        _filling = false;
        _subjects.Flyout = StringChecks("Subjects", "Subject",
            _session.Index?.ActiveSubjects.Select(subject => (subject.Code, $"{subject.Code} \u00b7 {subject.Name}")) ?? [],
            _filters.Subjects);
        _campus.Flyout = StringChecks("Campus", "Campus",
            _session.Index?.CampusNames.Select(name => (name, name)) ?? [],
            _filters.Campuses);
        _nupath.Flyout = ToggleChecks("NUpath", "NUpath",
            Enum.GetValues<NUPath>().Select(path => (path, $"{path.Short()} \u00b7 {path.Name()}")),
            path => _filters.Nupaths.Contains(path),
            (path, on) => { if (on) _filters.Nupaths.Add(path); else _filters.Nupaths.Remove(path); },
            () => _filters.Nupaths.Clear());
        _level.Flyout = ToggleChecks("Level", "Level",
            Enumerable.Range(0, 9).Select(level => (level, BrowseSummary.LevelTitle(level))),
            level => _filters.Levels.Contains(level),
            (level, on) => { if (on) _filters.Levels.Add(level); else _filters.Levels.Remove(level); },
            () => _filters.Levels.Clear());
        _all.Flyout = AllFilters();
        _clear.Click -= ClearFilters;
        _clear.Click += ClearFilters;
    }

    private void ClearFilters(object sender, RoutedEventArgs args)
    {
        _filters.Subjects.Clear();
        _filters.Nupaths.Clear();
        _filters.Levels.Clear();
        _filters.Campuses.Clear();
        _filters.ClassTypes.Clear();
        _filters.MinCredits = null;
        _filters.MaxCredits = null;
        _filters.Instruction = InstructionFilter.Any;
        _filters.StartsAfter = null;
        _filters.EndsBefore = null;
        _open.IsChecked = false;
        _honors.IsChecked = false;
        BuildMenus(resetDays: true);
        Apply();
    }

    private Flyout StringChecks(string title, string button, IEnumerable<(string Id, string Label)> items, HashSet<string> selected) =>
        ToggleChecks(title, button, items, selected.Contains, (id, on) =>
        {
            if (on) selected.Add(id);
            else selected.Remove(id);
        }, selected.Clear);

    private Flyout ToggleChecks<T>(string title, string button, IEnumerable<(T Id, string Label)> items, Func<T, bool> isOn, Action<T, bool> set, Action clear)
    {
        var panel = new StackPanel { Spacing = 4, MinWidth = 220, MaxWidth = 360 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        foreach (var (id, label) in items)
        {
            var box = new CheckBox
            {
                Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 },
                IsChecked = isOn(id),
            };
            var captured = id;
            box.Checked += (_, _) => { set(captured, true); Apply(); };
            box.Unchecked += (_, _) => { set(captured, false); Apply(); };
            panel.Children.Add(box);
        }
        var clearButton = new Button { Content = "Clear " + button, HorizontalAlignment = HorizontalAlignment.Left };
        clearButton.Click += (_, _) =>
        {
            clear();
            BuildMenus();
            Apply();
        };
        panel.Children.Add(clearButton);
        var flyoutScroll = new ScrollViewer { Content = panel, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Chrome.KeepInside(flyoutScroll);
        return new Flyout { Content = flyoutScroll };
    }

    private Flyout AllFilters()
    {
        var index = _session.Index;
        var panel = new StackPanel { Spacing = 8, Width = 280 };
        var min = new NumberBox { Header = "Minimum credits", Value = _filters.MinCredits ?? double.NaN, Minimum = 0, Maximum = 20, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var max = new NumberBox { Header = "Maximum credits", Value = _filters.MaxCredits ?? double.NaN, Minimum = 0, Maximum = 20, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var instruction = new ComboBox { Header = "Instruction", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var value in Enum.GetValues<InstructionFilter>())
            instruction.Items.Add(new InstructionChoice(value));
        instruction.SelectedIndex = (int)_filters.Instruction;
        var start = TimeBox("Starts after", _filters.StartsAfter);
        var end = TimeBox("Ends before", _filters.EndsBefore);
        panel.Children.Add(min);
        panel.Children.Add(max);
        panel.Children.Add(instruction);
        panel.Children.Add(start);
        panel.Children.Add(end);
        if (index is not null)
        {
            panel.Children.Add(new TextBlock { Text = "Class type", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var type in index.ClassTypes)
            {
                var box = new CheckBox { Content = type, IsChecked = _filters.ClassTypes.Contains(type) };
                var code = type;
                box.Checked += (_, _) => { _filters.ClassTypes.Add(code); Apply(); };
                box.Unchecked += (_, _) => { _filters.ClassTypes.Remove(code); Apply(); };
                panel.Children.Add(box);
            }
        }
        void Read()
        {
            _filters.MinCredits = double.IsNaN(min.Value) ? null : min.Value;
            _filters.MaxCredits = double.IsNaN(max.Value) ? null : max.Value;
            if (instruction.SelectedItem is InstructionChoice choice) _filters.Instruction = choice.Filter;
            _filters.StartsAfter = (start.SelectedItem as TimeChoice)?.Minutes;
            _filters.EndsBefore = (end.SelectedItem as TimeChoice)?.Minutes;
            Apply();
        }
        min.ValueChanged += (_, _) => Read();
        max.ValueChanged += (_, _) => Read();
        instruction.SelectionChanged += (_, _) => Read();
        start.SelectionChanged += (_, _) => Read();
        end.SelectionChanged += (_, _) => Read();
        var filterScroll = new ScrollViewer { Content = panel, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Chrome.KeepInside(filterScroll);
        return new Flyout { Content = filterScroll };
    }

    private static ComboBox TimeBox(string header, int? selected)
    {
        var box = new ComboBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch };
        box.Items.Add(new TimeChoice(null, "Any time"));
        for (var minutes = 7 * 60; minutes <= 22 * 60; minutes += 30)
            box.Items.Add(new TimeChoice(minutes, TimeOfDay.Label(minutes)));
        box.SelectedIndex = 0;
        if (selected is int value)
        {
            for (var i = 0; i < box.Items.Count; i++)
                if (box.Items[i] is TimeChoice choice && choice.Minutes == value) box.SelectedIndex = i;
        }
        return box;
    }

    private void ShowEmpty()
    {
        if (_session.Index is null)
        {
            Chrome.Fill(_detail, "Catalog", _session.Error ?? "Loading the catalog\u2026");
            return;
        }
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Chrome.Heading("Search the catalog"));
        panel.Children.Add(Chrome.Body($"{_session.Index.Courses.Count:N0} courses in {_session.Index.Term.Name}. Codes, nicknames, and typos all work."));
        foreach (var example in SearchExampleCache.Examples(_session.Index).Take(6))
        {
            var button = new Button { Content = example.Caption, HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => Run(example.Caption);
            panel.Children.Add(button);
        }
        _detail.Children.Clear();
        _detail.Children.Add(panel);
    }

    private sealed record DayChoice(DaySet? Days, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record TimeChoice(int? Minutes, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record InstructionChoice(InstructionFilter Filter)
    {
        public override string ToString() => Filter.Label();
    }

}
