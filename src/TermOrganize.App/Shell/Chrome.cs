// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace TermOrganize;

internal static class Chrome
{
    /// <summary>24px beside a compact pane, 36px when the window is at least 1008px wide.</summary>
    public static Thickness PagePadding { get; private set; } = new(24);

    public static void UsePageWidth(double width)
    {
        var inset = width >= 1008 ? 36 : 24;
        PagePadding = new Thickness(inset);
    }

    public static readonly string[] CourseColors =
    [
        "#C8102E", "#0D6E6E", "#1F4B99", "#8A5A00", "#5C4B8A",
        "#2E6B3A", "#8C3A4B", "#3D5A80", "#6B4F3A", "#3F6F8F",
    ];

    public static TextBlock Heading(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            Style = StyleOf("TitleTextBlockStyle"),
            TextWrapping = TextWrapping.WrapWholeWords,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 3,
        };
        ToolTipService.SetToolTip(block, text);
        return block;
    }

    /// <summary>Section title. The top margin plus an 8px stack gap lands on the 24px section step.</summary>
    public static TextBlock Section(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            Style = StyleOf("SubtitleTextBlockStyle"),
            TextWrapping = TextWrapping.WrapWholeWords,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            Margin = new Thickness(0, 16, 0, 4),
        };
        ToolTipService.SetToolTip(block, text);
        return block;
    }

    public static TextBlock Body(string text, bool wrap = true) => new()
    {
        Text = text,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        Opacity = 0.85,
    };

    /// <summary>Secondary note. Lighter than body text so it stays readable on light and dark fills.</summary>
    public static TextBlock Caption(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.65,
        FontSize = 12,
    };

    /// <summary>
    /// A fixed-height scroller. An unconstrained ScrollViewer grows with its content, so a dialog
    /// footer below it would sit past the window.
    /// </summary>
    public static ScrollViewer ConstrainedScroll(UIElement content, double height)
    {
        var scroll = new ScrollViewer
        {
            Content = content,
            Height = height,
            MaxHeight = height,
            VerticalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        KeepInside(scroll);
        return scroll;
    }

    /// <summary>Leaves room for a dialog title, the pinned buttons, and the window chrome.</summary>
    public static double DialogViewport(double windowHeight)
    {
        var height = windowHeight > 1 ? windowHeight : 900;
        return Math.Clamp(height - 280, 220, 480);
    }

    /// <summary>
    /// The right-hand course pane. Twenty-four pixels of left padding separate it from the
    /// splitter. A 24px right margin keeps the description and section cards off the window edge.
    /// </summary>
    public static ScrollViewer Details(UIElement content)
    {
        var scroll = new ScrollViewer
        {
            Content = content,
            Padding = new Thickness(24, 0, 0, 0),
            Margin = new Thickness(0, 0, 24, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        KeepInside(scroll);
        return scroll;
    }

    /// <summary>
    /// A vertical stack measured with infinite width never wraps, so the line runs off the
    /// window. Pin the content to the viewport, and keep a caller-set MaxWidth when it is tighter.
    /// </summary>
    public static void KeepInside(ScrollViewer scroll)
    {
        scroll.HorizontalScrollMode = ScrollMode.Disabled;
        scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        var cap = scroll.Content is FrameworkElement existing ? existing.MaxWidth : double.PositiveInfinity;
        void Fit()
        {
            if (scroll.Content is not FrameworkElement content) return;
            var inner = scroll.ActualWidth - scroll.Padding.Left - scroll.Padding.Right;
            if (inner <= 1) return;
            var view = scroll.ViewportWidth;
            var width = view > 1 && view <= inner + 0.5 ? view : inner;
            if (!double.IsPositiveInfinity(cap) && cap > 0 && cap < width)
                width = cap;
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (double.IsNaN(content.MaxWidth) || Math.Abs(content.MaxWidth - width) > 0.5)
                content.MaxWidth = width;
        }
        scroll.Loaded += (_, _) => Fit();
        scroll.SizeChanged += (_, _) => Fit();
    }

    /// <summary>Stops a child from painting through this element's padding into the window edge.</summary>
    public static void ClipToBounds(FrameworkElement element)
    {
        void Fit()
        {
            var (left, top, right, bottom) = Inset(element);
            var width = element.ActualWidth - left - right;
            var height = element.ActualHeight - top - bottom;
            if (element.ActualWidth <= 1 || element.ActualHeight <= 1 || width <= 0 || height <= 0) return;
            var rect = new Rect(left, top, Math.Max(0, width), Math.Max(0, height));
            if (element.Clip is RectangleGeometry geometry)
                geometry.Rect = rect;
            else
                element.Clip = new RectangleGeometry { Rect = rect };
        }
        element.Loaded += (_, _) => Fit();
        element.SizeChanged += (_, _) => Fit();
    }

    private static (double Left, double Top, double Right, double Bottom) Inset(FrameworkElement element) => element switch
    {
        Grid grid => (grid.Padding.Left, grid.Padding.Top, grid.Padding.Right, grid.Padding.Bottom),
        Control control => (control.Padding.Left, control.Padding.Top, control.Padding.Right, control.Padding.Bottom),
        Border border => (border.Padding.Left, border.Padding.Top, border.Padding.Right, border.Padding.Bottom),
        StackPanel stack => (stack.Padding.Left, stack.Padding.Top, stack.Padding.Right, stack.Padding.Bottom),
        _ => (0, 0, 0, 0),
    };

    public static Border Card(UIElement child)
    {
        var border = new Border
        {
            Child = child,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
        };
        void Paint()
        {
            var theme = border.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
            border.Background = ThemedBrush(theme, "CardBackgroundFillColorDefaultBrush",
                theme == ElementTheme.Dark ? Color.FromArgb(255, 45, 45, 45) : Color.FromArgb(255, 255, 255, 255));
            border.BorderBrush = ThemedBrush(theme, "CardStrokeColorDefaultBrush",
                theme == ElementTheme.Dark ? Color.FromArgb(255, 70, 70, 70) : Color.FromArgb(255, 220, 220, 220));
        }
        border.ActualThemeChanged += (_, _) => Paint();
        border.Loaded += (_, _) => Paint();
        return border;
    }

    public static void Fill(StackPanel panel, string title, string message)
    {
        panel.Children.Clear();
        panel.Children.Add(Heading(title));
        panel.Children.Add(Body(message));
    }

    public static void Fill(Grid host, string title, string message)
    {
        host.Children.Clear();
        host.RowDefinitions.Clear();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Heading(title));
        panel.Children.Add(Body(message));
        host.Children.Add(panel);
    }

    public static Brush ThemedBrush(ElementTheme theme, string key, Color fallback)
    {
        var name = theme == ElementTheme.Dark ? "Dark" : "Light";
        try
        {
            if (Application.Current is not null)
            {
                if (TryTheme(Application.Current.Resources, name, key, out var brush))
                    return Clone(brush);
                foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
                {
                    if (TryTheme(dictionary, name, key, out brush))
                        return Clone(brush);
                }
            }
        }
        catch (Exception)
        {
            // A missing theme dictionary still has the solid fallback.
        }
        return new SolidColorBrush(fallback);
    }

    public static Style? StyleOf(string key)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Style style)
                return style;
        }
        catch (Exception)
        {
            // The text block still renders with its local wrapping and trimming.
        }
        return null;
    }

    private static bool TryTheme(ResourceDictionary dictionary, string theme, string key, out Brush brush)
    {
        brush = null!;
        if (dictionary.ThemeDictionaries.TryGetValue(theme, out var themed) && themed is ResourceDictionary colors
            && colors.TryGetValue(key, out var value))
        {
            if (value is Brush found)
            {
                brush = found;
                return true;
            }
            if (value is Color color)
            {
                brush = new SolidColorBrush(color);
                return true;
            }
        }
        foreach (var nested in dictionary.MergedDictionaries)
        {
            if (TryTheme(nested, theme, key, out brush)) return true;
        }
        return false;
    }

    private static Brush Clone(Brush brush) =>
        brush is SolidColorBrush solid ? new SolidColorBrush(solid.Color) : brush;
}
