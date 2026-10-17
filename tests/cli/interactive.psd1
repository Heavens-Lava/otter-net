# The REPL with redirected input, otter debug and otter profile.
@{
    Cases = @(
        @{ Name = 'repl: variables persist'; Args = @(); Stdin = @'
x is 5
say x
exit
'@ }
        @{ Name = 'repl: a block until a blank line'; Args = @(); Stdin = @'
to greet name
    say "Hello" name

greet "Jeff"
EXIT
'@ }
        @{ Name = 'repl: errors are short and the session goes on'; Args = @(); Stdin = @'
say undefinedThing
y is
y is 10
say y
reset
say y
'@ }
        @{ Name = 'repl: end of input'; Args = @(); Stdin = 'say "last"' }
        @{ Name = 'repl: ask reads the next line'; Args = @(); Stdin = @'
ask "Name?" and call it name
Jeff
say name
'@ }
        @{ Name = 'repl: -DebugTokens'; Args = @('-DebugTokens'); Stdin = @'
say 1
'@ }
        @{ Name = 'debug: pause in a function'; Args = @('debug', 'p.ot', '-Breakpoints', '2'); Stdin = @'
continue
'@; Files = @{ 'p.ot' = @'
to greet name
    say "Hello" name
greet "Jeff"
say "done"
'@ } }
        @{ Name = 'debug: no breakpoints'; Args = @('debug', 'p.ot'); Files = @{ 'p.ot' = 'say "plain"' } }
        @{ Name = 'debug: end of input continues'; Args = @('debug', 'p.ot', '-Breakpoints', '1,2'); Files = @{ 'p.ot' = @'
to f
    say "in f"
f
'@ } }
        @{ Name = 'debug: a runtime error'; Args = @('debug', 'p.ot'); Files = @{ 'p.ot' = 'say nothing' } }
        @{ Name = 'debug: a syntax error'; Args = @('debug', 'p.ot'); Files = @{ 'p.ot' = 'x is' } }
        @{ Name = 'debug: missing file'; Args = @('debug', 'nope.ot') }
        @{ Name = 'profile: shape of the report'; Args = @('profile', 'p.ot'); Mask = @('\s*-?[0-9][0-9. ]*(GB|MB|KB|B)?'); Files = @{ 'p.ot' = 'say "x"' } }
        @{ Name = 'profile: missing file'; Args = @('profile', 'nope.ot') }
    )
}
