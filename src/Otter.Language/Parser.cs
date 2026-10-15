namespace Otter.Language;

// Thrown when a program uses something otter-net has not ported yet. The
// `otter` command then hands the whole program to the PowerShell Otter, so
// the user gets exactly today's behaviour - before anything has run.
public sealed class NotSupportedYet(string feature, int line) : Exception($"line {line}: {feature}")
{
    public string Feature { get; } = feature;
    public int Line { get; } = line;
}

// Tokens become the syntax tree. A port of otterPS's src/Otter.Parser.psm1
// for the core language, function by function, with the same messages.
//
// Where otterPS takes one of its many special forms (dates, files, things,
// UI, networking, queries ...), this parser throws NotSupportedYet instead of
// guessing, and a syntax error is reported by the PowerShell Otter too - so
// otter-net never prints a different error than otterPS while the parser is
// still incomplete.
public sealed class Parser
{
    readonly IReadOnlyList<Token> tokens;
    int position;
    int blockDepth;
    bool conditionUsesContinuation;
    // A plain PowerShell hashtable in otterPS: function names are matched
    // ignoring case at parse time (variables are not - D10).
    readonly Dictionary<string, int> knownFunctions = new(StringComparer.OrdinalIgnoreCase);

    Parser(IReadOnlyList<Token> tokens) => this.tokens = tokens;

    public static ProgramNode Parse(IReadOnlyList<Token> tokens)
    {
        var parser = new Parser(tokens);
        var statements = parser.ReadStatements();
        if (parser.Is(TokenKind.BlockEnd))
        {
            Token token = parser.Read();
            throw parser.Error("There is no open block for this period to close.", token, "Remove this period or place it after an indented block.", "UnexpectedPeriod");
        }
        if (!parser.Is(TokenKind.EndOfFile))
        {
            Token token = parser.Current;
            throw parser.Error($"Unexpected input at end of program: '{token.Text}'.", token, "Make sure all statements and blocks are valid.", "TrailingInput");
        }
        return new ProgramNode(statements);
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

    // ---- statements --------------------------------------------------------

    List<Stmt> ReadStatements()
    {
        var statements = new List<Stmt>();
        SkipNewlines();
        while (!Is(TokenKind.Dedent) && !Is(TokenKind.EndOfFile) && !Is(TokenKind.BlockEnd))
        {
            statements.Add(ReadStatement());
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

    // Words that start one of otterPS's special statement forms before its
    // main statement switch. otter-net does not run those yet.
    static readonly string[] SpecialStatementWords =
    {
        "the", "close", "stop", "cancel", "set", "store", "delete", "generate", "open", "listen", "send",
        "route", "go", "watch", "primary", "secondary", "danger",
        "window", "page", "card", "heading", "text", "button", "panel", "section", "sidebar", "main",
        "link", "image", "input", "grid",
    };

    Stmt ReadStatement()
    {
        Token start = Current;

        if (TimeUnitKinds.Contains(start.Kind)) throw Unsupported("dates");
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
                if (Is(TokenKind.In)) throw Unsupported("say in color");
                Expect(TokenKind.Newline, "I expected the say statement to end here.");
                return new SayStmt(parts, start.Line);
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
                if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "secretly")) throw Unsupported("ask secretly");
                Expr prompt = ReadValue();
                Expect(TokenKind.And, "I expected \"and call it\" after the question.");
                Expect(TokenKind.Call, "I expected \"call\" after \"and\".");
                Expect(TokenKind.It, "I expected \"it\" after \"call\".");
                Token name = ReadVariableName("I expected a variable name after \"call it\".");
                Expect(TokenKind.Newline, "I expected the question to end here.");
                return new AskStmt(prompt, name.Text, start.Line);
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
                // otterPS has many more statements (files, UI, try, use ...).
                throw Unsupported($"the '{start.Text}' statement");
        }
    }

    Stmt ReadAdd(Token start)
    {
        Read();
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "element")) throw Unsupported("xml");
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
            if (TimeUnitKinds.Contains(Current.Kind)) throw Unsupported("dates");
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
        if (Current.Kind == TokenKind.Identifier && SameAny(Current.Text, "element", "attribute")) throw Unsupported("xml");
        if (Same(start.Text, "decrease"))
        {
            Token target = ReadVariableName("I expected a variable name after \"decrease\".");
            Expr amount = new LiteralExpr(1.0, start.Line);
            if (Is(TokenKind.By)) { Read(); amount = ReadMathExpression(); }
            Expect(TokenKind.Newline, "I expected the decrease statement to end here.");
            return new RemoveFromStmt(amount, target.Text, start.Line);
        }
        Expr removed = ReadMathExpression();
        if (TimeUnitKinds.Contains(Current.Kind)) throw Unsupported("dates");
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
        if (Is(TokenKind.Has)) throw Unsupported("things");
        if (Is(TokenKind.Of)) throw Unsupported("properties");
        if (Is(TokenKind.Is))
        {
            Read();
            if (Is(TokenKind.A) || (Current.Kind == TokenKind.Identifier && Same(Current.Text, "panel") && IsAt(1, TokenKind.With)))
            {
                throw Unsupported("things");
            }
            Expr value = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected the assignment to end here.");
            return new AssignStmt(name.Text, value, name.Line);
        }
        if (Is(TokenKind.IsNot))
        {
            Read();
            Expr value = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected the assignment to end here.");
            return new AssignStmt(name.Text, new NotExpr(value, name.Line), name.Line);
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

    // ---- values and arithmetic ---------------------------------------------

    // Words that begin one of Read-OtterValue's special forms in otterPS.
    static readonly HashSet<string> SpecialValueWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "today", "now", "pi", "the", "date", "bytes", "empty", "text", "hex", "base64", "current", "route",
        "changed", "change", "old", "received", "close", "websocket", "secret", "xml", "pretty",
        "element", "elements", "child", "children", "attribute", "drag", "dragged", "drop", "dropped",
        "hmac", "incoming", "local", "network", "remote", "sender", "secure", "tls", "sha256", "sha384", "sha512",
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
        if (TimeUnitKinds.Contains(token.Kind)) throw Unsupported("dates");
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
            case TokenKind.Sine or TokenKind.Cosine or TokenKind.Tangent or TokenKind.LogTen or TokenKind.NaturalLog
                or TokenKind.ElapsedTime or TokenKind.ElapsedMilliseconds or TokenKind.Larger or TokenKind.Smaller:
                throw Unsupported($"'{token.Text} of'");
        }
        if (SpecialValueWords.Contains(token.Text)) throw Unsupported($"'{token.Text}'");
        if (CommandResultPropertyAhead()) throw Unsupported("command results");
        if (IsIdentifier(token))
        {
            if (knownFunctions.ContainsKey(token.Text)) return ReadFunctionCallExpression(token);
            Read();
            if (Is(TokenKind.Of)) throw Unsupported("properties");
            return new VariableExpr(token.Text, token.Line);
        }
        switch (token.Kind)
        {
            case TokenKind.String: Read(); return new LiteralExpr(token.Value, token.Line);
            case TokenKind.Number: Read(); return new LiteralExpr(token.Value, token.Line);
            case TokenKind.True: Read(); return new LiteralExpr(true, token.Line);
            case TokenKind.False: Read(); return new LiteralExpr(false, token.Line);
            case TokenKind.Gone: Read(); return new LiteralExpr(null, token.Line);
            case TokenKind.Today or TokenKind.Now or TokenKind.ConsoleInteractive: throw Unsupported($"'{token.Text}'");
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
        if (head.Kind == TokenKind.Identifier && SameAny(head.Text, "element", "registry")) throw Unsupported($"'{head.Text}' conditions");
        if (head.Kind == TokenKind.File) throw Unsupported("file conditions");
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
        if (Is(TokenKind.Has) && At(1) is { Kind: TokenKind.Identifier } attribute && Same(attribute.Text, "attribute")) throw Unsupported("xml");
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
