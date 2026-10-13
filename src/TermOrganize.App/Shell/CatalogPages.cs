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
        Padding = new Thickness(12);
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
    }

    internal static Grid Split(UIElement left, UIElement right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }
}

public sealed class SavedPage : UserControl
{
    public SavedPage(AppSession session)
    {
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(12) };
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
    public ProfessorsPage(AppSession session)
    {
        var query = new TextBox { PlaceholderText = "Search professors" };
        var list = new ListView();
        var detail = new StackPanel { Spacing = 6 };
        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(query);
        left.Children.Add(list);
        Content = SubjectsPage.Split(left, new ScrollViewer { Content = detail, Padding = new Thickness(16, 0, 0, 0) });
        Padding = new Thickness(12);
        if (session.Index is null)
        {
            Chrome.Fill(detail, "Professors", session.Error ?? "The catalog is still loading.");
            return;
        }
        var entries = ProfessorDirectoryEntry.Build(session.Index);
        void Show(string text)
        {
            list.Items.Clear();
            var folded = text.Trim().ToLowerInvariant();
            foreach (var entry in entries.Where(item => folded.Length == 0 || item.SearchText.Contains(folded, StringComparison.Ordinal)).Take(80))
                list.Items.Add(new ProfessorRow(entry));
        }
        query.TextChanged += (_, _) => Show(query.Text);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not ProfessorRow row || session.Index is null) return;
            var entry = row.Entry;
            detail.Children.Clear();
            detail.Children.Add(Chrome.Heading(entry.DisplayName));
            detail.Children.Add(Chrome.Body(entry.CourseSummary));
            var summary = session.Index.Instructor(entry.Id);
            if (summary is null) return;
            foreach (var section in ProfessorPage.Sections(summary, session.Index))
                detail.Children.Add(Chrome.Body($"{section.CourseKey} {section.SectionNumber} · {section.ScheduleSummary} · {section.SeatsLabel}"));
        };
        Show("");
    }

    private sealed class ProfessorRow(ProfessorDirectoryEntry entry)
    {
        public ProfessorDirectoryEntry Entry { get; } = entry;
        public override string ToString() => $"{Entry.DisplayName}  ·  {Entry.CourseSummary}";
    }
}

public sealed class RoomsPage : UserControl
{
    public RoomsPage(AppSession session)
    {
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(12) };
        Content = new ScrollViewer { Content = panel };
        panel.Children.Add(Chrome.Heading("Free rooms"));
        if (session.Index is null)
        {
            panel.Children.Add(Chrome.Body(session.Error ?? "The catalog is still loading."));
            return;
        }
        var moment = RoomAvailability.Moment.From(DateTime.Now);
        panel.Children.Add(Chrome.Body("Rooms with no class at " + moment.Label + ", from the bundled snapshot."));
        var buildings = RoomAvailability.Of(session.Index.Buildings, moment);
        var shown = 0;
        foreach (var building in RoomAvailability.Sorted(buildings, RoomAvailability.BuildingSort.MostFree).Take(12))
        {
            if (building.Free.Count == 0) continue;
            var body = new StackPanel { Spacing = 2 };
            body.Children.Add(new TextBlock { Text = $"{building.Building.Name} · {building.FreeCount} free", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var room in building.Free.Take(8))
                body.Children.Add(Chrome.Body(room.Room.Label));
            panel.Children.Add(Chrome.Card(body));
            if (++shown == 8) break;
        }
        if (shown == 0) panel.Children.Add(Chrome.Body("No free classrooms at this minute. Try again between classes."));
    }
}
