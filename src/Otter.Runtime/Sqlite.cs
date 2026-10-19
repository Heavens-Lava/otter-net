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
    public static string? ColumnName(nint statement, int column) => FromUtf8(ColumnNamePointer(statement, column));
    public static string? ColumnText(nint statement, int column) => FromUtf8(ColumnTextPointer(statement, column));
    public static int ParameterIndex(nint statement, string name) => BindParameterIndex(statement, Utf8(name));
    public static int BindText(nint statement, int index, string value) => BindTextBytes(statement, index, Utf8(value), -1, Transient);
    public static string? ErrorMessage(nint db) => FromUtf8(ErrorMessagePointer(db));
}
