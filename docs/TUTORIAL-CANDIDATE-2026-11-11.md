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

## Expanded rendered layout acceptance

Full LayoutRegression completed:72/72 scenarios passed, zero reachability or button-foreground failures. Matrix: both themes;1060x700,1044x660,960x620,944x600,780x500,640x420;96/144/192DPI; default/expanded controls. This remains offscreen rendering, not native pointer, keyboard, OS scaling or monitor-transition acceptance. Evidence: layout-full-night/summary.json and layout-full-paper/summary.json in the external evidence directory. The hardened interactive host also correctly refused a nonempty unmarked test directory before opening any window;123 tutorial assertions still passed.

The illustrative layout fixture was corrected to show one selected file for its sample queue and unresolved-request language instead of stale automatic-retry wording. Four focused layout cases passed again after this test-data-only change. The full72-case matrix preceded this label correction; no production UI change was made.

## Original documentation replacement

The previous missing-preview/manual gate has been addressed without reusing any withheld asset. A separate private clean export contains original HTML/PNG previews and an original comprehensive manual built from current source and corrected synthetic UI renders. The historical withheld files remain excluded and their provenance record is preserved.

The new manual v2 is222239bytes, SHA25640e5eb89e9630dec8976f173e15a9c0175d97df1afbbe808055fa17962b31c07. A bundled cloud renderer produced11 Letter pages, and every page image passed visual inspection after two layout revisions. Accessibility audit has zero high/medium/low findings. The first complete export passed the full static gate and56/56 mutation tests; its final source overlay and provenance are recorded beside the private export. This is evidence for documentation quality, not native app acceptance or permission to publish withheld artwork.

Reader/developer source guides were reconciled for supported media, disabled paid retries, local unresolved-attempt review, safe cache handling, current target framework/SDK/packages and tutorial navigation. No public main, installed app, public ZIP or deployment was changed. Native tutorial pointer/keyboard, Escape/owner lifecycle and actual monitor acceptance remain outstanding because app approval timed out, then desktop use was reserved for another user task.

## Native acceptance update, 11 November 2026

The earlier approval timeout is no longer the current blocker. The real MainWindow was tested through the isolated interactive host: real mouse Start and Next traversed all seven guide pages, Finish closed the guide, F1 replay started at page one, dragging worked, owner Alt+F4 closed both windows, and the dismissed invitation stayed dismissed after restart. Cosmic Paper was verified natively. Moving the app with Windows+Shift+Right placed both main and guide entirely on the negative-X secondary monitor (main origin -1005,12; guide -810,90). Minimizing the guide then F1 restored it. Show this area on account setup scrolled the key card without testing a key or sending audio.

The supported desktop inventory omits the owned guide window. Coordinate region clicks work, but element-index actions reject the child window and keyboard actions reactivate the parent. To test keyboard without changing production, QuickStartRegression now has --interactive-guide: actual QuickStartWindow controls/handlers, no callback, standalone taskbar-visible fixture. Native Enter traversed every page, Shift+Tab and Space activated Back, Tab could recover from the first-page disabled Back, and Escape closed the window. This does not certify owner-dialog keyboard targeting, normal App startup/exit, or the installed release. No paid provider request was made.

Remaining release work: source/privacy/CI checks on the new exact commit, release version/package identity, packaged-app startup/install acceptance, public ZIP verification and publication. Main and public ZIP were not changed by these tests. The installed 1.0.28 app was not modified. Native evidence remains external, with no personal content added to the repository.