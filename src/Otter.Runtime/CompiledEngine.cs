using System.Globalization;
using Otter.Language;

namespace Otter.Runtime;

// otterPS 1.0.1's `otter run` does not always interpret: Invoke-OtterSource
// first hands the program to its compiled engine (src/Otter.Compiler.Native.psm1,
// C# 5 compiled with Add-Type against src/native/OtterNativeRuntime.cs) and
// runs it there when every statement is in the engine's subset; only a
// program it refuses goes to the interpreter. A plain run, the shortest form
// and a project's run use it; `check`, `debug`, `profile`, -DebugAst,
// -ParseOnly, the REPL and OTTER_ENGINE=interpreter do not
// (Invoke-OtterFile's $script:OtterCompiledRunAllowed).
//
// The compiled runtime reproduces the interpreter's messages, but not where
// .NET itself fails, and not everywhere: what otter-net does differently in
// a compiled run is in Interpreter.CompiledEngine's uses (`length of` bytes,
// adding time to a date, random numbers, a failure that is not an Otter
// error). Accepts is ConvertTo-OtterNativeStatement/-Expression's refusal
// rules, node by node (otterPS's NodeKind in each comment).
public static class OtterPsCompiledEngine
{
    // ConvertTo-OtterCSharp: the whole program, functions included.
    public static bool Accepts(ProgramNode program) => Block(program.Statements);

    static bool Block(IReadOnlyList<Stmt>? statements) => statements == null || statements.All(Statement);

    static bool Statement(Stmt statement) => statement switch
    {
        // Say without "in color".
        SayStmt say => say.Color == null && say.Parts.All(Expression),
        // Assign to a variable or a property.
        AssignStmt assign => Expression(assign.Value) && assign.Target switch
        {
            VariableExpr => true,
            PropertyAccessExpr property => Expression(property.Target),
            _ => false,
        },
        TypeDefStmt => true,
        // ObjectDef: every property line `name is value`.
        ObjectDefStmt objectDef => objectDef.Properties.All(p => p.Target is VariableExpr && Expression(p.Value)),
        GetKeyStmt getKey => Expression(getKey.Target) && Expression(getKey.Key),
        MathIntoStmt mathInto => Expression(mathInto.Expression),
        ListDefStmt list => list.Items.All(Expression),
        AddToStmt addTo => Expression(addTo.Amount),
        RemoveFromStmt removeFrom => Expression(removeFrom.Amount),
        IfStmt ifStmt => ifStmt.Branches.All(b => Expression(b.Condition) && Block(b.Body)) && Block(ifStmt.ElseBody),
        // CountLoop, Repeat, While, ForEach (a loop with handlers, D130, has
        // statements outside the subset anyway).
        CountStmt count => Expression(count.From) && Expression(count.To) && Block(count.Body),
        RepeatStmt repeat => Expression(repeat.Count) && Block(repeat.Body),
        WhileStmt whileStmt => Expression(whileStmt.Condition) && Block(whileStmt.Body),
        ForEachStmt forEach => Expression(forEach.Collection) && Block(forEach.Body),
        FunctionDefStmt function => Block(function.Body),
        CallStmt call => Expression(call.Call),
        ReturnStmt ret => ret.Value == null || Expression(ret.Value),
        TryStmt tryStmt => Block(tryStmt.Body) && Block(tryStmt.OtherwiseBody),
        FailStmt fail => Expression(fail.Message),
        SortStmt => true,
        ReverseStmt => true,
        // ConvertToJson, ConvertFromJson (the library, through the bridge).
        ConvertJsonStmt json => Expression(json.Subject),
        // DateAdjust, DateDifference: not milliseconds.
        DateAdjustStmt adjust => adjust.Unit != TimeUnit.Millisecond && Expression(adjust.Amount),
        DateDifferenceStmt difference => difference.Unit != TimeUnit.Millisecond && Expression(difference.Start) && Expression(difference.End),
        FormatDateStmt format => Expression(format.Subject) && Expression(format.Format),
        WriteFileStmt write => Expression(write.Content) && Expression(write.Path),
        AppendFileStmt append => Expression(append.Content) && Expression(append.Path),
        ReadFileStmt read => Expression(read.Path),
        ReadJsonStmt readJson => Expression(readJson.Path),
        DeleteFileStmt delete => Expression(delete.Path),
        // CopyFile, MoveFile (not folders or archives).
        TransferStmt transfer => transfer.Action is FileAction.CopyFile or FileAction.MoveFile && Expression(transfer.Source) && Expression(transfer.Destination),
        ReplaceStmt replace => Expression(replace.Find) && Expression(replace.Replacement),
        SplitStmt split => Expression(split.Subject) && Expression(split.Separator),
        JoinStmt join => Expression(join.Subject) && Expression(join.Separator),
        SetRandomSeedStmt seed => Expression(seed.Seed),
        RandomNumberStmt randomNumber => Expression(randomNumber.From) && Expression(randomNumber.To),
        RandomItemStmt randomItem => Expression(randomItem.Collection),
        // RunProgram, the HTTP requests, UDP and their events are compiled
        // only with OTTER_NATIVE_EXPERIMENTAL=1; everything else is refused.
        _ => false,
    };

    static bool Expression(Expr? expression) => expression switch
    {
        null => true,
        LiteralExpr literal => literal.Value is not double number || double.IsFinite(number),
        VariableExpr => true,
        MathExpr math => Expression(math.Left) && Expression(math.Right),
        ComparisonExpr comparison => Expression(comparison.Left) && Expression(comparison.Right),
        LogicalExpr logical => Expression(logical.Left) && Expression(logical.Right),
        NotExpr not => Expression(not.Operand),
        ContainsExpr contains => Expression(contains.Collection) && Expression(contains.Item),
        // Every `of` operation but the timers' elapsed time.
        OfOperationExpr of => of.Operation is not (OfOperation.ElapsedTime or OfOperation.ElapsedMilliseconds) && Expression(of.Subject),
        FileExistsExpr exists => Expression(exists.Path),
        ClockExpr => true,
        DateDifferenceExpr difference => difference.Unit != TimeUnit.Millisecond && Expression(difference.Start) && Expression(difference.End),
        DateFromTextExpr fromText => Expression(fromText.Source) && Expression(fromText.Format),
        PropertyAccessExpr property => Expression(property.Target),
        CallExpr call => call.Arguments.All(Expression),
        // Only `bytes from text`.
        BytesExpr bytes => bytes.Op == BytesOp.FromText && Expression(bytes.Source),
        _ => false,
    };

    // (int) of a double in the compiled runtime's C#, as Windows PowerShell
    // 5.1's .NET Framework on x64 converts it: unchecked, so NaN and anything
    // outside Int32 become 0x80000000. (.NET 9 and later saturate instead:
    // PowerShell 7's compiled engine gives int.MaxValue for a large number.)
    public static int FrameworkInt32(double value) =>
        double.IsNaN(value) || value >= 2147483648.0 || value <= -2147483649.0 ? int.MinValue : (int)value;

    // The suggestion under "Otter's compiled engine hit an internal problem".
    public const string InternalProblemSuggestion = "Run it again with the interpreter: set OTTER_ENGINE=interpreter, and please report this.";

    // Invoke-OtterCompiledProgram: a failure that is not an Otter error ends
    // the program as an Otter runtime error naming the engine; the message is
    // PowerShell's for the reflection call that ran the program.
    public static OtterError InternalProblem(Exception failure) =>
        new($"Otter's compiled engine hit an internal problem: Exception calling \"Invoke\" with \"2\" argument(s): \"{RawMessage(failure)}\"",
            0, "runtime", 0, null, InternalProblemSuggestion);

    // The message a failure has inside the compiled program (what `otherwise
    // into reason` keeps): .NET's own, where the interpreter's PowerShell
    // would wrap it in "Exception calling ...".
    public static string RawMessage(Exception failure) => failure is DotNetCallProblem call ? call.RawMessage : failure.Message;
}

// A .NET method otterPS calls failed: PowerShell reports it as
// `Exception calling "Method" with "N" argument(s): "<.NET's message>"`
// (a MethodInvocationException). RawMessage is .NET Framework's message,
// what Windows PowerShell 5.1 shows inside the quotes.
public sealed class DotNetCallProblem(string method, int arguments, string rawMessage)
    : Exception($"Exception calling \"{method}\" with \"{arguments}\" argument(s): \"{rawMessage}\"")
{
    public string RawMessage { get; } = rawMessage;
}

// DateTime's Add methods as .NET Framework 4.x writes their failures
// (DateTime.cs in the reference source): the message, a new line and the
// parameter's name. .NET 10 says the same with "(Parameter 'value')", and
// names a different parameter for AddYears.
public static class FrameworkDates
{
    const long MaxMillis = 315537897600000;   // DateTime.MaxValue's days, in milliseconds
    const string AddValue = "Value to add was out of range.";
    const string DateArithmetic = "The added or subtracted value results in an un-representable DateTime.";

    static string Parameter(string message, string name) => message + "\r\nParameter name: " + name;

    // On failure, the Framework's message and the method PowerShell names.
    public static bool TryAdd(DateTime date, TimeUnit unit, double value, out DateTime result, out string method, out string error)
    {
        result = date;
        error = "";
        switch (unit)
        {
            case TimeUnit.Year:
                method = "AddYears";
                if (value < -10000 || value > 10000) { error = Parameter("Years value must be between +/-10000.", "years"); return false; }
                return TryAddMonths(date, (int)value * 12, out result, out error);
            case TimeUnit.Month:
                method = "AddMonths";
                return TryAddMonths(date, (int)value, out result, out error);
            case TimeUnit.Day: method = "AddDays"; return TryAddScaled(date, value, 86400000, out result, out error);
            case TimeUnit.Hour: method = "AddHours"; return TryAddScaled(date, value, 3600000, out result, out error);
            case TimeUnit.Minute: method = "AddMinutes"; return TryAddScaled(date, value, 60000, out result, out error);
            default: method = "AddSeconds"; return TryAddScaled(date, value, 1000, out result, out error);
        }
    }

    static bool TryAddMonths(DateTime date, int months, out DateTime result, out string error)
    {
        result = date;
        error = "";
        if (months < -120000 || months > 120000) { error = Parameter("Months value must be between +/-120000.", "months"); return false; }
        int total = date.Month - 1 + months;
        int year = date.Year + (total >= 0 ? total / 12 : (total - 11) / 12);
        if (year < 1 || year > 9999) { error = Parameter(DateArithmetic, "months"); return false; }
        result = date.AddMonths(months);
        return true;
    }

    // DateTime.Add(double value, int scale), then AddTicks.
    static bool TryAddScaled(DateTime date, double value, int scale, out DateTime result, out string error)
    {
        result = date;
        error = "";
        double scaled = value * scale + (value >= 0 ? 0.5 : -0.5);
        long millis = double.IsNaN(scaled) || scaled >= long.MaxValue || scaled <= long.MinValue ? long.MinValue : (long)scaled;
        if (millis <= -MaxMillis || millis >= MaxMillis) { error = Parameter(AddValue, "value"); return false; }
        long ticks = millis * TimeSpan.TicksPerMillisecond;
        if (ticks > DateTime.MaxValue.Ticks - date.Ticks || ticks < -date.Ticks) { error = Parameter(DateArithmetic, "value"); return false; }
        result = date.AddTicks(ticks);
        return true;
    }

    // PowerShell's error for a method argument it cannot convert to Int32
    // (AddYears(int value), AddMonths(int months)).
    public static string ArgumentConversion(string method, double value)
    {
        string shown = value.ToString("G15", CultureInfo.InvariantCulture);
        string parameter = method == "AddMonths" ? "months" : "value";
        return $"Cannot convert argument \"{parameter}\", with value: \"{shown}\", for \"{method}\" to type \"System.Int32\": \"Cannot convert value \"{shown}\" to type \"System.Int32\". Error: \"Value was either too large or too small for an Int32.\"\"";
    }
}
