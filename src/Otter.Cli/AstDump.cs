using System.Collections;
using System.Globalization;
using Otter.Language;
using Otter.Runtime;

namespace Otter.Cli;

// -DebugAst: otter.ps1's Show-OtterAst, which walks otterPS's own syntax
// tree objects. otter-net's tree has the same classes and properties as
// otterPS's (Otter.Contract.psm1) with a few exceptions mapped below; the
// table of what Show-OtterAst reads from each class is generated
// (AstDumpTable.g.cs, tools/New-AstDumpTable.ps1).
//
//   function Show-OtterAst($Node, $Depth) {
//       a node: "<pad><Kind>[ <first of Name, Target, VariableName,
//               ResultTarget that holds something>][ = <Value> for a
//               literal]", then each other property (not Kind, Line, Name,
//               Value, Target, VariableName, ResultTarget) one level deeper;
//       an IfBranch: Write-Host "$pad" + 'branch' (three arguments, so
//               "<pad> + branch"), its condition and body one level deeper;
//       an array: each item at the same depth; anything else: nothing.
//   }
//
// The lines are built before anything is printed. Every node class otter-net
// parses has a table entry (through PsClass) and every property the entry
// names (through Read); the dumps of all of tests/compat, tests/web and
// otterPS 1.0.1's programs, and tests/cli/run.psd1's -DebugAst cases, match
// otterPS's line for line. The OtterNetGap throws below only guard a
// node class added later without an entry.
internal static partial class AstDump
{
    // Show-OtterAst -Node $program -Depth 1.
    public static List<string> Lines(ProgramNode program)
    {
        var lines = new List<string>();
        lines.Add("  Program");
        foreach (Stmt statement in program.Statements) Show(statement, 2, lines);
        return lines;
    }

    static void Show(object? value, int depth, List<string> lines)
    {
        switch (value)
        {
            case null:
                return;
            case Node node:
                ShowNode(node, depth, lines);
                return;
            case IfBranch branch:
                lines.Add(new string(' ', 2 * depth) + " + branch");
                Show(branch.Condition, depth + 1, lines);
                foreach (Stmt statement in branch.Body) Show(statement, depth + 1, lines);
                return;
            case string:
                return;
            case IEnumerable items:
                foreach (object? item in items) Show(item, depth, lines);
                return;
        }
    }

    static void ShowNode(Node node, int depth, List<string> lines)
    {
        string psClass = PsClass(node);
        if (!Table.TryGetValue(psClass, out var entry))
        {
            throw new OtterNetGap($"-DebugAst does not know how otterPS shows a {node.GetType().Name}");
        }
        string label = entry.Kind;
        foreach (string property in entry.Labels)
        {
            object? labelValue = Read(node, property);
            if (!IsTrue(labelValue)) continue;
            label += " " + Text(labelValue);
            break;
        }
        if (node is LiteralExpr literal) label += " = " + Text(literal.Value);
        lines.Add(new string(' ', 2 * depth) + label);
        foreach (string property in entry.Children) Show(Read(node, property), depth + 1, lines);
    }

    // PowerShell's truth of a property value.
    static bool IsTrue(object? value) => value switch
    {
        null => false,
        string text => text.Length > 0,
        bool flag => flag,
        double number => number != 0,
        int number => number != 0,
        _ => true,
    };

    // "$value" in a PowerShell string: a node is its ToString(), "Kind@L<line>".
    static string Text(object? value) => value switch
    {
        null => "",
        string text => text,
        bool flag => flag ? "True" : "False",
        double number => Values.PowerShellText(number),
        int number => number.ToString(CultureInfo.InvariantCulture),
        Node node => $"{KindOf(node)}@L{node.Line}",
        _ => value.ToString() ?? "",
    };

    static string KindOf(Node node) =>
        Table.TryGetValue(PsClass(node), out var entry) ? entry.Kind
            : throw new OtterNetGap($"-DebugAst does not know how otterPS shows a {node.GetType().Name}");

    // otterPS's class for an otter-net node: the same name, except where
    // otter-net has one class for several of otterPS's.
    static string PsClass(Node node) => node switch
    {
        AwaitStmt => "AwaitExpr",
        CipherTextStmt cipher => cipher.Decrypt ? "DecryptTextStmt" : "EncryptTextStmt",
        ConvertCsvStmt csv => csv.ToCsv ? "ConvertToCsvStmt" : "ConvertFromCsvStmt",
        ConvertJsonStmt json => json.ToJson ? "ConvertToJsonStmt" : "ConvertFromJsonStmt",
        GetEntriesStmt entries => entries.Folders ? "GetFoldersStmt" : "GetFilesStmt",
        HttpSendStmt send => send.Method == "PUT" ? "HttpPutStmt" : "HttpPostStmt",
        RunCommandStmt or RunProgramStmt => "RunStmt",
        SetCursorStmt => "SetCursorPositionStmt",
        ChooseFileStmt choose => choose.Dialog switch
        {
            FileDialogKind.Folder => "ChooseFolderStmt",
            FileDialogKind.SaveFile => "ChooseSaveFileStmt",
            _ => "ChooseFileStmt",
        },
        TransferStmt transfer => transfer.Action switch
        {
            FileAction.CopyFile => "CopyFileStmt",
            FileAction.MoveFile => "MoveFileStmt",
            FileAction.CopyFolder => "CopyFolderStmt",
            FileAction.MoveFolder => "MoveFolderStmt",
            FileAction.ZipFolder => "ZipFolderStmt",
            _ => "UnzipFileStmt",
        },
        NetworkEventStmt => "WebSocketEventStmt",
        _ => node.GetType().Name,
    };

    // An otterPS property of an otter-net node.
    static object? Read(Node node, string psProperty)
    {
        string name = (node, psProperty) switch
        {
            (CipherTextStmt, "CipherText") => "Text",
            (TransferStmt, "SourceFolder") => "Source",
            (TransferStmt, "ArchivePath") => ((TransferStmt)node).Action == FileAction.ZipFolder ? "Destination" : "Source",
            (TransferStmt, "DestinationFolder") => "Destination",
            (SayStmt, "ColorExpr") => "Color",
            (KillProcessStmt or SetProcessPriorityStmt or WaitForProcessStmt, "ProcessExpr") => "Process",
            (StartServerStmt, "Server") => "Target",
            (TextMatchExpr, "Subject") => "Text",
            _ => psProperty,
        };
        var property = node.GetType().GetProperty(name)
            ?? throw new OtterNetGap($"-DebugAst: otter-net's {node.GetType().Name} has no {name}");
        return property.GetValue(node);
    }
}
