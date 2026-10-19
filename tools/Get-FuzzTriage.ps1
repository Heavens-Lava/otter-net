<#
.SYNOPSIS
Sorts the differences in tools/compare.ps1 reports of a fuzz run into known
reasons, and lists the rest.

.DESCRIPTION
A mutated program is named after its source (<folder>__<name>-m<seed>.ot,
-c<seed> for a case variant); the reasons below are the known places where
otterPS on PowerShell 7 differs from Windows PowerShell 5.1 and otter-net
(docs/baselines/), and programs whose output changes from run to run. A
difference whose only problem is the reference timing out is counted apart:
run those again with a longer -TimeoutSeconds. Exit code 1 when anything is
unexplained.

.EXAMPLE
pwsh -NoProfile -File tools/Get-FuzzTriage.ps1 -Report (Get-ChildItem fuzz-reports/*.md).FullName
#>
param([Parameter(Mandatory)][string[]]$Report)
$known = [ordered]@{
    '__(data_(db|query|syn_db|the|in_conditions)|database-)' = 'database: otterPS loads Windows winsqlite3.dll and cannot open a database on macOS or Linux (SPEC-QUESTIONS Q11)'
    '__(json|json_edge|bytes|p2_case_json|ok_json|json_random_diagnostics|file_json_command)-' = 'JSON layout: PowerShell 7 ConvertTo-Json (baseline)'
    '__environment-' = 'empty environment variable: PowerShell 7 (baseline)'
    '__(err_setenv_empty|err_setenv_equals|t3_ui_columns_text|console|data_query_internal)-' = '.NET error text: PowerShell 7 (baseline)'
    '__files_errors-' = 'a<b.txt allowed on macOS by PowerShell 7 (baseline)'
    '__folders-' = 'folder listing order: PowerShell 7 (baseline)'
    '__(numbers|ok_dates|dates)-' = 'negative zero: PowerShell 7 prints -0 (baseline)'
    '__timers-' = 'saying a timer: PowerShell 7 (baseline)'
    '__(random|dates_and_time)-' = 'output changes from run to run (unseeded random numbers, the current time)'
    '__zip-' = 'zip archive bytes: .NET Framework and .NET 10 compress differently (the source program deletes its archive; the mutation stopped first)'
}
$counts = [ordered]@{}
$unexplained = [System.Collections.Generic.List[string]]::new()
$total = 0; $identical = 0; $timeouts = 0; $bothTimeout = 0
foreach ($file in $Report) {
    foreach ($line in [System.IO.File]::ReadAllLines($file)) {
        if (-not $line.StartsWith('| ') -or $line.StartsWith('| Program') -or $line.StartsWith('|---')) { continue }
        $cells = $line.Split('|') | ForEach-Object { $_.Trim() }
        if ($cells.Count -lt 5) { continue }
        $name = $cells[1]; $outcome = $cells[2]; $detail = $cells[4]
        $total++
        if ($outcome -eq 'match') { $identical++; continue }
        if ($outcome -ne 'DIFFERENT') { continue }
        if ($detail -match '^exit code timeout vs') { $timeouts++; continue }
        $reason = $null
        foreach ($pattern in $known.Keys) { if ($name -match $pattern) { $reason = $known[$pattern]; break } }
        if ($reason) { if (-not $counts.Contains($reason)) { $counts[$reason] = 0 }; $counts[$reason]++ }
        else { $unexplained.Add("$name : $detail") }
    }
}
"$total programs: $identical identical, $timeouts reference timeouts (run again with a longer timeout)"
foreach ($reason in $counts.Keys) { '{0,6}  {1}' -f $counts[$reason], $reason }
"unexplained: $($unexplained.Count)"
foreach ($u in $unexplained) { "  $u" }
if ($unexplained.Count -gt 0) { exit 1 }
