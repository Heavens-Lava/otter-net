# otter run / otter check / the shortest form, with otter.ps1's own flags (D57, D94).
@{
    Cases = @(
        @{ Name = 'run with arguments'; Args = @('run', 'a.ot', 'x', 'y z'); Files = @{ 'a.ot' = 'say "args:" arguments' } }
        @{ Name = 'shortest form with arguments'; Args = @('a.ot', '1', '2'); Files = @{ 'a.ot' = 'say "args:" arguments' } }
        @{ Name = 'capital letters in the command and extension'; Args = @('Run', 'u.OT'); Files = @{ 'u.OT' = 'say "upper"' } }
        @{ Name = 'own flags are not program arguments'; Args = @('run', 'a.ot', 'x', '-Port', '8080', '-Open', '-NoOpen', 'y'); Files = @{ 'a.ot' = 'say "args:" arguments' } }
        @{ Name = 'flags before the command shift the arguments'; Args = @('-DebugErrors', 'run', 'a.ot', 'k'); Files = @{ 'a.ot' = 'say "args:" arguments' } }
        @{ Name = '-Breakpoints on a plain run'; Args = @('run', 'a.ot', '-Breakpoints', '1,2', 'z'); Files = @{ 'a.ot' = 'say "args:" arguments' } }
        @{ Name = '-DebugTokens'; Args = @('a.ot', '-DebugTokens'); Files = @{ 'a.ot' = @'
to double n
    return n times 2
say double 21
if true
    say "yes"
.
'@ } }
        @{ Name = '-DebugTokens with a syntax error'; Args = @('run', 'bad.ot', '-DebugTokens'); Files = @{ 'bad.ot' = @'
say "ok"
x is
'@ } }
        @{ Name = '-ParseOnly'; Args = @('run', 'a.ot', '-ParseOnly'); Files = @{ 'a.ot' = 'say "not printed"' } }
        @{ Name = '-ParseOnly on the shortest form'; Args = @('a.ot', '-parseonly'); Files = @{ 'a.ot' = 'say "not printed"' } }
        @{ Name = 'check'; Args = @('check', 'a.ot'); Files = @{ 'a.ot' = 'say "not printed"' } }
        @{ Name = 'check with -ParseOnly'; Args = @('check', 'a.ot', '-ParseOnly'); Files = @{ 'a.ot' = 'say "not printed"' } }
        @{ Name = 'check: a second file is only an argument'; Args = @('check', 'a.ot', 'bad.ot'); Files = @{ 'a.ot' = 'say 1'; 'bad.ot' = 'x is' } }
        @{ Name = 'check: syntax errors'; Args = @('check', 'bad.ot'); Files = @{ 'bad.ot' = @'
say "one"
x is
y is
'@ } }
        @{ Name = 'check: reserved name'; Args = @('check', 'r.ot'); Files = @{ 'r.ot' = @'
to main
    say "never"
'@ } }
        @{ Name = 'run: runtime error'; Args = @('run', 'e.ot'); Files = @{ 'e.ot' = @'
say "before"
say nothing
'@ } }
        @{ Name = 'run: runtime error with -DebugErrors'; Args = @('run', 'e.ot', '-DebugErrors'); Files = @{ 'e.ot' = 'fail with "stop here"' } }
        @{ Name = 'run: ask reads standard input'; Args = @('run', 'ask.ot'); Stdin = @'
Jeff
41
'@; Files = @{ 'ask.ot' = @'
ask "Name?" and call it name
ask "Age?" and call it age
say name age plus 1
'@ } }
        @{ Name = 'run: a file in another folder'; Args = @('run', 'sub/b.ot'); Files = @{ 'sub/b.ot' = 'say "from sub"' } }
        @{ Name = 'check: a file in another folder'; Args = @('check', 'sub/b.ot'); Files = @{ 'sub/b.ot' = 'say "from sub"' } }
        @{ Name = 'an empty program'; Args = @('empty.ot'); Files = @{ 'empty.ot' = '' } }
    )
}
