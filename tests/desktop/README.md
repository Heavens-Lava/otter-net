# Desktop window tests

Each `<name>.ot` here opens desktop windows (WPF) in `otter run`. The
harness (`Harness/`, `desktop-harness`) runs it with otterPS 1.0.1 and with
otter-net, drives its windows with `<name>.steps`, and compares everything a
user sees: the window's UI Automation tree after every step, the pixels of
every `snap`, standard output byte for byte, standard error and the exit
code. `.github/workflows/desktop.yml` runs it on a Windows desktop.

    desktop-harness --otterps otterps/otter.ps1 --otter artifacts/win-x64/otter.exe \
        --tests tests/desktop --out results/desktop [--only name]

Beside a program:

- `<name>.steps`: one step per line (`#` starts a comment); without it:
  `window *`, `snap`, `close`, `exit`.
- `<name>.ot.input`: standard input.
- `<name>.args`: the command line instead of `run <name>.ot` (empty: the
  REPL).

Steps (a selector is `button "Name"`, `button#2`, `edit#1`, `text "Hi"`,
`pane#1`, counted in tree order inside the current window):

| Step | Does | Observes |
|---|---|---|
| `window "Title"` / `window *` [ms] | waits for a window of the program's process tree | its title, or none |
| `snap` | moves the mouse away, waits for the window to settle | the automation tree, and the client area's pixels |
| `click SEL` | UI Automation Invoke | done / not found |
| `type SEL "text"` | UI Automation Value (sets the whole text) | done / not found |
| `focus SEL`, `keys "text"` | focus, then real keystrokes | done |
| `mouse SEL [dx dy]` | a real mouse click at the centre, or at dx, dy from the top left | done / not found |
| `close` | closes the current window (as its close button does) | done |
| `windows` | - | the titles of the program's windows |
| `wait ms` | waits | - |
| `exit [ms]` | waits for the program to end | ended / still running |

Each side runs in its own fresh copy of this folder. otter-net runs with
`OTTER_NET_TRACE=1` and `OTTER_ENGINE=native`: a program it would hand to
the PowerShell Otter fails, and one that creates windows must show that it
ran in `otter-desktop` (`otter-net desktop:`).
