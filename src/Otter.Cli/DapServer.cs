using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Otter.Language;
using Otter.Runtime;

namespace Otter.Cli;

/// <summary>
/// Native Debug Adapter Protocol (DAP) implementation for Otter.
/// Communicates over standard input/output using DAP JSON-RPC framing.
/// Connects VS Code / editor visual debuggers to the native Otter interpreter.
/// </summary>
internal static class DapServer
{
    private static int _seq = 1;
    private static readonly object WriteLock = new();
    private static readonly ConcurrentDictionary<string, HashSet<int>> FileBreakpoints = new(StringComparer.OrdinalIgnoreCase);

    private static volatile bool _stopOnEntry = false;
    private static volatile bool _stepping = false;
    private static volatile bool _pauseRequested = false;
    private static volatile bool _terminated = false;
    private static readonly AutoResetEvent StepEvent = new(false);

    private static Stmt? _currentStatement;
    private static Scope? _currentScope;
    private static string? _currentFilePath;
    private static Interpreter? _runningInterpreter;
    private static Thread? _executionThread;

    public static int Run(CommandLine line)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);

        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        while (!_terminated)
        {
            string? headerLine;
            int contentLength = -1;

            while ((headerLine = ReadHeaderLine(stdin)) != null)
            {
                if (string.IsNullOrEmpty(headerLine)) break;
                if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(headerLine["Content-Length:".Length..].Trim(), out contentLength);
                }
            }

            if (contentLength < 0) break;

            byte[] bodyBytes = new byte[contentLength];
            int readTotal = 0;
            while (readTotal < contentLength)
            {
                int r = stdin.Read(bodyBytes, readTotal, contentLength - readTotal);
                if (r <= 0) break;
                readTotal += r;
            }

            if (readTotal < contentLength) break;

            string json = Encoding.UTF8.GetString(bodyBytes);
            JsonNode? req;
            try
            {
                req = JsonNode.Parse(json);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[otter dap] JSON parse error: {ex.Message}");
                continue;
            }

            if (req is not JsonObject reqObj) continue;

            HandleDapMessage(reqObj, stdout);
        }

        Cleanup();
        return Cli.ExitSuccess;
    }

    private static void HandleDapMessage(JsonObject req, Stream stdout)
    {
        int reqSeq = req["seq"]?.GetValue<int>() ?? 0;
        string? command = req["command"]?.ToString();
        var args = req["arguments"] as JsonObject;

        if (string.IsNullOrEmpty(command)) return;

        switch (command)
        {
            case "initialize":
                HandleInitialize(reqSeq, command, stdout);
                break;

            case "launch":
                HandleLaunch(reqSeq, command, args, stdout);
                break;

            case "setBreakpoints":
                HandleSetBreakpoints(reqSeq, command, args, stdout);
                break;

            case "configurationDone":
                HandleConfigurationDone(reqSeq, command, stdout);
                break;

            case "threads":
                HandleThreads(reqSeq, command, stdout);
                break;

            case "stackTrace":
                HandleStackTrace(reqSeq, command, stdout);
                break;

            case "scopes":
                HandleScopes(reqSeq, command, stdout);
                break;

            case "variables":
                HandleVariables(reqSeq, command, stdout);
                break;

            case "continue":
                HandleContinue(reqSeq, command, stdout);
                break;

            case "next":
            case "stepIn":
            case "stepOut":
                HandleStep(reqSeq, command, stdout);
                break;

            case "pause":
                HandlePause(reqSeq, command, stdout);
                break;

            case "disconnect":
            case "terminate":
                HandleDisconnect(reqSeq, command, stdout);
                break;

            default:
                SendResponse(stdout, reqSeq, command, true, new JsonObject());
                break;
        }
    }

    // =========================================================================
    // DAP Command Handlers
    // =========================================================================

    private static void HandleInitialize(int reqSeq, string command, Stream stdout)
    {
        var caps = new JsonObject
        {
            ["supportsConfigurationDoneRequest"] = true,
            ["supportsFunctionBreakpoints"] = false,
            ["supportsConditionalBreakpoints"] = false,
            ["supportsHitConditionalBreakpoints"] = false,
            ["supportsEvaluateForHovers"] = false,
            ["supportsStepBack"] = false,
            ["supportsSetVariable"] = false,
            ["supportsRestartRequest"] = false
        };

        SendResponse(stdout, reqSeq, command, true, caps);
        SendEvent(stdout, "initialized", new JsonObject());
    }

    private static void HandleLaunch(int reqSeq, string command, JsonObject? args, Stream stdout)
    {
        string? programPath = args?["program"]?.ToString();
        _stopOnEntry = args?["stopOnEntry"]?.GetValue<bool>() ?? false;

        if (string.IsNullOrEmpty(programPath) || !File.Exists(programPath))
        {
            SendResponse(stdout, reqSeq, command, false, null, $"Cannot find program: {programPath}");
            return;
        }

        _currentFilePath = Path.GetFullPath(programPath);
        SendResponse(stdout, reqSeq, command, true, new JsonObject());
    }

    private static void HandleSetBreakpoints(int reqSeq, string command, JsonObject? args, Stream stdout)
    {
        string? filePath = args?["source"]?["path"]?.ToString();
        var bps = args?["breakpoints"]?.AsArray();

        var verifiedArray = new JsonArray();
        var linesSet = new HashSet<int>();

        if (!string.IsNullOrEmpty(filePath) && bps != null)
        {
            string fullPath = Path.GetFullPath(filePath);
            foreach (var b in bps)
            {
                int line = b?["line"]?.GetValue<int>() ?? 0;
                if (line > 0)
                {
                    linesSet.Add(line);
                    var bpItem = new JsonObject
                    {
                        ["verified"] = true,
                        ["line"] = line
                    };
                    verifiedArray.Add(bpItem);
                }
            }
            FileBreakpoints[fullPath] = linesSet;
        }

        var body = new JsonObject
        {
            ["breakpoints"] = verifiedArray
        };
        SendResponse(stdout, reqSeq, command, true, body);
    }

    private static void HandleConfigurationDone(int reqSeq, string command, Stream stdout)
    {
        SendResponse(stdout, reqSeq, command, true, new JsonObject());

        // Launch interpreter execution in dedicated background thread
        if (_currentFilePath != null && File.Exists(_currentFilePath))
        {
            _executionThread = new Thread(() => ExecuteProgram(stdout))
            {
                IsBackground = true,
                Name = "OtterDapExecutionThread"
            };
            _executionThread.Start();
        }
    }

    private static void HandleThreads(int reqSeq, string command, Stream stdout)
    {
        var threadsArray = new JsonArray
        {
            new JsonObject
            {
                ["id"] = 1,
                ["name"] = "main"
            }
        };

        var body = new JsonObject
        {
            ["threads"] = threadsArray
        };
        SendResponse(stdout, reqSeq, command, true, body);
    }

    private static void HandleStackTrace(int reqSeq, string command, Stream stdout)
    {
        var frames = new JsonArray();

        if (_currentStatement != null && _currentFilePath != null)
        {
            int line = _currentStatement.Line;
            string topFn = _runningInterpreter?.CallStack.Count > 0
                ? _runningInterpreter.CallStack[^1].FunctionName
                : "<main>";

            var topSource = new JsonObject
            {
                ["name"] = Path.GetFileName(_currentFilePath),
                ["path"] = _currentFilePath
            };

            frames.Add(new JsonObject
            {
                ["id"] = 1,
                ["name"] = topFn,
                ["source"] = topSource,
                ["line"] = line,
                ["column"] = 1
            });

            // Add call stack parent frames if present
            if (_runningInterpreter != null)
            {
                var stack = _runningInterpreter.CallStack;
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    var parentSource = new JsonObject
                    {
                        ["name"] = Path.GetFileName(_currentFilePath),
                        ["path"] = _currentFilePath
                    };

                    frames.Add(new JsonObject
                    {
                        ["id"] = frames.Count + 1,
                        ["name"] = stack[i].FunctionName,
                        ["source"] = parentSource,
                        ["line"] = stack[i].CallLine,
                        ["column"] = 1
                    });
                }
            }
        }

        var body = new JsonObject
        {
            ["stackFrames"] = frames,
            ["totalFrames"] = frames.Count
        };
        SendResponse(stdout, reqSeq, command, true, body);
    }

    private static void HandleScopes(int reqSeq, string command, Stream stdout)
    {
        var scopes = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "Locals",
                ["variablesReference"] = 1,
                ["expensive"] = false
            }
        };

        var body = new JsonObject
        {
            ["scopes"] = scopes
        };
        SendResponse(stdout, reqSeq, command, true, body);
    }

    private static void HandleVariables(int reqSeq, string command, Stream stdout)
    {
        var vars = new JsonArray();

        if (_currentScope != null)
        {
            foreach (var (name, val) in _currentScope.OwnVariables)
            {
                string shown;
                try
                {
                    shown = Values.Format(_currentScope.Get(name));
                }
                catch
                {
                    shown = "?";
                }

                vars.Add(new JsonObject
                {
                    ["name"] = name,
                    ["value"] = shown,
                    ["type"] = val != null ? val.GetType().Name : "null",
                    ["variablesReference"] = 0
                });
            }
        }

        var body = new JsonObject
        {
            ["variables"] = vars
        };
        SendResponse(stdout, reqSeq, command, true, body);
    }

    private static void HandleContinue(int reqSeq, string command, Stream stdout)
    {
        _stepping = false;
        _pauseRequested = false;
        StepEvent.Set();

        var body = new JsonObject
        {
            ["allThreadsContinued"] = true
        };
        SendResponse(stdout, reqSeq, command, true, body);
    }

    private static void HandleStep(int reqSeq, string command, Stream stdout)
    {
        _stepping = true;
        _pauseRequested = false;
        StepEvent.Set();

        SendResponse(stdout, reqSeq, command, true, new JsonObject());
    }

    private static void HandlePause(int reqSeq, string command, Stream stdout)
    {
        _pauseRequested = true;
        SendResponse(stdout, reqSeq, command, true, new JsonObject());
    }

    private static void HandleDisconnect(int reqSeq, string command, Stream stdout)
    {
        _terminated = true;
        StepEvent.Set();
        SendResponse(stdout, reqSeq, command, true, new JsonObject());
    }

    // =========================================================================
    // Program Execution & Hook
    // =========================================================================

    private static void ExecuteProgram(Stream stdout)
    {
        if (_currentFilePath == null) return;

        try
        {
            string source = File.ReadAllText(_currentFilePath);
            var tokens = Lexer.Tokenize(source);
            var program = Parser.Parse(tokens);

            var outputCapture = new DapOutputWriter(stdout);
            var interpreter = new Interpreter(outputCapture, Console.In, source, Array.Empty<string>())
            {
                StatementHook = (stmt, scope) =>
                {
                    if (_terminated) throw new Exception("Debug session terminated");

                    _currentStatement = stmt;
                    _currentScope = scope;

                    bool isBreakpoint = false;
                    if (FileBreakpoints.TryGetValue(_currentFilePath, out var lines))
                    {
                        isBreakpoint = lines.Contains(stmt.Line);
                    }

                    bool shouldPause = isBreakpoint || _stepping || _stopOnEntry || _pauseRequested;
                    if (shouldPause)
                    {
                        _stopOnEntry = false;
                        _pauseRequested = false;
                        _stepping = false;

                        string reason = isBreakpoint ? "breakpoint" : "step";
                        var stoppedBody = new JsonObject
                        {
                            ["reason"] = reason,
                            ["threadId"] = 1,
                            ["allThreadsStopped"] = true
                        };
                        SendEvent(stdout, "stopped", stoppedBody);

                        // Block until continue / step command received
                        StepEvent.WaitOne();
                    }
                }
            };

            _runningInterpreter = interpreter;
            interpreter.Run(program);

            // Program completed cleanly
            SendEvent(stdout, "terminated", new JsonObject());
            SendEvent(stdout, "exited", new JsonObject { ["exitCode"] = 0 });
        }
        catch (Exception ex)
        {
            if (!_terminated)
            {
                var errBody = new JsonObject
                {
                    ["category"] = "stderr",
                    ["output"] = $"Runtime Error: {ex.Message}\n"
                };
                SendEvent(stdout, "output", errBody);
                SendEvent(stdout, "terminated", new JsonObject());
                SendEvent(stdout, "exited", new JsonObject { ["exitCode"] = 1 });
            }
        }
    }

    private static void Cleanup()
    {
        _terminated = true;
        StepEvent.Set();
    }

    // =========================================================================
    // DAP Protocol Framing
    // =========================================================================

    private static void SendResponse(Stream stdout, int requestSeq, string command, bool success, JsonNode? body, string? message = null)
    {
        var resp = new JsonObject
        {
            ["seq"] = Interlocked.Increment(ref _seq),
            ["type"] = "response",
            ["request_seq"] = requestSeq,
            ["command"] = command,
            ["success"] = success
        };
        if (body != null) resp["body"] = body;
        if (message != null) resp["message"] = message;

        SendMessage(stdout, resp);
    }

    private static void SendEvent(Stream stdout, string eventName, JsonNode body)
    {
        var evt = new JsonObject
        {
            ["seq"] = Interlocked.Increment(ref _seq),
            ["type"] = "event",
            ["event"] = eventName,
            ["body"] = body
        };

        SendMessage(stdout, evt);
    }

    private static void SendMessage(Stream stdout, JsonNode message)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            message.WriteTo(writer);
        }
        byte[] body = ms.ToArray();
        string header = $"Content-Length: {body.Length}\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);

        lock (WriteLock)
        {
            try
            {
                stdout.Write(headerBytes, 0, headerBytes.Length);
                stdout.Write(body, 0, body.Length);
                stdout.Flush();
            }
            catch { }
        }
    }

    private static string? ReadHeaderLine(Stream stream)
    {
        var sb = new StringBuilder();
        int prev = -1;
        while (true)
        {
            int b = stream.ReadByte();
            if (b == -1) return sb.Length == 0 ? null : sb.ToString();
            if (b == '\n')
            {
                if (prev == '\r' && sb.Length > 0) sb.Length--;
                return sb.ToString();
            }
            sb.Append((char)b);
            prev = b;
        }
    }

    /// <summary>
    /// Custom TextWriter redirecting Otter stdout lines to DAP 'output' events.
    /// </summary>
    private sealed class DapOutputWriter(Stream stdout) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            var body = new JsonObject
            {
                ["category"] = "stdout",
                ["output"] = (value ?? "") + "\n"
            };
            SendEvent(stdout, "output", body);
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var body = new JsonObject
            {
                ["category"] = "stdout",
                ["output"] = value
            };
            SendEvent(stdout, "output", body);
        }
    }
}
