using System.Text.RegularExpressions;

namespace Otter.Cli;

// otter.ps1's command line as Windows PowerShell 5.1 hands it over:
// powershell.exe -File otter.ps1 <arguments>, bound to otter.ps1's
//
//   param([Parameter(Position = 0)][string]$Path,
//         [Parameter(Position = 1)][string]$Target,
//         [switch]$Open, [switch]$NoOpen, [int]$Port = 0,
//         [switch]$DebugTokens, [switch]$DebugAst, [switch]$ParseOnly,
//         [switch]$DebugErrors, [string]$Breakpoints,
//         [Alias('version')][switch]$VersionFlag, [Alias('help')][switch]$HelpFlag,
//         [Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
//
// [Parameter()] makes otter.ps1 an advanced script, so PowerShell adds its
// common parameters (-Verbose, -Debug, -ErrorAction, ...). This class does
// what PowerShell does with the arguments, step by step:
//
// 1. powershell.exe -File turns each argument starting with a dash into a
//    parameter name ("-Name:value" and "-Name:" followed by the value also
//    carry a value); everything else is a plain argument.
// 2. "-?" anywhere shows otter.ps1's syntax (Get-Help) instead.
// 3. The binder pairs each known non-switch parameter with the argument
//    after it (ReparseUnboundArguments), binds named parameters, then
//    positional ones (Path, Target), and gives everything left to
//    $Arguments (ValueFromRemainingArguments). A parameter name matches by
//    any unique prefix of a name or alias, capitals ignored; when several
//    match, otter.ps1's own parameter wins over common ones.
// 4. Any failure is PowerShell's own error, written to the error stream,
//    and powershell.exe exits with 1 before otter.ps1 runs.
//
// Messages are Windows PowerShell 5.1's (no -ProgressAction, .NET
// Framework's conversion messages); PowerShell 7 differs in places
// (tests/cli/binding.psd1, KnownOnPwsh7).
internal sealed class CommandLine
{
    // otter.ps1's $script:OtterOwnSwitchFlags and $script:OtterOwnValueFlags:
    // the tokens Get-OtterRawTrailingArguments removes.
    static readonly string[] OwnSwitchFlags = { "-DebugTokens", "-DebugAst", "-ParseOnly", "-DebugErrors", "-Open", "-NoOpen", "-version", "-help" };
    static readonly string[] OwnValueFlags = { "-Breakpoints", "-Port" };

    CommandLine(string[] raw) => Raw = raw;

    public string[] Raw { get; }
    public string? Path { get; private set; }
    public string? Target { get; private set; }
    // $Arguments: null when PowerShell bound nothing to it.
    public string?[]? Arguments { get; private set; }
    public bool Open { get; private set; }
    public bool NoOpen { get; private set; }
    public int Port { get; private set; }
    public bool DebugTokens { get; private set; }
    public bool DebugAst { get; private set; }
    public bool ParseOnly { get; private set; }
    public bool DebugErrors { get; private set; }
    public string? Breakpoints { get; private set; }
    public bool Version { get; private set; }
    public bool Help { get; private set; }

    // Common parameters that change what otter.ps1 shows.
    public bool Verbose { get; private set; }
    public int? InformationAction { get; private set; }

    // PowerShell's "if ($Path)": a missing or empty string is false.
    public bool HasPath => !string.IsNullOrEmpty(Path);
    public bool HasTarget => !string.IsNullOrEmpty(Target);

    // ---- the parameters ---------------------------------------------------

    enum ParameterType { String, Int, Switch, StringArray, CommonSwitch, ActionPreference, VariableName, OutBuffer }

    sealed record Parameter(string Name, ParameterType Type, int Position = -1, string? Alias = null, bool Common = false);

    // In PowerShell's order: otter.ps1's own, then the common parameters of
    // Windows PowerShell 5.1.
    static readonly Parameter[] Parameters =
    {
        new("Path", ParameterType.String, Position: 0),
        new("Target", ParameterType.String, Position: 1),
        new("Open", ParameterType.Switch),
        new("NoOpen", ParameterType.Switch),
        new("Port", ParameterType.Int),
        new("DebugTokens", ParameterType.Switch),
        new("DebugAst", ParameterType.Switch),
        new("ParseOnly", ParameterType.Switch),
        new("DebugErrors", ParameterType.Switch),
        new("Breakpoints", ParameterType.String),
        new("VersionFlag", ParameterType.Switch, Alias: "version"),
        new("HelpFlag", ParameterType.Switch, Alias: "help"),
        new("Arguments", ParameterType.StringArray),
        new("Verbose", ParameterType.CommonSwitch, Alias: "vb", Common: true),
        new("Debug", ParameterType.CommonSwitch, Alias: "db", Common: true),
        new("ErrorAction", ParameterType.ActionPreference, Alias: "ea", Common: true),
        new("WarningAction", ParameterType.ActionPreference, Alias: "wa", Common: true),
        new("InformationAction", ParameterType.ActionPreference, Alias: "infa", Common: true),
        new("ErrorVariable", ParameterType.VariableName, Alias: "ev", Common: true),
        new("WarningVariable", ParameterType.VariableName, Alias: "wv", Common: true),
        new("InformationVariable", ParameterType.VariableName, Alias: "iv", Common: true),
        new("OutVariable", ParameterType.VariableName, Alias: "ov", Common: true),
        new("OutBuffer", ParameterType.OutBuffer, Alias: "ob", Common: true),
        new("PipelineVariable", ParameterType.VariableName, Alias: "pv", Common: true),
    };

    // SpecialCharacters.IsDash: "-" and the en dash, em dash and horizontal bar.
    static bool IsDash(char c) => c is '-' or '–' or '—' or '―';

    // MergedCommandParameterMetadata.GetMatchingParameter.
    static Parameter? Match(string name)
    {
        if (name.Length == 0)
        {
            // PSTraceSource.NewArgumentException("name"): PowerShell's own
            // failure for a parameter token with no name (a lone "-").
            throw new PowerShellError("", "Cannot process argument because the value of argument \"name\" is not valid. Change the value of the \"name\" argument and run the operation again.",
                null, "InvalidArgument", "", "ParentContainsErrorRecordException", "Argument");
        }
        if (IsDash(name[0])) name = name.Substring(1);
        var matches = new List<Parameter>();
        foreach (Parameter parameter in Parameters)
        {
            if (parameter.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                if (parameter.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return parameter;
                matches.Add(parameter);
            }
        }
        foreach (Parameter parameter in Parameters)
        {
            if (parameter.Alias != null && parameter.Alias.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                if (parameter.Alias.Equals(name, StringComparison.OrdinalIgnoreCase)) return parameter;
                if (!matches.Contains(parameter)) matches.Add(parameter);
            }
        }
        if (matches.Count > 1)
        {
            // Prefer otter.ps1's own parameters over common parameters.
            var own = matches.Where(p => !p.Common).ToList();
            if (own.Count == 1) return own[0];
            throw PowerShellError.Binding(
                $"Parameter cannot be processed because the parameter name '{name}' is ambiguous. Possible matches include:{string.Concat(matches.Select(p => " -" + p.Name))}.",
                "AmbiguousParameter");
        }
        return matches.Count == 1 ? matches[0] : null;
    }

    // ---- the arguments ----------------------------------------------------

    static readonly object SwitchPresent = new();

    // CommandParameterInternal: a parameter name (Name, as typed after the
    // dash, and Text, the token) with or without a value, or a plain argument.
    sealed class Argument
    {
        public string? Name;
        public string Text = "";
        public bool HasValue;
        public object? Value;
        public bool IsNamed => Name != null;
    }

    // powershell.exe -File: CommandLineParameterParser (5.1) and
    // CommandParameter.ToCommandParameterInternal.
    static List<Argument> Tokenize(string[] args)
    {
        var result = new List<Argument>();
        var namePattern = new Regex(@"^.\w+\:", RegexOptions.CultureInvariant);
        string? pendingParameter = null;
        foreach (string arg in args)
        {
            if (pendingParameter != null)
            {
                result.Add(Named(pendingParameter, arg, hasValue: true));
                pendingParameter = null;
            }
            else if (arg.Length > 0 && IsDash(arg[0]))
            {
                if (namePattern.IsMatch(arg))
                {
                    int offset = arg.IndexOf(':');
                    if (offset == arg.Length - 1) pendingParameter = arg.TrimEnd(':');
                    else result.Add(Named(arg.Substring(0, offset), arg.Substring(offset + 1), hasValue: true));
                }
                else
                {
                    result.Add(Named(arg, null, hasValue: false));
                }
            }
            else
            {
                result.Add(new Argument { HasValue = true, Value = arg });
            }
        }
        // A "-Name:" at the very end has no value and is dropped.
        return result;
    }

    static Argument Named(string name, string? value, bool hasValue)
    {
        int end = name.Length;
        while (end > 0 && char.IsWhiteSpace(name[end - 1])) end--;
        string text = name.Substring(0, end);
        bool hasColon = end > 0 && name[end - 1] == ':';
        string parameterName = text.Length == 0 ? "" : text.Substring(1, Math.Max(0, text.Length - (hasColon ? 2 : 1)));
        return new Argument { Name = parameterName, Text = text, HasValue = hasValue || hasColon, Value = value };
    }

    // ---- binding ----------------------------------------------------------

    public static CommandLine Bind(string[] args)
    {
        var line = new CommandLine(args);
        try
        {
            line.BindArguments(Tokenize(args));
        }
        catch (PowerShellError error)
        {
            Program.Trace("native: parameter binding failed");
            error.Write();
            throw new CliExit(1);
        }
        return line;
    }

    readonly Dictionary<string, object?> bound = new(StringComparer.OrdinalIgnoreCase);

    void BindArguments(List<Argument> arguments)
    {
        // "-?": Get-Help otter.ps1, before any binding.
        if (arguments.Any(a => a.Name == "?"))
        {
            Program.Trace("native: -? (otter.ps1's syntax)");
            Host.Line("otter.ps1 [[-Path] <string>] [[-Target] <string>] [-Open] [-NoOpen] [-Port <int>] [-DebugTokens] [-DebugAst] [-ParseOnly] [-DebugErrors] [-Breakpoints <string>] [-VersionFlag] [-HelpFlag] [-Arguments <string[]>] [<CommonParameters>]");
            Host.Line("");
            throw new CliExit(0);
        }

        // ReparseUnboundArguments: a known parameter without a value takes
        // the next argument (a switch takes none).
        var reparsed = new List<Argument>();
        for (int index = 0; index < arguments.Count; index++)
        {
            Argument argument = arguments[index];
            if (!argument.IsNamed || argument.HasValue) { reparsed.Add(argument); continue; }
            Parameter? parameter = Match(argument.Name!);
            if (parameter == null) { reparsed.Add(argument); continue; }
            if (parameter.Type is ParameterType.Switch or ParameterType.CommonSwitch)
            {
                argument.HasValue = true;
                argument.Value = SwitchPresent;
                reparsed.Add(argument);
                continue;
            }
            if (index + 1 >= arguments.Count) throw MissingArgument(parameter);
            Argument following = arguments[index + 1];
            if (following.IsNamed)
            {
                Parameter? followingParameter = Match(following.Name!);
                if (followingParameter != null || following.HasValue) throw MissingArgument(parameter);
                index++;
                argument.Name = parameter.Name;
                argument.HasValue = true;
                argument.Value = following.Text;
                reparsed.Add(argument);
                continue;
            }
            index++;
            argument.Name = parameter.Name;
            argument.HasValue = true;
            argument.Value = following.Value;
            reparsed.Add(argument);
        }

        // Named parameters, in order.
        var unbound = new List<Argument>();
        foreach (Argument argument in reparsed)
        {
            if (!argument.IsNamed) { unbound.Add(argument); continue; }
            Parameter? parameter = Match(argument.Name!);
            if (parameter == null) { unbound.Add(argument); continue; }
            if (bound.ContainsKey(parameter.Name))
            {
                throw PowerShellError.Binding(
                    $"Cannot bind parameter because parameter '{argument.Name}' is specified more than once. To provide multiple values to parameters that can accept multiple values, use the array syntax. For example, \"-parameter value1,value2,value3\".",
                    "ParameterAlreadyBound");
            }
            BindValue(parameter, argument.Value);
        }

        // Positional parameters: Path (0), then Target (1), whichever is
        // still unbound. A parameter name that matched nothing keeps the
        // plain argument after it as its value (GetNextPositionalArgument).
        var leftover = new List<Argument>();
        int next = 0;
        foreach (Parameter positional in Parameters.Where(p => p.Position >= 0 && !bound.ContainsKey(p.Name)))
        {
            Argument? found = null;
            while (next < unbound.Count)
            {
                Argument candidate = unbound[next++];
                if (!candidate.IsNamed) { found = candidate; break; }
                leftover.Add(candidate);
                if (next < unbound.Count && !unbound[next].IsNamed) leftover.Add(unbound[next++]);
            }
            if (found == null) break;
            BindValue(positional, found.Value);
        }
        for (; next < unbound.Count; next++) leftover.Add(unbound[next]);

        // ValueFromRemainingArguments: each leftover parameter's text, then its value.
        if (leftover.Count > 0)
        {
            if (!bound.ContainsKey("Arguments"))
            {
                var remaining = new List<string?>();
                foreach (Argument argument in leftover)
                {
                    if (argument.IsNamed) remaining.Add(argument.Text);
                    if (argument.HasValue) remaining.Add(argument.Value as string);
                }
                bound["Arguments"] = remaining.ToArray();
            }
            else
            {
                Argument first = leftover[0];
                if (first.IsNamed)
                {
                    throw PowerShellError.Binding($"A parameter cannot be found that matches parameter name '{first.Name}'.", "NamedParameterNotFound");
                }
                throw PowerShellError.Binding($"A positional parameter cannot be found that accepts argument '{first.Value}'.", "PositionalParameterNotFound");
            }
        }

        Path = bound.GetValueOrDefault("Path") as string;
        Target = bound.GetValueOrDefault("Target") as string;
        Arguments = bound.GetValueOrDefault("Arguments") as string?[];
        Open = IsSet("Open");
        NoOpen = IsSet("NoOpen");
        Port = bound.GetValueOrDefault("Port") as int? ?? 0;
        DebugTokens = IsSet("DebugTokens");
        DebugAst = IsSet("DebugAst");
        ParseOnly = IsSet("ParseOnly");
        DebugErrors = IsSet("DebugErrors");
        Breakpoints = bound.GetValueOrDefault("Breakpoints") as string;
        Version = IsSet("VersionFlag");
        Help = IsSet("HelpFlag");
        Verbose = IsSet("Verbose");
        InformationAction = bound.GetValueOrDefault("InformationAction") as int?;
    }

    bool IsSet(string name) => bound.GetValueOrDefault(name) is true;

    static PowerShellError MissingArgument(Parameter parameter) =>
        PowerShellError.Binding(
            $"Missing an argument for parameter '{parameter.Name}'. Specify a parameter of type '{(parameter.Type == ParameterType.Int ? "System.Int32" : "System.String")}' and try again.",
            "MissingArgument");

    // ParameterBinderBase.BindParameter: the value converted to the
    // parameter's type, then validated.
    void BindValue(Parameter parameter, object? value)
    {
        string name = parameter.Name;
        string? text = value as string;
        switch (parameter.Type)
        {
            case ParameterType.Switch:
                // [switch] in a script: ArgumentTypeConverterAttribute.
                if (value == SwitchPresent || value == null) { bound[name] = value == SwitchPresent; return; }
                throw PowerShellError.Binding(
                    $"Cannot process argument transformation on parameter '{name}'. Cannot convert value \"System.String\" to type \"System.Management.Automation.SwitchParameter\". Boolean parameters accept only Boolean values and numbers, such as $True, $False, 1 or 0.",
                    "ParameterArgumentTransformationError", "InvalidData");
            case ParameterType.CommonSwitch:
                if (value == SwitchPresent || value == null) { bound[name] = value == SwitchPresent; return; }
                throw PowerShellError.Binding(
                    $"Cannot convert 'System.String' to the type 'System.Management.Automation.SwitchParameter' required by parameter '{name}'. ",
                    "CannotConvertArgument");
            case ParameterType.String:
                bound[name] = text;
                return;
            case ParameterType.StringArray:
                bound[name] = new[] { text };
                return;
            case ParameterType.Int:
            {
                if (!PowerShellConvert.TryToInt32(text ?? "", out int number, out string error))
                {
                    throw PowerShellError.Binding(
                        $"Cannot process argument transformation on parameter '{name}'. Cannot convert value \"{text}\" to type \"System.Int32\". Error: \"{error}\"",
                        "ParameterArgumentTransformationError", "InvalidData");
                }
                bound[name] = number;
                return;
            }
            case ParameterType.OutBuffer:
            {
                if (!PowerShellConvert.TryToInt32(text ?? "", out int number, out string error))
                {
                    throw PowerShellError.Binding(
                        $"Cannot bind parameter '{name}'. Cannot convert value \"{text}\" to type \"System.Int32\". Error: \"{error}\"",
                        "CannotConvertArgumentNoMessage");
                }
                if (number < 0)
                {
                    throw PowerShellError.Binding(
                        $"Cannot validate argument on parameter '{name}'. The {number} argument is less than the minimum allowed range of 0. Supply an argument that is greater than or equal to 0 and then try the command again.",
                        "ParameterArgumentValidationError", "InvalidData");
                }
                bound[name] = number;
                return;
            }
            case ParameterType.ActionPreference:
            {
                if (!PowerShellConvert.TryToActionPreference(text ?? "", out int preference, out string error))
                {
                    throw PowerShellError.Binding($"Cannot bind parameter '{name}'. {error}", "CannotConvertArgumentNoMessage");
                }
                if (preference == 5)
                {
                    // MshCommandRuntime refuses Suspend outside workflows.
                    string reason = name == "ErrorAction"
                        ? "The error action preference of \"Suspend\" is supported only on workflows."
                        : "The action preference of \"Suspend\" is supported only for ErrorAction.";
                    throw PowerShellError.Binding($"Cannot bind parameter '{name}' to the target. Exception setting \"{name}\": \"{reason}\"", "ParameterBindingFailed", "WriteError");
                }
                bound[name] = preference;
                return;
            }
            case ParameterType.VariableName:
            {
                if (string.IsNullOrEmpty(text))
                {
                    throw PowerShellError.Binding(
                        $"Cannot validate argument on parameter '{name}'. Cannot process argument because the value of argument \"path\" is not valid. Change the value of the \"path\" argument and run the operation again.",
                        "ParameterArgumentValidationError", "InvalidData");
                }
                if (!PowerShellConvert.IsVariableName(text))
                {
                    throw PowerShellError.Binding(
                        $"Cannot validate argument on parameter '{name}'. Cannot validate argument '{text}' because it is not a valid variable name.",
                        "ParameterArgumentValidationError", "InvalidData");
                }
                bound[name] = text;
                return;
            }
        }
    }

    // PowerShell's truth of an array: empty is false, one element is that
    // element's truth ("" and null are false), more than one is true.
    public static bool IsTrue(string?[]? values) => values != null && (values.Length > 1 || (values.Length == 1 && !string.IsNullOrEmpty(values[0])));

    // Get-OtterRawTrailingArguments: the real command line after the first
    // `skip` tokens (whatever they are - flags written before the command
    // count too), without otter.ps1's own flags and their values.
    public string[] RawTrailing(int skip)
    {
        var result = new List<string>();
        int index = 0;
        int skipRemaining = skip;
        while (index < Raw.Length)
        {
            string token = Raw[index];
            index++;
            if (skipRemaining > 0) { skipRemaining--; continue; }
            if (OwnSwitchFlags.Contains(token, StringComparer.OrdinalIgnoreCase)) continue;
            if (OwnValueFlags.Contains(token, StringComparer.OrdinalIgnoreCase)) { index++; continue; }
            result.Add(token);
        }
        return result.ToArray();
    }
}
