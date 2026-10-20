using System.Diagnostics;
using System.Globalization;
using System.Text;
using Otter.Language;
using Otter.Runtime;

namespace Otter.Cli;

// `otter debug <file.ot> -Breakpoints "5,12"`: otterPS's
// src/Otter.Debugger.psm1. As a statement on a breakpoint line is about to
// run, one line "@@OTTER_DEBUG@@ {json}" says where the program is, its
// locals (formatted as `say` formats them) and its Otter call stack; then
// the program waits for a line "continue" on standard input (end of input
// continues too). {"event":"finished"} follows when the program stops,
// however it stops.
internal sealed class Debugger(string fileName, IEnumerable<int> breakpoints)
{
    const string EventPrefix = "@@OTTER_DEBUG@@ ";
    readonly HashSet<int> breakpointSet = new(breakpoints);
    Interpreter? interpreter;

    public void Attach(Interpreter running) => interpreter = running;

    public Action<Stmt, Scope> Hook => (statement, scope) =>
    {
        if (!breakpointSet.Contains(statement.Line)) return;
        var json = new StringBuilder();
        json.Append("{\"event\":\"paused\",\"file\":");
        CompactJson.String(fileName, json);
        json.Append(",\"line\":").Append(statement.Line.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"locals\":{");
        bool first = true;
        // Only what this frame's own scope holds, never its parents'.
        foreach (var (name, _) in scope.OwnVariables.ToArray())
        {
            if (!first) json.Append(',');
            first = false;
            CompactJson.String(name, json);
            json.Append(':');
            // Get-OtterDebugLocals: $Environment.Get (a state variable's value,
            // a derived value worked out), '?' when that fails.
            string shown;
            try { shown = Values.Format(scope.Get(name)); }
            catch (OtterError) { shown = "?"; }
            CompactJson.String(shown, json);
        }
        json.Append("},\"callStack\":[");
        first = true;
        foreach (CallFrame frame in interpreter?.CallStack ?? new List<CallFrame>())
        {
            if (!first) json.Append(',');
            first = false;
            json.Append("{\"function\":");
            CompactJson.String(frame.FunctionName, json);
            json.Append(",\"line\":").Append(frame.CallLine.ToString(CultureInfo.InvariantCulture)).Append('}');
        }
        json.Append("]}");
        WriteEvent(json.ToString());

        while (true)
        {
            string? typed = Host.In.ReadLine();
            if (typed == null) return;   // input closed - never hang forever
            if (typed.Trim() == "continue") return;
        }
    };

    public void Finished() => WriteEvent("{\"event\":\"finished\"}");

    // [Console]::Out.WriteLine: the platform's line ending.
    static void WriteEvent(string json)
    {
        Host.Raw(EventPrefix + json + Environment.NewLine);
        Host.Out.Flush();
    }
}

// ConvertTo-Json -Compress on Windows PowerShell 5.1 (JavaScriptSerializer):
// the string escapes it uses.
internal static class CompactJson
{
    public static void String(string text, StringBuilder sb)
    {
        sb.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < ' ' || c is '\'' or '<' or '>' or '&' or '\u0085' or (char)0x2028 or (char)0x2029)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}

// `otter profile <file.ot>`: otterPS's src/Otter.Profiler.psm1. The time
// between two statement starts is charged to the earlier statement's line
// and to the function on top of the stack then; the report lists functions
// by total time and the hottest lines by self time. Times are measured, so
// they differ from run to run; the layout is otterPS's.
internal sealed class Profiler
{
    sealed class LineEntry { public long Hits, Ticks, CpuTicks, AllocatedBytes; }
    sealed class FunctionEntry { public long Calls, SelfTicks, TotalTicks, SelfCpuTicks, TotalCpuTicks, SelfAllocatedBytes, TotalAllocatedBytes; }

    readonly Stopwatch clock;
    readonly Process process;
    readonly long startCpuTicks, startUserTicks, startKernelTicks;
    readonly long startManagedBytes, startAllocatedBytes, startWorkingSet;
    readonly int startGen0, startGen1, startGen2;
    long peakManagedBytes, lastAllocatedBytes, lastTicks, lastCpuTicks;
    int lastLine;
    string[] lastStack = Array.Empty<string>();
    readonly Dictionary<int, CallFrame> lastFrames = new();
    readonly Dictionary<int, LineEntry> lines = new();
    // A PowerShell @{} hashtable: names differing only in capitals share an entry.
    readonly Dictionary<string, FunctionEntry> functions = new(StringComparer.OrdinalIgnoreCase);
    long mainTicks, mainCpuTicks, mainAllocatedBytes, statements;
    Interpreter? interpreter;
    bool reported;

    // Start-OtterProfile.
    public Profiler()
    {
        process = Process.GetCurrentProcess();
        process.Refresh();
        startCpuTicks = process.TotalProcessorTime.Ticks;
        startUserTicks = process.UserProcessorTime.Ticks;
        startKernelTicks = process.PrivilegedProcessorTime.Ticks;
        startManagedBytes = GC.GetTotalMemory(false);
        peakManagedBytes = startManagedBytes;
        startAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
        lastAllocatedBytes = startAllocatedBytes;
        startWorkingSet = process.WorkingSet64;
        startGen0 = GC.CollectionCount(0);
        startGen1 = GC.CollectionCount(1);
        startGen2 = GC.CollectionCount(2);
        lastCpuTicks = startCpuTicks;
        clock = Stopwatch.StartNew();
    }

    public void Attach(Interpreter running) => interpreter = running;

    public Action<Stmt, Scope> Hook => (statement, scope) =>
    {
        long now = clock.ElapsedTicks;
        long delta = now - lastTicks;
        lastTicks = now;
        long nowCpu = process.TotalProcessorTime.Ticks;
        long deltaCpu = nowCpu - lastCpuTicks;
        lastCpuTicks = nowCpu;
        long nowAllocated = GC.GetAllocatedBytesForCurrentThread();
        long deltaAllocated = Math.Max(0L, nowAllocated - lastAllocatedBytes);
        lastAllocatedBytes = nowAllocated;
        long nowManaged = GC.GetTotalMemory(false);
        if (nowManaged > peakManagedBytes) peakManagedBytes = nowManaged;

        AddInterval(delta, deltaCpu, deltaAllocated);

        statements++;
        int line = statement.Line;
        lastLine = line;
        if (!lines.TryGetValue(line, out LineEntry? entry)) lines[line] = entry = new LineEntry();
        entry.Hits++;

        IReadOnlyList<CallFrame> stack = interpreter?.CallStack ?? (IReadOnlyList<CallFrame>)Array.Empty<CallFrame>();
        var names = new string[stack.Count];
        for (int i = 0; i < stack.Count; i++)
        {
            CallFrame frame = stack[i];
            names[i] = frame.FunctionName;
            if (!lastFrames.TryGetValue(i, out CallFrame? seen) || !ReferenceEquals(seen, frame))
            {
                lastFrames[i] = frame;
                Function(frame.FunctionName).Calls++;
            }
        }
        foreach (int depth in lastFrames.Keys.ToList())
        {
            if (depth >= stack.Count) lastFrames.Remove(depth);
        }
        lastStack = names;
    };

    FunctionEntry Function(string name)
    {
        if (!functions.TryGetValue(name, out FunctionEntry? entry)) functions[name] = entry = new FunctionEntry();
        return entry;
    }

    void AddInterval(long ticks, long cpuTicks, long allocatedBytes)
    {
        if (lastLine <= 0) return;   // before the first statement
        LineEntry line = lines[lastLine];
        line.Ticks += ticks;
        line.CpuTicks += cpuTicks;
        line.AllocatedBytes += allocatedBytes;
        if (lastStack.Length == 0)
        {
            mainTicks += ticks;
            mainCpuTicks += cpuTicks;
            mainAllocatedBytes += allocatedBytes;
            return;
        }
        FunctionEntry top = Function(lastStack[^1]);
        top.SelfTicks += ticks;
        top.SelfCpuTicks += cpuTicks;
        top.SelfAllocatedBytes += allocatedBytes;
        // Total counts each distinct function once, so recursion is not double-counted.
        foreach (string name in lastStack.Distinct(StringComparer.Ordinal))
        {
            FunctionEntry f = Function(name);
            f.TotalTicks += ticks;
            f.TotalCpuTicks += cpuTicks;
            f.TotalAllocatedBytes += allocatedBytes;
        }
    }

    static double Ms(long stopwatchTicks) => Math.Round(stopwatchTicks * 1000.0 / Stopwatch.Frequency, 2);
    static double CpuMs(long ticks) => Math.Round(ticks / 10000.0, 2);

    // Format-OtterProfileBytes.
    public static string Bytes(long bytes)
    {
        string sign = bytes < 0 ? "-" : "";
        long abs = Math.Abs(bytes);
        if (abs >= 1073741824) return sign + Number(Math.Round(abs / 1073741824.0, 1)) + " GB";
        if (abs >= 1048576) return sign + Number(Math.Round(abs / 1048576.0, 1)) + " MB";
        if (abs >= 1024) return sign + Number(Math.Round(abs / 1024.0, 1)) + " KB";
        return sign + abs.ToString(CultureInfo.InvariantCulture) + " B";
    }

    static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    static string F(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);

    // Write-OtterProfileReport (top 10 of each).
    public void Report(string[] sourceLines)
    {
        if (reported) return;
        reported = true;
        const int top = 10;
        long now = clock.ElapsedTicks;
        long nowCpu = process.TotalProcessorTime.Ticks;
        long nowAllocated = GC.GetAllocatedBytesForCurrentThread();
        long deltaAllocated = Math.Max(0L, nowAllocated - lastAllocatedBytes);
        lastAllocatedBytes = nowAllocated;
        AddInterval(now - lastTicks, nowCpu - lastCpuTicks, deltaAllocated);
        lastTicks = now;
        lastCpuTicks = nowCpu;
        clock.Stop();

        double totalMs = Ms(clock.ElapsedTicks);
        double totalCpuMs = CpuMs(process.TotalProcessorTime.Ticks - startCpuTicks);
        double userCpuMs = CpuMs(process.UserProcessorTime.Ticks - startUserTicks);
        double kernelCpuMs = CpuMs(process.PrivilegedProcessorTime.Ticks - startKernelTicks);
        double utilization = totalMs > 0 ? Math.Round(totalCpuMs / totalMs * 100.0, 1) : 0.0;

        process.Refresh();
        long endManaged = GC.GetTotalMemory(false);
        if (endManaged > peakManagedBytes) peakManagedBytes = endManaged;
        long managedDelta = endManaged - startManagedBytes;
        long totalAllocated = Math.Max(0L, nowAllocated - startAllocatedBytes);
        long peakWorkingSet = process.PeakWorkingSet64;
        int gen0 = GC.CollectionCount(0) - startGen0, gen1 = GC.CollectionCount(1) - startGen1, gen2 = GC.CollectionCount(2) - startGen2;
        _ = startWorkingSet;

        var functionRows = functions
            .Select(pair => (Name: pair.Key, Entry: pair.Value, TotalMs: Ms(pair.Value.TotalTicks)))
            .OrderByDescending(row => row.TotalMs).Take(top).ToList();
        var lineRows = lines
            .Select(pair => (Line: pair.Key, Entry: pair.Value, SelfMs: Ms(pair.Value.Ticks)))
            .OrderByDescending(row => row.SelfMs).Take(top).ToList();

        Host.Line("", site: PsSite.At(@"src\Otter.Profiler.psm1", 307));
        Host.Line(new string('=', 60), ConsoleColor.Cyan);
        Host.Line("Otter profile", ConsoleColor.Cyan);
        Host.Line(new string('=', 60), ConsoleColor.Cyan);
        Host.Line(F("{0} statements run in {1} ms (CPU: {2} ms, {3}% utilization; user: {4} ms, kernel: {5} ms)",
            statements, totalMs, totalCpuMs, utilization, userCpuMs, kernelCpuMs));
        Host.Line(F("Memory: managed peak {0} (delta {1}), working set peak {2}; allocated: {3}; GC: {4} gen0, {5} gen1, {6} gen2",
            Bytes(peakManagedBytes), Bytes(managedDelta), Bytes(peakWorkingSet), Bytes(totalAllocated), gen0, gen1, gen2));
        Host.Line("(profiling adds overhead to every statement - read the shape, not the absolute times)", ConsoleColor.DarkGray);

        Host.Line("");
        Host.Line("Functions (by total time)", ConsoleColor.Yellow);
        if (functionRows.Count == 0)
        {
            Host.Line("  (no functions were called)", ConsoleColor.DarkGray);
        }
        else
        {
            Host.Line(F("  {0,-24} {1,8} {2,11} {3,10} {4,10} {5,10}", "function", "calls", "total ms", "self ms", "cpu ms", "alloc"));
            foreach (var row in functionRows)
            {
                Host.Line(F("  {0,-24} {1,8} {2,11} {3,10} {4,10} {5,10}", row.Name, row.Entry.Calls, row.TotalMs, Ms(row.Entry.SelfTicks), CpuMs(row.Entry.TotalCpuTicks), Bytes(row.Entry.TotalAllocatedBytes)));
            }
        }

        Host.Line("");
        Host.Line("Hottest lines (by self time)", ConsoleColor.Yellow);
        Host.Line(F("  {0,5} {1,8} {2,10} {3,10} {4,10}  {5}", "line", "hits", "self ms", "cpu ms", "alloc", "source"));
        foreach (var row in lineRows)
        {
            string text = row.Line >= 1 && row.Line <= sourceLines.Length ? sourceLines[row.Line - 1].Trim() : "";
            string shown = text.Length > 45 ? text.Substring(0, 42) + "..." : text;
            Host.Line(F("  {0,5} {1,8} {2,10} {3,10} {4,10}  {5}", row.Line, row.Entry.Hits, row.SelfMs, CpuMs(row.Entry.CpuTicks), Bytes(row.Entry.AllocatedBytes), shown));
        }
        Host.Line("");
    }
}
