#!/usr/bin/env python3
"""Automated verification tests for the River Slate web renderer in otter-net."""

from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
OTTER_DLL = ROOT / "src" / "Otter.Cli" / "bin" / "Release" / "net10.0" / "otter.dll"


def compile_otter(source: str, name: str = "sample") -> str:
    with tempfile.TemporaryDirectory() as tmp:
        src_path = Path(tmp) / f"{name}.ot"
        html_path = Path(tmp) / f"{name}.html"
        src_path.write_text(source, encoding="utf-8")
        proc = subprocess.run(
            ["dotnet", str(OTTER_DLL), "web", str(src_path), "-NoOpen"],
            capture_output=True,
            text=True,
            check=False,
        )
        if proc.returncode != 0:
            raise AssertionError(
                f"otter web failed ({proc.returncode}):\nSTDOUT:\n{proc.stdout}\nSTDERR:\n{proc.stderr}"
            )
        if not html_path.exists():
            raise AssertionError(f"Expected compiled HTML at {html_path}")
        return html_path.read_text(encoding="utf-8")


def test_dark_default_tokens_and_components() -> None:
    src = '''
app is a page
    title is "River Slate Dark"
    scroll is true
.
mainGrid is a grid
    columns is 2
    spacing is 20
.
formCard is a card
    title is "Account Profile"
    subtitle is "Manage workspace preferences."
.
emailInput is a text box
    label is "Email address"
    placeholder is "dev@otter.example"
    hint is "Used for alerts and receipts."
.
badInput is a text box
    label is "Workspace slug"
    value is "Bad Slug!"
    error is "Use lowercase letters and hyphens only."
.
bioBox is a text area
    label is "Bio"
    placeholder is "Tell us about your project"
    rows is 4
.
regionSelect is a dropdown
    label is "Region"
    options is "US West, US East, EU Central"
.
volumeSlider is a slider
    label is "Alert threshold"
    min is 0
    max is 100
    value is 65
    showvalue is true
.
primaryBtn is a primary button with text "Save"
secondaryBtn is a secondary button with text "Reset"
outlineBtn is an outline button with text "Preview"
ghostBtn is a ghost button with text "Cancel"
dangerBtn is a danger button with text "Delete"
disabledBtn is a button with text "Locked", disabled true
okBadge is a badge with text "Healthy", variant "success"
warnBadge is a badge with text "Degraded", variant "warning"
put emailInput, badInput, bioBox, regionSelect, volumeSlider in formCard
put primaryBtn, secondaryBtn, outlineBtn, ghostBtn, dangerBtn, disabledBtn, okBadge, warnBadge in formCard
put formCard in mainGrid
put mainGrid in app
show app
'''
    html = compile_otter(src, "dark_river_slate")

    # Dark default (no otter-theme-light on body)
    assert '<body class="otter-has-page otter-doc-scroll">' in html
    assert "--otter-bg: #0b1220;" in html
    assert "--otter-surface: #111b2e;" in html
    assert ".otter-theme-light {" in html
    assert "--otter-focus-ring:" in html

    # Grid layout
    assert 'id="mainGrid" class="otter-grid"' in html
    assert "--otter-cols: 2;" in html
    assert "@media (max-width: 680px)" in html

    # Card title + subtitle
    assert '<h3 class="otter-card-title">Account Profile</h3>' in html
    assert '<p class="otter-card-subtitle">Manage workspace preferences.</p>' in html

    # Labeled field, hint, error, aria-invalid
    assert '<label class="otter-field-label" for="emailInput">Email address</label>' in html
    assert '<span class="otter-field-hint">Used for alerts and receipts.</span>' in html
    assert 'id="badInput" class="otter-text-box otter-input-error"' in html
    assert 'aria-invalid="true"' in html
    assert '<span class="otter-field-error" role="alert">Use lowercase letters and hyphens only.</span>' in html

    # Slider with live output readout
    assert 'class="otter-slider-row"' in html
    assert 'data-otter-slider-for="volumeSlider">65</output>' in html

    # Button variants + disabled
    assert 'id="primaryBtn" class="otter-button otter-button-primary"' in html
    assert 'id="secondaryBtn" class="otter-button otter-button-secondary"' in html
    assert 'id="outlineBtn" class="otter-button otter-button-outline"' in html
    assert 'id="ghostBtn" class="otter-button otter-button-ghost"' in html
    assert 'id="dangerBtn" class="otter-button otter-button-danger"' in html
    assert 'id="disabledBtn" class="otter-button" disabled>' in html

    # Badges
    assert 'id="okBadge" class="otter-badge otter-badge-success"' in html
    assert 'id="warnBadge" class="otter-badge otter-badge-warning"' in html


def test_light_theme_opt_in() -> None:
    src = '''
app is a page
    title is "River Slate Light"
    theme is "light"
    scroll is true
.
cardA is a card
    title is "Light Surface"
    subtitle is "Crafted light tokens with AA contrast."
.
darkInset is a card
    title is "Embedded Dark Card"
    theme is "dark"
.
put cardA, darkInset in app
show app
'''
    html = compile_otter(src, "light_river_slate")

    assert '<body class="otter-has-page otter-doc-scroll otter-theme-light">' in html
    assert "--otter-bg: #f4f7fb;" in html
    assert "--otter-text: #0b1d36;" in html
    assert 'id="darkInset" class="otter-card otter-theme-dark"' in html


def test_backward_compatibility_plain_controls() -> None:
    src = '''
app is a page
    title is "Legacy App"
    background is "#101828"
    padding is 0
.
plainBtn is a button with text "Click"
plainBox is a text box with placeholder "Type here"
plainSlider is a slider with min 0, max 10, value 5
put plainBtn, plainBox, plainSlider in app
show app
'''
    html = compile_otter(src, "legacy_compat")

    assert "--otter-bg: #101828;" in html
    assert "padding: 0px;" in html
    assert '<button id="plainBtn" class="otter-button">Click</button>' in html
    assert '<input type="text" id="plainBox" class="otter-text-box" value="" placeholder="Type here" />' in html
    assert '<div class="otter-field">' not in html
    assert '<output ' not in html


def main() -> int:
    if not OTTER_DLL.exists():
        print(f"Missing built CLI at {OTTER_DLL}. Run dotnet build -c Release first.", file=sys.stderr)
        return 1
    tests = [
        test_dark_default_tokens_and_components,
        test_light_theme_opt_in,
        test_backward_compatibility_plain_controls,
    ]
    for fn in tests:
        fn()
        print(f"  pass  {fn.__name__}")
    print(f"All {len(tests)} River Slate renderer tests passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
