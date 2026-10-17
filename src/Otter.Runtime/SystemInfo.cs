using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;
using Otter.Language;

namespace Otter.Runtime;

public sealed class SystemInfoProblem(string message, string? suggestion = null) : Exception(message)
{
    public string? Suggestion { get; } = suggestion;
}

// get system information "os" / "cpu" / "memory" / "disk" / "network" /
// "user" / "groups" / "software" into info (D69, D76, D77): a port of
// otterPS's Get-OtterSystemInfoValue.
//
// Windows PowerShell reads "os", "cpu" and "memory" through CIM (WMI). Native
// AOT has no WMI client, so the same values come from where WMI itself takes
// them: the registry's CurrentVersion and CentralProcessor keys and
// GlobalMemoryStatusEx. Off Windows otterPS runs on PowerShell 7, where CIM
// does not exist, and its fallbacks (the .NET OS version, gone for the CPU
// name and memory) are what it shows; otter-net shows the same.
//
// "tasks" (the Task Scheduler's COM interface) and "printers" (WMI's
// Win32_Printer, whose status codes WMI works out itself) are left to the
// PowerShell Otter on Windows (the parser decides). Off Windows PowerShell 7
// has none of Get-ScheduledTask, Get-CimInstance or Get-Service, so tasks,
// printers and services are empty lists there.
public static partial class OtterSystemInfo
{
    public static object? Get(string kind)
    {
        switch (kind.ToLowerInvariant())
        {
            case "os": return OperatingSystemInfo();
            case "cpu":
            {
                var info = new OtterThing("cpu");
                info.WriteProperty("name", OperatingSystem.IsWindows() ? ProcessorName() : null);
                info.WriteProperty("cores", (double)Environment.ProcessorCount);
                return info;
            }
            case "memory":
            {
                var info = new OtterThing("memory");
                object? total = null, free = null;
                if (OperatingSystem.IsWindows() && Memory() is { } memory)
                {
                    // Win32_OperatingSystem counts whole kilobytes.
                    total = Math.Floor(memory.TotalPhys / 1024.0) * 1024.0;
                    free = Math.Floor(memory.AvailPhys / 1024.0) * 1024.0;
                }
                info.WriteProperty("totalBytes", total);
                info.WriteProperty("freeBytes", free);
                return info;
            }
            case "disk": return Disk();
            case "network": return Network();
            case "user": return User();
            case "groups": return Groups();
            case "software": return Software();
            case "services": return OperatingSystem.IsWindows() ? OtterServices.List() : new OtterList();
            case "tasks":
            case "printers":
                return new OtterList();
            default:
                throw new SystemInfoProblem($"I do not know a kind of system information called \"{kind}\".",
                    "get system information \"os\" into info (also: \"cpu\", \"memory\", \"disk\", \"network\", \"user\", \"groups\", \"software\", \"tasks\", \"printers\", \"services\")");
        }
    }

    static OtterThing OperatingSystemInfo()
    {
        var info = new OtterThing("operating system");
        string? caption = null, version = null;
        if (OperatingSystem.IsWindows() && WindowsCaption() is { } windowsCaption)
        {
            caption = windowsCaption;
            Version osVersion = Environment.OSVersion.Version;
            version = $"{osVersion.Major}.{osVersion.Minor}.{osVersion.Build}";
        }
        else
        {
            caption = Environment.OSVersion.VersionString;
            version = Environment.OSVersion.Version.ToString();
        }
        info.WriteProperty("name", caption);
        info.WriteProperty("version", version);
        info.WriteProperty("architecture", Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
        info.WriteProperty("machineName", Environment.MachineName);
        return info;
    }

    // Win32_OperatingSystem.Caption: "Microsoft " and the product name, which
    // the registry still spells "Windows 10" on Windows 11 (build 22000 on).
    [SupportedOSPlatform("windows")]
    static string? WindowsCaption()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("ProductName") is not string product || product.Length == 0) return null;
            if (Environment.OSVersion.Version.Build >= 22000 && product.StartsWith("Windows 10", StringComparison.Ordinal))
            {
                product = "Windows 11" + product.Substring("Windows 10".Length);
            }
            return "Microsoft " + product;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    // Win32_Processor.Name is the processor's own name string.
    [SupportedOSPlatform("windows")]
    static string? ProcessorName()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim();
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalMemoryStatusEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [SupportedOSPlatform("windows")]
    static MemoryStatusEx? Memory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status : null;
    }

    // The drive of the current location (PowerShell's Get-Location).Drive:
    // "C" on Windows, "/" elsewhere; none for a network share.
    static OtterThing Disk()
    {
        var info = new OtterThing("disk");
        object? total = null, free = null;
        string? driveName = null;
        string? drive = null;
        if (OperatingSystem.IsWindows())
        {
            string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "";
            if (root.Length >= 2 && root[1] == ':' && char.IsAsciiLetter(root[0])) drive = root.Substring(0, 1);
        }
        else
        {
            drive = "/";
        }
        if (drive != null)
        {
            try
            {
                var driveInfo = new DriveInfo(drive);
                total = (double)driveInfo.TotalSize;
                free = (double)driveInfo.AvailableFreeSpace;
                driveName = drive;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                total = null;
                free = null;
            }
        }
        info.WriteProperty("drive", driveName);
        info.WriteProperty("totalBytes", total);
        info.WriteProperty("freeBytes", free);
        return info;
    }

    // One "network interface" for each interface that is up and not loopback,
    // with its first IPv4 address.
    static OtterList Network()
    {
        var list = new OtterList();
        try
        {
            foreach (NetworkInterface face in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (face.OperationalStatus != OperationalStatus.Up || face.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var entry = new OtterThing("network interface");
                entry.WriteProperty("name", face.Name);
                string? address = null;
                foreach (UnicastIPAddressInformation unicast in face.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        address = unicast.Address.ToString();
                        break;
                    }
                }
                entry.WriteProperty("address", address);
                list.Add(entry);
            }
        }
        catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException or InvalidOperationException)
        {
            // an empty list rather than a crash on a locked-down host
        }
        return list;
    }

    // D76: the signed-in user. WindowsIdentity exists only on Windows; on
    // PowerShell 7 elsewhere the call fails and otterPS reports that failure.
    static OtterThing User()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InternalProblem("Exception calling \"GetCurrent\" with \"0\" argument(s): \"Windows Principal functionality is not supported on this platform.\"");
        }
        var info = new OtterThing("user");
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string[] nameParts = identity.Name.Split('\\', 2);
        info.WriteProperty("name", nameParts.Length == 2 ? nameParts[1] : nameParts[0]);
        info.WriteProperty("domain", nameParts.Length == 2 ? nameParts[0] : null);
        bool isAdmin;
        try { isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
        catch (Exception e) when (e is SystemException) { isAdmin = false; }
        info.WriteProperty("isAdmin", isAdmin);
        return info;
    }

    // D76: the names of the user's groups; a group whose name cannot be found
    // any more is left out.
    static OtterList Groups()
    {
        var list = new OtterList();
        if (!OperatingSystem.IsWindows()) return list;
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity.Groups is { } groups)
            {
                foreach (IdentityReference group in groups)
                {
                    try { list.Add(group.Translate(typeof(NTAccount)).Value); }
                    catch (Exception e) when (e is IdentityNotMappedException or SystemException) { }
                }
            }
        }
        catch (Exception e) when (e is SystemException)
        {
            // an empty list rather than a crash on a locked-down host
        }
        return list;
    }

    // D77: the installed applications, from the same Uninstall keys
    // "Apps & features" reads. Entries without a display name are left out.
    static OtterList Software()
    {
        var list = new OtterList();
        if (!OperatingSystem.IsWindows()) return list;
        var sources = new (RegistryKey Hive, string Path)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (hive, path) in sources)
        {
            try
            {
                using RegistryKey? uninstall = hive.OpenSubKey(path);
                if (uninstall == null) continue;
                foreach (string name in uninstall.GetSubKeyNames())
                {
                    using RegistryKey? entry = uninstall.OpenSubKey(name);
                    if (entry == null) continue;
                    object? displayName = OtterRegistry.ToOtter(entry.GetValue("DisplayName"));
                    if (displayName == null || string.IsNullOrWhiteSpace(Values.Format(displayName))) continue;
                    var software = new OtterThing("software");
                    software.WriteProperty("name", displayName);
                    software.WriteProperty("version", TruthyOrGone(OtterRegistry.ToOtter(entry.GetValue("DisplayVersion"))));
                    software.WriteProperty("publisher", TruthyOrGone(OtterRegistry.ToOtter(entry.GetValue("Publisher"))));
                    list.Add(software);
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // this hive or path is unavailable here - move on to the next
            }
        }
        return list;
    }

    static object? TruthyOrGone(object? value) => Values.IsTruthy(value) ? value : null;
}
