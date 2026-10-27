# Cosmic Transcriber 1.0.22 — UI, Drag-and-Drop, and Completion Workflow Audit

**Audit date:** 2026-07-29  
**Reviewed base:** Cosmic Transcriber 1.0.21 round-three reviewed source  
**Disposition:** repaired and repackaged as version 1.0.22.

## User-reported symptoms

1. The maximized/resized window appeared shifted and clipped, with the left edge of the interface outside the visible client area.
2. Drag-and-drop from File Explorer did not work.
3. A designed end-of-run window with an **Open output folder** action was requested.

## Root-cause findings and repairs

### 1. Retained horizontal scroll offset caused whole-window displacement — Medium

The main application content was hosted inside an outer horizontal `ScrollViewer`. A nonzero horizontal offset could survive resize/maximize transitions, shifting the complete visual tree left and reproducing the clipped screenshots. The former 1180-DIP starting width also fit poorly on displays using common Windows scaling factors.

Repairs:

- removed the outer horizontal scroll surface;
- changed the default geometry to 1060×700 with 960×620 minimums;
- rebalanced the main columns and their minimum widths;
- added work-area clamping on load;
- enabled layout rounding and device-pixel snapping; and
- declared Per-Monitor-V2 DPI awareness in `app.manifest`.

### 2. Windows integrity-level boundary blocked Explorer drag-and-drop — Medium

The screenshot and in-app banner established that Cosmic Transcriber was elevated. Normal File Explorer runs at medium integrity. Windows intentionally prevents lower-integrity applications from sending OLE drag/drop data to a higher-integrity process, so adding more WPF drop handlers cannot safely bypass the boundary.

Repairs:

- retained routed drop handling across the main window;
- retained the clipboard **Paste** fallback;
- added a prominent **Restart normally** action to the elevation warning;
- removed only the current-user `RUNASADMIN` compatibility token when present, preserving unrelated compatibility flags;
- delegated the relaunch to the ordinary Explorer desktop shell through the documented COM `ShellExecute` path; and
- refused to silently alter machine-wide compatibility settings.

The Explorer-shell interop is adapted from Microsoft Node.js Tools for Visual Studio and is attributed under Apache License 2.0 in `THIRD-PARTY-NOTICES.md` and `licenses/Apache-2.0.txt`.

### 3. Completion workflow enhanced

The previous generic message box was replaced by a themed modal completion window that displays:

- succeeded, failed, and skipped item counts;
- output-file count;
- elapsed time;
- the resolved output path; and
- a prominent **Open output folder** button.

The folder action uses Windows shell execution with directory validation and exception handling. Runs containing skipped or failed items are clearly labelled as completed with issues.

### 4. Stale visible verification evidence — Low

The Word manual still showed the prior source and test counts after the UI additions. The visible table was corrected and a dedicated mutation was added so stale evidence is detected in future releases.

## Verification completed

- Static source gate: PASS.
- 28 C# files inspected.
- 7 XAML files inspected.
- 36 XAML event handlers resolved.
- 52 XAML resource references resolved.
- 125/125 isolated adversarial mutations passed.
- Deterministic 680-second CBR and VBR media smoke tests passed.
- Six generated MP3 chunks decoded successfully with FFmpeg.
- Checkpoint, retry, cancellation, durable publication, cache containment, reparse-point, API-key, logging, model/language/keyword, and package-hygiene checks passed.
- Updated 18-page manual visually inspected page by page.
- DOCX accessibility audit: zero high-, medium-, or low-severity findings.

## Official implementation references consulted

- Microsoft Windows Classic Samples — Execute In Explorer sample.
- Microsoft Node.js Tools for Visual Studio — Explorer desktop-shell `ShellExecute` implementation.
- Microsoft Learn — High-DPI support in Windows Forms/WPF and application-manifest DPI declarations.
- Microsoft Learn — `ProcessStartInfo.UseShellExecute`.

## Residual Windows-only acceptance

The review environment cannot execute the native Windows WPF runtime, Explorer/OLE drag-and-drop, DPAPI, a live OpenAI upload, or real multi-monitor DPI transitions. On Windows:

1. Extract the source completely and run `BUILD.cmd`.
2. Launch the produced executable.
3. When the elevation warning appears, choose **Restart normally**.
4. Drag an MP3 from ordinary File Explorer into the queue.
5. Resize and maximize the window at the machine's actual display scaling.
6. Complete one short transcription and verify the designed completion window and **Open output folder** action.
