using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Runtime;

// A database problem in otterPS's own words; the interpreter reports it as a
// runtime error on the statement's line.
public sealed class DatabaseProblem(string message) : Exception(message);

// otterPS's OtterDatabaseConnection: the open SQLite handle behind a
// `database connection` thing (its hidden `__connection` property).
public sealed class OtterDatabaseConnection(string providerName, string connectionString, nint handle)
{
    public string ProviderName { get; } = providerName;
    public string ConnectionString { get; } = connectionString;
    public nint Handle { get; set; } = handle;
    public bool IsOpen { get; set; } = true;
    public OtterDatabaseTransaction? ActiveTransaction { get; set; }

    public string MaskedConnectionString() =>
        string.IsNullOrEmpty(ConnectionString) ? "" : OtterDatabase.Mask(ConnectionString);

    // A PowerShell class without ToString prints its class name.
    public override string ToString() => "OtterDatabaseConnection";
}

// otterPS's OtterDatabaseTransaction (a `database transaction` thing's
// hidden `__transaction` property).
public sealed class OtterDatabaseTransaction(OtterDatabaseConnection connection, int line)
{
    public OtterDatabaseConnection Connection { get; } = connection;
    public bool IsActive { get; set; } = true;
    public int Line { get; } = line;
    public override string ToString() => "OtterDatabaseTransaction";
}

// otterPS's src/Otter.Database.psm1 (D97, D98): the SQLite reference
// provider and the helpers the interpreter calls, with the same checks in
// the same order and the same messages. PowerShell's parameter binding
// errors (a mandatory argument that is gone or empty) reach the user as
// internal problems, as in otterPS.
public static class OtterDatabase
{
    static InternalProblem NullArgument(string parameter) => new($"Cannot bind argument to parameter '{parameter}' because it is null.");
    static InternalProblem EmptyArgument(string parameter) => new($"Cannot bind argument to parameter '{parameter}' because it is an empty string.");

    static readonly Regex Secret = new(@"(?i)(password|pwd|secret)\s*=\s*[^;]+", RegexOptions.CultureInvariant);

    public static string Mask(string connection) => Secret.Replace(connection, "$1=****");

    static bool IsType(OtterThing thing, string typeName) => string.Equals(thing.TypeName, typeName, StringComparison.OrdinalIgnoreCase);

    // Get-OtterDatabaseProvider: SQLite is the only provider.
    static void RequireProvider(string name)
    {
        if (name.Length == 0) throw EmptyArgument("Name");
        if (name.ToLowerInvariant() != "sqlite")
        {
            throw new DatabaseProblem($"I do not know a database provider called \"{name}\". Available providers are: sqlite.");
        }
    }

    // ---- connecting --------------------------------------------------------

    // Connect-OtterDatabase.
    public static OtterThing Connect(object? configValue)
    {
        if (configValue is null) throw NullArgument("ConfigValue");
        // A PowerShell hashtable: keys ignore case, a later one wins.
        var config = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (configValue is OtterThing thing)
        {
            foreach (string property in thing.PropertyNames) config[property] = thing.ReadProperty(property);
        }
        else if (configValue is string text)
        {
            config["provider"] = "sqlite";
            config["connection"] = text;
        }
        else throw new DatabaseProblem("I expected a database configuration object created with 'has'.");

        if (!config.ContainsKey("provider")) throw new DatabaseProblem("Database configuration must include a \"provider\" property (such as \"sqlite\").");
        string providerName = PowerShellText.Of(config["provider"]);
        RequireProvider(providerName);
        OtterDatabaseConnection connection = Open(config);

        var connectionThing = new OtterThing("database connection");
        connectionThing.WriteProperty("provider", providerName);
        connectionThing.WriteProperty("connection", connection.MaskedConnectionString());
        connectionThing.WriteProperty("__connection", connection);
        return connectionThing;
    }

    // `$Config['readonly'] -eq $true`: PowerShell converts true to the left
    // side's type.
    static bool EqualsTrue(object? value) => value switch
    {
        bool b => b,
        double d => d == 1,
        string s => string.Equals(s, "True", StringComparison.OrdinalIgnoreCase),
        OtterList list => list.Any(EqualsTrue),
        _ => false,
    };

    // Open-OtterSqliteConnection.
    static OtterDatabaseConnection Open(Dictionary<string, object?> config)
    {
        if (!config.TryGetValue("connection", out object? connectionValue))
        {
            throw new DatabaseProblem("SQLite database configuration is missing a \"connection\" property specifying the database path or \":memory:\".");
        }
        string connection = PowerShellText.Of(connectionValue);
        if (string.IsNullOrWhiteSpace(connection)) throw new DatabaseProblem("The database connection path cannot be empty.");

        string resolved = connection;
        bool memory = string.Equals(connection, ":memory:", StringComparison.OrdinalIgnoreCase) || connection.StartsWith(":memory:", StringComparison.Ordinal);
        if (!memory)
        {
            if (!Path.IsPathRooted(connection)) resolved = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, connection));
            string? folder = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                throw new DatabaseProblem($"The folder \"{folder}\" for the SQLite database does not exist.");
            }
        }

        int flags = SqliteNative.OpenReadWrite | SqliteNative.OpenCreate;
        if (config.TryGetValue("readonly", out object? readOnly) && EqualsTrue(readOnly)) flags = SqliteNative.OpenReadOnly;

        string path = resolved.Split(';')[0];
        int rc = SqliteNative.Open(path, out nint db, flags);
        if (rc != SqliteNative.Ok)
        {
            string error = db != 0 ? SqliteNative.ErrorMessage(db) ?? "" : $"Error code {rc}";
            if (db != 0) SqliteNative.CloseV2(db);
            throw new DatabaseProblem($"Could not open SQLite database \"{Mask(connection)}\": {error}");
        }
        return new OtterDatabaseConnection("sqlite", connection, db);
    }

    // Disconnect-OtterDatabase, then Close-OtterSqliteConnection.
    public static void Disconnect(object? value)
    {
        if (value is null) throw NullArgument("ConnectionValue");
        OtterDatabaseConnection? connection = value switch
        {
            OtterDatabaseConnection raw => raw,
            OtterThing thing when IsType(thing, "database connection") => thing.ReadProperty("__connection") as OtterDatabaseConnection,
            _ => throw new DatabaseProblem("I can only disconnect a live database connection."),
        };
        if (connection is null || !connection.IsOpen) throw new DatabaseProblem("This database connection is already closed.");
        RequireProvider(connection.ProviderName);
        if (connection.ActiveTransaction is { IsActive: true } open) Rollback(open);
        SqliteNative.CloseV2(connection.Handle);
        connection.IsOpen = false;
        connection.Handle = 0;
    }

    // Get-OtterSqliteConnectionHandle.
    static nint Handle(object? target)
    {
        switch (target)
        {
            case OtterDatabaseTransaction transaction:
                if (!transaction.IsActive) throw new DatabaseProblem("This transaction has already been completed.");
                return Handle(transaction.Connection);
            case OtterDatabaseConnection connection:
                if (!connection.IsOpen) throw new DatabaseProblem("This database connection is closed.");
                return connection.Handle;
            case OtterThing thing when IsType(thing, "database transaction"):
            {
                if (thing.ReadProperty("__transaction") is not OtterDatabaseTransaction { IsActive: true } transaction)
                {
                    throw new DatabaseProblem("This transaction has already been completed.");
                }
                return Handle(transaction.Connection);
            }
            case OtterThing thing when IsType(thing, "database connection"):
            {
                if (thing.ReadProperty("__connection") is not OtterDatabaseConnection { IsOpen: true } connection)
                {
                    throw new DatabaseProblem("This database connection is closed.");
                }
                return connection.Handle;
            }
        }
        throw new DatabaseProblem("Expected an open database connection or active transaction.");
    }

    // ---- statements --------------------------------------------------------

    // Invoke-OtterDatabaseQuery's parameter checks.
    static void RequireTargetAndSql(object? target, string sql)
    {
        if (target is null) throw NullArgument("TargetValue");
        if (sql.Length == 0) throw EmptyArgument("Sql");
    }

    // Bind-OtterSqliteParameters: a name binds to `name`, `@name`, `:name`
    // or `$name`; a parameter the statement does not use is ignored.
    static void Bind(nint statement, IReadOnlyDictionary<string, object?> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int index = 0;
            foreach (string prefix in new[] { "", "@", ":", "$" })
            {
                string candidate = name.StartsWith('@') || name.StartsWith(':') || name.StartsWith('$') ? name : prefix + name;
                index = SqliteNative.ParameterIndex(statement, candidate);
                if (index > 0) break;
            }
            if (index <= 0) continue;
            switch (value)
            {
                case null:
                    SqliteNative.BindNull(statement, index);
                    break;
                case bool b:
                    SqliteNative.BindInt64(statement, index, b ? 1 : 0);
                    break;
                case int i:
                    SqliteNative.BindInt64(statement, index, i);
                    break;
                case double d:
                    if (d == Math.Floor(d) && d >= long.MinValue && d < 9223372036854775808.0) SqliteNative.BindInt64(statement, index, (long)d);
                    else SqliteNative.BindDouble(statement, index, d);
                    break;
                default:
                    SqliteNative.BindText(statement, index, PowerShellText.Of(value));
                    break;
            }
        }
    }

    // Invoke-OtterSqliteQuery: every row as a `database row` thing.
    public static OtterList Query(object? target, string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        RequireTargetAndSql(target, sql);
        nint db = Handle(target);
        if (SqliteNative.Prepare(db, sql, out nint statement) != SqliteNative.Ok)
        {
            throw new DatabaseProblem($"Database query error: {SqliteNative.ErrorMessage(db)}");
        }
        try
        {
            Bind(statement, parameters);
            var rows = new OtterList();
            int rc;
            while ((rc = SqliteNative.Step(statement)) == SqliteNative.Row)
            {
                var row = new OtterThing("database row");
                int columns = SqliteNative.ColumnCount(statement);
                for (int column = 0; column < columns; column++)
                {
                    string name = SqliteNative.ColumnName(statement, column) ?? "";
                    object? value = SqliteNative.ColumnType(statement, column) switch
                    {
                        SqliteNative.Integer => (double)SqliteNative.ColumnInt64(statement, column),
                        SqliteNative.Float => SqliteNative.ColumnDouble(statement, column),
                        SqliteNative.Null => null,
                        // Text, and a blob read as text.
                        _ => SqliteNative.ColumnText(statement, column),
                    };
                    row.WriteProperty(name, value);
                }
                rows.Add(row);
            }
            if (rc != SqliteNative.Done && rc != SqliteNative.Row)
            {
                throw new DatabaseProblem($"Database query execution error: {SqliteNative.ErrorMessage(db)}");
            }
            return rows;
        }
        finally
        {
            if (statement != 0) SqliteNative.Finalize(statement);
        }
    }

    // Invoke-OtterSqliteCommand: one step of the first statement; the
    // result tells the rows changed and the last id inserted.
    public static OtterThing Execute(object? target, string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        RequireTargetAndSql(target, sql);
        return Command(target, sql, parameters);
    }

    static OtterThing Command(object? target, string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        nint db = Handle(target);
        if (SqliteNative.Prepare(db, sql, out nint statement) != SqliteNative.Ok)
        {
            throw new DatabaseProblem($"Database command error: {SqliteNative.ErrorMessage(db)}");
        }
        try
        {
            Bind(statement, parameters);
            int rc = SqliteNative.Step(statement);
            if (rc != SqliteNative.Done && rc != SqliteNative.Row)
            {
                throw new DatabaseProblem($"Database command execution error: {SqliteNative.ErrorMessage(db)}");
            }
            int changes = SqliteNative.Changes(db);
            long lastRowId = SqliteNative.LastInsertRowId(db);
            var result = new OtterThing("database result");
            result.WriteProperty("rows affected", (double)changes);
            result.WriteProperty("last inserted id", lastRowId > 0 ? (double)lastRowId : null);
            return result;
        }
        finally
        {
            if (statement != 0) SqliteNative.Finalize(statement);
        }
    }

    static readonly Dictionary<string, object?> NoParameters = new();

    // ---- transactions ------------------------------------------------------

    // Start-OtterDatabaseTransaction, then Start-OtterSqliteTransaction.
    public static OtterThing BeginTransaction(object? value, int line)
    {
        if (value is null) throw NullArgument("ConnectionValue");
        OtterDatabaseConnection? connection = value switch
        {
            OtterDatabaseConnection raw => raw,
            OtterThing thing when IsType(thing, "database connection") => thing.ReadProperty("__connection") as OtterDatabaseConnection,
            _ => throw new DatabaseProblem("I can only begin a transaction on a live database connection."),
        };
        if (connection is null || !connection.IsOpen) throw new DatabaseProblem("Cannot begin a transaction on a closed database connection.");
        RequireProvider(connection.ProviderName);
        if (connection.ActiveTransaction is { IsActive: true }) throw new DatabaseProblem("A transaction is already active on this database connection.");
        Command(connection, "BEGIN IMMEDIATE TRANSACTION", NoParameters);
        var transaction = new OtterDatabaseTransaction(connection, line);
        connection.ActiveTransaction = transaction;

        var transactionThing = new OtterThing("database transaction");
        transactionThing.WriteProperty("provider", connection.ProviderName);
        transactionThing.WriteProperty("__transaction", transaction);
        return transactionThing;
    }

    static OtterDatabaseTransaction RequireTransaction(object? value, string verb)
    {
        if (value is null) throw NullArgument("TransactionValue");
        OtterDatabaseTransaction? transaction = value switch
        {
            OtterDatabaseTransaction raw => raw,
            OtterThing thing when IsType(thing, "database transaction") => thing.ReadProperty("__transaction") as OtterDatabaseTransaction,
            _ => throw new DatabaseProblem($"I can only {verb} a real database transaction."),
        };
        if (transaction is null || !transaction.IsActive) throw new DatabaseProblem("This transaction has already been completed.");
        RequireProvider(transaction.Connection.ProviderName);
        return transaction;
    }

    // Complete-OtterDatabaseTransaction / Complete-OtterSqliteTransaction.
    public static void Commit(object? value) => Finish(RequireTransaction(value, "commit"), "COMMIT TRANSACTION", "commit");

    // Undo-OtterDatabaseTransaction / Undo-OtterSqliteTransaction.
    public static void Rollback(object? value)
    {
        if (value is OtterDatabaseTransaction raw && raw.IsActive) Finish(raw, "ROLLBACK TRANSACTION", "roll back");
        else Finish(RequireTransaction(value, "roll back"), "ROLLBACK TRANSACTION", "roll back");
    }

    static void Finish(OtterDatabaseTransaction transaction, string sql, string verb)
    {
        if (!transaction.IsActive) throw new DatabaseProblem("This transaction has already been completed.");
        OtterDatabaseConnection connection = transaction.Connection;
        if (!connection.IsOpen) throw new DatabaseProblem($"Cannot {verb} transaction because the database connection is closed.");
        Command(connection, sql, NoParameters);
        transaction.IsActive = false;
        connection.ActiveTransaction = null;
    }

    // ---- schema (D98) ------------------------------------------------------

    static OtterDatabaseConnection RequireSchemaConnection(object? value, string after)
    {
        if (value is null) throw NullArgument("TargetValue");
        OtterDatabaseConnection? connection = value switch
        {
            OtterDatabaseConnection raw => raw,
            OtterThing thing when IsType(thing, "database connection") => thing.ReadProperty("__connection") as OtterDatabaseConnection,
            _ => throw new DatabaseProblem($"I expected a database connection after '{after}'."),
        };
        if (connection is null || !connection.IsOpen) throw new DatabaseProblem("This database connection is closed.");
        RequireProvider(connection.ProviderName);
        return connection;
    }

    // Get-OtterDatabaseTables / Get-OtterSqliteTables.
    public static OtterList Tables(object? value)
    {
        OtterDatabaseConnection connection = RequireSchemaConnection(value, "from");
        if (!connection.IsOpen) throw new DatabaseProblem("This database connection is closed.");
        OtterList rows = Query(connection, "SELECT name, type FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%' ORDER BY name", NoParameters);
        var tables = new OtterList();
        foreach (OtterThing row in rows.Cast<OtterThing>())
        {
            var table = new OtterThing("database table");
            table.WriteProperty("name", PowerShellText.Of(row.ReadProperty("name")));
            table.WriteProperty("schema", "main");
            string type = PowerShellText.Of(row.ReadProperty("type"));
            table.WriteProperty("kind", type.ToLowerInvariant() == "view" ? "view" : "table");
            tables.Add(table);
        }
        return tables;
    }

    static readonly (Regex Pattern, string Type)[] PortableTypes =
    {
        (new Regex(@"\b(INT|INTEGER|TINYINT|SMALLINT|MEDIUMINT|BIGINT|INT2|INT8|NUMERIC|DECIMAL|REAL|DOUBLE|FLOAT)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "number"),
        (new Regex(@"\b(CHAR|CHARACTER|VARCHAR|VARYING CHARACTER|NCHAR|NATIVE CHARACTER|NVARCHAR|TEXT|CLOB|STRING)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "text"),
        (new Regex(@"\b(BOOL|BOOLEAN)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "boolean"),
        (new Regex(@"\b(BLOB|BINARY|VARBINARY)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "bytes"),
        (new Regex(@"\b(DATE|DATETIME|TIME|TIMESTAMP)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "time"),
    };

    // Get-OtterPortableColumnType.
    static string PortableColumnType(string declared)
    {
        if (string.IsNullOrWhiteSpace(declared)) return "any";
        string type = declared.Trim().ToUpperInvariant();
        foreach (var (pattern, portable) in PortableTypes)
        {
            if (pattern.IsMatch(type)) return portable;
        }
        return "any";
    }

    // Get-OtterDatabaseColumns / Get-OtterSqliteColumns.
    public static OtterList Columns(object? value, object? tableOrName)
    {
        if (value is null) throw NullArgument("TargetValue");
        if (tableOrName is null) throw NullArgument("TableOrName");
        OtterDatabaseConnection connection = RequireSchemaConnection(value, "in");
        if (!connection.IsOpen) throw new DatabaseProblem("This database connection is closed.");

        string tableName;
        if (tableOrName is OtterThing table)
        {
            if (!table.HasProperty("name")) throw new DatabaseProblem("I expected a database table object with a 'name' property, or a table name as text.");
            tableName = PowerShellText.Of(table.ReadProperty("name"));
        }
        else if (tableOrName is string text) tableName = text;
        else throw new DatabaseProblem("I expected a table name as text, or a database table object.");
        if (string.IsNullOrWhiteSpace(tableName)) throw new DatabaseProblem("Table name cannot be empty.");

        var check = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["tableName"] = tableName };
        OtterList existing = Query(connection, "SELECT name FROM sqlite_master WHERE type IN ('table', 'view') AND name = @tableName", check);
        if (existing.Count == 0) throw new DatabaseProblem($"I could not find a table or view called \"{tableName}\" in the database.");

        string escaped = tableName.Replace("\"", "\"\"");
        OtterList raw = Query(connection, $"PRAGMA table_info(\"{escaped}\")", NoParameters);
        var columns = new OtterList();
        foreach (OtterThing column in raw.Cast<OtterThing>())
        {
            string declared = column.HasProperty("type") && column.ReadProperty("type") is not null ? PowerShellText.Of(column.ReadProperty("type")) : "";
            double notNull = column.HasProperty("notnull") ? PowerShellText.ToInt(column.ReadProperty("notnull")) : 0;
            object? defaultValue = column.HasProperty("dflt_value") ? column.ReadProperty("dflt_value") : null;
            double primaryKey = column.HasProperty("pk") ? PowerShellText.ToInt(column.ReadProperty("pk")) : 0;

            var result = new OtterThing("database column");
            result.WriteProperty("name", PowerShellText.Of(column.ReadProperty("name")));
            result.WriteProperty("type", PortableColumnType(declared));
            result.WriteProperty("database type", declared);
            result.WriteProperty("nullable", notNull == 0);
            result.WriteProperty("primary key", primaryKey > 0);
            result.WriteProperty("primary key position", primaryKey > 0 ? primaryKey : null);
            result.WriteProperty("default expression", defaultValue is not null ? PowerShellText.Of(defaultValue) : null);
            columns.Add(result);
        }
        return columns;
    }
}

// PowerShell's conversions of an Otter value, where otterPS casts one.
public static class PowerShellText
{
    // [string]$value
    public static string Of(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "True" : "False",
        double d => Values.PowerShellText(d),
        int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        OtterList list => string.Join(" ", list.Select(Of)),
        OtterThing => "OtterObject",
        OtterBytes => "OtterBytes",
        _ => value.ToString() ?? "",
    };

    // [int]$value: the nearest whole number, halves to even. A value that
    // is not a number fails as PowerShell's conversion does.
    public static int ToInt(object? value)
    {
        switch (value)
        {
            case null: return 0;
            case bool b: return b ? 1 : 0;
            case int i: return i;
            case double d: return FromDouble(d, Values.PowerShellText(d));
            case string s:
            {
                if (s.Trim().Length == 0) return 0;
                if (int.TryParse(s.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int whole)) return whole;
                if (double.TryParse(s, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                        System.Globalization.CultureInfo.InvariantCulture, out double number))
                {
                    return FromDouble(number, s);
                }
                throw new InternalProblem($"Cannot convert value \"{s}\" to type \"System.Int32\". Error: \"Input string was not in a correct format.\"");
            }
            default:
                throw new InternalProblem($"Cannot convert the \"{Of(value)}\" value of type \"{TypeName(value)}\" to type \"System.Int32\".");
        }
    }

    static int FromDouble(double number, string shown)
    {
        double rounded = Math.Round(number, MidpointRounding.ToEven);
        if (double.IsNaN(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
        {
            throw new InternalProblem($"Cannot convert value \"{shown}\" to type \"System.Int32\". Error: \"Value was either too large or too small for an Int32.\"");
        }
        return (int)rounded;
    }

    static string TypeName(object value) => value switch
    {
        OtterList => "System.Collections.Generic.List`1[[System.Object, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]",
        OtterThing => "OtterObject",
        OtterDate => "OtterDate",
        OtterBytes => "OtterBytes",
        _ => value.GetType().Name,
    };
}
