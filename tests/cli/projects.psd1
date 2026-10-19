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
        # A manifest that is not valid JSON: ConvertFrom-Json's message, which is
        # JavaScriptSerializer's on Windows PowerShell 5.1 (Newtonsoft.Json's on 7).
        @{ Name = 'manifest: an unquoted value'; Args = @('run', '.'); Files = @{ 'otter.json' = '{ "entryPoint": main.ot }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'ConvertFrom-Json is Newtonsoft.Json on PowerShell 7' }
        @{ Name = 'manifest: a trailing comma'; Args = @('run', '.'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot", }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'Newtonsoft.Json accepts a trailing comma' }
        @{ Name = 'manifest: not closed'; Args = @('check', '.'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot"'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'ConvertFrom-Json is Newtonsoft.Json on PowerShell 7' }
        @{ Name = 'manifest: -NaN'; Args = @('run', '.'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot", "version": -NaN }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'ConvertFrom-Json is Newtonsoft.Json on PowerShell 7' }
        @{ Name = 'manifest: a date too big for a long'; Args = @('run', '.'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot", "description": "\/Date(99999999999999999999)\/" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'manifest: text after the object'; Args = @('run', '.'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" } }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'ConvertFrom-Json is Newtonsoft.Json on PowerShell 7' }
        @{ Name = 'manifest: a bad escape'; Args = @('build'); Files = @{ 'otter.json' = '{ "entryPoint": "ma\qin.ot" }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'ConvertFrom-Json is Newtonsoft.Json on PowerShell 7' }
        @{ Name = 'manifest: an array not closed'; Args = @('run', '.'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot", "assets": ["a" "b"] }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'ConvertFrom-Json is Newtonsoft.Json on PowerShell 7' }
        @{ Name = 'manifest: names differing in capitals'; Args = @('run', '.'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot", "EntryPoint": "x.ot" }'; 'main.ot' = 'say 1' }; KnownOnPwsh7 = 'PowerShell 7 words the duplicated keys differently' }
        @{ Name = 'manifest: single quotes are accepted'; Args = @('run', '.'); Files = @{ 'otter.json' = "{ 'entryPoint': 'main.ot' }"; 'main.ot' = 'say 1' } }
        # "~" is the home folder, as PowerShell's Resolve-Path expands it.
        @{ Name = '~ alone is a folder'; Args = @('~') }
        @{ Name = 'run a missing file under ~'; Args = @('run', '~/otter-net-cli-test-missing.ot') }
        @{ Name = 'check a missing project under ~'; Args = @('check', '~/otter-net-cli-test-missing') }
    )
}
