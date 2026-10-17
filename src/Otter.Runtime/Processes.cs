using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Otter.Runtime;

// Processes (D70, D71, D75): a port of otterPS's Start-OtterProgram,
// New-OtterProcessObject, Get-OtterProcessList, Stop-OtterProcess,
// Set-OtterProcessPriority and Wait-OtterProcess.
//
// A process handle is a thing of type "process" with id, name, memoryBytes,
// cpuSeconds and startTime - whether it came from `run ... into p` or from
// `get processes into list`.
public static class OtterProcesses
{
    // run "notepad.exe" [into p]: starts the program and carries straight on,
    // as Start-Process -PassThru does (Windows starts it through the shell;
    // the arguments are joined with plain spaces, exactly as Start-Process
    // joins -ArgumentList).
    public static OtterThing Start(string target)
    {
        List<string> parts = OtterCommands.Split(target);
        string program = parts[0];
        List<string> arguments = parts.Skip(1).ToList();

        // A .cmd or .bat script is started through cmd.exe, which reads the
        // whole line itself: the script and its arguments get the same
        // treatment as `run command` gives them.
        if (OperatingSystem.IsWindows())
        {
            string? resolved = OtterCommands.FindApplication(program);
            string extension = (resolved != null ? Path.GetExtension(resolved) : Path.GetExtension(program)).ToLowerInvariant();
            if (extension is ".cmd" or ".bat")
            {
                OtterCommands.CmdArgument(program);
                arguments = arguments.Select(OtterCommands.CmdArgument).ToList();
            }
        }

        var start = new ProcessStartInfo
        {
            FileName = program,
            Arguments = string.Join(" ", arguments),
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = OperatingSystem.IsWindows(),
        };
        if (!OperatingSystem.IsWindows())
        {
            // PowerShell 7 off Windows finds the program on PATH and starts it
            // directly, sharing this console.
            start.FileName = OtterCommands.FindApplication(program) ?? program;
        }
        Process? started;
        try
        {
            started = Process.Start(start);
        }
        catch (Win32Exception e)
        {
            throw new CommandProblem($"I could not start \"{program}\". This command cannot be run due to the error: {OtterCommands.FrameworkMessage(e)}.");
        }
        return started != null ? ProcessObject(started) : NoProcessObject();
    }

    // The shell may hand the work to a process that is already running (a
    // document opened in an open editor): Start-Process -PassThru then gives
    // nothing, and New-OtterProcessObject reads every detail of nothing - a
    // number as 0, the rest as gone.
    static OtterThing NoProcessObject()
    {
        var info = new OtterThing("process");
        info.WriteProperty("id", 0.0);
        info.WriteProperty("name", null);
        info.WriteProperty("memoryBytes", 0.0);
        info.WriteProperty("cpuSeconds", 0.0);
        info.WriteProperty("startTime", null);
        return info;
    }

    // New-OtterProcessObject: each detail read on its own, so a process this
    // user may not inspect still gives its id and name.
    public static OtterThing ProcessObject(Process process)
    {
        var info = new OtterThing("process");
        info.WriteProperty("id", (double)process.Id);
        info.WriteProperty("name", Try(() => process.ProcessName));
        info.WriteProperty("memoryBytes", Try<object?>(() => (double)process.WorkingSet64));
        info.WriteProperty("cpuSeconds", Try<object?>(() => process.TotalProcessorTime.TotalSeconds));
        info.WriteProperty("startTime", Try(() => process.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        return info;
    }

    static T? Try<T>(Func<T> read)
    {
        try { return read(); }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException or PlatformNotSupportedException) { return default; }
    }

    // Get-Process: every process, ordered by name and then id.
    public static OtterList List()
    {
        var list = new OtterList();
        Process[] processes = Process.GetProcesses();
        var ordered = processes
            .Select(p => (Process: p, Name: Try(() => p.ProcessName) ?? ""))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Process.Id);
        foreach (var entry in ordered) list.Add(ProcessObject(entry.Process));
        return list;
    }

    // Stop-OtterProcess: a process that has already gone is not an error. With
    // its children, the whole tree below it goes too (Windows PowerShell walks
    // Win32_Process's parent links for this).
    public static void Kill(int processId, bool includeChildren)
    {
        Process process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        try { process.Kill(includeChildren); }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException) { }
    }

    // Set-OtterProcessPriority.
    public static bool TryPriorityClass(string priority, out ProcessPriorityClass priorityClass)
    {
        switch (priority.ToLowerInvariant())
        {
            case "low": priorityClass = ProcessPriorityClass.Idle; return true;
            case "below normal": priorityClass = ProcessPriorityClass.BelowNormal; return true;
            case "normal": priorityClass = ProcessPriorityClass.Normal; return true;
            case "above normal": priorityClass = ProcessPriorityClass.AboveNormal; return true;
            case "high": priorityClass = ProcessPriorityClass.High; return true;
            case "realtime": priorityClass = ProcessPriorityClass.RealTime; return true;
            default: priorityClass = ProcessPriorityClass.Normal; return false;
        }
    }

    public static void SetPriority(int processId, ProcessPriorityClass priorityClass)
    {
        Process process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException)
        {
            throw new CommandProblem($"I could not set that process's priority. Cannot find a process with the process identifier {processId}.");
        }
        try { process.PriorityClass = priorityClass; }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException or PlatformNotSupportedException)
        {
            string message = e is Win32Exception win32 ? OtterCommands.FrameworkMessage(win32) : e.Message;
            throw new CommandProblem($"I could not set that process's priority. Exception setting \"PriorityClass\": \"{message}\"");
        }
    }

    // Wait-OtterProcess: true when the process has ended by the deadline (or
    // was never there to wait for).
    public static bool Wait(int processId, double timeoutSeconds)
    {
        Process process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        // [int]: the nearest whole number, halves to even.
        double milliseconds = Math.Round(Math.Max(0, timeoutSeconds * 1000), MidpointRounding.ToEven);
        int timeout = milliseconds >= int.MaxValue ? int.MaxValue : (int)milliseconds;
        try { return process.WaitForExit(timeout); }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or SystemException) { return true; }
    }
}
