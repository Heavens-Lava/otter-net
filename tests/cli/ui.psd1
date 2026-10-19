# The UI commands beyond a single `otter web` page: web, desktop and game
# projects (build, publish, web, browse), `otter serve`'s checks, `otter
# check` and `otter run` of UI programs, and web pages of other files.
@{
    Cases = @(
        @{ Name = 'check: a declarative page'; Args = @('check', 'page.ot'); Files = @{ 'page.ot' = @'
page "Home"
    heading "Welcome"
    card "One"
        text "Hello"
        primary button "Save"
            click
                say "saved"
'@ } }
        @{ Name = 'check: UI resources'; Args = @('check', 'app.ot'); Files = @{ 'app.ot' = @'
create window into app
create button into helloButton
text of helloButton is "Say hello"
put helloButton in app
when helloButton is clicked
    say "Hello"
show app
'@ } }
        @{ Name = 'check: a UI syntax error'; Args = @('check', 'bad.ot'); Files = @{ 'bad.ot' = "card is a panel`n    align sideways" } }
        @{ Name = 'run: declarative elements run their statements'; Args = @('run', 'page.ot'); Files = @{ 'page.ot' = "say 1`npage `"Home`"`n    heading `"Hi`"`nsay 2" } }
        @{ Name = 'run: create without WPF'; Args = @('run', 'app.ot'); Files = @{ 'app.ot' = "say `"before`"`ncreate card into c`nsay `"after`"" } }
        @{ Name = 'run: state and watchers'; Args = @('run', 's.ot'); Files = @{ 's.ot' = "state n is 1`nwhen n changes`n    say `"n is`" n`nn is 2`nn is 2`nn is 3" } }
        @{ Name = 'web: a project folder'; Args = @('web', 'proj', '-NoOpen'); KnownOnPwsh7 = 'the page: byte order mark and \r\n of 5.1'; Files = @{
            'proj/otter.json' = '{ "name": "site", "version": "1.0.0", "target": "web", "entryPoint": "app.ot" }'; 'proj/app.ot' = 'heading "Hi"' } }
        @{ Name = 'browse: a manifest'; Args = @('browse', 'proj/otter.json', '-NoOpen'); KnownOnPwsh7 = 'the page (5.1 form)'; Files = @{
            'proj/otter.json' = '{ "entryPoint": "app.ot", "target": "web" }'; 'proj/app.ot' = 'heading "Hi"' } }
        @{ Name = 'web: a file that is not .ot'; Args = @('web', 'notes.txt', '-NoOpen'); KnownOnPwsh7 = 'the page (5.1 form)'; Files = @{ 'notes.txt' = 'say "hi"' } }
        @{ Name = 'web: an import that is missing'; Args = @('web', 'imp.ot', '-NoOpen'); Files = @{ 'imp.ot' = "use `"missing.ot`"`nsay 1" } }
        @{ Name = 'web: a project with a manifest error'; Args = @('web', 'proj', '-NoOpen'); Files = @{
            'proj/otter.json' = '{ "target": "web" }'; 'proj/app.ot' = 'say 1' } }
        @{ Name = 'serve: a syntax error'; Args = @('serve', 'bad.ot'); Files = @{ 'bad.ot' = "say `"x`"`nwhen api receives GET at" } }
        @{ Name = 'serve: an import that is missing'; Args = @('serve', 'imp.ot'); Files = @{ 'imp.ot' = "use `"missing.ot`"`nsay 1" } }
        @{ Name = 'serve: a reserved name'; Args = @('serve', 'r.ot'); Files = @{ 'r.ot' = "to main`n    say 1" } }
        @{ Name = 'build: a web project'; Args = @('build'); KnownOnPwsh7 = 'built files (5.1 layout)'; Files = @{
            'otter.json' = '{ "name": "site", "version": "1.0.0", "target": "web", "entryPoint": "app.ot", "assets": ["logo.txt"] }'
            'app.ot' = "heading `"Hi`"`nsay `"loaded`""; 'app.css' = 'body { color: red; }'; 'logo.txt' = 'logo' } }
        @{ Name = 'build: a desktop project'; Args = @('build'); KnownOnPwsh7 = 'built files (5.1 layout)'; Files = @{
            'otter.json' = '{ "name": "tool", "version": "1.0.0", "target": "desktop", "entryPoint": "app.ot" }'; 'app.ot' = 'card "Hi"' } }
        @{ Name = 'build: a game project'; Args = @('build'); KnownOnPwsh7 = 'built files (5.1 layout)'; Files = @{
            'otter.json' = '{ "name": "game", "version": "1.0.0", "target": "game", "entryPoint": "game.ot" }'; 'game.ot' = 'say "play"' } }
        @{ Name = 'build: a web project whose page Otter refuses'; Args = @('build'); Files = @{
            'otter.json' = '{ "target": "web", "entryPoint": "app.ot" }'; 'app.ot' = 'say "x" in color "red"' } }
        @{ Name = 'build: a web project whose compiler fails inside itself'; Args = @('build'); Files = @{
            'otter.json' = '{ "target": "web", "entryPoint": "app.ot" }'; 'app.ot' = "to build doc`n    add element `"book`" to doc and call it book`n    return book`nsay 1" } }
        @{ Name = 'web: the compiler fails inside itself'; Args = @('web', 'app.ot', '-NoOpen'); Files = @{
            'app.ot' = "to build doc`n    add element `"book`" to doc and call it book`n    return book`nsay 1" } }
        @{ Name = 'build: a web project with a syntax error'; Args = @('build'); Files = @{
            'otter.json' = '{ "target": "web", "entryPoint": "app.ot" }'; 'app.ot' = 'x is' } }
        @{ Name = 'publish: a web project'; Args = @('publish'); KnownOnPwsh7 = 'packaged files (5.1 layout)'
            Mask = @('[0-9a-f]{64}')
            IgnoreFiles = @('publish/*.zip', 'publish/*.sha256', 'publish/otter.publish.json', 'publish/*/otter.publish.json')
            Files = @{ 'otter.json' = '{ "name": "Site", "version": "2.0.0", "target": "web", "entryPoint": "app.ot" }'; 'app.ot' = 'heading "Hi"' } }
        @{ Name = 'publish: a web project whose page Otter refuses'; Args = @('publish'); Files = @{
            'otter.json' = '{ "name": "Site", "version": "2.0.0", "target": "web", "entryPoint": "app.ot" }'; 'app.ot' = 'say "x" in color "red"' } }
    )
}
