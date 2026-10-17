namespace Otter.Language;

// Syntax nodes for networking and events (see docs/PORTING.md): HTTP (D49,
// D96, D101, D116A, D116B), servers and routes (D51), WebSockets (D106), TCP
// and UDP (D107, D108, D112, D113), file watching (D104) and `await` (D56).
// Property names follow otterPS's contract classes, because the D130 loop-pass
// analysis reads the names a statement sets from them (Otter.Runtime.LoopPasses).

public enum NetworkEventKind { Open, Message, Close, Error, Connect, Data, Connection, Complete, Cancel }
public enum WebSocketConnState { Connecting, Open, Closing, Closed, Connected }
public enum TcpServerState { Listening, Stopped }
public enum HttpRequestState { Pending, Completed, Failed, Cancelled }
public enum WatchKind { File, Folder }
public enum WatchEventKind { Change, Create, Delete, Rename }

// ---- HTTP (D49, D101, D116A) ------------------------------------------------

public sealed class HttpHeaderClause(Expr name, Expr value)
{
    public Expr Name { get; } = name;
    public Expr Value { get; } = value;
}

// The optional indented block under get/post/put/delete/start (D101).
public sealed class HttpOptions
{
    public List<HttpHeaderClause> Headers { get; } = new();
    public bool? WithCookies { get; set; }
    public bool? FollowRedirects { get; set; }
    public Expr? TimeoutSeconds { get; set; }
}

// get URL [as json] into X / get json from URL into X
public sealed class HttpGetStmt(Expr url, string target, bool asJson, HttpOptions? options, int line) : Stmt(line)
{
    public Expr Url { get; } = url;
    public string Target { get; } = target;
    public bool AsJson { get; } = asJson;
    public HttpOptions? Options { get; } = options;
}

// post DATA [as json] to URL [into X] / put DATA [as json] to URL [into X]
public sealed class HttpSendStmt(string method, Expr data, Expr url, string? target, bool asJson, HttpOptions? options, int line) : Stmt(line)
{
    public string Method { get; } = method;
    public Expr Data { get; } = data;
    public Expr Url { get; } = url;
    public string? Target { get; } = target;
    public bool AsJson { get; } = asJson;
    public HttpOptions? Options { get; } = options;
}

// delete from URL [into X]
public sealed class HttpDeleteStmt(Expr url, string? target, HttpOptions? options, int line) : Stmt(line)
{
    public Expr Url { get; } = url;
    public string? Target { get; } = target;
    public HttpOptions? Options { get; } = options;
}

// download file from URL to PATH (D96)
public sealed class DownloadFileStmt(Expr url, Expr path, int line) : Stmt(line)
{
    public Expr Url { get; } = url;
    public Expr Path { get; } = path;
}

// start get/post/put/delete ... and call it R (D116B)
public sealed class HttpStartStmt(string method, Expr url, Expr? data, bool asJson, string targetName, HttpOptions? options, int line) : Stmt(line)
{
    public string Method { get; } = method;
    public Expr Url { get; } = url;
    public Expr? Data { get; } = data;
    public bool AsJson { get; } = asJson;
    public string TargetName { get; } = targetName;
    public HttpOptions? Options { get; } = options;
}

// cancel R (D116B; also a command job, D119-R2)
public sealed class HttpCancelStmt(Expr request, int line) : Stmt(line) { public Expr Request { get; } = request; }

// R is pending/completed/failed/cancelled (D116B)
public sealed class HttpRequestIsStateExpr(Expr request, HttpRequestState state, int line) : Expr(line)
{
    public Expr Request { get; } = request;
    public HttpRequestState State { get; } = state;
}

// received response (D116B)
public sealed class ReceivedResponseExpr(int line) : Expr(line);

// ---- servers and routes (D51): `otter serve` only ---------------------------

// when S receives METHOD at PATH [into R]
public sealed class WebRouteStmt(Expr server, string method, Expr path, string? requestTarget, IReadOnlyList<Stmt> body, int line) : Stmt(line)
{
    public Expr Server { get; } = server;
    public string Method { get; } = method;
    public Expr Path { get; } = path;
    public string? RequestTarget { get; } = requestTarget;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

// respond with VALUE [as json] [and status N] / respond with status N
public sealed class RespondStmt(Expr? value, Expr? status, bool asJson, int line) : Stmt(line)
{
    public Expr? Value { get; } = value;
    public Expr? Status { get; } = status;
    public bool AsJson { get; } = asJson;
}

// start S / listen S
public sealed class StartServerStmt(Expr target, int line) : Stmt(line) { public Expr Target { get; } = target; }

// listen on port N
public sealed class ListenServerStmt(Expr port, int line) : Stmt(line) { public Expr Port { get; } = port; }

// ---- await (D56): parsed, refused when run ----------------------------------

public sealed class AwaitExpr(Expr inner, int line) : Expr(line) { public Expr Inner { get; } = inner; }

// `await x` as a statement of its own.
public sealed class AwaitStmt(AwaitExpr await, int line) : Stmt(line) { public AwaitExpr Await { get; } = await; }

// ---- WebSockets (D106), TCP and UDP (D107, D108, D112, D113) ----------------

// connect to websocket URL [using protocol P] and call it W
public sealed class WebSocketConnectStmt(Expr url, Expr? protocol, string target, int line) : Stmt(line)
{
    public Expr Url { get; } = url;
    public Expr? Protocol { get; } = protocol;
    public string Target { get; } = target;
}

// send X through W (also a tcp connection)
public sealed class WebSocketSendStmt(Expr message, Expr socket, int line) : Stmt(line)
{
    public Expr Message { get; } = message;
    public Expr Socket { get; } = socket;
}

// close websocket W [with code N] [and reason R]
public sealed class WebSocketCloseStmt(Expr socket, Expr? code, Expr? reason, int line) : Stmt(line)
{
    public Expr Socket { get; } = socket;
    public Expr? Code { get; } = code;
    public Expr? Reason { get; } = reason;
}

// on open/message/close/error of W, on connect of C, on data from C,
// on connection to S, on complete/cancel of R
public sealed class NetworkEventStmt(NetworkEventKind eventKind, Expr socket, IReadOnlyList<Stmt> body, int line) : Stmt(line)
{
    public NetworkEventKind EventKind { get; } = eventKind;
    public Expr Socket { get; } = socket;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

// W is connecting/open/closing/closed/connected
public sealed class WebSocketIsStateExpr(Expr socket, WebSocketConnState state, int line) : Expr(line)
{
    public Expr Socket { get; } = socket;
    public WebSocketConnState State { get; } = state;
}

public sealed class ReceivedMessageExpr(int line) : Expr(line);
public sealed class CloseCodeExpr(int line) : Expr(line);
public sealed class CloseReasonExpr(int line) : Expr(line);
public sealed class CloseWasCleanExpr(int line) : Expr(line);
public sealed class WebSocketErrorExpr(int line) : Expr(line);

// connect [securely] to tcp H on port N [for server S] [using protocol(s) P] and call it C
public sealed class TcpConnectStmt(Expr hostExpr, Expr port, string target, bool isSecure, Expr? serverName, Expr? protocols, int line) : Stmt(line)
{
    public Expr HostExpr { get; } = hostExpr;
    public Expr Port { get; } = port;
    public string Target { get; } = target;
    public bool IsSecure { get; } = isSecure;
    public Expr? ServerName { get; } = serverName;
    public Expr? Protocols { get; } = protocols;
}

// open udp [on port N] and call it U
public sealed class UdpOpenStmt(Expr? port, string target, int line) : Stmt(line)
{
    public Expr? Port { get; } = port;
    public string Target { get; } = target;
}

// send B through U to H on port N
public sealed class UdpSendStmt(Expr data, Expr socket, Expr hostExpr, Expr port, int line) : Stmt(line)
{
    public Expr Data { get; } = data;
    public Expr Socket { get; } = socket;
    public Expr HostExpr { get; } = hostExpr;
    public Expr Port { get; } = port;
}

// close tcp C / close udp U
public sealed class NetCloseStmt(string protocol, Expr socket, int line) : Stmt(line)
{
    public string Protocol { get; } = protocol;
    public Expr Socket { get; } = socket;
}

// listen for tcp [on A] on port N and call it S
public sealed class TcpListenStmt(Expr? addressExpr, Expr port, string target, int line) : Stmt(line)
{
    public Expr? AddressExpr { get; } = addressExpr;
    public Expr Port { get; } = port;
    public string Target { get; } = target;
}

// stop tcp S
public sealed class TcpStopStmt(Expr server, int line) : Stmt(line) { public Expr Server { get; } = server; }

// received data / sender address / sender port / network error / incoming connection
public sealed class NetContextExpr(string field, int line) : Expr(line) { public string Field { get; } = field; }

// C is secure (D112)
public sealed class ConnectionIsSecureExpr(Expr connection, int line) : Expr(line) { public Expr Connection { get; } = connection; }

// S is listening / stopped (D113)
public sealed class TcpServerIsStateExpr(Expr server, TcpServerState state, int line) : Expr(line)
{
    public Expr Server { get; } = server;
    public TcpServerState State { get; } = state;
}

// ---- file watching (D104) ---------------------------------------------------

// watch file/folder P [recursively] and call it W
public sealed class FileWatchStmt(WatchKind targetKind, Expr path, bool recursive, string target, int line) : Stmt(line)
{
    public WatchKind TargetKind { get; } = targetKind;
    public Expr Path { get; } = path;
    public bool Recursive { get; } = recursive;
    public string Target { get; } = target;
}

// stop watching W
public sealed class StopWatchingStmt(Expr watcher, int line) : Stmt(line) { public Expr Watcher { get; } = watcher; }

// on change of W / on create|delete|rename in W
public sealed class WatchEventStmt(WatchEventKind eventKind, Expr watcher, IReadOnlyList<Stmt> body, int line) : Stmt(line)
{
    public WatchEventKind EventKind { get; } = eventKind;
    public Expr Watcher { get; } = watcher;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

// W is watching
public sealed class IsWatchingExpr(Expr watcher, int line) : Expr(line) { public Expr Watcher { get; } = watcher; }

public sealed class ChangedPathExpr(int line) : Expr(line);
public sealed class ChangedFileNameExpr(int line) : Expr(line);
public sealed class ChangeKindExpr(int line) : Expr(line);
public sealed class OldPathExpr(int line) : Expr(line);
