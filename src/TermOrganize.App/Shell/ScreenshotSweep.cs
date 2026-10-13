// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace TermOrganize;

/// <summary>
/// <c>--screenshot-sweep &lt;directory&gt;</c> walks every sidebar page in light and dark
/// at 1440×900 and 800×900 and writes a PNG of the window from <see cref="RenderTargetBitmap"/>.
/// </summary>
internal static class ScreenshotSweep
{
    public static bool Enabled { get; private set; }
    public static string Directory { get; private set; } = "ui-sweep";

    private static readonly object Gate = new();
    private static int _hooks;

    private static readonly (string Name, int Width, int Height)[] Sizes =
    [
        ("1440x900", 1440, 900),
        ("800x900", 800, 900),
    ];

    private static readonly (string Name, ElementTheme Theme)[] Themes =
    [
        ("light", ElementTheme.Light),
        ("dark", ElementTheme.Dark),
    ];

    private static readonly string[] Pages =
    [
        "search", "subjects", "saved", "professors", "rooms", "schedules", "degree", "watch", "settings",
    ];

    public static void Configure(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals("--screenshot-sweep", StringComparison.OrdinalIgnoreCase)) continue;
            Enabled = true;
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                var dir = args[i + 1].Trim().Trim('"');
                Directory = Path.GetFullPath(dir);
            }
        }
    }

    /// <summary>
    /// Records crashes that happen before <see cref="Main"/>, including the Windows App SDK module initializer.
    /// </summary>
    public static void EnsureHooks()
    {
        if (Interlocked.Exchange(ref _hooks, 1) != 0) return;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) WriteError(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            if (!Enabled) return;
            WriteError(e.Exception);
            e.SetObserved();
        };
        try
        {
            Configure(Environment.GetCommandLineArgs());
        }
        catch (Exception ex)
        {
            WriteError(ex);
        }
    }

    public static void Log(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(
                    Path.Combine(Directory, "sweep.log"),
                    $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // The workflow still has the process exit code and the event log.
        }
    }

    public static void WriteError(Exception? ex)
    {
        if (!Enabled) return;
        var text = ex?.ToString() ?? "Unknown exception";
        Log(text);
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(Path.Combine(Directory, "error.txt"), text + Environment.NewLine + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // The workflow still has the process exit code and the event log.
        }
    }

    public static async Task CaptureAsync(MainWindow window)
    {
        System.IO.Directory.CreateDirectory(Directory);
        Log($"capture directory {Directory}");
        var root = (FrameworkElement)window.Content;
        var laidOut = DateTime.UtcNow.AddSeconds(20);
        while (root.ActualWidth < 200 && DateTime.UtcNow < laidOut)
            await Task.Delay(100);
        if (root.ActualWidth < 200)
            throw new InvalidOperationException($"The window did not lay out (width {root.ActualWidth:0}).");
        Log($"laid out {root.ActualWidth:0}x{root.ActualHeight:0}");

        var catalog = DateTime.UtcNow.AddSeconds(30);
        while (App.Session.Index is null && App.Session.Error is null && DateTime.UtcNow < catalog)
            await Task.Delay(100);
        Log(App.Session.Error is null ? "catalog ready" : "catalog error: " + App.Session.Error);

        foreach (var size in Sizes)
        {
            window.ResizeForCapture(size.Width, size.Height);
            await Task.Delay(200);
            foreach (var theme in Themes)
            {
                window.ApplyCaptureTheme(theme.Theme);
                await Task.Delay(150);
                foreach (var page in Pages)
                {
                    Log($"page {theme.Name} {size.Name} {page}");
                    window.PrepareCapture(page);
                    root.UpdateLayout();
                    await Task.Delay(120);
                    var path = Path.Combine(Directory, $"{theme.Name}-{size.Name}-{page}.png");
                    await SaveAsync(root, path);
                }
            }
        }
        Log("capture finished");
    }

    private static async Task SaveAsync(UIElement element, string path)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(element);
                if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
                    throw new InvalidOperationException($"Render of {path} was empty.");
                var pixels = (await bitmap.GetPixelsAsync()).ToArray();
                using var stream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth,
                    (uint)bitmap.PixelHeight,
                    96,
                    96,
                    pixels);
                await encoder.FlushAsync();
                stream.Seek(0);
                await using var file = File.Create(path);
                await stream.AsStreamForRead().CopyToAsync(file);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(200);
            }
        }
        throw new InvalidOperationException($"Could not write {path}.", last);
    }
}
