// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core.Tests;

public class UserDataMergeTests
{
    [Fact]
    public void DifferentPlansMergeAndTheSamePlanIsCopied()
    {
        var path = TempFile();
        try
        {
            var app = new UserDataStore(path);
            var mine = app.AddDegreePlan(new DegreePlan { Name = "Mine" });
            var shared = app.AddDegreePlan(new DegreePlan { Name = "Shared" });
            var schedule = app.CreatePlan("Week", "202710");

            var mcp = new UserDataStore(path);
            mcp.AddDegreePlan(new DegreePlan { Name = "From MCP" });
            mcp.UpdateDegreePlan(shared.Id, plan => plan.Name = "Shared from MCP");
            mcp.CreatePlan("Other week", "202730");

            app.DegreePlan(mine.Id)!.Name = "Mine renamed";
            app.DegreePlan(shared.Id)!.Name = "Shared from app";
            app.Plan(schedule.Id)!.Name = "Week from app";
            app.FlushNow();

            var saved = new UserDataStore(path);
            Assert.Equal("Mine renamed", saved.DegreePlan(mine.Id)!.Name);
            Assert.Contains(saved.DegreePlans, plan => plan.Name == "From MCP");
            Assert.Equal("Shared from MCP", saved.DegreePlan(shared.Id)!.Name);
            Assert.Contains(saved.DegreePlans, plan => plan.Name == "Shared from app (edited in app)" && plan.Id != shared.Id && !plan.IsPrimary);
            Assert.Equal("Week from app", saved.Plan(schedule.Id)!.Name);
            Assert.Contains(saved.Plans, plan => plan.Name == "Other week");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void ScheduleEditedOnBothSidesKeepsTheDiskCopy()
    {
        var path = TempFile();
        try
        {
            var app = new UserDataStore(path);
            var schedule = app.CreatePlan("Week", "202710");
            var mcp = new UserDataStore(path);
            mcp.UpdatePlan(schedule.Id, plan => plan.Name = "Week from MCP");
            app.UpdatePlan(schedule.Id, plan => plan.Name = "Week from app");

            var saved = new UserDataStore(path);
            Assert.Equal("Week from MCP", saved.Plan(schedule.Id)!.Name);
            Assert.Contains(saved.Plans, plan => plan.Name == "Week from app (edited in app)" && !plan.IsPrimary);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void UntouchedSaveDoesNotForkAndAnOutsideDeleteIsKept()
    {
        var path = TempFile();
        try
        {
            var app = new UserDataStore(path);
            var kept = app.AddDegreePlan(new DegreePlan { Name = "Kept" });
            var dropped = app.AddDegreePlan(new DegreePlan { Name = "Dropped" });
            app.UpdateDegreePlan(kept.Id, plan => plan.Program = "CS");
            var before = new UserDataStore(path);
            Assert.Equal(2, before.DegreePlans.Count);
            Assert.DoesNotContain(before.DegreePlans, plan => plan.Name.Contains("edited in app", StringComparison.Ordinal));

            var mcp = new UserDataStore(path);
            mcp.DeleteDegreePlan(dropped.Id);
            app.ReplacePreferences(new AppPreferences { PreferredCampus = "Boston" });

            var saved = new UserDataStore(path);
            Assert.Equal("Boston", saved.Preferences.PreferredCampus);
            Assert.Null(saved.DegreePlan(dropped.Id));
            Assert.Equal("Kept", saved.DegreePlan(kept.Id)!.Name);
            Assert.DoesNotContain(saved.DegreePlans, plan => plan.Name.Contains("edited in app", StringComparison.Ordinal));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void AppEditOfADeletedPlanIsSavedAsACopy()
    {
        var path = TempFile();
        try
        {
            var app = new UserDataStore(path);
            var plan = app.AddDegreePlan(new DegreePlan { Name = "Shared" });
            var mcp = new UserDataStore(path);
            mcp.DeleteDegreePlan(plan.Id);
            app.UpdateDegreePlan(plan.Id, stored => stored.Name = "Still editing");

            var saved = new UserDataStore(path);
            Assert.Null(saved.DegreePlan(plan.Id));
            Assert.Contains(saved.DegreePlans, item => item.Name == "Still editing (edited in app)");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task WatcherReloadsAnOutsideWrite()
    {
        var path = TempFile();
        var app = new UserDataStore(path);
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.ExternalEditApplied += () => seen.TrySetResult();
        app.WatchForExternalEdits(TimeSpan.FromMilliseconds(40));
        try
        {
            var mcp = new UserDataStore(path);
            mcp.AddDegreePlan(new DegreePlan { Name = "From MCP" });
            var finished = await Task.WhenAny(seen.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(seen.Task, finished);
            Assert.Contains(app.DegreePlans, plan => plan.Name == "From MCP");
        }
        finally
        {
            app.StopWatching();
            Delete(path);
        }
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "nto-merge-" + Guid.NewGuid().ToString("N") + ".json");

    private static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
    }
}
