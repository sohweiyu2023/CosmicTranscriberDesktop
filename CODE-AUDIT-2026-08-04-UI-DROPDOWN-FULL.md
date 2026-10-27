# Cosmic Transcriber 1.0.27 — UI/Dropdown Full Adversarial Audit

**Audit date:** 4 August 2026  
**Status:** SOURCE REPAIR COMPLETE; WINDOWS BUILD/RUNTIME ACCEPTANCE REQUIRED

## User-reported defects reproduced from source and screenshot

The selected model text was almost white on a light selection field. The app set a light foreground through its dark-theme resources but relied on WPF's platform Fluent `ComboBox` template for the field surface. On the affected Windows path the platform template painted a light field, creating a mixed-theme white-on-light result.

The apparent unclickable final model was not a hit-testing failure: `gpt-live-transcribe` was deliberately hard-coded with `IsEnabled="False"`. Presenting a disabled realtime model inside a completed-file model chooser was misleading and made the selector look broken.

The independent follow-through also found a second-order usability regression in the earlier wheel guard: it prevented accidental selector changes by marking `PreviewMouseWheel` handled, but it also swallowed the gesture and stopped the surrounding settings page from scrolling. The repaired handler now forwards the wheel event to the parent visual while keeping the closed selector value unchanged.

## Repairs

1. The `ComboBox` now owns a complete application-themed template. The closed field explicitly binds `SurfaceElevatedBrush` and the control foreground; it no longer depends on the platform template's internal color choices.
2. The required `PART_Popup` remains present. It uses `AllowsTransparency="False"`, `PopupAnimation="None"`, an opaque themed border, a bounded `ScrollViewer`, and a direct `ItemsPresenter`.
3. `ComboBoxItem` also owns a complete template with explicit foreground rendering and selected/highlighted states.
4. Closed dropdowns still suppress mouse-wheel value changes while ordinary page scrolling is forwarded to the surrounding settings page.
5. The disabled realtime-only row was removed. The selector now contains exactly four supported completed-file models: `gpt-transcribe`, `gpt-4o-transcribe-diarize`, `gpt-4o-transcribe`, and `gpt-4o-mini-transcribe`.
6. `TranscriptionModelCatalog` centralizes those file model IDs. Saved unknown or obsolete model IDs are normalized to `gpt-transcribe`; the paired editable price estimate is reset at the same boundary. Settings schema is now 4.
7. Cosmic Paper's normal-sized status/muted text colors now meet at least 4.5:1 against the light hover surface. Its progress/control accent is also darkened to 3.07:1 on the hover surface and 3.67:1 on the elevated surface, while primary-button text remains 4.88:1.
8. Runtime, project, assembly, manifest, UI, script, preview, manual, and current documentation identity was advanced to 1.0.27.

## Fresh whole-code review

The pass rechecked startup/error boundaries, API-key storage and redaction, system proxy/network classification, request IDs and elapsed timing, model/language protocol separation, diarization parsing/export/checkpoints, MP3 inspection/chunk limits, cancellation/retry billing warnings, settings autosave and atomic publication, filesystem containment/reparse-point handling, output rollback, drag/drop/Paste behavior, XAML parsing/handlers/resources, portable sharing hygiene, documentation, and deterministic manifests.

No additional secret, plaintext-key, recursive-delete, unbounded remote-body, automatic-redirect, or ordinary-settings key-persistence regression was found. The existing native Windows build boundary remains: this Linux audit environment cannot compile or render a .NET 10 WPF executable.

## Verification evidence

- 28 C# files structurally checked.
- 7 XAML files XML-parsed.
- 39 unique event handlers / 44 bindings resolved.
- 52 resource references resolved.
- Light-theme normal-sized text contrast checked algorithmically at 4.5:1 minimum; progress/control accent checked at 3:1 minimum.
- 52/52 adversarial mutations rejected after manifest regeneration.
- Fresh deterministic CBR/VBR MP3 smoke evidence: six generated chunks decoded successfully; each first-chunk PCM prefix matched its source.
- The 16-page DOCX manual was rendered page by page, visually inspected, and accessibility-audited with zero findings.
- Inventory, SHA-256 manifest, package hygiene, secret-pattern scan, ZIP integrity, and clean extraction revalidated.

## Mandatory Windows acceptance before binary distribution

Run `BUILD.cmd` with .NET SDK 10.0.302 or a compatible .NET 10 feature band. Then test both themes at 100%, 125%, 150%, and 200% scaling. Open both model and retry selectors by mouse and keyboard, select the final visible item, scroll the dropdown, verify selected text contrast, confirm closed-wheel value suppression, and verify the same wheel gesture still scrolls the surrounding settings page. Complete one short normal transcription, one short diarized transcription, Test connection, cancel/resume, output opening, DPAPI reload, drag/drop, and portable publish checks.
