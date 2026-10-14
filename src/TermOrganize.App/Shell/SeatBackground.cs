// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.InteropServices;
using TermOrganize.Core;

namespace TermOrganize;

/// <summary>One seat check with no window. The timer, the scheduled task, and the COM task all call this.</summary>
internal static class SeatCheckHost
{
    public static void RunHeadless()
    {
        try
        {
            var store = new UserDataStore();
            if (!store.Preferences.BackgroundRefreshEnabled) return;
            RunAsync(store).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // The next launch still has the last counts. A failed check does not clear them.
        }
    }

    public static async Task<WatchlistComparison.Outcome> RunAsync(UserDataStore store, CancellationToken cancellationToken = default)
    {
        var outcome = await SeatCheckService.RunAsync(store, Fetch, cancellationToken).ConfigureAwait(false);
        if (!store.Preferences.NotifyOnSeats) return outcome;
        var missed = new List<WatchlistAlert>();
        foreach (var alert in outcome.Alerts)
        {
            if (!PlatformServices.ShowToast(alert.Title, alert.Body)) missed.Add(alert);
        }
        SeatAlertLog.Append(missed);
        return outcome;
    }

    private static async Task<IReadOnlyDictionary<CourseKey, IReadOnlyList<CourseSection>>> Fetch(
        string termCode,
        IReadOnlyList<CourseKey> keys,
        CancellationToken cancellationToken)
    {
        var banner = await BannerClient.Shared.FetchSectionsAsync(termCode, keys, cancellationToken).ConfigureAwait(false);
        var map = new Dictionary<CourseKey, IReadOnlyList<CourseSection>>();
        foreach (var pair in banner) map[pair.Key] = CatalogBuilder.MakeSections(pair.Value);
        return map;
    }
}

/// <summary>Framework-dependent unpackaged runs need the Windows App SDK bootstrap before toasts or WinUI.</summary>
internal static class UnpackagedBootstrap
{
    public static void Try()
    {
        // Self-contained builds already load Microsoft.WindowsAppRuntime.dll from the exe directory.
        // Bootstrap.Initialize looks up a framework package and throws
        // "Package dependency criteria could not be resolved" when that package is not installed.
#if MICROSOFT_WINDOWSAPPSDK_SELFCONTAINED
        ScreenshotSweep.Log("bootstrap skipped (self-contained)");
        return;
#else
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "Microsoft.WindowsAppRuntime.dll")))
        {
            ScreenshotSweep.Log("bootstrap skipped (self-contained runtime)");
            return;
        }
        try
        {
            Microsoft.Windows.ApplicationModel.DynamicDependency.Bootstrap.Initialize(0x00010008);
        }
        catch (Exception ex)
        {
            // A packaged app already has the runtime and throws here.
            ScreenshotSweep.Log("bootstrap: " + ScreenshotSweep.Describe(ex));
        }
#endif
    }
}

/// <summary>
/// Task Scheduler keeps checking seats after Exit. The Windows time trigger
/// will not fire more often than every 15 minutes, so the interval is at least that.
/// </summary>
internal static class SeatScheduler
{
    public const string TaskName = "Term Organize Seat Check";

    public static void Register(AppPreferences preferences)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType is null) return;
            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            dynamic folder = scheduler.GetFolder("\\");
            dynamic task = scheduler.NewTask(0);
            task.RegistrationInfo.Description = "Checks watched Northeastern sections for open seats.";
            task.Settings.Enabled = true;
            task.Settings.StartWhenAvailable = true;
            task.Settings.DisallowStartIfOnBatteries = false;
            task.Settings.StopIfGoingOnBatteries = false;
            task.Settings.ExecutionTimeLimit = "PT10M";
            task.Settings.MultipleInstances = 2;
            var minutes = Math.Max(15, preferences.SeatCheckMinutes);
            dynamic trigger = task.Triggers.Create(2);
            trigger.DaysInterval = 1;
            trigger.StartBoundary = DateTime.Now.ToString("s");
            trigger.Enabled = true;
            trigger.Repetition.Interval = $"PT{minutes}M";
            trigger.Repetition.Duration = "P1D";
            trigger.Repetition.StopAtDurationEnd = false;
            dynamic action = task.Actions.Create(0);
            action.Path = exe;
            action.Arguments = "--seat-check";
            folder.RegisterTaskDefinition(TaskName, task, 6, null, null, 3, null);
        }
        catch (Exception)
        {
            // Unpackaged registration can be denied. The in-app timer and the packaged COM task still run.
        }
    }

    public static void Remove()
    {
        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType is null) return;
            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            dynamic folder = scheduler.GetFolder("\\");
            folder.DeleteTask(TaskName, 0);
        }
        catch (Exception)
        {
            // Already gone, or this account cannot see the task folder.
        }
    }
}

/// <summary>
/// The packaged time trigger. backgroundtaskhost.exe launches this exe with
/// -RegisterForBGTaskServer and activates <see cref="SeatTaskClass"/>.
/// </summary>
internal static class SeatBackgroundRegistration
{
    public const string TaskName = "Term Organize Seat Check";

    public static async Task RegisterAsync(AppPreferences preferences)
    {
        try
        {
            var access = await Windows.ApplicationModel.Background.BackgroundExecutionManager.RequestAccessAsync();
            if (access is Windows.ApplicationModel.Background.BackgroundAccessStatus.Denied
                or Windows.ApplicationModel.Background.BackgroundAccessStatus.DeniedBySystemPolicy
                or Windows.ApplicationModel.Background.BackgroundAccessStatus.DeniedByUser)
                return;
            foreach (var pair in Windows.ApplicationModel.Background.BackgroundTaskRegistration.AllTasks)
            {
                if (pair.Value.Name == TaskName) pair.Value.Unregister(true);
            }
            var minutes = (uint)Math.Max(15, preferences.SeatCheckMinutes);
            var builder = new Microsoft.Windows.ApplicationModel.Background.BackgroundTaskBuilder
            {
                Name = TaskName,
            };
            Windows.ApplicationModel.Background.IBackgroundTrigger trigger =
                new Windows.ApplicationModel.Background.TimeTrigger(minutes, false);
            builder.SetTrigger(trigger);
            builder.AddCondition(new Windows.ApplicationModel.Background.SystemCondition(
                Windows.ApplicationModel.Background.SystemConditionType.InternetAvailable));
            builder.SetTaskEntryPointClsid(SeatTaskClass.Id);
            builder.Register();
        }
        catch (Exception)
        {
            // Unpackaged runs have no background-task identity. Task Scheduler covers those.
        }
    }

    public static void Remove()
    {
        try
        {
            foreach (var pair in Windows.ApplicationModel.Background.BackgroundTaskRegistration.AllTasks)
            {
                if (pair.Value.Name == TaskName) pair.Value.Unregister(true);
            }
        }
        catch (Exception)
        {
            // Nothing registered.
        }
    }
}

/// <summary>COM identity of the seat-check class. The manifest Class Id must match.</summary>
internal static class SeatTaskClass
{
    public static readonly Guid Id = new("A7C3E1B2-4D58-4F0A-9B16-2E8D5C7A91F4");
}

/// <summary>
/// Out-of-process COM server for the seat check. The vtable is IUnknown plus
/// IInspectable plus IBackgroundTask.Run, which is what backgroundtaskhost asks for.
/// </summary>
internal static class SeatBackgroundServer
{
    private const uint ClsctxLocalServer = 4;
    private const uint RegclsMultipleUse = 1;
    private const int ENoInterface = unchecked((int)0x80004002);
    private const int ClassENoAggregation = unchecked((int)0x80040110);
    private const int EFail = unchecked((int)0x80004005);

    private static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IidClassFactory = new("00000001-0000-0000-C000-000000000046");
    private static readonly Guid IidInspectable = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
    private static readonly Guid IidBackgroundTask = new("7D13D534-FD12-43CE-8C22-EA1FF13C06DF");

    private static QueryDelegate? _factoryQuery;
    private static CountDelegate? _factoryAdd;
    private static CountDelegate? _factoryRelease;
    private static CreateDelegate? _factoryCreate;
    private static LockDelegate? _factoryLock;
    private static QueryDelegate? _taskQuery;
    private static CountDelegate? _taskAdd;
    private static CountDelegate? _taskRelease;
    private static IidsDelegate? _taskIids;
    private static NameDelegate? _taskName;
    private static TrustDelegate? _taskTrust;
    private static RunDelegate? _taskRun;
    private static nint _factoryObject;
    private static nint _taskObject;

    public static void RegisterAndPump()
    {
        _factoryObject = CreateFactory();
        _taskObject = CreateTask();
        var clsid = SeatTaskClass.Id;
        CoRegisterClassObject(ref clsid, _factoryObject, ClsctxLocalServer, RegclsMultipleUse, out _);
        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private static nint CreateFactory()
    {
        _factoryQuery = FactoryQuery;
        _factoryAdd = static _ => 1;
        _factoryRelease = static _ => 1;
        _factoryCreate = FactoryCreate;
        _factoryLock = static (_, _) => 0;
        return Object(new nint[]
        {
            Marshal.GetFunctionPointerForDelegate(_factoryQuery),
            Marshal.GetFunctionPointerForDelegate(_factoryAdd),
            Marshal.GetFunctionPointerForDelegate(_factoryRelease),
            Marshal.GetFunctionPointerForDelegate(_factoryCreate),
            Marshal.GetFunctionPointerForDelegate(_factoryLock),
        });
    }

    private static nint CreateTask()
    {
        _taskQuery = TaskQuery;
        _taskAdd = static _ => 1;
        _taskRelease = static _ => 1;
        _taskIids = TaskIids;
        _taskName = static (nint _, out nint name) => { name = 0; return 0; };
        _taskTrust = static (nint _, out int level) => { level = 0; return 0; };
        _taskRun = TaskRun;
        return Object(new nint[]
        {
            Marshal.GetFunctionPointerForDelegate(_taskQuery),
            Marshal.GetFunctionPointerForDelegate(_taskAdd),
            Marshal.GetFunctionPointerForDelegate(_taskRelease),
            Marshal.GetFunctionPointerForDelegate(_taskIids),
            Marshal.GetFunctionPointerForDelegate(_taskName),
            Marshal.GetFunctionPointerForDelegate(_taskTrust),
            Marshal.GetFunctionPointerForDelegate(_taskRun),
        });
    }

    private static nint Object(nint[] methods)
    {
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
        if (outer != 0)
        {
            ppv = 0;
            return ClassENoAggregation;
        }
        if (iid == IidUnknown || iid == IidInspectable || iid == IidBackgroundTask || iid == SeatTaskClass.Id)
        {
            ppv = _taskObject;
            return 0;
        }
        ppv = 0;
        return ENoInterface;
    }

    private static int TaskQuery(nint self, ref Guid iid, out nint ppv)
    {
        if (iid == IidUnknown || iid == IidInspectable || iid == IidBackgroundTask || iid == SeatTaskClass.Id)
        {
            ppv = self;
            return 0;
        }
        ppv = 0;
        return ENoInterface;
    }

    private static int TaskIids(nint self, out uint count, out nint iids)
    {
        count = 1;
        iids = Marshal.AllocCoTaskMem(16);
        Marshal.StructureToPtr(IidBackgroundTask, iids, false);
        return 0;
    }

    private static int TaskRun(nint self, nint instance)
    {
        try
        {
            SeatCheckHost.RunHeadless();
            return 0;
        }
        catch (Exception)
        {
            return EFail;
        }
    }

    private delegate int QueryDelegate(nint self, ref Guid iid, out nint ppv);
    private delegate uint CountDelegate(nint self);
    private delegate int CreateDelegate(nint self, nint outer, ref Guid iid, out nint ppv);
    private delegate int LockDelegate(nint self, int lockServer);
    private delegate int IidsDelegate(nint self, out uint count, out nint iids);
    private delegate int NameDelegate(nint self, out nint className);
    private delegate int TrustDelegate(nint self, out int trustLevel);
    private delegate int RunDelegate(nint self, nint taskInstance);

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
