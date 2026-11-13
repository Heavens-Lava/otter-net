using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Cli;

/// <summary>
/// Native Language Server Protocol (LSP 3.17) implementation for Otter.
/// Communicates over standard input/output using JSON-RPC 2.0.
/// Reuses Otter.Language.LanguageService for parsing, diagnostics, formatting, and navigation.
/// </summary>
internal static partial class LspServer
{
    private static readonly ConcurrentDictionary<string, string> OpenDocuments = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> WorkspaceRoots = new();
    private static readonly object WriteLock = new();
    private static string? _libraryDir;

    [GeneratedRegex(@"^\s*to\s+([A-Za-z][A-Za-z0-9_]*)", RegexOptions.IgnoreCase)]
    private static partial Regex FuncDefRegex();

    [GeneratedRegex(@"^\s*a\s+([A-Za-z][A-Za-z0-9_]*)\s+has", RegexOptions.IgnoreCase)]
    private static partial Regex TypeDefRegex();

    [GeneratedRegex(@"^\s*([A-Za-z][A-Za-z0-9_]*)\s+has\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ObjectDefRegex();

    public static int Run(CommandLine line)
    {
        try
        {
            _libraryDir = LibraryCommands.FindLibraryDir();
        }
        catch { }

        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);

        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        while (true)
        {
            string? headerLine;
            int contentLength = -1;

            while ((headerLine = ReadHeaderLine(stdin)) != null)
            {
                if (string.IsNullOrEmpty(headerLine)) break; // Headers finished
                if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(headerLine["Content-Length:".Length..].Trim(), out contentLength);
                }
            }

            if (contentLength < 0)
            {
                break;
            }

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
                Console.Error.WriteLine($"[otter lsp] JSON parse error: {ex.Message}");
                continue;
            }

            if (req is not JsonObject reqObj) continue;

            bool isExit = HandleMessage(reqObj, stdout);
            if (isExit) break;
        }

        return Cli.ExitSuccess;
    }

    private static bool HandleMessage(JsonObject req, Stream stdout)
    {
        string? method = req["method"]?.ToString();
        var idNode = req["id"];

        if (string.IsNullOrEmpty(method)) return false;

        switch (method)
        {
            case "initialize":
                HandleInitialize(req, idNode, stdout);
                return false;

            case "initialized":
                // Client confirmed initialization
                return false;

            case "shutdown":
                if (idNode != null)
                {
                    SendResponse(stdout, idNode, null);
                }
                return false;

            case "exit":
                return true;

            case "textDocument/didOpen":
                HandleDidOpen(req, stdout);
                return false;

            case "textDocument/didChange":
                HandleDidChange(req, stdout);
                return false;

            case "textDocument/didClose":
                HandleDidClose(req, stdout);
                return false;

            case "textDocument/completion":
                HandleCompletion(req, idNode, stdout);
                return false;

            case "textDocument/hover":
                HandleHover(req, idNode, stdout);
                return false;

            case "textDocument/definition":
                HandleDefinition(req, idNode, stdout);
                return false;

            case "textDocument/references":
                HandleReferences(req, idNode, stdout);
                return false;

            case "textDocument/rename":
                HandleRename(req, idNode, stdout);
                return false;

            case "textDocument/formatting":
                HandleFormatting(req, idNode, stdout);
                return false;

            case "workspace/symbol":
                HandleWorkspaceSymbol(req, idNode, stdout);
                return false;

            default:
                if (idNode != null)
                {
                    SendError(stdout, idNode, -32601, $"Method not found: {method}");
                }
                return false;
        }
    }

    // =========================================================================
    // Protocol Handlers
    // =========================================================================

    private static void HandleInitialize(JsonObject req, JsonNode? id, Stream stdout)
    {
        var @params = req["params"];
        if (@params != null)
        {
            string? rootUri = @params["rootUri"]?.ToString();
            if (!string.IsNullOrEmpty(rootUri))
            {
                string path = UriToPath(rootUri);
                if (Directory.Exists(path) && !WorkspaceRoots.Contains(path))
                {
                    WorkspaceRoots.Add(path);
                }
            }
            var folders = @params["workspaceFolders"]?.AsArray();
            if (folders != null)
            {
                foreach (var f in folders)
                {
                    string? u = f?["uri"]?.ToString();
                    if (!string.IsNullOrEmpty(u))
                    {
                        string p = UriToPath(u);
                        if (Directory.Exists(p) && !WorkspaceRoots.Contains(p))
                        {
                            WorkspaceRoots.Add(p);
                        }
                    }
                }
            }
        }

        var triggerChars = new JsonArray();
        triggerChars.Add((JsonNode)JsonValue.Create(" ")!);
        triggerChars.Add((JsonNode)JsonValue.Create(".")!);
        triggerChars.Add((JsonNode)JsonValue.Create("\"")!);
        triggerChars.Add((JsonNode)JsonValue.Create("@")!);

        var capabilities = new JsonObject
        {
            ["textDocumentSync"] = new JsonObject
            {
                ["openClose"] = true,
                ["change"] = 1 // Full synchronization
            },
            ["completionProvider"] = new JsonObject
            {
                ["resolveProvider"] = false,
                ["triggerCharacters"] = triggerChars
            },
            ["hoverProvider"] = true,
            ["definitionProvider"] = true,
            ["referencesProvider"] = true,
            ["renameProvider"] = true,
            ["documentFormattingProvider"] = true,
            ["workspaceSymbolProvider"] = true
        };

        var result = new JsonObject
        {
            ["capabilities"] = capabilities,
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "otter-lsp",
                ["version"] = Program.Version
            }
        };

        SendResponse(stdout, id, result);
    }

    private static void HandleDidOpen(JsonObject req, Stream stdout)
    {
        var textDoc = req["params"]?["textDocument"];
        if (textDoc == null) return;

        string? uri = textDoc["uri"]?.ToString();
        string? text = textDoc["text"]?.ToString();
        if (string.IsNullOrEmpty(uri) || text == null) return;

        OpenDocuments[uri] = text;
        PublishDiagnostics(stdout, uri, text);
    }

    private static void HandleDidChange(JsonObject req, Stream stdout)
    {
        var @params = req["params"];
        if (@params == null) return;

        string? uri = @params["textDocument"]?["uri"]?.ToString();
        var changes = @params["contentChanges"]?.AsArray();
        if (string.IsNullOrEmpty(uri) || changes == null || changes.Count == 0) return;

        // With Full sync (1), the last element contains the complete updated text
        string? text = changes[^1]?["text"]?.ToString();
        if (text == null) return;

        OpenDocuments[uri] = text;
        PublishDiagnostics(stdout, uri, text);
    }

    private static void HandleDidClose(JsonObject req, Stream stdout)
    {
        string? uri = req["params"]?["textDocument"]?["uri"]?.ToString();
        if (string.IsNullOrEmpty(uri)) return;

        OpenDocuments.TryRemove(uri, out _);

        // Clear diagnostics on close
        var notif = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/publishDiagnostics",
            ["params"] = new JsonObject
            {
                ["uri"] = uri,
                ["diagnostics"] = new JsonArray()
            }
        };
        SendMessage(stdout, notif);
    }

    private static void HandleCompletion(JsonObject req, JsonNode? id, Stream stdout)
    {
        string? uri = req["params"]?["textDocument"]?["uri"]?.ToString();
        int line = req["params"]?["position"]?["line"]?.GetValue<int>() ?? 0;
        int col = req["params"]?["position"]?["character"]?.GetValue<int>() ?? 0;

        string source = GetDocumentSource(uri);
        string filePath = uri != null ? UriToPath(uri) : "";

        var completions = LanguageService.GetCompletions(source, line + 1, col + 1, filePath, _libraryDir);

        var itemsArray = new JsonArray();
        foreach (var c in completions)
        {
            var item = new JsonObject
            {
                ["label"] = c.Label,
                ["kind"] = MapCompletionKind(c.Kind),
                ["detail"] = c.Detail,
                ["insertText"] = c.InsertText,
                ["insertTextFormat"] = c.InsertText.Contains('$') ? 2 : 1 // 2 = Snippet format
            };
            if (!string.IsNullOrEmpty(c.Documentation))
            {
                item["documentation"] = new JsonObject
                {
                    ["kind"] = "markdown",
                    ["value"] = c.Documentation
                };
            }
            itemsArray.Add(item);
        }

        var result = new JsonObject
        {
            ["isIncomplete"] = false,
            ["items"] = itemsArray
        };

        SendResponse(stdout, id, result);
    }

    private static void HandleHover(JsonObject req, JsonNode? id, Stream stdout)
    {
        string? uri = req["params"]?["textDocument"]?["uri"]?.ToString();
        int line = req["params"]?["position"]?["line"]?.GetValue<int>() ?? 0;
        int col = req["params"]?["position"]?["character"]?.GetValue<int>() ?? 0;

        string source = GetDocumentSource(uri);
        string[] lines = source.Replace("\r\n", "\n").Split('\n');

        if (line < 0 || line >= lines.Length)
        {
            SendResponse(stdout, id, null);
            return;
        }

        string targetLine = lines[line];
        string? word = ExtractWordAtColumn(targetLine, col + 1);

        if (string.IsNullOrEmpty(word))
        {
            SendResponse(stdout, id, null);
            return;
        }

        var docs = LanguageService.LookupDocumentation(word, _libraryDir);
        if (docs.Count > 0)
        {
            var first = docs[0];
            var sb = new StringBuilder();
            sb.AppendLine($"### `{first.Name}` ({first.Kind})");
            sb.AppendLine($"**Source:** {first.Source}\n");
            sb.AppendLine($"```otter\n{first.Signature}\n```\n");
            sb.AppendLine(first.Description);
            if (!string.IsNullOrEmpty(first.Example))
            {
                sb.AppendLine("\n**Example:**");
                sb.AppendLine($"```otter\n{first.Example}\n```");
            }

            var hoverObj = new JsonObject
            {
                ["contents"] = new JsonObject
                {
                    ["kind"] = "markdown",
                    ["value"] = sb.ToString()
                }
            };
            SendResponse(stdout, id, hoverObj);
            return;
        }

        // Check if word matches a user-defined function in source
        for (int i = 0; i < lines.Length; i++)
        {
            var mFunc = FuncDefRegex().Match(lines[i]);
            if (mFunc.Success && string.Equals(mFunc.Groups[1].Value, word, StringComparison.OrdinalIgnoreCase))
            {
                string defLine = lines[i].Trim();
                var sb = new StringBuilder();
                sb.AppendLine($"### Function `{word}`");
                sb.AppendLine($"*Defined at line {i + 1}*\n");
                sb.AppendLine($"```otter\n{defLine}\n```");

                var hoverObj = new JsonObject
                {
                    ["contents"] = new JsonObject
                    {
                        ["kind"] = "markdown",
                        ["value"] = sb.ToString()
                    }
                };
                SendResponse(stdout, id, hoverObj);
                return;
            }
        }

        SendResponse(stdout, id, null);
    }

    private static void HandleDefinition(JsonObject req, JsonNode? id, Stream stdout)
    {
        string? uri = req["params"]?["textDocument"]?["uri"]?.ToString();
        int line = req["params"]?["position"]?["line"]?.GetValue<int>() ?? 0;
        int col = req["params"]?["position"]?["character"]?.GetValue<int>() ?? 0;

        string source = GetDocumentSource(uri);
        string filePath = uri != null ? UriToPath(uri) : "";

        var def = LanguageService.GetDefinition(source, line + 1, col + 1, filePath, _libraryDir);
        if (def == null)
        {
            SendResponse(stdout, id, null);
            return;
        }

        int targetLine = Math.Max(0, def.Line - 1);
        int targetCol = Math.Max(0, def.Column - 1);
        int length = def.Symbol.Length;

        var loc = new JsonObject
        {
            ["uri"] = PathToUri(def.File),
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = targetLine, ["character"] = targetCol },
                ["end"] = new JsonObject { ["line"] = targetLine, ["character"] = targetCol + length }
            }
        };

        SendResponse(stdout, id, loc);
    }

    private static void HandleReferences(JsonObject req, JsonNode? id, Stream stdout)
    {
        string? uri = req["params"]?["textDocument"]?["uri"]?.ToString();
        int line = req["params"]?["position"]?["line"]?.GetValue<int>() ?? 0;
        int col = req["params"]?["position"]?["character"]?.GetValue<int>() ?? 0;

        string source = GetDocumentSource(uri);
        string filePath = uri != null ? UriToPath(uri) : "";

        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        string? word = line >= 0 && line < lines.Length ? ExtractWordAtColumn(lines[line], col + 1) : null;
        if (string.IsNullOrEmpty(word))
        {
            SendResponse(stdout, id, new JsonArray());
            return;
        }

        int wordLen = word.Length;
        var locations = new JsonArray();

        var allFiles = GetAllWorkspaceFiles();
        if (!string.IsNullOrEmpty(filePath) && !allFiles.Contains(filePath))
        {
            allFiles.Add(filePath);
        }

        string pattern = @"\b" + Regex.Escape(word) + @"\b";
        var rx = new Regex(pattern, RegexOptions.IgnoreCase);

        foreach (string file in allFiles)
        {
            string fUri = PathToUri(file);
            string fSource = GetDocumentSource(fUri);
            if (string.IsNullOrEmpty(fSource)) continue;

            string[] fLines = fSource.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < fLines.Length; i++)
            {
                string l = fLines[i];
                if (l.Trim().StartsWith("#")) continue;

                var matches = rx.Matches(l);
                foreach (Match m in matches)
                {
                    locations.Add(new JsonObject
                    {
                        ["uri"] = fUri,
                        ["range"] = new JsonObject
                        {
                            ["start"] = new JsonObject { ["line"] = i, ["character"] = m.Index },
                            ["end"] = new JsonObject { ["line"] = i, ["character"] = m.Index + wordLen }
                        }
                    });
                }
            }
        }

        SendResponse(stdout, id, locations);
    }

    private static void HandleRename(JsonObject req, JsonNode? id, Stream stdout)
    {
        string? uri = req["params"]?["textDocument"]?["uri"]?.ToString();
        int line = req["params"]?["position"]?["line"]?.GetValue<int>() ?? 0;
        int col = req["params"]?["position"]?["character"]?.GetValue<int>() ?? 0;
        string newName = req["params"]?["newName"]?.ToString() ?? "";

        if (string.IsNullOrEmpty(uri))
        {
            SendError(stdout, id, -32602, "Missing document URI.");
            return;
        }

        string source = GetDocumentSource(uri);
        string filePath = UriToPath(uri);

        string[] sourceLines = source.Replace("\r\n", "\n").Split('\n');
        string? oldSymbol = line >= 0 && line < sourceLines.Length ? ExtractWordAtColumn(sourceLines[line], col + 1) : null;

        var renameResult = LanguageService.RenameSymbol(source, line + 1, col + 1, newName);
        if (!renameResult.Success)
        {
            SendError(stdout, id, -32602, renameResult.Error ?? "Rename failed.");
            return;
        }

        var allFiles = GetAllWorkspaceFiles();
        if (!string.IsNullOrEmpty(filePath) && !allFiles.Contains(filePath))
        {
            allFiles.Add(filePath);
        }

        var changes = new JsonObject();

        if (!string.IsNullOrEmpty(oldSymbol))
        {
            string pattern = @"\b" + Regex.Escape(oldSymbol) + @"\b";
            var rx = new Regex(pattern, RegexOptions.IgnoreCase);

            foreach (string file in allFiles)
            {
                string fUri = PathToUri(file);
                string fSource = GetDocumentSource(fUri);
                if (string.IsNullOrEmpty(fSource)) continue;

                if (string.Equals(file, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    int lastLineIdx = Math.Max(0, sourceLines.Length - 1);
                    int lastLineLen = sourceLines.Length > 0 ? sourceLines[^1].Length : 0;
                    changes[fUri] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["range"] = new JsonObject
                            {
                                ["start"] = new JsonObject { ["line"] = 0, ["character"] = 0 },
                                ["end"] = new JsonObject { ["line"] = lastLineIdx, ["character"] = lastLineLen }
                            },
                            ["newText"] = renameResult.NewSource
                        }
                    };
                }
                else if (rx.IsMatch(fSource))
                {
                    string[] fLines = fSource.Replace("\r\n", "\n").Split('\n');
                    (bool Success, string NewSource, int ChangesCount, string? Error) otherResult = (false, fSource, 0, null);

                    for (int fi = 0; fi < fLines.Length; fi++)
                    {
                        var m = rx.Match(fLines[fi]);
                        if (m.Success)
                        {
                            otherResult = LanguageService.RenameSymbol(fSource, fi + 1, m.Index + 1, newName);
                            break;
                        }
                    }

                    if (otherResult.Success)
                    {
                        int lastIdx = Math.Max(0, fLines.Length - 1);
                        int lastLen = fLines.Length > 0 ? fLines[^1].Length : 0;
                        changes[fUri] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["range"] = new JsonObject
                                {
                                    ["start"] = new JsonObject { ["line"] = 0, ["character"] = 0 },
                                    ["end"] = new JsonObject { ["line"] = lastIdx, ["character"] = lastLen }
                                },
                                ["newText"] = otherResult.NewSource
                            }
                        };
                    }
                }
            }
        }
        else
        {
            int lastLineIdx = Math.Max(0, sourceLines.Length - 1);
            int lastLineLen = sourceLines.Length > 0 ? sourceLines[^1].Length : 0;
            changes[uri] = new JsonArray
            {
                new JsonObject
                {
                    ["range"] = new JsonObject
                    {
                        ["start"] = new JsonObject { ["line"] = 0, ["character"] = 0 },
                        ["end"] = new JsonObject { ["line"] = lastLineIdx, ["character"] = lastLineLen }
                    },
                    ["newText"] = renameResult.NewSource
                }
            };
        }

        var edit = new JsonObject
        {
            ["changes"] = changes
        };

        SendResponse(stdout, id, edit);
    }

    private static void HandleFormatting(JsonObject req, JsonNode? id, Stream stdout)
    {
        string? uri = req["params"]?["textDocument"]?["uri"]?.ToString();
        if (string.IsNullOrEmpty(uri))
        {
            SendResponse(stdout, id, new JsonArray());
            return;
        }

        string source = GetDocumentSource(uri);
        string formatted = LanguageService.FormatDocument(source);

        if (source == formatted)
        {
            SendResponse(stdout, id, new JsonArray());
            return;
        }

        string[] sourceLines = source.Replace("\r\n", "\n").Split('\n');
        int lastLineIdx = Math.Max(0, sourceLines.Length - 1);
        int lastLineLen = sourceLines.Length > 0 ? sourceLines[^1].Length : 0;

        var edits = new JsonArray
        {
            new JsonObject
            {
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject { ["line"] = 0, ["character"] = 0 },
                    ["end"] = new JsonObject { ["line"] = lastLineIdx, ["character"] = lastLineLen }
                },
                ["newText"] = formatted
            }
        };

        SendResponse(stdout, id, edits);
    }

    private static List<string> GetAllWorkspaceFiles()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in OpenDocuments)
        {
            string p = UriToPath(kvp.Key);
            if (File.Exists(p)) files.Add(Path.GetFullPath(p));
        }
        foreach (var root in WorkspaceRoots)
        {
            if (Directory.Exists(root))
            {
                try
                {
                    foreach (var f in Directory.GetFiles(root, "*.ot", SearchOption.AllDirectories))
                    {
                        if (f.Contains(".git") || f.Contains("dist") || f.Contains("bin") || f.Contains("obj")) continue;
                        files.Add(Path.GetFullPath(f));
                    }
                }
                catch { }
            }
        }
        return files.ToList();
    }

    private static void HandleWorkspaceSymbol(JsonObject req, JsonNode? id, Stream stdout)
    {
        string query = req["params"]?["query"]?.ToString() ?? "";
        var symbols = new JsonArray();

        var allFiles = GetAllWorkspaceFiles();
        foreach (var f in allFiles)
        {
            string fUri = PathToUri(f);
            string content = GetDocumentSource(fUri);
            if (!string.IsNullOrEmpty(content))
            {
                ScanSymbolsInSource(fUri, content, query, symbols);
            }
        }

        SendResponse(stdout, id, symbols);
    }

    private static void ScanSymbolsInSource(string uri, string source, string query, JsonArray symbols)
    {
        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i];

            // Functions: `to <name>`
            var mf = FuncDefRegex().Match(l);
            if (mf.Success)
            {
                string name = mf.Groups[1].Value;
                if (string.IsNullOrEmpty(query) || name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    int col = l.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                    symbols.Add(new JsonObject
                    {
                        ["name"] = name,
                        ["kind"] = 12, // Function
                        ["location"] = new JsonObject
                        {
                            ["uri"] = uri,
                            ["range"] = new JsonObject
                            {
                                ["start"] = new JsonObject { ["line"] = i, ["character"] = col },
                                ["end"] = new JsonObject { ["line"] = i, ["character"] = col + name.Length }
                            }
                        }
                    });
                }
            }

            // Types: `a <Type> has`
            var mt = TypeDefRegex().Match(l);
            if (mt.Success)
            {
                string name = mt.Groups[1].Value;
                if (string.IsNullOrEmpty(query) || name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    int col = l.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                    symbols.Add(new JsonObject
                    {
                        ["name"] = name,
                        ["kind"] = 5, // Class
                        ["location"] = new JsonObject
                        {
                            ["uri"] = uri,
                            ["range"] = new JsonObject
                            {
                                ["start"] = new JsonObject { ["line"] = i, ["character"] = col },
                                ["end"] = new JsonObject { ["line"] = i, ["character"] = col + name.Length }
                            }
                        }
                    });
                }
            }
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static void PublishDiagnostics(Stream stdout, string uri, string source)
    {
        string filePath = UriToPath(uri);
        var diagItems = LanguageService.GetDiagnostics(source, filePath);

        var diagArray = new JsonArray();
        foreach (var d in diagItems)
        {
            int line0 = Math.Max(0, d.Line - 1);
            int col0 = Math.Max(0, d.Column - 1);
            string msg = !string.IsNullOrEmpty(d.Suggestion)
                ? $"{d.Message} (Suggestion: {d.Suggestion})"
                : d.Message;

            diagArray.Add(new JsonObject
            {
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject { ["line"] = line0, ["character"] = col0 },
                    ["end"] = new JsonObject { ["line"] = line0, ["character"] = 999 }
                },
                ["severity"] = 1, // 1 = Error
                ["source"] = "otter",
                ["message"] = msg
            });
        }

        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/publishDiagnostics",
            ["params"] = new JsonObject
            {
                ["uri"] = uri,
                ["diagnostics"] = diagArray
            }
        };

        SendMessage(stdout, notification);
    }

    private static string GetDocumentSource(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return "";
        if (OpenDocuments.TryGetValue(uri, out string? openText))
        {
            return openText;
        }

        string path = UriToPath(uri);
        if (File.Exists(path))
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch { }
        }
        return "";
    }

    private static string UriToPath(string uriStr)
    {
        try
        {
            if (Uri.TryCreate(uriStr, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                return uri.LocalPath;
            }
        }
        catch { }

        if (uriStr.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return uriStr[7..];
        }
        return uriStr;
    }

    private static string PathToUri(string path)
    {
        try
        {
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }
        catch
        {
            return "file://" + path.Replace('\\', '/');
        }
    }

    private static string? ExtractWordAtColumn(string line, int col)
    {
        if (string.IsNullOrEmpty(line) || col <= 0) return null;
        int idx = col - 1;
        if (idx >= line.Length) idx = line.Length - 1;

        if (!char.IsLetterOrDigit(line[idx]) && line[idx] != '_' && idx > 0)
        {
            idx--;
        }

        if (!char.IsLetterOrDigit(line[idx]) && line[idx] != '_') return null;

        int start = idx;
        while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] == '_')) start--;

        int end = idx;
        while (end < line.Length - 1 && (char.IsLetterOrDigit(line[end + 1]) || line[end + 1] == '_')) end++;

        return line.Substring(start, end - start + 1);
    }

    private static int MapCompletionKind(string kind) => kind.ToLowerInvariant() switch
    {
        "keyword" => 14,
        "operator" => 24,
        "function" => 3,
        "parameter" => 6,
        "variable" => 6,
        "type" => 7,
        "snippet" => 15,
        _ => 1
    };

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

    private static void SendResponse(Stream stdout, JsonNode? id, JsonNode? result)
    {
        var resp = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result?.DeepClone()
        };
        SendMessage(stdout, resp);
    }

    private static void SendError(Stream stdout, JsonNode? id, int code, string message)
    {
        var resp = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        SendMessage(stdout, resp);
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
            stdout.Write(headerBytes, 0, headerBytes.Length);
            stdout.Write(body, 0, body.Length);
            stdout.Flush();
        }
    }
}
