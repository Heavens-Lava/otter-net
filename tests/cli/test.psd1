# otter test: discovery, PASS/FAIL lines, failure details, summary and exit codes.
@{
    Cases = @(
        @{ Name = 'all passing'; Args = @('test'); Files = @{
            'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1'
            'tests/a_test.ot' = 'say "a"'; 'tests/test_b.ot' = 'say "b"'; 'tests/sub/c_test.ot' = 'say "c"'
            'tests/helper.ot' = 'say "not a test"' } }
        @{ Name = 'a runtime failure and a syntax error'; Args = @('test', '.'); Files = @{
            'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1'
            'tests/a_test.ot' = 'say "a"'
            'tests/b_test.ot' = @'
score is 99
if score is not 100
    fail with "Expected score to be 100."
.
'@
            'tests/c_test.ot' = @'
say "c"
x is
'@ } }
        @{ Name = 'only a runtime failure'; Args = @('test'); Files = @{
            'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1'
            'tests/a_test.ot' = 'say missing' } }
        @{ Name = 'a project folder'; Args = @('test', 'proj'); Files = @{
            'proj/otter.json' = '{ "entryPoint": "main.ot" }'; 'proj/main.ot' = 'say 1'
            'proj/tests/a_test.ot' = 'say "a"' } }
        @{ Name = 'a tests folder itself'; Args = @('test', 'tests'); Files = @{ 'tests/a_test.ot' = 'say "a"' } }
        @{ Name = 'one test file'; Args = @('test', 'tests/a_test.ot'); Files = @{ 'tests/a_test.ot' = 'say "a"' } }
        @{ Name = 'one test file outside tests'; Args = @('test', 'x_test.ot'); Files = @{ 'x_test.ot' = 'fail with "no"' } }
        @{ Name = 'no tests'; Args = @('test'); Files = @{ 'otter.json' = '{ "entryPoint": "main.ot" }'; 'main.ot' = 'say 1' } }
        @{ Name = 'a missing test file'; Args = @('test', 'nope.ot') }
        @{ Name = 'a missing folder'; Args = @('test', 'nowhere') }
        @{ Name = 'tests reading standard input'; Args = @('test'); Stdin = @'
Jeff
'@; Files = @{ 'tests/ask_test.ot' = @'
ask "Name?" and call it name
say name
'@ } }
    )
}
