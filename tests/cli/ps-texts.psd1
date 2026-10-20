# Texts Windows PowerShell 5.1 itself prints for otterPS, measured in CI
# (.github/workflows/cli-windows.yml): -Verbose, -InformationAction, and
# the errors of `otter serve` and `otter desktop` that escape otterPS.
@{
    Cases = @(
        @{ Name = '-Verbose with check shows nothing'; Args = @('check', 'a.ot', '-vb'); Files = @{ 'a.ot' = 'say 1' } }
    )
}
