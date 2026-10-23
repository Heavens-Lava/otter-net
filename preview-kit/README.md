# otter-net preview kit

A quick, repeatable check for testers of the otter-net internal preview
(installation: [docs/PREVIEW_TESTING.md](../docs/PREVIEW_TESTING.md)). It
runs real Otter programs with your installed `otter-net` and compares the
results with what **Otter 1.0.1 (otterPS)** printed for the same programs,
then writes `preview-kit-report.txt` with your machine's details: attach it
to every report.

It changes nothing outside a temporary folder and sends nothing anywhere.

## Get it

On GitHub, open <https://github.com/Heavens-Lava/otter-net>, then **Code >
Download ZIP**, extract it, and open the `preview-kit` folder in a terminal.

## Run it

- **Windows** (PowerShell or Command Prompt):

      powershell -ExecutionPolicy Bypass -File check.ps1

- **macOS, Linux, Chromebook:**

      sh check.sh

If `otter-net` is not found, open a new terminal after installing, or pass
its path: `check.ps1 -OtterNet C:\path\otter-net.exe` / `sh check.sh
/path/to/otter-net`.

Expected result: **11 passed, 0 failed**. Anything else is worth a report:
the report file lists exactly what differed.

## What it checks

| Part | Programs | Compared with |
|---|---|---|
| Programs | `hello`, `conditions`, `collections`, `csv`, `json`, `files`, `math`, `objects` (otterPS 1.0.1's own examples, unchanged) | the output otterPS 1.0.1 printed on Windows PowerShell 5.1 (`expected/*.out`, from the certification of `f4e5134`) |
| Web pages | `hello-app`, `greeter` compiled with `otter-net web <file> -NoOpen` | otterPS 1.0.1's compiled HTML (`expected/*.html`); line endings ignored, as they follow your file's |
| A new project | `otter-net new console demo`, then `run .` and `test .` | the template's own output |

## Desktop windows (Windows only, by hand)

    otter-net run desktop\greeter.ot

A window titled **Otter** opens with a text box saying `Jeff` and a **Say
Hello** button. Clicking the button prints `Hello, Jeff` (or whatever you
typed) in the terminal; closing the window ends the program.

    otter-net run desktop\calculator.ot

A dark window **Otter Calculator** with two boxes and an **Add** button;
the result appears under "Result". Note: `2` and `3` show **`23`**. Text
boxes hold text, and Otter 1.0.1 joins two texts with `plus` - otterPS
shows the same, so that is not an otter-net bug (the preview does not
change the language).

Please also try it at a high display scale (150-200%), and tell us if
anything looks cut off or blurry.

On macOS and Linux, desktop windows are a Windows feature (as in Otter
1.0.1): the same programs stop with "Otter could not start its UI provider.
WPF may not be available on this system." and exit code 3. That is
expected.
