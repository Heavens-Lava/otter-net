#!/usr/bin/env python3
"""
tests/language/test_value_first_properties.py

Comprehensive automated verification for Otter Value-First Property Syntax
(`<value> <property>`), multi-line `with ... .` blocks, legacy compatibility,
UI property validation diagnostics, formatter support, and byte-for-byte
HTML/runtime equivalence.
"""

import os
import subprocess
import sys
import tempfile

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RELEASE_DLL = os.path.join(REPO_ROOT, "src", "Otter.Cli", "bin", "Release", "net10.0", "otter.dll")
DEBUG_DLL = os.path.join(REPO_ROOT, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")
CLI_DLL = RELEASE_DLL if os.path.exists(RELEASE_DLL) else DEBUG_DLL

passed = 0
failed = 0


def run_otter(args, source_text=None):
    tmp_path = None
    try:
        cmd = ["dotnet", CLI_DLL] + list(args)
        if source_text is not None:
            fd, tmp_path = tempfile.mkstemp(suffix=".ot", prefix="otter_vf_")
            with os.fdopen(fd, "w", encoding="utf-8") as f:
                f.write(source_text)
            cmd.append(tmp_path)
        proc = subprocess.run(cmd, cwd=REPO_ROOT, capture_output=True, text=True, timeout=30)
        return proc.returncode, proc.stdout, proc.stderr
    finally:
        if tmp_path and os.path.exists(tmp_path):
            os.remove(tmp_path)


def compile_web(source_text):
    fd_in, in_path = tempfile.mkstemp(suffix=".ot", prefix="otter_vf_web_")
    out_path = in_path[:-3] + ".html"
    try:
        with os.fdopen(fd_in, "w", encoding="utf-8") as f:
            f.write(source_text)
        proc = subprocess.run(
            ["dotnet", CLI_DLL, "web", in_path, "-NoOpen"],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            timeout=30,
        )
        html = ""
        if os.path.exists(out_path):
            with open(out_path, "r", encoding="utf-8") as f:
                html = f.read()
        return proc.returncode, html, proc.stdout + proc.stderr
    finally:
        for p in (in_path, out_path):
            if os.path.exists(p):
                os.remove(p)


def assert_test(name, condition, detail=""):
    global passed, failed
    if condition:
        passed += 1
        print(f"  [PASS] {name}")
    else:
        failed += 1
        print(f"  [FAIL] {name}: {detail}")


def main():
    if not os.path.exists(CLI_DLL):
        print(f"ERROR: Build Otter.Cli first ({CLI_DLL})", file=sys.stderr)
        sys.exit(1)

    print("=" * 68)
    print(" Otter Value-First Property Syntax Test Suite")
    print("=" * 68)

    # 1. Full Section 3 multi-line example + single-line equivalence
    new_multiline_nav = """
app is a page with
    "Otter Site" title,
    true hideheader,
    true scroll,
    full width,
    "#f4f7fb" background,
    "#0f1f36" foreground,
    0 spacing,
    0 padding
.

brandMark is a image with "/static/logo.png" source, "Otter" alt, 34 width, 34 height, 17 radius
brandName is a link with "Otter" text, "/" url, "#ffffff" foreground, 22 size, 700 weight
brandGroup is a row with 10 spacing, middle align
put brandMark, brandName in brandGroup

navPlayground is a link with
    "Playground" text,
    "/playground/" url,
    "#d7e3f4" foreground,
    14 size,
    600 weight,
    "padding: 22px 0; border-bottom: 2px solid transparent;" customstyle
.

navLibrary is a link with
    "Library" text,
    "/library/" url,
    "#d7e3f4" foreground,
    14 size,
    500 weight
.

navPulse is a link with
    "Otter Pulse" text,
    "/otter-pulse/" url,
    "#d7e3f4" foreground,
    14 size,
    500 weight
.

navDocs is a link with "Documentation" text, "/docs/" url, 14 size, 500 weight

docsSearchButton is a button with
    "Search docs & library...  Ctrl K" text,
    12 size,
    500 weight,
    "#173252" background,
    "#dbeafe" foreground,
    8 radius,
    "8px 14px" padding,
    "border: 1px solid #315377; min-width: 214px; text-align: left;" customstyle
.

navLinks is a row with
    18 spacing,
    middle align
.

put navPlayground, navLibrary, navPulse, navDocs, docsSearchButton in navLinks

topBar is a row with
    spread,
    middle align,
    36 spacing,
    full width,
    "#0b1d36" background,
    "0 40px" padding,
    64 height,
    "box-sizing: border-box; position: sticky; top: 0; z-index: 50;" customstyle
.

put brandGroup, navLinks in topBar
put topBar in app
show app
"""

    legacy_nav = """
app is a page with title "Otter Site", hideheader true, scroll true, width full, background "#f4f7fb", foreground "#0f1f36", spacing 0, padding 0

brandMark is a image with source "/static/logo.png", alt "Otter", width 34, height 34, radius 17
brandName is a link with text "Otter", url "/", foreground "#ffffff", size 22, weight 700
brandGroup is a row with spacing 10, align middle
put brandMark, brandName in brandGroup

navPlayground is a link with text "Playground", url "/playground/", foreground "#d7e3f4", size 14, weight 600, customstyle "padding: 22px 0; border-bottom: 2px solid transparent;"
navLibrary is a link with text "Library", url "/library/", foreground "#d7e3f4", size 14, weight 500
navPulse is a link with text "Otter Pulse", url "/otter-pulse/", foreground "#d7e3f4", size 14, weight 500
navDocs is a link with text "Documentation", url "/docs/", size 14, weight 500
docsSearchButton is a button with text "Search docs & library...  Ctrl K", size 12, weight 500, background "#173252", foreground "#dbeafe", radius 8, padding "8px 14px", customstyle "border: 1px solid #315377; min-width: 214px; text-align: left;"
navLinks is a row with spacing 18, align middle
put navPlayground, navLibrary, navPulse, navDocs, docsSearchButton in navLinks
topBar is a row with spread, align middle, spacing 36, width full, background "#0b1d36", padding "0 40px", height 64, customstyle "box-sizing: border-box; position: sticky; top: 0; z-index: 50;"
put brandGroup, navLinks in topBar
put topBar in app
show app
"""

    rc_new, html_new, log_new = compile_web(new_multiline_nav)
    rc_leg, html_leg, log_leg = compile_web(legacy_nav)
    assert_test("Full Section 3 multi-line + single-line navbar compiles via otter web", rc_new == 0, log_new)
    assert_test("Legacy navbar compiles via otter web", rc_leg == 0, log_leg)
    assert_test(
        "Value-first multi-line and legacy property-first produce BYTE-FOR-BYTE identical HTML",
        html_new == html_leg and len(html_new) > 500,
        f"len(new)={len(html_new)}, len(legacy)={len(html_leg)}",
    )

    # 2. All UI component families + mixed order + property of object preservation
    all_components = """
app is a page with "All Components" title, true scroll, full width
headingTxt is a text with "Welcome" value, 32 size, 700 weight, center align
cardBox is a card with "#ffffff" background, 16 radius, 24 padding, 12 spacing
nameField is a text box with "Enter name" placeholder, 260 width
notesArea is a text area with "Notes..." placeholder, 4 rows
agreeBox is a checkbox with "I agree" text, true checked
roleDrop is a dropdown with "Dev,Ops,QA" options
volSlider is a slider with 0 min, 100 max, 75 value
statusBadge is a badge with "LIVE" text, round, "#16a34a" background, "#ffffff" foreground
primaryBtn is a primary button with "Save" text, 14 size
dangerBtn is a danger button with "Delete" text, 14 size
dragPanel is a panel with true draggable, true accepts drops
mixedBtn is a button with "Mixed" text, size 16, middle align, width full, spread
customstyle of headingTxt is "margin-bottom: 12px;"
col is a column with 10 spacing, left align
put headingTxt, cardBox, nameField, notesArea, agreeBox, roleDrop, volSlider, statusBadge, primaryBtn, dangerBtn, dragPanel, mixedBtn in col
put col in app
show app
"""
    rc_all, html_all, log_all = compile_web(all_components)
    assert_test("All UI component families + mixed order + 'of' assignment compile cleanly", rc_all == 0 and "LIVE" in html_all and 'draggable="true"' in html_all and 'data-otter-accepts-drops="true"' in html_all, log_all)

    # 3. Runtime equivalence: custom types, expressions, variable-first, legacy is, inline has
    runtime_vf = """
a Person has
    name
    age
    role
.

baseAge is 25
userLabel is "Ada"
author has
    title is "Engineer"
.

p1 is a Person with
    userLabel name,
    baseAge plus 4 age,
    title of author role
.

p2 is a Person with "Grace" name, 35 age, "Admiral" role
inlineObj has "Otter" lang, 101 version
say name of p1 age of p1 role of p1
say name of p2 age of p2 role of p2
say lang of inlineObj version of inlineObj
"""
    runtime_leg = """
a Person has
    name
    age
    role
.

baseAge is 25
userLabel is "Ada"
author has
    title is "Engineer"
.

p1 is a Person with name is userLabel, age baseAge plus 4, role title of author
p2 is a Person with name "Grace", age 35, role "Admiral"
inlineObj has lang "Otter", version 101
say name of p1 age of p1 role of p1
say name of p2 age of p2 role of p2
say lang of inlineObj version of inlineObj
"""
    rc_rvf, out_rvf, err_rvf = run_otter(["run"], runtime_vf)
    rc_rleg, out_rleg, err_rleg = run_otter(["run"], runtime_leg)
    assert_test("Value-first runtime objects, expressions, variables, and inline 'has' execute cleanly", rc_rvf == 0, err_rvf)
    assert_test(
        "Value-first and legacy runtime output are identical",
        out_rvf == out_rleg and "Ada 29 Engineer" in out_rvf and "Otter 101" in out_rvf,
        f"vf={out_rvf!r} vs leg={out_rleg!r}",
    )

    # 4. Negative diagnostics
    negative_cases = [
        ("Duplicate property (value-first)", 'b is a button with "A" text, "B" text\n', "Duplicate property"),
        ("Duplicate property (mixed order)", 'b is a button with "A" text, text "B"\n', "Duplicate property"),
        ("Unknown UI property", 'b is a button with "Click" nonexistentProp\n', "Unknown property"),
        ("Invalid boolean literal for size", 'b is a button with true size\n', "expects a number"),
        ("Invalid string literal for spacing", 'r is a row with "wide" spacing\n', "expects a number"),
        ("Invalid non-boolean literal for scroll", 'p is a page with "yes" scroll\n', "true/false"),
        ("Invalid non-boolean literal for showvalue", 's is a slider with "yes" showvalue\n', "true/false"),
        ("Invalid non-numeric literal for columns", 'g is a grid with "two" columns\n', "expects a number"),
        ("Invalid non-string literal for theme", 'p is a page with 123 theme\n', "expects text"),
        ("Invalid non-string literal for url", 'l is a link with 123 url\n', "expects text"),
        ("Invalid alignment direction (value-first)", 'r is a row with "diagonal" align\n', "alignment direction"),
        ("Invalid alignment identifier (value-first)", 'r is a row with sideways align\n', "alignment direction"),
        ("Missing property name after value", 'b is a button with 14\n', "expected a property name"),
        ("Missing value after is", 'b is a button with text is\n', "expected a value"),
        ("Missing comma in single-line", 'b is a badge with text "JM" weight 700\n', "expected a comma"),
        ("Missing comma after flag", 'b is a badge with round weight 700\n', "expected a comma"),
        ("Trailing comma on single-line", 'b is a button with "Hi" text,\n', "expected a property after the comma"),
        ("Two entries on one multiline line", 'b is a button with\n    "Hi" text, 14 size\n.\n', "each property on its own line"),
    ]
    for label, src, expected_msg in negative_cases:
        rc, out, err = run_otter(["check"], src)
        combined = out + "\n" + err
        assert_test(
            f"Rejects: {label}",
            rc != 0 and expected_msg.lower() in combined.lower(),
            f"rc={rc}, output={combined.strip()[:180]}",
        )

    # 5. River Slate UI components using value-first syntax
    river_slate_vf = '''
app is a page with
    "River Slate Value-First" title,
    "light" theme,
    true scroll
.
mainGrid is a grid with 2 columns, 20 spacing
infoCard is a card with
    "Form Controls" title,
    "Validated under value-first syntax." subtitle,
    "dark" theme
.
emailBox is a text box with
    "Email address" label,
    "dev@otter.example" placeholder,
    "Used for notifications." hint,
    "Invalid email" error
.
thresholdSlider is a slider with
    "CPU threshold" label,
    0 min,
    100 max,
    65 value,
    5 step,
    true showvalue
.
primaryBtn is a primary button with "Primary" text
outlineBtn is an outline button with "Outline" text
ghostBtn is a ghost button with "Ghost" text
disabledBtn is a button with "Locked" text, true disabled
okBadge is a badge with "Healthy" text, "success" variant
put emailBox, thresholdSlider, primaryBtn, outlineBtn, ghostBtn, disabledBtn, okBadge in infoCard
put infoCard in mainGrid
put mainGrid in app
show app
'''
    rc_rs, out_rs, err_rs = run_otter(["check"], river_slate_vf)
    assert_test(
        "River Slate components with value-first syntax pass check & validation",
        rc_rs == 0,
        f"rc={rc_rs}, out={out_rs.strip()[:180]}, err={err_rs.strip()[:180]}",
    )

    # 5b. Regression test: legacy `<known-ui-prop> <var>` (e.g. `item has text task` from desktop example_tasks.ot)
    #     must continue assigning `item.text = task`, while value-first `item2 has task text` also assigns `item2.text = task`.
    legacy_two_ident_src = '''
task is "Walk the otter"
item has text task
item2 has task text
say text of item
say text of item2
'''
    rc_l2i, out_l2i, err_l2i = run_otter(["run"], legacy_two_ident_src)
    assert_test(
        "Legacy `<known-ui-prop> <var>` (`item has text task`) and value-first (`item2 has task text`) both assign text=task",
        rc_l2i == 0 and out_l2i.strip().splitlines() == ["Walk the otter", "Walk the otter"],
        f"rc={rc_l2i}, out={out_l2i!r}, err={err_l2i!r}",
    )

    # 6. Formatter preserves indentation for multi-line `with ... .`
    unformatted = 'nav is a link with\n"Home" text,\n"/" url\n.\n'
    fd_fmt, fmt_path = tempfile.mkstemp(suffix=".ot", prefix="otter_vf_fmt_")
    try:
        with os.fdopen(fd_fmt, "w", encoding="utf-8") as f:
            f.write(unformatted)
        proc_fmt = subprocess.run(["dotnet", CLI_DLL, "format", fmt_path], cwd=REPO_ROOT, capture_output=True, text=True, timeout=30)
        with open(fmt_path, "r", encoding="utf-8") as f:
            formatted_text = f.read()
        assert_test(
            "Formatter indents multi-line `with ... .` blocks cleanly",
            proc_fmt.returncode == 0 and '    "Home" text,' in formatted_text and '    "/" url' in formatted_text and "\n.\n" in formatted_text,
            f"rc={proc_fmt.returncode}, text={formatted_text!r}",
        )
    finally:
        if os.path.exists(fmt_path):
            os.remove(fmt_path)

    print("=" * 68)
    print(f" Summary: {passed} passed, {failed} failed")
    print("=" * 68)
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
