// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;
using System.Text.Json;

namespace TermOrganize.Core.Tests;

public class CloudSyncPortTests
{
    private static readonly DateTimeOffset Date = DateTimeOffset.FromUnixTimeSeconds(1_790_000_000);
    private static readonly CourseKey Cs3000 = new("CS", "3000");
    private static readonly CourseKey Cs3500 = new("CS", "3500");

    private static SchedulePlan Plan(string name = "My Schedule", Guid? id = null, bool isPrimary = false, DateTimeOffset? updatedAt = null)
    {
        var plan = new SchedulePlan
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            TermCode = "202710",
            IsPrimary = isPrimary,
            CreatedAt = Date,
            UpdatedAt = updatedAt ?? Date,
            Courses =
            [
                new PlanCourse
                {
                    Key = Cs3000,
                    ExcludedCRNs = ["10001", "10002", "10003"],
                    ColorIndex = 0,
                },
            ],
            SelectedCRNs = ["10004"],
        };
        return plan;
    }

    private static DegreePlan Degree(Guid? id = null, DateTimeOffset? updatedAt = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "BSCS",
        Program = "CS-B-CSCI-BSCS",
        StudentName = "SAMPLE STUDENT",
        StudentID = "000000000",
        IsPrimary = true,
        CreatedAt = Date,
        UpdatedAt = updatedAt ?? Date,
        Completed = [new DegreePlanCourse(Cs3000, "Algorithms and Data", 4)],
        Terms = [new DegreePlanTerm
        {
            Season = PlanSeason.Spring,
            Year = 2027,
            Courses = [new DegreePlanCourse(Cs3500, "OOD", 4)],
        }],
    };

    private static WatchItem Watch(string crn = "12345", DateTimeOffset? addedAt = null) => new()
    {
        TermCode = "202710",
        Crn = crn,
        CourseKey = Cs3000,
        CourseTitle = "Algorithms and Data",
        SectionLabel = "Lecture · Section 01",
        AddedAt = addedAt ?? Date,
        LastSeatsRemaining = 0,
        LastWaitlistRemaining = 2,
        SeatCapacity = 40,
        LastCheckedAt = Date,
    };

    private static SavedCourse Saved(CourseKey? key = null, DateTimeOffset? savedAt = null) => new()
    {
        TermCode = "202710",
        Key = key ?? Cs3000,
        SavedAt = savedAt ?? Date,
    };

    private static CloudSyncMetadata Synced(UserData data)
    {
        var metadata = new CloudSyncMetadata();
        var snapshot = new CloudSyncSnapshot(data);
        metadata.ObserveLocalData(snapshot, Date);
        foreach (var (key, item) in snapshot.Items)
            metadata.RecordSaved(key, item.Fingerprint, [1]);
        return metadata;
    }

    private static CloudSyncIncomingRecord Incoming(CloudSyncEntity entity, DateTimeOffset modifiedAt, int schemaVersion = 1, Dictionary<string, DateTimeOffset>? preferenceChangedAt = null)
    {
        var payload = CloudSyncCoding.Payload(entity, preferenceChangedAt);
        return new CloudSyncIncomingRecord(entity.Key, schemaVersion, modifiedAt, payload, [2]);
    }

    [Fact]
    public void PayloadsRoundTrip()
    {
        var preferences = new AppPreferences { PreferredCampus = "Boston", CalendarStartHour = 9 };
        var schedule = Plan();
        var degree = Degree();
        var watch = Watch();
        var course = Saved();
        var changedAt = new Dictionary<string, DateTimeOffset> { ["preferredCampus"] = Date };

        var scheduleBack = CloudSyncCoding.Decode(CloudSyncCoding.Payload(CloudSyncEntity.Schedule(schedule)), CloudSyncRecordType.SchedulePlan);
        Assert.Equal(schedule.Name, ((CloudSyncRemoteValue.SchedulePlanValue)scheduleBack).Plan.Name);
        Assert.Equal(CloudSyncCoding.Fingerprint(CloudSyncEntity.Schedule(schedule)), CloudSyncCoding.Fingerprint(scheduleBack));

        var degreeBack = (CloudSyncRemoteValue.DegreePlanValue)CloudSyncCoding.Decode(CloudSyncCoding.Payload(CloudSyncEntity.Degree(degree)), CloudSyncRecordType.DegreePlan);
        Assert.Null(degreeBack.Plan.StudentName);
        Assert.Equal(CloudSyncCoding.Fingerprint(CloudSyncEntity.Degree(degree)), CloudSyncCoding.Fingerprint(degreeBack));

        Assert.Equal(CloudSyncCoding.Fingerprint(CloudSyncEntity.Watch(watch)), CloudSyncCoding.Fingerprint(CloudSyncCoding.Decode(CloudSyncCoding.Payload(CloudSyncEntity.Watch(watch)), CloudSyncRecordType.WatchItem)));
        Assert.Equal(CloudSyncCoding.Fingerprint(CloudSyncEntity.Saved(course)), CloudSyncCoding.Fingerprint(CloudSyncCoding.Decode(CloudSyncCoding.Payload(CloudSyncEntity.Saved(course)), CloudSyncRecordType.SavedCourse)));

        var prefs = (CloudSyncRemoteValue.PreferencesValue)CloudSyncCoding.Decode(
            CloudSyncCoding.Payload(CloudSyncEntity.Preferences(preferences), changedAt), CloudSyncRecordType.Preferences);
        Assert.Equal("Boston", prefs.Values.PreferredCampus);
        Assert.Equal(9, prefs.Values.CalendarStartHour);
        Assert.Equal(Date, prefs.ChangedAt["preferredCampus"]);
    }

    [Fact]
    public void ExtraFutureFieldsStillDecode()
    {
        var watch = Watch();
        using var doc = JsonDocument.Parse(CloudSyncCoding.Payload(CloudSyncEntity.Watch(watch)));
        var obj = JsonNodeFrom(doc);
        obj["fieldFromTheFuture"] = new System.Text.Json.Nodes.JsonObject { ["nested"] = true };
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(obj);
        var decoded = CloudSyncCoding.Decode(payload, CloudSyncRecordType.WatchItem);
        Assert.Equal(CloudSyncCoding.Fingerprint(CloudSyncEntity.Watch(watch)), CloudSyncCoding.Fingerprint(decoded));
    }

    [Fact]
    public void RecordNamesUseStableIds()
    {
        var schedule = Plan();
        Assert.Equal(schedule.Id.ToString("D").ToUpperInvariant(), CloudSyncEntity.Schedule(schedule).Key.RecordName);
        Assert.Equal("202710|CS 3000", CloudSyncEntity.Saved(Saved()).Key.RecordName);
        Assert.Equal("202710|12345", CloudSyncEntity.Watch(Watch()).Key.RecordName);
        Assert.Equal(CloudSyncKey.Preferences, CloudSyncEntity.Preferences(new AppPreferences()).Key);
        foreach (var unusable in new[] { "", "_private", "Café", new string('x', 256), "tab\there" })
        {
            var name = CloudSyncKey.RecordNameForId(unusable);
            Assert.StartsWith("sha256-", name);
            Assert.True(name.Length <= 255);
            Assert.Equal(name, CloudSyncKey.RecordNameForId(unusable));
        }
        Assert.NotEqual(CloudSyncKey.RecordNameForId("a"), CloudSyncKey.RecordNameForId("_a"));
    }

    [Fact]
    public void DevicePreferencesStayOffThePayloadAndFingerprint()
    {
        var preferences = new AppPreferences
        {
            SelectedTermCode = "202730",
            HasCompletedOnboarding = true,
            AutoRefreshSeats = false,
            BackgroundRefreshEnabled = false,
            SeatCheckMinutes = 5,
            NotifyOnSeats = false,
            PreferredCampus = "Boston",
        };
        var text = Encoding.UTF8.GetString(CloudSyncCoding.Payload(CloudSyncEntity.Preferences(preferences)));
        foreach (var deviceOnly in new[] { "selectedTermCode", "hasCompletedOnboarding", "autoRefreshSeats", "backgroundRefreshEnabled", "seatCheckMinutes", "notifyOnSeats", "202730" })
            Assert.DoesNotContain(deviceOnly, text);
        var other = new AppPreferences
        {
            SelectedTermCode = "202710",
            HasCompletedOnboarding = false,
            AutoRefreshSeats = true,
            BackgroundRefreshEnabled = true,
            PreferredCampus = "Boston",
        };
        Assert.Equal(
            CloudSyncCoding.Fingerprint(CloudSyncEntity.Preferences(other)),
            CloudSyncCoding.Fingerprint(CloudSyncEntity.Preferences(preferences)));
        var remote = new CloudSyncedPreferences { PreferredCampus = "Oakland", ShowWeekends = true };
        var applied = remote.Applied(preferences);
        Assert.Equal("Oakland", applied.PreferredCampus);
        Assert.True(applied.ShowWeekends);
        Assert.Equal("202730", applied.SelectedTermCode);
        Assert.True(applied.HasCompletedOnboarding);
        Assert.False(applied.AutoRefreshSeats);
        Assert.False(applied.BackgroundRefreshEnabled);
        Assert.Equal(5, applied.SeatCheckMinutes);
        Assert.False(applied.NotifyOnSeats);
    }

    [Fact]
    public void DegreeStudentDetailsStayOnTheDevice()
    {
        var plan = Degree();
        var text = Encoding.UTF8.GetString(CloudSyncCoding.Payload(CloudSyncEntity.Degree(plan)));
        Assert.DoesNotContain("SAMPLE STUDENT", text);
        Assert.DoesNotContain("000000000", text);
        Assert.DoesNotContain("studentName", text);
        var without = CloudSyncCoding.SyncedForm(plan);
        Assert.Equal(CloudSyncCoding.Fingerprint(CloudSyncEntity.Degree(without)), CloudSyncCoding.Fingerprint(CloudSyncEntity.Degree(plan)));
        var remote = CloudSyncCoding.SyncedForm(plan);
        remote.Name = "BSCS (updated)";
        var merged = (CloudSyncEntity.DegreePlanEntity)CloudSyncMerge.LocalValue(new CloudSyncRemoteValue.DegreePlanValue(remote), CloudSyncEntity.Degree(plan));
        Assert.Equal("BSCS (updated)", merged.Plan.Name);
        Assert.Equal("SAMPLE STUDENT", merged.Plan.StudentName);
        Assert.Equal("000000000", merged.Plan.StudentID);
    }

    [Fact]
    public void WatchSeatChecksDoNotChangeTheFingerprint()
    {
        var item = Watch();
        var withSeats = CloudSyncCloneCopy(item);
        withSeats.LastSeatsRemaining = 3;
        withSeats.LastWaitlistRemaining = 0;
        withSeats.SeatCapacity = 45;
        withSeats.LastCheckedAt = Date.AddSeconds(600);
        withSeats.LastNotifiedAt = Date.AddSeconds(600);
        Assert.Equal(CloudSyncCoding.Fingerprint(CloudSyncEntity.Watch(withSeats)), CloudSyncCoding.Fingerprint(CloudSyncEntity.Watch(item)));
        var intent = CloudSyncCloneCopy(item);
        intent.NotifyOnWaitlist = true;
        Assert.NotEqual(CloudSyncCoding.Fingerprint(CloudSyncEntity.Watch(intent)), CloudSyncCoding.Fingerprint(CloudSyncEntity.Watch(item)));
        var remote = CloudSyncCloneCopy(item);
        remote.NotifyOnWaitlist = true;
        remote.LastSeatsRemaining = 0;
        remote.LastCheckedAt = Date.AddSeconds(-600);
        var merged = (CloudSyncEntity.WatchItemEntity)CloudSyncMerge.LocalValue(new CloudSyncRemoteValue.WatchItemValue(remote), CloudSyncEntity.Watch(withSeats));
        Assert.True(merged.Item.NotifyOnWaitlist);
        Assert.Equal(3, merged.Item.LastSeatsRemaining);
        Assert.Equal(45, merged.Item.SeatCapacity);
        Assert.Equal(Date.AddSeconds(600), merged.Item.LastCheckedAt);
    }

    [Fact]
    public void FingerprintsIgnoreSetOrder()
    {
        var first = Plan();
        var second = Plan(id: first.Id);
        second.CreatedAt = first.CreatedAt;
        second.UpdatedAt = first.UpdatedAt;
        first.Courses[0].ExcludedCRNs = [];
        second.Courses[0].ExcludedCRNs = [];
        for (var n = 1; n <= 40; n++) first.Courses[0].ExcludedCRNs.Add((20000 + n).ToString());
        for (var n = 40; n >= 1; n--) second.Courses[0].ExcludedCRNs.Add((20000 + n).ToString());
        Assert.Equal(CloudSyncCoding.Payload(CloudSyncEntity.Schedule(first)), CloudSyncCoding.Payload(CloudSyncEntity.Schedule(second)));
        Assert.Equal(CloudSyncCoding.Fingerprint(CloudSyncEntity.Schedule(first)), CloudSyncCoding.Fingerprint(CloudSyncEntity.Schedule(second)));
        using var doc = JsonDocument.Parse(CloudSyncCoding.Payload(CloudSyncEntity.Schedule(first)));
        var excluded = doc.RootElement.GetProperty("courses")[0].GetProperty("excludedCRNs").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Equal(40, excluded.Count);
        Assert.Equal(excluded.OrderBy(value => value, StringComparer.Ordinal).ToList(), excluded);
    }

    [Fact]
    public void FingerprintsSurviveTheLocalFile()
    {
        var data = new UserData
        {
            Plans = [Plan(updatedAt: Date.AddSeconds(0.75))],
            Saved = [Saved(savedAt: Date.AddSeconds(0.25))],
            Watchlist = [Watch(addedAt: Date.AddSeconds(0.5))],
        };
        var reloaded = JsonSerializer.Deserialize<UserData>(JsonSerializer.Serialize(data, AppJson.UserData), AppJson.UserData)!;
        var before = new CloudSyncSnapshot(data);
        var after = new CloudSyncSnapshot(reloaded);
        Assert.Equal(before.Items.Keys.OrderBy(key => key).ToList(), after.Items.Keys.OrderBy(key => key).ToList());
        foreach (var (key, item) in before.Items)
            Assert.Equal(item.Fingerprint, after.Items[key].Fingerprint);
    }

    [Fact]
    public void JournalDatesLocalChangesAndForgetKeepsHistory()
    {
        var data = new UserData { Plans = [Plan(isPrimary: false)] };
        var planKey = CloudSyncEntity.Schedule(data.Plans[0]).Key;
        var metadata = new CloudSyncMetadata();
        metadata.ObserveLocalData(new CloudSyncSnapshot(data), Date);
        Assert.Null(metadata.Records[planKey].LocalChangedAt);
        Assert.Empty(metadata.PreferenceChangedAt);

        data.Plans[0].IsPrimary = true;
        data.Preferences.ShowWeekends = true;
        metadata.ObserveLocalData(new CloudSyncSnapshot(data), Date.AddSeconds(300));
        Assert.Equal(Date.AddSeconds(300), metadata.Records[planKey].LocalChangedAt);
        Assert.Equal(Date.AddSeconds(300), metadata.PreferenceChangedAt["showWeekends"]);
        Assert.Equal(Date.AddSeconds(300), CloudSyncCoding.ModifiedAt(CloudSyncEntity.Schedule(data.Plans[0]), Date.AddSeconds(300)));

        data.Plans = [];
        metadata.ObserveLocalData(new CloudSyncSnapshot(data), Date.AddSeconds(600));
        Assert.False(metadata.Records.ContainsKey(planKey));
    }

    [Fact]
    public void RemoteChangeAppliesWithoutEchoAndTiesGoToRemote()
    {
        var original = Plan();
        var data = new UserData { Plans = [original] };
        var metadata = Synced(data);
        var remote = CloudSyncCloneCopy(original);
        remote.Name = "Renamed elsewhere";
        remote.UpdatedAt = Date.AddSeconds(60);
        var result = CloudSyncMerge.Merge([Incoming(CloudSyncEntity.Schedule(remote), Date.AddSeconds(60))], [], new CloudSyncSnapshot(data), metadata, Date.AddSeconds(120));
        Assert.Equal("Renamed elsewhere", ((CloudSyncEntity.SchedulePlanEntity)result.Saves[0]).Plan.Name);
        Assert.Empty(result.KeptLocal);
        data = data.ApplyingCloudChanges(result.Saves, result.Deletes);
        var after = new CloudSyncSnapshot(data);
        metadata.ObserveLocalData(after, Date.AddSeconds(180));
        Assert.True(metadata.PendingChanges(after).IsEmpty);

        var tied = Plan(id: original.Id);
        tied.Name = "Edited here";
        tied.UpdatedAt = Date.AddSeconds(50);
        var local = new UserData { Plans = [tied] };
        var tieMeta = Synced(new UserData { Plans = [original] });
        tieMeta.ObserveLocalData(new CloudSyncSnapshot(local), Date.AddSeconds(50));
        var elsewhere = CloudSyncCloneCopy(original);
        elsewhere.Name = "Edited elsewhere";
        elsewhere.UpdatedAt = Date.AddSeconds(50);
        var tie = CloudSyncMerge.Merge([Incoming(CloudSyncEntity.Schedule(elsewhere), Date.AddSeconds(50))], [], new CloudSyncSnapshot(local), tieMeta);
        Assert.Equal("Edited elsewhere", ((CloudSyncEntity.SchedulePlanEntity)tie.Saves.Single()).Plan.Name);
    }

    [Fact]
    public void NewerLocalChangeWinsAndRemoteDeletionRespectsEdits()
    {
        var original = Plan();
        var data = new UserData { Plans = [original] };
        var metadata = Synced(data);
        data.Plans[0].Name = "Edited here";
        data.Plans[0].UpdatedAt = Date.AddSeconds(100);
        metadata.ObserveLocalData(new CloudSyncSnapshot(data), Date.AddSeconds(100));
        var remote = CloudSyncCloneCopy(original);
        remote.Name = "Edited elsewhere";
        remote.UpdatedAt = Date.AddSeconds(50);
        var key = CloudSyncEntity.Schedule(original).Key;
        var result = CloudSyncMerge.Merge([Incoming(CloudSyncEntity.Schedule(remote), Date.AddSeconds(50))], [], new CloudSyncSnapshot(data), metadata);
        Assert.Empty(result.Saves);
        Assert.Equal(key, result.KeptLocal.Single());
        Assert.Equal(key, metadata.PendingChanges(new CloudSyncSnapshot(data)).Saves.Single());

        var course = Saved();
        var saved = new UserData { Saved = [course] };
        var savedMeta = Synced(saved);
        var removed = CloudSyncMerge.Merge([], [CloudSyncEntity.Saved(course).Key], new CloudSyncSnapshot(saved), savedMeta);
        Assert.Equal(CloudSyncEntity.Saved(course).Key, removed.Deletes.Single());

        var item = Watch();
        var watchData = new UserData { Watchlist = [item] };
        var watchMeta = Synced(watchData);
        watchData.Watchlist[0].NotifyOnWaitlist = true;
        watchMeta.ObserveLocalData(new CloudSyncSnapshot(watchData), Date.AddSeconds(30));
        var kept = CloudSyncMerge.Merge([], [CloudSyncEntity.Watch(item).Key], new CloudSyncSnapshot(watchData), watchMeta);
        Assert.Empty(kept.Deletes);
        Assert.Equal(CloudSyncEntity.Watch(item).Key, kept.KeptLocal.Single());
    }

    [Fact]
    public void LocalDeletionStandsUnlessTheRemoteEditIsNewer()
    {
        var original = Plan();
        var data = new UserData { Plans = [original] };
        var metadata = Synced(data);
        data.Plans = [];
        metadata.ObserveLocalData(new CloudSyncSnapshot(data), Date.AddSeconds(100));
        var key = CloudSyncEntity.Schedule(original).Key;
        var older = CloudSyncCloneCopy(original);
        older.Name = "Edited elsewhere, earlier";
        var keepDeleted = JsonClone(metadata);
        var stands = CloudSyncMerge.Merge([Incoming(CloudSyncEntity.Schedule(older), Date.AddSeconds(50))], [], new CloudSyncSnapshot(data), keepDeleted);
        Assert.Empty(stands.Saves);
        Assert.Equal(key, keepDeleted.PendingChanges(new CloudSyncSnapshot(data)).Deletes.Single());

        var newer = CloudSyncCloneCopy(original);
        newer.Name = "Edited elsewhere, later";
        newer.UpdatedAt = Date.AddSeconds(200);
        var resurrected = CloudSyncMerge.Merge([Incoming(CloudSyncEntity.Schedule(newer), Date.AddSeconds(200))], [], new CloudSyncSnapshot(data), metadata);
        Assert.Equal("Edited elsewhere, later", ((CloudSyncEntity.SchedulePlanEntity)resurrected.Saves.Single()).Plan.Name);
    }

    [Fact]
    public void NewerSchemaAndGarbageAreLeftAlone()
    {
        var plan = Plan();
        var data = new UserData { Plans = [plan] };
        var metadata = Synced(data);
        var key = CloudSyncEntity.Schedule(plan).Key;
        var newer = CloudSyncCloneCopy(plan);
        newer.Name = "Written by version 2";
        var record = Incoming(CloudSyncEntity.Schedule(newer), Date.AddSeconds(60), schemaVersion: CloudSyncConfig.SchemaVersion + 1);
        var result = CloudSyncMerge.Merge([record], [], new CloudSyncSnapshot(data), metadata);
        Assert.Equal(key, result.Skipped.Single());
        Assert.True(metadata.Records[key].IsServerCopyUnreadable);
        data.Plans[0].Name = "Edited here";
        metadata.ObserveLocalData(new CloudSyncSnapshot(data), Date.AddSeconds(120));
        Assert.True(metadata.PendingChanges(new CloudSyncSnapshot(data)).IsEmpty);

        var garbage = new CloudSyncIncomingRecord(key, 1, Date.AddSeconds(60), "{ not json"u8.ToArray(), null);
        var empty = new CloudSyncIncomingRecord(new CloudSyncKey(CloudSyncRecordType.SavedCourse, "202710|CS 9999"), 1, Date, null, null);
        var other = Plan("Other");
        var mismatched = Incoming(CloudSyncEntity.Schedule(other), Date.AddSeconds(60));
        mismatched = new CloudSyncIncomingRecord(new CloudSyncKey(CloudSyncRecordType.SchedulePlan, Guid.NewGuid()), mismatched.SchemaVersion, mismatched.ModifiedAt, mismatched.Payload, mismatched.SystemFields);
        var fresh = Synced(new UserData { Plans = [plan] });
        var skipped = CloudSyncMerge.Merge([garbage, empty, mismatched], [], new CloudSyncSnapshot(new UserData { Plans = [plan] }), fresh);
        Assert.Contains(key, skipped.Skipped);
        Assert.Contains(empty.Key, skipped.Skipped);
        Assert.Contains(mismatched.Key, skipped.Skipped);
        Assert.False(skipped.ChangesLocalData);
    }

    [Fact]
    public void OnePrimarySurvivesAndPreferencesMergeFieldByField()
    {
        var first = Plan("First", isPrimary: true);
        var second = Plan("Second");
        var data = new UserData { Plans = [first, second] };
        var metadata = Synced(data);
        var remote = CloudSyncCloneCopy(second);
        remote.IsPrimary = true;
        var result = CloudSyncMerge.Merge([Incoming(CloudSyncEntity.Schedule(remote), Date.AddSeconds(60))], [], new CloudSyncSnapshot(data), metadata, Date.AddSeconds(90));
        data = data.ApplyingCloudChanges(result.Saves, result.Deletes);
        Assert.Equal(["Second"], data.Plans.Where(plan => plan.IsPrimary).Select(plan => plan.Name).ToArray());
        var firstKey = CloudSyncEntity.Schedule(first).Key;
        Assert.Equal(Date.AddSeconds(90), metadata.Records[firstKey].LocalChangedAt);
        Assert.Equal(firstKey, metadata.PendingChanges(new CloudSyncSnapshot(data)).Saves.Single());

        var mine = Degree();
        mine.Name = "Mine";
        var degrees = new UserData { DegreePlans = [mine] };
        var degreeMeta = Synced(degrees);
        var theirs = CloudSyncCoding.SyncedForm(Degree(updatedAt: Date.AddSeconds(60)));
        theirs.Name = "Theirs";
        var degreeResult = CloudSyncMerge.Merge([Incoming(CloudSyncEntity.Degree(theirs), Date.AddSeconds(60))], [], new CloudSyncSnapshot(degrees), degreeMeta, Date.AddSeconds(90));
        degrees = degrees.ApplyingCloudChanges(degreeResult.Saves, degreeResult.Deletes);
        Assert.Equal(["Theirs"], degrees.DegreePlans.Where(plan => plan.IsPrimary).Select(plan => plan.Name).ToArray());
        Assert.Equal("SAMPLE STUDENT", degrees.DegreePlans.Single(plan => plan.Name == "Mine").StudentName);

        var prefs = new UserData();
        var prefMeta = Synced(prefs);
        prefs.Preferences.PreferredCampus = "Boston";
        prefMeta.ObserveLocalData(new CloudSyncSnapshot(prefs), Date.AddSeconds(200));
        var remotePrefs = new AppPreferences { CalendarStartHour = 7 };
        var merged = CloudSyncMerge.Merge(
            [Incoming(CloudSyncEntity.Preferences(remotePrefs), Date.AddSeconds(100), preferenceChangedAt: new Dictionary<string, DateTimeOffset> { ["calendarStartHour"] = Date.AddSeconds(100) })],
            [], new CloudSyncSnapshot(prefs), prefMeta);
        var values = ((CloudSyncEntity.PreferencesEntity)merged.Saves.Single()).Value;
        Assert.Equal("Boston", values.PreferredCampus);
        Assert.Equal(7, values.CalendarStartHour);
        Assert.Equal(CloudSyncKey.Preferences, merged.KeptLocal.Single());
    }

    [Fact]
    public void UndatedCustomBeatsDefaultsAndInvalidHoursFallBack()
    {
        var defaults = new CloudSyncedPreferences(new AppPreferences());
        var custom = new CloudSyncedPreferences(new AppPreferences()) { CalendarStartHour = 10, ShowWeekends = true };
        Assert.Equal(custom, CloudSyncedPreferences.MergeWith(custom, new Dictionary<string, DateTimeOffset>(), defaults, new Dictionary<string, DateTimeOffset>()).Values);
        Assert.Equal(custom, CloudSyncedPreferences.MergeWith(defaults, new Dictionary<string, DateTimeOffset>(), custom, new Dictionary<string, DateTimeOffset>()).Values);
        var dated = CloudSyncedPreferences.MergeWith(defaults, new Dictionary<string, DateTimeOffset> { ["showWeekends"] = Date }, custom, new Dictionary<string, DateTimeOffset>());
        Assert.False(dated.Values.ShowWeekends);
        Assert.Equal(10, dated.Values.CalendarStartHour);

        var json = """{"values":{"calendarStartHour":30,"calendarEndHour":2,"showWeekends":true},"changedAt":{}}"""u8.ToArray();
        var decoded = (CloudSyncRemoteValue.PreferencesValue)CloudSyncCoding.Decode(json, CloudSyncRecordType.Preferences);
        Assert.Equal(8, decoded.Values.CalendarStartHour);
        Assert.Equal(21, decoded.Values.CalendarEndHour);
        Assert.True(decoded.Values.ShowWeekends);
        Assert.Null(decoded.Values.PreferredCampus);
    }

    [Fact]
    public void ApplyingInsertsByDateAndFolderSyncRoundTrips()
    {
        var newest = Watch("3", Date.AddSeconds(300));
        var oldest = Watch("1", Date.AddSeconds(100));
        var plan = Plan();
        var data = new UserData { Watchlist = [newest, oldest], Plans = [plan], Saved = [Saved()] };
        var renamed = CloudSyncCloneCopy(plan);
        renamed.Name = "Renamed";
        var middle = Watch("2", Date.AddSeconds(200));
        var applied = data.ApplyingCloudChanges(
            [CloudSyncEntity.Schedule(renamed), CloudSyncEntity.Watch(middle), CloudSyncEntity.Saved(Saved(Cs3500, Date.AddSeconds(50)))],
            [CloudSyncEntity.Saved(Saved()).Key]);
        Assert.Equal(["Renamed"], applied.Plans.Select(item => item.Name).ToArray());
        Assert.Equal(["3", "2", "1"], applied.Watchlist.Select(item => item.Crn).ToArray());
        Assert.Equal(Cs3500, applied.Saved.Single().Key);

        var folder = Path.Combine(Path.GetTempPath(), "nto-sync-" + Guid.NewGuid().ToString("N"));
        var metaPath = Path.Combine(Path.GetTempPath(), "nto-meta-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var first = CloudSyncFolder.Synchronize(folder, data, metaPath, Date);
            Assert.Contains(first.Plans, item => item.Id == plan.Id);
            var otherMeta = Path.Combine(Path.GetTempPath(), "nto-meta-" + Guid.NewGuid().ToString("N") + ".json");
            var second = CloudSyncFolder.Synchronize(folder, new UserData(), otherMeta, Date.AddSeconds(10));
            Assert.Contains(second.Plans, item => item.Name == plan.Name);
            Assert.Contains(second.Watchlist, item => item.Crn == "3");
            if (File.Exists(otherMeta)) File.Delete(otherMeta);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            if (File.Exists(metaPath)) File.Delete(metaPath);
        }
    }

    [Fact]
    public void MetadataRoundTrips()
    {
        var data = new UserData { Plans = [Plan()] };
        data.Preferences.PreferredCampus = "Boston";
        var metadata = Synced(data);
        metadata.AccountRecordName = "_user";
        metadata.PreferenceChangedAt["preferredCampus"] = Date;
        metadata.Records[new CloudSyncKey(CloudSyncRecordType.WatchItem, "202710|1")] = new CloudSyncRecordState { IsServerCopyUnreadable = true };
        var decoded = JsonSerializer.Deserialize<CloudSyncMetadata>(JsonSerializer.Serialize(metadata, AppJson.UserData), AppJson.UserData)!;
        Assert.Equal(metadata.HasObservedLocalData, decoded.HasObservedLocalData);
        Assert.Equal("Boston", decoded.ObservedPreferences!.PreferredCampus);
        Assert.Equal(Date, decoded.PreferenceChangedAt["preferredCampus"]);
        Assert.Equal("_user", decoded.AccountRecordName);
        Assert.True(decoded.Records[new CloudSyncKey(CloudSyncRecordType.WatchItem, "202710|1")].IsServerCopyUnreadable);
        Assert.Equal(metadata.Records.Count, decoded.Records.Count);
    }

    private static System.Text.Json.Nodes.JsonObject JsonNodeFrom(JsonDocument doc) =>
        System.Text.Json.Nodes.JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();

    private static T CloudSyncCloneCopy<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, AppJson.UserData), AppJson.UserData)!;

    private static CloudSyncMetadata JsonClone(CloudSyncMetadata metadata) =>
        JsonSerializer.Deserialize<CloudSyncMetadata>(JsonSerializer.Serialize(metadata, AppJson.UserData), AppJson.UserData)!;
}

public class PortBehaviorTests
{
    [Fact]
    public void RequisiteFlattensRepeatedAlternatives()
    {
        var tree = Requisite.And(
            Requisite.Or(Requisite.Course(new CourseKey("CS", "2100")), Requisite.Course(new CourseKey("CS", "2100")), Requisite.Course(new CourseKey("CS", "2510"))),
            Requisite.And(Requisite.Course(new CourseKey("CS", "1800")), Requisite.Course(new CourseKey("MATH", "209M"))));
        var layout = RequisiteLayout.Create(tree, _ => true, _ => (false, null));
        Assert.IsType<RequisiteLayout.ChecklistLayout>(layout);
        Assert.Contains("one of CS 2100 or CS 2510", layout!.SpokenDescription("Prerequisites"));
        Assert.Contains("both CS 1800 and MATH 209M", layout.SpokenDescription("Prerequisites"));
    }

    [Fact]
    public void WatchlistAlertsWhenASeatOpens()
    {
        var item = new WatchItem
        {
            TermCode = "202710",
            Crn = "12345",
            CourseKey = new CourseKey("CS", "3000"),
            CourseTitle = "Algorithms",
            LastSeatsRemaining = 0,
            LastWaitlistRemaining = 0,
            NotifyOnWaitlist = true,
        };
        var section = new CourseSection
        {
            Crn = "12345",
            Subject = "CS",
            Number = "3000",
            ClassType = "Lecture",
            SectionNumber = "02",
            SeatsRemaining = 3,
            SeatCapacity = 40,
            WaitlistRemaining = 0,
        };
        var live = new Dictionary<string, IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>>
        {
            ["202710"] = new Dictionary<CourseKey, IReadOnlyList<CourseSection>>
            {
                [item.CourseKey] = [section],
            },
        };
        var outcome = WatchlistComparison.Compare([item], live, DateTimeOffset.UnixEpoch);
        Assert.Equal(WatchlistAlert.Kind.Seats, outcome.Alerts.Single().AlertKind);
        Assert.Equal("Seats open in CS 3000", outcome.Alerts[0].Title);
        Assert.Equal(3, outcome.Items[0].LastSeatsRemaining);
        Assert.Equal(WatchlistStatus.Open, WatchlistStatusInfo.For(outcome.Items[0], false));
    }

    [Fact]
    public void CourseColorIsStable()
    {
        Assert.Equal(CourseColor.ForCode("CS 3000"), CourseColor.ForCode("CS 3000"));
        Assert.Equal(10, CourseColor.Palette.Length);
        Assert.InRange(CourseColor.ForCourse(new CourseKey("CS", "3000")).Index, 0, 9);
    }

    [Fact]
    public void DayPresetAndBrowseSummary()
    {
        var days = (DaySet.Monday | DaySet.Tuesday | DaySet.Wednesday | DaySet.Thursday);
        Assert.Equal(SearchDayPreset.NoFridays, SearchDayPresetInfo.Matching(days));
        Assert.Equal(SearchDayPreset.Custom, SearchDayPresetInfo.Selection(DaySet.Friday));
        Assert.Equal("No Fridays", SearchFilterTitles.Days(days));
        Assert.Equal("12 courses", BrowseSummary.Text(12, 12, 0));
        Assert.Equal("1000-level", BrowseSummary.LevelTitle(1));
        Assert.Equal("Open seats", HomeQuickSearch.OpenSeats.Title());
        Assert.True(HomeQuickSearch.OpenSeats.Filters().OpenSeatsOnly);
    }
}
