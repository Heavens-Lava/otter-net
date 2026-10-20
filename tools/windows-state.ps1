<#
.SYNOPSIS
Gives a Windows CI runner the machine state that tests/windows-state
compares: printers in several states, scheduled tasks in several folders and
states, an event log with and without descriptions, services and installed
software entries. Then prints what Windows itself reports about them.

.DESCRIPTION
For CI runners only: it adds printers, tasks, services, an event log and
registry entries, all named "Otter W5"/"OtterW5", and changes the default
printer. Never run it on a machine you care about. Windows PowerShell 5.1,
as administrator. Every step is tried on its own; a step that cannot be
done here is reported and the rest go on.

-Dump only prints the report (WMI's and the spooler's view of the printers,
the Task Scheduler's view of the tasks, ...), for comparing with what both
Otters print.
#>
[CmdletBinding()]
param([switch]$Dump)

$ErrorActionPreference = 'Continue'

function Step {
    param([string]$StepName, [scriptblock]$StepBody)
    try { & $StepBody; Write-Host "state: $StepName - done" }
    catch { Write-Host "state: $StepName - FAILED: $($_.Exception.Message)" -ForegroundColor Yellow }
}

function Show {
    param([string]$ShowTitle, [scriptblock]$ShowBody)
    Write-Host ''
    Write-Host "==== $ShowTitle"
    try { & $ShowBody | Format-List | Out-String -Width 400 | Write-Host }
    catch { Write-Host "  (failed: $($_.Exception.Message))" }
}

if (-not $Dump) {
    # ---- printers (D83, SPEC-QUESTIONS T3-1) ----
    $driver = $null
    Step 'printer driver' {
        foreach ($name in 'Generic / Text Only', 'Microsoft Print To PDF', 'Microsoft XPS Document Writer v4', 'Microsoft enhanced Point and Print compatibility driver') {
            try { Add-PrinterDriver -Name $name -ErrorAction Stop } catch { }
            if (Get-PrinterDriver -Name $name -ErrorAction SilentlyContinue) { $script:driver = $name; break }
        }
        if (-not $script:driver) { $script:driver = (Get-PrinterDriver | Select-Object -First 1).Name }
        Write-Host "  driver: $script:driver"
    }
    $driver = $script:driver
    $folder = 'C:\otter-w5'
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    Step 'ports' {
        foreach ($port in "$folder\idle.prn", "$folder\paused.prn", "$folder\offline.prn", 'C:\otter-w5-missing-folder\error.prn') {
            if (-not (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) { Add-PrinterPort -Name $port -ErrorAction Stop }
        }
        if (-not (Get-PrinterPort -Name 'OtterW5_unreachable' -ErrorAction SilentlyContinue)) {
            # TEST-NET-1: nothing answers there.
            Add-PrinterPort -Name 'OtterW5_unreachable' -PrinterHostAddress '192.0.2.1' -ErrorAction Stop
        }
    }
    $printers = [ordered]@{
        'Otter W5 Idle' = "$folder\idle.prn"
        'Otter W5 Paused' = "$folder\paused.prn"
        'Otter W5 Paused With Job' = "$folder\paused.prn"
        'Otter W5 Work Offline' = "$folder\offline.prn"
        'Otter W5 Error' = 'C:\otter-w5-missing-folder\error.prn'
        'Otter W5 Unreachable' = 'OtterW5_unreachable'
        'otter w5 lower case' = "$folder\idle.prn"
    }
    foreach ($name in $printers.Keys) {
        Step "printer $name" { if (-not (Get-Printer -Name $name -ErrorAction SilentlyContinue)) { Add-Printer -Name $name -DriverName $driver -PortName $printers[$name] -ErrorAction Stop } }
    }
    Step 'pause' {
        foreach ($name in 'Otter W5 Paused', 'Otter W5 Paused With Job') {
            Get-CimInstance Win32_Printer -Filter "Name='$name'" | Invoke-CimMethod -MethodName Pause | Out-Null
        }
    }
    Step 'a job on a paused printer' { 'a page' | Out-Printer -Name 'Otter W5 Paused With Job' }
    Step 'work offline' { Get-CimInstance Win32_Printer -Filter "Name='Otter W5 Work Offline'" | Set-CimInstance -Property @{ WorkOffline = $true } }
    Step 'a job that fails' { 'a page' | Out-Printer -Name 'Otter W5 Error' }
    Step 'a job to nowhere' { 'a page' | Out-Printer -Name 'Otter W5 Unreachable' }
    Step 'default printer' { Get-CimInstance Win32_Printer -Filter "Name='Otter W5 Paused'" | Invoke-CimMethod -MethodName SetDefaultPrinter | Out-Null }

    # ---- scheduled tasks (D80, SPEC-QUESTIONS T3-2) ----
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount
    $quick = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c exit 0'
    $slow = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c ping -n 3600 127.0.0.1 >nul'
    $missing = New-ScheduledTaskAction -Execute 'C:\otter-w5-missing-folder\nothing.exe'
    $future = New-ScheduledTaskTrigger -Once -At ([datetime]'2030-01-01T10:00:00')
    $past = New-ScheduledTaskTrigger -Once -At ([datetime]'2020-01-01T10:00:00')
    $tasks = @(
        # Registered out of alphabetical order, in folders whose names sort
        # differently with and without case.
        @{ Path = '\OtterW5\'; Name = 'Otter z registered first'; Action = $quick; Trigger = $future }
        @{ Path = '\OtterW5\'; Name = 'Otter a registered second'; Action = $quick; Trigger = $future }
        @{ Path = '\OtterW5\'; Name = 'Otter never run, no trigger'; Action = $quick }
        @{ Path = '\OtterW5\'; Name = 'Otter disabled'; Action = $quick; Trigger = $future; Disable = $true }
        @{ Path = '\OtterW5\'; Name = 'Otter past trigger only'; Action = $quick; Trigger = $past }
        @{ Path = '\OtterW5\'; Name = 'Otter ran'; Action = $quick; Run = $true }
        @{ Path = '\OtterW5\'; Name = 'Otter ran and failed'; Action = $missing; Run = $true }
        @{ Path = '\OtterW5\'; Name = 'Otter running'; Action = $slow; Run = $true }
        @{ Path = '\OtterW5\Deeper\'; Name = 'Otter hidden'; Action = $quick; Trigger = $future; Hidden = $true }
        @{ Path = '\OtterW5\Deeper\'; Name = 'Otter same name'; Action = $quick }
        @{ Path = '\otterw5 lower\'; Name = 'Otter same name'; Action = $quick }
        @{ Path = '\OtterW5 Another\'; Name = 'Otter in another folder'; Action = $quick; Trigger = $future }
        @{ Path = '\'; Name = 'Otter W5 at the root'; Action = $quick; Trigger = $future }
        @{ Path = '\'; Name = 'otter w5 at the root, lower case'; Action = $quick }
    )
    foreach ($task in $tasks) {
        Step "task $($task.Path)$($task.Name)" {
            $settings = New-ScheduledTaskSettingsSet -Hidden:([bool]$task.Hidden)
            $arguments = @{ TaskPath = $task.Path; TaskName = $task.Name; Action = $task.Action; Principal = $principal; Settings = $settings; Force = $true }
            if ($task.Trigger) { $arguments.Trigger = $task.Trigger }
            Register-ScheduledTask @arguments -ErrorAction Stop | Out-Null
            if ($task.Disable) { Disable-ScheduledTask -TaskPath $task.Path -TaskName $task.Name -ErrorAction Stop | Out-Null }
            if ($task.Run) { Start-ScheduledTask -TaskPath $task.Path -TaskName $task.Name -ErrorAction Stop }
        }
    }
    Start-Sleep -Seconds 5

    # ---- the event log (D79, SPEC-QUESTIONS P2-2, P2-3) ----
    Step 'event log' {
        if (-not [System.Diagnostics.EventLog]::Exists('OtterW5')) { New-EventLog -LogName 'OtterW5' -Source 'OtterW5 Source', 'OtterW5 No Text', 'OtterW5 Gone' }
        Write-EventLog -LogName 'OtterW5' -Source 'OtterW5 Source' -EventId 1 -EntryType Information -Message 'Information, one line.'
        Write-EventLog -LogName 'OtterW5' -Source 'OtterW5 Source' -EventId 2 -EntryType Warning -Message "A warning`r`nover two lines."
        Write-EventLog -LogName 'OtterW5' -Source 'OtterW5 Source' -EventId 3 -EntryType Error -Message 'An error with "quotes" and %1 and 100%.'
        Write-EventLog -LogName 'OtterW5' -Source 'OtterW5 Source' -EventId 4 -EntryType SuccessAudit -Message 'A success audit.'
        Write-EventLog -LogName 'OtterW5' -Source 'OtterW5 Source' -EventId 5 -EntryType FailureAudit -Message 'A failure audit.'
        Write-EventLog -LogName 'OtterW5' -Source 'OtterW5 Source' -EventId 6 -EntryType Information -Message ''
        Write-EventLog -LogName 'OtterW5' -Source 'OtterW5 Source' -EventId 70000 -EntryType Information -Message 'An event id above 65535.'
        # A source whose message file does not exist: the event has no description.
        $key = 'HKLM:\SYSTEM\CurrentControlSet\Services\EventLog\OtterW5\OtterW5 No Text'
        Set-ItemProperty -Path $key -Name EventMessageFile -Value 'C:\otter-w5-missing-folder\messages.dll'
        Write-EventLog -LogName 'OtterW5' -Source 'OtterW5 No Text' -EventId 7 -EntryType Warning -Message 'Never shown: no message file.'
        # A source that is removed after writing: its provider is unknown.
        Write-EventLog -LogName 'OtterW5' -Source 'OtterW5 Gone' -EventId 8 -EntryType Error -Message 'The source is removed afterwards.'
        Remove-EventLog -Source 'OtterW5 Gone'
    }
    Step 'empty event log' { if (-not [System.Diagnostics.EventLog]::Exists('OtterW5 Empty')) { New-EventLog -LogName 'OtterW5 Empty' -Source 'OtterW5 Empty Source' } }

    # ---- services (D86) ----
    $binary = '"C:\Windows\System32\svchost.exe" -k OtterW5NoSuchGroup'
    Step 'services' {
        foreach ($service in @(
            @{ Name = 'OtterW5Manual'; Display = 'Otter W5 manual'; Start = 'Manual' }
            @{ Name = 'OtterW5Disabled'; Display = 'Otter W5 disabled'; Start = 'Disabled' }
            @{ Name = 'OtterW5Automatic'; Display = 'Otter W5 automatic'; Start = 'Automatic' }
            @{ Name = 'OtterW5Delayed'; Display = "Otter W5 d$([char]0xE9)lai $([char]0xFC)n$([char]0xEF)code"; Start = 'Automatic' }
            @{ Name = 'otterw5lower'; Display = 'otter w5 lower case'; Start = 'Manual' }
        )) {
            if (-not (Get-Service -Name $service.Name -ErrorAction SilentlyContinue)) {
                New-Service -Name $service.Name -DisplayName $service.Display -BinaryPathName $binary -StartupType $service.Start -ErrorAction Stop | Out-Null
            }
        }
        & sc.exe config OtterW5Delayed start= delayed-auto | Out-Null
    }

    # ---- installed software (D77) ----
    Step 'software' {
        $root = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
        New-Item -Force -Path $root | Out-Null
        $entries = @(
            @{ Key = 'OtterW5 A'; Values = @{ DisplayName = 'Otter W5 app'; DisplayVersion = '1.2.3'; Publisher = 'Otter W5 Ltd' } }
            @{ Key = 'OtterW5 B blank'; Values = @{ DisplayName = '   '; DisplayVersion = '9' } }
            @{ Key = 'OtterW5 C'; Values = @{ DisplayName = 'Otter W5 empty publisher'; Publisher = '' } }
            @{ Key = 'OtterW5 D'; Values = @{ DisplayName = 'Otter W5 no version' } }
            @{ Key = 'OtterW5 E[1]'; Values = @{ DisplayName = 'Otter W5 brackets in the key'; DisplayVersion = '2.0' } }
            @{ Key = 'otterw5 f lower'; Values = @{ DisplayName = 'otter w5 lower case key'; Publisher = 'p' } }
            @{ Key = 'OtterW5 G no name'; Values = @{ DisplayVersion = '3.0'; Publisher = 'nobody' } }
        )
        foreach ($entry in $entries) {
            New-Item -Force -Path ($root + '\' + $entry.Key) | Out-Null
            foreach ($name in $entry.Values.Keys) { New-ItemProperty -LiteralPath ($root + '\' + $entry.Key) -Name $name -Value $entry.Values[$name] -PropertyType String -Force | Out-Null }
        }
        # Values that are not text.
        $k = $root + '\OtterW5 H numbers'
        New-Item -Force -Path $k | Out-Null
        New-ItemProperty -LiteralPath $k -Name DisplayName -Value 'Otter W5 numeric version' -PropertyType String -Force | Out-Null
        New-ItemProperty -LiteralPath $k -Name DisplayVersion -Value 0 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -LiteralPath $k -Name Publisher -Value 7 -PropertyType DWord -Force | Out-Null
        $k = $root + '\OtterW5 I expand'
        New-Item -Force -Path $k | Out-Null
        New-ItemProperty -LiteralPath $k -Name DisplayName -Value 'Otter W5 %OS%' -PropertyType ExpandString -Force | Out-Null
        New-ItemProperty -LiteralPath $k -Name DisplayVersion -Value @('1', '2') -PropertyType MultiString -Force | Out-Null
    }
}

# ---- what Windows reports ----
Show 'Win32_Printer' { Get-CimInstance Win32_Printer | Select-Object Name, PrinterStatus, ExtendedPrinterStatus, PrinterState, DetectedErrorState, ExtendedDetectedErrorState, WorkOffline, Default, Attributes, Status, StatusInfo, PortName }
Show 'Get-Printer (the spooler status flags)' { Get-Printer | Select-Object Name, PrinterStatus, JobCount, PortName, DriverName }
Show 'Print jobs' { Get-Printer | ForEach-Object { Get-PrintJob -PrinterName $_.Name -ErrorAction SilentlyContinue } | Select-Object PrinterName, Id, JobStatus }
Show 'Scheduled tasks, Get-ScheduledTask order' { $i = 0; Get-ScheduledTask | ForEach-Object { $i++; [pscustomobject]@{ N = $i; Path = $_.TaskPath; Name = $_.TaskName; State = "$($_.State)" } } | Format-Table -AutoSize | Out-String -Width 400 }
Show 'Our tasks, Get-ScheduledTaskInfo' { Get-ScheduledTask | Where-Object TaskName -like 'Otter*' | ForEach-Object { $info = $_ | Get-ScheduledTaskInfo; [pscustomobject]@{ Path = $_.TaskPath; Name = $_.TaskName; State = "$($_.State)"; Last = $info.LastRunTime; LastIsNull = ($null -eq $info.LastRunTime); Next = $info.NextRunTime; Result = $info.LastTaskResult } } }
Show 'Our tasks, Task Scheduler COM' {
    $service = New-Object -ComObject Schedule.Service; $service.Connect()
    function Walk($folder) {
        foreach ($t in $folder.GetTasks(1)) { if ($t.Name -like 'Otter*') { [pscustomobject]@{ Path = $t.Path; State = $t.State; Last = $t.LastRunTime; Next = $t.NextRunTime } } }
        foreach ($f in $folder.GetFolders(0)) { Walk $f }
    }
    Walk ($service.GetFolder('\'))
}
Show 'OtterW5 event log' { Get-WinEvent -LogName 'OtterW5' -ErrorAction SilentlyContinue | Select-Object ProviderName, Id, Level, LevelDisplayName, TimeCreated, Message }
Show 'Our services' { Get-Service -Name 'OtterW5*', 'otterw5*' | Select-Object Name, DisplayName, Status, StartType }
Show 'Network interfaces' { [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() | Select-Object Name, NetworkInterfaceType, OperationalStatus }
Show 'Culture' { [pscustomobject]@{ Culture = (Get-Culture).Name; UICulture = (Get-UICulture).Name; Thread = [System.Threading.Thread]::CurrentThread.CurrentCulture.Name; ThreadUI = [System.Threading.Thread]::CurrentThread.CurrentUICulture.Name; UILanguages = ((Get-WinUserLanguageList).LanguageTag -join ', ') } }
