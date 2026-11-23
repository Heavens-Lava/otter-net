using Otter.Language;

namespace Otter.Runtime;

// Thrown when the browser playground exceeds its step, time, or output budget.
// Excluded from `try ... otherwise` so user code cannot swallow safety limits.
public sealed class PlaygroundAbortException(string message, int line, string code) : Exception(message)
{
    public int Line { get; } = line;
    public string Code { get; } = code;
}

public sealed partial class Interpreter
{
    // When true, blocks host filesystem, process, environment, database,
    // desktop UI, OS integration, and network statements before any host API
    // or P/Invoke is touched. Used by the browser WebAssembly playground.
    public bool SandboxMode { get; init; }

    void GuardSandboxStatement(Stmt statement)
    {
        if (!SandboxMode) return;

        string? category = statement switch
        {
            ReadFileStmt or WriteFileStmt or AppendFileStmt or TransferStmt or
            DeleteFileStmt or DeleteFolderStmt or CreateFolderStmt or GetEntriesStmt or
            ReadCsvStmt or WriteCsvStmt or XmlWriteFileStmt or WriteBytesFileStmt or
            ReadJsonStmt or SetCurrentDirectoryStmt or GetSystemFolderStmt
                => "File and folder access",

            RunCommandStmt or GetEnvironmentVariableStmt or SetEnvironmentVariableStmt
                => "Shell commands and environment variables",

            WaitDelayStmt or ChooseFromListStmt or SetCursorStmt
                => "Blocking console delays and interactive terminal prompts",

            ConnectDbStmt or DisconnectDbStmt or DbQueryStmt or DbExecuteStmt or
            BeginTransactionStmt or CommitTransactionStmt or RollbackTransactionStmt or
            GetTablesStmt or GetColumnsStmt or QueryStmt or QueryAggregateStmt
                => "Database connections",

            CreateUiResourceStmt
                => "Desktop windows (WPF)",

            UseModuleStmt
                => "External file imports ('use')",

            // Whitelist pure in-memory Otter statements; anything else falls into
            // Interpreter.System.cs or Interpreter.Network.cs (OS/process/network).
            SayStmt or AssignStmt or ObjectDefStmt or TypeDefStmt or AskStmt or
            MathIntoStmt or AddToStmt or RemoveFromStmt or IfStmt or WhileStmt or
            RepeatStmt or CountStmt or ForEachStmt or ListDefStmt or FunctionDefStmt or
            CallStmt or TryStmt or FailStmt or SortStmt or ReverseStmt or ReplaceStmt or
            SplitStmt or JoinStmt or FindStmt or GetKeyStmt or SetKeyStmt or
            DateDifferenceStmt or DateAdjustStmt or FormatDateStmt or DiagnosticStmt or
            ConvertCsvStmt or GenerateKeyStmt or CryptoCipherStmt or HashPasswordStmt or
            HashTextStmt or CipherTextStmt or ShowProgressStmt or StartTimerStmt or
            RandomNumberStmt or RandomItemStmt or SetRandomSeedStmt or
            XmlAddElementStmt or XmlRemoveElementStmt or XmlRemoveAttributeStmt or
            XmlSetTextStmt or XmlSetAttributeStmt or ConvertJsonStmt or ReturnStmt or
            StateDefStmt or DeriveDefStmt or WatchStmt
                => null,

            _ => "Host system and network operations"
        };

        if (category != null)
        {
            throw RuntimeError(
                $"{category} are disabled in the browser playground sandbox.",
                statement.Line,
                "Run this program locally with the Otter CLI to use files, networking, databases, or OS features."
            );
        }
    }

    void GuardSandboxExpression(Expr expression)
    {
        if (!SandboxMode) return;

        string? category = expression switch
        {
            BytesFromFileExpr or FileExistsExpr or FileLockedExpr
                => "File and folder access",
            XmlFromExpr { Source: XmlSourceKind.File }
                => "File and folder access",
            _ => null
        };

        if (category != null)
        {
            throw RuntimeError(
                $"{category} are disabled in the browser playground sandbox.",
                expression.Line,
                "Run this program locally with the Otter CLI to access files on disk."
            );
        }
    }
}
