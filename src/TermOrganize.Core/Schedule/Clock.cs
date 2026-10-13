// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Converts between minutes after midnight (what preferences store) and the dates time pickers use.

namespace TermOrganize.Core;

public static class ScheduleClock
{
    /// <summary>Today at <paramref name="minutes"/> after midnight. 24:00 clamps to 23:59.</summary>
    public static DateTime Date(int minutes, DateTime? day = null)
    {
        var clamped = Math.Clamp(minutes, 0, 24 * 60 - 1);
        var date = (day ?? DateTime.Now).Date;
        return date.AddHours(clamped / 60).AddMinutes(clamped % 60);
    }

    public static int Minutes(DateTime date) => date.Hour * 60 + date.Minute;
}
