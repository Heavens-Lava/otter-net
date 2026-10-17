using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Runtime;

// A problem the network library reports in Otter's words; the interpreter
// turns it into a runtime error on the statement's line.
public sealed class NetProblem(string message) : Exception(message);

// One `with header "X" is Y` clause, evaluated.
public readonly record struct HttpHeader(string Name, string Value);

// otterPS's HTTP client (src/Otter.Library.psm1: Get-OtterHttpClient,
// Invoke-OtterHttpRequest, Start-OtterHttpRequest, Receive-OtterFileDownload;
// D49, D96, D101, D116A, D116B), on .NET's HttpClient.
public static partial class OtterHttp
{
    // One client per (cookies, redirects) pair, all sharing one cookie jar,
    // for the life of the program.
    static readonly CookieContainer SharedCookies = new();
    static readonly Dictionary<(bool Cookies, bool Redirects), HttpClient> Clients = new();

    static HttpClient Client(bool useCookies, bool allowRedirect)
    {
        lock (Clients)
        {
            if (Clients.TryGetValue((useCookies, allowRedirect), out var existing)) return existing;
            var handler = new HttpClientHandler { AllowAutoRedirect = allowRedirect, MaxAutomaticRedirections = 10 };
            if (useCookies)
            {
                handler.UseCookies = true;
                handler.CookieContainer = SharedCookies;
            }
            else handler.UseCookies = false;
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            Clients[(useCookies, allowRedirect)] = client;
            return client;
        }
    }

    // [System.Uri]::new($Url, Absolute) and the http/https check. .NET on
    // Linux and macOS also reads "/path" as an absolute file URL; .NET
    // Framework on Windows (the certified otterPS) refuses it as not a URL.
    static Uri CheckUrl(string url, string verb)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new NetProblem(verb == "download" ? "I need a URL to download from." : "I need a URL to send the HTTP request to.");
        if (url.StartsWith('/') || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) throw new NetProblem($"\"{url}\" is not a valid URL.");
        if (uri.Scheme != "http" && uri.Scheme != "https")
        {
            throw new NetProblem(verb == "download"
                ? $"I can only download files using http or https, but this URL uses \"{uri.Scheme}\"."
                : $"I can only make HTTP requests using http or https, but this URL uses \"{uri.Scheme}\".");
        }
        return uri;
    }

    // The timeout check shared by both request forms.
    public static double? CheckTimeout(object? timeout)
    {
        if (timeout == null) return null;
        double seconds;
        if (timeout is double number) seconds = number;
        else if (timeout is string text && double.TryParse(text, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                     System.Globalization.CultureInfo.InvariantCulture, out double parsed)) seconds = parsed;
        else throw new NetProblem($"HTTP timeout must be a number of seconds, but got {Values.TypeName(timeout)}.");
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
        {
            throw new NetProblem($"HTTP timeout must be a positive number of seconds, but got {PowerShellNumber(seconds)}.");
        }
        return seconds;
    }

    // A double inside a PowerShell string.
    internal static string PowerShellNumber(double number) =>
        double.IsNaN(number) ? "NaN" : double.IsPositiveInfinity(number) ? "Infinity" : double.IsNegativeInfinity(number) ? "-Infinity" : Values.PowerShellText(number);

    [GeneratedRegex("[\\r\\n\\x00]")]
    private static partial Regex LineBreak();

    [GeneratedRegex("[:\\s]")]
    private static partial Regex ColonOrSpace();

    // Assert-OtterHttpHeader: no header injection, one-token names.
    static void CheckHeader(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new NetProblem("HTTP header name cannot be empty.");
        if (LineBreak().IsMatch(name) || LineBreak().IsMatch(value)) throw new NetProblem("HTTP header names and values cannot contain line breaks (CR or LF) or NUL characters.");
        if (ColonOrSpace().IsMatch(name)) throw new NetProblem($"HTTP header name \"{name}\" cannot contain a colon or whitespace.");
    }

    // Steps 4 to 6 of Invoke-OtterHttpRequest: the message, its body, its headers.
    static HttpRequestMessage BuildRequest(string method, Uri uri, object? data, bool asJson, IReadOnlyList<HttpHeader> headers, Func<object?, string?> toJson)
    {
        HttpMethod httpMethod = method.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            _ => new HttpMethod(method),
        };
        var request = new HttpRequestMessage(httpMethod, uri);
        if (method == "POST" || method == "PUT")
        {
            if (asJson || data is OtterThing or OtterList)
            {
                string? json = toJson(data);
                // ConvertTo-Json gives nothing for gone, and StringContent refuses that.
                if (json == null) throw new InternalProblem("Exception calling \".ctor\" with \"3\" argument(s): \"Value cannot be null.\r\nParameter name: content\"");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            else if (data is OtterBytes bytes)
            {
                request.Content = new ByteArrayContent(bytes.Value);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            }
            else
            {
                string text = data == null ? "" : Values.Format(data);
                request.Content = new StringContent(text, Encoding.UTF8, "text/plain");
            }
        }
        foreach (var header in headers)
        {
            CheckHeader(header.Name, header.Value);
            bool added = request.Headers.TryAddWithoutValidation(header.Name, header.Value);
            if (!added && request.Content != null)
            {
                if (request.Content.Headers.Contains(header.Name)) request.Content.Headers.Remove(header.Name);
                added = request.Content.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }
            if (!added)
            {
                try { request.Headers.Add(header.Name, header.Value); }
                catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException)
                {
                    throw new NetProblem($"Invalid HTTP header \"{header.Name}\": {e.Message}");
                }
            }
        }
        return request;
    }

    static Exception Innermost(Exception e)
    {
        while (e.InnerException != null) e = e.InnerException;
        return e;
    }

    // Invoke-OtterHttpRequest: sends one request and waits for the body.
    // A GET as json is read as JSON by the caller (`fromJson`).
    public static object? Send(string method, string url, object? data, bool asJson, IReadOnlyList<HttpHeader> headers,
        bool? withCookies, bool? followRedirects, object? timeout, Func<object?, string?> toJson, Func<string, object?> fromJson)
    {
        Uri uri = CheckUrl(url, "request");
        double? seconds = CheckTimeout(timeout);
        HttpClient client = Client(withCookies ?? true, followRedirects ?? true);
        using HttpRequestMessage request = BuildRequest(method, uri, data, asJson, headers, toJson);
        using var cts = new CancellationTokenSource();
        if (seconds != null) cts.CancelAfter(TimeSpan.FromSeconds(seconds.Value));
        HttpResponseMessage? response = null;
        try
        {
            response = client.SendAsync(request, cts.Token).GetAwaiter().GetResult();
            string body = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
            if (asJson && method == "GET") return fromJson(body);
            return body;
        }
        catch (Exception e) when (e is not OtterError and not NetProblem and not InternalProblem)
        {
            if (cts.IsCancellationRequested) throw new NetProblem($"The HTTP request timed out after {(seconds == null ? "" : PowerShellNumber(seconds.Value))} seconds.");
            Exception inner = Innermost(e);
            if (inner is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData }) throw new NetProblem($"Could not resolve host \"{uri.Host}\".");
            if (inner is SocketException { SocketErrorCode: SocketError.ConnectionRefused }) throw new NetProblem($"Could not connect to \"{url}\": connection refused.");
            throw new NetProblem($"Could not connect to \"{url}\": {inner.Message}");
        }
        finally
        {
            response?.Dispose();
        }
    }

    // Start-OtterHttpRequest: the request is sent in the background and the
    // event loop sees it finish (D116B).
    public static OtterHttpRequest Start(string method, string url, object? data, bool asJson, IReadOnlyList<HttpHeader> headers,
        bool? withCookies, bool? followRedirects, object? timeout, Func<object?, string?> toJson)
    {
        Uri uri = CheckUrl(url, "request");
        double? seconds = CheckTimeout(timeout);
        HttpClient client = Client(withCookies ?? true, followRedirects ?? true);
        HttpRequestMessage message = BuildRequest(method, uri, data, asJson, headers, toJson);
        var handle = new OtterHttpRequest(method, url, asJson) { TimeoutSeconds = seconds, Message = message };
        if (seconds != null) handle.Cts.CancelAfter(TimeSpan.FromSeconds(seconds.Value));
        handle.Task = client.SendAsync(message, handle.Cts.Token);
        return handle;
    }

    // Receive-OtterFileDownload (D96): streamed to a temporary file next to
    // the destination, then moved into place; never overwrites a file.
    public static void Download(string url, string path)
    {
        Uri uri = CheckUrl(url, "download");
        string full;
        try { full = OtterFiles.Resolve(path); }
        catch (FileProblem problem) { throw new NetProblem(problem.Message); }
        if (Directory.Exists(full)) throw new NetProblem($"\"{path}\" is a folder, not a file.");
        string? parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            throw new NetProblem($"I cannot find the folder \"{parent}\". Otter does not create destination folders automatically.");
        }
        if (File.Exists(full) || Directory.Exists(full)) throw new NetProblem($"A file called \"{path}\" already exists. Otter will not overwrite it.");
        string tempName = ".otter-dl-" + Guid.NewGuid().ToString("N") + ".tmp";
        string tempFile = string.IsNullOrEmpty(parent) ? tempName : Path.Combine(parent, tempName);

        HttpClientHandler? handler = null;
        HttpClient? client = null;
        HttpResponseMessage? response = null;
        Stream? responseStream = null;
        FileStream? fileStream = null;
        bool promoted = false;
        try
        {
            handler = new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10 };
            client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
            try
            {
                response = client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Exception inner = Innermost(e);
                string message = inner.Message;
                if (message.Contains("redirect", StringComparison.OrdinalIgnoreCase) || inner.GetType().Name == "ProtocolViolationException")
                {
                    throw new NetProblem($"Download from \"{url}\" exceeded the redirect limit.");
                }
                if (message.Contains("No such host", StringComparison.OrdinalIgnoreCase) || inner is SocketException { SocketErrorCode: SocketError.HostNotFound })
                {
                    throw new NetProblem($"Could not resolve host \"{uri.Host}\".");
                }
                if (message.Contains("refused", StringComparison.OrdinalIgnoreCase) || inner is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
                {
                    throw new NetProblem($"Could not connect to \"{url}\": connection refused.");
                }
                throw new NetProblem($"Could not connect to \"{url}\": {message}");
            }
            if (response.RequestMessage?.RequestUri is { } finalUri && finalUri.Scheme != "http" && finalUri.Scheme != "https")
            {
                throw new NetProblem($"I can only download files using http or https, but this URL redirected to \"{finalUri.Scheme}\".");
            }
            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode;
                if (status >= 300 && status < 400)
                {
                    if (response.Headers.Location is { } location && location.IsAbsoluteUri && location.Scheme != "http" && location.Scheme != "https")
                    {
                        throw new NetProblem($"I can only download files using http or https, but this URL redirected to \"{location.Scheme}\".");
                    }
                    throw new NetProblem($"Download from \"{url}\" exceeded the redirect limit.");
                }
                throw new NetProblem($"Download failed with HTTP status {status} ({response.ReasonPhrase}).");
            }
            long? expectedLength = response.Content.Headers.ContentLength;
            try { responseStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult(); }
            catch (Exception) { throw new NetProblem($"The network connection failed while opening download stream from \"{url}\"."); }
            try { fileStream = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (Exception e) { throw new NetProblem($"Could not create temporary file in \"{parent}\": {e.Message}"); }
            long total = 0;
            var buffer = new byte[65536];
            try
            {
                while (true)
                {
                    int read = responseStream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    fileStream.Write(buffer, 0, read);
                    total += read;
                }
                fileStream.Flush();
            }
            catch (Exception) { throw new NetProblem($"The network connection was closed prematurely while downloading \"{url}\"."); }
            fileStream.Dispose();
            fileStream = null;
            responseStream.Dispose();
            responseStream = null;
            if (expectedLength != null && total < expectedLength)
            {
                throw new NetProblem($"Download from \"{url}\" was interrupted: received {total} bytes, but expected {expectedLength} bytes.");
            }
            if (File.Exists(full) || Directory.Exists(full)) throw new NetProblem($"A file called \"{path}\" already exists. Otter will not overwrite it.");
            try
            {
                File.Move(tempFile, full);
                promoted = true;
            }
            catch (Exception e)
            {
                if (e.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) || File.Exists(full) || Directory.Exists(full))
                {
                    throw new NetProblem($"A file called \"{path}\" already exists. Otter will not overwrite it.");
                }
                throw new NetProblem($"Could not save download to \"{path}\": {e.Message}");
            }
        }
        catch (Exception e)
        {
            if (!promoted && File.Exists(tempFile))
            {
                try { fileStream?.Dispose(); fileStream = null; } catch (IOException) { }
                try { File.Delete(tempFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            if (e is NetProblem or OtterError or InternalProblem) throw;
            throw new NetProblem($"Could not download \"{url}\" to \"{path}\": {e.Message}");
        }
        finally
        {
            fileStream?.Dispose();
            responseStream?.Dispose();
            response?.Dispose();
            client?.Dispose();
            handler?.Dispose();
        }
    }
}
