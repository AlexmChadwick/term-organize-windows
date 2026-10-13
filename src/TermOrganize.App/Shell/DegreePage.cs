// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermOrganize.Core;
using Windows.Storage;
using Windows.System;

namespace TermOrganize;

public sealed class DegreePage : UserControl
{
    private readonly AppSession _session;
    private readonly MainWindow _window;
    private readonly ComboBox _plans = new() { MinWidth = 240 };
    private readonly StackPanel _body = new() { Spacing = 8 };
    private DegreePlan? _plan;

    public DegreePage(AppSession session, MainWindow window)
    {
        _session = session;
        _window = window;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        bar.Children.Add(_plans);
        var pdf = new Button { Content = "Export PDF" };
        pdf.Click += (_, _) => ExportPdf();
        var print = new Button { Content = "Print" };
        print.Click += (_, _) => ExportPdf(open: true);
        var autofill = new Button { Content = "Autofill schedule" };
        autofill.Click += (_, _) => Autofill();
        bar.Children.Add(pdf);
        bar.Children.Add(print);
        bar.Children.Add(autofill);
        var root = new StackPanel { Spacing = 12, Padding = new Thickness(12) };
        root.Children.Add(Chrome.Heading("Degree plans"));
        root.Children.Add(bar);
        root.Children.Add(_body);
        Content = new ScrollViewer { Content = root };
        _plans.SelectionChanged += (_, _) => ShowPlan();
        Reload();
    }

    public void ExportPdf(bool open = false)
    {
        if (_plan is null) return;
        var includeStudent = _plan.StudentName is not null || _plan.StudentID is not null;
        if (open)
        {
            var path = DegreePlanPdf.TemporaryFile(_plan, includeStudent);
            _ = LaunchAsync(path);
            return;
        }
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "degree-plan.pdf");
        File.WriteAllBytes(desktop, DegreePlanPdf.Render(_plan, includeStudent: includeStudent));
        _window.SetStatus("Wrote " + desktop);
    }

    private static async Task LaunchAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        await Launcher.LaunchFileAsync(file);
    }

    private void Reload()
    {
        _plans.Items.Clear();
        foreach (var plan in _session.Store.SortedDegreePlans)
            _plans.Items.Add(new PlanChoice(plan));
        if (_plans.Items.Count == 0)
        {
            _body.Children.Clear();
            _body.Children.Add(Chrome.Body("Import a degree audit PDF with Ctrl+O, or create a plan from the File menu."));
            return;
        }
        _plans.SelectedIndex = 0;
    }

    private void ShowPlan()
    {
        _plan = (_plans.SelectedItem as PlanChoice)?.Plan;
        _body.Children.Clear();
        if (_plan is null) return;
        _body.Children.Add(Chrome.Body($"{_plan.Name} · {_plan.CompletedCredits:0.#} completed · {_plan.PlannedCredits:0.#} planned"));
        if (_plan.Program is not null) _body.Children.Add(Chrome.Body(_plan.Program));
        var duplicates = DegreePlanAnalysis.DuplicateKeys(_plan);
        if (duplicates.Count > 0)
            _body.Children.Add(Chrome.Body("Listed more than once: " + string.Join(", ", duplicates)));
        for (var i = 0; i < _plan.Terms.Count; i++)
        {
            var term = _plan.Terms[i];
            var block = new StackPanel { Spacing = 2 };
            block.Children.Add(new TextBlock { Text = $"{term.Name} · {term.Credits:0.#} credits", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var course in term.Courses)
            {
                var line = course.Key?.ToString() ?? course.Label;
                if (course.Key is CourseKey key)
                {
                    var status = DegreePlanAnalysis.ForCourse(key, i, _plan, _session.Index);
                    if (status.IsMissing) line += " — needs " + status.MissingText;
                }
                block.Children.Add(Chrome.Body($"{line}  {course.Title}  ({course.Credits:0.#})"));
            }
            _body.Children.Add(Chrome.Card(block));
        }
        var add = new TextBox { PlaceholderText = "Add a course, such as CS 3000" };
        var addButton = new Button { Content = "Add to next term" };
        addButton.Click += (_, _) => AddCourse(add.Text);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(add);
        row.Children.Add(addButton);
        _body.Children.Add(row);
    }

    private void AddCourse(string text)
    {
        if (_plan is null || CourseKey.Parse(text) is not CourseKey key) return;
        var title = _session.Index?.Course(key)?.Title ?? "";
        var credits = _session.Index?.Course(key)?.MaxCredits ?? 4;
        _session.Store.UpdateDegreePlan(_plan.Id, plan =>
        {
            var term = plan.Terms.LastOrDefault() ?? NewTerm(plan);
            if (!plan.Terms.Contains(term)) plan.Terms.Add(term);
            term.Courses.Add(new DegreePlanCourse(key, title, credits));
        });
        Reload();
    }

    private static DegreePlanTerm NewTerm(DegreePlan plan)
    {
        var year = plan.Terms.LastOrDefault()?.Year ?? DateTime.Now.Year;
        return new DegreePlanTerm { Season = PlanSeason.Fall, Year = year };
    }

    private void Autofill()
    {
        if (_plan is null || _session.Term is null || _session.Index is null) return;
        var match = _plan.TermMatching(_session.Term);
        if (match is null)
        {
            _window.SetStatus($"This plan has no {_session.Term.Name} semester to fill.");
            return;
        }
        var schedule = _session.Store.PrimaryPlan(_session.Term.Code) ?? _session.Store.CreatePlan(_plan.Name, _session.Term.Code);
        var added = 0;
        foreach (var course in match.Courses)
        {
            if (course.Key is not CourseKey key) continue;
            if (_session.Store.AddCourse(key, schedule.Id)) added++;
        }
        _window.SetStatus(added == 0 ? "Those courses are already on the schedule." : $"Added {added} courses to {schedule.Name}.");
    }

    private sealed class PlanChoice(DegreePlan plan)
    {
        public DegreePlan Plan { get; } = plan;
        public override string ToString() => plan.IsPrimary ? plan.Name + " · Primary" : plan.Name;
    }
}
