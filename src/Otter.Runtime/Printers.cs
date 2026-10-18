using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Otter.Runtime;

// get system information "printers" into list (D83): otterPS's
// Get-CimInstance Win32_Printer, read from the print spooler that WMI's
// Win32_Printer provider itself reads (EnumPrinters, level 2, the local
// printers and the connections, in the spooler's order).
//
// Each is a "printer" thing: its name, its status and whether it is the
// default printer (GetDefaultPrinter, as Win32_Printer.Default reports it).
// The status is Win32_Printer.PrinterStatus put into words by otterPS's
// table (1 other, 3 idle, 4 printing, 5 warming up, 6 stopped printing,
// 7 offline, anything else unknown). WMI derives PrinterStatus from the
// spooler's PRINTER_INFO_2.Status: no flag is idle; a flag that is one of
// CIM's PrinterStatus values is that value (offline 7, printing 4, warming
// up 5); any other flag is an ExtendedPrinterStatus beyond PrinterStatus's
// range (paused, error, busy, ...), which PrinterStatus reports as other (1).
// See docs/SPEC-QUESTIONS.md T3-1 for the order the flags are tried in.
//
// If the spooler cannot be asked (it is stopped), the list is empty, as
// otterPS's catch makes it.
[SupportedOSPlatform("windows")]
public static unsafe partial class OtterPrinters
{
    const uint PrinterEnumLocal = 0x2;
    const uint PrinterEnumConnections = 0x4;
    const int ErrorInsufficientBuffer = 122;

    const uint StatusOffline = 0x80;
    const uint StatusPrinting = 0x400;
    const uint StatusWarmingUp = 0x10000;

    [StructLayout(LayoutKind.Sequential)]
    struct PrinterInfo2
    {
        public IntPtr ServerName;
        public IntPtr PrinterName;
        public IntPtr ShareName;
        public IntPtr PortName;
        public IntPtr DriverName;
        public IntPtr Comment;
        public IntPtr Location;
        public IntPtr DevMode;
        public IntPtr SepFile;
        public IntPtr PrintProcessor;
        public IntPtr Datatype;
        public IntPtr Parameters;
        public IntPtr SecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint Jobs;
        public uint AveragePpm;
    }

    [LibraryImport("winspool.drv", EntryPoint = "EnumPrintersW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumPrinters(uint flags, IntPtr name, uint level, IntPtr printers, uint bufferSize, out uint bytesNeeded, out uint returned);

    [LibraryImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDefaultPrinter(char* buffer, ref uint size);

    // Win32_Printer.PrinterStatus for a spooler status.
    public static int PrinterStatus(uint status)
    {
        if (status == 0) return 3;
        if ((status & StatusOffline) != 0) return 7;
        if ((status & ~(StatusPrinting | StatusWarmingUp)) != 0) return 1;
        if ((status & StatusPrinting) != 0) return 4;
        return 5;
    }

    public static string StatusText(int code) => code switch
    {
        1 => "other",
        2 => "unknown",
        3 => "idle",
        4 => "printing",
        5 => "warming up",
        6 => "stopped printing",
        7 => "offline",
        _ => "unknown",
    };

    public static OtterList List()
    {
        var list = new OtterList();
        const uint flags = PrinterEnumLocal | PrinterEnumConnections;
        EnumPrinters(flags, IntPtr.Zero, 2, IntPtr.Zero, 0, out uint needed, out _);
        if (needed == 0)
        {
            // No printers, or no spooler: an empty list either way.
            return list;
        }
        string? defaultPrinter = DefaultPrinter();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!EnumPrinters(flags, IntPtr.Zero, 2, buffer, needed, out uint more, out uint returned))
                {
                    if (Marshal.GetLastPInvokeError() == ErrorInsufficientBuffer && more > needed) { needed = more; continue; }
                    return list;
                }
                var printers = (PrinterInfo2*)buffer;
                for (int index = 0; index < returned; index++)
                {
                    string? name = Marshal.PtrToStringUni(printers[index].PrinterName);
                    var entry = new OtterThing("printer");
                    entry.WriteProperty("name", name);
                    entry.WriteProperty("status", StatusText(PrinterStatus(printers[index].Status)));
                    entry.WriteProperty("isDefault", name != null && defaultPrinter != null && string.Equals(name, defaultPrinter, StringComparison.OrdinalIgnoreCase));
                    list.Add(entry);
                }
                return list;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return list;
    }

    static string? DefaultPrinter()
    {
        uint size = 0;
        GetDefaultPrinter(null, ref size);
        if (size == 0) return null;
        char[] buffer = new char[size];
        fixed (char* text = buffer)
        {
            if (!GetDefaultPrinter(text, ref size)) return null;
            return new string(text);
        }
    }
}
