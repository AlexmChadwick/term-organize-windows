// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TermOrganize.Core;

namespace TermOrganize.Mcp;

public static class TextFormat
{
    private static readonly Regex Email = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled);

    public static bool IsAllowedEmailDomain(string domain) =>
        domain.Equals("northeastern.edu", StringComparison.OrdinalIgnoreCase)
        || domain.Equals("neu.edu", StringComparison.OrdinalIgnoreCase);

    /// <summary>Drops any address whose domain is not exactly northeastern.edu or neu.edu.</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return Email.Replace(text, match =>
        {
            var at = match.Value.LastIndexOf('@');
            if (at < 0) return "";
            return IsAllowedEmailDomain(match.Value[(at + 1)..]) ? match.Value : "";
        }).Trim();
    }

    public static string TimeRange(int start, int end) =>
        $"{TimeOfDay.Label(start)} to {TimeOfDay.Label(end)}";

    public static int? ParseClock(JsonElement? element)
    {
        if (element is not JsonElement value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (!value.TryGetInt32(out var minutes))
                throw new ModelContextProtocol.McpException("Time must be a whole number of minutes.");
            return CheckMinutes(minutes);
        }
        if (value.ValueKind != JsonValueKind.String)
            throw new ModelContextProtocol.McpException("Time must be HH:MM or minutes from midnight.");
        var text = value.GetString()?.Trim() ?? "";
        if (text.Length == 0) return null;
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var asMinutes))
            return CheckMinutes(asMinutes);
        var period = "";
        var clock = text;
        if (text.EndsWith("AM", StringComparison.OrdinalIgnoreCase) || text.EndsWith("PM", StringComparison.OrdinalIgnoreCase))
        {
            period = text[^2..];
            clock = text[..^2].Trim();
        }
        var parts = clock.Split(':');
        if (parts.Length is < 2 or > 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minute))
            throw new ModelContextProtocol.McpException($"Could not read time '{text}'.");
        if (period.Length > 0)
        {
            if (hour < 1 || hour > 12 || minute is < 0 or > 59)
                throw new ModelContextProtocol.McpException($"Could not read time '{text}'.");
            if (hour == 12) hour = 0;
            if (period.Equals("PM", StringComparison.OrdinalIgnoreCase)) hour += 12;
        }
        else if (hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            throw new ModelContextProtocol.McpException($"Could not read time '{text}'.");
        }
        return CheckMinutes(hour * 60 + minute);
    }

    public static DaySet ParseDaysOff(IEnumerable<string>? days)
    {
        var set = DaySet.None;
        if (days is null) return set;
        foreach (var raw in days)
        {
            var text = raw.Trim();
            if (text.Length == 0) continue;
            if (TryOneDay(text, out var one))
            {
                set |= one;
                continue;
            }
            var upper = text.ToUpperInvariant();
            if (upper.Length > 1 && upper.All(ch => "UMTWRFS".Contains(ch)))
            {
                foreach (var ch in upper)
                    set |= OneLetter(ch);
                continue;
            }
            throw new ModelContextProtocol.McpException($"Unknown day '{text}'. Use U, M, T, W, R, F, or S.");
        }
        return set;
    }

    public static bool TryParseTerm(string? text, out PlanSeason season, out int year, out string code)
    {
        season = PlanSeason.Fall;
        year = 0;
        code = "";
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.Length == 6 && trimmed.All(char.IsDigit))
        {
            var term = new Term(trimmed, "");
            season = PlanSeasonInfo.FromTerm(term.Season);
            year = term.CalendarYear;
            code = trimmed;
            return year > 0;
        }
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out year))
            return false;
        var name = string.Join(' ', parts[..^1]);
        if (PlanSeasonInfo.ParseExportName(name) is not PlanSeason parsed) return false;
        season = parsed;
        code = BannerCode(parsed, year);
        return true;
    }

    public static string BannerCode(PlanSeason season, int year) => season switch
    {
        PlanSeason.Fall => $"{year + 1}10",
        PlanSeason.Winter => $"{year}20",
        PlanSeason.Spring => $"{year}30",
        PlanSeason.SummerA => $"{year}40",
        PlanSeason.SummerFull => $"{year}50",
        PlanSeason.SummerB => $"{year}60",
        _ => $"{year}30",
    };

    private static int CheckMinutes(int minutes)
    {
        if (minutes is < 0 or > 24 * 60)
            throw new ModelContextProtocol.McpException("Time is outside 00:00 to 24:00.");
        return minutes;
    }

    private static bool TryOneDay(string text, out DaySet day)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "u" or "su" or "sun" or "sunday": day = DaySet.Sunday; return true;
            case "m" or "mo" or "mon" or "monday": day = DaySet.Monday; return true;
            case "t" or "tu" or "tue" or "tues" or "tuesday": day = DaySet.Tuesday; return true;
            case "w" or "we" or "wed" or "wednesday": day = DaySet.Wednesday; return true;
            case "r" or "th" or "thu" or "thur" or "thurs" or "thursday": day = DaySet.Thursday; return true;
            case "f" or "fr" or "fri" or "friday": day = DaySet.Friday; return true;
            case "s" or "sa" or "sat" or "saturday": day = DaySet.Saturday; return true;
            default: day = DaySet.None; return false;
        }
    }

    private static DaySet OneLetter(char letter) => letter switch
    {
        'U' => DaySet.Sunday,
        'M' => DaySet.Monday,
        'T' => DaySet.Tuesday,
        'W' => DaySet.Wednesday,
        'R' => DaySet.Thursday,
        'F' => DaySet.Friday,
        'S' => DaySet.Saturday,
        _ => DaySet.None,
    };
}
