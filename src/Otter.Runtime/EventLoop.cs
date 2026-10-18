using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Otter.Language;

namespace Otter.Runtime;

// The console event loop (otterPS's Invoke-OtterEventLoop and its steps in
// src/Otter.Interpreter.psm1; the contract is D121, docs/OTTER_1_0_EVENT_MODEL.md
// in otterPS): handlers run after the program's last statement and during
// `wait`, one at a time, on the interpreter's own thread. .NET completes
// network work on its own threads; the loop only looks at whether a task has
// finished, exactly as otterPS polls IsCompleted, so a handler never
// interrupts running Otter code.
//
// Also here: the run-time half of D130 (a loop that sets up a handler gives
// each pass its own names) and D133 (a `when` handler set up during another
// handler's run keeps that run's names). The static half is LoopPasses.cs.
public sealed partial class Interpreter
{
    // ---- the event sources of this run ----

    readonly List<OtterFileWatcher> activeWatchers = new();
    readonly Dictionary<OtterFileWatcher, List<WatchHandler>> watcherHandlers = new(ReferenceEqualityComparer.Instance);
    readonly WatchQueue watchQueue = new();
    readonly Dictionary<string, DateTime> watchLastEventAt = new(StringComparer.Ordinal);
    WatchContext? currentWatchEvent;

    readonly List<OtterWebSocket> activeWebSockets = new();
    readonly List<object> activeNet = new();               // OtterTcp and OtterUdp
    readonly List<OtterTcpServer> activeTcpServers = new();
    readonly List<OtterHttpRequest> activeHttpRequests = new();
    readonly List<IOtterEventSource> otherEventSources = new();
    // Every `on ... of` handler of a socket, server or request, by object.
    readonly Dictionary<object, List<NetHandler>> netHandlers = new(ReferenceEqualityComparer.Instance);
    Dictionary<string, object?>? currentNetContext;

    sealed class WatchHandler(WatchEventKind kind, IReadOnlyList<Stmt> body, Scope scope)
    {
        public WatchEventKind Kind { get; } = kind;
        public IReadOnlyList<Stmt> Body { get; } = body;
        public Scope Scope { get; } = scope;
    }

    sealed class NetHandler(NetworkEventKind kind, IReadOnlyList<Stmt> body, Scope scope)
    {
        public NetworkEventKind Kind { get; } = kind;
        public IReadOnlyList<Stmt> Body { get; } = body;
        public Scope Scope { get; } = scope;
        public bool Fired { get; set; }
    }

    sealed record WatchContext(string Path, string FileName, string Kind, string? OldPath);

    // Another area's event source (command jobs) joins the loop here.
    public void AddEventSource(IOtterEventSource source) => otherEventSources.Add(source);

    // ---- the loop ----

    // Invoke-OtterEventLoop: runs until nothing can produce an event a
    // handler cares about. A watcher keeps the program alive until it is
    // stopped; a socket, server or request only while it has handlers.
    void RunEventLoop()
    {
        while (true)
        {
            bool hasWatchers = activeWatchers.Count > 0;
            bool hasSockets = activeWebSockets.Any(ws => ws.State is "connecting" or "open" or "closing" && HasHandlers(ws))
                || NetActive() || HttpActive() || JobsActive() || otherEventSources.Any(source => source.IsActive);
            if (!hasWatchers && !hasSockets) break;
            if (hasWatchers) WatchStep(TimeSpan.FromMilliseconds(hasSockets ? 20 : 200));
            else Thread.Sleep(10);
            if (hasSockets)
            {
                WebSocketStep();
                NetStep();
                HttpStep();
                JobStep();
                foreach (var source in otherEventSources.ToArray()) source.Step();
            }
        }
    }

    // Invoke-OtterWaitServiceStep: one pass over every source, without
    // waiting; `wait` calls it between its short sleeps (EV3).
    void ServiceEventsDuringWait()
    {
        if (activeWatchers.Count == 0 && activeWebSockets.Count == 0 && activeNet.Count == 0 && activeTcpServers.Count == 0 &&
            activeHttpRequests.Count == 0 && activeJobs.Count == 0 && otherEventSources.Count == 0) return;
        WatchStep(TimeSpan.Zero);
        WebSocketStep();
        NetStep();
        HttpStep();
        JobStep();
        foreach (var source in otherEventSources.ToArray()) source.Step();
    }

    // The `finally` of Invoke-OtterProgram: nothing is left running.
    void CloseEventSources()
    {
        foreach (var watcher in activeWatchers.ToArray()) StopWatcher(watcher);
        foreach (var ws in activeWebSockets.ToArray()) CloseWebSocketNow(ws);
        foreach (var net in activeNet.ToArray()) CloseNet(net);
        foreach (var server in activeTcpServers.ToArray()) StopTcpServer(server);
        foreach (var request in activeHttpRequests.ToArray())
        {
            if (request.State == "pending")
            {
                request.State = "cancelled";
                try { request.Cts.Cancel(); } catch (ObjectDisposedException) { }
            }
            if (!request.Disposed)
            {
                request.Cts.Dispose();
                request.Disposed = true;
            }
        }
        StopJobs();
        foreach (var source in otherEventSources.ToArray())
        {
            try { source.Shutdown(); } catch (Exception e) when (e is not OutOfMemoryException) { }
        }
    }

    bool HasHandlers(object target) => netHandlers.TryGetValue(target, out var list) && list.Count > 0;

    bool NetHasHandler(object target, NetworkEventKind kind) => netHandlers.TryGetValue(target, out var list) && list.Any(h => h.Kind == kind);

    // Invoke-OtterWebSocketHandlers: the handlers of one kind, in the order
    // they were set up, with the event's ambient values.
    void InvokeNetHandlers(object target, NetworkEventKind kind, Dictionary<string, object?> context)
    {
        if (!netHandlers.TryGetValue(target, out var all)) return;
        var handlers = all.Where(h => h.Kind == kind).ToList();
        if (handlers.Count == 0) return;
        var previous = currentNetContext;
        currentNetContext = context;
        try
        {
            foreach (var handler in handlers)
            {
                if (target is OtterHttpRequest)
                {
                    if (handler.Fired) continue;
                    handler.Fired = true;
                }
                // Each incoming connection gets a scope of its own.
                Scope scope = kind == NetworkEventKind.Connection ? new Scope(handler.Scope) : handler.Scope;
                Execute(handler.Body, scope);
            }
        }
        finally
        {
            currentNetContext = previous;
        }
    }

    // Send-OtterNetError: to "on error of" when there is one; otherwise the
    // program stops with the error (never silently).
    void SendNetError(object target, string message)
    {
        switch (target)
        {
            case OtterTcp tcp: tcp.LastError = message; break;
            case OtterUdp udp: udp.LastError = message; break;
            case OtterTcpServer server: server.LastError = message; break;
        }
        if (NetHasHandler(target, NetworkEventKind.Error))
        {
            InvokeNetHandlers(target, NetworkEventKind.Error, new Dictionary<string, object?> { ["NetError"] = message });
            return;
        }
        throw RuntimeError(message, 0, "Add \"on error of <name>\" to handle network errors.");
    }

    static string BaseMessage(Exception? e)
    {
        if (e == null) return "";
        Exception inner = e is AggregateException aggregate ? aggregate.GetBaseException() : e;
        while (inner.InnerException != null && inner is not SocketException) inner = inner.InnerException;
        return inner.Message;
    }

    // ---- file watching (D104) ----

    OtterFileWatcher NewFileWatcher(string path, bool isFolder, bool recursive, int line)
    {
        if (isFolder ? !Directory.Exists(path) : !File.Exists(path))
        {
            throw isFolder
                ? RuntimeError($"I can't watch a folder that doesn't exist: \"{path}\".", line, "Check the folder path, or create it first.")
                : RuntimeError($"I can't watch a file that doesn't exist: \"{path}\".", line, "Check the file path, or watch its folder instead to detect creation.");
        }
        string resolved = Path.GetFullPath(path);
        var native = new FileSystemWatcher();
        if (isFolder)
        {
            native.Path = resolved;
            native.Filter = "*";
            native.IncludeSubdirectories = recursive;
        }
        else
        {
            // One file is watched through its folder with a name filter, so
            // an editor's save-to-temp-then-rename is still seen.
            native.Path = Path.GetDirectoryName(resolved)!;
            native.Filter = Path.GetFileName(resolved);
            native.IncludeSubdirectories = false;
        }
        native.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size;
        var watcher = new OtterFileWatcher(native, resolved, isFolder);
        native.Changed += (_, e) => watchQueue.Add(new WatchNotification(watcher, "Changed", e.FullPath, null));
        native.Created += (_, e) => watchQueue.Add(new WatchNotification(watcher, "Created", e.FullPath, null));
        native.Deleted += (_, e) => watchQueue.Add(new WatchNotification(watcher, "Deleted", e.FullPath, null));
        native.Renamed += (_, e) => watchQueue.Add(new WatchNotification(watcher, "Renamed", e.FullPath, e.OldFullPath));
        native.Error += (_, _) => watchQueue.Add(new WatchNotification(watcher, "Error", null, null));
        native.EnableRaisingEvents = true;
        activeWatchers.Add(watcher);
        return watcher;
    }

    void StopWatcher(OtterFileWatcher watcher)
    {
        if (!watcher.Active) return;
        watcher.Active = false;
        watcher.Native.EnableRaisingEvents = false;
        watcher.Native.Dispose();
        activeWatchers.Remove(watcher);
    }

    // Duplicate notifications for one change (an editor writing a file in
    // several steps) within 150 ms count once (D104 section 15).
    bool WatchEventShouldCoalesce(string key)
    {
        DateTime now = DateTime.UtcNow;
        if (watchLastEventAt.TryGetValue(key, out DateTime last) && (now - last).TotalMilliseconds < 150)
        {
            watchLastEventAt[key] = now;
            return true;
        }
        watchLastEventAt[key] = now;
        return false;
    }

    // Invoke-OtterWatchEventLoopStep: at most one notification per call.
    void WatchStep(TimeSpan timeout)
    {
        if (activeWatchers.Count == 0) return;
        WatchNotification? notification = watchQueue.Take(timeout);
        if (notification == null) return;
        OtterFileWatcher watcher = notification.Watcher;
        if (!watcher.Active) return;
        if (notification.KindWord == "Error")
        {
            CompleteProgressBarLine();
            output.WriteLine($"error: A file watcher stopped working unexpectedly for \"{watcher.Path}\".");
            StopWatcher(watcher);
            return;
        }
        WatchEventKind kind = notification.KindWord switch
        {
            "Changed" => WatchEventKind.Change,
            "Created" => WatchEventKind.Create,
            "Deleted" => WatchEventKind.Delete,
            _ => WatchEventKind.Rename,
        };
        string path = notification.Path ?? "";
        if (WatchEventShouldCoalesce($"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(watcher)}|{notification.KindWord}|{path}")) return;
        if (!watcherHandlers.TryGetValue(watcher, out var all)) return;
        var handlers = all.Where(h => h.Kind == kind).ToList();
        if (handlers.Count == 0) return;
        string kindText = kind switch
        {
            WatchEventKind.Change => "changed",
            WatchEventKind.Create => "created",
            WatchEventKind.Delete => "deleted",
            _ => "renamed",
        };
        var previous = currentWatchEvent;
        currentWatchEvent = new WatchContext(path, Path.GetFileName(path.TrimEnd('/', '\\')), kindText, kind == WatchEventKind.Rename ? notification.OldPath : null);
        try
        {
            foreach (var handler in handlers) Execute(handler.Body, handler.Scope);
        }
        finally
        {
            currentWatchEvent = previous;
        }
    }

    // ---- WebSockets (D106) ----

    void CloseWebSocketNow(OtterWebSocket ws)
    {
        if (ws.Disposed) return;
        ws.Disposed = true;
        try { ws.Cts.Cancel(); } catch (ObjectDisposedException) { }
        try { ws.Native.Dispose(); } catch (Exception e) when (e is not OutOfMemoryException) { }
        ws.MessageBuffer.Dispose();
    }

    static Dictionary<string, object?> CloseContext(double code, string reason, bool clean) =>
        new() { ["CloseCode"] = code, ["CloseReason"] = reason, ["CloseWasClean"] = clean };

    void WebSocketFailed(OtterWebSocket ws, string error)
    {
        ws.State = "closed";
        ws.LastError = error;
        ws.CloseCode = 1006;
        ws.CloseReason = error;
        ws.CloseWasClean = false;
        InvokeNetHandlers(ws, NetworkEventKind.Error, new Dictionary<string, object?> { ["Error"] = error });
        InvokeNetHandlers(ws, NetworkEventKind.Close, CloseContext(1006, error, false));
    }

    // Invoke-OtterWebSocketEventLoopStep: at most one message per socket per call.
    void WebSocketStep()
    {
        foreach (var ws in activeWebSockets.ToArray())
        {
            if (ws.State == "connecting")
            {
                if (ws.ConnectTask is { IsCompleted: true } connect)
                {
                    if (connect.IsFaulted || connect.IsCanceled)
                    {
                        WebSocketFailed(ws, connect.Exception != null ? BaseMessage(connect.Exception) : $"Failed to connect to {ws.Url}");
                    }
                    else
                    {
                        ws.State = "open";
                        if (!string.IsNullOrEmpty(ws.Native.SubProtocol)) ws.Protocol = ws.Native.SubProtocol;
                        InvokeNetHandlers(ws, NetworkEventKind.Open, new Dictionary<string, object?>());
                    }
                }
            }
            else if (ws.State == "open")
            {
                if (ws.Native.State is WebSocketState.CloseReceived or WebSocketState.Closed or WebSocketState.Aborted)
                {
                    ws.State = "closed";
                    double code = ws.Native.CloseStatus is { } status ? (int)status : 1000;
                    string reason = ws.Native.CloseStatusDescription ?? "";
                    ws.CloseCode = code;
                    ws.CloseReason = reason;
                    ws.CloseWasClean = code == 1000;
                    InvokeNetHandlers(ws, NetworkEventKind.Close, CloseContext(code, reason, code == 1000));
                    continue;
                }
                if (ws.ReceiveTask == null)
                {
                    ws.ReceiveBuffer = new byte[65536];
                    ws.ReceiveTask = ws.Native.ReceiveAsync(new ArraySegment<byte>(ws.ReceiveBuffer), ws.Cts.Token);
                }
                if (ws.ReceiveTask.IsCompleted)
                {
                    var task = ws.ReceiveTask;
                    byte[] buffer = ws.ReceiveBuffer!;
                    ws.ReceiveTask = null;
                    if (task.IsFaulted || task.IsCanceled)
                    {
                        WebSocketFailed(ws, task.Exception != null ? BaseMessage(task.Exception) : "WebSocket connection ended unexpectedly");
                        continue;
                    }
                    WebSocketReceiveResult result = task.Result;
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        ws.State = "closed";
                        ws.CloseCode = result.CloseStatus is { } status ? (int)status : 1000;
                        ws.CloseReason = result.CloseStatusDescription ?? "";
                        ws.CloseWasClean = ws.CloseCode == 1000;
                        InvokeNetHandlers(ws, NetworkEventKind.Close, CloseContext(ws.CloseCode, ws.CloseReason, ws.CloseWasClean));
                        continue;
                    }
                    ws.MessageBuffer.Write(buffer, 0, result.Count);
                    ws.MessageBufferType ??= result.MessageType;
                    if (result.EndOfMessage)
                    {
                        object message = ws.MessageBufferType == WebSocketMessageType.Text
                            ? System.Text.Encoding.UTF8.GetString(ws.MessageBuffer.ToArray())
                            : new OtterBytes(ws.MessageBuffer.ToArray());
                        ws.MessageBuffer.SetLength(0);
                        ws.MessageBufferType = null;
                        InvokeNetHandlers(ws, NetworkEventKind.Message, new Dictionary<string, object?> { ["Message"] = message });
                    }
                }
            }
            else if (ws.State == "closing")
            {
                if (ws.Native.State is WebSocketState.Closed or WebSocketState.Aborted or WebSocketState.CloseReceived)
                {
                    ws.State = "closed";
                    InvokeNetHandlers(ws, NetworkEventKind.Close, CloseContext(ws.CloseCode, ws.CloseReason, ws.CloseWasClean));
                    continue;
                }
                if (ws.ReceiveTask == null)
                {
                    ws.ReceiveBuffer = new byte[4096];
                    try { ws.ReceiveTask = ws.Native.ReceiveAsync(new ArraySegment<byte>(ws.ReceiveBuffer), ws.Cts.Token); }
                    catch (Exception e) when (e is WebSocketException or InvalidOperationException or ObjectDisposedException)
                    {
                        ws.ReceiveTask = Task.FromException<WebSocketReceiveResult>(e);
                    }
                }
                if (ws.ReceiveTask.IsCompleted)
                {
                    ws.ReceiveTask = null;
                    ws.State = "closed";
                    InvokeNetHandlers(ws, NetworkEventKind.Close, CloseContext(ws.CloseCode, ws.CloseReason, ws.CloseWasClean));
                }
            }
        }
    }

    // ---- TCP and UDP (D107, D108, D112, D113) ----

    void StopTcpServer(OtterTcpServer server)
    {
        if (server.Disposed || server.State == "stopped") return;
        server.State = "stopped";
        server.Disposed = true;
        try { server.Listener.Stop(); } catch (SocketException) { }
    }

    void CloseNet(object socket)
    {
        switch (socket)
        {
            case OtterTcp tcp:
                if (tcp.Disposed) return;
                tcp.Disposed = true;
                tcp.State = "closed";
                try { tcp.Stream?.Dispose(); } catch (Exception e) when (e is not OutOfMemoryException) { }
                try { tcp.Client.Close(); } catch (Exception e) when (e is not OutOfMemoryException) { }
                return;
            case OtterUdp udp:
                if (udp.Disposed) return;
                udp.Disposed = true;
                udp.State = "closed";
                try { udp.Client.Close(); } catch (Exception e) when (e is not OutOfMemoryException) { }
                return;
        }
    }

    void TcpServerStep(OtterTcpServer server)
    {
        if (server.State != "listening") return;
        try
        {
            server.AcceptTask ??= server.Listener.AcceptTcpClientAsync();
            if (!server.AcceptTask.IsCompleted) return;
            var task = server.AcceptTask;
            server.AcceptTask = null;
            if (server.State != "listening") return;
            if (task.IsFaulted || task.IsCanceled)
            {
                if (server.State != "listening" || server.Disposed) return;
                SendNetError(server, $"The tcp server on port {server.BoundPort} failed: {BaseMessage(task.Exception)}");
                return;
            }
            TcpClient raw = task.Result;
            var remote = raw.Client.RemoteEndPoint as IPEndPoint;
            var connection = new OtterTcp(raw, remote?.Address.ToString() ?? "", remote?.Port ?? 0)
            {
                State = "connected",
                Stream = raw.GetStream(),
                ConnectFired = true,
            };
            activeNet.Add(connection);
            InvokeNetHandlers(server, NetworkEventKind.Connection, new Dictionary<string, object?> { ["IncomingConnection"] = connection });
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException or IOException)
        {
            if (server.State == "listening") SendNetError(server, $"The tcp server on port {server.BoundPort} failed: {BaseMessage(e)}");
        }
    }

    void TcpStep(OtterTcp tcp)
    {
        if (tcp.State == "connecting" && tcp.ConnectTask is { IsCompleted: true } connect)
        {
            if (connect.IsFaulted || connect.IsCanceled)
            {
                string message = $"Could not connect to tcp {tcp.RemoteHost} on port {tcp.RemotePort}: {BaseMessage(connect.Exception)}";
                CloseNet(tcp);
                SendNetError(tcp, message);
            }
            else if (!tcp.IsSecure)
            {
                tcp.Stream = tcp.Client.GetStream();
                tcp.State = "connected";
                InvokeNetHandlers(tcp, NetworkEventKind.Connect, new Dictionary<string, object?>());
            }
            else if (tcp.HandshakeTask == null)
            {
                // D112: the system checks the certificate chain, expiry,
                // revocation and host name; nothing can turn that off.
                try
                {
                    var ssl = new System.Net.Security.SslStream(tcp.Client.GetStream(), false);
                    tcp.Stream = ssl;
                    tcp.HandshakeTask = ssl.AuthenticateAsClientAsync(tcp.ServerName, null, System.Security.Authentication.SslProtocols.None, true);
                }
                catch (Exception e) when (e is not OutOfMemoryException and not OtterError)
                {
                    string message = $"Could not start TLS with {tcp.RemoteHost}: {BaseMessage(e)}";
                    CloseNet(tcp);
                    SendNetError(tcp, message);
                }
            }
            else if (tcp.HandshakeTask.IsCompleted)
            {
                if (tcp.HandshakeTask.IsFaulted || tcp.HandshakeTask.IsCanceled)
                {
                    string message = $"The TLS handshake with {tcp.RemoteHost} failed: {BaseMessage(tcp.HandshakeTask.Exception)}";
                    CloseNet(tcp);
                    SendNetError(tcp, message);
                }
                else
                {
                    tcp.State = "connected";
                    InvokeNetHandlers(tcp, NetworkEventKind.Connect, new Dictionary<string, object?>());
                }
            }
        }
        if (tcp.State == "connected")
        {
            // Only read while something listens; unread data waits in the OS.
            if (!NetHasHandler(tcp, NetworkEventKind.Data) && !NetHasHandler(tcp, NetworkEventKind.Close)) return;
            try
            {
                // Armed in one pass, looked at in the next (otterPS's Step-OtterTcp).
                if (tcp.ReadTask == null)
                {
                    tcp.ReadTask = tcp.Stream!.ReadAsync(tcp.ReadBuffer, 0, tcp.ReadBuffer.Length);
                }
                else if (tcp.ReadTask.IsCompleted)
                {
                    var task = tcp.ReadTask;
                    tcp.ReadTask = null;
                    if (task.IsFaulted || task.IsCanceled)
                    {
                        string message = $"The tcp connection to {tcp.RemoteHost} failed: {BaseMessage(task.Exception)}";
                        CloseNet(tcp);
                        SendNetError(tcp, message);
                    }
                    else if (task.Result == 0) CloseNet(tcp);
                    else
                    {
                        byte[] chunk = tcp.ReadBuffer.AsSpan(0, task.Result).ToArray();
                        InvokeNetHandlers(tcp, NetworkEventKind.Data, new Dictionary<string, object?> { ["Data"] = new OtterBytes(chunk) });
                    }
                }
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                string message = $"The tcp connection to {tcp.RemoteHost} failed: {BaseMessage(e)}";
                CloseNet(tcp);
                SendNetError(tcp, message);
            }
        }
        if (tcp.State == "closed" && !tcp.CloseFired)
        {
            tcp.CloseFired = true;
            InvokeNetHandlers(tcp, NetworkEventKind.Close, new Dictionary<string, object?>());
        }
    }

    void UdpStep(OtterUdp udp)
    {
        if (udp.State == "open")
        {
            if (!NetHasHandler(udp, NetworkEventKind.Data)) return;
            try
            {
                if (udp.ReceiveTask == null) udp.ReceiveTask = udp.Client.ReceiveAsync();
                else if (udp.ReceiveTask.IsCompleted)
                {
                    var task = udp.ReceiveTask;
                    udp.ReceiveTask = null;
                    if (task.IsFaulted || task.IsCanceled)
                    {
                        Exception? inner = task.Exception?.GetBaseException();
                        // Windows reports an ICMP "port unreachable" from an
                        // earlier send on the next receive; not a failure.
                        if (inner is not SocketException { SocketErrorCode: SocketError.ConnectionReset })
                        {
                            SendNetError(udp, $"The udp socket failed: {inner?.Message}");
                        }
                    }
                    else
                    {
                        UdpReceiveResult result = task.Result;
                        InvokeNetHandlers(udp, NetworkEventKind.Data, new Dictionary<string, object?>
                        {
                            ["Data"] = new OtterBytes(result.Buffer),
                            ["SenderAddress"] = result.RemoteEndPoint.Address.ToString(),
                            ["SenderPort"] = (double)result.RemoteEndPoint.Port,
                        });
                    }
                }
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                SendNetError(udp, $"The udp socket failed: {BaseMessage(e)}");
            }
        }
        if (udp.State == "closed" && !udp.CloseFired)
        {
            udp.CloseFired = true;
            InvokeNetHandlers(udp, NetworkEventKind.Close, new Dictionary<string, object?>());
        }
    }

    void NetStep()
    {
        foreach (var server in activeTcpServers.ToArray()) TcpServerStep(server);
        foreach (var net in activeNet.ToArray())
        {
            if (net is OtterTcp tcp) TcpStep(tcp);
            else UdpStep((OtterUdp)net);
        }
    }

    // Test-OtterNetActive
    bool NetActive()
    {
        foreach (var server in activeTcpServers)
        {
            if (server.State == "listening" && HasHandlers(server)) return true;
        }
        foreach (var net in activeNet)
        {
            if (!HasHandlers(net)) continue;
            bool closed, closeFired;
            if (net is OtterTcp tcp) { closed = tcp.State == "closed"; closeFired = tcp.CloseFired; }
            else { var udp = (OtterUdp)net; closed = udp.State == "closed"; closeFired = udp.CloseFired; }
            if (!closed || !closeFired) return true;
        }
        return false;
    }

    // ---- HTTP requests (D116B) ----

    bool HttpActive() => activeHttpRequests.Any(request => request.State == "pending" && HasHandlers(request));

    void FinishHttpRequest(OtterHttpRequest request, NetworkEventKind kind, Dictionary<string, object?> context)
    {
        request.RetainedTerminalEvent = new NetEvent(kind, context);
        InvokeNetHandlers(request, kind, context);
    }

    void FailHttpRequest(OtterHttpRequest request, string error)
    {
        request.State = "failed";
        request.Error = error;
        FinishHttpRequest(request, NetworkEventKind.Error, new Dictionary<string, object?> { ["Error"] = error, ["NetError"] = error });
    }

    void DisposeHttpRequest(OtterHttpRequest request)
    {
        if (request.Disposed) return;
        request.Cts.Dispose();
        request.Disposed = true;
    }

    // Invoke-OtterHttpEventLoopStep: every request that has finished.
    void HttpStep()
    {
        foreach (var request in activeHttpRequests.ToArray())
        {
            if (request.State != "pending") continue;
            if (request.Task == null || !request.Task.IsCompleted)
            {
                if (request.TimeoutSeconds != null && request.Cts.IsCancellationRequested && !request.IsTimeout) request.IsTimeout = true;
                else continue;
            }
            if (request.IsTimeout || (request.TimeoutSeconds != null && request.Cts.IsCancellationRequested))
            {
                FailHttpRequest(request, $"The HTTP request timed out after {OtterHttp.PowerShellNumber(request.TimeoutSeconds ?? 0)} seconds.");
                DisposeHttpRequest(request);
                continue;
            }
            var task = request.Task!;
            if (task.IsFaulted)
            {
                FailHttpRequest(request, task.Exception != null ? BaseMessage(task.Exception) : $"Failed to send HTTP request to {request.Url}");
                DisposeHttpRequest(request);
                continue;
            }
            if (task.IsCanceled)
            {
                request.State = "cancelled";
                FinishHttpRequest(request, NetworkEventKind.Cancel, new Dictionary<string, object?>());
                DisposeHttpRequest(request);
                continue;
            }
            HttpResponseMessage response = task.Result;
            request.Status = (int)response.StatusCode;
            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (request.AsJson)
            {
                object? parsed = null;
                string? jsonError = null;
                try { parsed = FromJson(body, 0); }
                catch (OtterError error) { jsonError = error.Message; }
                if (jsonError != null) FailHttpRequest(request, $"Failed to parse HTTP response as JSON: {jsonError}");
                else
                {
                    request.Response = parsed;
                    request.State = "completed";
                    FinishHttpRequest(request, NetworkEventKind.Complete, new Dictionary<string, object?> { ["Response"] = parsed });
                }
            }
            else
            {
                request.Response = body;
                request.State = "completed";
                FinishHttpRequest(request, NetworkEventKind.Complete, new Dictionary<string, object?> { ["Response"] = body });
            }
            response.Dispose();
            request.Message?.Dispose();
            DisposeHttpRequest(request);
        }
    }

    // ---- D130: loop passes ----

    // The names each loop pass and handler run owns (LoopPassAnalysis.Compute),
    // worked out once when the program starts.
    Dictionary<Node, HashSet<string>> loopPassNames = new(ReferenceEqualityComparer.Instance);

    sealed class LoopPass(Scope scope, HashSet<string>? own)
    {
        public Scope Scope { get; } = scope;
        public HashSet<string>? Own { get; } = own;
    }

    // Only a loop that sets up a handler has names of its own per pass.
    bool LoopHasPasses(Stmt loop) => loopPassNames.TryGetValue(loop, out var names) && names.Count > 0;

    // New-OtterLoopPass: a scope for one pass, starting from what the last
    // pass (or an earlier run of the loop) left.
    LoopPass NewLoopPass(Stmt loop, Scope scope, LoopPass? previous)
    {
        if (!loopPassNames.TryGetValue(loop, out var names) || names.Count == 0) return new LoopPass(scope, null);
        var pass = new Scope(scope);
        var own = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (previous != null && previous.Scope.TryGetLocal(name, out object? carried))
            {
                pass.SetLocal(name, carried);
                own.Add(name);
            }
            else if (scope.TryGetLocal(name, out object? left))
            {
                // Left by an earlier run of this loop in the same scope.
                pass.SetLocal(name, left);
                own.Add(name);
            }
            else if (!scope.Has(name)) own.Add(name);
            // Otherwise it lives outside this scope (a global seen from a
            // function): shared, so assignments keep reaching it.
        }
        return new LoopPass(pass, own);
    }

    static void SetLoopVariable(LoopPass pass, Scope scope, string name, object? value)
    {
        if (pass.Own != null && pass.Own.Contains(name)) pass.Scope.SetLocal(name, value);
        else scope.SetLocal(name, value);
    }

    // Complete-OtterLoopPasses: what the last pass set is what the program sees after the loop.
    static void CompleteLoopPasses(Scope scope, LoopPass? last)
    {
        if (last?.Own == null) return;
        foreach (var (name, value) in last.Scope.Locals.ToArray()) scope.SetLocal(name, value);
    }

    // The loops of D130, as otterPS's Invoke-OtterStatement runs them when
    // the loop has pass names.
    void RunWhileWithPasses(WhileStmt loop, Scope scope)
    {
        LoopPass? last = null;
        try
        {
            while (true)
            {
                // The condition sees what the last pass set.
                if (!Values.IsTruthy(Evaluate(loop.Condition, last?.Scope ?? scope))) break;
                last = NewLoopPass(loop, scope, last);
                Execute(loop.Body, last.Scope);
            }
        }
        finally { CompleteLoopPasses(scope, last); }
    }

    void RunRepeatWithPasses(RepeatStmt loop, int times, Scope scope)
    {
        LoopPass? last = null;
        try
        {
            for (int i = 0; i < times; i++)
            {
                last = NewLoopPass(loop, scope, last);
                Execute(loop.Body, last.Scope);
            }
        }
        finally { CompleteLoopPasses(scope, last); }
    }

    void RunCountWithPasses(CountStmt loop, double from, double to, Scope scope)
    {
        int step = from <= to ? 1 : -1;
        LoopPass? last = null;
        try
        {
            for (double n = from; (step > 0 && n <= to) || (step < 0 && n >= to); n += step)
            {
                last = NewLoopPass(loop, scope, last);
                SetLoopVariable(last, scope, loop.VariableName, n);
                Execute(loop.Body, last.Scope);
            }
        }
        finally { CompleteLoopPasses(scope, last); }
    }

    void RunForEachWithPasses(ForEachStmt loop, object?[] items, Scope scope)
    {
        LoopPass? last = null;
        try
        {
            foreach (object? item in items)
            {
                last = NewLoopPass(loop, scope, last);
                SetLoopVariable(last, scope, loop.VariableName, item);
                Execute(loop.Body, last.Scope);
            }
        }
        finally { CompleteLoopPasses(scope, last); }
    }

    // ---- D133: handler runs ----

    // The names of the `when` handler runs in progress, innermost last. A
    // handler only has run names when its body sets up another handler.
    readonly List<HashSet<string>> handlerRunStack = new();

    // Enter-OtterHandlerRun: call before running a `when` handler's body
    // (the UI runtime does), and ExitHandlerRun with the result after it.
    public bool EnterHandlerRun(Stmt handler)
    {
        if (!loopPassNames.TryGetValue(handler, out var names) || names.Count == 0) return false;
        handlerRunStack.Add(names);
        return true;
    }

    public void ExitHandlerRun(bool entered)
    {
        if (entered) handlerRunStack.RemoveAt(handlerRunStack.Count - 1);
    }

    // New-OtterHandlerCaptureEnvironment: a `when` handler set up during a
    // run gets a scope holding a copy of the run's names as they are now;
    // with no run in progress it uses the scope as it is.
    public Scope NewHandlerCaptureScope(Scope scope)
    {
        if (handlerRunStack.Count == 0) return scope;
        var capture = new Scope(scope);
        bool any = false;
        foreach (var names in handlerRunStack)
        {
            foreach (string name in names)
            {
                if (capture.HasLocal(name) || !scope.Has(name)) continue;
                capture.SetLocal(name, scope.Get(name));
                any = true;
            }
        }
        return any ? capture : scope;
    }
}
