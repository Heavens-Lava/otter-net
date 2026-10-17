using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Otter.Runtime;

public sealed class ClipboardProblem(string message) : Exception(message);

// The clipboard (D67): what Set-Clipboard and Get-Clipboard -Raw do for
// otterPS. On Windows that is the system clipboard's Unicode text; on macOS
// PowerShell 7 runs pbcopy and pbpaste, and on Linux xclip, keeping the text
// inside the program when xclip is not there.
public static partial class OtterClipboard
{
    static string? internalClipboard;

    public static void SetText(string text)
    {
        if (OperatingSystem.IsWindows())
        {
            // otterPS tries five times, 25 ms apart: another program may be
            // holding the clipboard open.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (WindowsClipboard.TrySetText(text)) return;
                if (attempt < 4) Thread.Sleep(25);
            }
            return;
        }
        if (OperatingSystem.IsMacOS())
        {
            RunTool("pbcopy", "", text);
            return;
        }
        try { RunTool("xclip", "-selection clipboard -in", text); }
        catch (Win32Exception) { internalClipboard = text; }
    }

    // gone when the clipboard holds no text.
    public static string? GetText()
    {
        string? value = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (OperatingSystem.IsWindows())
            {
                value = WindowsClipboard.TryGetText();
            }
            else
            {
                try { value = OperatingSystem.IsMacOS() ? RunTool("pbpaste", "", null) : ReadLinux(); }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException)
                {
                    if (attempt >= 4) throw new ClipboardProblem($"I could not read the clipboard: {e.Message}");
                    value = null;
                }
            }
            if (!string.IsNullOrEmpty(value)) break;
            if (attempt < 4) Thread.Sleep(40);
        }
        return value;
    }

    static string? ReadLinux()
    {
        try { return RunTool("xclip", "-selection clipboard -out", null); }
        catch (Win32Exception) { return internalClipboard; }
    }

    static string RunTool(string tool, string arguments, string? input)
    {
        var start = new ProcessStartInfo(tool, arguments)
        {
            UseShellExecute = false,
            RedirectStandardInput = input != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        if (input != null) start.StandardInputEncoding = new UTF8Encoding(false);
        using Process process = Process.Start(start) ?? throw new Win32Exception(2);
        if (input != null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        error.Wait();
        return output;
    }

    // The Windows clipboard's CF_UNICODETEXT.
    [SupportedOSPlatform("windows")]
    static partial class WindowsClipboard
    {
        const uint UnicodeText = 13;
        const uint Moveable = 0x0002;

        [LibraryImport("user32.dll", EntryPoint = "OpenClipboard", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool OpenClipboard(IntPtr owner);

        [LibraryImport("user32.dll", EntryPoint = "CloseClipboard", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseClipboard();

        [LibraryImport("user32.dll", EntryPoint = "EmptyClipboard", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool EmptyClipboard();

        [LibraryImport("user32.dll", EntryPoint = "SetClipboardData", SetLastError = true)]
        private static partial IntPtr SetClipboardData(uint format, IntPtr data);

        [LibraryImport("user32.dll", EntryPoint = "GetClipboardData", SetLastError = true)]
        private static partial IntPtr GetClipboardData(uint format);

        [LibraryImport("user32.dll", EntryPoint = "IsClipboardFormatAvailable", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsClipboardFormatAvailable(uint format);

        [LibraryImport("kernel32.dll", EntryPoint = "GlobalAlloc", SetLastError = true)]
        private static partial IntPtr GlobalAlloc(uint flags, nuint bytes);

        [LibraryImport("kernel32.dll", EntryPoint = "GlobalLock", SetLastError = true)]
        private static partial IntPtr GlobalLock(IntPtr memory);

        [LibraryImport("kernel32.dll", EntryPoint = "GlobalUnlock", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GlobalUnlock(IntPtr memory);

        [LibraryImport("kernel32.dll", EntryPoint = "GlobalFree", SetLastError = true)]
        private static partial IntPtr GlobalFree(IntPtr memory);

        // Empty text clears the clipboard, so reading it back gives gone.
        public static bool TrySetText(string text)
        {
            if (!OpenClipboard(IntPtr.Zero)) return false;
            try
            {
                if (!EmptyClipboard()) return false;
                if (text.Length == 0) return true;
                int bytes = (text.Length + 1) * 2;
                IntPtr memory = GlobalAlloc(Moveable, (nuint)bytes);
                if (memory == IntPtr.Zero) return false;
                IntPtr target = GlobalLock(memory);
                if (target == IntPtr.Zero)
                {
                    GlobalFree(memory);
                    return false;
                }
                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                Marshal.WriteInt16(target, text.Length * 2, 0);
                GlobalUnlock(memory);
                if (SetClipboardData(UnicodeText, memory) == IntPtr.Zero)
                {
                    GlobalFree(memory);
                    return false;
                }
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }

        public static string? TryGetText()
        {
            if (!IsClipboardFormatAvailable(UnicodeText)) return null;
            if (!OpenClipboard(IntPtr.Zero)) return null;
            try
            {
                IntPtr memory = GetClipboardData(UnicodeText);
                if (memory == IntPtr.Zero) return null;
                IntPtr source = GlobalLock(memory);
                if (source == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(source); }
                finally { GlobalUnlock(memory); }
            }
            finally
            {
                CloseClipboard();
            }
        }
    }
}
