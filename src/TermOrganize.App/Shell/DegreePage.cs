// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TermOrganize.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace TermOrganize;

public sealed class DegreePage : UserControl
{
    private readonly AppSession _session;
    private readonly MainWindow _window;
    private readonly ComboBox _plans = new() { MinWidth = 240, PlaceholderText = "Choose a degree plan" };
    private readonly StackPanel _terms = new() { Spacing = 8 };
    private readonly DegreeRequirementsPane _requirements;
    private readonly Grid _host = new();
    private readonly PaneSplit _split;
    private DegreePlan? _plan;
    private OfferingHistory _history = OfferingHistory.Empty;
    private bool _filling;

    public DegreePage(AppSession session, MainWindow window)
    {
        _session = session;
        _window = window;
        _requirements = new DegreeRequirementsPane(session, window, PaintTerms);
        var bar = new WrapLayout { Spacing = 8 };
        bar.Children.Add(_plans);
        bar.Children.Add(Make("Export PDF", (_, _) => ExportPdf()));
        bar.Children.Add(Make("Print", (_, _) => Print(WinRT.Interop.WindowNative.GetWindowHandle(_window))));
        bar.Children.Add(Make("Autofill schedule", (_, _) => Autofill()));
        bar.Children.Add(Make("Check when courses are offered", (_, _) => _ = LoadHistoryAsync(force: true)));
        var left = new StackPanel { Spacing = 12 };
        left.Children.Add(Chrome.Heading("Degree plans"));
        left.Children.Add(bar);
        left.Children.Add(_terms);
        var scroll = new ScrollViewer
        {
            Content = left,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Chrome.KeepInside(scroll);
        _split = new PaneSplit(scroll, _requirements, 460) { Padding = Chrome.PagePadding };
        Content = _host;
        Chrome.ClipToBounds(this);
        _plans.SelectionChanged += (_, _) => { if (!_filling) ShowPlan(); };
        Reload();
    }

    public void ExportPdf()
    {
        if (_plan is null) return;
        var includeStudent = _plan.StudentName is not null || _plan.StudentID is not null;
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "degree-plan.pdf");
        File.WriteAllBytes(desktop, DegreePlanPdf.Render(_plan, includeStudent: includeStudent));
        _window.SetStatus("Wrote " + desktop);
    }

    public void Print(nint owner)
    {
        if (_plan is null)
        {
            _window.SetStatus("Open a degree plan, then press Ctrl+P.");
            return;
        }
        var includeStudent = _plan.StudentName is not null || _plan.StudentID is not null;
        var lines = new List<string>();
        lines.AddRange(DegreePlanFormat.HeaderLines(_plan, DateTimeOffset.Now, includeStudent));
        lines.Add("");
        foreach (var line in DegreePlanFormat.BodyLines(_plan))
            lines.Add(new string(' ', line.Indent * 2) + line.Text);
        if (NativePrint.Show(owner, _plan.Name, lines)) _window.SetStatus("Sent the degree plan to the printer.");
        else _window.SetStatus("Print was canceled.");
    }

    private void Reload()
    {
        _filling = true;
        _plans.Items.Clear();
        foreach (var plan in _session.Store.SortedDegreePlans)
            _plans.Items.Add(new PlanChoice(plan));
        _filling = false;
        if (_plans.Items.Count == 0)
        {
            _plan = null;
            _terms.Children.Clear();
            ShowEmpty();
            return;
        }
        ShowSplit();
        _plans.SelectedIndex = 0;
        ShowPlan();
    }

    private void ShowEmpty()
    {
        _host.Children.Clear();
        var icon = new FontIcon
        {
            Glyph = "\uE7BE",
            FontSize = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.85,
        };
        var title = Chrome.Heading("No degree plan yet");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.TextAlignment = TextAlignment.Center;
        var body = Chrome.Body("Import a degree audit PDF, or start a plan and add your semesters.");
        body.HorizontalAlignment = HorizontalAlignment.Center;
        body.TextAlignment = TextAlignment.Center;
        var create = new Button { Content = "New degree plan", HorizontalAlignment = HorizontalAlignment.Center };
        if (Chrome.StyleOf("AccentButtonStyle") is Style accent) create.Style = accent;
        create.Click += (_, _) =>
        {
            _session.Store.AddDegreePlan(new DegreePlan
            {
                Name = "Degree Plan",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            Reload();
        };
        var import = new Button { Content = "Import degree audit\u2026", HorizontalAlignment = HorizontalAlignment.Center };
        import.Click += (_, _) => _ = _window.ImportDegreeAsync();
        var stack = new StackPanel
        {
            Spacing = 12,
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = Chrome.PagePadding,
        };
        stack.Children.Add(icon);
        stack.Children.Add(title);
        stack.Children.Add(body);
        stack.Children.Add(create);
        stack.Children.Add(import);
        _host.Children.Add(stack);
    }

    private void ShowSplit()
    {
        if (_host.Children.Count == 1 && ReferenceEquals(_host.Children[0], _split)) return;
        _host.Children.Clear();
        _host.Children.Add(_split);
    }

    private void ShowPlan()
    {
        _plan = (_plans.SelectedItem as PlanChoice)?.Plan;
        _terms.Children.Clear();
        if (_plan is null) return;
        var cached = OfferingHistoryBuilder.TryLoad(HistoryPath, _plan.AllCourseKeys());
        if (cached is not null) _history = cached;
        PaintTerms();
        _requirements.Show(_plan, _history);
    }

    private void PaintTerms()
    {
        if (_plan is null) return;
        _terms.Children.Clear();
        _terms.Children.Add(Chrome.Body($"{_plan.Name} \u00b7 {_plan.CompletedCredits:0.#} completed \u00b7 {_plan.PlannedCredits:0.#} planned"));
        if (_plan.CatalogProgramTitle is not null) _terms.Children.Add(Chrome.Body(_plan.CatalogProgramTitle));
        var duplicates = DegreePlanAnalysis.DuplicateKeys(_plan);
        if (duplicates.Count > 0)
            _terms.Children.Add(Chrome.Body("Listed more than once: " + string.Join(", ", duplicates)));
        if (_plan.Completed.Count > 0)
        {
            var done = new StackPanel { Spacing = 2 };
            done.Children.Add(new TextBlock { Text = "Completed", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            foreach (var course in _plan.Completed)
                done.Children.Add(CourseLine(course, completed: true));
            _terms.Children.Add(Chrome.Card(done));
        }
        _terms.Children.Add(Chrome.Body("Drag a course onto a semester, or onto another course, to reorder it."));
        for (var termIndex = 0; termIndex < _plan.Terms.Count; termIndex++)
            _terms.Children.Add(TermBlock(_plan.Terms[termIndex], termIndex));
        _terms.Children.Add(AddCourseRow());
        _terms.Children.Add(AddTermRow());
    }

    private StackPanel TermBlock(DegreePlanTerm term, int termIndex)
    {
        var block = new StackPanel { Spacing = 4 };
        block.Children.Add(new TextBlock
        {
            Text = $"{term.Name} \u00b7 {term.Credits:0.#} credits",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        var blocking = new List<CourseKey>();
        for (var position = 0; position < term.Courses.Count; position++)
        {
            var course = term.Courses[position];
            block.Children.Add(CourseLine(course, completed: false, term, position));
            if (course.Key is not CourseKey key) continue;
            var offered = _history.IsLikelyOffered(key, term.Season);
            if (_history.IsChecked(key)) block.Children.Add(Chrome.Body(_history.Summary(key)));
            if (offered == false) blocking.Add(key);
            if (_session.Index is not null && course.Key is CourseKey planned)
            {
                var status = DegreePlanAnalysis.ForCourse(planned, termIndex, _plan!, _session.Index);
                if (status.MissingText is string missing) block.Children.Add(Chrome.Body("Needs " + missing));
            }
        }
        if (blocking.Count > 0 && _plan is not null)
        {
            var suggestions = DegreePlanReordering.Suggestions(
                _plan,
                term.Id,
                blocking,
                _history,
                Prerequisite,
                _plan.DeclaredEquivalences);
            foreach (var suggestion in suggestions)
            {
                var row = new StackPanel { Spacing = 2 };
                row.Children.Add(Chrome.Body(suggestion.Title));
                foreach (var reason in suggestion.Reasons) row.Children.Add(Chrome.Body(reason));
                var apply = new Button { Content = "Apply this move", HorizontalAlignment = HorizontalAlignment.Left };
                var captured = suggestion;
                apply.Click += (_, _) =>
                {
                    _session.Store.UpdateDegreePlan(_plan.Id, plan => DegreePlanReordering.Apply(captured, plan));
                    ShowPlan();
                };
                row.Children.Add(apply);
                block.Children.Add(row);
            }
        }
        var drop = new Border
        {
            AllowDrop = true,
            MinHeight = 32,
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Child = Chrome.Body(term.Courses.Count == 0 ? "Drop a course here." : "Drop here to put it last."),
        };
        var endId = term.Id;
        var end = term.Courses.Count;
        drop.DragOver += AcceptDegreeDrag;
        drop.Drop += (_, args) => DropDegreeCourse(args, endId, end);
        block.Children.Add(drop);
        var remove = new Button { Content = "Remove semester", HorizontalAlignment = HorizontalAlignment.Left };
        var termId = term.Id;
        remove.Click += (_, _) =>
        {
            if (_plan is null) return;
            _session.Store.UpdateDegreePlan(_plan.Id, plan => DegreePlanEditing.RemoveTerm(termId, plan));
            ShowPlan();
        };
        block.Children.Add(remove);
        return block;
    }

    private StackPanel CourseLine(DegreePlanCourse course, bool completed, DegreePlanTerm? term = null, int position = 0)
    {
        var row = new StackPanel
        {
            Spacing = 4,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
        };
        if (!completed && term is not null)
        {
            row.CanDrag = true;
            var courseId = course.Id;
            var termId = term.Id;
            var index = position;
            row.DragStarting += (_, args) =>
            {
                args.Data.SetText("termorganize-course:" + courseId);
                args.AllowedOperations = DataPackageOperation.Move;
            };
            row.AllowDrop = true;
            row.DragOver += AcceptDegreeDrag;
            row.Drop += (_, args) =>
            {
                args.Handled = true;
                DropDegreeCourse(args, termId, index);
            };
        }
        var line = course.Key?.ToString() ?? course.Label;
        var pin = DegreeCourseRepeats.CleanCrn(course.SelectedCrn);
        row.Children.Add(Chrome.Body(pin is null
            ? $"{line}  {course.Title}  ({course.Credits:0.#})"
            : $"{line}  {course.Title}  ({course.Credits:0.#})  CRN {pin}"));
        var buttons = new WrapLayout { Spacing = 8 };
        var id = course.Id;
        if (!completed)
        {
            var mark = new Button { Content = "Mark completed" };
            mark.Click += (_, _) =>
            {
                if (_plan is null) return;
                _session.Store.UpdateDegreePlan(_plan.Id, plan => DegreePlanEditing.MarkCompleted(id, plan));
                ShowPlan();
            };
            buttons.Children.Add(mark);
        }
        var remove = new Button { Content = "Remove" };
        remove.Click += (_, _) =>
        {
            if (_plan is null) return;
            _session.Store.UpdateDegreePlan(_plan.Id, plan =>
            {
                if (completed) DegreePlanEditing.RemoveCompletedCourse(id, plan);
                else DegreePlanEditing.RemovePlannedCourse(id, plan);
            });
            ShowPlan();
        };
        buttons.Children.Add(remove);
        row.Children.Add(buttons);
        if (course.Key is CourseKey key)
        {
            var counts = new TextBox { PlaceholderText = "Counts as, for example CS 2500", Width = 220 };
            var save = new Button { Content = "Save renumbering" };
            save.Click += (_, _) =>
            {
                if (_plan is null || CourseKey.Parse(counts.Text) is not CourseKey countsAs) return;
                DegreeEquivalenceEditing.Declare(_session.Store, _plan.Id, key, countsAs, "Renumbered");
                ShowPlan();
            };
            var pair = new WrapLayout { Spacing = 8 };
            pair.Children.Add(counts);
            pair.Children.Add(save);
            row.Children.Add(pair);
        }
        return row;
    }

    private WrapLayout AddCourseRow()
    {
        var add = new TextBox { PlaceholderText = "Add a course, such as CS 3000", MinWidth = 160 };
        var termBox = new ComboBox { MinWidth = 140 };
        if (_plan is not null)
        {
            foreach (var term in _plan.Terms) termBox.Items.Add(new TermChoice(term));
            if (termBox.Items.Count > 0) termBox.SelectedIndex = termBox.Items.Count - 1;
        }
        var button = new Button { Content = "Add" };
        button.Click += (_, _) =>
        {
            if (_plan is null || CourseKey.Parse(add.Text) is not CourseKey key || termBox.SelectedItem is not TermChoice choice) return;
            var title = _session.Index?.Course(key)?.Title ?? "";
            var credits = _session.Index?.Course(key)?.MaxCredits ?? 4;
            var added = false;
            _session.Store.UpdateDegreePlan(_plan.Id, plan =>
                added = DegreePlanEditing.Add(new DegreePlanCourse(key, title, credits), choice.Term.Id, plan));
            if (!added) _window.SetStatus($"{key} was not added. That semester is not on this plan.");
            ShowPlan();
        };
        var row = new WrapLayout { Spacing = 8 };
        row.Children.Add(add);
        row.Children.Add(termBox);
        row.Children.Add(button);
        return row;
    }

    private WrapLayout AddTermRow()
    {
        var season = new ComboBox();
        foreach (var value in Enum.GetValues<PlanSeason>()) season.Items.Add(new SeasonChoice(value));
        season.SelectedIndex = 0;
        var year = new NumberBox { Value = DateTime.Now.Year, Minimum = 1990, Maximum = 2045, Width = 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var button = new Button { Content = "Add semester" };
        button.Click += (_, _) =>
        {
            if (_plan is null || season.SelectedItem is not SeasonChoice choice || double.IsNaN(year.Value)) return;
            _session.Store.UpdateDegreePlan(_plan.Id, plan =>
                DegreePlanEditing.AddTerm(new DegreePlanTerm { Season = choice.Season, Year = (int)year.Value }, plan));
            ShowPlan();
        };
        var row = new WrapLayout { Spacing = 8 };
        row.Children.Add(season);
        row.Children.Add(year);
        row.Children.Add(button);
        return row;
    }

    private async Task LoadHistoryAsync(bool force)
    {
        if (_plan is null) return;
        var keys = _plan.AllCourseKeys();
        if (!force && OfferingHistoryBuilder.TryLoad(HistoryPath, keys) is OfferingHistory cached)
        {
            _history = cached;
            PaintTerms();
            _requirements.Show(_plan, _history);
            return;
        }
        _window.SetStatus("Checking which terms offered these courses\u2026");
        try
        {
            var terms = await RecentTermsAsync();
            _history = await OfferingHistoryBuilder.FetchAsync(terms, keys, Offered);
            OfferingHistoryBuilder.Save(HistoryPath, _history);
            _window.SetStatus("Offering history is up to date.");
        }
        catch (Exception ex)
        {
            _window.SetStatus("Couldn\u2019t check offering history. " + ex.Message);
        }
        PaintTerms();
        if (_plan is not null) _requirements.Show(_plan, _history);
    }

    private static async Task<IReadOnlySet<CourseKey>> Offered(string code, IReadOnlyList<CourseKey> wanted, CancellationToken token) =>
        await BannerClient.Shared.FetchOfferedCoursesAsync(code, wanted, token);

    private async Task<List<Term>> RecentTermsAsync()
    {
        var known = _session.KnownTerms.Where(term => term.College == TermCollege.Neu).ToList();
        if (known.Count == 0)
        {
            try { known = (await BannerTermList.FetchAsync()).Where(term => term.College == TermCollege.Neu).ToList(); }
            catch (Exception) { known = _session.Term is Term current ? [current] : []; }
        }
        if (known.Count == 0) return [];
        var newest = known.MaxBy(term => term.Code)!;
        var floor = TermCalendar.MonthIndex(newest.Code) - 36;
        return known.Where(term => TermCalendar.MonthIndex(term.Code) >= floor).OrderByDescending(term => term.Code, StringComparer.Ordinal).Take(16).ToList();
    }

    private RequisiteAnswer Prerequisite(CourseKey key)
    {
        var course = _session.Course(key);
        if (course is null) return RequisiteAnswer.Unknown;
        return course.Prereqs is Requisite requisite ? RequisiteAnswer.Known(requisite) : RequisiteAnswer.None;
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
        var pins = 0;
        _session.Store.UpdatePlan(schedule.Id, plan => (added, pins) = DegreePlanSchedule.SyncRows(plan, match.Courses));
        _window.SetStatus(added == 0 && pins == 0
            ? "Those courses are already on the schedule."
            : pins > 0 && added == 0
                ? $"Added the selected sections to {schedule.Name}."
                : $"Added {added} courses to {schedule.Name}.");
    }

    private static void AcceptDegreeDrag(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = args.DataView.Contains(StandardDataFormats.Text)
            ? DataPackageOperation.Move
            : DataPackageOperation.None;
        args.Handled = true;
    }

    private async void DropDegreeCourse(DragEventArgs args, Guid termId, int position)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (_plan is null || !args.DataView.Contains(StandardDataFormats.Text)) return;
            var text = await args.DataView.GetTextAsync();
            const string prefix = "termorganize-course:";
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) return;
            if (!Guid.TryParse(text[prefix.Length..], out var courseId)) return;
            _session.Store.UpdateDegreePlan(_plan.Id, plan => DegreePlanEditing.Move(courseId, termId, position, plan));
            ShowPlan();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static string HistoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TermOrganize",
        "offering-history.json");

    private static Button Make(string label, RoutedEventHandler click)
    {
        var button = new Button { Content = label };
        button.Click += click;
        return button;
    }

    private sealed class PlanChoice(DegreePlan plan)
    {
        public DegreePlan Plan { get; } = plan;
        public override string ToString() => Plan.IsPrimary ? Plan.Name + " \u00b7 Primary" : Plan.Name;
    }

    private sealed record TermChoice(DegreePlanTerm Term)
    {
        public override string ToString() => Term.Name;
    }

    private sealed record SeasonChoice(PlanSeason Season)
    {
        public override string ToString() => Season.ExportName();
    }
}
