<#
.SYNOPSIS
Runs every program in a corpus with one Otter implementation and saves what
it printed, so runs on different machines can be compared afterwards.

.DESCRIPTION
For the cross-platform check in CI: otterPS (the reference) needs Windows
PowerShell, so it runs only on Windows, while otter-net runs on Windows, Linux
and macOS. Each program runs in a fresh copy of its folder with its .input
file as standard input, like tools\compare.ps1. Saved for each program, as
<name>.out under -OutDir: the exit code and standard output, with line
endings made "\n" and the temporary folder's path replaced by <work>, since
both legitimately differ from machine to machine.

Works in Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
pwsh tools/capture.ps1 -Otter artifacts/linux-x64/otter -Corpus tests/compat -OutDir results/linux
#>
[CmdletBinding()]
param(
    # An otter executable, otter.dll, or otterPS's otter.ps1.
    [Parameter(Mandatory)][string]$Otter,
    [Parameter(Mandatory)][string]$Corpus,
    [Parameter(Mandatory)][string]$OutDir,
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$Otter = (Resolve-Path $Otter).Path
$Corpus = (Resolve-Path $Corpus).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

$onWindows = [System.IO.Path]::DirectorySeparatorChar -eq '\'
if ($Otter.EndsWith('.ps1') -and $onWindows) { $file = 'powershell.exe'; $prefix = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $Otter) }
elseif ($Otter.EndsWith('.ps1')) { $file = 'pwsh'; $prefix = @('-NoProfile', '-File', $Otter) }
elseif ($Otter.EndsWith('.dll')) { $file = 'dotnet'; $prefix = @($Otter) }
else { $file = $Otter; $prefix = @() }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-capture-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$count = 0
try {
    foreach ($program in @(Get-ChildItem -LiteralPath $Corpus -Recurse -Filter '*.ot' | Sort-Object FullName)) {
        $count++
        $folder = Join-Path $work "$count"
        New-Item -ItemType Directory -Path $folder | Out-Null
        Get-ChildItem -LiteralPath $program.DirectoryName -File | Copy-Item -Destination $folder
        $inputPath = $program.FullName + '.input'
        $inputText = if (Test-Path -LiteralPath $inputPath) { [System.IO.File]::ReadAllText($inputPath) } else { '' }

        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = $file
        $psi.Arguments = (@($prefix) + @('run', $program.Name) | ForEach-Object { '"' + $_ + '"' }) -join ' '
        $psi.WorkingDirectory = $folder
        $psi.UseShellExecute = $false
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        # otter-net must not hand anything to the PowerShell Otter here.
        if (-not $Otter.EndsWith('.ps1')) { $psi.EnvironmentVariables['OTTER_ENGINE'] = 'native' }
        $process = [System.Diagnostics.Process]::Start($psi)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        try { $process.StandardInput.Write($inputText); $process.StandardInput.Close() } catch { }
        $exit = if ($process.WaitForExit($TimeoutSeconds * 1000)) { $process.ExitCode } else { try { $process.Kill() } catch { }; 'timeout' }
        [void]$stdout.Wait(5000)

        $text = $stdout.Result -replace "`r`n", "`n"
        $text = $text.Replace($folder, '<work>')
        $relative = $program.FullName.Substring($Corpus.Length).TrimStart('\', '/') -replace '[\\/]', '__'
        $saved = "exit: $exit`n$text"
        [System.IO.File]::WriteAllText((Join-Path $OutDir "$relative.out"), $saved, [System.Text.UTF8Encoding]::new($false))
    }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "$count programs captured into $OutDir"
