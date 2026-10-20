# Texts Windows PowerShell 5.1 itself prints for otterPS, measured in CI
# (.github/workflows/cli-windows.yml, docs/SPEC-QUESTIONS.md C2-5 and F5):
# -InformationAction Stop and Ignore (every Write-Host fails), -Verbose (the
# VERBOSE lines of otterPS's Import-Module calls), and the errors of `otter
# serve` and `otter desktop` that end otter.ps1. `otter desktop` runs only on
# Windows; elsewhere both sides refuse it alike. Nothing here opens a browser
# or a window: the desktop cases all fail before that, and the servers that
# start are stopped by the time limit (long enough for Windows PowerShell to
# have started them).
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
        @{ Name = 'V: serve'; Args = @('serve', 's.ot', '-Port', '18192', '-Verbose'); TimeoutSeconds = 30; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'VERBOSE lines of PowerShell 7' }
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
        @{ Name = 'serve: only blank lines'; Args = @('serve', 'nl.ot', '-Port', '18194'); TimeoutSeconds = 30; Files = @{ 'nl.ot' = "`n`n" } }
        @{ Name = 'serve: a port [int] cannot read'; Args = @('serve', 's.ot'); TimeoutSeconds = 20; Files = @{ 's.ot' = "api is a web server`n    port is `"abc`"`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        @{ Name = 'serve: a port too big for [int]'; Args = @('serve', 's.ot', '-Verbose'); TimeoutSeconds = 20; Files = @{ 's.ot' = "api is a web server`n    port is 30000000000`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView); VERBOSE lines of PowerShell 7' }
        @{ Name = 'serve: a listen port [int] cannot read'; Args = @('serve', 's.ot'); TimeoutSeconds = 20; Files = @{ 's.ot' = "listen on port `"x`"`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" }; KnownOnPwsh7 = 'error view (5.1 NormalView)' }
        # The web compiler: UI resources nested 1,499 deep (the deepest native; F5-6).
        @{ Name = 'web: resources nested 1,499 deep'; Args = @('web', 'd.ot', '-NoOpen'); TimeoutSeconds = 600; KnownOnPwsh7 = 'the page (5.1 form)'; Files = @{ 'd.ot' = 'app is a page
    title is "D"
.
c1 is a column with align left
c2 is a column with align left
c3 is a column with align left
c4 is a column with align left
c5 is a column with align left
c6 is a column with align left
c7 is a column with align left
c8 is a column with align left
c9 is a column with align left
c10 is a column with align left
c11 is a column with align left
c12 is a column with align left
c13 is a column with align left
c14 is a column with align left
c15 is a column with align left
c16 is a column with align left
c17 is a column with align left
c18 is a column with align left
c19 is a column with align left
c20 is a column with align left
c21 is a column with align left
c22 is a column with align left
c23 is a column with align left
c24 is a column with align left
c25 is a column with align left
c26 is a column with align left
c27 is a column with align left
c28 is a column with align left
c29 is a column with align left
c30 is a column with align left
c31 is a column with align left
c32 is a column with align left
c33 is a column with align left
c34 is a column with align left
c35 is a column with align left
c36 is a column with align left
c37 is a column with align left
c38 is a column with align left
c39 is a column with align left
c40 is a column with align left
c41 is a column with align left
c42 is a column with align left
c43 is a column with align left
c44 is a column with align left
c45 is a column with align left
c46 is a column with align left
c47 is a column with align left
c48 is a column with align left
c49 is a column with align left
c50 is a column with align left
c51 is a column with align left
c52 is a column with align left
c53 is a column with align left
c54 is a column with align left
c55 is a column with align left
c56 is a column with align left
c57 is a column with align left
c58 is a column with align left
c59 is a column with align left
c60 is a column with align left
c61 is a column with align left
c62 is a column with align left
c63 is a column with align left
c64 is a column with align left
c65 is a column with align left
c66 is a column with align left
c67 is a column with align left
c68 is a column with align left
c69 is a column with align left
c70 is a column with align left
c71 is a column with align left
c72 is a column with align left
c73 is a column with align left
c74 is a column with align left
c75 is a column with align left
c76 is a column with align left
c77 is a column with align left
c78 is a column with align left
c79 is a column with align left
c80 is a column with align left
c81 is a column with align left
c82 is a column with align left
c83 is a column with align left
c84 is a column with align left
c85 is a column with align left
c86 is a column with align left
c87 is a column with align left
c88 is a column with align left
c89 is a column with align left
c90 is a column with align left
c91 is a column with align left
c92 is a column with align left
c93 is a column with align left
c94 is a column with align left
c95 is a column with align left
c96 is a column with align left
c97 is a column with align left
c98 is a column with align left
c99 is a column with align left
c100 is a column with align left
c101 is a column with align left
c102 is a column with align left
c103 is a column with align left
c104 is a column with align left
c105 is a column with align left
c106 is a column with align left
c107 is a column with align left
c108 is a column with align left
c109 is a column with align left
c110 is a column with align left
c111 is a column with align left
c112 is a column with align left
c113 is a column with align left
c114 is a column with align left
c115 is a column with align left
c116 is a column with align left
c117 is a column with align left
c118 is a column with align left
c119 is a column with align left
c120 is a column with align left
c121 is a column with align left
c122 is a column with align left
c123 is a column with align left
c124 is a column with align left
c125 is a column with align left
c126 is a column with align left
c127 is a column with align left
c128 is a column with align left
c129 is a column with align left
c130 is a column with align left
c131 is a column with align left
c132 is a column with align left
c133 is a column with align left
c134 is a column with align left
c135 is a column with align left
c136 is a column with align left
c137 is a column with align left
c138 is a column with align left
c139 is a column with align left
c140 is a column with align left
c141 is a column with align left
c142 is a column with align left
c143 is a column with align left
c144 is a column with align left
c145 is a column with align left
c146 is a column with align left
c147 is a column with align left
c148 is a column with align left
c149 is a column with align left
c150 is a column with align left
c151 is a column with align left
c152 is a column with align left
c153 is a column with align left
c154 is a column with align left
c155 is a column with align left
c156 is a column with align left
c157 is a column with align left
c158 is a column with align left
c159 is a column with align left
c160 is a column with align left
c161 is a column with align left
c162 is a column with align left
c163 is a column with align left
c164 is a column with align left
c165 is a column with align left
c166 is a column with align left
c167 is a column with align left
c168 is a column with align left
c169 is a column with align left
c170 is a column with align left
c171 is a column with align left
c172 is a column with align left
c173 is a column with align left
c174 is a column with align left
c175 is a column with align left
c176 is a column with align left
c177 is a column with align left
c178 is a column with align left
c179 is a column with align left
c180 is a column with align left
c181 is a column with align left
c182 is a column with align left
c183 is a column with align left
c184 is a column with align left
c185 is a column with align left
c186 is a column with align left
c187 is a column with align left
c188 is a column with align left
c189 is a column with align left
c190 is a column with align left
c191 is a column with align left
c192 is a column with align left
c193 is a column with align left
c194 is a column with align left
c195 is a column with align left
c196 is a column with align left
c197 is a column with align left
c198 is a column with align left
c199 is a column with align left
c200 is a column with align left
c201 is a column with align left
c202 is a column with align left
c203 is a column with align left
c204 is a column with align left
c205 is a column with align left
c206 is a column with align left
c207 is a column with align left
c208 is a column with align left
c209 is a column with align left
c210 is a column with align left
c211 is a column with align left
c212 is a column with align left
c213 is a column with align left
c214 is a column with align left
c215 is a column with align left
c216 is a column with align left
c217 is a column with align left
c218 is a column with align left
c219 is a column with align left
c220 is a column with align left
c221 is a column with align left
c222 is a column with align left
c223 is a column with align left
c224 is a column with align left
c225 is a column with align left
c226 is a column with align left
c227 is a column with align left
c228 is a column with align left
c229 is a column with align left
c230 is a column with align left
c231 is a column with align left
c232 is a column with align left
c233 is a column with align left
c234 is a column with align left
c235 is a column with align left
c236 is a column with align left
c237 is a column with align left
c238 is a column with align left
c239 is a column with align left
c240 is a column with align left
c241 is a column with align left
c242 is a column with align left
c243 is a column with align left
c244 is a column with align left
c245 is a column with align left
c246 is a column with align left
c247 is a column with align left
c248 is a column with align left
c249 is a column with align left
c250 is a column with align left
c251 is a column with align left
c252 is a column with align left
c253 is a column with align left
c254 is a column with align left
c255 is a column with align left
c256 is a column with align left
c257 is a column with align left
c258 is a column with align left
c259 is a column with align left
c260 is a column with align left
c261 is a column with align left
c262 is a column with align left
c263 is a column with align left
c264 is a column with align left
c265 is a column with align left
c266 is a column with align left
c267 is a column with align left
c268 is a column with align left
c269 is a column with align left
c270 is a column with align left
c271 is a column with align left
c272 is a column with align left
c273 is a column with align left
c274 is a column with align left
c275 is a column with align left
c276 is a column with align left
c277 is a column with align left
c278 is a column with align left
c279 is a column with align left
c280 is a column with align left
c281 is a column with align left
c282 is a column with align left
c283 is a column with align left
c284 is a column with align left
c285 is a column with align left
c286 is a column with align left
c287 is a column with align left
c288 is a column with align left
c289 is a column with align left
c290 is a column with align left
c291 is a column with align left
c292 is a column with align left
c293 is a column with align left
c294 is a column with align left
c295 is a column with align left
c296 is a column with align left
c297 is a column with align left
c298 is a column with align left
c299 is a column with align left
c300 is a column with align left
c301 is a column with align left
c302 is a column with align left
c303 is a column with align left
c304 is a column with align left
c305 is a column with align left
c306 is a column with align left
c307 is a column with align left
c308 is a column with align left
c309 is a column with align left
c310 is a column with align left
c311 is a column with align left
c312 is a column with align left
c313 is a column with align left
c314 is a column with align left
c315 is a column with align left
c316 is a column with align left
c317 is a column with align left
c318 is a column with align left
c319 is a column with align left
c320 is a column with align left
c321 is a column with align left
c322 is a column with align left
c323 is a column with align left
c324 is a column with align left
c325 is a column with align left
c326 is a column with align left
c327 is a column with align left
c328 is a column with align left
c329 is a column with align left
c330 is a column with align left
c331 is a column with align left
c332 is a column with align left
c333 is a column with align left
c334 is a column with align left
c335 is a column with align left
c336 is a column with align left
c337 is a column with align left
c338 is a column with align left
c339 is a column with align left
c340 is a column with align left
c341 is a column with align left
c342 is a column with align left
c343 is a column with align left
c344 is a column with align left
c345 is a column with align left
c346 is a column with align left
c347 is a column with align left
c348 is a column with align left
c349 is a column with align left
c350 is a column with align left
c351 is a column with align left
c352 is a column with align left
c353 is a column with align left
c354 is a column with align left
c355 is a column with align left
c356 is a column with align left
c357 is a column with align left
c358 is a column with align left
c359 is a column with align left
c360 is a column with align left
c361 is a column with align left
c362 is a column with align left
c363 is a column with align left
c364 is a column with align left
c365 is a column with align left
c366 is a column with align left
c367 is a column with align left
c368 is a column with align left
c369 is a column with align left
c370 is a column with align left
c371 is a column with align left
c372 is a column with align left
c373 is a column with align left
c374 is a column with align left
c375 is a column with align left
c376 is a column with align left
c377 is a column with align left
c378 is a column with align left
c379 is a column with align left
c380 is a column with align left
c381 is a column with align left
c382 is a column with align left
c383 is a column with align left
c384 is a column with align left
c385 is a column with align left
c386 is a column with align left
c387 is a column with align left
c388 is a column with align left
c389 is a column with align left
c390 is a column with align left
c391 is a column with align left
c392 is a column with align left
c393 is a column with align left
c394 is a column with align left
c395 is a column with align left
c396 is a column with align left
c397 is a column with align left
c398 is a column with align left
c399 is a column with align left
c400 is a column with align left
c401 is a column with align left
c402 is a column with align left
c403 is a column with align left
c404 is a column with align left
c405 is a column with align left
c406 is a column with align left
c407 is a column with align left
c408 is a column with align left
c409 is a column with align left
c410 is a column with align left
c411 is a column with align left
c412 is a column with align left
c413 is a column with align left
c414 is a column with align left
c415 is a column with align left
c416 is a column with align left
c417 is a column with align left
c418 is a column with align left
c419 is a column with align left
c420 is a column with align left
c421 is a column with align left
c422 is a column with align left
c423 is a column with align left
c424 is a column with align left
c425 is a column with align left
c426 is a column with align left
c427 is a column with align left
c428 is a column with align left
c429 is a column with align left
c430 is a column with align left
c431 is a column with align left
c432 is a column with align left
c433 is a column with align left
c434 is a column with align left
c435 is a column with align left
c436 is a column with align left
c437 is a column with align left
c438 is a column with align left
c439 is a column with align left
c440 is a column with align left
c441 is a column with align left
c442 is a column with align left
c443 is a column with align left
c444 is a column with align left
c445 is a column with align left
c446 is a column with align left
c447 is a column with align left
c448 is a column with align left
c449 is a column with align left
c450 is a column with align left
c451 is a column with align left
c452 is a column with align left
c453 is a column with align left
c454 is a column with align left
c455 is a column with align left
c456 is a column with align left
c457 is a column with align left
c458 is a column with align left
c459 is a column with align left
c460 is a column with align left
c461 is a column with align left
c462 is a column with align left
c463 is a column with align left
c464 is a column with align left
c465 is a column with align left
c466 is a column with align left
c467 is a column with align left
c468 is a column with align left
c469 is a column with align left
c470 is a column with align left
c471 is a column with align left
c472 is a column with align left
c473 is a column with align left
c474 is a column with align left
c475 is a column with align left
c476 is a column with align left
c477 is a column with align left
c478 is a column with align left
c479 is a column with align left
c480 is a column with align left
c481 is a column with align left
c482 is a column with align left
c483 is a column with align left
c484 is a column with align left
c485 is a column with align left
c486 is a column with align left
c487 is a column with align left
c488 is a column with align left
c489 is a column with align left
c490 is a column with align left
c491 is a column with align left
c492 is a column with align left
c493 is a column with align left
c494 is a column with align left
c495 is a column with align left
c496 is a column with align left
c497 is a column with align left
c498 is a column with align left
c499 is a column with align left
c500 is a column with align left
c501 is a column with align left
c502 is a column with align left
c503 is a column with align left
c504 is a column with align left
c505 is a column with align left
c506 is a column with align left
c507 is a column with align left
c508 is a column with align left
c509 is a column with align left
c510 is a column with align left
c511 is a column with align left
c512 is a column with align left
c513 is a column with align left
c514 is a column with align left
c515 is a column with align left
c516 is a column with align left
c517 is a column with align left
c518 is a column with align left
c519 is a column with align left
c520 is a column with align left
c521 is a column with align left
c522 is a column with align left
c523 is a column with align left
c524 is a column with align left
c525 is a column with align left
c526 is a column with align left
c527 is a column with align left
c528 is a column with align left
c529 is a column with align left
c530 is a column with align left
c531 is a column with align left
c532 is a column with align left
c533 is a column with align left
c534 is a column with align left
c535 is a column with align left
c536 is a column with align left
c537 is a column with align left
c538 is a column with align left
c539 is a column with align left
c540 is a column with align left
c541 is a column with align left
c542 is a column with align left
c543 is a column with align left
c544 is a column with align left
c545 is a column with align left
c546 is a column with align left
c547 is a column with align left
c548 is a column with align left
c549 is a column with align left
c550 is a column with align left
c551 is a column with align left
c552 is a column with align left
c553 is a column with align left
c554 is a column with align left
c555 is a column with align left
c556 is a column with align left
c557 is a column with align left
c558 is a column with align left
c559 is a column with align left
c560 is a column with align left
c561 is a column with align left
c562 is a column with align left
c563 is a column with align left
c564 is a column with align left
c565 is a column with align left
c566 is a column with align left
c567 is a column with align left
c568 is a column with align left
c569 is a column with align left
c570 is a column with align left
c571 is a column with align left
c572 is a column with align left
c573 is a column with align left
c574 is a column with align left
c575 is a column with align left
c576 is a column with align left
c577 is a column with align left
c578 is a column with align left
c579 is a column with align left
c580 is a column with align left
c581 is a column with align left
c582 is a column with align left
c583 is a column with align left
c584 is a column with align left
c585 is a column with align left
c586 is a column with align left
c587 is a column with align left
c588 is a column with align left
c589 is a column with align left
c590 is a column with align left
c591 is a column with align left
c592 is a column with align left
c593 is a column with align left
c594 is a column with align left
c595 is a column with align left
c596 is a column with align left
c597 is a column with align left
c598 is a column with align left
c599 is a column with align left
c600 is a column with align left
c601 is a column with align left
c602 is a column with align left
c603 is a column with align left
c604 is a column with align left
c605 is a column with align left
c606 is a column with align left
c607 is a column with align left
c608 is a column with align left
c609 is a column with align left
c610 is a column with align left
c611 is a column with align left
c612 is a column with align left
c613 is a column with align left
c614 is a column with align left
c615 is a column with align left
c616 is a column with align left
c617 is a column with align left
c618 is a column with align left
c619 is a column with align left
c620 is a column with align left
c621 is a column with align left
c622 is a column with align left
c623 is a column with align left
c624 is a column with align left
c625 is a column with align left
c626 is a column with align left
c627 is a column with align left
c628 is a column with align left
c629 is a column with align left
c630 is a column with align left
c631 is a column with align left
c632 is a column with align left
c633 is a column with align left
c634 is a column with align left
c635 is a column with align left
c636 is a column with align left
c637 is a column with align left
c638 is a column with align left
c639 is a column with align left
c640 is a column with align left
c641 is a column with align left
c642 is a column with align left
c643 is a column with align left
c644 is a column with align left
c645 is a column with align left
c646 is a column with align left
c647 is a column with align left
c648 is a column with align left
c649 is a column with align left
c650 is a column with align left
c651 is a column with align left
c652 is a column with align left
c653 is a column with align left
c654 is a column with align left
c655 is a column with align left
c656 is a column with align left
c657 is a column with align left
c658 is a column with align left
c659 is a column with align left
c660 is a column with align left
c661 is a column with align left
c662 is a column with align left
c663 is a column with align left
c664 is a column with align left
c665 is a column with align left
c666 is a column with align left
c667 is a column with align left
c668 is a column with align left
c669 is a column with align left
c670 is a column with align left
c671 is a column with align left
c672 is a column with align left
c673 is a column with align left
c674 is a column with align left
c675 is a column with align left
c676 is a column with align left
c677 is a column with align left
c678 is a column with align left
c679 is a column with align left
c680 is a column with align left
c681 is a column with align left
c682 is a column with align left
c683 is a column with align left
c684 is a column with align left
c685 is a column with align left
c686 is a column with align left
c687 is a column with align left
c688 is a column with align left
c689 is a column with align left
c690 is a column with align left
c691 is a column with align left
c692 is a column with align left
c693 is a column with align left
c694 is a column with align left
c695 is a column with align left
c696 is a column with align left
c697 is a column with align left
c698 is a column with align left
c699 is a column with align left
c700 is a column with align left
c701 is a column with align left
c702 is a column with align left
c703 is a column with align left
c704 is a column with align left
c705 is a column with align left
c706 is a column with align left
c707 is a column with align left
c708 is a column with align left
c709 is a column with align left
c710 is a column with align left
c711 is a column with align left
c712 is a column with align left
c713 is a column with align left
c714 is a column with align left
c715 is a column with align left
c716 is a column with align left
c717 is a column with align left
c718 is a column with align left
c719 is a column with align left
c720 is a column with align left
c721 is a column with align left
c722 is a column with align left
c723 is a column with align left
c724 is a column with align left
c725 is a column with align left
c726 is a column with align left
c727 is a column with align left
c728 is a column with align left
c729 is a column with align left
c730 is a column with align left
c731 is a column with align left
c732 is a column with align left
c733 is a column with align left
c734 is a column with align left
c735 is a column with align left
c736 is a column with align left
c737 is a column with align left
c738 is a column with align left
c739 is a column with align left
c740 is a column with align left
c741 is a column with align left
c742 is a column with align left
c743 is a column with align left
c744 is a column with align left
c745 is a column with align left
c746 is a column with align left
c747 is a column with align left
c748 is a column with align left
c749 is a column with align left
c750 is a column with align left
c751 is a column with align left
c752 is a column with align left
c753 is a column with align left
c754 is a column with align left
c755 is a column with align left
c756 is a column with align left
c757 is a column with align left
c758 is a column with align left
c759 is a column with align left
c760 is a column with align left
c761 is a column with align left
c762 is a column with align left
c763 is a column with align left
c764 is a column with align left
c765 is a column with align left
c766 is a column with align left
c767 is a column with align left
c768 is a column with align left
c769 is a column with align left
c770 is a column with align left
c771 is a column with align left
c772 is a column with align left
c773 is a column with align left
c774 is a column with align left
c775 is a column with align left
c776 is a column with align left
c777 is a column with align left
c778 is a column with align left
c779 is a column with align left
c780 is a column with align left
c781 is a column with align left
c782 is a column with align left
c783 is a column with align left
c784 is a column with align left
c785 is a column with align left
c786 is a column with align left
c787 is a column with align left
c788 is a column with align left
c789 is a column with align left
c790 is a column with align left
c791 is a column with align left
c792 is a column with align left
c793 is a column with align left
c794 is a column with align left
c795 is a column with align left
c796 is a column with align left
c797 is a column with align left
c798 is a column with align left
c799 is a column with align left
c800 is a column with align left
c801 is a column with align left
c802 is a column with align left
c803 is a column with align left
c804 is a column with align left
c805 is a column with align left
c806 is a column with align left
c807 is a column with align left
c808 is a column with align left
c809 is a column with align left
c810 is a column with align left
c811 is a column with align left
c812 is a column with align left
c813 is a column with align left
c814 is a column with align left
c815 is a column with align left
c816 is a column with align left
c817 is a column with align left
c818 is a column with align left
c819 is a column with align left
c820 is a column with align left
c821 is a column with align left
c822 is a column with align left
c823 is a column with align left
c824 is a column with align left
c825 is a column with align left
c826 is a column with align left
c827 is a column with align left
c828 is a column with align left
c829 is a column with align left
c830 is a column with align left
c831 is a column with align left
c832 is a column with align left
c833 is a column with align left
c834 is a column with align left
c835 is a column with align left
c836 is a column with align left
c837 is a column with align left
c838 is a column with align left
c839 is a column with align left
c840 is a column with align left
c841 is a column with align left
c842 is a column with align left
c843 is a column with align left
c844 is a column with align left
c845 is a column with align left
c846 is a column with align left
c847 is a column with align left
c848 is a column with align left
c849 is a column with align left
c850 is a column with align left
c851 is a column with align left
c852 is a column with align left
c853 is a column with align left
c854 is a column with align left
c855 is a column with align left
c856 is a column with align left
c857 is a column with align left
c858 is a column with align left
c859 is a column with align left
c860 is a column with align left
c861 is a column with align left
c862 is a column with align left
c863 is a column with align left
c864 is a column with align left
c865 is a column with align left
c866 is a column with align left
c867 is a column with align left
c868 is a column with align left
c869 is a column with align left
c870 is a column with align left
c871 is a column with align left
c872 is a column with align left
c873 is a column with align left
c874 is a column with align left
c875 is a column with align left
c876 is a column with align left
c877 is a column with align left
c878 is a column with align left
c879 is a column with align left
c880 is a column with align left
c881 is a column with align left
c882 is a column with align left
c883 is a column with align left
c884 is a column with align left
c885 is a column with align left
c886 is a column with align left
c887 is a column with align left
c888 is a column with align left
c889 is a column with align left
c890 is a column with align left
c891 is a column with align left
c892 is a column with align left
c893 is a column with align left
c894 is a column with align left
c895 is a column with align left
c896 is a column with align left
c897 is a column with align left
c898 is a column with align left
c899 is a column with align left
c900 is a column with align left
c901 is a column with align left
c902 is a column with align left
c903 is a column with align left
c904 is a column with align left
c905 is a column with align left
c906 is a column with align left
c907 is a column with align left
c908 is a column with align left
c909 is a column with align left
c910 is a column with align left
c911 is a column with align left
c912 is a column with align left
c913 is a column with align left
c914 is a column with align left
c915 is a column with align left
c916 is a column with align left
c917 is a column with align left
c918 is a column with align left
c919 is a column with align left
c920 is a column with align left
c921 is a column with align left
c922 is a column with align left
c923 is a column with align left
c924 is a column with align left
c925 is a column with align left
c926 is a column with align left
c927 is a column with align left
c928 is a column with align left
c929 is a column with align left
c930 is a column with align left
c931 is a column with align left
c932 is a column with align left
c933 is a column with align left
c934 is a column with align left
c935 is a column with align left
c936 is a column with align left
c937 is a column with align left
c938 is a column with align left
c939 is a column with align left
c940 is a column with align left
c941 is a column with align left
c942 is a column with align left
c943 is a column with align left
c944 is a column with align left
c945 is a column with align left
c946 is a column with align left
c947 is a column with align left
c948 is a column with align left
c949 is a column with align left
c950 is a column with align left
c951 is a column with align left
c952 is a column with align left
c953 is a column with align left
c954 is a column with align left
c955 is a column with align left
c956 is a column with align left
c957 is a column with align left
c958 is a column with align left
c959 is a column with align left
c960 is a column with align left
c961 is a column with align left
c962 is a column with align left
c963 is a column with align left
c964 is a column with align left
c965 is a column with align left
c966 is a column with align left
c967 is a column with align left
c968 is a column with align left
c969 is a column with align left
c970 is a column with align left
c971 is a column with align left
c972 is a column with align left
c973 is a column with align left
c974 is a column with align left
c975 is a column with align left
c976 is a column with align left
c977 is a column with align left
c978 is a column with align left
c979 is a column with align left
c980 is a column with align left
c981 is a column with align left
c982 is a column with align left
c983 is a column with align left
c984 is a column with align left
c985 is a column with align left
c986 is a column with align left
c987 is a column with align left
c988 is a column with align left
c989 is a column with align left
c990 is a column with align left
c991 is a column with align left
c992 is a column with align left
c993 is a column with align left
c994 is a column with align left
c995 is a column with align left
c996 is a column with align left
c997 is a column with align left
c998 is a column with align left
c999 is a column with align left
c1000 is a column with align left
c1001 is a column with align left
c1002 is a column with align left
c1003 is a column with align left
c1004 is a column with align left
c1005 is a column with align left
c1006 is a column with align left
c1007 is a column with align left
c1008 is a column with align left
c1009 is a column with align left
c1010 is a column with align left
c1011 is a column with align left
c1012 is a column with align left
c1013 is a column with align left
c1014 is a column with align left
c1015 is a column with align left
c1016 is a column with align left
c1017 is a column with align left
c1018 is a column with align left
c1019 is a column with align left
c1020 is a column with align left
c1021 is a column with align left
c1022 is a column with align left
c1023 is a column with align left
c1024 is a column with align left
c1025 is a column with align left
c1026 is a column with align left
c1027 is a column with align left
c1028 is a column with align left
c1029 is a column with align left
c1030 is a column with align left
c1031 is a column with align left
c1032 is a column with align left
c1033 is a column with align left
c1034 is a column with align left
c1035 is a column with align left
c1036 is a column with align left
c1037 is a column with align left
c1038 is a column with align left
c1039 is a column with align left
c1040 is a column with align left
c1041 is a column with align left
c1042 is a column with align left
c1043 is a column with align left
c1044 is a column with align left
c1045 is a column with align left
c1046 is a column with align left
c1047 is a column with align left
c1048 is a column with align left
c1049 is a column with align left
c1050 is a column with align left
c1051 is a column with align left
c1052 is a column with align left
c1053 is a column with align left
c1054 is a column with align left
c1055 is a column with align left
c1056 is a column with align left
c1057 is a column with align left
c1058 is a column with align left
c1059 is a column with align left
c1060 is a column with align left
c1061 is a column with align left
c1062 is a column with align left
c1063 is a column with align left
c1064 is a column with align left
c1065 is a column with align left
c1066 is a column with align left
c1067 is a column with align left
c1068 is a column with align left
c1069 is a column with align left
c1070 is a column with align left
c1071 is a column with align left
c1072 is a column with align left
c1073 is a column with align left
c1074 is a column with align left
c1075 is a column with align left
c1076 is a column with align left
c1077 is a column with align left
c1078 is a column with align left
c1079 is a column with align left
c1080 is a column with align left
c1081 is a column with align left
c1082 is a column with align left
c1083 is a column with align left
c1084 is a column with align left
c1085 is a column with align left
c1086 is a column with align left
c1087 is a column with align left
c1088 is a column with align left
c1089 is a column with align left
c1090 is a column with align left
c1091 is a column with align left
c1092 is a column with align left
c1093 is a column with align left
c1094 is a column with align left
c1095 is a column with align left
c1096 is a column with align left
c1097 is a column with align left
c1098 is a column with align left
c1099 is a column with align left
c1100 is a column with align left
c1101 is a column with align left
c1102 is a column with align left
c1103 is a column with align left
c1104 is a column with align left
c1105 is a column with align left
c1106 is a column with align left
c1107 is a column with align left
c1108 is a column with align left
c1109 is a column with align left
c1110 is a column with align left
c1111 is a column with align left
c1112 is a column with align left
c1113 is a column with align left
c1114 is a column with align left
c1115 is a column with align left
c1116 is a column with align left
c1117 is a column with align left
c1118 is a column with align left
c1119 is a column with align left
c1120 is a column with align left
c1121 is a column with align left
c1122 is a column with align left
c1123 is a column with align left
c1124 is a column with align left
c1125 is a column with align left
c1126 is a column with align left
c1127 is a column with align left
c1128 is a column with align left
c1129 is a column with align left
c1130 is a column with align left
c1131 is a column with align left
c1132 is a column with align left
c1133 is a column with align left
c1134 is a column with align left
c1135 is a column with align left
c1136 is a column with align left
c1137 is a column with align left
c1138 is a column with align left
c1139 is a column with align left
c1140 is a column with align left
c1141 is a column with align left
c1142 is a column with align left
c1143 is a column with align left
c1144 is a column with align left
c1145 is a column with align left
c1146 is a column with align left
c1147 is a column with align left
c1148 is a column with align left
c1149 is a column with align left
c1150 is a column with align left
c1151 is a column with align left
c1152 is a column with align left
c1153 is a column with align left
c1154 is a column with align left
c1155 is a column with align left
c1156 is a column with align left
c1157 is a column with align left
c1158 is a column with align left
c1159 is a column with align left
c1160 is a column with align left
c1161 is a column with align left
c1162 is a column with align left
c1163 is a column with align left
c1164 is a column with align left
c1165 is a column with align left
c1166 is a column with align left
c1167 is a column with align left
c1168 is a column with align left
c1169 is a column with align left
c1170 is a column with align left
c1171 is a column with align left
c1172 is a column with align left
c1173 is a column with align left
c1174 is a column with align left
c1175 is a column with align left
c1176 is a column with align left
c1177 is a column with align left
c1178 is a column with align left
c1179 is a column with align left
c1180 is a column with align left
c1181 is a column with align left
c1182 is a column with align left
c1183 is a column with align left
c1184 is a column with align left
c1185 is a column with align left
c1186 is a column with align left
c1187 is a column with align left
c1188 is a column with align left
c1189 is a column with align left
c1190 is a column with align left
c1191 is a column with align left
c1192 is a column with align left
c1193 is a column with align left
c1194 is a column with align left
c1195 is a column with align left
c1196 is a column with align left
c1197 is a column with align left
c1198 is a column with align left
c1199 is a column with align left
c1200 is a column with align left
c1201 is a column with align left
c1202 is a column with align left
c1203 is a column with align left
c1204 is a column with align left
c1205 is a column with align left
c1206 is a column with align left
c1207 is a column with align left
c1208 is a column with align left
c1209 is a column with align left
c1210 is a column with align left
c1211 is a column with align left
c1212 is a column with align left
c1213 is a column with align left
c1214 is a column with align left
c1215 is a column with align left
c1216 is a column with align left
c1217 is a column with align left
c1218 is a column with align left
c1219 is a column with align left
c1220 is a column with align left
c1221 is a column with align left
c1222 is a column with align left
c1223 is a column with align left
c1224 is a column with align left
c1225 is a column with align left
c1226 is a column with align left
c1227 is a column with align left
c1228 is a column with align left
c1229 is a column with align left
c1230 is a column with align left
c1231 is a column with align left
c1232 is a column with align left
c1233 is a column with align left
c1234 is a column with align left
c1235 is a column with align left
c1236 is a column with align left
c1237 is a column with align left
c1238 is a column with align left
c1239 is a column with align left
c1240 is a column with align left
c1241 is a column with align left
c1242 is a column with align left
c1243 is a column with align left
c1244 is a column with align left
c1245 is a column with align left
c1246 is a column with align left
c1247 is a column with align left
c1248 is a column with align left
c1249 is a column with align left
c1250 is a column with align left
c1251 is a column with align left
c1252 is a column with align left
c1253 is a column with align left
c1254 is a column with align left
c1255 is a column with align left
c1256 is a column with align left
c1257 is a column with align left
c1258 is a column with align left
c1259 is a column with align left
c1260 is a column with align left
c1261 is a column with align left
c1262 is a column with align left
c1263 is a column with align left
c1264 is a column with align left
c1265 is a column with align left
c1266 is a column with align left
c1267 is a column with align left
c1268 is a column with align left
c1269 is a column with align left
c1270 is a column with align left
c1271 is a column with align left
c1272 is a column with align left
c1273 is a column with align left
c1274 is a column with align left
c1275 is a column with align left
c1276 is a column with align left
c1277 is a column with align left
c1278 is a column with align left
c1279 is a column with align left
c1280 is a column with align left
c1281 is a column with align left
c1282 is a column with align left
c1283 is a column with align left
c1284 is a column with align left
c1285 is a column with align left
c1286 is a column with align left
c1287 is a column with align left
c1288 is a column with align left
c1289 is a column with align left
c1290 is a column with align left
c1291 is a column with align left
c1292 is a column with align left
c1293 is a column with align left
c1294 is a column with align left
c1295 is a column with align left
c1296 is a column with align left
c1297 is a column with align left
c1298 is a column with align left
c1299 is a column with align left
c1300 is a column with align left
c1301 is a column with align left
c1302 is a column with align left
c1303 is a column with align left
c1304 is a column with align left
c1305 is a column with align left
c1306 is a column with align left
c1307 is a column with align left
c1308 is a column with align left
c1309 is a column with align left
c1310 is a column with align left
c1311 is a column with align left
c1312 is a column with align left
c1313 is a column with align left
c1314 is a column with align left
c1315 is a column with align left
c1316 is a column with align left
c1317 is a column with align left
c1318 is a column with align left
c1319 is a column with align left
c1320 is a column with align left
c1321 is a column with align left
c1322 is a column with align left
c1323 is a column with align left
c1324 is a column with align left
c1325 is a column with align left
c1326 is a column with align left
c1327 is a column with align left
c1328 is a column with align left
c1329 is a column with align left
c1330 is a column with align left
c1331 is a column with align left
c1332 is a column with align left
c1333 is a column with align left
c1334 is a column with align left
c1335 is a column with align left
c1336 is a column with align left
c1337 is a column with align left
c1338 is a column with align left
c1339 is a column with align left
c1340 is a column with align left
c1341 is a column with align left
c1342 is a column with align left
c1343 is a column with align left
c1344 is a column with align left
c1345 is a column with align left
c1346 is a column with align left
c1347 is a column with align left
c1348 is a column with align left
c1349 is a column with align left
c1350 is a column with align left
c1351 is a column with align left
c1352 is a column with align left
c1353 is a column with align left
c1354 is a column with align left
c1355 is a column with align left
c1356 is a column with align left
c1357 is a column with align left
c1358 is a column with align left
c1359 is a column with align left
c1360 is a column with align left
c1361 is a column with align left
c1362 is a column with align left
c1363 is a column with align left
c1364 is a column with align left
c1365 is a column with align left
c1366 is a column with align left
c1367 is a column with align left
c1368 is a column with align left
c1369 is a column with align left
c1370 is a column with align left
c1371 is a column with align left
c1372 is a column with align left
c1373 is a column with align left
c1374 is a column with align left
c1375 is a column with align left
c1376 is a column with align left
c1377 is a column with align left
c1378 is a column with align left
c1379 is a column with align left
c1380 is a column with align left
c1381 is a column with align left
c1382 is a column with align left
c1383 is a column with align left
c1384 is a column with align left
c1385 is a column with align left
c1386 is a column with align left
c1387 is a column with align left
c1388 is a column with align left
c1389 is a column with align left
c1390 is a column with align left
c1391 is a column with align left
c1392 is a column with align left
c1393 is a column with align left
c1394 is a column with align left
c1395 is a column with align left
c1396 is a column with align left
c1397 is a column with align left
c1398 is a column with align left
c1399 is a column with align left
c1400 is a column with align left
c1401 is a column with align left
c1402 is a column with align left
c1403 is a column with align left
c1404 is a column with align left
c1405 is a column with align left
c1406 is a column with align left
c1407 is a column with align left
c1408 is a column with align left
c1409 is a column with align left
c1410 is a column with align left
c1411 is a column with align left
c1412 is a column with align left
c1413 is a column with align left
c1414 is a column with align left
c1415 is a column with align left
c1416 is a column with align left
c1417 is a column with align left
c1418 is a column with align left
c1419 is a column with align left
c1420 is a column with align left
c1421 is a column with align left
c1422 is a column with align left
c1423 is a column with align left
c1424 is a column with align left
c1425 is a column with align left
c1426 is a column with align left
c1427 is a column with align left
c1428 is a column with align left
c1429 is a column with align left
c1430 is a column with align left
c1431 is a column with align left
c1432 is a column with align left
c1433 is a column with align left
c1434 is a column with align left
c1435 is a column with align left
c1436 is a column with align left
c1437 is a column with align left
c1438 is a column with align left
c1439 is a column with align left
c1440 is a column with align left
c1441 is a column with align left
c1442 is a column with align left
c1443 is a column with align left
c1444 is a column with align left
c1445 is a column with align left
c1446 is a column with align left
c1447 is a column with align left
c1448 is a column with align left
c1449 is a column with align left
c1450 is a column with align left
c1451 is a column with align left
c1452 is a column with align left
c1453 is a column with align left
c1454 is a column with align left
c1455 is a column with align left
c1456 is a column with align left
c1457 is a column with align left
c1458 is a column with align left
c1459 is a column with align left
c1460 is a column with align left
c1461 is a column with align left
c1462 is a column with align left
c1463 is a column with align left
c1464 is a column with align left
c1465 is a column with align left
c1466 is a column with align left
c1467 is a column with align left
c1468 is a column with align left
c1469 is a column with align left
c1470 is a column with align left
c1471 is a column with align left
c1472 is a column with align left
c1473 is a column with align left
c1474 is a column with align left
c1475 is a column with align left
c1476 is a column with align left
c1477 is a column with align left
c1478 is a column with align left
c1479 is a column with align left
c1480 is a column with align left
c1481 is a column with align left
c1482 is a column with align left
c1483 is a column with align left
c1484 is a column with align left
c1485 is a column with align left
c1486 is a column with align left
c1487 is a column with align left
c1488 is a column with align left
c1489 is a column with align left
c1490 is a column with align left
c1491 is a column with align left
c1492 is a column with align left
c1493 is a column with align left
c1494 is a column with align left
c1495 is a column with align left
c1496 is a column with align left
c1497 is a column with align left
c1498 is a column with align left
c1499 is a column with align left
put c2 in c1
put c3 in c2
put c4 in c3
put c5 in c4
put c6 in c5
put c7 in c6
put c8 in c7
put c9 in c8
put c10 in c9
put c11 in c10
put c12 in c11
put c13 in c12
put c14 in c13
put c15 in c14
put c16 in c15
put c17 in c16
put c18 in c17
put c19 in c18
put c20 in c19
put c21 in c20
put c22 in c21
put c23 in c22
put c24 in c23
put c25 in c24
put c26 in c25
put c27 in c26
put c28 in c27
put c29 in c28
put c30 in c29
put c31 in c30
put c32 in c31
put c33 in c32
put c34 in c33
put c35 in c34
put c36 in c35
put c37 in c36
put c38 in c37
put c39 in c38
put c40 in c39
put c41 in c40
put c42 in c41
put c43 in c42
put c44 in c43
put c45 in c44
put c46 in c45
put c47 in c46
put c48 in c47
put c49 in c48
put c50 in c49
put c51 in c50
put c52 in c51
put c53 in c52
put c54 in c53
put c55 in c54
put c56 in c55
put c57 in c56
put c58 in c57
put c59 in c58
put c60 in c59
put c61 in c60
put c62 in c61
put c63 in c62
put c64 in c63
put c65 in c64
put c66 in c65
put c67 in c66
put c68 in c67
put c69 in c68
put c70 in c69
put c71 in c70
put c72 in c71
put c73 in c72
put c74 in c73
put c75 in c74
put c76 in c75
put c77 in c76
put c78 in c77
put c79 in c78
put c80 in c79
put c81 in c80
put c82 in c81
put c83 in c82
put c84 in c83
put c85 in c84
put c86 in c85
put c87 in c86
put c88 in c87
put c89 in c88
put c90 in c89
put c91 in c90
put c92 in c91
put c93 in c92
put c94 in c93
put c95 in c94
put c96 in c95
put c97 in c96
put c98 in c97
put c99 in c98
put c100 in c99
put c101 in c100
put c102 in c101
put c103 in c102
put c104 in c103
put c105 in c104
put c106 in c105
put c107 in c106
put c108 in c107
put c109 in c108
put c110 in c109
put c111 in c110
put c112 in c111
put c113 in c112
put c114 in c113
put c115 in c114
put c116 in c115
put c117 in c116
put c118 in c117
put c119 in c118
put c120 in c119
put c121 in c120
put c122 in c121
put c123 in c122
put c124 in c123
put c125 in c124
put c126 in c125
put c127 in c126
put c128 in c127
put c129 in c128
put c130 in c129
put c131 in c130
put c132 in c131
put c133 in c132
put c134 in c133
put c135 in c134
put c136 in c135
put c137 in c136
put c138 in c137
put c139 in c138
put c140 in c139
put c141 in c140
put c142 in c141
put c143 in c142
put c144 in c143
put c145 in c144
put c146 in c145
put c147 in c146
put c148 in c147
put c149 in c148
put c150 in c149
put c151 in c150
put c152 in c151
put c153 in c152
put c154 in c153
put c155 in c154
put c156 in c155
put c157 in c156
put c158 in c157
put c159 in c158
put c160 in c159
put c161 in c160
put c162 in c161
put c163 in c162
put c164 in c163
put c165 in c164
put c166 in c165
put c167 in c166
put c168 in c167
put c169 in c168
put c170 in c169
put c171 in c170
put c172 in c171
put c173 in c172
put c174 in c173
put c175 in c174
put c176 in c175
put c177 in c176
put c178 in c177
put c179 in c178
put c180 in c179
put c181 in c180
put c182 in c181
put c183 in c182
put c184 in c183
put c185 in c184
put c186 in c185
put c187 in c186
put c188 in c187
put c189 in c188
put c190 in c189
put c191 in c190
put c192 in c191
put c193 in c192
put c194 in c193
put c195 in c194
put c196 in c195
put c197 in c196
put c198 in c197
put c199 in c198
put c200 in c199
put c201 in c200
put c202 in c201
put c203 in c202
put c204 in c203
put c205 in c204
put c206 in c205
put c207 in c206
put c208 in c207
put c209 in c208
put c210 in c209
put c211 in c210
put c212 in c211
put c213 in c212
put c214 in c213
put c215 in c214
put c216 in c215
put c217 in c216
put c218 in c217
put c219 in c218
put c220 in c219
put c221 in c220
put c222 in c221
put c223 in c222
put c224 in c223
put c225 in c224
put c226 in c225
put c227 in c226
put c228 in c227
put c229 in c228
put c230 in c229
put c231 in c230
put c232 in c231
put c233 in c232
put c234 in c233
put c235 in c234
put c236 in c235
put c237 in c236
put c238 in c237
put c239 in c238
put c240 in c239
put c241 in c240
put c242 in c241
put c243 in c242
put c244 in c243
put c245 in c244
put c246 in c245
put c247 in c246
put c248 in c247
put c249 in c248
put c250 in c249
put c251 in c250
put c252 in c251
put c253 in c252
put c254 in c253
put c255 in c254
put c256 in c255
put c257 in c256
put c258 in c257
put c259 in c258
put c260 in c259
put c261 in c260
put c262 in c261
put c263 in c262
put c264 in c263
put c265 in c264
put c266 in c265
put c267 in c266
put c268 in c267
put c269 in c268
put c270 in c269
put c271 in c270
put c272 in c271
put c273 in c272
put c274 in c273
put c275 in c274
put c276 in c275
put c277 in c276
put c278 in c277
put c279 in c278
put c280 in c279
put c281 in c280
put c282 in c281
put c283 in c282
put c284 in c283
put c285 in c284
put c286 in c285
put c287 in c286
put c288 in c287
put c289 in c288
put c290 in c289
put c291 in c290
put c292 in c291
put c293 in c292
put c294 in c293
put c295 in c294
put c296 in c295
put c297 in c296
put c298 in c297
put c299 in c298
put c300 in c299
put c301 in c300
put c302 in c301
put c303 in c302
put c304 in c303
put c305 in c304
put c306 in c305
put c307 in c306
put c308 in c307
put c309 in c308
put c310 in c309
put c311 in c310
put c312 in c311
put c313 in c312
put c314 in c313
put c315 in c314
put c316 in c315
put c317 in c316
put c318 in c317
put c319 in c318
put c320 in c319
put c321 in c320
put c322 in c321
put c323 in c322
put c324 in c323
put c325 in c324
put c326 in c325
put c327 in c326
put c328 in c327
put c329 in c328
put c330 in c329
put c331 in c330
put c332 in c331
put c333 in c332
put c334 in c333
put c335 in c334
put c336 in c335
put c337 in c336
put c338 in c337
put c339 in c338
put c340 in c339
put c341 in c340
put c342 in c341
put c343 in c342
put c344 in c343
put c345 in c344
put c346 in c345
put c347 in c346
put c348 in c347
put c349 in c348
put c350 in c349
put c351 in c350
put c352 in c351
put c353 in c352
put c354 in c353
put c355 in c354
put c356 in c355
put c357 in c356
put c358 in c357
put c359 in c358
put c360 in c359
put c361 in c360
put c362 in c361
put c363 in c362
put c364 in c363
put c365 in c364
put c366 in c365
put c367 in c366
put c368 in c367
put c369 in c368
put c370 in c369
put c371 in c370
put c372 in c371
put c373 in c372
put c374 in c373
put c375 in c374
put c376 in c375
put c377 in c376
put c378 in c377
put c379 in c378
put c380 in c379
put c381 in c380
put c382 in c381
put c383 in c382
put c384 in c383
put c385 in c384
put c386 in c385
put c387 in c386
put c388 in c387
put c389 in c388
put c390 in c389
put c391 in c390
put c392 in c391
put c393 in c392
put c394 in c393
put c395 in c394
put c396 in c395
put c397 in c396
put c398 in c397
put c399 in c398
put c400 in c399
put c401 in c400
put c402 in c401
put c403 in c402
put c404 in c403
put c405 in c404
put c406 in c405
put c407 in c406
put c408 in c407
put c409 in c408
put c410 in c409
put c411 in c410
put c412 in c411
put c413 in c412
put c414 in c413
put c415 in c414
put c416 in c415
put c417 in c416
put c418 in c417
put c419 in c418
put c420 in c419
put c421 in c420
put c422 in c421
put c423 in c422
put c424 in c423
put c425 in c424
put c426 in c425
put c427 in c426
put c428 in c427
put c429 in c428
put c430 in c429
put c431 in c430
put c432 in c431
put c433 in c432
put c434 in c433
put c435 in c434
put c436 in c435
put c437 in c436
put c438 in c437
put c439 in c438
put c440 in c439
put c441 in c440
put c442 in c441
put c443 in c442
put c444 in c443
put c445 in c444
put c446 in c445
put c447 in c446
put c448 in c447
put c449 in c448
put c450 in c449
put c451 in c450
put c452 in c451
put c453 in c452
put c454 in c453
put c455 in c454
put c456 in c455
put c457 in c456
put c458 in c457
put c459 in c458
put c460 in c459
put c461 in c460
put c462 in c461
put c463 in c462
put c464 in c463
put c465 in c464
put c466 in c465
put c467 in c466
put c468 in c467
put c469 in c468
put c470 in c469
put c471 in c470
put c472 in c471
put c473 in c472
put c474 in c473
put c475 in c474
put c476 in c475
put c477 in c476
put c478 in c477
put c479 in c478
put c480 in c479
put c481 in c480
put c482 in c481
put c483 in c482
put c484 in c483
put c485 in c484
put c486 in c485
put c487 in c486
put c488 in c487
put c489 in c488
put c490 in c489
put c491 in c490
put c492 in c491
put c493 in c492
put c494 in c493
put c495 in c494
put c496 in c495
put c497 in c496
put c498 in c497
put c499 in c498
put c500 in c499
put c501 in c500
put c502 in c501
put c503 in c502
put c504 in c503
put c505 in c504
put c506 in c505
put c507 in c506
put c508 in c507
put c509 in c508
put c510 in c509
put c511 in c510
put c512 in c511
put c513 in c512
put c514 in c513
put c515 in c514
put c516 in c515
put c517 in c516
put c518 in c517
put c519 in c518
put c520 in c519
put c521 in c520
put c522 in c521
put c523 in c522
put c524 in c523
put c525 in c524
put c526 in c525
put c527 in c526
put c528 in c527
put c529 in c528
put c530 in c529
put c531 in c530
put c532 in c531
put c533 in c532
put c534 in c533
put c535 in c534
put c536 in c535
put c537 in c536
put c538 in c537
put c539 in c538
put c540 in c539
put c541 in c540
put c542 in c541
put c543 in c542
put c544 in c543
put c545 in c544
put c546 in c545
put c547 in c546
put c548 in c547
put c549 in c548
put c550 in c549
put c551 in c550
put c552 in c551
put c553 in c552
put c554 in c553
put c555 in c554
put c556 in c555
put c557 in c556
put c558 in c557
put c559 in c558
put c560 in c559
put c561 in c560
put c562 in c561
put c563 in c562
put c564 in c563
put c565 in c564
put c566 in c565
put c567 in c566
put c568 in c567
put c569 in c568
put c570 in c569
put c571 in c570
put c572 in c571
put c573 in c572
put c574 in c573
put c575 in c574
put c576 in c575
put c577 in c576
put c578 in c577
put c579 in c578
put c580 in c579
put c581 in c580
put c582 in c581
put c583 in c582
put c584 in c583
put c585 in c584
put c586 in c585
put c587 in c586
put c588 in c587
put c589 in c588
put c590 in c589
put c591 in c590
put c592 in c591
put c593 in c592
put c594 in c593
put c595 in c594
put c596 in c595
put c597 in c596
put c598 in c597
put c599 in c598
put c600 in c599
put c601 in c600
put c602 in c601
put c603 in c602
put c604 in c603
put c605 in c604
put c606 in c605
put c607 in c606
put c608 in c607
put c609 in c608
put c610 in c609
put c611 in c610
put c612 in c611
put c613 in c612
put c614 in c613
put c615 in c614
put c616 in c615
put c617 in c616
put c618 in c617
put c619 in c618
put c620 in c619
put c621 in c620
put c622 in c621
put c623 in c622
put c624 in c623
put c625 in c624
put c626 in c625
put c627 in c626
put c628 in c627
put c629 in c628
put c630 in c629
put c631 in c630
put c632 in c631
put c633 in c632
put c634 in c633
put c635 in c634
put c636 in c635
put c637 in c636
put c638 in c637
put c639 in c638
put c640 in c639
put c641 in c640
put c642 in c641
put c643 in c642
put c644 in c643
put c645 in c644
put c646 in c645
put c647 in c646
put c648 in c647
put c649 in c648
put c650 in c649
put c651 in c650
put c652 in c651
put c653 in c652
put c654 in c653
put c655 in c654
put c656 in c655
put c657 in c656
put c658 in c657
put c659 in c658
put c660 in c659
put c661 in c660
put c662 in c661
put c663 in c662
put c664 in c663
put c665 in c664
put c666 in c665
put c667 in c666
put c668 in c667
put c669 in c668
put c670 in c669
put c671 in c670
put c672 in c671
put c673 in c672
put c674 in c673
put c675 in c674
put c676 in c675
put c677 in c676
put c678 in c677
put c679 in c678
put c680 in c679
put c681 in c680
put c682 in c681
put c683 in c682
put c684 in c683
put c685 in c684
put c686 in c685
put c687 in c686
put c688 in c687
put c689 in c688
put c690 in c689
put c691 in c690
put c692 in c691
put c693 in c692
put c694 in c693
put c695 in c694
put c696 in c695
put c697 in c696
put c698 in c697
put c699 in c698
put c700 in c699
put c701 in c700
put c702 in c701
put c703 in c702
put c704 in c703
put c705 in c704
put c706 in c705
put c707 in c706
put c708 in c707
put c709 in c708
put c710 in c709
put c711 in c710
put c712 in c711
put c713 in c712
put c714 in c713
put c715 in c714
put c716 in c715
put c717 in c716
put c718 in c717
put c719 in c718
put c720 in c719
put c721 in c720
put c722 in c721
put c723 in c722
put c724 in c723
put c725 in c724
put c726 in c725
put c727 in c726
put c728 in c727
put c729 in c728
put c730 in c729
put c731 in c730
put c732 in c731
put c733 in c732
put c734 in c733
put c735 in c734
put c736 in c735
put c737 in c736
put c738 in c737
put c739 in c738
put c740 in c739
put c741 in c740
put c742 in c741
put c743 in c742
put c744 in c743
put c745 in c744
put c746 in c745
put c747 in c746
put c748 in c747
put c749 in c748
put c750 in c749
put c751 in c750
put c752 in c751
put c753 in c752
put c754 in c753
put c755 in c754
put c756 in c755
put c757 in c756
put c758 in c757
put c759 in c758
put c760 in c759
put c761 in c760
put c762 in c761
put c763 in c762
put c764 in c763
put c765 in c764
put c766 in c765
put c767 in c766
put c768 in c767
put c769 in c768
put c770 in c769
put c771 in c770
put c772 in c771
put c773 in c772
put c774 in c773
put c775 in c774
put c776 in c775
put c777 in c776
put c778 in c777
put c779 in c778
put c780 in c779
put c781 in c780
put c782 in c781
put c783 in c782
put c784 in c783
put c785 in c784
put c786 in c785
put c787 in c786
put c788 in c787
put c789 in c788
put c790 in c789
put c791 in c790
put c792 in c791
put c793 in c792
put c794 in c793
put c795 in c794
put c796 in c795
put c797 in c796
put c798 in c797
put c799 in c798
put c800 in c799
put c801 in c800
put c802 in c801
put c803 in c802
put c804 in c803
put c805 in c804
put c806 in c805
put c807 in c806
put c808 in c807
put c809 in c808
put c810 in c809
put c811 in c810
put c812 in c811
put c813 in c812
put c814 in c813
put c815 in c814
put c816 in c815
put c817 in c816
put c818 in c817
put c819 in c818
put c820 in c819
put c821 in c820
put c822 in c821
put c823 in c822
put c824 in c823
put c825 in c824
put c826 in c825
put c827 in c826
put c828 in c827
put c829 in c828
put c830 in c829
put c831 in c830
put c832 in c831
put c833 in c832
put c834 in c833
put c835 in c834
put c836 in c835
put c837 in c836
put c838 in c837
put c839 in c838
put c840 in c839
put c841 in c840
put c842 in c841
put c843 in c842
put c844 in c843
put c845 in c844
put c846 in c845
put c847 in c846
put c848 in c847
put c849 in c848
put c850 in c849
put c851 in c850
put c852 in c851
put c853 in c852
put c854 in c853
put c855 in c854
put c856 in c855
put c857 in c856
put c858 in c857
put c859 in c858
put c860 in c859
put c861 in c860
put c862 in c861
put c863 in c862
put c864 in c863
put c865 in c864
put c866 in c865
put c867 in c866
put c868 in c867
put c869 in c868
put c870 in c869
put c871 in c870
put c872 in c871
put c873 in c872
put c874 in c873
put c875 in c874
put c876 in c875
put c877 in c876
put c878 in c877
put c879 in c878
put c880 in c879
put c881 in c880
put c882 in c881
put c883 in c882
put c884 in c883
put c885 in c884
put c886 in c885
put c887 in c886
put c888 in c887
put c889 in c888
put c890 in c889
put c891 in c890
put c892 in c891
put c893 in c892
put c894 in c893
put c895 in c894
put c896 in c895
put c897 in c896
put c898 in c897
put c899 in c898
put c900 in c899
put c901 in c900
put c902 in c901
put c903 in c902
put c904 in c903
put c905 in c904
put c906 in c905
put c907 in c906
put c908 in c907
put c909 in c908
put c910 in c909
put c911 in c910
put c912 in c911
put c913 in c912
put c914 in c913
put c915 in c914
put c916 in c915
put c917 in c916
put c918 in c917
put c919 in c918
put c920 in c919
put c921 in c920
put c922 in c921
put c923 in c922
put c924 in c923
put c925 in c924
put c926 in c925
put c927 in c926
put c928 in c927
put c929 in c928
put c930 in c929
put c931 in c930
put c932 in c931
put c933 in c932
put c934 in c933
put c935 in c934
put c936 in c935
put c937 in c936
put c938 in c937
put c939 in c938
put c940 in c939
put c941 in c940
put c942 in c941
put c943 in c942
put c944 in c943
put c945 in c944
put c946 in c945
put c947 in c946
put c948 in c947
put c949 in c948
put c950 in c949
put c951 in c950
put c952 in c951
put c953 in c952
put c954 in c953
put c955 in c954
put c956 in c955
put c957 in c956
put c958 in c957
put c959 in c958
put c960 in c959
put c961 in c960
put c962 in c961
put c963 in c962
put c964 in c963
put c965 in c964
put c966 in c965
put c967 in c966
put c968 in c967
put c969 in c968
put c970 in c969
put c971 in c970
put c972 in c971
put c973 in c972
put c974 in c973
put c975 in c974
put c976 in c975
put c977 in c976
put c978 in c977
put c979 in c978
put c980 in c979
put c981 in c980
put c982 in c981
put c983 in c982
put c984 in c983
put c985 in c984
put c986 in c985
put c987 in c986
put c988 in c987
put c989 in c988
put c990 in c989
put c991 in c990
put c992 in c991
put c993 in c992
put c994 in c993
put c995 in c994
put c996 in c995
put c997 in c996
put c998 in c997
put c999 in c998
put c1000 in c999
put c1001 in c1000
put c1002 in c1001
put c1003 in c1002
put c1004 in c1003
put c1005 in c1004
put c1006 in c1005
put c1007 in c1006
put c1008 in c1007
put c1009 in c1008
put c1010 in c1009
put c1011 in c1010
put c1012 in c1011
put c1013 in c1012
put c1014 in c1013
put c1015 in c1014
put c1016 in c1015
put c1017 in c1016
put c1018 in c1017
put c1019 in c1018
put c1020 in c1019
put c1021 in c1020
put c1022 in c1021
put c1023 in c1022
put c1024 in c1023
put c1025 in c1024
put c1026 in c1025
put c1027 in c1026
put c1028 in c1027
put c1029 in c1028
put c1030 in c1029
put c1031 in c1030
put c1032 in c1031
put c1033 in c1032
put c1034 in c1033
put c1035 in c1034
put c1036 in c1035
put c1037 in c1036
put c1038 in c1037
put c1039 in c1038
put c1040 in c1039
put c1041 in c1040
put c1042 in c1041
put c1043 in c1042
put c1044 in c1043
put c1045 in c1044
put c1046 in c1045
put c1047 in c1046
put c1048 in c1047
put c1049 in c1048
put c1050 in c1049
put c1051 in c1050
put c1052 in c1051
put c1053 in c1052
put c1054 in c1053
put c1055 in c1054
put c1056 in c1055
put c1057 in c1056
put c1058 in c1057
put c1059 in c1058
put c1060 in c1059
put c1061 in c1060
put c1062 in c1061
put c1063 in c1062
put c1064 in c1063
put c1065 in c1064
put c1066 in c1065
put c1067 in c1066
put c1068 in c1067
put c1069 in c1068
put c1070 in c1069
put c1071 in c1070
put c1072 in c1071
put c1073 in c1072
put c1074 in c1073
put c1075 in c1074
put c1076 in c1075
put c1077 in c1076
put c1078 in c1077
put c1079 in c1078
put c1080 in c1079
put c1081 in c1080
put c1082 in c1081
put c1083 in c1082
put c1084 in c1083
put c1085 in c1084
put c1086 in c1085
put c1087 in c1086
put c1088 in c1087
put c1089 in c1088
put c1090 in c1089
put c1091 in c1090
put c1092 in c1091
put c1093 in c1092
put c1094 in c1093
put c1095 in c1094
put c1096 in c1095
put c1097 in c1096
put c1098 in c1097
put c1099 in c1098
put c1100 in c1099
put c1101 in c1100
put c1102 in c1101
put c1103 in c1102
put c1104 in c1103
put c1105 in c1104
put c1106 in c1105
put c1107 in c1106
put c1108 in c1107
put c1109 in c1108
put c1110 in c1109
put c1111 in c1110
put c1112 in c1111
put c1113 in c1112
put c1114 in c1113
put c1115 in c1114
put c1116 in c1115
put c1117 in c1116
put c1118 in c1117
put c1119 in c1118
put c1120 in c1119
put c1121 in c1120
put c1122 in c1121
put c1123 in c1122
put c1124 in c1123
put c1125 in c1124
put c1126 in c1125
put c1127 in c1126
put c1128 in c1127
put c1129 in c1128
put c1130 in c1129
put c1131 in c1130
put c1132 in c1131
put c1133 in c1132
put c1134 in c1133
put c1135 in c1134
put c1136 in c1135
put c1137 in c1136
put c1138 in c1137
put c1139 in c1138
put c1140 in c1139
put c1141 in c1140
put c1142 in c1141
put c1143 in c1142
put c1144 in c1143
put c1145 in c1144
put c1146 in c1145
put c1147 in c1146
put c1148 in c1147
put c1149 in c1148
put c1150 in c1149
put c1151 in c1150
put c1152 in c1151
put c1153 in c1152
put c1154 in c1153
put c1155 in c1154
put c1156 in c1155
put c1157 in c1156
put c1158 in c1157
put c1159 in c1158
put c1160 in c1159
put c1161 in c1160
put c1162 in c1161
put c1163 in c1162
put c1164 in c1163
put c1165 in c1164
put c1166 in c1165
put c1167 in c1166
put c1168 in c1167
put c1169 in c1168
put c1170 in c1169
put c1171 in c1170
put c1172 in c1171
put c1173 in c1172
put c1174 in c1173
put c1175 in c1174
put c1176 in c1175
put c1177 in c1176
put c1178 in c1177
put c1179 in c1178
put c1180 in c1179
put c1181 in c1180
put c1182 in c1181
put c1183 in c1182
put c1184 in c1183
put c1185 in c1184
put c1186 in c1185
put c1187 in c1186
put c1188 in c1187
put c1189 in c1188
put c1190 in c1189
put c1191 in c1190
put c1192 in c1191
put c1193 in c1192
put c1194 in c1193
put c1195 in c1194
put c1196 in c1195
put c1197 in c1196
put c1198 in c1197
put c1199 in c1198
put c1200 in c1199
put c1201 in c1200
put c1202 in c1201
put c1203 in c1202
put c1204 in c1203
put c1205 in c1204
put c1206 in c1205
put c1207 in c1206
put c1208 in c1207
put c1209 in c1208
put c1210 in c1209
put c1211 in c1210
put c1212 in c1211
put c1213 in c1212
put c1214 in c1213
put c1215 in c1214
put c1216 in c1215
put c1217 in c1216
put c1218 in c1217
put c1219 in c1218
put c1220 in c1219
put c1221 in c1220
put c1222 in c1221
put c1223 in c1222
put c1224 in c1223
put c1225 in c1224
put c1226 in c1225
put c1227 in c1226
put c1228 in c1227
put c1229 in c1228
put c1230 in c1229
put c1231 in c1230
put c1232 in c1231
put c1233 in c1232
put c1234 in c1233
put c1235 in c1234
put c1236 in c1235
put c1237 in c1236
put c1238 in c1237
put c1239 in c1238
put c1240 in c1239
put c1241 in c1240
put c1242 in c1241
put c1243 in c1242
put c1244 in c1243
put c1245 in c1244
put c1246 in c1245
put c1247 in c1246
put c1248 in c1247
put c1249 in c1248
put c1250 in c1249
put c1251 in c1250
put c1252 in c1251
put c1253 in c1252
put c1254 in c1253
put c1255 in c1254
put c1256 in c1255
put c1257 in c1256
put c1258 in c1257
put c1259 in c1258
put c1260 in c1259
put c1261 in c1260
put c1262 in c1261
put c1263 in c1262
put c1264 in c1263
put c1265 in c1264
put c1266 in c1265
put c1267 in c1266
put c1268 in c1267
put c1269 in c1268
put c1270 in c1269
put c1271 in c1270
put c1272 in c1271
put c1273 in c1272
put c1274 in c1273
put c1275 in c1274
put c1276 in c1275
put c1277 in c1276
put c1278 in c1277
put c1279 in c1278
put c1280 in c1279
put c1281 in c1280
put c1282 in c1281
put c1283 in c1282
put c1284 in c1283
put c1285 in c1284
put c1286 in c1285
put c1287 in c1286
put c1288 in c1287
put c1289 in c1288
put c1290 in c1289
put c1291 in c1290
put c1292 in c1291
put c1293 in c1292
put c1294 in c1293
put c1295 in c1294
put c1296 in c1295
put c1297 in c1296
put c1298 in c1297
put c1299 in c1298
put c1300 in c1299
put c1301 in c1300
put c1302 in c1301
put c1303 in c1302
put c1304 in c1303
put c1305 in c1304
put c1306 in c1305
put c1307 in c1306
put c1308 in c1307
put c1309 in c1308
put c1310 in c1309
put c1311 in c1310
put c1312 in c1311
put c1313 in c1312
put c1314 in c1313
put c1315 in c1314
put c1316 in c1315
put c1317 in c1316
put c1318 in c1317
put c1319 in c1318
put c1320 in c1319
put c1321 in c1320
put c1322 in c1321
put c1323 in c1322
put c1324 in c1323
put c1325 in c1324
put c1326 in c1325
put c1327 in c1326
put c1328 in c1327
put c1329 in c1328
put c1330 in c1329
put c1331 in c1330
put c1332 in c1331
put c1333 in c1332
put c1334 in c1333
put c1335 in c1334
put c1336 in c1335
put c1337 in c1336
put c1338 in c1337
put c1339 in c1338
put c1340 in c1339
put c1341 in c1340
put c1342 in c1341
put c1343 in c1342
put c1344 in c1343
put c1345 in c1344
put c1346 in c1345
put c1347 in c1346
put c1348 in c1347
put c1349 in c1348
put c1350 in c1349
put c1351 in c1350
put c1352 in c1351
put c1353 in c1352
put c1354 in c1353
put c1355 in c1354
put c1356 in c1355
put c1357 in c1356
put c1358 in c1357
put c1359 in c1358
put c1360 in c1359
put c1361 in c1360
put c1362 in c1361
put c1363 in c1362
put c1364 in c1363
put c1365 in c1364
put c1366 in c1365
put c1367 in c1366
put c1368 in c1367
put c1369 in c1368
put c1370 in c1369
put c1371 in c1370
put c1372 in c1371
put c1373 in c1372
put c1374 in c1373
put c1375 in c1374
put c1376 in c1375
put c1377 in c1376
put c1378 in c1377
put c1379 in c1378
put c1380 in c1379
put c1381 in c1380
put c1382 in c1381
put c1383 in c1382
put c1384 in c1383
put c1385 in c1384
put c1386 in c1385
put c1387 in c1386
put c1388 in c1387
put c1389 in c1388
put c1390 in c1389
put c1391 in c1390
put c1392 in c1391
put c1393 in c1392
put c1394 in c1393
put c1395 in c1394
put c1396 in c1395
put c1397 in c1396
put c1398 in c1397
put c1399 in c1398
put c1400 in c1399
put c1401 in c1400
put c1402 in c1401
put c1403 in c1402
put c1404 in c1403
put c1405 in c1404
put c1406 in c1405
put c1407 in c1406
put c1408 in c1407
put c1409 in c1408
put c1410 in c1409
put c1411 in c1410
put c1412 in c1411
put c1413 in c1412
put c1414 in c1413
put c1415 in c1414
put c1416 in c1415
put c1417 in c1416
put c1418 in c1417
put c1419 in c1418
put c1420 in c1419
put c1421 in c1420
put c1422 in c1421
put c1423 in c1422
put c1424 in c1423
put c1425 in c1424
put c1426 in c1425
put c1427 in c1426
put c1428 in c1427
put c1429 in c1428
put c1430 in c1429
put c1431 in c1430
put c1432 in c1431
put c1433 in c1432
put c1434 in c1433
put c1435 in c1434
put c1436 in c1435
put c1437 in c1436
put c1438 in c1437
put c1439 in c1438
put c1440 in c1439
put c1441 in c1440
put c1442 in c1441
put c1443 in c1442
put c1444 in c1443
put c1445 in c1444
put c1446 in c1445
put c1447 in c1446
put c1448 in c1447
put c1449 in c1448
put c1450 in c1449
put c1451 in c1450
put c1452 in c1451
put c1453 in c1452
put c1454 in c1453
put c1455 in c1454
put c1456 in c1455
put c1457 in c1456
put c1458 in c1457
put c1459 in c1458
put c1460 in c1459
put c1461 in c1460
put c1462 in c1461
put c1463 in c1462
put c1464 in c1463
put c1465 in c1464
put c1466 in c1465
put c1467 in c1466
put c1468 in c1467
put c1469 in c1468
put c1470 in c1469
put c1471 in c1470
put c1472 in c1471
put c1473 in c1472
put c1474 in c1473
put c1475 in c1474
put c1476 in c1475
put c1477 in c1476
put c1478 in c1477
put c1479 in c1478
put c1480 in c1479
put c1481 in c1480
put c1482 in c1481
put c1483 in c1482
put c1484 in c1483
put c1485 in c1484
put c1486 in c1485
put c1487 in c1486
put c1488 in c1487
put c1489 in c1488
put c1490 in c1489
put c1491 in c1490
put c1492 in c1491
put c1493 in c1492
put c1494 in c1493
put c1495 in c1494
put c1496 in c1495
put c1497 in c1496
put c1498 in c1497
put c1499 in c1498
put c1 in app
show app' } }
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
