// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core.Tests;

public class UserDataStoreTests
{
    [Fact]
    public void FirstPlanOfATermIsPrimaryAndDuplicateCopiesPreferences()
    {
        var path = Path.Combine(Path.GetTempPath(), "nto-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new UserDataStore(path);
            var plan = store.CreatePlan("Fall", "202710");
            var other = store.CreatePlan("Other", "202710");
            Assert.True(plan.IsPrimary);
            Assert.False(other.IsPrimary);
            var preferences = new SchedulePreferences { Campus = "Boston", DaysOff = DaySet.Friday };
            store.UpdatePlan(plan.Id, p => p.Preferences = preferences);
            store.FlushNow();

            var reloaded = new UserDataStore(path);
            Assert.Equal("Boston", reloaded.Plan(plan.Id)!.Preferences!.Campus);
            Assert.Null(reloaded.Plan(other.Id)!.Preferences);
            var copy = reloaded.DuplicatePlan(plan.Id)!;
            Assert.False(copy.IsPrimary);
            Assert.Equal("Boston", reloaded.Plan(copy.Id)!.Preferences!.Campus);
            Assert.NotEqual(plan.Id, copy.Id);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DeletingThePrimaryPlanPromotesTheNext()
    {
        var path = Path.Combine(Path.GetTempPath(), "nto-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new UserDataStore(path);
        var first = store.CreatePlan("A", "202710");
        var second = store.CreatePlan("B", "202710");
        store.DeletePlan(first.Id);
        Assert.True(store.Plan(second.Id)!.IsPrimary);
        File.Delete(path);
    }

    [Fact]
    public void LicenseTextLoads()
    {
        var text = LegalText.LoadLicenseText();
        Assert.NotNull(text);
        Assert.Contains("GNU AFFERO GENERAL PUBLIC LICENSE", text);
        Assert.Equal("Not affiliated with, endorsed by, or sponsored by Northeastern University or Sandbox.", LegalText.UnofficialNotice);
    }
}

public class BannerSmokeTests
{
    [Fact]
    public async Task LiveBannerListsFall2026()
    {
        var terms = await BannerTermList.FetchAsync();
        Assert.Contains(terms, term => term.Code == "202710" && term.Name.Contains("Fall 2026"));
    }
}
