// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermOrganize.Core;

namespace TermOrganize;

/// <summary>The course page's prerequisite and corequisite trees.</summary>
internal static class RequisiteTreeView
{
    public static UIElement? Build(Requisite? requisite, AppSession session, string heading)
    {
        if (requisite is null || session.Index is null) return null;
        var node = RequisiteNode.Create(
            requisite,
            key => session.Index.Course(key) is not null,
            key => session.Store.CompletionFor(key));
        if (node is null) return null;
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = heading, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Node(node, 0));
        return panel;
    }

    private static FrameworkElement Node(RequisiteNode node, int depth)
    {
        switch (node)
        {
            case RequisiteNode.ItemNode item:
                return new TextBlock
                {
                    Text = item.Item.SpokenText,
                    Opacity = item.Item.IsGreyedOut ? 0.45 : 1,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(depth * 16, 0, 0, 0),
                };
            case RequisiteNode.AllNode all:
                return Group("All of these", all.Members, depth);
            case RequisiteNode.OneOfNode one:
                return Group("One of these", one.Members, depth);
            default:
                return new TextBlock();
        }
    }

    private static StackPanel Group(string title, IReadOnlyList<RequisiteNode> members, int depth)
    {
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(depth * 16, 0, 0, 0) };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        foreach (var member in members) panel.Children.Add(Node(member, 1));
        return panel;
    }
}
