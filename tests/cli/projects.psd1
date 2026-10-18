# Projects: otter.json / project.json, run and check through a manifest (D118B, RC3 B4/B5).
@{
    Cases = @(
        @{ Name = 'run a project folder'; Args = @('run', 'proj', 'a', 'b'); Files = @{
            'proj/otter.json' = '{ "name": "demo", "entryPoint": "main.ot" }'
            'proj/main.ot' = 'say "demo" arguments' } }
        @{ Name = 'shortest form: a project folder'; Args = @('proj', 'a'); Files = @{
            'proj/otter.json' = '{ "name": "demo", "entryPoint": "main.ot" }'
            'proj/main.ot' = 'say "demo" arguments' } }
        @{ Name = 'run .'; Args = @('run', '.'); Files = @{
            'otter.json' = '{ "entryPoint": "src/app.ot" }'
            'src/app.ot' = 'say "app"' } }
        @{ Name = 'check a project'; Args = @('check', 'proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.ot" }'
            'proj/main.ot' = 'say 1' } }
        @{ Name = 'check the manifest itself'; Args = @('check', 'proj/otter.json'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.ot" }'
            'proj/main.ot' = 'say 1' } }
        @{ Name = 'project.json with main'; Args = @('run', 'proj'); Files = @{
            'proj/project.json' = '{ "main": "start.ot" }'
            'proj/start.ot' = 'say "legacy"' } }
        @{ Name = 'otter.json wins over project.json'; Args = @('run', 'proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "a.ot" }'
            'proj/project.json' = '{ "main": "b.ot" }'
            'proj/a.ot' = 'say "a"'; 'proj/b.ot' = 'say "b"' } }
        @{ Name = 'entryPoint missing'; Args = @('run', 'proj'); Files = @{ 'proj/otter.json' = '{ "name": "x" }' } }
        @{ Name = 'entryPoint is a number'; Args = @('run', 'proj'); Files = @{ 'proj/otter.json' = '{ "entryPoint": 5 }' } }
        @{ Name = 'entryPoint does not exist'; Args = @('run', 'proj'); Files = @{ 'proj/otter.json' = '{ "entryPoint": "nope.ot" }' } }
        @{ Name = 'entryPoint outside the project'; Args = @('run', 'proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "../outside.ot" }'; 'outside.ot' = 'say "outside"' } }
        @{ Name = 'entryPoint is not a .ot file'; Args = @('run', 'proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.txt" }'; 'proj/main.txt' = 'say 1' } }
        @{ Name = 'an empty manifest'; Args = @('check', 'proj'); Files = @{ 'proj/otter.json' = '' } }
        @{ Name = 'a manifest that is a list'; Args = @('check', 'proj'); Files = @{ 'proj/otter.json' = '["a"]' }
           KnownOnPwsh7 = 'ConvertFrom-Json in PowerShell 7 unwraps a one-item array to its item ("a": text); 5.1 keeps the array (a list)' }
        @{ Name = 'a manifest that is text'; Args = @('check', 'proj'); Files = @{ 'proj/otter.json' = '"hello"' } }
        @{ Name = 'an unsupported target'; Args = @('check', 'proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.ot", "target": "phone" }'; 'proj/main.ot' = 'say 1' } }
        @{ Name = 'build.clean as text'; Args = @('check', 'proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.ot", "build": { "clean": "false" } }'; 'proj/main.ot' = 'say 1' } }
        @{ Name = 'build is null'; Args = @('check', 'proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.ot", "build": null }'; 'proj/main.ot' = 'say 1' } }
        @{ Name = 'build.outputDir outside'; Args = @('check', 'proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.ot", "build": { "outputDir": "../out" } }'; 'proj/main.ot' = 'say 1' } }
        @{ Name = 'assets is text'; Args = @('check', 'proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.ot", "assets": "a.png" }'; 'proj/main.ot' = 'say 1' } }
        @{ Name = 'an asset that is a number'; Args = @('check', 'proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.ot", "assets": ["a.png", 3] }'; 'proj/main.ot' = 'say 1' } }
        @{ Name = 'a script that is not text'; Args = @('check', 'proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.ot", "scripts": { "start": true } }'; 'proj/main.ot' = 'say 1' } }
        @{ Name = 'property names ignore capitals'; Args = @('run', 'proj'); Files = @{
            'proj/otter.json' = '{ "EntryPoint": "main.ot", "NAME": "x" }'; 'proj/main.ot' = 'say "found"' } }
        @{ Name = 'a project whose program has a syntax error'; Args = @('proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.ot" }'; 'proj/main.ot' = 'x is' } }
    )
}
