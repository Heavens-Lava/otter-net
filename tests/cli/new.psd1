# otter new: the scaffold, byte for byte (New-OtterProject).
@{
    Cases = @(
        @{ Name = 'console'; Args = @('new', 'console', 'myapp'); KnownOnPwsh7 = 'Set-Content -Encoding UTF8 writes a byte order mark and \r\n on 5.1, neither on PowerShell 7 off Windows' }
        @{ Name = 'desktop'; Args = @('new', 'desktop', 'deskapp'); KnownOnPwsh7 = 'byte order mark and \r\n (5.1)' }
        @{ Name = 'web'; Args = @('new', 'web', 'site'); KnownOnPwsh7 = 'byte order mark and \r\n (5.1)' }
        @{ Name = 'automation'; Args = @('new', 'automation', 'task'); KnownOnPwsh7 = 'byte order mark and \r\n (5.1)' }
        @{ Name = 'game'; Args = @('new', 'game', 'play'); KnownOnPwsh7 = 'byte order mark and \r\n (5.1)' }
        @{ Name = 'archetype in capitals'; Args = @('new', 'CONSOLE', 'App2'); KnownOnPwsh7 = 'byte order mark and \r\n (5.1)' }
        @{ Name = 'an unknown archetype'; Args = @('new', 'phone', 'x') }
        @{ Name = 'no name'; Args = @('new', 'console') }
        @{ Name = 'a name with a slash'; Args = @('new', 'console', 'a/b') }
        @{ Name = 'a name with a colon'; Args = @('new', 'console', 'a:b') }
        @{ Name = 'a folder that is not empty'; Args = @('new', 'console', 'taken'); Files = @{ 'taken/keep.txt' = 'mine' } }
        @{ Name = 'an empty folder is used'; Args = @('new', 'console', 'empty'); Files = @{ 'empty/' = '' }; KnownOnPwsh7 = 'byte order mark and \r\n (5.1)' }
    )
}
