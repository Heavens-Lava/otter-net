using System.Globalization;
using Otter.Language;

namespace Otter.Runtime;

// Runs a syntax tree. A port of otterPS's src/Otter.Interpreter.psm1 for the
// core language: the same rules (D5, D6, D8-D13, D22) and the same runtime
// messages, word for word.
//
// The prototype walks the syntax tree with dictionary scopes, exactly as
// otterPS does, so behaviour can be compared first; the binder and its
// slot-numbered bound tree (docs/ARCHITECTURE.md) replace this walk in phase 2.
public sealed class Interpreter
{
    readonly TextWriter output;
    readonly TextReader input;
    readonly string[] sourceLines;
    readonly Scope globals;
    readonly bool echoInput;
    int callDepth;
    // A `show progress` bar is on the current line until something else is
    // written (otterPS's Complete-OtterProgressBarLine).
    bool progressBarActive;
    readonly PowerShellRandom random = new();

    // echoInput: when input is redirected, PowerShell's Read-Host writes each
    // line it reads back to the output, so otterPS's transcript shows it.
    public Interpreter(TextWriter output, TextReader input, string source, IReadOnlyList<string> arguments, bool echoInput = false)
    {
        this.output = output;
        this.input = input;
        this.echoInput = echoInput;
        // PowerShell: $Source -split "`r?`n"
        sourceLines = source.Replace("\r\n", "\n").Split('\n');
        globals = new Scope();
        globals.Set("arguments", new OtterList(arguments.Select(a => (object?)a)));
    }

    // Set by the `otter` command when it writes to a real console: shows a
    // line in a color, as Write-Host -ForegroundColor does. Without it (output
    // redirected) the text is written plainly, as Write-Host writes it then.
    public Action<string, ConsoleColor>? WriteColoredLine { get; init; }

    // Set when typing comes from a real console: reads a line showing "*"
    // for each character (D100). Otherwise `ask secretly` reads like `ask`,
    // as otterPS's Read-OtterSecretLine falls back to Read-Host.
    public Func<string?>? ReadSecretLine { get; init; }

    // Set when the output is a real console: moves the cursor (column, row).
    public Action<int, int>? MoveCursor { get; init; }

    void CompleteProgressBarLine()
    {
        if (!progressBarActive) return;
        output.WriteLine("");
        progressBarActive = false;
    }

    // `return` (and `stop`) unwind to the function call that is running.
    sealed class ReturnSignal(object? value, int line) : Exception("return")
    {
        public object? Value { get; } = value;
        public int Line { get; } = line;
    }

    OtterError RuntimeError(string message, int line, string? suggestion = null)
    {
        string? sourceLine = line >= 1 && line <= sourceLines.Length ? sourceLines[line - 1] : null;
        return new OtterError(message, line, "runtime", 0, sourceLine, suggestion);
    }

    public void Run(ProgramNode program)
    {
        try
        {
            Execute(program.Statements, globals);
        }
        catch (ReturnSignal signal)
        {
            // D37: stop outside anything callable.
            throw RuntimeError("stop only works inside something Otter can call, like a function. There is nothing here to stop.", signal.Line);
        }
    }

    void Execute(IReadOnlyList<Stmt> statements, Scope scope)
    {
        foreach (var statement in statements) Execute(statement, scope);
    }

    void Execute(Stmt statement, Scope scope)
    {
        switch (statement)
        {
            // say "Hello" name     (D8: parts joined by exactly one space)
            case SayStmt say:
            {
                if (say.Parts.Count == 0)
                {
                    // A bare `say in color X` formats the color without checking it is text.
                    if (say.Color != null)
                    {
                        ConsoleColor emptyColor = ConsoleColorNamed(Values.Format(Evaluate(say.Color, scope)), say.Line);
                        CompleteProgressBarLine();
                        WriteLine("", emptyColor);
                    }
                    else
                    {
                        CompleteProgressBarLine();
                        output.WriteLine("");
                    }
                    return;
                }
                var rendered = new string[say.Parts.Count];
                for (int i = 0; i < say.Parts.Count; i++) rendered[i] = Values.Format(Evaluate(say.Parts[i], scope));
                string text = string.Join(" ", rendered);
                if (say.Color == null)
                {
                    CompleteProgressBarLine();
                    output.WriteLine(text);
                    return;
                }
                object? colorValue = Evaluate(say.Color, scope);
                if (colorValue is not string colorName)
                {
                    throw RuntimeError($"I need text for a color name, but this is {Values.TypeName(colorValue)}.", say.Line, "say \"Error!\" in color \"red\"");
                }
                ConsoleColor color = ConsoleColorNamed(colorName, say.Line);
                CompleteProgressBarLine();
                WriteLine(text, color);
                return;
            }
            case AssignStmt assign:
                SetTarget(assign.Target, Evaluate(assign.Value, scope), scope);
                return;
            // person is a thing / jeff is a Person with name "Jeff"
            case ObjectDefStmt objectDef:
            {
                if (scope.Has(objectDef.Name))
                {
                    throw RuntimeError($"Otter will not replace existing {Values.TypeName(scope.Get(objectDef.Name))} called \"{objectDef.Name}\" with a new thing.",
                        objectDef.Line, "Use a new name, or assign individual properties instead.");
                }
                var thing = new OtterThing(objectDef.TypeName);
                // A declared type starts with its fields present but empty.
                if (objectDef.TypeName != "thing" && scope.Has(objectDef.TypeName) && scope.Get(objectDef.TypeName) is OtterType declared)
                {
                    foreach (var field in declared.FieldNames) thing.WriteProperty(field, null);
                }
                foreach (var property in objectDef.Properties)
                {
                    thing.WriteProperty(((VariableExpr)property.Target).Name, Evaluate(property.Value, scope));
                }
                scope.Set(objectDef.Name, thing);
                return;
            }
            case TypeDefStmt typeDef:
                scope.Set(typeDef.TypeName, new OtterType(typeDef.TypeName, typeDef.FieldNames));
                return;
            // ask "What is your name?" and call it name      (D6)
            case AskStmt ask:
            {
                CompleteProgressBarLine();
                string prompt = Values.Format(Evaluate(ask.Prompt, scope));
                output.Write(prompt + " ");
                output.Flush();
                string? typed;
                if (ask.Secret && ReadSecretLine != null) typed = ReadSecretLine();
                else
                {
                    typed = input.ReadLine();
                    // At the end of the input there is nothing to echo.
                    if (echoInput && typed != null) output.WriteLine(typed);
                }
                scope.Set(ask.Name, Values.FromInput(typed));
                return;
            }
            case MathIntoStmt mathInto:
                scope.Set(mathInto.Target, Evaluate(mathInto.Expression, scope));
                return;
            case AddToStmt addTo:
                AddTo(addTo, scope);
                return;
            case RemoveFromStmt removeFrom:
                RemoveFrom(removeFrom, scope);
                return;
            case IfStmt ifStmt:
            {
                foreach (var branch in ifStmt.Branches)
                {
                    if (Values.IsTruthy(Evaluate(branch.Condition, scope))) { Execute(branch.Body, scope); return; }
                }
                if (ifStmt.ElseBody != null) Execute(ifStmt.ElseBody, scope);
                return;
            }
            case WhileStmt whileStmt:
                while (Values.IsTruthy(Evaluate(whileStmt.Condition, scope))) Execute(whileStmt.Body, scope);
                return;
            case RepeatStmt repeat:
            {
                double count = RequireNumber(Evaluate(repeat.Count, scope), repeat.Line, "the number of repeats");
                int whole = (int)Math.Floor(count);
                for (int i = 0; i < whole; i++) Execute(repeat.Body, scope);
                return;
            }
            // count from 1 to 5 as number     (D5: both ends inclusive; counts down when from > to)
            case CountStmt count:
            {
                double from = RequireNumber(Evaluate(count.From, scope), count.Line, "the value to count from");
                double to = RequireNumber(Evaluate(count.To, scope), count.Line, "the value to count to");
                int step = from <= to ? 1 : -1;
                for (double n = from; (step > 0 && n <= to) || (step < 0 && n >= to); n += step)
                {
                    scope.SetLocal(count.VariableName, n);
                    Execute(count.Body, scope);
                }
                return;
            }
            case ForEachStmt forEach:
            {
                object? collection = Evaluate(forEach.Collection, scope);
                if (collection is not OtterList list)
                {
                    throw RuntimeError($"I can only go through a list, but this is {Values.TypeName(collection)}.", forEach.Line);
                }
                // Copy first: the body may add to the list while we walk it.
                foreach (var item in list.ToArray())
                {
                    scope.SetLocal(forEach.VariableName, item);
                    Execute(forEach.Body, scope);
                }
                return;
            }
            case ListDefStmt listDef:
            {
                var values = new OtterList();
                foreach (var item in listDef.Items) values.Add(Evaluate(item, scope));
                scope.Set(listDef.Name, values);
                return;
            }
            case FunctionDefStmt function:
                scope.Set(function.Name, new OtterFunction(function.Name, function.Parameters, function.Body));
                return;
            case CallStmt call:
            {
                object? result = Call(call.Call, scope);
                if (!string.IsNullOrEmpty(call.ResultTarget)) scope.Set(call.ResultTarget, result);
                return;
            }
            // try / otherwise (D23): any failure in the body is caught.
            case TryStmt tryStmt:
            {
                try
                {
                    Execute(tryStmt.Body, scope);
                }
                catch (Exception failure) when (failure is not ReturnSignal and not OutOfMemoryException)
                {
                    // `return` passes straight through a try; everything else is
                    // a failure. D68: `otherwise into reason` keeps its message.
                    if (!string.IsNullOrEmpty(tryStmt.ErrorTarget)) scope.Set(tryStmt.ErrorTarget, failure.Message);
                    if (tryStmt.OtherwiseBody != null) Execute(tryStmt.OtherwiseBody, scope);
                }
                return;
            }
            // fail with "message" (D68): an ordinary runtime error.
            case FailStmt fail:
                throw RuntimeError(Values.Format(Evaluate(fail.Message, scope)), fail.Line);
            // sort games / reverse games: change the list where it stands.
            case SortStmt sort:
            {
                OtterList list = MutableList(sort.Target, scope, sort.Line, "sort");
                var items = list.ToArray();
                Array.Sort(items, CompareForSort);
                list.Clear();
                list.AddRange(items);
                return;
            }
            case ReverseStmt reverse:
                MutableList(reverse.Target, scope, reverse.Line, "reverse").Reverse();
                return;
            // replace "Jeff" with "Jeffrey" in name [into result]
            case ReplaceStmt replace:
            {
                if (!scope.Has(replace.Target)) throw RuntimeError($"Otter could not find the variable \"{replace.Target}\".", replace.Line);
                string subject = Values.Format(scope.Get(replace.Target));
                string find = Values.Format(Evaluate(replace.Find, scope));
                string replacement = Values.Format(Evaluate(replace.Replacement, scope));
                if (find.Length == 0) throw RuntimeError("I cannot replace empty text.", replace.Line);
                // Plain text, no patterns: "." means a full stop.
                string result = subject.Replace(find, replacement, StringComparison.Ordinal);
                // D27: with a destination, the source is left as it was.
                scope.Set(replace.ResultTarget ?? replace.Target, result);
                return;
            }
            case SplitStmt split:
            {
                string subject = Values.Format(Evaluate(split.Subject, scope));
                string separator = Values.Format(Evaluate(split.Separator, scope));
                if (separator.Length == 0) throw RuntimeError("I need something to split by.", split.Line);
                var pieces = subject.Split(new[] { separator }, StringSplitOptions.None);
                scope.Set(split.Target, new OtterList(pieces.Select(p => (object?)p)));
                return;
            }
            case JoinStmt join:
            {
                object? value = Evaluate(join.Subject, scope);
                if (value is not OtterList list) throw RuntimeError($"I can only join a list, but this is {Values.TypeName(value)}.", join.Line);
                string separator = Values.Format(Evaluate(join.Separator, scope));
                scope.Set(join.Target, string.Join(separator, list.Select(Values.Format)));
                return;
            }
            // D26: find gives the first match, or gone.
            case FindStmt find:
            {
                object? collection = Evaluate(find.Collection, scope);
                if (collection is not OtterList list) throw RuntimeError($"I can only search a list, but this is {Values.TypeName(collection)}.", find.Line);
                object? found = null;
                foreach (var item in list.ToArray())
                {
                    // The item name exists for the condition only.
                    var itemScope = new Scope(scope);
                    itemScope.SetLocal(find.ItemName, item);
                    if (Values.IsTruthy(Evaluate(find.Condition, itemScope))) { found = item; break; }
                }
                scope.Set(find.Target, found);
                return;
            }
            // get "Jeff" from scores into score (D41): a key never set is gone.
            case GetKeyStmt getKey:
            {
                OtterThing thing = DynamicKeyTarget(Evaluate(getKey.Target, scope), getKey.Line, "read from");
                string key = StringKey(Evaluate(getKey.Key, scope), getKey.Line);
                scope.Set(getKey.ResultTarget, thing.ReadProperty(key));
                return;
            }
            // set "Jeff" to 100 in scores (D41): creates or replaces.
            case SetKeyStmt setKey:
            {
                OtterThing thing = DynamicKeyTarget(Evaluate(setKey.Target, scope), setKey.Line, "write to");
                string key = StringKey(Evaluate(setKey.Key, scope), setKey.Line);
                thing.WriteProperty(key, Evaluate(setKey.Value, scope));
                return;
            }
            // days between a and b make n (D32)
            case DateDifferenceStmt difference:
            {
                object? start = Evaluate(difference.Start, scope);
                object? end = Evaluate(difference.End, scope);
                scope.Set(difference.Target, MeasureDateDifference(start, end, difference.Unit, difference.Line));
                return;
            }
            // add 7 days to date / remove 1 month from date (D32)
            case DateAdjustStmt adjust:
            {
                string name = adjust.Target;
                if (!scope.Has(name)) throw RuntimeError($"Otter could not find the variable \"{name}\".", adjust.Line);
                if (scope.Get(name) is not OtterDate current)
                {
                    throw RuntimeError($"I can only add time to a date, but \"{name}\" holds {Values.TypeName(scope.Get(name))}.", adjust.Line);
                }
                double amount = RequireNumber(Evaluate(adjust.Amount, scope), adjust.Line, "the amount of time");
                int whole = (int)Math.Truncate(amount);
                if (adjust.IsRemoval) whole = -whole;
                RequireUnitAllowed(current, adjust.Unit, adjust.Line);
                DateTime moved = adjust.Unit switch
                {
                    TimeUnit.Year => current.Value.AddYears(whole),
                    TimeUnit.Month => current.Value.AddMonths(whole),
                    TimeUnit.Day => current.Value.AddDays(whole),
                    TimeUnit.Hour => current.Value.AddHours(whole),
                    TimeUnit.Minute => current.Value.AddMinutes(whole),
                    _ => current.Value.AddSeconds(whole),
                };
                scope.Set(name, new OtterDate(moved, current.HasTime));
                return;
            }
            // format date as "MM/dd/yyyy" into text (D32)
            case FormatDateStmt format:
            {
                object? subject = Evaluate(format.Subject, scope);
                if (subject is not OtterDate date) throw RuntimeError($"I can only format a date, but this is {Values.TypeName(subject)}.", format.Line);
                string pattern = Values.Format(Evaluate(format.Format, scope));
                string text;
                try { text = date.Value.ToString(pattern, CultureInfo.InvariantCulture); }
                catch (FormatException)
                {
                    throw RuntimeError($"I do not understand the date format {pattern}.", format.Line, "format date as \"MM/dd/yyyy\" into text");
                }
                scope.Set(format.Target, text);
                return;
            }
            // log / warn / error (D31): "log: text", "warn: text", "error: text"
            case DiagnosticStmt diagnostic:
            {
                var rendered = diagnostic.Parts.Select(part => Values.Format(Evaluate(part, scope)));
                string label = diagnostic.Level switch
                {
                    DiagnosticLevel.Warning => "warn",
                    DiagnosticLevel.Problem => "error",
                    _ => "log",
                };
                string line = $"{label}: {string.Join(" ", rendered)}";
                CompleteProgressBarLine();
                output.WriteLine(line);
                return;
            }
            // ---- files and folders (Otter.Library.psm1) ----
            case ReadFileStmt readFile:
            {
                string path = PathArgument(readFile.Path, scope);
                scope.Set(readFile.Target, Library(() => OtterFiles.Read(path), readFile.Line));
                return;
            }
            case WriteFileStmt writeFile:
            {
                string content = Values.Format(Evaluate(writeFile.Content, scope));
                string path = PathArgument(writeFile.Path, scope);
                Library(() => OtterFiles.Write(path, content, writeFile.Atomic), writeFile.Line);
                return;
            }
            case AppendFileStmt appendFile:
            {
                string content = Values.Format(Evaluate(appendFile.Content, scope));
                string path = PathArgument(appendFile.Path, scope);
                Library(() => OtterFiles.Append(path, content), appendFile.Line);
                return;
            }
            case TransferStmt transfer:
            {
                string source = PathArgument(transfer.Source, scope);
                string destination = PathArgument(transfer.Destination, scope);
                Action action = transfer.Action switch
                {
                    FileAction.CopyFile => () => OtterFiles.CopyFile(source, destination),
                    FileAction.MoveFile => () => OtterFiles.MoveFile(source, destination),
                    FileAction.CopyFolder => () => OtterFiles.CopyFolder(source, destination),
                    FileAction.ZipFolder => () => OtterFiles.ZipFolder(source, destination),
                    FileAction.Unzip => () => OtterFiles.Unzip(source, destination),
                    _ => () => OtterFiles.MoveFolder(source, destination),
                };
                Library(action, transfer.Line);
                return;
            }
            case DeleteFileStmt deleteFile:
            {
                string path = PathArgument(deleteFile.Path, scope);
                Library(() => OtterFiles.DeleteFile(path), deleteFile.Line);
                return;
            }
            case DeleteFolderStmt deleteFolder:
            {
                string path = PathArgument(deleteFolder.Path, scope);
                Library(() => OtterFiles.DeleteFolder(path), deleteFolder.Line);
                return;
            }
            case CreateFolderStmt createFolder:
            {
                string path = PathArgument(createFolder.Path, scope);
                Library(() => OtterFiles.CreateFolder(path), createFolder.Line);
                return;
            }
            case GetEntriesStmt entries:
            {
                string path = PathArgument(entries.Folder, scope);
                scope.Set(entries.Target, Library(() => OtterFiles.Entries(path, entries.Folders, entries.IncludeSubfolders), entries.Line));
                return;
            }
            // ---- csv (D95) ----
            case ReadCsvStmt readCsv:
            {
                string path = PathArgument(readCsv.Path, scope);
                string text = Library(() => OtterFiles.Read(path), readCsv.Line);
                scope.Set(readCsv.Target, Csv(() => OtterCsv.Read(text), readCsv.Line));
                return;
            }
            case WriteCsvStmt writeCsv:
            {
                object? rows = Evaluate(writeCsv.Rows, scope);
                string path = PathArgument(writeCsv.Path, scope);
                string text = Csv(() => OtterCsv.Write(rows), writeCsv.Line);
                Library(() => OtterFiles.Write(path, text, atomic: false), writeCsv.Line);
                return;
            }
            case ConvertCsvStmt convertCsv:
            {
                object? subject = Evaluate(convertCsv.Subject, scope);
                object? converted = convertCsv.ToCsv
                    ? Csv(() => OtterCsv.Write(subject), convertCsv.Line)
                    : Csv(() => OtterCsv.Read(Values.Format(subject)), convertCsv.Line);
                scope.Set(convertCsv.Target, converted);
                return;
            }
            // ---- environment and folders (D67, D94) ----
            case GetEnvironmentVariableStmt getVariable:
            {
                string name = Values.Format(Evaluate(getVariable.Name, scope));
                scope.Set(getVariable.Target, EnvironmentVariable(name));
                return;
            }
            case SetEnvironmentVariableStmt setVariable:
            {
                string name = Values.Format(Evaluate(setVariable.Name, scope));
                object? value = Evaluate(setVariable.Value, scope);
                // .NET Framework's refusals reach the user as otterPS's internal
                // error, word for word.
                if (name.Length == 0)
                {
                    throw new InternalProblem("Exception calling \"SetEnvironmentVariable\" with \"2\" argument(s): \"String cannot be of zero length." + (char)13 + (char)10 + "Parameter name: variable\"");
                }
                if (name.Contains('='))
                {
                    throw new InternalProblem("Exception calling \"SetEnvironmentVariable\" with \"2\" argument(s): \"Environment variable name cannot contain equal character.\"");
                }
                Environment.SetEnvironmentVariable(name, value is null ? null : Values.Format(value));
                return;
            }
            case GetSystemFolderStmt systemFolder:
            {
                string folderName = Values.Format(Evaluate(systemFolder.FolderName, scope));
                string path = folderName.ToLowerInvariant() switch
                {
                    "temp" => Path.GetTempPath(),
                    "appdata" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "user" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "current" => Environment.CurrentDirectory,
                    _ => throw RuntimeError($"I do not know a system folder called \"{folderName}\".", systemFolder.Line,
                        "get system folder \"temp\" into path (also: \"appdata\", \"user\", \"current\")"),
                };
                scope.Set(systemFolder.Target, path);
                return;
            }
            case SetCurrentDirectoryStmt setDirectory:
            {
                string path = PathArgument(setDirectory.Path, scope);
                string? full = null;
                try { full = Path.GetFullPath(path, Environment.CurrentDirectory); }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { }
                if (full == null || !Directory.Exists(full))
                {
                    throw RuntimeError($"I cannot find a folder called \"{path}\".", setDirectory.Line, "set current directory to \"path/to/folder\"");
                }
                Environment.CurrentDirectory = full;
                return;
            }
            // ---- hashing and encryption (D91, D92) ----
            case HashTextStmt hash:
            {
                string text = Values.Format(Evaluate(hash.Text, scope));
                string algorithm = Values.Format(Evaluate(hash.Algorithm, scope));
                string? key = hash.Key == null ? null : Values.Format(Evaluate(hash.Key, scope));
                try { scope.Set(hash.ResultTarget, OtterCrypto.Hash(text, algorithm, key)); }
                catch (CryptoProblem problem) { throw RuntimeError(problem.Message, hash.Line); }
                return;
            }
            case CipherTextStmt cipher:
            {
                string text = Values.Format(Evaluate(cipher.Text, scope));
                string key = Values.Format(Evaluate(cipher.Key, scope));
                try { scope.Set(cipher.ResultTarget, cipher.Decrypt ? OtterCrypto.Decrypt(text, key) : OtterCrypto.Encrypt(text, key)); }
                catch (CryptoProblem problem) { throw RuntimeError(problem.Message, cipher.Line); }
                return;
            }
            // ---- console (D100) ----
            case ShowProgressStmt progress:
            {
                double percent = RequireNumber(Evaluate(progress.Percent, scope), progress.Line, "a progress percentage");
                if (percent < 0 || percent > 100)
                {
                    throw RuntimeError($"I need a percentage between 0 and 100, but got {Values.Format(percent)}.", progress.Line, "show progress 50 percent");
                }
                const int width = 20;
                int filled = (int)Math.Round(width * (percent / 100), MidpointRounding.ToEven);
                output.Write($"\r[{new string('#', filled)}{new string('-', width - filled)}] {Values.Format(percent)}%");
                progressBarActive = true;
                return;
            }
            case ChooseFromListStmt choose:
            {
                CompleteProgressBarLine();
                object? optionsValue = Evaluate(choose.Options, scope);
                if (optionsValue is not OtterList options || options.Count == 0) throw RuntimeError("I cannot choose from an empty list.", choose.Line);
                for (int i = 0; i < options.Count; i++) output.WriteLine($"  {i + 1}. {Values.Format(options[i])}");
                object? selected = null;
                bool chosen = false;
                while (!chosen)
                {
                    output.Write("Choose a number: ");
                    string? typed = input.ReadLine();
                    if (echoInput && typed != null) output.WriteLine(typed);
                    if (typed != null && int.TryParse(typed, NumberStyles.Integer, CultureInfo.CurrentCulture, out int number) && number >= 1 && number <= options.Count)
                    {
                        selected = options[number - 1];
                        chosen = true;
                    }
                    else WriteLine($"  Please enter a number from 1 to {options.Count}.", ConsoleColor.Yellow);
                }
                scope.Set(choose.Target, selected);
                return;
            }
            case SetCursorStmt cursor:
            {
                double row = RequireNumber(Evaluate(cursor.Row, scope), cursor.Line, "a cursor row");
                double column = RequireNumber(Evaluate(cursor.Column, scope), cursor.Line, "a cursor column");
                if (row < 1 || column < 1) throw RuntimeError("I need a row and column of 1 or greater.", cursor.Line, "set cursor to row 1 column 1");
                if (MoveCursor == null)
                {
                    // Without a console, .NET Framework reports the redirected handle.
                    throw RuntimeError("I could not move the cursor there. Exception calling \"SetCursorPosition\" with \"2\" argument(s): \"The handle is invalid." + (char)13 + (char)10 + "\"", cursor.Line);
                }
                try { MoveCursor((int)Math.Round(column, MidpointRounding.ToEven) - 1, (int)Math.Round(row, MidpointRounding.ToEven) - 1); }
                catch (Exception e) when (e is IOException or ArgumentOutOfRangeException or OverflowException)
                {
                    throw RuntimeError($"I could not move the cursor there. {e.Message}", cursor.Line);
                }
                return;
            }
            // ---- waiting and timers (D101) ----
            case WaitDelayStmt wait:
            {
                double amount = RequireNumber(Evaluate(wait.Duration, scope), wait.Line, "a wait duration");
                if (amount < 0) throw RuntimeError($"I can't wait a negative amount of time ({Values.PowerShellText(amount)}).", wait.Line);
                double milliseconds = wait.Unit switch
                {
                    TimeUnit.Millisecond => amount,
                    TimeUnit.Minute => amount * 60000,
                    TimeUnit.Hour => amount * 3600000,
                    TimeUnit.Day => amount * 86400000,
                    TimeUnit.Month => amount * 2629746000,
                    TimeUnit.Year => amount * 31556952000,
                    _ => amount * 1000,
                };
                // [Math]::Round: halves to even. Nothing else can happen while a
                // console program waits, so this is a plain sleep.
                double target = Math.Round(milliseconds);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < target)
                {
                    double remaining = target - clock.ElapsedMilliseconds;
                    if (remaining > 0) Thread.Sleep((int)Math.Min(remaining, int.MaxValue));
                }
                return;
            }
            case StartTimerStmt startTimer:
                scope.Set(startTimer.Target, new OtterTimer());
                return;
            // ---- commands ----
            case RunCommandStmt runCommand:
            {
                string commandLine = Values.Format(Evaluate(runCommand.Target, scope));
                OtterThing result;
                try { result = OtterCommands.Run(commandLine); }
                catch (CommandProblem problem) { throw RuntimeError(problem.Message, runCommand.Line); }
                if (runCommand.ResultTarget != null) scope.Set(runCommand.ResultTarget, result);
                return;
            }
            // ---- random (D30, D101) ----
            case RandomNumberStmt randomNumber:
            {
                object? fromValue = Evaluate(randomNumber.From, scope);
                object? toValue = Evaluate(randomNumber.To, scope);
                double from = RequireNumber(fromValue, randomNumber.Line, "the lowest number");
                double to = RequireNumber(toValue, randomNumber.Line, "the highest number");
                if (from > to) (from, to) = (to, from);
                // Get-Random -Maximum is exclusive, so one is added to include the top.
                int picked = random.Next(PowerShellInt(Math.Floor(from), randomNumber.Line), PowerShellInt(Math.Floor(to), randomNumber.Line) + 1);
                scope.Set(randomNumber.Target, (double)picked);
                return;
            }
            case RandomItemStmt randomItem:
            {
                object? collection = Evaluate(randomItem.Collection, scope);
                if (collection is not OtterList list) throw RuntimeError($"I can only pick from a list, but this is {Values.TypeName(collection)}.", randomItem.Line);
                scope.Set(randomItem.Target, list.Count == 0 ? null : list[random.Next(0, list.Count)]);
                return;
            }
            case SetRandomSeedStmt setSeed:
            {
                double seed = RequireNumber(Evaluate(setSeed.Seed, scope), setSeed.Line, "a random seed");
                random.Seed(PowerShellInt(seed, setSeed.Line));
                return;
            }
            // ---- xml (D105) ----
            case XmlWriteFileStmt writeXml:
            {
                object? value = Evaluate(writeXml.Xml, scope);
                if (value is not OtterXml xml) throw RuntimeError($"I can only write xml, but this is {Values.TypeName(value)}.", writeXml.Line);
                string path = PathArgument(writeXml.Path, scope);
                string outer = PowerShellXml.Text(PowerShellXml.OuterXml(xml.Node));
                Library(() => OtterFiles.Write(path, outer, atomic: false), writeXml.Line);
                return;
            }
            case XmlAddElementStmt addElement:
            {
                string name = Values.Format(Evaluate(addElement.Name, scope));
                object? value = Evaluate(addElement.Xml, scope);
                if (value is not OtterXml xml) throw RuntimeError($"I can only add an element to xml, but this is {Values.TypeName(value)}.", addElement.Line);
                object? ownerValue = xml.Node is System.Xml.XmlDocument ownDocument ? ownDocument
                    : PowerShellXml.Property(xml.Node, "OwnerDocument", () => xml.Node.OwnerDocument);
                if (ownerValue is not System.Xml.XmlDocument owner) throw NoSuchMethod(ownerValue, "CreateElement");
                System.Xml.XmlElement created = XmlCall(() => owner.CreateElement(name), "CreateElement", 1, addElement.Line);
                if (addElement.Text != null) created.InnerText = Values.Format(Evaluate(addElement.Text, scope));
                // Adding to a whole document adds to (or creates) its root.
                object? parentValue = xml.Node is System.Xml.XmlDocument document ? PowerShellXml.DocumentElement(document) ?? document : xml.Node;
                if (parentValue is not System.Xml.XmlNode parent) throw NoSuchMethod(parentValue, "AppendChild");
                XmlCall(() => parent.AppendChild(created), "AppendChild", 1, addElement.Line);
                if (addElement.Target != null) scope.Set(addElement.Target, new OtterXml(created));
                return;
            }
            case XmlRemoveElementStmt removeElement:
            {
                object? value = Evaluate(removeElement.Element, scope);
                if (value is not OtterXml xml) throw RuntimeError($"I can only remove an xml element, but this is {Values.TypeName(value)}.", removeElement.Line);
                RequireXmlElement(xml, "remove", removeElement.Line);
                object? parentValue = PowerShellXml.Property(xml.Node, "ParentNode", () => xml.Node.ParentNode);
                if (parentValue == null) throw RuntimeError("This element has no parent to remove it from.", removeElement.Line);
                if (parentValue is not System.Xml.XmlNode parent) throw NoSuchMethod(parentValue, "RemoveChild");
                XmlCall(() => parent.RemoveChild(xml.Node), "RemoveChild", 1, removeElement.Line);
                return;
            }
            case XmlRemoveAttributeStmt removeAttribute:
            {
                string name = Values.Format(Evaluate(removeAttribute.Name, scope));
                object? value = Evaluate(removeAttribute.Element, scope);
                if (value is not OtterXml xml) throw RuntimeError($"I can only remove an attribute of an xml element, but this is {Values.TypeName(value)}.", removeAttribute.Line);
                RequireXmlElement(xml, "remove an attribute of", removeAttribute.Line);
                ((System.Xml.XmlElement)xml.Node).RemoveAttribute(name);
                return;
            }
            case XmlSetTextStmt setText:
            {
                string text = Values.Format(Evaluate(setText.Value, scope));
                if (setText.Element != null)
                {
                    object? value = Evaluate(setText.Element, scope);
                    if (value is not OtterXml element) throw RuntimeError($"I can only set the text of an xml element, but this is {Values.TypeName(value)}.", setText.Line);
                    RequireXmlElement(element, "set the text of", setText.Line);
                    element.Node.InnerText = text;
                    return;
                }
                string name = Values.Format(Evaluate(setText.NameIn!, scope));
                object? within = Evaluate(setText.XmlIn!, scope);
                if (within is not OtterXml xml) throw RuntimeError($"I can only set text within xml, but this is {Values.TypeName(within)}.", setText.Line);
                List<System.Xml.XmlElement> found = XmlChildrenNamed(xml.Node, name);
                if (found.Count == 0) throw RuntimeError($"I couldn't find an element called \"{name}\" to set the text of.", setText.Line);
                found[0].InnerText = text;
                return;
            }
            case XmlSetAttributeStmt setAttribute:
            {
                string name = Values.Format(Evaluate(setAttribute.Name, scope));
                object? value = Evaluate(setAttribute.Element, scope);
                if (value is not OtterXml xml) throw RuntimeError($"I can only set an attribute of an xml element, but this is {Values.TypeName(value)}.", setAttribute.Line);
                RequireXmlElement(xml, "set an attribute of", setAttribute.Line);
                string attributeValue = Values.Format(Evaluate(setAttribute.Value, scope));
                XmlCall(() => ((System.Xml.XmlElement)xml.Node).SetAttribute(name, attributeValue), "SetAttribute", 2, setAttribute.Line);
                return;
            }
            case WriteBytesFileStmt writeBytes:
            {
                object? data = Evaluate(writeBytes.Data, scope);
                string path = PathArgument(writeBytes.Path, scope);
                if (data is not OtterBytes bytes) throw RuntimeError($"Binary file writes require bytes, but this is {Values.TypeName(data)}.", writeBytes.Line);
                Library(() => OtterFiles.WriteBytes(path, bytes.Value, writeBytes.Atomic), writeBytes.Line);
                return;
            }
            // ---- json (D29) ----
            case ReadJsonStmt readJson:
            {
                string path = PathArgument(readJson.Path, scope);
                string text = Library(() => OtterFiles.Read(path), readJson.Line);
                scope.Set(readJson.Target, FromJson(text, readJson.Line));
                return;
            }
            case ConvertJsonStmt convert:
            {
                object? subject = Evaluate(convert.Subject, scope);
                if (!convert.ToJson)
                {
                    scope.Set(convert.Target, FromJson(Values.Format(subject), convert.Line));
                    return;
                }
                try { scope.Set(convert.Target, OtterJson.Write(subject)); }
                catch (OtterJson.NotData) { throw RuntimeError("Otter cannot turn something it can do into JSON.", convert.Line); }
                return;
            }
            case ReturnStmt returnStmt:
                throw new ReturnSignal(returnStmt.Value == null ? null : Evaluate(returnStmt.Value, scope), returnStmt.Line);
            default:
                throw new InvalidOperationException($"No runtime for {statement.GetType().Name}.");
        }
    }

    void SetTarget(Expr target, object? value, Scope scope)
    {
        switch (target)
        {
            case VariableExpr variable:
                scope.Set(variable.Name, value);
                return;
            // age of person is 30
            case PropertyAccessExpr property:
            {
                object? owner = Evaluate(property.Target, scope);
                if (owner is not OtterThing thing)
                {
                    throw RuntimeError($"I can only set properties on a thing, but this is {Values.TypeName(owner)}.", property.Line);
                }
                thing.WriteProperty(property.Property, value);
                return;
            }
            default:
                throw RuntimeError("This is not something Otter can give a value to.", target.Line);
        }
    }

    OtterList MutableList(string name, Scope scope, int line, string verb)
    {
        if (!scope.Has(name)) throw RuntimeError($"Otter could not find the variable \"{name}\".", line);
        object? value = scope.Get(name);
        if (value is not OtterList list) throw RuntimeError($"I can only {verb} a list, but \"{name}\" holds {Values.TypeName(value)}.", line);
        return list;
    }

    // Numbers by value, everything else by its text, ordinal (as otterPS).
    static int CompareForSort(object? left, object? right)
    {
        if (Values.TryNumber(left, out double l) && Values.TryNumber(right, out double r)) return l.CompareTo(r);
        return string.CompareOrdinal(Values.Format(left), Values.Format(right));
    }

    // [Environment]::GetEnvironmentVariable: gone when it is not set, and for
    // a name no variable can have (empty, or with "=").
    static object? EnvironmentVariable(string name)
    {
        if (name.Length == 0 || name.Contains('=')) return null;
        return Environment.GetEnvironmentVariable(name);
    }

    // PowerShell's [int] cast: the nearest whole number, halves to even.
    // Out of range is an error there too; otter-net names it plainly.
    int PowerShellInt(double value, int line)
    {
        double rounded = Math.Round(value, MidpointRounding.ToEven);
        if (double.IsNaN(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
        {
            throw RuntimeError($"{Values.FormatNumber(value)} is too large for a random number.", line);
        }
        return (int)rounded;
    }

    static readonly string[] ConsoleColorNames =
    {
        "red", "green", "yellow", "blue", "cyan", "magenta", "white", "black",
        "gray", "darkgray", "darkred", "darkgreen", "darkyellow", "darkblue",
        "darkcyan", "darkmagenta",
    };

    // Get-OtterConsoleColor (D100).
    ConsoleColor ConsoleColorNamed(string name, int line)
    {
        string normalized = name.Trim().ToLowerInvariant();
        if (!ConsoleColorNames.Contains(normalized))
        {
            throw RuntimeError($"I don't recognize the color \"{name}\". Valid colors are: {string.Join(", ", ConsoleColorNames)}.", line,
                "say \"Error!\" in color \"red\"");
        }
        return Enum.Parse<ConsoleColor>(normalized, ignoreCase: true);
    }

    void WriteLine(string text, ConsoleColor color)
    {
        if (WriteColoredLine != null) WriteColoredLine(text, color);
        else output.WriteLine(text);
    }

    // ---- xml (D105) ----

    object? XmlFrom(XmlFromExpr from, Scope scope)
    {
        var document = new System.Xml.XmlDocument();
        string text;
        if (from.Source == XmlSourceKind.File)
        {
            string path = PathArgument(from.Value, scope);
            text = Library(() => OtterFiles.Read(path), from.Line);
        }
        else text = Values.Format(Evaluate(from.Value, scope));
        if (from.Source == XmlSourceKind.Root)
        {
            document.AppendChild(XmlCall(() => document.CreateElement(text), "CreateElement", 1, from.Line));
            return new OtterXml(document);
        }
        try { document.LoadXml(text); }
        catch (System.Xml.XmlException e) { throw RuntimeError($"This is not valid XML, so Otter could not read it: {e.Message}", from.Line); }
        return new OtterXml(document);
    }

    // name / root / text / attributes of an xml value.
    object? XmlProperty(OtterXml xml, string property, int line)
    {
        switch (property.ToLowerInvariant())
        {
            case "name":
                RequireXmlElement(xml, "read the name of", line);
                return PowerShellXml.Name(xml.Node);
            case "root":
            {
                if (xml.Node is not System.Xml.XmlDocument document) throw RuntimeError("\"root of\" only makes sense on an xml document, not an element.", line);
                object? root = PowerShellXml.DocumentElement(document);
                return root switch
                {
                    null => null,
                    System.Xml.XmlNode rootNode => new OtterXml(rootNode),
                    _ => new OtterXml(document.DocumentElement!),
                };
            }
            case "text":
                return PowerShellXml.InnerText(xml.Node);
            case "attributes":
            {
                RequireXmlElement(xml, "read the attributes of", line);
                object? attributes = PowerShellXml.Property(xml.Node, "Attributes", () => xml.Node.Attributes);
                return new OtterList(PowerShellXml.Enumerate(attributes).Select(item => item is System.Xml.XmlNode node ? PowerShellXml.Name(node) : null));
            }
            default:
                throw RuntimeError($"This xml value has no property called \"{property}\". Try name, root, text, or attributes.", line);
        }
    }

    // Only direct children are searched; a document searches its root.
    static List<System.Xml.XmlElement> XmlChildren(System.Xml.XmlNode node) => PowerShellXml.ChildElements(node);

    static List<System.Xml.XmlElement> XmlChildrenNamed(System.Xml.XmlNode node, string name) =>
        XmlChildren(node).Where(e => PowerShellXml.NameIs(e, name)).ToList();

    // Calling an XML method on what PowerShell's adapter returned instead of
    // a node (text, say) fails inside otterPS like this.
    static InternalProblem NoSuchMethod(object? value, string method) =>
        new($"Method invocation failed because [{(value is null ? "System.Object" : value is PowerShellArray ? "System.Object[]" : value.GetType().FullName)}] does not contain a method named '{method}'.");

    void RequireXmlElement(OtterXml xml, string what, int line)
    {
        if (xml.Node is not System.Xml.XmlElement)
        {
            throw RuntimeError($"I can only {what} on an xml element, but this is an xml document. Use \"root of ...\" or \"element ... in ...\" to get an element first.", line);
        }
    }

    // A System.Xml call that refuses its input (a name that is not a valid
    // XML name, say) fails inside otterPS as a .NET method call.
    T XmlCall<T>(Func<T> call, string method, int arguments, int line)
    {
        try { return call(); }
        catch (Exception e) when (e is System.Xml.XmlException or ArgumentException or InvalidOperationException)
        {
            throw new InternalProblem($"Exception calling \"{method}\" with \"{arguments}\" argument(s): \"{e.Message}\"");
        }
    }

    void XmlCall(Action call, string method, int arguments, int line) => XmlCall(() => { call(); return true; }, method, arguments, line);

    // empty bytes / bytes from text|hex|base64 X / text|hex|base64 from bytes X (D102)
    object? EvaluateBytes(BytesExpr bytes, Scope scope)
    {
        switch (bytes.Op)
        {
            case BytesOp.Empty:
                return new OtterBytes(Array.Empty<byte>());
            case BytesOp.FromText:
                return new OtterBytes(System.Text.Encoding.UTF8.GetBytes(Values.Format(Evaluate(bytes.Source!, scope))));
            case BytesOp.FromHex:
            {
                string text = Values.Format(Evaluate(bytes.Source!, scope));
                string clean = text.Trim();
                if (clean.Length == 0 || clean.Length % 2 != 0 || !clean.All(Uri.IsHexDigit))
                {
                    throw RuntimeError($"I can't read \"{text}\" as hex - it needs to be pairs of hex digits (0-9, A-F).", bytes.Line, "bytes from hex \"48656C6C6F\"");
                }
                return new OtterBytes(Convert.FromHexString(clean));
            }
            case BytesOp.FromBase64:
            {
                string text = Values.Format(Evaluate(bytes.Source!, scope));
                try { return new OtterBytes(Convert.FromBase64String(text)); }
                catch (FormatException)
                {
                    throw RuntimeError($"I can't read \"{text}\" as base64 - it isn't valid base64 text.", bytes.Line, "bytes from base64 \"SGVsbG8=\"");
                }
            }
        }
        object? value = Evaluate(bytes.Source!, scope);
        string reading = bytes.Op switch { BytesOp.ToText => "text", BytesOp.ToHex => "hex", _ => "base64" };
        if (value is not OtterBytes source) throw RuntimeError($"I can only read {reading} from bytes, but this is {Values.TypeName(value)}.", bytes.Line);
        switch (bytes.Op)
        {
            case BytesOp.ToText:
                try { return new System.Text.UTF8Encoding(false, true).GetString(source.Value); }
                catch (System.Text.DecoderFallbackException) { throw RuntimeError("These bytes aren't valid UTF-8 text.", bytes.Line); }
            case BytesOp.ToHex:
                return Convert.ToHexString(source.Value);
            default:
                return Convert.ToBase64String(source.Value);
        }
    }

    // Get-OtterPathArgument: typed text, or a file or folder object.
    string PathArgument(Expr expression, Scope scope)
    {
        object? value = Evaluate(expression, scope);
        try { return OtterFiles.PathOf(value); }
        catch (FileProblem problem) { throw RuntimeError(problem.Message, expression.Line, problem.Suggestion); }
    }

    // The library reports problems in its own words; they become runtime
    // errors on the statement's line.
    T Library<T>(Func<T> work, int line)
    {
        try { return work(); }
        catch (FileProblem problem) { throw RuntimeError(problem.Message, line, problem.Suggestion); }
    }

    void Library(Action work, int line) => Library(() => { work(); return true; }, line);

    T Csv<T>(Func<T> work, int line)
    {
        try { return work(); }
        catch (CsvProblem problem) { throw RuntimeError(problem.Message, line); }
    }

    // ConvertFrom-OtterJsonText.
    object? FromJson(string text, int line)
    {
        if (string.IsNullOrWhiteSpace(text)) throw RuntimeError("There is no JSON here to read.", line);
        try { return OtterJson.Read(text); }
        catch (JsonProblem) { throw RuntimeError("This is not valid JSON, so Otter could not read it.", line); }
    }

    // D32.1: a date with no time of day has no hour, minute or second.
    void RequireUnitAllowed(OtterDate date, TimeUnit unit, int line)
    {
        if (date.HasTime || unit is TimeUnit.Year or TimeUnit.Month or TimeUnit.Day) return;
        throw RuntimeError($"This is a date with no time of day, so it has no {unit.ToString().ToLowerInvariant()}.", line, "started is now");
    }

    // year of date / month of date / hour of started ... (the part name is
    // matched ignoring case, as otterPS's switch does)
    object? DatePart(OtterDate date, string part, int line)
    {
        switch (part.ToLowerInvariant())
        {
            case "year": return (double)date.Value.Year;
            case "month": return (double)date.Value.Month;
            case "day": return (double)date.Value.Day;
            case "hour": RequireUnitAllowed(date, TimeUnit.Hour, line); return (double)date.Value.Hour;
            case "minute": RequireUnitAllowed(date, TimeUnit.Minute, line); return (double)date.Value.Minute;
            case "second": RequireUnitAllowed(date, TimeUnit.Second, line); return (double)date.Value.Second;
        }
        throw RuntimeError($"A date has no part called \"{part}\".", line, "year of ...");
    }

    // Whole units, truncated toward zero, end minus start (D32.7). Years and
    // months count calendar months, exactly as otterPS does.
    object? MeasureDateDifference(object? start, object? end, TimeUnit unit, int line)
    {
        if (start is not OtterDate from) throw RuntimeError($"I can only measure time between two dates, but the first one is {Values.TypeName(start)}.", line);
        if (end is not OtterDate to) throw RuntimeError($"I can only measure time between two dates, but the second one is {Values.TypeName(end)}.", line);
        if (unit is TimeUnit.Year or TimeUnit.Month)
        {
            int months = (to.Value.Year - from.Value.Year) * 12 + (to.Value.Month - from.Value.Month);
            if (months > 0 && to.Value.Day < from.Value.Day) months--;
            if (months < 0 && to.Value.Day > from.Value.Day) months++;
            return unit == TimeUnit.Month ? months : Math.Truncate(months / 12.0);
        }
        TimeSpan span = to.Value - from.Value;
        return unit switch
        {
            TimeUnit.Day => Math.Truncate(span.TotalDays),
            TimeUnit.Hour => Math.Truncate(span.TotalHours),
            TimeUnit.Minute => Math.Truncate(span.TotalMinutes),
            _ => Math.Truncate(span.TotalSeconds),
        };
    }

    // D41: dynamic keys only on a plain thing, never a typed one.
    OtterThing DynamicKeyTarget(object? value, int line, string verb)
    {
        if (value is not OtterThing thing) throw RuntimeError($"I can only {verb} a thing, but this is {Values.TypeName(value)}.", line);
        if (thing.TypeName != "thing") throw RuntimeError($"I can only {verb} properties dynamically on a thing, but this is a {thing.TypeName}.", line);
        return thing;
    }

    // D41: keys are text, on purpose.
    string StringKey(object? value, int line)
    {
        if (value is string text) return text;
        throw RuntimeError($"I need text for a dynamic key, but this is {Values.TypeName(value)}.", line, "get \"Jeff\" from scores into score");
    }

    // D12: add to a number is arithmetic, add to a list appends.
    void AddTo(AddToStmt statement, Scope scope)
    {
        string name = statement.Target;
        if (!scope.Has(name))
        {
            throw RuntimeError($"Otter could not find the variable \"{name}\".", statement.Line, $"{name} is 0");
        }
        object? current = scope.Get(name);
        object? amount = Evaluate(statement.Amount, scope);
        if (current is OtterList list) { list.Add(amount); return; }
        if (Values.TryNumber(current, out double number))
        {
            double addend = RequireNumber(amount, statement.Line, $"the amount to add to \"{name}\"");
            scope.Set(name, number + addend);
            return;
        }
        throw RuntimeError($"I can only add to a number or a list, but \"{name}\" holds {Values.TypeName(current)}.", statement.Line);
    }

    void RemoveFrom(RemoveFromStmt statement, Scope scope)
    {
        string name = statement.Target;
        if (!scope.Has(name))
        {
            throw RuntimeError($"Otter could not find the variable \"{name}\".", statement.Line);
        }
        object? current = scope.Get(name);
        object? amount = Evaluate(statement.Amount, scope);
        if (current is OtterList list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (Values.AreEqual(list[i], amount)) { list.RemoveAt(i); return; }
            }
            // Removing something absent is not an error.
            return;
        }
        if (Values.TryNumber(current, out double number))
        {
            double subtrahend = RequireNumber(amount, statement.Line, $"the amount to remove from \"{name}\"");
            scope.Set(name, number - subtrahend);
            return;
        }
        throw RuntimeError($"I can only remove from a number or a list, but \"{name}\" holds {Values.TypeName(current)}.", statement.Line);
    }

    // "I need a number here" - otterPS's Assert-OtterNumber.
    double RequireNumber(object? value, int line, string what)
    {
        if (Values.TryNumber(value, out double number)) return number;
        string shown = value is string s ? "\"" + s + "\"" : Values.Format(value);
        throw RuntimeError($"I expected a number for {what} but got {shown}.", line);
    }

    object? Evaluate(Expr expression, Scope scope)
    {
        switch (expression)
        {
            case LiteralExpr literal:
                return literal.Value;
            case VariableExpr variable:
            {
                if (!scope.Has(variable.Name))
                {
                    throw RuntimeError($"Otter could not find the variable \"{variable.Name}\". It has not been given a value yet.",
                        variable.Line, $"{variable.Name} is 0");
                }
                return scope.Get(variable.Name);
            }
            case MathExpr math:
                return EvaluateMath(math, scope);
            case ComparisonExpr comparison:
                return EvaluateComparison(comparison, scope);
            // D11: both short-circuit.
            case LogicalExpr logical:
            {
                bool left = Values.IsTruthy(Evaluate(logical.Left, scope));
                if (logical.Op == LogicalOp.And) return left && Values.IsTruthy(Evaluate(logical.Right, scope));
                return left || Values.IsTruthy(Evaluate(logical.Right, scope));
            }
            case NotExpr not:
                return !Values.IsTruthy(Evaluate(not.Operand, scope));
            // D13: text contains text; a list contains an item.
            case ContainsExpr contains:
            {
                object? collection = Evaluate(contains.Collection, scope);
                object? item = Evaluate(contains.Item, scope);
                if (collection is string text) return text.Contains(Values.Format(item), StringComparison.Ordinal);
                if (collection is not OtterList list)
                {
                    throw RuntimeError($"Only text or a list can contain something, but this is {Values.TypeName(collection)}.", contains.Line);
                }
                foreach (var entry in list) { if (Values.AreEqual(entry, item)) return true; }
                return false;
            }
            case TextMatchExpr textMatch:
            {
                string subject = Values.Format(Evaluate(textMatch.Text, scope));
                string part = Values.Format(Evaluate(textMatch.Part, scope));
                return textMatch.Match == TextMatch.StartsWith
                    ? subject.StartsWith(part, StringComparison.Ordinal)
                    : subject.EndsWith(part, StringComparison.Ordinal);
            }
            case OfOperationExpr ofOperation:
                return EvaluateOfOperation(ofOperation, scope);
            case CallExpr call:
                return Call(call, scope);
            case BytesExpr bytes:
                return EvaluateBytes(bytes, scope);
            case XmlFromExpr xmlFrom:
                return XmlFrom(xmlFrom, scope);
            case XmlSelectExpr select:
            {
                object? value = Evaluate(select.Xml, scope);
                if (value is not OtterXml xml) throw RuntimeError($"I can only look for elements in xml, but this is {Values.TypeName(value)}.", select.Line);
                switch (select.Kind)
                {
                    case XmlSelectKind.Element:
                    {
                        var found = XmlChildrenNamed(xml.Node, Values.Format(Evaluate(select.Selector, scope)));
                        return found.Count == 0 ? null : new OtterXml(found[0]);
                    }
                    case XmlSelectKind.Elements:
                        return new OtterList(XmlChildrenNamed(xml.Node, Values.Format(Evaluate(select.Selector, scope))).Select(e => (object?)new OtterXml(e)));
                    case XmlSelectKind.Child:
                    {
                        double index = RequireNumber(Evaluate(select.Selector, scope), select.Line, "a child index");
                        var children = XmlChildren(xml.Node);
                        int position = PowerShellInt(index, select.Line);
                        return position < 0 || position >= children.Count ? null : new OtterXml(children[position]);
                    }
                    default:
                        return new OtterList(XmlChildren(xml.Node).Select(e => (object?)new OtterXml(e)));
                }
            }
            case XmlAttributeExpr attribute:
            {
                string name = Values.Format(Evaluate(attribute.Name, scope));
                object? value = Evaluate(attribute.Element, scope);
                if (value is not OtterXml xml) throw RuntimeError($"I can only read an attribute of an xml element, but this is {Values.TypeName(value)}.", attribute.Line);
                RequireXmlElement(xml, "read an attribute of", attribute.Line);
                var element = (System.Xml.XmlElement)xml.Node;
                return element.HasAttribute(name) ? element.GetAttribute(name) : null;
            }
            case XmlTextOfNameInExpr textOf:
            {
                string name = Values.Format(Evaluate(textOf.Name, scope));
                object? value = Evaluate(textOf.Xml, scope);
                if (value is not OtterXml xml) throw RuntimeError($"I can only look for elements in xml, but this is {Values.TypeName(value)}.", textOf.Line);
                var found = XmlChildrenNamed(xml.Node, name);
                if (found.Count == 0) throw RuntimeError($"I couldn't find an element called \"{name}\".", textOf.Line);
                return PowerShellXml.InnerText(found[0]);
            }
            case XmlElementExistsExpr exists:
            {
                object? value = Evaluate(exists.Xml, scope);
                if (value is not OtterXml xml) throw RuntimeError($"I can only look for elements in xml, but this is {Values.TypeName(value)}.", exists.Line);
                return XmlChildrenNamed(xml.Node, Values.Format(Evaluate(exists.Selector, scope))).Count > 0;
            }
            case XmlHasAttributeExpr hasAttribute:
            {
                object? value = Evaluate(hasAttribute.Element, scope);
                if (value is not OtterXml xml) throw RuntimeError($"I can only ask whether an xml element has an attribute, but this is {Values.TypeName(value)}.", hasAttribute.Line);
                RequireXmlElement(xml, "ask about an attribute of", hasAttribute.Line);
                return ((System.Xml.XmlElement)xml.Node).HasAttribute(Values.Format(Evaluate(hasAttribute.Name, scope)));
            }
            case XmlToTextExpr toText:
            {
                object? value = Evaluate(toText.Xml, scope);
                if (value is not OtterXml xml) throw RuntimeError($"I can only read text from xml, but this is {Values.TypeName(value)}.", toText.Line);
                if (!toText.Pretty) return PowerShellXml.OuterXml(xml.Node);
                var stream = new MemoryStream();
                var settings = new System.Xml.XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    OmitXmlDeclaration = xml.Node is not System.Xml.XmlDocument,
                    Encoding = new System.Text.UTF8Encoding(false),
                };
                using (var writer = System.Xml.XmlWriter.Create(stream, settings))
                {
                    xml.Node.WriteTo(writer);
                }
                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
            case BytesFromFileExpr bytesFromFile:
            {
                string path = PathArgument(bytesFromFile.Path, scope);
                return Library(() => new OtterBytes(OtterFiles.ReadBytes(path)), bytesFromFile.Line);
            }
            case MinMaxExpr minMax:
            {
                double left = RequireNumber(Evaluate(minMax.Left, scope), minMax.Line, "comparing sizes");
                double right = RequireNumber(Evaluate(minMax.Right, scope), minMax.Line, "comparing sizes");
                return minMax.IsMax ? Math.Max(left, right) : Math.Min(left, right);
            }
            // D100: false when input or output is redirected.
            case ConsoleInteractiveExpr:
                return !(Console.IsInputRedirected || Console.IsOutputRedirected);
            case FileExistsExpr fileExists:
            {
                string path = PathArgument(fileExists.Path, scope);
                return Library(() => OtterFiles.Exists(path), fileExists.Line);
            }
            case FileLockedExpr fileLocked:
            {
                string path = PathArgument(fileLocked.Path, scope);
                return Library(() => OtterFiles.IsLocked(path), fileLocked.Line);
            }
            case ClockExpr clock:
                return new OtterDate(DateTime.Now, clock.Clock == ClockKind.Now);
            case DateDifferenceExpr difference:
                return MeasureDateDifference(Evaluate(difference.Start, scope), Evaluate(difference.End, scope), difference.Unit, difference.Line);
            // date from "2024-01-15" [using "yyyy-MM-dd"] (D101)
            case DateFromTextExpr fromText:
            {
                string text = Values.Format(Evaluate(fromText.Source, scope));
                if (fromText.Format != null)
                {
                    string formatText = Values.Format(Evaluate(fromText.Format, scope));
                    try
                    {
                        DateTime exact = DateTime.ParseExact(text, formatText, CultureInfo.InvariantCulture);
                        return new OtterDate(exact, exact.TimeOfDay != TimeSpan.Zero);
                    }
                    catch (FormatException)
                    {
                        throw RuntimeError($"I couldn't understand \"{text}\" as a date using the format \"{formatText}\".", fromText.Line);
                    }
                }
                try
                {
                    DateTime parsed = DateTime.Parse(text, CultureInfo.InvariantCulture);
                    return new OtterDate(parsed, parsed.TimeOfDay != TimeSpan.Zero);
                }
                catch (FormatException)
                {
                    throw RuntimeError($"I couldn't understand \"{text}\" as a date.", fromText.Line, "date from \"2024-01-15\" using \"yyyy-MM-dd\"");
                }
            }
            // name of person / city of address of user / year of date
            case PropertyAccessExpr property:
            {
                object? target = Evaluate(property.Target, scope);
                // D32.2: a date answers its own parts.
                if (target is OtterDate date) return DatePart(date, property.Property, property.Line);
                if (target is OtterXml xml) return XmlProperty(xml, property.Property, property.Line);
                if (target is not OtterThing thing)
                {
                    throw RuntimeError($"I can only read properties of a thing, but this is {Values.TypeName(target)}.", property.Line);
                }
                if (!thing.HasProperty(property.Property))
                {
                    string? suggestion = thing.PropertyNames.Count > 0 ? $"{thing.PropertyNames[0]} of ..." : null;
                    throw RuntimeError($"This {thing.TypeName} has no property called \"{property.Property}\".", property.Line, suggestion);
                }
                return thing.ReadProperty(property.Property);
            }
            default:
                throw new InvalidOperationException($"No runtime for {expression.GetType().Name}.");
        }
    }

    object? EvaluateMath(MathExpr math, Scope scope)
    {
        object? leftRaw = Evaluate(math.Left, scope);
        object? rightRaw = Evaluate(math.Right, scope);
        // `plus` joins two texts.
        if (math.Op == MathOp.Add && leftRaw is string ls && rightRaw is string rs) return ls + rs;
        if (math.Op == MathOp.Add && (leftRaw is bool || rightRaw is bool))
        {
            throw RuntimeError("A true/false value cannot be combined with \"and\" here. Boolean \"and\"/\"or\" only work inside an if or while condition.",
                math.Line, "if condition1 and condition2");
        }
        double left = leftRaw is double l ? l : RequireNumber(leftRaw, math.Line, "the left side of this calculation");
        double right = rightRaw is double r ? r : RequireNumber(rightRaw, math.Line, "the right side of this calculation");
        switch (math.Op)
        {
            case MathOp.Add: return left + right;
            case MathOp.Subtract: return left - right;
            case MathOp.Multiply: return left * right;
            case MathOp.Divide:
                if (right == 0) throw RuntimeError("I cannot divide by zero.", math.Line);
                return left / right;
            case MathOp.Percent: return (left / 100.0) * right;   // D88
            default: return Math.Pow(left, right);                // D88
        }
    }

    object? EvaluateComparison(ComparisonExpr comparison, Scope scope)
    {
        object? left = Evaluate(comparison.Left, scope);
        object? right = Evaluate(comparison.Right, scope);
        if (comparison.Op == CompareOp.Equal) return Values.AreEqual(left, right);
        if (comparison.Op == CompareOp.NotEqual) return !Values.AreEqual(left, right);
        // D32.6: two dates order by their instant.
        if (left is OtterDate leftDate && right is OtterDate rightDate)
        {
            int order = leftDate.Value.CompareTo(rightDate.Value);
            return comparison.Op switch
            {
                CompareOp.AtLeast => order >= 0,
                CompareOp.AtMost => order <= 0,
                CompareOp.GreaterThan => order > 0,
                _ => order < 0,
            };
        }
        double l = left is double ld ? ld : RequireNumber(left, comparison.Line, "the left side of this comparison");
        double r = right is double rd ? rd : RequireNumber(right, comparison.Line, "the right side of this comparison");
        return comparison.Op switch
        {
            CompareOp.AtLeast => l >= r,
            CompareOp.AtMost => l <= r,
            CompareOp.GreaterThan => l > r,
            _ => l < r,
        };
    }

    object? EvaluateOfOperation(OfOperationExpr operation, Scope scope)
    {
        object? subject = Evaluate(operation.Subject, scope);
        switch (operation.Operation)
        {
            case OfOperation.Length:
                if (subject is OtterList list) return (double)list.Count;
                if (subject is string text) return (double)text.Length;
                if (subject is OtterBytes bytes) return (double)bytes.Value.Length;
                throw RuntimeError($"I can only measure the length of text, a list, or bytes, but this is {Values.TypeName(subject)}.", operation.Line);
            case OfOperation.Uppercase: return Values.Format(subject).ToUpperInvariant();
            case OfOperation.Lowercase: return Values.Format(subject).ToLowerInvariant();
            // D22: first/last of an empty list is gone, not an error.
            case OfOperation.First:
                if (subject is not OtterList firstList)
                    throw RuntimeError($"Only a list has a first item, but this is {Values.TypeName(subject)}.", operation.Line);
                return firstList.Count == 0 ? null : firstList[0];
            case OfOperation.Last:
                if (subject is not OtterList lastList)
                    throw RuntimeError($"Only a list has a last item, but this is {Values.TypeName(subject)}.", operation.Line);
                return lastList.Count == 0 ? null : lastList[^1];
            case OfOperation.AbsoluteValue:
                return Math.Abs(RequireNumber(subject, operation.Line, "the absolute value"));
            case OfOperation.SquareRoot:
            {
                double n = RequireNumber(subject, operation.Line, "the square root");
                if (n < 0) throw RuntimeError($"I can't take the square root of a negative number ({Values.PowerShellText(n)}).", operation.Line);
                return Math.Sqrt(n);
            }
            case OfOperation.ElapsedTime:
            case OfOperation.ElapsedMilliseconds:
            {
                if (subject is not OtterTimer timer)
                {
                    throw RuntimeError($"I can only measure elapsed time of a timer, but this is {Values.TypeName(subject)}.", operation.Line, "start timer workTimer");
                }
                return operation.Operation == OfOperation.ElapsedTime ? timer.Clock.Elapsed.TotalSeconds : timer.Clock.Elapsed.TotalMilliseconds;
            }
            // D90: angles in degrees.
            case OfOperation.Sine:
                return Math.Sin(RequireNumber(subject, operation.Line, "sine") * Math.PI / 180.0);
            case OfOperation.Cosine:
                return Math.Cos(RequireNumber(subject, operation.Line, "cosine") * Math.PI / 180.0);
            case OfOperation.Tangent:
                return Math.Tan(RequireNumber(subject, operation.Line, "tangent") * Math.PI / 180.0);
            case OfOperation.LogTen:
            {
                double n = RequireNumber(subject, operation.Line, "a logarithm");
                if (n <= 0) throw RuntimeError($"I can't take the log of a number that isn't positive ({Values.PowerShellText(n)}).", operation.Line);
                return Math.Log10(n);
            }
            case OfOperation.NaturalLog:
            {
                double n = RequireNumber(subject, operation.Line, "a logarithm");
                if (n <= 0) throw RuntimeError($"I can't take the natural log of a number that isn't positive ({Values.PowerShellText(n)}).", operation.Line);
                return Math.Log(n);
            }
            case OfOperation.Round:
                return Math.Round(RequireNumber(subject, operation.Line, "rounding"), 0, MidpointRounding.AwayFromZero);
            case OfOperation.RoundUp:
                return Math.Ceiling(RequireNumber(subject, operation.Line, "rounding"));
            default:
                return Math.Floor(RequireNumber(subject, operation.Line, "rounding"));
        }
    }

    object? Call(CallExpr call, Scope scope)
    {
        string name = call.Name;
        if (!scope.Has(name))
        {
            throw RuntimeError($"Otter could not find anything called \"{name}\".", call.Line, $"to {name}");
        }
        if (scope.Get(name) is not OtterFunction function)
        {
            throw RuntimeError($"\"{name}\" is not something Otter can do - it holds {Values.TypeName(scope.Get(name))}.", call.Line);
        }
        int expected = function.Parameters.Count;
        int given = call.Arguments.Count;
        if (given != expected)
        {
            string word = expected == 1 ? "value" : "values";
            throw RuntimeError($"\"{name}\" needs {expected} {word} but was given {given}.", call.Line,
                $"{name} {string.Join(" ", function.Parameters)}");
        }
        // Arguments are worked out in the caller's scope, before the new one exists.
        var arguments = new object?[expected];
        for (int i = 0; i < expected; i++) arguments[i] = Evaluate(call.Arguments[i], scope);

        // A function sees the globals, never its caller's locals.
        var local = new Scope(globals);
        for (int i = 0; i < expected; i++) local.SetLocal(function.Parameters[i], arguments[i]);

        if (callDepth >= 250)
        {
            throw RuntimeError("Call depth limit exceeded (possible infinite recursion).", call.Line,
                "Check for infinite recursion or reduce call nesting.");
        }
        callDepth++;
        try
        {
            Execute(function.Body, local);
            return null;   // a function that never returns produces nothing
        }
        catch (ReturnSignal signal)
        {
            return signal.Value;
        }
        finally
        {
            callDepth--;
        }
    }
}
