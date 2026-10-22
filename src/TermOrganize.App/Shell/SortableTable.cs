// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace TermOrganize;

/// <summary>A header row and text rows. Click a header to sort. Double-click a row to open it.</summary>
internal sealed class SortableTable : UserControl
{
    public sealed class Column
    {
        public required string Title { get; init; }
        public double Width { get; init; } = 120;
        public bool Numeric { get; init; }
    }

    public sealed class Row
    {
        public required string[] Cells { get; init; }
        public required object Tag { get; init; }
        /// <summary>Sort keys aligned with the columns. Null uses the cell text.</summary>
        public double?[] Sort { get; init; } = [];
    }

    private readonly ScrollViewer _headerScroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalScrollMode = ScrollMode.Enabled,
        VerticalScrollMode = ScrollMode.Disabled,
    };
    private readonly ListView _list = new()
    {
        SelectionMode = ListViewSelectionMode.Single,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    private readonly TextBlock _empty = new()
    {
        Text = "Nothing to show.",
        Opacity = 0.7,
        Margin = new Thickness(8),
        Visibility = Visibility.Collapsed,
    };
    private readonly Grid _header = new() { Height = 36 };
    private IReadOnlyList<Column> _columns = [];
    private List<Row> _rows = [];
    private int _sortColumn = -1;
    private bool _sortDescending;
    private bool _syncingScroll;
    private bool _painting;
    private ScrollViewer? _bodyScroll;

    public event Action<object>? RowChosen;
    public event Action<object>? RowOpened;

    public SortableTable()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ScrollViewer.SetHorizontalScrollMode(_list, ScrollMode.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        _headerScroll.Content = _header;
        root.Children.Add(_headerScroll);
        var body = new Grid();
        body.Children.Add(_list);
        body.Children.Add(_empty);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Content = root;
        _list.SelectionChanged += (_, _) =>
        {
            if (_painting) return;
            if (_list.SelectedItem is not ListViewItem item) return;
            _list.ScrollIntoView(item);
            if (item.Tag is { } tag) RowChosen?.Invoke(tag);
        };
        _list.DoubleTapped += (_, args) =>
        {
            if (_list.SelectedItem is ListViewItem { Tag: { } tag })
            {
                RowOpened?.Invoke(tag);
                args.Handled = true;
            }
        };
        _list.Loaded += (_, _) => AttachBodyScroll();
        Chrome.ClipToBounds(this);
        ActualThemeChanged += (_, _) =>
        {
            if (_columns.Count > 0)
            {
                PaintHeader();
                PaintRows();
            }
        };
        MinHeight = 120;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        var width = double.IsInfinity(availableSize.Width) ? size.Width : Math.Min(size.Width, availableSize.Width);
        var height = double.IsInfinity(availableSize.Height) ? size.Height : Math.Min(size.Height, availableSize.Height);
        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    public void Show(Column[] columns, IReadOnlyList<Row> rows)
    {
        _columns = columns;
        _rows = rows.ToList();
        if (_sortColumn >= _columns.Count) _sortColumn = -1;
        PaintHeader();
        PaintRows();
    }

    private void PaintHeader()
    {
        _header.Children.Clear();
        _header.ColumnDefinitions.Clear();
        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(column.Width) });
            var title = column.Title;
            if (_sortColumn == i) title += _sortDescending ? " \u25BC" : " \u25B2";
            var button = new Button
            {
                Content = title,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4, 8, 4),
            };
            QuietButton(button);
            var index = i;
            button.Click += (_, _) =>
            {
                if (_sortColumn == index) _sortDescending = !_sortDescending;
                else
                {
                    _sortColumn = index;
                    _sortDescending = false;
                }
                PaintHeader();
                PaintRows();
            };
            Grid.SetColumn(button, i);
            _header.Children.Add(button);
        }
        _header.Width = _columns.Sum(column => column.Width);
    }

    private void PaintRows()
    {
        IEnumerable<Row> ordered = _rows;
        if (_sortColumn >= 0 && _sortColumn < _columns.Count)
        {
            var index = _sortColumn;
            var numeric = _columns[index].Numeric;
            ordered = _sortDescending
                ? _rows.OrderByDescending(row => Key(row, index, numeric))
                : _rows.OrderBy(row => Key(row, index, numeric));
        }
        _painting = true;
        _list.Items.Clear();
        foreach (var row in ordered)
        {
            var grid = new Grid { Width = _header.Width, MinHeight = 32 };
            for (var i = 0; i < _columns.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_columns[i].Width) });
                var text = i < row.Cells.Length ? row.Cells[i] : "";
                var block = new TextBlock
                {
                    Text = text,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 4, 8, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = text.Contains('\n') ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    MaxLines = text.Contains('\n') ? 3 : 1,
                };
                Grid.SetColumn(block, i);
                grid.Children.Add(block);
            }
            _list.Items.Add(new ListViewItem
            {
                Content = grid,
                Tag = row.Tag,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0),
            });
        }
        _empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _painting = false;
        AttachBodyScroll();
    }

    /// <summary>The list's own scroller moves the header. Arrow keys stay inside the list.</summary>
    private void AttachBodyScroll()
    {
        var viewer = FindScrollViewer(_list);
        if (viewer is null || ReferenceEquals(viewer, _bodyScroll)) return;
        if (_bodyScroll is not null) _bodyScroll.ViewChanged -= SyncHeader;
        _bodyScroll = viewer;
        _bodyScroll.ViewChanged += SyncHeader;
    }

    private void SyncHeader(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (_syncingScroll || _bodyScroll is null) return;
        _syncingScroll = true;
        _headerScroll.ChangeView(_bodyScroll.HorizontalOffset, null, null, true);
        _syncingScroll = false;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer) return viewer;
            var nested = FindScrollViewer(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static string Key(Row row, int index, bool numeric)
    {
        if (index < row.Sort.Length && row.Sort[index] is double number)
            return number.ToString("0000000000.000", System.Globalization.CultureInfo.InvariantCulture);
        var text = index < row.Cells.Length ? row.Cells[index] : "";
        if (numeric && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed.ToString("0000000000.000", System.Globalization.CultureInfo.InvariantCulture);
        return text;
    }

    private Brush Themed(string key, Color fallback)
    {
        var theme = ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        return Chrome.ThemedBrush(theme, key, fallback);
    }

    private void QuietButton(Button button)
    {
        var theme = ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        var bg = Themed("SubtleFillColorTransparentBrush", Color.FromArgb(0, 0, 0, 0));
        var hover = Themed("SubtleFillColorSecondaryBrush", Color.FromArgb(24, 128, 128, 128));
        var fg = Chrome.ThemedBrush(theme, "TextFillColorPrimaryBrush",
            theme == ElementTheme.Dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 27, 27, 27));
        button.Background = bg;
        button.Foreground = fg;
        button.Resources["ButtonBackground"] = bg;
        button.Resources["ButtonBackgroundPointerOver"] = hover;
        button.Resources["ButtonBackgroundPressed"] = hover;
        button.Resources["ButtonForeground"] = fg;
        button.Resources["ButtonForegroundPointerOver"] = fg;
        button.Resources["ButtonBorderBrush"] = bg;
        button.Resources["ButtonBorderBrushPointerOver"] = hover;
    }
}
