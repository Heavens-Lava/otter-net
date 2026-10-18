using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Otter.Language;

namespace Otter.Runtime;

// D119-R2: a command started in the background. A port of otterPS's
// OtterProcessTracker and OtterCommandJob (src/Otter.Runtime.psm1),
// Start-OtterCommandJob and Stop-OtterCommandJob (src/Otter.Library.psm1) and
// Invoke-OtterJobEventLoopStep (src/Otter.Interpreter.psm1). The process's
// lines arrive on .NET's threads and wait in a queue for the event loop.
public sealed class OtterCommandJob(string commandLine) : IOtterTypeNamed
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string CommandLine { get; } = commandLine;
    // running, completed, failed or cancelled
    public string State { get; set; } = "running";
    public double? ExitCode { get; set; }
    public Process? Process { get; set; }
    public JobTracker? Tracker { get; set; }
    // Set when the command could not start at all.
    public StringBuilder ErrorBuffer { get; } = new();
    public List<JobHandler> Handlers { get; } = new();
    public ConcurrentQueue<JobEvent> EventQueue { get; } = new();
    public JobEvent? RetainedTerminalEvent { get; set; }

    public string GetOutput() => Tracker?.GetOutput() ?? "";
    public string GetErrorOutput() => Tracker != null ? Tracker.GetErrorOutput() : ErrorBuffer.ToString().TrimEnd();

    public string OtterTypeName => "a command job";
    public string OtterText => $"<a command job: {CommandLine}>";
    public override string ToString() => "OtterCommandJob";
}

public sealed class JobEvent(string kind, Dictionary<string, object?> context)
{
    public string Kind { get; } = kind;
    public Dictionary<string, object?> Context { get; } = context;
}

public sealed class JobHandler(string eventName, IReadOnlyList<Stmt> body, Scope scope)
{
    public string EventName { get; } = eventName;
    public IReadOnlyList<Stmt> Body { get; } = body;
    public Scope Scope { get; } = scope;
    public bool Fired { get; set; }
}

// OtterProcessTracker: reads the process's output and error output line by
// line and queues them, then its exit (and "complete" when the exit code is 0).
public sealed class JobTracker
{
    public readonly record struct Item(string Kind, string? Data, double ExitCode);

    readonly StringBuilder output = new();
    readonly StringBuilder errorOutput = new();
    readonly object gate = new();
    public Process Process { get; }
    public ConcurrentQueue<Item> Queue { get; } = new();

    public JobTracker(ProcessStartInfo info)
    {
        Process = new Process { StartInfo = info, EnableRaisingEvents = true };
        Process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (gate) output.Append(e.Data).Append(Environment.NewLine);
            Queue.Enqueue(new Item("output", e.Data, 0));
        };
        Process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (gate) errorOutput.Append(e.Data).Append(Environment.NewLine);
            Queue.Enqueue(new Item("error output", e.Data, 0));
        };
        Process.Exited += (_, _) =>
        {
            double code = 0;
            try
            {
                // Waits for the last lines of output too.
                Process.WaitForExit();
                code = Process.ExitCode;
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
            Queue.Enqueue(new Item("exit", null, code));
            if (code == 0) Queue.Enqueue(new Item("complete", null, code));
        };
        Process.Start();
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
    }

    public string GetOutput() { lock (gate) return output.ToString().TrimEnd(); }
    public string GetErrorOutput() { lock (gate) return errorOutput.ToString().TrimEnd(); }
}

public sealed partial class Interpreter
{
    readonly List<OtterCommandJob> activeJobs = new();
    Dictionary<string, object?>? currentJobContext;

    // otterPS: one job handles at most this many events per turn (EV2).
    const int JobEventsPerTurn = 256;

    // Start-OtterCommandJob: a command that cannot start gives a failed job
    // whose exit is kept for a handler set up afterwards.
    static OtterCommandJob StartJob(string commandLine)
    {
        var job = new OtterCommandJob(commandLine);
        ProcessStartInfo info;
        try { info = OtterCommands.StartInfoFor(commandLine).Start; }
        catch (CommandProblem problem) { return FailedJob(job, problem.Message); }
        try
        {
            var tracker = new JobTracker(info);
            job.Tracker = tracker;
            job.Process = tracker.Process;
        }
        catch (Win32Exception e)
        {
            return FailedJob(job, $"Exception calling \"Start\" with \"1\" argument(s): \"{OtterCommands.FrameworkMessage(e)}\"");
        }
        return job;
    }

    static OtterCommandJob FailedJob(OtterCommandJob job, string message)
    {
        job.State = "failed";
        job.ExitCode = -1;
        job.ErrorBuffer.Append(message);
        job.RetainedTerminalEvent = new JobEvent("exit", new Dictionary<string, object?> { ["ExitCode"] = -1.0, ["ErrorOutput"] = message });
        return job;
    }

    // Stop-OtterCommandJob: the process and its children end; the cancel
    // event waits in the job's queue (otterPS runs it at once only inside a
    // desktop window's dispatcher).
    static void CancelJob(OtterCommandJob job)
    {
        if (job.State is "cancelled" or "completed" or "failed") return;
        job.State = "cancelled";
        try
        {
            if (job.Process is { HasExited: false } process) process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException) { }
        var cancel = new JobEvent("cancel", new Dictionary<string, object?>());
        job.RetainedTerminalEvent = cancel;
        job.EventQueue.Enqueue(cancel);
    }

    // Test-OtterJobsActive
    bool JobsActive() => activeJobs.Any(job =>
        (job.Tracker != null && !job.Tracker.Queue.IsEmpty) || !job.EventQueue.IsEmpty || (job.State == "running" && job.Handlers.Count > 0));

    void RunJobHandlers(OtterCommandJob job, string kind, Dictionary<string, object?> context)
    {
        var matching = job.Handlers.Where(h => h.EventName == kind).ToList();
        foreach (var handler in matching)
        {
            if (kind is "exit" or "complete" or "cancel")
            {
                if (handler.Fired) continue;
                handler.Fired = true;
            }
            var previous = currentJobContext;
            currentJobContext = context;
            try { Execute(handler.Body, handler.Scope); }
            finally { currentJobContext = previous; }
        }
    }

    // Invoke-OtterJobEventLoopStep
    void JobStep()
    {
        foreach (var job in activeJobs.ToArray())
        {
            int handled = 0;
            if (job.Tracker != null)
            {
                while (handled < JobEventsPerTurn && job.Tracker.Queue.TryDequeue(out JobTracker.Item item))
                {
                    handled++;
                    var context = new Dictionary<string, object?>();
                    if (item.Kind == "output") context["Output"] = item.Data;
                    else if (item.Kind == "error output") context["ErrorOutput"] = item.Data;
                    else if (item.Kind is "exit" or "complete")
                    {
                        context["ExitCode"] = item.ExitCode;
                        if (job.State != "cancelled")
                        {
                            job.ExitCode = item.ExitCode;
                            job.State = item.ExitCode == 0 ? "completed" : "failed";
                        }
                        if (item.Kind == "exit") job.RetainedTerminalEvent = new JobEvent("exit", context);
                    }
                    RunJobHandlers(job, item.Kind, context);
                }
            }
            while (handled < JobEventsPerTurn && job.EventQueue.TryDequeue(out JobEvent? queued))
            {
                handled++;
                RunJobHandlers(job, queued.Kind, queued.Context);
            }
        }
    }

    void StopJobs()
    {
        foreach (var job in activeJobs.ToArray())
        {
            if (job.State == "running") CancelJob(job);
        }
    }

    // ---- statements and values ----

    bool TryExecuteJob(Stmt statement, Scope scope)
    {
        switch (statement)
        {
            case StartCommandStmt start:
            {
                object? command = Evaluate(start.CommandLine, scope);
                if (command is not string commandLine)
                {
                    throw RuntimeError($"I need text for a command line to start, but this is {Values.TypeName(command)}.", start.Line);
                }
                OtterCommandJob job = StartJob(commandLine);
                activeJobs.Add(job);
                scope.Set(start.Target, job);
                return true;
            }
            // on output from / on error output from / on exit of <job>: the
            // console's `when` (otterPS's When case); UI resources are the
            // desktop's, which otter-net hands to otterPS.
            case WhenStmt handlerStmt:
            {
                if (handlerStmt.EventName is "drag" or "drop" or "files dropped")
                {
                    throw RuntimeError("Drag and drop (\"on drag of\", \"on drop on\", \"on files dropped on\") is not supported for windows yet. It works in web applications.",
                        handlerStmt.Line, "Compile the program with: otter web <file.ot>");
                }
                object? target = Evaluate(handlerStmt.Target, scope);
                if (target is not OtterCommandJob job)
                {
                    throw RuntimeError($"I can only listen for an event on a UI resource or command job, but this is {Values.TypeName(target)}.", handlerStmt.Line);
                }
                string eventName = handlerStmt.EventName.ToLowerInvariant();
                if (eventName is not ("output" or "error output" or "exit"))
                {
                    throw RuntimeError("A command job only supports \"on output from\", \"on error output from\", or \"on exit of\".", handlerStmt.Line);
                }
                var handler = new JobHandler(eventName, handlerStmt.Body, scope);
                job.Handlers.Add(handler);
                if (eventName == "exit" && job.RetainedTerminalEvent is { Kind: "exit" } retained)
                {
                    handler.Fired = true;
                    var previous = currentJobContext;
                    currentJobContext = retained.Context;
                    try { Execute(handlerStmt.Body, scope); }
                    finally { currentJobContext = previous; }
                }
                return true;
            }
        }
        return false;
    }

    // `on complete/cancel/error of <job>` (a NetworkEventStmt in otterPS too).
    void AddJobNetworkHandler(OtterCommandJob job, NetworkEventStmt networkEvent, Scope scope)
    {
        string? eventName = networkEvent.EventKind switch
        {
            NetworkEventKind.Complete => "complete",
            NetworkEventKind.Cancel => "cancel",
            NetworkEventKind.Error => "error",
            _ => null,
        };
        if (eventName == null) throw RuntimeError("A command job only supports \"on complete of\" or \"on cancel of\".", networkEvent.Line);
        var handler = new JobHandler(eventName, networkEvent.Body, scope);
        job.Handlers.Add(handler);
        // A finished job hands its outcome to a handler set up afterwards; an
        // exit counts as completing.
        if (job.RetainedTerminalEvent is { } retained && (retained.Kind == eventName || (eventName == "complete" && retained.Kind == "exit")))
        {
            handler.Fired = true;
            var previous = currentJobContext;
            currentJobContext = retained.Context;
            try { Execute(networkEvent.Body, scope); }
            finally { currentJobContext = previous; }
        }
    }

    bool TryEvaluateJob(Expr expression, out object? value)
    {
        value = null;
        if (expression is not JobContextExpr context) return false;
        if (context.Field == "output")
        {
            if (currentJobContext == null || !currentJobContext.TryGetValue("Output", out value))
            {
                throw RuntimeError("\"received output\" is only available inside \"on output from ...\".", context.Line);
            }
            return true;
        }
        if (currentJobContext == null || !currentJobContext.TryGetValue("ErrorOutput", out value))
        {
            throw RuntimeError("\"received error output\" is only available inside \"on error output from ...\".", context.Line);
        }
        return true;
    }

    // state / exit code / output / error output / id / command of a job
    bool TryJobProperty(object? target, PropertyAccessExpr property, out object? value)
    {
        value = null;
        if (target is not OtterCommandJob job) return false;
        value = property.Property.ToLowerInvariant() switch
        {
            "state" => job.State,
            "exit code" => job.ExitCode,
            "output" => job.GetOutput(),
            "error output" => job.GetErrorOutput(),
            "id" => job.Id,
            "command" => job.CommandLine,
            _ => throw RuntimeError($"This command job has no property called \"{property.Property}\". Try state, exit code, output, error output, id, or command.", property.Line),
        };
        return true;
    }
}
