using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Otter.Language;

namespace Otter.Runtime;

// The runtime for networking and events: HTTP, downloads, servers and routes,
// WebSockets, TCP, UDP, file watching. A port of the matching cases of
// otterPS's Invoke-OtterStatement and Get-OtterValue, with the same messages.
// The event loop that runs the handlers is EventLoop.cs.
public sealed partial class Interpreter
{
    bool TryExecuteNetwork(Stmt statement, Scope scope)
    {
        if (TryExecuteJob(statement, scope)) return true;   // command jobs (Jobs.cs)
        switch (statement)
        {
            // ---- HTTP (D49, D96, D101, D116A, D116B) ----
            case HttpGetStmt get:
            {
                string url = Values.Format(Evaluate(get.Url, scope));
                var options = HttpOptionsArguments(get.Options, scope);
                object? result = Net(() => OtterHttp.Send("GET", url, null, get.AsJson, options.Headers, options.WithCookies, options.FollowRedirects,
                    options.Timeout, JsonForHttp, text => FromJson(text, get.Line)), get.Line);
                scope.Set(get.Target, result);
                return true;
            }
            case HttpSendStmt send:
            {
                string url = Values.Format(Evaluate(send.Url, scope));
                object? data = Evaluate(send.Data, scope);
                var options = HttpOptionsArguments(send.Options, scope);
                object? result = Net(() => OtterHttp.Send(send.Method, url, data, send.AsJson, options.Headers, options.WithCookies, options.FollowRedirects,
                    options.Timeout, JsonForHttp, text => FromJson(text, send.Line)), send.Line);
                if (!string.IsNullOrEmpty(send.Target)) scope.Set(send.Target, result);
                return true;
            }
            case HttpDeleteStmt delete:
            {
                string url = Values.Format(Evaluate(delete.Url, scope));
                var options = HttpOptionsArguments(delete.Options, scope);
                object? result = Net(() => OtterHttp.Send("DELETE", url, null, false, options.Headers, options.WithCookies, options.FollowRedirects,
                    options.Timeout, JsonForHttp, text => FromJson(text, delete.Line)), delete.Line);
                if (!string.IsNullOrEmpty(delete.Target)) scope.Set(delete.Target, result);
                return true;
            }
            case DownloadFileStmt download:
            {
                string url = Values.Format(Evaluate(download.Url, scope));
                string path = PathArgument(download.Path, scope);
                Net(() => { OtterHttp.Download(url, path); return true; }, download.Line);
                return true;
            }
            case HttpStartStmt start:
            {
                string url = Values.Format(Evaluate(start.Url, scope));
                object? data = start.Data != null ? Evaluate(start.Data, scope) : null;
                var options = HttpOptionsArguments(start.Options, scope);
                OtterHttpRequest request = Net(() => OtterHttp.Start(start.Method, url, data, start.AsJson, options.Headers, options.WithCookies,
                    options.FollowRedirects, options.Timeout, JsonForHttp), start.Line);
                activeHttpRequests.Add(request);
                scope.Set(start.TargetName, request);
                return true;
            }
            case HttpCancelStmt cancel:
            {
                object? value = Evaluate(cancel.Request, scope);
                // A command job (D119-R2) is cancelled by the system area.
                if (value is OtterCommandJob job) { CancelJob(job); return true; }
                if (value is IOtterHandlerTarget other) { other.Cancel(cancel.Line); return true; }
                if (value is not OtterHttpRequest request) throw RuntimeError("cancel requires an HTTP request.", cancel.Line);
                if (request.State != "pending") return true;
                request.State = "cancelled";
                try { request.Cts.Cancel(); } catch (ObjectDisposedException) { }
                var context = new Dictionary<string, object?>();
                request.RetainedTerminalEvent = new NetEvent(NetworkEventKind.Cancel, context);
                InvokeNetHandlers(request, NetworkEventKind.Cancel, context);
                return true;
            }

            // ---- servers and routes (D51), await (D56): `otter run` has no
            // case for them, so they stop with the interpreter's own message.
            case WebRouteStmt route: throw NotRunnable("WebRoute", route.Line);
            case RespondStmt respond: throw NotRunnable("Respond", respond.Line);
            case StartServerStmt startServer: throw NotRunnable("StartServer", startServer.Line);
            case ListenServerStmt listen: throw NotRunnable("ListenServer", listen.Line);
            case AwaitStmt awaitStatement: throw NotRunnable("Await", awaitStatement.Line);

            // ---- file watching (D104) ----
            case FileWatchStmt watch:
            {
                string path = Values.Format(Evaluate(watch.Path, scope));
                scope.Set(watch.Target, NewFileWatcher(path, watch.TargetKind == WatchKind.Folder, watch.Recursive, watch.Line));
                return true;
            }
            case StopWatchingStmt stop:
            {
                object? value = Evaluate(stop.Watcher, scope);
                if (value is not OtterFileWatcher watcher)
                {
                    throw RuntimeError($"I can only stop watching a file watcher, but this is {Values.TypeName(value)}.", stop.Line);
                }
                StopWatcher(watcher);
                return true;
            }
            case WatchEventStmt watchEvent:
            {
                object? value = Evaluate(watchEvent.Watcher, scope);
                if (value is not OtterFileWatcher watcher)
                {
                    throw RuntimeError($"I can only listen for a watcher event on a file watcher, but this is {Values.TypeName(value)}.", watchEvent.Line);
                }
                if (!watcherHandlers.TryGetValue(watcher, out var handlers)) watcherHandlers[watcher] = handlers = new List<WatchHandler>();
                handlers.Add(new WatchHandler(watchEvent.EventKind, watchEvent.Body, scope));
                return true;
            }

            // ---- WebSockets (D106) ----
            case WebSocketConnectStmt connect:
                ConnectWebSocket(connect, scope);
                return true;
            case WebSocketSendStmt send:
                SendThrough(send, scope);
                return true;
            case WebSocketCloseStmt close:
                CloseWebSocket(close, scope);
                return true;
            case NetworkEventStmt networkEvent:
                AddNetworkHandler(networkEvent, scope);
                return true;

            // ---- TCP and UDP (D107, D108, D112, D113) ----
            case TcpConnectStmt tcpConnect:
                ConnectTcp(tcpConnect, scope);
                return true;
            case UdpOpenStmt udpOpen:
            {
                int localPort = 0;
                if (udpOpen.Port != null) localPort = NetPort(Evaluate(udpOpen.Port, scope), udpOpen.Line, allowZero: true);
                UdpClient client;
                try { client = new UdpClient(localPort, AddressFamily.InterNetwork); }
                catch (SocketException e)
                {
                    throw RuntimeError($"I could not open a udp socket on port {localPort}: {e.Message}", udpOpen.Line);
                }
                var udp = new OtterUdp(client, ((IPEndPoint)client.Client.LocalEndPoint!).Port);
                activeNet.Add(udp);
                scope.Set(udpOpen.Target, udp);
                return true;
            }
            case UdpSendStmt udpSend:
                SendDatagram(udpSend, scope);
                return true;
            case NetCloseStmt netClose:
            {
                object? value = Evaluate(netClose.Socket, scope);
                bool tcp = Same(netClose.Protocol, "tcp");
                if (tcp ? value is not OtterTcp : value is not OtterUdp)
                {
                    throw RuntimeError($"I can only close {(tcp ? "a tcp connection" : "a udp socket")} here, but this is {Values.TypeName(value)}.", netClose.Line,
                        $"close {netClose.Protocol} <name>");
                }
                CloseNet(value!);
                return true;
            }
            case TcpListenStmt listen:
                ListenTcp(listen, scope);
                return true;
            case TcpStopStmt stop:
            {
                object? value = Evaluate(stop.Server, scope);
                if (value is not OtterTcpServer server)
                {
                    throw RuntimeError($"I can only stop a tcp server, but this is {Values.TypeName(value)}.", stop.Line, "stop tcp server");
                }
                StopTcpServer(server);
                return true;
            }
        }
        return false;
    }

    bool TryEvaluateNetwork(Expr expression, Scope scope, out object? value)
    {
        if (TryEvaluateJob(expression, out value)) return true;   // command jobs (Jobs.cs)
        switch (expression)
        {
            case AwaitExpr awaitExpr:
                throw RuntimeError("'await' is not supported in Otter 1.0.", awaitExpr.Line);
            // ---- HTTP requests (D116B) ----
            case HttpRequestIsStateExpr isState:
            {
                object? target = Evaluate(isState.Request, scope);
                if (target is OtterCommandJob job)
                {
                    value = job.State == isState.State switch
                    {
                        HttpRequestState.Pending => "running",
                        HttpRequestState.Completed => "completed",
                        HttpRequestState.Failed => "failed",
                        _ => "cancelled",
                    };
                    return true;
                }
                if (target is IOtterHandlerTarget other) { value = other.IsInState(isState.State); return true; }
                if (target is not OtterHttpRequest request)
                {
                    throw RuntimeError($"I can only check the state of an HTTP request or command job, but got {Values.TypeName(target)}.", isState.Line);
                }
                string wanted = isState.State switch
                {
                    HttpRequestState.Pending => "pending",
                    HttpRequestState.Completed => "completed",
                    HttpRequestState.Failed => "failed",
                    _ => "cancelled",
                };
                value = request.State == wanted;
                return true;
            }
            case ReceivedResponseExpr received:
                value = Ambient("Response", "\"received response\" is only available inside \"on complete of ...\".", received.Line);
                return true;
            // ---- WebSockets (D106) ----
            case ReceivedMessageExpr message:
                value = Ambient("Message", "\"received message\" is only available inside \"on message from ...\".", message.Line);
                return true;
            case CloseCodeExpr closeCode:
                value = Ambient("CloseCode", "\"close code\" is only available inside \"on close of ...\".", closeCode.Line);
                return true;
            case CloseReasonExpr closeReason:
                value = Ambient("CloseReason", "\"close reason\" is only available inside \"on close of ...\".", closeReason.Line);
                return true;
            case CloseWasCleanExpr clean:
                value = Ambient("CloseWasClean", "\"close was clean\" is only available inside \"on close of ...\".", clean.Line);
                return true;
            case WebSocketErrorExpr error:
                value = Ambient("Error", "\"websocket error\" is only available inside \"on error of ...\".", error.Line);
                return true;
            case WebSocketIsStateExpr isState:
            {
                object? target = Evaluate(isState.Socket, scope);
                string? state = target switch
                {
                    OtterWebSocket ws => ws.State,
                    OtterTcp tcp => tcp.State,
                    OtterUdp udp => udp.State,
                    _ => throw RuntimeError($"I can only ask about the state of a websocket, tcp connection or udp socket, but this is {Values.TypeName(target)}.", isState.Line),
                };
                value = state == isState.State.ToString().ToLowerInvariant();
                return true;
            }
            // ---- TCP and UDP (D107, D108, D112, D113) ----
            case NetContextExpr context:
            {
                (string key, string where) = context.Field switch
                {
                    "data" => ("Data", "on data from ..."),
                    "sender address" => ("SenderAddress", "a udp \"on data from ...\""),
                    "sender port" => ("SenderPort", "a udp \"on data from ...\""),
                    "network error" => ("NetError", "on error of ..."),
                    _ => ("IncomingConnection", "on connection to ..."),
                };
                string shown = context.Field == "data" ? "received data" : context.Field;
                value = Ambient(key, $"\"{shown}\" is only available inside {where}.", context.Line);
                return true;
            }
            case ConnectionIsSecureExpr secure:
            {
                object? target = Evaluate(secure.Connection, scope);
                if (target is not OtterTcp tcp) throw RuntimeError($"I can only ask whether a tcp connection is secure, but this is {Values.TypeName(target)}.", secure.Line);
                value = tcp.IsSecure && tcp.State == "connected";
                return true;
            }
            case TcpServerIsStateExpr serverState:
            {
                object? target = Evaluate(serverState.Server, scope);
                if (target is not OtterTcpServer server) throw RuntimeError($"I can only ask about the state of a tcp server, but this is {Values.TypeName(target)}.", serverState.Line);
                value = server.State == (serverState.State == TcpServerState.Listening ? "listening" : "stopped");
                return true;
            }
            // ---- file watching (D104) ----
            case IsWatchingExpr isWatching:
            {
                object? target = Evaluate(isWatching.Watcher, scope);
                if (target is not OtterFileWatcher watcher) throw RuntimeError($"I can only ask whether a file watcher is watching, but this is {Values.TypeName(target)}.", isWatching.Line);
                value = watcher.Active;
                return true;
            }
            case ChangedPathExpr changedPath:
                value = WatchContextOrError("\"changed path\"", changedPath.Line).Path;
                return true;
            case ChangedFileNameExpr changedFileName:
                value = WatchContextOrError("\"changed file name\"", changedFileName.Line).FileName;
                return true;
            case ChangeKindExpr changeKind:
                value = WatchContextOrError("\"change kind\"", changeKind.Line).Kind;
                return true;
            case OldPathExpr oldPath:
            {
                WatchContext context = WatchContextOrError("\"old path\"", oldPath.Line);
                if (context.OldPath == null)
                {
                    throw RuntimeError("\"old path\" is only available inside \"on rename in ...\" - this is a different kind of watcher event.", oldPath.Line);
                }
                value = context.OldPath;
                return true;
            }
        }
        return false;
    }

    // `X of Y` for the values of this area (otterPS's PropertyAccess, in its
    // order: http request, `count of` bytes, tcp connection, tcp server, udp
    // socket, websocket). False when the target is none of them.
    bool TryNetworkProperty(object? target, PropertyAccessExpr property, out object? value)
    {
        if (TryJobProperty(target, property, out value)) return true;   // Jobs.cs
        string name = property.Property;
        int line = property.Line;
        switch (target)
        {
            case OtterHttpRequest request:
                switch (name.ToLowerInvariant())
                {
                    case "state":
                        value = request.State;
                        return true;
                    case "response":
                        if (request.State == "pending") throw RuntimeError("The HTTP request has not completed yet.", line);
                        if (request.State == "cancelled") throw RuntimeError("The HTTP request was cancelled.", line);
                        if (request.State == "failed") throw RuntimeError($"The HTTP request failed: {request.Error}", line);
                        value = request.Response;
                        return true;
                    case "error":
                        value = request.State == "failed" && request.Error != null ? request.Error : null;
                        return true;
                    case "status":
                        if (request.Status <= 0 && request.State == "pending") throw RuntimeError("The HTTP request has not completed yet.", line);
                        value = (double)request.Status;
                        return true;
                    default:
                        throw RuntimeError($"This http request has no property called \"{name}\". Try state, response, error, or status.", line);
                }
            // D107: count of data (length of stays the D102 spelling).
            case OtterBytes bytes when Same(name, "count"):
                value = (double)bytes.Value.Length;
                return true;
            case OtterTcp tcp:
                switch (name.ToLowerInvariant())
                {
                    case "state":
                        value = tcp.State;
                        return true;
                    case "remote address":
                        value = tcp.State == "connected" && SafeEndPoint(() => tcp.Client.Client.RemoteEndPoint) is IPEndPoint remote ? remote.Address.ToString() : tcp.RemoteHost;
                        return true;
                    case "remote port":
                        value = (double)tcp.RemotePort;
                        return true;
                    case "local address":
                        value = tcp.State == "connected" && SafeEndPoint(() => tcp.Client.Client.LocalEndPoint) is IPEndPoint localAddress ? localAddress.Address.ToString() : "127.0.0.1";
                        return true;
                    case "local port":
                        value = tcp.State == "connected" && SafeEndPoint(() => tcp.Client.Client.LocalEndPoint) is IPEndPoint localPort ? (double)localPort.Port : 0.0;
                        return true;
                    case "tls version":
                    {
                        if (!tcp.IsSecure || tcp.State != "connected")
                        {
                            throw RuntimeError("This connection is not secure (or not connected yet), so it has no TLS version.", line,
                                "connect securely to tcp \"example.com\" on port 443 and call it connection");
                        }
                        string protocol = tcp.Stream is System.Net.Security.SslStream ssl ? ssl.SslProtocol.ToString() : "";
                        if (protocol.Length == 5 && protocol.StartsWith("Tls", StringComparison.Ordinal) && char.IsDigit(protocol[3]) && char.IsDigit(protocol[4]))
                        {
                            protocol = $"TLS {protocol[3]}.{protocol[4]}";
                        }
                        else if (protocol == "Tls") protocol = "TLS 1.0";
                        value = protocol;
                        return true;
                    }
                    case "tls protocol":
                        // No ALPN on Windows PowerShell 5.1, so no protocol is ever negotiated.
                        if (!tcp.IsSecure || tcp.State != "connected")
                        {
                            throw RuntimeError("This connection is not secure (or not connected yet), so it has no negotiated protocol.", line);
                        }
                        value = "";
                        return true;
                    default:
                        throw RuntimeError($"A tcp connection has no property called \"{name}\". Try state, remote address, remote port, local address, local port, tls version, or tls protocol.", line);
                }
            case OtterTcpServer server:
                switch (name.ToLowerInvariant())
                {
                    case "state": value = server.State; return true;
                    case "local address": value = server.BoundAddress; return true;
                    case "local port": value = (double)server.BoundPort; return true;
                    default: throw RuntimeError($"A tcp server has no property called \"{name}\". Try state, local address, or local port.", line);
                }
            case OtterUdp udp:
                switch (name.ToLowerInvariant())
                {
                    case "state": value = udp.State; return true;
                    case "port": value = (double)udp.LocalPort; return true;
                    default: throw RuntimeError($"A udp socket has no property called \"{name}\". Try state or port.", line);
                }
            case OtterWebSocket ws:
                switch (name.ToLowerInvariant())
                {
                    case "state": value = ws.State; return true;
                    case "url": value = ws.Url; return true;
                    case "protocol": value = ws.Protocol; return true;
                    default: throw RuntimeError($"A websocket has no property called \"{name}\". Try state, url, or protocol.", line);
                }
        }
        return false;
    }

    // For another area's handlers (IOtterHandlerTarget, IOtterEventSource):
    // runs a handler body the way this area runs its own.
    public void RunHandlerBody(IReadOnlyList<Stmt> body, Scope scope) => Execute(body, scope);

    static EndPoint? SafeEndPoint(Func<EndPoint?> read)
    {
        try { return read(); }
        catch (Exception e) when (e is SocketException or ObjectDisposedException) { return null; }
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    OtterError NotRunnable(string kind, int line) => RuntimeError($"I do not know how to run a {kind} statement yet.", line);

    // The library's problems become runtime errors on the statement's line.
    T Net<T>(Func<T> work, int line)
    {
        try { return work(); }
        catch (NetProblem problem) { throw RuntimeError(problem.Message, line); }
    }

    // ConvertTo-OtterJsonText, for a request body.
    static string? JsonForHttp(object? data)
    {
        try { return OtterJson.Write(data); }
        catch (OtterJson.NotData) { throw new NetProblem("Otter cannot turn something it can do into JSON."); }
    }

    sealed record HttpArguments(List<HttpHeader> Headers, bool? WithCookies, bool? FollowRedirects, object? Timeout);

    // Get-OtterHttpOptionsArguments: header names and values as text, the
    // timeout as whatever value it is (checked by the library).
    HttpArguments HttpOptionsArguments(HttpOptions? options, Scope scope)
    {
        var headers = new List<HttpHeader>();
        if (options == null) return new HttpArguments(headers, null, null, null);
        foreach (var header in options.Headers)
        {
            string name = Values.Format(Evaluate(header.Name, scope));
            string value = Values.Format(Evaluate(header.Value, scope));
            headers.Add(new HttpHeader(name, value));
        }
        object? timeout = options.TimeoutSeconds != null ? Evaluate(options.TimeoutSeconds, scope) : null;
        return new HttpArguments(headers, options.WithCookies, options.FollowRedirects, timeout);
    }

    // The values the handler that is running was given (received message,
    // close code ...); outside such a handler, an error naming where it works.
    object? Ambient(string key, string message, int line)
    {
        if (currentNetContext == null || !currentNetContext.TryGetValue(key, out object? value)) throw RuntimeError(message, line);
        return value;
    }

    WatchContext WatchContextOrError(string what, int line) =>
        currentWatchEvent ?? throw RuntimeError($"{what} only means something inside a watch-event handler (on change/create/delete/rename).", line);

    // Get-OtterNetPort
    int NetPort(object? value, int line, bool allowZero = false)
    {
        int min = allowZero ? 0 : 1;
        if (value is not double number || number != Math.Floor(number) || number < min || number > 65535)
        {
            throw RuntimeError($"I need a whole-number port between {min} and 65535, but this is {Values.Format(value)}.", line);
        }
        return (int)number;
    }

    // Assert-OtterNetBytes: TCP and UDP never turn text into bytes by themselves (D107).
    OtterBytes NetBytes(object? value, int line, string where) =>
        value as OtterBytes ?? throw RuntimeError($"I can only send bytes through {where}, but this is {Values.TypeName(value)}. Otter never converts text to bytes silently.", line,
            "send bytes from text \"Hello\" through connection");

    void ConnectWebSocket(WebSocketConnectStmt connect, Scope scope)
    {
        const string suggestion = "connect to websocket \"wss://example.com/chat\" and call it socket";
        object? rawUrl = Evaluate(connect.Url, scope);
        if (rawUrl is not string urlText) throw RuntimeError($"I need text for a websocket URL, but this is {Values.TypeName(rawUrl)}.", connect.Line, suggestion);
        string url = urlText.Trim();
        if (!url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            throw RuntimeError($"I need a websocket URL starting with 'ws://' or 'wss://', but this was '{url}'.", connect.Line, suggestion);
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) throw RuntimeError($"The websocket URL '{url}' is not a valid web address.", connect.Line);
        string? protocol = null;
        if (connect.Protocol != null)
        {
            object? protocolValue = Evaluate(connect.Protocol, scope);
            if (protocolValue is not string protocolText) throw RuntimeError($"I need text for a websocket protocol, but this is {Values.TypeName(protocolValue)}.", connect.Line);
            protocol = protocolText;
        }
        var native = new ClientWebSocket();
        if (!string.IsNullOrEmpty(protocol))
        {
            try { native.Options.AddSubProtocol(protocol); }
            catch (ArgumentException e) { throw new InternalProblem($"Exception calling \"AddSubProtocol\" with \"1\" argument(s): \"{e.Message}\""); }
        }
        var ws = new OtterWebSocket(native, url, protocol ?? "");
        activeWebSockets.Add(ws);
        scope.Set(connect.Target, ws);
        try { ws.ConnectTask = native.ConnectAsync(uri, ws.Cts.Token); }
        catch (Exception e) when (e is WebSocketException or ArgumentException or InvalidOperationException)
        {
            ws.State = "closed";
            ws.LastError = BaseMessage(e);
        }
    }

    // send X through a websocket, or bytes through a tcp connection.
    void SendThrough(WebSocketSendStmt send, Scope scope)
    {
        object? target = Evaluate(send.Socket, scope);
        if (target is OtterTcp tcp)
        {
            if (tcp.State != "connected")
            {
                throw RuntimeError($"I cannot send through a tcp connection that is not connected (its state is '{tcp.State}').", send.Line,
                    "Send from inside \"on connect of <connection>\".");
            }
            OtterBytes data = NetBytes(Evaluate(send.Message, scope), send.Line, "a tcp connection");
            try
            {
                tcp.Stream!.Write(data.Value, 0, data.Value.Length);
                tcp.Stream.Flush();
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                string failure = $"I could not send through the tcp connection: {BaseMessage(e)}";
                CloseNet(tcp);
                throw RuntimeError(failure, send.Line);
            }
            return;
        }
        if (target is OtterUdp) throw RuntimeError("A udp send needs a destination.", send.Line, "send data through socket to \"127.0.0.1\" on port 9000");
        if (target is not OtterWebSocket ws) throw RuntimeError($"I can only send through a websocket, but this is {Values.TypeName(target)}.", send.Line, "send \"Hello\" through socket");
        if (ws.State != "open")
        {
            throw RuntimeError($"I cannot send through a websocket that is not open (its state is '{ws.State}').", send.Line,
                $"Wait for \"on open of {SocketExpressionText(send.Socket)}\" before sending.");
        }
        object? message = Evaluate(send.Message, scope);
        byte[] bytes;
        WebSocketMessageType type;
        if (message is string text)
        {
            bytes = System.Text.Encoding.UTF8.GetBytes(text);
            type = WebSocketMessageType.Text;
        }
        else if (message is OtterBytes binary)
        {
            bytes = binary.Value;
            type = WebSocketMessageType.Binary;
        }
        else throw RuntimeError($"I can only send text or bytes through a websocket, but this is {Values.TypeName(message)}.", send.Line, "send \"Hello\" through socket");
        try { ws.Native.SendAsync(new ArraySegment<byte>(bytes), type, true, ws.Cts.Token).GetAwaiter().GetResult(); }
        catch (Exception e) when (e is WebSocketException or IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
        {
            throw RuntimeError($"Failed to send through websocket: {BaseMessage(e)}", send.Line);
        }
    }

    // otterPS puts the socket's syntax node into its suggestion text, which
    // PowerShell shows as the node's ToString(): "Variable@L<line>" for a name.
    static string SocketExpressionText(Expr socket) => socket switch
    {
        VariableExpr => $"Variable@L{socket.Line}",
        PropertyAccessExpr => $"PropertyAccess@L{socket.Line}",
        _ => $"{socket.GetType().Name.Replace("Expr", "")}@L{socket.Line}",
    };

    // The values System.Net.WebSockets.WebSocketCloseStatus names.
    static readonly HashSet<int> DefinedCloseStatuses = new() { 1000, 1001, 1002, 1003, 1005, 1007, 1008, 1009, 1010, 1011 };

    void CloseWebSocket(WebSocketCloseStmt close, Scope scope)
    {
        object? target = Evaluate(close.Socket, scope);
        if (target is not OtterWebSocket ws) throw RuntimeError($"I can only close a websocket, but this is {Values.TypeName(target)}.", close.Line, "close websocket socket");
        int code = 1000;
        if (close.Code != null)
        {
            object? codeValue = Evaluate(close.Code, scope);
            if (!Values.TryNumber(codeValue, out double number))
            {
                throw RuntimeError($"I need a number for a close code, but this is {Values.TypeName(codeValue)}.", close.Line);
            }
            // [int](ConvertTo-OtterNumber $cVal): PowerShell's own error beyond Int32.
            if (!PowerShellInt32.TryCast(number, out code, out string castError)) throw new InternalProblem(castError);
        }
        string reason = "";
        if (close.Reason != null)
        {
            object? reasonValue = Evaluate(close.Reason, scope);
            if (reasonValue is not string reasonText) throw RuntimeError($"I need text for a close reason, but this is {Values.TypeName(reasonValue)}.", close.Line);
            reason = reasonText;
        }
        if (ws.State is "connecting" or "open")
        {
            ws.State = "closing";
            ws.CloseCode = code;
            ws.CloseReason = reason;
            ws.CloseWasClean = code == 1000;
            // [System.Net.WebSockets.WebSocketCloseStatus]$code inside otterPS's
            // try: PowerShell refuses a number the enum does not name (an
            // application code such as 4000), and the catch marks the socket
            // closed without sending anything or running `on close`.
            if (!DefinedCloseStatuses.Contains(code))
            {
                ws.State = "closed";
                return;
            }
            try { _ = ws.Native.CloseOutputAsync((WebSocketCloseStatus)code, reason, ws.Cts.Token); }
            catch (Exception e) when (e is WebSocketException or ArgumentException or InvalidOperationException or ObjectDisposedException)
            {
                ws.State = "closed";
            }
        }
    }

    // on open/message/close/error/connect/data/connection/complete/cancel of ...
    void AddNetworkHandler(NetworkEventStmt networkEvent, Scope scope)
    {
        object? target = Evaluate(networkEvent.Socket, scope);
        NetworkEventKind kind = networkEvent.EventKind;
        // A command job (D119-R2) keeps its own handlers.
        if (target is OtterCommandJob job)
        {
            AddJobNetworkHandler(job, networkEvent, scope);
            return;
        }
        if (target is IOtterHandlerTarget other)
        {
            other.AddHandler(kind, networkEvent.Body, scope, networkEvent.Line);
            return;
        }
        if (target is OtterHttpRequest request)
        {
            if (kind is not (NetworkEventKind.Complete or NetworkEventKind.Error or NetworkEventKind.Cancel))
            {
                throw RuntimeError("An http request only supports \"on complete of\", \"on error of\", or \"on cancel of\".", networkEvent.Line);
            }
            var handler = new NetHandler(kind, networkEvent.Body, scope);
            if (!netHandlers.TryGetValue(request, out var list)) netHandlers[request] = list = new List<NetHandler>();
            list.Add(handler);
            // D116B section 30: a request that already finished hands its
            // outcome to a handler set up afterwards.
            if (request.RetainedTerminalEvent is { } retained && retained.Kind == kind)
            {
                handler.Fired = true;
                var previous = currentNetContext;
                currentNetContext = retained.Context;
                try { Execute(networkEvent.Body, scope); }
                finally { currentNetContext = previous; }
            }
            return;
        }
        if (target is not (OtterWebSocket or OtterTcp or OtterUdp or OtterTcpServer))
        {
            throw RuntimeError($"I can only listen for a network event on a websocket, tcp connection, tcp server, udp socket, http request or command job, but this is {Values.TypeName(target)}.",
                networkEvent.Line);
        }
        if (target is OtterTcpServer)
        {
            if (kind is not (NetworkEventKind.Connection or NetworkEventKind.Error))
            {
                throw RuntimeError("A tcp server only supports \"on connection to\" or \"on error of\".", networkEvent.Line);
            }
        }
        else if (kind == NetworkEventKind.Connection)
        {
            throw RuntimeError($"\"on connection to\" is only valid for a tcp server, but this is {Values.TypeName(target)}.", networkEvent.Line);
        }
        if (!netHandlers.TryGetValue(target, out var handlers)) netHandlers[target] = handlers = new List<NetHandler>();
        handlers.Add(new NetHandler(kind, networkEvent.Body, scope));
    }

    void ConnectTcp(TcpConnectStmt connect, Scope scope)
    {
        object? hostValue = Evaluate(connect.HostExpr, scope);
        object? portValue = Evaluate(connect.Port, scope);
        if (hostValue is not string host || string.IsNullOrWhiteSpace(host))
        {
            throw RuntimeError($"I need text for a tcp host name or address, but this is {Values.TypeName(hostValue)}.", connect.Line,
                "connect to tcp \"localhost\" on port 9000 and call it connection");
        }
        int port = NetPort(portValue, connect.Line);
        // D112: TLS options are checked before any socket exists.
        string serverName = host;
        if (!connect.IsSecure && (connect.ServerName != null || connect.Protocols != null))
        {
            throw RuntimeError("\"for server\" and \"using protocol\" only apply to a secure connection.", connect.Line,
                "connect securely to tcp \"example.com\" on port 443 and call it connection");
        }
        if (connect.ServerName != null)
        {
            object? nameValue = Evaluate(connect.ServerName, scope);
            if (nameValue is not string name || string.IsNullOrWhiteSpace(name))
            {
                throw RuntimeError($"I need text for the tls server name, but this is {Values.TypeName(nameValue)}.", connect.Line);
            }
            serverName = name;
        }
        if (connect.Protocols != null)
        {
            object? requested = Evaluate(connect.Protocols, scope);
            IEnumerable<object?> protocols = requested is OtterList list ? list : new[] { requested };
            foreach (object? protocol in protocols)
            {
                if (protocol is not string text || string.IsNullOrWhiteSpace(text))
                {
                    throw RuntimeError($"Each tls protocol must be text such as \"h2\" or \"http/1.1\", but this is {Values.TypeName(protocol)}.", connect.Line);
                }
            }
            // ALPN needs SslClientAuthenticationOptions, which .NET Framework
            // under Windows PowerShell 5.1 (the certified otterPS) does not have.
            throw RuntimeError("Protocol negotiation (\"using protocol\") is not available on this Otter runtime (Windows PowerShell 5.1 has no ALPN support).", connect.Line,
                "Remove \"using protocol\" to connect with TLS only.");
        }
        var client = new TcpClient(AddressFamily.InterNetwork);
        var tcp = new OtterTcp(client, host, port) { IsSecure = connect.IsSecure, ServerName = serverName };
        activeNet.Add(tcp);
        scope.Set(connect.Target, tcp);
        try { tcp.ConnectTask = client.ConnectAsync(host, port); }
        catch (Exception e) when (e is SocketException or ArgumentException or ObjectDisposedException)
        {
            tcp.State = "closed";
            tcp.LastError = $"Could not connect to tcp {host} on port {port}: {BaseMessage(e)}";
            throw RuntimeError(tcp.LastError, connect.Line);
        }
    }

    void SendDatagram(UdpSendStmt send, Scope scope)
    {
        object? target = Evaluate(send.Socket, scope);
        if (target is not OtterUdp udp)
        {
            throw RuntimeError($"I can only send to a host and port through a udp socket, but this is {Values.TypeName(target)}.", send.Line,
                "open udp on port 9000 and call it socket");
        }
        if (udp.State != "open") throw RuntimeError($"I cannot send through a udp socket that is not open (its state is '{udp.State}').", send.Line);
        OtterBytes data = NetBytes(Evaluate(send.Data, scope), send.Line, "a udp socket");
        object? hostValue = Evaluate(send.HostExpr, scope);
        if (hostValue is not string host || string.IsNullOrWhiteSpace(host))
        {
            throw RuntimeError($"I need text for the udp destination host, but this is {Values.TypeName(hostValue)}.", send.Line);
        }
        int port = NetPort(Evaluate(send.Port, scope), send.Line);
        try
        {
            if (!IPAddress.TryParse(host, out IPAddress? address))
            {
                address = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                    ?? throw new InvalidOperationException($"no IPv4 address found for '{host}'");
            }
            udp.Client.Send(data.Value, data.Value.Length, new IPEndPoint(address, port));
        }
        catch (Exception e) when (e is SocketException or InvalidOperationException or ArgumentException or ObjectDisposedException)
        {
            throw RuntimeError($"I could not send the udp datagram to {host}:{port}: {BaseMessage(e)}", send.Line);
        }
    }

    void ListenTcp(TcpListenStmt listen, Scope scope)
    {
        string address = "127.0.0.1";
        if (listen.AddressExpr != null)
        {
            object? rawAddress = Evaluate(listen.AddressExpr, scope);
            if (rawAddress is not string addressText || string.IsNullOrWhiteSpace(addressText))
            {
                throw RuntimeError($"I need text for a tcp bind address, but this is {Values.TypeName(rawAddress)}.", listen.Line,
                    "listen for tcp on \"127.0.0.1\" on port 8080 and call it server");
            }
            address = addressText.Trim();
        }
        int port = NetPort(Evaluate(listen.Port, scope), listen.Line, allowZero: true);
        if (!IPAddress.TryParse(address, out IPAddress? ip))
        {
            try { ip = Dns.GetHostAddresses(address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork); }
            catch (Exception e) when (e is SocketException or ArgumentException) { ip = null; }
            if (ip == null) throw RuntimeError($"I could not resolve the bind address \"{address}\".", listen.Line);
        }
        TcpListener listener;
        try
        {
            listener = new TcpListener(ip, port);
            listener.Start();
        }
        catch (SocketException e)
        {
            throw RuntimeError($"I could not start a tcp server on {address} port {port}: {e.Message}", listen.Line);
        }
        var bound = (IPEndPoint)listener.LocalEndpoint;
        var server = new OtterTcpServer(listener, bound.Address.ToString(), bound.Port);
        activeTcpServers.Add(server);
        scope.Set(listen.Target, server);
    }
}
