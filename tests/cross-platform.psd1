# Differences between otter-net on another platform and the Windows
# reference that are expected, with the reason (tools/compare-captures.ps1,
# the cross-platform half of tools/Invoke-OtterNetCertification.ps1). Any
# other difference fails the certification.
#
#   Platforms  -like patterns of runtime identifiers (osx-arm64, linux-x64, ...)
#   Gates      -like patterns of gate names (optional: every gate)
#   Cases      -like patterns of case ids (the capture file names)
#   Parts      exit, stdout, stderr, files, missing (optional: any part)
#   Reason     why the platform differs
#
# The first ten rules are ci.yml's cross-platform declarations.
@{
    Rules = @(
        @{ Platforms = 'osx-*', 'linux-*'; Cases = '*__commands.ot'; Reason = 'runs cmd.exe and .cmd files: a Windows-only test' }
        @{ Platforms = 'osx-*', 'linux-*'; Cases = '*__files_errors.ot', '*__zip.ot', '*__net_http_errors.ot'; Reason = 'paths in messages use the platform separator, / (docs/CROSS-PLATFORM-CONSISTENCY.md, 7)' }
        @{ Platforms = 'osx-*', 'linux-*'; Cases = '*__sys_windows.ot', '*__sys_print_notify.ot', '*__sys_desktop.ot', '*__sys_system_info.ot', '*__t3_system_tasks_printers.ot'; Reason = 'Windows-only features (registry, credentials, notifications, printing, event log, tasks) give otterPS''s "only available on Windows" message' }
        @{ Platforms = 'osx-*', 'linux-*'; Cases = '*__sys_processes.ot', '*__sys_ssh_arguments.ot'; Reason = 'the operating system''s own words for a missing program; otterPS looks for ssh.exe (SPEC-QUESTIONS Q8)' }
        @{ Platforms = 'osx-*', 'linux-*'; Cases = '*__ui_run_create.ot', '*__ui_run_create_try.ot', '*__ui_run_create_unknown.ot'; Reason = 'desktop windows (WPF) exist only on Windows; elsewhere otterPS''s "could not start its UI provider"' }
        @{ Platforms = 'win-arm64'; Cases = '*__t3_system_tasks_printers.ot'; Reason = 'lists the names of the machine''s scheduled tasks and printers, and win-arm64 is another machine than the reference''s' }
        @{ Platforms = 'linux-*'; Cases = '*__c4_core.ot'; Reason = 'the Debian 12 container has no ~/.config, and .NET (otterPS too) gives no "appdata" folder that does not exist' }
        @{ Platforms = 'osx-*', 'linux-*'; Cases = '*__p2_remote_command.ot'; Reason = 'remote commands (WinRM) exist only on Windows; elsewhere otterPS''s internal error (SPEC-QUESTIONS P2-1)' }
    )
}
