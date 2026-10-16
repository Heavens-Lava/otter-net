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
                if (say.Parts.Count == 0) { output.WriteLine(""); return; }
                var rendered = new string[say.Parts.Count];
                for (int i = 0; i < say.Parts.Count; i++) rendered[i] = Values.Format(Evaluate(say.Parts[i], scope));
                output.WriteLine(string.Join(" ", rendered));
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
                string prompt = Values.Format(Evaluate(ask.Prompt, scope));
                output.Write(prompt + " ");
                output.Flush();
                string? typed = input.ReadLine();
                // At the end of the input there is nothing to echo.
                if (echoInput && typed != null) output.WriteLine(typed);
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
                output.WriteLine($"{label}: {string.Join(" ", rendered)}");
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
                if (n < 0) throw RuntimeError($"I can't take the square root of a negative number ({Values.FormatNumber(n)}).", operation.Line);
                return Math.Sqrt(n);
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
