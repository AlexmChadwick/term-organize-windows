// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

    public static ScrollViewer Details(UIElement content) => new()
    {
        Content = content,
        Padding = new Thickness(24, 0, 0, 0),
        HorizontalScrollMode = ScrollMode.Disabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
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
