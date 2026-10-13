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
    private readonly ComboBox _subject = new() { PlaceholderText = "Subject", MinWidth = 160 };
    private readonly ListView _results = new();
    private readonly StackPanel _detail = new() { Spacing = 8 };
    private SearchResponse? _response;

    public SearchPage(AppSession session)
    {
        _session = session;
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        filters.Children.Add(_open);
        filters.Children.Add(_honors);
        filters.Children.Add(_subject);
        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(_query);
        left.Children.Add(filters);
        left.Children.Add(_results);
        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        split.Children.Add(left);
        var scroll = new ScrollViewer { Content = _detail, Padding = new Thickness(16, 0, 0, 0) };
        Grid.SetColumn(scroll, 1);
        split.Children.Add(scroll);
        Content = split;
        Padding = new Thickness(12);

        _query.TextChanged += (_, _) => Run(_query.Text);
        _open.Checked += (_, _) => Run(_query.Text);
        _open.Unchecked += (_, _) => Run(_query.Text);
        _honors.Checked += (_, _) => Run(_query.Text);
        _honors.Unchecked += (_, _) => Run(_query.Text);
        _subject.SelectionChanged += (_, _) => Run(_query.Text);
        _results.SelectionChanged += (_, _) => ShowSelected();
        FillSubjects();
        ShowEmpty();
    }

    public void Run(string text)
    {
        _query.Text = text;
        var index = _session.Index;
        if (index is null)
        {
            Chrome.Fill(_detail, "Catalog", _session.Error ?? "Loading the bundled Fall 2026 catalog…");
            return;
        }
        var filters = new SearchFilters
        {
            OpenSeatsOnly = _open.IsChecked == true,
            HonorsOnly = _honors.IsChecked == true,
        };
        if (_subject.SelectedItem is SubjectChoice choice && choice.Code is not null) filters.Subjects.Add(choice.Code);
        _response = SearchEngine.Search(text, index, filters);
        _results.Items.Clear();
        foreach (var result in _response.Results.Take(80))
            _results.Items.Add(new ResultRow(result));
        if (_response.CrnMatch is CourseSection section)
            ShowCourse(section.CourseKey);
        else if (_results.Items.Count > 0)
            _results.SelectedIndex = 0;
        else
            Chrome.Fill(_detail, "No matches", "Try a course code, a nickname like orgo or linalg, or a professor's last name.");
        if (text.Trim().Length >= 2) _session.Store.RecordSearch(text);
    }

    public void ShowCourse(CourseKey key)
    {
        var index = _session.Index;
        var course = index?.Course(key);
        if (index is null || course is null)
        {
            Chrome.Fill(_detail, key.ToString(), "That course is not in the bundled snapshot.");
            return;
        }
        _session.Store.RecordView(key, index.Term.Code);
        var stats = index.Stats(key);
        _detail.Children.Clear();
        _detail.Children.Add(Chrome.Heading($"{course.Code}  {course.Title}"));
        _detail.Children.Add(Chrome.Body($"{course.CreditsLabel} · {stats.AvailabilityLabel} · {stats.SeatsRemaining} seats left"));
        if (!string.IsNullOrWhiteSpace(course.Description))
            _detail.Children.Add(Chrome.Body(course.Description));
        if (course.Prereqs is Requisite prereq)
            _detail.Children.Add(Chrome.Body("Prerequisites: " + (prereq.NormalizedForDisplay()?.Summary ?? prereq.Summary)));
        if (course.Coreqs is Requisite coreq)
            _detail.Children.Add(Chrome.Body("Corequisites: " + (coreq.NormalizedForDisplay()?.Summary ?? coreq.Summary)));
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
        add.Click += (_, _) => AddToSchedule(key, index.Term.Code);
        var refresh = new Button { Content = "Refresh seats" };
        refresh.Click += async (_, _) => await RefreshAsync(course, index.Term.Code);
        actions.Children.Add(save);
        actions.Children.Add(add);
        actions.Children.Add(refresh);
        _detail.Children.Add(actions);

        foreach (var section in index.Sections(key))
            _detail.Children.Add(SectionCard(section, course.Title, index.Term.Code));
    }

    private async Task RefreshAsync(Course course, string term)
    {
        try
        {
            var live = await BannerClient.Shared.FetchSectionsAsync(term, [course.Key]);
            if (live.TryGetValue(course.Key, out var banner) && _session.Index is not null)
            {
                CatalogBuilder.ApplyCourseRefresh(_session.Index.Catalog, course.Key, banner);
                ShowCourse(course.Key);
            }
        }
        catch (Exception ex)
        {
            _detail.Children.Insert(0, Chrome.Body("Banner didn't answer: " + ex.Message));
        }
    }

    private void AddToSchedule(CourseKey key, string term)
    {
        var plan = _session.Store.PrimaryPlan(term) ?? _session.Store.CreatePlan("Schedule", term);
        _session.Store.AddCourse(key, plan.Id);
    }

    private Border SectionCard(CourseSection section, string title, string term)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = $"{section.ClassType} {section.SectionNumber} · CRN {section.Crn}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Chrome.Body($"{section.ScheduleSummary} · {section.InstructorLabel} · {section.Campus}"));
        panel.Children.Add(Chrome.Body($"{section.SeatsLabel}" + (section.HasWaitlist ? $" · waitlist {section.WaitlistRemaining}" : "")));
        var watch = new Button { Content = _session.Store.IsWatching(section.Crn, term) ? "Watching" : "Watch seats", HorizontalAlignment = HorizontalAlignment.Left };
        watch.Click += (_, _) =>
        {
            _session.Store.ToggleWatch(section, title, term);
            watch.Content = _session.Store.IsWatching(section.Crn, term) ? "Watching" : "Watch seats";
        };
        panel.Children.Add(watch);
        return Chrome.Card(panel);
    }

    private void ShowSelected()
    {
        if (_results.SelectedItem is ResultRow row) ShowCourse(row.Key);
    }

    private void FillSubjects()
    {
        _subject.Items.Add(new SubjectChoice(null, "Any subject"));
        _subject.SelectedIndex = 0;
        if (_session.Index is null) return;
        foreach (var subject in _session.Index.ActiveSubjects.OrderBy(s => s.Code))
            _subject.Items.Add(new SubjectChoice(subject.Code, $"{subject.Code} — {subject.Name}"));
    }

    private void ShowEmpty()
    {
        if (_session.Index is null)
        {
            Chrome.Fill(_detail, "Catalog", _session.Error ?? "Loading the bundled Fall 2026 catalog…");
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

    private sealed record SubjectChoice(string? Code, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class ResultRow
    {
        public CourseKey Key { get; }
        private readonly string _label;
        public ResultRow(SearchResult result)
        {
            Key = result.Course.Key;
            _label = $"{result.Course.Code}  {result.Course.Title}  ·  {result.Stats.AvailabilityLabel}";
        }
        public override string ToString() => _label;
    }
}
