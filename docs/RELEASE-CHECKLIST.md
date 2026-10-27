# Cosmic Transcriber 1.0.27 - Release Checklist

## Structured keyword acceptance

- [ ] `gpt-transcribe` accepts several one-per-line keyword hints and sends the full list.
- [ ] Duplicate keyword lines normalize to one entry.
- [ ] `<`, `>`, embedded control characters, overlong input, and more than 200 entries are rejected before upload.
- [ ] A legacy model plus non-empty keywords is rejected locally.
- [ ] Changing keywords produces a different checkpoint work identity.

Use this checklist on Windows 10/11 with the .NET 10 SDK. Record failures and unexecuted items honestly; a Linux source audit is not a substitute.

## A. Clean source and identity

- [ ] Source root is the intended 1.0.27 tree.
- [ ] Project versions are `1.0.27` / `1.0.27.0`; manifest identity is `1.0.27.0`.
- [ ] Manifest execution level is `asInvoker` with `uiAccess=false`.
- [ ] Only NAudio is an explicit PackageReference; no redundant ProtectedData package is present.
- [ ] No API key, bearer token, private audio, or transcript is in the tree.
- [ ] Remove `bin`, `obj`, `publish`, and `__pycache__`.
- [ ] Run `python scripts/generate_release_manifests.py --root .` and then `python scripts/generate_release_manifests.py --root . --check`.
- [ ] Run `python scripts/static_verify.py --root .`.
- [ ] Run `python scripts/test_release_verifier.py --root .` and confirm all 52 current release mutations pass.
- [ ] Confirm the DOCX core metadata and embedded-image accessibility title identify v1.0.27.
- [ ] Run/review `python scripts/media_smoke.py` where ffmpeg/ffprobe are available.
- [ ] Search source for `Directory.Delete(..., recursive: true)`; none is allowed.
- [ ] Confirm `SafeFileSystem.cs` is present and covered by static and mutation tests.

## B. Restore and compile

- [ ] Open a normal non-elevated Command Prompt.
- [ ] Run `BUILD.cmd` as the mandatory compiler gate; a passing Python verifier is not a C# build.
- [ ] Restore and Release build complete with zero warnings/errors.
- [ ] No `NU1510`, missing ProtectedData type, WPF temporary-project, or source-generator error appears.
- [ ] Launch target is `bin\Release\net10.0-windows10.0.19041.0\CosmicTranscriber.exe`, not an `obj`/`_wpftmp` file.

## C. Startup, identity, and layout

- [ ] Run `RUN.cmd`; the window appears, activates, and is not hidden/off-screen.
- [ ] Header says `v1.0.27`; title bar says `Cosmic Transcriber 1.0.27`.
- [ ] Resize, maximize, restore, and move the app between monitors at 100%, 125%, 150%, and 200% scaling; no horizontal offset or clipped left edge appears.
- [ ] Launch once from an elevated shell; **Restart normally** reopens unelevated and drag-and-drop from normal Explorer works.
- [ ] Finish a short transcription; the themed completion window appears and **Open output folder** opens the selected folder.
- [ ] About shows 1.0.27, Soh Wei Yu, site, PayPal email, optional wording, and disclaimer.
- [ ] Website opens and PayPal copy works; failures are contained.
- [ ] Cosmic Night/Paper render correctly.
- [ ] Start text is dark/readable when enabled and readable when disabled in both themes.
- [ ] Model and retry selected text is readable in Cosmic Night and Cosmic Paper.
- [ ] Open each selector by mouse, Space/Alt+Down, and keyboard navigation; the popup is visible and every visible item—including the final one—is selectable.
- [ ] The model selector shows exactly four completed-file models and no disabled realtime-only row.
- [ ] Scrolling over a closed model or retry selector does not change its value and still scrolls the surrounding settings page.
- [ ] Selected/disabled controls, queue item, muted labels, status text, and warning banner remain readable.
- [ ] Test normal, maximised, minimum, and small remote-desktop work areas.
- [ ] Test 100%, 125%, 150%, and 200% scaling; the right settings scrollbar preserves access.
- [ ] About fits/clamps and scrolls on a small work area.

## D. Drag-and-drop and Paste

- [ ] App is not elevated; executable/shortcut compatibility does not force administrator mode.
- [ ] Drag an MP3 over queue, settings, list, header, and footer; valid cursor/“Release anywhere…” appears.
- [ ] Drop over each region; the file is queued exactly once.
- [ ] Drop a nested folder; valid MP3s are found in natural order.
- [ ] Drag unsupported/virtual content; it is rejected without crash/dialog spam.
- [ ] Copy files/folders in Explorer and test **Paste** and `Ctrl+V` away from text/key fields.
- [ ] Verify `Ctrl+V` still pastes text normally inside text/key fields.
- [ ] Deliberately launch elevated once; warning/log appears and normal Explorer drag is not falsely claimed to work.
- [ ] Set/clear a test `RUNASADMIN` compatibility flag; source-specific warning behaves correctly.

## E. Import and progress

- [ ] Import a large folder; footer is indeterminate during discovery.
- [ ] Progress becomes determinate during known file inspection.
- [ ] A duplicate path is not added twice.
- [ ] Inaccessible directories/reparse points do not hang or recurse unexpectedly.
- [ ] A file changing during inspection is rejected coherently.
- [ ] Progress/cursor/controls reset after success, no-match, failure, and close cancellation.

## F. Settings autosave and key persistence

- [ ] Change each ordinary option and confirm the autosave card reports success after a short pause.
- [ ] Restart and confirm output, model, language, keywords, prompt, chunk limits, retries, exports, merge title, theme, and price are restored.
- [ ] Enter an invalid keyword or price, change another valid option, restart, and confirm the valid option persisted while the invalid field retained its prior valid value.
- [ ] Confirm the queue, progress, and activity log are not restored.
- [ ] With **Remember securely** off, confirm a session key is not persisted. With it on, confirm the current Windows user can reload the protected key.
- [ ] Deny write access to the settings directory and confirm the autosave card and diagnostics report failure without crashing.


- [ ] Normal Local AppData settings save/load works.
- [ ] Simulate unwritable Local AppData; app selects/logs a writable fallback and still starts.
- [ ] Simulate all candidates unwritable; app starts with defaults and reports persistence limitation.
- [ ] Malformed/oversized/denied settings file falls back without startup crash.
- [ ] API key shortcut focuses the key card.
- [ ] Show/Hide synchronization works; no stale second visible value remains.
- [ ] Test session-only key, environment key, secure save/restart, and delete-saved-copy.
- [ ] DPAPI copy cannot be decrypted by a different Windows user as expected.
- [ ] Malformed/oversized/denied protected file falls back without startup crash.
- [ ] Logs contain no key/bearer value.
- [ ] Test connection restores UI/progress after success, HTTP failure, timeout, cancellation, and close.
- [ ] Test-connection timeout records elapsed time, a client request ID, and nested diagnostics without misreporting application shutdown.
- [ ] A simulated post-header response-body failure preserves any `x-request-id` and HTTP status in the exception/log.

## G. Pricing and API

- [ ] Price field accepts finite nonnegative invariant-decimal input and updates the estimate.
- [ ] Estimate caveat is visible; current official price is manually verified before test spending.
- [ ] All four visible file models are selectable; test the models supported by the account.
- [ ] Automatic retries default to 0 and the duplicate-billing warning is visible.
- [ ] Authentication, rate/billing, unavailable model, proxy/TLS, timeout, opt-in retry, and bounded remote-error paths are exercised.
- [ ] Multipart upload exposes a generic filename, not the local source filename.

## H. Real transcription and output

- [ ] Use a short non-sensitive MP3 and valid key.
- [ ] Footer, overall, and per-file progress move.
- [ ] TXT and Markdown outputs open and contain expected text/timestamps.
- [ ] Existing output is never overwritten; unique names are used.
- [ ] Multi-format failure attempts rollback coherently.
- [ ] Merge order/title and 1,000-file/25,000,000-character bounds behave correctly.
- [ ] Output folder removed/read-only/full mid-run produces a systemic export error.

## I. Long MP3 and checkpoints

- [ ] Test long CBR and VBR files at 5 MB/3 min and 24 MB/5 min.
- [ ] Every upload remains within 24,000,000 audio bytes.
- [ ] Listen/read around boundaries for missing or repeated speech.
- [ ] Cancel after several chunks; temporary chunks clean and checkpoints remain.
- [ ] Resume identical job; valid chunks are reused.
- [ ] Change source/prompt/model/language/chunk/context; incompatible checkpoints are not reused.
- [ ] Corrupt a checkpoint/text digest; it is rejected/regenerated.
- [ ] With context enabled, create a gap; later context-dependent checkpoints are not reused.
- [ ] Two instances cannot unsafely process/clear the same job/cache.
- [ ] Place an unexpected file/folder in the cache root; clear stops before deleting anything.
- [ ] Place a test junction/symlink/reparse point in a disposable cache tree; clear refuses it and does not traverse the target.
- [ ] Queue a source under the selected output cache; start/clear are blocked with a clear message.
- [ ] Test an outside alias that resolves into the cache while the final MP3 name is missing; containment still rejects it.
- [ ] Simulate checkpoint/output flush failure; the run stops systemically before later billable work.

## J. Diagnostics and publish

- [ ] Logs appear beside executable when writable.
- [ ] Read-only executable folder falls back to user log location.
- [ ] Retention keeps the intended recent sessions.
- [ ] In a disposable test location, a log-folder/file junction or symbolic link is rejected and is not followed for append or deletion.
- [ ] `RUN-DIAGNOSTIC.cmd` produces host trace/managed log and preserves exit code.
- [ ] Run `PUBLISH-WIN-X64.cmd`; confirm it opens only the folder and does not launch Cosmic Transcriber.
- [ ] Zip or copy the clean publish folder before first launch when preparing a shareable portable build.
- [ ] Confirm the shareable folder contains no `logs`, `output`, `CosmicTranscriber-settings`, `openai-key.protected`, API key, transcript, or private audio.
- [ ] Copy self-contained output to a clean Windows x64 machine without separately installed .NET.
- [ ] Repeat startup, identity/About, drag/Paste, key test, short transcription, cancel/resume, output, and logs.

## K. Documentation and handoff

- [ ] Comprehensive DOCX is version 1.0.27, rendered, every page visually inspected, and accessibility-audited.
- [ ] README, guides, audit, QA, design, changelog, and preview contain no stale version/count/behavior claims.
- [ ] Run final `python scripts/generate_release_manifests.py --root .`, then `python scripts/generate_release_manifests.py --root . --check`.
- [ ] Run final `python scripts/static_verify.py --root .` for independent manifest and package validation.
- [ ] ZIP root folder is `CosmicTranscriber-1.0.27` and extracts cleanly.
- [ ] Compare extracted tree against `ARTIFACT-INVENTORY.json` and `SHA256SUMS.txt`.
- [ ] Record all Windows tests actually performed and all remaining limitations.

## Diarization acceptance

- [ ] Model list contains `gpt-4o-transcribe-diarize`; `gpt-transcribe` remains selected by default.
- [ ] A short two-speaker MP3 exports at least two labelled turns where the source clearly alternates speakers.
- [ ] TXT and Markdown output retain speaker labels and start/end timestamps.
- [ ] Prompt, keyword, and previous-context controls disable in diarization mode; one language hint remains accepted.
- [ ] A file longer than the chosen chunk duration creates Part-scoped labels and no overlap-duplicated turns.
- [ ] Cancel/resume restores diarized speaker segments from checkpoints without another API call.
- [ ] The editable cost estimate is presented as an approximation, not a guaranteed quote.
