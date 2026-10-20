# Measurement only: the deepest nesting of UI resources otterPS's web
# compiler renders on Windows PowerShell 5.1 before "call depth overflow",
# found by bisection, for a few shapes and commands.
param([string]$OtterPs)
$ErrorActionPreference = 'Stop'
$otter = (Resolve-Path (Join-Path $OtterPs 'otter.ps1')).Path
function New-Program([string]$Kind, [int]$Depth) {
    $lines = @('app is a page', '    title is "D"', '.')
    for ($i = 1; $i -le $Depth; $i++) { $lines += "c$i is a $Kind with align left" }
    for ($i = 1; $i -lt $Depth; $i++) { $lines += "put c$($i + 1) in c$i" }
    $lines += 'put c1 in app'; $lines += 'show app'
    return ($lines -join "`n") + "`n"
}
function Test-Depth([string]$Command, [string]$Kind, [int]$Depth) {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ("depth-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $dir | Out-Null
    [IO.File]::WriteAllText((Join-Path $dir 'd.ot'), (New-Program $Kind $Depth))
    Push-Location $dir
    try {
        if ($Command -eq 'build') {
            [IO.File]::WriteAllText((Join-Path $dir 'otter.json'), '{ "entryPoint": "d.ot", "target": "web" }')
            $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $otter build 2>&1 | Out-String
        } else {
            $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $otter web d.ot -NoOpen 2>&1 | Out-String
        }
    } finally { Pop-Location; Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue }
    return -not ($out -match 'call depth overflow')
}
foreach ($probe in @(@('web', 'column'), @('web', 'column'), @('web', 'card'), @('build', 'column'))) {
    $command, $kind = $probe
    $low = 1500; $high = 2000   # low renders, high overflows (measured)
    while ($high - $low -gt 1) {
        $mid = [int](($low + $high) / 2)
        if (Test-Depth $command $kind $mid) { $low = $mid } else { $high = $mid }
        Write-Host "  $command $kind ${mid}: $(if ($low -eq $mid) { 'renders' } else { 'overflows' })"
    }
    Write-Host "DEPTH $command $kind : renders $low, overflows $high"
}
