// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;

namespace TermOrganize.Core;

/// <summary>
/// In which recent Banner terms courses had sections. A course is likely offered in a season
/// when it had sections in at least one checked term of that season. Fall, spring, and summer
/// are what matters; Summer 1, Summer 2, and the full summer term all count as summer.
/// A null <see cref="CheckedKeys"/> means every course was checked.
/// </summary>
public sealed class OfferingHistory
{
    public static OfferingHistory Empty { get; } = new();

    public IReadOnlyList<string> CheckedTerms { get; }
    public IReadOnlyDictionary<CourseKey, IReadOnlySet<string>> Offered { get; }
    public IReadOnlySet<CourseKey>? CheckedKeys { get; }

    private readonly Dictionary<CourseKey, HashSet<string>> _offered;

    public OfferingHistory(
        IEnumerable<string>? checkedTerms = null,
        IReadOnlyDictionary<CourseKey, IEnumerable<string>>? offered = null,
        IReadOnlyCollection<CourseKey>? checkedKeys = null)
    {
        CheckedTerms = checkedTerms?.ToList() ?? [];
        _offered = [];
        if (offered is not null)
        {
            foreach (var (key, codes) in offered)
                _offered[key] = codes.ToHashSet(StringComparer.Ordinal);
        }
        Offered = _offered.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value);
        CheckedKeys = checkedKeys is null ? null : checkedKeys.ToHashSet();
    }

    public bool IsChecked(CourseKey key) =>
        CheckedTerms.Count > 0 && (CheckedKeys is null || CheckedKeys.Contains(key));

    public List<string> OfferedTerms(CourseKey key)
    {
        if (!_offered.TryGetValue(key, out var terms)) return [];
        return CheckedTerms.Where(terms.Contains).ToList();
    }

    public HashSet<PlanSeason> Seasons(CourseKey key) =>
        OfferedTerms(key).Select(code => PlanSeasonInfo.FromTerm(SeasonOf(code))).ToHashSet();

    /// <summary>
    /// True when the course had sections in a checked term of the season, false when it had none,
    /// and null when that cannot be said.
    /// </summary>
    public bool? IsLikelyOffered(CourseKey key, PlanSeason season)
    {
        if (!IsChecked(key)) return null;
        var relevant = CheckedTerms.Where(code => season.Matches(SeasonOf(code))).ToList();
        if (relevant.Count == 0) return null;
        var terms = _offered.TryGetValue(key, out var offered) ? offered : [];
        return relevant.Any(terms.Contains);
    }

    public string Summary(CourseKey key)
    {
        if (!IsChecked(key)) return "Offering history unknown";
        var terms = OfferedTerms(key);
        if (terms.Count == 0)
        {
            var years = CheckedYears;
            return years == 1 ? "Not offered in the last year" : $"Not offered in the last {years} years";
        }
        var regular = Families.Where(family => IsRegular(family, key)).ToList();
        if (regular.Count == 0) return "Last offered " + Label(terms[0]);
        var others = Families.Where(family =>
            !regular.Contains(family) && terms.Any(code => FamilyOf(SeasonOf(code)) == family)).ToList();
        var regularly = "Offered every " + List(regular.Select(Name));
        return others.Count == 0
            ? regularly
            : regularly + ", and sometimes in the " + List(others.Select(Name));
    }

    /// <summary>
    /// <c>Offered every spring</c>, <c>Offered in Spring 2025</c>,
    /// <c>Not offered in a fall term in the last 2 years</c>, or <c>Offering history unknown</c>.
    /// </summary>
    public string Summary(CourseKey key, PlanSeason season)
    {
        if (IsLikelyOffered(key, season) is null) return "Offering history unknown";
        var family = FamilyOf(season);
        var relevant = CheckedTerms.Where(code => season.Matches(SeasonOf(code))).ToList();
        var terms = _offered.TryGetValue(key, out var offered) ? offered : [];
        string? latest = null;
        foreach (var code in relevant)
        {
            if (!terms.Contains(code)) continue;
            latest = code;
            break;
        }
        if (latest is null)
        {
            var years = CheckedYears;
            var span = years == 1 ? "the last year" : $"the last {years} years";
            var name = family is SeasonFamily known ? Name(known) : season.ShortName().ToLowerInvariant();
            return $"Not offered in a {name} term in {span}";
        }
        if (family is SeasonFamily found && IsRegular(found, key)) return "Offered every " + Name(found);
        return "Offered in " + Label(latest);
    }

    public OfferingHistory Merging(OfferingHistory other)
    {
        var terms = CheckedTerms.Concat(other.CheckedTerms).Distinct(StringComparer.Ordinal)
            .OrderByDescending(code => code, StringComparer.Ordinal).ToList();
        var combined = new Dictionary<CourseKey, IEnumerable<string>>();
        foreach (var (key, codes) in _offered) combined[key] = codes.ToHashSet(StringComparer.Ordinal);
        foreach (var (key, codes) in other._offered)
        {
            if (!combined.TryGetValue(key, out var existing))
            {
                combined[key] = codes.ToHashSet(StringComparer.Ordinal);
                continue;
            }
            var set = existing.ToHashSet(StringComparer.Ordinal);
            set.UnionWith(codes);
            combined[key] = set;
        }
        HashSet<CourseKey>? keys = null;
        if (CheckedKeys is not null && other.CheckedKeys is not null)
            keys = CheckedKeys.Concat(other.CheckedKeys).ToHashSet();
        return new OfferingHistory(terms, combined, keys);
    }

    public static int MonthIndex(string termCode) => TermCalendar.MonthIndex(termCode);

    /// <summary><c>Spring 2025</c>, <c>Summer 1 2025</c>, <c>Fall 2024</c>.</summary>
    public static string Label(string termCode)
    {
        var term = new Term(termCode, "");
        return $"{SeasonName(term.Season)} {term.CalendarYear}";
    }

    private int CheckedYears
    {
        get
        {
            if (CheckedTerms.Count == 0) return 1;
            var months = MonthIndex(CheckedTerms[0]) - MonthIndex(CheckedTerms[^1]);
            return Math.Max(1, (int)Math.Round(months / 12.0, MidpointRounding.AwayFromZero));
        }
    }

    private bool IsRegular(SeasonFamily family, CourseKey key)
    {
        var offeredTerms = _offered.TryGetValue(key, out var set) ? set : [];
        var periods = new Dictionary<int, bool>();
        foreach (var code in CheckedTerms)
        {
            if (FamilyOf(SeasonOf(code)) != family) continue;
            var year = new Term(code, "").CalendarYear;
            var already = periods.TryGetValue(year, out var offered) && offered;
            periods[year] = already || offeredTerms.Contains(code);
        }
        return periods.Count >= 2 && periods.Values.All(offered => offered);
    }

    private static TermSeason SeasonOf(string code) => new Term(code, "").Season;

    private static string SeasonName(TermSeason season) => season switch
    {
        TermSeason.Fall => "Fall",
        TermSeason.Winter => "Winter",
        TermSeason.Spring => "Spring",
        TermSeason.Summer1 => "Summer 1",
        TermSeason.SummerFull => "Summer",
        TermSeason.Summer2 => "Summer 2",
        _ => "Fall",
    };

    private enum SeasonFamily
    {
        Fall,
        Spring,
        Summer,
    }

    private static readonly SeasonFamily[] Families = [SeasonFamily.Fall, SeasonFamily.Spring, SeasonFamily.Summer];

    private static SeasonFamily? FamilyOf(TermSeason season) => season switch
    {
        TermSeason.Fall => SeasonFamily.Fall,
        TermSeason.Spring => SeasonFamily.Spring,
        TermSeason.Summer1 or TermSeason.SummerFull or TermSeason.Summer2 => SeasonFamily.Summer,
        _ => null,
    };

    private static SeasonFamily? FamilyOf(PlanSeason season) => season switch
    {
        PlanSeason.Fall => SeasonFamily.Fall,
        PlanSeason.Spring => SeasonFamily.Spring,
        PlanSeason.SummerA or PlanSeason.SummerFull or PlanSeason.SummerB => SeasonFamily.Summer,
        _ => null,
    };

    private static string Name(SeasonFamily family) => family switch
    {
        SeasonFamily.Fall => "fall",
        SeasonFamily.Spring => "spring",
        _ => "summer",
    };

    private static string List(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            2 => $"{list[0]} and {list[1]}",
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
        };
    }
}

/// <summary>
/// Suggests how to reorder a degree plan when a semester's courses are not taught then:
/// move one later, swap it with a later course that is taught now, or add a semester.
/// </summary>
public static class DegreePlanReordering
{
    public readonly record struct Move(CourseKey Course, Guid From, Guid To);

    public sealed class Suggestion
    {
        public required string Id { get; init; }
        public required IReadOnlyList<Move> Moves { get; init; }
        public required string Title { get; init; }
        public required IReadOnlyList<string> Reasons { get; init; }
        public DegreePlanTerm? AddsSemester { get; init; }
    }

    public const int MaxSuggestions = 5;

    public static List<Suggestion> Suggestions(
        DegreePlan plan,
        Guid termId,
        IReadOnlyList<CourseKey> blocking,
        OfferingHistory history,
        Func<CourseKey, RequisiteAnswer> prerequisites,
        CourseEquivalences equivalences,
        double maxCreditsPerTerm = 20)
    {
        var working = DegreePlanCopy.Clone(plan);
        working.SortTerms();
        var origin = working.Terms.FindIndex(term => term.Id == termId);
        if (origin < 0) return [];

        var requirements = new Dictionary<CourseKey, Requirements>();
        foreach (var key in working.Terms.SelectMany(term => term.Courses).Select(course => course.Key).OfType<CourseKey>())
        {
            if (requirements.ContainsKey(key)) continue;
            var answer = prerequisites(key);
            requirements[key] = !answer.IsKnown
                ? Requirements.Unknown
                : answer.Requisite is Requisite requisite ? Requirements.Needs(requisite) : Requirements.Nothing;
        }
        var engine = new Engine(
            working,
            origin,
            history,
            requirements,
            equivalences.Merging(working.DeclaredEquivalences),
            maxCreditsPerTerm);

        var candidates = new List<Candidate>();
        var seen = new HashSet<CourseKey>();
        for (var order = 0; order < blocking.Count; order++)
        {
            var key = blocking[order];
            if (!seen.Add(key)) continue;
            var course = working.Terms[origin].Courses.FirstOrDefault(item => item.Key == key);
            if (course is null) continue;
            candidates.AddRange(engine.Candidates(course, order, blocking.ToHashSet()));
        }
        candidates.Sort((a, b) => a.Rank.CompareTo(b.Rank));
        return candidates.Take(MaxSuggestions).Select(candidate => candidate.Item).ToList();
    }

    /// <summary>
    /// Adds the semester a suggestion needs, then moves each course. Does nothing when the plan
    /// no longer matches, so a stale suggestion cannot apply halfway.
    /// </summary>
    public static void Apply(Suggestion suggestion, DegreePlan plan)
    {
        var updated = DegreePlanCopy.Clone(plan);
        var semesterId = new Dictionary<Guid, Guid>();
        if (suggestion.AddsSemester is DegreePlanTerm semester)
        {
            var existing = updated.Terms.FirstOrDefault(term => term.Season == semester.Season && term.Year == semester.Year);
            if (existing is not null) semesterId[semester.Id] = existing.Id;
            else
            {
                updated.Terms.Add(DegreePlanCopy.CloneTerm(semester));
                updated.SortTerms();
            }
        }
        foreach (var move in suggestion.Moves)
        {
            var to = semesterId.TryGetValue(move.To, out var mapped) ? mapped : move.To;
            if (!PerformMove(move.Course, move.From, to, updated)) return;
        }
        plan.Terms = updated.Terms;
    }

    /// <summary>A UUID that depends only on the text, so an added semester has the same id every time.</summary>
    public static Guid StableId(string text)
    {
        unchecked
        {
            ulong first = 0xcbf29ce484222325;
            ulong second = 0x84222325cbf29ce4;
            foreach (var b in Encoding.UTF8.GetBytes(text))
            {
                first = (first ^ b) * 0x100000001B3;
                second = ((second + b) * 0x100000001B3) ^ first;
            }
            var hex = $"{first:x16}{second:x16}";
            var formatted = $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
            return Guid.Parse(formatted);
        }
    }

    private static bool PerformMove(CourseKey course, Guid from, Guid to, DegreePlan plan)
    {
        var source = plan.Terms.FindIndex(term => term.Id == from);
        var target = plan.Terms.FindIndex(term => term.Id == to);
        if (source < 0 || target < 0) return false;
        var position = plan.Terms[source].Courses.FindIndex(item => item.Key == course);
        if (position < 0) return false;
        var moved = plan.Terms[source].Courses[position];
        plan.Terms[source].Courses.RemoveAt(position);
        plan.Terms[target].Courses.Add(moved);
        return true;
    }

    private static string GuidText(Guid id) => id.ToString("D").ToUpperInvariant();

    private abstract class Requirements
    {
        public static Requirements Unknown { get; } = new UnknownKind();
        public static Requirements Nothing { get; } = new NothingKind();
        public static Requirements Needs(Requisite requisite) => new NeedsKind(requisite);
        private sealed class UnknownKind : Requirements;
        private sealed class NothingKind : Requirements;
        public sealed class NeedsKind(Requisite requisite) : Requirements
        {
            public Requisite Requisite { get; } = requisite;
        }
    }

    private enum Check
    {
        Met,
        Missing,
        Unknown,
    }

    private readonly record struct Rank(int Moves, int Target, int Uncertainty, int Order, int Detail, int Sequence) : IComparable<Rank>
    {
        public int CompareTo(Rank other)
        {
            var compared = Moves.CompareTo(other.Moves);
            if (compared != 0) return compared;
            compared = Target.CompareTo(other.Target);
            if (compared != 0) return compared;
            compared = Uncertainty.CompareTo(other.Uncertainty);
            if (compared != 0) return compared;
            compared = Order.CompareTo(other.Order);
            if (compared != 0) return compared;
            compared = Detail.CompareTo(other.Detail);
            return compared != 0 ? compared : Sequence.CompareTo(other.Sequence);
        }
    }

    private sealed class Candidate(Suggestion suggestion, Rank rank)
    {
        public Suggestion Item { get; } = suggestion;
        public Rank Rank { get; } = rank;
    }

    private sealed class Engine
    {
        private readonly DegreePlan _plan;
        private readonly int _origin;
        private readonly OfferingHistory _history;
        private readonly Dictionary<CourseKey, Requirements> _requirements;
        private readonly CourseEquivalences _equivalences;
        private readonly double _maxCredits;
        private readonly Dictionary<Guid, Check> _before;
        private int _sequence;

        public Engine(
            DegreePlan plan,
            int origin,
            OfferingHistory history,
            Dictionary<CourseKey, Requirements> requirements,
            CourseEquivalences equivalences,
            double maxCredits)
        {
            _plan = plan;
            _origin = origin;
            _history = history;
            _requirements = requirements;
            _equivalences = equivalences;
            _maxCredits = maxCredits;
            _before = Evaluate(plan);
        }

        public List<Candidate> Candidates(DegreePlanCourse course, int order, HashSet<CourseKey> blocking)
        {
            if (course.Key is not CourseKey key) return [];
            var found = new List<Candidate>();
            for (var target = _origin + 1; target < _plan.Terms.Count; target++)
            {
                var single = SingleMove(course, key, target, order);
                if (single is not null) found.Add(single);
                var partners = _plan.Terms[target].Courses;
                for (var position = 0; position < partners.Count; position++)
                {
                    var partner = partners[position];
                    if (partner.Key is not CourseKey partnerKey || blocking.Contains(partnerKey) || partnerKey == key) continue;
                    var swap = Swap(course, key, partner, partnerKey, target, order, position);
                    if (swap is not null) found.Add(swap);
                }
            }
            if (found.Count == 0)
            {
                var appended = AppendedSemester(course, key, order);
                if (appended is not null) found.Add(appended);
            }
            return found;
        }

        private static Check CheckOf(CourseKey key, HashSet<CourseKey> taken, Dictionary<CourseKey, Requirements> requirements, CourseEquivalences equivalences)
        {
            var requirement = requirements.TryGetValue(key, out var known) ? known : Requirements.Unknown;
            return requirement switch
            {
                Requirements.NeedsKind needs => DegreePlanAnalysis.Status(needs.Requisite, taken, equivalences) == DegreePlanAnalysis.PrerequisiteStatus.Met
                    ? Check.Met
                    : Check.Missing,
                _ when requirement == Requirements.Nothing => Check.Met,
                _ => Check.Unknown,
            };
        }

        private Dictionary<Guid, Check> Evaluate(DegreePlan plan) => Evaluate(plan, _requirements, _equivalences);

        private static Dictionary<Guid, Check> Evaluate(DegreePlan plan, Dictionary<CourseKey, Requirements> requirements, CourseEquivalences equivalences)
        {
            var taken = new HashSet<CourseKey>(plan.CompletedKeys);
            var checks = new Dictionary<Guid, Check>();
            foreach (var term in plan.Terms)
            {
                foreach (var course in term.Courses)
                {
                    if (course.Key is not CourseKey key) continue;
                    checks[course.Id] = CheckOf(key, taken, requirements, equivalences);
                }
                foreach (var key in term.Courses.Select(course => course.Key).OfType<CourseKey>()) taken.Add(key);
            }
            return checks;
        }

        private Dictionary<Guid, Check>? ChecksAfter(DegreePlan changed, HashSet<Guid> moved)
        {
            var after = Evaluate(changed);
            foreach (var (id, check) in after)
            {
                if (moved.Contains(id))
                {
                    if (check == Check.Missing) return null;
                }
                else if (_before.TryGetValue(id, out var prior) && prior == Check.Met && check == Check.Missing)
                {
                    return null;
                }
            }
            return after;
        }

        private static string CreditsLabel(double credits) =>
            $"{CreditFormatter.Number(credits)} {(credits == 1 ? "credit" : "credits")}";

        private int? Uncertainty(CourseKey key, PlanSeason season) => _history.IsLikelyOffered(key, season) switch
        {
            false => null,
            true => 0,
            _ => 1,
        };

        private string OfferingReason(CourseKey key, PlanSeason season, bool named)
        {
            var text = _history.Summary(key, season);
            return named ? $"{key}: {LowerFirst(text)}" : text;
        }

        private string PrerequisiteReason(CourseKey key, Check check, bool wasMet, bool named)
        {
            var requirement = _requirements.TryGetValue(key, out var known) ? known : Requirements.Unknown;
            var text = check == Check.Unknown
                ? "Prerequisites couldn\u2019t be checked"
                : requirement == Requirements.Nothing
                    ? "No prerequisites"
                    : check == Check.Met
                        ? wasMet ? "Prerequisites still met" : "Prerequisites met by then"
                        : "Prerequisites not met";
            return named ? $"{key}: {LowerFirst(text)}" : text;
        }

        private static string LowerFirst(string text) =>
            text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

        private Candidate? SingleMove(DegreePlanCourse course, CourseKey key, int target, int order)
        {
            var semester = _plan.Terms[target];
            var offering = Uncertainty(key, semester.Season);
            if (offering is not int offeringUncertainty) return null;
            var total = semester.Credits + course.Credits;
            if (total > _maxCredits) return null;

            var move = new Move(key, _plan.Terms[_origin].Id, semester.Id);
            var changed = DegreePlanCopy.Clone(_plan);
            if (!PerformMove(key, move.From, move.To, changed)) return null;
            var after = ChecksAfter(changed, [course.Id]);
            if (after is null) return null;
            var check = after.TryGetValue(course.Id, out var found) ? found : Check.Unknown;
            var suggestion = new Suggestion
            {
                Id = $"move-{key}-{GuidText(move.To)}",
                Moves = [move],
                Title = $"Move {key} to {semester.ShortName}",
                Reasons =
                [
                    OfferingReason(key, semester.Season, false),
                    PrerequisiteReason(key, check, _before.TryGetValue(course.Id, out var prior) && prior == Check.Met, false),
                    $"{semester.ShortName} would have {CreditsLabel(total)}",
                ],
            };
            return new Candidate(suggestion, new Rank(1, target, offeringUncertainty + (check == Check.Unknown ? 1 : 0), order, 0, _sequence++));
        }

        private Candidate? Swap(
            DegreePlanCourse course,
            CourseKey key,
            DegreePlanCourse partner,
            CourseKey partnerKey,
            int target,
            int order,
            int detail)
        {
            var semester = _plan.Terms[target];
            var home = _plan.Terms[_origin];
            var courseOffering = Uncertainty(key, semester.Season);
            var partnerOffering = Uncertainty(partnerKey, home.Season);
            if (courseOffering is not int courseUncertainty || partnerOffering is not int partnerUncertainty) return null;

            var newHome = home.Credits - course.Credits + partner.Credits;
            var newTarget = semester.Credits - partner.Credits + course.Credits;
            if (newHome > Math.Max(_maxCredits, home.Credits) || newTarget > Math.Max(_maxCredits, semester.Credits)) return null;

            Move[] moves =
            [
                new(key, home.Id, semester.Id),
                new(partnerKey, semester.Id, home.Id),
            ];
            var changed = DegreePlanCopy.Clone(_plan);
            foreach (var move in moves)
            {
                if (!PerformMove(move.Course, move.From, move.To, changed)) return null;
            }
            var after = ChecksAfter(changed, [course.Id, partner.Id]);
            if (after is null) return null;
            var courseCheck = after.TryGetValue(course.Id, out var courseFound) ? courseFound : Check.Unknown;
            var partnerCheck = after.TryGetValue(partner.Id, out var partnerFound) ? partnerFound : Check.Unknown;
            var suggestion = new Suggestion
            {
                Id = $"swap-{key}-{GuidText(semester.Id)}-{partnerKey}",
                Moves = moves,
                Title = $"Move {key} to {semester.ShortName} and {partnerKey} to {home.ShortName}",
                Reasons =
                [
                    OfferingReason(key, semester.Season, true),
                    OfferingReason(partnerKey, home.Season, true),
                    PrerequisiteReason(key, courseCheck, _before.TryGetValue(course.Id, out var coursePrior) && coursePrior == Check.Met, true),
                    PrerequisiteReason(partnerKey, partnerCheck, _before.TryGetValue(partner.Id, out var partnerPrior) && partnerPrior == Check.Met, true),
                    $"{home.ShortName} would have {CreditsLabel(newHome)}",
                    $"{semester.ShortName} would have {CreditsLabel(newTarget)}",
                ],
            };
            var unsure = courseUncertainty + partnerUncertainty + (courseCheck == Check.Unknown ? 1 : 0) + (partnerCheck == Check.Unknown ? 1 : 0);
            return new Candidate(suggestion, new Rank(2, target, unsure, order, detail, _sequence++));
        }

        private Candidate? AppendedSemester(DegreePlanCourse course, CourseKey key, int order)
        {
            if (course.Credits > _maxCredits) return null;
            var home = _plan.Terms[_origin];
            foreach (var upcoming in DegreePlanAutofill.NextSemesters(_plan, 8))
            {
                if (upcoming.SortKey <= home.SortKey) continue;
                var offering = Uncertainty(key, upcoming.Season);
                if (offering is not int offeringUncertainty) continue;
                var semester = new DegreePlanTerm
                {
                    Season = upcoming.Season,
                    Year = upcoming.Year,
                    Id = StableId($"degree-plan-term-{PlanSeasonNames.Raw(upcoming.Season)}-{upcoming.Year}"),
                };
                var changed = DegreePlanCopy.Clone(_plan);
                changed.Terms.Add(DegreePlanCopy.CloneTerm(semester));
                changed.SortTerms();
                var move = new Move(key, home.Id, semester.Id);
                if (!PerformMove(key, move.From, move.To, changed)) continue;
                var after = ChecksAfter(changed, [course.Id]);
                if (after is null) continue;
                var check = after.TryGetValue(course.Id, out var found) ? found : Check.Unknown;
                var suggestion = new Suggestion
                {
                    Id = $"move-{key}-{GuidText(semester.Id)}",
                    Moves = [move],
                    Title = $"Move {key} to a new {semester.ShortName} semester",
                    Reasons =
                    [
                        OfferingReason(key, semester.Season, false),
                        PrerequisiteReason(key, check, _before.TryGetValue(course.Id, out var prior) && prior == Check.Met, false),
                        $"Adds {semester.ShortName} to your plan, with {CreditsLabel(course.Credits)}",
                    ],
                    AddsSemester = semester,
                };
                return new Candidate(suggestion, new Rank(1, _plan.Terms.Count, offeringUncertainty + (check == Check.Unknown ? 1 : 0), order, 0, _sequence++));
            }
            return null;
        }
    }
}
