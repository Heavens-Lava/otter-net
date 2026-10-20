using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Otter.Runtime;

// get system information "tasks" into list (D80): otterPS's Get-ScheduledTask
// and Get-ScheduledTaskInfo, read from the Task Scheduler 2.0 COM API that the
// ScheduledTasks module's CIM provider itself reads.
//
// Every task in every folder, hidden ones included (Get-ScheduledTask lists
// them), a folder's own tasks before its subfolders', from the root down,
// each folder's tasks and subfolders in the Task Scheduler's order.
// Each is a "scheduled task" thing: name, state (MSFT_ScheduledTask's
// StateEnum, whose names are TASK_STATE's), and lastRunTime/nextRunTime as
// "yyyy-MM-dd HH:mm:ss" text, or gone when the Task Scheduler has no time
// (its zero DATE, which the CIM provider reports as an empty datetime) or
// will not answer for that task. A task that never ran has the Task
// Scheduler's own "never" time, 1999-11-30 00:00:00, as lastRunTime, in the
// CIM provider too. If the Task Scheduler cannot be reached at all the list
// is empty, as otterPS's catch makes it. All of this was compared with
// Windows PowerShell 5.1 on a Windows runner with tasks in every state
// (tests/windows-state/w5_tasks.ot, SPEC-QUESTIONS T3-2).
//
// No COM interop code is generated: the few calls needed go straight
// through each interface's vtable (taskschd.h's order; slots 0-6 are
// IUnknown and IDispatch), which Native AOT compiles as ordinary calls.
[SupportedOSPlatform("windows")]
public static unsafe partial class OtterScheduledTasks
{
    static readonly Guid ClsidTaskScheduler = new("0f87369f-a4e5-4cfc-bd3e-73e6154572dd");
    static readonly Guid IidTaskService = new("2faba4c7-4da9-4013-9697-20cc3fd40f85");

    const uint ClsctxInprocServer = 0x1;
    const uint CoinitMultithreaded = 0x0;
    const int TaskEnumHidden = 1;

    // MSFT_ScheduledTask.State (StateEnum) = TASK_STATE, by value.
    static readonly string[] StateNames = { "Unknown", "Disabled", "Queued", "Ready", "Running" };

    [StructLayout(LayoutKind.Sequential)]
    struct Variant
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public IntPtr Data1;
        public IntPtr Data2;

        public static Variant Empty => default;
        public static Variant Int32(int value) => new() { Type = 3, Data1 = value };
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(IntPtr reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr instance);

    [LibraryImport("oleaut32.dll")]
    private static partial IntPtr SysAllocString([MarshalAs(UnmanagedType.LPWStr)] string text);

    [LibraryImport("oleaut32.dll")]
    private static partial void SysFreeString(IntPtr text);

    static void** Table(IntPtr instance) => *(void***)instance;

    static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Table(instance)[2])(instance);
    }

    static string? TakeString(IntPtr bstr)
    {
        if (bstr == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringBSTR(bstr); }
        finally { SysFreeString(bstr); }
    }

    public static OtterList List()
    {
        var list = new OtterList();
        int init = CoInitializeEx(IntPtr.Zero, CoinitMultithreaded);
        // S_OK, S_FALSE (already initialised) and RPC_E_CHANGED_MODE (this
        // thread is already a single-threaded apartment) all leave COM usable.
        if (init < 0 && init != unchecked((int)0x80010106)) return list;
        IntPtr service = IntPtr.Zero, root = IntPtr.Zero;
        try
        {
            if (CoCreateInstance(ClsidTaskScheduler, IntPtr.Zero, ClsctxInprocServer, IidTaskService, out service) < 0) return list;
            // ITaskService::Connect(VARIANT serverName, user, domain, password)
            var connect = (delegate* unmanaged[Stdcall]<IntPtr, Variant, Variant, Variant, Variant, int>)Table(service)[10];
            if (connect(service, Variant.Empty, Variant.Empty, Variant.Empty, Variant.Empty) < 0) return list;
            // ITaskService::GetFolder(BSTR path, ITaskFolder**)
            IntPtr rootPath = SysAllocString("\\");
            try
            {
                var getFolder = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Table(service)[7];
                if (getFolder(service, rootPath, &root) < 0) return list;
            }
            finally { SysFreeString(rootPath); }
            AddFolder(root, list);
        }
        finally
        {
            Release(root);
            Release(service);
        }
        return list;
    }

    static void AddFolder(IntPtr folder, OtterList list)
    {
        // ITaskFolder::GetTasks(LONG flags, IRegisteredTaskCollection**)
        IntPtr tasks = IntPtr.Zero;
        var getTasks = (delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Table(folder)[14];
        if (getTasks(folder, TaskEnumHidden, &tasks) >= 0)
        {
            try
            {
                foreach (IntPtr task in Items(tasks))
                {
                    try { list.Add(Task(task)); }
                    finally { Release(task); }
                }
            }
            finally { Release(tasks); }
        }
        // ITaskFolder::GetFolders(LONG flags, ITaskFolderCollection**)
        IntPtr folders = IntPtr.Zero;
        var getFolders = (delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Table(folder)[10];
        if (getFolders(folder, 0, &folders) < 0) return;
        try
        {
            foreach (IntPtr subfolder in Items(folders))
            {
                try { AddFolder(subfolder, list); }
                finally { Release(subfolder); }
            }
        }
        finally { Release(folders); }
    }

    // A collection's items (get_Count, get_Item(VARIANT index) from 1), each
    // to be released by the caller.
    static List<IntPtr> Items(IntPtr collection)
    {
        var items = new List<IntPtr>();
        int count = 0;
        var getCount = (delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Table(collection)[7];
        if (getCount(collection, &count) < 0) return items;
        var getItem = (delegate* unmanaged[Stdcall]<IntPtr, Variant, IntPtr*, int>)Table(collection)[8];
        for (int index = 1; index <= count; index++)
        {
            IntPtr item = IntPtr.Zero;
            if (getItem(collection, Variant.Int32(index), &item) >= 0 && item != IntPtr.Zero) items.Add(item);
        }
        return items;
    }

    static OtterThing Task(IntPtr task)
    {
        void** table = Table(task);
        IntPtr name = IntPtr.Zero;
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)table[7])(task, &name);
        int state = 0;
        if (((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)table[9])(task, &state) < 0) state = 0;
        var entry = new OtterThing("scheduled task");
        entry.WriteProperty("name", TakeString(name));
        entry.WriteProperty("state", state >= 0 && state < StateNames.Length ? StateNames[state] : state.ToString(CultureInfo.InvariantCulture));
        // Get-ScheduledTaskInfo answers both times or neither.
        double lastRun = 0, nextRun = 0;
        bool known = ((delegate* unmanaged[Stdcall]<IntPtr, double*, int>)table[15])(task, &lastRun) >= 0 &&
                     ((delegate* unmanaged[Stdcall]<IntPtr, double*, int>)table[18])(task, &nextRun) >= 0;
        entry.WriteProperty("lastRunTime", known ? Time(lastRun) : null);
        entry.WriteProperty("nextRunTime", known ? Time(nextRun) : null);
        return entry;
    }

    // An OLE automation DATE in local time; zero is "no time".
    static string? Time(double date)
    {
        if (date == 0) return null;
        try { return DateTime.FromOADate(date).ToString("yyyy-MM-dd HH:mm:ss", Otter.Language.UserFormats.Culture); }
        catch (ArgumentException) { return null; }
    }
}
