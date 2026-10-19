using Otter.Language;

namespace Otter.Runtime;

// The runtime for data: databases (D97, D98) and the query language (D99,
// in Queries.cs), as otterPS's Invoke-OtterStatement cases run them.
public sealed partial class Interpreter
{
    bool TryExecuteData(Stmt statement, Scope scope)
    {
        switch (statement)
        {
            // connect database into db
            case ConnectDbStmt connect:
            {
                object? config = Evaluate(connect.Config, scope);
                scope.Set(connect.Target, Database(() => OtterDatabase.Connect(config), connect.Line));
                return true;
            }
            case DisconnectDbStmt disconnect:
            {
                object? connection = Evaluate(disconnect.Connection, scope);
                Database(() => OtterDatabase.Disconnect(connection), disconnect.Line);
                return true;
            }
            // query db with "select ..." into rows
            case DbQueryStmt query:
            {
                object? connection = Evaluate(query.Connection, scope);
                string sql = Values.Format(Evaluate(query.Query, scope));
                var parameters = DbParameters(query.Parameters, scope);
                scope.Set(query.Target, Database(() => OtterDatabase.Query(connection, sql, parameters), query.Line));
                return true;
            }
            // execute db with "insert ..." [into result]
            case DbExecuteStmt execute:
            {
                object? connection = Evaluate(execute.Connection, scope);
                string sql = Values.Format(Evaluate(execute.Command, scope));
                var parameters = DbParameters(execute.Parameters, scope);
                OtterThing result = Database(() => OtterDatabase.Execute(connection, sql, parameters), execute.Line);
                if (!string.IsNullOrEmpty(execute.Target)) scope.Set(execute.Target, result);
                return true;
            }
            case BeginTransactionStmt begin:
            {
                object? connection = Evaluate(begin.Connection, scope);
                scope.Set(begin.Target, Database(() => OtterDatabase.BeginTransaction(connection, begin.Line), begin.Line));
                return true;
            }
            case CommitTransactionStmt commit:
            {
                object? transaction = Evaluate(commit.Transaction, scope);
                Database(() => OtterDatabase.Commit(transaction), commit.Line);
                return true;
            }
            case RollbackTransactionStmt rollback:
            {
                object? transaction = Evaluate(rollback.Transaction, scope);
                Database(() => OtterDatabase.Rollback(transaction), rollback.Line);
                return true;
            }
            // get tables from db into tables (D98)
            case GetTablesStmt tables:
            {
                object? connection = Evaluate(tables.Connection, scope);
                scope.Set(tables.Target, Database(() => OtterDatabase.Tables(connection), tables.Line));
                return true;
            }
            // get columns from "tasks" in db into columns (D98)
            case GetColumnsStmt columns:
            {
                object? table = Evaluate(columns.Table, scope);
                object? connection = Evaluate(columns.Connection, scope);
                scope.Set(columns.Target, Database(() => OtterDatabase.Columns(connection, table), columns.Line));
                return true;
            }
            case QueryStmt query:
                scope.Set(query.Target, RunQuery(query, scope));
                return true;
            case QueryAggregateStmt aggregate:
                scope.Set(aggregate.Target, RunAggregate(aggregate, scope));
                return true;
        }
        return false;
    }

    bool TryEvaluateData(Expr expression, Scope scope, out object? value)
    {
        value = null;
        return false;
    }

    // The block's parameters, evaluated in order into a PowerShell hashtable
    // (names ignore case; a repeated name keeps its first spelling).
    Dictionary<string, object?> DbParameters(IReadOnlyList<DbParameter> parameters, Scope scope)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (DbParameter parameter in parameters) values[parameter.Name] = Evaluate(parameter.Value, scope);
        return values;
    }

    // Database problems become runtime errors on the statement's line. A
    // missing SQLite library is reported plainly (otterPS on Windows always
    // has winsqlite3.dll).
    T Database<T>(Func<T> work, int line)
    {
        try { return work(); }
        catch (DatabaseProblem problem) { throw RuntimeError(problem.Message, line); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            throw RuntimeError($"I could not load SQLite ({SqliteNative.LibraryName}), which Otter needs to use a database.", line,
                "Install SQLite on this computer, then run the program again.");
        }
    }

    void Database(Action work, int line) => Database(() => { work(); return true; }, line);
}
