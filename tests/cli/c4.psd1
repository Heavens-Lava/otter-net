# Coverage audit (c4): command-line paths the other case files never reach.
@{
    Cases = @(
        @{ Name = 'profile: a recursive function'; Args = @('profile', 'p.ot'); Mask = @('\s*-?[0-9][0-9. ]*(GB|MB|KB|B)?', '(?s)Hottest lines.*'); Files = @{ 'p.ot' = @'
to countdown n
    if n is 0
        return 0
    .
    m is n minus 1
    return countdown m
.
say countdown 5
'@ } }
        @{ Name = 'debug: locals with characters JSON escapes'; Args = @('debug', 'p.ot', '-Breakpoints', '2'); Stdin = @'
continue
'@; Files = @{ 'p.ot' = @'
to show odd
    say odd
show "<a & 'b'>\t\"q\"\\ end\nline"
'@ }; KnownOnPwsh7 = 'ConvertTo-Json -Compress on PowerShell 7 writes < > & and single quotes as they are; 5.1 (and otter-net) escapes them' }
        @{ Name = 'publish: a game project'; Args = @('publish'); KnownOnPwsh7 = 'packaged files (5.1 layout)'
            Mask = @('[0-9a-f]{64}')
            IgnoreFiles = @('publish/*.zip', 'publish/*.sha256', 'publish/otter.publish.json', 'publish/*/otter.publish.json')
            Files = @{ 'otter.json' = '{ "name": "Game", "version": "2.0.0", "entryPoint": "main.ot", "target": "game" }'; 'main.ot' = "app is a page`n    title is `"Game`"`n.`nshow app" } }
        @{ Name = 'publish: a desktop project'; Args = @('publish'); KnownOnPwsh7 = 'packaged files (5.1 layout)'
            Mask = @('[0-9a-f]{64}')
            IgnoreFiles = @('publish/*.zip', 'publish/*.sha256', 'publish/otter.publish.json', 'publish/*/otter.publish.json')
            Files = @{ 'otter.json' = '{ "name": "Desk", "version": "0.1.0", "entryPoint": "main.ot", "target": "desktop" }'; 'main.ot' = "app is a page`n    title is `"Desk`"`n.`nshow app" } }
        @{ Name = 'publish: an automation project'; Args = @('publish'); KnownOnPwsh7 = 'packaged files (5.1 layout)'
            Mask = @('[0-9a-f]{64}')
            IgnoreFiles = @('publish/*.zip', 'publish/*.sha256', 'publish/otter.publish.json', 'publish/*/otter.publish.json')
            Files = @{ 'otter.json' = '{ "name": "Auto", "version": "0.1.0", "entryPoint": "main.ot", "target": "automation" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'build: a web project'; Args = @('build'); KnownOnPwsh7 = 'the page (5.1 layout)'
            Files = @{ 'otter.json' = '{ "name": "Site", "version": "0.1.0", "entryPoint": "main.ot", "target": "web" }'; 'main.ot' = "app is a page`n    title is `"Site`"`n.`nshow app" } }
    )
}
