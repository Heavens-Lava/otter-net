# Texts Windows PowerShell 5.1 itself prints for otterPS, measured in CI
# (.github/workflows/cli-windows.yml, docs/SPEC-QUESTIONS.md C2-5 and F5):
# -InformationAction Stop and Ignore (every Write-Host fails), -Verbose (the
# VERBOSE lines of otterPS's Import-Module calls), and the errors of `otter
# serve` and `otter desktop` that end otter.ps1. `otter desktop` runs only on
# Windows; elsewhere both sides refuse it alike. Nothing here opens a browser
# or a window: the desktop cases all fail before that, and the servers that
# start are stopped by the time limit.
@{
    Cases = @(
        # -InformationAction Stop and Ignore.
        @{ Name = 'IA: --version Stop'; Args = @('--version', '-InformationAction', 'Stop'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: --version Ignore'; Args = @('--version', '-InformationAction', 'Ignore'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: -InformationAction:Ignore'; Args = @('--version', '-InformationAction:Ignore'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: -infa 1 is Stop'; Args = @('--version', '-infa', '1'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: help Stop'; Args = @('help', '-informationaction', 'stop'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: --help Ignore'; Args = @('--help', '-InformationAction', 'Ignore'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: SilentlyContinue changes nothing'; Args = @('a.ot', '-InformationAction', 'SilentlyContinue'); Files = @{ 'a.ot' = "say 1`nsay 2" } }
        @{ Name = 'IA: Continue changes nothing'; Args = @('a.ot', '-infa', 'Continue'); Files = @{ 'a.ot' = "say 1`nsay 2" } }
        @{ Name = 'IA: say Stop'; Args = @('a.ot', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = "say 1`nsay `"two`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: say Ignore'; Args = @('a.ot', '-InformationAction', 'Ignore'); Files = @{ 'a.ot' = "say 1`nsay `"two`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: say Stop, interpreter'; Args = @('a.ot', '-InformationAction', 'Stop'); Env = @{ OTTER_ENGINE = 'interpreter' }; Files = @{ 'a.ot' = "say 1`nsay `"two`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: say Ignore, interpreter'; Args = @('a.ot', '-InformationAction', 'Ignore'); Env = @{ OTTER_ENGINE = 'interpreter' }; Files = @{ 'a.ot' = "say 1`nsay `"two`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: try catches a say, Stop'; Args = @('t.ot', '-InformationAction', 'Stop'); Files = @{ 't.ot' = "try`n    say 1`notherwise into reason`n    say reason`n.`nsay 2" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: try catches a say, Ignore'; Args = @('t.ot', '-InformationAction', 'Ignore'); Files = @{ 't.ot' = "try`n    say 1`notherwise into reason`n    say 2`n.`nsay 3" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: try catches a say, Ignore, interpreter'; Args = @('t.ot', '-InformationAction', 'Ignore'); Env = @{ OTTER_ENGINE = 'interpreter' }; Files = @{ 't.ot' = "try`n    say 1`notherwise into reason`n    x is reason`n.`nsay x" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: a long line, Stop'; Args = @('a.ot', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'say "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: a silent program, Ignore'; Args = @('a.ot', '-InformationAction', 'Ignore'); Files = @{ 'a.ot' = 'x is 1' } }
        @{ Name = 'IA: check Stop'; Args = @('check', 'a.ot', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: check Ignore'; Args = @('check', 'a.ot', '-InformationAction', 'Ignore'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: -ParseOnly Stop'; Args = @('a.ot', '-ParseOnly', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: -DebugTokens Stop'; Args = @('a.ot', '-DebugTokens', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: -DebugTokens of a syntax error, Ignore'; Args = @('bad.ot', '-DebugTokens', '-InformationAction', 'Ignore'); Files = @{ 'bad.ot' = 'say (' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: -DebugAst Ignore'; Args = @('a.ot', '-DebugAst', '-InformationAction', 'Ignore'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: a syntax error, Stop'; Args = @('bad.ot', '-InformationAction', 'Stop'); Files = @{ 'bad.ot' = 'say (' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: a syntax error, Ignore'; Args = @('bad.ot', '-InformationAction', 'Ignore'); Files = @{ 'bad.ot' = 'say (' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: a runtime error, Stop'; Args = @('r.ot', '-InformationAction', 'Stop'); Files = @{ 'r.ot' = "say 1`nsay 1 / 0" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: a silent runtime error, Ignore'; Args = @('r.ot', '-InformationAction', 'Ignore'); Files = @{ 'r.ot' = "x is 1 / 0" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: an empty program, Stop'; Args = @('e.ot', '-InformationAction', 'Stop'); Files = @{ 'e.ot' = '' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: a missing import, Stop'; Args = @('i.ot', '-InformationAction', 'Stop'); Files = @{ 'i.ot' = "use `"m.ot`"`nsay 1" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: unknown command, Stop'; Args = @('nope', '-InformationAction', 'Stop'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: unknown command, Ignore'; Args = @('nope', '-InformationAction', 'Ignore'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: a folder with no manifest, Stop'; Args = @('sub', '-InformationAction', 'Stop'); Files = @{ 'sub/' = '' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: run of a missing file, Stop'; Args = @('run', 'missing.ot', '-InformationAction', 'Stop'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: run of a folder, Stop'; Args = @('dir.ot', '-InformationAction', 'Stop'); Files = @{ 'dir.ot/' = '' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: run of a text file, Stop'; Args = @('run', 'x.txt', '-InformationAction', 'Stop'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: run without a file, Ignore'; Args = @('run', '-InformationAction', 'Ignore'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: run of a folder with no manifest, Stop'; Args = @('run', 'sub', '-InformationAction', 'Stop'); Files = @{ 'sub/' = '' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: a project, Stop'; Args = @('.', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: run of a bad manifest, Stop'; Args = @('run', '.', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "entryPoint": 5 }' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: a bad manifest, Ignore'; Args = @('.', '-InformationAction', 'Ignore'); Files = @{ 'otter.json' = '{ "entryPoint": 5 }' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: the REPL, Stop'; Args = @('-infa', 'Stop'); Stdin = "say 1`n"; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: the REPL, Ignore'; Args = @('-InformationAction', 'Ignore'); Stdin = "say 1`nexit`n"; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: new, Stop'; Args = @('new', 'console', 'app', '-InformationAction', 'Stop'); KnownOnPwsh7 = 'error view (5.1 NormalView); byte order mark and \r\n (5.1)' }
        @{ Name = 'IA: new, Ignore'; Args = @('new', 'console', 'app', '-InformationAction', 'Ignore'); KnownOnPwsh7 = 'byte order mark and \r\n (5.1)' }
        @{ Name = 'IA: new without a type, Stop'; Args = @('new', '-InformationAction', 'Stop'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: new into a full folder, Stop'; Args = @('new', 'console', 'app', '-InformationAction', 'Stop'); Files = @{ 'app/x.txt' = 'x' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: new into a full folder, Ignore'; Args = @('new', 'console', 'app', '-InformationAction', 'Ignore'); Files = @{ 'app/x.txt' = 'x' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: web, Stop'; Args = @('web', 'p.ot', '-NoOpen', '-InformationAction', 'Stop'); Files = @{ 'p.ot' = 'heading "Hi"' }; KnownOnPwsh7 = 'error view (5.1 NormalView); the page (5.1 form)' }
        @{ Name = 'IA: browse, Ignore'; Args = @('browse', 'p.ot', '-NoOpen', '-InformationAction', 'Ignore'); Files = @{ 'p.ot' = 'heading "Hi"' }; KnownOnPwsh7 = 'error view (5.1 NormalView); the page (5.1 form)' }
        @{ Name = 'IA: web of a syntax error, Stop'; Args = @('web', 'bad.ot', '-NoOpen', '-InformationAction', 'Stop'); Files = @{ 'bad.ot' = 'say (' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: web of a syntax error, Ignore'; Args = @('web', 'bad.ot', '-NoOpen', '-InformationAction', 'Ignore'); Files = @{ 'bad.ot' = 'say (' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: web of a missing import, Stop'; Args = @('web', 'i.ot', '-NoOpen', '-InformationAction', 'Stop'); Files = @{ 'i.ot' = "use `"m.ot`"`nheading `"x`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: build, Stop'; Args = @('build', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: build, Ignore'; Args = @('build', '-InformationAction', 'Ignore'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: build without a manifest, Stop'; Args = @('build', '-InformationAction', 'Stop'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: build without a manifest, Ignore'; Args = @('build', '-InformationAction', 'Ignore'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: publish, Stop'; Args = @('publish', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "name": "c", "version": "1.0.0", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: publish, Ignore'; Args = @('publish', '-InformationAction', 'Ignore'); Files = @{ 'otter.json' = '{ "name": "c", "version": "1.0.0", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: publish without a name, Stop'; Args = @('publish', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: test with no tests, Stop'; Args = @('test', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: test, Stop'; Args = @('test', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1'; 'tests/a_test.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: test, Ignore'; Args = @('test', '-InformationAction', 'Ignore'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1'; 'tests/a_test.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: profile, Ignore'; Args = @('profile', 'a.ot', '-InformationAction', 'Ignore'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: profile of a silent program, Stop'; Args = @('profile', 'a.ot', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'x is 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: profile without a file, Stop'; Args = @('profile', '-InformationAction', 'Stop'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: debug, Stop'; Args = @('debug', 'a.ot', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: debug of a silent program, Ignore'; Args = @('debug', 'a.ot', '-InformationAction', 'Ignore'); Files = @{ 'a.ot' = 'x is 1' } }
        @{ Name = 'IA: serve without a file, Stop'; Args = @('serve', '-InformationAction', 'Stop'); KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: serve of a syntax error, Stop'; Args = @('serve', 'bad.ot', '-InformationAction', 'Stop'); Files = @{ 'bad.ot' = 'say (' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: serve, Stop'; Args = @('serve', 's.ot', '-Port', '18190', '-InformationAction', 'Stop'); TimeoutSeconds = 20; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: serve, Ignore'; Args = @('serve', 's.ot', '-Port', '18191', '-InformationAction', 'Ignore'); TimeoutSeconds = 20; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: serve of an empty program, Stop'; Args = @('serve', 'e.ot', '-InformationAction', 'Stop'); Files = @{ 'e.ot' = '' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: desktop of a missing import, Stop'; Args = @('desktop', 'i.ot', '-InformationAction', 'Stop'); Files = @{ 'i.ot' = "use `"m.ot`"`nheading `"x`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'IA: -Verbose and Stop'; Args = @('a.ot', '-Verbose', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView); VERBOSE lines of PowerShell 7' }
        # -Verbose: the VERBOSE lines of otterPS's Import-Module calls.
        @{ Name = 'V: run'; Args = @('a.ot', '-Verbose'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: run -vb'; Args = @('run', 'a.ot', '-vb'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: run with the interpreter'; Args = @('a.ot', '-Verbose'); Env = @{ OTTER_ENGINE = 'interpreter' }; Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V: run of a program the compiled engine refuses'; Args = @('a.ot', '-Verbose'); Files = @{ 'a.ot' = "create card into c`nsay 1" }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: run of a syntax error'; Args = @('bad.ot', '-Verbose'); Files = @{ 'bad.ot' = 'say (' } }
        @{ Name = 'V: run of a runtime error'; Args = @('r.ot', '-Verbose'); Files = @{ 'r.ot' = "say 1`nsay 1 / 0" }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: run -DebugTokens'; Args = @('a.ot', '-Verbose', '-DebugTokens'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: run -DebugAst'; Args = @('a.ot', '-DebugAst', '-Verbose'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V: run -ParseOnly'; Args = @('a.ot', '-ParseOnly', '-Verbose'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V: run of an empty program'; Args = @('e.ot', '-Verbose'); Files = @{ 'e.ot' = '' } }
        @{ Name = 'V: run of a missing import'; Args = @('i.ot', '-Verbose'); Files = @{ 'i.ot' = "use `"m.ot`"`nsay 1" } }
        @{ Name = 'V: run of a missing file'; Args = @('run', 'missing.ot', '-Verbose') }
        @{ Name = 'V: a project'; Args = @('.', '-vb'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: -Verbose:$false'; Args = @('a.ot', '-Verbose:$false'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'PowerShell 7 -File turns "$false" into a Boolean; 5.1 passes the text, which a switch refuses' }
        @{ Name = 'V: --version'; Args = @('--version', '-Verbose') }
        @{ Name = 'V: the REPL'; Args = @('-Verbose'); Stdin = "say 1`nexit`n" }
        @{ Name = 'V: profile'; Args = @('profile', 'a.ot', '-Verbose'); Files = @{ 'a.ot' = 'say 1' }; Mask = @('\s*-?[0-9][0-9. ]*(GB|MB|KB|B)?', '(?s)Hottest lines.*'); KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: profile of a missing file'; Args = @('profile', 'nope.ot', '-Verbose'); KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: debug'; Args = @('debug', 'a.ot', '-Verbose'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: debug with a bad breakpoint'; Args = @('debug', 'a.ot', '-Breakpoints', 'x', '-Verbose'); Files = @{ 'a.ot' = 'say 1' }; KnownOnPwsh7 = 'error view (5.1 NormalView); .NET Framework wording' }
        @{ Name = 'V: build'; Args = @('build', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: build of a web project'; Args = @('build', '-Verbose'); Mask = @('_staging_[0-9a-f]+(\r\n)?[0-9a-f]*'); Files = @{ 'otter.json' = '{ "entryPoint": "app.ot", "target": "web" }'; 'app.ot' = 'heading "Hi"' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7; built files (5.1 layout)' }
        @{ Name = 'V: build of a desktop project'; Args = @('build', '-Verbose'); Mask = @('_staging_[0-9a-f]+(\r\n)?[0-9a-f]*'); Files = @{ 'otter.json' = '{ "name": "d", "version": "1.0.0", "entryPoint": "app.ot", "target": "desktop" }'; 'app.ot' = 'heading "Hi"' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7; built files (5.1 layout)' }
        @{ Name = 'V: build of a server project'; Args = @('build', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot", "target": "server" }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: build without a manifest'; Args = @('build', '-Verbose'); KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: build of a syntax error'; Args = @('build', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say (' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: publish'; Args = @('publish', '-Verbose'); KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7; packaged files (5.1 layout)'
            Mask = @('[0-9a-f]{64}')
            IgnoreFiles = @('publish/*.zip', 'publish/*.sha256', 'publish/otter.publish.json', 'publish/*/otter.publish.json')
            Files = @{ 'otter.json' = '{ "name": "c", "version": "1.0.0", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'V: publish of a web project'; Args = @('publish', '-Verbose'); KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7; packaged files (5.1 layout)'
            Mask = @('[0-9a-f]{64}', '_staging_[0-9a-f]+(\r\n)?[0-9a-f]*')
            IgnoreFiles = @('publish/*.zip', 'publish/*.sha256', 'publish/otter.publish.json', 'publish/*/otter.publish.json')
            Files = @{ 'otter.json' = '{ "name": "w", "version": "1.0.0", "entryPoint": "app.ot", "target": "web" }'; 'app.ot' = 'heading "Hi"' } }
        @{ Name = 'V: publish without a name'; Args = @('publish', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'V: test'; Args = @('test', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1'; 'tests/a_test.ot' = 'say 1' } }
        @{ Name = 'V: check'; Args = @('check', 'a.ot', '-vb'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V: web'; Args = @('web', 'p.ot', '-NoOpen', '-Verbose'); Files = @{ 'p.ot' = 'heading "Hi"' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7; the page (5.1 form)' }
        @{ Name = 'V: web, a long path'; Args = @('web', 'a-very-long-folder-name-for-wrapping-the-verbose-line/page-with-a-long-name.ot', '-NoOpen', '-Verbose'); Files = @{ 'a-very-long-folder-name-for-wrapping-the-verbose-line/page-with-a-long-name.ot' = 'heading "Hi"' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7; the page (5.1 form)' }
        @{ Name = 'V: web of a project'; Args = @('web', '.', '-NoOpen', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "app.ot", "target": "web" }'; 'app.ot' = 'heading "Hi"' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7; the page (5.1 form)' }
        @{ Name = 'V: web of a syntax error'; Args = @('web', 'bad.ot', '-NoOpen', '-Verbose'); Files = @{ 'bad.ot' = 'say (' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: web of a missing import'; Args = @('web', 'i.ot', '-NoOpen', '-Verbose'); Files = @{ 'i.ot' = "use `"m.ot`"`nheading `"x`"" }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: serve of a syntax error'; Args = @('serve', 'bad.ot', '-Verbose'); Files = @{ 'bad.ot' = 'say (' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: serve of an empty program'; Args = @('serve', 'e.ot', '-Verbose'); Files = @{ 'e.ot' = '' }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7; error view (5.1 NormalView)' }
        @{ Name = 'V: serve'; Args = @('serve', 's.ot', '-Port', '18192', '-Verbose'); TimeoutSeconds = 10; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
        @{ Name = 'V: serve on a port in use'; Args = @('serve', 's.ot', '-Port', '18193', '-Verbose'); HoldPort = 18193; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7; error view (5.1 NormalView), .NET Framework wording' }
        @{ Name = 'V: desktop of a missing import'; Args = @('desktop', 'imp.ot', '-Verbose'); Files = @{ 'imp.ot' = "use `"missing.ot`"`nheading `"x`"" } }
        @{ Name = 'V: desktop of a syntax error'; Args = @('desktop', 'bad.ot', '-Verbose'); Files = @{ 'bad.ot' = 'say (' } }
        @{ Name = 'V: desktop, the page cannot be written'; Args = @('desktop', 'app.ot', '-Verbose'); Files = @{ 'app.ot' = 'heading "x"'; 'app.html/' = '' } }
        # otter serve: an empty program, and a server that cannot start.
        @{ Name = 'serve: an empty program'; Args = @('serve', 'empty.ot'); Files = @{ 'empty.ot' = '' }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'serve: a port in use'; Args = @('serve', 's.ot', '-Port', '18185'); HoldPort = 18185; TimeoutSeconds = 20; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView); .NET Framework wording' }
        @{ Name = 'serve: a declared port in use'; Args = @('serve', 's.ot'); HoldPort = 18186; TimeoutSeconds = 20; Files = @{ 's.ot' = "api is a web server`n    port is 18186`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView); .NET Framework wording' }
        @{ Name = 'serve: a host with a space'; Args = @('serve', 's.ot'); TimeoutSeconds = 20; Files = @{ 's.ot' = "api is a web server`n    port is 18187`n    host is `"bad host`"`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView); .NET Framework wording' }
        @{ Name = 'serve: an empty host'; Args = @('serve', 's.ot'); TimeoutSeconds = 20; Files = @{ 's.ot' = "api is a web server`n    port is 18188`n    host is `"`"`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView); .NET Framework wording' }
        @{ Name = 'serve: a host with a colon'; Args = @('serve', 's.ot'); TimeoutSeconds = 20; Files = @{ 's.ot' = "api is a web server`n    port is 18189`n    host is `"a:b`"`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView); .NET Framework wording' }
        @{ Name = 'serve: port 70000'; Args = @('serve', 's.ot', '-Port', '70000'); TimeoutSeconds = 20; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView); .NET Framework wording' }
        @{ Name = 'serve: port 0'; Args = @('serve', 's.ot'); TimeoutSeconds = 20; Files = @{ 's.ot' = "api is a web server`n    port is 0`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView); .NET Framework wording' }
        @{ Name = 'serve: only blank lines'; Args = @('serve', 'nl.ot', '-Port', '18194'); TimeoutSeconds = 8; Files = @{ 'nl.ot' = "`n`n" } }
        # otter desktop: failures that end otter.ps1 (Windows; elsewhere both refuse).
        @{ Name = 'desktop: a missing import'; Args = @('desktop', 'imp.ot'); Files = @{ 'imp.ot' = "use `"missing.ot`"`nheading `"x`"" } }
        @{ Name = 'desktop: a project with a missing import'; Args = @('desktop', '.'); Files = @{ 'otter.json' = '{ "entryPoint": "a.ot", "target": "desktop" }'; 'a.ot' = "use `"m.ot`"`nheading `"x`"" } }
        @{ Name = 'desktop: an import cycle'; Args = @('desktop', 'a.ot'); Files = @{ 'a.ot' = "use `"b.ot`"`nheading `"x`""; 'b.ot' = "use `"a.ot`"`nto f`n    say 1" } }
        @{ Name = 'desktop: an import that is a folder'; Args = @('desktop', 'a.ot'); Files = @{ 'a.ot' = "use `"lib.ot`"`nheading `"x`""; 'lib.ot/' = '' } }
        @{ Name = 'desktop: an import in other capitals'; Args = @('desktop', 'a.ot'); Files = @{ 'a.ot' = "use `"B.ot`"`nheading `"x`""; 'b.ot' = "to f`n    say 1" } }
        @{ Name = 'desktop: a syntax error in an import'; Args = @('desktop', 'a.ot'); Files = @{ 'a.ot' = "use `"b.ot`"`nheading `"x`""; 'b.ot' = 'say (' } }
        @{ Name = 'desktop: the page cannot be written'; Args = @('desktop', 'app.ot'); Files = @{ 'app.ot' = 'heading "x"'; 'app.html/' = '' } }
        @{ Name = 'desktop: the page cannot be written, a long name'; Args = @('desktop', 'a-much-longer-application-file-name-to-wrap.ot'); Files = @{ 'a-much-longer-application-file-name-to-wrap.ot' = 'heading "x"'; 'a-much-longer-application-file-name-to-wrap.html/' = '' } }
        @{ Name = 'desktop: the session page cannot be written'; Args = @('desktop', 'app.ot'); Files = @{ 'app.ot' = 'heading "x"'; 'app.desktop.html/' = '' } }
    )
}
