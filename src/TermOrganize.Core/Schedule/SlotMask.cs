// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// Portions derived from SearchNEU where the Swift file says so. Modified 2026-10-13: ported from Swift to C#.

namespace TermOrganize.Core;

/// <summary>
/// 7 days × 288 five-minute slots. A one-minute overlap conflicts; a meeting that ends
/// when the next one starts does not. Bit index is <c>day * 288 + slot</c>.
/// </summary>
public sealed class ScheduleSlotMask : IEquatable<ScheduleSlotMask>
{
    public const int MinutesPerSlot = 5;
    public const int SlotsPerDay = 24 * 60 / MinutesPerSlot;
    public const int WordCount = 32;

    private readonly ulong[] _words = new ulong[WordCount];

    public ScheduleSlotMask() { }

    public ScheduleSlotMask(CourseSection section)
    {
        foreach (var meeting in section.WeeklyMeetings) Insert(meeting);
    }

    public ScheduleSlotMask(IEnumerable<Meeting> meetings)
    {
        foreach (var meeting in meetings) Insert(meeting);
    }

    public void Insert(Meeting meeting)
    {
        if (meeting.Start is not int start || meeting.End is not int end) return;
        foreach (var day in meeting.Days.Indices) Insert(day, start, end);
    }

    public void Insert(int day, int start, int end)
    {
        if (day is < 0 or > 6 || end <= start) return;
        var first = Math.Max(0, start / MinutesPerSlot);
        var lastExclusive = Math.Min(SlotsPerDay, Math.Max(first + 1, (end + MinutesPerSlot - 1) / MinutesPerSlot));
        if (first >= lastExclusive) return;
        for (var slot = first; slot < lastExclusive; slot++)
        {
            var bit = day * SlotsPerDay + slot;
            _words[bit >> 6] |= 1UL << (bit & 63);
        }
    }

    public bool IsEmpty
    {
        get
        {
            for (var i = 0; i < WordCount; i++)
                if (_words[i] != 0) return false;
            return true;
        }
    }

    public bool Conflicts(ScheduleSlotMask other)
    {
        for (var i = 0; i < WordCount; i++)
            if ((_words[i] & other._words[i]) != 0) return true;
        return false;
    }

    public ScheduleSlotMask Union(ScheduleSlotMask other)
    {
        var copy = new ScheduleSlotMask();
        for (var i = 0; i < WordCount; i++) copy._words[i] = _words[i] | other._words[i];
        return copy;
    }

    public bool Equals(ScheduleSlotMask? other)
    {
        if (other is null) return false;
        for (var i = 0; i < WordCount; i++)
            if (_words[i] != other._words[i]) return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ScheduleSlotMask);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (var i = 0; i < WordCount; i++) hash.Add(_words[i]);
        return hash.ToHashCode();
    }
}
