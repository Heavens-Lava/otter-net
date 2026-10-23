using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Otter.Runtime;

namespace Otter.Web.Hosting;

// One Otter web server on Kestrel (docs/web/RUNTIME_CONTRACT.md §2, §6-§8).
// Kestrel reads requests concurrently on its own threads, with limits and
// timeouts, so a slow client blocks no one. A request reaches the program
// only whole: headers, decoded query and the full body, through a queue the
// interpreter's event loop empties on its own thread.
public sealed class KestrelWebHost : IOtterWebHost
{
    // Requests waiting for the program, and how long one may wait before it
    // is answered 503 and abandoned (contract §6-§7). The tests make these
    // small through two environment variables; nothing else reads them.
    readonly int maxWaiting = EnvironmentNumber("OTTER_WEB_TEST_MAX_WAITING", 1000);
    readonly TimeSpan answerTimeout = TimeSpan.FromSeconds(EnvironmentNumber("OTTER_WEB_TEST_QUEUE_TIMEOUT_SECONDS", 60));
    int live;   // requests in the queue that are still waiting
    static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    readonly OtterWebServerOptions options;
    readonly ConcurrentQueue<OtterWebRequest> waiting = new();
    WebApplication? app;
    volatile Task? stopTask;
    PosixSignalRegistration? sigint, sigterm;
    volatile bool stopRequested;
    volatile bool stopping;

    public KestrelWebHost(OtterWebServerOptions options) => this.options = options;

    static int EnvironmentNumber(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int value) && value > 0 ? value : fallback;

    public static OtterWebHostFactory Factory => options => new KestrelWebHost(options);

    public string Url { get; private set; } = "";
    public bool StopRequested => stopRequested;
    public bool IsStopped => stopTask?.IsCompleted ?? false;

    public void Start()
    {
        IPAddress[] addresses = BindAddresses(options.Host, options.AllowPublicBind);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.Logging.ClearProviders();
        // Otter handles Ctrl+C and SIGTERM itself (graceful stop, then the
        // program ends normally), not the generic host's console lifetime.
        builder.Services.AddSingleton<IHostLifetime, OtterHostLifetime>();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = StopTimeout);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            KestrelServerLimits limits = kestrel.Limits;
            limits.MaxRequestBodySize = options.MaxBody;
            limits.MaxRequestHeadersTotalSize = 32 * 1024;
            limits.MaxRequestLineSize = 8 * 1024;
            limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            limits.MinRequestBodyDataRate = new MinDataRate(bytesPerSecond: 240, gracePeriod: TimeSpan.FromSeconds(5));
            limits.MaxConcurrentConnections = 1000;
            limits.MaxConcurrentUpgradedConnections = 0;
            foreach (IPAddress address in addresses) kestrel.Listen(address, options.Port);
        });
        app = builder.Build();
        app.Run(HandleAsync);
        try
        {
            app.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception e) when (IsAddressInUse(e))
        {
            throw new OtterWebHostProblem($"The web server could not start: port {options.Port} on {options.Host} is already in use.");
        }
        catch (Exception e) when (e is IOException or SocketException or InvalidOperationException)
        {
            throw new OtterWebHostProblem($"The web server could not start on {options.Host}:{options.Port}: {e.Message}");
        }
        string shown = addresses[0].AddressFamily == AddressFamily.InterNetworkV6 ? $"[{addresses[0]}]" : addresses[0].ToString();
        if (addresses.Length > 1 || options.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) shown = "localhost";
        Url = $"http://{shown}:{options.Port}/";
        sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
        sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);
    }

    // The first Ctrl+C or SIGTERM asks for a graceful stop; a second one
    // while stopping ends the process at once (the default action).
    // The stop starts here, on the signal's own thread, so it also happens
    // while a handler is busy (the event loop is not running then): Kestrel
    // stops accepting at once and closes the remaining connections after
    // the 5 s grace, so no client is left waiting.
    void OnSignal(PosixSignalContext context)
    {
        if (stopRequested) return;
        context.Cancel = true;
        stopRequested = true;
        BeginStop();
    }

    // §2: loopback unless the project allows public binding.
    static IPAddress[] BindAddresses(string host, bool allowPublic)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return new[] { IPAddress.Loopback, IPAddress.IPv6Loopback };
        IPAddress? address = host switch
        {
            "*" or "+" or "0.0.0.0" => IPAddress.Any,
            "::" or "[::]" => IPAddress.IPv6Any,
            _ => IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? parsed) ? parsed : null,
        };
        if (address == null)
            throw new OtterWebHostProblem($"The web server's host \"{host}\" is not an IP address. Use \"127.0.0.1\", \"localhost\" or \"::1\".");
        if (!IPAddress.IsLoopback(address) && !allowPublic)
            throw new OtterWebHostProblem($"The web server will not listen on \"{host}\": only this computer (127.0.0.1, localhost, ::1) is allowed. To accept other computers, set \"allowPublicBind\": true under \"web\" in otter.json.");
        return new[] { address };
    }

    static bool IsAddressInUse(Exception e)
    {
        for (Exception? inner = e; inner != null; inner = inner.InnerException)
        {
            if (inner is AddressInUseException) return true;
            if (inner is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }) return true;
            if (inner is IOException io && io.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // The next request the program may run: abandoned ones (timed out,
    // client gone) are dropped here and never reach a handler.
    public bool TryTake(out OtterWebRequest request)
    {
        while (waiting.TryDequeue(out OtterWebRequest? next))
        {
            if (next.TryTake())
            {
                Interlocked.Decrement(ref live);
                request = next;
                return true;
            }
        }
        request = null!;
        return false;
    }

    int stopStarted;

    // Idempotent: from the signal handler and from the event loop.
    public void BeginStop()
    {
        WebApplication? running = app;
        if (running == null || Interlocked.Exchange(ref stopStarted, 1) == 1) return;
        stopping = true;
        stopTask = Task.Run(() => running.StopAsync());
    }

    // The program has ended: everything still waiting is answered 503 and
    // abandoned, so no client is left waiting and no handler runs later.
    public void Shutdown()
    {
        if (app == null) return;
        stopping = true;
        AbandonWaiting();
        BeginStop();
        try { stopTask?.Wait(StopTimeout + TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        AbandonWaiting();
        sigint?.Dispose();
        sigterm?.Dispose();
        try { ((IAsyncDisposable)app).DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        app = null;
    }

    void AbandonWaiting()
    {
        while (waiting.TryDequeue(out OtterWebRequest? request))
        {
            if (request.TryAbandon())
            {
                Interlocked.Decrement(ref live);
                request.Completion.TrySetResult(Unavailable());
            }
        }
    }

    static OtterWebResponse Unavailable() => OtterWebResponse.Json(503, "{\"error\":\"Service Unavailable\"}");

    // ---- one request ----

    async Task HandleAsync(HttpContext context)
    {
        string id = NewId();
        HttpResponse response = context.Response;
        response.Headers["X-Request-Id"] = id;
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Cache-Control"] = "no-store";
        bool head = HttpMethods.IsHead(context.Request.Method);

        OtterWebResponse answer;
        try
        {
            answer = await AnswerAsync(context, id);
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException bad)
        {
            // Kestrel's own limits: 413 body too large, 408 too slow, 400 malformed.
            answer = OtterWebResponse.Json(bad.StatusCode, $"{{\"error\":\"{Reason(bad.StatusCode)}\"}}");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return;   // the client went away
        }

        response.StatusCode = answer.Status;
        foreach (var (name, value) in answer.Headers ?? Array.Empty<KeyValuePair<string, string>>())
        {
            if (name.AsSpan().IndexOfAny("\r\n\0") >= 0 || value.AsSpan().IndexOfAny("\r\n\0") >= 0) continue;
            response.Headers[name] = value;
        }
        if (answer.ContentType != null) response.ContentType = answer.ContentType;
        if (answer.Body.Length > 0)
        {
            response.ContentLength = answer.Body.Length;
            if (!head) await response.Body.WriteAsync(answer.Body, context.RequestAborted);
        }
        else if (answer.Status != 204 && answer.Status != 304) response.ContentLength = 0;
    }

    async Task<OtterWebResponse> AnswerAsync(HttpContext context, string id)
    {
        HttpRequest request = context.Request;
        if (stopping) return Unavailable();

        // The body, whole, within maxbody (Kestrel enforces the size and rate).
        byte[] bodyBytes;
        using (var buffer = new MemoryStream())
        {
            await request.Body.CopyToAsync(buffer, context.RequestAborted);
            bodyBytes = buffer.ToArray();
        }
        string body;
        try { body = StrictUtf8.GetString(bodyBytes); }
        catch (DecoderFallbackException) { return BadRequest("The request body is not valid UTF-8 text."); }

        // The path (Kestrel has decoded it) and the query, decoded here so
        // every value is checked and every repeated name is kept in order.
        string path = request.Path.HasValue ? request.Path.Value! : "/";
        if (path.Contains('�')) return BadRequest("The request path is not valid UTF-8.");
        var query = new List<KeyValuePair<string, string>>();
        string raw = request.QueryString.HasValue ? request.QueryString.Value!.TrimStart('?') : "";
        foreach (string pair in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            string name = equals < 0 ? pair : pair[..equals];
            string value = equals < 0 ? "" : pair[(equals + 1)..];
            if (!TryDecode(name, out string decodedName) || !TryDecode(value, out string decodedValue))
                return BadRequest("The query string is not valid UTF-8.");
            if (decodedName.Length > 0) query.Add(new(decodedName, decodedValue));
        }

        var headers = new List<KeyValuePair<string, string>>();
        foreach (var header in request.Headers)
        {
            foreach (string? value in header.Value) headers.Add(new(header.Key, value ?? ""));
        }

        var otterRequest = new OtterWebRequest
        {
            Id = id,
            Method = request.Method.ToUpperInvariant(),
            Path = path,
            Query = query,
            Headers = headers,
            Body = body,
            Aborted = context.RequestAborted,
        };
        // Overload: more requests waiting than allowed get 503 at once.
        if (Interlocked.Increment(ref live) > maxWaiting)
        {
            Interlocked.Decrement(ref live);
            return Unavailable();
        }
        waiting.Enqueue(otterRequest);

        Task finished = await Task.WhenAny(otterRequest.Completion.Task, Task.Delay(answerTimeout, context.RequestAborted))
            .ConfigureAwait(false);
        if (finished != otterRequest.Completion.Task)
        {
            // Timed out or the client went away while it waited: abandon it
            // (its handler will never run) - unless the program already took
            // it, in which case its handler is running and finishes; a gone
            // client's answer is then discarded.
            if (otterRequest.TryAbandon())
            {
                Interlocked.Decrement(ref live);
                context.RequestAborted.ThrowIfCancellationRequested();
                otterRequest.Completion.TrySetResult(Unavailable());
                return Unavailable();
            }
            context.RequestAborted.ThrowIfCancellationRequested();
        }
        // Taken: wait for the handler (a shutdown aborts the connection
        // after its 5 s grace, which ends this wait).
        return await otterRequest.Completion.Task.WaitAsync(context.RequestAborted).ConfigureAwait(false);
    }

    static OtterWebResponse BadRequest(string message) =>
        OtterWebResponse.Json(400, "{\"error\":\"Bad Request\",\"message\":\"" + message + "\"}");

    // application/x-www-form-urlencoded decoding: + is a space, %XX bytes
    // are UTF-8; anything else is refused.
    static bool TryDecode(string text, out string decoded)
    {
        decoded = "";
        var bytes = new List<byte>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '+') bytes.Add((byte)' ');
            else if (c == '%')
            {
                if (i + 2 >= text.Length || !Uri.IsHexDigit(text[i + 1]) || !Uri.IsHexDigit(text[i + 2])) return false;
                bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                i += 2;
            }
            else if (c < 0x80) bytes.Add((byte)c);
            else bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
        }
        try { decoded = StrictUtf8.GetString(bytes.ToArray()); return true; }
        catch (DecoderFallbackException) { return false; }
    }

    static string Reason(int status) => status switch
    {
        400 => "Bad Request",
        408 => "Request Timeout",
        413 => "Payload Too Large",
        431 => "Request Header Fields Too Large",
        _ => "Bad Request",
    };

    static string NewId() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    // The generic host's lifetime without console handling.
    sealed class OtterHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
