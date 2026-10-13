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
    private readonly ListView _results = new();
    private readonly StackPanel _detail = new() { Spacing = 8 };
    private readonly SearchFilters _filters = new();
    private bool _filling;
    private bool _liveRefresh;
    private SearchResponse? _response;

    public SearchPage(AppSession session)
    {
        _session = session;
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        filters.Children.Add(_open);
        filters.Children.Add(_honors);
        filters.Children.Add(_subjects);
        filters.Children.Add(_nupath);
        filters.Children.Add(_level);
        filters.Children.Add(_days);
        filters.Children.Add(_campus);
        filters.Children.Add(_all);
        filters.Children.Add(_clear);
        var filterScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = filters,
        };
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.Children.Add(_query);
        Grid.SetRow(filterScroll, 1);
        left.Children.Add(filterScroll);
        Grid.SetRow(_results, 2);
        left.Children.Add(_results);
        var scroll = new ScrollViewer { Content = _detail, Padding = new Thickness(4, 0, 0, 0) };
        Content = new PaneSplit(left, scroll, 420) { Padding = new Thickness(12) };

        _query.TextChanged += (_, _) => { if (!_filling) Run(_query.Text); };
        _open.Checked += (_, _) => Apply();
        _open.Unchecked += (_, _) => Apply();
        _honors.Checked += (_, _) => Apply();
        _honors.Unchecked += (_, _) => Apply();
        _days.SelectionChanged += (_, _) => { if (!_filling) Apply(); };
        _results.SelectionChanged += (_, _) => { if (_results.SelectedItem is ResultRow row) ShowCourse(row.Key); };
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
        _results.Items.Clear();
        foreach (var result in _response.Results.Take(80))
            _results.Items.Add(new ResultRow(result));
        _clear.Visibility = _filters.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (_response.CrnMatch is CourseSection section)
            ShowCourse(section.CourseKey);
        else if (_results.Items.Count > 0)
            _results.SelectedIndex = 0;
        else if (text.Trim().Length > 0)
            Chrome.Fill(_detail, "No matches", "Try a course code, a nickname like orgo or linalg, or a professor\u2019s last name.");
        else
            ShowEmpty();
        if (text.Trim().Length >= 2) _session.Store.RecordSearch(text);
    }

    public void ShowCourse(CourseKey key)
    {
        var index = _session.Index;
        var course = index?.Course(key);
        if (index is null || course is null)
        {
            Chrome.Fill(_detail, key.ToString(), "That course is not in this term\u2019s catalog.");
            return;
        }
        _session.Store.RecordView(key, index.Term.Code);
        var stats = index.Stats(key);
        _detail.Children.Clear();
        _detail.Children.Add(Chrome.Heading($"{course.Code}  {course.Title}"));
        _detail.Children.Add(Chrome.Body($"{course.CreditsLabel} \u00b7 {stats.AvailabilityLabel} \u00b7 {stats.SeatsRemaining} seats left"));
        if (!string.IsNullOrWhiteSpace(course.Description))
            _detail.Children.Add(Chrome.Body(course.Description));
        if (RequisiteTreeView.Build(course.Prereqs, _session, "Prerequisites") is UIElement prereq)
            _detail.Children.Add(prereq);
        if (RequisiteTreeView.Build(course.Coreqs, _session, "Corequisites") is UIElement coreq)
            _detail.Children.Add(coreq);
        var standing = CourseDegreePlanStandingLookup.For(_session.Store, key);
        if (standing is not null) _detail.Children.Add(Chrome.Body(standing.Text));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var save = new Button { Content = _session.Store.IsSaved(key, index.Term.Code) ? "Saved" : "Save" };
        save.Click += (_, _) =>
        {
            _session.Store.ToggleSaved(key, index.Term.Code);
            save.Content = _session.Store.IsSaved(key, index.Term.Code) ? "Saved" : "Save";
        };
        var add = new Button { Content = "Add to schedule" };
        add.Click += (_, _) =>
        {
            var plan = _session.Store.PrimaryPlan(index.Term.Code) ?? _session.Store.CreatePlan("Schedule", index.Term.Code);
            _session.Store.AddCourse(key, plan.Id);
        };
        var refresh = new Button { Content = "Refresh seats" };
        refresh.Click += async (_, _) => await RefreshAsync(course, index.Term.Code);
        actions.Children.Add(save);
        actions.Children.Add(add);
        actions.Children.Add(refresh);
        _detail.Children.Add(actions);
        foreach (var section in index.Sections(key))
            _detail.Children.Add(SectionCard(section, course.Title, index.Term.Code));
        if (!_liveRefresh && _session.Store.Preferences.AutoRefreshSeats)
            _ = RefreshAsync(course, index.Term.Code);
    }

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
        var panel = new StackPanel { Spacing = 4, MinWidth = 220 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var (id, label) in items)
        {
            var box = new CheckBox { Content = label, IsChecked = isOn(id) };
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
        return new Flyout { Content = new ScrollViewer { Content = panel, MaxHeight = 360 } };
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
        return new Flyout { Content = new ScrollViewer { Content = panel, MaxHeight = 480 } };
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

    private async Task RefreshAsync(Course course, string term)
    {
        _liveRefresh = true;
        try
        {
            var live = await BannerClient.Shared.FetchSectionsAsync(term, [course.Key]);
            if (live.TryGetValue(course.Key, out var banner))
            {
                _session.RefreshCourse(course.Key, banner);
                ShowCourse(course.Key);
            }
        }
        catch (Exception ex)
        {
            _detail.Children.Insert(0, Chrome.Body("Banner didn\u2019t answer: " + ex.Message));
        }
        finally
        {
            _liveRefresh = false;
        }
    }

    private Border SectionCard(CourseSection section, string title, string term)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = $"{section.ClassType} {section.SectionNumber} \u00b7 CRN {section.Crn}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Chrome.Body($"{section.ScheduleSummary} \u00b7 {section.InstructorLabel} \u00b7 {section.Campus}"));
        panel.Children.Add(Chrome.Body($"{section.SeatsLabel}" + (section.HasWaitlist ? $" \u00b7 waitlist {section.WaitlistRemaining}" : "")));
        var watch = new Button { Content = _session.Store.IsWatching(section.Crn, term) ? "Watching" : "Watch seats", HorizontalAlignment = HorizontalAlignment.Left };
        watch.Click += (_, _) =>
        {
            _session.Store.ToggleWatch(section, title, term);
            watch.Content = _session.Store.IsWatching(section.Crn, term) ? "Watching" : "Watch seats";
        };
        panel.Children.Add(watch);
        return Chrome.Card(panel);
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

    private sealed class ResultRow
    {
        public CourseKey Key { get; }
        private readonly string _label;
        public ResultRow(SearchResult result)
        {
            Key = result.Course.Key;
            _label = $"{result.Course.Code}  {result.Course.Title}  \u00b7  {result.Stats.AvailabilityLabel}";
        }
        public override string ToString() => _label;
    }
}
