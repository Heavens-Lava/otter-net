namespace Otter.Language;

// Thrown when a program uses something otter-net has not ported yet. The
// `otter` command then hands the whole program to the PowerShell Otter, so
// the user gets exactly today's behaviour - before anything has run.
// An attempted reading of a line that does not fit, so the parser goes back
// and reads it another way (otterPS's try/catch backtracking).
sealed class NotThisForm() : Exception("not this form");

public sealed class NotSupportedYet(string feature, int line) : Exception($"line {line}: {feature}")
{
    public string Feature { get; } = feature;
    public int Line { get; } = line;
}

// Tokens become the syntax tree. A port of otterPS's src/Otter.Parser.psm1
// for the core language, function by function, with the same messages.
//
// Where otterPS takes one of its special forms that otter-net does not run yet
// (UI, networking, queries ...), this parser throws NotSupportedYet instead of
// guessing, even in the middle of reporting syntax errors, so the PowerShell
// Otter reports that program. Syntax errors are collected exactly as otterPS
// collects them (D117): each damaged statement is recorded, the parser skips
// to the next line or statement and goes on, up to 100 errors.
public sealed class Parser
{
    readonly IReadOnlyList<Token> tokens;
    int position;
    int blockDepth;
    bool conditionUsesContinuation;
    // A plain PowerShell hashtable in otterPS: function names are matched
    // ignoring case at parse time (variables are not - D10).
    readonly Dictionary<string, int> knownFunctions = new(StringComparer.OrdinalIgnoreCase);
    // Declared with `a Person has` (also a case-insensitive hashtable in otterPS).
    readonly HashSet<string> knownTypes = new(StringComparer.OrdinalIgnoreCase);

    // ---- syntax-error recovery (otterPS's Record-OtterParserError) ----
    const int MaxDiagnostics = 100;
    readonly List<OtterError> diagnostics = new();
    bool inRecoveryMode;
    bool hitMaxDiagnostics;

    Parser(IReadOnlyList<Token> tokens) => this.tokens = tokens;

    // Every syntax error in the program, or the program. Throws
    // OtterMultipleErrors (one or more errors) as otterPS's ConvertTo-OtterAst
    // does, and NotSupportedYet for anything not ported.
    public static ProgramNode Parse(IReadOnlyList<Token> tokens)
    {
        var parser = new Parser(tokens);
        var statements = parser.ReadStatements();
        if (parser.Is(TokenKind.BlockEnd))
        {
            Token token = parser.Read();
            parser.Record(parser.Error("There is no open block for this period to close.", token, "Remove this period or place it after an indented block.", "UnexpectedPeriod"));
        }
        if (!parser.Is(TokenKind.EndOfFile))
        {
            Token token = parser.Current;
            parser.Record(parser.Error($"Unexpected input at end of program: '{token.Text}'.", token, "Make sure all statements and blocks are valid.", "TrailingInput"));
        }
        if (parser.diagnostics.Count > 0) throw new OtterMultipleErrors(parser.diagnostics);
        return new ProgramNode(statements);
    }

    // Only the first error of a damaged statement counts; the rest would be
    // echoes of it.
    void Record(OtterError error)
    {
        if (hitMaxDiagnostics) return;
        if (diagnostics.Count >= MaxDiagnostics)
        {
            hitMaxDiagnostics = true;
            diagnostics.Add(new OtterError("Too many syntax errors; parsing stopped.", error.Line, "parser", error.Column,
                SourceLine(error.Line), "Fix the reported errors and run check again.", "TooManyErrors"));
            return;
        }
        if (!inRecoveryMode)
        {
            diagnostics.Add(error);
            inRecoveryMode = true;
        }
    }

    static readonly HashSet<TokenKind> StatementStarterKinds = new()
    {
        TokenKind.Say, TokenKind.If, TokenKind.While, TokenKind.Repeat, TokenKind.Count, TokenKind.ForEach,
        TokenKind.Set, TokenKind.Make, TokenKind.Write, TokenKind.Get, TokenKind.Post, TokenKind.Put,
        TokenKind.Delete, TokenKind.Start, TokenKind.Return, TokenKind.On, TokenKind.To, TokenKind.Use,
        TokenKind.Try, TokenKind.Log, TokenKind.Warn, TokenKind.Problem, TokenKind.Connect,
        TokenKind.Disconnect, TokenKind.Query, TokenKind.Execute, TokenKind.BeginTransaction,
        TokenKind.Commit, TokenKind.Rollback, TokenKind.Notify, TokenKind.Choose, TokenKind.Download,
        TokenKind.State, TokenKind.Derive, TokenKind.Await, TokenKind.A, TokenKind.Number,
    };

    static readonly HashSet<string> StatementStarterTexts = new(StringComparer.Ordinal)
    {
        "say", "if", "while", "repeat", "count", "for", "each", "set", "make",
        "write", "get", "post", "put", "delete", "start", "cancel", "return",
        "stop", "on", "to", "use", "try", "log", "warn", "problem", "connect",
        "disconnect", "query", "execute", "begin", "commit", "rollback",
        "notify", "choose", "download", "state", "derive", "await", "listen",
        "watch", "lock", "unlock", "open", "route", "go", "replace", "remove", "add",
        "window", "page", "card", "heading", "text", "button", "panel", "section",
        "sidebar", "main", "link", "image", "input", "grid", "primary", "secondary", "danger",
    };

    static bool IsStatementStarter(Token token) =>
        StatementStarterKinds.Contains(token.Kind) ||
        (token.Text.Length > 0 && StatementStarterTexts.Contains(token.Text.ToLowerInvariant()));

    // Skip the rest of a damaged statement: to the end of its line, or to
    // the next statement word, never past the end of the block it is in.
    void Synchronize(int failedPosition)
    {
        int nestedDepth = 0;
        while (position < tokens.Count)
        {
            Token token = Current;
            if (token.Kind == TokenKind.EndOfFile) break;
            if (token.Kind == TokenKind.Indent)
            {
                nestedDepth++;
                Read();
                continue;
            }
            if (token.Kind == TokenKind.Dedent)
            {
                if (nestedDepth > 0)
                {
                    nestedDepth--;
                    Read();
                    continue;
                }
                break;
            }
            if (token.Kind == TokenKind.BlockEnd && nestedDepth == 0) break;
            if (token.Kind == TokenKind.Newline)
            {
                Read();
                if (nestedDepth == 0) break;
                continue;
            }
            if (nestedDepth == 0 && position > failedPosition && IsStatementStarter(token)) break;
            Read();
        }

        // An indented block under the damaged line goes with it.
        if (Is(TokenKind.Indent))
        {
            Read();
            int depth = 1;
            while (depth > 0 && !Is(TokenKind.EndOfFile))
            {
                Token token = Read();
                if (token.Kind == TokenKind.Indent) depth++;
                else if (token.Kind == TokenKind.Dedent) depth--;
            }
            if (Is(TokenKind.BlockEnd)) Read();
        }

        // Always make progress.
        if (position <= failedPosition && !Is(TokenKind.EndOfFile)) Read();
    }

    // ---- token helpers -----------------------------------------------------

    Token Current => tokens[position];
    Token? At(int offset) => position + offset >= 0 && position + offset < tokens.Count ? tokens[position + offset] : null;
    bool Is(TokenKind kind) => Current.Kind == kind;
    bool IsAt(int offset, TokenKind kind) => At(offset)?.Kind == kind;
    Token Read() => tokens[position++];
    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    static bool SameAny(string text, params string[] words) => words.Any(w => Same(text, w));

    // otterPS rebuilds the quoted line from its tokens, joined by one space.
    string SourceLine(int line) => string.Join(" ", tokens
        .Where(t => t.Line == line && t.Kind is not (TokenKind.Indent or TokenKind.Dedent or TokenKind.Newline or TokenKind.EndOfFile))
        .Select(t => t.Text));

    OtterError Error(string message, Token token, string? suggestion = null, string? code = null) =>
        new(message, token.Line, "parser", token.Column, SourceLine(token.Line), suggestion, code);

    Token Expect(TokenKind kind, string message, string suggestion = "Check the expected word and try again.")
    {
        Token token = Current;
        if (token.Kind != kind)
        {
            string found = token.Kind == TokenKind.EndOfFile ? "end of file" : $"'{token.Text}'";
            throw Error($"{message} I found {found} instead.", token, suggestion);
        }
        return Read();
    }

    void SkipNewlines() { while (Is(TokenKind.Newline)) Read(); }

    NotSupportedYet Unsupported(string feature) => new(feature, Current.Line);

    static readonly HashSet<TokenKind> IdentifierKinds = new()
    {
        TokenKind.Identifier, TokenKind.File, TokenKind.Files, TokenKind.Folder, TokenKind.Folders, TokenKind.Has,
        TokenKind.Copy, TokenKind.Move, TokenKind.Delete, TokenKind.Create, TokenKind.Read, TokenKind.Write, TokenKind.Append,
        TokenKind.Sort, TokenKind.Reverse, TokenKind.Replace, TokenKind.Split, TokenKind.Join, TokenKind.Find,
        TokenKind.Get, TokenKind.Try, TokenKind.Run, TokenKind.Log, TokenKind.Warn, TokenKind.Problem,
        TokenKind.Random, TokenKind.Json, TokenKind.Csv, TokenKind.Convert, TokenKind.Format, TokenKind.Today, TokenKind.Now,
        TokenKind.Between, TokenKind.Otherwise, TokenKind.ForEach, TokenKind.Count, TokenKind.Notify, TokenKind.Choose,
        TokenKind.Download, TokenKind.Connect, TokenKind.Disconnect, TokenKind.Query, TokenKind.Execute, TokenKind.Commit,
        TokenKind.Rollback, TokenKind.Parameter, TokenKind.Distinct, TokenKind.Then, TokenKind.Ascending,
        TokenKind.Descending, TokenKind.Sum, TokenKind.Average, TokenKind.Minimum, TokenKind.Maximum,
    };

    static bool IsIdentifier(Token token) => IdentifierKinds.Contains(token.Kind);

    static readonly HashSet<TokenKind> TimeUnitKinds = new()
    {
        TokenKind.Year, TokenKind.Month, TokenKind.Day, TokenKind.Hour, TokenKind.Minute, TokenKind.Second,
    };

    static TimeUnit ToTimeUnit(TokenKind kind) => kind switch
    {
        TokenKind.Year => TimeUnit.Year,
        TokenKind.Month => TimeUnit.Month,
        TokenKind.Day => TimeUnit.Day,
        TokenKind.Hour => TimeUnit.Hour,
        TokenKind.Minute => TimeUnit.Minute,
        _ => TimeUnit.Second,
    };

    // ---- statements --------------------------------------------------------

    List<Stmt> ReadStatements()
    {
        var statements = new List<Stmt>();
        SkipNewlines();
        while (!Is(TokenKind.Dedent) && !Is(TokenKind.EndOfFile) && !Is(TokenKind.BlockEnd))
        {
            if (hitMaxDiagnostics) break;
            int before = position;
            try
            {
                inRecoveryMode = false;
                statements.Add(ReadStatement());
            }
            catch (OtterError error)
            {
                Record(error);
                Synchronize(before);
            }
            SkipNewlines();
        }
        return statements;
    }

    List<Stmt> ReadBlock()
    {
        Expect(TokenKind.Newline, "I expected the statement to end here.");
        Expect(TokenKind.Indent, "I expected an indented block after this statement.");
        blockDepth++;
        List<Stmt> body;
        try { body = ReadStatements(); }
        finally { blockDepth--; }
        Expect(TokenKind.Dedent, "I expected the indented block to end.");
        if (Is(TokenKind.BlockEnd)) { Read(); SkipNewlines(); }
        return body;
    }

    // The cases of Read-OtterStatement's main switch in otterPS.
    static readonly HashSet<TokenKind> OtterPsStatementKinds = new()
    {
        TokenKind.Await, TokenKind.State, TokenKind.Derive, TokenKind.Memo, TokenKind.On, TokenKind.Shared, TokenKind.Use,
        TokenKind.Focus, TokenKind.Hide, TokenKind.Layout, TokenKind.Say, TokenKind.If, TokenKind.When, TokenKind.Respond,
        TokenKind.Start, TokenKind.Listen, TokenKind.Post, TokenKind.Put, TokenKind.Show, TokenKind.While, TokenKind.Repeat,
        TokenKind.Count, TokenKind.Sum, TokenKind.Average, TokenKind.Minimum, TokenKind.Maximum, TokenKind.ForEach,
        TokenKind.Ask, TokenKind.Get, TokenKind.Set, TokenKind.Create, TokenKind.Try, TokenKind.Fail, TokenKind.Kill,
        TokenKind.Wait, TokenKind.Lock, TokenKind.Sign, TokenKind.Restart, TokenKind.Shut, TokenKind.Print, TokenKind.Zip,
        TokenKind.Unzip, TokenKind.Hash, TokenKind.Encrypt, TokenKind.Decrypt, TokenKind.Sort, TokenKind.Reverse,
        TokenKind.Replace, TokenKind.Split, TokenKind.Join, TokenKind.Find, TokenKind.Format, TokenKind.Log, TokenKind.Warn,
        TokenKind.Problem, TokenKind.Random, TokenKind.Convert, TokenKind.Read, TokenKind.Write, TokenKind.Append,
        TokenKind.Copy, TokenKind.Move, TokenKind.Delete, TokenKind.Download, TokenKind.Connect, TokenKind.Disconnect,
        TokenKind.Query, TokenKind.Execute, TokenKind.BeginTransaction, TokenKind.Commit, TokenKind.Rollback,
        TokenKind.Notify, TokenKind.Choose, TokenKind.Run, TokenKind.A, TokenKind.To, TokenKind.Return, TokenKind.Add,
        TokenKind.Remove, TokenKind.Identifier, TokenKind.Number, TokenKind.Indent, TokenKind.Dedent,
    };

    // Words that start one of otterPS's special statement forms before its
    // main statement switch. otter-net does not run those yet.
    static readonly string[] SpecialStatementWords =
    {
        "the", "close", "stop", "cancel", "store", "generate", "open", "listen", "send",
        "route", "go", "watch", "primary", "secondary", "danger",
        "window", "page", "card", "heading", "text", "button", "panel", "section", "sidebar", "main",
        "link", "image", "input", "grid",
    };

    Stmt ReadStatement()
    {
        Token start = Current;

        // days between startDate and endDate make days (D32)
        if (TimeUnitKinds.Contains(start.Kind) && IsAt(1, TokenKind.Between))
        {
            Token unit = Read();
            Read();
            Expr from = ReadValue();
            Expect(TokenKind.And, "I expected \"and\" and the second date.");
            Expr to = ReadValue();
            Expect(TokenKind.Make, "I expected \"make\" and a result name.");
            Token target = ReadVariableName("I expected a result name after \"make\".");
            Expect(TokenKind.Newline, "I expected the difference statement to end here.");
            return new DateDifferenceStmt(ToTimeUnit(unit.Kind), from, to, target.Text, start.Line);
        }
        if (IsAggregateQueryAhead()) throw Unsupported("queries");

        TokenKind statementKind = start.Kind;
        TokenKind nextKind = At(1)?.Kind ?? TokenKind.EndOfFile;
        if (start.Kind == TokenKind.Count && blockDepth == 0 && nextKind is TokenKind.Is or TokenKind.Are or TokenKind.IsNot)
        {
            throw Error("'count' is a reserved statement keyword and cannot be used as a variable name here.", start,
                "Choose a different variable name, such as \"total\" or \"itemCount\".");
        }
        if ((IsIdentifier(start) || (start.Kind == TokenKind.ForEach && Same(start.Text, "each"))) &&
            nextKind is TokenKind.Is or TokenKind.Are or TokenKind.Of or TokenKind.IsNot)
        {
            statementKind = TokenKind.Identifier;
        }
        if (statementKind == TokenKind.Identifier && SameAny(start.Text, "today", "now", "pi"))
        {
            throw Error($"'{start.Text}' is a built-in value, not a variable name.", start,
                "Choose a different variable name, such as \"currentDate\", \"currentTime\", or \"circleRatio\".");
        }
        // Each of these words takes over its line in otterPS unless the next
        // word makes the line an ordinary assignment or list (`text is "hi"`).
        // otter-net leaves all those forms to the PowerShell Otter for now.
        if (Same(start.Text, "the") ||
            (Same(start.Text, "stop") && nextKind != TokenKind.Newline) ||
            (SameAny(start.Text, SpecialStatementWords) && !Same(start.Text, "stop") &&
             nextKind is not (TokenKind.Is or TokenKind.Are or TokenKind.Of or TokenKind.IsNot or TokenKind.Has or TokenKind.Make or TokenKind.Into)))
        {
            throw Unsupported($"the '{start.Text}' statement");
        }

        switch (statementKind)
        {
            case TokenKind.Say:
            {
                Read();
                var parts = new List<Expr>();
                while (!Is(TokenKind.Newline) && !Is(TokenKind.In)) parts.Add(ReadMathExpression());
                Expr? color = null;
                if (Is(TokenKind.In))
                {
                    Read();
                    Token colorWord = Current;
                    if (colorWord.Kind != TokenKind.Identifier || !Same(colorWord.Text, "color"))
                    {
                        throw Error("I expected \"color\" after \"in\".", colorWord, "say \"Error!\" in color \"red\"");
                    }
                    Read();
                    color = ReadValue();
                }
                Expect(TokenKind.Newline, "I expected the say statement to end here.");
                return new SayStmt(parts, color, start.Line);
            }
            case TokenKind.If:
            {
                Read();
                var branches = new List<IfBranch> { new(ReadHeaderCondition(), ReadConditionBlock()) };
                while (Is(TokenKind.Otherwise) && IsAt(1, TokenKind.If))
                {
                    Read(); Read();
                    branches.Add(new IfBranch(ReadHeaderCondition(), ReadConditionBlock()));
                }
                List<Stmt>? elseBody = null;
                if (Is(TokenKind.Otherwise)) { Read(); elseBody = ReadBlock(); }
                return new IfStmt(branches, elseBody, start.Line);
            }
            case TokenKind.While:
            {
                Read();
                return new WhileStmt(ReadHeaderCondition(), ReadConditionBlock(), start.Line);
            }
            case TokenKind.Repeat:
            {
                Read();
                // "times" ends the count, so it is not read as multiplication.
                Expr count = ReadValue();
                Expect(TokenKind.Times, "I expected \"times\" after the repeat count.");
                return new RepeatStmt(count, ReadBlock(), start.Line);
            }
            case TokenKind.Count:
            {
                if (!IsAt(1, TokenKind.From)) throw Unsupported("queries");
                Read();
                Expect(TokenKind.From, "I expected \"from\" after count.");
                Expr from = ReadMathExpression();
                Expect(TokenKind.To, "I expected \"to\" after the starting number.");
                Expr to = ReadMathExpression();
                Expect(TokenKind.As, "I expected \"as\" before the counter name.");
                Token name = ReadVariableName("I expected a counter name.");
                return new CountStmt(name.Text, from, to, ReadBlock(), start.Line);
            }
            case TokenKind.ForEach:
            {
                Read();
                Token name = ReadVariableName("I expected a loop variable after \"for each\".");
                Expect(TokenKind.In, "I expected \"in\" after the loop variable.");
                Expr collection = ReadValue();
                return new ForEachStmt(name.Text, collection, ReadBlock(), start.Line);
            }
            case TokenKind.Ask:
            {
                Read();
                bool secret = Current.Kind == TokenKind.Identifier && Same(Current.Text, "secretly");
                if (secret) Read();
                Expr prompt = ReadValue();
                Expect(TokenKind.And, "I expected \"and call it\" after the question.");
                Expect(TokenKind.Call, "I expected \"call\" after \"and\".");
                Expect(TokenKind.It, "I expected \"it\" after \"call\".");
                Token name = ReadVariableName("I expected a variable name after \"call it\".");
                Expect(TokenKind.Newline, "I expected the question to end here.");
                return new AskStmt(prompt, name.Text, secret, start.Line);
            }
            case TokenKind.To:
            {
                Read();
                Token name = ReadFunctionName();
                var parameters = new List<string>();
                while (!Is(TokenKind.Newline))
                {
                    if (Is(TokenKind.And)) { Read(); continue; }
                    parameters.Add(ReadVariableName("I expected a parameter name.").Text);
                }
                // Visible from its own body onward, so a function can call itself.
                knownFunctions[name.Text] = parameters.Count;
                return new FunctionDefStmt(name.Text, parameters, ReadBlock(), start.Line);
            }
            case TokenKind.Return:
            {
                Read();
                if (Same(start.Text, "stop"))
                {
                    if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "watching")) throw Unsupported("stop watching");
                    Expect(TokenKind.Newline, "I expected stop to end here.");
                    return new ReturnStmt(null, start.Line);
                }
                Expr value = ReadMathExpression();
                Expect(TokenKind.Newline, "I expected the return statement to end here.");
                return new ReturnStmt(value, start.Line);
            }
            case TokenKind.A:
            {
                Read();
                Token typeName = ReadVariableName("I expected a type name after \"a\".");
                Expect(TokenKind.Has, "I expected \"has\" after the type name.");
                knownTypes.Add(typeName.Text);
                return new TypeDefStmt(typeName.Text, ReadTypeFields(), start.Line);
            }
            case TokenKind.Try:
            {
                Read();
                List<Stmt> body = ReadBlock();
                List<Stmt>? otherwiseBody = null;
                string? errorTarget = null;
                if (Is(TokenKind.Otherwise))
                {
                    Read();
                    // D68: `otherwise into reason` is optional.
                    if (Is(TokenKind.Into))
                    {
                        Read();
                        errorTarget = ReadVariableName("I expected a variable name after \"into\".").Text;
                    }
                    otherwiseBody = ReadBlock();
                }
                return new TryStmt(body, otherwiseBody, errorTarget, start.Line);
            }
            case TokenKind.Fail:
            {
                Read();
                Expect(TokenKind.With, "I expected \"with\" and a message.");
                Expr message = ReadValue();
                Expect(TokenKind.Newline, "I expected the fail statement to end here.");
                return new FailStmt(message, start.Line);
            }
            case TokenKind.Sort:
            {
                Read();
                Token target = ReadVariableName("I expected a collection name after \"sort\".");
                Expect(TokenKind.Newline, "I expected the sort statement to end here.");
                return new SortStmt(target.Text, start.Line);
            }
            case TokenKind.Reverse:
            {
                Read();
                Token target = ReadVariableName("I expected a collection name after \"reverse\".");
                Expect(TokenKind.Newline, "I expected the reverse statement to end here.");
                return new ReverseStmt(target.Text, start.Line);
            }
            case TokenKind.Replace:
            {
                Read();
                Expr find = ReadValue();
                Expect(TokenKind.With, "I expected \"with\" and replacement text.");
                Expr replacement = ReadMathExpression();
                // D103: `replace route with "/path"` (no `in`) is SPA routing.
                if (find is VariableExpr { Name: "route" } && !Is(TokenKind.In)) throw Unsupported("replace route");
                Expect(TokenKind.In, "I expected \"in\" and the text variable to change.");
                Token target = ReadVariableName("I expected a text variable after \"in\".");
                string? resultTarget = null;
                if (Is(TokenKind.Into))
                {
                    Read();
                    resultTarget = ReadVariableName("I expected a variable name after \"into\".").Text;
                }
                Expect(TokenKind.Newline, "I expected the replace statement to end here.");
                return new ReplaceStmt(find, replacement, target.Text, resultTarget, start.Line);
            }
            case TokenKind.Split:
            {
                Read();
                Expr subject = ReadValue();
                Expect(TokenKind.By, "I expected \"by\" and a separator.");
                Expr separator = ReadValue();
                Expect(TokenKind.Into, "I expected \"into\" and a list name.");
                Token target = ReadVariableName("I expected a list name after \"into\".");
                Expect(TokenKind.Newline, "I expected the split statement to end here.");
                return new SplitStmt(subject, separator, target.Text, start.Line);
            }
            case TokenKind.Join:
            {
                Read();
                Expr subject = ReadValue();
                Expect(TokenKind.With, "I expected \"with\" and a separator.");
                Expr separator = ReadValue();
                Expect(TokenKind.Into, "I expected \"into\" and a text name.");
                Token target = ReadVariableName("I expected a text name after \"into\".");
                Expect(TokenKind.Newline, "I expected the join statement to end here.");
                return new JoinStmt(subject, separator, target.Text, start.Line);
            }
            case TokenKind.Find:
            {
                Read();
                Token item = ReadVariableName("I expected an item name after \"find\".");
                Expect(TokenKind.In, "I expected \"in\" and a collection.");
                Expr collection = ReadValue();
                Expect(TokenKind.Where, "I expected \"where\" and a condition.");
                Expr condition = ReadCondition(allowContinuation: false);
                Expect(TokenKind.Into, "I expected \"into\" and a result name.");
                Token target = ReadVariableName("I expected a result name after \"into\".");
                Expect(TokenKind.Newline, "I expected the find statement to end here.");
                return new FindStmt(item.Text, collection, condition, target.Text, start.Line);
            }
            case TokenKind.Format:
            {
                Read();
                Expr subject = ReadValue();
                Expect(TokenKind.As, "I expected \"as\" and a date format.");
                Expr pattern = ReadValue();
                Expect(TokenKind.Into, "I expected \"into\" and a variable name.");
                Token target = ReadVariableName("I expected a variable name after \"into\".");
                Expect(TokenKind.Newline, "I expected the format statement to end here.");
                return new FormatDateStmt(subject, pattern, target.Text, start.Line);
            }
            case TokenKind.Log:
                Read();
                return ReadDiagnostic(DiagnosticLevel.Note, start);
            case TokenKind.Warn:
                Read();
                return ReadDiagnostic(DiagnosticLevel.Warning, start);
            case TokenKind.Problem:
                Read();
                return ReadDiagnostic(DiagnosticLevel.Problem, start);
            case TokenKind.Read:
                return ReadRead(start);
            case TokenKind.Write:
                return ReadWrite(start);
            case TokenKind.Append:
            {
                Read();
                Expr content = ReadValue();
                Expect(TokenKind.To, "I expected \"to\" and a file path.");
                Expr path = ReadValue();
                Expect(TokenKind.Newline, "I expected the append statement to end here.");
                return new AppendFileStmt(content, path, start.Line);
            }
            case TokenKind.Copy:
            case TokenKind.Move:
                return ReadTransfer(start);
            case TokenKind.Delete:
                return ReadDelete(start);
            case TokenKind.Create:
            {
                Read();
                if (!Is(TokenKind.Folder)) throw Unsupported("create");
                Read();
                Expr path = ReadValue();
                Expect(TokenKind.Newline, "I expected the create statement to end here.");
                return new CreateFolderStmt(path, start.Line);
            }
            case TokenKind.Convert:
                return ReadConvert(start);
            case TokenKind.Random:
                return ReadRandom(start);
            case TokenKind.Run:
                return ReadRun(start);
            case TokenKind.Wait:
                return ReadWait(start);
            case TokenKind.Hash:
                return ReadHash(start);
            // zip folder "src" into "archive.zip" / unzip "archive.zip" into "dest" (D87)
            case TokenKind.Zip:
            {
                Read();
                Expect(TokenKind.Folder, "I expected \"folder\" after \"zip\".");
                Expr folder = ReadValue();
                Expect(TokenKind.Into, "I expected \"into\" and an archive path.");
                Expr archive = ReadValue();
                Expect(TokenKind.Newline, "I expected the zip statement to end here.");
                return new TransferStmt(FileAction.ZipFolder, folder, archive, start.Line);
            }
            case TokenKind.Unzip:
            {
                Read();
                Expr archive = ReadValue();
                Expect(TokenKind.Into, "I expected \"into\" and a destination folder.");
                Expr destination = ReadValue();
                Expect(TokenKind.Newline, "I expected the unzip statement to end here.");
                return new TransferStmt(FileAction.Unzip, archive, destination, start.Line);
            }
            case TokenKind.Encrypt:
            case TokenKind.Decrypt:
                return ReadCipher(start);
            // show progress 50 percent (D100); showing a UI resource is not ported.
            case TokenKind.Show when At(1) is { Kind: TokenKind.Identifier } progressWord && Same(progressWord.Text, "progress"):
            {
                Read(); Read();
                Expr percent = ReadValue();
                Expect(TokenKind.Percent, "I expected \"percent\" after the progress amount.");
                Expect(TokenKind.Newline, "I expected the show progress statement to end here.");
                return new ShowProgressStmt(percent, start.Line);
            }
            // choose from options into choice (D100); file dialogs are not ported.
            case TokenKind.Choose when IsAt(1, TokenKind.From):
            {
                Read(); Read();
                Expr options = ReadValue();
                Expect(TokenKind.Into, "I expected \"into\" and a result name.");
                Token choice = ReadVariableName("I expected a result name after \"into\".");
                Expect(TokenKind.Newline, "I expected the choose statement to end here.");
                return new ChooseFromListStmt(options, choice.Text, start.Line);
            }
            case TokenKind.Start when At(1) is { Kind: TokenKind.Identifier } timerWord && Same(timerWord.Text, "timer"):
            {
                Read(); Read();
                Token timer = ReadVariableName("I expected a timer name after \"start timer\".");
                Expect(TokenKind.Newline, "I expected the start timer statement to end here.");
                return new StartTimerStmt(timer.Text, start.Line);
            }
            case TokenKind.Get:
                return ReadGet(start);
            case TokenKind.Set:
                return ReadSet(start);
            case TokenKind.Add:
                return ReadAdd(start);
            case TokenKind.Remove:
                return ReadRemove(start);
            case TokenKind.Identifier:
                return ReadIdentifierStatement(start);
            case TokenKind.Number:
            {
                Expr expression = ReadMathExpression();
                Expect(TokenKind.Make, "I expected \"make\" and a result variable.");
                Token target = ReadVariableName("I expected a variable name after \"make\".");
                Expect(TokenKind.Newline, "I expected the math statement to end here.");
                return new MathIntoStmt(expression, target.Text, start.Line);
            }
            case TokenKind.Indent:
                throw Error("Unexpected indentation.", start, "Remove the extra space or tab at the beginning of the line.");
            case TokenKind.Dedent:
                throw Error("Unexpected unindent.", start, "Make sure block indentation lines up.");
            default:
                // A word no statement starts with is a syntax error in otterPS;
                // a statement otterPS has that otter-net does not run yet goes
                // to the PowerShell Otter.
                if (!OtterPsStatementKinds.Contains(statementKind))
                {
                    throw Error($"I don't understand '{start.Text}'.", start, "Start a statement with a word such as say, if, or a variable name.");
                }
                throw Unsupported($"the '{start.Text}' statement");
        }
    }

    // read "notes.txt" into notes / read json from "x.json" into settings
    Stmt ReadRead(Token start)
    {
        Read();
        if (Is(TokenKind.Csv))
        {
            Read();
            Expect(TokenKind.From, "I expected \"from\" and a CSV file path.");
            Expr csvPath = ReadValue();
            Expect(TokenKind.Into, "I expected \"into\" and a variable name.");
            Token csvTarget = ReadVariableName("I expected a variable name after \"into\".");
            Expect(TokenKind.Newline, "I expected the read statement to end here.");
            return new ReadCsvStmt(csvPath, csvTarget.Text, start.Line);
        }
        bool json = Is(TokenKind.Json);
        if (json)
        {
            Read();
            Expect(TokenKind.From, "I expected \"from\" and a file path.");
        }
        Expr path = ReadValue();
        Expect(TokenKind.Into, "I expected \"into\" and a variable name.");
        Token target = ReadVariableName("I expected a variable name after \"into\".");
        Expect(TokenKind.Newline, "I expected the read statement to end here.");
        return json ? new ReadJsonStmt(path, target.Text, start.Line) : new ReadFileStmt(path, target.Text, start.Line);
    }

    // write "Hello!" to "hello.txt" [atomically]
    Stmt ReadWrite(Token start)
    {
        Read();
        if (Is(TokenKind.Csv))
        {
            Read();
            Expr rows = ReadValue();
            Expect(TokenKind.To, "I expected \"to\" and a CSV file path.");
            Expr csvPath = ReadValue();
            Expect(TokenKind.Newline, "I expected the write CSV statement to end here.");
            return new WriteCsvStmt(rows, csvPath, start.Line);
        }
        // write xml doc to file "books.xml" (D105)
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "xml"))
        {
            Read();
            Expr xml = ReadValue();
            Expect(TokenKind.To, "I expected \"to\" and a file path.");
            Expect(TokenKind.File, "I expected \"file\" and a path.", "Write: write xml document to file \"books.xml\"");
            Expr xmlPath = ReadValue();
            Expect(TokenKind.Newline, "I expected the write xml statement to end here.");
            return new XmlWriteFileStmt(xml, xmlPath, start.Line);
        }
        // write bytes data to file "copy.png" [atomically] (D115)
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "bytes"))
        {
            Read();
            Expr data = ReadValue();
            Expect(TokenKind.To, "I expected \"to\" and a file path.", "Write: write bytes data to file \"copy.png\"");
            Expect(TokenKind.File, "I expected \"file\" and a path.", "Write: write bytes data to file \"copy.png\"");
            Expr bytesPath = ReadValue();
            bool atomicBytes = false;
            if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "atomically"))
            {
                Read();
                atomicBytes = true;
            }
            Expect(TokenKind.Newline, "I expected the write bytes statement to end here.");
            return new WriteBytesFileStmt(data, bytesPath, atomicBytes, start.Line);
        }
        Expr content = ReadValue();
        Expect(TokenKind.To, "I expected \"to\" and a file path.");
        Expr path = ReadValue();
        bool atomic = false;
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "atomically"))
        {
            Read();
            atomic = true;
        }
        Expect(TokenKind.Newline, "I expected the write statement to end here.");
        return new WriteFileStmt(content, path, atomic, start.Line);
    }

    // copy "a.txt" to "backup" / move folder "Old" to "Archive"
    Stmt ReadTransfer(Token start)
    {
        Read();
        bool copy = start.Kind == TokenKind.Copy;
        string verb = copy ? "copy" : "move";
        if (Is(TokenKind.Folder))
        {
            Read();
            Expr folderSource = ReadValue();
            Expect(TokenKind.To, "I expected \"to\" and a destination folder.");
            Expr folderDestination = ReadValue();
            Expect(TokenKind.Newline, $"I expected the {verb} statement to end here.");
            return new TransferStmt(copy ? FileAction.CopyFolder : FileAction.MoveFolder, folderSource, folderDestination, start.Line);
        }
        Expr source = ReadValue();
        Expect(TokenKind.To, "I expected \"to\" and a destination path.");
        if (copy && Current.Kind == TokenKind.Identifier && Same(Current.Text, "clipboard")) throw Unsupported("copy to clipboard");
        Expr destination = ReadValue();
        Expect(TokenKind.Newline, $"I expected the {verb} statement to end here.");
        return new TransferStmt(copy ? FileAction.CopyFile : FileAction.MoveFile, source, destination, start.Line);
    }

    // delete file "a.txt" / delete folder "Old"
    Stmt ReadDelete(Token start)
    {
        // D111's `delete secret "name"` is matched before otterPS's switch.
        if (At(1) is { Kind: TokenKind.Identifier } secret && Same(secret.Text, "secret") && !IsAt(2, TokenKind.Is)) throw Unsupported("delete secret");
        Read();
        if (Current.Kind == TokenKind.Identifier && SameAny(Current.Text, "registry", "credential")) throw Unsupported($"delete {Current.Text}");
        if (Is(TokenKind.Folder))
        {
            Read();
            Expr folder = ReadValue();
            Expect(TokenKind.Newline, "I expected the delete statement to end here.");
            return new DeleteFolderStmt(folder, start.Line);
        }
        if (Is(TokenKind.From)) throw Unsupported("HTTP delete");
        Expect(TokenKind.File, "I expected \"file\" after delete.");
        Expr path = ReadValue();
        Expect(TokenKind.Newline, "I expected the delete statement to end here.");
        return new DeleteFileStmt(path, start.Line);
    }

    // convert user to json into text / convert text from json into user
    Stmt ReadConvert(Token start)
    {
        Read();
        Expr subject = ReadValue();
        bool toJson = Is(TokenKind.To);
        if (toJson) Read();
        else Expect(TokenKind.From, "I expected \"to json/csv\" or \"from json/csv\" here.");
        Token format = Current;
        if (format.Kind is not (TokenKind.Json or TokenKind.Csv))
        {
            throw toJson
                ? Error("I expected \"json\" or \"csv\" after \"to\".", format, "Write \"convert rows to csv into text\" or \"convert user to json into text\".")
                : Error("I expected \"json\" or \"csv\" after \"from\".", format, "Write \"convert text from csv into rows\" or \"convert text from json into user\".");
        }
        Read();
        Expect(TokenKind.Into, "I expected \"into\" and a variable name.");
        Token target = ReadVariableName("I expected a variable name after \"into\".");
        Expect(TokenKind.Newline, "I expected the convert statement to end here.");
        if (format.Kind == TokenKind.Csv) return new ConvertCsvStmt(toJson, subject, target.Text, start.Line);
        return new ConvertJsonStmt(toJson, subject, target.Text, start.Line);
    }

    // Words that start one of otterPS's other `get` forms (clipboard,
    // environment variables, system folders, processes, the registry, files,
    // databases ...). Only the D41 form `get KEY from THING into NAME` runs here.
    static readonly string[] SpecialGetWords =
    {
        "arguments", "clipboard", "columns", "credential", "current", "environment", "event", "owner",
        "processes", "registry", "symbolic", "system", "tables", "all",
    };

    Stmt ReadGet(Token start)
    {
        Read();
        Token kind = Current;
        if (kind.Kind == TokenKind.Identifier && Same(kind.Text, "environment"))
        {
            Read();
            ExpectWord("variable", "I expected \"variable\" after \"environment\".", "get environment variable \"PATH\" into value");
            Expr name = ReadValue();
            return new GetEnvironmentVariableStmt(name, ReadGetResult(), start.Line);
        }
        if (kind.Kind == TokenKind.Identifier && Same(kind.Text, "current"))
        {
            Read();
            if (!IsFolderWord(Current)) throw Error("I expected \"directory\" or \"folder\" after \"current\".", Current, "get current directory into folder");
            Read();
            return new GetSystemFolderStmt(new LiteralExpr("current", start.Line), ReadGetResult(), start.Line);
        }
        if (kind.Kind == TokenKind.Identifier && Same(kind.Text, "system") && IsAt(1, TokenKind.Folder))
        {
            Read(); Read();
            Expr folderName = ReadValue();
            return new GetSystemFolderStmt(folderName, ReadGetResult(), start.Line);
        }
        if (kind.Kind is TokenKind.Files or TokenKind.Folders)
        {
            Read();
            Expect(TokenKind.In, "I expected \"in\" and a folder path.");
            Expr folder = ReadValue();
            bool includeSubfolders = false;
            if (Is(TokenKind.And))
            {
                Read();
                Expect(TokenKind.Subfolders, "I expected \"subfolders\" after \"and\".");
                includeSubfolders = true;
            }
            if (Is(TokenKind.Newline) && IsAt(1, TokenKind.Indent)) throw Unsupported("a continued discovery clause");
            Expect(TokenKind.Into, "I expected \"into\" and a result name.");
            Token entries = ReadVariableName("I expected a result name after \"into\".");
            Expect(TokenKind.Newline, "I expected the discovery statement to end here.");
            return new GetEntriesStmt(kind.Kind == TokenKind.Folders, folder, includeSubfolders, entries.Text, start.Line);
        }
        if (kind.Kind is TokenKind.Distinct or TokenKind.Json ||
            (kind.Kind is TokenKind.Identifier or TokenKind.Newline or TokenKind.Indent && SameAny(kind.Text, SpecialGetWords)))
        {
            throw Unsupported($"get {kind.Text}");
        }
        if (kind.Kind is TokenKind.Newline or TokenKind.Indent) throw Unsupported("queries");
        Expr key = ReadValue();
        // Without `from` it is an HTTP request (`get "https://..." into page`).
        if (!Is(TokenKind.From)) throw Unsupported("HTTP get");
        Read();
        Expr target = ReadValue();
        // A clause continued on an indented line (D38A).
        if (Is(TokenKind.Newline) && IsAt(1, TokenKind.Indent)) throw Unsupported("a continued get clause");
        Expect(TokenKind.Into, "I expected \"into\" and a result name.");
        Token result = ReadVariableName("I expected a result name after \"into\".");
        Expect(TokenKind.Newline, "I expected the get statement to end here.");
        return new GetKeyStmt(key, target, result.Text, start.Line);
    }

    // into NAME, then the end of the line, for the get forms.
    string ReadGetResult()
    {
        Expect(TokenKind.Into, "I expected \"into\" and a result name.");
        Token result = ReadVariableName("I expected a result name after \"into\".");
        Expect(TokenKind.Newline, "I expected the get statement to end here.");
        return result.Text;
    }

    void ExpectWord(string word, string message, string suggestion)
    {
        if (Current.Kind != TokenKind.Identifier || !Same(Current.Text, word)) throw Error(message, Current, suggestion);
        Read();
    }

    static bool IsFolderWord(Token token) =>
        token.Kind == TokenKind.Folder || (token.Kind == TokenKind.Identifier && SameAny(token.Text, "directory", "folder"));

    static readonly string[] SpecialSetWords =
    {
        "text", "attribute", "registry", "credential", "cursor", "random", "priority", "read", "writable",
        "current", "directory", "environment", "folder", "drag",
    };

    // hash "text" as "sha256" [with key "k"] into digest (D91). Password
    // hashing (D109) is not ported yet.
    Stmt ReadHash(Token start)
    {
        Read();
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "password") && !IsAt(1, TokenKind.As)) throw Unsupported("hash password");
        Expr text = ReadValue();
        Expect(TokenKind.As, "I expected \"as\" and an algorithm name.");
        Expr algorithm = ReadValue();
        Expr? key = null;
        if (Is(TokenKind.With))
        {
            Read();
            ExpectWord("key", "I expected \"key\" after \"with\".", "hash \"text\" as \"sha256\" with key \"secret\" into digest");
            key = ReadValue();
        }
        Expect(TokenKind.Into, "I expected \"into\" and a result name.");
        Token digest = ReadVariableName("I expected a result name after \"into\".");
        Expect(TokenKind.Newline, "I expected the hash statement to end here.");
        return new HashTextStmt(text, algorithm, key, digest.Text, start.Line);
    }

    // encrypt/decrypt TEXT with key KEY into NAME (D92). The `using` cipher
    // forms (D109) are not ported yet.
    Stmt ReadCipher(Token start)
    {
        Read();
        bool decrypt = start.Kind == TokenKind.Decrypt;
        string verb = decrypt ? "decrypt" : "encrypt";
        Expr text = ReadValue();
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "using")) throw Unsupported($"{verb} using");
        Expect(TokenKind.With, "I expected \"with key\" and a key.");
        ExpectWord("key", "I expected \"key\" after \"with\".", decrypt ? "decrypt \"cipher\" with key \"secret\" into text" : "encrypt \"text\" with key \"secret\" into cipher");
        Expr key = ReadValue();
        Expect(TokenKind.Into, "I expected \"into\" and a result name.");
        Token result = ReadVariableName("I expected a result name after \"into\".");
        Expect(TokenKind.Newline, $"I expected the {verb} statement to end here.");
        return new CipherTextStmt(decrypt, text, key, result.Text, start.Line);
    }

    // wait 5 seconds (D101). `wait for process` is not ported yet.
    Stmt ReadWait(Token start)
    {
        Read();
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "for"))
        {
            Read();
            if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "process")) throw Unsupported("wait for process");
            throw Error("I expected \"process\" after \"for\".", Current, "wait for process p up to 5 seconds");
        }
        Expr duration = ReadValue();
        Token unitToken = Current;
        TimeUnit? unit = unitToken.Kind switch
        {
            TokenKind.Year => TimeUnit.Year,
            TokenKind.Month => TimeUnit.Month,
            TokenKind.Day => TimeUnit.Day,
            TokenKind.Hour => TimeUnit.Hour,
            TokenKind.Minute => TimeUnit.Minute,
            TokenKind.Second => TimeUnit.Second,
            TokenKind.Millisecond => TimeUnit.Millisecond,
            _ => null,
        };
        if (unit == null && unitToken.Kind == TokenKind.Identifier)
        {
            unit = unitToken.Text.ToLowerInvariant() switch
            {
                "year" or "years" => TimeUnit.Year,
                "month" or "months" => TimeUnit.Month,
                "day" or "days" => TimeUnit.Day,
                "hour" or "hours" => TimeUnit.Hour,
                "minute" or "minutes" => TimeUnit.Minute,
                "second" or "seconds" => TimeUnit.Second,
                "millisecond" or "milliseconds" => TimeUnit.Millisecond,
                _ => null,
            };
        }
        if (unit == null) throw Error("I expected a time unit (seconds, minutes, milliseconds, ...) after the wait duration.", unitToken, "wait 5 seconds");
        Read();
        Expect(TokenKind.Newline, "I expected the wait statement to end here.");
        return new WaitDelayStmt(duration, unit.Value, start.Line);
    }

    // run command "git status" [into result]. Starting a program without
    // waiting (`run "notepad"`), remote and ssh commands are not ported yet.
    Stmt ReadRun(Token start)
    {
        Read();
        if (!Is(TokenKind.Command)) throw Unsupported("run without command");
        Read();
        Expr target = ReadValue();
        if (Current.Kind == TokenKind.Identifier && SameAny(Current.Text, "on", "over")) throw Unsupported($"run command {Current.Text}");
        string? resultTarget = null;
        if (Is(TokenKind.Into))
        {
            Read();
            resultTarget = ReadVariableName("I expected a variable name after \"into\".").Text;
        }
        Expect(TokenKind.Newline, "I expected the run statement to end here.");
        return new RunCommandStmt(target, resultTarget, start.Line);
    }

    // random number from 1 to 10 into n / random item from games into g (D30)
    Stmt ReadRandom(Token start)
    {
        Read();
        Token what = Current;
        if (what.Kind == TokenKind.Identifier && Same(what.Text, "number"))
        {
            Read();
            Expect(TokenKind.From, "I expected \"from\" and the lowest number.");
            Expr from = ReadMathExpression();
            Expect(TokenKind.To, "I expected \"to\" and the highest number.");
            Expr to = ReadMathExpression();
            Expect(TokenKind.Into, "I expected \"into\" and a variable name.");
            Token number = ReadVariableName("I expected a variable name after \"into\".");
            Expect(TokenKind.Newline, "I expected the random statement to end here.");
            return new RandomNumberStmt(from, to, number.Text, start.Line);
        }
        if (what.Kind == TokenKind.Item || (what.Kind == TokenKind.Identifier && Same(what.Text, "item")))
        {
            Read();
            Expect(TokenKind.From, "I expected \"from\" and a list.");
            Expr collection = ReadValue();
            Expect(TokenKind.Into, "I expected \"into\" and a variable name.");
            Token item = ReadVariableName("I expected a variable name after \"into\".");
            Expect(TokenKind.Newline, "I expected the random statement to end here.");
            return new RandomItemStmt(collection, item.Text, start.Line);
        }
        throw Error("I expected \"number\" or \"item\" after \"random\".", what,
            "Write \"random number from 1 to 10 into n\" or \"random item from games into g\".");
    }

    Stmt ReadSet(Token start)
    {
        Read();
        // set random seed to 42 (D101)
        if (Current.Kind is TokenKind.Random or TokenKind.Identifier && Same(Current.Text, "random"))
        {
            Read();
            Token seedWord = Current;
            if (seedWord.Kind != TokenKind.Identifier || !Same(seedWord.Text, "seed"))
            {
                throw Error("I expected \"seed\" after \"random\".", seedWord, "set random seed to 42");
            }
            Read();
            Expect(TokenKind.To, "I expected \"to\" after \"seed\".");
            Expr seed = ReadValue();
            Expect(TokenKind.Newline, "I expected the set random seed statement to end here.");
            return new SetRandomSeedStmt(seed, start.Line);
        }
        // set text of X to V / set text of NAME in X to V (D105)
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "text") && IsAt(1, TokenKind.Of))
        {
            Read(); Read();
            Expr? element = ReadValue();
            Expr? nameIn = null, xmlIn = null;
            if (Is(TokenKind.In))
            {
                Read();
                nameIn = element;
                xmlIn = ReadValue();
                element = null;
            }
            Expect(TokenKind.To, "I expected \"to\" and a value.");
            Expr textValue = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected the set text statement to end here.");
            return new XmlSetTextStmt(element, nameIn, xmlIn, textValue, start.Line);
        }
        // set attribute "id" of book to V (D105)
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "attribute"))
        {
            Read();
            Expr attributeName = ReadValue();
            Expect(TokenKind.Of, "I expected \"of\" and an element after the attribute name.", "Write: set attribute \"id\" of book to \"42\"");
            Expr element = ReadValue();
            Expect(TokenKind.To, "I expected \"to\" and a value.");
            Expr attributeValue = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected the set attribute statement to end here.");
            return new XmlSetAttributeStmt(attributeName, element, attributeValue, start.Line);
        }
        // set cursor to row 5 column 10 (D100)
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "cursor"))
        {
            Read();
            Expect(TokenKind.To, "I expected \"to\" after \"cursor\".");
            ExpectWord("row", "I expected \"row\" after \"to\".", "set cursor to row 5 column 10");
            Expr row = ReadValue();
            ExpectWord("column", "I expected \"column\" after the row number.", "set cursor to row 5 column 10");
            Expr column = ReadValue();
            Expect(TokenKind.Newline, "I expected the set cursor statement to end here.");
            return new SetCursorStmt(row, column, start.Line);
        }
        // set current directory to "Projects" (D94)
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "current"))
        {
            Read();
            if (!IsFolderWord(Current)) throw Error("I expected \"directory\" or \"folder\" after \"current\".", Current, "set current directory to \"Projects\"");
            Read();
            Expect(TokenKind.To, "I expected \"to\" and a folder path.");
            Expr path = ReadValue();
            Expect(TokenKind.Newline, "I expected the set statement to end here.");
            return new SetCurrentDirectoryStmt(path, start.Line);
        }
        // set environment variable "NAME" to "VALUE" (D94)
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "environment"))
        {
            Read();
            ExpectWord("variable", "I expected \"variable\" after \"environment\".", "set environment variable \"NAME\" to \"VALUE\"");
            Expr name = ReadValue();
            Expect(TokenKind.To, "I expected \"to\" and a value.");
            Expr variableValue = ReadValue();
            Expect(TokenKind.Newline, "I expected the set statement to end here.");
            return new SetEnvironmentVariableStmt(name, variableValue, start.Line);
        }
        if (Current.Kind == TokenKind.Identifier && SameAny(Current.Text, SpecialSetWords)) throw Unsupported($"set {Current.Text}");
        Expr key = ReadValue();
        Expect(TokenKind.To, "I expected \"to\" and a value.");
        Expr value = ReadValue();
        Expect(TokenKind.In, "I expected \"in\" and a target thing.");
        Expr target = ReadValue();
        Expect(TokenKind.Newline, "I expected the set statement to end here.");
        return new SetKeyStmt(key, value, target, start.Line);
    }

    // Parts are read like say's, one value each (D8, D31).
    Stmt ReadDiagnostic(DiagnosticLevel level, Token start)
    {
        var parts = new List<Expr>();
        while (!Is(TokenKind.Newline)) parts.Add(ReadValue());
        Expect(TokenKind.Newline, "I expected the diagnostic to end here.");
        return new DiagnosticStmt(level, parts, start.Line);
    }

    Stmt ReadAdd(Token start)
    {
        Read();
        // add element NAME [with text T] to X [and call it N] (D105), tried
        // and abandoned for D12's add when it does not fit.
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "element"))
        {
            int saved = position;
            Stmt? added = null;
            try
            {
                Read();
                Expr name = ReadValue();
                Expr? text = null;
                if (Is(TokenKind.With))
                {
                    Read();
                    if (!(Current.Kind == TokenKind.Identifier && Same(Current.Text, "text"))) throw new NotThisForm();
                    Read();
                    text = ReadValue();
                }
                if (!Is(TokenKind.To)) throw new NotThisForm();
                Read();
                Expr xml = ReadValue();
                string? target = null;
                if (Is(TokenKind.And))
                {
                    Read();
                    Expect(TokenKind.Call, "I expected \"call\" after \"and\".");
                    Expect(TokenKind.It, "I expected \"it\" after \"call\".");
                    target = ReadVariableName("I expected a name after \"call it\".").Text;
                }
                Expect(TokenKind.Newline, "I expected the add element statement to end here.");
                added = new XmlAddElementStmt(name, xml, text, target, start.Line);
            }
            catch (Exception e) when (e is OtterError or NotThisForm) { }
            if (added != null) return added;
            position = saved;
        }
        if (Same(start.Text, "increase"))
        {
            Token target = ReadVariableName("I expected a variable name after \"increase\".");
            Expr amount = new LiteralExpr(1.0, start.Line);
            if (Is(TokenKind.By)) { Read(); amount = ReadMathExpression(); }
            Expect(TokenKind.Newline, "I expected the increase statement to end here.");
            return new AddToStmt(amount, target.Text, start.Line);
        }
        if (TokenBeforeNewline(TokenKind.To))
        {
            Expr amount = ReadMathExpression();
            // add 7 days to date (D32): the unit word is what tells it apart.
            if (TimeUnitKinds.Contains(Current.Kind))
            {
                Token unit = Read();
                Expect(TokenKind.To, "I expected \"to\" and a date name.");
                Token dateName = ReadVariableName("I expected a date name after \"to\".");
                Expect(TokenKind.Newline, "I expected the add statement to end here.");
                return new DateAdjustStmt(amount, ToTimeUnit(unit.Kind), dateName.Text, false, start.Line);
            }
            Expect(TokenKind.To, "I expected \"to\" and a variable name.");
            Token name = ReadVariableName("I expected a variable name after \"to\".");
            Expect(TokenKind.Newline, "I expected the add statement to end here.");
            return new AddToStmt(amount, name.Text, start.Line);
        }
        var call = new CallExpr(start.Text, ReadCallArguments(), start.Line);
        string? resultTarget = ReadCallResultTarget();
        Expect(TokenKind.Newline, "I expected the function call to end here.");
        return new CallStmt(call, resultTarget, start.Line);
    }

    Stmt ReadRemove(Token start)
    {
        Read();
        // remove element X / remove attribute NAME from X (D105), tried and
        // abandoned for D12's remove when they do not fit.
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "element"))
        {
            int saved = position;
            Stmt? removedElement = null;
            try
            {
                Read();
                Expr element = ReadValue();
                if (!Is(TokenKind.Newline)) throw new NotThisForm();
                Read();
                removedElement = new XmlRemoveElementStmt(element, start.Line);
            }
            catch (Exception e) when (e is OtterError or NotThisForm) { }
            if (removedElement != null) return removedElement;
            position = saved;
        }
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "attribute"))
        {
            int saved = position;
            Stmt? removedAttribute = null;
            try
            {
                Read();
                Expr name = ReadValue();
                if (!Is(TokenKind.From)) throw new NotThisForm();
                Read();
                Expr element = ReadValue();
                if (!Is(TokenKind.Newline)) throw new NotThisForm();
                Read();
                removedAttribute = new XmlRemoveAttributeStmt(name, element, start.Line);
            }
            catch (Exception e) when (e is OtterError or NotThisForm) { }
            if (removedAttribute != null) return removedAttribute;
            position = saved;
        }
        if (Same(start.Text, "decrease"))
        {
            Token target = ReadVariableName("I expected a variable name after \"decrease\".");
            Expr amount = new LiteralExpr(1.0, start.Line);
            if (Is(TokenKind.By)) { Read(); amount = ReadMathExpression(); }
            Expect(TokenKind.Newline, "I expected the decrease statement to end here.");
            return new RemoveFromStmt(amount, target.Text, start.Line);
        }
        Expr removed = ReadMathExpression();
        // remove 1 month from date (D32)
        if (TimeUnitKinds.Contains(Current.Kind))
        {
            Token unit = Read();
            Expect(TokenKind.From, "I expected \"from\" and a date name.");
            Token dateName = ReadVariableName("I expected a date name after \"from\".");
            Expect(TokenKind.Newline, "I expected the remove statement to end here.");
            return new DateAdjustStmt(removed, ToTimeUnit(unit.Kind), dateName.Text, true, start.Line);
        }
        Expect(TokenKind.From, "I expected \"from\" and a variable name.");
        Token from = ReadVariableName("I expected a variable name after \"from\".");
        Expect(TokenKind.Newline, "I expected the remove statement to end here.");
        return new RemoveFromStmt(removed, from.Text, start.Line);
    }

    Stmt ReadIdentifierStatement(Token start)
    {
        Token name = Read();
        if (Same(name.Text, "pi"))
        {
            throw Error("'pi' is a built-in value, not a variable name.", name, "Choose a different variable name, such as \"circleRatio\".");
        }
        // D40: `person has` is the untyped object literal.
        if (Is(TokenKind.Has))
        {
            Read();
            var properties = Is(TokenKind.Newline) ? ReadObjectBlockProperties(allowEmpty: true) : ReadInlineObjectProperties();
            return new ObjectDefStmt(name.Text, "thing", properties, name.Line);
        }
        if (Is(TokenKind.Of))
        {
            // Re-read the whole property target: `age of person is 30`.
            position--;
            Expr target = ReadValue();
            Expect(TokenKind.Is, "I expected \"is\" after the property target.");
            Expr value = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected the property assignment to end here.");
            return new AssignStmt(target, value, name.Line);
        }
        if (Is(TokenKind.Is))
        {
            Read();
            if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "panel") && IsAt(1, TokenKind.With)) throw Unsupported("UI elements");
            if (Is(TokenKind.A))
            {
                Read();
                string typeName = ReadObjectTypeName();
                // otterPS may treat other type names (button, window ...) as UI.
                if (!Same(typeName, "thing") && !knownTypes.Contains(typeName)) throw Unsupported($"'{typeName}' things");
                List<AssignStmt> properties;
                if (Is(TokenKind.With))
                {
                    Read();
                    properties = ReadInlineObjectProperties();
                }
                else if (Same(typeName, "thing") || !knownTypes.Contains(typeName))
                {
                    properties = ReadObjectBlockProperties(allowEmpty: Same(typeName, "thing"));
                }
                else
                {
                    Expect(TokenKind.Newline, "I expected the object definition to end here.");
                    if (Is(TokenKind.Indent))
                    {
                        throw Error($"'{typeName}' is a declared type, so its properties must use 'with' on the same line.", Current,
                            $"Write '{name.Text} is a {typeName} with property value'.");
                    }
                    properties = new List<AssignStmt>();
                }
                return new ObjectDefStmt(name.Text, typeName, properties, name.Line);
            }
            Expr value = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected the assignment to end here.");
            return new AssignStmt(new VariableExpr(name.Text, name.Line), value, name.Line);
        }
        if (Is(TokenKind.IsNot))
        {
            Read();
            Expr value = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected the assignment to end here.");
            return new AssignStmt(new VariableExpr(name.Text, name.Line), new NotExpr(value, name.Line), name.Line);
        }
        if (Is(TokenKind.Are))
        {
            Read();
            if (Is(TokenKind.Empty))
            {
                Read();
                Expect(TokenKind.Newline, "I expected the empty list definition to end here.");
                return new ListDefStmt(name.Text, Array.Empty<Expr>(), name.Line);
            }
            return new ListDefStmt(name.Text, ReadListItems(), name.Line);
        }
        // Resolved from declared function names: "double 5 make result" is a
        // call, "number1 and number2 make total" is arithmetic.
        if (knownFunctions.ContainsKey(name.Text))
        {
            var call = new CallExpr(name.Text, ReadCallArguments(), name.Line);
            string? target = ReadCallResultTarget();
            Expect(TokenKind.Newline, "I expected the function call to end here.");
            return new CallStmt(call, target, name.Line);
        }
        if (TokenBeforeNewline(TokenKind.Make))
        {
            position--;
            Expr expression = ReadMathExpression();
            Expect(TokenKind.Make, "I expected \"make\" and a result variable.");
            Token target = ReadVariableName("I expected a variable name after \"make\".");
            Expect(TokenKind.Newline, "I expected the math statement to end here.");
            return new MathIntoStmt(expression, target.Text, start.Line);
        }
        var plainCall = new CallExpr(name.Text, ReadCallArguments(), name.Line);
        string? resultTarget = ReadCallResultTarget();
        Expect(TokenKind.Newline, "I expected the function call to end here.");
        return new CallStmt(plainCall, resultTarget, name.Line);
    }

    // otterPS's Test-OtterAggregateStatementAhead.
    bool IsAggregateQueryAhead()
    {
        Token start = Current;
        bool candidate = start.Kind is TokenKind.Count or TokenKind.Sum or TokenKind.Average or TokenKind.Minimum or TokenKind.Maximum ||
                         (start.Kind == TokenKind.Identifier && SameAny(start.Text, "total", "avg", "min", "max"));
        if (!candidate) return false;
        int pos = position + 1;
        bool hasFrom = false, hasInto = false, hasTo = false;
        while (pos < tokens.Count && tokens[pos].Kind != TokenKind.Newline && tokens[pos].Kind != TokenKind.EndOfFile)
        {
            if (tokens[pos].Kind == TokenKind.From) hasFrom = true;
            if (tokens[pos].Kind == TokenKind.Into) hasInto = true;
            if (tokens[pos].Kind == TokenKind.To) hasTo = true;
            pos++;
        }
        if (start.Kind == TokenKind.Count && hasTo && !hasInto) return false;
        if (hasFrom && !hasInto && pos < tokens.Count && tokens[pos].Kind == TokenKind.Newline &&
            pos + 1 < tokens.Count && tokens[pos + 1].Kind == TokenKind.Indent)
        {
            int depth = 0;
            int limit = Math.Min(tokens.Count, pos + 200);
            while (pos < limit)
            {
                TokenKind k = tokens[pos].Kind;
                if (k == TokenKind.EndOfFile) break;
                if (k == TokenKind.Indent) depth++;
                else if (k == TokenKind.Dedent) depth--;
                else if (k == TokenKind.Newline)
                {
                    if (depth <= 0)
                    {
                        int next = pos + 1;
                        while (next < limit && tokens[next].Kind == TokenKind.Newline) next++;
                        if (next >= limit || tokens[next].Kind != TokenKind.Indent) break;
                    }
                }
                else if (k == TokenKind.Into && depth <= 0) { hasInto = true; break; }
                pos++;
            }
        }
        return hasFrom && hasInto;
    }

    bool TokenBeforeNewline(TokenKind kind)
    {
        for (int index = position; index < tokens.Count; index++)
        {
            if (tokens[index].Kind == TokenKind.Newline) return false;
            if (tokens[index].Kind == kind) return true;
        }
        return false;
    }

    List<Expr> ReadListItems()
    {
        Expect(TokenKind.Newline, "I expected the list definition to end here.");
        Expect(TokenKind.Indent, "I expected indented list items.");
        var items = new List<Expr>();
        SkipNewlines();
        while (!Is(TokenKind.Dedent))
        {
            items.Add(ReadValue());
            Expect(TokenKind.Newline, "I expected the list item to end here.");
            SkipNewlines();
        }
        Read();
        if (Is(TokenKind.BlockEnd)) { Read(); SkipNewlines(); }
        return items;
    }

    List<Expr> ReadCallArguments()
    {
        var arguments = new List<Expr>();
        while (!Is(TokenKind.Newline) && !Is(TokenKind.Make) && !Is(TokenKind.Into))
        {
            if (Is(TokenKind.And)) { Read(); continue; }
            arguments.Add(ReadValue());
        }
        return arguments;
    }

    // In expression position a call takes exactly its declared number of
    // arguments, so `if isAdult age and active is true` leaves `and` alone.
    CallExpr ReadFunctionCallExpression(Token functionToken)
    {
        Read();
        var arguments = new List<Expr>();
        int arity = knownFunctions[functionToken.Text];
        for (int index = 0; index < arity; index++)
        {
            if (index > 0 && Is(TokenKind.And)) Read();
            Token current = Current;
            if (current.Kind is TokenKind.Newline or TokenKind.EndOfFile or TokenKind.Make or TokenKind.Into or TokenKind.Or)
            {
                throw Error($"I expected argument {index + 1} for '{functionToken.Text}'.", current, $"Provide {arity} argument(s) for '{functionToken.Text}'.");
            }
            arguments.Add(ReadValue());
        }
        return new CallExpr(functionToken.Text, arguments, functionToken.Line);
    }

    Token ReadFunctionName()
    {
        Token token = Current;
        if (!IsIdentifier(token) && token.Kind != TokenKind.Add)
        {
            throw Error("I expected a function name.", token, "Write a name after \"to\", such as \"to greet name\".");
        }
        return Read();
    }

    string? ReadCallResultTarget()
    {
        if (Is(TokenKind.Make)) { Read(); return ReadVariableName("I expected a result variable after \"make\".").Text; }
        if (Is(TokenKind.Into)) { Read(); return ReadVariableName("I expected a result variable after \"into\".").Text; }
        return null;
    }

    Token ReadVariableName(string message, bool allowReservedLiteral = false)
    {
        Token token = Current;
        if (!IsIdentifier(token)) throw Error(message, token, "Use a name to hold this value.");
        if (!allowReservedLiteral && SameAny(token.Text, "today", "now", "pi"))
        {
            throw Error($"'{token.Text}' is a built-in value, not a variable name.", token,
                "Choose a different variable name, such as \"currentTime\" or \"circleRatio\".");
        }
        return Read();
    }

    // ---- things and types (D40, D41, D54) -----------------------------------

    List<string> ReadTypeFields()
    {
        Expect(TokenKind.Newline, "I expected the type definition to end here.");
        Expect(TokenKind.Indent, "I expected indented property names for this type.");
        var fields = new List<string>();
        SkipNewlines();
        while (!Is(TokenKind.Dedent))
        {
            fields.Add(ReadVariableName("I expected a property name.", allowReservedLiteral: true).Text);
            Expect(TokenKind.Newline, "I expected the property name to end here.");
            SkipNewlines();
        }
        Read();
        if (Is(TokenKind.BlockEnd)) { Read(); SkipNewlines(); }
        return fields;
    }

    string ReadObjectTypeName()
    {
        var words = new List<string>();
        while (!Is(TokenKind.Newline) && !Is(TokenKind.With)) words.Add(Read().Text);
        if (words.Count == 0)
        {
            throw Error("I expected a type name after \"is a\".", Current, "Write a type, such as \"thing\" or \"Person\".");
        }
        return string.Join(" ", words);
    }

    // UI-only property forms (align, width full, bare round/spread, accepts
    // drops) are left to the PowerShell Otter.
    void RefuseUiProperty(Token property)
    {
        if (SameAny(property.Text, "accepts", "align", "width", "height", "round", "spread")) throw Unsupported($"the '{property.Text}' property");
    }

    List<AssignStmt> ReadObjectBlockProperties(bool allowEmpty)
    {
        Expect(TokenKind.Newline, "I expected the object statement to end here.");
        if (!Is(TokenKind.Indent))
        {
            if (allowEmpty)
            {
                // An empty object may still end with the block terminator.
                if (Is(TokenKind.BlockEnd)) { Read(); SkipNewlines(); }
                return new List<AssignStmt>();
            }
            throw Error("I expected an indented block of properties.", Current, "Indent the properties under the object statement.");
        }
        Read();
        var properties = new List<AssignStmt>();
        SkipNewlines();
        while (!Is(TokenKind.Dedent) && !Is(TokenKind.EndOfFile) && !Is(TokenKind.BlockEnd))
        {
            Token current = Current;
            if (current.Kind is TokenKind.Say or TokenKind.If or TokenKind.While or TokenKind.Repeat or TokenKind.To or TokenKind.Return or TokenKind.Ask)
            {
                throw Error("Only property assignments belong inside an object.", current, "Remove control flow or actions from this object block.");
            }
            Token property = ReadVariableName("I expected a property name.", allowReservedLiteral: true);
            RefuseUiProperty(property);
            // `is` is optional: "age 36" and "age is 36" mean the same.
            bool hadIs = false;
            if (Is(TokenKind.Is)) { Read(); hadIs = true; }
            Expr value;
            if (!hadIs && Current.Kind is TokenKind.Newline or TokenKind.EndOfFile or TokenKind.Dedent or TokenKind.BlockEnd)
            {
                // D54: a bare property is true.
                value = new LiteralExpr(true, property.Line);
            }
            else
            {
                if (Current.Kind is TokenKind.Newline or TokenKind.EndOfFile or TokenKind.Dedent or TokenKind.BlockEnd)
                {
                    throw Error($"I expected a value for property '{property.Text}'.", Current,
                        $"Provide a value after the property name, such as '{property.Text} 10' or '{property.Text} is 10'.");
                }
                value = ReadMathExpression();
            }
            properties.Add(new AssignStmt(new VariableExpr(property.Text, property.Line), value, property.Line));
            Token end = Current;
            if (end.Kind != TokenKind.Newline)
            {
                if (end.Kind is TokenKind.Identifier or TokenKind.With or TokenKind.Is or TokenKind.String or TokenKind.Number)
                {
                    throw Error($"I expected each property on its own line, but found '{end.Text}' on the same line.", end,
                        $"Place '{end.Text}' on a new indented line, or use 'with' to define inline properties separated by commas.");
                }
                throw Error($"I expected the property assignment to end here, but found '{end.Text}'.", end,
                    "Place each property on its own line, or check for extra words at the end of the line.");
            }
            Read();
            SkipNewlines();
        }
        Expect(TokenKind.Dedent, "I expected the object block to end.");
        if (Is(TokenKind.BlockEnd)) { Read(); SkipNewlines(); }
        return properties;
    }

    // Comma-separated, each `is` optional: `person has name "Ada", age 36`.
    List<AssignStmt> ReadInlineObjectProperties()
    {
        var properties = new List<AssignStmt>();
        while (true)
        {
            Token property = ReadVariableName("I expected a property name after \"has\", \"with\", or a comma.", allowReservedLiteral: true);
            RefuseUiProperty(property);
            bool hadIs = false;
            if (Is(TokenKind.Is)) { Read(); hadIs = true; }
            Expr value;
            if (!hadIs && (Current.Text == "," || Current.Kind is TokenKind.Newline or TokenKind.EndOfFile))
            {
                value = new LiteralExpr(true, property.Line);   // D54
            }
            else
            {
                if (Current.Text == "," || Current.Kind is TokenKind.Newline or TokenKind.EndOfFile)
                {
                    throw Error($"I expected a value for property '{property.Text}'.", Current,
                        $"Provide a value after the property name, such as '{property.Text} 10' or '{property.Text} is 10'.");
                }
                value = ReadMathExpression();
            }
            properties.Add(new AssignStmt(new VariableExpr(property.Text, property.Line), value, property.Line));
            if (Current.Text != ",") break;
            Read();
            if (Is(TokenKind.Newline)) throw Error("I expected a property after the comma.", Current, "Add another property assignment after the comma.");
        }
        Token end = Current;
        if (end.Kind != TokenKind.Newline && end.Kind != TokenKind.EndOfFile)
        {
            if (end.Kind is TokenKind.Identifier or TokenKind.With or TokenKind.Is or TokenKind.String or TokenKind.Number)
            {
                throw Error($"I expected a comma between properties in this inline list, but found '{end.Text}'.", end,
                    $"Separate each property with a comma, such as '..., {end.Text} ...'.");
            }
            throw Error($"I expected the inline properties to end here, but found '{end.Text}'.", end, "Separate properties with commas or end the line.");
        }
        if (end.Kind == TokenKind.Newline) Read();
        return properties;
    }


    // ---- values and arithmetic ---------------------------------------------

    // Words that begin one of Read-OtterValue's special forms in otterPS.
    static readonly HashSet<string> SpecialValueWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "today", "now", "pi", "the", "date", "text", "current", "route",
        "changed", "change", "old", "received", "close", "websocket", "secret", "xml", "pretty",
        "element", "elements", "child", "children", "attribute", "drag", "dragged", "drop", "dropped",
        "hmac", "incoming", "local", "network", "remote", "sender", "secure", "tls", "sha256", "sha384", "sha512",
    };

    // For these words otterPS's special form needs one of these next words
    // (matched ignoring case); followed by anything else the word is an
    // ordinary name. Taken from Read-OtterValue, condition by condition.
    static readonly Dictionary<string, string[]> SpecialWhenFollowedBy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pretty"] = new[] { "text" },                           // pretty text from xml (D105)
        ["current"] = new[] { "route" },                         // current route (D103)
        ["old"] = new[] { "path" },                              // old path (D104)
        ["change"] = new[] { "kind" },                           // change kind (D104)
        ["changed"] = new[] { "file", "path" },                  // changed file name / changed path (D104)
        ["received"] = new[] { "message", "response", "output", "error", "data" },   // D106, D116B, D113
        ["close"] = new[] { "was", "code", "reason" },           // D106
        ["websocket"] = new[] { "error" },                       // D106
        ["secure"] = new[] { "random" },                         // secure random bytes (D109)
        ["hmac"] = new[] { "sha256", "sha384", "sha512" },       // D109
        ["drag"] = new[] { "data" },                             // D110
        ["dragged"] = new[] { "item" },
        ["drop"] = new[] { "x", "y" },
        ["dropped"] = new[] { "files" },
        ["sender"] = new[] { "address", "port" },                // D113
        ["network"] = new[] { "error" },
        ["incoming"] = new[] { "connection" },
    };

    // Tokens that cannot continue any of otterPS's special value forms.
    static readonly HashSet<TokenKind> SafeFollowers = new()
    {
        TokenKind.Newline, TokenKind.EndOfFile, TokenKind.BlockEnd, TokenKind.Dedent,
        TokenKind.Is, TokenKind.IsNot, TokenKind.IsAtLeast, TokenKind.IsAtMost, TokenKind.IsGreaterThan, TokenKind.IsLessThan,
        TokenKind.And, TokenKind.Or, TokenKind.Minus, TokenKind.Times, TokenKind.DividedBy, TokenKind.Percent, TokenKind.Power,
        TokenKind.Make, TokenKind.Into, TokenKind.Contains, TokenKind.StartsWith, TokenKind.EndsWith,
    };

    // otterPS's Test-OtterCommandResultPropertyAt: `exit code of x` and friends.
    bool CommandResultPropertyAhead()
    {
        Token first = Current;
        if (!IsIdentifier(first) || At(2) is null) return false;
        Token second = At(1)!, third = At(2)!;
        string pair = (first.Text + " " + second.Text).ToLowerInvariant();
        if (pair is "exit code" or "error output" or "rows affected" or "primary key" or "database type" or "default expression" &&
            third.Kind == TokenKind.Of) return true;
        if (At(3) is { } fourth && fourth.Kind == TokenKind.Of)
        {
            if (pair == "last inserted" && Same(third.Text, "id")) return true;
            if (pair == "primary key" && Same(third.Text, "position")) return true;
        }
        return false;
    }

    Expr ReadValue()
    {
        Token token = Current;
        if (token.Kind == TokenKind.Await) throw Unsupported("await");
        if (token.Kind == TokenKind.Not)
        {
            Read();
            return new NotExpr(ReadValue(), token.Line);
        }
        // D42: `days between a and b` is a value too.
        if (TimeUnitKinds.Contains(token.Kind) && IsAt(1, TokenKind.Between))
        {
            Read(); Read();
            Expr start = ReadValue();
            Expect(TokenKind.And, "I expected \"and\" and the second date.");
            Expr end = ReadValue();
            return new DateDifferenceExpr(ToTimeUnit(token.Kind), start, end, token.Line);
        }
        switch (token.Kind)
        {
            case TokenKind.Length: return ReadOfOperation(token, OfOperation.Length);
            case TokenKind.Uppercase: return ReadOfOperation(token, OfOperation.Uppercase);
            case TokenKind.Lowercase: return ReadOfOperation(token, OfOperation.Lowercase);
            case TokenKind.First: return ReadOfOperation(token, OfOperation.First);
            case TokenKind.Last: return ReadOfOperation(token, OfOperation.Last);
            case TokenKind.AbsoluteValue: return ReadOfOperation(token, OfOperation.AbsoluteValue);
            case TokenKind.SquareRoot: return ReadOfOperation(token, OfOperation.SquareRoot);
            case TokenKind.Round: return ReadOfOperation(token, OfOperation.Round);
            case TokenKind.RoundUp: return ReadOfOperation(token, OfOperation.RoundUp);
            case TokenKind.RoundDown: return ReadOfOperation(token, OfOperation.RoundDown);
            case TokenKind.Sine: return ReadOfOperation(token, OfOperation.Sine);
            case TokenKind.Cosine: return ReadOfOperation(token, OfOperation.Cosine);
            case TokenKind.Tangent: return ReadOfOperation(token, OfOperation.Tangent);
            case TokenKind.LogTen: return ReadOfOperation(token, OfOperation.LogTen);
            case TokenKind.NaturalLog: return ReadOfOperation(token, OfOperation.NaturalLog);
            // elapsed time of workTimer / elapsed milliseconds of workTimer (D101)
            case TokenKind.ElapsedTime: return ReadOfOperation(token, OfOperation.ElapsedTime);
            case TokenKind.ElapsedMilliseconds: return ReadOfOperation(token, OfOperation.ElapsedMilliseconds);
            // larger of X and Y / smaller of X and Y (D89)
            case TokenKind.Larger or TokenKind.Smaller:
            {
                Read();
                Expect(TokenKind.Of, "I expected \"of\" after this operation.");
                Expr left = ReadValue();
                Expect(TokenKind.And, "I expected \"and\" and the second value.");
                Expr right = ReadValue();
                return new MinMaxExpr(token.Kind == TokenKind.Larger, left, right, token.Line);
            }
        }
        // today / now (D32) and pi (D90) always mean the built-in value here.
        if (token.Kind == TokenKind.Identifier && Same(token.Text, "today")) { Read(); return new ClockExpr(ClockKind.Today, token.Line); }
        if (token.Kind == TokenKind.Identifier && Same(token.Text, "now")) { Read(); return new ClockExpr(ClockKind.Now, token.Line); }
        if (token.Kind == TokenKind.Identifier && Same(token.Text, "pi")) { Read(); return new LiteralExpr(Math.PI, token.Line); }
        // date from "2024-01-15" [using "MM/dd/yyyy"] (D101)
        if (token.Kind == TokenKind.Identifier && Same(token.Text, "date") && IsAt(1, TokenKind.From))
        {
            Read(); Read();
            Expr source = ReadValue();
            Expr? format = null;
            if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "using")) { Read(); format = ReadValue(); }
            return new DateFromTextExpr(source, format, token.Line);
        }
        // These four are ordinary names except in their exact phrases.
        bool exactPhrase = Same(token.Text, "date") || Same(token.Text, "bytes") || Same(token.Text, "empty") || Same(token.Text, "text");
        bool phraseFollows =
            (Same(token.Text, "date") || Same(token.Text, "bytes")) ? IsAt(1, TokenKind.From)        // date from / bytes from (D101, D102)
            : Same(token.Text, "text") ? IsAt(1, TokenKind.From) || IsAt(1, TokenKind.Of)            // text from bytes/xml, text of x (D102, D105)
            : Same(token.Text, "empty") ? At(1) is { Kind: TokenKind.Identifier } b && Same(b.Text, "bytes")   // empty bytes (D102)
            : false;
        // Every special form continues with a particular word; followed by
        // the end of the line, a comparison, arithmetic, make or into, the
        // word is an ordinary name (`pretty is "x"`, `say text`).
        bool ordinaryName = At(1) is { } follower && (SafeFollowers.Contains(follower.Kind) || follower.Text == ",");
        if (SpecialWhenFollowedBy.TryGetValue(token.Text, out var nextWords))
        {
            ordinaryName = !(At(1) is { } next && SameAny(next.Text, nextWords));
        }
        // element/elements/child/children SELECTOR in X and attribute NAME of X
        // (D105) are tried and abandoned, as otterPS does: when no selector and
        // "in" (or "of") follow, the word is an ordinary name.
        // D102: bytes from file|text|hex|base64 X / empty bytes /
        // text|hex|base64 from bytes X - otherwise these are ordinary names.
        if (token.Kind == TokenKind.Identifier && Same(token.Text, "bytes") && IsAt(1, TokenKind.From))
        {
            Read(); Read();
            Token encoding = Current;
            if (encoding.Kind == TokenKind.File || (encoding.Kind == TokenKind.Identifier && Same(encoding.Text, "file")))
            {
                Read();
                return new BytesFromFileExpr(ReadValue(), token.Line);
            }
            BytesOp? fromOp = encoding.Kind != TokenKind.Identifier ? null : encoding.Text.ToLowerInvariant() switch
            {
                "text" => BytesOp.FromText,
                "hex" => BytesOp.FromHex,
                "base64" => BytesOp.FromBase64,
                _ => null,
            };
            if (fromOp == null)
            {
                throw Error("I expected \"file\", \"text\", \"hex\", or \"base64\" after \"bytes from\".", encoding,
                    "Write: bytes from file \"photo.png\", bytes from text \"Hello\", bytes from hex \"48656C6C6F\", or bytes from base64 \"SGVsbG8=\"");
            }
            Read();
            return new BytesExpr(fromOp.Value, ReadValue(), token.Line);
        }
        if (token.Kind == TokenKind.Identifier && Same(token.Text, "empty") && At(1) is { Kind: TokenKind.Identifier } bytesWord && Same(bytesWord.Text, "bytes"))
        {
            Read(); Read();
            return new BytesExpr(BytesOp.Empty, null, token.Line);
        }
        if (token.Kind == TokenKind.Identifier && SameAny(token.Text, "text", "hex", "base64") && IsAt(1, TokenKind.From) &&
            At(2) is { Kind: TokenKind.Identifier } fromBytes && Same(fromBytes.Text, "bytes"))
        {
            BytesOp toOp = token.Text.ToLowerInvariant() switch { "text" => BytesOp.ToText, "hex" => BytesOp.ToHex, _ => BytesOp.ToBase64 };
            Read(); Read(); Read();
            return new BytesExpr(toOp, ReadValue(), token.Line);
        }
        // query parameter X (D103 routing) is not ported yet.
        if (token.Kind == TokenKind.Query && IsAt(1, TokenKind.Parameter)) throw Unsupported("query parameter");
        bool plainXmlWord = false;
        // xml from text|file X / xml with root X (D105)
        if (token.Kind == TokenKind.Identifier && Same(token.Text, "xml") && (IsAt(1, TokenKind.From) || IsAt(1, TokenKind.With)))
        {
            Read();
            Token fromOrWith = Read();
            if (fromOrWith.Kind == TokenKind.From)
            {
                Token kindWord = Current;
                if (kindWord.Kind == TokenKind.Identifier && Same(kindWord.Text, "text")) { Read(); return new XmlFromExpr(XmlSourceKind.Text, ReadValue(), token.Line); }
                if (kindWord.Kind == TokenKind.File) { Read(); return new XmlFromExpr(XmlSourceKind.File, ReadValue(), token.Line); }
                throw Error("I expected \"text\" or \"file\" after \"xml from\".", kindWord, "Write: xml from text source, or xml from file \"books.xml\"");
            }
            ExpectWord("root", "I expected \"root\" after \"xml with\".", "Write: xml with root \"library\"");
            return new XmlFromExpr(XmlSourceKind.Root, ReadValue(), token.Line);
        }
        // pretty text from xml X
        if (token.Kind == TokenKind.Identifier && Same(token.Text, "pretty") && At(1) is { Kind: TokenKind.Identifier } prettyText &&
            Same(prettyText.Text, "text") && IsAt(2, TokenKind.From))
        {
            Read(); Read(); Read();
            ExpectWord("xml", "I expected \"xml\" after \"pretty text from\".", "Write: pretty text from xml document");
            return new XmlToTextExpr(ReadValue(), true, token.Line);
        }
        // text from xml X / text of NAME in X (D105); text of X is the
        // property; any other text is an ordinary name.
        if (token.Kind == TokenKind.Identifier && Same(token.Text, "text"))
        {
            if (IsAt(1, TokenKind.From) && At(2) is { Kind: TokenKind.Identifier } xmlWord && Same(xmlWord.Text, "xml"))
            {
                Read(); Read(); Read();
                return new XmlToTextExpr(ReadValue(), false, token.Line);
            }
            if (IsAt(1, TokenKind.Of))
            {
                Read(); Read();
                Expr owner = ReadValue();
                if (Is(TokenKind.In))
                {
                    Read();
                    return new XmlTextOfNameInExpr(owner, ReadValue(), token.Line);
                }
                return new PropertyAccessExpr("text", owner, token.Line);
            }
            plainXmlWord = true;
        }
        if (token.Kind == TokenKind.Identifier && SameAny(token.Text, "element", "elements", "child", "children", "attribute"))
        {
            int saved = position;
            Read();
            Expr? selector = null;
            try { selector = ReadValue(); }
            catch (OtterError) { }
            bool attribute = Same(token.Text, "attribute");
            if (selector != null && Is(attribute ? TokenKind.Of : TokenKind.In))
            {
                Read();
                Expr owner = ReadValue();
                if (attribute) return new XmlAttributeExpr(selector, owner, token.Line);
                XmlSelectKind kind = token.Text.ToLowerInvariant() switch
                {
                    "element" => XmlSelectKind.Element,
                    "elements" => XmlSelectKind.Elements,
                    "child" => XmlSelectKind.Child,
                    _ => XmlSelectKind.Children,
                };
                return new XmlSelectExpr(kind, selector, owner, token.Line);
            }
            position = saved;
            plainXmlWord = true;
        }
        if (!plainXmlWord && (token.Kind == TokenKind.Identifier && exactPhrase ? phraseFollows : SpecialValueWords.Contains(token.Text) && !ordinaryName))
        {
            throw Unsupported($"'{token.Text}'");
        }
        // exit code of result / error output of result (D65), and the other
        // two-word properties, read like any property.
        if (CommandResultPropertyAhead())
        {
            Token first = Read();
            Token second = Read();
            string propertyName = $"{first.Text} {second.Text}";
            if ((Same(first.Text, "last") && Same(second.Text, "inserted") && Same(Current.Text, "id")) ||
                (Same(first.Text, "primary") && Same(second.Text, "key") && Same(Current.Text, "position")))
            {
                propertyName += " " + Read().Text;
            }
            Expect(TokenKind.Of, "I expected \"of\" after this command result property.");
            return new PropertyAccessExpr(propertyName, ReadValue(), first.Line);
        }
        if (IsIdentifier(token))
        {
            if (knownFunctions.ContainsKey(token.Text)) return ReadFunctionCallExpression(token);
            Read();
            if (Is(TokenKind.Of))
            {
                Read();
                return new PropertyAccessExpr(token.Text, ReadValue(), token.Line);
            }
            return new VariableExpr(token.Text, token.Line);
        }
        switch (token.Kind)
        {
            case TokenKind.String: Read(); return new LiteralExpr(token.Value, token.Line);
            case TokenKind.Number: Read(); return new LiteralExpr(token.Value, token.Line);
            case TokenKind.True: Read(); return new LiteralExpr(true, token.Line);
            case TokenKind.False: Read(); return new LiteralExpr(false, token.Line);
            case TokenKind.Gone: Read(); return new LiteralExpr(null, token.Line);
            case TokenKind.Today: Read(); return new ClockExpr(ClockKind.Today, token.Line);
            case TokenKind.Now: Read(); return new ClockExpr(ClockKind.Now, token.Line);
            case TokenKind.ConsoleInteractive: Read(); return new ConsoleInteractiveExpr(token.Line);
            default:
                throw Error("I expected a value here.", token, "Add a text value, number, true, false, or variable name.");
        }
    }

    Expr ReadOfOperation(Token token, OfOperation operation)
    {
        Read();
        Expect(TokenKind.Of, "I expected \"of\" after this operation.");
        // otterPS reads the subject with -PropertyTarget (which only matters
        // for "the"); "the" is a special form here anyway.
        return new OfOperationExpr(operation, ReadValue(), token.Line);
    }

    static MathOp ToMathOp(TokenKind kind) => kind switch
    {
        TokenKind.And => MathOp.Add,
        TokenKind.Minus => MathOp.Subtract,
        TokenKind.Times => MathOp.Multiply,
        TokenKind.DividedBy => MathOp.Divide,
        TokenKind.Percent => MathOp.Percent,
        _ => MathOp.Power,
    };

    Expr ReadMathExpression()
    {
        Expr left = ReadValue();
        while (Current.Kind is TokenKind.And or TokenKind.Minus or TokenKind.Times or TokenKind.DividedBy or TokenKind.Percent or TokenKind.Power)
        {
            Token op = Read();
            // `X percent of Y` (D88) has "of" between operator and operand.
            if (op.Kind == TokenKind.Percent) Expect(TokenKind.Of, "I expected \"of\" after \"percent\".");
            Expr right = ReadValue();
            left = new MathExpr(left, ToMathOp(op.Kind), right, op.Line);
        }
        if (Is(TokenKind.Or))
        {
            throw Error("\"or\" only works inside an if or while condition.", Current, "Move the boolean expression into an if or while condition.");
        }
        return left;
    }

    // ---- conditions (D11, D123) --------------------------------------------

    bool ConditionArithmeticOperatorAhead()
    {
        Token token = Current;
        return token.Kind switch
        {
            TokenKind.Minus or TokenKind.Times or TokenKind.DividedBy or TokenKind.Percent or TokenKind.Power => true,
            // D34: `plus` and `+` share And with the logical `and`; only the
            // spelled-out `and` stays logical inside a condition.
            TokenKind.And => SameAny(token.Text, "plus", "+"),
            _ => false,
        };
    }

    Expr ReadConditionOperand()
    {
        Expr left = ReadValue();
        while (ConditionArithmeticOperatorAhead())
        {
            Token op = Read();
            if (op.Kind == TokenKind.Percent) Expect(TokenKind.Of, "I expected \"of\" after \"percent\".");
            Expr right = ReadValue();
            left = new MathExpr(left, ToMathOp(op.Kind), right, op.Line);
        }
        return left;
    }

    bool OrdinaryComparisonAhead() =>
        Current.Kind is TokenKind.Is or TokenKind.IsNot or TokenKind.IsAtLeast or TokenKind.IsAtMost or TokenKind.IsGreaterThan or TokenKind.IsLessThan;

    static readonly string[] StateWords =
    {
        "watching", "connecting", "open", "closing", "connected", "closed",
        "secure", "listening", "stopped", "pending", "running", "completed", "failed", "cancelled",
    };

    bool StatePredicateAhead() =>
        Current.Kind is TokenKind.Is or TokenKind.IsNot && At(1) is { } next && SameAny(next.Text, StateWords);

    Expr ReadConditionPrimary()
    {
        if (Is(TokenKind.Not))
        {
            Token not = Read();
            return new NotExpr(ReadConditionPrimary(), not.Line);
        }
        Token head = Current;
        // element "book" exists in doc (D105). Only "exists" makes it this
        // form; otherwise the line is read again from "element".
        if (head.Kind == TokenKind.Identifier && Same(head.Text, "element"))
        {
            int saved = position;
            Read();
            Expr selector = ReadValue();
            if (Is(TokenKind.Exists))
            {
                Read();
                Expect(TokenKind.In, "I expected \"in\" after \"exists\".");
                return new XmlElementExistsExpr(selector, ReadValue(), head.Line);
            }
            position = saved;
        }
        if (head.Kind == TokenKind.Identifier && Same(head.Text, "registry")) throw Unsupported("'registry' conditions");
        // file "x" exists / file "x" is locked (D72); read only and
        // symbolic links are left to otterPS.
        if (head.Kind == TokenKind.File)
        {
            Read();
            Expr path = ReadValue();
            if (Is(TokenKind.Exists))
            {
                Read();
                return new FileExistsExpr(path, head.Line);
            }
            if (!(Is(TokenKind.Is) && At(1) is { Kind: TokenKind.Identifier } locked && Same(locked.Text, "locked"))) throw Unsupported("file conditions");
            Read(); Read();
            return new FileLockedExpr(path, head.Line);
        }
        if (head.Kind == TokenKind.Identifier && Same(head.Text, "password") && !IsAt(1, TokenKind.Is) && !IsAt(1, TokenKind.Of))
        {
            throw Unsupported("password conditions");
        }
        Expr left = ReadConditionOperand();
        if (left is MathExpr && (!OrdinaryComparisonAhead() || StatePredicateAhead()))
        {
            throw Error("Arithmetic in a condition must be followed by an ordinary comparison.", Current,
                "Compare the arithmetic result using is, is not, is at least, is at most, is greater than, or is less than.");
        }
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "securely")) throw Unsupported("securely equals");
        if (StatePredicateAhead() || (Is(TokenKind.Is) && At(1)?.Kind == TokenKind.Open)) throw Unsupported("state checks");
        // book has attribute "id" (D105)
        if (Is(TokenKind.Has) && At(1) is { Kind: TokenKind.Identifier } attribute && Same(attribute.Text, "attribute"))
        {
            Token hasWord = Read();
            Read();
            return new XmlHasAttributeExpr(left, ReadValue(), hasWord.Line);
        }
        if (Is(TokenKind.Contains))
        {
            Token op = Read();
            return new ContainsExpr(left, ReadValue(), op.Line);
        }
        if (Is(TokenKind.StartsWith) || Is(TokenKind.EndsWith))
        {
            Token op = Read();
            var match = op.Kind == TokenKind.StartsWith ? TextMatch.StartsWith : TextMatch.EndsWith;
            return new TextMatchExpr(left, match, ReadValue(), op.Line);
        }
        Token comparisonToken = Current;
        CompareOp? comparison = comparisonToken.Kind switch
        {
            TokenKind.Is => CompareOp.Equal,
            TokenKind.IsNot => CompareOp.NotEqual,
            TokenKind.IsAtLeast => CompareOp.AtLeast,
            TokenKind.IsAtMost => CompareOp.AtMost,
            TokenKind.IsGreaterThan => CompareOp.GreaterThan,
            TokenKind.IsLessThan => CompareOp.LessThan,
            _ => null,
        };
        if (comparison is null)
        {
            if (Current.Kind is TokenKind.IsIn or TokenKind.IsNotIn or TokenKind.IsBetween) throw Unsupported($"'{Current.Text}'");
            return left;
        }
        Read();
        return new ComparisonExpr(left, comparison.Value, ReadConditionOperand(), comparisonToken.Line);
    }

    // D38B: a condition may continue on the next line after `and` or `or`.
    void ReadConditionContinuation(bool allowContinuation)
    {
        if (!Is(TokenKind.Newline) || !allowContinuation) return;
        if (!conditionUsesContinuation)
        {
            if (!IsAt(1, TokenKind.Indent))
            {
                throw Error("I expected an indented condition continuation after this connective.", Current,
                    "Indent the next condition line one level, or finish the condition on the same line.");
            }
            Read(); Read();
            conditionUsesContinuation = true;
        }
        else
        {
            Read();
            if (Is(TokenKind.Indent))
            {
                throw Error("I expected the continued condition to stay at its current indentation level.", Current,
                    "Keep each continued condition line aligned with the first continuation line.");
            }
        }
        if (Current.Kind is TokenKind.Newline or TokenKind.Dedent or TokenKind.BlockEnd or TokenKind.EndOfFile or TokenKind.And or TokenKind.Or)
        {
            throw Error("I expected a condition after this connective.", Current, "Write a comparison or condition after \"and\" or \"or\".");
        }
    }

    Expr ReadAndCondition(bool allowContinuation)
    {
        Expr left = ReadConditionPrimary();
        while (Is(TokenKind.And))
        {
            Token op = Read();
            if (!Same(op.Text, "and"))
            {
                throw Error($"'{op.Text}' is arithmetic, not a condition connector.", op,
                    "Use \"and\" between conditions, or compare the arithmetic result with is.");
            }
            ReadConditionContinuation(allowContinuation);
            left = new LogicalExpr(left, LogicalOp.And, ReadConditionPrimary(), op.Line);
        }
        return left;
    }

    Expr ReadCondition(bool allowContinuation)
    {
        Expr left = ReadAndCondition(allowContinuation);
        while (Is(TokenKind.Or))
        {
            Token op = Read();
            ReadConditionContinuation(allowContinuation);
            left = new LogicalExpr(left, LogicalOp.Or, ReadAndCondition(allowContinuation), op.Line);
        }
        return left;
    }

    Expr ReadHeaderCondition()
    {
        conditionUsesContinuation = false;
        return ReadCondition(allowContinuation: true);
    }

    List<Stmt> ReadConditionBlock()
    {
        if (!conditionUsesContinuation) return ReadBlock();
        // The continuation's Indent is already read: the body follows at
        // that same level.
        Expect(TokenKind.Newline, "I expected the continued condition to end here.");
        if (Current.Kind is TokenKind.Dedent or TokenKind.BlockEnd or TokenKind.EndOfFile)
        {
            throw Error("I expected an indented block after this condition.", Current, "Add at least one statement after the continued condition.");
        }
        blockDepth++;
        List<Stmt> body;
        try { body = ReadStatements(); }
        finally { blockDepth--; }
        Expect(TokenKind.Dedent, "I expected the indented block to end.");
        if (Is(TokenKind.BlockEnd)) { Read(); SkipNewlines(); }
        return body;
    }
}
