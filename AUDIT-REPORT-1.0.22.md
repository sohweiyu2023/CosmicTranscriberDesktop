# Cosmic Transcriber 1.0.22 — Independent Corrective Audit Report

**Audit date:** 2026-07-29  
**Reviewed base:** Cosmic Transcriber 1.0.21 round-three reviewed source  
**Result:** two user-visible defects, one requested workflow enhancement, and one low-severity evidence defect repaired; no additional blocker or high-severity defect found in this pass.

## Finding repaired: whole-window horizontal offset and oversized default geometry (Medium)

The main visual tree was wrapped in an outer horizontal `ScrollViewer`. After the user resized or maximized the window, that control could retain a nonzero horizontal offset, shifting the header and left queue edge off-screen. The original 1180-DIP default width also occupied nearly the entire physical screen under common Windows scaling values.

Version 1.0.22 removes the outer horizontal scroll surface, uses a 1060×700 default with rebalanced column minimums, enables layout rounding/device-pixel snapping, and declares Per-Monitor-V2 DPI awareness in the application manifest.

## Finding repaired: drag-and-drop blocked by elevation mismatch (Medium)

The screenshots showed that Cosmic Transcriber was running as administrator while normal File Explorer was not. Windows User Interface Privilege Isolation intentionally prevents this OLE drag-and-drop direction; a WPF handler cannot safely override the OS boundary.

The warning banner now includes **Restart normally**. The app can remove a current-user `RUNASADMIN` compatibility token while preserving unrelated compatibility tokens, then asks the ordinary Explorer shell to reopen the current executable. Machine-wide compatibility rules are detected and explained but are not changed automatically. Full-window routed drop handling and the Paste fallback remain in place.

## Requested enhancement: designed completion window

The plain end-of-run message box is replaced with a themed modal window showing succeeded/failed/skipped counts, output-file count, elapsed time, and a selectable output path. A prominent **Open output folder** button uses Windows shell execution. Skipped items now produce a “completed with issues” status.

## Finding repaired: stale manual verification counts (Low)

The comprehensive manual still displayed the pre-change C#/XAML/handler and mutation counts. The visible evidence table is corrected to 28 C# files, 7 XAML files, 36 handlers, 52 resources, and 125 mutation cases. The static verifier and a dedicated mutation now protect those visible values.

## Verification

- Static source gate: PASS after all repairs.
- C#/XAML event and resource wiring: PASS.
- Ran 125 mutation tests: PASS — 125/125 cases.
- New mutations cover responsive-layout regression, DPI declaration removal, loss of one-click restart, loss of Explorer delegation, loss of the completion dialog, and loss of its shell folder action, and loss of current visible verification counts in the DOCX manual.
- Existing media, checkpoint, billing, filesystem, API-key, logging, model/language/keyword, and package-hygiene checks remain enabled.

## Residual Windows acceptance

This Linux environment cannot execute the Windows WPF runtime, OLE/Explorer drag-and-drop, DPAPI, or a real OpenAI request. Run `BUILD.cmd`, verify resize/maximize behavior at the PC’s actual scaling, use **Restart normally** from an elevated launch, drag an MP3 from normal Explorer, and complete one short transcription on Windows.
