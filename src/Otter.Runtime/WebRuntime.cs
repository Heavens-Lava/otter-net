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

// What the program declared for one server. StaticRoot and Fallback are
// canonical absolute paths (checked when the server is declared); Routes is
// read by the host to let routes win over static files.
public sealed record OtterWebServerOptions(string Name, string Host, int Port, long MaxBody, bool AllowPublicBind)
{
    public string? StaticRoot { get; init; }
    public string? Fallback { get; init; }
    public OtterWebRouteTable Routes { get; init; } = new();
}

// One complete request, as the host read it. Its state decides, once and
// atomically, who owns it: the program takes it (its handler runs and
// answers) or the host abandons it (time-out, client gone, shutdown: the
// host answers, and the handler never runs). Never both.
public sealed class OtterWebRequest
{
    const int Waiting = 0, Taken = 1, Abandoned = 2;
    int state = Waiting;

    // The program takes the request; false if the host abandoned it first.
    public bool TryTake() => Interlocked.CompareExchange(ref state, Taken, Waiting) == Waiting;
    // The host gives up on it; false if the program already took it.
    public bool TryAbandon() => Interlocked.CompareExchange(ref state, Abandoned, Waiting) == Waiting;
    public bool IsAbandoned => Volatile.Read(ref state) == Abandoned;
    // Signalled when the client goes away.
    public CancellationToken Aborted { get; init; }

    public required string Id { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    // The request target's path as sent, still percent-encoded (route
    // parameters are decoded from it, segment by segment).
    public string? RawPath { get; init; }
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
    // The next complete request that is still waiting, taken for the
    // program (OtterWebRequest.TryTake); abandoned ones are skipped.
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
    // The project's folder: `static` folders are inside it.
    public string? WebRoot { get; init; }
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

    sealed class WebRoute(string method, OtterWebRoutePattern pattern, string? requestTarget, IReadOnlyList<Stmt> body, Scope scope, int line, int order)
    {
        public string Method { get; } = method;           // "" = any method
        public OtterWebRoutePattern Pattern { get; } = pattern;
        public int Order { get; } = order;                // declaration order
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
            // Two routes that answer exactly the same requests (§3).
            for (int i = 0; i < Routes.Count; i++)
            {
                for (int j = i + 1; j < Routes.Count; j++)
                {
                    if (Routes[i].Method == Routes[j].Method && Routes[i].Pattern.Shape == Routes[j].Pattern.Shape)
                        throw interpreter.RuntimeError($"The routes on lines {Routes[i].Line} and {Routes[j].Line} answer the same requests ({(Routes[i].Method.Length == 0 ? "any method" : Routes[i].Method)} {Routes[i].Pattern.Shape}).", Routes[j].Line,
                            "Remove one of them, or give them different methods or paths.");
                }
            }
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
            // Requests already waiting are answered, also while stopping -
            // but never inside another request's handler (a `wait` there
            // services events): handlers run one at a time (contract §7).
            if (interpreter.currentWebRequest == null)
            {
                for (int i = 0; i < 64 && host.TryTake(out OtterWebRequest request); i++) interpreter.HandleWebRequest(this, request);
            }
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
        string? staticFolder = null, fallback = null;
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
                case "static":
                    if (value is not string folderText || folderText.Trim().Length == 0)
                        throw RuntimeError($"The web server's static folder must be text, such as \"public\", not {Values.Format(value)}.", definition.Line);
                    staticFolder = folderText.Trim();
                    break;
                case "fallback":
                    if (value is not string fallbackText || fallbackText.Trim().Length == 0)
                        throw RuntimeError($"The web server's fallback must be the name of a file in its static folder, such as \"index.html\", not {Values.Format(value)}.", definition.Line);
                    fallback = fallbackText.Trim();
                    break;
                default:
                    throw RuntimeError($"A web server has no property called \"{name}\" in the Otter Web runtime 0.1.", definition.Line,
                        "Use port, host, maxbody, static and fallback.");
            }
        }
        if (fallback != null && staticFolder == null)
            throw RuntimeError("A web server's fallback is a file in its static folder: set static too.", definition.Line);
        string? staticRoot = staticFolder == null ? null : StaticRoot(staticFolder, definition.Line);
        string? fallbackFile = fallback == null ? null : FallbackFile(staticRoot!, fallback, definition.Line);
        var options = new OtterWebServerOptions(definition.Name, host, port, maxBody, WebAllowPublicBind) { StaticRoot = staticRoot, Fallback = fallbackFile };
        var server = new WebServer(this, thing, options, definition.Line);
        webServers.Add(server);
        AddEventSource(server);
    }

    // `static`: a folder inside the project (never the project folder itself,
    // which holds otter.json and the program), resolved once, links included.
    string StaticRoot(string folder, int line)
    {
        string project = Path.GetFullPath(WebRoot ?? Directory.GetCurrentDirectory());
        string full;
        try { full = Path.GetFullPath(Path.Combine(project, folder)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw RuntimeError($"The web server's static folder \"{folder}\" is not a valid path.", line);
        }
        if (!Directory.Exists(full)) throw RuntimeError($"The web server's static folder \"{folder}\" does not exist in the project.", line);
        string real = OtterWebStatic.RealPath(full);
        string projectReal = OtterWebStatic.RealPath(project);
        if (!OtterWebStatic.IsInside(real, projectReal) || OtterWebStatic.SamePath(real, projectReal))
            throw RuntimeError($"The web server's static folder \"{folder}\" must be a folder inside the project, not the project itself or a folder outside it.", line);
        return real;
    }

    // `fallback`: a file in the static folder (no dot names, no way out).
    string FallbackFile(string staticRoot, string file, int line)
    {
        if (!OtterWebPaths.TryDecodeSegments("/" + file.Replace('\\', '/'), out var segments) || segments.Count == 0 ||
            OtterWebStatic.Resolve(staticRoot, segments) is not { } resolved || Directory.Exists(resolved))
        {
            throw RuntimeError($"The web server's fallback \"{file}\" must be a file in its static folder.", line);
        }
        return resolved;
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
                var pattern = new OtterWebRoutePattern(path);
                server.Routes.Add(new WebRoute(method, pattern, route.RequestTarget, route.Body, scope, route.Line, server.Routes.Count));
                server.Options.Routes.Add(pattern);
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
        // A `web response` object (§5): its properties describe the answer.
        if (!respond.AsJson && value is OtterThing { TypeName: var typeName } reply && string.Equals(typeName, WebResponseType, StringComparison.OrdinalIgnoreCase))
        {
            return ResponseFromObject(reply, respond.Line);
        }
        if (respond.AsJson)
        {
            string json;
            try { json = OtterJson.Write(value) ?? "null"; }
            catch (OtterJson.NotData) { throw RuntimeError("Otter cannot turn something it can do into JSON.", respond.Line); }
            return OtterWebResponse.Json(status, json);
        }
        return new OtterWebResponse(status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(Values.Format(value)));
    }

    // ---- the web response object (§5) ----

    const string WebResponseType = "web response";
    static readonly HashSet<string> ForbiddenHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Length", "Transfer-Encoding", "Connection", "Server", "Date", "X-Request-Id", "X-Content-Type-Options", "Content-Type",
    };

    OtterWebResponse ResponseFromObject(OtterThing reply, int line)
    {
        int status = 200;
        string type = "text/plain; charset=utf-8";
        byte[] body = Array.Empty<byte>();
        string? location = null;
        var headers = new List<KeyValuePair<string, string>>();
        foreach (string name in reply.PropertyNames)
        {
            object? value = reply.ReadProperty(name);
            switch (name.ToLowerInvariant())
            {
                case "status":
                    status = WholeNumber(value, 200, 599, $"A web response's status must be a whole number from 200 to 599, not {Values.Format(value)}.", line);
                    break;
                case "type":
                    if (value is not string typeText || typeText.Trim().Length == 0 || HasLineBreak(typeText))
                        throw RuntimeError($"A web response's type must be a content type such as \"text/html\", not {Values.Format(value)}.", line);
                    type = typeText.Trim();
                    if (type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && !type.Contains("charset", StringComparison.OrdinalIgnoreCase)) type += "; charset=utf-8";
                    break;
                case "body":
                    if (value is OtterThing or OtterList)
                        throw RuntimeError("A web response's body must be text. For a thing or a list, use `respond with V as json`.", line);
                    body = value == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(Values.Format(value));
                    break;
                case "location":
                    location = value as string ?? throw RuntimeError($"A web response's location must be text, not {Values.Format(value)}.", line);
                    break;
                case "headers":
                    if (value is not OtterThing extra)
                        throw RuntimeError("A web response's headers must be a thing: `extra is a thing`, then `set \"Header-Name\" to \"value\" in extra`.", line);
                    foreach (string header in extra.PropertyNames) headers.Add(new(CheckHeaderName(header, line), CheckHeaderValue(header, extra.ReadProperty(header), line)));
                    break;
                case "cookie":
                    throw RuntimeError("Cookies come in a later version of the Otter Web runtime (Phase 3, with safe defaults).", line);
                default:
                    throw RuntimeError($"A web response has no property called \"{name}\".", line, "Use status, type, body, location and headers.");
            }
        }
        if (status is >= 300 and < 400 && location == null)
            throw RuntimeError($"A web response with status {status} (a redirect) needs a location.", line);
        if (location != null) headers.Add(new("Location", CheckLocation(location, line)));
        return new OtterWebResponse(status, type, body, headers);
    }

    static bool HasLineBreak(string text) => text.AsSpan().IndexOfAny("\r\n\0") >= 0;

    string CheckHeaderName(string name, int line)
    {
        bool token = name.Length > 0 && name.All(c => c < 128 && (char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c)));
        if (!token) throw RuntimeError($"\"{name}\" is not a valid HTTP header name.", line);
        if (string.Equals(name, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            throw RuntimeError("Cookies come in a later version of the Otter Web runtime (Phase 3, with safe defaults); Set-Cookie cannot be set yet.", line);
        if (ForbiddenHeaders.Contains(name))
            throw RuntimeError($"The header \"{name}\" is set by the Otter Web runtime and cannot be set by the program.", line,
                string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase) ? "Use the web response's type." : null);
        return name;
    }

    string CheckHeaderValue(string name, object? value, int line)
    {
        if (value is OtterThing or OtterList) throw RuntimeError($"The header \"{name}\" must have text as its value.", line);
        string text = Values.Format(value);
        if (HasLineBreak(text)) throw RuntimeError($"The header \"{name}\" cannot contain line breaks or NUL characters.", line);
        return text;
    }

    // Redirect targets stay on this site: a relative path, or an http(s) URL
    // whose host is the request's own (no open redirects).
    string CheckLocation(string location, int line)
    {
        string target = location.Trim();
        bool bad = target.Length == 0 || HasLineBreak(target) || target.Contains('\\') || target.Any(char.IsWhiteSpace) || target.StartsWith("//");
        if (!bad && System.Text.RegularExpressions.Regex.IsMatch(target, "^[A-Za-z][A-Za-z0-9+.-]*:"))
        {
            string? requestHost = currentWebRequest?.Request.Headers.FirstOrDefault(h => string.Equals(h.Key, "Host", StringComparison.OrdinalIgnoreCase)).Value;
            bad = !(Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && (uri.Scheme == "http" || uri.Scheme == "https") &&
                    requestHost != null && string.Equals(uri.Authority, requestHost, StringComparison.OrdinalIgnoreCase));
        }
        if (bad)
            throw RuntimeError($"The redirect location \"{location}\" is not allowed: use a path on this site, such as \"/home\".", line,
                "Redirects to other sites are refused (open redirects).");
        return target;
    }

    // ---- requests ----

    void HandleWebRequest(WebServer server, OtterWebRequest request)
    {
        // The client left while the request waited: its handler does not run.
        if (request.Aborted.IsCancellationRequested)
        {
            request.Completion.TrySetCanceled();
            return;
        }
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
        if (!OtterWebPaths.TryDecodeSegments(request.RawPath ?? request.Path, out List<string> segments))
            return OtterWebResponse.Json(400, "{\"error\":\"Bad Request\",\"message\":\"The request path is not valid UTF-8.\"}");
        string method = request.Method == "HEAD" ? "GET" : request.Method;
        // Routes whose pattern matches the path, most literal segments first,
        // then in the order they were declared (§3).
        var onPath = server.Routes
            .Select(r => (Route: r, Matched: r.Pattern.TryMatch(segments, out var found), Parameters: found))
            .Where(m => m.Matched)
            .OrderByDescending(m => m.Route.Pattern.LiteralCount).ThenBy(m => m.Route.Order)
            .ToList();
        if (onPath.Count == 0) return OtterWebResponse.Json(404, "{\"error\":\"Not Found\"}");
        var chosen = onPath.FirstOrDefault(m => m.Route.Method.Length == 0 || m.Route.Method == method);
        WebRoute? route = chosen.Route;
        if (route != null && chosen.Parameters.Any(p => p.Value.Contains('/')))
            return OtterWebResponse.Json(400, "{\"error\":\"Bad Request\",\"message\":\"A path parameter cannot contain an encoded slash.\"}");
        if (route == null)
        {
            var allowed = onPath.Select(m => m.Route.Method).Distinct().ToList();
            if (allowed.Contains("GET")) allowed.Add("HEAD");
            return OtterWebResponse.Json(405, "{\"error\":\"Method Not Allowed\"}") with
            {
                Headers = new[] { new KeyValuePair<string, string>("Allow", string.Join(", ", allowed.Distinct())) },
            };
        }

        var scope = new Scope(route.Scope);
        if (route.RequestTarget != null) scope.SetLocal(route.RequestTarget, RequestThing(request, chosen.Parameters));
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
    // `queries` (every value of each query name, in order) and `parameters`
    // (the route's :name values).
    static OtterThing RequestThing(OtterWebRequest request, IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        var parameterThing = new OtterThing("thing");
        foreach (var (name, value) in parameters) parameterThing.WriteProperty(name, value);
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
        thing.WriteProperty("parameters", parameterThing);
        return thing;
    }
}
