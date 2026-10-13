// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TermOrganize.Core;
using Windows.UI;

namespace TermOrganize;

/// <summary>The week grid: hours down the side, days across, meetings in lanes.</summary>
internal sealed class WeekCalendarView : UserControl
{
    private readonly Canvas _canvas = new();
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    public WeekCalendarView()
    {
        _scroll.Content = _canvas;
        Content = _scroll;
        MinHeight = 280;
    }

    public void Show(IReadOnlyList<CourseSection> sections, Func<CourseKey, int> colorIndex, int startHour, int endHour, bool showWeekends)
    {
        var events = new List<CalendarEvent>();
        foreach (var section in sections)
            events.AddRange(CalendarEvent.Events(section, CourseColor.At(colorIndex(section.CourseKey)), title: section.CourseKey.ToString()));
        events = CalendarEvent.MarkingConflicts(events);
        var days = DaysToShow(events, showWeekends);
        startHour = Math.Clamp(startHour, 0, 22);
        endHour = Math.Clamp(Math.Max(endHour, startHour + 1), startHour + 1, 24);
        const double hourHeight = 48;
        const double axis = 52;
        const double dayWidth = 128;
        var height = (endHour - startHour) * hourHeight;
        _canvas.Children.Clear();
        _canvas.Width = axis + days.Count * dayWidth;
        _canvas.Height = height + 28;

        for (var hour = startHour; hour <= endHour; hour++)
        {
            var y = 24 + (hour - startHour) * hourHeight;
            _canvas.Children.Add(Line(axis, y, _canvas.Width, y, Color.FromArgb(40, 128, 128, 128)));
            var label = new TextBlock { Text = TimeOfDay.Label(hour * 60), FontSize = 11, Opacity = 0.7 };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 8);
            _canvas.Children.Add(label);
        }
        for (var index = 0; index < days.Count; index++)
        {
            var title = new TextBlock
            {
                Text = days[index] switch { 0 => "Sun", 1 => "Mon", 2 => "Tue", 3 => "Wed", 4 => "Thu", 5 => "Fri", _ => "Sat" },
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            };
            Canvas.SetLeft(title, axis + index * dayWidth + 8);
            Canvas.SetTop(title, 0);
            _canvas.Children.Add(title);
        }

        var lanes = CalendarLayout.Lanes(events);
        foreach (var calendarEvent in events)
        {
            if (!days.Contains(calendarEvent.Day)) continue;
            var dayIndex = days.IndexOf(calendarEvent.Day);
            var (lane, count) = lanes.TryGetValue(calendarEvent.Id, out var laneInfo) ? laneInfo : (0, 1);
            var laneWidth = (dayWidth - 8) / Math.Max(1, count);
            var top = 24 + (calendarEvent.Start / 60d - startHour) * hourHeight;
            var blockHeight = Math.Max(18, (calendarEvent.End - calendarEvent.Start) / 60d * hourHeight);
            var color = Color.FromArgb(255, calendarEvent.Color.R, calendarEvent.Color.G, calendarEvent.Color.B);
            var fill = Color.FromArgb((byte)(255 * CourseColor.FillOpacity), color.R, color.G, color.B);
            var card = new Border
            {
                Width = Math.Max(24, laneWidth - 4),
                Height = blockHeight,
                Background = new SolidColorBrush(fill),
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(calendarEvent.IsConflict ? 2 : 3, 1, 1, 1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 2, 4, 2),
                Child = new TextBlock
                {
                    Text = calendarEvent.Title + (calendarEvent.Subtitle is null ? "" : "\n" + calendarEvent.Subtitle),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            Canvas.SetLeft(card, axis + dayIndex * dayWidth + 4 + lane * laneWidth);
            Canvas.SetTop(card, top);
            _canvas.Children.Add(card);
        }

        var now = DateTime.Now;
        if (days.Contains((int)now.DayOfWeek))
        {
            var minutes = now.Hour * 60 + now.Minute;
            if (minutes >= startHour * 60 && minutes <= endHour * 60)
            {
                var y = 24 + (minutes / 60d - startHour) * hourHeight;
                var dayIndex = days.IndexOf((int)now.DayOfWeek);
                _canvas.Children.Add(Line(axis + dayIndex * dayWidth, y, axis + (dayIndex + 1) * dayWidth, y, Color.FromArgb(220, 200, 16, 46)));
            }
        }
    }

    private static List<int> DaysToShow(IReadOnlyList<CalendarEvent> events, bool showWeekends)
    {
        var days = new List<int>();
        for (var day = 0; day < 7; day++)
        {
            var weekend = day is 0 or 6;
            if (weekend && !showWeekends && !events.Any(item => item.Day == day)) continue;
            days.Add(day);
        }
        return days.Count == 0 ? [1, 2, 3, 4, 5] : days;
    }

    private static Line Line(double x1, double y1, double x2, double y2, Color color) => new()
    {
        X1 = x1,
        Y1 = y1,
        X2 = x2,
        Y2 = y2,
        Stroke = new SolidColorBrush(color),
        StrokeThickness = 1,
    };
}
