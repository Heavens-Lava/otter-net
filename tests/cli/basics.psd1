# Version, help, unknown commands and usage errors (D57). See tools/compare-cli.ps1.
@{
    Cases = @(
        @{ Name = '--version'; Args = @('--version') }
        @{ Name = '-version'; Args = @('-version') }
        @{ Name = '-Version, other capitals'; Args = @('-Version') }
        @{ Name = 'version is not a command'; Args = @('version') }
        @{ Name = 'help'; Args = @('help') }
        @{ Name = 'HELP'; Args = @('HELP') }
        @{ Name = '--help'; Args = @('--help') }
        @{ Name = '-help'; Args = @('-help') }
        @{ Name = '--help after a file'; Args = @('a.ot', '--help'); Files = @{ 'a.ot' = 'say 1' } }
        @{ Name = 'unknown command'; Args = @('bogus') }
        @{ Name = 'a folder without a manifest'; Args = @('sub'); Files = @{ 'sub/' = '' } }
        @{ Name = 'dot without a manifest'; Args = @('.') }
        @{ Name = 'run without a file'; Args = @('run') }
        @{ Name = 'RUN without a file'; Args = @('RUN') }
        @{ Name = 'check without a file'; Args = @('check') }
        @{ Name = 'profile without a file'; Args = @('profile') }
        @{ Name = 'debug without a file'; Args = @('debug') }
        @{ Name = 'new without anything'; Args = @('new') }
        @{ Name = 'web without a file'; Args = @('web') }
        @{ Name = 'serve without a file'; Args = @('serve') }
        @{ Name = 'browse without a file'; Args = @('browse') }
        @{ Name = 'web: missing file'; Args = @('web', 'nope.ot') }
        @{ Name = 'serve: folder without a manifest'; Args = @('serve', 'sub'); Files = @{ 'sub/' = '' } }
        @{ Name = 'desktop: missing file'; Args = @('desktop', 'nope.ot') }
        @{ Name = 'run: not a .ot file'; Args = @('run', 'notes.txt'); Files = @{ 'notes.txt' = 'hello' } }
        @{ Name = 'run: a missing name without .ot'; Args = @('run', 'nothing') }
        @{ Name = 'shortest form: not a .ot file'; Args = @('notes.txt'); Files = @{ 'notes.txt' = 'hello' } }
        @{ Name = 'profile: not a .ot file'; Args = @('profile', 'notes.txt'); Files = @{ 'notes.txt' = 'hello' } }
        @{ Name = 'run: missing file'; Args = @('run', 'nope.ot') }
        @{ Name = 'shortest form: missing file'; Args = @('nope.ot') }
        @{ Name = 'check: missing file'; Args = @('check', 'nope.ot') }
        @{ Name = 'run: a folder named like a program'; Args = @('run', 'folder.ot'); Files = @{ 'folder.ot/' = '' } }
    )
}
