# linux-x64 compared with Windows

Reference: otterPS 1.0.1 on Windows PowerShell 5.1 (win-x64) where it ran; otherwise otter-net on win-x64. Normalised: the case folder (<work>), the otterPS checkout's folder (<otterps>) and the path separator after them; in PowerShell's own texts that name such a path, the line breaks of its 120-column console view. Differences not declared in `tests/cross-platform.psd1` fail.

- **cli** (against otterPS): 398 compared, 372 identical, 26 declared differences, **0 unexpected**
- **conformance** (against otterPS): 16 compared, 14 identical, 2 declared differences, **0 unexpected**
- **fuzz** (against otterPS): 444 compared, 438 identical, 6 declared differences, **0 unexpected**
- **network** (against otterPS): 22 compared, 22 identical, 0 declared differences, **0 unexpected**
- **programs** (against otterPS): 234 compared, 218 identical, 16 declared differences, **0 unexpected**
- **projects** (against otterPS): 84 compared, 83 identical, 1 declared differences, **0 unexpected**
- **site** (against otterPS): 102 compared, 102 identical, 0 declared differences, **0 unexpected**
- **web** (against otterPS): 363 compared, 363 identical, 0 declared differences, **0 unexpected**

## Declared differences

- cli modules___an_absolute_path_with_other_capitals: the program written by the case holds the case folder's absolute path, whose length differs from machine to machine
- cli modules___an_absolute_path: the program written by the case holds the case folder's absolute path, whose length differs from machine to machine
- cli new___a_name_with_a_line_break: a folder name with a line break or tab: Windows (.NET Framework's Path.Combine) refuses it with "Illegal characters in path."; macOS and Linux allow it, and the project is made
- cli new___a_name_with_a_tab: a folder name with a line break or tab: Windows (.NET Framework's Path.Combine) refuses it with "Illegal characters in path."; macOS and Linux allow it, and the project is made
- cli ps-texts___desktop___a_missing_import: `otter desktop` exists only on Windows; elsewhere otterPS's "desktop windows are only available on Windows"
- cli ps-texts___desktop___a_project_with_a_missing_import: `otter desktop` exists only on Windows; elsewhere otterPS's "desktop windows are only available on Windows"
- cli ps-texts___desktop___a_syntax_error_in_an_import: `otter desktop` exists only on Windows; elsewhere otterPS's "desktop windows are only available on Windows"
- cli ps-texts___desktop___an_import_cycle: `otter desktop` exists only on Windows; elsewhere otterPS's "desktop windows are only available on Windows"
- cli ps-texts___desktop___an_import_in_other_capitals: `otter desktop` exists only on Windows; elsewhere otterPS's "desktop windows are only available on Windows"
- cli ps-texts___desktop___an_import_that_is_a_folder: `otter desktop` exists only on Windows; elsewhere otterPS's "desktop windows are only available on Windows"
- cli ps-texts___desktop___the_page_cannot_be_written_a_long_name: `otter desktop` exists only on Windows; elsewhere otterPS's "desktop windows are only available on Windows"
- cli ps-texts___desktop___the_page_cannot_be_written: `otter desktop` exists only on Windows; elsewhere otterPS's "desktop windows are only available on Windows"
- cli ps-texts___desktop___the_session_page_cannot_be_written: `otter desktop` exists only on Windows; elsewhere otterPS's "desktop windows are only available on Windows"
- cli ps-texts___IA___desktop_of_a_missing_import_Stop~7cab8246: `otter desktop` exists only on Windows; elsewhere otterPS's "desktop windows are only available on Windows"
- cli ps-texts___serve___a_declared_port_in_use: HttpListener's own message for a listener that cannot start: Windows' http.sys words it ("The parameter is incorrect", "Failed to listen on prefix"), .NET's managed listener elsewhere ("Invalid port in prefix.", "Address already in use"); otterPS shows the same message of the same .NET class
- cli ps-texts___serve___a_host_with_a_colon: HttpListener's own message for a listener that cannot start: Windows' http.sys words it ("The parameter is incorrect", "Failed to listen on prefix"), .NET's managed listener elsewhere ("Invalid port in prefix.", "Address already in use"); otterPS shows the same message of the same .NET class
- cli ps-texts___serve___a_host_with_a_space: HttpListener's own message for a listener that cannot start: Windows' http.sys words it ("The parameter is incorrect", "Failed to listen on prefix"), .NET's managed listener elsewhere ("Invalid port in prefix.", "Address already in use"); otterPS shows the same message of the same .NET class
- cli ps-texts___serve___a_port_in_use: HttpListener's own message for a listener that cannot start: Windows' http.sys words it ("The parameter is incorrect", "Failed to listen on prefix"), .NET's managed listener elsewhere ("Invalid port in prefix.", "Address already in use"); otterPS shows the same message of the same .NET class
- cli ps-texts___serve___port_0: HttpListener's own message for a listener that cannot start: Windows' http.sys words it ("The parameter is incorrect", "Failed to listen on prefix"), .NET's managed listener elsewhere ("Invalid port in prefix.", "Address already in use"); otterPS shows the same message of the same .NET class
- cli ps-texts___serve___port_70000: HttpListener's own message for a listener that cannot start: Windows' http.sys words it ("The parameter is incorrect", "Failed to listen on prefix"), .NET's managed listener elsewhere ("Invalid port in prefix.", "Address already in use"); otterPS shows the same message of the same .NET class
- cli ps-texts___V___desktop_of_a_missing_import~b5dc6db1: `otter desktop` exists only on Windows; elsewhere otterPS's "desktop windows are only available on Windows"
- cli ps-texts___V___desktop_of_a_syntax_error~8d410ae7: `otter desktop` exists only on Windows; elsewhere otterPS's "desktop windows are only available on Windows"
- cli ps-texts___V___desktop_the_page_cannot_be_written~ebdb6dac: `otter desktop` exists only on Windows; elsewhere otterPS's "desktop windows are only available on Windows"
- cli ps-texts___V___run_of_a_program_the_compiled_engine_refuses~a23fc2e0: creates a window: WPF exists only on Windows; elsewhere otterPS's "could not start its UI provider"
- cli ps-texts___V___serve_on_a_port_in_use~99a084db: HttpListener's own message for a listener that cannot start: Windows' http.sys words it ("The parameter is incorrect", "Failed to listen on prefix"), .NET's managed listener elsewhere ("Invalid port in prefix.", "Address already in use"); otterPS shows the same message of the same .NET class
- cli ui___run___create_without_WPF~95289a56: creates a window: WPF exists only on Windows; elsewhere otterPS's "could not start its UI provider"
- conformance otterPS__conformance__misc__file_exists_run_command_json_roundtrip.ot~c62ceac5: runs "cmd /c echo": cmd.exe exists only on Windows
- conformance otterPS__conformance__system_integration__clipboard_env_notify.ot~07808837: prints how many environment variables the machine has and its temporary folder
- fuzz otter-net__compat-results__certification__fuzz-variants__clipboard_env_notify-c20261002__clipboard_env_notify-c20261002.ot: prints how many environment variables the machine has and its temporary folder
- fuzz otter-net__compat-results__certification__fuzz-variants__clipboard_env_notify-m20261002__clipboard_env_notify-m20261002.ot: prints how many environment variables the machine has and its temporary folder
- fuzz otter-net__compat-results__certification__fuzz-variants__dates-m20261002__dates-m20261002.ot: prints the current time of day, which moves between two machines' runs
- fuzz otter-net__compat-results__certification__fuzz-variants__file_exists_run_command_json_roundtrip-c20261002__file_exists_run_command_json_roundtrip-c20261002.ot: runs "cmd /c echo": cmd.exe exists only on Windows
- fuzz otter-net__compat-results__certification__fuzz-variants__file_exists_run_command_json_roundtrip-c20261003__file_exists_run_command_json_roundtrip-c20261003.ot: runs "cmd /c echo": cmd.exe exists only on Windows
- fuzz otter-net__compat-results__certification__fuzz-variants__file_exists_run_command_json_roundtrip-m20261002__file_exists_run_command_json_roundtrip-m20261002.ot: runs "cmd /c echo": cmd.exe exists only on Windows
- programs otter-net__tests__compat__c4_core.ot: the Debian 12 container has no ~/.config, and .NET (otterPS too) gives no "appdata" folder that does not exist
- programs otter-net__tests__compat__commands.ot: runs cmd.exe and .cmd files: a Windows-only test
- programs otter-net__tests__compat__p2_remote_command.ot: remote commands (WinRM) exist only on Windows; elsewhere otterPS's internal error (SPEC-QUESTIONS P2-1)
- programs otter-net__tests__compat__sys_desktop.ot: Windows-only features (registry, credentials, notifications, printing, event log, tasks) give otterPS's "only available on Windows" message
- programs otter-net__tests__compat__sys_print_notify.ot: Windows-only features (registry, credentials, notifications, printing, event log, tasks) give otterPS's "only available on Windows" message
- programs otter-net__tests__compat__sys_processes.ot: the operating system's own words for a missing program; otterPS looks for ssh.exe (SPEC-QUESTIONS Q8)
- programs otter-net__tests__compat__sys_ssh_arguments.ot: the operating system's own words for a missing program; otterPS looks for ssh.exe (SPEC-QUESTIONS Q8)
- programs otter-net__tests__compat__sys_system_info.ot: Windows-only features (registry, credentials, notifications, printing, event log, tasks) give otterPS's "only available on Windows" message
- programs otter-net__tests__compat__sys_windows.ot: Windows-only features (registry, credentials, notifications, printing, event log, tasks) give otterPS's "only available on Windows" message
- programs otter-net__tests__compat__t3_system_tasks_printers.ot: Windows-only features (registry, credentials, notifications, printing, event log, tasks) give otterPS's "only available on Windows" message
- programs otter-net__tests__compat__ui_run_create_try.ot: desktop windows (WPF) exist only on Windows; elsewhere otterPS's "could not start its UI provider"
- programs otter-net__tests__compat__ui_run_create_unknown.ot: desktop windows (WPF) exist only on Windows; elsewhere otterPS's "could not start its UI provider"
- programs otter-net__tests__compat__ui_run_create.ot: desktop windows (WPF) exist only on Windows; elsewhere otterPS's "could not start its UI provider"
- programs otterPS__conformance__misc__file_exists_run_command_json_roundtrip.ot~c62ceac5: runs "cmd /c echo": cmd.exe exists only on Windows
- programs otterPS__conformance__system_integration__clipboard_env_notify.ot~07808837: prints how many environment variables the machine has and its temporary folder
- programs otterPS__examples__dates.ot~1f940c57: prints the current time of day, which moves between two machines' runs
- projects projects___project_OtterWorkspace___run~926288cc: creates a window: WPF exists only on Windows; elsewhere otterPS's "could not start its UI provider"

PowerShell texts identical once their line breaks are taken out: 3.
