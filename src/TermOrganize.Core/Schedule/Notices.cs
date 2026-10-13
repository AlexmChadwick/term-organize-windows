// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// Portions derived from SearchNEU where the Swift file says so. Modified 2026-10-13: ported from Swift to C#.

namespace TermOrganize.Core;

/// <summary>A short line for a schedule card: "1 conflict", "1 full section".</summary>
public sealed class ScheduleWarning : IEquatable<ScheduleWarning>
{
    public enum Severity { Error, Warning, Neutral }

    public string Text { get; }
    public string Symbol { get; }
    public Severity Level { get; }
    public string Id => Text;

    public ScheduleWarning(string text, string symbol, Severity level)
    {
        Text = text;
        Symbol = symbol;
        Level = level;
    }

    public bool Equals(ScheduleWarning? other) => other is not null && Text == other.Text && Symbol == other.Symbol && Level == other.Level;
    public override bool Equals(object? obj) => Equals(obj as ScheduleWarning);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);
}

public static class ResolvedPlanInfo
{
    /// <summary>What needs attention, most serious first. Empty while the catalog is not loaded.</summary>
    public static List<ScheduleWarning> Warnings(this ResolvedPlan resolved)
    {
        var items = new List<ScheduleWarning>();
        if (!resolved.HasCatalog) return items;
        if (resolved.Conflicts.Count > 0)
        {
            var count = resolved.Conflicts.Count;
            items.Add(new ScheduleWarning(count == 1 ? "1 conflict" : $"{count} conflicts", "exclamationmark.triangle.fill", ScheduleWarning.Severity.Error));
        }
        if (resolved.FullSections.Count > 0)
        {
            var count = resolved.FullSections.Count;
            items.Add(new ScheduleWarning(count == 1 ? "1 full section" : $"{count} full sections", "chair", ScheduleWarning.Severity.Warning));
        }
        if (resolved.MissingCourses.Count > 0)
        {
            var count = resolved.MissingCourses.Count;
            items.Add(new ScheduleWarning(count == 1 ? "1 course not offered" : $"{count} courses not offered", "questionmark.folder", ScheduleWarning.Severity.Warning));
        }
        if (resolved.IsOverCreditLimit)
            items.Add(new ScheduleWarning("Over 19 credits", "exclamationmark.circle", ScheduleWarning.Severity.Warning));
        var missingSections = resolved.CoursesWithoutSection.Count(entry => entry.PlanCourse.IsRequired);
        if (missingSections > 0 && resolved.Plan.Courses.Count > 0)
            items.Add(new ScheduleWarning(missingSections == 1 ? "1 course needs a section" : $"{missingSections} courses need sections", "circle.dashed", ScheduleWarning.Severity.Neutral));
        return items;
    }
}

public sealed class ScheduleNotice : IEquatable<ScheduleNotice>
{
    public enum Severity { Error, Warning, Info }

    public abstract class Fix
    {
        public sealed class RemoveSections : Fix, IEquatable<RemoveSections>
        {
            public List<string> Crns { get; }
            public RemoveSections(List<string> crns) => Crns = crns;
            public bool Equals(RemoveSections? other) => other is not null && Crns.SequenceEqual(other.Crns);
            public override bool Equals(object? obj) => Equals(obj as RemoveSections);
            public override int GetHashCode()
            {
                var hash = new HashCode();
                foreach (var crn in Crns) hash.Add(crn, StringComparer.Ordinal);
                return hash.ToHashCode();
            }
        }
    }

    public string Id { get; set; } = "";
    public Severity Level { get; set; }
    public string Title { get; set; } = "";
    public List<string> Lines { get; set; } = [];
    public Fix? NoticeFix { get; set; }

    public string? FixTitle => NoticeFix is Fix.RemoveSections ? "Remove" : null;

    public bool Equals(ScheduleNotice? other) =>
        other is not null && Id == other.Id && Level == other.Level && Title == other.Title &&
        Lines.SequenceEqual(other.Lines) && Equals(NoticeFix, other.NoticeFix);

    public override bool Equals(object? obj) => Equals(obj as ScheduleNotice);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);
}

public static class ScheduleNotices
{
    public static List<ScheduleNotice> Make(ResolvedPlan resolved, string termName, bool catalogUnavailable)
    {
        var items = new List<ScheduleNotice>();
        if (!resolved.HasCatalog && catalogUnavailable && resolved.Plan.Courses.Count > 0)
        {
            items.Add(new ScheduleNotice
            {
                Id = "catalog",
                Level = ScheduleNotice.Severity.Warning,
                Title = $"Course data for {termName} isn\u2019t on this device",
                Lines = ["Switch to that term and download its catalog to see sections, times and seats."],
            });
        }
        if (resolved.Conflicts.Count > 0)
        {
            var count = resolved.Conflicts.Count;
            var lines = resolved.Conflicts.Take(3).Select(c => c.Summary).ToList();
            if (count > 3) lines.Add($"and {count - 3} more");
            items.Add(new ScheduleNotice
            {
                Id = "conflicts",
                Level = ScheduleNotice.Severity.Error,
                Title = count == 1 ? "1 time conflict" : $"{count} time conflicts",
                Lines = lines,
            });
        }
        if (resolved.FullSections.Count > 0)
        {
            var lines = resolved.FullSections.Select(pair =>
            {
                var waitlist = pair.Section.WaitlistCapacity > 0 ? $" \u00B7 {pair.Section.WaitlistRemaining} waitlist spots left" : "";
                return $"{pair.Section.CourseKey} {ScheduleFormat.SectionTitle(pair.Section)} is full{waitlist}";
            }).ToList();
            items.Add(new ScheduleNotice
            {
                Id = "full",
                Level = ScheduleNotice.Severity.Warning,
                Title = resolved.FullSections.Count == 1 ? "1 section is full" : $"{resolved.FullSections.Count} sections are full",
                Lines = lines,
            });
        }
        if (resolved.MissingCourses.Count > 0)
        {
            items.Add(new ScheduleNotice
            {
                Id = "missing",
                Level = ScheduleNotice.Severity.Warning,
                Title = resolved.MissingCourses.Count == 1
                    ? $"1 course isn\u2019t offered in {termName}"
                    : $"{resolved.MissingCourses.Count} courses aren\u2019t offered in {termName}",
                Lines = resolved.MissingCourses.Select(key => $"{key} has no sections this term.").ToList(),
            });
        }
        if (resolved.IsOverCreditLimit)
        {
            items.Add(new ScheduleNotice
            {
                Id = "credits",
                Level = ScheduleNotice.Severity.Warning,
                Title = $"{resolved.CreditsLabel} is a heavy load",
                Lines = ["Above 19 credits you may need approval from your advisor or college."],
            });
        }
        if (resolved.UnlistedCRNs.Count > 0)
        {
            var unlisted = resolved.UnlistedCRNs;
            items.Add(new ScheduleNotice
            {
                Id = "unlisted",
                Level = ScheduleNotice.Severity.Info,
                Title = unlisted.Count == 1 ? "A saved section is no longer listed" : $"{unlisted.Count} saved sections are no longer listed",
                Lines = [$"CRN {string.Join(", ", unlisted)} isn\u2019t in the current {termName} catalog."],
                NoticeFix = new ScheduleNotice.Fix.RemoveSections(unlisted),
            });
        }
        if (resolved.OrphanedCRNs.Count > 0)
        {
            var orphaned = resolved.OrphanedCRNs;
            items.Add(new ScheduleNotice
            {
                Id = "orphaned",
                Level = ScheduleNotice.Severity.Info,
                Title = orphaned.Count == 1 ? "A section from a removed course is still selected" : $"{orphaned.Count} sections from removed courses are still selected",
                Lines = [$"CRN {string.Join(", ", orphaned)} doesn\u2019t belong to any course in this schedule."],
                NoticeFix = new ScheduleNotice.Fix.RemoveSections(orphaned),
            });
        }
        return items;
    }
}
