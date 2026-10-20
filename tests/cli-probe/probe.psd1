# Measurement only (cli-windows.yml): texts PowerShell itself prints. Round 2.
@{
    Cases = @(
        @{ Name = 'IA try say Stop'; Args = @('t.ot', '-InformationAction', 'Stop'); Files = @{ 't.ot' = "try`n    say 1`notherwise into reason`n    say reason`n.`nsay 2" } }
        @{ Name = 'IA try say Ignore'; Args = @('t.ot', '-InformationAction', 'Ignore'); Files = @{ 't.ot' = "try`n    say 1`notherwise into reason`n    say 2`n.`nsay 3" } }
        @{ Name = 'IA try say interpreter Stop'; Args = @('t.ot', '-InformationAction', 'Stop'); Env = @{ OTTER_ENGINE = 'interpreter' }; Files = @{ 't.ot' = "try`n    say 1`notherwise into reason`n    set x to reason`n.`nsay x" } }
        @{ Name = 'IA try say interpreter Ignore'; Args = @('t.ot', '-InformationAction', 'Ignore'); Env = @{ OTTER_ENGINE = 'interpreter' }; Files = @{ 't.ot' = "try`n    say 1`notherwise into reason`n    x is reason`n.`nsay x" } }
        @{ Name = 'IA DebugTokens Stop'; Args = @('a.ot', '-DebugTokens', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'IA DebugAst Ignore'; Args = @('a.ot', '-DebugAst', '-InformationAction', 'Ignore'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'IA ParseOnly Stop'; Args = @('a.ot', '-ParseOnly', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'IA profile Ignore'; Args = @('profile', 'a.ot', '-InformationAction', 'Ignore'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'IA profile silent program Stop'; Args = @('profile', 'a.ot', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'x is 1' } }
        @{ Name = 'IA debug Stop'; Args = @('debug', 'a.ot', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'IA debug silent Ignore'; Args = @('debug', 'a.ot', '-InformationAction', 'Ignore'); Files = @{ 'a.ot' = 'x is 1' } }
        @{ Name = 'IA ask Stop'; Args = @('a.ot', '-InformationAction', 'Stop'); Stdin = "Jo`n"; Files = @{ 'a.ot' = "ask `"Name?`" into n`nsay n" } }
        @{ Name = 'IA serve syntax error Stop'; Args = @('serve', 'bad.ot', '-InformationAction', 'Stop'); Files = @{ 'bad.ot' = 'say (' } }
        @{ Name = 'IA serve runs Stop'; Args = @('serve', 's.ot', '-Port', '18090', '-InformationAction', 'Stop'); TimeoutSeconds = 15; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'IA serve runs Ignore'; Args = @('serve', 's.ot', '-Port', '18091', '-InformationAction', 'Ignore'); TimeoutSeconds = 15; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'IA serve empty Stop'; Args = @('serve', 'e.ot', '-InformationAction', 'Stop'); Files = @{ 'e.ot' = '' } }
        @{ Name = 'IA web error Stop'; Args = @('web', 'bad.ot', '-NoOpen', '-InformationAction', 'Stop'); Files = @{ 'bad.ot' = 'say (' } }
        @{ Name = 'IA web error Ignore'; Args = @('web', 'bad.ot', '-NoOpen', '-InformationAction', 'Ignore'); Files = @{ 'bad.ot' = 'say (' } }
        @{ Name = 'IA web missing import Stop'; Args = @('web', 'i.ot', '-NoOpen', '-InformationAction', 'Stop'); Files = @{ 'i.ot' = "use `"m.ot`"`nheading `"x`"" } }
        @{ Name = 'IA web open Ignore'; Args = @('web', 'p.ot', '-InformationAction', 'Ignore'); TimeoutSeconds = 30; Files = @{ 'p.ot' = 'heading "Hi"' } }
        @{ Name = 'IA web open Stop'; Args = @('browse', 'p.ot', '-InformationAction', 'Stop'); TimeoutSeconds = 30; Files = @{ 'p.ot' = 'heading "Hi"' } }
        @{ Name = 'IA desktop Stop'; Args = @('desktop', 'p.ot', '-InformationAction', 'Stop'); TimeoutSeconds = 40; Files = @{ 'p.ot' = 'heading "Hi"' } }
        @{ Name = 'IA desktop Ignore'; Args = @('desktop', 'p.ot', '-InformationAction', 'Ignore'); TimeoutSeconds = 40; Files = @{ 'p.ot' = 'heading "Hi"' } }
        @{ Name = 'IA desktop missing import Stop'; Args = @('desktop', 'i.ot', '-InformationAction', 'Stop'); Files = @{ 'i.ot' = "use `"m.ot`"`nheading `"x`"" } }
        @{ Name = 'IA new usage Stop'; Args = @('new', '-InformationAction', 'Stop') }
        @{ Name = 'IA new exists Stop'; Args = @('new', 'console', 'app', '-InformationAction', 'Stop'); Files = @{ 'app/x.txt' = 'x' } }
        @{ Name = 'IA new exists Ignore'; Args = @('new', 'console', 'app', '-InformationAction', 'Ignore'); Files = @{ 'app/x.txt' = 'x' } }
        @{ Name = 'IA build no manifest Stop'; Args = @('build', '-InformationAction', 'Stop') }
        @{ Name = 'IA build no manifest Ignore'; Args = @('build', '-InformationAction', 'Ignore') }
        @{ Name = 'IA publish Stop'; Args = @('publish', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "name": "c", "version": "1.0.0", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'IA publish Ignore'; Args = @('publish', '-InformationAction', 'Ignore'); Files = @{ 'otter.json' = '{ "name": "c", "version": "1.0.0", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'IA publish no name Stop'; Args = @('publish', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'IA test tests Ignore'; Args = @('test', '-InformationAction', 'Ignore'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1'; 'tests/a_test.ot' = 'say 1' } }
        @{ Name = 'IA test tests Stop'; Args = @('test', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1'; 'tests/a_test.ot' = 'say 1' } }
        @{ Name = 'IA project bad manifest Stop'; Args = @('run', '.', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "entryPoint": 5 }' } }
        @{ Name = 'IA project bad manifest path Ignore'; Args = @('.', '-InformationAction', 'Ignore'); Files = @{ 'otter.json' = '{ "entryPoint": 5 }' } }
        @{ Name = 'IA project run Stop'; Args = @('.', '-InformationAction', 'Stop'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'IA folder no manifest Stop'; Args = @('run', 'sub', '-InformationAction', 'Stop'); Files = @{ 'sub/' = '' } }
        @{ Name = 'IA not ot Stop'; Args = @('run', 'x.txt', '-InformationAction', 'Stop') }
        @{ Name = 'IA run usage Ignore'; Args = @('run', '-InformationAction', 'Ignore') }
        @{ Name = 'IA folder file Stop'; Args = @('dir.ot', '-InformationAction', 'Stop'); Files = @{ 'dir.ot/' = '' } }
        @{ Name = 'IA import missing Stop'; Args = @('i.ot', '-InformationAction', 'Stop'); Files = @{ 'i.ot' = "use `"m.ot`"`nsay 1" } }
        @{ Name = 'IA empty program Stop'; Args = @('e.ot', '-InformationAction', 'Stop'); Files = @{ 'e.ot' = '' } }
        @{ Name = 'IA repl say Stop after banner'; Args = @('-infa', 'Stop'); Stdin = "say 1`n" }
        @{ Name = 'IA Verbose Stop'; Args = @('a.ot', '-Verbose', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'IA studio Stop'; Args = @('studio', '-InformationAction', 'Stop') }
        @{ Name = 'IA lowercase stop'; Args = @('--version', '-informationaction', 'stop') }
        @{ Name = 'IA numeric 1'; Args = @('--version', '-infa', '1') }
        @{ Name = 'IA colon Ignore'; Args = @('--version', '-InformationAction:Ignore') }
        @{ Name = 'IA long line Stop'; Args = @('a.ot', '-InformationAction', 'Stop'); Files = @{ 'a.ot' = "say `"$('x' * 150)`"" } }
        # -Verbose, more paths
        @{ Name = 'V serve runs'; Args = @('serve', 's.ot', '-Port', '18092', '-Verbose'); TimeoutSeconds = 15; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'V serve empty'; Args = @('serve', 'e.ot', '-Verbose'); Files = @{ 'e.ot' = '' } }
        @{ Name = 'V serve held'; Args = @('serve', 's.ot', '-Port', '18093', '-Verbose'); HoldPort = 18093; Files = @{ 's.ot' = "when api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'V desktop runs'; Args = @('desktop', 'p.ot', '-Verbose'); TimeoutSeconds = 40; Files = @{ 'p.ot' = 'heading "Hi"' } }
        @{ Name = 'V desktop page folder'; Args = @('desktop', 'app.ot', '-Verbose'); Files = @{ 'app.ot' = 'heading "x"'; 'app.html/' = '' } }
        @{ Name = 'V desktop syntax error'; Args = @('desktop', 'bad.ot', '-Verbose'); Files = @{ 'bad.ot' = 'say (' } }
        @{ Name = 'V web error'; Args = @('web', 'bad.ot', '-NoOpen', '-Verbose'); Files = @{ 'bad.ot' = 'say (' } }
        @{ Name = 'V web missing import'; Args = @('web', 'i.ot', '-NoOpen', '-Verbose'); Files = @{ 'i.ot' = "use `"m.ot`"`nheading `"x`"" } }
        @{ Name = 'V web long path'; Args = @('web', 'a-very-long-folder-name-for-wrapping-the-verbose-line/page-with-a-long-name.ot', '-NoOpen', '-Verbose'); Files = @{ 'a-very-long-folder-name-for-wrapping-the-verbose-line/page-with-a-long-name.ot' = 'heading "Hi"' } }
        @{ Name = 'V web project'; Args = @('web', '.', '-NoOpen', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "app.ot", "target": "web" }'; 'app.ot' = 'heading "Hi"' } }
        @{ Name = 'V build desktop'; Args = @('build', '-Verbose'); Files = @{ 'otter.json' = '{ "name": "d", "version": "1.0.0", "entryPoint": "app.ot", "target": "desktop" }'; 'app.ot' = 'heading "Hi"' } }
        @{ Name = 'V build game'; Args = @('build', '-Verbose'); Files = @{ 'otter.json' = '{ "name": "g", "version": "1.0.0", "entryPoint": "app.ot", "target": "game" }'; 'app.ot' = 'heading "Hi"' } }
        @{ Name = 'V build server'; Args = @('build', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot", "target": "server" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'V build no manifest'; Args = @('build', '-Verbose') }
        @{ Name = 'V build syntax error'; Args = @('build', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say (' } }
        @{ Name = 'V build web syntax error'; Args = @('build', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "app.ot", "target": "web" }'; 'app.ot' = 'say (' } }
        @{ Name = 'V publish web'; Args = @('publish', '-Verbose'); Files = @{ 'otter.json' = '{ "name": "w", "version": "1.0.0", "entryPoint": "app.ot", "target": "web" }'; 'app.ot' = 'heading "Hi"' } }
        @{ Name = 'V publish no name'; Args = @('publish', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'V test with tests'; Args = @('test', '-Verbose'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1'; 'tests/a_test.ot' = 'say 1' } }
        @{ Name = 'V repl'; Args = @('-Verbose'); Stdin = "say 1`nexit`n" }
        @{ Name = 'V run project'; Args = @('.', '-vb'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'V run missing file'; Args = @('run', 'missing.ot', '-Verbose') }
        @{ Name = 'V run import missing'; Args = @('i.ot', '-Verbose'); Files = @{ 'i.ot' = "use `"m.ot`"`nsay 1" } }
        @{ Name = 'V run ParseOnly'; Args = @('a.ot', '-ParseOnly', '-Verbose'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V run DebugAst'; Args = @('a.ot', '-DebugAst', '-Verbose'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V run empty'; Args = @('e.ot', '-Verbose'); Files = @{ 'e.ot' = '' } }
        @{ Name = 'V run engine compiled forced'; Args = @('a.ot', '-Verbose'); Env = @{ OTTER_ENGINE = 'compiled' }; Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V profile error'; Args = @('profile', 'bad.ot', '-Verbose'); Files = @{ 'bad.ot' = 'say (' } }
        @{ Name = 'V profile missing'; Args = @('profile', 'nope.ot', '-Verbose') }
        @{ Name = 'V debug bad breakpoint'; Args = @('debug', 'a.ot', '-Breakpoints', 'x', '-Verbose'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V false'; Args = @('a.ot', '-Verbose:$false'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'V studio'; Args = @('studio', '-Verbose') }
        # serve: more start failures
        @{ Name = 'S scheme host'; Args = @('serve', 's.ot'); Files = @{ 's.ot' = "api is a web server`n    port is 18094`n    host is `"a/b`"`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'S port 0'; Args = @('serve', 's.ot'); Files = @{ 's.ot' = "api is a web server`n    port is 0`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'S negative port'; Args = @('serve', 's.ot'); Files = @{ 's.ot' = "api is a web server`n    port is -5`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'S host star'; Args = @('serve', 's.ot'); TimeoutSeconds = 10; Files = @{ 's.ot' = "api is a web server`n    port is 18095`n    host is `"*`"`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'S host colon'; Args = @('serve', 's.ot'); TimeoutSeconds = 10; Files = @{ 's.ot' = "api is a web server`n    port is 18096`n    host is `"a:b`"`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'S tab only'; Args = @('serve', 't.ot'); TimeoutSeconds = 8; Files = @{ 't.ot' = "`t" } }
        # desktop: more
        @{ Name = 'D import syntax error'; Args = @('desktop', 'a.ot'); Files = @{ 'a.ot' = "use `"b.ot`"`nheading `"x`""; 'b.ot' = 'say (' } }
        @{ Name = 'D import not ot'; Args = @('desktop', 'a.ot'); Files = @{ 'a.ot' = "use `"b.txt`"`nheading `"x`""; 'b.txt' = 'x' } }
        @{ Name = 'D import outside'; Args = @('desktop', 'sub/a.ot'); Files = @{ 'sub/a.ot' = "use `"../b.ot`"`nheading `"x`""; 'b.ot' = 'to f`n    say 1' } }
        @{ Name = 'D long name page folder'; Args = @('desktop', 'a-much-longer-application-file-name-to-wrap.ot'); Files = @{ 'a-much-longer-application-file-name-to-wrap.ot' = 'heading "x"'; 'a-much-longer-application-file-name-to-wrap.html/' = '' } }
        @{ Name = 'D project import missing'; Args = @('desktop', '.'); Files = @{ 'otter.json' = '{ "entryPoint": "a.ot", "target": "desktop" }'; 'a.ot' = "use `"m.ot`"`nheading `"x`"" } }
    )
}
