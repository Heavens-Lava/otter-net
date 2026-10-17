using System.Globalization;

namespace Otter.Cli;

// otter.ps1's command line as PowerShell's parameter binder hands it over:
//
//   param([Parameter(Position = 0)][string]$Path,
//         [Parameter(Position = 1)][string]$Target,
//         [switch]$Open, [switch]$NoOpen, [int]$Port = 0,
//         [switch]$DebugTokens, [switch]$DebugAst, [switch]$ParseOnly,
//         [switch]$DebugErrors, [string]$Breakpoints,
//         [Alias('version')][switch]$VersionFlag, [Alias('help')][switch]$HelpFlag,
//         [Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
//
// Parameter names match whatever their capitals, and `--version`/`--help`
// reach the aliases (D57). Every other token that starts with "-" goes
// through rules otter-net does not reproduce (PowerShell's common parameters
// such as -Verbose and -OutVariable, unique-prefix matching, ambiguity
// errors, -Name:value), so such a command line is handed to the PowerShell
// Otter as a whole.
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
    public List<string> Arguments { get; } = new();
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

    // PowerShell's "if ($Path)": a missing or empty string is false.
    public bool HasPath => !string.IsNullOrEmpty(Path);
    public bool HasTarget => !string.IsNullOrEmpty(Target);

    public static CommandLine Bind(string[] args)
    {
        var line = new CommandLine(args);
        var positional = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (!token.StartsWith('-'))
            {
                positional.Add(token);
                continue;
            }
            switch (token.ToLowerInvariant())
            {
                case "-open": line.Open = true; break;
                case "-noopen": line.NoOpen = true; break;
                case "-debugtokens": line.DebugTokens = true; break;
                case "-debugast": line.DebugAst = true; break;
                case "-parseonly": line.ParseOnly = true; break;
                case "-debugerrors": line.DebugErrors = true; break;
                case "-version": case "--version": case "-versionflag": line.Version = true; break;
                case "-help": case "--help": case "-helpflag": line.Help = true; break;
                case "-port":
                {
                    // [int]: a missing or non-numeric value is PowerShell's own binder error.
                    if (i + 1 >= args.Length || !IsPlainInteger(args[i + 1], out int port))
                    {
                        throw new NeedsPowerShell("-Port without a whole number is reported by PowerShell's parameter binder");
                    }
                    line.Port = port;
                    i++;
                    break;
                }
                case "-breakpoints":
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                    {
                        throw new NeedsPowerShell("-Breakpoints without a value is reported by PowerShell's parameter binder");
                    }
                    line.Breakpoints = args[i + 1];
                    i++;
                    break;
                }
                default:
                    throw new NeedsPowerShell($"the argument \"{token}\" goes through PowerShell's parameter binder");
            }
        }
        if (positional.Count > 0) line.Path = positional[0];
        if (positional.Count > 1) line.Target = positional[1];
        for (int i = 2; i < positional.Count; i++) line.Arguments.Add(positional[i]);
        return line;
    }

    static bool IsPlainInteger(string text, out int value)
    {
        value = 0;
        string trimmed = text.Trim();
        if (trimmed.Length == 0 || !trimmed.All(c => c is >= '0' and <= '9')) return false;
        return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

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
