// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

/// <summary>
/// The audit meets as many specific requirements as any matching of courses, checked against an
/// exhaustive search written separately from the engine.
/// </summary>
public class DegreeAuditTests
{
    readonly record struct PoolCourse(CourseKey Key, double Credits);

    static readonly (string Code, double Credits)[] Catalog =
    [
        ("CS 1200", 1), ("CS 1800", 4), ("CS 1802", 1), ("CS 2500", 4), ("CS 2501", 1), ("CS 2510", 4),
        ("CS 3000", 4), ("CS 3200", 4), ("CS 3500", 4), ("CS 3501", 1), ("CS 3650", 4), ("CS 4100", 4),
        ("CS 4400", 4), ("CS 4530", 4), ("DS 3000", 4), ("DS 4400", 4), ("MATH 1341", 4), ("MATH 2331", 4),
    ];

    static readonly PoolCourse[] Pool = Catalog
        .Select(item => new PoolCourse(CourseKey.Parse(item.Code) ?? throw new InvalidOperationException(item.Code), item.Credits))
        .ToArray();

    [Fact]
    public void MatchesAsManyRequirementsAsPossible()
    {
        var generator = new SeededGenerator(2026);
        var checkedCount = 0;
        for (var i = 0; i < 160; i++)
        {
            var plan = MakePlan(generator);
            var rules = RandomRules(plan.Courses, generator);
            var pins = RandomPins(rules, plan.Courses, generator);
            plan.DegreePlan.Substitutions = pins.Select(pin => new RequirementSubstitution
            {
                RuleID = rules[pin.Rule].Id,
                RuleTitle = rules[pin.Rule].Id,
                Change = SubstitutionChange.CountCourse(plan.Courses[pin.Course].Key),
            }).ToList();
            var requirements = Requirements(rules);
            var audit = DegreeAudit.Run(plan.DegreePlan, requirements);
            var possible = MostThatCanBeMet(rules, plan.Courses, pins);
            var context = Describe(rules, plan.Courses) + "; pins: " +
                string.Join(", ", pins.Select(pin => $"{plan.Courses[pin.Course].Key}\u2192{rules[pin.Rule].Id}"));
            var specificMet = audit.RuleResults.Count(result => !result.Rule.DegreeIsGeneralElectives() && result.Status.IsSatisfied());
            Assert.True(specificMet == possible.Specific, $"{context}: specific {specificMet} != {possible.Specific}");
            if (possible.Everything)
                Assert.True(audit.SatisfiedCount == audit.TotalCount, context);
            var counted = audit.RuleResults.SelectMany(result => result.Used).Select(use => use.Id).ToList();
            Assert.Equal(counted.Count, counted.ToHashSet().Count);
            checkedCount++;
        }
        Assert.Equal(160, checkedCount);
    }

    [Fact]
    public void LargePlansStayFast()
    {
        var rules = new List<RequirementRule>();
        var cs = Pool.Where(course => course.Key.Subject == "CS" && course.Credits >= 4).Select(course => course.Key).ToList();
        for (var index = 0; index < 6; index++)
        {
            var options = Shuffled(cs, (ulong)index).Take(5)
                .Select(key => new CourseOption([[key]]))
                .ToList();
            rules.Add(Rule($"list-{index}", new RequirementKind.Choose(2, null, options, [])));
        }
        rules.Add(Rule("electives", new RequirementKind.Choose(null, 12, [], [new CourseRange("CS", 2000, null, [], "CS 2000 or higher")])));
        rules.Add(Rule("petition", new RequirementKind.Choose(3, null, [], [new CourseRange("CS", 0, null, [], "Any CS course")])));
        rules.Add(Rule("general", new RequirementKind.GeneralElectives(16)));
        var plan = new DegreePlan
        {
            Name = "Big",
            Completed = Pool.Select(course => new DegreePlanCourse(course.Key, "", course.Credits)).ToList(),
        };
        var start = DateTime.UtcNow;
        var audit = DegreeAudit.Run(plan, Requirements(rules));
        Assert.True((DateTime.UtcNow - start).TotalSeconds < 2);
        Assert.Equal(rules.Count, audit.TotalCount);
    }

    sealed class GeneratedPlan
    {
        public required List<PoolCourse> Courses { get; init; }
        public required DegreePlan DegreePlan { get; init; }
    }

    static GeneratedPlan MakePlan(SeededGenerator generator)
    {
        var count = generator.NextInt(3, 6);
        var courses = generator.Shuffled(Pool).Take(count).ToList();
        var plan = new DegreePlan { Name = "Random" };
        var term = new DegreePlanTerm { Season = PlanSeason.Spring, Year = 2027 };
        foreach (var course in courses)
        {
            var line = new DegreePlanCourse(course.Key, "", course.Credits);
            if (generator.NextBool()) plan.Completed.Add(line);
            else term.Courses.Add(line);
        }
        plan.Terms = [term];
        return new GeneratedPlan { Courses = courses, DegreePlan = plan };
    }

    static List<RequirementRule> RandomRules(List<PoolCourse> courses, SeededGenerator generator)
    {
        var keys = courses.Select(course => course.Key).ToList();
        CourseKey SomeKey() => generator.NextInt(0, 4) == 0
            ? generator.Element(Pool).Key
            : generator.Element(keys);

        var rules = new List<RequirementRule>();
        var total = generator.NextInt(2, 4);
        var withElectives = generator.NextBool();
        var ruleCount = withElectives ? total - 1 : total;
        for (var index = 0; index < ruleCount; index++)
        {
            RequirementKind kind = generator.NextInt(0, 7) switch
            {
                0 => new RequirementKind.Required(new CourseOption([[SomeKey()]])),
                1 => new RequirementKind.Required(new CourseOption([Distinct([SomeKey(), SomeKey()])])),
                2 => new RequirementKind.Required(new CourseOption([[SomeKey()], [SomeKey()]])),
                3 => ChooseList(generator, SomeKey),
                4 => CountRange(generator),
                5 => new RequirementKind.Choose(null, generator.Element([5.0, 8.0]), [],
                [
                    new CourseRange("CS", 2000, null, [], "CS 2000 or higher"),
                    new CourseRange("DS", 2000, null, [], "DS 2000 or higher"),
                ]),
                6 => PairList(generator, SomeKey),
                _ => Petition(generator, SomeKey),
            };
            rules.Add(Rule($"rule-{index}", kind));
        }
        if (withElectives)
            rules.Add(Rule("general", new RequirementKind.GeneralElectives(generator.Element([4.0, 8.0]))));
        return rules;
    }

    static RequirementKind ChooseList(SeededGenerator generator, Func<CourseKey> someKey)
    {
        var options = Distinct(Enumerable.Range(0, generator.NextInt(2, 4)).Select(_ => someKey()).ToList());
        return new RequirementKind.Choose(
            generator.NextInt(1, 2),
            null,
            options.Select(key => new CourseOption([[key]])).ToList(),
            []);
    }

    static RequirementKind CountRange(SeededGenerator generator)
    {
        var lowest = generator.Element([2000, 3000]);
        return new RequirementKind.Choose(
            generator.NextInt(1, 2),
            null,
            [],
            [new CourseRange("CS", lowest, null, [], $"CS {lowest} or higher")]);
    }

    static RequirementKind PairList(SeededGenerator generator, Func<CourseKey> someKey)
    {
        var pair = Distinct([someKey(), someKey()]);
        var singles = Distinct(Enumerable.Range(0, generator.NextInt(1, 2)).Select(_ => someKey()).ToList())
            .Where(key => !pair.Contains(key))
            .ToList();
        var options = new List<CourseOption> { new([pair]) };
        options.AddRange(singles.Select(key => new CourseOption([[key]])));
        return new RequirementKind.Choose(generator.NextInt(1, 2), null, options, []);
    }

    static RequirementKind Petition(SeededGenerator generator, Func<CourseKey> someKey)
    {
        var approved = new CourseOption([[someKey()]]);
        var ranges = new List<CourseRange> { new("CS", 2500, null, [], "CS 2500 or higher") };
        return generator.NextBool()
            ? new RequirementKind.Choose(generator.NextInt(1, 2), null, [approved], ranges)
            : new RequirementKind.Choose(null, generator.Element([4.0, 8.0]), [approved], ranges);
    }

    static List<CourseKey> Distinct(List<CourseKey> keys)
    {
        var seen = new HashSet<CourseKey>();
        return keys.Where(key => seen.Add(key)).ToList();
    }

    static List<(int Course, int Rule)> RandomPins(List<RequirementRule> rules, List<PoolCourse> courses, SeededGenerator generator)
    {
        if (generator.NextInt(0, 9) >= 3) return [];
        return [(generator.NextInt(0, courses.Count - 1), generator.NextInt(0, rules.Count - 1))];
    }

    static RequirementRule Rule(string id, RequirementKind kind) => new()
    {
        Id = id,
        Kind = kind,
        Label = id,
    };

    static ProgramRequirements Requirements(List<RequirementRule> rules) => new()
    {
        Year = new CatalogYear(2024),
        Program = new CatalogProgram("Random, BS", "undergraduate/random/"),
        Sections =
        [
            new RequirementSection
            {
                Id = "major",
                Title = "Major",
                Groups = rules.Select(rule => new RequirementGroup
                {
                    Id = "group-" + rule.Id,
                    Title = rule.Id,
                    Rules = [rule],
                }).ToList(),
            },
        ],
        FetchedAt = DateTimeOffset.UtcNow,
    };

    static (int Specific, bool Everything) MostThatCanBeMet(
        List<RequirementRule> rules,
        List<PoolCourse> courses,
        List<(int Course, int Rule)> pins)
    {
        var assignment = Enumerable.Repeat(rules.Count, courses.Count).ToArray();
        var pinned = new Dictionary<int, int>();
        foreach (var pin in pins) pinned.TryAdd(pin.Course, pin.Rule);
        var bestSpecific = 0;
        var everything = false;

        bool IsMet(int ruleIndex)
        {
            var rule = rules[ruleIndex];
            var mine = courses.Select((course, index) => (course, index))
                .Where(item => assignment[item.index] == ruleIndex && !pinned.ContainsKey(item.index))
                .Select(item => item.course)
                .ToList();
            var placed = courses.Select((course, index) => (course, index))
                .Where(item => pinned.TryGetValue(item.index, out var rule) && rule == ruleIndex)
                .Select(item => item.course)
                .ToList();
            var keys = mine.Select(course => course.Key).ToHashSet();
            var placedKeys = placed.Select(course => course.Key).ToHashSet();
            bool Complete(IReadOnlyList<CourseKey> alternative) => alternative.All(keys.Contains);
            switch (rule.Kind)
            {
                case RequirementKind.Required required:
                    return placed.Count > 0 || required.Option.Alternatives.Any(Complete);
                case RequirementKind.Choose choose:
                    var open = choose.Options.Where(option => option.AllCourses.All(key => !placedKeys.Contains(key))).ToList();
                    var listed = open.SelectMany(option => option.AllCourses).ToHashSet();
                    var optionUnits = open.Count(option => option.Alternatives.Any(Complete));
                    var inRanges = mine.Where(course => !listed.Contains(course.Key) && choose.Ranges.Any(range => range.Contains(course.Key))).ToList();
                    if (choose.Count is int count)
                        return placed.Count + optionUnits + inRanges.Count(course => course.Credits >= 2) >= count;
                    if (choose.Hours is double hours)
                    {
                        var fromOptions = mine.Where(course => open.Any(option =>
                            option.AllCourses.Contains(course.Key) && option.Alternatives.Any(Complete))).ToList();
                        return placed.Concat(fromOptions).Concat(inRanges).Sum(course => course.Credits) >= hours;
                    }
                    return placed.Count + optionUnits >= 1;
                case RequirementKind.GeneralElectives general:
                    return mine.Concat(placed).Sum(course => course.Credits) >= general.Hours;
                default:
                    return false;
            }
        }

        void Assign(int position)
        {
            if (position >= courses.Count)
            {
                var met = Enumerable.Range(0, rules.Count).Where(IsMet).ToList();
                var specific = met.Count(index => !rules[index].DegreeIsGeneralElectives());
                bestSpecific = Math.Max(bestSpecific, specific);
                if (met.Count == rules.Count) everything = true;
                return;
            }
            if (pinned.TryGetValue(position, out var pinnedRule))
            {
                assignment[position] = pinnedRule;
                Assign(position + 1);
                return;
            }
            for (var bucket = 0; bucket <= rules.Count; bucket++)
            {
                assignment[position] = bucket;
                Assign(position + 1);
            }
        }

        Assign(0);
        return (bestSpecific, everything);
    }

    static string Describe(List<RequirementRule> rules, List<PoolCourse> courses)
    {
        var plan = string.Join(", ", courses.Select(course => $"{course.Key} ({course.Credits})"));
        var listed = rules.Select(rule => rule.Kind switch
        {
            RequirementKind.Required required => "required " + required.Option.Label,
            RequirementKind.Choose choose =>
                $"choose {choose.Count?.ToString() ?? "-"}/{choose.Hours?.ToString() ?? "-"} of {string.Join(", ", choose.Options.Select(option => option.Label))} {string.Join(", ", choose.Ranges.Select(range => range.Label))}",
            RequirementKind.GeneralElectives general => "general " + general.Hours,
            _ => rule.Id,
        });
        return "plan: " + plan + "; rules: " + string.Join("; ", listed);
    }

    static List<T> Shuffled<T>(IReadOnlyList<T> items, ulong seed) => new SeededGenerator(seed).Shuffled(items);

    /// <summary>SplitMix64, used only so one run of the property test is reproducible.</summary>
    sealed class SeededGenerator
    {
        ulong _state;
        public SeededGenerator(ulong seed) => _state = seed;

        public ulong Next()
        {
            _state += 0x9E3779B97F4A7C15;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }

        public int NextInt(int minInclusive, int maxInclusive)
        {
            var span = (uint)(maxInclusive - minInclusive + 1);
            return minInclusive + (int)(Next() % span);
        }

        public bool NextBool() => (Next() & 1) == 1;

        public T Element<T>(IReadOnlyList<T> items) => items[(int)(Next() % (ulong)items.Count)];

        public List<T> Shuffled<T>(IReadOnlyList<T> items)
        {
            var list = items.ToList();
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = (int)(Next() % (ulong)(i + 1));
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }
    }
}
