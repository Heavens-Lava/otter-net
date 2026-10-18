namespace Otter.Language;

// The parser for networking and events: HTTP, downloads, servers and routes,
// WebSockets, TCP, UDP, file watching, the event loop (D130, D133).
//
// A port of the matching parts of otterPS's Read-OtterStatement (its
// pre-switch special forms and the Await, On, When, Respond, Start, Listen,
// Post, Put, Get, Delete, Download, Connect and Return cases),
// Read-OtterValue and Read-OtterConditionPrimary, with the same messages.
// The hooks run before the shared parser's own checks, so each one claims a
// line only when otterPS would read it as one of this area's forms; anything
// else is left untouched (null) for the rest of the parser.
public sealed partial class Parser
{
    static bool AssignmentFollows(TokenKind next) => next is TokenKind.Is or TokenKind.Are or TokenKind.Of or TokenKind.IsNot;

    bool IsWordAt(int offset, string word) => At(offset) is { } token && Same(token.Text, word);

    bool IsIdentifierWordAt(int offset, string word) => At(offset) is { Kind: TokenKind.Identifier } token && Same(token.Text, word);

    // A statement this area starts at `start`, or null (the token is left unread).
    Stmt? TryReadNetworkStatement(Token start)
    {
        TokenKind next = At(1)?.Kind ?? TokenKind.EndOfFile;
        bool assignment = AssignmentFollows(next);
        // otterPS's $statementKind: a word that can be a name, followed by
        // is/are/of/is not, makes the line an ordinary assignment.
        bool identifierStatement = (IsIdentifier(start) || (start.Kind == TokenKind.ForEach && Same(start.Text, "each"))) && assignment;

        // ---- the special forms otterPS checks before its statement switch ----

        // D104: watch file "..." and call it X / watch folder "..." [recursively] and call it X
        if (Same(start.Text, "watch") && !assignment && next is TokenKind.File or TokenKind.Folder) return ReadFileWatch();
        // D107/D108: close tcp connection / close udp socket
        if (Same(start.Text, "close") && At(1) is { Kind: TokenKind.Identifier } closeWhat && SameAny(closeWhat.Text, "tcp", "udp") && !IsAt(2, TokenKind.Newline))
        {
            Read();
            string protocol = Read().Text;
            Expr socket = ReadValue();
            Expect(TokenKind.Newline, $"I expected the close {protocol} statement to end here.");
            return new NetCloseStmt(protocol, socket, start.Line);
        }
        // D113: stop tcp <server>
        if (Same(start.Text, "stop") && IsIdentifierWordAt(1, "tcp") && !IsAt(2, TokenKind.Newline))
        {
            Read(); Read();
            Expr server = ReadValue();
            Expect(TokenKind.Newline, "I expected the stop tcp statement to end here.");
            return new TcpStopStmt(server, start.Line);
        }
        // D116B: cancel <request>
        if (Same(start.Text, "cancel") && !assignment)
        {
            Read();
            Expr request = ReadValue();
            Expect(TokenKind.Newline, "I expected the cancel statement to end here.");
            return new HttpCancelStmt(request, start.Line);
        }
        // D107: open udp [on port 9000] and call it socket
        if (Same(start.Text, "open") && IsIdentifierWordAt(1, "udp")) return ReadUdpOpen(start);
        // D113: listen for tcp [on <address>] on port <port> and call it <name>
        if (Same(start.Text, "listen"))
        {
            if (At(3) != null && IsWordAt(1, "securely") && IsWordAt(2, "for") && IsWordAt(3, "tcp"))
            {
                throw Error("\"listen securely for tcp\" is reserved for TLS servers, which Otter does not support yet.", start,
                    "Write: listen for tcp on port 8080 and call it server");
            }
            if (At(2) != null && IsWordAt(1, "for") && IsWordAt(2, "tcp")) return ReadTcpListen(start);
        }
        // D106: close websocket socket [with code 1000] [and reason "Done"]
        if (Same(start.Text, "close") && !assignment && IsIdentifierWordAt(1, "websocket")) return ReadWebSocketClose(start);
        // D106: send "Hello" through socket (D108: ... to <host> on port <port>)
        if (Same(start.Text, "send") && !assignment) return ReadSend(start);

        // ---- otterPS's statement switch ----

        if (identifierStatement) return null;
        switch (start.Kind)
        {
            // `await` is read by Parser.Ui.cs, whose hook only runs for the web target.
            case TokenKind.Await:
                return ReadAwaitStatement(start);
            case TokenKind.On:
                return ReadOnHandler(start);
            // D51: when <server> receives <method> at <path> [into <request>]
            case TokenKind.When when At(1) is { } routeTarget && IsIdentifier(routeTarget) && IsAt(2, TokenKind.Receives):
                return ReadWebRoute(start);
            case TokenKind.Respond:
                return ReadRespond(start);
            case TokenKind.Start:
                return ReadStart(start);
            case TokenKind.Listen:
                return ReadListenServer(start);
            case TokenKind.Post:
                return ReadPost(start);
            case TokenKind.Put:
                return TryReadHttpPut(start);
            case TokenKind.Get:
                return TryReadHttpGet(start);
            case TokenKind.Delete when next == TokenKind.From:
                return ReadHttpDelete(start);
            case TokenKind.Download:
                return ReadDownload(start);
            case TokenKind.Connect when IsIdentifierWordAt(1, "securely") || next == TokenKind.To:
                return ReadNetworkConnect(start);
            // `stop` lexes as Return. D104: stop watching X; any other word
            // after `stop` is otterPS's "I expected stop to end here."
            case TokenKind.Return when Same(start.Text, "stop") && next != TokenKind.Newline:
            {
                Read();
                if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "watching"))
                {
                    Read();
                    Expr watcher = ReadValue();
                    Expect(TokenKind.Newline, "I expected the stop watching statement to end here.");
                    return new StopWatchingStmt(watcher, start.Line);
                }
                Expect(TokenKind.Newline, "I expected stop to end here.");
                return new ReturnStmt(null, start.Line);
            }
        }
        return null;
    }

    // ---- file watching (D104) ----

    Stmt ReadFileWatch()
    {
        Token start = Read();
        Token kindToken = Read();
        WatchKind kind = kindToken.Kind == TokenKind.File ? WatchKind.File : WatchKind.Folder;
        Expr path = ReadValue();
        bool recursive = false;
        if (kind == WatchKind.Folder && Current.Kind == TokenKind.Identifier && Same(Current.Text, "recursively"))
        {
            Read();
            recursive = true;
        }
        Expect(TokenKind.And, "I expected \"and call it\" and a name after the watch path.", "Write: watch file \"settings.json\" and call it settingsWatcher");
        Expect(TokenKind.Call, "I expected \"call\" after \"and\".");
        Expect(TokenKind.It, "I expected \"it\" after \"call\".");
        Token name = ReadVariableName("I expected a name after \"call it\".");
        Expect(TokenKind.Newline, "I expected the watch statement to end here.");
        return new FileWatchStmt(kind, path, recursive, name.Text, start.Line);
    }

    // ---- TCP and UDP clauses (Read-OtterNetClauses, Read-OtterListenClauses) ----

    sealed class NetClauses
    {
        public Expr? Host, Port, ServerName, Protocols, Address;
        public string? Target;
    }

    // to <host> / on port <port> / for server <name> / using protocol(s) <p> /
    // and call it <name>, in any order, on the line or in an indented block.
    NetClauses ReadNetClauses(string endMessage)
    {
        var result = new NetClauses();
        int depth = 0;
        while (true)
        {
            Token current = Current;
            if (current.Kind == TokenKind.Newline)
            {
                if (depth == 0)
                {
                    if (IsAt(1, TokenKind.Indent))
                    {
                        Read(); Read();
                        depth = 1;
                        continue;
                    }
                    Read();
                    break;
                }
                Read();
                if (Is(TokenKind.Dedent)) { Read(); break; }
                continue;
            }
            if (current.Kind == TokenKind.To && result.Host == null)
            {
                Read();
                result.Host = ReadValue();
                continue;
            }
            if (Same(current.Text, "on") && result.Port == null && IsWordAt(1, "port"))
            {
                Read(); Read();
                result.Port = ReadValue();
                continue;
            }
            // D112: for server <server-name-expression>
            if (Same(current.Text, "for") && result.ServerName == null && IsWordAt(1, "server"))
            {
                Read(); Read();
                result.ServerName = ReadValue();
                continue;
            }
            // D112: using protocol <p> / using protocols <list>
            if (Same(current.Text, "using") && result.Protocols == null && At(1) is { } protocolWord && SameAny(protocolWord.Text, "protocol", "protocols"))
            {
                Read(); Read();
                result.Protocols = ReadValue();
                continue;
            }
            if (current.Kind == TokenKind.And && IsAt(1, TokenKind.Call) && result.Target == null)
            {
                Read(); Read();
                Expect(TokenKind.It, "I expected \"it\" after \"call\".");
                result.Target = ReadVariableName("I expected a name after \"call it\".").Text;
                continue;
            }
            throw Error(endMessage, current);
        }
        return result;
    }

    // [on <address>] on port <port> and call it <name> (D113)
    NetClauses ReadListenClauses(string endMessage)
    {
        var result = new NetClauses();
        int depth = 0;
        while (true)
        {
            Token current = Current;
            if (current.Kind == TokenKind.Newline)
            {
                if (depth == 0)
                {
                    if (IsAt(1, TokenKind.Indent))
                    {
                        Read(); Read();
                        depth = 1;
                        continue;
                    }
                    Read();
                    break;
                }
                Read();
                if (Is(TokenKind.Dedent)) { Read(); break; }
                continue;
            }
            if (Same(current.Text, "on"))
            {
                if (IsWordAt(1, "port") && result.Port == null)
                {
                    Read(); Read();
                    result.Port = ReadValue();
                    continue;
                }
                if (result.Address == null)
                {
                    Read();
                    result.Address = ReadValue();
                    continue;
                }
            }
            if (current.Kind == TokenKind.And && IsAt(1, TokenKind.Call) && result.Target == null)
            {
                Read(); Read();
                Expect(TokenKind.It, "I expected \"it\" after \"call\".");
                result.Target = ReadVariableName("I expected a name after \"call it\".").Text;
                continue;
            }
            throw Error(endMessage, current);
        }
        return result;
    }

    Stmt ReadUdpOpen(Token start)
    {
        Read(); Read();
        const string message = "I expected \"and call it\" and a name after \"open udp\".";
        NetClauses clauses = ReadNetClauses(message);
        if (clauses.Target == null) throw Error(message, Current, "Write: open udp on port 9000 and call it socket");
        if (clauses.Host != null) throw Error("\"open udp\" takes only \"on port\" - use \"to ...\" when sending.", start, "Write: open udp on port 9000 and call it socket");
        return new UdpOpenStmt(clauses.Port, clauses.Target, start.Line);
    }

    Stmt ReadTcpListen(Token start)
    {
        Read(); Read(); Read();
        NetClauses clauses = ReadListenClauses("I expected \"on port\" and \"and call it\" in the listen statement.");
        if (clauses.Port == null) throw Error("I expected \"on port <number>\" in \"listen for tcp\".", start, "Write: listen for tcp on port 8080 and call it server");
        if (clauses.Target == null) throw Error("I expected \"and call it\" and a name in \"listen for tcp\".", start, "Write: listen for tcp on port 8080 and call it server");
        return new TcpListenStmt(clauses.Address, clauses.Port, clauses.Target, start.Line);
    }

    Stmt ReadWebSocketClose(Token start)
    {
        Read(); Read();
        Expr socket = ReadValue();
        Expr? code = null, reason = null;
        if (Is(TokenKind.With) && IsIdentifierWordAt(1, "code"))
        {
            Read(); Read();
            code = ReadValue();
        }
        if (Is(TokenKind.And) && IsIdentifierWordAt(1, "reason"))
        {
            Read(); Read();
            reason = ReadMathExpression();
        }
        Expect(TokenKind.Newline, "I expected the close websocket statement to end here.");
        return new WebSocketCloseStmt(socket, code, reason, start.Line);
    }

    Stmt ReadSend(Token start)
    {
        Read();
        Expr message = ReadMathExpression();
        Token through = Current;
        if (!(through.Kind == TokenKind.Identifier && Same(through.Text, "through")))
        {
            throw Error("I expected \"through\" and a websocket after the message.", through, "Write: send \"Hello\" through socket");
        }
        Read();
        Expr socket = ReadValue();
        // D108: a udp send carries "to <host> on port <port>".
        NetClauses clauses = ReadNetClauses("I expected the send statement to end here.");
        if (clauses.Host != null || clauses.Port != null)
        {
            if (clauses.Host == null || clauses.Port == null)
            {
                throw Error("A udp send needs both \"to <host>\" and \"on port <port>\".", start, "Write: send data through socket to \"127.0.0.1\" on port 9000");
            }
            return new UdpSendStmt(message, socket, clauses.Host, clauses.Port, start.Line);
        }
        return new WebSocketSendStmt(message, socket, start.Line);
    }

    // ---- event handlers: on ... (D104, D106, D107, D113, D116B) ----

    Stmt? ReadOnHandler(Token start)
    {
        Token? stage = At(1);
        if (stage == null) return null;
        TokenKind afterStage = At(2)?.Kind ?? TokenKind.EndOfFile;
        // D103: on route change - the UI's.
        if (stage.Kind == TokenKind.Identifier && Same(stage.Text, "route") && IsIdentifierWordAt(2, "change")) return null;
        // D104: on change of X / on create|delete|rename in X
        if (stage.Kind == TokenKind.Identifier && Same(stage.Text, "change") && afterStage == TokenKind.Of) return ReadWatchEvent(start, WatchEventKind.Change);
        if (stage.Kind == TokenKind.Identifier && Same(stage.Text, "create") && afterStage == TokenKind.In) return ReadWatchEvent(start, WatchEventKind.Create);
        if (stage.Kind == TokenKind.Identifier && Same(stage.Text, "delete") && afterStage == TokenKind.In) return ReadWatchEvent(start, WatchEventKind.Delete);
        if (stage.Kind == TokenKind.Identifier && Same(stage.Text, "rename") && afterStage == TokenKind.In) return ReadWatchEvent(start, WatchEventKind.Rename);
        // D106: on open of <socket>
        if ((stage.Kind == TokenKind.Open || Same(stage.Text, "open")) && afterStage == TokenKind.Of) return ReadNetworkEvent(start, NetworkEventKind.Open);
        // D110: drag and drop - the UI's.
        if (Same(stage.Text, "drag") && afterStage == TokenKind.Of) return null;
        if (Same(stage.Text, "drop") && IsWordAt(2, "on")) return null;
        if (Same(stage.Text, "files") && At(3) != null && IsWordAt(2, "dropped") && IsWordAt(3, "on")) return null;
        // D113: on connection to <server>
        if (Same(stage.Text, "connection") && afterStage == TokenKind.To) return ReadNetworkEvent(start, NetworkEventKind.Connection);
        // D107: on connect of <connection>
        if (Same(stage.Text, "connect") && afterStage == TokenKind.Of) return ReadNetworkEvent(start, NetworkEventKind.Connect);
        // D119-R2: on output from / on error output from / on exit of <job>
        if (Same(stage.Text, "output") && afterStage == TokenKind.From) return ReadJobHandler(start, "output", 3);
        if ((stage.Kind == TokenKind.Problem || Same(stage.Text, "error")) && At(3) != null && IsWordAt(2, "output") && IsAt(3, TokenKind.From))
        {
            return ReadJobHandler(start, "error output", 4);
        }
        if (Same(stage.Text, "exit") && afterStage == TokenKind.Of) return ReadJobHandler(start, "exit", 3);
        // D107/D108: on data from; D106: on message from / close of / error of
        if (Same(stage.Text, "data") && afterStage == TokenKind.From) return ReadNetworkEvent(start, NetworkEventKind.Data);
        if (Same(stage.Text, "message") && afterStage == TokenKind.From) return ReadNetworkEvent(start, NetworkEventKind.Message);
        if (Same(stage.Text, "close") && afterStage == TokenKind.Of) return ReadNetworkEvent(start, NetworkEventKind.Close);
        if ((stage.Kind == TokenKind.Problem || Same(stage.Text, "error")) && afterStage == TokenKind.Of) return ReadNetworkEvent(start, NetworkEventKind.Error);
        // D116B: on complete of / on cancel of <request>
        if (Same(stage.Text, "complete") && afterStage == TokenKind.Of) return ReadNetworkEvent(start, NetworkEventKind.Complete);
        if (Same(stage.Text, "cancel") && afterStage == TokenKind.Of) return ReadNetworkEvent(start, NetworkEventKind.Cancel);
        // on start / on close are lifecycle stages (the UI's); anything else
        // is otterPS's syntax error.
        if (SameAny(stage.Text, "start", "close")) return null;
        Read(); Read();
        throw Error($"I expected 'start' or 'close' after 'on', but got '{stage.Text}'.", stage, "Write 'on start' or 'on close'.");
    }

    // A command job's handlers are `when` handlers in otterPS (WhenStmt).
    Stmt ReadJobHandler(Token start, string eventName, int words)
    {
        for (int i = 0; i < words; i++) Read();
        Expr job = ReadValue();
        return new WhenStmt(job, eventName, ReadBlock(), start.Line);
    }

    Stmt ReadWatchEvent(Token start, WatchEventKind kind)
    {
        Read(); Read(); Read();
        Expr watcher = ReadValue();
        return new WatchEventStmt(kind, watcher, ReadBlock(), start.Line);
    }

    Stmt ReadNetworkEvent(Token start, NetworkEventKind kind)
    {
        Read(); Read(); Read();
        Expr socket = ReadValue();
        return new NetworkEventStmt(kind, socket, ReadBlock(), start.Line);
    }

    // ---- servers and routes (D51) ----

    Stmt ReadWebRoute(Token start)
    {
        Read();
        Token targetToken = ReadVariableName("I expected a resource name after \"when\".");
        var target = new VariableExpr(targetToken.Text, targetToken.Line);
        Read(); // receives
        string method = "GET";
        Token current = Current;
        if (current.Kind == TokenKind.A)
        {
            Read();
            Token request = Read();
            if (!Same(request.Text, "request")) throw Error("I expected \"request\" after \"a\".", request, "Write `when server receives a request at \"/path\"`.");
            method = "ALL";
        }
        else if (current.Kind != TokenKind.At && !Same(current.Text, "at"))
        {
            method = Read().Text.ToUpperInvariant();
        }
        if (!Is(TokenKind.At) && !Same(Current.Text, "at")) throw Error("I expected \"at\" and a route path.", Current, "Write `at \"/path\"` after the method.");
        Read();
        Expr path = ReadValue();
        string? requestTarget = null;
        if (Is(TokenKind.Into))
        {
            Read();
            requestTarget = ReadVariableName("I expected a variable name after \"into\".").Text;
        }
        return new WebRouteStmt(target, method, path, requestTarget, ReadBlock(), start.Line);
    }

    Stmt ReadRespond(Token start)
    {
        Read();
        Expect(TokenKind.With, "I expected \"with\" after \"respond\".");
        if (Same(Current.Text, "status"))
        {
            Read();
            Expr statusOnly = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected the respond statement to end here.");
            return new RespondStmt(null, statusOnly, false, start.Line);
        }
        Expr value = ReadMathExpression();
        bool asJson = false;
        if (Is(TokenKind.As))
        {
            Read();
            Expect(TokenKind.Json, "I expected \"json\" after \"as\".");
            asJson = true;
        }
        Expr? status = null;
        // otterPS writes `Test-OtterTokenKind And -or Test-OtterTokenKind With`
        // without parentheses, which PowerShell reads as one call with extra
        // arguments: only "and status" is recognised, never "with status".
        if (Is(TokenKind.And) && IsWordAt(1, "status"))
        {
            Read(); Read();
            status = ReadMathExpression();
        }
        Expect(TokenKind.Newline, "I expected the respond statement to end here.");
        return new RespondStmt(value, status, asJson, start.Line);
    }

    Stmt? ReadStart(Token start)
    {
        Token? next = At(1);
        if (next == null) return null;
        // D116B: start get/post/put/delete ... and call it <target>
        if (next.Kind is TokenKind.Get or TokenKind.Post or TokenKind.Put or TokenKind.Delete || SameAny(next.Text, "get", "post", "put", "delete"))
        {
            return ReadHttpStart(start);
        }
        // D119-R2: start command <expr> and call it <target>
        if (next.Kind == TokenKind.Command || (next.Kind == TokenKind.Identifier && Same(next.Text, "command")))
        {
            Read(); Read();
            Expr commandLine = ReadValue();
            Expect(TokenKind.And, "I expected \"and call it\" and a variable name.");
            Expect(TokenKind.Call, "I expected \"call\" after \"and\".");
            Expect(TokenKind.It, "I expected \"it\" after \"call\".");
            Token jobName = ReadVariableName("I expected a variable name after \"call it\".");
            Expect(TokenKind.Newline, "I expected the start command statement to end here.");
            return new StartCommandStmt(commandLine, jobName.Text, start.Line);
        }
        // start timer (D101) is core.
        if (next.Kind == TokenKind.Identifier && Same(next.Text, "timer")) return null;
        Read();
        ReadOptionalTheBeforeName();
        Token targetToken = ReadVariableName("I expected a server name after \"start\".");
        Expect(TokenKind.Newline, "I expected the start statement to end here.");
        return new StartServerStmt(new VariableExpr(targetToken.Text, targetToken.Line), start.Line);
    }

    Stmt ReadListenServer(Token start)
    {
        Read();
        if (Same(Current.Text, "on")) Read();
        if (Same(Current.Text, "port"))
        {
            Read();
            Expr port = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected the listen statement to end here.");
            return new ListenServerStmt(port, start.Line);
        }
        Token targetToken = ReadVariableName("I expected \"on port <number>\" or a server name after \"listen\".");
        Expect(TokenKind.Newline, "I expected the listen statement to end here.");
        return new StartServerStmt(new VariableExpr(targetToken.Text, targetToken.Line), start.Line);
    }

    // ---- HTTP (D49, D96, D101, D116A, D116B) ----

    // Test-OtterSoftContinuation (D38A): a clause continued on an indented line.
    bool SoftContinuation()
    {
        if (!Is(TokenKind.Newline) || !IsAt(1, TokenKind.Indent)) return false;
        Read(); Read();
        return true;
    }

    // Read-OtterHttpOptions: the optional indented block of option clauses.
    // A damaged clause is recorded and skipped, as otterPS does.
    HttpOptions? ReadHttpOptions()
    {
        if (!Is(TokenKind.Newline) || !IsAt(1, TokenKind.Indent)) return null;
        Read(); Read();
        var options = new HttpOptions();
        while (!Is(TokenKind.Dedent) && !Is(TokenKind.EndOfFile))
        {
            int before = position;
            try
            {
                Token clause = Current;
                if (clause.Kind == TokenKind.With)
                {
                    Read();
                    Token word = Current;
                    if (word.Kind == TokenKind.Identifier && Same(word.Text, "header"))
                    {
                        Read();
                        Expr name = ReadValue();
                        Expect(TokenKind.Is, "I expected \"is\" and a header value after the header name.", "Write: with header \"Accept\" is \"application/json\"");
                        options.Headers.Add(new HttpHeaderClause(name, ReadValue()));
                    }
                    else if (word.Kind == TokenKind.Identifier && Same(word.Text, "cookies"))
                    {
                        Read();
                        options.WithCookies = true;
                    }
                    else if (word.Kind == TokenKind.Identifier && Same(word.Text, "timeout"))
                    {
                        Read();
                        options.TimeoutSeconds = ReadValue();
                        Token unit = Current;
                        if (unit.Kind == TokenKind.Second || (unit.Kind == TokenKind.Identifier && SameAny(unit.Text, "second", "seconds"))) Read();
                        else throw Error("I expected \"seconds\" after the timeout value.", unit, "Write: with timeout 30 seconds", "ExpectedSeconds");
                    }
                    else
                    {
                        throw Error($"I don't recognize the HTTP option \"with {word.Text}\".", word, "Write: with header \"X\" is Y, with cookies, or with timeout N seconds.", "UnknownHttpOption");
                    }
                }
                else if (clause.Kind == TokenKind.Identifier && Same(clause.Text, "without"))
                {
                    Read();
                    Token word = Current;
                    if (word.Kind == TokenKind.Identifier && Same(word.Text, "cookies")) { Read(); options.WithCookies = false; }
                    else if (word.Kind == TokenKind.Identifier && Same(word.Text, "redirects")) { Read(); options.FollowRedirects = false; }
                    else throw Error($"I don't recognize the HTTP option \"without {word.Text}\".", word, "Write: without cookies, or without redirects.", "UnknownHttpOption");
                }
                else if (clause.Kind == TokenKind.Identifier && Same(clause.Text, "following"))
                {
                    Read();
                    Token word = Current;
                    if (word.Kind == TokenKind.Identifier && Same(word.Text, "redirects")) { Read(); options.FollowRedirects = true; }
                    else throw Error("I expected \"redirects\" after \"following\".", word, "Write: following redirects", "ExpectedRedirects");
                }
                else
                {
                    throw Error($"I don't recognize \"{clause.Text}\" as an HTTP option.", clause,
                        "Write: with header \"X\" is Y, with cookies, without cookies, following redirects, without redirects, or with timeout N seconds.", "UnknownHttpOption");
                }
                // otterPS passes the code as the suggestion here.
                Expect(TokenKind.Newline, "I expected the HTTP option to end here.", "ExpectedNewline");
            }
            catch (OtterError error)
            {
                Record(error);
                while (!Is(TokenKind.Newline) && !Is(TokenKind.Dedent) && !Is(TokenKind.EndOfFile)) Read();
                if (Is(TokenKind.Newline)) Read();
                if (position <= before && !Is(TokenKind.EndOfFile)) Read();
            }
        }
        if (Is(TokenKind.Dedent)) Read();
        return options;
    }

    // The end of a get/post/put/delete line: [into X] (already read by the
    // caller), the options block or the end of the line, and the end of a
    // continued clause.
    HttpOptions? ReadHttpEnd(bool continued, string verb)
    {
        HttpOptions? options = continued ? null : ReadHttpOptions();
        if (options == null) Expect(TokenKind.Newline, $"I expected the {verb} statement to end here.");
        if (continued) Expect(TokenKind.Dedent, $"I expected the continued {verb} clause to end.");
        return options;
    }

    string? ReadOptionalInto()
    {
        if (!Is(TokenKind.Into)) return null;
        Read();
        return ReadVariableName("I expected a result name after \"into\".").Text;
    }

    Stmt ReadPost(Token start)
    {
        Read();
        Expr data = ReadValue();
        bool asJson = false;
        if (Is(TokenKind.As))
        {
            Read();
            Expect(TokenKind.Json, "I expected \"json\" after \"as\".");
            asJson = true;
        }
        Expect(TokenKind.To, "I expected \"to\" and a URL after post data.");
        Expr url = ReadValue();
        bool continued = SoftContinuation();
        string? target = ReadOptionalInto();
        HttpOptions? options = ReadHttpEnd(continued, "post");
        return new HttpSendStmt("POST", data, url, target, asJson, options, start.Line);
    }

    // put DATA [as json] to URL - otherwise `put x in y` (the UI's).
    Stmt? TryReadHttpPut(Token start)
    {
        int saved = position;
        Read();
        ReadOptionalTheBeforeName();
        Expr first;
        try { first = ReadValue(); }
        catch (OtterError) { position = saved; return null; }
        bool asJson = Is(TokenKind.As);
        if (asJson)
        {
            Read();
            Expect(TokenKind.Json, "I expected \"json\" after \"as\".");
            Expect(TokenKind.To, "I expected \"to\" and a URL after put data.");
        }
        else if (Is(TokenKind.To)) Read();
        else { position = saved; return null; }
        Expr url = ReadValue();
        bool continued = SoftContinuation();
        string? target = ReadOptionalInto();
        HttpOptions? options = ReadHttpEnd(continued, "put");
        return new HttpSendStmt("PUT", first, url, target, asJson, options, start.Line);
    }

    Stmt ReadHttpDelete(Token start)
    {
        Read(); Read();
        Expr url = ReadValue();
        bool continued = SoftContinuation();
        string? target = ReadOptionalInto();
        HttpOptions? options = ReadHttpEnd(continued, "delete");
        return new HttpDeleteStmt(url, target, options, start.Line);
    }

    // The words that start otterPS's other `get` forms (clipboard,
    // environment, system folders, processes, the registry, databases ...).
    static readonly string[] OtherGetWords =
    {
        "clipboard", "environment", "current", "arguments", "system", "processes", "symbolic", "owner",
        "registry", "credential", "event", "tables", "columns",
    };

    // get URL [as json] into X / get json from URL into X. `get KEY from
    // THING into X` (D41), the other get forms and queries are not this area's.
    Stmt? TryReadHttpGet(Token start)
    {
        if (QueryStatementAhead()) return null;
        Token? kind = At(1);
        if (kind == null || kind.Kind is TokenKind.Newline or TokenKind.Indent or TokenKind.Files or TokenKind.Folders) return null;
        if (kind.Kind == TokenKind.Identifier && SameAny(kind.Text, OtherGetWords)) return null;
        if (kind.Kind == TokenKind.Json)
        {
            Read(); Read();
            Expect(TokenKind.From, "I expected \"from\" and a URL after \"get json\".");
            Expr jsonUrl = ReadValue();
            bool jsonContinued = SoftContinuation();
            Expect(TokenKind.Into, "I expected \"into\" and a result name.");
            Token jsonTarget = ReadVariableName("I expected a result name after \"into\".");
            HttpOptions? jsonOptions = ReadHttpEnd(jsonContinued, "get");
            return new HttpGetStmt(jsonUrl, jsonTarget.Text, true, jsonOptions, start.Line);
        }
        int saved = position;
        Read();
        Expr first;
        try { first = ReadValue(); }
        catch (OtterError) { position = saved; return null; }
        if (Is(TokenKind.From)) { position = saved; return null; }
        bool asJson = false;
        if (Is(TokenKind.As))
        {
            Read();
            Expect(TokenKind.Json, "I expected \"json\" after \"as\".");
            asJson = true;
        }
        bool continued = SoftContinuation();
        Expect(TokenKind.Into, "I expected \"from\" or \"into\" after the value.");
        Token result = ReadVariableName("I expected a result name after \"into\".");
        HttpOptions? options = ReadHttpEnd(continued, "get");
        return new HttpGetStmt(first, result.Text, asJson, options, start.Line);
    }

    // Test-OtterIsQueryStatement (D99): `get ... from TABLE in DB ...`.
    bool QueryStatementAhead()
    {
        int index = position + 1;
        if (index >= tokens.Count) return false;
        while (index < tokens.Count && tokens[index].Kind is TokenKind.Newline or TokenKind.Indent) index++;
        if (index >= tokens.Count) return false;
        Token first = tokens[index];
        if (first.Kind == TokenKind.Distinct) return true;
        if (first.Kind == TokenKind.Identifier && Same(first.Text, "all"))
        {
            int next = index + 1;
            while (next < tokens.Count && tokens[next].Kind is TokenKind.Newline or TokenKind.Indent) next++;
            if (next < tokens.Count && tokens[next].Kind == TokenKind.From) return true;
        }
        if (first.Kind is TokenKind.Files or TokenKind.Folders) return false;
        if (first.Kind == TokenKind.Identifier && SameAny(first.Text, "files", "folders", "clipboard", "environment", "current", "arguments",
                "system", "processes", "symbolic", "owner", "registry", "credential", "event", "tables", "columns")) return false;
        bool sawFrom = false;
        int limit = Math.Min(tokens.Count, index + 80);
        int indentDepth = 0;
        for (int at = index; at < limit; at++)
        {
            TokenKind kind = tokens[at].Kind;
            if (kind == TokenKind.EndOfFile) break;
            if (kind == TokenKind.Indent) indentDepth++;
            else if (kind == TokenKind.Dedent)
            {
                indentDepth--;
                if (indentDepth <= 0) break;
            }
            else if (kind == TokenKind.Newline)
            {
                if (indentDepth == 0)
                {
                    int next = at + 1;
                    while (next < limit && tokens[next].Kind == TokenKind.Newline) next++;
                    if (next >= limit || tokens[next].Kind != TokenKind.Indent) break;
                }
            }
            else if (kind == TokenKind.From) sawFrom = true;
            else if (sawFrom && kind == TokenKind.In) return true;
        }
        return false;
    }

    Stmt ReadHttpStart(Token start)
    {
        Read();
        Token verb = Read();
        string method = verb.Text.ToLowerInvariant() switch
        {
            "get" => "GET",
            "post" => "POST",
            "put" => "PUT",
            "delete" => "DELETE",
            // A switch with no matching case leaves otterPS's $method empty.
            _ => "",
        };
        Expr? data = null;
        bool asJson = false;
        Expr url;
        if (method == "GET")
        {
            Expect(TokenKind.From, "I expected \"from\" and a URL after \"start get\".");
            url = ReadValue();
            if (Is(TokenKind.As))
            {
                Read();
                Expect(TokenKind.Json, "I expected \"json\" after \"as\".");
                asJson = true;
            }
        }
        else if (method == "DELETE")
        {
            Expect(TokenKind.From, "I expected \"from\" and a URL after \"start delete\".");
            url = ReadValue();
        }
        else
        {
            data = ReadValue();
            if (Is(TokenKind.As))
            {
                Read();
                Expect(TokenKind.Json, "I expected \"json\" after \"as\".");
                asJson = true;
            }
            Expect(TokenKind.To, $"I expected \"to\" and a URL after {method} data.");
            url = ReadValue();
        }
        Expect(TokenKind.And, "I expected \"and call it\" and a variable name.");
        Expect(TokenKind.Call, "I expected \"call\" after \"and\".");
        Expect(TokenKind.It, "I expected \"it\" after \"call\".");
        Token target = ReadVariableName("I expected a variable name after \"call it\".");
        HttpOptions? options = ReadHttpOptions();
        if (options == null) Expect(TokenKind.Newline, "I expected the start statement to end here.");
        return new HttpStartStmt(method, url, data, asJson, target.Text, options, start.Line);
    }

    Stmt ReadDownload(Token start)
    {
        Read();
        const string suggestion = "Write: download file from <url> to <path>";
        Expect(TokenKind.File, "I expected \"file\" after \"download\".", suggestion);
        Expect(TokenKind.From, "I expected \"from\" after \"file\".", suggestion);
        Expr url = ReadMathExpression();
        Expect(TokenKind.To, "I expected \"to\" and a destination path.", suggestion);
        Expr path = ReadMathExpression();
        Expect(TokenKind.Newline, "I expected the download statement to end here.");
        return new DownloadFileStmt(url, path, start.Line);
    }

    // connect to websocket ... / connect [securely] to tcp ... (D106, D107, D112)
    Stmt ReadNetworkConnect(Token start)
    {
        Read();
        bool secure = false;
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "securely"))
        {
            Read();
            secure = true;
            if (!Is(TokenKind.To))
            {
                throw Error("I expected \"to tcp\" after \"connect securely\".", Current, "Write: connect securely to tcp \"example.com\" on port 443 and call it connection");
            }
        }
        Read(); // to
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "tcp"))
        {
            Read();
            Expr host = ReadValue();
            string verbPrefix = secure ? "connect securely to tcp" : "connect to tcp";
            NetClauses clauses = ReadNetClauses($"I expected \"on port\" and \"and call it\" in the {verbPrefix} statement.");
            if (clauses.Port == null) throw Error($"I expected \"on port <number>\" in \"{verbPrefix}\".", start, $"Write: {verbPrefix} \"localhost\" on port 9000 and call it connection");
            if (clauses.Target == null) throw Error($"I expected \"and call it\" and a name in \"{verbPrefix}\".", start, $"Write: {verbPrefix} \"localhost\" on port 9000 and call it connection");
            return new TcpConnectStmt(host, clauses.Port, clauses.Target, secure, clauses.ServerName, clauses.Protocols, start.Line);
        }
        if (secure) throw Error("I expected \"tcp\" after \"connect securely to\".", Current, "Write: connect securely to tcp \"example.com\" on port 443 and call it connection");
        if (!(Current.Kind == TokenKind.Identifier && Same(Current.Text, "websocket")))
        {
            throw Error("I expected \"websocket\" or \"tcp\" after \"connect to\".", Current, "Write: connect to websocket \"wss://example.com\" and call it chat");
        }
        Read();
        Expr url = ReadValue();
        Expr? protocol = null;
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "using"))
        {
            Read();
            if (!(Current.Kind == TokenKind.Identifier && Same(Current.Text, "protocol")))
            {
                throw Error("I expected \"protocol\" after \"using\".", Current, "Write: connect to websocket \"...\" using protocol \"chat\" and call it chat");
            }
            Read();
            protocol = ReadValue();
        }
        Expect(TokenKind.And, "I expected \"and call it\" and a name.", "Write: connect to websocket \"...\" and call it chat");
        Expect(TokenKind.Call, "I expected \"call\" after \"and\".");
        Expect(TokenKind.It, "I expected \"it\" after \"call\".");
        Token target = ReadVariableName("I expected a name after \"call it\".");
        Expect(TokenKind.Newline, "I expected the connect statement to end here.");
        return new WebSocketConnectStmt(url, protocol, target.Text, start.Line);
    }

    // ---- values ----

    // A value this area starts at `token`, or null.
    Expr? TryReadNetworkValue(Token token)
    {
        if (token.Kind == TokenKind.Await) return ReadAwaitValue(token);   // Parser.Ui.cs
        if (token.Kind != TokenKind.Identifier || At(1) is not { } next) return null;
        string word = token.Text;
        // D104: changed file name / changed path / change kind / old path
        if (Same(word, "changed") && next.Kind == TokenKind.File && IsIdentifierWordAt(2, "name")) { Read(); Read(); Read(); return new ChangedFileNameExpr(token.Line); }
        if (Same(word, "changed") && next.Kind == TokenKind.Identifier && Same(next.Text, "path")) { Read(); Read(); return new ChangedPathExpr(token.Line); }
        if (Same(word, "change") && next.Kind == TokenKind.Identifier && Same(next.Text, "kind")) { Read(); Read(); return new ChangeKindExpr(token.Line); }
        if (Same(word, "old") && next.Kind == TokenKind.Identifier && Same(next.Text, "path")) { Read(); Read(); return new OldPathExpr(token.Line); }
        // D106: received message; D116B: received response
        if (Same(word, "received") && next.Kind == TokenKind.Identifier && Same(next.Text, "message")) { Read(); Read(); return new ReceivedMessageExpr(token.Line); }
        if (Same(word, "received") && next.Kind == TokenKind.Identifier && Same(next.Text, "response")) { Read(); Read(); return new ReceivedResponseExpr(token.Line); }
        // D119-R2: received output / received error output
        if (Same(word, "received") && next.Kind == TokenKind.Identifier && Same(next.Text, "output")) { Read(); Read(); return new JobContextExpr("output", token.Line); }
        if (Same(word, "received") && At(2) != null && (next.Kind == TokenKind.Problem || Same(next.Text, "error")) && IsIdentifierWordAt(2, "output"))
        {
            Read(); Read(); Read();
            return new JobContextExpr("error output", token.Line);
        }
        // D106: close was clean / close code / close reason / websocket error
        if (Same(word, "close") && At(2) != null && next.Kind == TokenKind.Identifier && Same(next.Text, "was") && IsIdentifierWordAt(2, "clean"))
        {
            Read(); Read(); Read();
            return new CloseWasCleanExpr(token.Line);
        }
        if (Same(word, "close") && next.Kind == TokenKind.Identifier && Same(next.Text, "code")) { Read(); Read(); return new CloseCodeExpr(token.Line); }
        if (Same(word, "close") && next.Kind == TokenKind.Identifier && Same(next.Text, "reason")) { Read(); Read(); return new CloseReasonExpr(token.Line); }
        if (Same(word, "websocket") && (next.Kind == TokenKind.Problem || Same(next.Text, "error"))) { Read(); Read(); return new WebSocketErrorExpr(token.Line); }
        // `secret`, the cryptography words and drag/drop come next in
        // otterPS; none of them starts with one of the words below.
        // D107/D108/D113: received data / sender address / sender port /
        // network error / incoming connection
        string? field = null;
        if (Same(word, "received") && Same(next.Text, "data")) field = "data";
        else if (Same(word, "sender") && Same(next.Text, "address")) field = "sender address";
        else if (Same(word, "sender") && Same(next.Text, "port")) field = "sender port";
        else if (Same(word, "network") && Same(next.Text, "error")) field = "network error";
        else if (Same(word, "incoming") && Same(next.Text, "connection")) field = "incoming connection";
        if (field != null)
        {
            Read(); Read();
            return new NetContextExpr(field, token.Line);
        }
        // remote address|port of X (D107), tls version|protocol of X (D112),
        // local address|port of X (D113)
        if (((Same(word, "remote") || Same(word, "local")) && SameAny(next.Text, "address", "port") ||
             Same(word, "tls") && SameAny(next.Text, "version", "protocol")) && IsAt(2, TokenKind.Of))
        {
            Read(); Read(); Read();
            // otterPS reads the owner with -PropertyTarget, which only differs
            // for a leading `the`; that is left to otterPS.
            if (Same(Current.Text, "the") && At(1) is { } afterThe && IsIdentifier(afterThe)) throw Unsupported("'the' after a property");
            return new PropertyAccessExpr($"{word.ToLowerInvariant()} {next.Text}", ReadValue(), token.Line);
        }
        return null;
    }

    // ---- conditions ----

    // A condition this area starts at `head`, or null.
    Expr? TryReadNetworkCondition(Token head) => null;

    // The state checks otterPS reads after a condition's left side (D104,
    // D106, D112, D113, D116B), in its order; null when none applies, so the
    // line goes on as an ordinary comparison.
    Expr? ReadNetworkStatePredicate(Expr left)
    {
        if (At(1) is not { } word) return null;
        if (Is(TokenKind.Is))
        {
            // D104: watcher is watching
            if (word.Kind == TokenKind.Identifier && Same(word.Text, "watching"))
            {
                Token isWord = Read(); Read();
                return new IsWatchingExpr(left, isWord.Line);
            }
            // D106: socket is connecting / open / closing / connected / closed
            WebSocketConnState? connState =
                Same(word.Text, "connecting") ? WebSocketConnState.Connecting
                : word.Kind == TokenKind.Open || Same(word.Text, "open") ? WebSocketConnState.Open
                : Same(word.Text, "closing") ? WebSocketConnState.Closing
                : Same(word.Text, "connected") ? WebSocketConnState.Connected
                : Same(word.Text, "closed") ? WebSocketConnState.Closed
                : null;
            if (connState != null)
            {
                Token isWord = Read(); Read();
                return new WebSocketIsStateExpr(left, connState.Value, isWord.Line);
            }
            // D112: connection is secure
            if (word.Kind == TokenKind.Identifier && Same(word.Text, "secure"))
            {
                Token isWord = Read(); Read();
                return new ConnectionIsSecureExpr(left, isWord.Line);
            }
            // D113: server is listening / stopped
            if (word.Kind == TokenKind.Identifier && SameAny(word.Text, "listening", "stopped"))
            {
                Token isWord = Read(); Read();
                return new TcpServerIsStateExpr(left, Same(word.Text, "listening") ? TcpServerState.Listening : TcpServerState.Stopped, isWord.Line);
            }
        }
        // D116B: request is (not) pending / running / completed / failed / cancelled
        if (Current.Kind is TokenKind.Is or TokenKind.IsNot && word.Kind == TokenKind.Identifier)
        {
            HttpRequestState? state =
                SameAny(word.Text, "pending", "running") ? HttpRequestState.Pending
                : Same(word.Text, "completed") ? HttpRequestState.Completed
                : Same(word.Text, "failed") ? HttpRequestState.Failed
                : Same(word.Text, "cancelled") ? HttpRequestState.Cancelled
                : null;
            if (state != null)
            {
                Token isWord = Read(); Read();
                var check = new HttpRequestIsStateExpr(left, state.Value, isWord.Line);
                return isWord.Kind == TokenKind.IsNot ? new NotExpr(check, isWord.Line) : check;
            }
        }
        return null;
    }
}
