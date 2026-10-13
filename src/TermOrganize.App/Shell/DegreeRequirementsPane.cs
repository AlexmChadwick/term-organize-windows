// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermOrganize.Core;

namespace TermOrganize;

/// <summary>
/// Catalog year, program, and concentration, then the requirement tree with
/// substitutions, petitions, and waivers.
/// </summary>
internal sealed class DegreeRequirementsPane : UserControl
{
    private readonly AppSession _session;
    private readonly MainWindow _window;
    private readonly Action? _onEdited;
    private readonly StackPanel _body = new() { Spacing = 10 };
    private DegreePlan? _plan;
    private ProgramRequirements? _requirements;
    private DegreeCourseAttributes _attributes = DegreeCourseAttributes.None;
    private OfferingHistory _history = OfferingHistory.Empty;
    private CatalogEditions? _editions;
    private int _generation;
    private bool _filling;
    private string? _selectedRule;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private DegreeRequirementsFilter _filter = DegreeRequirementsFilter.All;

    public DegreeRequirementsPane(AppSession session, MainWindow window, Action? onEdited = null)
    {
        _session = session;
        _window = window;
        _onEdited = onEdited;
        Content = new ScrollViewer { Content = _body, Padding = new Thickness(16, 0, 0, 16) };
    }

    public void Show(DegreePlan plan, OfferingHistory history)
    {
        _plan = plan;
        _history = history;
        var generation = ++_generation;
        _ = LoadAsync(plan, generation);
    }

    private async Task LoadAsync(DegreePlan plan, int generation)
    {
        Paint(Chrome.Body("Loading catalog editions\u2026"), new ProgressRing { IsActive = true, Width = 24, Height = 24 });
        CatalogEditions editions;
        try
        {
            editions = await ProgramCatalogClient.EditionsAsync();
        }
        catch (Exception ex)
        {
            if (generation == _generation) ShowFailure(ex.Message);
            return;
        }
        if (generation != _generation || _plan is null) return;
        _editions = editions;
        if (plan.CatalogYear is int start && !string.IsNullOrEmpty(plan.CatalogProgramPath))
        {
            try
            {
                var year = new CatalogYear(start);
                var programs = await ProgramCatalogClient.ProgramsAsync(editions, year);
                var program = programs.FirstOrDefault(item => item.Path == plan.CatalogProgramPath);
                if (program is not null)
                {
                    var requirements = await ProgramCatalogClient.RequirementsAsync(editions, year, program);
                    if (requirements is not null && generation == _generation)
                    {
                        _requirements = requirements;
                        PaintAudit();
                        _ = LoadAttributesAsync(generation);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                if (generation == _generation) ShowFailure(ex.Message);
                return;
            }
        }
        if (generation == _generation) ShowSetup(editions, null);
    }

    private async Task LoadAttributesAsync(int generation)
    {
        if (_plan is null) return;
        try
        {
            var repository = new CourseAttributeRepository();
            var keys = new List<CourseKey>();
            var seen = new HashSet<CourseKey>();
            foreach (var key in _plan.AllCourseKeys())
            {
                if (seen.Add(key)) keys.Add(key);
                foreach (var other in _plan.DeclaredEquivalences.GroupOf(key))
                    if (seen.Add(other)) keys.Add(other);
            }
            var lookups = await repository.LookupAsync(keys, _session.IndexesNewestFirst());
            if (generation != _generation || _plan is null) return;
            var pairs = new List<KeyValuePair<CourseKey, IEnumerable<NUPath>>>();
            foreach (var (key, lookup) in lookups)
            {
                if (lookup is CourseAttributeRepository.Lookup.Found found)
                    pairs.Add(new(key, NUPathInfo.FromAttributeCodes(found.Codes)));
            }
            _attributes = pairs.Count == 0 ? DegreeCourseAttributes.None : new DegreeCourseAttributes(pairs);
            PaintAudit();
        }
        catch (Exception)
        {
            // The audit still runs. NUpath stays unchecked until Banner answers.
        }
    }

    private void ShowFailure(string message)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new InfoBar { Title = "Couldn\u2019t load the requirements", Message = message, Severity = InfoBarSeverity.Error, IsOpen = true, IsClosable = false });
        var again = new Button { Content = "Try again", HorizontalAlignment = HorizontalAlignment.Left };
        again.Click += (_, _) => { if (_plan is not null) Show(_plan, _history); };
        var change = new Button { Content = "Change program or year\u2026", HorizontalAlignment = HorizontalAlignment.Left };
        change.Click += (_, _) => { if (_editions is not null) ShowSetup(_editions, message); };
        panel.Children.Add(again);
        panel.Children.Add(change);
        Paint(panel);
    }

    private void ShowSetup(CatalogEditions editions, string? failure)
    {
        var panel = new StackPanel { Spacing = 8 };
        if (failure is null)
        {
            panel.Children.Add(Chrome.Heading("Set up degree requirements"));
            panel.Children.Add(Chrome.Body("Choose the catalog year you started in and your program to check this plan against Northeastern\u2019s requirements."));
        }
        var yearBox = new ComboBox { Header = "Catalog year", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var year in editions.All) yearBox.Items.Add(new YearChoice(year));
        var suggestedYear = _plan is null ? editions.Current : ProgramCatalogStore.SuggestedYear(_plan.CompletedCredits, editions);
        Select(yearBox, item => item is YearChoice choice && choice.Year == (_plan?.CatalogYear is int saved ? new CatalogYear(saved) : suggestedYear));
        var filter = new TextBox { Header = "Filter programs", PlaceholderText = "Computer science, business, nursing\u2026" };
        var programBox = new ComboBox { Header = "Program", HorizontalAlignment = HorizontalAlignment.Stretch };
        var programs = new List<CatalogProgram>();
        async Task FillPrograms()
        {
            if (yearBox.SelectedItem is not YearChoice year) return;
            programBox.Items.Clear();
            programBox.IsEnabled = false;
            try
            {
                programs = (await ProgramCatalogClient.ProgramsAsync(editions, year.Year)).ToList();
            }
            catch (Exception ex)
            {
                panel.Children.Add(Chrome.Body(ex.Message));
                return;
            }
            ApplyProgramFilter();
            programBox.IsEnabled = true;
        }
        void ApplyProgramFilter()
        {
            var query = filter.Text.Trim();
            var shown = programs.Where(program => query.Length == 0 || program.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(80).ToList();
            if (query.Length == 0 && _plan is not null)
            {
                var suggested = ProgramCatalogStore.SuggestedPrograms(_plan.Program, programs);
                if (suggested.Count > 0) shown = suggested.Concat(shown.Where(program => suggested.All(item => item.Path != program.Path))).Take(80).ToList();
            }
            _filling = true;
            programBox.Items.Clear();
            foreach (var program in shown) programBox.Items.Add(new ProgramChoice(program));
            if (_plan?.CatalogProgramPath is string path)
                Select(programBox, item => item is ProgramChoice choice && choice.Program.Path == path);
            else if (programBox.Items.Count > 0) programBox.SelectedIndex = 0;
            _filling = false;
        }
        yearBox.SelectionChanged += async (_, _) => { if (!_filling) await FillPrograms(); };
        filter.TextChanged += (_, _) => { if (!_filling) ApplyProgramFilter(); };
        var save = new Button { Content = "Check this plan", HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += async (_, _) =>
        {
            if (_plan is null || yearBox.SelectedItem is not YearChoice year || programBox.SelectedItem is not ProgramChoice program) return;
            save.IsEnabled = false;
            ProgramRequirements? requirements;
            try
            {
                requirements = await ProgramCatalogClient.RequirementsAsync(editions, year.Year, program.Program);
            }
            catch (Exception ex)
            {
                ShowFailure(ex.Message);
                return;
            }
            if (requirements is null)
            {
                ShowFailure("That program page didn\u2019t include a requirements list.");
                return;
            }
            _session.Store.UpdateDegreePlan(_plan.Id, stored =>
            {
                if (stored.Program is null || stored.Program == stored.CatalogProgramTitle)
                    stored.Program = program.Program.TitleWithoutCampus;
                stored.CatalogYear = year.Year.StartYear;
                stored.CatalogProgramPath = program.Program.Path;
                stored.CatalogProgramTitle = program.Program.Title;
            });
            _requirements = requirements;
            PaintAudit();
            _ = LoadAttributesAsync(_generation);
        };
        panel.Children.Add(yearBox);
        panel.Children.Add(filter);
        panel.Children.Add(programBox);
        panel.Children.Add(save);
        Paint(panel);
        _ = FillPrograms();
    }

    private void PaintAudit()
    {
        if (_plan is null || _requirements is null || _editions is null) return;
        _filling = true;
        var plan = _plan;
        var requirements = _requirements;
        var audit = DegreeAudit.Run(plan, requirements, plan.DeclaredEquivalences, _attributes);
        var lookup = new DegreeRequirementsLookup(
            plan,
            requirements,
            audit,
            _session.Store.Plans,
            code => DegreeRequirementsLookup.TermName(code, _session.KnownTerms.ToList()),
            key => _session.Course(key),
            plan.DeclaredEquivalences);
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(PickerRow(requirements));
        panel.Children.Add(Chrome.Body($"{audit.SatisfiedCount} of {audit.TotalCount} met \u00b7 {audit.CompletedHours:0.#} completed hours \u00b7 {audit.PlannedHours:0.#} planned"));
        panel.Children.Add(Chrome.Body("Unofficial planning aid. Confirm with your degree audit and academic advisor."));
        if (audit.NeedsConcentration)
            panel.Children.Add(new InfoBar { Title = "Choose your concentration", Message = "Your program has concentrations. Pick yours to check its courses too.", Severity = InfoBarSeverity.Informational, IsOpen = true, IsClosable = false });
        foreach (var question in DegreePetitionQuestions.Questions(plan, requirements, plan.DeclaredEquivalences, audit))
            panel.Children.Add(QuestionCard(question));
        foreach (var renumbering in DegreeSubstitutionEditing.LikelyRenumberings(plan, requirements, Retired))
            panel.Children.Add(RenumberingCard(renumbering));
        foreach (var section in audit.Sections)
        {
            var rules = section.Rules.Where(rule => _filter.Includes(rule.Status)).ToList();
            if (rules.Count == 0 && _filter != DegreeRequirementsFilter.All) continue;
            var covered = section.Rules.Count(rule => rule.Status.IsSatisfied());
            var block = new StackPanel { Spacing = 6 };
            block.Children.Add(new TextBlock { Text = $"{section.Section.Title} \u00b7 {covered} of {section.Rules.Count}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var group in section.Groups)
            {
                if (group.Group.Title is string title) block.Children.Add(Chrome.Body(title));
                foreach (var rule in group.Rules.Where(rule => _filter.Includes(rule.Status)))
                    block.Children.Add(RuleBlock(rule, lookup, requirements));
            }
            foreach (var check in audit.NoteChecks.Where(check => check.SectionId == section.Id))
                block.Children.Add(Chrome.Body($"{check.Title}: {check.Status.DegreeLabel()} \u2014 {check.Detail}"));
            panel.Children.Add(Chrome.Card(block));
        }
        if (audit.GeneralElectives.Count > 0)
            panel.Children.Add(Chrome.Body("Extra courses: " + string.Join(", ", audit.GeneralElectives.Select(use => use.Key.ToString()))));
        panel.Children.Add(SubstitutionList(plan, requirements));
        Paint(panel);
    }

    private StackPanel PickerRow(ProgramRequirements requirements)
    {
        var row = new StackPanel { Spacing = 8 };
        var yearBox = new ComboBox { Header = "Catalog year", MinWidth = 140 };
        foreach (var year in _editions!.All) yearBox.Items.Add(new YearChoice(year));
        Select(yearBox, item => item is YearChoice choice && choice.Year.StartYear == requirements.Year.StartYear);
        var programBox = new ComboBox { Header = "Program", MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
        programBox.Items.Add(new ProgramChoice(requirements.Program));
        programBox.SelectedIndex = 0;
        var change = new Button { Content = "Change\u2026", VerticalAlignment = VerticalAlignment.Bottom };
        change.Click += (_, _) => ShowSetup(_editions, null);
        var concentration = new ComboBox { Header = "Concentration", MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Stretch };
        concentration.Items.Add(new ConcentrationChoice(null, requirements.Concentrations.Count == 0 ? "No concentration" : "Choose a concentration"));
        foreach (var section in requirements.Concentrations)
            concentration.Items.Add(new ConcentrationChoice(section.Id, section.ConcentrationName));
        Select(concentration, item => item is ConcentrationChoice choice && choice.Id == _plan!.ConcentrationID);
        if (concentration.SelectedIndex < 0) concentration.SelectedIndex = 0;
        var filter = new ComboBox { Header = "Show", MinWidth = 120 };
        foreach (var value in Enum.GetValues<DegreeRequirementsFilter>()) filter.Items.Add(new FilterChoice(value));
        Select(filter, item => item is FilterChoice choice && choice.Filter == _filter);
        yearBox.SelectionChanged += async (_, _) =>
        {
            if (_filling || _plan is null || yearBox.SelectedItem is not YearChoice year || _editions is null) return;
            var programs = await ProgramCatalogClient.ProgramsAsync(_editions, year.Year);
            var match = programs.FirstOrDefault(program => program.Path == _plan.CatalogProgramPath) ?? programs.FirstOrDefault();
            if (match is null) return;
            await ApplyProgram(year.Year, match);
        };
        concentration.SelectionChanged += (_, _) =>
        {
            if (_filling || _plan is null || concentration.SelectedItem is not ConcentrationChoice choice) return;
            _session.Store.UpdateDegreePlan(_plan.Id, plan => plan.ConcentrationID = choice.Id);
            PaintAudit();
        };
        filter.SelectionChanged += (_, _) =>
        {
            if (_filling || filter.SelectedItem is not FilterChoice choice) return;
            _filter = choice.Filter;
            PaintAudit();
        };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line.Children.Add(yearBox);
        line.Children.Add(programBox);
        line.Children.Add(change);
        row.Children.Add(line);
        row.Children.Add(concentration);
        row.Children.Add(filter);
        return row;
    }

    private async Task ApplyProgram(CatalogYear year, CatalogProgram program)
    {
        if (_plan is null || _editions is null) return;
        var requirements = await ProgramCatalogClient.RequirementsAsync(_editions, year, program);
        if (requirements is null) return;
        _session.Store.UpdateDegreePlan(_plan.Id, stored =>
        {
            stored.CatalogYear = year.StartYear;
            stored.CatalogProgramPath = program.Path;
            stored.CatalogProgramTitle = program.Title;
            if (stored.Program is null || stored.Program == stored.CatalogProgramTitle) stored.Program = program.TitleWithoutCampus;
            stored.ConcentrationID = null;
        });
        _requirements = requirements;
        PaintAudit();
        _ = LoadAttributesAsync(_generation);
    }

    private UIElement RuleBlock(DegreeAudit.RuleResult result, DegreeRequirementsLookup lookup, ProgramRequirements requirements)
    {
        var panel = new StackPanel { Spacing = 4 };
        var open = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = $"{result.Rule.DisplayLabel} \u00b7 {DegreeRequirementText.Progress(result)} \u00b7 {result.Status.DegreeLabel()}",
        };
        open.Click += (_, _) =>
        {
            _selectedRule = _selectedRule == result.Rule.Id ? null : result.Rule.Id;
            PaintAudit();
        };
        panel.Children.Add(open);
        if (_selectedRule != result.Rule.Id) return panel;
        foreach (var note in result.Rule.DegreeParagraphs())
        {
            var reason = DegreeAudit.UncheckedReason(note);
            panel.Children.Add(Chrome.Body(reason is null ? note : $"{note} \u2014 {reason}"));
        }
        var (options, ranges) = DegreeRequirementsListing.Listing(result.Rule);
        var entries = result.EffectiveKind is RequirementKind.Required && options.Count > 0
            ? DegreeRequirementsListing.Entries(options[0])
            : DegreeRequirementsListing.Entries(options, result.Rule, SubjectName);
        var expanded = _expanded.Contains(result.Rule.Id);
        var (shown, isLong, _) = DegreeRequirementsListing.Visible(entries, result, expanded, lookup, new DegreeEquivalencePrompts());
        foreach (var entry in shown)
        {
            if (entry.Category is string category) panel.Children.Add(Chrome.Body(category));
            foreach (var key in entry.Courses.Distinct())
                panel.Children.Add(CourseRow(key, result, lookup));
        }
        foreach (var range in ranges) panel.Children.Add(Chrome.Body(range.DescribedLabel()));
        if (isLong)
        {
            var more = new Button { Content = expanded ? "Show fewer" : "Show the rest", HorizontalAlignment = HorizontalAlignment.Left };
            more.Click += (_, _) =>
            {
                if (!_expanded.Add(result.Rule.Id)) _expanded.Remove(result.Rule.Id);
                PaintAudit();
            };
            panel.Children.Add(more);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Action("Count a course", () => _ = SubstitutionAsync(result, DegreeSubstitutionRequest.Kind.CountCourse)));
        actions.Children.Add(Action("Petition", () => _ = SubstitutionAsync(result, DegreeSubstitutionRequest.Kind.Replace)));
        actions.Children.Add(Action("Waive", () => _ = SubstitutionAsync(result, DegreeSubstitutionRequest.Kind.Waive)));
        panel.Children.Add(actions);
        foreach (var substitution in result.Substitutions)
            panel.Children.Add(Chrome.Body(SubstitutionLabel(substitution, requirements)));
        return panel;
    }

    private StackPanel CourseRow(CourseKey key, DegreeAudit.RuleResult result, DegreeRequirementsLookup lookup)
    {
        var info = lookup.RowInfo(key, result);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var title = lookup.TitleOf(key);
        row.Children.Add(new TextBlock { Text = title is null ? $"{key} \u00b7 {info.Status.Label}" : $"{key} {title} \u00b7 {info.Status.Label}", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        if (info.Caption is string caption) row.Children.Add(Chrome.Body(caption));
        if (info.AddKey is CourseKey add && !info.Status.IsInPlan)
        {
            var button = new Button { Content = "Add to plan" };
            button.Click += (_, _) => AddToPlan(add, lookup.TitleOf(add) ?? title ?? "", lookup.CreditsOf(add) ?? 4);
            row.Children.Add(button);
        }
        return row;
    }

    private void AddToPlan(CourseKey key, string title, double credits)
    {
        if (_plan is null) return;
        var targets = DegreeRequirementsLookup.AddTargets(_plan);
        if (targets.Count == 0) return;
        var term = targets[0];
        _session.Store.UpdateDegreePlan(_plan.Id, plan =>
        {
            var destination = plan.Terms.FirstOrDefault(item => item.Season == term.Season && item.Year == term.Year) ?? term;
            DegreePlanEditing.AddCourse(new DegreePlanCourse(key, title, credits), destination, plan);
        });
        _window.SetStatus($"Added {key} to {term.Name}.");
        PaintAudit();
        _onEdited?.Invoke();
    }

    private async Task SubstitutionAsync(DegreeAudit.RuleResult result, DegreeSubstitutionRequest.Kind kind)
    {
        if (_plan is null || _requirements is null) return;
        var request = new DegreeSubstitutionRequest(_plan.Id, result.Rule.Id, result.Rule.DisplayLabel, kind: kind);
        var note = new TextBox { Header = "Note", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 48 };
        var approved = new CalendarDatePicker { Header = "Approved on", PlaceholderText = "Optional" };
        var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
        panel.Children.Add(Chrome.Body(result.Rule.DisplayLabel));
        ComboBox? courses = null;
        ComboBox? like = null;
        CheckBox? inHours = null;
        NumberBox? amount = null;
        NumberBox? minimum = null;
        ComboBox? numbers = null;
        var specific = new HashSet<CourseKey>();
        if (kind == DegreeSubstitutionRequest.Kind.CountCourse)
        {
            courses = new ComboBox { Header = "Course on this plan", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var choice in _plan.DegreeCourseChoices()) courses.Items.Add(new CourseChoice(choice.Key, $"{choice.Key} {choice.Title}"));
            if (courses.Items.Count > 0) courses.SelectedIndex = 0;
            panel.Children.Add(courses);
        }
        else if (kind == DegreeSubstitutionRequest.Kind.Replace)
        {
            var initial = DegreeSubstitutionRequest.InitialAmount(result.Rule);
            inHours = new CheckBox { Content = "Count semester hours", IsChecked = initial.InHours };
            amount = new NumberBox { Header = "How many", Value = initial.InHours ? initial.Hours : initial.Count, Minimum = 1, Maximum = 200 };
            like = new ComboBox { Header = "Courses like", HorizontalAlignment = HorizontalAlignment.Stretch };
            like.Items.Add(new RuleChoice(null, "Specific courses only"));
            foreach (var rule in _requirements.AllRules)
                like.Items.Add(new RuleChoice(rule.Id, DegreeSubstitutionEditing.SourceName(rule, _requirements)));
            like.SelectedIndex = 0;
            panel.Children.Add(inHours);
            panel.Children.Add(amount);
            panel.Children.Add(like);
            panel.Children.Add(Chrome.Body("Or pick courses from this plan"));
            foreach (var choice in _plan.DegreeCourseChoices())
            {
                var box = new CheckBox { Content = $"{choice.Key} {choice.Title}" };
                var key = choice.Key;
                box.Checked += (_, _) => specific.Add(key);
                box.Unchecked += (_, _) => specific.Remove(key);
                panel.Children.Add(box);
            }
            numbers = new ComboBox { Header = "Which course numbers count", HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
            foreach (var choice in Enum.GetValues<CourseNumberChoice>())
                numbers.Items.Add(new NumberChoice(choice, choice.Title("that requirement")));
            numbers.SelectedIndex = 0;
            minimum = new NumberBox { Header = "Minimum course number", Value = 2000, Minimum = 1, Maximum = 9999, Visibility = Visibility.Collapsed };
            like.SelectionChanged += (_, _) =>
            {
                var id = (like.SelectedItem as RuleChoice)?.Id;
                var asks = DegreeSubstitutionEditing.NeedsCourseNumbers(id, _requirements);
                numbers.Visibility = asks ? Visibility.Visible : Visibility.Collapsed;
                minimum.Visibility = asks && numbers.SelectedItem is NumberChoice { Choice: CourseNumberChoice.AtLeast } ? Visibility.Visible : Visibility.Collapsed;
                if (asks)
                {
                    var ranges = DegreeSubstitutionEditing.LikeRanges(id, _requirements);
                    minimum.Value = CourseNumberChoiceInfo.SuggestedMinimum(ranges, null);
                }
            };
            numbers.SelectionChanged += (_, _) =>
            {
                minimum.Visibility = numbers.SelectedItem is NumberChoice { Choice: CourseNumberChoice.AtLeast } ? Visibility.Visible : Visibility.Collapsed;
            };
            panel.Children.Add(numbers);
            panel.Children.Add(minimum);
        }
        else
        {
            panel.Children.Add(Chrome.Body("The requirement goes back to the catalog\u2019s rule when you delete the waiver."));
        }
        panel.Children.Add(note);
        panel.Children.Add(approved);
        var dialog = new ContentDialog
        {
            Title = kind switch
            {
                DegreeSubstitutionRequest.Kind.CountCourse => "Count a course",
                DegreeSubstitutionRequest.Kind.Replace => "Petition",
                _ => "Waive",
            },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot ?? _window.Content.XamlRoot,
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var course = (courses?.SelectedItem as CourseChoice)?.Key;
            var likeId = (like?.SelectedItem as RuleChoice)?.Id;
            CourseNumberLimit? limit = null;
            if (numbers is { Visibility: Visibility.Visible } && numbers.SelectedItem is NumberChoice numberChoice)
                limit = numberChoice.Choice.ToLimit((int)(minimum?.Value ?? 0));
            if (!DegreeSubstitutionEditing.CanSave(kind, course, likeId, specific, numbers is { Visibility: Visibility.Visible }, limit))
                args.Cancel = true;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _plan is null) return;
        var courseKey = (courses?.SelectedItem as CourseChoice)?.Key;
        var likeRule = (like?.SelectedItem as RuleChoice)?.Id;
        CourseNumberLimit? courseNumbers = null;
        if (numbers is { Visibility: Visibility.Visible } && numbers.SelectedItem is NumberChoice picked)
            courseNumbers = picked.Choice.ToLimit((int)(minimum?.Value ?? 0));
        var change = DegreeSubstitutionEditing.Change(
            kind,
            courseKey,
            inHours?.IsChecked == true,
            (int)(amount?.Value ?? 1),
            amount?.Value ?? 4,
            likeRule,
            specific,
            courseNumbers,
            _requirements,
            result.Rule.DisplayLabel);
        if (change is null) return;
        var substitution = DegreeSubstitutionEditing.Substitution(request, change, note.Text, approved.Date);
        _session.Store.UpdateDegreePlan(_plan.Id, stored => DegreeSubstitutionEditing.Store(substitution, stored));
        PaintAudit();
    }

    private UIElement QuestionCard(DegreePetitionQuestion question)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = question.Question, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Chrome.Body(question.Explanation));
        var minimum = CourseNumberChoiceInfo.SuggestedMinimum(question.Ranges, null);
        var box = new NumberBox { Header = "A different minimum", Value = minimum, Minimum = 1, Maximum = 9999, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        foreach (var choice in Enum.GetValues<CourseNumberChoice>())
        {
            var captured = choice;
            var button = new Button { Content = choice.Title(question.LikeTitle), HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) =>
            {
                if (_plan is null) return;
                var floor = (int)box.Value;
                _session.Store.UpdateDegreePlan(_plan.Id, plan => plan.SetCourseNumbers(captured.ToLimit(floor), question.Id));
                PaintAudit();
            };
            panel.Children.Add(button);
            panel.Children.Add(Chrome.Body(choice.Detail(question.Ranges, (int)box.Value)));
        }
        panel.Children.Add(box);
        return Chrome.Card(panel);
    }

    private UIElement RenumberingCard(DegreeSubstitutionEditing.LikelyRenumbering renumbering)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = renumbering.Question, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Chrome.Body(renumbering.Explanation));
        var button = new Button { Content = $"Same course, new number ({renumbering.Course} = {renumbering.CountsAs})", HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) =>
        {
            if (_plan is null) return;
            _session.Store.UpdateDegreePlan(_plan.Id, plan => DegreeSubstitutionEditing.ConvertToEquivalence(renumbering.Substitution, _requirements, plan));
            PaintAudit();
        };
        panel.Children.Add(button);
        return Chrome.Card(panel);
    }

    private UIElement SubstitutionList(DegreePlan plan, ProgramRequirements requirements)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = "Substitutions and petitions", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (plan.Substitutions.Count == 0)
            panel.Children.Add(Chrome.Body("When an advisor approves a substitution or petition, select the requirement it changes and record it."));
        foreach (var substitution in plan.Substitutions)
        {
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(Chrome.Body(SubstitutionLabel(substitution, requirements)));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var delete = new Button { Content = "Delete" };
            var id = substitution.Id;
            delete.Click += (_, _) =>
            {
                _session.Store.UpdateDegreePlan(plan.Id, stored => stored.Substitutions.RemoveAll(item => item.Id == id));
                PaintAudit();
            };
            buttons.Children.Add(delete);
            row.Children.Add(buttons);
            panel.Children.Add(row);
        }
        return panel;
    }

    private static string SubstitutionLabel(RequirementSubstitution substitution, ProgramRequirements requirements)
    {
        var kind = substitution.Change switch
        {
            SubstitutionChange.CountCourseChange count => $"Count {count.Course}",
            SubstitutionChange.ReplaceChange replace => replace.Replacement.LikeRuleTitle is string like ? $"Petition \u00b7 courses like {like}" : "Petition",
            _ => "Waived",
        };
        var name = requirements.Title(substitution.RuleID) ?? substitution.RuleTitle;
        return substitution.Note.Length == 0 ? $"{name}: {kind}" : $"{name}: {kind} \u2014 {substitution.Note}";
    }

    private bool? Retired(CourseKey key)
    {
        if (!_history.IsChecked(key)) return null;
        var offered = Enum.GetValues<PlanSeason>().Any(season => _history.IsLikelyOffered(key, season) == true);
        return !offered;
    }

    private string? SubjectName(string code) =>
        _session.Index?.ActiveSubjects.FirstOrDefault(subject => subject.Code == code)?.Name;

    private void Paint(params UIElement[] children)
    {
        _filling = true;
        _body.Children.Clear();
        foreach (var child in children) _body.Children.Add(child);
        _filling = false;
    }

    private static void Select(ComboBox box, Func<object, bool> match)
    {
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (!match(box.Items[i])) continue;
            box.SelectedIndex = i;
            return;
        }
    }

    private static Button Action(string label, Action click)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => click();
        return button;
    }

    private sealed record YearChoice(CatalogYear Year)
    {
        public override string ToString() => Year.ToString();
    }

    private sealed record ProgramChoice(CatalogProgram Program)
    {
        public override string ToString() => Program.Title;
    }

    private sealed record ConcentrationChoice(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record FilterChoice(DegreeRequirementsFilter Filter)
    {
        public override string ToString() => Filter.Title();
    }

    private sealed record CourseChoice(CourseKey Key, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record RuleChoice(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record NumberChoice(CourseNumberChoice Choice, string Label)
    {
        public override string ToString() => Label;
    }
}
