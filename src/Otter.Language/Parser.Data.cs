namespace Otter.Language;

// The parser for data: databases (D97, D98), the query language (D99) and
// the readability article `the` (`the name of person`). Each method follows
// its otterPS counterpart in src/Otter.Parser.psm1 token by token, with the
// same messages.
public sealed partial class Parser
{
    // A statement this area starts at `start`, or null (the token is left unread).
    Stmt? TryReadDataStatement(Token start)
    {
        TokenKind nextKind = At(1)?.Kind ?? TokenKind.EndOfFile;
        // `the property of target is value` is the assignment counterpart of
        // the readability form in ReadValue.
        if (Same(start.Text, "the") && At(1) is { } property && IsIdentifier(property) && IsAt(2, TokenKind.Of))
        {
            Expr target = ReadValue();
            Expect(TokenKind.Is, "I expected \"is\" after the property target.");
            Expr value = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected the property assignment to end here.");
            return new AssignStmt(target, value, start.Line);
        }
        // A D33 statement word followed by is/are/of/is not names a variable.
        if (IsIdentifier(start) && nextKind is TokenKind.Is or TokenKind.Are or TokenKind.Of or TokenKind.IsNot) return null;
        switch (start.Kind)
        {
            // connect to websocket/tcp and connect securely are networking.
            case TokenKind.Connect when nextKind != TokenKind.To && !(At(1) is { Kind: TokenKind.Identifier } securely && Same(securely.Text, "securely")):
                return ReadDatabaseConnect(start);
            case TokenKind.Disconnect:
            {
                Read();
                Expr connection = ReadValue();
                Expect(TokenKind.Newline, "I expected the disconnect statement to end here.");
                return new DisconnectDbStmt(connection, start.Line);
            }
            case TokenKind.Query:
                return ReadDbQuery(start);
            case TokenKind.Execute:
                return ReadDbExecute(start);
            case TokenKind.BeginTransaction:
                return ReadBeginTransaction(start);
            case TokenKind.Commit:
            {
                Read();
                Expr transaction = ReadValue();
                Expect(TokenKind.Newline, "I expected the commit statement to end here.");
                return new CommitTransactionStmt(transaction, start.Line);
            }
            case TokenKind.Rollback:
            {
                Read();
                Expr transaction = ReadValue();
                Expect(TokenKind.Newline, "I expected the rollback statement to end here.");
                return new RollbackTransactionStmt(transaction, start.Line);
            }
        }
        return null;
    }

    // A value this area starts at `token`, or null.
    Expr? TryReadDataValue(Token token) => null;

    // A condition this area starts at `head`, or null.
    Expr? TryReadDataCondition(Token head) => null;

    // ---- the readability article -------------------------------------------

    // `the` is skipped when it introduces a real `<property> of ...` (or a
    // two-word property such as `exit code of`), and, where otterPS reads a
    // property target (-PropertyTarget), before any name. Elsewhere `the` is
    // an ordinary name.
    void SkipReadabilityArticle(bool propertyTarget)
    {
        if (!Same(Current.Text, "the")) return;
        bool beforeName = position + 1 < tokens.Count && IsIdentifier(tokens[position + 1]);
        bool beforeProperty = position + 2 < tokens.Count && IsIdentifier(tokens[position + 1]) &&
                              (tokens[position + 2].Kind == TokenKind.Of || CommandResultPropertyAt(position + 1));
        if ((propertyTarget && beforeName) || beforeProperty) Read();
    }

    // otterPS's Test-OtterCommandResultPropertyAt.
    bool CommandResultPropertyAt(int at)
    {
        if (at >= tokens.Count) return false;
        Token first = tokens[at];
        if (!IsIdentifier(first)) return false;
        if (at + 2 >= tokens.Count) return false;
        Token second = tokens[at + 1], third = tokens[at + 2];
        bool Pair(string a, string b) => Same(first.Text, a) && Same(second.Text, b);
        if ((Pair("exit", "code") || Pair("error", "output") || Pair("rows", "affected") || Pair("primary", "key") ||
             Pair("database", "type") || Pair("default", "expression")) && third.Kind == TokenKind.Of) return true;
        if (at + 3 < tokens.Count)
        {
            Token fourth = tokens[at + 3];
            if (Pair("last", "inserted") && Same(third.Text, "id") && fourth.Kind == TokenKind.Of) return true;
            if (Pair("primary", "key") && Same(third.Text, "position") && fourth.Kind == TokenKind.Of) return true;
        }
        return false;
    }

    // ---- databases (D97, D98) ----------------------------------------------

    // connect database into db
    Stmt ReadDatabaseConnect(Token start)
    {
        Read();
        Expr config = ReadMathExpression();
        Expect(TokenKind.Into, "I expected \"into\" and a variable name after the database configuration.", "Write: connect database into db");
        Token target = ReadVariableName("I expected a variable name after \"into\".");
        Expect(TokenKind.Newline, "I expected the connect statement to end here.");
        return new ConnectDbStmt(config, target.Text, start.Line);
    }

    // The indented `parameter "name" is value` lines of a query or execute
    // block, up to the block's end.
    List<DbParameter> ReadDbParameters(string blockName)
    {
        var parameters = new List<DbParameter>();
        while (!Is(TokenKind.Dedent) && !Is(TokenKind.BlockEnd) && !Is(TokenKind.EndOfFile))
        {
            Token current = Current;
            if (current.Kind == TokenKind.Parameter || (current.Kind == TokenKind.Identifier && Same(current.Text, "parameter")))
            {
                Read();
                Token nameToken = Current;
                if (nameToken.Kind is not (TokenKind.String or TokenKind.Identifier))
                {
                    throw Error("I expected a parameter name (such as \"id\" or id) after \"parameter\".", nameToken, "Write: parameter \"id\" is userId");
                }
                Read();
                string name = nameToken.Kind == TokenKind.String ? (string)nameToken.Value! : nameToken.Text;
                Expect(TokenKind.Is, "I expected \"is\" after the parameter name.", $"Write: parameter \"{name}\" is value");
                Expr value = ReadMathExpression();
                Expect(TokenKind.Newline, "I expected the parameter definition to end here.");
                SkipNewlines();
                parameters.Add(new DbParameter(name, value, nameToken.Line));
            }
            else
            {
                throw Error($"I expected 'parameter' or the end of the {blockName} block, but found '{current.Text}'.", current, "Write: parameter \"name\" is value");
            }
        }
        Expect(TokenKind.Dedent, $"I expected the {blockName} block to end.");
        if (Is(TokenKind.BlockEnd)) { Read(); SkipNewlines(); }
        SkipNewlines();
        return parameters;
    }

    // query db with "select ..." into rows (or the indented block form)
    Stmt ReadDbQuery(Token start)
    {
        Read();
        Expr connection = ReadValue();
        Expect(TokenKind.With, "I expected \"with\" after the database connection.", "Write: query db with \"select ... \" into tasks");
        Expr query;
        var parameters = new List<DbParameter>();
        if (Is(TokenKind.Newline))
        {
            Read();
            Expect(TokenKind.Indent, "I expected an indented block after \"with\".");
            SkipNewlines();
            query = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected a newline after the SQL query string.");
            SkipNewlines();
            parameters = ReadDbParameters("query");
            Expect(TokenKind.Into, "I expected \"into\" and a result variable name for the query.", "Write: query db with ... into tasks");
        }
        else
        {
            query = ReadMathExpression();
            Expect(TokenKind.Into, "I expected \"into\" and a result variable name for the query.", "Write: query db with \"select ... \" into tasks");
        }
        Token target = ReadVariableName("I expected a variable name after \"into\".");
        Expect(TokenKind.Newline, "I expected the query statement to end here.");
        return new DbQueryStmt(connection, query, parameters, target.Text, start.Line);
    }

    // execute db with "insert ..." [into result] (or the indented block form)
    Stmt ReadDbExecute(Token start)
    {
        Read();
        Expr connection = ReadValue();
        Expect(TokenKind.With, "I expected \"with\" after the database connection.", "Write: execute db with \"insert ... \" into result");
        Expr command;
        var parameters = new List<DbParameter>();
        string? target = null;
        if (Is(TokenKind.Newline))
        {
            Read();
            Expect(TokenKind.Indent, "I expected an indented block after \"with\".");
            SkipNewlines();
            command = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected a newline after the SQL command string.");
            SkipNewlines();
            parameters = ReadDbParameters("execute");
            if (Is(TokenKind.Into))
            {
                Read();
                target = ReadVariableName("I expected a variable name after \"into\".").Text;
                Expect(TokenKind.Newline, "I expected the execute statement to end here.");
            }
        }
        else
        {
            command = ReadMathExpression();
            if (Is(TokenKind.Into))
            {
                Read();
                target = ReadVariableName("I expected a variable name after \"into\".").Text;
            }
            Expect(TokenKind.Newline, "I expected the execute statement to end here.");
        }
        return new DbExecuteStmt(connection, command, parameters, target, start.Line);
    }

    // begin transaction on db into tx
    Stmt ReadBeginTransaction(Token start)
    {
        Read();
        Token on = Current;
        if (on.Kind != TokenKind.On && !(on.Kind == TokenKind.Identifier && Same(on.Text, "on")))
        {
            throw Error("I expected \"on\" after \"begin transaction\".", on, "Write: begin transaction on db into tx");
        }
        Read();
        Expr connection = ReadValue();
        Expect(TokenKind.Into, "I expected \"into\" and a transaction variable name.", "Write: begin transaction on db into tx");
        Token target = ReadVariableName("I expected a transaction variable name after \"into\".");
        Expect(TokenKind.Newline, "I expected the begin transaction statement to end here.");
        return new BeginTransactionStmt(connection, target.Text, start.Line);
    }

    // SoftContinuation (otterPS's Test-OtterSoftContinuation) is in Parser.Network.cs.

    // get tables from db into tables / get columns from TABLE in db into
    // columns (D98), or null when `get` is not followed by either word.
    Stmt? TryReadSchemaGet(Token start)
    {
        Token kind = Current;
        if (kind.Kind != TokenKind.Identifier) return null;
        if (Same(kind.Text, "tables"))
        {
            Read();
            Expect(TokenKind.From, "I expected \"from\" after \"get tables\".", "Write: get tables from db into tables");
            Expr connection = ReadValue();
            bool continued = SoftContinuation();
            Expect(TokenKind.Into, "I expected \"into\" and a result name after \"get tables from db\".", "Write: get tables from db into tables");
            Token target = ReadVariableName("I expected a result name after \"into\".");
            Expect(TokenKind.Newline, "I expected the get tables statement to end here.");
            if (continued) Expect(TokenKind.Dedent, "I expected the continued get tables clause to end.");
            return new GetTablesStmt(connection, target.Text, start.Line);
        }
        if (Same(kind.Text, "columns"))
        {
            Read();
            Expect(TokenKind.From, "I expected \"from\" after \"get columns\".", "Write: get columns from \"table\" in db into columns");
            Expr table = ReadValue();
            Expect(TokenKind.In, "I expected \"in\" and the database connection after the table.", "Write: get columns from \"table\" in db into columns");
            Expr connection = ReadValue();
            bool continued = SoftContinuation();
            Expect(TokenKind.Into, "I expected \"into\" and a result name after the database connection.", "Write: get columns from \"table\" in db into columns");
            Token target = ReadVariableName("I expected a result name after \"into\".");
            Expect(TokenKind.Newline, "I expected the get columns statement to end here.");
            if (continued) Expect(TokenKind.Dedent, "I expected the continued get columns clause to end.");
            return new GetColumnsStmt(table, connection, target.Text, start.Line);
        }
        return null;
    }

    // ---- the query language (D99) ------------------------------------------

    static readonly string[] ProtectedGetWords =
    {
        "files", "folders", "clipboard", "environment", "current", "arguments", "system", "processes",
        "symbolic", "owner", "registry", "credential", "event", "tables", "columns",
    };

    // otterPS's Test-OtterIsQueryStatement: `get` followed by distinct,
    // `all from`, or a `from` and then an `in` within the statement.
    bool IsQueryStatementAhead()
    {
        if (Current.Kind != TokenKind.Get) return false;
        int pos = position + 1;
        if (pos >= tokens.Count) return false;
        while (pos < tokens.Count && tokens[pos].Kind is TokenKind.Newline or TokenKind.Indent) pos++;
        if (pos >= tokens.Count) return false;
        Token first = tokens[pos];
        if (first.Kind == TokenKind.Distinct) return true;
        if (first.Kind == TokenKind.Identifier && Same(first.Text, "all"))
        {
            int next = pos + 1;
            while (next < tokens.Count && tokens[next].Kind is TokenKind.Newline or TokenKind.Indent) next++;
            if (next < tokens.Count && tokens[next].Kind == TokenKind.From) return true;
        }
        if (first.Kind is TokenKind.Files or TokenKind.Folders) return false;
        if (first.Kind == TokenKind.Identifier && SameAny(first.Text, ProtectedGetWords)) return false;
        bool sawFrom = false;
        int limit = Math.Min(tokens.Count, pos + 80);
        int depth = 0;
        while (pos < limit)
        {
            TokenKind kind = tokens[pos].Kind;
            if (kind == TokenKind.EndOfFile) break;
            if (kind == TokenKind.Indent) depth++;
            else if (kind == TokenKind.Dedent)
            {
                depth--;
                if (depth <= 0) break;
            }
            else if (kind == TokenKind.Newline)
            {
                if (depth == 0)
                {
                    int next = pos + 1;
                    while (next < limit && tokens[next].Kind == TokenKind.Newline) next++;
                    if (next >= limit || tokens[next].Kind != TokenKind.Indent) break;
                }
            }
            else if (kind == TokenKind.From) sawFrom = true;
            else if (sawFrom && kind == TokenKind.In) return true;
            pos++;
        }
        return false;
    }

    // Newlines and indents between a query's words; each indent is counted
    // so the matching dedents can be read at the end.
    void SkipQueryLayout(ref int indentCount)
    {
        while (Is(TokenKind.Newline) || Is(TokenKind.Indent))
        {
            if (Is(TokenKind.Indent)) indentCount++;
            Read();
        }
    }

    // FROM TABLE IN CONNECTION [as alias] [with], shared by both query forms.
    (string Table, Expr Connection, string? Alias) ReadQuerySource(ref int indentCount, string fromMessage)
    {
        SkipQueryLayout(ref indentCount);
        Expect(TokenKind.From, fromMessage);
        SkipQueryLayout(ref indentCount);
        Token tableToken = Read();
        string table = tableToken.Kind == TokenKind.String ? (string)tableToken.Value! : tableToken.Text;
        SkipQueryLayout(ref indentCount);
        Expect(TokenKind.In, "I expected \"in\" and database connection after table name.");
        Expr connection = ReadMathExpression();
        string? alias = null;
        if (Is(TokenKind.As))
        {
            Read();
            alias = ReadVariableName("I expected alias name after \"as\".").Text;
        }
        if (Is(TokenKind.With)) Read();
        return (table, connection, alias);
    }

    // Newlines, indents and dedents before a query clause.
    void SkipQueryClauseLayout(ref int indentCount)
    {
        while (Is(TokenKind.Newline) || Is(TokenKind.Indent) || Is(TokenKind.Dedent))
        {
            if (Is(TokenKind.Indent)) indentCount++;
            else if (Is(TokenKind.Dedent)) indentCount--;
            Read();
        }
    }

    void ReadQueryDedents(ref int indentCount)
    {
        while (indentCount > 0 && (Is(TokenKind.Dedent) || Is(TokenKind.Newline)))
        {
            if (Is(TokenKind.Dedent)) indentCount--;
            Read();
        }
    }

    // otterPS's Read-OtterQueryStatement.
    Stmt ReadQueryStatement()
    {
        Token start = Read();
        bool distinct = false;
        int indentCount = 0;
        SkipQueryLayout(ref indentCount);
        if (Is(TokenKind.Distinct))
        {
            distinct = true;
            Read();
        }
        var projections = new List<Expr>();
        if (Current.Kind == TokenKind.Identifier && Same(Current.Text, "all"))
        {
            Read();
        }
        else
        {
            while (!Is(TokenKind.From) && !Is(TokenKind.EndOfFile))
            {
                SkipQueryLayout(ref indentCount);
                if (Is(TokenKind.From)) break;
                projections.Add(ReadValue());
                if (Is(TokenKind.And)) Read();
                else if (Current.Kind == TokenKind.Identifier && Current.Text == ",") Read();
                SkipQueryLayout(ref indentCount);
            }
        }
        var (table, connection, alias) = ReadQuerySource(ref indentCount, "I expected \"from\" and a table name in query.");

        Expr? where = null, limit = null, offset = null;
        IReadOnlyList<QueryOrderByItem> orderBy = Array.Empty<QueryOrderByItem>();
        string? target = null;
        while (!Is(TokenKind.EndOfFile))
        {
            SkipQueryClauseLayout(ref indentCount);
            Token clause = Current;
            if (clause.Kind == TokenKind.With) Read();
            else if (clause.Kind == TokenKind.Where)
            {
                Read();
                where = ReadQueryWhere();
            }
            else if (clause.Kind == TokenKind.OrderBy)
            {
                Read();
                orderBy = ReadQueryOrderByItems();
            }
            else if (clause.Kind == TokenKind.Identifier && SameAny(clause.Text, "take", "first", "limit"))
            {
                Read();
                limit = ReadMathExpression();
            }
            else if (clause.Kind == TokenKind.Identifier && SameAny(clause.Text, "skip", "offset"))
            {
                Read();
                offset = ReadMathExpression();
            }
            else if (clause.Kind == TokenKind.Into)
            {
                Read();
                target = ReadVariableName("I expected target variable name after \"into\".").Text;
                break;
            }
            else break;
        }
        if (string.IsNullOrEmpty(target))
        {
            throw Error("I expected \"into\" and a result name for the query.", Current, "get all from customers in db into customers");
        }
        ReadQueryDedents(ref indentCount);
        return new QueryStmt(distinct, projections, table, connection, alias, where, orderBy, limit, offset, target, start.Line);
    }

    // otterPS's Read-OtterQueryAggregateStatement.
    Stmt ReadQueryAggregateStatement()
    {
        Token start = Read();
        string function = start.Text.ToLowerInvariant();
        int indentCount = 0;
        SkipQueryLayout(ref indentCount);
        if (Is(TokenKind.Of))
        {
            Read();
            SkipQueryLayout(ref indentCount);
        }
        Expr expression;
        if (function == "count" && Same(Current.Text, "all"))
        {
            Read();
            expression = new VariableExpr("*", start.Line);
        }
        else expression = ReadMathExpression();
        var (table, connection, alias) = ReadQuerySource(ref indentCount, "I expected \"from\" and table name after aggregate expression.");

        Expr? where = null;
        string? target = null;
        while (!Is(TokenKind.EndOfFile))
        {
            SkipQueryClauseLayout(ref indentCount);
            Token clause = Current;
            if (clause.Kind == TokenKind.With) Read();
            else if (clause.Kind == TokenKind.Where)
            {
                Read();
                where = ReadQueryWhere();
            }
            else if (clause.Kind == TokenKind.Into)
            {
                Read();
                target = ReadVariableName("I expected target variable name after \"into\".").Text;
                break;
            }
            else break;
        }
        if (string.IsNullOrEmpty(target))
        {
            throw Error("I expected \"into\" and a result name for the aggregate.", Current, $"{function} ... from {table} in db into total");
        }
        ReadQueryDedents(ref indentCount);
        if (Is(TokenKind.Newline)) Read();
        return new QueryAggregateStmt(function, expression, table, connection, alias, where, target, start.Line);
    }

    // or binds loosest, then and; no `not` and no parentheses (D99).
    Expr ReadQueryWhere()
    {
        Expr left = ReadQueryAnd();
        while (Is(TokenKind.Or))
        {
            Token op = Read();
            left = new LogicalExpr(left, LogicalOp.Or, ReadQueryAnd(), op.Line);
        }
        return left;
    }

    Expr ReadQueryAnd()
    {
        Expr left = ReadQueryTerm();
        while (Is(TokenKind.And))
        {
            Token op = Read();
            left = new LogicalExpr(left, LogicalOp.And, ReadQueryTerm(), op.Line);
        }
        return left;
    }

    // Operands are single values: `and` must stay the where-clause connective.
    Expr ReadQueryTerm()
    {
        Expr left = ReadValue();
        int line = left.Line;
        if (Is(TokenKind.IsIn) || Is(TokenKind.In) || (Current.Kind == TokenKind.Identifier && Same(Current.Text, "in")))
        {
            Read();
            return new QueryInExpr(left, ReadValue(), false, line);
        }
        if (Is(TokenKind.IsNotIn))
        {
            Read();
            return new QueryInExpr(left, ReadValue(), true, line);
        }
        if (Is(TokenKind.IsBetween) || Is(TokenKind.Between) || (Current.Kind == TokenKind.Identifier && Same(Current.Text, "between")))
        {
            Read();
            Expr lower = ReadValue();
            Expect(TokenKind.And, "I expected \"and\" and upper bound in between expression.", "where age is between 18 and 65");
            Expr upper = ReadValue();
            return new QueryBetweenExpr(left, lower, upper, line);
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
        CompareOp? comparison = Current.Kind switch
        {
            TokenKind.Is => CompareOp.Equal,
            TokenKind.IsNot => CompareOp.NotEqual,
            TokenKind.IsAtLeast => CompareOp.AtLeast,
            TokenKind.IsAtMost => CompareOp.AtMost,
            TokenKind.IsGreaterThan => CompareOp.GreaterThan,
            TokenKind.IsLessThan => CompareOp.LessThan,
            _ => null,
        };
        if (comparison is null) return left;
        Read();
        return new ComparisonExpr(left, comparison.Value, ReadValue(), line);
    }

    // order by price descending then name - the layout between items is
    // read here and not counted.
    List<QueryOrderByItem> ReadQueryOrderByItems()
    {
        var items = new List<QueryOrderByItem>();
        while (!Is(TokenKind.EndOfFile))
        {
            Expr expression = ReadMathExpression();
            bool descending = false;
            if (Is(TokenKind.Ascending)) Read();
            else if (Is(TokenKind.Descending))
            {
                Read();
                descending = true;
            }
            items.Add(new QueryOrderByItem(expression, descending));
            while (Is(TokenKind.Newline) || Is(TokenKind.Indent)) Read();
            if (Is(TokenKind.Then)) Read();
            else break;
        }
        return items;
    }
}
