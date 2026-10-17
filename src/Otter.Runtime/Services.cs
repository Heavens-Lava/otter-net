using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Otter.Runtime;

// get system information "services" into list (D86): Get-Service, read
// through the service control manager as ServiceController reads it - the
// Win32 services in every state, ordered by name, each with its display
// name, status and start type (gone when the service will not say).
[SupportedOSPlatform("windows")]
public static unsafe partial class OtterServices
{
    const uint ScManagerConnect = 0x0001;
    const uint ScManagerEnumerateService = 0x0004;
    const uint ServiceQueryConfig = 0x0001;
    const int ScEnumProcessInfo = 0;
    const uint ServiceWin32 = 0x30;
    const uint ServiceStateAll = 3;
    const int ErrorMoreData = 234;
    const int ErrorInsufficientBuffer = 122;

    [StructLayout(LayoutKind.Sequential)]
    struct EnumServiceStatusProcess
    {
        public IntPtr ServiceName;
        public IntPtr DisplayName;
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct QueryServiceConfig
    {
        public uint ServiceType;
        public uint StartType;
        public uint ErrorControl;
        public IntPtr BinaryPathName;
        public IntPtr LoadOrderGroup;
        public uint TagId;
        public IntPtr Dependencies;
        public IntPtr ServiceStartName;
        public IntPtr DisplayName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenService(IntPtr manager, string serviceName, uint access);

    [LibraryImport("advapi32.dll", EntryPoint = "CloseServiceHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(IntPtr handle);

    [LibraryImport("advapi32.dll", EntryPoint = "EnumServicesStatusExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumServicesStatusEx(IntPtr manager, int infoLevel, uint serviceType, uint serviceState,
        IntPtr services, uint bufferSize, out uint bytesNeeded, out uint servicesReturned, ref uint resumeHandle, IntPtr groupName);

    [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceConfigNative(IntPtr service, IntPtr config, uint bufferSize, out uint bytesNeeded);

    // ServiceControllerStatus, by its value.
    static readonly string[] StatusNames = { "", "Stopped", "StartPending", "StopPending", "Running", "ContinuePending", "PausePending", "Paused" };

    // ServiceStartMode, by its value.
    static readonly string[] StartModeNames = { "Boot", "System", "Automatic", "Manual", "Disabled" };

    public static OtterList List()
    {
        var list = new OtterList();
        IntPtr manager = OpenSCManager(null, null, ScManagerConnect | ScManagerEnumerateService);
        if (manager == IntPtr.Zero) return list;
        try
        {
            var services = new List<(string Name, string DisplayName, uint State)>();
            uint resume = 0;
            uint size = 0;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                while (true)
                {
                    bool done = EnumServicesStatusEx(manager, ScEnumProcessInfo, ServiceWin32, ServiceStateAll, buffer, size, out uint needed, out uint returned, ref resume, IntPtr.Zero);
                    int error = done ? 0 : Marshal.GetLastPInvokeError();
                    if (!done && error != ErrorMoreData) return list;
                    var entries = (EnumServiceStatusProcess*)buffer;
                    for (int i = 0; i < returned; i++)
                    {
                        string name = Marshal.PtrToStringUni(entries[i].ServiceName) ?? "";
                        string display = Marshal.PtrToStringUni(entries[i].DisplayName) ?? "";
                        services.Add((name, display, entries[i].CurrentState));
                    }
                    if (done) break;
                    if (needed > size)
                    {
                        if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                        size = needed;
                        buffer = Marshal.AllocHGlobal((int)size);
                    }
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
            foreach (var service in services.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                var entry = new OtterThing("service");
                entry.WriteProperty("name", service.Name);
                entry.WriteProperty("displayName", service.DisplayName);
                entry.WriteProperty("status", service.State < StatusNames.Length ? StatusNames[service.State] : service.State.ToString(System.Globalization.CultureInfo.InvariantCulture));
                entry.WriteProperty("startType", StartType(manager, service.Name));
                list.Add(entry);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
        return list;
    }

    static string? StartType(IntPtr manager, string name)
    {
        IntPtr service = OpenService(manager, name, ServiceQueryConfig);
        if (service == IntPtr.Zero) return null;
        try
        {
            QueryServiceConfigNative(service, IntPtr.Zero, 0, out uint needed);
            if (Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer || needed == 0) return null;
            IntPtr buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!QueryServiceConfigNative(service, buffer, needed, out _)) return null;
                uint startType = ((QueryServiceConfig*)buffer)->StartType;
                return startType < StartModeNames.Length ? StartModeNames[startType] : startType.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseServiceHandle(service);
        }
    }
}
