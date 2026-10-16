namespace Otter.Language;

// The syntax tree. One class per otterPS node kind, named as in otterPS's
// contract, carrying the source line every runtime error quotes. The
// prototype has the core language; each further kind arrives with the
// feature that needs it.

public abstract class Node
{
    protected Node(int line) => Line = line;
    public int Line { get; }
}

public abstract class Expr : Node { protected Expr(int line) : base(line) { } }
public abstract class Stmt : Node { protected Stmt(int line) : base(line) { } }

// ---- expressions -----------------------------------------------------------

// A number (double), text (string), true/false (bool) or gone (null).
public sealed class LiteralExpr(object? value, int line) : Expr(line) { public object? Value { get; } = value; }

public sealed class VariableExpr(string name, int line) : Expr(line) { public string Name { get; } = name; }

public enum MathOp { Add, Subtract, Multiply, Divide, Percent, Power }

// Arithmetic has no precedence: a chain is built strictly left to right.
public sealed class MathExpr(Expr left, MathOp op, Expr right, int line) : Expr(line)
{
    public Expr Left { get; } = left;
    public MathOp Op { get; } = op;
    public Expr Right { get; } = right;
}

public enum CompareOp { Equal, NotEqual, AtLeast, AtMost, GreaterThan, LessThan }

public sealed class ComparisonExpr(Expr left, CompareOp op, Expr right, int line) : Expr(line)
{
    public Expr Left { get; } = left;
    public CompareOp Op { get; } = op;
    public Expr Right { get; } = right;
}

public enum LogicalOp { And, Or }

public sealed class LogicalExpr(Expr left, LogicalOp op, Expr right, int line) : Expr(line)
{
    public Expr Left { get; } = left;
    public LogicalOp Op { get; } = op;
    public Expr Right { get; } = right;
}

public sealed class NotExpr(Expr operand, int line) : Expr(line) { public Expr Operand { get; } = operand; }

public sealed class ContainsExpr(Expr collection, Expr item, int line) : Expr(line)
{
    public Expr Collection { get; } = collection;
    public Expr Item { get; } = item;
}

public enum TextMatch { StartsWith, EndsWith }

public sealed class TextMatchExpr(Expr text, TextMatch match, Expr part, int line) : Expr(line)
{
    public Expr Text { get; } = text;
    public TextMatch Match { get; } = match;
    public Expr Part { get; } = part;
}

public enum OfOperation
{
    Length, Uppercase, Lowercase, First, Last,
    AbsoluteValue, SquareRoot, Round, RoundUp, RoundDown,
}

public sealed class OfOperationExpr(OfOperation operation, Expr subject, int line) : Expr(line)
{
    public OfOperation Operation { get; } = operation;
    public Expr Subject { get; } = subject;
}

public enum TimeUnit { Year, Month, Day, Hour, Minute, Second }

public enum ClockKind { Today, Now }

// today / now (D32)
public sealed class ClockExpr(ClockKind clock, int line) : Expr(line) { public ClockKind Clock { get; } = clock; }

// date from "2024-01-15" [using "yyyy-MM-dd"] (D101)
public sealed class DateFromTextExpr(Expr source, Expr? format, int line) : Expr(line)
{
    public Expr Source { get; } = source;
    public Expr? Format { get; } = format;
}

// days between a and b (D42)
public sealed class DateDifferenceExpr(TimeUnit unit, Expr start, Expr end, int line) : Expr(line)
{
    public TimeUnit Unit { get; } = unit;
    public Expr Start { get; } = start;
    public Expr End { get; } = end;
}

// name of person / city of address of user
public sealed class PropertyAccessExpr(string property, Expr target, int line) : Expr(line)
{
    public string Property { get; } = property;
    public Expr Target { get; } = target;
}

public sealed class CallExpr(string name, IReadOnlyList<Expr> arguments, int line) : Expr(line)
{
    public string Name { get; } = name;
    public IReadOnlyList<Expr> Arguments { get; } = arguments;
}

// ---- statements ------------------------------------------------------------

public sealed class SayStmt(IReadOnlyList<Expr> parts, int line) : Stmt(line) { public IReadOnlyList<Expr> Parts { get; } = parts; }

// name is "Jeff" (target is a VariableExpr) / age of person is 30 (a PropertyAccessExpr).
public sealed class AssignStmt(Expr target, Expr value, int line) : Stmt(line)
{
    public Expr Target { get; } = target;
    public Expr Value { get; } = value;
}

// person has ... / jeff is a Person with ... / x is a thing (block)
public sealed class ObjectDefStmt(string name, string typeName, IReadOnlyList<AssignStmt> properties, int line) : Stmt(line)
{
    public string Name { get; } = name;
    public string TypeName { get; } = typeName;
    public IReadOnlyList<AssignStmt> Properties { get; } = properties;
}

// a Person has
//     name
// .
public sealed class TypeDefStmt(string typeName, IReadOnlyList<string> fieldNames, int line) : Stmt(line)
{
    public string TypeName { get; } = typeName;
    public IReadOnlyList<string> FieldNames { get; } = fieldNames;
}

public sealed class AskStmt(Expr prompt, string name, int line) : Stmt(line)
{
    public Expr Prompt { get; } = prompt;
    public string Name { get; } = name;
}

public sealed class MathIntoStmt(Expr expression, string target, int line) : Stmt(line)
{
    public Expr Expression { get; } = expression;
    public string Target { get; } = target;
}

public sealed class AddToStmt(Expr amount, string target, int line) : Stmt(line)
{
    public Expr Amount { get; } = amount;
    public string Target { get; } = target;
}

public sealed class RemoveFromStmt(Expr amount, string target, int line) : Stmt(line)
{
    public Expr Amount { get; } = amount;
    public string Target { get; } = target;
}

public sealed class IfBranch(Expr condition, IReadOnlyList<Stmt> body)
{
    public Expr Condition { get; } = condition;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

public sealed class IfStmt(IReadOnlyList<IfBranch> branches, IReadOnlyList<Stmt>? elseBody, int line) : Stmt(line)
{
    public IReadOnlyList<IfBranch> Branches { get; } = branches;
    public IReadOnlyList<Stmt>? ElseBody { get; } = elseBody;
}

public sealed class WhileStmt(Expr condition, IReadOnlyList<Stmt> body, int line) : Stmt(line)
{
    public Expr Condition { get; } = condition;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

public sealed class RepeatStmt(Expr count, IReadOnlyList<Stmt> body, int line) : Stmt(line)
{
    public Expr Count { get; } = count;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

public sealed class CountStmt(string variableName, Expr from, Expr to, IReadOnlyList<Stmt> body, int line) : Stmt(line)
{
    public string VariableName { get; } = variableName;
    public Expr From { get; } = from;
    public Expr To { get; } = to;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

public sealed class ForEachStmt(string variableName, Expr collection, IReadOnlyList<Stmt> body, int line) : Stmt(line)
{
    public string VariableName { get; } = variableName;
    public Expr Collection { get; } = collection;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

public sealed class ListDefStmt(string name, IReadOnlyList<Expr> items, int line) : Stmt(line)
{
    public string Name { get; } = name;
    public IReadOnlyList<Expr> Items { get; } = items;
}

public sealed class FunctionDefStmt(string name, IReadOnlyList<string> parameters, IReadOnlyList<Stmt> body, int line) : Stmt(line)
{
    public string Name { get; } = name;
    public IReadOnlyList<string> Parameters { get; } = parameters;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

public sealed class CallStmt(CallExpr call, string? resultTarget, int line) : Stmt(line)
{
    public CallExpr Call { get; } = call;
    public string? ResultTarget { get; } = resultTarget;
}

// try / otherwise [into reason] (D23, D68)
public sealed class TryStmt(IReadOnlyList<Stmt> body, IReadOnlyList<Stmt>? otherwiseBody, string? errorTarget, int line) : Stmt(line)
{
    public IReadOnlyList<Stmt> Body { get; } = body;
    public IReadOnlyList<Stmt>? OtherwiseBody { get; } = otherwiseBody;
    public string? ErrorTarget { get; } = errorTarget;
}

// fail with "message" (D68)
public sealed class FailStmt(Expr message, int line) : Stmt(line) { public Expr Message { get; } = message; }

// ---- collections and text (D25, D26, D27) ----

public sealed class SortStmt(string target, int line) : Stmt(line) { public string Target { get; } = target; }

public sealed class ReverseStmt(string target, int line) : Stmt(line) { public string Target { get; } = target; }

// replace "Jeff" with "Jeffrey" in name [into result]
public sealed class ReplaceStmt(Expr find, Expr replacement, string target, string? resultTarget, int line) : Stmt(line)
{
    public Expr Find { get; } = find;
    public Expr Replacement { get; } = replacement;
    public string Target { get; } = target;
    public string? ResultTarget { get; } = resultTarget;
}

// split sentence by " " into words
public sealed class SplitStmt(Expr subject, Expr separator, string target, int line) : Stmt(line)
{
    public Expr Subject { get; } = subject;
    public Expr Separator { get; } = separator;
    public string Target { get; } = target;
}

// join words with ", " into text
public sealed class JoinStmt(Expr subject, Expr separator, string target, int line) : Stmt(line)
{
    public Expr Subject { get; } = subject;
    public Expr Separator { get; } = separator;
    public string Target { get; } = target;
}

// find game in games where game is "Mario" into result
public sealed class FindStmt(string itemName, Expr collection, Expr condition, string target, int line) : Stmt(line)
{
    public string ItemName { get; } = itemName;
    public Expr Collection { get; } = collection;
    public Expr Condition { get; } = condition;
    public string Target { get; } = target;
}

// get "Jeff" from scores into score (D41)
public sealed class GetKeyStmt(Expr key, Expr target, string resultTarget, int line) : Stmt(line)
{
    public Expr Key { get; } = key;
    public Expr Target { get; } = target;
    public string ResultTarget { get; } = resultTarget;
}

// set "Jeff" to 100 in scores (D41: creates or replaces)
public sealed class SetKeyStmt(Expr key, Expr value, Expr target, int line) : Stmt(line)
{
    public Expr Key { get; } = key;
    public Expr Value { get; } = value;
    public Expr Target { get; } = target;
}

// days between a and b make n (D32)
public sealed class DateDifferenceStmt(TimeUnit unit, Expr start, Expr end, string target, int line) : Stmt(line)
{
    public TimeUnit Unit { get; } = unit;
    public Expr Start { get; } = start;
    public Expr End { get; } = end;
    public string Target { get; } = target;
}

// add 7 days to date / remove 1 month from date (D32)
public sealed class DateAdjustStmt(Expr amount, TimeUnit unit, string target, bool isRemoval, int line) : Stmt(line)
{
    public Expr Amount { get; } = amount;
    public TimeUnit Unit { get; } = unit;
    public string Target { get; } = target;
    public bool IsRemoval { get; } = isRemoval;
}

// format date as "MM/dd/yyyy" into text (D32)
public sealed class FormatDateStmt(Expr subject, Expr format, string target, int line) : Stmt(line)
{
    public Expr Subject { get; } = subject;
    public Expr Format { get; } = format;
    public string Target { get; } = target;
}

public enum DiagnosticLevel { Note, Warning, Problem }

// log "Server started." / warn "..." / error "..." (D31)
public sealed class DiagnosticStmt(DiagnosticLevel level, IReadOnlyList<Expr> parts, int line) : Stmt(line)
{
    public DiagnosticLevel Level { get; } = level;
    public IReadOnlyList<Expr> Parts { get; } = parts;
}

public sealed class ReturnStmt(Expr? value, int line) : Stmt(line) { public Expr? Value { get; } = value; }

public sealed class ProgramNode(IReadOnlyList<Stmt> statements)
{
    public IReadOnlyList<Stmt> Statements { get; } = statements;
}

// ---- files and folders (rules.md section 30, D20, D21, D61, D72) ----------

// read "notes.txt" into notes
public sealed class ReadFileStmt(Expr path, string target, int line) : Stmt(line)
{
    public Expr Path { get; } = path;
    public string Target { get; } = target;
}

// write "Hello!" to "hello.txt" [atomically]
public sealed class WriteFileStmt(Expr content, Expr path, bool atomic, int line) : Stmt(line)
{
    public Expr Content { get; } = content;
    public Expr Path { get; } = path;
    public bool Atomic { get; } = atomic;
}

// append "line one" to "log.txt"
public sealed class AppendFileStmt(Expr content, Expr path, int line) : Stmt(line)
{
    public Expr Content { get; } = content;
    public Expr Path { get; } = path;
}

public enum FileAction { CopyFile, MoveFile, CopyFolder, MoveFolder }

// copy "a.txt" to "backup" / move folder "Old" to "Archive"
public sealed class TransferStmt(FileAction action, Expr source, Expr destination, int line) : Stmt(line)
{
    public FileAction Action { get; } = action;
    public Expr Source { get; } = source;
    public Expr Destination { get; } = destination;
}

// delete file "a.txt" / delete folder "Old" / create folder "Backup"
public sealed class DeleteFileStmt(Expr path, int line) : Stmt(line) { public Expr Path { get; } = path; }
public sealed class DeleteFolderStmt(Expr path, int line) : Stmt(line) { public Expr Path { get; } = path; }
public sealed class CreateFolderStmt(Expr path, int line) : Stmt(line) { public Expr Path { get; } = path; }

// get files in "Pictures" [and subfolders] into files (also folders)
public sealed class GetEntriesStmt(bool folders, Expr folder, bool includeSubfolders, string target, int line) : Stmt(line)
{
    public bool Folders { get; } = folders;
    public Expr Folder { get; } = folder;
    public bool IncludeSubfolders { get; } = includeSubfolders;
    public string Target { get; } = target;
}

// file "x" exists / file "x" is locked
public sealed class FileExistsExpr(Expr path, int line) : Expr(line) { public Expr Path { get; } = path; }
public sealed class FileLockedExpr(Expr path, int line) : Expr(line) { public Expr Path { get; } = path; }

// ---- json (D29) -------------------------------------------------------------

// read json from "settings.json" into settings
public sealed class ReadJsonStmt(Expr path, string target, int line) : Stmt(line)
{
    public Expr Path { get; } = path;
    public string Target { get; } = target;
}

// convert user to json into text / convert text from json into user
public sealed class ConvertJsonStmt(bool toJson, Expr subject, string target, int line) : Stmt(line)
{
    public bool ToJson { get; } = toJson;
    public Expr Subject { get; } = subject;
    public string Target { get; } = target;
}
