using System.Text.RegularExpressions;

namespace Otter.Language;

// otterPS's check stage (src/Otter.Validation.psm1, decision D-2) refuses
// names the grammar would silently misread: `to main` can never be called,
// and a variable named `completed` is read as a state check in a condition.
// Every such declaration is reported, in source order, with otterPS's
// message, before anything runs.
public static class ReservedWords
{
    const string FunctionUi = "a line starting with `{0}` is read as a UI element (D56 declarative UI), which does nothing when the program runs in the console, so a function named `{0}` could never be called";
    const string FunctionStatement = "a line starting with `{0}` is read as the `{0}` statement, so a function named `{0}` could never be called";
    const string FunctionKeyword = "`{0}` is part of Otter's own grammar and a line cannot start with it, so a function named `{0}` could never be called";
    const string FunctionBuiltin = "`{0}` is a built-in value, so a function named `{0}` could never be called";
    const string StateHttp = "in a condition, `x is {0}` is read as a check of an HTTP request's or command job's state, not a comparison with a variable named `{0}`";
    const string StateSocket = "in a condition, `x is {0}` is read as a check of a websocket's, TCP connection's or UDP socket's state, not a comparison with a variable named `{0}`";
    const string StateServer = "in a condition, `x is {0}` is read as a check of a TCP server's state, not a comparison with a variable named `{0}`";
    const string StateTls = "in a condition, `x is {0}` is read as a check of whether a TCP connection uses TLS, not a comparison with a variable named `{0}`";
    const string StateWatch = "in a condition, `x is {0}` is read as a check of whether a file watcher is running, not a comparison with a variable named `{0}`";

    static readonly Dictionary<string, string> FunctionNames = Build(
        (FunctionUi, new[] { "window", "page", "card", "heading", "text", "button", "panel", "section",
                             "sidebar", "main", "link", "image", "input", "grid" }),
        (FunctionStatement, new[] { "append", "average", "cancel", "choose", "commit", "connect", "convert", "copy",
                             "count", "create", "decrease", "decrypt", "delete", "derive", "disconnect",
                             "download", "each", "encrypt", "error", "execute", "fail", "find", "focus",
                             "format", "get", "hash", "hide", "increase", "join", "kill", "layout", "listen",
                             "lock", "log", "maximum", "memo", "minimum", "move", "notify", "on", "post",
                             "print", "query", "random", "read", "replace", "respond", "restart", "reverse",
                             "rollback", "route", "run", "send", "shared", "shut", "sign", "sort", "split",
                             "start", "state", "stop", "sum", "try", "unzip", "use", "wait", "warn", "write", "zip" }),
        (FunctionKeyword, new[] { "animate", "ascending", "between", "descending", "distinct", "file", "files",
                             "folder", "folders", "gap", "has", "json", "motion", "otherwise", "parameter", "then" }),
        (FunctionBuiltin, new[] { "today", "now", "pi" }));

    static readonly Dictionary<string, string> VariableNames = Build(
        (StateHttp, new[] { "pending", "running", "completed", "failed", "cancelled" }),
        (StateSocket, new[] { "connecting", "closing", "connected", "closed" }),
        (StateServer, new[] { "listening", "stopped" }),
        (StateTls, new[] { "secure" }),
        (StateWatch, new[] { "watching" }));

    static Dictionary<string, string> Build(params (string Reason, string[] Words)[] groups)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (reason, words) in groups) foreach (string word in words) table[word] = reason;
        return table;
    }

    enum Role { Function, Variable }

    // Get-OtterLanguageContractDiagnostics: every reserved declaration,
    // ordered by line and column. sourceLines are the program's lines.
    public static List<OtterError> Diagnostics(ProgramNode program, string[] sourceLines)
    {
        var walk = new Walk(sourceLines);
        walk.Statements(program.Statements);
        return walk.Errors.OrderBy(e => e.Line).ThenBy(e => e.Column).ToList();
    }

    sealed class Walk(string[] sourceLines)
    {
        public readonly List<OtterError> Errors = new();
        readonly HashSet<string> seen = new(StringComparer.Ordinal);

        public void Statements(IEnumerable<Stmt>? statements)
        {
            if (statements == null) return;
            foreach (Stmt statement in statements) Statement(statement);
        }

        void Variable(string? name, int line) => Finding(name, Role.Variable, line, 0);

        void Statement(Stmt statement)
        {
            int line = statement.Line;
            switch (statement)
            {
                case FunctionDefStmt function:
                    Finding(function.Name, Role.Function, line, 0);
                    // Parameters come after the function name on the same line.
                    int afterName = WordColumn(line, function.Name, 0);
                    foreach (string parameter in function.Parameters) Finding(parameter, Role.Variable, line, afterName);
                    Statements(function.Body);
                    break;
                case AssignStmt { Target: VariableExpr variable }: Variable(variable.Name, line); break;
                case AskStmt ask: Variable(ask.Name, line); break;
                case ListDefStmt list: Variable(list.Name, line); break;
                case ObjectDefStmt thing:
                    Variable(thing.Name, line);
                    Statements(thing.Properties);
                    break;
                // `into x`, `make x`, `for each x`, `count ... as x`, `otherwise into e`
                case MathIntoStmt math: Variable(math.Target, line); break;
                case AddToStmt add: Variable(add.Target, line); break;
                case RemoveFromStmt remove: Variable(remove.Target, line); break;
                case CallStmt call: Variable(call.ResultTarget, line); break;
                case CountStmt count: Variable(count.VariableName, line); Statements(count.Body); break;
                case ForEachStmt forEach: Variable(forEach.VariableName, line); Statements(forEach.Body); break;
                case SortStmt sort: Variable(sort.Target, line); break;
                case ReverseStmt reverse: Variable(reverse.Target, line); break;
                case ReplaceStmt replace: Variable(replace.Target, line); Variable(replace.ResultTarget, line); break;
                case SplitStmt split: Variable(split.Target, line); break;
                case JoinStmt join: Variable(join.Target, line); break;
                case FindStmt find: Variable(find.ItemName, line); Variable(find.Target, line); break;
                case GetKeyStmt get: Variable(get.ResultTarget, line); break;
                case DateDifferenceStmt difference: Variable(difference.Target, line); break;
                case DateAdjustStmt adjust: Variable(adjust.Target, line); break;
                case FormatDateStmt format: Variable(format.Target, line); break;
                case ReadFileStmt read: Variable(read.Target, line); break;
                case ReadJsonStmt readJson: Variable(readJson.Target, line); break;
                case ConvertJsonStmt convert: Variable(convert.Target, line); break;
                case GetEntriesStmt entries: Variable(entries.Target, line); break;
                case RandomNumberStmt randomNumber: Variable(randomNumber.Target, line); break;
                case RandomItemStmt randomItem: Variable(randomItem.Target, line); break;
                case TryStmt attempt:
                    Variable(attempt.ErrorTarget, line);
                    Statements(attempt.Body);
                    Statements(attempt.OtherwiseBody);
                    break;
                case IfStmt choice:
                    foreach (IfBranch branch in choice.Branches) Statements(branch.Body);
                    Statements(choice.ElseBody);
                    break;
                case WhileStmt loop: Statements(loop.Body); break;
                case RepeatStmt repeat: Statements(repeat.Body); break;
            }
        }

        void Finding(string? word, Role role, int line, int after)
        {
            if (string.IsNullOrEmpty(word)) return;
            var table = role == Role.Function ? FunctionNames : VariableNames;
            if (!table.TryGetValue(word.ToLowerInvariant(), out string? reason)) return;
            string key = $"{(role == Role.Function ? "function" : "variable")}|{word.ToLowerInvariant()}|{line}";
            if (!seen.Add(key)) return;

            string alternative = role == Role.Function ? $"{word}Task" : $"{word}Value";
            string message = $"`{word}` is a reserved word in Otter: {string.Format(reason, word)}. Choose a different name, for example `{alternative}`.";
            string? sourceLine = line >= 1 && line <= sourceLines.Length ? sourceLines[line - 1] : null;
            string suggestion = $"Rename `{word}` everywhere it is used, for example to `{alternative}`. See docs/OTTER_1_0_RESERVED_WORDS.md.";
            Errors.Add(new OtterError(message, line, "parser", WordColumn(line, word, after), sourceLine, suggestion, "ReservedWord"));
        }

        // Get-OtterWordColumn: where the word stands on its line, 1-based.
        int WordColumn(int line, string word, int after)
        {
            if (line < 1 || line > sourceLines.Length) return 0;
            string text = sourceLines[line - 1];
            int start = Math.Min(after, text.Length);
            var pattern = new Regex(@"(?<![\p{L}\p{Nd}_])" + Regex.Escape(word) + @"(?![\p{L}\p{Nd}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Match match = pattern.Match(text.Substring(start));
            return match.Success ? match.Index + start + 1 : 0;
        }
    }
}
