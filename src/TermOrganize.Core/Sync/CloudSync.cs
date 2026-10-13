// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// What syncs, how each value becomes a payload and a fingerprint, and how two copies merge.
// There is no CloudKit here. A folder of JSON files is the Windows stand-in, and it uses
// the same rules: most recent change wins, preferences merge field by field, a newer
// schema is left alone, and device-only fields never leave the machine.
//
// Device-only: student name and ID, seat-check numbers and timestamps (they ride along in a
// watch payload so a new device has a starting point, but they are not part of the fingerprint),
// search history, recents, the selected term, onboarding, and this device's own refresh settings.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TermOrganize.Core;

public static class CloudSyncConfig
{
    /// <summary>
    /// The payload format this build writes and reads. A higher version was written by a newer
    /// app, and this build leaves that record alone rather than overwrite data it doesn't understand.
    /// </summary>
    public const int SchemaVersion = 1;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CloudSyncRecordType
{
    SchedulePlan,
    DegreePlan,
    WatchItem,
    SavedCourse,
    Preferences,
}

public sealed class CloudSyncKey : IEquatable<CloudSyncKey>, IComparable<CloudSyncKey>
{
    public CloudSyncRecordType Type { get; }
    public string RecordName { get; }

    [JsonConstructor]
    public CloudSyncKey(CloudSyncRecordType type, string recordName)
    {
        Type = type;
        RecordName = recordName;
    }

    public CloudSyncKey(CloudSyncRecordType type, Guid id)
        : this(type, RecordNameForId(id.ToString("D").ToUpperInvariant())) { }

    public static CloudSyncKey Preferences { get; } = new(CloudSyncRecordType.Preferences, "preferences");

    /// <summary>
    /// Record names are 1–255 printable ASCII characters that don't start with an underscore.
    /// Anything else gets a stable digest.
    /// </summary>
    public static string RecordNameForId(string id)
    {
        var usable = id.Length > 0
            && Encoding.UTF8.GetByteCount(id) <= 255
            && !id.StartsWith('_')
            && id.All(character => character is >= (char)0x20 and < (char)0x7F);
        return usable ? id : "sha256-" + CloudSyncCoding.Hex(Encoding.UTF8.GetBytes(id));
    }

    public int CompareTo(CloudSyncKey? other)
    {
        if (other is null) return 1;
        var type = string.CompareOrdinal(Type.ToString(), other.Type.ToString());
        return type != 0 ? type : string.CompareOrdinal(RecordName, other.RecordName);
    }

    public bool Equals(CloudSyncKey? other) => other is not null && Type == other.Type && RecordName == other.RecordName;
    public override bool Equals(object? obj) => Equals(obj as CloudSyncKey);
    public override int GetHashCode() => HashCode.Combine(Type, RecordName);
    public override string ToString() => $"{Type} {RecordName}";
}

public abstract class CloudSyncEntity
{
    public abstract CloudSyncKey Key { get; }

    public sealed class SchedulePlanEntity(SchedulePlan plan) : CloudSyncEntity
    {
        public SchedulePlan Plan { get; } = plan;
        public override CloudSyncKey Key => new(CloudSyncRecordType.SchedulePlan, Plan.Id);
    }

    public sealed class DegreePlanEntity(DegreePlan plan) : CloudSyncEntity
    {
        public DegreePlan Plan { get; } = plan;
        public override CloudSyncKey Key => new(CloudSyncRecordType.DegreePlan, Plan.Id);
    }

    public sealed class WatchItemEntity(WatchItem item) : CloudSyncEntity
    {
        public WatchItem Item { get; } = item;
        public override CloudSyncKey Key => new(CloudSyncRecordType.WatchItem, CloudSyncKey.RecordNameForId(Item.Id));
    }

    public sealed class SavedCourseEntity(SavedCourse course) : CloudSyncEntity
    {
        public SavedCourse Course { get; } = course;
        public override CloudSyncKey Key => new(CloudSyncRecordType.SavedCourse, CloudSyncKey.RecordNameForId(Course.Id));
    }

    public sealed class PreferencesEntity(AppPreferences preferences) : CloudSyncEntity
    {
        public AppPreferences Value { get; } = preferences;
        public override CloudSyncKey Key => CloudSyncKey.Preferences;
    }

    public static CloudSyncEntity Schedule(SchedulePlan plan) => new SchedulePlanEntity(plan);
    public static CloudSyncEntity Degree(DegreePlan plan) => new DegreePlanEntity(plan);
    public static CloudSyncEntity Watch(WatchItem item) => new WatchItemEntity(item);
    public static CloudSyncEntity Saved(SavedCourse course) => new SavedCourseEntity(course);
    public static CloudSyncEntity Preferences(AppPreferences preferences) => new PreferencesEntity(preferences);
}

public abstract class CloudSyncRemoteValue
{
    public abstract CloudSyncKey Key { get; }
    public sealed class SchedulePlanValue(SchedulePlan plan) : CloudSyncRemoteValue
    {
        public SchedulePlan Plan { get; } = plan;
        public override CloudSyncKey Key => new(CloudSyncRecordType.SchedulePlan, Plan.Id);
    }

    public sealed class DegreePlanValue(DegreePlan plan) : CloudSyncRemoteValue
    {
        public DegreePlan Plan { get; } = plan;
        public override CloudSyncKey Key => new(CloudSyncRecordType.DegreePlan, Plan.Id);
    }

    public sealed class WatchItemValue(WatchItem item) : CloudSyncRemoteValue
    {
        public WatchItem Item { get; } = item;
        public override CloudSyncKey Key => new(CloudSyncRecordType.WatchItem, CloudSyncKey.RecordNameForId(Item.Id));
    }

    public sealed class SavedCourseValue(SavedCourse course) : CloudSyncRemoteValue
    {
        public SavedCourse Course { get; } = course;
        public override CloudSyncKey Key => new(CloudSyncRecordType.SavedCourse, CloudSyncKey.RecordNameForId(Course.Id));
    }

    public sealed class PreferencesValue(CloudSyncedPreferences values, Dictionary<string, DateTimeOffset> changedAt) : CloudSyncRemoteValue
    {
        public CloudSyncedPreferences Values { get; } = values;
        public Dictionary<string, DateTimeOffset> ChangedAt { get; } = changedAt;
        public override CloudSyncKey Key => CloudSyncKey.Preferences;
    }
}

public enum CloudSyncedPreferenceField
{
    PreferredCampus,
    ShowWeekends,
    CalendarStartHour,
    CalendarEndHour,
}

/// <summary>The preferences that follow the user. Everything else in AppPreferences stays on this device.</summary>
[JsonConverter(typeof(CloudSyncedPreferencesConverter))]
public sealed class CloudSyncedPreferences : IEquatable<CloudSyncedPreferences>
{
    public static IReadOnlyList<CloudSyncedPreferenceField> Fields { get; } = Enum.GetValues<CloudSyncedPreferenceField>();

    public string? PreferredCampus { get; set; }
    public bool ShowWeekends { get; set; }
    public int CalendarStartHour { get; set; } = 8;
    public int CalendarEndHour { get; set; } = 21;

    public CloudSyncedPreferences() { }

    public CloudSyncedPreferences(AppPreferences preferences)
    {
        PreferredCampus = preferences.PreferredCampus;
        ShowWeekends = preferences.ShowWeekends;
        CalendarStartHour = preferences.CalendarStartHour;
        CalendarEndHour = preferences.CalendarEndHour;
    }

    public AppPreferences Applied(AppPreferences preferences)
    {
        var result = CloudSyncClone.Copy(preferences);
        result.PreferredCampus = PreferredCampus;
        result.ShowWeekends = ShowWeekends;
        result.CalendarStartHour = CalendarStartHour;
        result.CalendarEndHour = CalendarEndHour;
        return result;
    }

    public List<string> FieldsDiffering(CloudSyncedPreferences other) =>
        Fields.Select(Name).Where(name => !Equals(FieldValue(name), other.FieldValue(name))).ToList();

    public CloudSyncedPreferences Copying(string field, CloudSyncedPreferences other)
    {
        var result = Copy();
        switch (field)
        {
            case "preferredCampus": result.PreferredCampus = other.PreferredCampus; break;
            case "showWeekends": result.ShowWeekends = other.ShowWeekends; break;
            case "calendarStartHour": result.CalendarStartHour = other.CalendarStartHour; break;
            case "calendarEndHour": result.CalendarEndHour = other.CalendarEndHour; break;
        }
        return result;
    }

    public readonly record struct Merge(CloudSyncedPreferences Values, Dictionary<string, DateTimeOffset> ChangedAt);

    /// <summary>
    /// Field-by-field last writer wins. A local field wins when it changed more recently, or when
    /// only the local time is known. Ties go to the remote copy. When neither time is known, a
    /// customized value beats a default one.
    /// </summary>
    public static Merge MergeWith(
        CloudSyncedPreferences local,
        IReadOnlyDictionary<string, DateTimeOffset> localChangedAt,
        CloudSyncedPreferences remote,
        IReadOnlyDictionary<string, DateTimeOffset> remoteChangedAt)
    {
        var defaults = new CloudSyncedPreferences(new AppPreferences());
        var values = remote.Copy();
        var changedAt = new Dictionary<string, DateTimeOffset>(remoteChangedAt);
        var localCustom = local.FieldsDiffering(defaults).ToHashSet(StringComparer.Ordinal);
        var remoteCustom = remote.FieldsDiffering(defaults).ToHashSet(StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            var name = Name(field);
            var hasLocal = localChangedAt.TryGetValue(name, out var localTime);
            var hasRemote = remoteChangedAt.TryGetValue(name, out var remoteTime);
            var localWins = (hasLocal, hasRemote) switch
            {
                (true, true) => localTime > remoteTime,
                (true, false) => true,
                (false, true) => false,
                _ => remoteCustom.Contains(name) == false && localCustom.Contains(name),
            };
            if (!localWins) continue;
            values = values.Copying(name, local);
            if (hasLocal) changedAt[name] = localTime;
        }
        return new Merge(values, changedAt);
    }

    public bool Equals(CloudSyncedPreferences? other) =>
        other is not null && PreferredCampus == other.PreferredCampus && ShowWeekends == other.ShowWeekends
        && CalendarStartHour == other.CalendarStartHour && CalendarEndHour == other.CalendarEndHour;

    public override bool Equals(object? obj) => Equals(obj as CloudSyncedPreferences);
    public override int GetHashCode() => HashCode.Combine(PreferredCampus, ShowWeekends, CalendarStartHour, CalendarEndHour);

    public static string Name(CloudSyncedPreferenceField field) => field switch
    {
        CloudSyncedPreferenceField.PreferredCampus => "preferredCampus",
        CloudSyncedPreferenceField.ShowWeekends => "showWeekends",
        CloudSyncedPreferenceField.CalendarStartHour => "calendarStartHour",
        CloudSyncedPreferenceField.CalendarEndHour => "calendarEndHour",
        _ => field.ToString(),
    };

    private object? FieldValue(string name) => name switch
    {
        "preferredCampus" => PreferredCampus,
        "showWeekends" => ShowWeekends,
        "calendarStartHour" => CalendarStartHour,
        "calendarEndHour" => CalendarEndHour,
        _ => null,
    };

    private CloudSyncedPreferences Copy() => new()
    {
        PreferredCampus = PreferredCampus,
        ShowWeekends = ShowWeekends,
        CalendarStartHour = CalendarStartHour,
        CalendarEndHour = CalendarEndHour,
    };
}

public sealed class CloudSyncedPreferencesConverter : JsonConverter<CloudSyncedPreferences>
{
    public override CloudSyncedPreferences Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var defaults = new CloudSyncedPreferences(new AppPreferences());
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var prefs = new CloudSyncedPreferences();
        if (root.TryGetProperty("preferredCampus", out var campus) && campus.ValueKind == JsonValueKind.String)
            prefs.PreferredCampus = campus.GetString();
        if (root.TryGetProperty("showWeekends", out var weekends) && weekends.ValueKind is JsonValueKind.True or JsonValueKind.False)
            prefs.ShowWeekends = weekends.GetBoolean();
        else
            prefs.ShowWeekends = defaults.ShowWeekends;
        var start = root.TryGetProperty("calendarStartHour", out var startEl) && startEl.TryGetInt32(out var startValue)
            ? startValue : defaults.CalendarStartHour;
        var end = root.TryGetProperty("calendarEndHour", out var endEl) && endEl.TryGetInt32(out var endValue)
            ? endValue : defaults.CalendarEndHour;
        if (start is >= 0 and <= 23 && end is >= 1 and <= 24 && start < end)
        {
            prefs.CalendarStartHour = start;
            prefs.CalendarEndHour = end;
        }
        else
        {
            prefs.CalendarStartHour = defaults.CalendarStartHour;
            prefs.CalendarEndHour = defaults.CalendarEndHour;
        }
        return prefs;
    }

    public override void Write(Utf8JsonWriter writer, CloudSyncedPreferences value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.PreferredCampus is not null) writer.WriteString("preferredCampus", value.PreferredCampus);
        writer.WriteBoolean("showWeekends", value.ShowWeekends);
        writer.WriteNumber("calendarStartHour", value.CalendarStartHour);
        writer.WriteNumber("calendarEndHour", value.CalendarEndHour);
        writer.WriteEndObject();
    }
}

public sealed class CloudSyncPreferencesPayload
{
    public CloudSyncedPreferences Values { get; set; } = new();
    public Dictionary<string, DateTimeOffset> ChangedAt { get; set; } = new();
}

public static class CloudSyncCoding
{
    public const string Unencodable = "unencodable";
    private static readonly HashSet<string> UnorderedArrayKeys = new(StringComparer.Ordinal) { "excludedCRNs" };

    public static byte[] Payload(CloudSyncEntity entity, IReadOnlyDictionary<string, DateTimeOffset>? preferenceChangedAt = null)
    {
        preferenceChangedAt ??= new Dictionary<string, DateTimeOffset>();
        return entity switch
        {
            CloudSyncEntity.SchedulePlanEntity plan => Canonical(plan.Plan),
            CloudSyncEntity.DegreePlanEntity plan => Canonical(SyncedForm(plan.Plan)),
            CloudSyncEntity.WatchItemEntity item => Canonical(item.Item),
            CloudSyncEntity.SavedCourseEntity course => Canonical(course.Course),
            CloudSyncEntity.PreferencesEntity preferences => Canonical(new CloudSyncPreferencesPayload
            {
                Values = new CloudSyncedPreferences(preferences.Value),
                ChangedAt = new Dictionary<string, DateTimeOffset>(preferenceChangedAt),
            }),
            _ => throw new InvalidOperationException("Unknown sync entity."),
        };
    }

    public static CloudSyncRemoteValue Decode(byte[] payload, CloudSyncRecordType type)
    {
        return type switch
        {
            CloudSyncRecordType.SchedulePlan => new CloudSyncRemoteValue.SchedulePlanValue(
                JsonSerializer.Deserialize<SchedulePlan>(payload, AppJson.UserData) ?? throw new JsonException("Empty schedule.")),
            CloudSyncRecordType.DegreePlan => new CloudSyncRemoteValue.DegreePlanValue(
                SyncedForm(JsonSerializer.Deserialize<DegreePlan>(payload, AppJson.UserData) ?? throw new JsonException("Empty degree plan."))),
            CloudSyncRecordType.WatchItem => new CloudSyncRemoteValue.WatchItemValue(
                JsonSerializer.Deserialize<WatchItem>(payload, AppJson.UserData) ?? throw new JsonException("Empty watch item.")),
            CloudSyncRecordType.SavedCourse => new CloudSyncRemoteValue.SavedCourseValue(
                JsonSerializer.Deserialize<SavedCourse>(payload, AppJson.UserData) ?? throw new JsonException("Empty saved course.")),
            CloudSyncRecordType.Preferences => DecodePreferences(payload),
            _ => throw new JsonException("Unknown record type."),
        };
    }

    public static string Fingerprint(CloudSyncEntity entity) => entity switch
    {
        CloudSyncEntity.SchedulePlanEntity plan => FingerprintOf(plan.Plan),
        CloudSyncEntity.DegreePlanEntity plan => FingerprintOf(SyncedForm(plan.Plan)),
        CloudSyncEntity.WatchItemEntity item => FingerprintOf(TrackedForm(item.Item)),
        CloudSyncEntity.SavedCourseEntity course => FingerprintOf(course.Course),
        CloudSyncEntity.PreferencesEntity preferences => FingerprintOf(new CloudSyncedPreferences(preferences.Value)),
        _ => Unencodable,
    };

    public static string Fingerprint(CloudSyncRemoteValue value) => value switch
    {
        CloudSyncRemoteValue.SchedulePlanValue plan => FingerprintOf(plan.Plan),
        CloudSyncRemoteValue.DegreePlanValue plan => FingerprintOf(SyncedForm(plan.Plan)),
        CloudSyncRemoteValue.WatchItemValue item => FingerprintOf(TrackedForm(item.Item)),
        CloudSyncRemoteValue.SavedCourseValue course => FingerprintOf(course.Course),
        CloudSyncRemoteValue.PreferencesValue preferences => FingerprintOf(preferences.Values),
        _ => Unencodable,
    };

    public static DateTimeOffset ModifiedAt(
        CloudSyncEntity entity,
        DateTimeOffset? localChangedAt,
        IReadOnlyDictionary<string, DateTimeOffset>? preferenceChangedAt = null)
    {
        var distant = DateTimeOffset.MinValue;
        return entity switch
        {
            CloudSyncEntity.SchedulePlanEntity plan => Max(plan.Plan.UpdatedAt, localChangedAt ?? distant),
            CloudSyncEntity.DegreePlanEntity plan => Max(plan.Plan.UpdatedAt, localChangedAt ?? distant),
            CloudSyncEntity.WatchItemEntity item => localChangedAt ?? item.Item.AddedAt,
            CloudSyncEntity.SavedCourseEntity course => localChangedAt ?? course.Course.SavedAt,
            CloudSyncEntity.PreferencesEntity => preferenceChangedAt is { Count: > 0 } times
                ? times.Values.Max()
                : localChangedAt ?? distant,
            _ => distant,
        };
    }

    public static DegreePlan SyncedForm(DegreePlan plan)
    {
        var copy = CloudSyncClone.Copy(plan);
        copy.StudentName = null;
        copy.StudentID = null;
        return copy;
    }

    public static WatchItem TrackedForm(WatchItem item)
    {
        var copy = CloudSyncClone.Copy(item);
        copy.LastSeatsRemaining = 0;
        copy.LastWaitlistRemaining = 0;
        copy.SeatCapacity = 0;
        copy.LastCheckedAt = null;
        copy.LastNotifiedAt = null;
        return copy;
    }

    public static string Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static CloudSyncRemoteValue DecodePreferences(byte[] payload)
    {
        var decoded = JsonSerializer.Deserialize<CloudSyncPreferencesPayload>(payload, AppJson.UserData)
            ?? throw new JsonException("Empty preferences.");
        decoded.Values ??= new CloudSyncedPreferences();
        decoded.ChangedAt ??= new Dictionary<string, DateTimeOffset>();
        return new CloudSyncRemoteValue.PreferencesValue(decoded.Values, decoded.ChangedAt);
    }

    private static string FingerprintOf<T>(T value)
    {
        try { return Hex(Canonical(value)); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException) { return Unencodable; }
    }

    private static byte[] Canonical<T>(T value)
    {
        var encoded = JsonSerializer.SerializeToUtf8Bytes(value, AppJson.UserData);
        var node = JsonNode.Parse(encoded);
        var sorted = Sort(node);
        return JsonSerializer.SerializeToUtf8Bytes(sorted, CanonicalOptions);
    }

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static JsonNode? Sort(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var sorted = new JsonObject();
            foreach (var key in obj.Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal))
            {
                var child = obj[key];
                if (UnorderedArrayKeys.Contains(key) && child is JsonArray strings && strings.All(item => item is JsonValue))
                {
                    var values = new List<string>();
                    var allStrings = true;
                    foreach (var item in strings)
                    {
                        if (item is JsonValue json && json.TryGetValue<string>(out var text)) values.Add(text);
                        else allStrings = false;
                    }
                    if (allStrings)
                    {
                        values.Sort(StringComparer.Ordinal);
                        var sortedStrings = new JsonArray();
                        foreach (var text in values) sortedStrings.Add(text);
                        sorted[key] = sortedStrings;
                        continue;
                    }
                }
                sorted[key] = Sort(child?.DeepClone());
            }
            return sorted;
        }
        if (node is JsonArray array)
        {
            var copy = new JsonArray();
            foreach (var item in array) copy.Add(Sort(item?.DeepClone()));
            return copy;
        }
        return node?.DeepClone();
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}

public sealed class CloudSyncSnapshot
{
    public sealed class Item(CloudSyncEntity entity, string fingerprint)
    {
        public CloudSyncEntity Entity { get; } = entity;
        public string Fingerprint { get; } = fingerprint;
    }

    public IReadOnlyDictionary<CloudSyncKey, Item> Items { get; }
    public AppPreferences Preferences { get; }
    private readonly Dictionary<string, CloudSyncKey> _keysByRecordName;

    public CloudSyncSnapshot(UserData data)
    {
        Preferences = data.Preferences;
        var items = new Dictionary<CloudSyncKey, Item>();
        _keysByRecordName = new Dictionary<string, CloudSyncKey>(StringComparer.Ordinal);
        void Add(CloudSyncEntity entity)
        {
            var key = entity.Key;
            if (items.ContainsKey(key)) return;
            items[key] = new Item(entity, CloudSyncCoding.Fingerprint(entity));
            _keysByRecordName.TryAdd(key.RecordName, key);
        }
        Add(CloudSyncEntity.Preferences(data.Preferences));
        foreach (var plan in data.Plans) Add(CloudSyncEntity.Schedule(plan));
        foreach (var plan in data.DegreePlans) Add(CloudSyncEntity.Degree(plan));
        foreach (var item in data.Watchlist) Add(CloudSyncEntity.Watch(item));
        foreach (var course in data.Saved) Add(CloudSyncEntity.Saved(course));
        Items = items;
    }

    public CloudSyncKey? KeyForRecordName(string name) =>
        _keysByRecordName.TryGetValue(name, out var key) ? key : null;
}

public sealed class CloudSyncRecordState
{
    public string? LocalFingerprint { get; set; }
    public DateTimeOffset? LocalChangedAt { get; set; }
    public string? ServerFingerprint { get; set; }
    public byte[]? SystemFields { get; set; }
    public bool IsServerCopyUnreadable { get; set; }

    public bool IsKnownInICloud => ServerFingerprint is not null || SystemFields is not null || IsServerCopyUnreadable;
}

[JsonConverter(typeof(CloudSyncMetadataConverter))]
public sealed class CloudSyncMetadata
{
    public Dictionary<CloudSyncKey, CloudSyncRecordState> Records { get; } = new();
    public bool HasObservedLocalData { get; set; }
    public CloudSyncedPreferences? ObservedPreferences { get; set; }
    public Dictionary<string, DateTimeOffset> PreferenceChangedAt { get; } = new();
    public string? AccountRecordName { get; set; }
    public bool IsZoneSaved { get; set; }
    public DateTimeOffset? LastSyncDate { get; set; }

    public bool HasDataInICloud => Records.Values.Any(state => state.IsKnownInICloud);

    public CloudSyncKey? KeyForRecordName(string name) =>
        Records.Keys.FirstOrDefault(key => key.RecordName == name);

    public void ObserveLocalData(CloudSyncSnapshot snapshot, DateTimeOffset now)
    {
        var isFirstLook = !HasObservedLocalData;
        foreach (var (key, item) in snapshot.Items)
        {
            if (!Records.TryGetValue(key, out var state)) state = new CloudSyncRecordState();
            if (state.LocalFingerprint == item.Fingerprint) continue;
            state.LocalFingerprint = item.Fingerprint;
            state.LocalChangedAt = isFirstLook ? null : now;
            Records[key] = state;
        }
        foreach (var key in Records.Keys.ToList())
        {
            var state = Records[key];
            if (snapshot.Items.ContainsKey(key) || state.LocalFingerprint is null) continue;
            state.LocalFingerprint = null;
            state.LocalChangedAt = isFirstLook ? null : now;
            if (state.IsKnownInICloud) Records[key] = state;
            else Records.Remove(key);
        }
        var preferences = new CloudSyncedPreferences(snapshot.Preferences);
        if (ObservedPreferences is not null && !isFirstLook)
        {
            foreach (var field in preferences.FieldsDiffering(ObservedPreferences))
                PreferenceChangedAt[field] = now;
        }
        ObservedPreferences = preferences;
        HasObservedLocalData = true;
    }

    public CloudSyncPendingChanges PendingChanges(CloudSyncSnapshot snapshot)
    {
        var changes = new CloudSyncPendingChanges();
        foreach (var (key, item) in snapshot.Items)
        {
            Records.TryGetValue(key, out var state);
            if (state?.IsServerCopyUnreadable == true) continue;
            if (state?.ServerFingerprint != item.Fingerprint) changes.Saves.Add(key);
        }
        foreach (var (key, state) in Records)
        {
            if (snapshot.Items.ContainsKey(key) || !state.IsKnownInICloud || state.IsServerCopyUnreadable) continue;
            changes.Deletes.Add(key);
        }
        changes.Saves.Sort();
        changes.Deletes.Sort();
        return changes;
    }

    /// <summary>
    /// Forgets the server copies but keeps the local change history, so a later merge still
    /// knows what changed when. A value that was never uploaded is not deleted from other devices.
    /// </summary>
    public void ForgetRemote()
    {
        foreach (var key in Records.Keys.ToList())
        {
            var state = Records[key];
            state.ServerFingerprint = null;
            state.SystemFields = null;
            state.IsServerCopyUnreadable = false;
            if (state.LocalFingerprint is null) Records.Remove(key);
        }
        IsZoneSaved = false;
        LastSyncDate = null;
    }

    public void RecordSaved(CloudSyncKey key, string? fingerprint, byte[]? systemFields)
    {
        if (!Records.TryGetValue(key, out var state)) state = new CloudSyncRecordState();
        state.ServerFingerprint = fingerprint;
        state.SystemFields = systemFields;
        state.IsServerCopyUnreadable = false;
        Records[key] = state;
    }

    public void RecordDeletedFromRemote(CloudSyncKey key)
    {
        if (!Records.TryGetValue(key, out var state)) return;
        state.ServerFingerprint = null;
        state.SystemFields = null;
        state.IsServerCopyUnreadable = false;
        if (state.LocalFingerprint is null) Records.Remove(key);
    }
}

public sealed class CloudSyncMetadataConverter : JsonConverter<CloudSyncMetadata>
{
    private static bool TryReadKey(JsonElement element, out CloudSyncKey key)
    {
        key = null!;
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (!element.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String) return false;
        if (!element.TryGetProperty("recordName", out var nameEl) || nameEl.ValueKind != JsonValueKind.String) return false;
        if (!Enum.TryParse<CloudSyncRecordType>(typeEl.GetString(), ignoreCase: true, out var type)) return false;
        var name = nameEl.GetString();
        if (string.IsNullOrEmpty(name)) return false;
        key = new CloudSyncKey(type, name);
        return true;
    }

    public override CloudSyncMetadata Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var metadata = new CloudSyncMetadata();
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in records.EnumerateArray())
            {
                if (!entry.TryGetProperty("key", out var keyEl) || !entry.TryGetProperty("state", out var stateEl)) continue;
                if (!TryReadKey(keyEl, out var key)) continue;
                var state = stateEl.Deserialize<CloudSyncRecordState>(AppJson.UserData);
                if (state is null) continue;
                metadata.Records.TryAdd(key, state);
            }
        }
        if (root.TryGetProperty("hasObservedLocalData", out var observed) && observed.ValueKind == JsonValueKind.True)
            metadata.HasObservedLocalData = true;
        if (root.TryGetProperty("observedPreferences", out var prefs) && prefs.ValueKind == JsonValueKind.Object)
            metadata.ObservedPreferences = prefs.Deserialize<CloudSyncedPreferences>(AppJson.UserData);
        if (root.TryGetProperty("preferenceChangedAt", out var changed) && changed.ValueKind == JsonValueKind.Object)
        {
            var times = changed.Deserialize<Dictionary<string, DateTimeOffset>>(AppJson.UserData);
            if (times is not null)
                foreach (var pair in times) metadata.PreferenceChangedAt[pair.Key] = pair.Value;
        }
        if (root.TryGetProperty("accountRecordName", out var account) && account.ValueKind == JsonValueKind.String)
            metadata.AccountRecordName = account.GetString();
        if (root.TryGetProperty("isZoneSaved", out var zone) && zone.ValueKind == JsonValueKind.True)
            metadata.IsZoneSaved = true;
        if (root.TryGetProperty("lastSyncDate", out var last) && last.ValueKind == JsonValueKind.String)
            metadata.LastSyncDate = JsonSerializer.Deserialize<DateTimeOffset>(last.GetRawText(), AppJson.UserData);
        return metadata;
    }

    public override void Write(Utf8JsonWriter writer, CloudSyncMetadata value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("records");
        writer.WriteStartArray();
        foreach (var pair in value.Records.OrderBy(pair => pair.Key))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("key");
            JsonSerializer.Serialize(writer, pair.Key, AppJson.UserData);
            writer.WritePropertyName("state");
            JsonSerializer.Serialize(writer, pair.Value, AppJson.UserData);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteBoolean("hasObservedLocalData", value.HasObservedLocalData);
        if (value.ObservedPreferences is not null)
        {
            writer.WritePropertyName("observedPreferences");
            JsonSerializer.Serialize(writer, value.ObservedPreferences, AppJson.UserData);
        }
        writer.WritePropertyName("preferenceChangedAt");
        JsonSerializer.Serialize(writer, value.PreferenceChangedAt, AppJson.UserData);
        if (value.AccountRecordName is not null) writer.WriteString("accountRecordName", value.AccountRecordName);
        writer.WriteBoolean("isZoneSaved", value.IsZoneSaved);
        if (value.LastSyncDate is DateTimeOffset last)
        {
            writer.WritePropertyName("lastSyncDate");
            JsonSerializer.Serialize(writer, last, AppJson.UserData);
        }
        writer.WriteEndObject();
    }
}

public sealed class CloudSyncPendingChanges
{
    public List<CloudSyncKey> Saves { get; } = [];
    public List<CloudSyncKey> Deletes { get; } = [];
    public bool IsEmpty => Saves.Count == 0 && Deletes.Count == 0;
}

public sealed class CloudSyncIncomingRecord
{
    public CloudSyncKey Key { get; }
    public int SchemaVersion { get; }
    public DateTimeOffset ModifiedAt { get; }
    public byte[]? Payload { get; }
    /// <summary>Kept so the merge algorithm matches the Swift model. Folder sync leaves it null.</summary>
    public byte[]? SystemFields { get; }

    public CloudSyncIncomingRecord(CloudSyncKey key, int schemaVersion, DateTimeOffset modifiedAt, byte[]? payload, byte[]? systemFields)
    {
        Key = key;
        SchemaVersion = schemaVersion;
        ModifiedAt = modifiedAt;
        Payload = payload;
        SystemFields = systemFields;
    }
}

public sealed class CloudSyncMergeResult
{
    public List<CloudSyncEntity> Saves { get; set; } = [];
    public List<CloudSyncKey> Deletes { get; set; } = [];
    public List<CloudSyncKey> KeptLocal { get; } = [];
    public List<CloudSyncKey> Skipped { get; } = [];
    public bool ChangesLocalData => Saves.Count > 0 || Deletes.Count > 0;
}

public static class CloudSyncMerge
{
    public static CloudSyncMergeResult Merge(
        IReadOnlyList<CloudSyncIncomingRecord> modifications,
        IReadOnlyList<CloudSyncKey> deletions,
        CloudSyncSnapshot snapshot,
        CloudSyncMetadata metadata,
        DateTimeOffset? now = null)
    {
        var moment = now ?? DateTimeOffset.UtcNow;
        var working = new Working(snapshot);
        var result = new CloudSyncMergeResult();
        var applied = new List<CloudSyncKey>();
        foreach (var record in modifications)
        {
            var key = record.Key;
            if (!metadata.Records.TryGetValue(key, out var state)) state = new CloudSyncRecordState();
            var wasKnown = state.IsKnownInICloud;
            var previousServer = state.ServerFingerprint;
            CloudSyncRemoteValue? remote = null;
            var readable = record.SchemaVersion <= CloudSyncConfig.SchemaVersion && record.Payload is not null;
            if (readable)
            {
                try { remote = CloudSyncCoding.Decode(record.Payload!, key.Type); }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException) { remote = null; }
            }
            if (remote is null || !remote.Key.Equals(key))
            {
                state.IsServerCopyUnreadable = true;
                state.SystemFields = record.SystemFields;
                metadata.Records[key] = state;
                result.Skipped.Add(key);
                continue;
            }
            var remoteFingerprint = CloudSyncCoding.Fingerprint(remote);
            state.IsServerCopyUnreadable = false;
            state.SystemFields = record.SystemFields;
            state.ServerFingerprint = remoteFingerprint;
            if (remote is CloudSyncRemoteValue.PreferencesValue preferences)
            {
                var local = new CloudSyncedPreferences(working.Preferences);
                var merged = CloudSyncedPreferences.MergeWith(local, metadata.PreferenceChangedAt, preferences.Values, preferences.ChangedAt);
                if (!merged.Values.Equals(local))
                    working.Write(CloudSyncEntity.Preferences(merged.Values.Applied(working.Preferences)));
                metadata.PreferenceChangedAt.Clear();
                foreach (var pair in merged.ChangedAt) metadata.PreferenceChangedAt[pair.Key] = pair.Value;
                metadata.ObservedPreferences = merged.Values;
                state.LocalFingerprint = CloudSyncCoding.Fingerprint(CloudSyncEntity.Preferences(working.Preferences));
                state.LocalChangedAt = merged.ChangedAt.Count == 0 ? null : merged.ChangedAt.Values.Max();
                metadata.Records[key] = state;
                if (!merged.Values.Equals(preferences.Values)) result.KeptLocal.Add(key);
                continue;
            }
            if (!working.Local.TryGetValue(key, out var current))
            {
                if (wasKnown && state.LocalFingerprint is null && state.LocalChangedAt is DateTimeOffset deletedAt && deletedAt > record.ModifiedAt)
                {
                    metadata.Records[key] = state;
                    result.KeptLocal.Add(key);
                    continue;
                }
                var inserted = LocalValue(remote, null);
                working.Write(inserted);
                state.LocalFingerprint = CloudSyncCoding.Fingerprint(inserted);
                state.LocalChangedAt = record.ModifiedAt;
                metadata.Records[key] = state;
                applied.Add(key);
                continue;
            }
            var localFingerprint = CloudSyncCoding.Fingerprint(current);
            if (localFingerprint == remoteFingerprint)
            {
                state.LocalFingerprint = localFingerprint;
                metadata.Records[key] = state;
                continue;
            }
            if (previousServer != localFingerprint)
            {
                var localTime = CloudSyncCoding.ModifiedAt(current, state.LocalChangedAt, metadata.PreferenceChangedAt);
                if (localTime > record.ModifiedAt)
                {
                    metadata.Records[key] = state;
                    result.KeptLocal.Add(key);
                    continue;
                }
            }
            var replaced = LocalValue(remote, current);
            working.Write(replaced);
            state.LocalFingerprint = CloudSyncCoding.Fingerprint(replaced);
            state.LocalChangedAt = record.ModifiedAt;
            metadata.Records[key] = state;
            applied.Add(key);
        }

        foreach (var key in deletions)
        {
            metadata.Records.TryGetValue(key, out var state);
            if (key.Type == CloudSyncRecordType.Preferences)
            {
                var kept = state ?? new CloudSyncRecordState();
                kept.ServerFingerprint = null;
                kept.SystemFields = null;
                kept.IsServerCopyUnreadable = false;
                metadata.Records[key] = kept;
                result.KeptLocal.Add(key);
                continue;
            }
            if (!working.Local.TryGetValue(key, out var current))
            {
                metadata.Records.Remove(key);
                continue;
            }
            var localFingerprint = CloudSyncCoding.Fingerprint(current);
            if (state is not null && state.ServerFingerprint == localFingerprint)
            {
                working.Delete(key);
                metadata.Records.Remove(key);
            }
            else
            {
                var kept = state ?? new CloudSyncRecordState();
                kept.ServerFingerprint = null;
                kept.SystemFields = null;
                kept.IsServerCopyUnreadable = false;
                kept.LocalFingerprint = localFingerprint;
                metadata.Records[key] = kept;
                result.KeptLocal.Add(key);
            }
        }

        NormalizePrimaries(applied, working, metadata, moment);
        result.Saves = working.Written.Values.OrderBy(entity => entity.Key).ToList();
        result.Deletes = working.Deleted.OrderBy(key => key).ToList();
        return result;
    }

    public static CloudSyncEntity LocalValue(CloudSyncRemoteValue remote, CloudSyncEntity? current) => remote switch
    {
        CloudSyncRemoteValue.SchedulePlanValue plan => CloudSyncEntity.Schedule(plan.Plan),
        CloudSyncRemoteValue.DegreePlanValue degree => DegreeKeepingStudent(degree.Plan, current),
        CloudSyncRemoteValue.WatchItemValue watch => WatchKeepingSeats(watch.Item, current),
        CloudSyncRemoteValue.SavedCourseValue course => CloudSyncEntity.Saved(course.Course),
        CloudSyncRemoteValue.PreferencesValue preferences => CloudSyncEntity.Preferences(
            preferences.Values.Applied(current is CloudSyncEntity.PreferencesEntity local ? local.Value : new AppPreferences())),
        _ => throw new InvalidOperationException("Unknown remote value."),
    };

    private static CloudSyncEntity DegreeKeepingStudent(DegreePlan remote, CloudSyncEntity? current)
    {
        var plan = CloudSyncClone.Copy(remote);
        if (current is CloudSyncEntity.DegreePlanEntity local)
        {
            plan.StudentName = local.Plan.StudentName;
            plan.StudentID = local.Plan.StudentID;
        }
        return CloudSyncEntity.Degree(plan);
    }

    private static CloudSyncEntity WatchKeepingSeats(WatchItem remote, CloudSyncEntity? current)
    {
        var item = CloudSyncClone.Copy(remote);
        if (current is CloudSyncEntity.WatchItemEntity local)
        {
            item.LastSeatsRemaining = local.Item.LastSeatsRemaining;
            item.LastWaitlistRemaining = local.Item.LastWaitlistRemaining;
            item.SeatCapacity = local.Item.SeatCapacity;
            item.LastCheckedAt = local.Item.LastCheckedAt;
            item.LastNotifiedAt = local.Item.LastNotifiedAt;
        }
        return CloudSyncEntity.Watch(item);
    }

    private static void NormalizePrimaries(List<CloudSyncKey> applied, Working working, CloudSyncMetadata metadata, DateTimeOffset now)
    {
        (DateTimeOffset Time, string Name) Rank(CloudSyncKey key, CloudSyncEntity entity) =>
            (CloudSyncCoding.ModifiedAt(entity, metadata.Records.TryGetValue(key, out var state) ? state.LocalChangedAt : null, metadata.PreferenceChangedAt), key.RecordName);

        void Demote(CloudSyncKey key, CloudSyncEntity value)
        {
            working.Write(value);
            if (!metadata.Records.TryGetValue(key, out var state)) state = new CloudSyncRecordState();
            state.LocalFingerprint = CloudSyncCoding.Fingerprint(value);
            state.LocalChangedAt = now;
            metadata.Records[key] = state;
        }

        var terms = new SortedSet<string>(StringComparer.Ordinal);
        var touchesDegreePlans = false;
        foreach (var key in applied)
        {
            if (!working.Local.TryGetValue(key, out var entity)) continue;
            if (entity is CloudSyncEntity.SchedulePlanEntity plan) terms.Add(plan.Plan.TermCode);
            else if (entity is CloudSyncEntity.DegreePlanEntity) touchesDegreePlans = true;
        }
        foreach (var term in terms)
        {
            var primaries = working.Local.Where(pair => pair.Value is CloudSyncEntity.SchedulePlanEntity plan && plan.Plan.TermCode == term && plan.Plan.IsPrimary).ToList();
            if (primaries.Count < 2) continue;
            var keep = primaries.OrderBy(pair => Rank(pair.Key, pair.Value).Time).ThenBy(pair => Rank(pair.Key, pair.Value).Name, StringComparer.Ordinal).Last().Key;
            foreach (var (key, entity) in primaries)
            {
                if (key.Equals(keep) || entity is not CloudSyncEntity.SchedulePlanEntity plan) continue;
                var copy = CloudSyncClone.Copy(plan.Plan);
                copy.IsPrimary = false;
                Demote(key, CloudSyncEntity.Schedule(copy));
            }
        }
        if (!touchesDegreePlans) return;
        var degreePrimaries = working.Local.Where(pair => pair.Value is CloudSyncEntity.DegreePlanEntity plan && plan.Plan.IsPrimary).ToList();
        if (degreePrimaries.Count < 2) return;
        var keepDegree = degreePrimaries.OrderBy(pair => Rank(pair.Key, pair.Value).Time).ThenBy(pair => Rank(pair.Key, pair.Value).Name, StringComparer.Ordinal).Last().Key;
        foreach (var (key, entity) in degreePrimaries)
        {
            if (key.Equals(keepDegree) || entity is not CloudSyncEntity.DegreePlanEntity plan) continue;
            var copy = CloudSyncClone.Copy(plan.Plan);
            copy.IsPrimary = false;
            Demote(key, CloudSyncEntity.Degree(copy));
        }
    }

    private sealed class Working
    {
        public Dictionary<CloudSyncKey, CloudSyncEntity> Local { get; } = new();
        public AppPreferences Preferences { get; private set; }
        public Dictionary<CloudSyncKey, CloudSyncEntity> Written { get; } = new();
        public HashSet<CloudSyncKey> Deleted { get; } = [];

        public Working(CloudSyncSnapshot snapshot)
        {
            foreach (var (key, item) in snapshot.Items) Local[key] = item.Entity;
            Preferences = snapshot.Preferences;
        }

        public void Write(CloudSyncEntity entity)
        {
            var key = entity.Key;
            Local[key] = entity;
            Written[key] = entity;
            Deleted.Remove(key);
            if (entity is CloudSyncEntity.PreferencesEntity preferences) Preferences = preferences.Value;
        }

        public void Delete(CloudSyncKey key)
        {
            Local.Remove(key);
            Written.Remove(key);
            Deleted.Add(key);
        }
    }
}

public static class CloudSyncData
{
    /// <summary>
    /// A copy with fetched values written (replacing the value with the same id, or added in
    /// date order) and deleted values removed. Recents, search history and the selected term stay.
    /// </summary>
    public static UserData ApplyingCloudChanges(this UserData source, IReadOnlyList<CloudSyncEntity> saves, IReadOnlyList<CloudSyncKey> deletes)
    {
        var data = new UserData
        {
            Version = source.Version,
            Preferences = source.Preferences,
            Saved = source.Saved.ToList(),
            Recents = source.Recents.ToList(),
            Plans = source.Plans.ToList(),
            Watchlist = source.Watchlist.ToList(),
            SearchHistory = source.SearchHistory.ToList(),
            DegreePlans = source.DegreePlans.ToList(),
        };
        if (deletes.Count > 0)
        {
            var removed = deletes.ToHashSet();
            data.Plans.RemoveAll(plan => removed.Contains(CloudSyncEntity.Schedule(plan).Key));
            data.DegreePlans.RemoveAll(plan => removed.Contains(CloudSyncEntity.Degree(plan).Key));
            data.Watchlist.RemoveAll(item => removed.Contains(CloudSyncEntity.Watch(item).Key));
            data.Saved.RemoveAll(course => removed.Contains(CloudSyncEntity.Saved(course).Key));
        }
        foreach (var entity in saves)
        {
            switch (entity)
            {
                case CloudSyncEntity.SchedulePlanEntity plan:
                    ReplaceOrAdd(data.Plans, item => item.Id == plan.Plan.Id, plan.Plan);
                    break;
                case CloudSyncEntity.DegreePlanEntity plan:
                    ReplaceOrAdd(data.DegreePlans, item => item.Id == plan.Plan.Id, plan.Plan);
                    break;
                case CloudSyncEntity.WatchItemEntity watch:
                    var watchIndex = data.Watchlist.FindIndex(item => item.Id == watch.Item.Id);
                    if (watchIndex >= 0) data.Watchlist[watchIndex] = watch.Item;
                    else InsertByDate(data.Watchlist, watch.Item, item => item.AddedAt);
                    break;
                case CloudSyncEntity.SavedCourseEntity course:
                    var savedIndex = data.Saved.FindIndex(item => item.Id == course.Course.Id);
                    if (savedIndex >= 0) data.Saved[savedIndex] = course.Course;
                    else InsertByDate(data.Saved, course.Course, item => item.SavedAt);
                    break;
                case CloudSyncEntity.PreferencesEntity preferences:
                    data.Preferences = preferences.Value;
                    break;
            }
        }
        return data;
    }

    private static void ReplaceOrAdd<T>(List<T> list, Func<T, bool> match, T value)
    {
        var index = list.FindIndex(item => match(item));
        if (index >= 0) list[index] = value;
        else list.Add(value);
    }

    private static void InsertByDate<T>(List<T> list, T value, Func<T, DateTimeOffset> date)
    {
        var index = list.FindIndex(item => date(item) < date(value));
        if (index < 0) list.Add(value);
        else list.Insert(index, value);
    }
}

/// <summary>
/// Folder sync using the same merge rules as iCloud. Each record is one JSON file.
/// Metadata stays on this device and is not written into the folder.
/// An empty folder on a device that has never synced uploads. An empty folder after a
/// previous sync treats the missing files as deletions.
/// </summary>
public static class CloudSyncFolder
{
    public static string DefaultMetadataPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "TermOrganize", "CloudSync", "metadata.json");
    }

    public static CloudSyncMetadata LoadMetadata(string? path = null)
    {
        var file = path ?? DefaultMetadataPath();
        if (!File.Exists(file)) return new CloudSyncMetadata();
        try
        {
            return JsonSerializer.Deserialize<CloudSyncMetadata>(File.ReadAllText(file), AppJson.UserData) ?? new CloudSyncMetadata();
        }
        catch (JsonException)
        {
            return new CloudSyncMetadata();
        }
    }

    public static void SaveMetadata(CloudSyncMetadata metadata, string? path = null)
    {
        var file = path ?? DefaultMetadataPath();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(metadata, AppJson.UserData));
        File.Move(temp, file, overwrite: true);
    }

    public static UserData Synchronize(string folder, UserData data, CloudSyncMetadata metadata, DateTimeOffset? now = null)
    {
        var moment = now ?? DateTimeOffset.UtcNow;
        var snapshot = new CloudSyncSnapshot(data);
        metadata.ObserveLocalData(snapshot, moment);
        var remote = ReadFolder(folder);
        var deletions = new List<CloudSyncKey>();
        var folderIsEmpty = remote.Count == 0;
        if (!(folderIsEmpty && !metadata.HasDataInICloud))
        {
            foreach (var (key, state) in metadata.Records)
            {
                if (!state.IsKnownInICloud || remote.ContainsKey(key)) continue;
                deletions.Add(key);
            }
        }
        var result = CloudSyncMerge.Merge(remote.Values.ToList(), deletions, snapshot, metadata, moment);
        var updated = data.ApplyingCloudChanges(result.Saves, result.Deletes);
        var after = new CloudSyncSnapshot(updated);
        metadata.ObserveLocalData(after, moment);
        var pending = metadata.PendingChanges(after);
        Directory.CreateDirectory(folder);
        foreach (var key in pending.Saves)
        {
            if (!after.Items.TryGetValue(key, out var item)) continue;
            var modified = CloudSyncCoding.ModifiedAt(item.Entity, metadata.Records.TryGetValue(key, out var state) ? state.LocalChangedAt : null, metadata.PreferenceChangedAt);
            WriteRecord(folder, key, item.Entity, modified, metadata.PreferenceChangedAt);
            metadata.RecordSaved(key, item.Fingerprint, systemFields: null);
        }
        foreach (var key in pending.Deletes)
        {
            var path = RecordPath(folder, key);
            if (File.Exists(path)) File.Delete(path);
            metadata.RecordDeletedFromRemote(key);
        }
        metadata.LastSyncDate = moment;
        return updated;
    }

    public static UserData Synchronize(string folder, UserData data, string? metadataPath = null, DateTimeOffset? now = null)
    {
        var path = metadataPath ?? DefaultMetadataPath();
        var metadata = LoadMetadata(path);
        var updated = Synchronize(folder, data, metadata, now);
        SaveMetadata(metadata, path);
        return updated;
    }

    private static Dictionary<CloudSyncKey, CloudSyncIncomingRecord> ReadFolder(string folder)
    {
        var records = new Dictionary<CloudSyncKey, CloudSyncIncomingRecord>();
        if (!Directory.Exists(folder)) return records;
        foreach (var typeDir in Directory.EnumerateDirectories(folder))
        {
            var typeName = Path.GetFileName(typeDir);
            if (typeName.StartsWith('_') || !Enum.TryParse<CloudSyncRecordType>(typeName, out var type)) continue;
            foreach (var file in Directory.EnumerateFiles(typeDir, "*.json"))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith('_')) continue;
                var record = ReadRecord(type, file);
                if (record is not null) records[record.Key] = record;
            }
        }
        return records;
    }

    private static CloudSyncIncomingRecord? ReadRecord(CloudSyncRecordType type, string file)
    {
        var recordName = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(file));
        var key = new CloudSyncKey(type, recordName);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            var root = doc.RootElement;
            var schema = root.TryGetProperty("schemaVersion", out var schemaEl) && schemaEl.TryGetInt32(out var version) ? version : 1;
            var modified = DateTimeOffset.UnixEpoch;
            if (root.TryGetProperty("modifiedAt", out var modifiedEl) && modifiedEl.ValueKind == JsonValueKind.String)
                modified = DateTimeOffset.Parse(modifiedEl.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (root.TryGetProperty("recordType", out var typeEl) && Enum.TryParse<CloudSyncRecordType>(typeEl.GetString(), out var storedType))
                type = storedType;
            if (root.TryGetProperty("recordName", out var nameEl) && nameEl.ValueKind == JsonValueKind.String && nameEl.GetString() is string storedName)
                recordName = storedName;
            key = new CloudSyncKey(type, recordName);
            byte[]? payload = null;
            if (root.TryGetProperty("payload", out var payloadEl) && payloadEl.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                payload = Encoding.UTF8.GetBytes(payloadEl.GetRawText());
            return new CloudSyncIncomingRecord(key, schema, modified, payload, systemFields: null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or FormatException)
        {
            return new CloudSyncIncomingRecord(key, 1, DateTimeOffset.UnixEpoch, "{ not json"u8.ToArray(), systemFields: null);
        }
    }

    private static void WriteRecord(
        string folder,
        CloudSyncKey key,
        CloudSyncEntity entity,
        DateTimeOffset modifiedAt,
        IReadOnlyDictionary<string, DateTimeOffset> preferenceChangedAt)
    {
        var payload = CloudSyncCoding.Payload(entity, preferenceChangedAt);
        var path = RecordPath(folder, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CloudSyncConfig.SchemaVersion);
            writer.WritePropertyName("modifiedAt");
            JsonSerializer.Serialize(writer, modifiedAt, AppJson.UserData);
            writer.WriteString("recordType", key.Type.ToString());
            writer.WriteString("recordName", key.RecordName);
            writer.WritePropertyName("payload");
            using var payloadDoc = JsonDocument.Parse(payload);
            payloadDoc.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, stream.ToArray());
        File.Move(temp, path, overwrite: true);
    }

    private static string RecordPath(string folder, CloudSyncKey key) =>
        Path.Combine(folder, key.Type.ToString(), Uri.EscapeDataString(key.RecordName) + ".json");
}

internal static class CloudSyncClone
{
    public static T Copy<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, AppJson.UserData), AppJson.UserData)
        ?? throw new InvalidOperationException("Couldn't copy a sync value.");
}
