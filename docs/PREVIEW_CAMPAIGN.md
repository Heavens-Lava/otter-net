# otter-net internal testing campaign (0.9.0-preview.1)

The plan for the first round of trusted testing, and the place its results
are recorded. Approved by the owner on 2026-10-23.

The goal is to find **regressions and security problems** before any public
distribution is proposed. New language features are out of scope.

## What is being tested

| | |
|---|---|
| Baseline | commit `f4e513431b23d64898ac44543f057dc5699952a0` (certified; [CERTIFICATION.md](CERTIFICATION.md)) |
| The **only** artifact for testers | run [39881403663](https://github.com/Heavens-Lava/otter-net/actions/runs/39881403663), `otter-net-0.9.0-preview.1-INTERNAL-PREVIEW-UNSIGNED` (artifact 12471417045), available until **2026-11-22** |
| Tester guide (authoritative) | [PREVIEW_TESTING.md](PREVIEW_TESTING.md) |
| Repeatable check | [preview-kit/](../preview-kit/README.md): 11 checks against Otter 1.0.1's own output, and a report of the machine's details |
| Labels | unsigned, internal, trusted testers only, not production-ready, not private (public repository) |

The packages install the `otter-net` command only. The stable `otter`
command stays otterPS's, and testers check that it does.

## Schedule

| When | What |
|---|---|
| until 2026-10-26 | The owner invites the testers and sends them PREVIEW_TESTING.md. |
| 2026-10-26 to 2026-11-09 | Testing (two weeks). Reports are triaged within two working days. |
| 2026-11-12 | The feedback report (below): coverage, issues by severity, blockers. Only after it, a proposal on public distribution. |
| 2026-11-22 | The artifact expires. A longer campaign needs a fresh artifact of the same commit (a manual run of package.yml on the baseline), approved by the owner. |

## Coverage: one tester (at least) per row

| # | Machine | Package | otterPS 1.0.1 also installed? |
|---|---|---|---|
| 1 | Windows 11 x64 | `.msi` | **yes** (side by side) |
| 2 | Windows 11 x64 or Windows 10 | `.zip` | no |
| 3 | Windows 11 on Arm | `.msi` | either |
| 4 | Mac, Apple silicon | `.pkg` | **yes** |
| 5 | Mac, Intel | `.tar.gz` | either |
| 6 | Debian or Ubuntu x64 | `.deb` | either |
| 7 | Chromebook (Linux development environment) | `.deb` | no |
| 8 | Any Linux | `.tar.gz` | **yes** |

Prefer testers who already write Otter programs: their own programs are
the most valuable test.

## What each tester does

PREVIEW_TESTING.md has the steps; the focus areas:

1. **Installation usability**:
   - Install from the guide alone.
   - Note every warning (SmartScreen, Gatekeeper, antivirus) and anything
     unclear.
   - Check that a new terminal has `otter-net`, and that `otter` is
     unchanged.
2. **Actual programs**:
   - The preview kit: 11 checks, expected "11 passed, 0 failed".
   - The tester's own programs and projects.
   - Where otterPS is installed, run the same program with `otter` and
     `otter-net` and compare the output.
3. **Diagnostics**:
   - Make deliberate mistakes (a typo, a missing file, dividing by zero).
   - Check that the error messages are the same as Otter 1.0.1's.
   - For anything odd, collect `OTTER_NET_TRACE=1` output and the kit
     report.
4. **Desktop windows (Windows)**:
   - The kit's `desktop\greeter.ot` and `desktop\calculator.ot`, and the
     tester's own windows.
   - Try them at 150-200% display scale.
5. **Uninstall**:
   - Uninstall following the guide.
   - Check that nothing is left, that `otter-net` is gone, and that
     `otter` works as before.

## Reporting

- **Bugs:** the
  ["Preview: bug report"](https://github.com/Heavens-Lava/otter-net/issues/new?template=preview-bug.yml)
  issue form. It asks for the platform, the package, the area, whether
  otterPS does the same, exact steps and output, and the kit report.
- **Overall experience:** the
  ["Preview: feedback"](https://github.com/Heavens-Lava/otter-net/issues/new?template=preview-feedback.yml)
  form.
- **Security problems: never in a public issue.** Send them to the owner
  privately. The repository's private vulnerability reporting is
  **off**; turning it on (Settings > Code security > Private
  vulnerability reporting) gives testers a private form. Recommended
  before the campaign starts.
- Issues are public: testers leave out private code and data.

## Triage

| Severity | What | Response |
|---|---|---|
| **S0 security** | runs, reads, writes or sends what it should not; an installer that changes or deletes something not its own (including `otter`) | stop handing out the artifact, fix first, owner informed the same day |
| **S1 regression** | different from Otter 1.0.1 (otterPS) for the same program; a crash or "internal error"; data loss; install or uninstall broken on a platform | fix before any public proposal |
| **S2 defect** | a problem otterPS also has, an installer rough edge, slowness | fix or record a decision |
| **S3 usability** | unclear guide, message or warning | improve the docs or packages |
| out of scope | new language features, changes to Otter 1.0.1's behaviour | recorded for the owner (`docs/SPEC-QUESTIONS.md`), not changed in the preview |

Fixes go on a branch from the baseline. Each one gets a test that
reproduces the report, plus the full certification. A fixed build reaches
testers only as a new preview (`0.9.0-preview.2`, new artifact; the old one
retired) with the owner's approval. otterPS 1.0.1 is never modified.

## What blocks proposing public distribution

- Any open **S0** or **S1**.
- Any platform row above not covered, or failing to install or uninstall
  cleanly.
- `otter` ever changed by an otter-net package.
- Signing not active: the owner's purchase decision ([SIGNING.md](SIGNING.md)).
- The `.deb` maintainer address not confirmed by a delivery test.

## Feedback report

| Date | Testers | Rows covered | Reports | S0 | S1 | S2 | S3 | Blockers |
|---|---|---|---|---|---|---|---|---|
| 2026-10-23 | none yet (campaign prepared) | 0 of 8 | 0 | 0 | 0 | 0 | 0 | public distribution: signing not active; maintainer address unconfirmed |

Before testers start, the kit was run against the official artifact on
clean machines of every platform (`.github/workflows/preview-kit.yml`):
Windows x64 and Arm64 (MSI, Windows PowerShell 5.1), Debian 12 x64 and
Arm64 (`.deb`), and macOS arm64 and x64 (`.tar.gz`). The result is in the
row above once the run finishes.
