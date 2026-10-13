// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TermOrganize.Core;

/// <summary>Reads and writes the text layout of a Northeastern degree-audit "View Plan" export.</summary>
public static partial class DegreePlanFormat
{
    public static readonly string Separator = new('-', 115);
    public const int TitleLength = 30;

    public enum ImportErrorKind { Unreadable, NotADegreePlan }

    public sealed class ImportException : Exception
    {
        public ImportErrorKind Kind { get; }
        public ImportException(ImportErrorKind kind) : base(MessageFor(kind)) => Kind = kind;

        public static string MessageFor(ImportErrorKind kind) => kind switch
        {
            ImportErrorKind.Unreadable => "That file couldn't be read. Export the plan again as a PDF and try once more.",
            _ => "This doesn't look like a degree audit plan. In the degree audit planner, open your plan and choose View Plan, then import the PDF it creates.",
        };
    }

    public sealed class ParseResult
    {
        public required DegreePlan Plan { get; init; }
        public List<string> SkippedLines { get; init; } = [];
    }

    public static ParseResult Parse(string text, string? fileName = null)
    {
        var plan = new DegreePlan { Name = "", ImportedFrom = fileName };
        var skipped = new List<string>();
        var sawStructure = false;
        var section = Section.Header;
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = Normalize(raw);
            if (line.Length == 0 || line is "-" or "•") continue;
            if (HeaderValue(line, "Printed:") is string printed)
            {
                plan.PrintedAt = ParsePrintedDate(printed);
                continue;
            }
            var studentId = HeaderValue(line, "StudentID:") ?? HeaderValue(line, "Student ID:");
            if (studentId is not null)
            {
                plan.StudentID = BlankToNull(studentId);
                continue;
            }
            if (HeaderValue(line, "Student:") is string student)
            {
                plan.StudentName = BlankToNull(student);
                continue;
            }
            var planName = HeaderValue(line, "Degree Audit Plan Name:") ?? HeaderValue(line, "Plan Name:");
            if (planName is not null)
            {
                plan.Name = planName;
                sawStructure = true;
                continue;
            }
            if (HeaderValue(line, "Program:") is string program)
            {
                plan.Program = BlankToNull(program);
                continue;
            }
            if (line.StartsWith("completed courses", StringComparison.OrdinalIgnoreCase))
            {
                section = Section.Completed;
                sawStructure = true;
                continue;
            }
            if (IsSeparator(line))
            {
                section = Section.Between;
                continue;
            }
            if (ParseTermHeader(line) is DegreePlanTerm term)
            {
                plan.Terms.Add(term);
                section = Section.Term;
                sawStructure = true;
                continue;
            }
            if (ParseCourseLine(line) is DegreePlanCourse course)
            {
                switch (section)
                {
                    case Section.Term:
                        plan.Terms[^1].Courses.Add(course);
                        break;
                    case Section.Header:
                    case Section.Completed:
                        plan.Completed.Add(course);
                        break;
                    default:
                        if (plan.Terms.Count == 0) plan.Completed.Add(course);
                        else plan.Terms[^1].Courses.Add(course);
                        break;
                }
                continue;
            }
            skipped.Add(line);
        }
        if (!sawStructure || (plan.Completed.Count == 0 && plan.Terms.Count == 0 && plan.Name.Length == 0))
            throw new ImportException(ImportErrorKind.NotADegreePlan);
        if (plan.Name.Length == 0) plan.Name = "Imported Plan";
        plan.SortTerms();
        return new ParseResult { Plan = plan, SkippedLines = skipped };
    }

    public static DegreePlanTerm? ParseTermHeader(string line)
    {
        var match = TermHeaderPattern().Match(line);
        if (!match.Success) return null;
        var season = PlanSeasonInfo.ParseExportName(match.Groups[1].Value);
        if (season is null || !int.TryParse(match.Groups[2].Value, out var year)) return null;
        return new DegreePlanTerm { Season = season.Value, Year = year };
    }

    public static DegreePlanCourse? ParseCourseLine(string line)
    {
        var match = CourseLinePattern().Match(line);
        if (!match.Success || !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var credits))
            return null;
        var code = match.Groups[1].Value.Trim();
        var title = match.Groups[3].Value.Trim();
        if (ParseCourseCode(code) is CourseKey key)
            return new DegreePlanCourse(key, title, credits);
        if (code.Length > 40 || code.Contains(':')) return null;
        return new DegreePlanCourse(null, title, credits, code);
    }

    public static CourseKey? ParseCourseCode(string code)
    {
        var match = CourseCodePattern().Match(code.ToUpperInvariant());
        if (!match.Success) return null;
        return new CourseKey(match.Groups[1].Value, match.Groups[2].Value);
    }

    public static string Normalize(string line)
    {
        var builder = new StringBuilder(line.Length);
        foreach (var scalar in line.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(scalar);
            switch (category)
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.SpaceSeparator:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    builder.Append(' ');
                    break;
                case UnicodeCategory.Format:
                    break;
                default:
                    builder.Append(scalar.ToString());
                    break;
            }
        }
        var text = SpaceRun().Replace(builder.ToString(), " ").Trim();
        if (text.Length > 0 && Bullets.Contains(text[0]) && !IsSeparator(text))
            text = text.TrimStart('-', '•', '–', '—').Trim();
        return text;
    }

    public static DateTimeOffset? ParsePrintedDate(string value)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");
        foreach (var format in new[] { "MM/dd/yyyy HH:mm", "MM/dd/yyyy h:mm tt", "MM/dd/yyyy" })
        {
            if (!DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                continue;
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
        }
        return null;
    }

    public static string Hours(double credits)
    {
        var number = credits.ToString("0.0", CultureInfo.InvariantCulture);
        return $"({number} {(credits == 1 ? "Hour" : "Hours")})";
    }

    public static string ExportCode(DegreePlanCourse course)
    {
        if (course.Key is not CourseKey key) return course.Label;
        var subject = key.Subject.Length < 4 ? key.Subject.PadRight(4) : key.Subject;
        return subject + key.Number;
    }

    public static string ExportTitle(string title) => title.Length <= TitleLength ? title : title[..TitleLength];

    public static string CourseLine(DegreePlanCourse course) =>
        $"{ExportCode(course)} {Hours(course.Credits)} {ExportTitle(course.Title)}";

    public static string TermHeader(DegreePlanTerm term)
    {
        var name = term.Season.ExportName();
        var gap = name.Contains(' ') ? " " : "  ";
        return $"{name}{gap}{term.Year} {Hours(term.Credits)} ";
    }

    public static string PrintedLine(DateTimeOffset date)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");
        var local = TimeZoneInfo.ConvertTime(date, zone);
        return "Printed:  " + local.ToString("MM/dd/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    public static List<string> HeaderLines(DegreePlan plan, DateTimeOffset printedAt, bool includeStudent)
    {
        var lines = new List<string> { PrintedLine(printedAt) };
        if (includeStudent)
        {
            lines.Add($"Student:  {plan.StudentName ?? ""}");
            lines.Add($"StudentID:  {plan.StudentID ?? ""}");
        }
        lines.Add($"Degree Audit Plan Name:\t{plan.Name}");
        lines.Add($"Program:\t\t{plan.Program ?? ""}");
        return lines;
    }

    public static List<(int Indent, string Text)> BodyLines(DegreePlan plan)
    {
        var lines = new List<(int, string)> { (1, "Completed Courses:  ") };
        lines.AddRange(plan.Completed.Select(course => (2, CourseLine(course))));
        foreach (var term in plan.Terms)
        {
            lines.Add((1, Separator));
            lines.Add((1, TermHeader(term)));
            lines.AddRange(term.Courses.Select(course => (2, CourseLine(course))));
        }
        return lines;
    }

    public static string Text(DegreePlan plan, DateTimeOffset? printedAt = null, bool includeStudent = false)
    {
        var header = HeaderLines(plan, printedAt ?? DateTimeOffset.UtcNow, includeStudent);
        var body = BodyLines(plan).Select(line => new string(' ', line.Indent * 2) + line.Text);
        return string.Join('\n', header.Concat(body));
    }

    private enum Section { Header, Completed, Term, Between }

    private static readonly char[] Bullets = ['-', '•', '–', '—'];

    private static string? HeaderValue(string line, string label) =>
        line.StartsWith(label, StringComparison.OrdinalIgnoreCase) ? line[label.Length..].Trim() : null;

    private static bool IsSeparator(string line) =>
        line.Length >= 10 && line.All(ch => ch is '-' or '_' or '=');

    private static string? BlankToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex(@"^(.+?)\s+(\d{4})\s*\(\s*\d+(?:\.\d+)?\s*Hours?\s*\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TermHeaderPattern();

    [GeneratedRegex(@"^(.+?)\s*\(\s*(\d+(?:\.\d+)?)\s*Hours?\s*\)\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CourseLinePattern();

    [GeneratedRegex(@"^([A-Z]{2,5})\s*(\d{4}[A-Z]?)$", RegexOptions.CultureInvariant)]
    private static partial Regex CourseCodePattern();

    [GeneratedRegex(" {2,}", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceRun();
}
