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

## Native acceptance follow-up

Added an explicit disposable-profile interactive harness using the real MainWindow and app resources. Normal application startup remains unchanged; the internal constructor permits test settings injection. The harness clears its own inherited OPENAI_API_KEY and does not use the installed app profile. Run `QuickStartRegression.exe --interactive <disposable-directory>` after building. Native interactions must use the approved desktop control tool.

The fixture launched successfully with title `Cosmic tutorial QA - disposable profile`, alongside the unchanged installed instance. Selecting the fixture through Computer Use returned `Computer Use app approval timed out`; no mouse/keyboard actions were sent. Native acceptance therefore remains blocked on app approval, not passed.

The five withheld original assets were found inside Downloads/CosmicTranscriber-1.0.27-ui-dropdown-full-audit-reviewed.zip. Each byte count and SHA-256 matches WITHHELD-ASSETS.json. They were read for identity only, not imported or published. Their declared rights/provenance restrictions remain unresolved; locating them does not clear a release gate.

The interactive host now refuses nonempty unmarked profile directories and any saved-key file. A fixed marker permits repeat tests in its own disposable profile. This tests the real MainWindow constructor/handlers but not normal App startup/exit wiring. Production diagnostics can still fall back to LocalAppData if executable-directory logging fails. The idle test process was stopped after approval timed out; installed Cosmic was not touched.
