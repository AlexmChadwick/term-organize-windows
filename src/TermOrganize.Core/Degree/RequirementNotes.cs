// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Which sentences of a catalog section the requirements page shows, with the audit's check.
// Term Organize is not affiliated with Northeastern University.

namespace TermOrganize.Core;

/// <summary>A sentence of the catalog's requirements and what the app could check of it.</summary>
public sealed class DegreeNoteEntry
{
    public required string SectionId { get; init; }
    public required string Text { get; init; }

    /// <summary>The check of the sentence. Null when the app can't check it.</summary>
    public DegreeAudit.NoteCheck? Check { get; init; }

    public string Id => $"{SectionId}|{Text}";
}

public static class DegreeRequirementsFilterNotes
{
    /// <summary>
    /// Whether a checked sentence shows for this filter. A check that doesn't count yet shows in All only.
    /// </summary>
    public static bool Includes(this DegreeRequirementsFilter filter, DegreeAudit.NoteCheck check) =>
        check.Counts ? filter.Includes(check.Status) : filter == DegreeRequirementsFilter.All;
}

/// <summary>Which sentences of a section the requirements page shows.</summary>
public static class DegreeRequirementNotes
{
    /// <summary>
    /// The section's sentences for a filter. All shows every sentence; Remaining and Done only the
    /// checked ones that match. A sentence in a group without requirements is shown only when it
    /// has a check. A total-hours sentence the page's header already states isn't repeated unless checked.
    /// </summary>
    public static List<DegreeNoteEntry> Entries(
        RequirementSection section,
        DegreeAudit audit,
        DegreeRequirementsFilter filter,
        bool statesTotalHours)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        DegreeNoteEntry? Entry(string note, bool showsUnchecked)
        {
            var entry = new DegreeNoteEntry
            {
                SectionId = section.Id,
                Text = note,
                Check = audit.NoteCheckFor(section.Id, note),
            };
            if (!seen.Add(entry.Id)) return null;
            if (entry.Check is DegreeAudit.NoteCheck check) return filter.Includes(check) ? entry : null;
            var isRestatedTotal = statesTotalHours && note.Contains("total semester hours", StringComparison.OrdinalIgnoreCase);
            return filter == DegreeRequirementsFilter.All && showsUnchecked && !isRestatedTotal ? entry : null;
        }

        var own = section.Notes.Select(note => Entry(note, true)).OfType<DegreeNoteEntry>();
        var inOtherGroups = section.Groups.Where(group => group.Rules.Count == 0)
            .SelectMany(group => group.Notes)
            .Select(note => Entry(note, false))
            .OfType<DegreeNoteEntry>();
        return own.Concat(inOtherGroups).ToList();
    }

    /// <summary>Only the checked ones, for a section that has requirements of its own.</summary>
    public static List<DegreeNoteEntry> CheckedEntries(
        RequirementSection section,
        DegreeAudit audit,
        DegreeRequirementsFilter filter) =>
        Entries(section, audit, filter, statesTotalHours: false).Where(entry => entry.Check is not null).ToList();

    /// <summary>Whether the page has something to show for a filter among the checked sentences.</summary>
    public static bool HasChecks(DegreeAudit audit, DegreeRequirementsFilter filter) =>
        audit.NoteChecks.Any(check => filter.Includes(check));
}
