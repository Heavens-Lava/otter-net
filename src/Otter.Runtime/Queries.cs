using Otter.Language;

namespace Otter.Runtime;

// The Otter query language (D99, experimental in 1.0.1): a query becomes one
// parameterised SQL statement, exactly as otterPS's QueryStmt and
// QueryAggregateStmt cases and ConvertTo-OtterQuerySqlExpression build it.
// Values from the program are always parameters (@p0, @p1, ...), never SQL
// text; names of columns and tables are written inside [brackets].
public sealed partial class Interpreter
{
    sealed class QueryParameters
    {
        public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        int next;
        public string Add(object? value)
        {
            string name = $"p{next++}";
            Values[name] = value;
            return name;
        }
        public string NewName() => $"p{next++}";
    }

    // A column named in a projection, an ordering or an aggregate. otterPS
    // reads `.PropertyName` there, which PropertyAccessExpr does not have, so
    // `name of t` gives the column "[t].[]" (docs/SPEC-QUESTIONS.md, Q2).
    static string? ColumnName(Expr expression) => expression switch
    {
        VariableExpr variable => $"[{variable.Name}]",
        PropertyAccessExpr property => $"[{(property.Target is VariableExpr owner ? owner.Name : "col")}].[]",
        _ => null,
    };

    static string TableSql(string table, string? alias) =>
        string.IsNullOrEmpty(alias) ? $"[{table}]" : $"[{table}] AS [{alias}]";

    // get [distinct] ... from TABLE in DB [as alias] [where] [order by] [take] [skip] into x
    object? RunQuery(QueryStmt query, Scope scope)
    {
        object? connection = Evaluate(query.Connection, scope);
        var parameters = new QueryParameters();

        var projections = new List<string>();
        if (query.Projections.Count == 0) projections.Add("*");
        foreach (Expr projection in query.Projections)
        {
            if (projection is VariableExpr { Name: "*" }) projections.Add("*");
            else projections.Add(ColumnName(projection) ?? $"[{Values.Format(Evaluate(projection, scope))}]");
        }
        string distinct = query.IsDistinct ? "DISTINCT " : "";
        string where = query.Where is null ? "" : QuerySql(query.Where, scope, parameters);

        var order = new List<string>();
        foreach (QueryOrderByItem item in query.OrderBy)
        {
            string column = ColumnName(item.Expression) ?? $"[{Values.Format(Evaluate(item.Expression, scope))}]";
            order.Add($"{column} {(item.IsDescending ? "DESC" : "ASC")}");
        }

        string limit = "";
        if (query.Limit is not null)
        {
            object? limitValue = Evaluate(query.Limit, scope);
            string limitName = parameters.NewName();
            parameters.Values[limitName] = PowerShellText.ToInt(limitValue);
            if (query.Offset is not null)
            {
                object? offsetValue = Evaluate(query.Offset, scope);
                string offsetName = parameters.NewName();
                parameters.Values[offsetName] = PowerShellText.ToInt(offsetValue);
                limit = $" LIMIT @{limitName} OFFSET @{offsetName}";
            }
            else limit = $" LIMIT @{limitName}";
        }
        else if (query.Offset is not null)
        {
            object? offsetValue = Evaluate(query.Offset, scope);
            string offsetName = parameters.NewName();
            parameters.Values[offsetName] = PowerShellText.ToInt(offsetValue);
            limit = $" LIMIT -1 OFFSET @{offsetName}";
        }

        string sql = $"SELECT {distinct}{string.Join(", ", projections)} FROM {TableSql(query.Table, query.Alias)}";
        if (where.Length > 0) sql += $" WHERE {where}";
        if (order.Count > 0) sql += $" ORDER BY {string.Join(", ", order)}";
        sql += limit;
        return Database(() => OtterDatabase.Query(connection, sql, parameters.Values), query.Line);
    }

    // count all / sum of x / ... from TABLE in DB [where] into total
    object? RunAggregate(QueryAggregateStmt aggregate, Scope scope)
    {
        object? connection = Evaluate(aggregate.Connection, scope);
        var parameters = new QueryParameters();
        string function = aggregate.AggregateFunction.ToLowerInvariant() switch
        {
            "sum" => "SUM",
            "total" => "TOTAL",
            "average" or "avg" => "AVG",
            "minimum" or "min" => "MIN",
            "maximum" or "max" => "MAX",
            _ => "COUNT",
        };
        string column = aggregate.Expression is VariableExpr { Name: "*" } ? "*" : ColumnName(aggregate.Expression) ?? "*";
        string where = aggregate.Where is null ? "" : QuerySql(aggregate.Where, scope, parameters);

        string sql = $"SELECT {function}({column}) AS [agg_result] FROM {TableSql(aggregate.Table, aggregate.Alias)}";
        if (where.Length > 0) sql += $" WHERE {where}";
        OtterList rows = Database(() => OtterDatabase.Query(connection, sql, parameters.Values), aggregate.Line);

        bool count = aggregate.AggregateFunction.ToLowerInvariant() == "count";
        object? value = rows.Count > 0 && rows[0] is OtterThing first ? first.ReadProperty("agg_result") : null;
        return value ?? (count ? 0.0 : null);
    }

    // ConvertTo-OtterQuerySqlExpression: names stay names, everything else
    // is evaluated in the program and bound as a parameter.
    string QuerySql(Expr node, Scope scope, QueryParameters parameters)
    {
        switch (node)
        {
            case VariableExpr variable:
                return variable.Name == "*" ? "*" : $"[{variable.Name}]";
            case PropertyAccessExpr property:
                return $"[{(property.Target is VariableExpr owner ? owner.Name : "col")}].[{property.Property}]";
            case LiteralExpr literal:
                return "@" + parameters.Add(literal.Value);
            case QueryBetweenExpr between:
            {
                string column = QuerySql(between.Expression, scope, parameters);
                object? low = Evaluate(between.Lower, scope);
                object? high = Evaluate(between.Upper, scope);
                string lowName = parameters.Add(low);
                string highName = parameters.Add(high);
                return $"({column} BETWEEN @{lowName} AND @{highName})";
            }
            case QueryInExpr inList:
            {
                string column = QuerySql(inList.Expression, scope, parameters);
                object? items = Evaluate(inList.Collection, scope);
                var list = items switch
                {
                    OtterList many => many.ToList(),
                    null => new List<object?>(),
                    _ => new List<object?> { items },
                };
                if (list.Count == 0) return inList.IsNot ? "(1 = 1)" : "(1 = 0)";
                var names = list.Select(item => "@" + parameters.Add(item)).ToList();
                return $"({column} {(inList.IsNot ? "NOT IN" : "IN")} ({string.Join(", ", names)}))";
            }
            case TextMatchExpr textMatch:
            {
                string column = QuerySql(textMatch.Text, scope, parameters);
                string pattern = PowerShellText.Of(Evaluate(textMatch.Part, scope));
                string like = textMatch.Match == TextMatch.StartsWith ? pattern + "%" : "%" + pattern;
                return $"({column} LIKE @{parameters.Add(like)})";
            }
            case ContainsExpr contains:
            {
                string column = QuerySql(contains.Collection, scope, parameters);
                string pattern = PowerShellText.Of(Evaluate(contains.Item, scope));
                return $"({column} LIKE @{parameters.Add("%" + pattern + "%")})";
            }
            case ComparisonExpr comparison:
            {
                string column = QuerySql(comparison.Left, scope, parameters);
                object? right = Evaluate(comparison.Right, scope);
                if (right is null && comparison.Op == CompareOp.Equal) return $"({column} IS NULL)";
                if (right is null && comparison.Op == CompareOp.NotEqual) return $"({column} IS NOT NULL)";
                string name = parameters.Add(right);
                string op = comparison.Op switch
                {
                    CompareOp.NotEqual => "<>",
                    CompareOp.GreaterThan => ">",
                    CompareOp.AtLeast => ">=",
                    CompareOp.LessThan => "<",
                    CompareOp.AtMost => "<=",
                    _ => "=",
                };
                return $"({column} {op} @{name})";
            }
            case LogicalExpr logical:
            {
                string left = QuerySql(logical.Left, scope, parameters);
                string right = QuerySql(logical.Right, scope, parameters);
                return $"({left} {(logical.Op == LogicalOp.Or ? "OR" : "AND")} {right})";
            }
            case NotExpr not:
                return $"(NOT {QuerySql(not.Operand, scope, parameters)})";
            default:
                return "@" + parameters.Add(Evaluate(node, scope));
        }
    }
}
