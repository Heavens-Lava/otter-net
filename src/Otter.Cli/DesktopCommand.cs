using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Otter.Language;
using Otter.Runtime;
using Otter.Web;

namespace Otter.Cli;

// `otter desktop <file.ot>` on Windows: otterPS's Start-OtterDesktopApplication
// (src/Otter.Desktop.psm1). Not WPF: the program is compiled to a web page,
// an authenticated loopback bridge (an HttpListener) serves the page's
// files, commands, clipboard and environment requests, and the page opens
// in an Edge or Chrome `--app=` window. The command ends when the page's
// heartbeat stops.
//
// OTTER_NET_DESKTOP_NO_BROWSER=1 (otter-net's own test hook) opens no
// window and runs the bridge anywhere, so it can be tested with plain HTTP
// requests; the session then ends 20 seconds after the last heartbeat.
static partial class DesktopCommand
{
    public static bool TestMode => Environment.GetEnvironmentVariable("OTTER_NET_DESKTOP_NO_BROWSER") == "1";

    public static int Run(string scriptFile)
    {
        string primarySource = Path.GetFullPath(scriptFile);
        // Export-OtterWebApplication, before anything is started or printed.
        ResolvedProgram resolved;
        try { resolved = Modules.Resolve(primarySource); }
        // Raised outside Export's own error handling: PowerShell's error text.
        catch (OtterError) { throw new NeedsPowerShell("an import problem in otter desktop is reported by PowerShell"); }
        string html;
        try
        {
            html = WebCommand.CompilePage(resolved, primarySource);
        }
        catch (NotSupportedYet unsupported)
        {
            throw new NeedsPowerShell($"{unsupported.Feature} (line {unsupported.Line}) is not native yet");
        }
        catch (WebInternalProblem problem)
        {
            Program.Trace("native: desktop (internal problem)");
            return WebCommand.ReportInternalProblem(problem);
        }
        catch (OtterError error)
        {
            Program.Trace("native: desktop (errors)");
            Host.Line("");
            Host.Line(resolved.Remap(error).FormatDetailed(), ConsoleColor.Red);
            Host.Line("");
            throw new CliExit(Cli.ExitCheckError);
        }
        catch (Exception unexpected) when (unexpected is not OutOfMemoryException)
        {
            throw new NeedsPowerShell($"the web compiler stopped unexpectedly ({unexpected.GetType().Name})");
        }

        string projectRoot = ProjectRoot(primarySource);
        Bridge bridge = Bridge.Start(projectRoot) ?? throw new NeedsPowerShell("the desktop bridge could not start (PowerShell's error)");

        // The page next to the program, then a copy for this session with the
        // bridge's port and token in it.
        string htmlPath = Path.ChangeExtension(primarySource, ".html");
        string instancePath = Path.ChangeExtension(htmlPath, ".desktop.html");
        try
        {
            File.WriteAllText(htmlPath, html + "\r\n", new UTF8Encoding(true));
            string rawHtml = File.ReadAllText(htmlPath, Encoding.UTF8);
            string injection = DesktopTemplates.InjectionScript.Replace("\r\n", "\n").Replace("\n", "\r\n")
                .Replace("{{PORT}}", bridge.Port.ToString(CultureInfo.InvariantCulture))
                .Replace("{{TOKEN}}", bridge.AuthToken);
            string instanceHtml = HeadClose().IsMatch(rawHtml)
                ? HeadClose().Replace(rawHtml, _ => injection + "\n</head>")
                : injection + "\n" + rawHtml;
            File.WriteAllText(instancePath, instanceHtml + "\r\n", new UTF8Encoding(true));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            bridge.Stop();
            throw new NeedsPowerShell("the desktop page could not be written (PowerShell's error)");
        }

        Process? window = TestMode ? null : OpenWindow(instancePath);
        Program.Trace("native: desktop");
        string pid = window != null ? window.Id.ToString(CultureInfo.InvariantCulture) : "";
        Host.Line($"Otter Desktop Application running (PID: {pid}, Bridge Port: {bridge.Port.ToString(CultureInfo.InvariantCulture)}).", ConsoleColor.Green);

        bool interrupted = false;
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; interrupted = true; };
        Console.CancelKeyPress += onCancel;
        try
        {
            while (!interrupted && bridge.IsRunning && bridge.IsSessionAlive()) bridge.HandleNextRequest(500);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            try { if (window != null && !window.HasExited) window.Kill(); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            bridge.Stop();
            try { if (File.Exists(instancePath)) File.Delete(instancePath); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Host.Line("Otter Desktop Application closed cleanly.", ConsoleColor.Gray);
        }
        return Cli.ExitSuccess;
    }

    [GeneratedRegex("</head>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadClose();

    // The folder the bridge works in: the program's folder, or otterPS's own
    // repository when the program lies one folder inside it.
    static string ProjectRoot(string source)
    {
        string parent = Path.GetDirectoryName(source)!;
        string? grandParent = Path.GetDirectoryName(parent);
        if (!string.IsNullOrEmpty(grandParent) && File.Exists(Path.Combine(grandParent, "Otter.Contract.psm1"))) return grandParent;
        string here = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(here, "Otter.Contract.psm1"))) return here;
        return parent;
    }

    // Edge or Chrome in app mode; otherwise the default handler for the page.
    static Process? OpenWindow(string instancePath)
    {
        string[] candidates =
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        };
        string? browser = candidates.FirstOrDefault(File.Exists);
        string uri = "file:///" + instancePath.Replace('\\', '/');
        if (browser != null)
        {
            var start = new ProcessStartInfo(browser) { UseShellExecute = true };
            start.ArgumentList.Add($"--app={uri}");
            start.ArgumentList.Add("--window-size=1440,900");
            start.ArgumentList.Add("--disable-infobars");
            start.ArgumentList.Add("--no-first-run");
            start.ArgumentList.Add("--no-default-browser-check");
            string? userDataDir = Environment.GetEnvironmentVariable("OTTER_BROWSER_USER_DATA_DIR");
            if (!string.IsNullOrEmpty(userDataDir)) start.ArgumentList.Add($"--user-data-dir={userDataDir}");
            return Process.Start(start);
        }
        return Process.Start(new ProcessStartInfo(instancePath) { UseShellExecute = true });
    }

    // OtterTerminalBridgeSession and Start-OtterTerminalBridge.
    sealed class Bridge
    {
        readonly HttpListener listener;
        readonly List<string> allowedOrigins;
        Task<HttpListenerContext>? pending;
        DateTime lastHeartbeatUtc = DateTime.UtcNow;
        bool hasReceivedHeartbeat;
        const int HeartbeatGraceMs = 8000, StartupGraceMs = 20000;

        public int Port { get; }
        public string Cwd { get; }
        public string AuthToken { get; private set; }
        public bool IsRunning { get; private set; } = true;

        Bridge(HttpListener listener, int port, string cwd, string token)
        {
            this.listener = listener;
            Port = port;
            Cwd = cwd;
            AuthToken = token;
            allowedOrigins = new List<string> { $"http://127.0.0.1:{port}", $"http://localhost:{port}", "null" };
        }

        public static Bridge? Start(string cwd)
        {
            // New-OtterSessionToken: 256 random bits, base64url.
            string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int chosen = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int port = chosen + attempt;
                var candidate = new HttpListener();
                try
                {
                    candidate.Prefixes.Add($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/");
                    candidate.Start();
                    return new Bridge(candidate, port, cwd, token);
                }
                catch (Exception e) when (e is HttpListenerException or ArgumentException or InvalidOperationException)
                {
                    candidate.Close();
                }
            }
            return null;
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            AuthToken = Guid.NewGuid().ToString();
            try { listener.Stop(); listener.Close(); }
            catch (Exception e) when (e is ObjectDisposedException or HttpListenerException) { }
        }

        public bool IsSessionAlive()
        {
            int grace = hasReceivedHeartbeat ? HeartbeatGraceMs : StartupGraceMs;
            return (DateTime.UtcNow - lastHeartbeatUtc).TotalMilliseconds <= grace;
        }

        public bool HandleNextRequest(int timeoutMs)
        {
            if (!IsRunning || !listener.IsListening) return false;
            HttpListenerContext context;
            try
            {
                pending ??= listener.GetContextAsync();
                if (!pending.Wait(timeoutMs)) return false;
                context = pending.Result;
                pending = null;
            }
            catch (Exception e) when (e is AggregateException or HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                pending = null;
                return false;
            }
            Handle(context);
            return true;
        }

        static void Write(HttpListenerResponse response, string payload)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(payload);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }

        static void Refuse(HttpListenerResponse response, int status, string description, string payload)
        {
            response.StatusCode = status;
            response.StatusDescription = description;
            response.ContentType = "application/json; charset=utf-8";
            Write(response, payload);
            response.Close();
        }

        void Handle(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            // Origin: loopback pages and the file:// page only.
            string? origin = request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin))
            {
                string normalized = origin.Trim().ToLowerInvariant();
                bool allowed = normalized.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) ||
                               normalized.StartsWith("http://localhost:", StringComparison.Ordinal) ||
                               allowedOrigins.Contains(normalized, StringComparer.OrdinalIgnoreCase);
                if (!allowed)
                {
                    Refuse(response, 403, "Forbidden - Invalid Origin", "{\"error\":\"Forbidden: cross-origin requests from unauthorized origins are rejected.\"}");
                    return;
                }
                response.Headers.Add("Access-Control-Allow-Origin", origin);
                response.Headers.Add("Vary", "Origin");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Otter-Token, Authorization");
            }
            if (Same(request.HttpMethod, "OPTIONS"))
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            // The session token: a header, a bearer token, or ?token=.
            string? provided = request.Headers["X-Otter-Token"];
            if (string.IsNullOrEmpty(provided))
            {
                string? authorization = request.Headers["Authorization"];
                if (authorization != null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) provided = authorization.Substring(7).Trim();
            }
            if (string.IsNullOrEmpty(provided)) provided = request.QueryString["token"];
            if (string.IsNullOrEmpty(provided))
            {
                Refuse(response, 401, "Unauthorized - Missing Token", "{\"error\":\"Unauthorized: missing session authentication token.\"}");
                return;
            }
            // PowerShell's -ne: capitals ignored.
            if (!string.Equals(provided, AuthToken, StringComparison.OrdinalIgnoreCase))
            {
                Refuse(response, 403, "Forbidden - Invalid Token", "{\"error\":\"Forbidden: invalid or expired session authentication token.\"}");
                return;
            }

            string path = request.Url!.AbsolutePath.ToLowerInvariant();
            string method = request.HttpMethod;
            try
            {
                (int status, string? contentType, string payload) = Route(path, method, request);
                // otterPS sets the status after writing a success; http.sys
                // has sent 200 by then, so the status is set first here.
                response.StatusCode = status;
                if (contentType != null) response.ContentType = contentType;
                Write(response, payload);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // The response may be gone already (the listener answered
                // the request itself).
                try
                {
                    response.StatusCode = 500;
                    Write(response, Json.Object(("error", e.Message)));
                }
                catch (Exception) when (e is not OutOfMemoryException) { }
            }
            finally
            {
                try { response.Close(); }
                catch (Exception e) when (e is ObjectDisposedException or HttpListenerException or InvalidOperationException) { }
            }
        }

        const string JsonType = "application/json; charset=utf-8";

        (int, string?, string) Route(string path, string method, HttpListenerRequest request)
        {
            bool post = Same(method, "POST"), get = Same(method, "GET");
            switch (path)
            {
                case "/api/session/heartbeat":
                    lastHeartbeatUtc = DateTime.UtcNow;
                    hasReceivedHeartbeat = true;
                    return (200, JsonType, Json.Object(("status", "ok"), ("serverTimeUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))));
                case "/api/terminal/ping":
                    return (200, JsonType, Json.Object(("status", "ok"), ("port", Port), ("cwd", Cwd)));
                case "/api/terminal/profiles":
                {
                    List<Shell> shells = Shells.Available();
                    // A pipeline of one is written as that one object; of none, as nothing.
                    string payload = shells.Count switch
                    {
                        0 => "",
                        1 => shells[0].ToJson(),
                        _ => "[" + string.Join(",", shells.Select(s => s.ToJson())) + "]",
                    };
                    return (200, JsonType, payload);
                }
                case "/api/terminal/exec" when post:
                {
                    JsonElement? json = ReadJson(request);
                    string command = Text(json, "command") ?? "";
                    string? shellId = Text(json, "shell");
                    string cwd = Text(json, "cwd") ?? Cwd;
                    ShellResult result = Shells.Run(command, shellId, cwd);
                    return (200, JsonType, Json.Object(("command", command), ("shell", result.Shell), ("shellId", result.ShellId), ("cwd", result.Cwd),
                        ("exitCode", result.ExitCode), ("stdout", result.Stdout), ("stderr", result.Stderr), ("output", result.Output)));
                }
                case "/api/fs/read" when post || get:
                {
                    string target = post ? Text(ReadJson(request), "path") ?? "" : request.QueryString["path"] ?? "";
                    if (target.Length == 0) return (400, null, Json.Object(("error", "Path parameter is required.")));
                    string full = InCwd(target);
                    if (!File.Exists(full))
                    {
                        string? parentCwd = Path.GetDirectoryName(Cwd);
                        if (!string.IsNullOrEmpty(parentCwd) && File.Exists(Path.Combine(parentCwd, target))) full = Path.Combine(parentCwd, target);
                    }
                    if (!File.Exists(full)) return (404, null, Json.Object(("error", $"File not found: {target}")));
                    string content = File.ReadAllText(full, Encoding.UTF8);
                    return (200, JsonType, Json.Object(("path", target), ("fullPath", Path.GetFullPath(full)), ("content", content), ("size", content.Length)));
                }
                case "/api/fs/write" when post:
                {
                    JsonElement? json = ReadJson(request);
                    string target = Text(json, "path") ?? "";
                    string content = TextOrEmpty(json, "content");
                    if (target.Length == 0) return (400, null, Json.Object(("error", "Path parameter is required.")));
                    string full = InCwd(target);
                    string? parent = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent)) Directory.CreateDirectory(parent);
                    // [System.Text.Encoding]::UTF8: with a byte order mark.
                    File.WriteAllText(full, content, new UTF8Encoding(true));
                    return (200, JsonType, Json.Object(("path", target), ("fullPath", Path.GetFullPath(full)), ("size", content.Length), ("saved", true)));
                }
                case "/api/fs/download" when post:
                {
                    JsonElement? json = ReadJson(request);
                    string url = Text(json, "url") ?? "";
                    string target = Text(json, "path") ?? "";
                    if (url.Length == 0 || target.Length == 0) return (400, null, Json.Object(("error", "Both url and path parameters are required.")));
                    string full = InCwd(target);
                    try
                    {
                        OtterHttp.Download(url, full);
                        return (200, JsonType, Json.Object(("url", url), ("path", target), ("fullPath", Path.GetFullPath(full)), ("downloaded", true)));
                    }
                    catch (Exception e) when (e is not OutOfMemoryException)
                    {
                        return (500, JsonType, Json.Object(("error", e.Message)));
                    }
                }
                case "/api/fs/files" when post || get:
                case "/api/fs/folders" when post || get:
                {
                    bool files = path == "/api/fs/files";
                    string target;
                    bool recursive;
                    if (post)
                    {
                        JsonElement? json = ReadJson(request);
                        target = Text(json, "path") ?? "";
                        recursive = Truthy(json, "recursive");
                    }
                    else
                    {
                        target = request.QueryString["path"] ?? "";
                        recursive = string.Equals(request.QueryString["recursive"], "true", StringComparison.OrdinalIgnoreCase);
                    }
                    if (target.Length == 0) target = ".";
                    string full = InCwd(target);
                    if (!Directory.Exists(full))
                    {
                        string? parentCwd = Path.GetDirectoryName(Cwd);
                        if (!string.IsNullOrEmpty(parentCwd) && Directory.Exists(Path.Combine(parentCwd, target))) full = Path.Combine(parentCwd, target);
                    }
                    if (!Directory.Exists(full)) return (404, null, Json.Object(("error", $"Folder not found: {target}")));
                    var items = new List<string>();
                    foreach (FileSystemInfo item in ChildItems(new DirectoryInfo(full), recursive, files))
                    {
                        string created = item.CreationTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                        string modified = item.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                        string props = item is FileInfo file
                            ? Json.Object(("name", file.Name), ("path", file.FullName), ("extension", file.Extension), ("size", file.Length), ("created", created), ("modified", modified))
                            : Json.Object(("name", item.Name), ("path", item.FullName), ("created", created), ("modified", modified));
                        string order = files ? "[\"name\",\"path\",\"extension\",\"size\",\"created\",\"modified\"]" : "[\"name\",\"path\",\"created\",\"modified\"]";
                        items.Add($"{{\"__otterThing\":true,\"typeName\":\"{(files ? "file" : "folder")}\",\"props\":{props},\"order\":{order}}}");
                    }
                    return (200, JsonType, "[" + string.Join(",", items) + "]");
                }
                case "/api/timer/wait" when post:
                {
                    JsonElement? json = ReadJson(request);
                    int delayMs = Has(json, "delayMs") ? ToInt(Value(json, "delayMs")) : Has(json, "seconds") ? ToInt(Number(Value(json, "seconds")) * 1000) : 0;
                    if (delayMs > 0) Thread.Sleep(delayMs);
                    return (200, JsonType, Json.Object(("completed", true), ("elapsedMs", delayMs)));
                }
                case "/api/system/clipboard":
                {
                    if (post)
                    {
                        string text = TextOrEmpty(ReadJson(request), "text");
                        try { OtterClipboard.SetText(text); } catch (Exception e) when (e is not OutOfMemoryException) { }
                        return (200, JsonType, Json.Object(("completed", true), ("length", text.Length)));
                    }
                    string read = "";
                    try { read = OtterClipboard.GetText() ?? ""; } catch (Exception e) when (e is not OutOfMemoryException) { }
                    return (200, JsonType, Json.Object(("text", read), ("completed", true)));
                }
                case "/api/system/notify" when post:
                {
                    JsonElement? json = ReadJson(request);
                    string title = Has(json, "title") ? PsString(Value(json, "title")) : "Otter Notification";
                    string message = TextOrEmpty(json, "message");
                    return (200, JsonType, Json.Object(("completed", true), ("title", title), ("message", message)));
                }
                case "/api/system/env":
                {
                    string? name = null;
                    if (post)
                    {
                        JsonElement? json = ReadJson(request);
                        name = Has(json, "name") ? PsString(Value(json, "name")) : null;
                    }
                    string? value = !string.IsNullOrEmpty(name) ? Environment.GetEnvironmentVariable(name) : null;
                    return (200, JsonType, Json.Object(("completed", true), ("name", name), ("value", value), ("tempFolder", Path.GetTempPath()),
                        ("appDataFolder", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
                        ("userFolder", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), ("currentDirectory", Cwd)));
                }
                // The file dialogs: no Otter page reaches them natively (the web
                // compiler here leaves `choose file` to the PowerShell Otter);
                // otterPS answers after waiting half a second for the dialog.
                case "/api/dialog/open-file":
                case "/api/dialog/save-file":
                case "/api/dialog/folder":
                    return (200, JsonType, Json.Object(("path", ""), ("cancelled", true)));
                case "/api/fs/operate" when post:
                    return (200, JsonType, Operate(ReadJson(request)));
                default:
                    return (404, null, "{\"error\":\"Not Found\"}");
            }
        }

        string InCwd(string value) => Path.IsPathRooted(value) ? value : Path.Combine(Cwd, value);

        // Get-ChildItem -LiteralPath <folder> -File|-Directory [-Recurse]:
        // a folder's items, then each subfolder's.
        static IEnumerable<FileSystemInfo> ChildItems(DirectoryInfo folder, bool recurse, bool files)
        {
            FileSystemInfo[] entries;
            try { entries = folder.GetFileSystemInfos(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }
            Array.Sort(entries, (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            var visible = entries.Where(e => !(OperatingSystem.IsWindows() ? (e.Attributes & FileAttributes.Hidden) != 0 : e.Name.StartsWith('.'))).ToList();
            foreach (FileSystemInfo entry in visible)
            {
                if ((entry is FileInfo) == files) yield return entry;
            }
            if (!recurse) yield break;
            foreach (FileSystemInfo entry in visible)
            {
                if (entry is DirectoryInfo sub && sub.LinkTarget == null)
                {
                    foreach (FileSystemInfo inner in ChildItems(sub, recurse, files)) yield return inner;
                }
            }
        }

        // /api/fs/operate: the page's file and folder statements.
        string Operate(JsonElement? json)
        {
            string operation = Text(json, "operation") ?? "";
            string source = Text(json, "source") ?? "";
            string destination = Text(json, "destination") ?? "";
            string target = Text(json, "path") ?? "";
            string content = TextOrEmpty(json, "content");
            string Resolve(string value)
            {
                if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("A file or folder path is required.");
                return InCwd(value);
            }
            string Completed() => Json.Object(("completed", true));
            switch (operation)
            {
                case "file-exists":
                    return Json.Object(("exists", File.Exists(Resolve(target))));
                case "append-file":
                {
                    string full = Resolve(target);
                    string? parent = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    File.AppendAllText(full, content, new UTF8Encoding(false));
                    return Completed();
                }
                case "copy-file":
                case "move-file":
                {
                    bool copy = operation == "copy-file";
                    string from = Resolve(source), to = Resolve(destination);
                    if (!File.Exists(from)) throw new InvalidOperationException($"I could not find a file called \"{source}\" to {(copy ? "copy" : "move")}.");
                    if (Directory.Exists(to)) to = Path.Combine(to, Path.GetFileName(from));
                    else
                    {
                        string? parent = Path.GetDirectoryName(to);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    }
                    if (copy) File.Copy(from, to, overwrite: true);
                    else File.Move(from, to, overwrite: true);
                    return Completed();
                }
                case "delete-file":
                {
                    string full = Resolve(target);
                    if (Directory.Exists(full)) throw new InvalidOperationException($"\"{target}\" is a folder. Otter only deletes files.");
                    if (!File.Exists(full)) throw new InvalidOperationException($"I could not find a file called \"{target}\" to delete.");
                    File.Delete(full);
                    return Completed();
                }
                case "create-folder":
                    Directory.CreateDirectory(Resolve(target));
                    return Completed();
                case "delete-folder":
                {
                    string full = Resolve(target);
                    if (File.Exists(full)) throw new InvalidOperationException($"\"{target}\" is a file, not a folder.");
                    if (!Directory.Exists(full)) throw new InvalidOperationException($"I could not find a folder called \"{target}\" to delete.");
                    if (Directory.EnumerateFileSystemEntries(full).Any()) throw new InvalidOperationException($"The folder \"{target}\" is not empty. Otter only deletes empty folders.");
                    Directory.Delete(full);
                    return Completed();
                }
                case "copy-folder":
                case "move-folder":
                {
                    string from = Resolve(source), to = Resolve(destination);
                    if (!Directory.Exists(from)) throw new InvalidOperationException($"I could not find a folder called \"{source}\".");
                    if (Directory.Exists(to)) to = Path.Combine(to, Path.GetFileName(from.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                    if (operation == "copy-folder") CopyFolder(from, to);
                    else Directory.Move(from, to);
                    return Completed();
                }
                default:
                    throw new InvalidOperationException("That filesystem operation is not available.");
            }
        }

        // Copy-Item -Recurse -Force into a new or existing folder.
        static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
            foreach (string folder in Directory.GetDirectories(from)) CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
        }

        // ConvertFrom-Json of the request body ("" gives nothing).
        static JsonElement? ReadJson(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            string body = reader.ReadToEnd();
            if (body.Length == 0) return null;
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }

        // $json.name, capitals ignored, as PowerShell reads properties.
        static JsonElement? Value(JsonElement? json, string name)
        {
            if (json is not { ValueKind: JsonValueKind.Object } obj) return null;
            foreach (JsonProperty property in obj.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
            }
            return null;
        }

        static bool Has(JsonElement? json, string name) => Value(json, name) is { ValueKind: not JsonValueKind.Null };

        // `if ($json -and $json.name)`: PowerShell's truth of the value.
        static bool Truthy(JsonElement? json, string name) => Value(json, name) switch
        {
            null => false,
            { ValueKind: JsonValueKind.Null or JsonValueKind.False } => false,
            { ValueKind: JsonValueKind.String } s => s.GetString()!.Length > 0,
            { ValueKind: JsonValueKind.Number } n => n.GetDouble() != 0,
            { ValueKind: JsonValueKind.Array } a => a.GetArrayLength() > 0,
            _ => true,
        };

        // `if ($json -and $json.name) { [string]$json.name } else { <default> }`
        static string? Text(JsonElement? json, string name) => Truthy(json, name) ? PsString(Value(json, name)) : null;

        // `if ($json -and $null -ne $json.name) { [string]$json.name } else { '' }`
        static string TextOrEmpty(JsonElement? json, string name) => Has(json, name) ? PsString(Value(json, name)) : "";

        static string PsString(JsonElement? value) => value switch
        {
            null => "",
            { ValueKind: JsonValueKind.String } s => s.GetString()!,
            { ValueKind: JsonValueKind.True } => "True",
            { ValueKind: JsonValueKind.False } => "False",
            { ValueKind: JsonValueKind.Number } n => PowerShellText.Of(n.GetDouble()),
            { ValueKind: JsonValueKind.Null } => "",
            { } other => other.GetRawText(),
        };

        static double Number(JsonElement? value) => value switch
        {
            { ValueKind: JsonValueKind.Number } n => n.GetDouble(),
            { ValueKind: JsonValueKind.String } s when double.TryParse(s.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) => d,
            { ValueKind: JsonValueKind.True } => 1,
            _ => 0,
        };

        static int ToInt(JsonElement? value) => ToInt(Number(value));

        static int ToInt(double number)
        {
            double rounded = Math.Round(number, MidpointRounding.ToEven);
            if (double.IsNaN(rounded) || rounded < int.MinValue || rounded > int.MaxValue) throw new InvalidCastException("Value was either too large or too small for an Int32.");
            return (int)rounded;
        }
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // Compressed JSON as the bridge writes it (ConvertTo-Json -Compress on
    // Windows PowerShell 5.1); key order is otter-net's.
    static class Json
    {
        public static string Object(params (string Name, object? Value)[] properties)
        {
            var json = new StringBuilder("{");
            for (int i = 0; i < properties.Length; i++)
            {
                if (i > 0) json.Append(',');
                json.Append(JsonText(properties[i].Name)).Append(':').Append(Value(properties[i].Value));
            }
            return json.Append('}').ToString();
        }

        // A JSON string as JavaScriptSerializer writes it.
        public static string JsonText(string text) => OtterJson.Write(text)!;

        public static string Value(object? value) => value switch
        {
            null => "null",
            bool flag => flag ? "true" : "false",
            string text => JsonText(text),
            int number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            double number => OtterJson.RoundTrip(number),
            string[] list => "[" + string.Join(",", list.Select(JsonText)) + "]",
            _ => JsonText(value.ToString() ?? ""),
        };
    }

    sealed record Shell(string Id, string Name, string Path, string[] Args, bool IsDefault, string Prompt)
    {
        public string ToJson() => Json.Object(("Id", Id), ("Name", Name), ("Path", Path), ("Args", Args), ("IsDefault", IsDefault), ("Prompt", Prompt));
    }

    sealed record ShellResult(string Shell, string ShellId, string Cwd, int ExitCode, string Stdout, string Stderr, string Output);

    // Get-OtterAvailableShells and Invoke-OtterShellCommand.
    static class Shells
    {
        static string Windir => Environment.GetEnvironmentVariable("WINDIR") ?? "";

        public static List<Shell> Available()
        {
            var shells = new List<Shell>();
            string location = Environment.CurrentDirectory;
            string? pwsh = FindOnPath("pwsh") ?? (File.Exists(@"C:\Program Files\PowerShell\7\pwsh.exe") ? @"C:\Program Files\PowerShell\7\pwsh.exe" : null);
            if (pwsh != null) shells.Add(new Shell("pwsh", "PowerShell 7", pwsh, new[] { "-NoLogo" }, true, $"PS {location}> "));
            string ps5 = $@"{Windir}\System32\WindowsPowerShell\v1.0\powershell.exe";
            if (File.Exists(ps5)) shells.Add(new Shell("powershell", "Windows PowerShell", ps5, new[] { "-NoLogo" }, pwsh == null, $"PS {location}> "));
            string cmd = Environment.GetEnvironmentVariable("COMSPEC") is { Length: > 0 } comspec ? comspec : $@"{Windir}\System32\cmd.exe";
            if (File.Exists(cmd)) shells.Add(new Shell("cmd", "Command Prompt", cmd, Array.Empty<string>(), false, $"{location}> "));
            string[] gitBash =
            {
                @"C:\Program Files\Git\bin\bash.exe",
                @"C:\Program Files (x86)\Git\bin\bash.exe",
                $@"{Environment.GetEnvironmentVariable("LOCALAPPDATA")}\Programs\Git\bin\bash.exe",
            };
            if (gitBash.FirstOrDefault(File.Exists) is { } bash) shells.Add(new Shell("bash", "Git Bash", bash, new[] { "--login", "-i" }, false, $"user@otter:{location}$ "));
            string wsl = $@"{Windir}\System32\wsl.exe";
            if (File.Exists(wsl)) shells.Add(new Shell("wsl", "WSL (Linux)", wsl, Array.Empty<string>(), false, "otter@linux:~$ "));
            // otterPS lists its own otter.cmd; otter-net is its own REPL.
            if (Environment.ProcessPath is { } otter) shells.Add(new Shell("otter", "Otter REPL", otter, Array.Empty<string>(), false, "otter> "));
            return shells;
        }

        // Get-Command: the first match on PATH (with PATHEXT on Windows).
        static string? FindOnPath(string name)
        {
            string[] extensions = OperatingSystem.IsWindows()
                ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';', StringSplitOptions.RemoveEmptyEntries)
                : new[] { "" };
            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string extension in extensions)
                {
                    string candidate = Path.Combine(folder, name + extension);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        public static ShellResult Run(string command, string? shellId, string workingDirectory)
        {
            List<Shell> shells = Available();
            Shell? shell = shellId != null ? shells.FirstOrDefault(s => Same(s.Id, shellId)) : null;
            shell ??= shells.FirstOrDefault(s => s.IsDefault) ?? shells.FirstOrDefault();
            if (shell == null) throw new OtterError("No suitable shell found on host system.", 0, "runtime");

            var start = new ProcessStartInfo
            {
                WorkingDirectory = Directory.Exists(workingDirectory) || File.Exists(workingDirectory) ? Path.GetFullPath(workingDirectory) : Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                FileName = shell.Path,
            };
            start.Arguments = shell.Id switch
            {
                "pwsh" or "powershell" => $"-NoLogo -NoProfile -NonInteractive -Command \"{command}\"",
                "cmd" => $"/c \"{command}\"",
                "bash" => $"-c \"{command}\"",
                "wsl" => $"-e sh -c \"{command}\"",
                "otter" => command,
                _ => $"-Command \"{command}\"",
            };
            using Process process = Process.Start(start)!;
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = stderrTask.Result;
            process.WaitForExit(30000);
            int exitCode = process.HasExited ? process.ExitCode : -1;
            string combined = stdout.Length > 0 && stderr.Length > 0 ? $"{stdout}\n{stderr}" : stdout.Length > 0 ? stdout : stderr;
            return new ShellResult(shell.Name, shell.Id, start.WorkingDirectory, exitCode, stdout, stderr, combined);
        }
    }
}
