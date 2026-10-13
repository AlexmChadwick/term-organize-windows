// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

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
    private readonly ScrollViewer _bodyScroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly Grid _header = new() { Height = 36 };
    private readonly StackPanel _body = new();
    private IReadOnlyList<Column> _columns = [];
    private List<Row> _rows = [];
    private int _sortColumn = -1;
    private bool _sortDescending;
    private bool _syncingScroll;
    private Border? _selected;
    private readonly Dictionary<Border, Brush> _idle = [];

    public event Action<object>? RowChosen;
    public event Action<object>? RowOpened;

    public SortableTable()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _headerScroll.Content = _header;
        _bodyScroll.Content = _body;
        root.Children.Add(_headerScroll);
        Grid.SetRow(_bodyScroll, 1);
        root.Children.Add(_bodyScroll);
        Content = root;
        _bodyScroll.ViewChanged += (_, _) =>
        {
            if (_syncingScroll) return;
            _syncingScroll = true;
            _headerScroll.ChangeView(_bodyScroll.HorizontalOffset, null, null, true);
            _syncingScroll = false;
        };
        MinHeight = 120;
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
                Background = (Brush)Application.Current.Resources["SubtleFillColorTransparentBrush"],
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4, 8, 4),
            };
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
        _body.Children.Clear();
        _selected = null;
        _idle.Clear();
        var stripe = false;
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
                    TextWrapping = TextWrapping.NoWrap,
                };
                Grid.SetColumn(block, i);
                grid.Children.Add(block);
            }
            var stripeBrush = stripe
                ? (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]
                : (Brush)Application.Current.Resources["SubtleFillColorTransparentBrush"];
            stripe = !stripe;
            var card = new Border
            {
                Child = grid,
                Background = stripeBrush,
                Tag = row.Tag,
            };
            _idle[card] = stripeBrush;
            var tag = row.Tag;
            card.Tapped += (_, _) => Choose(card, tag);
            card.DoubleTapped += (_, args) =>
            {
                Choose(card, tag);
                RowOpened?.Invoke(tag);
                args.Handled = true;
            };
            _body.Children.Add(card);
        }
        if (_rows.Count == 0)
            _body.Children.Add(new TextBlock { Text = "Nothing to show.", Opacity = 0.7, Margin = new Thickness(8) });
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

    private void Choose(Border card, object tag)
    {
        if (_selected is not null && _idle.TryGetValue(_selected, out var idle))
            _selected.Background = idle;
        card.Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        _selected = card;
        RowChosen?.Invoke(tag);
    }
}
