// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace TermOrganize.Mcp;

[McpServerToolType]
public sealed class PlanReadTools
{
    private readonly PlanService _plans;

    public PlanReadTools(PlanService plans) => _plans = plans;

    [McpServerTool(Name = "list_plans", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists degree plans saved in the local UserData.json file the desktop app reads.")]
    public Dictionary<string, object?> list_plans() => _plans.List();

    [McpServerTool(Name = "get_plan", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Returns one saved plan, including sections and validation. Student name and id are omitted.")]
    public Dictionary<string, object?> get_plan(
        [Description("Plan UUID.")] string plan_id) => _plans.Get(plan_id);
}

[McpServerToolType]
public sealed class PlanWriteTools
{
    private readonly PlanService _plans;

    public PlanWriteTools(PlanService plans) => _plans = plans;

    [McpServerTool(Name = "create_plan", ReadOnly = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Creates a degree plan in the local UserData.json file.")]
    public Dictionary<string, object?> create_plan(
        [Description("Plan name.")] string name,
        [Description("Optional term labels, such as Fall 2026 or 202710.")] string[]? terms = null) =>
        _plans.Create(name, terms);

    [McpServerTool(Name = "rename_plan", ReadOnly = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Renames a plan when expected_revision still matches.")]
    public Dictionary<string, object?> rename_plan(
        [Description("Plan UUID.")] string plan_id,
        [Description("New name.")] string name,
        [Description("Revision from the last read.")] string expected_revision) =>
        _plans.Rename(plan_id, name, expected_revision);

    [McpServerTool(Name = "add_course", ReadOnly = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Adds a bundled course to a term. An unknown course or CRN is rejected.")]
    public Dictionary<string, object?> add_course(
        [Description("Plan UUID.")] string plan_id,
        [Description("Term label or Banner code.")] string term,
        [Description("Subject code.")] string subject,
        [Description("Course number.")] string number,
        [Description("Revision from the last read.")] string expected_revision,
        [Description("Optional section CRN.")] string? crn = null) =>
        _plans.AddCourse(plan_id, term, subject, number, crn, expected_revision);

    [McpServerTool(Name = "remove_course", ReadOnly = false, Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Removes a course from one term of a saved plan.")]
    public Dictionary<string, object?> remove_course(
        [Description("Plan UUID.")] string plan_id,
        [Description("Term label or Banner code.")] string term,
        [Description("Subject code.")] string subject,
        [Description("Course number.")] string number,
        [Description("Revision from the last read.")] string expected_revision) =>
        _plans.RemoveCourse(plan_id, term, subject, number, expected_revision);

    [McpServerTool(Name = "move_course", ReadOnly = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Moves a course from one term to another.")]
    public Dictionary<string, object?> move_course(
        [Description("Plan UUID.")] string plan_id,
        [Description("Subject code.")] string subject,
        [Description("Course number.")] string number,
        [Description("Term the course leaves.")] string from_term,
        [Description("Term the course joins.")] string to_term,
        [Description("Revision from the last read.")] string expected_revision) =>
        _plans.MoveCourse(plan_id, subject, number, from_term, to_term, expected_revision);

    [McpServerTool(Name = "set_section", ReadOnly = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Selects a CRN for a course already in the term.")]
    public Dictionary<string, object?> set_section(
        [Description("Plan UUID.")] string plan_id,
        [Description("Term label or Banner code.")] string term,
        [Description("Subject code.")] string subject,
        [Description("Course number.")] string number,
        [Description("Section CRN.")] string crn,
        [Description("Revision from the last read.")] string expected_revision) =>
        _plans.SetSection(plan_id, term, subject, number, crn, expected_revision);

    [McpServerTool(Name = "apply_reorganize", ReadOnly = false, Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Runs reorganize_plan on a saved plan and writes the result. Returns the moves.")]
    public Dictionary<string, object?> apply_reorganize(
        [Description("Plan UUID.")] string plan_id,
        [Description("Revision from the last read.")] string expected_revision) =>
        _plans.ApplyReorganize(plan_id, expected_revision);

    [McpServerTool(Name = "delete_plan", ReadOnly = false, Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Deletes a plan. Refuses unless confirm is true.")]
    public Dictionary<string, object?> delete_plan(
        [Description("Plan UUID.")] string plan_id,
        [Description("Revision from the last read.")] string expected_revision,
        [Description("Must be true.")] bool confirm = false) =>
        _plans.Delete(plan_id, expected_revision, confirm);
}
