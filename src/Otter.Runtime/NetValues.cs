using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace Otter.Runtime;

// A runtime value that names its own kind in messages ("a websocket", "a
// tcp server" ...), as otterPS's Get-OtterTypeName does for its classes.
public interface IOtterTypeNamed
{
    string OtterTypeName { get; }
}

// The values of networking and file watching (otterPS's Otter.Runtime.psm1
// classes). Their text, when a program says one, is what otterPS's
// Format-OtterValue gives: a phrase for sockets, the class name for the rest.

// D104: a file watcher. Events arrive on .NET's threads and wait in the
// interpreter's queue until the event loop hands them to handlers.
public sealed class OtterFileWatcher(FileSystemWatcher native, string path, bool isFolder) : IOtterTypeNamed
{
    public FileSystemWatcher Native { get; } = native;
    public string Path { get; } = path;
    public bool IsFolder { get; } = isFolder;
    public bool Active { get; set; } = true;
    public string OtterTypeName => "a file watcher";
    public override string ToString() => "OtterFileWatcher";
}

// D106
public sealed class OtterWebSocket(ClientWebSocket native, string url, string protocol) : IOtterTypeNamed
{
    public ClientWebSocket Native { get; } = native;
    public string Url { get; } = url;
    public string Protocol { get; set; } = protocol;
    public string State { get; set; } = "connecting";
    public string? LastError { get; set; }
    public double CloseCode { get; set; }
    public string CloseReason { get; set; } = "";
    public bool CloseWasClean { get; set; }
    public CancellationTokenSource Cts { get; } = new();
    public Task? ConnectTask { get; set; }
    public Task<WebSocketReceiveResult>? ReceiveTask { get; set; }
    public byte[]? ReceiveBuffer { get; set; }
    public MemoryStream MessageBuffer { get; } = new();
    public WebSocketMessageType? MessageBufferType { get; set; }
    public bool Disposed { get; set; }
    public string OtterTypeName => "a websocket";
    public override string ToString() => "a websocket";
}

// D107, D112: a tcp connection, made by `connect to tcp` or accepted by a server.
public sealed class OtterTcp(TcpClient client, string remoteHost, int remotePort) : IOtterTypeNamed
{
    public TcpClient Client { get; } = client;
    public string RemoteHost { get; } = remoteHost;
    public int RemotePort { get; } = remotePort;
    public string State { get; set; } = "connecting";
    public string? LastError { get; set; }
    public Task? ConnectTask { get; set; }
    public Task<int>? ReadTask { get; set; }
    public byte[] ReadBuffer { get; } = new byte[8192];
    public bool ConnectFired { get; set; }
    public bool CloseFired { get; set; }
    public bool Disposed { get; set; }
    public bool IsSecure { get; set; }
    public string ServerName { get; set; } = remoteHost;
    public Stream? Stream { get; set; }
    public Task? HandshakeTask { get; set; }
    public string OtterTypeName => "a tcp connection";
    public override string ToString() => "a tcp connection";
}

// D108: a udp socket; each receive is one datagram.
public sealed class OtterUdp(UdpClient client, int localPort) : IOtterTypeNamed
{
    public UdpClient Client { get; } = client;
    public int LocalPort { get; } = localPort;
    public string State { get; set; } = "open";
    public string? LastError { get; set; }
    public Task<UdpReceiveResult>? ReceiveTask { get; set; }
    public bool CloseFired { get; set; }
    public bool Disposed { get; set; }
    public string OtterTypeName => "a udp socket";
    public override string ToString() => "a udp socket";
}

// D113: a tcp server; each accepted connection is an ordinary OtterTcp.
public sealed class OtterTcpServer(TcpListener listener, string boundAddress, int boundPort) : IOtterTypeNamed
{
    public TcpListener Listener { get; } = listener;
    public string BoundAddress { get; } = boundAddress;
    public int BoundPort { get; } = boundPort;
    public string State { get; set; } = "listening";
    public string? LastError { get; set; }
    public Task<TcpClient>? AcceptTask { get; set; }
    public bool Disposed { get; set; }
    public string OtterTypeName => "a tcp server";
    public override string ToString() => "OtterTcpServer";
}

// D116B: an http request started with `start get ...`.
public sealed class OtterHttpRequest(string method, string url, bool asJson) : IOtterTypeNamed
{
    public string Method { get; } = method;
    public string Url { get; } = url;
    public bool AsJson { get; } = asJson;
    public string State { get; set; } = "pending";
    public object? Response { get; set; }
    public string? Error { get; set; }
    public int Status { get; set; }
    public CancellationTokenSource Cts { get; } = new();
    public Task<HttpResponseMessage>? Task { get; set; }
    public HttpRequestMessage? Message { get; set; }
    public NetEvent? RetainedTerminalEvent { get; set; }
    public double? TimeoutSeconds { get; set; }
    public bool IsTimeout { get; set; }
    public bool Disposed { get; set; }
    public string OtterTypeName => "an http request";
    public override string ToString() => $"<an http request to {Url}>";
}

// One event handed to `on ... of` handlers, with the values the ambient
// expressions read (received message, close code, received data ...).
public sealed class NetEvent(Otter.Language.NetworkEventKind kind, Dictionary<string, object?> context)
{
    public Otter.Language.NetworkEventKind Kind { get; } = kind;
    public Dictionary<string, object?> Context { get; } = context;
}

// One file-system notification waiting for the event loop.
sealed class WatchNotification(OtterFileWatcher watcher, string kindWord, string? path, string? oldPath)
{
    public OtterFileWatcher Watcher { get; } = watcher;
    public string KindWord { get; } = kindWord;
    public string? Path { get; } = path;
    public string? OldPath { get; } = oldPath;
}

// otterPS's PowerShell event queue: every watcher's notifications, in the
// order they arrived.
sealed class WatchQueue
{
    readonly ConcurrentQueue<WatchNotification> queue = new();
    readonly SemaphoreSlim signal = new(0);

    public void Add(WatchNotification notification)
    {
        queue.Enqueue(notification);
        signal.Release();
    }

    // The oldest notification, waiting up to `timeout` for one to arrive.
    public WatchNotification? Take(TimeSpan timeout)
    {
        if (queue.TryDequeue(out var ready)) { signal.Wait(0); return ready; }
        if (timeout <= TimeSpan.Zero || !signal.Wait(timeout)) return null;
        return queue.TryDequeue(out var item) ? item : null;
    }
}

// A source of events the console event loop services that another area of
// the language owns (command jobs, D119-R2): it registers itself with
// Interpreter.AddEventSource.
public interface IOtterEventSource
{
    // True while the source may still produce an event a handler cares about.
    bool IsActive { get; }
    // Handles whatever is ready, without waiting.
    void Step();
    // Called once when the program ends, however it ends.
    void Shutdown();
}
