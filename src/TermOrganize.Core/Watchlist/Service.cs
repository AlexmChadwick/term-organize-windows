// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// SearchNEU (https://github.com/sandboxnu/searchneu) texts people through Twilio
// when a seat opens. This app does the same job on the device: it re-checks watched
// sections against Banner and builds local notifications. No server, account or
// phone number is involved. A Windows host can run <see cref="WatchlistService.BackgroundRefreshAsync"/>
// from a scheduled task.

using System.Text.Json;

namespace TermOrganize.Core;

/// <summary>Checks watched sections for open seats and builds local notifications.</summary>
public sealed class WatchlistService
{
    public static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan StaleInterval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan BackgroundRefreshInterval = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan FailedRetryFloor = TimeSpan.FromMinutes(5);

    private readonly UserDataStore _store;
    private readonly BannerClient _banner;
    private readonly object _gate = new();
    private Task? _refreshTask;
    private DateTimeOffset? _lastAttempt;

    public WatchlistService(UserDataStore store, BannerClient? banner = null)
    {
        _store = store;
        _banner = banner ?? BannerClient.Shared;
    }

    public bool IsRefreshing { get; private set; }
    public DateTimeOffset? LastRefresh { get; private set; }
    public string? LastError { get; private set; }
    public HashSet<string> MissingItemIds { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, a live refresh also writes the new sections into that term's catalog.</summary>
    public Func<string, TermCatalog?>? CatalogForTerm { get; set; }

    public IWatchlistNotifier? Notifier { get; set; }

    /// <summary>
    /// Adopts seat numbers saved by a background check when they are newer than the live store,
    /// so the next check does not announce the same opening again.
    /// </summary>
    public void ReconcileWithSavedWatchlist()
    {
        List<WatchItem> saved;
        try
        {
            if (!File.Exists(_store.FilePath)) return;
            saved = JsonSerializer.Deserialize<UserData>(File.ReadAllText(_store.FilePath), AppJson.UserData)?.Watchlist ?? [];
        }
        catch (JsonException)
        {
            return;
        }
        if (saved.Count == 0) return;
        var savedById = new Dictionary<string, WatchItem>(StringComparer.Ordinal);
        foreach (var item in saved) savedById.TryAdd(item.Id, item);
        foreach (var item in _store.Watchlist.ToList())
        {
            if (!savedById.TryGetValue(item.Id, out var newer) || newer.LastCheckedAt is not DateTimeOffset savedCheck)
                continue;
            if (savedCheck <= (item.LastCheckedAt ?? DateTimeOffset.MinValue)) continue;
            item.LastSeatsRemaining = newer.LastSeatsRemaining;
            item.LastWaitlistRemaining = newer.LastWaitlistRemaining;
            item.SeatCapacity = newer.SeatCapacity;
            item.LastCheckedAt = newer.LastCheckedAt;
            item.LastNotifiedAt = newer.LastNotifiedAt;
            _store.UpdateWatchItem(item);
        }
    }

    public async Task RefreshIfStaleAsync(CancellationToken cancellationToken = default)
    {
        Notifier?.ClearBadge();
        if (_store.Watchlist.Count == 0) return;
        var oldest = _store.Watchlist.Min(item => item.LastCheckedAt ?? DateTimeOffset.MinValue);
        if (DateTimeOffset.UtcNow - oldest <= StaleInterval) return;
        await RefreshAsync(force: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-checks when the oldest check is at least interval old. A failed check is retried
    /// after five minutes at the earliest, so being offline doesn't mean a request every tick.
    /// </summary>
    public async Task RefreshIfDueAsync(TimeSpan interval, CancellationToken cancellationToken = default)
    {
        if (_store.Watchlist.Count == 0) return;
        var oldest = _store.Watchlist.Min(item => item.LastCheckedAt ?? DateTimeOffset.MinValue);
        if (DateTimeOffset.UtcNow - oldest < interval) return;
        if (_lastAttempt is DateTimeOffset attempt && DateTimeOffset.UtcNow - attempt < Min(interval, FailedRetryFloor)) return;
        await RefreshAsync(force: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches live seats, updates the stored numbers and posts a notification for each section
    /// that gained seats or waitlist room. Without force, nothing happens within two minutes of the last attempt.
    /// </summary>
    public async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        Task? current;
        lock (_gate) current = _refreshTask;
        if (current is not null)
        {
            await current.ConfigureAwait(false);
            return;
        }
        if (_store.Watchlist.Count == 0) return;
        if (!force && _lastAttempt is DateTimeOffset last && DateTimeOffset.UtcNow - last < MinimumRefreshInterval) return;
        _lastAttempt = DateTimeOffset.UtcNow;
        IsRefreshing = true;
        var task = PerformRefreshAsync(cancellationToken);
        lock (_gate) _refreshTask = task;
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate) _refreshTask = null;
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// A scheduled-task check. Does nothing unless background refresh is on and the watchlist
    /// is not empty. Saves before notifying, so a crash in between cannot repeat an alert.
    /// </summary>
    public async Task<WatchlistComparison.Outcome?> BackgroundRefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!_store.Preferences.BackgroundRefreshEnabled || _store.Watchlist.Count == 0) return null;
        if (Notifier is { IsAuthorized: false }) return null;
        var live = await FetchLiveAsync(_store.Watchlist, cancellationToken).ConfigureAwait(false);
        if (live.Count == 0) return null;
        var outcome = WatchlistComparison.Compare(_store.Watchlist, live);
        _store.ReplaceWatchlist(outcome.Items);
        _store.FlushNow();
        RecordOutcome(outcome);
        Post(outcome);
        return outcome;
    }

    public void DidEraseWatchlist()
    {
        MissingItemIds.Clear();
        LastError = null;
        Notifier?.RemoveAll();
        Notifier?.ClearBadge();
    }

    private async Task PerformRefreshAsync(CancellationToken cancellationToken)
    {
        LastError = null;
        var failures = new List<Exception>();
        Dictionary<string, IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>> live;
        try
        {
            live = await FetchLiveAsync(_store.Watchlist, cancellationToken, failures).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (live.Count > 0)
        {
            var outcome = WatchlistComparison.Compare(_store.Watchlist, live);
            _store.ReplaceWatchlist(outcome.Items);
            LastRefresh = DateTimeOffset.UtcNow;
            RecordOutcome(outcome);
            Post(outcome);
        }
        if (failures.Count > 0) LastError = failures[0].Message;
    }

    private void RecordOutcome(WatchlistComparison.Outcome outcome)
    {
        LastRefresh = DateTimeOffset.UtcNow;
        var uncheckedIds = outcome.Items
            .Where(item => !outcome.CheckedTermCodes.Contains(item.TermCode))
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var kept = MissingItemIds.Where(uncheckedIds.Contains).ToHashSet(StringComparer.Ordinal);
        MissingItemIds.Clear();
        foreach (var id in outcome.MissingIds) MissingItemIds.Add(id);
        foreach (var id in kept) MissingItemIds.Add(id);
    }

    private void Post(WatchlistComparison.Outcome outcome)
    {
        if (Notifier is not { IsAuthorized: true } || outcome.Alerts.Count == 0) return;
        var openCount = outcome.Items.Count(item => item.LastSeatsRemaining > 0);
        Notifier.Post(WatchlistNotifications.Content(outcome.Alerts, openCount));
    }

    private async Task<Dictionary<string, IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>>> FetchLiveAsync(
        IReadOnlyList<WatchItem> items,
        CancellationToken cancellationToken,
        List<Exception>? failures = null)
    {
        var live = new Dictionary<string, IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>>(StringComparer.Ordinal);
        foreach (var termCode in items.Select(item => item.TermCode).Distinct().OrderByDescending(code => code, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keys = items.Where(item => item.TermCode == termCode).Select(item => item.CourseKey).Distinct().OrderBy(key => key).ToList();
            try
            {
                var banner = await _banner.FetchSectionsAsync(termCode, keys, cancellationToken).ConfigureAwait(false);
                var catalog = CatalogForTerm?.Invoke(termCode);
                var sections = new Dictionary<CourseKey, IReadOnlyList<CourseSection>>();
                foreach (var key in keys)
                {
                    var raw = banner.TryGetValue(key, out var found) ? found : [];
                    if (catalog is not null) CatalogBuilder.ApplyCourseRefresh(catalog, key, raw);
                    var made = catalog is null
                        ? CatalogBuilder.MakeSections(raw)
                        : catalog.Sections.Where(section => section.CourseKey == key).ToList();
                    sections[key] = made;
                }
                live[termCode] = sections;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures?.Add(ex);
            }
        }
        return live;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
