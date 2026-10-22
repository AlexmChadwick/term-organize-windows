// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text.RegularExpressions;

namespace TermOrganize.Core;

/// <summary>Matches plan courses to catalog requirements. Port of the Swift audit engine.</summary>
internal sealed class AuditEngine
{
    private abstract class Effective
    {
        public static Effective Waived { get; } = new WaivedKind();
        public static Effective Of(RequirementKind kind, bool anyCourse) => new KindEffective(kind, anyCourse);
        private sealed class WaivedKind : Effective;
        public sealed class KindEffective(RequirementKind ruleKind, bool anyCourse) : Effective
        {
            public RequirementKind RuleKind { get; } = ruleKind;
            public bool AnyCourse { get; } = anyCourse;
        }
    }

    private readonly DegreePlan _plan;
    private readonly ProgramRequirements _requirements;
    private readonly CourseEquivalences _equivalences;
    private readonly DegreeCourseAttributes _attributes;
    private readonly List<RequirementSection> _sections;
    private readonly List<RequirementRule> _rules;
    private readonly Dictionary<string, List<RequirementSubstitution>> _substitutionsByRule;
    private readonly List<RequirementSubstitution> _unplaced;
    private readonly Dictionary<string, HashSet<string>> _rulesByName;
    private readonly Dictionary<string, DegreeAudit.CourseUse> _pool = new(StringComparer.Ordinal);
    private readonly Dictionary<CourseKey, List<string>> _instances = [];
    private HashSet<string> _available = new(StringComparer.Ordinal);
    private Dictionary<string, List<DegreeAudit.CourseUse>> _used = new(StringComparer.Ordinal);
    private Dictionary<string, int> _optionsCounted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Effective> _effective = new(StringComparer.Ordinal);
    private readonly Dictionary<CourseKey, int> _demand = [];
    private Dictionary<string, HashSet<string>> _singles = new(StringComparer.Ordinal);
    private List<RequirementSubstitution> _unmatched = [];

    public AuditEngine(DegreePlan plan, ProgramRequirements requirements, CourseEquivalences equivalences, DegreeCourseAttributes attributes)
    {
        _plan = plan;
        _requirements = requirements;
        _equivalences = equivalences;
        _attributes = attributes;
        _sections = requirements.ApplicableSections(plan.ConcentrationID);
        _rules = _sections.SelectMany(section => section.Groups).SelectMany(group => group.Rules).ToList();
        _rulesByName = [];
        foreach (var rule in _rules)
        {
            foreach (var name in new[] { requirements.DegreeShortName(rule.Id), requirements.Title(rule.Id) })
            {
                if (name is null) continue;
                var key = name.ToLowerInvariant();
                if (!_rulesByName.TryGetValue(key, out var bucket)) _rulesByName[key] = bucket = new HashSet<string>(StringComparer.Ordinal);
                bucket.Add(rule.Id);
            }
        }

        var ruleIds = _rules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
        _substitutionsByRule = new Dictionary<string, List<RequirementSubstitution>>(StringComparer.Ordinal);
        _unplaced = [];
        foreach (var substitution in plan.Substitutions)
        {
            if (ruleIds.Contains(substitution.RuleID))
                AddSub(substitution.RuleID, substitution);
            else if (_rulesByName.TryGetValue(substitution.RuleTitle.ToLowerInvariant(), out var named) && named.Count == 1)
                AddSub(named.First(), substitution);
            else
                _unplaced.Add(substitution);
        }

        var order = 0;
        void Offer(DegreePlanCourse course, string? semester)
        {
            var currentOrder = order;
            order++;
            if (course.Key is not CourseKey key) return;
            var use = new DegreeAudit.CourseUse
            {
                Key = key,
                Title = course.Title,
                Credits = course.Credits,
                Semester = semester,
                Order = currentOrder,
                SelectedCrn = DegreeCourseRepeats.CleanCrn(course.SelectedCrn),
                RowId = course.Id,
            };
            var earlier = _instances.TryGetValue(key, out var prior)
                ? prior.Select(id => _pool[id]).ToList()
                : [];
            // A line with no credit is a placeholder, not a second course. Every other copy
            // counts on its own, including two seminars with the same title or the same CRN.
            // DuplicateKeys still warns about those. It does not drop them from the audit.
            if (use.Credits <= 0 && earlier.FirstOrDefault() is DegreeAudit.CourseUse repeated)
            {
                var better = repeated.IsCompleted == use.IsCompleted ? use.Credits > repeated.Credits : use.IsCompleted;
                if (better)
                {
                    use.Instance = repeated.Instance;
                    _pool[use.Id] = use;
                }
                return;
            }
            use.Instance = earlier.Count;
            _pool[use.Id] = use;
            if (!_instances.TryGetValue(key, out var list)) _instances[key] = list = [];
            list.Add(use.Id);
        }

        foreach (var course in plan.Completed) Offer(course, null);
        foreach (var term in plan.Terms)
            foreach (var course in term.Courses) Offer(course, term.Name);
        _available = _pool.Keys.ToHashSet(StringComparer.Ordinal);
    }

    public DegreeAudit Run()
    {
        ResolveSubstitutions();
        ApplyCourseSubstitutions();

        var required = new List<(int Index, RequirementRule Rule, CourseOption Option)>();
        for (var index = 0; index < _rules.Count; index++)
        {
            if (_effective.GetValueOrDefault(_rules[index].Id) is Effective.KindEffective { RuleKind: RequirementKind.Required requiredKind })
                required.Add((index, _rules[index], requiredKind.Option));
        }
        foreach (var item in required.OrderBy(item => item.Option.Alternatives.Count).ThenBy(item => item.Index))
            FillRequired(item.Rule, item.Option);

        var choices = new List<(int Index, RequirementRule Rule, int Options, bool Ranged)>();
        for (var index = 0; index < _rules.Count; index++)
        {
            if (_effective.GetValueOrDefault(_rules[index].Id) is Effective.KindEffective { RuleKind: RequirementKind.Choose choose } kind)
                choices.Add((index, _rules[index], choose.Options.Count, choose.Ranges.Count > 0 || kind.AnyCourse));
        }
        foreach (var item in choices.Where(item => !item.Ranged).OrderBy(item => item.Options).ThenBy(item => item.Index))
            FillChoice(item.Rule);
        var ranged = choices.Where(item => item.Ranged).ToList();
        foreach (var item in ranged.Where(item => CountsCourses(item.Rule))) FillChoice(item.Rule);
        foreach (var item in ranged.Where(item => !CountsCourses(item.Rule))) FillChoice(item.Rule);
        Rebalance();
        if (EnsureBestMatching())
        {
            foreach (var rule in _rules.Where(rule => !IsMet(rule.Id)))
            {
                switch (_effective.GetValueOrDefault(rule.Id))
                {
                    case Effective.KindEffective { RuleKind: RequirementKind.Required requiredKind }:
                        FillRequired(rule, requiredKind.Option);
                        break;
                    case Effective.KindEffective { RuleKind: RequirementKind.Choose }:
                        FillChoice(rule);
                        break;
                }
            }
        }
        TrimSurplusHours();

        var generalElectives = new List<DegreeAudit.CourseUse>();
        foreach (var rule in _rules)
        {
            if (_effective.GetValueOrDefault(rule.Id) is not Effective.KindEffective { RuleKind: RequirementKind.GeneralElectives general }) continue;
            var counted = Uses(rule.Id).Sum(use => use.Credits);
            var candidates = Remaining().Where(use => use.Credits > 0).ToList();
            var chosen = FewestCredits(candidates, general.Hours - counted) ?? candidates.Select(use => use.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var use in candidates)
                if (chosen.Contains(use.Id)) Consume(use.Id, rule.Id);
            generalElectives.AddRange(Uses(rule.Id));
        }

        var sections = _sections.Select(section => new DegreeAudit.SectionResult
        {
            Section = section,
            Groups = section.Groups.Select(group => new DegreeAudit.GroupResult
            {
                Group = group,
                Rules = group.Rules.Select(ResultFor).ToList(),
            }).ToList(),
        }).ToList();
        var courses = _pool.Values.Where(use => use.CountsTowardHours).ToList();
        var concentrationChosen = _plan.ConcentrationID is string concentrationId &&
            _requirements.Concentrations.Any(section => section.Id == concentrationId);
        var placements = new Dictionary<Guid, string>();
        foreach (var (ruleId, substitutions) in _substitutionsByRule)
            foreach (var substitution in substitutions) placements[substitution.Id] = ruleId;

        return new DegreeAudit
        {
            Requirements = _requirements,
            Sections = sections,
            GeneralElectives = generalElectives,
            Unused = Remaining(),
            CompletedHours = courses.Where(use => use.IsCompleted).Sum(use => Math.Max(0, use.Credits)),
            PlannedHours = courses.Where(use => !use.IsCompleted).Sum(use => Math.Max(0, use.Credits)),
            RequiredHours = _requirements.TotalHours,
            UnmatchedSubstitutions = _unmatched,
            Placements = placements,
            NeedsConcentration = _requirements.Concentrations.Count > 0 && !concentrationChosen,
            NoteChecks = NoteChecks(),
            Attributes = _attributes,
        };
    }

    public static HashSet<string>? FewestCredits(IReadOnlyList<DegreeAudit.CourseUse> uses, double need)
    {
        if (need <= 0) return new HashSet<string>(StringComparer.Ordinal);
        var items = uses.Where(use => use.Credits > 0).ToList();
        var weights = items.Select(item => Math.Max(1, (int)Math.Round(item.Credits * 2, MidpointRounding.AwayFromZero))).ToArray();
        var target = (int)Math.Ceiling(need * 2);
        if (weights.Length == 0 || weights.Sum() < target) return null;
        var largest = weights.Max();
        var cap = target + largest;
        var best = new Pick?[cap + 1];
        best[0] = new Pick();
        for (var index = 0; index < items.Count; index++)
        {
            var weight = weights[index];
            if (weight > cap) continue;
            var item = items[index];
            for (var sum = cap; sum >= weight; sum--)
            {
                if (best[sum - weight] is not Pick source) continue;
                var pick = new Pick
                {
                    Completed = source.Completed + (item.IsCompleted ? 1 : 0),
                    Count = source.Count + 1,
                    Order = source.Order + item.Order,
                    Items = new List<int>(source.Items) { index },
                };
                if (best[sum] is Pick current && !pick.IsBetterThan(current)) continue;
                best[sum] = pick;
            }
        }
        for (var sum = target; sum <= cap; sum++)
        {
            if (best[sum] is not Pick pick) continue;
            return pick.Items.Select(index => items[index].Id).ToHashSet(StringComparer.Ordinal);
        }
        return null;
    }

    private void ResolveSubstitutions()
    {
        _unmatched = _unplaced.ToList();
        foreach (var rule in _rules)
        {
            var substitutions = Subs(rule.Id);
            var replacement = substitutions.Select(sub => sub.Change).OfType<SubstitutionChange.ReplaceChange>().LastOrDefault();
            if (substitutions.Any(sub => sub.Change.Equals(SubstitutionChange.Waive)))
                _effective[rule.Id] = Effective.Waived;
            else if (replacement is not null)
                _effective[rule.Id] = ReplacementKind(replacement.Replacement);
            else
                _effective[rule.Id] = Effective.Of(rule.Kind, false);
        }
        foreach (var rule in _rules)
        {
            if (_effective.GetValueOrDefault(rule.Id) is not Effective.KindEffective kind) continue;
            var listed = kind.RuleKind switch
            {
                RequirementKind.Required required => required.Option.AllCourses,
                RequirementKind.Choose choose => choose.Options.SelectMany(option => option.AllCourses).ToList(),
                _ => [],
            };
            foreach (var key in listed.Distinct()) _demand[key] = _demand.GetValueOrDefault(key) + 1;
        }
    }

    private Effective ReplacementKind(RequirementReplacement replacement)
    {
        var options = replacement.Courses.Select(key => new CourseOption([new[] { key }])).ToList();
        var ranges = new List<CourseRange>();
        var anyCourse = false;
        string? named = null;
        if (replacement.LikeRuleTitle is string title &&
            _rulesByName.TryGetValue(title.ToLowerInvariant(), out var namedIds) && namedIds.Count == 1)
            named = namedIds.First();
        var likeById = replacement.LikeRuleID is string likeId ? _requirements.Rule(likeId) : null;
        if (likeById is null && named is null &&
            replacement.LikeRuleTitle?.StartsWith("general electives", StringComparison.OrdinalIgnoreCase) == true)
            anyCourse = true;
        var like = likeById ?? (named is null ? null : _requirements.Rule(named));
        if (like is not null)
        {
            switch (like.Kind)
            {
                case RequirementKind.Required required:
                    options.Insert(0, required.Option);
                    break;
                case RequirementKind.Choose choose:
                    options = choose.Options.Concat(options).ToList();
                    ranges = choose.Ranges.Select(range => range.Applying(replacement.CourseNumbers)).ToList();
                    break;
                case RequirementKind.GeneralElectives:
                    anyCourse = true;
                    break;
            }
        }
        int? count = replacement.Count ?? (replacement.Hours is null ? 1 : null);
        return Effective.Of(new RequirementKind.Choose(count, replacement.Hours, options, ranges), anyCourse);
    }

    private void ApplyCourseSubstitutions()
    {
        foreach (var rule in _rules)
        {
            if (_effective.GetValueOrDefault(rule.Id) is not Effective.KindEffective) continue;
            foreach (var substitution in Subs(rule.Id))
            {
                if (substitution.Change is not SubstitutionChange.CountCourseChange count) continue;
                var taken = count.RowId is Guid row ? Row(row) : StandIn(count.Course);
                if (taken is null)
                {
                    _unmatched.Add(substitution);
                    continue;
                }
                if (count.RowId is not null) _pool[taken].CountsTowardHours = true;
                Consume(taken, rule.Id, bySubstitution: true, satisfying: count.Course);
                _optionsCounted[rule.Id] = _optionsCounted.GetValueOrDefault(rule.Id) + 1;
            }
        }
    }

    private void FillRequired(RequirementRule rule, CourseOption option)
    {
        if (Uses(rule.Id).Count > 0) return;
        var complete = option.Alternatives.Where(IsComplete).ToList();
        if (complete.Count > 0)
        {
            var best = complete.OrderBy(Score).First();
            foreach (var (required, taken) in Assign(best)) Consume(taken, rule.Id, satisfying: required);
            return;
        }
        var bestPartial = new List<(CourseKey Required, string Taken)>();
        var bestCount = -1;
        foreach (var alternative in option.Alternatives)
        {
            var pairs = Assign(alternative);
            if (pairs.Count > bestCount)
            {
                bestCount = pairs.Count;
                bestPartial = pairs;
            }
        }
        foreach (var (required, taken) in bestPartial) Consume(taken, rule.Id, satisfying: required);
    }

    private void FillChoice(RequirementRule rule)
    {
        if (_effective.GetValueOrDefault(rule.Id) is not Effective.KindEffective { RuleKind: RequirementKind.Choose choose } kind) return;
        bool Done() => IsSatisfied(rule.Id, choose.Count, choose.Hours);

        var candidates = new List<(List<CourseKey> Best, int Planned, int Contested, int Index)>();
        for (var index = 0; index < choose.Options.Count; index++)
        {
            var complete = choose.Options[index].Alternatives.Where(IsComplete).ToList();
            if (complete.Count == 0) continue;
            var best = complete.OrderBy(Score).First();
            var score = Score(best);
            candidates.Add((best, score.Planned, score.Contested, index));
        }
        candidates.Sort((a, b) =>
        {
            var compared = a.Planned.CompareTo(b.Planned);
            if (compared != 0) return compared;
            compared = a.Contested.CompareTo(b.Contested);
            return compared != 0 ? compared : a.Index.CompareTo(b.Index);
        });
        foreach (var candidate in candidates)
        {
            if (Done()) break;
            if (!IsComplete(candidate.Best)) continue;
            var pairs = Assign(candidate.Best);
            foreach (var (required, taken) in pairs) Consume(taken, rule.Id, satisfying: required);
            if (pairs.Count == 1) MarkSingle(rule.Id, pairs[0].Taken);
            _optionsCounted[rule.Id] = _optionsCounted.GetValueOrDefault(rule.Id) + 1;
        }
        if (Done() || (choose.Ranges.Count == 0 && !kind.AnyCourse)) return;
        var matches = new List<(DegreeAudit.CourseUse Use, CourseKey? Via)>();
        foreach (var use in Remaining())
        {
            if (use.Credits <= 0 || (choose.Count is not null && use.Credits < 2)) continue;
            if (kind.AnyCourse)
            {
                matches.Add((use, null));
                continue;
            }
            var inRange = CourseInRange(use.Key, choose.Ranges);
            if (inRange is null) continue;
            matches.Add((use, inRange.Value == use.Key ? null : inRange));
        }
        foreach (var (use, via) in matches)
        {
            if (Done()) break;
            Consume(use.Id, rule.Id, satisfying: via);
            _optionsCounted[rule.Id] = _optionsCounted.GetValueOrDefault(rule.Id) + 1;
            MarkSingle(rule.Id, use.Id);
        }
    }

    private List<DegreeAudit.NoteCheck> NoteChecks()
    {
        var checks = new List<DegreeAudit.NoteCheck>();
        foreach (var section in _sections)
        {
            var notes = section.Notes.Concat(section.Groups.Where(group => group.Rules.Count == 0).SelectMany(group => group.Notes));
            foreach (var note in notes)
            {
                if (DegreeAudit.MajorHours(note) is double major)
                    checks.Add(MajorHoursCheck(section.Id, note, major));
                else if (DegreeAudit.IsCoopNote(note))
                    checks.Add(CoopCheck(section.Id, note));
                else if (DegreeAudit.IsNupathNote(note))
                {
                    if (NupathCheck(section.Id, note) is DegreeAudit.NoteCheck nupath) checks.Add(nupath);
                }
                else if (DegreeAudit.IsUniversitywideNote(note))
                {
                    if (UniversitywideCheck(section.Id, note) is DegreeAudit.NoteCheck university) checks.Add(university);
                }
                else if (DegreeAudit.TotalHours(note) is double total)
                    checks.Add(TotalHoursCheck(section.Id, note, total));
            }
        }
        return checks;
    }

    private DegreeAudit.NoteCheck MajorHoursCheck(string sectionId, string note, double required)
    {
        var counted = new Dictionary<string, DegreeAudit.CourseUse>(StringComparer.Ordinal);
        var ranges = new List<CourseRange>();
        foreach (var section in _sections)
        {
            foreach (var group in section.Groups)
            {
                var isWriting = ContainsWriting(section.Title) || ContainsWriting(group.Title ?? "");
                foreach (var rule in group.Rules)
                {
                    if (_effective.GetValueOrDefault(rule.Id) is not Effective.KindEffective kind) continue;
                    if (kind.RuleKind is RequirementKind.GeneralElectives) continue;
                    if (kind.RuleKind is RequirementKind.Choose choose) ranges.AddRange(choose.Ranges);
                    if (isWriting) continue;
                    foreach (var use in Uses(rule.Id)) counted[use.Id] = use;
                }
            }
        }
        foreach (var use in _pool.Values)
        {
            if (use.CountsTowardHours && !counted.ContainsKey(use.Id) && CourseInRange(use.Key, ranges) is not null) counted[use.Id] = use;
        }
        var completed = counted.Values.Where(use => use.IsCompleted).Sum(use => Math.Max(0, use.Credits));
        var planned = counted.Values.Where(use => !use.IsCompleted).Sum(use => Math.Max(0, use.Credits));
        var (status, detail) = DegreeAudit.HoursStatus(completed, planned, required);
        return new DegreeAudit.NoteCheck
        {
            SectionId = sectionId,
            Note = note,
            CheckKind = new DegreeAudit.NoteCheck.Kind.MajorHours(required),
            Status = status,
            Detail = detail,
        };
    }

    private DegreeAudit.NoteCheck CoopCheck(string sectionId, string note)
    {
        var coops = _pool.Values.Where(use => use.CountsTowardHours && IsCoop(use)).OrderBy(use => use.Order).ToList();
        DegreeAudit.Status status;
        string detail;
        if (coops.FirstOrDefault(use => use.IsCompleted) is DegreeAudit.CourseUse done)
        {
            status = DegreeAudit.Status.Complete;
            detail = $"{done.Key} completed";
        }
        else if (coops.FirstOrDefault() is DegreeAudit.CourseUse next)
        {
            status = DegreeAudit.Status.Planned;
            detail = $"{next.Key} planned{(next.Semester is string semester ? $" \u00b7 {semester}" : "")}";
        }
        else
        {
            status = DegreeAudit.Status.Missing;
            detail = "No co-op in the plan";
            if (NUpathRequirement.SatisfiedThroughCoop(note) is NUPath path)
            {
                var course = SortedPool().Where(use => Nupaths(use)?.Contains(path) == true)
                    .OrderBy(use => use.IsCompleted ? 0 : 1).ThenBy(use => use.Order).FirstOrDefault();
                if (course is not null)
                {
                    status = course.IsCompleted ? DegreeAudit.Status.Complete : DegreeAudit.Status.Planned;
                    detail = $"No co-op in the plan \u00b7 {course.Key} ({path.Short()}) {(course.IsCompleted ? "completed" : "planned")}";
                }
            }
        }
        return new DegreeAudit.NoteCheck
        {
            SectionId = sectionId,
            Note = note,
            CheckKind = new DegreeAudit.NoteCheck.Kind.Coop(),
            Status = status,
            Detail = detail,
        };
    }

    private DegreeAudit.NoteCheck? NupathCheck(string sectionId, string note)
    {
        if (_attributes.IsEmpty) return null;
        var uses = SortedPool();
        var courses = new List<NUpathMatching.Course>();
        var unknown = new List<DegreeAudit.CourseUse>();
        foreach (var use in uses)
        {
            var paths = Nupaths(use);
            if (paths is null)
            {
                unknown.Add(use);
                continue;
            }
            if (paths.Count > 0) courses.Add(new NUpathMatching.Course(use.Id, paths, use.IsCompleted));
        }
        var coops = uses.Where(IsCoop).Select(use => new NUpathMatching.Course(use.Id, [], use.IsCompleted)).ToList();
        var coopCounts = new Dictionary<NUPath, IReadOnlyList<NUpathMatching.Course>>();
        if (coops.Count > 0)
        {
            foreach (var section in _requirements.Sections)
            {
                foreach (var sectionNote in section.Notes.Concat(section.Groups.SelectMany(group => group.Notes)))
                {
                    if (NUpathRequirement.SatisfiedThroughCoop(sectionNote) is NUPath path) coopCounts[path] = coops;
                }
            }
        }
        var matching = new NUpathMatching(courses, coopCounts: coopCounts);
        var isLoading = unknown.Any(use => _attributes.Loading.Contains(use.Key));
        var items = new List<DegreeAudit.NoteCheck.Item>();
        foreach (var requirement in NUpathRequirement.All)
        {
            var chosen = matching.Courses(requirement.Path).Select(id => _pool.GetValueOrDefault(id)).OfType<DegreeAudit.CourseUse>().ToList();
            DegreeAudit.Status? status;
            string detail;
            if (chosen.Count >= requirement.Count)
            {
                status = chosen.All(use => use.IsCompleted) ? DegreeAudit.Status.Complete : DegreeAudit.Status.Planned;
                detail = Describe(chosen);
            }
            else
            {
                status = unknown.Count == 0 ? (chosen.Count == 0 ? DegreeAudit.Status.Missing : DegreeAudit.Status.Partial) : null;
                var found = chosen.Count == 0 ? "" : $"{chosen.Count} of {requirement.Count} \u00b7 {Describe(chosen)}";
                if (status is null)
                    detail = found.Length == 0
                        ? (isLoading ? "Looking up the plan\u2019s courses\u2026" : "Not found among the courses with NUpath data")
                        : found;
                else if (chosen.Count == 0)
                    detail = requirement.Count == 1 ? "No course in the plan has it" : $"Needs {requirement.Count} courses";
                else
                    detail = found;
            }
            items.Add(new DegreeAudit.NoteCheck.Item
            {
                Id = requirement.Path.AttributeCode(),
                Title = requirement.Title,
                Status = status,
                Detail = detail,
                Courses = chosen.Select(use => use.Id).ToList(),
            });
        }
        var met = items.Where(item => item.Status is DegreeAudit.Status itemStatus && itemStatus.IsSatisfied()).ToList();
        var open = items.Where(item => item.Status is not DegreeAudit.Status itemStatus || !itemStatus.IsSatisfied()).ToList();
        var checkStatus = open.Count == 0
            ? (met.All(item => item.Status == DegreeAudit.Status.Complete) ? DegreeAudit.Status.Complete : DegreeAudit.Status.Planned)
            : (met.Count == 0 && items.All(item => item.Courses.Count == 0) ? DegreeAudit.Status.Missing : DegreeAudit.Status.Partial);
        var detailText = $"{met.Count} of {items.Count} met";
        var missing = items.Where(item => item.Status is DegreeAudit.Status.Missing or DegreeAudit.Status.Partial).ToList();
        if (missing.Count is > 0 and <= 3)
        {
            detailText += " \u00b7 missing " + string.Join(", ", missing.Select(item =>
                Enum.GetValues<NUPath>().FirstOrDefault(path => path.AttributeCode() == item.Id) is var path && path.AttributeCode() == item.Id
                    ? path.Short()
                    : item.Title));
        }
        if (unknown.Count > 0 && items.Any(item => item.Status is null))
        {
            var names = string.Join(", ", unknown.Take(3).Select(use => use.Key.ToString()));
            detailText += isLoading
                ? $" \u00b7 looking up {(unknown.Count == 1 ? "1 course" : $"{unknown.Count} courses")}"
                : $" \u00b7 no NUpath data for {names}{(unknown.Count > 3 ? $" and {unknown.Count - 3} more" : "")}";
        }
        return new DegreeAudit.NoteCheck
        {
            SectionId = sectionId,
            Note = note,
            CheckKind = new DegreeAudit.NoteCheck.Kind.Nupath(),
            Status = checkStatus,
            Detail = detailText,
            Items = items,
            Counts = items.All(item => item.Status is not null),
        };
    }

    private DegreeAudit.NoteCheck? UniversitywideCheck(string sectionId, string note)
    {
        if (_requirements.Universitywide is not UniversitywideRequirements minimums) return null;
        var completed = _pool.Values.Where(use => use.CountsTowardHours && use.IsCompleted).Sum(use => Math.Max(0, use.Credits));
        var planned = _pool.Values.Where(use => use.CountsTowardHours && !use.IsCompleted).Sum(use => Math.Max(0, use.Credits));
        var items = new List<DegreeAudit.NoteCheck.Item>();
        if (minimums.TotalHours is double total)
        {
            var (status, detail) = DegreeAudit.HoursStatus(completed, planned, total);
            items.Add(new DegreeAudit.NoteCheck.Item
            {
                Id = "total-hours",
                Title = $"{DegreeAudit.HoursText(total)} total semester hours",
                Status = status,
                Detail = detail,
            });
        }
        if (minimums.RequiresExperientialLearning)
        {
            var coops = SortedPool().Where(IsCoop).ToList();
            var coop = coops.FirstOrDefault(use => use.IsCompleted) ?? coops.FirstOrDefault();
            items.Add(new DegreeAudit.NoteCheck.Item
            {
                Id = "experiential",
                Title = "Experiential learning",
                Status = coop is null ? null : coop.IsCompleted ? DegreeAudit.Status.Complete : DegreeAudit.Status.Planned,
                Detail = coop is null ? "Not checked: only co-ops are recognized" : Describe([coop]),
                Courses = coop is null ? [] : [coop.Id],
            });
        }
        if (minimums.Gpa is double gpa)
        {
            items.Add(new DegreeAudit.NoteCheck.Item
            {
                Id = "gpa",
                Title = $"{GpaText(gpa)} GPA",
                Detail = "Not checked: plans don\u2019t include grades",
            });
        }
        if (minimums.ResidencyHours is double residency)
        {
            items.Add(new DegreeAudit.NoteCheck.Item
            {
                Id = "residency",
                Title = $"{DegreeAudit.HoursText(residency)} semester hours at Northeastern",
                Detail = "Not checked: a plan doesn\u2019t say where credits were earned",
            });
        }
        var checkedItems = items.Where(item => item.Status is not null).ToList();
        if (checkedItems.Count == 0) return null;
        var statusValue = checkedItems.Select(item => item.Status!.Value).Min();
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["experiential"] = "experiential learning",
            ["gpa"] = "GPA",
            ["residency"] = "residency",
        };
        var uncheckedNames = items.Where(item => item.Status is null).Select(item => names.GetValueOrDefault(item.Id)).OfType<string>();
        var detailText = string.Join(" \u00b7 ", checkedItems.Select(item => item.Detail));
        var uncheckedList = uncheckedNames.ToList();
        if (uncheckedList.Count > 0) detailText += $" \u00b7 {DegreeAudit.JoinAnd(uncheckedList)} not checked";
        return new DegreeAudit.NoteCheck
        {
            SectionId = sectionId,
            Note = note,
            CheckKind = new DegreeAudit.NoteCheck.Kind.Universitywide(),
            Status = statusValue,
            Detail = detailText,
            Items = items,
        };
    }

    private DegreeAudit.NoteCheck TotalHoursCheck(string sectionId, string note, double required)
    {
        var completed = _pool.Values.Where(use => use.CountsTowardHours && use.IsCompleted).Sum(use => Math.Max(0, use.Credits));
        var planned = _pool.Values.Where(use => use.CountsTowardHours && !use.IsCompleted).Sum(use => Math.Max(0, use.Credits));
        var (status, detail) = DegreeAudit.HoursStatus(completed, planned, required);
        return new DegreeAudit.NoteCheck
        {
            SectionId = sectionId,
            Note = note,
            CheckKind = new DegreeAudit.NoteCheck.Kind.TotalHours(required),
            Status = status,
            Detail = detail,
        };
    }

    private void TrimSurplusHours()
    {
        foreach (var rule in _rules)
        {
            if (_effective.GetValueOrDefault(rule.Id) is not Effective.KindEffective { RuleKind: RequirementKind.Choose { Count: null, Hours: double hours } })
                continue;
            if (!ChoiceIsSatisfied(rule.Id)) continue;
            var uses = Uses(rule.Id);
            var singles = _singles.TryGetValue(rule.Id, out var set) ? set : [];
            var movable = uses.Where(use => !use.BySubstitution && singles.Contains(use.Id)).ToList();
            var movableIds = movable.Select(use => use.Id).ToHashSet(StringComparer.Ordinal);
            var fixedCredits = uses.Where(use => !movableIds.Contains(use.Id)).Sum(use => use.Credits);
            var unused = new List<(DegreeAudit.CourseUse Use, CourseKey Named)>();
            foreach (var use in Remaining())
            {
                if (SingleMatch(use, rule.Id) is CourseKey named) unused.Add((use, named));
            }
            var keep = FewestCredits(movable.Concat(unused.Select(item => item.Use)).ToList(), hours - fixedCredits);
            if (keep is null) continue;
            foreach (var use in movable)
                if (!keep.Contains(use.Id)) Release(use.Id, rule.Id);
            foreach (var (use, named) in unused)
                if (keep.Contains(use.Id)) Place(use.Id, rule.Id, named);
        }
    }

    private void Rebalance()
    {
        foreach (var rule in _rules)
        {
            if (ChoiceIsSatisfied(rule.Id)) continue;
            var budget = 2_000;
            while (!ChoiceIsSatisfied(rule.Id))
            {
                AuditEngine? next = null;
                for (var steps = 0; steps <= 3 && next is null; steps++)
                    next = Adding(rule.Id, 0, steps, new HashSet<string>(StringComparer.Ordinal) { rule.Id }, ref budget);
                if (next is null) break;
                CopyFrom(next);
            }
        }
    }

    private AuditEngine? Adding(string ruleId, double minCredits, int steps, HashSet<string> avoiding, ref int budget)
    {
        foreach (var use in Remaining())
        {
            if (use.Credits < minCredits || SingleMatch(use, ruleId) is not CourseKey named) continue;
            var next = Clone();
            next.Place(use.Id, ruleId, named);
            return next;
        }
        if (steps <= 0) return null;
        foreach (var donor in _rules)
        {
            if (avoiding.Contains(donor.Id) || !ChoiceIsSatisfied(donor.Id)) continue;
            var singles = _singles.TryGetValue(donor.Id, out var set) ? set : [];
            foreach (var use in Uses(donor.Id))
            {
                if (use.Credits < minCredits || !singles.Contains(use.Id)) continue;
                if (budget <= 0) return null;
                budget--;
                if (SingleMatch(use, ruleId) is not CourseKey named) continue;
                var next = Clone();
                next.Release(use.Id, donor.Id);
                next.Place(use.Id, ruleId, named);
                if (next.ChoiceIsSatisfied(donor.Id)) return next;
                var avoid = new HashSet<string>(avoiding, StringComparer.Ordinal) { ruleId, donor.Id };
                var refilled = next.Adding(donor.Id, next.Shortfall(donor.Id), steps - 1, avoid, ref budget);
                if (refilled is not null && refilled.ChoiceIsSatisfied(donor.Id)) return refilled;
            }
        }
        foreach (var donor in _rules)
        {
            if (avoiding.Contains(donor.Id)) continue;
            if (_effective.GetValueOrDefault(donor.Id) is not Effective.KindEffective { RuleKind: RequirementKind.Required required } ||
                required.Option.Alternatives.Count <= 1) continue;
            var current = Uses(donor.Id);
            if (current.Count == 0 || current.Any(use => use.BySubstitution)) continue;
            foreach (var use in current)
            {
                if (use.Credits < minCredits) continue;
                if (budget <= 0) return null;
                budget--;
                if (SingleMatch(use, ruleId) is not CourseKey named) continue;
                var next = Clone();
                next.ReleaseAll(donor.Id);
                next._available.Remove(use.Id);
                var complete = required.Option.Alternatives.Where(next.IsComplete).ToList();
                if (complete.Count == 0) continue;
                var best = complete.OrderBy(next.Score).First();
                var pairs = next.Assign(best);
                next._available.Add(use.Id);
                foreach (var (requiredKey, taken) in pairs) next.Consume(taken, donor.Id, satisfying: requiredKey);
                next.Place(use.Id, ruleId, named);
                return next;
            }
        }
        return null;
    }

    private bool EnsureBestMatching()
    {
        var pinned = _used.Values.SelectMany(uses => uses.Where(use => use.BySubstitution).Select(use => use.Id)).ToHashSet(StringComparer.Ordinal);
        var free = _pool.Keys.Where(id => !pinned.Contains(id)).ToList();
        free.Sort((a, b) => ComesFirst(a, b) ? -1 : ComesFirst(b, a) ? 1 : 0);
        if (free.Count == 0 || free.Count > CourseBits.Capacity) return false;
        var index = free.Select((id, position) => (id, position)).ToDictionary(item => item.id, item => item.position, StringComparer.Ordinal);
        var credits = free.Select(id => Math.Max(0, _pool[id].Credits)).ToArray();
        var all = CourseBits.Empty;
        for (var i = 0; i < free.Count; i++) all = all.Union(new CourseBits(i));

        var needs = new List<Need>();
        var electives = new List<double>();
        foreach (var rule in _rules)
        {
            switch (_effective.GetValueOrDefault(rule.Id))
            {
                case Effective.KindEffective { RuleKind: RequirementKind.GeneralElectives general }:
                    var counted = Uses(rule.Id).Where(use => use.BySubstitution).Sum(use => use.Credits);
                    if (counted < general.Hours) electives.Add(general.Hours - counted);
                    break;
                case Effective.KindEffective kind:
                    if (NeedFor(rule.Id, kind.RuleKind, kind.AnyCourse, free, index) is Need need) needs.Add(need);
                    break;
            }
        }

        var scale = _rules.Count + 1;
        int ElectivesMet(CourseBits available)
        {
            if (electives.Count == 0) return 0;
            var left = 0.0;
            for (var i = 0; i < credits.Length; i++)
                if (available.Has(i)) left += credits[i];
            var met = 0;
            foreach (var hours in electives)
            {
                if (left < hours) continue;
                met++;
                left -= hours;
            }
            return met;
        }

        var now = CourseBits.Empty;
        foreach (var id in _available)
            if (index.TryGetValue(id, out var position)) now = now.Union(new CourseBits(position));
        var currentSpecific = needs.Count(need => IsMet(need.RuleId));
        var currentElectives = ElectivesMet(now);
        var current = currentSpecific * scale + currentElectives;
        var mayImprove = needs.Any(need => !IsMet(need.RuleId) && CanStillMeet(need, all, credits)) || ElectivesMet(all) > currentElectives;
        if (!mayImprove) return false;

        var order = Enumerable.Range(0, needs.Count)
            .Select(position => (Fills: Fills(needs[position], all, 400).Count, Index: position))
            .OrderBy(item => item.Fills).ThenBy(item => item.Index)
            .Select(item => item.Index).ToArray();
        Dictionary<int, List<int>>? best = null;
        var bestScore = current;
        var bestSpecific = currentSpecific;
        var bestElectives = currentElectives;
        var chosen = new Dictionary<int, List<int>>();
        var visited = new Dictionary<(int Depth, ulong Low, ulong High), int>();
        var nodes = 0;
        const int budget = 20_000;

        void Visit(int depth, CourseBits available, int met)
        {
            nodes++;
            if (nodes > budget) return;
            var key = (depth, available.Low, available.High);
            if (visited.TryGetValue(key, out var earlier) && earlier >= met) return;
            visited[key] = met;
            var optimistic = met;
            for (var later = depth; later < order.Length; later++)
                if (CanStillMeet(needs[order[later]], available, credits)) optimistic++;
            var electivesAtBest = ElectivesMet(available);
            if (optimistic * scale + electivesAtBest <= bestScore) return;
            if (depth >= order.Length)
            {
                bestScore = optimistic * scale + electivesAtBest;
                bestSpecific = optimistic;
                bestElectives = electivesAtBest;
                best = chosen.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
                return;
            }
            var n = order[depth];
            foreach (var fill in Fills(needs[n], available, 400))
            {
                var taken = CourseBits.Empty;
                foreach (var unitIndex in fill) taken = taken.Union(needs[n].Units[unitIndex].Bits);
                chosen[n] = fill;
                Visit(depth + 1, available.Subtracting(taken), met + 1);
                chosen.Remove(n);
                if (nodes > budget) return;
            }
            Visit(depth + 1, available, met);
        }
        Visit(0, all, 0);
        if (best is null) return false;
        var meetsEverything = bestSpecific == needs.Count && bestElectives == electives.Count;
        if (bestSpecific <= currentSpecific && !meetsEverything) return false;
        Adopt(best, needs, free);
        return true;
    }

    private CourseKey? SingleMatch(DegreeAudit.CourseUse use, string ruleId)
    {
        if (_effective.GetValueOrDefault(ruleId) is not Effective.KindEffective { RuleKind: RequirementKind.Choose choose } kind) return null;
        if (choose.Count is null && use.Credits <= 0) return null;
        var counted = Uses(ruleId).Select(item => item.Satisfies ?? item.Key).ToHashSet();
        foreach (var option in choose.Options)
        {
            if (option.AllCourses.Any(counted.Contains)) continue;
            foreach (var alternative in option.Alternatives)
            {
                if (alternative.Count == 1 && _equivalences.AreEquivalent(alternative[0], use.Key)) return alternative[0];
            }
        }
        if (use.Credits <= 0 || (choose.Count is not null && use.Credits < 2)) return null;
        if (kind.AnyCourse) return use.Key;
        return choose.Ranges.Count == 0 ? null : CourseInRange(use.Key, choose.Ranges);
    }

    private bool ChoiceIsSatisfied(string ruleId)
    {
        if (_effective.GetValueOrDefault(ruleId) is not Effective.KindEffective { RuleKind: RequirementKind.Choose choose }) return true;
        return IsSatisfied(ruleId, choose.Count, choose.Hours);
    }

    private double Shortfall(string ruleId)
    {
        if (_effective.GetValueOrDefault(ruleId) is not Effective.KindEffective { RuleKind: RequirementKind.Choose { Count: null, Hours: double hours } })
            return 0;
        return Math.Max(0, hours - Uses(ruleId).Sum(use => use.Credits));
    }

    private void Place(string id, string ruleId, CourseKey named)
    {
        Consume(id, ruleId, satisfying: named);
        _optionsCounted[ruleId] = _optionsCounted.GetValueOrDefault(ruleId) + 1;
        MarkSingle(ruleId, id);
    }

    private void Release(string id, string ruleId)
    {
        if (!_used.TryGetValue(ruleId, out var list)) return;
        var index = list.FindIndex(use => use.Id == id);
        if (index < 0) return;
        list.RemoveAt(index);
        _optionsCounted[ruleId] = _optionsCounted.GetValueOrDefault(ruleId) - 1;
        if (_singles.TryGetValue(ruleId, out var singles)) singles.Remove(id);
        _available.Add(id);
    }

    private void ReleaseAll(string ruleId)
    {
        foreach (var use in Uses(ruleId)) _available.Add(use.Id);
        _used[ruleId] = [];
        _optionsCounted[ruleId] = 0;
        _singles[ruleId] = new HashSet<string>(StringComparer.Ordinal);
    }

    private bool IsSatisfied(string ruleId, int? count, double? hours)
    {
        var counted = _optionsCounted.GetValueOrDefault(ruleId);
        if (count is int needed) return counted >= needed;
        if (hours is double neededHours) return Uses(ruleId).Sum(use => use.Credits) >= neededHours;
        return counted >= 1;
    }

    private (int Planned, int Contested) Score(IReadOnlyList<CourseKey> alternative)
    {
        var taken = Assign(alternative).Select(pair => _pool.GetValueOrDefault(pair.Taken)).OfType<DegreeAudit.CourseUse>().ToList();
        return (taken.Count(use => !use.IsCompleted), taken.Sum(use => Contest(use.Key)));
    }

    private int Contest(CourseKey key) => _equivalences.GroupOf(key).Sum(member => _demand.GetValueOrDefault(member));

    private void Consume(string id, string ruleId, bool bySubstitution = false, CourseKey? satisfying = null)
    {
        if (!_available.Contains(id) || !_pool.TryGetValue(id, out var source)) return;
        var use = source.Copy();
        use.BySubstitution = bySubstitution;
        use.Satisfies = satisfying is CourseKey named && named != use.Key ? named : null;
        _available.Remove(id);
        if (!_used.TryGetValue(ruleId, out var list)) _used[ruleId] = list = [];
        list.Add(use);
    }

    private string? StandIn(CourseKey required, HashSet<string>? claimed = null)
    {
        bool Free(string id) => _available.Contains(id) && _pool[id].CountsTowardHours && (claimed is null || !claimed.Contains(id));
        if (_instances.TryGetValue(required, out var own))
        {
            var best = MinByComesFirst(own.Where(Free));
            if (best is not null) return best;
        }
        if (_equivalences.IsEmpty) return null;
        var candidates = _equivalences.EquivalentsOf(required).SelectMany(key =>
            _instances.TryGetValue(key, out var ids) ? ids : []);
        return MinByComesFirst(candidates.Where(Free));
    }

    private bool ComesFirst(string a, string b)
    {
        static (int Completed, int Order) Rank(DegreeAudit.CourseUse? use) =>
            (use?.IsCompleted == true ? 0 : 1, use?.Order ?? int.MaxValue);
        var first = Rank(_pool.GetValueOrDefault(a));
        var second = Rank(_pool.GetValueOrDefault(b));
        return first != second ? first.CompareTo(second) < 0 : string.CompareOrdinal(a, b) < 0;
    }

    private string? MinByComesFirst(IEnumerable<string> ids)
    {
        string? best = null;
        foreach (var id in ids)
            if (best is null || ComesFirst(id, best)) best = id;
        return best;
    }

    private List<(CourseKey Required, string Taken)> Assign(IReadOnlyList<CourseKey> alternative)
    {
        var named = new HashSet<CourseKey>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var matches = new List<(CourseKey Required, string Taken)>();
        foreach (var required in alternative)
        {
            if (!named.Add(required)) continue;
            var taken = StandIn(required, claimed);
            if (taken is null) continue;
            claimed.Add(taken);
            matches.Add((required, taken));
        }
        return matches;
    }

    private bool IsComplete(IReadOnlyList<CourseKey> alternative) => Assign(alternative).Count == alternative.Distinct().Count();

    private CourseKey? CourseInRange(CourseKey key, IReadOnlyList<CourseRange> ranges)
    {
        if (ranges.Any(range => range.Contains(key))) return key;
        foreach (var equivalent in _equivalences.EquivalentsOf(key).OrderBy(item => item))
            if (ranges.Any(range => range.Contains(equivalent))) return equivalent;
        return null;
    }

    private string? Row(Guid rowId)
    {
        foreach (var (id, use) in _pool)
            if (use.RowId == rowId && _available.Contains(id)) return id;
        return null;
    }

    private List<DegreeAudit.CourseUse> Remaining() =>
        _available.Select(id => _pool[id]).Where(use => use.CountsTowardHours).OrderBy(use => use.IsCompleted ? 0 : 1).ThenBy(use => use.Order).ToList();

    private DegreeAudit.RuleResult ResultFor(RequirementRule rule)
    {
        var uses = Uses(rule.Id).ToList();
        var substitutions = Subs(rule.Id);
        if (_effective.GetValueOrDefault(rule.Id) is not Effective.KindEffective kind)
        {
            return new DegreeAudit.RuleResult
            {
                Rule = rule,
                Status = DegreeAudit.Status.Waived,
                Used = [],
                Substitutions = substitutions,
            };
        }
        var hoursDone = uses.Sum(use => use.Credits);
        bool satisfied;
        int? remainingCount = null;
        double? remainingHours = null;
        switch (kind.RuleKind)
        {
            case RequirementKind.Required required:
                var keys = uses.Select(use => use.Satisfies ?? use.Key).ToHashSet();
                satisfied = uses.Any(use => use.BySubstitution) || required.Option.Alternatives.Any(alternative => alternative.All(keys.Contains));
                break;
            case RequirementKind.Choose choose:
                satisfied = IsSatisfied(rule.Id, choose.Count, choose.Hours);
                if (choose.Count is int count) remainingCount = Math.Max(0, count - _optionsCounted.GetValueOrDefault(rule.Id));
                else if (choose.Hours is double hours) remainingHours = Math.Max(0, hours - hoursDone);
                else remainingCount = satisfied ? 0 : 1;
                break;
            case RequirementKind.GeneralElectives general:
                satisfied = hoursDone >= general.Hours;
                remainingHours = Math.Max(0, general.Hours - hoursDone);
                break;
            default:
                satisfied = false;
                break;
        }
        var status = satisfied
            ? (uses.All(use => use.IsCompleted) ? DegreeAudit.Status.Complete : DegreeAudit.Status.Planned)
            : (uses.Count == 0 ? DegreeAudit.Status.Missing : DegreeAudit.Status.Partial);
        return new DegreeAudit.RuleResult
        {
            Rule = rule,
            EffectiveKind = kind.RuleKind,
            Status = status,
            Used = uses,
            RemainingCount = remainingCount,
            RemainingHours = remainingHours,
            Substitutions = substitutions,
        };
    }

    private Need? NeedFor(string ruleId, RequirementKind kind, bool anyCourse, List<string> free, Dictionary<string, int> index)
    {
        var pinned = Uses(ruleId).Where(use => use.BySubstitution).ToList();
        var freeSet = free.ToHashSet(StringComparer.Ordinal);
        switch (kind)
        {
            case RequirementKind.Required required:
                if (pinned.Count > 0) return null;
                var requiredUnits = required.Option.Alternatives
                    .SelectMany(alternative => Assignments(alternative, freeSet).Select(pairs => MakeUnit(pairs, 0, index)))
                    .OrderBy(unit => unit, UnitPreference.Instance).ToList();
                return new Need(ruleId, Need.AmountKind.Alternative, 0, 0, requiredUnits);
            case RequirementKind.Choose choose:
                var counted = pinned.Select(use => use.Satisfies ?? use.Key).ToHashSet();
                var units = new List<Unit>();
                for (var number = 0; number < choose.Options.Count; number++)
                {
                    var option = choose.Options[number];
                    if (option.AllCourses.Any(counted.Contains)) continue;
                    foreach (var alternative in option.Alternatives)
                        units.AddRange(Assignments(alternative, freeSet).Select(pairs => MakeUnit(pairs, number, index)));
                }
                if (choose.Ranges.Count > 0 || anyCourse)
                {
                    var tag = choose.Options.Count;
                    foreach (var id in free)
                    {
                        if (!_pool.TryGetValue(id, out var use) || use.Credits <= 0 || (choose.Count is not null && use.Credits < 2)) continue;
                        var named = anyCourse ? use.Key : CourseInRange(use.Key, choose.Ranges);
                        if (named is null) continue;
                        units.Add(MakeUnit([(named.Value, id)], tag, index));
                        tag++;
                    }
                }
                Need.AmountKind amount;
                int countValue = 0;
                double hoursValue = 0;
                if (choose.Count is int count)
                {
                    if (count - pinned.Count <= 0) return null;
                    amount = Need.AmountKind.Count;
                    countValue = count - pinned.Count;
                }
                else if (choose.Hours is double hours)
                {
                    var left = hours - pinned.Sum(use => use.Credits);
                    if (left <= 0) return null;
                    amount = Need.AmountKind.Hours;
                    hoursValue = left;
                    units.RemoveAll(unit => unit.Credits <= 0);
                }
                else
                {
                    if (pinned.Count > 0) return null;
                    amount = Need.AmountKind.Count;
                    countValue = 1;
                }
                return new Need(ruleId, amount, countValue, hoursValue, units.OrderBy(unit => unit, UnitPreference.Instance).ToList());
            default:
                return null;
        }
    }

    private List<List<(CourseKey Named, string Taken)>> Assignments(IReadOnlyList<CourseKey> alternative, HashSet<string> freeSet)
    {
        var seen = new HashSet<CourseKey>();
        var named = alternative.Where(seen.Add).ToList();
        var results = new List<List<(CourseKey Named, string Taken)>>();
        var current = new List<(CourseKey Named, string Taken)>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        void Extend(int position)
        {
            if (results.Count >= 4) return;
            if (position >= named.Count)
            {
                results.Add(current.ToList());
                return;
            }
            var key = named[position];
            var candidates = new List<string>();
            if (_instances.TryGetValue(key, out var own)) candidates.AddRange(own);
            foreach (var equivalent in _equivalences.EquivalentsOf(key).OrderBy(item => item))
                if (_instances.TryGetValue(equivalent, out var ids)) candidates.AddRange(ids);
            foreach (var id in candidates)
            {
                if (!freeSet.Contains(id) || !_pool[id].CountsTowardHours || !claimed.Add(id)) continue;
                current.Add((key, id));
                Extend(position + 1);
                claimed.Remove(id);
                current.RemoveAt(current.Count - 1);
            }
        }
        Extend(0);
        return results;
    }

    private Unit MakeUnit(IReadOnlyList<(CourseKey Named, string Taken)> pairs, int option, Dictionary<string, int> index)
    {
        var uses = pairs.Select(pair => _pool.GetValueOrDefault(pair.Taken)).OfType<DegreeAudit.CourseUse>().ToList();
        var bits = CourseBits.Empty;
        foreach (var pair in pairs)
            if (index.TryGetValue(pair.Taken, out var position)) bits = bits.Union(new CourseBits(position));
        return new Unit(
            pairs.Select(pair => pair.Taken).ToList(),
            pairs.Select(pair => pair.Named).ToList(),
            bits,
            option,
            uses.Sum(use => Math.Max(0, use.Credits)),
            uses.Count == 0 || uses.All(use => use.IsCompleted),
            uses.Count == 0 ? 0 : uses.Min(use => use.Order));
    }

    private static List<List<int>> Fills(Need need, CourseBits available, int limit)
    {
        var usable = need.Units.Select((unit, position) => (unit, position)).Where(item => available.Contains(item.unit.Bits)).Select(item => item.position).ToList();
        var result = new List<List<int>>();
        var chosen = new List<int>();
        switch (need.Amount)
        {
            case Need.AmountKind.Alternative:
                return usable.Take(limit).Select(position => new List<int> { position }).ToList();
            case Need.AmountKind.Count:
                void PickCount(int start, CourseBits taken, HashSet<int> options)
                {
                    if (chosen.Count == need.Count)
                    {
                        result.Add(chosen.ToList());
                        return;
                    }
                    if (usable.Count - start < need.Count - chosen.Count) return;
                    for (var position = start; position < usable.Count && result.Count < limit; position++)
                    {
                        var unit = need.Units[usable[position]];
                        if (taken.Intersects(unit.Bits) || !options.Add(unit.Option)) continue;
                        chosen.Add(usable[position]);
                        PickCount(position + 1, taken.Union(unit.Bits), options);
                        options.Remove(unit.Option);
                        chosen.RemoveAt(chosen.Count - 1);
                    }
                }
                PickCount(0, CourseBits.Empty, []);
                break;
            default:
                var later = new double[usable.Count + 1];
                for (var position = usable.Count - 1; position >= 0; position--)
                    later[position] = later[position + 1] + need.Units[usable[position]].Credits;
                void PickHours(int start, CourseBits taken, HashSet<int> options, double creditsSoFar)
                {
                    if (creditsSoFar >= need.Hours)
                    {
                        if (chosen.All(position => creditsSoFar - need.Units[position].Credits < need.Hours)) result.Add(chosen.ToList());
                        return;
                    }
                    if (creditsSoFar + later[start] < need.Hours) return;
                    for (var position = start; position < usable.Count && result.Count < limit; position++)
                    {
                        var unit = need.Units[usable[position]];
                        if (taken.Intersects(unit.Bits) || !options.Add(unit.Option)) continue;
                        chosen.Add(usable[position]);
                        PickHours(position + 1, taken.Union(unit.Bits), options, creditsSoFar + unit.Credits);
                        options.Remove(unit.Option);
                        chosen.RemoveAt(chosen.Count - 1);
                    }
                }
                PickHours(0, CourseBits.Empty, [], 0);
                break;
        }
        return result;
    }

    private static bool CanStillMeet(Need need, CourseBits available, double[] credits)
    {
        switch (need.Amount)
        {
            case Need.AmountKind.Alternative:
                return need.Units.Any(unit => available.Contains(unit.Bits));
            case Need.AmountKind.Count:
                var options = new HashSet<int>();
                foreach (var unit in need.Units)
                {
                    if (!available.Contains(unit.Bits)) continue;
                    options.Add(unit.Option);
                    if (options.Count >= need.Count) return true;
                }
                return false;
            default:
                var covered = CourseBits.Empty;
                foreach (var unit in need.Units)
                    if (available.Contains(unit.Bits)) covered = covered.Union(unit.Bits);
                var sum = 0.0;
                for (var i = 0; i < credits.Length; i++)
                    if (covered.Has(i)) sum += credits[i];
                return sum >= need.Hours;
        }
    }

    private bool IsMet(string ruleId)
    {
        switch (_effective.GetValueOrDefault(ruleId))
        {
            case Effective.KindEffective { RuleKind: RequirementKind.Required required }:
                var uses = Uses(ruleId);
                var keys = uses.Select(use => use.Satisfies ?? use.Key).ToHashSet();
                return uses.Any(use => use.BySubstitution) || required.Option.Alternatives.Any(alternative => alternative.All(keys.Contains));
            case Effective.KindEffective { RuleKind: RequirementKind.Choose choose }:
                return IsSatisfied(ruleId, choose.Count, choose.Hours);
            case Effective.KindEffective { RuleKind: RequirementKind.GeneralElectives general }:
                return Uses(ruleId).Sum(use => use.Credits) >= general.Hours;
            default:
                return true;
        }
    }

    private void Adopt(Dictionary<int, List<int>> fills, List<Need> needs, List<string> free)
    {
        foreach (var need in needs)
        {
            var pinned = Uses(need.RuleId).Where(use => use.BySubstitution).ToList();
            _used[need.RuleId] = pinned;
            _optionsCounted[need.RuleId] = pinned.Count;
            _singles[need.RuleId] = new HashSet<string>(StringComparer.Ordinal);
        }
        _available = free.ToHashSet(StringComparer.Ordinal);
        foreach (var (n, chosen) in fills)
        {
            var ruleId = needs[n].RuleId;
            foreach (var position in chosen)
            {
                var unit = needs[n].Units[position];
                for (var i = 0; i < unit.Courses.Count; i++) Consume(unit.Courses[i], ruleId, satisfying: unit.Named[i]);
                _optionsCounted[ruleId] = _optionsCounted.GetValueOrDefault(ruleId) + 1;
                if (unit.Courses.Count == 1) MarkSingle(ruleId, unit.Courses[0]);
            }
        }
    }

    private AuditEngine Clone()
    {
        var copy = (AuditEngine)MemberwiseClone();
        copy._available = new HashSet<string>(_available, StringComparer.Ordinal);
        copy._used = _used.ToDictionary(pair => pair.Key, pair => pair.Value.Select(use => use.Copy()).ToList(), StringComparer.Ordinal);
        copy._optionsCounted = new Dictionary<string, int>(_optionsCounted, StringComparer.Ordinal);
        copy._singles = _singles.ToDictionary(pair => pair.Key, pair => new HashSet<string>(pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        return copy;
    }

    private void CopyFrom(AuditEngine other)
    {
        _available = other._available;
        _used.Clear();
        foreach (var (key, value) in other._used) _used[key] = value;
        _optionsCounted.Clear();
        foreach (var (key, value) in other._optionsCounted) _optionsCounted[key] = value;
        _singles.Clear();
        foreach (var (key, value) in other._singles) _singles[key] = value;
    }

    private bool CountsCourses(RequirementRule rule) =>
        _effective.GetValueOrDefault(rule.Id) is Effective.KindEffective { RuleKind: RequirementKind.Choose { Count: not null } };

    private static bool IsCoop(DegreeAudit.CourseUse use) =>
        use.Key.Subject == "COOP" || CoopTitlePattern().IsMatch(use.Title);

    private static bool ContainsWriting(string text) => text.Contains("writing", StringComparison.OrdinalIgnoreCase);

    private HashSet<NUPath>? Nupaths(DegreeAudit.CourseUse use)
    {
        if (_attributes.Nupaths.TryGetValue(use.Key, out var paths)) return paths;
        if (IsCoop(use)) return [];
        foreach (var other in _equivalences.GroupOf(use.Key))
        {
            if (other == use.Key) continue;
            if (_attributes.Nupaths.TryGetValue(other, out var equivalent)) return equivalent;
        }
        return null;
    }

    private List<DegreeAudit.CourseUse> SortedPool() => _pool.Values.OrderBy(use => use.Order).ToList();

    private static string Describe(IReadOnlyList<DegreeAudit.CourseUse> uses) =>
        string.Join(" \u00b7 ", uses.Select(use => use.IsCompleted
            ? $"{use.Key} completed"
            : $"{use.Key} planned{(use.Semester is string semester ? $" \u00b7 {semester}" : "")}"));

    private static string GpaText(double gpa)
    {
        var text = gpa.ToString("0.###", CultureInfo.InvariantCulture);
        if (!text.Contains('.', StringComparison.Ordinal)) text += ".0";
        return text;
    }

    private List<DegreeAudit.CourseUse> Uses(string ruleId) => _used.TryGetValue(ruleId, out var list) ? list : [];

    private List<RequirementSubstitution> Subs(string ruleId) =>
        _substitutionsByRule.TryGetValue(ruleId, out var list) ? list : [];

    private void AddSub(string ruleId, RequirementSubstitution substitution)
    {
        if (!_substitutionsByRule.TryGetValue(ruleId, out var list))
            _substitutionsByRule[ruleId] = list = [];
        list.Add(substitution);
    }

    private void MarkSingle(string ruleId, string id)
    {
        if (!_singles.TryGetValue(ruleId, out var set))
            _singles[ruleId] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(id);
    }

    private static Regex CoopTitlePattern() => new(@"(?i)co-?op\s+(?:work\s+)?experience|cooperative\s+education", RegexOptions.CultureInvariant);

    private struct Pick
    {
        public int Completed;
        public int Count;
        public int Order;
        public List<int> Items = [];
        public Pick() { }
        public bool IsBetterThan(Pick other) =>
            (-Completed, Count, Order).CompareTo((-other.Completed, other.Count, other.Order)) < 0;
    }

    private readonly record struct CourseBits(ulong Low, ulong High)
    {
        public const int Capacity = 128;
        public static CourseBits Empty => new(0, 0);
        public CourseBits(int index) : this(index < 64 ? 1UL << index : 0, index < 64 ? 0 : 1UL << (index - 64)) { }
        public bool Contains(CourseBits other) => (Low & other.Low) == other.Low && (High & other.High) == other.High;
        public bool Intersects(CourseBits other) => (Low & other.Low) != 0 || (High & other.High) != 0;
        public bool Has(int index) => index < 64 ? (Low & (1UL << index)) != 0 : (High & (1UL << (index - 64))) != 0;
        public CourseBits Union(CourseBits other) => new(Low | other.Low, High | other.High);
        public CourseBits Subtracting(CourseBits other) => new(Low & ~other.Low, High & ~other.High);
    }

    private sealed class Need(string ruleId, Need.AmountKind amount, int count, double hours, List<AuditEngine.Unit> units)
    {
        public enum AmountKind { Alternative, Count, Hours }
        public string RuleId { get; } = ruleId;
        public AmountKind Amount { get; } = amount;
        public int Count { get; } = count;
        public double Hours { get; } = hours;
        public List<Unit> Units { get; } = units;
    }

    private sealed class Unit(List<string> courses, List<CourseKey> named, CourseBits bits, int option, double credits, bool isCompleted, int order)
    {
        public List<string> Courses { get; } = courses;
        public List<CourseKey> Named { get; } = named;
        public CourseBits Bits { get; } = bits;
        public int Option { get; } = option;
        public double Credits { get; } = credits;
        public bool IsCompleted { get; } = isCompleted;
        public int Order { get; } = order;
    }

    private sealed class UnitPreference : IComparer<Unit>
    {
        public static readonly UnitPreference Instance = new();
        public int Compare(Unit? x, Unit? y)
        {
            if (x is null || y is null) return 0;
            var completed = (x.IsCompleted ? 0 : 1).CompareTo(y.IsCompleted ? 0 : 1);
            if (completed != 0) return completed;
            var count = x.Courses.Count.CompareTo(y.Courses.Count);
            return count != 0 ? count : x.Order.CompareTo(y.Order);
        }
    }
}
