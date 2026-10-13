// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TermOrganize;

internal static class Chrome
{
    /// <summary>Page inset. 16 is the compact step on the Fluent 4px grid, so an 800px window still has room for the pane.</summary>
    public static Thickness PagePadding { get; } = new(16);

    public static readonly string[] CourseColors =
    [
        "#C8102E", "#0D6E6E", "#1F4B99", "#8A5A00", "#5C4B8A",
        "#2E6B3A", "#8C3A4B", "#3D5A80", "#6B4F3A", "#3F6F8F",
    ];

    public static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 22,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
    };

    public static TextBlock Body(string text, bool wrap = true) => new()
    {
        Text = text,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        Opacity = 0.85,
    };

    public static Border Card(UIElement child) => new()
    {
        Child = child,
        Padding = new Thickness(12),
        Margin = new Thickness(0, 0, 0, 8),
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
    };

    public static void Fill(StackPanel panel, string title, string message)
    {
        panel.Children.Clear();
        panel.Children.Add(Heading(title));
        panel.Children.Add(Body(message));
    }
}
