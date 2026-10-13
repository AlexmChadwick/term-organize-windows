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
    private readonly StackPanel _body = new() { Spacing = 8 };
    private SchedulePlan? _plan;

    public SchedulesPage(AppSession session, MainWindow window)
    {
        _session = session;
        _window = window;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        bar.Children.Add(_plans);
        var generate = new Button { Content = "Generate" };
        generate.Click += (_, _) => Generate();
        var copy = new Button { Content = "Copy CRNs" };
        copy.Click += (_, _) => CopyCrns();
        var ics = new Button { Content = "Export .ics" };
        ics.Click += (_, _) => ExportIcs();
        var image = new Button { Content = "Export image" };
        image.Click += (_, _) => ExportImage();
        bar.Children.Add(generate);
        bar.Children.Add(copy);
        bar.Children.Add(ics);
        bar.Children.Add(image);
        var root = new StackPanel { Spacing = 12, Padding = new Thickness(12) };
        root.Children.Add(Chrome.Heading("Schedules"));
        root.Children.Add(bar);
        root.Children.Add(_body);
        Content = new ScrollViewer { Content = root };
        _plans.SelectionChanged += (_, _) => ShowPlan();
        ReloadPlans();
    }

    public void ExportIcs()
    {
        if (_plan is null || _session.Term is null || _session.Index is null) return;
        var sections = SelectedSections().ToList();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Sanitize(_plan.Name) + ".ics");
        File.WriteAllText(path, IcsWriter.Write(_plan.Name, _session.Term, sections));
        _window.SetStatus("Wrote " + path);
    }

    private void ReloadPlans()
    {
        var term = _session.Term?.Code ?? _session.Index?.Term.Code ?? BundledCatalog.DefaultTerm;
        _plans.Items.Clear();
        foreach (var plan in _session.Store.PlansIn(term))
            _plans.Items.Add(new PlanChoice(plan));
        if (_plans.Items.Count == 0)
            _plans.Items.Add(new PlanChoice(_session.Store.CreatePlan("Schedule", term)));
        _plans.SelectedIndex = 0;
    }

    private void ShowPlan()
    {
        _plan = (_plans.SelectedItem as PlanChoice)?.Plan;
        _body.Children.Clear();
        if (_plan is null || _session.Index is null) return;
        _body.Children.Add(Chrome.Body(_plan.IsPrimary ? "Primary plan for this term." : "A saved plan."));
        foreach (var course in _plan.Courses)
        {
            var title = _session.Index.Course(course.Key)?.Title ?? "";
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = $"{course.Key}  {title}" + (course.IsRequired ? "" : "  · optional"), VerticalAlignment = VerticalAlignment.Center });
            var remove = new Button { Content = "Remove" };
            var key = course.Key;
            remove.Click += (_, _) =>
            {
                _session.Store.RemoveCourse(key, _plan.Id, _session.Index.Sections(key).Select(s => s.Crn));
                ReloadPlans();
            };
            row.Children.Add(remove);
            _body.Children.Add(row);
        }
        var selected = SelectedSections().ToList();
        if (selected.Count > 1)
        {
            for (var i = 0; i < selected.Count; i++)
                for (var j = i + 1; j < selected.Count; j++)
                    if (selected[i].ConflictsWith(selected[j]))
                        _body.Children.Add(Chrome.Body($"Conflict: {selected[i].CourseKey} CRN {selected[i].Crn} overlaps {selected[j].CourseKey} CRN {selected[j].Crn}."));
        }
        _body.Children.Add(Week(selected));
        var wish = new TextBox { PlaceholderText = "Describe the week: no classes before 10, Fridays off, choir Tuesdays 6-8pm", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 64 };
        var apply = new Button { Content = "Read that sentence", HorizontalAlignment = HorizontalAlignment.Left };
        apply.Click += (_, _) =>
        {
            var parsed = PreferenceParser.Interpret(wish.Text, _plan.Preferences ?? new SchedulePreferences());
            if (parsed.Message is not null)
            {
                _body.Children.Add(Chrome.Body(parsed.Message));
                return;
            }
            _session.Store.UpdatePlan(_plan.Id, plan => plan.Preferences = parsed.Preferences);
            _body.Children.Add(Chrome.Body(parsed.Preferences.Summary(6)));
            ReloadPlans();
        };
        _body.Children.Add(Chrome.Body("Preferences: " + (_plan.Preferences?.Summary(6) ?? "No preferences yet")));
        _body.Children.Add(wish);
        _body.Children.Add(apply);
    }

    private void Generate()
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
        _body.Children.Add(Chrome.Heading(result.TotalFound == 0 ? "No schedules" : $"{result.TotalFound} schedules"));
        foreach (var suggestion in result.Suggestions) _body.Children.Add(Chrome.Body(suggestion));
        foreach (var schedule in result.Schedules.Take(12))
        {
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = string.Join(" · ", schedule.Sections.Select(s => $"{s.CourseKey} {s.Crn}")) + $"  ·  {schedule.Score:0}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(Chrome.Body(string.Join(" · ", schedule.Reasons)));
            var use = new Button { Content = "Use these sections", HorizontalAlignment = HorizontalAlignment.Left };
            var crns = schedule.Crns.ToList();
            use.Click += (_, _) =>
            {
                _session.Store.UpdatePlan(_plan.Id, plan => plan.SelectedCRNs = crns);
                _window.RefreshUpNext();
                ReloadPlans();
            };
            panel.Children.Add(use);
            _body.Children.Add(Chrome.Card(panel));
        }
    }

    private void CopyCrns()
    {
        var crns = SelectedSections().Select(s => s.Crn).ToList();
        var package = new DataPackage();
        package.SetText(crns.Count == 0 ? "" : string.Join(", ", crns));
        Clipboard.SetContent(package);
        _window.SetStatus(crns.Count == 0 ? "Pick sections first." : "Copied " + string.Join(", ", crns));
    }

    private void ExportImage()
    {
        var sections = SelectedSections().ToList();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "term-organize-week.bmp");
        WeekImage.Write(path, sections);
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

    private static StackPanel Week(IReadOnlyList<CourseSection> sections)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = "Week", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (sections.Count == 0)
        {
            panel.Children.Add(Chrome.Body("Generate a schedule, then use a result to put sections on the calendar."));
            return panel;
        }
        foreach (var section in sections)
            panel.Children.Add(Chrome.Body($"{section.CourseKey} · {section.ScheduleSummary} · {section.Meetings.FirstOrDefault()?.LocationLabel}"));
        return panel;
    }

    private sealed class PlanChoice(SchedulePlan plan)
    {
        public SchedulePlan Plan { get; } = plan;
        public override string ToString() => plan.IsPrimary ? plan.Name + " · Primary" : plan.Name;
    }

    private static string Sanitize(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '-');
        return string.IsNullOrWhiteSpace(name) ? "schedule" : name;
    }
}

internal static class WeekImage
{
    public static void Write(string path, IReadOnlyList<CourseSection> sections)
    {
        const int width = 900;
        const int height = 520;
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = 248;
            pixels[i + 1] = 246;
            pixels[i + 2] = 242;
        }
        var day = 0;
        foreach (var section in sections)
        {
            foreach (var meeting in section.WeeklyMeetings)
            {
                if (meeting.Start is not int start || meeting.End is not int end) continue;
                foreach (var index in meeting.Days.Indices)
                {
                    var x = 40 + index * 120;
                    var y = 40 + (start - 8 * 60) * 400 / (12 * 60);
                    var h = Math.Max(8, (end - start) * 400 / (12 * 60));
                    Fill(pixels, width, height, x, y, 100, h, (byte)(40 + day * 30), (byte)(16 + day * 10), 46);
                }
            }
            day++;
        }
        WriteBmp(path, width, height, pixels);
    }

    private static void Fill(byte[] pixels, int width, int height, int x, int y, int w, int h, byte r, byte g, byte b)
    {
        for (var row = Math.Max(0, y); row < Math.Min(height, y + h); row++)
            for (var col = Math.Max(0, x); col < Math.Min(width, x + w); col++)
            {
                var i = (row * width + col) * 3;
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
            }
    }

    private static void WriteBmp(string path, int width, int height, byte[] rgb)
    {
        var stride = (width * 3 + 3) & ~3;
        var pixelBytes = stride * height;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(54 + pixelBytes);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);
        var pad = new byte[stride - width * 3];
        for (var row = height - 1; row >= 0; row--)
        {
            writer.Write(rgb, row * width * 3, width * 3);
            if (pad.Length > 0) writer.Write(pad);
        }
    }
}
