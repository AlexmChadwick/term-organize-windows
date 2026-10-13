// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// How a prerequisite or corequisite is laid out on the course page.
//
// Banner stores requisites as nested "and" / "or" trees. Groups that need no
// nesting are flattened, a group of plain courses is one flat group, an "and"
// that mixes courses with "one of" choices is a checklist, and an "or" with
// compound branches is a set of numbered options. Never more than two levels:
// anything deeper is an inline expression. This also writes the sentence a
// screen reader reads.

using System.Globalization;

namespace TermOrganize.Core;

public static class RequisiteDisplay
{
    /// <summary>
    /// The requisite as the course page reads it. A group of one becomes that one,
    /// an "and" inside an "and" (or an "or" inside an "or") merges, and a repeated
    /// alternative is kept once. Null when nothing is left.
    /// </summary>
    public static Requisite? NormalizedForDisplay(this Requisite requisite) => requisite switch
    {
        Requisite.CourseNode or Requisite.TestNode => requisite,
        Requisite.AndNode andNode => Merging(andNode.Items, asAnd: true),
        Requisite.OrNode orNode => Merging(orNode.Items, asAnd: false),
        _ => requisite,
    };

    private static Requisite? Merging(IReadOnlyList<Requisite> items, bool asAnd)
    {
        var members = new List<Requisite>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(Requisite member)
        {
            if (seen.Add(DisplayKey(member))) members.Add(member);
        }
        foreach (var item in items)
        {
            var member = item.NormalizedForDisplay();
            if (member is null) continue;
            if (asAnd && member is Requisite.AndNode innerAnd)
            {
                foreach (var child in innerAnd.Items) Add(child);
            }
            else if (!asAnd && member is Requisite.OrNode innerOr)
            {
                foreach (var child in innerOr.Items) Add(child);
            }
            else Add(member);
        }
        return members.Count switch
        {
            0 => null,
            1 => members[0],
            _ => asAnd ? Requisite.And(members) : Requisite.Or(members),
        };
    }

    private static string DisplayKey(Requisite requisite) => requisite switch
    {
        Requisite.CourseNode course => $"c:{course.Key}",
        Requisite.TestNode test => $"t:{test.Name}:{ScoreKey(test.Score)}",
        Requisite.AndNode and => "and(" + string.Join(",", and.Items.Select(DisplayKey).OrderBy(key => key, StringComparer.Ordinal)) + ")",
        Requisite.OrNode or => "or(" + string.Join(",", or.Items.Select(DisplayKey).OrderBy(key => key, StringComparer.Ordinal)) + ")",
        _ => "",
    };

    private static string ScoreKey(double? score) =>
        score is double value ? value.ToString("G17", CultureInfo.InvariantCulture) : "";
}

/// <summary>A course or a placement test, with what the card needs to draw it.</summary>
public sealed class RequisiteItem : IEquatable<RequisiteItem>
{
    public abstract record Kind
    {
        public sealed record Course(CourseKey Key) : Kind;
        public sealed record Test(string Name, double? Score) : Kind;
    }

    public required Kind ItemKind { get; init; }
    public bool IsComplete { get; init; }
    /// <summary>The course that counted when it was not this one. Only set on a completed course.</summary>
    public CourseKey? Via { get; init; }
    public bool IsOffered { get; init; } = true;

    public string Id => ItemKind switch
    {
        Kind.Course course => $"c:{course.Key}",
        Kind.Test test => $"t:{test.Name}:{(test.Score is double score ? score.ToString("G17", CultureInfo.InvariantCulture) : "")}",
        _ => "",
    };

    public CourseKey? CourseKey => ItemKind is Kind.Course course ? course.Key : null;

    /// <summary>A course this term's catalog doesn't list and the user hasn't completed.</summary>
    public bool IsGreyedOut => CourseKey is not null && !IsOffered && !IsComplete;

    /// <summary>
    /// A placement test's minimum score, when it adds anything. Banner sometimes
    /// repeats a course number as the score ("Placement in SPNS 1101", 1101).
    /// </summary>
    public string? ScoreText
    {
        get
        {
            if (ItemKind is not Kind.Test test || test.Score is not double score) return null;
            var text = FormatScore(score);
            return test.Name.Contains(text, StringComparison.Ordinal) ? null : text;
        }
    }

    public string SpokenText
    {
        get
        {
            switch (ItemKind)
            {
                case Kind.Course course:
                    if (!IsComplete) return course.Key.ToString();
                    return Via is CourseKey via ? $"{course.Key} (completed via {via})" : $"{course.Key} (completed)";
                case Kind.Test test:
                    return ScoreText is string score ? $"{test.Name} (minimum score {score})" : test.Name;
                default:
                    return "";
            }
        }
    }

    public bool Equals(RequisiteItem? other) =>
        other is not null && Id == other.Id && IsComplete == other.IsComplete && Via == other.Via && IsOffered == other.IsOffered;

    public override bool Equals(object? obj) => Equals(obj as RequisiteItem);
    public override int GetHashCode() => HashCode.Combine(Id, IsComplete, Via, IsOffered);

    internal static string FormatScore(double score)
    {
        var rounded = Math.Round(score, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded - Math.Round(rounded)) < 0.0000001)
            return Math.Round(rounded).ToString("0", CultureInfo.InvariantCulture);
        return rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

/// <summary>A normalized requisite: courses and tests, "all of" groups and "one of" groups.</summary>
public abstract class RequisiteNode
{
    public sealed class ItemNode : RequisiteNode
    {
        public required RequisiteItem Item { get; init; }
    }

    public sealed class AllNode : RequisiteNode
    {
        public required IReadOnlyList<RequisiteNode> Members { get; init; }
    }

    public sealed class OneOfNode : RequisiteNode
    {
        public required IReadOnlyList<RequisiteNode> Members { get; init; }
    }

    public static RequisiteNode? Create(
        Requisite requisite,
        Func<CourseKey, bool> isOffered,
        Func<CourseKey, (bool IsComplete, CourseKey? Via)> completion)
    {
        var tree = requisite.NormalizedForDisplay();
        return tree is null ? null : Make(tree, isOffered, completion);
    }

    private static RequisiteNode Make(
        Requisite requisite,
        Func<CourseKey, bool> isOffered,
        Func<CourseKey, (bool IsComplete, CourseKey? Via)> completion) => requisite switch
    {
        Requisite.CourseNode course => ItemFrom(course.Key, isOffered, completion),
        Requisite.TestNode test => new ItemNode { Item = new RequisiteItem { ItemKind = new RequisiteItem.Kind.Test(test.Name, test.Score) } },
        Requisite.AndNode and => new AllNode { Members = and.Items.Select(item => Make(item, isOffered, completion)).ToList() },
        Requisite.OrNode or => new OneOfNode { Members = or.Items.Select(item => Make(item, isOffered, completion)).ToList() },
        _ => throw new InvalidOperationException("Unknown requisite."),
    };

    private static ItemNode ItemFrom(
        CourseKey key,
        Func<CourseKey, bool> isOffered,
        Func<CourseKey, (bool IsComplete, CourseKey? Via)> completion)
    {
        var status = completion(key);
        return new ItemNode
        {
            Item = new RequisiteItem
            {
                ItemKind = new RequisiteItem.Kind.Course(key),
                IsComplete = status.IsComplete,
                Via = status.IsComplete ? status.Via : null,
                IsOffered = isOffered(key),
            },
        };
    }

    public List<RequisiteItem> AllItems() => this switch
    {
        ItemNode item => [item.Item],
        AllNode all => all.Members.SelectMany(member => member.AllItems()).ToList(),
        OneOfNode one => one.Members.SelectMany(member => member.AllItems()).ToList(),
        _ => [],
    };

    public bool IsDone => this switch
    {
        ItemNode item => item.Item.IsComplete,
        AllNode all => all.Members.All(member => member.IsDone),
        OneOfNode one => one.Members.Any(member => member.IsDone),
        _ => false,
    };

    /// <summary>For a "one of" done through a single course, that course. Null when a compound alternative did it.</summary>
    public CourseKey? DoneWith
    {
        get
        {
            if (this is not OneOfNode one) return null;
            var done = one.Members.FirstOrDefault(member => member.IsDone);
            if (done is not ItemNode item) return null;
            return item.Item.Via ?? item.Item.CourseKey;
        }
    }

    public string SpokenExpression => this switch
    {
        ItemNode item => item.Item.SpokenText,
        AllNode all => SpeakAll(all.Members.Select(member => member.SpokenExpression).ToList()),
        OneOfNode one => "either " + string.Join(", or ", one.Members.Select(member => member.SpokenExpression)),
        _ => "",
    };

    internal static string SpeakAll(IReadOnlyList<string> spoken) => spoken.Count == 2
        ? $"both {spoken[0]} and {spoken[1]}"
        : "all of " + SpokenList(spoken, "and");

    internal static string SpokenList(IReadOnlyList<string> names, string joining)
    {
        if (names.Count == 0) return "";
        if (names.Count == 1) return names[0];
        return string.Join(", ", names.Take(names.Count - 1)) + $" {joining} " + names[^1];
    }
}

/// <summary>Two or more plain courses or tests, all of them or any one of them.</summary>
public sealed class RequisiteGroup
{
    public enum Kind
    {
        All,
        OneOf,
    }

    public Kind GroupKind { get; init; }
    public required IReadOnlyList<RequisiteItem> Items { get; init; }

    public bool IsDone => GroupKind switch
    {
        Kind.All => Items.All(item => item.IsComplete),
        Kind.OneOf => Items.Any(item => item.IsComplete),
        _ => false,
    };

    /// <summary>For a "one of" group that is done, the course that did it.</summary>
    public CourseKey? DoneWith
    {
        get
        {
            if (GroupKind != Kind.OneOf) return null;
            var done = Items.FirstOrDefault(item => item.IsComplete);
            if (done is null) return null;
            return done.Via ?? done.CourseKey;
        }
    }

    public string SpokenText
    {
        get
        {
            var names = Items.Select(item => item.SpokenText).ToList();
            return GroupKind switch
            {
                Kind.OneOf => "one of " + RequisiteNode.SpokenList(names, "or"),
                Kind.All => RequisiteNode.SpeakAll(names),
                _ => "",
            };
        }
    }
}

/// <summary>One requirement inside a checklist or an option.</summary>
public abstract class RequisitePart
{
    public sealed class ItemPart : RequisitePart
    {
        public required RequisiteItem Item { get; init; }
    }

    public sealed class GroupPart : RequisitePart
    {
        public required RequisiteGroup Group { get; init; }
    }

    public sealed class ExpressionPart : RequisitePart
    {
        public required RequisiteNode Expression { get; init; }
    }

    public bool IsDone => this switch
    {
        ItemPart item => item.Item.IsComplete,
        GroupPart group => group.Group.IsDone,
        ExpressionPart expression => expression.Expression.IsDone,
        _ => false,
    };

    public bool IsExpression => this is ExpressionPart;

    public string SpokenText => this switch
    {
        ItemPart item => item.Item.SpokenText,
        GroupPart group => group.Group.SpokenText,
        ExpressionPart expression => expression.Expression.SpokenExpression,
        _ => "",
    };
}

/// <summary>An "and" that mixes courses with "one of" choices: every part is needed.</summary>
public sealed class RequisiteChecklist
{
    public int RequirementCount { get; }
    public IReadOnlyList<RequisitePart> Parts { get; }
    public bool IsDone => Parts.All(part => part.IsDone);

    public RequisiteChecklist(IReadOnlyList<RequisiteNode> members)
    {
        var flat = Flatten(members, oneOf: false);
        var parts = new List<RequisitePart>();
        int? looseIndex = null;
        var loose = new List<RequisiteItem>();
        foreach (var member in flat)
        {
            switch (member)
            {
                case RequisiteNode.ItemNode item:
                    if (looseIndex is null)
                    {
                        looseIndex = parts.Count;
                        parts.Add(new RequisitePart.ItemPart { Item = item.Item });
                    }
                    loose.Add(item.Item);
                    break;
                case RequisiteNode.OneOfNode one:
                    if (ItemMembers(one.Members) is List<RequisiteItem> items)
                        parts.Add(new RequisitePart.GroupPart { Group = new RequisiteGroup { GroupKind = RequisiteGroup.Kind.OneOf, Items = InChoiceOrder(items) } });
                    else
                        parts.Add(new RequisitePart.ExpressionPart { Expression = member });
                    break;
                default:
                    parts.Add(new RequisitePart.ExpressionPart { Expression = member });
                    break;
            }
        }
        if (looseIndex is int index)
        {
            parts[index] = loose.Count == 1
                ? new RequisitePart.ItemPart { Item = loose[0] }
                : new RequisitePart.GroupPart { Group = new RequisiteGroup { GroupKind = RequisiteGroup.Kind.All, Items = loose } };
        }
        RequirementCount = flat.Count;
        Parts = parts;
    }

    public bool HasGreyedOutChips => Parts.Any(part => part switch
    {
        RequisitePart.GroupPart group => group.Group.Items.Any(item => item.IsGreyedOut),
        RequisitePart.ExpressionPart expression => expression.Expression.AllItems().Any(item => item.IsGreyedOut),
        _ => false,
    });

    public string SpokenText
    {
        get
        {
            var separator = Parts.Any(part => part.IsExpression) ? "; and " : ", and ";
            return string.Join(separator, Parts.Select(part => part.SpokenText));
        }
    }

    internal static List<RequisiteNode> Flatten(IReadOnlyList<RequisiteNode> members, bool oneOf) =>
        members.SelectMany(member => member switch
        {
            RequisiteNode.OneOfNode inner when oneOf => Flatten(inner.Members, true),
            RequisiteNode.AllNode inner when !oneOf => Flatten(inner.Members, false),
            _ => (IEnumerable<RequisiteNode>)[member],
        }).ToList();

    internal static List<RequisiteItem>? ItemMembers(IReadOnlyList<RequisiteNode> members)
    {
        var items = new List<RequisiteItem>();
        foreach (var member in members)
        {
            if (member is not RequisiteNode.ItemNode item) return null;
            items.Add(item.Item);
        }
        return items;
    }

    internal static List<RequisiteItem> InChoiceOrder(IReadOnlyList<RequisiteItem> items)
    {
        static int Rank(RequisiteItem item)
        {
            if (item.IsComplete) return 0;
            return item.CourseKey is not null && item.IsOffered ? 1 : 2;
        }
        return items.Select((item, index) => (item, index))
            .OrderBy(pair => Rank(pair.item))
            .ThenBy(pair => pair.index)
            .Select(pair => pair.item)
            .ToList();
    }
}

/// <summary>One way to meet an "or" whose branches are not all plain courses.</summary>
public sealed class RequisitePath
{
    public abstract record Content
    {
        public sealed record ItemContent(RequisiteItem Item) : Content;
        public sealed record GroupContent(RequisiteGroup Group) : Content;
        public sealed record ChecklistContent(RequisiteChecklist Checklist) : Content;
    }

    public required Content Body { get; set; }

    public bool IsDone => Body switch
    {
        Content.ItemContent item => item.Item.IsComplete,
        Content.GroupContent group => group.Group.IsDone,
        Content.ChecklistContent checklist => checklist.Checklist.IsDone,
        _ => false,
    };

    public bool HasGreyedOutChips => Body switch
    {
        Content.GroupContent group => group.Group.Items.Any(item => item.IsGreyedOut),
        Content.ChecklistContent checklist => checklist.Checklist.HasGreyedOutChips,
        _ => false,
    };

    public string SpokenText => Body switch
    {
        Content.ItemContent item => item.Item.SpokenText,
        Content.GroupContent group => group.Group.SpokenText,
        Content.ChecklistContent checklist => checklist.Checklist.SpokenText,
        _ => "",
    };

    public static List<RequisitePath> Paths(IReadOnlyList<RequisiteNode> members)
    {
        var paths = new List<RequisitePath>();
        int? looseIndex = null;
        var loose = new List<RequisiteItem>();
        foreach (var member in RequisiteChecklist.Flatten(members, oneOf: true))
        {
            switch (member)
            {
                case RequisiteNode.ItemNode item:
                    if (looseIndex is null)
                    {
                        looseIndex = paths.Count;
                        paths.Add(new RequisitePath { Body = new Content.ItemContent(item.Item) });
                    }
                    loose.Add(item.Item);
                    break;
                case RequisiteNode.AllNode all:
                    if (RequisiteChecklist.ItemMembers(all.Members) is List<RequisiteItem> items)
                        paths.Add(new RequisitePath { Body = new Content.GroupContent(new RequisiteGroup { GroupKind = RequisiteGroup.Kind.All, Items = items }) });
                    else
                        paths.Add(new RequisitePath { Body = new Content.ChecklistContent(new RequisiteChecklist(all.Members)) });
                    break;
            }
        }
        if (looseIndex is int index)
        {
            paths[index].Body = loose.Count == 1
                ? new Content.ItemContent(loose[0])
                : new Content.GroupContent(new RequisiteGroup { GroupKind = RequisiteGroup.Kind.OneOf, Items = RequisiteChecklist.InChoiceOrder(loose) });
        }
        return paths;
    }
}

/// <summary>What the requirements card draws for a requisite.</summary>
public abstract class RequisiteLayout
{
    public sealed class ItemLayout : RequisiteLayout
    {
        public required RequisiteItem Item { get; init; }
    }

    public sealed class GroupLayout : RequisiteLayout
    {
        public required RequisiteGroup Group { get; init; }
    }

    public sealed class ChecklistLayout : RequisiteLayout
    {
        public required RequisiteChecklist Checklist { get; init; }
    }

    public sealed class PathsLayout : RequisiteLayout
    {
        public required IReadOnlyList<RequisitePath> Paths { get; init; }
    }

    public static RequisiteLayout? Create(
        Requisite requisite,
        Func<CourseKey, bool> isOffered,
        Func<CourseKey, (bool IsComplete, CourseKey? Via)> completion)
    {
        var node = RequisiteNode.Create(requisite, isOffered, completion);
        return node is null ? null : Create(node);
    }

    public static RequisiteLayout Create(RequisiteNode node) => node switch
    {
        RequisiteNode.ItemNode item => new ItemLayout { Item = item.Item },
        RequisiteNode.AllNode all => FromAll(RequisiteChecklist.Flatten(all.Members, oneOf: false)),
        RequisiteNode.OneOfNode one => FromOne(RequisiteChecklist.Flatten(one.Members, oneOf: true)),
        _ => throw new InvalidOperationException("Unknown requisite node."),
    };

    private static RequisiteLayout FromAll(IReadOnlyList<RequisiteNode> members)
    {
        if (RequisiteChecklist.ItemMembers(members) is List<RequisiteItem> items)
            return new GroupLayout { Group = new RequisiteGroup { GroupKind = RequisiteGroup.Kind.All, Items = items } };
        return new ChecklistLayout { Checklist = new RequisiteChecklist(members) };
    }

    private static RequisiteLayout FromOne(IReadOnlyList<RequisiteNode> members)
    {
        if (RequisiteChecklist.ItemMembers(members) is List<RequisiteItem> items)
            return new GroupLayout { Group = new RequisiteGroup { GroupKind = RequisiteGroup.Kind.OneOf, Items = RequisiteChecklist.InChoiceOrder(items) } };
        return new PathsLayout { Paths = RequisitePath.Paths(members) };
    }

    public bool IsDone => this switch
    {
        ItemLayout item => item.Item.IsComplete,
        GroupLayout group => group.Group.IsDone,
        ChecklistLayout checklist => checklist.Checklist.IsDone,
        PathsLayout paths => paths.Paths.Any(path => path.IsDone),
        _ => false,
    };

    /// <summary>Whether any chip is greyed out. A lone course is a row, which says so itself.</summary>
    public bool HasGreyedOutChips => this switch
    {
        GroupLayout group => group.Group.Items.Any(item => item.IsGreyedOut),
        ChecklistLayout checklist => checklist.Checklist.HasGreyedOutChips,
        PathsLayout paths => paths.Paths.Any(path => path.HasGreyedOutChips),
        _ => false,
    };

    public string SpokenDescription(string title)
    {
        var body = this switch
        {
            ItemLayout item => item.Item.SpokenText,
            GroupLayout group => group.Group.SpokenText,
            ChecklistLayout checklist => checklist.Checklist.SpokenText,
            PathsLayout paths => "either " + string.Join("; or ", paths.Paths.Select(path => path.SpokenText)),
            _ => "",
        };
        return $"{title}: {body}.";
    }
}
