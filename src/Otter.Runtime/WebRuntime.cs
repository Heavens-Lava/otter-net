using System.Text;
using Otter.Language;

namespace Otter.Runtime;

// The Otter Web runtime 0.1 (docs/web/RUNTIME_CONTRACT.md): D51's web-server
// grammar given real behaviour, only in a project whose otter.json opts in.
// Without a web host (WebHostFactory is null) every statement here keeps
// Otter 1.0.1's behaviour: `otter run` stops at a route with "I do not know
// how to run a WebRoute statement yet".
//
// The host (Otter.Web.Hosting, Kestrel) reads requests on its own threads;
// a request reaches the program only whole, through the host's queue, and
// its handler runs on the interpreter's thread from the event loop, one at a
// time, like every Otter handler (D121).

// What the program declared for one server.
public sealed record OtterWebServerOptions(string Name, string Host, int Port, long MaxBody, bool AllowPublicBind);

// One complete request, as the host read it.
public sealed class OtterWebRequest
{
    public required string Id { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required IReadOnlyList<KeyValuePair<string, string>> Query { get; init; }
    public required IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; }
    public required string Body { get; init; }
    public TaskCompletionSource<OtterWebResponse> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

// The answer the program gave (or the runtime gave for it).
public sealed record OtterWebResponse(int Status, string? ContentType, byte[] Body, IReadOnlyList<KeyValuePair<string, string>>? Headers = null)
{
    public static OtterWebResponse Json(int status, string json) => new(status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
}

// The HTTP side, implemented by Otter.Web.Hosting.
public interface IOtterWebHost
{
    // The address the server listens on, after Start.
    string Url { get; }
    // Binds and starts accepting; throws OtterWebHostProblem on failure.
    void Start();
    // The next complete request, if one is waiting.
    bool TryTake(out OtterWebRequest request);
    // Ctrl+C or SIGTERM was pressed.
    bool StopRequested { get; }
    // Stops accepting and lets requests in progress finish (up to 5 s).
    void BeginStop();
    // True once the server has stopped.
    bool IsStopped { get; }
    // Stops at once: requests still waiting get 503.
    void Shutdown();
}

public sealed class OtterWebHostProblem(string message) : Exception(message);

public delegate IOtterWebHost OtterWebHostFactory(OtterWebServerOptions options);

public sealed partial class Interpreter
{
    // Set when the project opts in to the Otter Web runtime.
    public OtterWebHostFactory? WebHostFactory { get; init; }
    // web.allowPublicBind in otter.json.
    public bool WebAllowPublicBind { get; init; }
    // Status lines ("listening", "stopped", request failures) - standard error.
    public Action<string>? WebLog { get; init; }
    // A handler's failure, shown as the program's errors are shown.
    public Action<Exception>? WebHandlerFailed { get; init; }

    const string WebServerType = "web server";
    const long DefaultMaxBody = 1024 * 1024;
    const long LargestMaxBody = 100L * 1024 * 1024;

    readonly List<WebServer> webServers = new();
    WebServer? defaultWebServer;
    int defaultWebPort = 8080;
    WebRequestContext? currentWebRequest;

    sealed class WebRoute(string method, string path, string? requestTarget, IReadOnlyList<Stmt> body, Scope scope, int line)
    {
        public string Method { get; } = method;           // "" = any method
        public string Path { get; } = path;               // normalised: lower case, no trailing slash
        public string? RequestTarget { get; } = requestTarget;
        public IReadOnlyList<Stmt> Body { get; } = body;
        public Scope Scope { get; } = scope;
        public int Line { get; } = line;
    }

    sealed class WebRequestContext(OtterWebRequest request)
    {
        public OtterWebRequest Request { get; } = request;
    }

    // `respond` ends its handler with this answer.
    sealed class WebRespondSignal(OtterWebResponse response) : Exception("respond")
    {
        public OtterWebResponse Response { get; } = response;
    }

    // One declared server: an event source of the console event loop.
    sealed class WebServer(Interpreter interpreter, OtterThing? thing, OtterWebServerOptions options, int line) : IOtterEventSource
    {
        public OtterThing? Thing { get; } = thing;
        public OtterWebServerOptions Options { get; set; } = options;
        public int Line { get; } = line;
        public List<WebRoute> Routes { get; } = new();
        public IOtterWebHost? Host { get; private set; }
        bool stopping, stopped, announcedStop;

        public bool IsActive => !stopped && (Host != null || Routes.Count > 0);

        public void Start()
        {
            if (Host != null || stopped) return;
            IOtterWebHost host = interpreter.WebHostFactory!(Options);
            try { host.Start(); }
            catch (OtterWebHostProblem problem) { throw interpreter.RuntimeError(problem.Message, Line); }
            Host = host;
            interpreter.WebLog?.Invoke($"Otter Web: listening on {host.Url} (Ctrl+C to stop)");
        }

        public void Step()
        {
            if (stopped) return;
            if (Host == null) Start();
            var host = Host!;
            if (host.StopRequested && !stopping)
            {
                stopping = true;
                host.BeginStop();
            }
            // Requests already waiting are answered, also while stopping.
            for (int i = 0; i < 64 && host.TryTake(out OtterWebRequest request); i++) interpreter.HandleWebRequest(this, request);
            if (stopping && host.IsStopped)
            {
                stopped = true;
                Announce();
            }
        }

        public void Shutdown()
        {
            if (Host != null && !stopped)
            {
                Host.Shutdown();
                stopped = true;
                Announce();
            }
            stopped = true;
        }

        void Announce()
        {
            if (announcedStop) return;
            announcedStop = true;
            interpreter.WebLog?.Invoke("Otter Web: stopped.");
        }
    }

    // ---- statements (only with the runtime on) ----

    // `site is a web server` with its properties: checked, then registered.
    void RegisterWebServer(OtterThing thing, ObjectDefStmt definition)
    {
        int port = 8080;
        string host = "127.0.0.1";
        long maxBody = DefaultMaxBody;
        foreach (string name in thing.PropertyNames)
        {
            object? value = thing.ReadProperty(name);
            switch (name.ToLowerInvariant())
            {
                case "port":
                    port = WholeNumber(value, 1, 65535, $"The web server's port must be a whole number from 1 to 65535, not {Values.Format(value)}.", definition.Line);
                    break;
                case "host":
                    if (value is not string hostText || hostText.Trim().Length == 0)
                        throw RuntimeError($"The web server's host must be text, such as \"127.0.0.1\", not {Values.Format(value)}.", definition.Line);
                    host = hostText.Trim();
                    break;
                case "maxbody":
                    maxBody = WholeNumber(value, 0, LargestMaxBody, $"The web server's maxbody must be a whole number of bytes from 0 to {LargestMaxBody}, not {Values.Format(value)}.", definition.Line);
                    break;
                default:
                    throw RuntimeError($"A web server has no property called \"{name}\" in the Otter Web runtime 0.1.", definition.Line,
                        "Use port, host and maxbody. Static files come in a later version.");
            }
        }
        var options = new OtterWebServerOptions(definition.Name, host, port, maxBody, WebAllowPublicBind);
        var server = new WebServer(this, thing, options, definition.Line);
        webServers.Add(server);
        AddEventSource(server);
    }

    int WholeNumber(object? value, long min, long max, string message, int line)
    {
        if (value is double number && number == Math.Floor(number) && number >= min && number <= max) return (int)Math.Min(number, int.MaxValue);
        throw RuntimeError(message, line);
    }

    // The server a route or `start` names: a declared web server, or the
    // default one (`listen on port N`, else port 8080) for any other name.
    WebServer ServerFor(Expr server, Scope scope, int line)
    {
        if (server is VariableExpr variable && scope.Has(variable.Name) && scope.Get(variable.Name) is OtterThing thing)
        {
            var declared = webServers.FirstOrDefault(s => ReferenceEquals(s.Thing, thing));
            if (declared != null) return declared;
        }
        if (server is not VariableExpr) Evaluate(server, scope);
        if (defaultWebServer == null)
        {
            var options = new OtterWebServerOptions("server", "127.0.0.1", defaultWebPort, DefaultMaxBody, WebAllowPublicBind);
            defaultWebServer = new WebServer(this, null, options, line);
            webServers.Add(defaultWebServer);
            AddEventSource(defaultWebServer);
        }
        return defaultWebServer;
    }

    bool TryExecuteWeb(Stmt statement, Scope scope)
    {
        switch (statement)
        {
            case WebRouteStmt route:
            {
                WebServer server = ServerFor(route.Server, scope, route.Line);
                if (route.Path is not LiteralExpr { Value: string path } || !path.StartsWith('/'))
                    throw RuntimeError("A route's path must be text that starts with \"/\", such as \"/hello\".", route.Line);
                string method = route.Method.Trim().ToUpperInvariant();
                if (method == "ALL") method = "";   // `a request`: any method
                server.Routes.Add(new WebRoute(method, NormalisePath(path), route.RequestTarget, route.Body, scope, route.Line));
                return true;
            }
            case StartServerStmt start:
            {
                ServerFor(start.Target, scope, start.Line).Start();
                return true;
            }
            case ListenServerStmt listen:
            {
                object? port = Evaluate(listen.Port, scope);
                defaultWebPort = WholeNumber(port, 1, 65535, $"The port must be a whole number from 1 to 65535, not {Values.Format(port)}.", listen.Line);
                if (defaultWebServer != null) defaultWebServer.Options = defaultWebServer.Options with { Port = defaultWebPort };
                return true;
            }
            case RespondStmt respond:
            {
                if (currentWebRequest == null)
                    throw RuntimeError("\"respond\" can only be used inside a web route (when ... receives ...).", respond.Line);
                throw new WebRespondSignal(BuildResponse(respond, scope));
            }
        }
        return false;
    }

    OtterWebResponse BuildResponse(RespondStmt respond, Scope scope)
    {
        int status = 200;
        if (respond.Status != null)
        {
            object? statusValue = Evaluate(respond.Status, scope);
            status = WholeNumber(statusValue, 200, 599, $"A response status must be a whole number from 200 to 599, not {Values.Format(statusValue)}.", respond.Line);
        }
        if (respond.Value == null) return new OtterWebResponse(status, null, Array.Empty<byte>());
        object? value = Evaluate(respond.Value, scope);
        if (respond.AsJson)
        {
            string json;
            try { json = OtterJson.Write(value) ?? "null"; }
            catch (OtterJson.NotData) { throw RuntimeError("Otter cannot turn something it can do into JSON.", respond.Line); }
            return OtterWebResponse.Json(status, json);
        }
        return new OtterWebResponse(status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(Values.Format(value)));
    }

    // ---- requests ----

    static string NormalisePath(string path)
    {
        string trimmed = path.TrimEnd('/');
        return (trimmed.Length == 0 ? "/" : trimmed).ToLowerInvariant();
    }

    void HandleWebRequest(WebServer server, OtterWebRequest request)
    {
        OtterWebResponse response;
        try
        {
            response = AnswerWebRequest(server, request);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            response = InternalError(request, e);
        }
        var headers = new List<KeyValuePair<string, string>>(response.Headers ?? Array.Empty<KeyValuePair<string, string>>());
        request.Completion.TrySetResult(response with { Headers = headers });
    }

    OtterWebResponse AnswerWebRequest(WebServer server, OtterWebRequest request)
    {
        string path = NormalisePath(request.Path);
        string method = request.Method == "HEAD" ? "GET" : request.Method;
        var onPath = server.Routes.Where(r => r.Path == path).ToList();
        if (onPath.Count == 0) return OtterWebResponse.Json(404, "{\"error\":\"Not Found\"}");
        WebRoute? route = onPath.FirstOrDefault(r => r.Method.Length == 0 || r.Method == method);
        if (route == null)
        {
            var allowed = onPath.Select(r => r.Method).Distinct().ToList();
            if (allowed.Contains("GET")) allowed.Add("HEAD");
            return OtterWebResponse.Json(405, "{\"error\":\"Method Not Allowed\"}") with
            {
                Headers = new[] { new KeyValuePair<string, string>("Allow", string.Join(", ", allowed.Distinct())) },
            };
        }

        var scope = new Scope(route.Scope);
        if (route.RequestTarget != null) scope.SetLocal(route.RequestTarget, RequestThing(request));
        var previous = currentWebRequest;
        currentWebRequest = new WebRequestContext(request);
        try
        {
            Execute(route.Body, scope);
            return new OtterWebResponse(204, null, Array.Empty<byte>());
        }
        catch (WebRespondSignal answer)
        {
            return answer.Response;
        }
        catch (ReturnSignal)
        {
            // `stop` or `return` at the handler's own level ends it.
            return new OtterWebResponse(204, null, Array.Empty<byte>());
        }
        finally
        {
            currentWebRequest = previous;
        }
    }

    OtterWebResponse InternalError(OtterWebRequest request, Exception failure)
    {
        WebLog?.Invoke($"Otter Web: request {request.Id} ({request.Method} {request.Path}) failed:");
        WebHandlerFailed?.Invoke(failure);
        string id = request.Id.Replace("\\", "").Replace("\"", "");
        return OtterWebResponse.Json(500, $"{{\"error\":\"Internal Server Error\",\"requestId\":\"{id}\"}}");
    }

    // The `into req` value: D51's method, path, query, body, headers, and
    // `queries` (every value of each query name, in order).
    static OtterThing RequestThing(OtterWebRequest request)
    {
        var query = new OtterThing("thing");
        var queries = new OtterThing("thing");
        foreach (var (name, value) in request.Query)
        {
            if (!query.HasProperty(name)) query.WriteProperty(name, value);
            if (queries.ReadProperty(name) is not OtterList list)
            {
                list = new OtterList();
                queries.WriteProperty(name, list);
            }
            list.Add(value);
        }
        var headers = new OtterThing("thing", namesIgnoreCase: true);
        foreach (var (name, value) in request.Headers)
        {
            headers.WriteProperty(name, headers.ReadProperty(name) is string earlier ? earlier + ", " + value : value);
        }
        var thing = new OtterThing("web request");
        thing.WriteProperty("method", request.Method);
        thing.WriteProperty("path", request.Path);
        thing.WriteProperty("query", query);
        thing.WriteProperty("queries", queries);
        thing.WriteProperty("body", request.Body);
        thing.WriteProperty("headers", headers);
        return thing;
    }
}
