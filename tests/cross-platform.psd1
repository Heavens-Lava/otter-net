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
        # Found by the certification (tools/Invoke-OtterNetCertification.ps1).
        @{ Platforms = '*'; Cases = '*clipboard_env_notify*'; Reason = 'prints how many environment variables the machine has and its temporary folder' }
        @{ Platforms = '*'; Cases = '*fuzz-variants__dates-*', 'otterPS__examples__dates.ot*'; Reason = 'prints the current time of day, which moves between two machines'' runs' }
        @{ Platforms = '*'; Gates = 'cli'; Cases = 'modules___an_absolute_path*'; Parts = 'files'; Reason = 'the program written by the case holds the case folder''s absolute path, whose length differs from machine to machine' }
        @{ Platforms = 'osx-*', 'linux-*'; Cases = '*file_exists_run_command_json_roundtrip*'; Reason = 'runs "cmd /c echo": cmd.exe exists only on Windows' }
        @{ Platforms = 'osx-*', 'linux-*'; Gates = 'cli'; Cases = 'ps-texts___desktop___*', 'ps-texts___V___desktop_*', 'ps-texts___IA___desktop_*'; Reason = '`otter desktop` exists only on Windows; elsewhere otterPS''s "desktop windows are only available on Windows"' }
        @{ Platforms = 'osx-*', 'linux-*'; Gates = 'cli', 'projects'; Cases = 'ui___run___create_without_WPF*', 'ps-texts___V___run_of_a_program_the_compiled_engine_refuses*', 'projects___project_OtterWorkspace___run*'; Reason = 'creates a window: WPF exists only on Windows; elsewhere otterPS''s "could not start its UI provider"' }
        @{ Platforms = 'osx-*', 'linux-*'; Gates = 'cli'; Cases = 'ps-texts___serve___a_declared_port_in_use*', 'ps-texts___serve___a_port_in_use*', 'ps-texts___serve___a_host_with_a_colon*', 'ps-texts___serve___a_host_with_a_space*', 'ps-texts___serve___port_0*', 'ps-texts___serve___port_70000*', 'ps-texts___V___serve_on_a_port_in_use*'; Parts = 'stderr'; Reason = 'HttpListener''s own message for a listener that cannot start: Windows'' http.sys words it ("The parameter is incorrect", "Failed to listen on prefix"), .NET''s managed listener elsewhere ("Invalid port in prefix.", "Address already in use"); otterPS shows the same message of the same .NET class' }
        @{ Platforms = 'osx-*', 'linux-*'; Gates = 'cli'; Cases = 'new___a_name_with_a_line_break*', 'new___a_name_with_a_tab*'; Reason = 'a folder name with a line break or tab: Windows (.NET Framework''s Path.Combine) refuses it with "Illegal characters in path."; macOS and Linux allow it, and the project is made' }
    )
}
