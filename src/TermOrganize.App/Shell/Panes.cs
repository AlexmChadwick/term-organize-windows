// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace TermOrganize;

/// <summary>A two-pane layout with a drag handle, the same job as the Mac split.</summary>
internal sealed class PaneSplit : Grid
{
    private readonly ColumnDefinition _left;
    private readonly double _preferred;
    private bool _fitted;

    public PaneSplit(FrameworkElement left, FrameworkElement right, double start = 420)
    {
        _preferred = start;
        _left = new ColumnDefinition { Width = new GridLength(start), MinWidth = 160, MaxWidth = 720 };
        ColumnDefinitions.Add(_left);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
        Children.Add(left);
        var thumb = new Thumb
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 128, 128, 128)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        thumb.DragDelta += (_, args) =>
        {
            var next = Math.Clamp(_left.ActualWidth + args.HorizontalChange, _left.MinWidth, _left.MaxWidth);
            _left.Width = new GridLength(next);
        };
        Grid.SetColumn(thumb, 1);
        Children.Add(thumb);
        Grid.SetColumn(right, 2);
        Children.Add(right);
        Loaded += (_, _) => Fit();
        SizeChanged += (_, _) => Fit();
    }

    /// <summary>
    /// Keeps both panes inside the window. A fixed left width plus a 240px right minimum
    /// used to paint past the edge at 800px.
    /// </summary>
    private void Fit()
    {
        if (ActualWidth <= 0) return;
        var rightFloor = Math.Min(220, Math.Max(120, ActualWidth * 0.28));
        var maxLeft = Math.Max(120, ActualWidth - 8 - rightFloor);
        _left.MaxWidth = Math.Min(720, maxLeft);
        _left.MinWidth = Math.Min(160, _left.MaxWidth);
        var current = _left.Width.IsAbsolute ? _left.Width.Value : _preferred;
        var next = current;
        if (!_fitted || current > _left.MaxWidth || current < _left.MinWidth)
            next = Math.Clamp(_fitted ? current : _preferred, _left.MinWidth, _left.MaxWidth);
        _fitted = true;
        if (Math.Abs(next - current) > 0.5)
            _left.Width = new GridLength(next);
    }
}

/// <summary>Wraps toolbar controls onto the next line instead of clipping them.</summary>
internal sealed class WrapLayout : Panel
{
    public double Spacing { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        var rows = Break(availableSize.Width);
        var height = rows.Count == 0 ? 0 : rows.Sum(row => row.Height) + Spacing * (rows.Count - 1);
        var width = double.IsPositiveInfinity(availableSize.Width)
            ? rows.Select(row => row.Width).DefaultIfEmpty(0).Max()
            : availableSize.Width;
        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (var row in Break(finalSize.Width))
        {
            double x = 0;
            foreach (var child in row.Items)
            {
                var w = child.DesiredSize.Width;
                var h = child.DesiredSize.Height;
                child.Arrange(new Rect(x, y + (row.Height - h) / 2, w, h));
                x += w + Spacing;
            }
            y += row.Height + Spacing;
        }
        return finalSize;
    }

    private List<Row> Break(double availableWidth)
    {
        var limit = double.IsPositiveInfinity(availableWidth) || availableWidth <= 0
            ? double.PositiveInfinity
            : availableWidth;
        var rows = new List<Row>();
        var current = new Row();
        foreach (var child in Children)
        {
            child.Measure(new Size(limit, double.PositiveInfinity));
            var w = child.DesiredSize.Width;
            var h = child.DesiredSize.Height;
            var gap = current.Items.Count > 0 ? Spacing : 0;
            if (current.Items.Count > 0 && current.Width + gap + w > limit)
            {
                rows.Add(current);
                current = new Row();
                gap = 0;
            }
            current.Items.Add(child);
            current.Width += gap + w;
            current.Height = Math.Max(current.Height, h);
        }
        if (current.Items.Count > 0) rows.Add(current);
        return rows;
    }

    private sealed class Row
    {
        public List<UIElement> Items { get; } = [];
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
