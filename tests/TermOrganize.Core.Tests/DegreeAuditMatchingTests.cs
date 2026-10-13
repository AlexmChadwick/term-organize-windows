// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core.Tests;

/// <summary>
/// Whenever some matching of courses to requirements meets every requirement, the audit meets
/// every requirement, and it meets as many specific requirements as any matching. Ported from
/// DegreeAuditMatchingTests.swift. The random plans use a fixed seed; the exhaustive search is
/// written separately from the engine.
/// </summary>
public class DegreeAuditMatchingTests
{
    private readonly record struct PoolCourse(CourseKey Key, double Credits);

    private static readonly PoolCourse[] Pool =
    [
        Course("CS 1200", 1), Course("CS 1800", 4), Course("CS 1802", 1), Course("CS 2500", 4), Course("CS 2501", 1),
        Course("CS 2510", 4), Course("CS 3000", 4), Course("CS 3200", 4), Course("CS 3500", 4), Course("CS 3501", 1),
        Course("CS 3650", 4), Course("CS 4100", 4), Course("CS 4400", 4), Course("CS 4530", 4), Course("DS 3000", 4),
        Course("DS 4400", 4), Course("MATH 1341", 4), Course("MATH 2331", 4),
    ];

    [Fact]
    public void MatchesAsManyRequirementsAsPossible()
    {
        var random = new Random(2026);
        var checkedPlans = 0;
        for (var trial = 0; trial < 160; trial++)
        {
            var (courses, plan) = RandomPlan(random);
            var rules = RandomRules(courses, random);
            var pins = RandomPins(rules.Count, courses.Count, random);
            plan.Substitutions = pins.Select(pin => new RequirementSubstitution
            {
                RuleID = rules[pin.Rule].Id,
                RuleTitle = rules[pin.Rule].Id,
                Change = SubstitutionChange.CountCourse(courses[pin.Course].Key),
            }).ToList();
            var audit = DegreeAudit.Run(plan, Requirements(rules));
            var possible = MostThatCanBeMet(rules, courses, pins);
            var specificMet = audit.RuleResults.Count(result => !result.Rule.DegreeIsGeneralElectives() && result.Status.IsSatisfied());
            Assert.Equal(possible.Specific, specificMet);
            if (possible.Everything) Assert.Equal(audit.TotalCount, audit.SatisfiedCount);
            var counted = audit.RuleResults.SelectMany(result => result.Used.Select(use => use.Id)).ToList();
            Assert.Equal(counted.Count, counted.Distinct(StringComparer.Ordinal).Count());
            checkedPlans++;
        }
        Assert.Equal(160, checkedPlans);
    }

    [Fact]
    public void LargePlansStayFast()
    {
        var rules = new List<RequirementRule>();
        var lectures = Pool.Where(course => course.Key.Subject == "CS" && course.Credits >= 4).Select(course => course.Key).ToList();
        for (var index = 0; index < 6; index++)
        {
            var options = Shuffle(lectures, new Random(index)).Take(5)
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

    private static (List<PoolCourse> Courses, DegreePlan Plan) RandomPlan(Random random)
    {
        var courses = Shuffle(Pool, random).Take(random.Next(3, 7)).ToList();
        var plan = new DegreePlan { Name = "Random" };
        var term = new DegreePlanTerm { Season = PlanSeason.Spring, Year = 2027 };
        foreach (var course in courses)
        {
            var line = new DegreePlanCourse(course.Key, "", course.Credits);
            if (random.Next(2) == 0) plan.Completed.Add(line);
            else term.Courses.Add(line);
        }
        plan.Terms = [term];
        return (courses, plan);
    }

    private static List<RequirementRule> RandomRules(IReadOnlyList<PoolCourse> courses, Random random)
    {
        var keys = courses.Select(course => course.Key).ToList();
        CourseKey SomeKey() => random.Next(5) == 0 ? Pool[random.Next(Pool.Length)].Key : keys[random.Next(keys.Count)];
        var rules = new List<RequirementRule>();
        var total = random.Next(2, 5);
        var withElectives = random.Next(2) == 0;
        var specific = withElectives ? total - 1 : total;
        for (var index = 0; index < specific; index++)
        {
            RequirementKind kind = random.Next(8) switch
            {
                0 => new RequirementKind.Required(new CourseOption([[SomeKey()]])),
                1 => new RequirementKind.Required(new CourseOption([Distinct([SomeKey(), SomeKey()])])),
                2 => new RequirementKind.Required(new CourseOption([[SomeKey()], [SomeKey()]])),
                3 => ChooseList(random, SomeKey),
                4 => RangeCount(random),
                5 => new RequirementKind.Choose(null, random.Next(2) == 0 ? 5 : 8, [],
                [
                    new CourseRange("CS", 2000, null, [], "CS 2000 or higher"),
                    new CourseRange("DS", 2000, null, [], "DS 2000 or higher"),
                ]),
                6 => LectureAndLab(random, SomeKey),
                _ => Petition(random, SomeKey),
            };
            rules.Add(Rule($"rule-{index}", kind));
        }
        if (withElectives) rules.Add(Rule("general", new RequirementKind.GeneralElectives(random.Next(2) == 0 ? 4 : 8)));
        return rules;
    }

    private static RequirementKind ChooseList(Random random, Func<CourseKey> someKey)
    {
        var options = Distinct(Enumerable.Range(0, random.Next(2, 5)).Select(_ => someKey()).ToList())
            .Select(key => new CourseOption([[key]])).ToList();
        return new RequirementKind.Choose(random.Next(1, 3), null, options, []);
    }

    private static RequirementKind RangeCount(Random random)
    {
        var lowest = random.Next(2) == 0 ? 2000 : 3000;
        return new RequirementKind.Choose(random.Next(1, 3), null, [], [new CourseRange("CS", lowest, null, [], $"CS {lowest} or higher")]);
    }

    private static RequirementKind LectureAndLab(Random random, Func<CourseKey> someKey)
    {
        var pair = Distinct([someKey(), someKey()]);
        var singles = Distinct(Enumerable.Range(0, random.Next(1, 3)).Select(_ => someKey()).ToList())
            .Where(key => !pair.Contains(key))
            .Select(key => new CourseOption([[key]]));
        var options = new List<CourseOption> { new([pair]) };
        options.AddRange(singles);
        return new RequirementKind.Choose(random.Next(1, 3), null, options, []);
    }

    private static RequirementKind Petition(Random random, Func<CourseKey> someKey)
    {
        var approved = new CourseOption([[someKey()]]);
        var ranges = new List<CourseRange> { new("CS", 2500, null, [], "CS 2500 or higher") };
        return random.Next(2) == 0
            ? new RequirementKind.Choose(random.Next(1, 3), null, [approved], ranges)
            : new RequirementKind.Choose(null, random.Next(2) == 0 ? 4 : 8, [approved], ranges);
    }

    private static List<(int Course, int Rule)> RandomPins(int ruleCount, int courseCount, Random random)
    {
        if (random.Next(10) >= 3) return [];
        return [(random.Next(courseCount), random.Next(ruleCount))];
    }

    private static RequirementRule Rule(string id, RequirementKind kind) => new()
    {
        Id = id,
        Kind = kind,
        Label = id,
    };

    private static ProgramRequirements Requirements(IReadOnlyList<RequirementRule> rules) => new()
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
    };

    private static (int Specific, bool Everything) MostThatCanBeMet(
        IReadOnlyList<RequirementRule> rules,
        IReadOnlyList<PoolCourse> courses,
        IReadOnlyList<(int Course, int Rule)> pins)
    {
        var assignment = new int[courses.Count];
        var pinned = pins.GroupBy(pin => pin.Course).ToDictionary(group => group.Key, group => group.First().Rule);
        var bestSpecific = 0;
        var everything = false;

        bool IsMet(int ruleIndex)
        {
            var rule = rules[ruleIndex];
            var mine = courses.Select((course, index) => (course, index))
                .Where(item => assignment[item.index] == ruleIndex && !pinned.ContainsKey(item.index))
                .Select(item => item.course).ToList();
            var placed = courses.Select((course, index) => (course, index))
                .Where(item => pinned.TryGetValue(item.index, out var rule) && rule == ruleIndex)
                .Select(item => item.course).ToList();
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
                var specific = met.Count(index => rules[index].Kind is not RequirementKind.GeneralElectives);
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

    private static List<CourseKey> Distinct(IReadOnlyList<CourseKey> keys)
    {
        var seen = new HashSet<CourseKey>();
        return keys.Where(seen.Add).ToList();
    }

    private static List<T> Shuffle<T>(IReadOnlyList<T> items, Random random)
    {
        var copy = items.ToList();
        for (var i = copy.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }

    private static PoolCourse Course(string code, double credits) => new(CourseKey.Parse(code)!.Value, credits);
}
