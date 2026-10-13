// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;

namespace TermOrganize.Core;

/// <summary>Swift <c>PlanSeason</c> raw values (<c>summerA</c>, not <c>SummerA</c>).</summary>
internal static class PlanSeasonNames
{
    public static string Raw(PlanSeason season) => season switch
    {
        PlanSeason.Winter => "winter",
        PlanSeason.Spring => "spring",
        PlanSeason.SummerA => "summerA",
        PlanSeason.SummerFull => "summerFull",
        PlanSeason.SummerB => "summerB",
        PlanSeason.Fall => "fall",
        _ => "fall",
    };
}

/// <summary>Wording shared by the degree plan screens.</summary>
public static class DegreePlanText
{
    public static string Number(double value) => CreditFormatter.Number(value);

    public static string Credits(double value) => $"{Number(value)} {(value == 1 ? "credit" : "credits")}";

    public static string Courses(int count) => count == 1 ? "1 course" : $"{count} courses";

    public static string Semesters(int count) => count == 1 ? "1 semester" : $"{count} semesters";

    /// <summary><c>Oct 3, 2026</c>. Defaults to the local time zone.</summary>
    public static string Date(DateTimeOffset date, TimeZoneInfo? timeZone = null)
    {
        var local = TimeZoneInfo.ConvertTime(date, timeZone ?? TimeZoneInfo.Local);
        return local.ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));
    }

    public static string ChipName(this DegreePlanTerm term)
    {
        var name = term.Season switch
        {
            PlanSeason.Winter => "Wi",
            PlanSeason.Spring => "Sp",
            PlanSeason.SummerA => "Su A",
            PlanSeason.SummerFull => "Su",
            PlanSeason.SummerB => "Su B",
            _ => "Fa",
        };
        return $"{name} \u2019{term.Year % 100:00}";
    }

    public static string SummaryLine(this DegreePlanTerm term) =>
        $"{Credits(term.Credits)} \u00b7 {Courses(term.Courses.Count)}";

    /// <summary>Banner code for this semester (<c>202730</c> for Spring 2027).</summary>
    public static string BannerCode(this DegreePlanTerm term) => term.Season switch
    {
        PlanSeason.Fall => $"{term.Year + 1}10",
        PlanSeason.Winter => $"{term.Year}20",
        PlanSeason.Spring => $"{term.Year}30",
        PlanSeason.SummerA => $"{term.Year}40",
        PlanSeason.SummerFull => $"{term.Year}50",
        PlanSeason.SummerB => $"{term.Year}60",
        _ => $"{term.Year}10",
    };

    public static bool HasEnded(this DegreePlanTerm term, DateTimeOffset? now = null)
    {
        var window = TermCalendar.ApproximateWindow(term.BannerCode());
        if (window is null) return false;
        return window.Value.End < (now ?? DateTimeOffset.Now);
    }

    public static List<DegreePlanTerm> UpcomingTerms(this DegreePlan plan, DateTimeOffset? now = null) =>
        plan.Terms.Where(term => !term.HasEnded(now)).ToList();

    public static DegreePlanTerm? NextTerm(this DegreePlan plan, DateTimeOffset? now = null) =>
        plan.UpcomingTerms(now).FirstOrDefault(term => term.Courses.Count > 0);

    public static bool HasStudentInfo(this DegreePlan plan) => plan.StudentName is not null || plan.StudentID is not null;

    public static string SourceLine(this DegreePlan plan)
    {
        if (plan.ImportedFrom is null) return "Created " + Date(plan.CreatedAt);
        var line = "Imported from " + plan.ImportedFrom;
        if (plan.PrintedAt is DateTimeOffset printed)
            line += " \u00b7 printed " + Date(printed, TermCalendar.NewYork);
        return line;
    }

    public static string CompletedLine(this DegreePlan plan) =>
        plan.Completed.Count == 0 ? "No completed courses yet" : Credits(plan.CompletedCredits) + " completed";

    public static string? NextSummary(this DegreePlan plan, DateTimeOffset? now = null)
    {
        var next = plan.NextTerm(now);
        return next is null ? null : $"{next.Name} \u00b7 {Credits(next.Credits)} \u00b7 {Courses(next.Courses.Count)}";
    }

    public static string NextLine(this DegreePlan plan, DateTimeOffset? now = null)
    {
        var summary = plan.NextSummary(now);
        if (summary is not null) return "Next: " + summary;
        if (plan.Terms.Count == 0) return "No semesters planned yet";
        if (plan.UpcomingTerms(now).Count == 0) return "No upcoming semesters";
        return "No courses planned yet";
    }
}
