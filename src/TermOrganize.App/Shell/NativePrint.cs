// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.InteropServices;

namespace TermOrganize;

/// <summary>
/// The Windows print dialog, then GDI text pages. WinUI has no print document,
/// and opening a PDF is not the print panel the Mac shows for Ctrl+P.
/// </summary>
internal static class NativePrint
{
    private const int PdReturnDc = 0x00000100;
    private const int PdUseDevModeCopies = 0x00040000;
    private const int PdNoSelection = 0x00000004;
    private const int HorzRes = 8;
    private const int VertRes = 10;
    private const int LogPixelsX = 88;
    private const int LogPixelsY = 90;

    public static bool Show(nint owner, string documentName, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return false;
        var dialog = new PRINTDLGW
        {
            lStructSize = Marshal.SizeOf<PRINTDLGW>(),
            hwndOwner = owner,
            Flags = PdReturnDc | PdUseDevModeCopies | PdNoSelection,
        };
        if (!PrintDlgW(ref dialog) || dialog.hDC == 0)
        {
            Free(dialog);
            return false;
        }
        var hdc = dialog.hDC;
        nint font = 0;
        nint previous = 0;
        nint title = 0;
        var started = false;
        try
        {
            var dpiY = Math.Max(72, GetDeviceCaps(hdc, LogPixelsY));
            var dpiX = Math.Max(72, GetDeviceCaps(hdc, LogPixelsX));
            font = CreateFontW(-dpiY * 11 / 72, 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 0, 0, "Segoe UI");
            if (font != 0) previous = SelectObject(hdc, font);
            SetBkMode(hdc, 1);
            title = Marshal.StringToCoTaskMemUni(documentName);
            var info = new DOCINFOW { cbSize = Marshal.SizeOf<DOCINFOW>(), lpszDocName = title };
            if (StartDocW(hdc, ref info) <= 0) return false;
            started = true;
            var width = GetDeviceCaps(hdc, HorzRes);
            var height = GetDeviceCaps(hdc, VertRes);
            var marginX = dpiX * 3 / 4;
            var marginY = dpiY * 3 / 4;
            var lineHeight = dpiY * 16 / 96;
            var chars = Math.Max(20, (width - marginX * 2) / Math.Max(1, dpiX * 6 / 72));
            var y = marginY;
            if (StartPage(hdc) <= 0) return false;
            foreach (var line in Wrap(lines, chars))
            {
                if (y + lineHeight > height - marginY)
                {
                    EndPage(hdc);
                    if (StartPage(hdc) <= 0) return false;
                    y = marginY;
                }
                TextOutW(hdc, marginX, y, line, line.Length);
                y += lineHeight;
            }
            EndPage(hdc);
            EndDoc(hdc);
            started = false;
            return true;
        }
        finally
        {
            if (started) AbortDoc(hdc);
            if (title != 0) Marshal.FreeCoTaskMem(title);
            if (previous != 0) SelectObject(hdc, previous);
            if (font != 0) DeleteObject(font);
            DeleteDC(hdc);
            Free(dialog);
        }
    }

    private static void Free(PRINTDLGW dialog)
    {
        if (dialog.hDevMode != 0) GlobalFree(dialog.hDevMode);
        if (dialog.hDevNames != 0) GlobalFree(dialog.hDevNames);
    }

    private static IEnumerable<string> Wrap(IReadOnlyList<string> lines, int width)
    {
        foreach (var line in lines)
        {
            if (line.Length <= width)
            {
                yield return line;
                continue;
            }
            for (var i = 0; i < line.Length; i += width)
                yield return line.Substring(i, Math.Min(width, line.Length - i));
        }
    }

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    private struct PRINTDLGW
    {
        [FieldOffset(0)] public int lStructSize;
        [FieldOffset(8)] public nint hwndOwner;
        [FieldOffset(16)] public nint hDevMode;
        [FieldOffset(24)] public nint hDevNames;
        [FieldOffset(32)] public nint hDC;
        [FieldOffset(40)] public int Flags;
        [FieldOffset(44)] public short nFromPage;
        [FieldOffset(46)] public short nToPage;
        [FieldOffset(48)] public short nMinPage;
        [FieldOffset(50)] public short nMaxPage;
        [FieldOffset(52)] public short nCopies;
        [FieldOffset(56)] public nint hInstance;
        [FieldOffset(64)] public nint lCustData;
        [FieldOffset(72)] public nint lpfnPrintHook;
        [FieldOffset(80)] public nint lpfnSetupHook;
        [FieldOffset(88)] public nint lpPrintTemplateName;
        [FieldOffset(96)] public nint lpSetupTemplateName;
        [FieldOffset(104)] public nint hPrintTemplate;
        [FieldOffset(112)] public nint hSetupTemplate;
    }

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    private struct DOCINFOW
    {
        [FieldOffset(0)] public int cbSize;
        [FieldOffset(8)] public nint lpszDocName;
        [FieldOffset(16)] public nint lpszOutput;
        [FieldOffset(24)] public nint lpszDatatype;
        [FieldOffset(32)] public int fwType;
        [FieldOffset(36)] private int _pad;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PrintDlgW(ref PRINTDLGW dialog);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int StartDocW(nint hdc, ref DOCINFOW info);

    [DllImport("gdi32.dll")] private static extern int AbortDoc(nint hdc);
    [DllImport("gdi32.dll")] private static extern int StartPage(nint hdc);
    [DllImport("gdi32.dll")] private static extern int EndPage(nint hdc);
    [DllImport("gdi32.dll")] private static extern int EndDoc(nint hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(nint hdc, int index);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint hdc, int mode);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TextOutW(nint hdc, int x, int y, string text, int length);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
}
