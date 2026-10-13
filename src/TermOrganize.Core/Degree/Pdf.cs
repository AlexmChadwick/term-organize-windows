// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text;
using UglyToad.PdfPig;

namespace TermOrganize.Core;

public static class DegreePlanImporter
{
    public static DegreePlanFormat.ParseResult Import(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var name = Path.GetFileName(path);
        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || IsPdf(bytes))
        {
            var text = TextFromPdf(bytes) ?? throw new DegreePlanFormat.ImportException(DegreePlanFormat.ImportErrorKind.Unreadable);
            return DegreePlanFormat.Parse(text, name);
        }
        return DegreePlanFormat.Parse(Encoding.UTF8.GetString(bytes), name);
    }

    public static DegreePlanFormat.ParseResult Import(byte[] data, string? fileName = null, bool pdf = false)
    {
        if (pdf || IsPdf(data))
        {
            var text = TextFromPdf(data) ?? throw new DegreePlanFormat.ImportException(DegreePlanFormat.ImportErrorKind.Unreadable);
            return DegreePlanFormat.Parse(text, fileName);
        }
        return DegreePlanFormat.Parse(Encoding.UTF8.GetString(data), fileName);
    }

    public static string? TextFromPdf(byte[] data)
    {
        if (!IsPdf(data)) return null;
        using var document = PdfDocument.Open(data);
        var builder = new StringBuilder();
        var any = false;
        foreach (var page in document.GetPages())
        {
            var lines = page.GetWords()
                .GroupBy(word => Math.Round(word.BoundingBox.Bottom, 1))
                .OrderByDescending(group => group.Key);
            foreach (var line in lines)
            {
                any = true;
                builder.AppendLine(string.Join(' ', line.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text)));
            }
        }
        return any ? builder.ToString() : null;
    }

    private static bool IsPdf(byte[] data) =>
        data.Length >= 5 && data[0] == (byte)'%' && data[1] == (byte)'P' && data[2] == (byte)'D' && data[3] == (byte)'F';
}

/// <summary>
/// A4 "View Plan" PDF: Helvetica 12 pt, 18 pt lines, baselines 54 through 774,
/// dash bullets 30 pt left of indented lines. Header tabs are drawn as spaces.
/// </summary>
public static class DegreePlanPdf
{
    public const double PageWidth = 595;
    public const double PageHeight = 842;
    public const double LineHeight = 18;
    public const double FirstBaseline = 54;
    public const double LastBaseline = 774;
    private static readonly double[] IndentX = [36, 96, 126];

    public static byte[] Render(DegreePlan plan, DateTimeOffset? printedAt = null, bool includeStudent = false)
    {
        var when = printedAt ?? DateTimeOffset.UtcNow;
        var lines = new List<(int Indent, string Text, bool Header)>();
        lines.Add((0, " ", false));
        lines.Add((0, " ", false));
        lines.AddRange(DegreePlanFormat.HeaderLines(plan, when, includeStudent).Select(text => (0, text, true)));
        lines.AddRange(DegreePlanFormat.BodyLines(plan).Select(line => (line.Indent, line.Text, false)));
        lines.Add((0, " ", false));
        lines.Add((0, " ", false));
        var lastVisible = 0;
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].Text != " ") lastVisible = i;

        var pages = new List<List<(int Indent, string Text, bool Header)>>();
        List<(int Indent, string Text, bool Header)>? page = null;
        var baseline = LastBaseline + 1;
        for (var index = 0; index < lines.Count; index++)
        {
            if (baseline > LastBaseline)
            {
                if (index > lastVisible) break;
                page = [];
                pages.Add(page);
                baseline = FirstBaseline;
            }
            page!.Add(lines[index]);
            baseline += LineHeight;
        }
        return WritePdf(pages, plan.Name);
    }

    public static string TemporaryFile(DegreePlan plan, bool includeStudent)
    {
        var safe = string.Concat((plan.Name.Length == 0 ? "Degree Plan" : plan.Name).Select(ch => "/\\?%*|\"<>:".Contains(ch) ? '-' : ch));
        var directory = Path.Combine(Path.GetTempPath(), "DegreePlanExport");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, safe + " – view-plan.pdf");
        File.WriteAllBytes(path, Render(plan, includeStudent: includeStudent));
        return path;
    }

    private static byte[] WritePdf(List<List<(int Indent, string Text, bool Header)>> pages, string title)
    {
        var objects = new List<string>();
        // 1 catalog, 2 pages, 3 font regular, 4 font header, then page + content pairs.
        var pageObjectNumbers = new List<int>();
        var next = 5;
        var content = new List<(int PageId, int ContentId, string Stream)>();
        foreach (var page in pages)
        {
            var pageId = next++;
            var contentId = next++;
            pageObjectNumbers.Add(pageId);
            content.Add((pageId, contentId, ContentStream(page)));
        }
        objects.Add("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");
        var kids = string.Join(' ', pageObjectNumbers.Select(id => $"{id} 0 R"));
        objects.Add($"2 0 obj << /Type /Pages /Count {pages.Count} /Kids [{kids}] >> endobj\n");
        objects.Add("3 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >> endobj\n");
        objects.Add("4 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica-BoldOblique /Encoding /WinAnsiEncoding >> endobj\n");
        foreach (var (pageId, contentId, stream) in content)
        {
            objects.Add($"{pageId} 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >> endobj\n");
            var bytes = Encoding.ASCII.GetBytes(stream);
            objects.Add($"{contentId} 0 obj << /Length {bytes.Length} >> stream\n{stream}endstream\nendobj\n");
        }
        var header = "%PDF-1.4\n";
        var builder = new StringBuilder(header);
        var offsets = new List<int> { 0 };
        foreach (var obj in objects)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(obj);
        }
        var xrefAt = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append($"xref\n0 {objects.Count + 1}\n");
        builder.Append("0000000000 65535 f \n");
        for (var i = 1; i <= objects.Count; i++)
            builder.Append(offsets[i].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
        var safeTitle = Escape(title);
        builder.Append($"trailer << /Size {objects.Count + 1} /Root 1 0 R /Info << /Creator (Term Organize) /Title (Degree Audit Plan \\226 {safeTitle}) >> >>\n");
        builder.Append($"startxref\n{xrefAt}\n%%EOF");
        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    private static string ContentStream(List<(int Indent, string Text, bool Header)> lines)
    {
        var builder = new StringBuilder();
        builder.Append("BT\n");
        var baseline = FirstBaseline;
        foreach (var line in lines)
        {
            var y = PageHeight - baseline;
            var shown = line.Text.Replace('\t', ' ');
            Draw(builder, shown, IndentX[line.Indent], y, line.Header);
            if (line.Indent > 0) Draw(builder, "-", IndentX[line.Indent] - 30, y, false);
            baseline += LineHeight;
        }
        builder.Append("ET\n");
        return builder.ToString();
    }

    private static void Draw(StringBuilder builder, string text, double x, double y, bool header)
    {
        if (text.Length == 0) return;
        builder.Append(header ? "/F2 12 Tf\n" : "/F1 12 Tf\n");
        builder.Append(FormattableString.Invariant($"1 0 0 1 {x:0.###} {y:0.###} Tm\n"));
        builder.Append('(').Append(Escape(text)).Append(") Tj\n");
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '(' or ')' or '\\') builder.Append('\\');
            if (ch is >= (char)32 and <= (char)126) builder.Append(ch);
            else if (ch == '\u2013') builder.Append("\\226"); // not used inside parentheses this way
            else builder.Append('?');
        }
        return builder.ToString();
    }
}
