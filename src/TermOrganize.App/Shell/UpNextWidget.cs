// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.Widgets.Providers;
using TermOrganize.Core;
using WinRT;

namespace TermOrganize;

/// <summary>
/// Windows 11 Widgets Board provider for Up Next. Packaged installs only.
/// The host launches <c>TermOrganize.App.exe -RegisterWidgetProvider</c>.
/// Unpackaged runs keep the tray flyout.
/// </summary>
/// <summary>
/// Partial so the Windows App SDK source generator can emit the WinRT vtable.
/// The GUID is the COM class the package manifest activates.
/// </summary>
[Guid("B4E8C1A7-6F32-4D19-8A55-3C9E1D70B2F6")]
public sealed partial class UpNextWidgetProvider : IWidgetProvider
{
    public const string DefinitionId = "UpNext";
    public static readonly Guid ClassId = new("B4E8C1A7-6F32-4D19-8A55-3C9E1D70B2F6");

    private readonly Dictionary<string, bool> _active = new(StringComparer.Ordinal);

    public UpNextWidgetProvider()
    {
        try
        {
            foreach (var info in WidgetManager.GetDefault().GetWidgetInfos())
            {
                if (info.WidgetContext.DefinitionId == DefinitionId)
                    _active[info.WidgetContext.Id] = false;
            }
        }
        catch (Exception)
        {
            // The host calls CreateWidget when this process was not serving anyone yet.
        }
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        _active[widgetContext.Id] = true;
        Push(widgetContext.Id);
    }

    public void DeleteWidget(string widgetId, string customState) => _active.Remove(widgetId);

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs) =>
        Push(actionInvokedArgs.WidgetContext.Id);

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs) =>
        Push(contextChangedArgs.WidgetContext.Id);

    public void Activate(WidgetContext widgetContext)
    {
        _active[widgetContext.Id] = true;
        Push(widgetContext.Id);
    }

    public void Deactivate(string widgetId)
    {
        if (_active.ContainsKey(widgetId)) _active[widgetId] = false;
    }

    private static void Push(string widgetId)
    {
        try
        {
            var line = JsonSerializer.Serialize(UpNextLines.FromDisk());
            var template = $$"""
            {
              "type": "AdaptiveCard",
              "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
              "version": "1.5",
              "body": [
                { "type": "TextBlock", "text": "Up Next", "weight": "Bolder", "size": "Medium", "color": "Attention" },
                { "type": "TextBlock", "text": {{line}}, "wrap": true, "size": "Large" },
                { "type": "TextBlock", "text": "From your primary schedule. Unofficial, and not affiliated with Northeastern.", "wrap": true, "isSubtle": true, "size": "Small" }
              ]
            }
            """;
            var update = new WidgetUpdateRequestOptions(widgetId)
            {
                Template = template,
                Data = "{}",
                CustomState = "",
            };
            WidgetManager.GetDefault().UpdateWidget(update);
        }
        catch (Exception)
        {
            // A failed push leaves the last card. The tray flyout is still there.
        }
    }
}

internal static class UpNextLines
{
    public static string Current(UserDataStore store, CatalogIndex? index, DateTime now)
    {
        var term = index?.Term;
        var plan = term is null ? null : store.PrimaryPlan(term.Code);
        if (index is null || plan is null || term is null)
            return "No class on the calendar yet. Pick sections in a schedule.";
        var minutes = now.Hour * 60 + now.Minute;
        var today = (int)now.DayOfWeek;
        string? next = null;
        var best = int.MaxValue;
        foreach (var crn in plan.SelectedCRNs)
        {
            var section = index.Section(crn);
            if (section is null) continue;
            foreach (var meeting in section.WeeklyMeetings)
            {
                if (meeting.Start is not int start || !meeting.Days.Contains(today)) continue;
                var delta = start >= minutes - 60 ? start - minutes : start + 24 * 60 - minutes;
                if (delta < best)
                {
                    best = delta;
                    next = $"{section.CourseKey} \u00b7 {meeting.Days.CompactLabel} {meeting.TimeLabel} \u00b7 {meeting.LocationLabel}";
                }
            }
        }
        return next ?? "Nothing else today.";
    }

    public static string FromDisk()
    {
        try
        {
            var store = new UserDataStore();
            var code = store.Preferences.SelectedTermCode;
            if (string.IsNullOrEmpty(code))
                return "No class on the calendar yet. Pick sections in a schedule.";
            TermCatalog? catalog = null;
            try { catalog = TermCatalogCache.TryLoad(code); } catch (Exception) { catalog = null; }
            if (catalog is null)
            {
                try { catalog = BundledCatalog.TryLoad(code); }
                catch (Exception) { catalog = null; }
            }
            return Current(store, catalog is null ? null : CatalogIndex.Build(catalog), TermCalendar.Now());
        }
        catch (Exception)
        {
            return "Up Next couldn\u2019t read the schedule.";
        }
    }
}

/// <summary>Registers the widget class factory and pumps until the host releases the process.</summary>
internal static class UpNextWidgetHost
{
    private const uint ClsctxLocalServer = 4;
    private const uint RegclsMultipleUse = 1;
    private const int ENoInterface = unchecked((int)0x80004002);
    private const int ClassENoAggregation = unchecked((int)0x80040110);

    private static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IidClassFactory = new("00000001-0000-0000-C000-000000000046");
    private static readonly Guid IidInspectable = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
    private static readonly Guid IidWidget = new("5C57A4CC-72A0-452D-B9ED-075C0DD25EED");

    private static QueryDelegate? _query;
    private static CountDelegate? _add;
    private static CountDelegate? _release;
    private static CreateDelegate? _create;
    private static LockDelegate? _lock;
    private static UpNextWidgetProvider? _provider;

    public static void RegisterAndPump()
    {
        UnpackagedBootstrap.Try();
        _provider = new UpNextWidgetProvider();
        var factory = CreateFactory();
        var clsid = UpNextWidgetProvider.ClassId;
        CoRegisterClassObject(ref clsid, factory, ClsctxLocalServer, RegclsMultipleUse, out _);
        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private static nint CreateFactory()
    {
        _query = FactoryQuery;
        _add = static _ => 1;
        _release = static _ => 1;
        _create = FactoryCreate;
        _lock = static (_, _) => 0;
        var methods = new nint[]
        {
            Marshal.GetFunctionPointerForDelegate(_query),
            Marshal.GetFunctionPointerForDelegate(_add),
            Marshal.GetFunctionPointerForDelegate(_release),
            Marshal.GetFunctionPointerForDelegate(_create),
            Marshal.GetFunctionPointerForDelegate(_lock),
        };
        var vtable = Marshal.AllocHGlobal(nint.Size * methods.Length);
        for (var i = 0; i < methods.Length; i++)
            Marshal.WriteIntPtr(vtable, i * nint.Size, methods[i]);
        var obj = Marshal.AllocHGlobal(nint.Size);
        Marshal.WriteIntPtr(obj, vtable);
        return obj;
    }

    private static int FactoryQuery(nint self, ref Guid iid, out nint ppv)
    {
        if (iid == IidUnknown || iid == IidClassFactory)
        {
            ppv = self;
            return 0;
        }
        ppv = 0;
        return ENoInterface;
    }

    private static int FactoryCreate(nint self, nint outer, ref Guid iid, out nint ppv)
    {
        ppv = 0;
        if (outer != 0) return ClassENoAggregation;
        nint managed;
        try
        {
            // The pointer is the IWidgetProvider vtable, which begins with IUnknown and IInspectable.
            managed = MarshalInspectable<IWidgetProvider>.FromManaged(_provider ?? new UpNextWidgetProvider());
        }
        catch (Exception)
        {
            return ENoInterface;
        }
        if (iid == IidUnknown || iid == IidInspectable || iid == IidWidget || iid == UpNextWidgetProvider.ClassId)
        {
            ppv = managed;
            return 0;
        }
        var query = Marshal.GetDelegateForFunctionPointer<QueryDelegate>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(managed)));
        var result = query(managed, ref iid, out ppv);
        ReleaseCom(managed);
        return result;
    }

    private static void ReleaseCom(nint unknown)
    {
        var release = Marshal.GetDelegateForFunctionPointer<CountDelegate>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(unknown), nint.Size * 2));
        release(unknown);
    }

    private delegate int QueryDelegate(nint self, ref Guid iid, out nint ppv);
    private delegate uint CountDelegate(nint self);
    private delegate int CreateDelegate(nint self, nint outer, ref Guid iid, out nint ppv);
    private delegate int LockDelegate(nint self, int lockServer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(ref Guid classId, nint unknown, uint context, uint flags, out uint token);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessageW(out MSG message, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref MSG message);
}
