// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermOrganize.Core;

namespace TermOrganize;

public sealed class SubjectsPage : UserControl
{
    public SubjectsPage(AppSession session)
    {
        var list = new ListView();
        var detail = new StackPanel { Spacing = 6 };
        var split = Split(list, new ScrollViewer { Content = detail, Padding = new Thickness(16, 0, 0, 0) });
        Content = split;
        Padding = Chrome.PagePadding;
        if (session.Index is null)
        {
            Chrome.Fill(detail, "Subjects", session.Error ?? "The catalog is still loading.");
            return;
        }
        foreach (var subject in session.Index.ActiveSubjects.OrderBy(s => s.Code))
            list.Items.Add(subject.Code + " — " + subject.Name);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not string label) return;
            var code = label.Split(' ')[0];
            detail.Children.Clear();
            detail.Children.Add(Chrome.Heading(label));
            foreach (var course in session.Index.CoursesInSubject(code).Take(200))
                detail.Children.Add(Chrome.Body($"{course.Code}  {course.Title}  ·  {course.CreditsLabel}"));
        };
        if (list.Items.Count > 0) list.SelectedIndex = 0;
    }

    internal static Grid Split(FrameworkElement left, FrameworkElement right) => new PaneSplit(left, right, 320);
}

public sealed class SavedPage : UserControl
{
    public SavedPage(AppSession session)
    {
        var panel = new StackPanel { Spacing = 12, Padding = Chrome.PagePadding, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        Content = new ScrollViewer { Content = panel };
        var term = session.Term?.Code ?? session.Index?.Term.Code;
        panel.Children.Add(Chrome.Heading("Saved"));
        if (term is null || session.Index is null)
        {
            panel.Children.Add(Chrome.Body("The catalog is still loading."));
            return;
        }
        var saved = session.Store.SavedCourses(term);
        if (saved.Count == 0)
        {
            panel.Children.Add(Chrome.Body("Courses you save from search land here, for this term."));
            return;
        }
        foreach (var item in saved)
        {
            var course = session.Index.Course(item.Key);
            panel.Children.Add(Chrome.Card(Chrome.Body(course is null ? item.Key.ToString() : $"{course.Code}  {course.Title}")));
        }
    }
}

public sealed class ProfessorsPage : UserControl
{
    private readonly AppSession _session;
    private readonly TextBox _query = new() { PlaceholderText = "Search professors" };
    private readonly SortableTable _table = new();
    private readonly TextBlock _count = new() { Opacity = 0.75 };
    private List<ProfessorDirectoryEntry> _entries = [];

    public ProfessorsPage(AppSession session)
    {
        _session = session;
        var root = new Grid { Padding = Chrome.PagePadding, RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Chrome.Heading("Professors"));
        Grid.SetRow(_query, 1);
        root.Children.Add(_query);
        Grid.SetRow(_count, 2);
        root.Children.Add(_count);
        Grid.SetRow(_table, 3);
        root.Children.Add(_table);
        Content = root;
        if (session.Index is null)
        {
            _count.Text = session.Error ?? "The catalog is still loading.";
            return;
        }
        _entries = ProfessorDirectoryEntry.Build(session.Index);
        _query.TextChanged += (_, _) => Show(_query.Text);
        _table.RowOpened += tag =>
        {
            if (tag is ProfessorDirectoryEntry entry) ProfessorWindow.Open(_session, entry);
        };
        _count.Text = "Double-click a name to open their sections and week.";
        Show("");
    }

    private void Show(string text)
    {
        var folded = ProfessorDirectoryEntry.Fold(text);
        var matches = _entries.Where(entry => folded.Length == 0 || entry.SearchText.Contains(folded, StringComparison.Ordinal)).Take(300).ToList();
        var index = _session.Index;
        var rows = matches.Select(entry =>
        {
            var email = index?.Instructor(entry.Id)?.Email;
            return new SortableTable.Row
            {
                Tag = entry,
                Cells = [entry.DisplayName, entry.CourseSummary, entry.SectionCount.ToString(), string.IsNullOrWhiteSpace(email) ? "\u2014" : email],
                Sort = [null, null, entry.SectionCount, null],
            };
        }).ToList();
        _table.Show(
        [
            new() { Title = "Name", Width = 220 },
            new() { Title = "Courses", Width = 260 },
            new() { Title = "Sections", Width = 90, Numeric = true },
            new() { Title = "Email", Width = 220 },
        ], rows);
        _count.Text = matches.Count == 0
            ? "No professors match."
            : $"{matches.Count:N0} professors \u00b7 double-click a name to open their window.";
    }
}
