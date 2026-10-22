# win-arm64 compared with Windows

Reference: otterPS 1.0.1 on Windows PowerShell 5.1 (win-x64) where it ran; otherwise otter-net on win-x64. Normalised: the case folder (<work>), the otterPS checkout's folder (<otterps>) and the path separator after them; in PowerShell's own texts that name such a path, the line breaks of its 120-column console view. Differences not declared in `tests/cross-platform.psd1` fail.

- **cli** (against otterPS): 398 compared, 396 identical, 2 declared differences, **0 unexpected**
- **conformance** (against otterPS): 16 compared, 15 identical, 1 declared differences, **0 unexpected**
- **fuzz** (against otterPS): 444 compared, 441 identical, 3 declared differences, **0 unexpected**
- **network** (against otterPS): 22 compared, 22 identical, 0 declared differences, **0 unexpected**
- **programs** (against otterPS): 234 compared, 231 identical, 3 declared differences, **0 unexpected**
- **projects** (against otterPS): 84 compared, 84 identical, 0 declared differences, **0 unexpected**
- **site** (against otterPS): 102 compared, 102 identical, 0 declared differences, **0 unexpected**
- **web** (against otterPS): 363 compared, 363 identical, 0 declared differences, **0 unexpected**

## Declared differences

- cli modules___an_absolute_path_with_other_capitals: the program written by the case holds the case folder's absolute path, whose length differs from machine to machine
- cli modules___an_absolute_path: the program written by the case holds the case folder's absolute path, whose length differs from machine to machine
- conformance otterPS__conformance__system_integration__clipboard_env_notify.ot~07808837: prints how many environment variables the machine has and its temporary folder
- fuzz otter-net__compat-results__certification__fuzz-variants__clipboard_env_notify-c20261002__clipboard_env_notify-c20261002.ot: prints how many environment variables the machine has and its temporary folder
- fuzz otter-net__compat-results__certification__fuzz-variants__clipboard_env_notify-m20261002__clipboard_env_notify-m20261002.ot: prints how many environment variables the machine has and its temporary folder
- fuzz otter-net__compat-results__certification__fuzz-variants__dates-m20261002__dates-m20261002.ot: prints the current time of day, which moves between two machines' runs
- programs otter-net__tests__compat__t3_system_tasks_printers.ot: lists the names of the machine's scheduled tasks and printers, and win-arm64 is another machine than the reference's
- programs otterPS__conformance__system_integration__clipboard_env_notify.ot~07808837: prints how many environment variables the machine has and its temporary folder
- programs otterPS__examples__dates.ot~1f940c57: prints the current time of day, which moves between two machines' runs

PowerShell texts identical once their line breaks are taken out: 2.
