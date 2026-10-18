# otter build and otter publish for console and automation projects.
@{
    Cases = @(
        @{ Name = 'build'; Args = @('build'); KnownOnPwsh7 = 'built files: byte order mark, \r\n and the ConvertTo-Json layout of 5.1'; Files = @{
            'otter.json' = '{ "name": "demo", "version": "1.2.0", "entryPoint": "main.ot", "assets": ["data/info.txt"] }'
            'main.ot' = @'
use "lib.ot"
say greeting
'@
            'lib.ot' = 'greeting is "hello"'
            'data/info.txt' = 'info' } }
        @{ Name = 'build a project folder'; Args = @('build', 'proj'); KnownOnPwsh7 = 'built files (5.1 layout)'; Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.ot", "archetype": "automation" }'; 'proj/main.ot' = 'say 1' } }
        @{ Name = 'build: syntax error'; Args = @('build'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'x is' } }
        @{ Name = 'build: use error'; Args = @('build'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'use "gone.ot"' } }
        @{ Name = 'build: missing asset'; Args = @('build'); Files = @{
            'otter.json' = '{ "entryPoint": "main.ot", "assets": ["nope.png"] }'; 'main.ot' = 'say 1' } }
        @{ Name = 'build: absolute asset'; Args = @('build'); Files = @{
            'otter.json' = '{ "entryPoint": "main.ot", "assets": ["/etc/hosts"] }'; 'main.ot' = 'say 1' } }
        @{ Name = 'build: asset outside'; Args = @('build', 'proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.ot", "assets": ["../x.txt"] }'; 'proj/main.ot' = 'say 1'; 'x.txt' = 'x' } }
        @{ Name = 'build: output folder not made by Otter'; Args = @('build'); Files = @{
            'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1'; 'dist/mine.txt' = 'keep me' } }
        @{ Name = 'build: output folder holding the program'; Args = @('build'); Files = @{
            'otter.json' = '{ "entryPoint": "src/main.ot", "build": { "outputDir": "src" } }'; 'src/main.ot' = 'say 1' } }
        @{ Name = 'build: output folder is the project'; Args = @('build'); Files = @{
            'otter.json' = '{ "entryPoint": "main.ot", "build": { "outputDir": "." } }'; 'main.ot' = 'say 1' } }
        @{ Name = 'build: a server project'; Args = @('build'); Files = @{
            'otter.json' = '{ "entryPoint": "main.ot", "target": "server" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'build: no manifest'; Args = @('build') }
        @{ Name = 'publish'; Args = @('publish'); KnownOnPwsh7 = 'packaged files (5.1 layout)'
            Mask = @('[0-9a-f]{64}')
            IgnoreFiles = @('publish/*.zip', 'publish/*.sha256', 'publish/otter.publish.json', 'publish/*/otter.publish.json')
            Files = @{ 'otter.json' = '{ "name": "My App", "version": "1.0.0", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'publish: no version'; Args = @('publish'); Files = @{
            'otter.json' = '{ "name": "app", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'publish: no name'; Args = @('publish'); Files = @{
            'otter.json' = '{ "version": "1.0.0", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'publish: a version that is a path'; Args = @('publish'); Files = @{
            'otter.json' = '{ "name": "app", "version": "1/../../x", "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'publish: syntax error'; Args = @('publish'); Files = @{
            'otter.json' = '{ "name": "app", "version": "1.0.0", "entryPoint": "main.ot" }'; 'main.ot' = 'x is' } }
    )
}
