# Measurement only (cli-windows.yml): texts PowerShell itself prints.
@{
    Cases = @(
        # -InformationAction
        @{ Name = 'IA version Ignore'; Args = @('--version', '-InformationAction', 'Ignore') }
        @{ Name = 'IA version Stop'; Args = @('--version', '-InformationAction', 'Stop') }
        @{ Name = 'IA version Inquire empty'; Args = @('--version', '-InformationAction', 'Inquire') }
        @{ Name = 'IA version Inquire Y'; Args = @('--version', '-InformationAction', 'Inquire'); Stdin = "Y`n" }
        @{ Name = 'IA help Inquire A'; Args = @('help', '-infa', 'Inquire'); Stdin = "A`n" }
        @{ Name = 'IA help Inquire N'; Args = @('help', '-infa', 'Inquire'); Stdin = "N`nY`nL`n" }
        @{ Name = 'IA help Inquire H'; Args = @('help', '-infa', 'Inquire'); Stdin = "H`n" }
        @{ Name = 'IA help Inquire S'; Args = @('help', '-infa', 'Inquire'); Stdin = "S`nexit`nY`n" }
        @{ Name = 'IA help Inquire ?'; Args = @('help', '-infa', 'Inquire'); Stdin = "?`nx`n`nY`n" }
        @{ Name = 'IA help Stop'; Args = @('help', '-InformationAction', 'Stop') }
        @{ Name = 'IA help Ignore'; Args = @('help', '-InformationAction', 'Ignore') }
        @{ Name = 'IA say Ignore'; Args = @('a.ot', '-InformationAction', 'Ignore'); Files = @{ 'a.ot' = "say 1`nsay `"two`"" } }
        @{ Name = 'IA say Stop'; Args = @('a.ot', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = "say 1`nsay `"two`"" } }
        @{ Name = 'IA say Inquire'; Args = @('a.ot', '-InformationAction', 'Inquire'); Files = @{ 'a.ot' = "say 1`nsay `"two`"" } }
        @{ Name = 'IA say interpreter Stop'; Args = @('a.ot', '-InformationAction', 'Stop'); Env = @{ OTTER_ENGINE = 'interpreter' }; Files = @{ 'a.ot' = "say 1`nsay `"two`"" } }
        @{ Name = 'IA say interpreter Ignore'; Args = @('a.ot', '-InformationAction', 'Ignore'); Env = @{ OTTER_ENGINE = 'interpreter' }; Files = @{ 'a.ot' = "say 1`nsay `"two`"" } }
        @{ Name = 'IA check Ignore'; Args = @('check', 'a.ot', '-InformationAction', 'Ignore'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'IA check Stop'; Args = @('check', 'a.ot', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'IA check Inquire Y'; Args = @('check', 'a.ot', '-InformationAction', 'Inquire'); Stdin = "Y`n"; Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'IA syntax error Stop'; Args = @('bad.ot', '-InformationAction', 'Stop'); Files = @{ 'bad.ot' = 'say (' } }
        @{ Name = 'IA syntax error Ignore'; Args = @('bad.ot', '-InformationAction', 'Ignore'); Files = @{ 'bad.ot' = 'say (' } }
        @{ Name = 'IA runtime error Stop'; Args = @('r.ot', '-InformationAction', 'Stop'); Files = @{ 'r.ot' = "say 1`nsay 1 / 0" } }
        @{ Name = 'IA runtime error Ignore'; Args = @('r.ot', '-InformationAction', 'Ignore'); Files = @{ 'r.ot' = "say 1`nsay 1 / 0" } }
        @{ Name = 'IA unknown command Stop'; Args = @('nope', '-InformationAction', 'Stop') }
        @{ Name = 'IA unknown command Ignore'; Args = @('nope', '-InformationAction', 'Ignore') }
        @{ Name = 'IA missing file Stop'; Args = @('run', 'missing.ot', '-InformationAction', 'Stop') }
        @{ Name = 'IA repl Ignore'; Args = @('-InformationAction', 'Ignore'); Stdin = "say 1`nexit`n" }
        @{ Name = 'IA repl Stop'; Args = @('-InformationAction', 'Stop'); Stdin = "say 1`nexit`n" }
        @{ Name = 'IA new Stop'; Args = @('new', 'console', 'app', '-InformationAction', 'Stop') }
        @{ Name = 'IA new Ignore'; Args = @('new', 'console', 'app', '-InformationAction', 'Ignore') }
        @{ Name = 'IA web Stop'; Args = @('web', 'p.ot', '-NoOpen', '-InformationAction', 'Stop'); Files = @{ 'p.ot' = 'heading "Hi"' } }
        @{ Name = 'IA web Ignore'; Args = @('web', 'p.ot', '-NoOpen', '-InformationAction', 'Ignore'); Files = @{ 'p.ot' = 'heading "Hi"' } }
        @{ Name = 'IA build Ignore'; Args = @('build', '-InformationAction', 'Ignore'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'IA build Stop'; Args = @('build', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'IA test Stop'; Args = @('test', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1'; 'tests/a.test.ot' = "test `"one`"`n    expect 1 is 1" } }
        @{ Name = 'IA serve usage Stop'; Args = @('serve', '-InformationAction', 'Stop') }
        @{ Name = 'IA profile Stop'; Args = @('profile', 'a.ot', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'IA SilentlyContinue say'; Args = @('a.ot', '-InformationAction', 'SilentlyContinue'); Files = @{ 'a.ot' = 'say 1' } }
        # -Verbose
        @{ Name = 'V run'; Args = @('a.ot', '-Verbose'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V run vb'; Args = @('run', 'a.ot', '-vb'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V run interpreter'; Args = @('a.ot', '-Verbose'); Env = @{ OTTER_ENGINE = 'interpreter' }; Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V run not compilable'; Args = @('a.ot', '-Verbose'); Files = @{ 'a.ot' = "create window into app`nsay 1" } }
        @{ Name = 'V run syntax error'; Args = @('bad.ot', '-Verbose'); Files = @{ 'bad.ot' = 'say (' } }
        @{ Name = 'V run runtime error'; Args = @('r.ot', '-Verbose'); Files = @{ 'r.ot' = "say 1`nsay 1 / 0" } }
        @{ Name = 'V run DebugTokens'; Args = @('a.ot', '-Verbose', '-DebugTokens'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V run IA Ignore'; Args = @('a.ot', '-Verbose', '-InformationAction', 'Ignore'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V version'; Args = @('--version', '-Verbose') }
        @{ Name = 'V profile'; Args = @('profile', 'a.ot', '-Verbose'); Files = @{ 'a.ot' = 'say 1' }; Mask = @('\d+(\.\d+)? ?ms') }
        @{ Name = 'V debug'; Args = @('debug', 'a.ot', '-Verbose'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V build console'; Args = @('build', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'V build web'; Args = @('build', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "app.ot", "target": "web" }'; 'app.ot' = 'heading "Hi"' } }
        @{ Name = 'V publish console'; Args = @('publish', '-Verbose'); Files = @{ 'otter.json' = '{ "name": "c", "version": "1.0.0", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'V test'; Args = @('test', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1'; 'tests/a.test.ot' = "test `"one`"`n    expect 1 is 1" } }
        @{ Name = 'V web'; Args = @('web', 'p.ot', '-NoOpen', '-Verbose'); Files = @{ 'p.ot' = 'heading "Hi"' } }
        @{ Name = 'V serve syntax error'; Args = @('serve', 'bad.ot', '-Verbose'); Files = @{ 'bad.ot' = 'say (' } }
        @{ Name = 'V desktop missing import'; Args = @('desktop', 'imp.ot', '-Verbose'); Files = @{ 'imp.ot' = "use `"missing.ot`"`nheading `"x`"" } }
        @{ Name = 'V check'; Args = @('check', 'a.ot', '-Verbose'); Files = @{ 'a.ot' = 'say 1' } }
        # otter serve
        @{ Name = 'S empty'; Args = @('serve', 'empty.ot'); Files = @{ 'empty.ot' = '' } }
        @{ Name = 'S newline only'; Args = @('serve', 'nl.ot'); Files = @{ 'nl.ot' = "`n`n" } }
        @{ Name = 'S comment only'; Args = @('serve', 'c.ot'); Files = @{ 'c.ot' = '# nothing' } }
        @{ Name = 'S port held'; Args = @('serve', 's.ot', '-Port', '18085'); HoldPort = 18085; TimeoutSeconds = 20; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'S port held by declaration'; Args = @('serve', 's.ot'); HoldPort = 18086; TimeoutSeconds = 20; Files = @{ 's.ot' = "api is a web server`n    port is 18086`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'S bad host'; Args = @('serve', 's.ot'); TimeoutSeconds = 20; Files = @{ 's.ot' = "api is a web server`n    port is 18087`n    host is `"bad host`"`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'S empty host'; Args = @('serve', 's.ot'); TimeoutSeconds = 20; Files = @{ 's.ot' = "api is a web server`n    port is 18088`n    host is `"`"`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'S port 70000'; Args = @('serve', 's.ot', '-Port', '70000'); TimeoutSeconds = 20; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'S port 80 (no admin?)'; Args = @('serve', 's.ot', '-Port', '80'); TimeoutSeconds = 10; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'S runs'; Args = @('serve', 's.ot', '-Port', '18089'); TimeoutSeconds = 8; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'S no routes'; Args = @('serve', 's.ot'); TimeoutSeconds = 8; Files = @{ 's.ot' = 'say 1' } }
        # otter desktop
        @{ Name = 'D missing import'; Args = @('desktop', 'imp.ot'); TimeoutSeconds = 60; Files = @{ 'imp.ot' = "use `"missing.ot`"`nheading `"x`"" } }
        @{ Name = 'D import cycle'; Args = @('desktop', 'a.ot'); TimeoutSeconds = 60; Files = @{ 'a.ot' = "use `"b.ot`"`nheading `"x`""; 'b.ot' = "use `"a.ot`"`nto f`n    say 1" } }
        @{ Name = 'D import is a folder'; Args = @('desktop', 'a.ot'); TimeoutSeconds = 60; Files = @{ 'a.ot' = "use `"lib.ot`"`nheading `"x`""; 'lib.ot/' = '' } }
        @{ Name = 'D page is a folder'; Args = @('desktop', 'app.ot'); TimeoutSeconds = 60; Files = @{ 'app.ot' = 'heading "x"'; 'app.html/' = '' } }
        @{ Name = 'D instance page is a folder'; Args = @('desktop', 'app.ot'); TimeoutSeconds = 60; Files = @{ 'app.ot' = 'heading "x"'; 'app.desktop.html/' = '' } }
    )
}
