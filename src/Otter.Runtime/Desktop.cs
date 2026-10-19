using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Otter.Runtime;

public sealed class PrintProblem(string message, string? suggestion = null) : Exception(message)
{
    public string? Suggestion { get; } = suggestion;
}

// The Windows desktop features of D67 and D83, which otterPS reaches through
// Windows Forms and System.Drawing: notifications (NotifyIcon), file and
// folder dialogs (OpenFileDialog, SaveFileDialog, FolderBrowserDialog) and
// printing a text file (PrintDocument). Native AOT has neither library, so
// the same Windows calls they make are made here directly.
[SupportedOSPlatform("windows")]
public static unsafe partial class OtterDesktop
{
    // ---- notify "Title" with "Message" ------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint TimeoutOrVersion;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    const uint NimAdd = 0, NimModify = 1, NimDelete = 2;
    const uint NifIcon = 0x2, NifInfo = 0x10;
    const int IdiInformation = 32516;

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr window);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    private static partial IntPtr LoadIcon(IntPtr instance, IntPtr name);

    static void CopyInto(char* target, int capacity, string text)
    {
        int length = Math.Min(text.Length, capacity - 1);
        for (int i = 0; i < length; i++) target[i] = text[i];
        target[length] = '\0';
    }

    // Show-OtterNotification: an information icon in the notification area
    // with a balloon (a toast on Windows 10 and later), shown for 200 ms and
    // then taken away. A desktop that cannot show one shows nothing.
    public static void Notify(string title, string message)
    {
        IntPtr window = CreateWindowEx(0, "STATIC", "Otter", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero) return;
        try
        {
            var data = new NotifyIconData
            {
                Size = (uint)sizeof(NotifyIconData),
                Window = window,
                Id = 1,
                Flags = NifIcon,
                Icon = LoadIcon(IntPtr.Zero, (IntPtr)IdiInformation),
            };
            if (!ShellNotifyIcon(NimAdd, ref data)) return;
            data.Flags = NifInfo;
            data.TimeoutOrVersion = 5000;
            CopyInto(data.Info, 256, message);
            CopyInto(data.InfoTitle, 64, title);
            ShellNotifyIcon(NimModify, ref data);
            Thread.Sleep(200);
            ShellNotifyIcon(NimDelete, ref data);
        }
        finally
        {
            DestroyWindow(window);
        }
    }

    // ---- choose file / choose folder / choose file to save ------------------------

    [StructLayout(LayoutKind.Sequential)]
    struct OpenFileName
    {
        public uint StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        public IntPtr Filter;
        public IntPtr CustomFilter;
        public uint MaxCustomFilter;
        public uint FilterIndex;
        public IntPtr File;
        public uint MaxFile;
        public IntPtr FileTitle;
        public uint MaxFileTitle;
        public IntPtr InitialDirectory;
        public IntPtr Title;
        public uint Flags;
        public ushort FileOffset;
        public ushort FileExtension;
        public IntPtr DefaultExtension;
        public IntPtr CustomData;
        public IntPtr Hook;
        public IntPtr TemplateName;
        public IntPtr Reserved;
        public uint ReservedNumber;
        public uint FlagsEx;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BrowseInfo
    {
        public IntPtr Owner;
        public IntPtr Root;
        public IntPtr DisplayName;
        public IntPtr Title;
        public uint Flags;
        public IntPtr Callback;
        public IntPtr Parameter;
        public int Image;
    }

    const uint OfnOverwritePrompt = 0x2, OfnHideReadOnly = 0x4, OfnNoChangeDir = 0x8, OfnPathMustExist = 0x800,
        OfnFileMustExist = 0x1000, OfnExplorer = 0x80000, OfnEnableSizing = 0x800000;
    const uint BifReturnOnlyFileSystemDirectories = 0x1, BifNewDialogStyle = 0x40;
    const int MaxPathCharacters = 32768;

    [LibraryImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileName(ref OpenFileName details);

    [LibraryImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSaveFileName(ref OpenFileName details);

    [LibraryImport("shell32.dll", EntryPoint = "SHBrowseForFolderW")]
    private static partial IntPtr ShBrowseForFolder(ref BrowseInfo details);

    [LibraryImport("shell32.dll", EntryPoint = "SHGetPathFromIDListW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShGetPathFromIdList(IntPtr idList, char* path);

    // Show-OtterFileDialog: the dialog runs on its own single-threaded
    // apartment thread, and the program waits until the person closes it.
    // gone when they cancel.
    public static string? ChooseFile(bool folder, bool save)
    {
        string? chosen = null;
        var thread = new Thread(() =>
        {
            try { chosen = folder ? BrowseForFolder() : FileDialog(save); }
            catch (Exception e) when (e is Win32Exception or ExternalException or InvalidOperationException) { chosen = null; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return chosen;
    }

    static string? FileDialog(bool save)
    {
        IntPtr buffer = Marshal.AllocHGlobal(MaxPathCharacters * 2);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var details = new OpenFileName
            {
                StructSize = (uint)sizeof(OpenFileName),
                File = buffer,
                MaxFile = MaxPathCharacters,
                Flags = OfnExplorer | OfnEnableSizing | OfnHideReadOnly | OfnNoChangeDir | OfnPathMustExist |
                        (save ? OfnOverwritePrompt : OfnFileMustExist),
            };
            bool chosen = save ? GetSaveFileName(ref details) : GetOpenFileName(ref details);
            return chosen ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    static string? BrowseForFolder()
    {
        char* displayName = stackalloc char[260];
        var details = new BrowseInfo
        {
            DisplayName = (IntPtr)displayName,
            Flags = BifReturnOnlyFileSystemDirectories | BifNewDialogStyle,
        };
        IntPtr idList = ShBrowseForFolder(ref details);
        if (idList == IntPtr.Zero) return null;
        try
        {
            char* path = stackalloc char[MaxPathCharacters];
            return ShGetPathFromIdList(idList, path) ? new string(path) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(idList);
        }
    }

    // ---- print "file.txt" to "PrinterName" -------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    struct PrinterInfo4
    {
        public IntPtr PrinterName;
        public IntPtr ServerName;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DocInfo
    {
        public int Size;
        public IntPtr DocumentName;
        public IntPtr Output;
        public IntPtr DataType;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TextMetric
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AverageCharacterWidth, MaximumCharacterWidth,
            Weight, Overhang, DigitizedAspectX, DigitizedAspectY;
        public ushort FirstChar, LastChar, DefaultChar, BreakChar;
        public byte Italic, Underlined, StruckOut, PitchAndFamily, CharacterSet;
    }

    const uint PrinterEnumLocal = 0x2, PrinterEnumConnections = 0x4;
    const int LogPixelsX = 88, LogPixelsY = 90, PhysicalHeight = 111, PhysicalOffsetY = 113, PhysicalOffsetX = 112;

    [LibraryImport("winspool.drv", EntryPoint = "EnumPrintersW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumPrinters(uint flags, IntPtr name, uint level, IntPtr buffer, uint size, out uint needed, out uint returned);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDCW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateDC(string driver, string device, IntPtr output, IntPtr initData);

    [LibraryImport("gdi32.dll", EntryPoint = "DeleteDC")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr context);

    [LibraryImport("gdi32.dll", EntryPoint = "StartDocW", SetLastError = true)]
    private static partial int StartDoc(IntPtr context, ref DocInfo document);

    [LibraryImport("gdi32.dll", EntryPoint = "EndDoc")]
    private static partial int EndDoc(IntPtr context);

    [LibraryImport("gdi32.dll", EntryPoint = "AbortDoc")]
    private static partial int AbortDoc(IntPtr context);

    [LibraryImport("gdi32.dll", EntryPoint = "StartPage", SetLastError = true)]
    private static partial int StartPage(IntPtr context);

    [LibraryImport("gdi32.dll", EntryPoint = "EndPage", SetLastError = true)]
    private static partial int EndPage(IntPtr context);

    [LibraryImport("gdi32.dll", EntryPoint = "GetDeviceCaps")]
    private static partial int GetDeviceCaps(IntPtr context, int index);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline,
        uint strikeOut, uint characterSet, uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);

    [LibraryImport("gdi32.dll", EntryPoint = "SelectObject")]
    private static partial IntPtr SelectObject(IntPtr context, IntPtr gdiObject);

    [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr gdiObject);

    [LibraryImport("gdi32.dll", EntryPoint = "GetTextMetricsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTextMetrics(IntPtr context, out TextMetric metric);

    [LibraryImport("gdi32.dll", EntryPoint = "TextOutW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TextOut(IntPtr context, int x, int y, string text, int length);

    // PrinterSettings.InstalledPrinters: local printers and connections.
    static List<string> InstalledPrinters()
    {
        var names = new List<string>();
        uint flags = PrinterEnumLocal | PrinterEnumConnections;
        EnumPrinters(flags, IntPtr.Zero, 4, IntPtr.Zero, 0, out uint needed, out _);
        if (needed == 0) return names;
        IntPtr buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumPrinters(flags, IntPtr.Zero, 4, buffer, needed, out _, out uint returned)) return names;
            var printers = (PrinterInfo4*)buffer;
            for (int i = 0; i < returned; i++)
            {
                if (Marshal.PtrToStringUni(printers[i].PrinterName) is { } name) names.Add(name);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return names;
    }

    // Send-OtterFileToPrinter: the file's text in 10-point Consolas, one-inch
    // margins, as many lines on each page as fit between them.
    public static void Print(string path, string content, string printerName)
    {
        if (!InstalledPrinters().Contains(printerName, StringComparer.OrdinalIgnoreCase))
        {
            throw new PrintProblem($"I could not find a printer called \"{printerName}\".",
                "Check the exact printer name with get system information \"printers\" into list.");
        }
        IntPtr context = CreateDC("WINSPOOL", printerName, IntPtr.Zero, IntPtr.Zero);
        if (context == IntPtr.Zero) throw PrintFailed(path, printerName, new Win32Exception(Marshal.GetLastPInvokeError()));
        IntPtr documentName = Marshal.StringToHGlobalUni("document");
        IntPtr font = IntPtr.Zero;
        bool started = false;
        try
        {
            int dotsX = GetDeviceCaps(context, LogPixelsX), dotsY = GetDeviceCaps(context, LogPixelsY);
            font = CreateFont(-(int)Math.Round(10.0 * dotsY / 72.0), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Consolas");
            SelectObject(context, font);
            GetTextMetrics(context, out TextMetric metric);
            double lineHeight = metric.Height + metric.ExternalLeading;
            // One inch from each edge of the paper, in device dots; the device
            // draws from the corner of what it can print on.
            double pageHeightInches = (double)GetDeviceCaps(context, PhysicalHeight) / dotsY;
            int left = dotsX - GetDeviceCaps(context, PhysicalOffsetX);
            int top = dotsY - GetDeviceCaps(context, PhysicalOffsetY);
            double marginHeightDots = (pageHeightInches - 2) * dotsY;
            int linesPerPage = Math.Max(1, (int)Math.Round(marginHeightDots / lineHeight, MidpointRounding.ToEven));

            var document = new DocInfo { Size = sizeof(DocInfo), DocumentName = documentName };
            if (StartDoc(context, ref document) <= 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            started = true;
            string[] lines = Regex.Split(content, "\r\n|\n");
            int next = 0;
            do
            {
                if (StartPage(context) <= 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                for (int row = 0; row < linesPerPage && next < lines.Length; row++, next++)
                {
                    string line = lines[next];
                    TextOut(context, left, top + (int)Math.Round(row * lineHeight), line, line.Length);
                }
                if (EndPage(context) <= 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            while (next < lines.Length);
            EndDoc(context);
            started = false;
        }
        catch (Win32Exception e)
        {
            throw PrintFailed(path, printerName, e);
        }
        finally
        {
            if (started) AbortDoc(context);
            if (font != IntPtr.Zero) DeleteObject(font);
            Marshal.FreeHGlobal(documentName);
            DeleteDC(context);
        }
    }

    static PrintProblem PrintFailed(string path, string printerName, Win32Exception e) =>
        new($"I could not print \"{path}\" to \"{printerName}\". {OtterCommands.FrameworkMessage(e)}");
}
