using System.Globalization;
using System.Net;
using System.Text;
using Otter.Language;
using Otter.Runtime;
using Otter.Web;

namespace Otter.Cli;

// `otter serve <file.ot> [-Port N]` (D51): otter.ps1's serve branch and
// otterPS's src/Otter.Server.psm1. The program is checked as `otter check`
// checks it, but never run: its top-level `when S receives ...` routes are
// answered by an HttpListener from their first `respond with` (a literal
// value, or the statement's node text), until the process is stopped.
static class ServeCommand
{
    // RC3 B11: the largest request body read (10 MB).
    const long MaxRequestBodyBytes = 10485760;

    sealed record ServerDefinition(string Name, int Port, string Host);

    public static int Run(CommandLine line, string scriptFile)
    {
        string resolvedPath = Path.GetFullPath(scriptFile);
        ResolvedProgram? resolved = null;
        ProgramNode program;
        try
        {
            resolved = Modules.Resolve(resolvedPath);
            string source = resolved.CombinedSource;
            string[] sourceLines = source.Replace("\r\n", "\n").Split('\n');
            FileMode.Compiled compiled = FileMode.Compile(source, sourceLines, showTokens: false);
            if (compiled.Failure is OtterError failure) throw failure;
            // Not an Otter error (an empty program's parameter binding error):
            // PowerShell's own error text.
            if (compiled.Failure != null) throw new NeedsPowerShell("otter serve of a program that fails inside Otter");
            program = compiled.Program!;
        }
        catch (OtterError error)
        {
            Program.Trace("native: serve (errors)");
            OtterError shown = resolved != null ? resolved.Remap(error) : error;
            Host.Line("");
            Host.Line(shown.FormatDetailed(), ConsoleColor.Red);
            Host.Line("");
            throw new CliExit(Cli.ExitCheckError);
        }

        // Start-OtterServer: what the program declares, then -Port.
        var servers = new List<ServerDefinition>();
        var routes = new List<WebRouteStmt>();
        var listens = new List<ListenServerStmt>();
        foreach (Stmt statement in program.Statements)
        {
            if (statement is ObjectDefStmt objectDef && string.Equals(objectDef.TypeName, "web server", StringComparison.OrdinalIgnoreCase))
            {
                int port = 8080;
                string host = "localhost";
                foreach (AssignStmt property in objectDef.Properties)
                {
                    string propertyName = property.Target is VariableExpr variable ? variable.Name : NodeText(property.Target);
                    if (string.Equals(propertyName, "port", StringComparison.OrdinalIgnoreCase) && property.Value is LiteralExpr portLiteral)
                    {
                        port = CastPort(portLiteral.Value, port, 24, "                            $port = [int]$prop.Value.Value");
                    }
                    else if (string.Equals(propertyName, "host", StringComparison.OrdinalIgnoreCase) && property.Value is LiteralExpr hostLiteral)
                    {
                        host = PowerShellText.Of(hostLiteral.Value);
                    }
                }
                // An [ordered] dictionary: a name used again (any capitals)
                // keeps its place and takes the new values.
                int existing = servers.FindIndex(s => string.Equals(s.Name, objectDef.Name, StringComparison.OrdinalIgnoreCase));
                var server = new ServerDefinition(objectDef.Name, port, host);
                if (existing >= 0) servers[existing] = server with { Name = servers[existing].Name };
                else servers.Add(server);
            }
            else if (statement is WebRouteStmt route) routes.Add(route);
            else if (statement is ListenServerStmt listen) listens.Add(listen);
        }
        if (servers.Count == 0 && routes.Count > 0)
        {
            int defaultPort = 8080;
            if (listens.Count > 0 && listens[0].Port is LiteralExpr listenPort)
            {
                defaultPort = CastPort(listenPort.Value, defaultPort, 49, "            $defaultPort = [int]$listenServers[0].Port.Value");
            }
            servers.Add(new ServerDefinition("server", defaultPort, "localhost"));
        }
        int targetPort = line.Port > 0 ? line.Port : servers.Count > 0 ? servers[0].Port : 8080;
        string targetHost = servers.Count > 0 ? servers[0].Host : "localhost";

        // A server that cannot start fails with PowerShell's own error text;
        // nothing has been written yet, so the PowerShell Otter says it.
        var listener = new HttpListener();
        try
        {
            listener.Prefixes.Add($"http://{targetHost}:{targetPort.ToString(CultureInfo.InvariantCulture)}/");
            listener.Start();
        }
        catch (Exception e) when (e is HttpListenerException or ArgumentException or ObjectDisposedException or InvalidOperationException)
        {
            listener.Close();
            throw new NeedsPowerShell("the web server could not start (PowerShell's error)");
        }

        Program.Trace("native: serve");
        Host.Line($"Otter Web Server running on port {targetPort.ToString(CultureInfo.InvariantCulture)}. Press Ctrl+C to stop.");
        try
        {
            while (true) HandleNextRequest(listener, routes);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    // OtterServerSession.HandleNextRequest
    static void HandleNextRequest(HttpListener listener, List<WebRouteStmt> routes)
    {
        HttpListenerContext context = listener.GetContext();
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        try
        {
            if (request.HasEntityBody)
            {
                if (request.ContentLength64 > MaxRequestBodyBytes)
                {
                    SendText(response, 413, "Request body too large.");
                    return;
                }
                // The body is read (and refused when too large) but no route uses it.
                var buffer = new byte[65536];
                long total = 0;
                while (true)
                {
                    int read = request.InputStream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    total += read;
                    if (total > MaxRequestBodyBytes) break;
                }
                if (total > MaxRequestBodyBytes)
                {
                    SendText(response, 413, "Request body too large.");
                    return;
                }
            }

            (int status, string contentType, string content) = Answer(routes, request.HttpMethod, request.Url!.AbsolutePath);
            response.StatusCode = status;
            response.ContentType = contentType;
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Close();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Program.Trace($"serve: {e.GetType().Name}: {e.Message}");
            Host.Line($"Otter: a {request.HttpMethod} request to {request.Url?.AbsolutePath} could not be completed and was skipped. The server is still running.", ConsoleColor.DarkYellow);
            try { SendText(response, 500, "Internal Server Error"); }
            catch (Exception)
            {
                try { response.Abort(); } catch (Exception) { }
            }
        }
    }

    // A refusal or failure: a short text, and the connection closed.
    static void SendText(HttpListenerResponse response, int statusCode, string text)
    {
        response.KeepAlive = false;
        response.StatusCode = statusCode;
        response.ContentType = "text/plain; charset=utf-8";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Close();
    }

    // Find-OtterMatchingRoute and Invoke-OtterServerRoute.
    static (int Status, string ContentType, string Content) Answer(List<WebRouteStmt> routes, string method, string path)
    {
        WebRouteStmt? matched = Match(routes, method, path);
        if (matched == null) return (404, "application/json", "{\"error\":\"Not Found\"}");
        int status = 200;
        string contentType = "text/plain; charset=utf-8";
        string content = "";
        foreach (Stmt statement in matched.Body)
        {
            if (statement is not RespondStmt respond) continue;
            object? value = null;
            if (respond.Value != null) value = respond.Value is LiteralExpr literal ? literal.Value : NodeText(respond.Value);
            status = respond.Status is LiteralExpr statusLiteral
                ? (PowerShellInt32.TryCast(statusLiteral.Value, out int code, out _) ? code : throw new InvalidCastException("not a whole number"))
                : 200;
            if (respond.AsJson)
            {
                contentType = "application/json; charset=utf-8";
                content = value != null ? OtterJson.Write(value) ?? "null" : "null";
            }
            else content = value != null ? PowerShellText.Of(value) : "";
            break;
        }
        return (status, contentType, content);
    }

    static WebRouteStmt? Match(List<WebRouteStmt> routes, string method, string path)
    {
        string requestMethod = method.ToUpperInvariant();
        string normalizedPath = path.StartsWith('/') ? path : "/" + path;
        if (normalizedPath.Length > 1 && normalizedPath.EndsWith('/')) normalizedPath = normalizedPath.TrimEnd('/');
        foreach (WebRouteStmt route in routes)
        {
            string routeMethod = route.Method.ToUpperInvariant();
            if (routeMethod != "ALL" && routeMethod != "REQUEST" && routeMethod != requestMethod) continue;
            string routePath = route.Path is LiteralExpr literal ? PowerShellText.Of(literal.Value) : "";
            if (!routePath.StartsWith('/')) routePath = "/" + routePath;
            if (routePath.Length > 1 && routePath.EndsWith('/')) routePath = routePath.TrimEnd('/');
            // PowerShell's -eq: capitals ignored.
            if (string.Equals(routePath, normalizedPath, StringComparison.OrdinalIgnoreCase)) return route;
        }
        return null;
    }

    // [string] of an otterPS node: Node.ToString(), "<Kind>@L<line>".
    static string NodeText(Node node)
    {
        string name = node.GetType().Name;
        string kind = name switch
        {
            "DateDifferenceExpr" => "DateDifferenceValue",
            "WebSocketErrorExpr" => "WebSocketErrorValue",
            "QueryBetweenExpr" or "QueryInExpr" => name,
            _ when name.EndsWith("Expr", StringComparison.Ordinal) => name[..^4],
            _ => name,
        };
        return $"{kind}@L{node.Line.ToString(CultureInfo.InvariantCulture)}";
    }

    // Get-OtterServerDefinition's `$port = [int]...` (Otter.Server.psm1 line
    // 24) and `$defaultPort = [int]...` (line 49). A value [int] refuses is a
    // statement-terminating error inside a script module, where
    // $ErrorActionPreference is the global Continue (a module does not see
    // otter.ps1's Stop): PowerShell reports it and goes on, and the port
    // keeps the value it had. Windows PowerShell 5.1's report: the message,
    // the statement's position, and the RuntimeException's category
    // (docs/SPEC-QUESTIONS.md T3-3).
    static int CastPort(object? value, int current, int moduleLine, string moduleText)
    {
        if (PowerShellInt32.TryCast(value, out int port, out string message, out string errorId)) return port;
        int start = moduleText.Length - moduleText.TrimStart().Length + 1;
        string file = Path.Combine(AppContext.BaseDirectory, "src", "Otter.Server.psm1");
        new PowerShellError("", message, PowerShellError.PositionMessage(file, moduleLine, moduleText, start, moduleText.Length - start + 1),
            "InvalidArgument", "", "RuntimeException", errorId).Write();
        return current;
    }
}
