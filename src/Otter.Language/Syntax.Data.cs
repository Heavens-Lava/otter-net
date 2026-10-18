namespace Otter.Language;

// Syntax nodes for data: databases (D97, D98) and the query language (D99).

// ---- databases (D97) -------------------------------------------------------

// connect database into db
public sealed class ConnectDbStmt(Expr config, string target, int line) : Stmt(line)
{
    public Expr Config { get; } = config;
    public string Target { get; } = target;
}

// disconnect db
public sealed class DisconnectDbStmt(Expr connection, int line) : Stmt(line)
{
    public Expr Connection { get; } = connection;
}

// parameter "id" is userId (inside a query or execute block)
public sealed class DbParameter(string name, Expr value, int line)
{
    public string Name { get; } = name;
    public Expr Value { get; } = value;
    public int Line { get; } = line;
}

// query db with "select ..." [parameter ...] into rows
public sealed class DbQueryStmt(Expr connection, Expr query, IReadOnlyList<DbParameter> parameters, string target, int line) : Stmt(line)
{
    public Expr Connection { get; } = connection;
    public Expr Query { get; } = query;
    public IReadOnlyList<DbParameter> Parameters { get; } = parameters;
    public string Target { get; } = target;
}

// execute db with "insert ..." [parameter ...] [into result]
public sealed class DbExecuteStmt(Expr connection, Expr command, IReadOnlyList<DbParameter> parameters, string? target, int line) : Stmt(line)
{
    public Expr Connection { get; } = connection;
    public Expr Command { get; } = command;
    public IReadOnlyList<DbParameter> Parameters { get; } = parameters;
    public string? Target { get; } = target;
}

// begin transaction on db into tx
public sealed class BeginTransactionStmt(Expr connection, string target, int line) : Stmt(line)
{
    public Expr Connection { get; } = connection;
    public string Target { get; } = target;
}

// commit tx / rollback tx
public sealed class CommitTransactionStmt(Expr transaction, int line) : Stmt(line)
{
    public Expr Transaction { get; } = transaction;
}

public sealed class RollbackTransactionStmt(Expr transaction, int line) : Stmt(line)
{
    public Expr Transaction { get; } = transaction;
}

// get tables from db into tables (D98)
public sealed class GetTablesStmt(Expr connection, string target, int line) : Stmt(line)
{
    public Expr Connection { get; } = connection;
    public string Target { get; } = target;
}

// get columns from "tasks" in db into columns (D98)
public sealed class GetColumnsStmt(Expr table, Expr connection, string target, int line) : Stmt(line)
{
    public Expr Table { get; } = table;
    public Expr Connection { get; } = connection;
    public string Target { get; } = target;
}

// ---- the query language (D99) ----------------------------------------------

public sealed class QueryOrderByItem(Expr expression, bool isDescending)
{
    public Expr Expression { get; } = expression;
    public bool IsDescending { get; } = isDescending;
}

// get [distinct] all|fields from TABLE in DB [as alias] [where ...] [order by ...]
//     [take N] [skip N] into target
public sealed class QueryStmt(bool isDistinct, IReadOnlyList<Expr> projections, string table, Expr connection, string? alias,
    Expr? where, IReadOnlyList<QueryOrderByItem> orderBy, Expr? limit, Expr? offset, string target, int line) : Stmt(line)
{
    public bool IsDistinct { get; } = isDistinct;
    public IReadOnlyList<Expr> Projections { get; } = projections;
    public string Table { get; } = table;
    public Expr Connection { get; } = connection;
    public string? Alias { get; } = alias;
    public Expr? Where { get; } = where;
    public IReadOnlyList<QueryOrderByItem> OrderBy { get; } = orderBy;
    public Expr? Limit { get; } = limit;
    public Expr? Offset { get; } = offset;
    public string Target { get; } = target;
}

// count all / sum of score / ... from TABLE in DB [where ...] into target
public sealed class QueryAggregateStmt(string aggregateFunction, Expr expression, string table, Expr connection, string? alias,
    Expr? where, string target, int line) : Stmt(line)
{
    public string AggregateFunction { get; } = aggregateFunction;
    public Expr Expression { get; } = expression;
    public string Table { get; } = table;
    public Expr Connection { get; } = connection;
    public string? Alias { get; } = alias;
    public Expr? Where { get; } = where;
    public string Target { get; } = target;
}

// where age is between 18 and 65 (only inside a query's where clause)
public sealed class QueryBetweenExpr(Expr expression, Expr lower, Expr upper, int line) : Expr(line)
{
    public Expr Expression { get; } = expression;
    public Expr Lower { get; } = lower;
    public Expr Upper { get; } = upper;
}

// where category is [not] in targetCats (only inside a query's where clause)
public sealed class QueryInExpr(Expr expression, Expr collection, bool isNot, int line) : Expr(line)
{
    public Expr Expression { get; } = expression;
    public Expr Collection { get; } = collection;
    public bool IsNot { get; } = isNot;
}
