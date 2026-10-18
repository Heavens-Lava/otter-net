# `use` problems otterPS reports (D60, D122, M1); before otter-net 1.0.1-port they fell back.
@{
    Cases = @(
        @{ Name = 'a missing file'; Args = @('main.ot'); Files = @{ 'main.ot' = @'
say "start"
use "nope.ot"
'@ } }
        @{ Name = 'a file with other capitals'; Args = @('main.ot'); Files = @{ 'main.ot' = 'use "Helpers.ot"'; 'helpers.ot' = 'say "helper"' } }
        @{ Name = 'a folder with other capitals'; Args = @('main.ot'); Files = @{ 'main.ot' = 'use "LIB/helpers.ot"'; 'lib/helpers.ot' = 'say "helper"' } }
        @{ Name = 'a circular import'; Args = @('a.ot'); Files = @{ 'a.ot' = 'use "b.ot"'; 'b.ot' = 'use "c.ot"'; 'c.ot' = 'use "a.ot"' } }
        @{ Name = 'a file importing itself'; Args = @('a.ot'); Files = @{ 'a.ot' = 'use "a.ot"' } }
        @{ Name = 'a folder'; Args = @('main.ot'); Files = @{ 'main.ot' = 'use "lib.ot"'; 'lib.ot/' = '' } }
        @{ Name = 'an absolute path'; Args = @('main.ot'); Files = @{ 'main.ot' = @'
use "{work}/lib/helpers.ot"
say "main"
'@; 'lib/helpers.ot' = 'say "helper"' } }
        @{ Name = 'an absolute path with other capitals'; Args = @('main.ot'); Files = @{ 'main.ot' = 'use "{work}/lib/Helpers.ot"'; 'lib/helpers.ot' = 'say "helper"' } }
        @{ Name = 'a missing file in an imported file'; Args = @('main.ot'); Files = @{ 'main.ot' = 'use "lib/a.ot"'; 'lib/a.ot' = @'
say "a"
use "gone.ot"
'@ } }
        @{ Name = 'parent folders'; Args = @('app/main.ot'); Files = @{ 'app/main.ot' = 'use "../lib/helpers.ot"'; 'lib/helpers.ot' = 'say "helper"' } }
        @{ Name = 'a file used twice runs once'; Args = @('main.ot'); Files = @{ 'main.ot' = @'
use "h.ot"
use "h.ot"
say "done"
'@; 'h.ot' = 'say "h"' } }
        @{ Name = 'a runtime error in an imported file'; Args = @('main.ot'); Files = @{ 'main.ot' = @'
use "lib.ot"
say "main"
boom
'@; 'lib.ot' = @'
to boom
    say missing
'@ } }
        @{ Name = 'syntax errors in an imported file'; Args = @('check', 'main.ot'); Files = @{ 'main.ot' = @'
say "main"
use "lib.ot"
'@; 'lib.ot' = @'
x is
y is
'@ } }
        @{ Name = 'check: a use problem'; Args = @('check', 'main.ot'); Files = @{ 'main.ot' = 'use "Missing.ot"' } }
    )
}
