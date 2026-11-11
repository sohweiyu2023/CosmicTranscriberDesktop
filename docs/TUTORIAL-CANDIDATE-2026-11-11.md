# Beginner tutorial candidate

Base: e3257ba2194a7bd767248b2dd15ae3004f880837 (main, desktop 1.0.28).
Branch: feature/beginner-tutorial-20261111. The installed app, remote main, public ZIP and web app are unchanged.

Implemented optional first invitation, persistent Quick start/F1 replay, seven guide screens, Back/Next/Finish/Close and reveal-area navigation. No provider requests. The guide explains setup, media conversion requirements, models, output, cost, cancellation and recovery.

## Verified locally
- Release solution build: zero warnings/errors.
- QuickStartRegression: 123 passing checks, compiled WPF handlers and rendered layouts.
- NormalSettingsRegression: 15 passing checks with synthetic settings.
- ThemeRegression: 235 checks, zero failures.
- LayoutRegression quick assertion runs: both themes, 960x620 and 640x420, expanded/default, zero reachability/foreground failures in all eight scenarios.
- Source-only static verifier and source export audit passed.
- Two independent source review rounds completed; reported issues fixed.

The first banner layout squeezed queue rows. Increasing scrollable workspace minimum content height to720 fixed all findings. Unchanged main had eight findings in four dark-theme baseline scenarios; candidate has zero.

## Not yet verified / release gates
- Actual pointer and keyboard inputs, Escape, owner close, invitation persistence through actual MainWindow, dual monitor and DPI acceptance. Do not equate rendered tests with native input tests.
- Full release mutation suite cannot start on this source-only built checkout: withheld original preview/manual assets and build/Git residue fail its baseline. Run it on a complete clean release export; do not weaken that gate.
- No installer, release package, paid test, commit on main, deployment or public upload performed.

Rendered evidence is in the sibling cosmic-tutorial-evidence-20261111 directory, outside source publication. Developer guide lists reproducible commands and native acceptance steps.
