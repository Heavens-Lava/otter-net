namespace Otter.Language;

// The parser for user interfaces: web and desktop apps, state, events,
// animation, routing (otterPS src/Otter.Parser.psm1: Read-OtterStatement's UI
// cases, Read-OtterUiElementStatement, Read-OtterAnimationBlock and the UI
// forms of Read-OtterValue).
//
// otterPS reads these forms the same way for every command (`otter web`,
// `run`, `check`); what each one does when run is Interpreter.Ui.cs's.
public sealed partial class Parser
{
    // A `create KIND into x` was read: running it needs WPF.
    bool createsUiResources;

    static readonly string[] UiTags =
    {
        "window", "page", "card", "heading", "text", "button", "panel", "section", "sidebar", "main", "link", "image", "input", "grid",
    };

    static readonly string[] UiVariantTags = { "button", "card", "heading", "text", "panel", "link", "image", "input" };

    static bool IsAssignmentFollower(TokenKind kind) => kind is TokenKind.Is or TokenKind.Are or TokenKind.Of or TokenKind.IsNot;

    // A statement this area starts at `start`, or null (the token is left unread).
    Stmt? TryReadUiStatement(Token start)
    {
        TokenKind nextKind = At(1)?.Kind ?? TokenKind.EndOfFile;

        bool isVariant = SameAny(start.Text, "primary", "secondary", "danger") && At(1) is { } variantNext &&
                         UiVariantTags.Contains(variantNext.Text.ToLowerInvariant());
        bool isUiTag = UiTags.Contains(start.Text.ToLowerInvariant()) &&
                       nextKind is not (TokenKind.Is or TokenKind.Are or TokenKind.Of or TokenKind.Has or TokenKind.Make or TokenKind.Into or TokenKind.IsNot);
        if (isVariant || isUiTag) return ReadUiElementStatement();

        // D103: route "/path" shows page / route otherwise shows page
        if (Same(start.Text, "route") && !IsAssignmentFollower(nextKind)) return ReadRouteStatement(start);
        // D103: go to "/path" / go back / go forward
        if (Same(start.Text, "go") && !IsAssignmentFollower(nextKind) &&
            (nextKind == TokenKind.To || (At(1) is { Kind: TokenKind.Identifier } direction && SameAny(direction.Text, "back", "forward"))))
        {
            Read();
            Token way = Current;
            if (way.Kind == TokenKind.Identifier && SameAny(way.Text, "back", "forward"))
            {
                Read();
                Expect(TokenKind.Newline, "I expected the go statement to end here.");
                return new GoNavigateStmt(Same(way.Text, "forward"), start.Line);
            }
            Expect(TokenKind.To, "I expected \"to\" and a route after \"go\".", "Write: go to \"/about\"");
            Expr path = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected the go statement to end here.");
            return new GoToRouteStmt(path, start.Line);
        }
        // D110: set drag data to cardId
        if (Same(start.Text, "set") && At(2) is { } dataWord && Same(At(1)!.Text, "drag") && Same(dataWord.Text, "data"))
        {
            Read(); Read(); Read();
            Expect(TokenKind.To, "I expected \"to\" and the data to attach.", "set drag data to cardId");
            Expr value = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected the set drag data statement to end here.");
            return new SetDragDataStmt(value, start.Line);
        }

        switch (start.Kind)
        {
            case TokenKind.Await:
                return ReadAwaitStatement(start);
            case TokenKind.State:
            {
                Read();
                Token name = ReadVariableName("I expected a variable name after \"state\".");
                Expect(TokenKind.Is, "I expected \"is\" after the state variable name.");
                Expr value = ReadMathExpression();
                Expect(TokenKind.Newline, "I expected the state definition to end here.");
                return new StateDefStmt(name.Text, value, start.Line);
            }
            case TokenKind.Derive:
            {
                Read();
                Token name = ReadVariableName("I expected a variable name after \"derive\".");
                Expect(TokenKind.Is, "I expected \"is\" after the derived variable name.");
                Expr expression = ReadMathExpression();
                Expect(TokenKind.Newline, "I expected the derive statement to end here.");
                return new DeriveDefStmt(name.Text, expression, start.Line);
            }
            case TokenKind.Memo:
            {
                Read();
                Token name = ReadVariableName("I expected a memo name after \"memo\".");
                return new MemoDefStmt(name.Text, ReadBlock(), start.Line);
            }
            case TokenKind.Shared:
            {
                Read();
                Token name = ReadVariableName("I expected a variable name after \"shared\".");
                Expect(TokenKind.Is, "I expected \"is\" after the shared variable name.");
                Expr value = ReadMathExpression();
                Expect(TokenKind.Newline, "I expected the shared statement to end here.");
                return new SharedStateStmt(name.Text, value, start.Line);
            }
            case TokenKind.Use:
            {
                Read();
                Token module = Read();
                Expect(TokenKind.Newline, "I expected the use statement to end here.");
                return new UseModuleStmt(module.Text, start.Line);
            }
            case TokenKind.Focus:
            case TokenKind.Hide:
            {
                Read();
                Expr target = ReadValue();
                Expect(TokenKind.Newline, start.Kind == TokenKind.Focus ? "I expected the focus statement to end here." : "I expected the hide statement to end here.");
                return new UiActionStmt(start.Kind == TokenKind.Focus ? "focus" : "hide", target, start.Line);
            }
            case TokenKind.Layout:
            {
                Read();
                Token modeToken = Read();
                string mode = modeToken.Text.ToLowerInvariant();
                Expr? columns = null;
                if (mode == "grid" && !Is(TokenKind.Newline)) columns = ReadMathExpression();
                Expect(TokenKind.Newline, "I expected the layout statement to end here.");
                var spec = new UiLayoutSpec { Mode = mode, Columns = columns };
                return new UiElementStmt("layout", null, null, null, spec, Array.Empty<Stmt>(), Array.Empty<Stmt>(), Array.Empty<Stmt>(), Array.Empty<Stmt>(), start.Line);
            }
            case TokenKind.On:
                return TryReadUiOnStatement(start);
            case TokenKind.When:
                return TryReadWhenStatement(start);
            case TokenKind.Put:
                return TryReadPutInStatement(start);
            case TokenKind.Show:
            {
                if (At(1) is { Kind: TokenKind.Identifier } progressWord && Same(progressWord.Text, "progress")) return null;
                Read();
                ReadOptionalTheBeforeName();
                Token targetToken = ReadVariableName("I expected a resource name after \"show\".");
                Expect(TokenKind.Newline, "I expected the show statement to end here.");
                return new ShowStmt(new VariableExpr(targetToken.Text, targetToken.Line), start.Line);
            }
            case TokenKind.Create:
            {
                if (IsAssignmentFollower(nextKind) || nextKind == TokenKind.Folder) return null;
                if (At(1) is { Kind: TokenKind.Identifier } symbolic && Same(symbolic.Text, "symbolic")) return null;
                Read();
                // `the` is an article here only when another kind word follows.
                if (Same(Current.Text, "the") && At(1) is { } afterThe && IsIdentifier(afterThe)) Read();
                string typeName = ReadUiResourceTypeName();
                Expect(TokenKind.Into, "I expected \"into\" and a resource variable name.");
                ReadOptionalTheBeforeName();
                Token target = ReadVariableName("I expected a variable name after \"into\".");
                Expect(TokenKind.Newline, "I expected the create statement to end here.");
                createsUiResources = true;
                return new CreateUiResourceStmt(typeName, target.Text, start.Line);
            }
            case TokenKind.Replace when !IsAssignmentFollower(nextKind):
                return TryReadReplaceRouteStatement(start);
        }
        return null;
    }

    // await X [make|into y] (Read-OtterStatement's Await case)
    Stmt ReadAwaitStatement(Token start)
    {
        Read();
        Token next = Current;
        Expr expression;
        if (next.Kind == TokenKind.Get || Same(next.Text, "get"))
        {
            Read();
            expression = new CallExpr("get", new[] { ReadValue() }, next.Line);
        }
        else if (IsIdentifier(next))
        {
            Read();
            var arguments = new List<Expr>();
            while (!Is(TokenKind.Newline) && !Is(TokenKind.Make) && !Is(TokenKind.Into))
            {
                if (Is(TokenKind.And)) { Read(); continue; }
                arguments.Add(ReadValue());
            }
            expression = arguments.Count > 0 || knownFunctions.ContainsKey(next.Text)
                ? new CallExpr(next.Text, arguments, next.Line)
                : new VariableExpr(next.Text, next.Line);
        }
        else
        {
            expression = ReadValue();
        }
        if (Is(TokenKind.Make) || Is(TokenKind.Into))
        {
            bool make = Is(TokenKind.Make);
            Read();
            Token target = ReadVariableName(make ? "I expected a variable name after \"make\"." : "I expected a variable name after \"into\".");
            Expect(TokenKind.Newline, "I expected the await statement to end here.");
            return new AssignStmt(new VariableExpr(target.Text, start.Line), new AwaitExpr(expression, start.Line), start.Line);
        }
        Expect(TokenKind.Newline, "I expected the await statement to end here.");
        return new AwaitStmt(expression, start.Line);
    }

    // await X as a value (Read-OtterValue)
    Expr ReadAwaitValue(Token token)
    {
        Read();
        Token next = Current;
        if (next.Kind == TokenKind.Get || Same(next.Text, "get"))
        {
            Read();
            return new AwaitExpr(new CallExpr("get", new[] { ReadValue() }, next.Line), token.Line);
        }
        if (IsIdentifier(next))
        {
            Read();
            var arguments = new List<Expr>();
            while (!Is(TokenKind.Newline) && !Is(TokenKind.Make) && !Is(TokenKind.Into) && !Is(TokenKind.And) &&
                   !Is(TokenKind.Minus) && !Is(TokenKind.Times) && !Is(TokenKind.DividedBy))
            {
                if (Current.Kind is TokenKind.Number or TokenKind.String or TokenKind.True or TokenKind.False or TokenKind.Identifier) arguments.Add(ReadValue());
                else break;
            }
            if (arguments.Count > 0 || knownFunctions.ContainsKey(next.Text))
                return new AwaitExpr(new CallExpr(next.Text, arguments, next.Line), token.Line);
            return new AwaitExpr(new VariableExpr(next.Text, next.Line), token.Line);
        }
        return new AwaitExpr(ReadValue(), token.Line);
    }

    // route "/path" shows page / route otherwise shows page (D103)
    Stmt ReadRouteStatement(Token start)
    {
        Read();
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "otherwise"))
        {
            Read();
            if (!Is(TokenKind.Show) && !Same(Current.Text, "shows"))
            {
                throw Error("I expected \"shows\" and a page after \"route otherwise\".", Current, "Write: route otherwise shows notFoundPage");
            }
            Read();
            Token otherwisePage = ReadVariableName("I expected a page name after \"shows\".");
            Expect(TokenKind.Newline, "I expected the route statement to end here.");
            return new RouteStmt(null, true, otherwisePage.Text, start.Line);
        }
        Expr path = ReadMathExpression();
        if (!Is(TokenKind.Show) && !Same(Current.Text, "shows"))
        {
            throw Error("I expected \"shows\" and a page after the route path.", Current, "Write: route \"/about\" shows aboutPage");
        }
        Read();
        Token page = ReadVariableName("I expected a page name after \"shows\".");
        Expect(TokenKind.Newline, "I expected the route statement to end here.");
        return new RouteStmt(path, false, page.Text, start.Line);
    }

    // on route change / on drag of x / on drop on x / on files dropped on x /
    // on start / on close. Every other `on` form belongs to another area.
    Stmt? TryReadUiOnStatement(Token start)
    {
        Token stage = At(1) ?? start;
        Token? second = At(2);
        Token? third = At(3);
        if (stage.Kind == TokenKind.Identifier && Same(stage.Text, "route") && second is { Kind: TokenKind.Identifier } && Same(second.Text, "change"))
        {
            Read(); Read(); Read();
            return new RouteChangeStmt(ReadBlock(), start.Line);
        }
        // The forms otterPS checks before drag/drop: watchers and `open of`.
        if (stage.Kind == TokenKind.Identifier && Same(stage.Text, "change") && second?.Kind == TokenKind.Of) return null;
        if (stage.Kind == TokenKind.Identifier && SameAny(stage.Text, "create", "delete", "rename") && second?.Kind == TokenKind.In) return null;
        if ((stage.Kind == TokenKind.Open || Same(stage.Text, "open")) && second?.Kind == TokenKind.Of) return null;
        if (Same(stage.Text, "drag") && second?.Kind == TokenKind.Of)
        {
            Read(); Read(); Read();
            Expr target = ReadValue();
            return new WhenStmt(target, "drag", ReadBlock(), start.Line);
        }
        if (Same(stage.Text, "drop") && second != null && Same(second.Text, "on"))
        {
            Read(); Read(); Read();
            Expr target = ReadValue();
            return new WhenStmt(target, "drop", ReadBlock(), start.Line);
        }
        if (Same(stage.Text, "files") && second != null && third != null && Same(second.Text, "dropped") && Same(third.Text, "on"))
        {
            Read(); Read(); Read(); Read();
            Expr target = ReadValue();
            return new WhenStmt(target, "files dropped", ReadBlock(), start.Line);
        }
        // on start / on close (not `on close of`, a network event).
        if (Same(stage.Text, "start") || (Same(stage.Text, "close") && second?.Kind != TokenKind.Of))
        {
            // The network and job forms otterPS checks before this one.
            Read();
            Token stageToken = Read();
            return new LifecycleStmt(stageToken.Text, ReadBlock(), start.Line);
        }
        return null;
    }

    // when helloButton is clicked / when count changes. `when x receives`
    // (a web server route) belongs to the network area.
    Stmt? TryReadWhenStatement(Token start)
    {
        int saved = position;
        Read();
        if (Same(Current.Text, "the") && At(1) is { } afterThe && IsIdentifier(afterThe))
        {
            throw Error("Event targets do not use \"the\" here.", Current, "Write `when helloButton is clicked` without \"the\" before the resource name.");
        }
        Token targetToken = ReadVariableName("I expected a resource name after \"when\".");
        if (Is(TokenKind.Receives))
        {
            position = saved;
            return null;
        }
        var target = new VariableExpr(targetToken.Text, targetToken.Line);
        if (Is(TokenKind.Is)) Read();
        Token eventToken = Current;
        if (eventToken.Kind is TokenKind.Newline or TokenKind.EndOfFile)
        {
            throw Error("I expected an event name.", eventToken, "Write an event such as \"clicked\" or \"changed\".");
        }
        Read();
        if (Same(eventToken.Text, "changes")) return new WatchStmt(target.Name, ReadBlock(), start.Line);
        return new WhenStmt(target, eventToken.Text, ReadBlock(), start.Line);
    }

    // put a, b in app (D47) - one PutIn per item. `put data to url` and
    // `put data as json to url` are HTTP (the network area).
    Stmt? TryReadPutInStatement(Token start)
    {
        int saved = position;
        Read();
        ReadOptionalTheBeforeName();
        Expr first = ReadValue();
        if (Is(TokenKind.As) || Is(TokenKind.To))
        {
            position = saved;
            return null;
        }
        var items = new List<Expr> { first };
        while (Current.Text == ",")
        {
            Read();
            Token next = Current;
            if (next.Text == "," || next.Kind is TokenKind.In or TokenKind.Newline)
            {
                throw Error("I expected a resource name after the comma.", next, "Write another resource name after each comma.");
            }
            ReadOptionalTheBeforeName();
            items.Add(ReadValue());
        }
        Expect(TokenKind.In, "I expected \"in\" before the container.");
        ReadOptionalTheBeforeName();
        Token containerToken = ReadVariableName("I expected a resource name after \"in\".");
        var container = new VariableExpr(containerToken.Text, containerToken.Line);
        Expect(TokenKind.Newline, "I expected the put statement to end here.");
        var puts = items.Select(item => (Stmt)new PutInStmt(item, container, start.Line)).ToList();
        return puts.Count == 1 ? puts[0] : new StatementGroup(puts, start.Line);
    }

    // replace route with "/login" (D103). Any other `replace` is the text
    // statement, read by the main parser from the start again.
    Stmt? TryReadReplaceRouteStatement(Token start)
    {
        int saved = position;
        try
        {
            Read();
            Expr find = ReadValue();
            if (!Is(TokenKind.With)) { position = saved; return null; }
            Read();
            Expr replacement = ReadMathExpression();
            if (find is VariableExpr { Name: var findName } && Same(findName, "route") && !Is(TokenKind.In))
            {
                Expect(TokenKind.Newline, "I expected the replace route statement to end here.");
                return new ReplaceRouteStmt(replacement, start.Line);
            }
        }
        catch (OtterError) { }
        position = saved;
        return null;
    }

    // ReadOptionalTheBeforeName (otterPS's Read-OtterOptionalTheBeforeName) is in Parser.System.cs.

    string ReadUiResourceTypeName()
    {
        var words = new List<string>();
        while (!Is(TokenKind.Into))
        {
            Token token = Current;
            if (token.Kind is TokenKind.Newline or TokenKind.EndOfFile)
            {
                throw Error("I expected \"into\" after the resource type.", token, "Write a resource type followed by \"into\" and a variable name.");
            }
            words.Add(Read().Text);
        }
        if (words.Count == 0)
        {
            throw Error("I expected a resource type after \"create\".", Current, "Write a resource type, such as \"button\" or \"text box\".");
        }
        return string.Join(" ", words);
    }

    // Read-OtterUiElementStatement (D56)
    Stmt ReadUiElementStatement()
    {
        Token start = Current;
        string? variant = null;
        if (SameAny(start.Text, "primary", "secondary", "danger")) variant = Read().Text;
        Token tagToken = Read();
        string tag = tagToken.Text.ToLowerInvariant();

        Expr? label = null;
        string? name = null;
        if (tag == "input" && !Is(TokenKind.Newline) && IsIdentifier(Current)) name = Read().Text;
        else if (!Is(TokenKind.Newline)) label = ReadMathExpression();

        var layout = new UiLayoutSpec();
        var properties = new List<Stmt>();
        var events = new List<Stmt>();
        var animations = new List<Stmt>();
        var children = new List<Stmt>();

        Expect(TokenKind.Newline, "I expected the UI element header to end here.");
        if (Is(TokenKind.Indent))
        {
            Read();
            SkipNewlines();
            while (!Is(TokenKind.Dedent) && !Is(TokenKind.EndOfFile) && !Is(TokenKind.BlockEnd))
            {
                Token current = Current;
                if (current.Kind == TokenKind.Layout || Same(current.Text, "layout"))
                {
                    Read();
                    layout.Mode = Read().Text.ToLowerInvariant();
                    if (layout.Mode == "grid" && !Is(TokenKind.Newline)) layout.Columns = ReadMathExpression();
                    Expect(TokenKind.Newline, "I expected the layout statement to end here.");
                    SkipNewlines();
                    continue;
                }
                if (Same(current.Text, "align"))
                {
                    Read();
                    layout.Align = Read().Text.ToLowerInvariant();
                    Expect(TokenKind.Newline, "I expected the align statement to end here.");
                    SkipNewlines();
                    continue;
                }
                if (current.Kind == TokenKind.Gap || Same(current.Text, "gap"))
                {
                    Read();
                    layout.Gap = ReadMathExpression();
                    Expect(TokenKind.Newline, "I expected the gap statement to end here.");
                    SkipNewlines();
                    continue;
                }
                if (Same(current.Text, "spread"))
                {
                    Read();
                    layout.Spread = true;
                    Expect(TokenKind.Newline, "I expected the spread statement to end here.");
                    SkipNewlines();
                    continue;
                }
                if (Same(current.Text, "columns") && position + 3 < tokens.Count && Same(tokens[position + 2].Text, "on"))
                {
                    Read();
                    object? countValue = Read().Value;
                    Read(); // on
                    string breakpoint = Read().Text;
                    // otterPS casts with [int] only now, after `on` and the
                    // breakpoint; a value [int] refuses is PowerShell's
                    // conversion error, which Read-OtterStatements reports as
                    // a syntax error at the current token.
                    if (!PowerShellInt32.TryCast(countValue, out int count, out string castError))
                    {
                        throw Error($"Syntax error: {castError}", Current, null);
                    }
                    layout.Responsive.Add(new ResponsiveRule(breakpoint, count, false));
                    Expect(TokenKind.Newline, "I expected the responsive rule to end here.");
                    SkipNewlines();
                    continue;
                }
                if (Same(current.Text, "stack") && position + 2 < tokens.Count && Same(tokens[position + 1].Text, "on"))
                {
                    Read();
                    Read(); // on
                    string breakpoint = Read().Text;
                    layout.Responsive.Add(new ResponsiveRule(breakpoint, 1, true));
                    Expect(TokenKind.Newline, "I expected the responsive rule to end here.");
                    SkipNewlines();
                    continue;
                }
                if (SameAny(current.Text, "round", "background", "placeholder", "padding", "margin", "width", "height"))
                {
                    Token property = Read();
                    Expr value = ReadMathExpression();
                    properties.Add(new AssignStmt(new VariableExpr(property.Text, property.Line), value, property.Line));
                    Expect(TokenKind.Newline, "I expected the property statement to end here.");
                    SkipNewlines();
                    continue;
                }
                if (SameAny(current.Text, "enter", "leave", "hover", "press") && IsAt(1, TokenKind.Newline) && IsAt(2, TokenKind.Indent))
                {
                    Token trigger = Read();
                    animations.Add(ReadAnimationBlock(trigger.Text, trigger.Line));
                    SkipNewlines();
                    continue;
                }
                if (SameAny(current.Text, "click", "change", "input", "submit", "hover", "press", "focus", "blur") &&
                    IsAt(1, TokenKind.Newline) && IsAt(2, TokenKind.Indent))
                {
                    Token eventToken = Read();
                    events.Add(new UiEventStmt(eventToken.Text, ReadBlock(), eventToken.Line));
                    SkipNewlines();
                    continue;
                }
                Stmt child = ReadStatement();
                if (child is StatementGroup group) children.AddRange(group.Statements);
                else children.Add(child);
                SkipNewlines();
            }
            Expect(TokenKind.Dedent, "I expected the UI element block to end.");
            if (Is(TokenKind.BlockEnd)) { Read(); SkipNewlines(); }
        }
        return new UiElementStmt(tag, variant, label, name, layout, properties, events, animations, children, start.Line);
    }

    // Read-OtterAnimationBlock
    UiAnimationBlock ReadAnimationBlock(string trigger, int line)
    {
        Expect(TokenKind.Newline, "I expected the animation header to end here.");
        Expect(TokenKind.Indent, "I expected an indented animation block.");
        var steps = new List<UiAnimationStep>();
        double durationMs = 300.0;
        string easing = "ease";
        SkipNewlines();
        while (!Is(TokenKind.Dedent) && !Is(TokenKind.EndOfFile) && !Is(TokenKind.BlockEnd))
        {
            Token current = Current;
            if (current.Kind == TokenKind.Animate || Same(current.Text, "animate"))
            {
                Read();
                Token duration = Current;
                if (duration.Kind != TokenKind.Number)
                {
                    throw Error($"Otter expected a duration (e.g. '300ms') and optional easing ('ease', 'ease-out', 'ease-in', 'spring') after 'animate', but got '{duration.Text}'.",
                        duration, "Write 'animate 300ms ease-out' or similar.");
                }
                double amount = Convert.ToDouble(Read().Value, System.Globalization.CultureInfo.InvariantCulture);
                if (Same(Current.Text, "ms")) { Read(); durationMs = amount; }
                else if (Same(Current.Text, "s")) { Read(); durationMs = amount * 1000.0; }
                else durationMs = amount;
                if (!Is(TokenKind.Newline)) easing = Read().Text.ToLowerInvariant();
                Expect(TokenKind.Newline, "I expected the animate statement to end here.");
                SkipNewlines();
                continue;
            }
            string operation = Read().Text.ToLowerInvariant();
            string? direction = null;
            Expr? stepAmount = null;
            if (operation == "fade")
            {
                if (!Is(TokenKind.Newline)) direction = Read().Text.ToLowerInvariant();
            }
            else if (operation == "move")
            {
                if (!Is(TokenKind.Newline)) direction = Read().Text.ToLowerInvariant();
                if (!Is(TokenKind.Newline)) stepAmount = ReadMathExpression();
            }
            else if (operation == "slide")
            {
                var parts = new List<string>();
                while (!Is(TokenKind.Newline)) parts.Add(Read().Text.ToLowerInvariant());
                direction = string.Join(" ", parts);
            }
            else if (operation is "grow" or "shrink" or "scale" or "rotate")
            {
                if (!Is(TokenKind.Newline)) stepAmount = ReadMathExpression();
            }
            steps.Add(new UiAnimationStep(operation, direction, stepAmount));
            Expect(TokenKind.Newline, "I expected the animation step to end here.");
            SkipNewlines();
        }
        Expect(TokenKind.Dedent, "I expected the animation block to end.");
        if (Is(TokenKind.BlockEnd)) { Read(); SkipNewlines(); }
        return new UiAnimationBlock(trigger, steps, durationMs, easing, line);
    }

    // D110: `accepts drops` is a two-word property name in a `has`/`with` list.
    string ReadAcceptsDrops(Token property)
    {
        if (Same(property.Text, "accepts") && Same(Current.Text, "drops"))
        {
            Read();
            return "accepts drops";
        }
        return property.Text;
    }

    static readonly string[] AlignWords = { "top", "middle", "bottom", "left", "center", "right" };

    // The UI property values of Read-OtterObjectBlockProperties and
    // Read-OtterInlineObjectProperties (D54): align <direction>, width full,
    // height full and a bare round or spread. Null for an ordinary value.
    Expr? ReadUiPropertyValue(Token property, bool hadIs)
    {
        if (Same(property.Text, "align"))
        {
            Token current = Current;
            if (current.Kind == TokenKind.Identifier && SameAny(current.Text, AlignWords))
            {
                Token direction = Read();
                return new LiteralExpr(direction.Text, direction.Line);
            }
            if (current.Kind == TokenKind.String && current.Value is string text && SameAny(text, AlignWords))
            {
                Token direction = Read();
                return new LiteralExpr(direction.Value, direction.Line);
            }
            throw Error($"I expected an alignment direction (top, middle, bottom, left, center, right) after 'align', but got '{current.Text}'.",
                current, "Use align top, align middle, align bottom, align left, align center, or align right.");
        }
        if (SameAny(property.Text, "width", "height") && Current.Kind == TokenKind.Identifier && Same(Current.Text, "full"))
        {
            Token full = Read();
            return new LiteralExpr("full", full.Line);
        }
        if (!hadIs && SameAny(property.Text, "round", "spread")) return new LiteralExpr(true, property.Line);
        return null;
    }

    // Words whose special value forms TryReadUiValue reads on the web; any
    // other use of them is an ordinary name there.
    static bool IsUiValueWord(string word) => SameAny(word, "current", "route", "drag", "dragged", "drop", "dropped");

    // A value this area starts at `token`, or null.
    Expr? TryReadUiValue(Token token)
    {
        if (token.Kind == TokenKind.Await) return ReadAwaitValue(token);
        Token? next = At(1);
        // D103: current route / route parameter X / query parameter X
        if (token.Kind == TokenKind.Identifier && Same(token.Text, "current") && next is { Kind: TokenKind.Identifier } && Same(next.Text, "route"))
        {
            Read(); Read();
            return new CurrentRouteExpr(token.Line);
        }
        if (token.Kind == TokenKind.Identifier && Same(token.Text, "route") && next?.Kind == TokenKind.Parameter)
        {
            Read(); Read();
            return new RouteParameterExpr(ReadValue(), token.Line);
        }
        if (token.Kind == TokenKind.Query && next?.Kind == TokenKind.Parameter)
        {
            Read(); Read();
            return new QueryParameterExpr(ReadValue(), token.Line);
        }
        // D110: dragged item / dropped files / drag data / drop x / drop y
        if (token.Kind == TokenKind.Identifier && next != null)
        {
            string? field = null;
            if (Same(token.Text, "dragged") && Same(next.Text, "item")) field = "dragged item";
            else if (Same(token.Text, "dropped") && Same(next.Text, "files")) field = "dropped files";
            else if (Same(token.Text, "drag") && Same(next.Text, "data")) field = "drag data";
            else if (Same(token.Text, "drop") && Same(next.Text, "x")) field = "drop x";
            else if (Same(token.Text, "drop") && Same(next.Text, "y")) field = "drop y";
            if (field != null)
            {
                Read(); Read();
                return new DragContextExpr(field, token.Line);
            }
        }
        return null;
    }

    // A condition this area starts at `head`, or null.
    Expr? TryReadUiCondition(Token head) => null;
}
