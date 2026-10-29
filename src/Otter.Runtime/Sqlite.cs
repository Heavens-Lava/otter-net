using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Otter.Runtime;

// The operating system's own SQLite (D97): winsqlite3.dll on Windows (the
// library otterPS uses), libsqlite3.dylib on macOS, libsqlite3.so.0 on Linux.
// No package is shipped; when the library cannot be loaded the program gets
// an Otter error (SqliteMissing) instead of a crash.
static partial class SqliteNative
{
    const string Library = "otter-sqlite3";

    public const int Ok = 0;
    public const int Row = 100;
    public const int Done = 101;
    public const int Integer = 1;
    public const int Float = 2;
    public const int Text = 3;
    public const int Blob = 4;
    public const int Null = 5;
    public const int OpenReadOnly = 0x00000001;
    public const int OpenReadWrite = 0x00000002;
    public const int OpenCreate = 0x00000004;
    static readonly nint Transient = -1;

    static readonly string[] Candidates =
        OperatingSystem.IsWindows() ? new[] { "winsqlite3.dll", "sqlite3.dll" }
        : OperatingSystem.IsMacOS() ? new[] { "/usr/lib/libsqlite3.dylib", "libsqlite3.dylib" }
        : new[] { "libsqlite3.so.0", "libsqlite3.so" };

    // The name a program sees when SQLite is missing.
    public static string LibraryName => Candidates[0];

    static SqliteNative()
    {
        try { NativeLibrary.SetDllImportResolver(typeof(SqliteNative).Assembly, Resolve); }
        catch (InvalidOperationException) { /* another resolver is registered for this assembly */ }
    }

    static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library) return 0;
        foreach (string candidate in Candidates)
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out nint handle)) return handle;
        }
        return 0;
    }

    [LibraryImport(Library, EntryPoint = "sqlite3_open_v2")]
    private static partial int OpenV2(byte[] filename, out nint db, int flags, nint vfs);

    [LibraryImport(Library, EntryPoint = "sqlite3_close_v2")]
    public static partial int CloseV2(nint db);

    [LibraryImport(Library, EntryPoint = "sqlite3_prepare_v2")]
    private static partial int PrepareV2(nint db, byte[] sql, int bytes, out nint statement, nint tail);

    // The same function with the text in memory we own, so `tail` (where
    // the first statement ends) stays valid after the call.
    [LibraryImport(Library, EntryPoint = "sqlite3_prepare_v2")]
    private static partial int PrepareV2At(nint db, nint sql, int bytes, out nint statement, out nint tail);

    [LibraryImport(Library, EntryPoint = "sqlite3_busy_timeout")]
    public static partial int BusyTimeout(nint db, int milliseconds);

    [LibraryImport(Library, EntryPoint = "sqlite3_step")]
    public static partial int Step(nint statement);

    [LibraryImport(Library, EntryPoint = "sqlite3_column_count")]
    public static partial int ColumnCount(nint statement);

    [LibraryImport(Library, EntryPoint = "sqlite3_column_name")]
    private static partial nint ColumnNamePointer(nint statement, int column);

    [LibraryImport(Library, EntryPoint = "sqlite3_column_type")]
    public static partial int ColumnType(nint statement, int column);

    [LibraryImport(Library, EntryPoint = "sqlite3_column_int64")]
    public static partial long ColumnInt64(nint statement, int column);

    [LibraryImport(Library, EntryPoint = "sqlite3_column_double")]
    public static partial double ColumnDouble(nint statement, int column);

    [LibraryImport(Library, EntryPoint = "sqlite3_column_text")]
    private static partial nint ColumnTextPointer(nint statement, int column);

    [LibraryImport(Library, EntryPoint = "sqlite3_column_blob")]
    private static partial nint ColumnBlobPointer(nint statement, int column);

    [LibraryImport(Library, EntryPoint = "sqlite3_column_bytes")]
    private static partial int ColumnBytes(nint statement, int column);

    [LibraryImport(Library, EntryPoint = "sqlite3_bind_parameter_index")]
    private static partial int BindParameterIndex(nint statement, byte[] name);

    [LibraryImport(Library, EntryPoint = "sqlite3_bind_null")]
    public static partial int BindNull(nint statement, int index);

    [LibraryImport(Library, EntryPoint = "sqlite3_bind_int64")]
    public static partial int BindInt64(nint statement, int index, long value);

    [LibraryImport(Library, EntryPoint = "sqlite3_bind_double")]
    public static partial int BindDouble(nint statement, int index, double value);

    [LibraryImport(Library, EntryPoint = "sqlite3_bind_text")]
    private static partial int BindTextBytes(nint statement, int index, byte[] value, int length, nint destructor);

    [LibraryImport(Library, EntryPoint = "sqlite3_bind_blob")]
    private static partial int BindBlobBytes(nint statement, int index, byte[] value, int length, nint destructor);

    [LibraryImport(Library, EntryPoint = "sqlite3_bind_zeroblob")]
    private static partial int BindZeroBlob(nint statement, int index, int length);

    [LibraryImport(Library, EntryPoint = "sqlite3_changes")]
    public static partial int Changes(nint db);

    [LibraryImport(Library, EntryPoint = "sqlite3_last_insert_rowid")]
    public static partial long LastInsertRowId(nint db);

    [LibraryImport(Library, EntryPoint = "sqlite3_finalize")]
    public static partial int Finalize(nint statement);

    [LibraryImport(Library, EntryPoint = "sqlite3_errmsg")]
    private static partial nint ErrorMessagePointer(nint db);

    // As otterPS's StringToUtf8Bytes: UTF-8 with a terminating zero.
    static byte[] Utf8(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Array.Resize(ref bytes, bytes.Length + 1);
        return bytes;
    }

    // As otterPS's Utf8PtrToString: up to the first zero byte.
    static string? FromUtf8(nint pointer) => pointer == 0 ? null : Marshal.PtrToStringUTF8(pointer);

    public static int Open(string path, out nint db, int flags) => OpenV2(Utf8(path), out db, flags, 0);
    public static int Prepare(nint db, string sql, out nint statement) => PrepareV2(db, Utf8(sql), -1, out statement, 0);
    // S-7: prepares the first statement of `sql`, and gives in `rest` the
    // text from the start of a second statement, or null when only spaces,
    // comments and semicolons follow (sqlite3_prepare_v2 compiles only the
    // first statement and would silently drop the others).
    public static int PrepareFirst(nint db, string sql, out nint statement, out string? rest)
    {
        rest = null;
        byte[] bytes = Utf8(sql);
        nint buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            int rc = PrepareV2At(db, buffer, bytes.Length, out statement, out nint tail);
            if (rc != Ok || statement == 0) return rc;
            nint end = buffer + (bytes.Length - 1);   // the terminating zero
            while (tail != 0 && tail < end)
            {
                int next = PrepareV2At(db, tail, (int)(end - tail), out nint other, out nint after);
                if (other != 0) Finalize(other);
                if (next != Ok || other != 0)
                {
                    rest = Marshal.PtrToStringUTF8(tail, (int)(end - tail));
                    break;
                }
                if (after == 0 || after <= tail) break;
                tail = after;
            }
            return Ok;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static string? ColumnName(nint statement, int column) => FromUtf8(ColumnNamePointer(statement, column));
    public static string? ColumnText(nint statement, int column) => FromUtf8(ColumnTextPointer(statement, column));
    public static int ParameterIndex(nint statement, string name) => BindParameterIndex(statement, Utf8(name));
    public static int BindText(nint statement, int index, string value) => BindTextBytes(statement, index, Utf8(value), -1, Transient);
    // A BLOB's bytes (an empty BLOB has no pointer).
    public static byte[] ColumnBlob(nint statement, int column)
    {
        nint pointer = ColumnBlobPointer(statement, column);
        int length = ColumnBytes(statement, column);
        byte[] value = new byte[pointer == 0 ? 0 : length];
        if (value.Length > 0) Marshal.Copy(pointer, value, 0, value.Length);
        return value;
    }

    public static int BindBlob(nint statement, int index, byte[] value) =>
        value.Length == 0 ? BindZeroBlob(statement, index, 0) : BindBlobBytes(statement, index, value, value.Length, Transient);
    public static string? ErrorMessage(nint db) => FromUtf8(ErrorMessagePointer(db));
}
