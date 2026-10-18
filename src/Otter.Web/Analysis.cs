using Otter.Language;

namespace Otter.Web;

// Get-OtterJsBindingNames: the names a function body binds, split the way
// the interpreter treats them - Set-style (climbs to an existing global) and
// always local (loop variables).
static class Bindings
{
    public sealed class Result
    {
        public HashSet<string> SetStyle { get; } = new(StringComparer.Ordinal);
        public HashSet<string> AlwaysLocal { get; } = new(StringComparer.Ordinal);
    }

    public static Result Of(IEnumerable<Stmt>? statements)
    {
        var result = new Result();
        Walk(statements, result);
        return result;
    }

    // Get-OtterJsTopLevelGlobalNames
    public static HashSet<string> TopLevelGlobals(IEnumerable<Stmt> statements)
    {
        Result bindings = Of(statements);
        var all = new HashSet<string>(bindings.SetStyle, StringComparer.Ordinal);
        foreach (string name in bindings.AlwaysLocal) all.Add(name);
        return all;
    }

    static void Add(HashSet<string> set, string? name)
    {
        if (!string.IsNullOrEmpty(name)) set.Add(name);
    }

    static void Walk(IEnumerable<Stmt>? statements, Result result)
    {
        if (statements == null) return;
        var setStyle = result.SetStyle;
        foreach (Stmt s in statements)
        {
            switch (s)
            {
                case AssignStmt { Target: VariableExpr variable }: setStyle.Add(variable.Name); break;
                case CallStmt call: Add(setStyle, call.ResultTarget); break;
                case MathIntoStmt mathInto: setStyle.Add(mathInto.Target); break;
                case CreateUiResourceStmt create: setStyle.Add(create.Target); break;
                case ReadFileStmt read: Add(setStyle, read.Target); break;
                case RunCommandStmt run: Add(setStyle, run.ResultTarget); break;
                case HashTextStmt hash: Add(setStyle, hash.ResultTarget); break;
                case CipherTextStmt cipher: Add(setStyle, cipher.ResultTarget); break;
                case GetEnvironmentVariableStmt getVariable: Add(setStyle, getVariable.Target); break;
                case GetSystemFolderStmt systemFolder: Add(setStyle, systemFolder.Target); break;
                case GetEntriesStmt entries: Add(setStyle, entries.Target); break;
                case ListDefStmt list: setStyle.Add(list.Name); break;
                case JoinStmt join: setStyle.Add(join.Target); break;
                case SplitStmt split: setStyle.Add(split.Target); break;
                case AddToStmt add: setStyle.Add(add.Target); break;
                case RemoveFromStmt remove: setStyle.Add(remove.Target); break;
                case RandomNumberStmt random: Add(setStyle, random.Target); break;
                case RandomItemStmt randomItem: Add(setStyle, randomItem.Target); break;
                case DateAdjustStmt adjust: setStyle.Add(adjust.Target); break;
                case GetKeyStmt getKey: Add(setStyle, getKey.ResultTarget); break;
                case DateDifferenceStmt difference: Add(setStyle, difference.Target); break;
                case FormatDateStmt format: Add(setStyle, format.Target); break;
                case ReadJsonStmt readJson: Add(setStyle, readJson.Target); break;
                case ConvertJsonStmt convertJson: Add(setStyle, convertJson.Target); break;
                case ReadCsvStmt readCsv: Add(setStyle, readCsv.Target); break;
                case ConvertCsvStmt convertCsv: Add(setStyle, convertCsv.Target); break;
                case ObjectDefStmt objectDef: setStyle.Add(objectDef.Name); break;
                case TypeDefStmt typeDef: setStyle.Add(typeDef.TypeName); break;
                case AskStmt ask: setStyle.Add(ask.Name); break;
                case CountStmt count: result.AlwaysLocal.Add(count.VariableName); Walk(count.Body, result); break;
                case ForEachStmt forEach: result.AlwaysLocal.Add(forEach.VariableName); Walk(forEach.Body, result); break;
                case IfStmt ifStmt:
                    foreach (IfBranch branch in ifStmt.Branches) Walk(branch.Body, result);
                    Walk(ifStmt.ElseBody, result);
                    break;
                case WhileStmt loop: Walk(loop.Body, result); break;
                case RepeatStmt repeat: Walk(repeat.Body, result); break;
                case TryStmt attempt:
                    Add(setStyle, attempt.ErrorTarget);
                    Walk(attempt.Body, result);
                    Walk(attempt.OtherwiseBody, result);
                    break;
                case GenerateKeyStmt generateKey: Add(setStyle, generateKey.Target); break;
                case CryptoCipherStmt cryptoCipher: Add(setStyle, cryptoCipher.Target); break;
                case HashPasswordStmt hashPassword: Add(setStyle, hashPassword.Target); break;
            }
        }
    }
}

// Test-OtterJsExpressionNeedsAsync / Test-OtterJsBodyNeedsAsync: does a
// function or handler body need to be an `async function`? Copied with
// otterPS's quirks: several checks read a field the node does not have
// (FormatDate's "Date", Split's "Source", CountLoop's "Start" ...), which
// PowerShell reads as nothing, so those parts never count.
static class AsyncAnalysis
{
    public static bool ExpressionNeedsAsync(Expr? expression)
    {
        switch (expression)
        {
            case null: return false;
            case AwaitExpr or FileExistsExpr or FileLockedExpr or CryptoHashExpr or CryptoHmacExpr or PasswordMatchesExpr: return true;
            case SecureRandomBytesExpr random: return ExpressionNeedsAsync(random.Count);
            case SecurelyEqualsExpr securely: return ExpressionNeedsAsync(securely.Left) || ExpressionNeedsAsync(securely.Right);
            case BytesExpr bytes: return ExpressionNeedsAsync(bytes.Source);
            case MathExpr math: return ExpressionNeedsAsync(math.Left) || ExpressionNeedsAsync(math.Right);
            case ComparisonExpr comparison: return ExpressionNeedsAsync(comparison.Left) || ExpressionNeedsAsync(comparison.Right);
            case LogicalExpr logical: return ExpressionNeedsAsync(logical.Left) || ExpressionNeedsAsync(logical.Right);
            case NotExpr not: return ExpressionNeedsAsync(not.Operand);
            case ContainsExpr contains: return ExpressionNeedsAsync(contains.Collection) || ExpressionNeedsAsync(contains.Item);
            case TextMatchExpr textMatch: return ExpressionNeedsAsync(textMatch.Text) || ExpressionNeedsAsync(textMatch.Part);
            case OfOperationExpr of: return ExpressionNeedsAsync(of.Subject);
            case MinMaxExpr minMax: return ExpressionNeedsAsync(minMax.Left) || ExpressionNeedsAsync(minMax.Right);
            case PropertyAccessExpr access: return ExpressionNeedsAsync(access.Target);
            case DateDifferenceExpr difference: return ExpressionNeedsAsync(difference.Start) || ExpressionNeedsAsync(difference.End);
            case XmlFromExpr xmlFrom: return xmlFrom.Source == XmlSourceKind.File;
            case XmlTextOfNameInExpr textOf: return ExpressionNeedsAsync(textOf.Xml);
            case XmlSelectExpr select: return ExpressionNeedsAsync(select.Xml);
            case XmlElementExistsExpr exists: return ExpressionNeedsAsync(exists.Xml);
            // XmlAttribute ("Target"), XmlHasAttribute ("Xml") and XmlToText
            // ("Source") read fields those nodes do not have.
            case DateFromTextExpr fromText: return ExpressionNeedsAsync(fromText.Format);
            case CallExpr call: return call.Arguments.Any(ExpressionNeedsAsync);
            default: return false;
        }
    }

    public static bool BodyNeedsAsync(IEnumerable<Stmt>? statements)
    {
        if (statements == null) return false;
        foreach (Stmt s in statements)
        {
            switch (s)
            {
                case AwaitStmt or ReadFileStmt or WriteFileStmt or AppendFileStmt or TransferStmt or DeleteFileStmt or CreateFolderStmt or DeleteFolderStmt
                    or GetEntriesStmt or RunCommandStmt or GetEnvironmentVariableStmt or GetSystemFolderStmt or HashTextStmt or CipherTextStmt:
                    return true;
                case CryptoCipherStmt or HashPasswordStmt:
                    return true;
                case ReadJsonStmt or ReadCsvStmt or WriteCsvStmt or XmlWriteFileStmt:
                    return true;
                case AssignStmt assign when ExpressionNeedsAsync(assign.Value): return true;
                case MathIntoStmt mathInto when ExpressionNeedsAsync(mathInto.Expression): return true;
                case ReturnStmt ret when ExpressionNeedsAsync(ret.Value): return true;
                case AddToStmt add when ExpressionNeedsAsync(add.Amount): return true;
                case RemoveFromStmt remove when ExpressionNeedsAsync(remove.Amount): return true;
                case SetKeyStmt setKey when ExpressionNeedsAsync(setKey.Key) || ExpressionNeedsAsync(setKey.Value): return true;
                case ListDefStmt list when list.Items.Any(ExpressionNeedsAsync): return true;
                case ObjectDefStmt objectDef when objectDef.Properties.Any(p => ExpressionNeedsAsync(p.Value)): return true;
                case FormatDateStmt format when ExpressionNeedsAsync(format.Format): return true;
                case DateAdjustStmt adjust when ExpressionNeedsAsync(adjust.Amount): return true;
                case DateDifferenceStmt difference when ExpressionNeedsAsync(difference.Start) || ExpressionNeedsAsync(difference.End): return true;
                case ReplaceStmt replace when ExpressionNeedsAsync(replace.Find): return true;
                case FindStmt find when ExpressionNeedsAsync(find.Collection) || ExpressionNeedsAsync(find.Condition): return true;
                case FailStmt fail when ExpressionNeedsAsync(fail.Message): return true;
                case XmlSetTextStmt setText when ExpressionNeedsAsync(setText.Value): return true;
                case XmlSetAttributeStmt setAttribute when ExpressionNeedsAsync(setAttribute.Value): return true;
                case XmlAddElementStmt addElement:
                    // otterPS hands the element's name (text) to
                    // Test-OtterJsExpressionNeedsAsync's [Node] parameter:
                    // PowerShell's argument transformation fails, and the web
                    // compiler reports an internal problem.
                    if (!string.IsNullOrEmpty(addElement.Target))
                    {
                        throw new WebInternalProblem($"Cannot process argument transformation on parameter 'Expression'. Cannot convert the \"{addElement.Target}\" value of type \"System.String\" to type \"Node\".");
                    }
                    if (ExpressionNeedsAsync(addElement.Text)) return true;
                    break;
                case SayStmt say when say.Parts.Any(ExpressionNeedsAsync): return true;
                case DiagnosticStmt diagnostic when diagnostic.Parts.Any(ExpressionNeedsAsync): return true;
            }
            if (s is IfStmt ifStmt)
            {
                foreach (IfBranch branch in ifStmt.Branches)
                {
                    if (ExpressionNeedsAsync(branch.Condition)) return true;
                    if (BodyNeedsAsync(branch.Body)) return true;
                }
                if (ifStmt.ElseBody != null && BodyNeedsAsync(ifStmt.ElseBody)) return true;
            }
            if (s is WhileStmt whileStmt && ExpressionNeedsAsync(whileStmt.Condition)) return true;
            if (s is RepeatStmt repeat && ExpressionNeedsAsync(repeat.Count)) return true;
            if (s is ForEachStmt forEach && ExpressionNeedsAsync(forEach.Collection)) return true;
            if (s is WhileStmt or RepeatStmt or CountStmt or ForEachStmt && BodyNeedsAsync(LoopBody(s))) return true;
            if (s is TryStmt attempt)
            {
                if (BodyNeedsAsync(attempt.Body)) return true;
                if (attempt.OtherwiseBody != null && BodyNeedsAsync(attempt.OtherwiseBody)) return true;
            }
        }
        return false;
    }

    static IReadOnlyList<Stmt> LoopBody(Stmt loop) => loop switch
    {
        WhileStmt w => w.Body,
        RepeatStmt r => r.Body,
        CountStmt c => c.Body,
        ForEachStmt f => f.Body,
        _ => Array.Empty<Stmt>(),
    };
}

// Sort-Object on names: PowerShell compares strings with the current
// culture, ignoring case. For Otter names (letters, digits, _) that is the
// ordinal order of the upper-cased text, except that _ sorts before digits
// and letters.
static class PowerShellSort
{
    public static List<string> Strings(IEnumerable<string> names)
    {
        var list = names.ToList();
        // List.Sort is not stable; neither is Sort-Object for equal keys.
        list.Sort(Compare);
        return list;
    }

    static int Compare(string? a, string? b)
    {
        a ??= ""; b ??= "";
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            int x = Weight(a[i]), y = Weight(b[i]);
            if (x != y) return x.CompareTo(y);
        }
        return a.Length.CompareTo(b.Length);
    }

    static int Weight(char ch) => ch == '_' ? 0 : char.ToUpperInvariant(ch);
}
