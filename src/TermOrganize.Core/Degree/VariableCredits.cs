// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

/// <summary>
/// Banner's credit range for one course. <see cref="DefaultHours"/> is the catalog default
/// (<c>creditHours</c>), not the value the student keeps.
/// </summary>
public readonly record struct CreditSpan(double Low, double High, double DefaultHours)
{
    public bool IsVariable => High != Low;

    /// <summary>Whole default when it sits inside the range, otherwise the low end.</summary>
    public double Starting => IsVariable && Fits(Whole(DefaultHours)) ? Whole(DefaultHours) : Whole(Low);

    public double Clamp(double value)
    {
        var whole = Whole(value);
        if (whole < Low) return Whole(Low);
        if (whole > High) return Whole(High);
        return whole;
    }

    public bool Fits(double value) => value >= Low && value <= High;

    public static double Whole(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
        return Math.Round(value, MidpointRounding.AwayFromZero);
    }
}

/// <summary>Chosen credits for a variable-credit course. Fixed courses keep the catalog value.</summary>
public static class VariableCredits
{
    /// <summary>
    /// A high value that differs from the low value is variable. A missing high is fixed.
    /// The default is <paramref name="creditHours"/>, or the low value when Banner omits it.
    /// </summary>
    public static CreditSpan FromBanner(double? creditHours, double? low, double? high)
    {
        var resolvedLow = low ?? creditHours ?? 0;
        var resolvedHigh = high ?? resolvedLow;
        return new CreditSpan(resolvedLow, resolvedHigh, creditHours ?? resolvedLow);
    }

    /// <summary>
    /// The span stored on a course. Bundled catalogs omitted <c>creditHours</c>, so a variable
    /// course with no default uses 4 when 4 is inside the range, which is Banner's usual default.
    /// </summary>
    public static CreditSpan Of(Course course)
    {
        var fallback = course.DefaultCredits;
        if (fallback == 0 && course.MaxCredits != course.MinCredits)
            fallback = course.MinCredits <= 4 && 4 <= course.MaxCredits ? 4 : course.MinCredits;
        else if (fallback == 0)
            fallback = course.MinCredits;
        return new CreditSpan(course.MinCredits, course.MaxCredits, fallback);
    }

    public static string Caption(double chosen, CreditSpan span) =>
        $"Credits: {CreditFormatter.Number(chosen)} ({CreditFormatter.Number(span.Low)} to {CreditFormatter.Number(span.High)})";

    public static bool IsWhole(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value - CreditSpan.Whole(value)) < 1e-6;

    /// <summary>
    /// Credits to store for a new row. A missing value uses the starting default.
    /// A fixed course accepts only its own credit value. A variable course accepts a whole number inside the range.
    /// </summary>
    public static bool TryResolve(Course course, double? requested, out double credits, out string? error)
    {
        var span = Of(course);
        if (requested is not double chosen)
        {
            credits = span.Starting;
            error = null;
            return true;
        }
        if (!span.IsVariable)
        {
            credits = span.Starting;
            if (!IsWhole(chosen) || Math.Abs(chosen - credits) > 1e-6)
            {
                error = $"{course.Code} is fixed at {CreditFormatter.Number(credits)} credits.";
                return false;
            }
            error = null;
            return true;
        }
        if (!IsWhole(chosen) || !span.Fits(chosen))
        {
            credits = span.Starting;
            error = $"credits must be a whole number from {CreditFormatter.Number(span.Low)} to {CreditFormatter.Number(span.High)}.";
            return false;
        }
        credits = span.Clamp(chosen);
        error = null;
        return true;
    }

    /// <summary>Rejects a fixed course and a value outside the range. Used by set_credits.</summary>
    public static bool TrySet(Course course, double credits, out double chosen, out string? error)
    {
        var span = Of(course);
        chosen = span.Starting;
        if (!span.IsVariable)
        {
            error = $"{course.Code} is fixed at {CreditFormatter.Number(span.Starting)} credits.";
            return false;
        }
        if (!IsWhole(credits) || !span.Fits(credits))
        {
            error = $"credits must be a whole number from {CreditFormatter.Number(span.Low)} to {CreditFormatter.Number(span.High)}.";
            return false;
        }
        chosen = span.Clamp(credits);
        error = null;
        return true;
    }

    /// <summary>The value a schedule slot should count. Null keeps the catalog range, for a fixed course.</summary>
    public static double? Exact(Course? course, double? chosen)
    {
        if (course is not { IsVariableCredit: true }) return null;
        var span = Of(course);
        return chosen is double set && set > 0 ? span.Clamp(set) : span.Starting;
    }

    /// <summary>Fills a missing schedule credit and clamps a saved one into the current range.</summary>
    public static bool Fill(PlanCourse row, Course course)
    {
        if (!course.IsVariableCredit) return false;
        var span = Of(course);
        var next = row.Credits is not double chosen || chosen <= 0 ? span.Starting : span.Clamp(chosen);
        if (row.Credits is double current && Math.Abs(current - next) < 1e-6) return false;
        row.Credits = next;
        return true;
    }

    /// <summary>A missing or non-positive degree credit becomes the default. An in-range choice stays.</summary>
    public static bool Fill(DegreePlanCourse row, Course course)
    {
        if (!course.IsVariableCredit) return false;
        var span = Of(course);
        var next = row.Credits <= 0 ? span.Starting : span.Clamp(row.Credits);
        if (Math.Abs(row.Credits - next) < 1e-6) return false;
        row.Credits = next;
        return true;
    }

    /// <summary>Writes chosen credits that were missing or sat outside a refreshed range. In-range choices stay.</summary>
    public static void ClampStored(UserDataStore store, Func<CourseKey, Course?> lookup)
    {
        foreach (var plan in store.DegreePlans.ToList())
        {
            var dirty = false;
            foreach (var row in plan.Completed.Concat(plan.Terms.SelectMany(term => term.Courses)))
            {
                if (row.Key is not CourseKey key || lookup(key) is not Course course) continue;
                dirty |= Fill(row, course);
            }
            if (dirty) store.UpdateDegreePlan(plan.Id, _ => { });
        }
        foreach (var schedule in store.Plans.ToList())
        {
            var dirty = false;
            foreach (var row in schedule.Courses)
            {
                if (lookup(row.Key) is not Course course) continue;
                dirty |= Fill(row, course);
            }
            if (dirty) store.UpdatePlan(schedule.Id, _ => { });
        }
    }
}
