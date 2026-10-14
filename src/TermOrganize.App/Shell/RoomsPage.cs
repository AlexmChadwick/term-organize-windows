// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// The free-rooms idea follows SearchNEU. The day bar and the inspector follow the Mac screens.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TermOrganize.Core;
using Windows.UI;

namespace TermOrganize;

public sealed class RoomsPage : UserControl
{
    private const string AllBuildings = "*";
    private readonly AppSession _session;
    private readonly ListView _buildings = new();
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly Canvas _axis = new() { Height = 30 };
    private readonly StackPanel _inspector = new() { Spacing = 12 };
    private readonly TextBox _search = new() { PlaceholderText = "Search buildings and rooms" };
    private readonly ComboBox _campus = new() { MinWidth = 160, Header = "Campus" };
    private readonly ComboBox _when = new() { MinWidth = 140, Header = "When" };
    private readonly CheckBox _showBusy = new() { Content = "Show rooms in use", IsChecked = true };
    private readonly TextBlock _status = new() { Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    private CatalogIndex? _index;
    private string _buildingId = AllBuildings;
    private string? _roomId;
    private bool _filling = true;
    private bool _followNow = true;

    public RoomsPage(AppSession session)
    {
        _session = session;
        _index = session.Index;
        var filters = new WrapLayout { Spacing = 8 };
        filters.Children.Add(_when);
        filters.Children.Add(_campus);
        filters.Children.Add(_showBusy);
        var middle = new Grid { RowSpacing = 0 };
        middle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        middle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        middle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        middle.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        middle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tools = new StackPanel { Spacing = 8 };
        tools.Children.Add(_search);
        tools.Children.Add(filters);
        middle.Children.Add(tools);
        Grid.SetRow(_axis, 1);
        middle.Children.Add(_axis);
        var list = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Chrome.KeepInside(list);
        Grid.SetRow(list, 3);
        middle.Children.Add(list);
        _status.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(_status, 4);
        middle.Children.Add(_status);

        var buildingPane = new Grid { RowSpacing = 8 };
        buildingPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        buildingPane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        buildingPane.Children.Add(new TextBlock { Text = "Buildings", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        Grid.SetRow(_buildings, 1);
        buildingPane.Children.Add(_buildings);

        var inspectorScroll = Chrome.Details(_inspector);
        var right = new PaneSplit(middle, inspectorScroll, 420);
        Content = new PaneSplit(buildingPane, right, 240) { Padding = Chrome.PagePadding };
        Chrome.ClipToBounds(this);

        _when.Items.Add(new WhenChoice(null, "Right now"));
        for (var minutes = RoomAvailability.FirstClassMinute; minutes < RoomAvailability.LastClassMinute; minutes += 30)
            _when.Items.Add(new WhenChoice(minutes, TimeOfDay.Label(minutes)));
        _when.SelectedIndex = 0;
        _campus.Items.Add("Every campus");
        foreach (var name in _index?.CampusNames ?? []) _campus.Items.Add(name);
        var preferred = session.Store.Preferences.PreferredCampus;
        _campus.SelectedItem = preferred is not null && _campus.Items.Contains(preferred) ? preferred : "Every campus";
        _filling = false;

        _search.TextChanged += (_, _) => Paint();
        _campus.SelectionChanged += (_, _) => { if (!_filling) Paint(); };
        _when.SelectionChanged += (_, _) =>
        {
            if (_filling) return;
            _followNow = _when.SelectedItem is WhenChoice { Minutes: null };
            Paint();
        };
        _showBusy.Checked += (_, _) => Paint();
        _showBusy.Unchecked += (_, _) => Paint();
        _buildings.SelectionChanged += (_, _) =>
        {
            if (_buildings.SelectedItem is ListViewItem { Tag: BuildingChoice choice }) _buildingId = choice.Id;
            PaintRooms();
        };
        _axis.SizeChanged += (_, _) => DrawAxis();

        if (_index is null)
        {
            _inspector.Children.Add(Chrome.Heading("Free rooms"));
            _inspector.Children.Add(Chrome.Body(session.Error ?? "The catalog is still loading."));
            return;
        }
        Paint();
        if (DispatcherQueue is null) return;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMinutes(1);
        timer.Tick += (_, _) =>
        {
            if (_followNow) Paint();
        };
        timer.Start();
    }

    private RoomAvailability.Moment Moment()
    {
        if (_followNow || _when.SelectedItem is not WhenChoice { Minutes: int minutes })
            return RoomAvailability.Moment.From(TermCalendar.Now());
        var today = TermCalendar.Now();
        return new RoomAvailability.Moment((int)today.DayOfWeek, minutes, DateOnly.FromDateTime(today));
    }

    private void Paint()
    {
        if (_index is not CatalogIndex index) return;
        var moment = Moment();
        var campus = _campus.SelectedItem as string;
        var buildings = index.Buildings.Where(building =>
            campus is null or "Every campus"
            || string.Equals(building.CampusCode, campus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(index.CampusName(building.CampusCode), campus, StringComparison.OrdinalIgnoreCase));
        var availability = RoomAvailability.Filter(RoomAvailability.Of(buildings, moment), _search.Text);
        availability = RoomAvailability.Sorted(availability, RoomAvailability.BuildingSort.MostFree);
        var free = availability.Sum(item => item.FreeCount);
        var total = availability.Sum(item => item.RoomCount);
        _status.Text = RoomsText.FreeSummary(free, total) + " \u00b7 " + moment.Label + " \u00b7 " + RoomsText.Disclaimer;

        _buildings.Items.Clear();
        _buildings.Items.Add(BuildingItem(new BuildingChoice(AllBuildings, $"All buildings \u00b7 {RoomsText.FreeSummary(free, total)}")));
        foreach (var item in availability)
            _buildings.Items.Add(BuildingItem(new BuildingChoice(item.Building.Code, $"{item.Building.Name} \u00b7 {item.FreeCount} of {item.RoomCount} free")));
        for (var i = 0; i < _buildings.Items.Count; i++)
        {
            if (_buildings.Items[i] is ListViewItem { Tag: BuildingChoice choice } && choice.Id == _buildingId)
            {
                _buildings.SelectedIndex = i;
                break;
            }
        }
        if (_buildings.SelectedIndex < 0 && _buildings.Items.Count > 0) _buildings.SelectedIndex = 0;
        PaintRooms(availability, moment);
        DrawAxis();
    }

    private void PaintRooms()
    {
        if (_index is null) return;
        var moment = Moment();
        var campus = _campus.SelectedItem as string;
        var buildings = _index.Buildings.Where(building =>
            campus is null or "Every campus"
            || string.Equals(building.CampusCode, campus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(_index.CampusName(building.CampusCode), campus, StringComparison.OrdinalIgnoreCase));
        var availability = RoomAvailability.Filter(RoomAvailability.Of(buildings, moment), _search.Text);
        PaintRooms(RoomAvailability.Sorted(availability, RoomAvailability.BuildingSort.MostFree), moment);
    }

    private void PaintRooms(IReadOnlyList<RoomAvailability.BuildingAvailability> availability, RoomAvailability.Moment moment)
    {
        var scoped = availability.Where(item => _buildingId == AllBuildings || item.Building.Code == _buildingId).ToList();
        if (scoped.Count == 0) scoped = availability.ToList();
        var showBusy = _showBusy.IsChecked == true;
        _rows.Children.Clear();
        RoomSchedule? selected = null;
        foreach (var item in scoped)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = item.Building.Name,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 10, 8, 4),
            });
            var entries = item.Free.Concat(showBusy ? item.Busy : []).ToList();
            if (entries.Count == 0)
            {
                _rows.Children.Add(Chrome.Body("No rooms match."));
                continue;
            }
            foreach (var entry in entries)
            {
                if (entry.Room.Id == _roomId) selected = entry.Room;
                _rows.Children.Add(RoomRow(entry, moment));
            }
        }
        selected ??= scoped.SelectMany(item => item.Free.Concat(item.Busy)).FirstOrDefault()?.Room;
        if (selected is not null) _roomId = selected.Id;
        PaintInspector(selected, moment);
    }

    private UIElement RoomRow(RoomAvailability.Entry entry, RoomAvailability.Moment moment)
    {
        var grid = new Grid { Padding = new Thickness(4, 3, 4, 3), ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(172) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new StackPanel();
        label.Children.Add(new TextBlock { Text = entry.Room.Label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var status = new TextBlock { Text = entry.Availability.Summary, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        status.Foreground = TintBrush(entry.Availability.Tint(moment));
        label.Children.Add(status);
        grid.Children.Add(label);
        var track = new RoomTrack(RoomAvailability.Bookings(entry.Room, moment.Weekday, moment.Date), moment.Minutes);
        Grid.SetColumn(track, 1);
        grid.Children.Add(track);
        var selectedRow = entry.Room.Id == _roomId;
        var card = new Border
        {
            Child = grid,
            CornerRadius = new CornerRadius(4),
        };
        void PaintRow()
        {
            var theme = card.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
            card.Background = selectedRow
                ? Chrome.ThemedBrush(theme, "SubtleFillColorSecondaryBrush", Color.FromArgb(24, 128, 128, 128))
                : Chrome.ThemedBrush(theme, "SubtleFillColorTransparentBrush", Color.FromArgb(0, 0, 0, 0));
        }
        card.Loaded += (_, _) => PaintRow();
        card.ActualThemeChanged += (_, _) => PaintRow();
        var room = entry.Room;
        card.Tapped += (_, _) =>
        {
            _roomId = room.Id;
            Paint();
        };
        ToolTipService.SetToolTip(card, entry.Availability.DetailedSummary);
        return card;
    }

    private void DrawAxis()
    {
        _axis.Children.Clear();
        var width = Math.Max(0, _axis.ActualWidth - 180);
        var title = new TextBlock { Text = "Room", FontSize = 12, Opacity = 0.7 };
        Canvas.SetLeft(title, 8);
        Canvas.SetTop(title, 6);
        _axis.Children.Add(title);
        if (width < 40) return;
        var range = RoomAvailability.LastClassMinute - RoomAvailability.FirstClassMinute;
        var step = width < 520 ? 2 : 1;
        for (var hour = RoomAvailability.FirstClassMinute / 60; hour <= RoomAvailability.LastClassMinute / 60; hour += step)
        {
            var x = 180 + width * (hour * 60 - RoomAvailability.FirstClassMinute) / range;
            var label = new TextBlock { Text = TimeOfDay.HourAxisLabel(hour), FontSize = 10, Opacity = 0.7 };
            Canvas.SetLeft(label, x - 12);
            Canvas.SetTop(label, 8);
            _axis.Children.Add(label);
        }
    }

    private void PaintInspector(RoomSchedule? room, RoomAvailability.Moment moment)
    {
        _inspector.Children.Clear();
        if (_index is not CatalogIndex index || room is null)
        {
            _inspector.Children.Add(Chrome.Heading("Room"));
            _inspector.Children.Add(Chrome.Body("Select a room to see its day."));
            return;
        }
        var status = RoomAvailability.At(room, moment);
        var today = RoomAvailability.Bookings(room, moment.Weekday, moment.Date);
        _inspector.Children.Add(Chrome.Heading(room.Label));
        _inspector.Children.Add(Chrome.Body(room.BuildingName));
        var summary = new TextBlock
        {
            Text = status.DetailedSummary,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TintBrush(status.Tint(moment)),
        };
        var note = RoomsText.FollowUpNote(status, today);
        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(summary);
        if (note is not null) header.Children.Add(Chrome.Body(note));
        _inspector.Children.Add(Chrome.Card(header));

        var dates = RoomAvailability.ClassDates.Derive(index.Catalog.Sections);
        if (_followNow && dates is RoomAvailability.ClassDates window && moment.Date is DateOnly day && !window.Contains(day))
            _inspector.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Message = RoomsText.ClassDatesMessage(index.Term.Name, window),
            });
        if (!moment.IsWithinClassHours)
            _inspector.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Informational,
                Message = RoomsText.OutsideHours,
            });

        var mode = new ComboBox { MinWidth = 120 };
        mode.Items.Add("Day");
        mode.Items.Add("Week");
        mode.SelectedIndex = 0;
        var dayBox = new ComboBox { MinWidth = 140 };
        foreach (var weekday in new[] { 1, 2, 3, 4, 5, 6, 0 })
            dayBox.Items.Add(new DayChoice(weekday));
        for (var i = 0; i < dayBox.Items.Count; i++)
            if (dayBox.Items[i] is DayChoice choice && choice.Day == moment.Weekday) dayBox.SelectedIndex = i;
        var controls = new WrapLayout { Spacing = 8 };
        controls.Children.Add(mode);
        controls.Children.Add(dayBox);
        _inspector.Children.Add(controls);

        var host = new StackPanel { Spacing = 8 };
        _inspector.Children.Add(host);
        var listHost = new StackPanel { Spacing = 4 };
        _inspector.Children.Add(listHost);
        _inspector.Children.Add(Chrome.Body(RoomsText.Disclaimer));

        void PaintMode()
        {
            var weekday = (dayBox.SelectedItem as DayChoice)?.Day ?? moment.Weekday;
            dayBox.Visibility = mode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            host.Children.Clear();
            if (mode.SelectedIndex == 0)
                host.Children.Add(new RoomDayColumn(room, index, weekday, 8, 18, _session));
            else
            {
                var sections = room.Bookings.Select(booking => index.Section(booking.Crn)).OfType<CourseSection>().DistinctBy(section => section.Crn).ToList();
                var week = new WeekCalendarView();
                week.Show(sections, key => CourseColor.ForCourse(key).Index, 8, 18, _session.Store.Preferences.ShowWeekends);
                host.Children.Add(week);
            }
            var bookings = RoomAvailability.Bookings(room, weekday, weekday == moment.Weekday ? moment.Date : null);
            listHost.Children.Clear();
            var title = weekday == moment.Weekday && _followNow ? $"Today \u00b7 {DaySet.FullNames[weekday]}" : DaySet.FullNames[weekday];
            listHost.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (bookings.Count == 0)
            {
                listHost.Children.Add(Chrome.Body("No classes. This room is free all day."));
                return;
            }
            foreach (var booking in bookings)
                listHost.Children.Add(ClassRow(booking, weekday, moment));
        }
        mode.SelectionChanged += (_, _) => PaintMode();
        dayBox.SelectionChanged += (_, _) => PaintMode();
        PaintMode();
    }

    private UIElement ClassRow(RoomBooking booking, int weekday, RoomAvailability.Moment moment)
    {
        var section = _index?.Section(booking.Crn);
        var title = section?.Title ?? _index?.Course(booking.Course)?.Title ?? "";
        var current = weekday == moment.Weekday && booking.Start <= moment.Minutes && moment.Minutes < booking.End;
        var past = weekday == moment.Weekday && booking.End <= moment.Minutes;
        var line = new StackPanel { Spacing = 2 };
        var code = new TextBlock
        {
            Text = booking.Course.ToString(),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = past ? 0.55 : 1,
            TextWrapping = TextWrapping.Wrap,
        };
        line.Children.Add(code);
        if (section?.ClassType is string type) line.Children.Add(Chrome.Body(type));
        if (title.Length > 0) line.Children.Add(Chrome.Body(title));
        var time = new TextBlock { Text = TimeOfDay.RangeLabel(booking.Start, booking.End), Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(line);
        var side = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        side.Children.Add(time);
        if (current) side.Children.Add(new TextBlock { Text = _followNow ? "In progress" : "At this time", Foreground = TintBrush("red") });
        Grid.SetColumn(side, 1);
        row.Children.Add(side);
        var button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        var course = booking.Course;
        button.Click += (_, _) => CourseWindow.Open(_session, course);
        return button;
    }

    private static Brush TintBrush(string tint) => tint switch
    {
        "red" => new SolidColorBrush(Color.FromArgb(255, 200, 16, 46)),
        "orange" => new SolidColorBrush(Color.FromArgb(255, 180, 90, 0)),
        _ => new SolidColorBrush(Color.FromArgb(255, 30, 140, 60)),
    };

    private sealed record WhenChoice(int? Minutes, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record DayChoice(int Day)
    {
        public override string ToString() => DaySet.FullNames[Day];
    }

    private sealed record BuildingChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private static ListViewItem BuildingItem(BuildingChoice choice) => new()
    {
        Content = new TextBlock { Text = choice.Label, TextWrapping = TextWrapping.Wrap },
        Tag = choice,
    };
}

/// <summary>One room's school day: free stretches in green, classes in gray, a line at the chosen time.</summary>
internal sealed class RoomTrack : UserControl
{
    private readonly IReadOnlyList<RoomBooking> _bookings;
    private readonly int _marker;
    private readonly Canvas _canvas = new() { Height = 18 };

    public RoomTrack(IReadOnlyList<RoomBooking> bookings, int marker)
    {
        _bookings = bookings;
        _marker = marker;
        Content = _canvas;
        SizeChanged += (_, _) => Draw();
    }

    private void Draw()
    {
        var width = Math.Max(0, ActualWidth);
        _canvas.Children.Clear();
        _canvas.Width = width;
        var start = RoomAvailability.FirstClassMinute;
        var end = RoomAvailability.LastClassMinute;
        var span = end - start;
        double X(int minutes)
        {
            var clamped = Math.Clamp(minutes, start, end);
            return width * (clamped - start) / span;
        }
        _canvas.Children.Add(new Rectangle
        {
            Width = width,
            Height = 18,
            Fill = new SolidColorBrush(Color.FromArgb(33, 128, 128, 128)),
            RadiusX = 4,
            RadiusY = 4,
        });
        foreach (var stretch in RoomAvailability.FreeStretches(_bookings, start, end))
        {
            void Bar(int from, int to, byte alpha)
            {
                var left = X(from);
                var right = X(to);
                var rect = new Rectangle
                {
                    Width = Math.Max(0, right - left),
                    Height = 18,
                    Fill = new SolidColorBrush(Color.FromArgb(alpha, 46, 160, 70)),
                };
                Canvas.SetLeft(rect, left);
                _canvas.Children.Add(rect);
            }
            if (_marker <= stretch.Start) Bar(stretch.Start, stretch.End, 190);
            else if (_marker >= stretch.End) Bar(stretch.Start, stretch.End, 70);
            else
            {
                Bar(stretch.Start, _marker, 70);
                Bar(_marker, stretch.End, 190);
            }
        }
        foreach (var booking in _bookings)
        {
            var left = X(booking.Start);
            var blockWidth = Math.Max(0, X(booking.End) - left);
            var block = new Border
            {
                Width = blockWidth,
                Height = 18,
                Background = new SolidColorBrush(Color.FromArgb(90, 80, 80, 80)),
            };
            if (blockWidth >= 46)
            {
                block.Child = new TextBlock
                {
                    Text = booking.Course.ToString(),
                    FontSize = 9,
                    Margin = new Thickness(3, 1, 3, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
            }
            Canvas.SetLeft(block, left);
            _canvas.Children.Add(block);
            ToolTipService.SetToolTip(block, $"{booking.Course} \u00b7 {TimeOfDay.RangeLabel(booking.Start, booking.End)}");
        }
        if (_marker >= start && _marker < end)
        {
            var line = new Rectangle
            {
                Width = 2,
                Height = 26,
                Fill = new SolidColorBrush(Color.FromArgb(255, 200, 16, 46)),
            };
            Canvas.SetLeft(line, X(_marker) - 1);
            Canvas.SetTop(line, -4);
            _canvas.Children.Add(line);
        }
    }
}

/// <summary>One day of a room, hour by hour, from the room's bookings.</summary>
internal sealed class RoomDayColumn : UserControl
{
    public RoomDayColumn(RoomSchedule room, CatalogIndex index, int weekday, int startHour, int endHour, AppSession session)
    {
        var events = room.CalendarEvents(index).Where(item => item.Day == weekday).ToList();
        const double hourHeight = 38;
        const double axis = 52;
        var height = (endHour - startHour) * hourHeight;
        var canvas = new Canvas { Width = axis + 220, Height = height + 8 };
        for (var hour = startHour; hour <= endHour; hour++)
        {
            var y = (hour - startHour) * hourHeight;
            canvas.Children.Add(new Line
            {
                X1 = axis,
                X2 = canvas.Width,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
            });
            var label = new TextBlock { Text = TimeOfDay.Label(hour * 60), FontSize = 11, Opacity = 0.7 };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 8);
            canvas.Children.Add(label);
        }
        foreach (var item in events)
        {
            var top = (item.Start / 60d - startHour) * hourHeight;
            var blockHeight = Math.Max(18, (item.End - item.Start) / 60d * hourHeight);
            var color = Color.FromArgb(255, item.Color.R, item.Color.G, item.Color.B);
            var card = new Border
            {
                Width = 200,
                Height = blockHeight,
                Background = new SolidColorBrush(Color.FromArgb(46, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(3, 1, 1, 1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 2, 4, 2),
                Child = new TextBlock { Text = item.Title, FontSize = 11, TextWrapping = TextWrapping.Wrap },
            };
            Canvas.SetLeft(card, axis + 4);
            Canvas.SetTop(card, top);
            var course = item.Title;
            card.Tapped += (_, _) =>
            {
                if (CourseKey.Parse(course) is CourseKey key) CourseWindow.Open(session, key);
            };
            canvas.Children.Add(card);
        }
        Content = new ScrollViewer { Content = canvas, MaxHeight = 420 };
    }
}
