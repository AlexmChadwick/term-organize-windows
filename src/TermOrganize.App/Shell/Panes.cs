// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace TermOrganize;

/// <summary>A two-pane layout with a drag handle, the same job as the Mac split.</summary>
internal sealed class PaneSplit : Grid
{
    private readonly ColumnDefinition _left;

    public PaneSplit(UIElement left, UIElement right, double start = 420)
    {
        _left = new ColumnDefinition { Width = new GridLength(start), MinWidth = 220, MaxWidth = 720 };
        ColumnDefinitions.Add(_left);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
        Children.Add(left);
        var thumb = new Thumb
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 128, 128, 128)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        thumb.DragDelta += (_, args) =>
        {
            var next = Math.Clamp(_left.ActualWidth + args.HorizontalChange, 220, 720);
            _left.Width = new GridLength(next);
        };
        Grid.SetColumn(thumb, 1);
        Children.Add(thumb);
        Grid.SetColumn(right, 2);
        Children.Add(right);
    }
}
