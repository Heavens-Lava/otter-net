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
    )
}
