# otter.ps1's parameters as PowerShell binds them: unknown dash arguments,
# unique prefixes, ambiguity, -Name:value, [int] conversion, common
# parameters, positional binding, "-?". See src/Otter.Cli/CommandLine.cs.
#
# Binding errors are PowerShell's own, on the error stream: Windows
# PowerShell 5.1 shows them in its NormalView ("C:\...\otter.ps1 : message"
# and two "+" lines), PowerShell 7 in its ConciseView ("otter.ps1: message"),
# so every error case is a known PowerShell 7 difference here; the exit code
# and standard output are still compared.
@{
    Cases = @(
        # Unknown parameter names go to $Arguments; the program gets the raw line.
        @{ Name = 'unknown -x after the file'; Args = @('a.ot', '-x'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = 'unknown --name and its value'; Args = @('a.ot', '--name', 'v'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = 'a negative number'; Args = @('a.ot', '-5', 'x'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = 'unknown name before the command swallows it'; Args = @('-x', 'run', 'a.ot'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = 'unknown name before the file: the REPL'; Args = @('-x', 'a.ot'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = 'unknown name with a colon value'; Args = @('-x:1', 'a.ot', 'k'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = '-Path by name'; Args = @('-Path', 'a.ot', 'k'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = '-Target by name, Path positional'; Args = @('-Target', 'b.ot', 'run'); Files = @{ 'b.ot' = 'say "b"' } }
        @{ Name = '-Arguments by name'; Args = @('a.ot', '-Arguments', 'x', 'y'); Files = @{ 'a.ot' = 'say arguments' } }
        # Prefixes, aliases and otter.ps1's parameters preferred over common ones.
        @{ Name = '-v is -VersionFlag'; Args = @('-v') }
        @{ Name = '-ver'; Args = @('-ver') }
        @{ Name = '-he is -HelpFlag'; Args = @('-he') }
        @{ Name = '-o is -Open'; Args = @('a.ot', '-o'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = '-DebugT prefix'; Args = @('a.ot', '-DebugT'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = '-PARSEONLY capitals'; Args = @('a.ot', '-PARSEONLY'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = '-Debug does nothing visible'; Args = @('a.ot', '-Debug'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = 'common parameters with values'; Args = @('a.ot', '-ErrorAction', 'Continue', '-ov', 'x', '-OutBuffer', '2', '-wa', 'stop,continue', '-pv', 'global:p'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = '-InformationAction SilentlyContinue'; Args = @('a.ot', '-InformationAction', 'SilentlyContinue'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = '-Verbose with check'; Args = @('check', 'a.ot', '-Verbose'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = '-Verbose with new'; Args = @('new', 'console', 'app', '-Verbose'); KnownOnPwsh7 = 'byte order mark and \r\n (5.1)' }
        # -Port: [int], as PowerShell converts a string.
        @{ Name = '-Port:5'; Args = @('a.ot', '-Port:5'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = '-Port: 5'; Args = @('a.ot', '-Port:', '5'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = '-Port: at the end is dropped'; Args = @('a.ot', '-Port:'); Files = @{ 'a.ot' = 'say arguments' } }
        @{ Name = '-Port hexadecimal'; Args = @('a.ot', '-Port', '0x10'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = '-po 2.5 is rounded'; Args = @('a.ot', '-po', '2.5'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = '-Port 1,000'; Args = @('a.ot', '-Port', '1,000'); Files = @{ 'a.ot' = 'say 1' } }
        # Binding errors (exit 1, nothing on standard output).
        @{ Name = 'ambiguous -Deb'; Args = @('-Deb'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'ambiguous -p (no -ProgressAction on 5.1)'; Args = @('a.ot', '-p', '3'); KnownOnPwsh7 = 'error view; PowerShell 7 also lists -ProgressAction' }
        @{ Name = 'ambiguous -i'; Args = @('a.ot', '-i', 'x'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = '-- matches every parameter'; Args = @('a.ot', '--'); KnownOnPwsh7 = 'error view; PowerShell 7 also lists -ProgressAction' }
        @{ Name = '-Port without a value'; Args = @('a.ot', '-Port'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = '-Port followed by a parameter'; Args = @('a.ot', '-Port', '-Open'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = '-Port abc'; Args = @('a.ot', '-Port', 'abc'); KnownOnPwsh7 = 'error view; .NET Framework says "Input string was not in a correct format."' }
        @{ Name = '-Port too large'; Args = @('a.ot', '-Port', '99999999999'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = '-Port 0x-1'; Args = @('a.ot', '-Port', '0x-1'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = '-Port blank'; Args = @('a.ot', '-Port', ' '); KnownOnPwsh7 = '.NET Framework fails on a blank number ("Index was outside the bounds of the array."), .NET gives 0' }
        @{ Name = '-Breakpoints without a value'; Args = @('debug', 'a.ot', '-Breakpoints'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = '-Breakpoints -Port'; Args = @('debug', 'a.ot', '-Breakpoints', '-Port', '3'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = '-Path twice'; Args = @('-Path', 'a.ot', '-Path', 'b'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = '--version twice'; Args = @('--version', '-version'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'a switch with a text value'; Args = @('a.ot', '-Open:x'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'a switch with true'; Args = @('a.ot', '-DebugTokens:true'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'PowerShell 7 -File turns "true" and "$true" into a Boolean; 5.1 passes the text, which a switch refuses' }
        @{ Name = '-Verbose with a value'; Args = @('a.ot', '-Verbose:x'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = '-ErrorAction foo'; Args = @('a.ot', '-ErrorAction', 'foo'); KnownOnPwsh7 = 'error view; PowerShell 7 also lists Break' }
        @{ Name = '-ErrorAction 9'; Args = @('a.ot', '-ea', '9'); KnownOnPwsh7 = 'error view; PowerShell 7 also lists Break' }
        @{ Name = '-ErrorAction Suspend'; Args = @('a.ot', '-ea', 'Suspend'); KnownOnPwsh7 = 'error view; PowerShell 7 calls Suspend "reserved for future use"' }
        @{ Name = '-OutBuffer -1'; Args = @('a.ot', '-ob', '-1'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = '-OutVariable a drive path'; Args = @('a.ot', '-ov', 'env:x'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = '-Arguments and another parameter'; Args = @('a.ot', '-Arguments', 'x', '-y'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = '-Arguments and another argument'; Args = @('-Arguments', 'x', 'a.ot', 'b', 'c'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'a lone dash'; Args = @('a.ot', '-'); Files = @{ 'a.ot' = 'say arguments' }; KnownOnPwsh7 = 'PowerShell 7 -File passes "-" as an argument; 5.1 makes it a parameter with no name and fails' }
        # -? shows otter.ps1's syntax.
        @{ Name = '-?'; Args = @('-?') }
        @{ Name = '-? after a command'; Args = @('run', 'a.ot', '-?') }
        # otter debug: each breakpoint is [int]($_.Trim()).
        @{ Name = 'debug: a breakpoint 1.5 is 2'; Args = @('debug', 'a.ot', '-Breakpoints', '1.5'); Files = @{ 'a.ot' = "say 1`nsay 2" } }
        @{ Name = 'debug: a breakpoint that is not a number'; Args = @('debug', 'a.ot', '-Breakpoints', '1,x'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView); .NET Framework wording' }
        @{ Name = 'debug: a breakpoint that is a parameter'; Args = @('debug', 'a.ot', '-Breakpoints', '-x'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView); .NET Framework wording' }
        # otter publish --output.
        @{ Name = 'publish . --output out'; Args = @('publish', '.', '--output', 'out'); KnownOnPwsh7 = 'packaged files (5.1 layout)'
            Mask = @('[0-9a-f]{64}')
            IgnoreFiles = @('out/*.zip', 'out/*.sha256', 'out/otter.publish.json', 'out/*/otter.publish.json')
            Files = @{ 'otter.json' = '{ "name": "app", "version": "1.0.0", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'publish --output out: the binder swallows both'; Args = @('publish', '--output', 'out'); KnownOnPwsh7 = 'packaged files (5.1 layout)'
            Mask = @('[0-9a-f]{64}')
            IgnoreFiles = @('publish/*.zip', 'publish/*.sha256', 'publish/otter.publish.json', 'publish/*/otter.publish.json')
            Files = @{ 'otter.json' = '{ "name": "app", "version": "1.0.0", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'publish -Output outside the project'; Args = @('publish', '.', '-Output', '../x')
            Files = @{ 'otter.json' = '{ "name": "app", "version": "1.0.0", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'publish -Target:--output: the folder from $Arguments'; Args = @('publish', '-Target:--output', 'out'); KnownOnPwsh7 = 'packaged files (5.1 layout)'
            Mask = @('[0-9a-f]{64}')
            IgnoreFiles = @('out/*.zip', 'out/*.sha256', 'out/otter.publish.json', 'out/*/otter.publish.json')
            Files = @{ 'otter.json' = '{ "name": "app", "version": "1.0.0", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
    )
}
