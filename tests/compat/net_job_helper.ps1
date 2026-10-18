# Used by net_jobs.ot: two lines of output, one of error output, exit code 3.
Write-Output 'first line'
Write-Output 'second line'
[Console]::Error.WriteLine('a warning')
exit 3
