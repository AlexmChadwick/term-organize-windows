// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermOrganize.Core;

namespace TermOrganize;

/// <summary>The "Your ideal week" survey. Nothing is stored until Done.</summary>
internal sealed class PreferencesSurvey : StackPanel
{
    public const string Placeholder = "No classes before 10, I have robotics club Tuesdays and Thursdays 6\u20138pm, I\u2019d like Fridays off and a lunch break";

    private SchedulePreferences _draft;
    private SchedulePreferences? _beforeFill;
    private readonly IReadOnlyList<string> _campuses;
    private readonly Dictionary<TimeBand, ComboBox> _bands = [];
    private readonly Dictionary<int, CheckBox> _daysOff = [];
    private ComboBox? _daysOffStrength;
    private ComboBox? _layout;
    private CheckBox? _earlyOn;
    private ComboBox? _earlyTime;
    private ComboBox? _earlyStrength;
    private CheckBox? _lateOn;
    private ComboBox? _lateTime;
    private ComboBox? _lateStrength;
    private StackPanel? _blocks;
    private ComboBox? _gaps;
    private ComboBox? _breaks;
    private NumberBox? _breakMinutes;
    private CheckBox? _lunch;
    private NumberBox? _maxClasses;
    private NumberBox? _maxMinutes;
    private ComboBox? _format;
    private ComboBox? _campus;
    private ComboBox? _seats;
    private TextBox? _preferredInstructors;
    private TextBox? _avoidedInstructors;
    private NumberBox? _creditsLow;
    private NumberBox? _creditsHigh;
    private TextBox? _description;
    private TextBlock? _fillStatus;

    public PreferencesSurvey(SchedulePreferences start, IReadOnlyList<string> campuses)
    {
        _draft = start.Clone();
        _campuses = campuses;
        Spacing = 16;
        Build();
    }

    public SchedulePreferences Read()
    {
        Capture();
        return _draft.Normalized();
    }

    private void Build()
    {
        Children.Clear();
        _bands.Clear();
        _daysOff.Clear();
        Children.Add(Chrome.Body("Generated schedules follow what you tell us."));
        Children.Add(Describe());
        Children.Add(Section("Quick start", Presets()));
        Children.Add(Section("Time of day", TimeOfDaySection()));
        Children.Add(Section("Start and finish", Limits()));
        Children.Add(Section("Days", Days()));
        Children.Add(Section("Blocked times", Blocks()));
        Children.Add(Section("More", More()));
        var reset = new Button { Content = "Reset ideal week", HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) =>
        {
            _draft = new SchedulePreferences();
            Build();
        };
        Children.Add(reset);
        Children.Add(Chrome.Body("Saved with this schedule."));
    }

    private StackPanel Describe()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Heading("Describe your ideal schedule"));
        _description = new TextBox
        {
            PlaceholderText = Placeholder,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
        };
        var fill = new Button { Content = "Fill in the survey", HorizontalAlignment = HorizontalAlignment.Left };
        _fillStatus = Chrome.Body("");
        fill.Click += (_, _) =>
        {
            var parsed = PreferenceParser.Interpret(_description.Text, _draft);
            if (parsed.Message is not null)
            {
                _fillStatus.Text = parsed.Message;
                return;
            }
            _beforeFill = _draft.Clone();
            _draft = parsed.Preferences;
            var found = parsed.ChangedCount;
            _fillStatus.Text = found == 1 ? "Found 1 setting. Review the rows below. Nothing is saved until you click Done." : $"Found {found} settings. Review the rows below. Nothing is saved until you click Done.";
            var status = _fillStatus.Text;
            Build();
            if (_fillStatus is not null) _fillStatus.Text = status;
        };
        var undo = new Button { Content = "Undo", HorizontalAlignment = HorizontalAlignment.Left };
        undo.Click += (_, _) =>
        {
            if (_beforeFill is null) return;
            _draft = _beforeFill;
            _beforeFill = null;
            Build();
        };
        panel.Children.Add(_description);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(fill);
        row.Children.Add(undo);
        panel.Children.Add(row);
        panel.Children.Add(_fillStatus);
        return panel;
    }

    private StackPanel Presets()
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var preset in Enum.GetValues<SchedulePreset>())
        {
            var captured = preset;
            var toggle = new ToggleButton
            {
                Content = preset.Title() + " \u2014 " + preset.Detail(),
                IsChecked = preset.IsApplied(_draft),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            toggle.Click += (_, _) =>
            {
                Capture();
                _draft = captured.Toggled(_draft);
                Build();
            };
            panel.Children.Add(toggle);
        }
        return panel;
    }

    private StackPanel TimeOfDaySection()
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Chrome.Body("\u201CPrefer\u201D puts schedules with classes then first. \u201CAvoid\u201D puts them last. Neither removes a schedule."));
        foreach (var band in Enum.GetValues<TimeBand>())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = band.Title() + " \u00b7 " + band.Detail(), VerticalAlignment = VerticalAlignment.Center, Width = 220 });
            var box = EnumBox(_draft[band], value => value.Title());
            _bands[band] = box;
            row.Children.Add(box);
            panel.Children.Add(row);
        }
        return panel;
    }

    private StackPanel Limits()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Chrome.Body("\u201CAlways\u201D leaves out a class outside these times. \u201CIf possible\u201D only ranks it lower."));
        (_earlyOn, _earlyTime, _earlyStrength) = LimitRow(panel, "Nothing before", _draft.EarliestStart, 7 * 60, 12 * 60);
        (_lateOn, _lateTime, _lateStrength) = LimitRow(panel, "Done by", _draft.LatestEnd, 12 * 60, 22 * 60);
        return panel;
    }

    private (CheckBox On, ComboBox Time, ComboBox Strength) LimitRow(StackPanel panel, string label, TimeLimit? limit, int from, int to)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var on = new CheckBox { Content = label, IsChecked = limit is not null, VerticalAlignment = VerticalAlignment.Center };
        var time = TimeBox(limit?.Minutes ?? from, from, to);
        var strength = EnumBox(limit?.Strength ?? PreferenceStrength.Preferred, value => value.Title());
        row.Children.Add(on);
        row.Children.Add(time);
        row.Children.Add(strength);
        panel.Children.Add(row);
        return (on, time, strength);
    }

    private StackPanel Days()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Chrome.Body("\u201CAlways\u201D leaves out a class on a day off. \u201CIf possible\u201D puts schedules with those days free first."));
        var names = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        for (var day = 0; day < 7; day++)
        {
            var box = new CheckBox { Content = names[day][..3], IsChecked = _draft.DaysOff.Contains(day) };
            _daysOff[day] = box;
            row.Children.Add(box);
        }
        panel.Children.Add(row);
        _daysOffStrength = EnumBox(_draft.DaysOffStrength, value => value.Title());
        _daysOffStrength.Header = "Days off";
        _layout = EnumBox(_draft.DayLayout, value => value.Title());
        _layout.Header = "Day layout";
        panel.Children.Add(_daysOffStrength);
        panel.Children.Add(_layout);
        return panel;
    }

    private StackPanel Blocks()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Chrome.Body("Classes won\u2019t be scheduled over these. Think club meetings, work, a commute, or practice."));
        _blocks = new StackPanel { Spacing = 6 };
        foreach (var block in _draft.BlockedTimes)
            _blocks.Children.Add(BlockRow(block));
        panel.Children.Add(_blocks);
        var add = new Button { Content = "Add blocked time", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
        {
            Capture();
            _draft.BlockedTimes.Add(new BlockedTime { Label = "Blocked time", Days = DaySet.Tuesday | DaySet.Thursday, Start = 18 * 60, End = 20 * 60 });
            Build();
        };
        panel.Children.Add(add);
        return panel;
    }

    private StackPanel BlockRow(BlockedTime block)
    {
        var panel = new StackPanel { Spacing = 4 };
        var label = new TextBox { Header = "Label", Text = block.Label, Tag = block };
        var days = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var names = new[] { "U", "M", "T", "W", "R", "F", "S" };
        for (var day = 0; day < 7; day++)
            days.Children.Add(new CheckBox { Content = names[day], IsChecked = block.Days.Contains(day), Tag = day });
        var start = TimeBox(block.Start, 6 * 60, 22 * 60);
        var end = TimeBox(block.End, 7 * 60, 23 * 60);
        var strength = EnumBox(block.Strength, value => value.Title());
        var remove = new Button { Content = "Delete", HorizontalAlignment = HorizontalAlignment.Left };
        remove.Click += (_, _) =>
        {
            Capture();
            _draft.BlockedTimes.RemoveAll(item => item.Id == block.Id);
            Build();
        };
        panel.Children.Add(label);
        panel.Children.Add(days);
        var times = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        times.Children.Add(start);
        times.Children.Add(end);
        times.Children.Add(strength);
        panel.Children.Add(times);
        panel.Children.Add(remove);
        panel.Tag = (block.Id, label, days, start, end, strength);
        return panel;
    }

    private StackPanel More()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Sub("Breaks"));
        _breaks = EnumBox(_draft.Breaks, value => value.Title());
        _breaks.Header = "Back to back";
        _breakMinutes = new NumberBox { Header = "Minutes between buildings", Value = _draft.MinimumBreakMinutes, Minimum = 5, Maximum = 90, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        panel.Children.Add(_breaks);
        panel.Children.Add(_breakMinutes);
        panel.Children.Add(Sub("Gaps"));
        _gaps = EnumBox(_draft.Gaps, value => value.Title());
        panel.Children.Add(_gaps);
        _lunch = new CheckBox { Content = "Keep a lunch break", IsChecked = _draft.Lunch is not null };
        panel.Children.Add(_lunch);
        panel.Children.Add(Sub("Workload"));
        _maxClasses = new NumberBox { Header = "At most this many classes a day", Value = _draft.MaxClassesPerDay ?? double.NaN, Minimum = 1, Maximum = 8, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        _maxMinutes = new NumberBox { Header = "At most this many minutes of class a day", Value = _draft.MaxMinutesPerDay ?? double.NaN, Minimum = 60, Maximum = 12 * 60, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        panel.Children.Add(_maxClasses);
        panel.Children.Add(_maxMinutes);
        panel.Children.Add(Sub("Format and campus"));
        _format = EnumBox(_draft.Format, value => value.Title());
        _format.Header = "Format";
        _campus = new ComboBox { Header = "Campus", HorizontalAlignment = HorizontalAlignment.Stretch };
        _campus.Items.Add(new TextChoice(null, "No preference"));
        foreach (var campus in _campuses) _campus.Items.Add(new TextChoice(campus, campus));
        _campus.SelectedIndex = 0;
        for (var i = 0; i < _campus.Items.Count; i++)
            if (_campus.Items[i] is TextChoice choice && choice.Value == _draft.Campus) _campus.SelectedIndex = i;
        panel.Children.Add(_format);
        panel.Children.Add(_campus);
        panel.Children.Add(Sub("Seats"));
        _seats = EnumBox(_draft.Seats, value => value.Title());
        panel.Children.Add(_seats);
        _preferredInstructors = new TextBox { Header = "Prefer instructors", Text = string.Join(", ", _draft.PreferredInstructors), PlaceholderText = "Last names, separated by commas" };
        _avoidedInstructors = new TextBox { Header = "Avoid instructors", Text = string.Join(", ", _draft.AvoidedInstructors), PlaceholderText = "Last names, separated by commas" };
        panel.Children.Add(_preferredInstructors);
        panel.Children.Add(_avoidedInstructors);
        panel.Children.Add(Sub("Credits"));
        _creditsLow = new NumberBox { Header = "At least", Value = _draft.CreditTarget?.Lower ?? double.NaN, Minimum = 1, Maximum = 30 };
        _creditsHigh = new NumberBox { Header = "At most", Value = _draft.CreditTarget?.Upper ?? double.NaN, Minimum = 1, Maximum = 30 };
        var credits = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        credits.Children.Add(_creditsLow);
        credits.Children.Add(_creditsHigh);
        panel.Children.Add(credits);
        return panel;
    }

    private void Capture()
    {
        var next = _draft.Clone();
        next.TimeOfDay.Clear();
        foreach (var (band, box) in _bands)
            if (box.SelectedItem is EnumChoice<TimePreference> choice) next[band] = choice.Value;
        var off = DaySet.None;
        foreach (var (day, box) in _daysOff)
            if (box.IsChecked == true) off = off.Union(DaySet.Day(day));
        next.DaysOff = off;
        if (_daysOffStrength?.SelectedItem is EnumChoice<PreferenceStrength> dayStrength) next.DaysOffStrength = dayStrength.Value;
        if (_layout?.SelectedItem is EnumChoice<DayLayout> layout) next.DayLayout = layout.Value;
        next.EarliestStart = ReadLimit(_earlyOn, _earlyTime, _earlyStrength);
        next.LatestEnd = ReadLimit(_lateOn, _lateTime, _lateStrength);
        if (_blocks is not null)
        {
            var blocks = new List<BlockedTime>();
            foreach (StackPanel row in _blocks.Children)
            {
                if (row.Tag is not (Guid id, TextBox label, StackPanel days, ComboBox start, ComboBox end, ComboBox strength)) continue;
                var mask = DaySet.None;
                foreach (CheckBox day in days.Children)
                    if (day.IsChecked == true && day.Tag is int index) mask = mask.Union(DaySet.Day(index));
                blocks.Add(new BlockedTime
                {
                    Id = id,
                    Label = label.Text,
                    Days = mask,
                    Start = (start.SelectedItem as TimeChoice)?.Minutes ?? 18 * 60,
                    End = (end.SelectedItem as TimeChoice)?.Minutes ?? 20 * 60,
                    Strength = strength.SelectedItem is EnumChoice<PreferenceStrength> blockStrength ? blockStrength.Value : PreferenceStrength.Required,
                });
            }
            next.BlockedTimes = blocks;
        }
        if (_gaps?.SelectedItem is EnumChoice<GapPreference> gaps) next.Gaps = gaps.Value;
        if (_breaks?.SelectedItem is EnumChoice<BreakPreference> breaks) next.Breaks = breaks.Value;
        if (_breakMinutes is not null && !double.IsNaN(_breakMinutes.Value)) next.MinimumBreakMinutes = (int)_breakMinutes.Value;
        next.Lunch = _lunch?.IsChecked == true ? _draft.Lunch ?? new LunchBreak() : null;
        next.MaxClassesPerDay = Whole(_maxClasses);
        next.MaxMinutesPerDay = Whole(_maxMinutes);
        if (_format?.SelectedItem is EnumChoice<FormatPreference> format) next.Format = format.Value;
        next.Campus = (_campus?.SelectedItem as TextChoice)?.Value;
        if (_seats?.SelectedItem is EnumChoice<SeatPolicy> seats) next.Seats = seats.Value;
        next.PreferredInstructors = SplitNames(_preferredInstructors?.Text);
        next.AvoidedInstructors = SplitNames(_avoidedInstructors?.Text);
        if (Whole(_creditsLow) is int low && Whole(_creditsHigh) is int high) next.CreditTarget = new CreditTarget(low, Math.Max(low, high));
        else next.CreditTarget = null;
        _draft = next;
    }

    private static TimeLimit? ReadLimit(CheckBox? on, ComboBox? time, ComboBox? strength)
    {
        if (on?.IsChecked != true || time?.SelectedItem is not TimeChoice choice || choice.Minutes is not int minutes) return null;
        var how = strength?.SelectedItem is EnumChoice<PreferenceStrength> value ? value.Value : PreferenceStrength.Preferred;
        return new TimeLimit(minutes, how);
    }

    private static int? Whole(NumberBox? box) => box is null || double.IsNaN(box.Value) ? null : (int)box.Value;

    private static List<string> SplitNames(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static ComboBox TimeBox(int selected, int from, int to)
    {
        var box = new ComboBox { MinWidth = 120 };
        var index = 0;
        var chosen = 0;
        for (var minutes = from; minutes <= to; minutes += 30)
        {
            box.Items.Add(new TimeChoice(minutes, TimeOfDay.Label(minutes)));
            if (Math.Abs(minutes - selected) < 15) chosen = index;
            index++;
        }
        box.SelectedIndex = chosen;
        return box;
    }

    private static ComboBox EnumBox<T>(T selected, Func<T, string> title) where T : struct, Enum
    {
        var box = new ComboBox { MinWidth = 160 };
        var index = 0;
        foreach (var value in Enum.GetValues<T>())
        {
            box.Items.Add(new EnumChoice<T>(value, title(value)));
            if (value.Equals(selected)) box.SelectedIndex = index;
            index++;
        }
        if (box.SelectedIndex < 0) box.SelectedIndex = 0;
        return box;
    }

    private static StackPanel Section(string title, UIElement body)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Heading(title));
        panel.Children.Add(body);
        return panel;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 16 };

    private static TextBlock Sub(string text) => new() { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

    private sealed record TimeChoice(int Minutes, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record TextChoice(string? Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record EnumChoice<T>(T Value, string Label) where T : struct, Enum
    {
        public override string ToString() => Label;
    }
}
