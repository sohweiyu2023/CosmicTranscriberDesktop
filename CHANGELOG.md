# Change Log

## 1.0.27 - dropdown contrast, model-list clarity, and settings hardening

- Replaced the mixed built-in Fluent `ComboBox` rendering path with a complete application-themed template that explicitly renders selected text against `SurfaceElevatedBrush`.
- Kept the required WPF `PART_Popup`, made the popup opaque with `AllowsTransparency="False"`, disabled popup animation, and retained a bounded scrolling `ItemsPresenter` so every item remains reachable.
- Added a complete `ComboBoxItem` template with explicit foreground binding for consistent dark/light theme rendering.
- Removed the disabled `gpt-live-transcribe` row from the completed-file selector; every visible model is now selectable and valid for file transcription.
- Centralized the four supported file model IDs and normalized stale/unknown saved model IDs to `gpt-transcribe` with a matching editable price estimate.
- Increased settings schema to 4 for the stricter model normalization boundary.
- Expanded Cosmic Paper contrast hardening across normal-sized status/muted text and the progress/control accent; text remains at least 4.5:1, control graphics at least 3:1, and primary-button text at least 4.5:1.
- Expanded the adversarial release suite from 44 to 52 mutations and added direct verifier checks for the selector template, popup safety, selected/item foregrounds, model-list clarity, saved-model sanitization, and light-theme contrast.
- Fixed a second-order wheel-input regression: closed selectors still cannot change silently, but their wheel gesture is now forwarded to the surrounding settings page instead of trapping scrolling.
- Re-ran source, XAML, API, network, diarization, checkpoint, export, settings, security, package, documentation, and clean-extraction review.

## 1.0.26 - independent dropdown/network re-audit and diagnostic hardening

- Gave the repaired dropdown/network build a distinct version identity so logs and binaries cannot be confused with the faulty 1.0.25 build.
- Preserved OpenAI server request IDs and HTTP status codes when a response begins but its body fails or times out.
- Added elapsed request timing and a bounded nested exception chain, including socket error codes, to network diagnostics.
- Added elapsed timing to retry activity messages and HTTP diagnostic records.
- Parallelized isolated release mutations with a bounded `--jobs` option, avoiding the previous full-suite runtime timeout.
- Expanded the adversarial release gate from 40 to 44 mutations for elapsed timing, post-header request-ID preservation, and nested network diagnostics, and connection-test timeout classification.
- Strengthened the verifier after an independent first mutation run exposed three blind spots: server request-ID response logging, nested exception propagation, and connection-test timeout classification are now checked at their actual call sites.
- Re-ran API-contract, UI/dropdown, checkpoint, export, package-hygiene, media, documentation, and deterministic-manifest review.

## 1.0.25 - compile-blocker repair and release-gate hardening

### Dropdown and network-diagnostics corrective audit (30 July 2026)

- Removes the hand-built transparent `ComboBox` popup template that could accept wheel/keyboard selection while rendering no visible drop-down on some Windows graphics, DPI, remote-session, or theme paths. The controls now use WPF's platform template.
- Suppresses mouse-wheel value changes while the model and retry drop-downs are closed, preventing accidental model or billable-retry changes during page scrolling.
- Replaces generic network-failure reporting with DNS, connection, TLS/certificate, proxy-tunnel/authentication, HTTP-protocol, early-response-end, configuration-limit, timeout, and unknown classifications.
- Adds a unique `X-Client-Request-Id` to each OpenAI request, logs returned `x-request-id` values when a response exists, and preserves the full exception/inner-exception chain in the redacted diagnostic log.
- Enables current-user credentials for an authenticated system proxy and applies bounded connection/pooling/header safeguards.
- Corrects model-specific language validation: `gpt-transcribe` retains documented ISO 639-1, selected ISO 639-3, and regional-Chinese hints; single-language file models retain one ISO 639-1 hint.
- Expands the current release mutation gate from 32 to 40 cases for the repaired UI, network, proxy, request-ID, and language paths.
- Confirms that the uploaded source ZIP contained no runtime `.log` file; the exact cause of the user's earlier network failure therefore cannot be reconstructed from that archive.

- Fixes a release-blocking C# CS0103 error in `MainWindow.Start_Click`: `usesSpeakerDiarization` was referenced in the run-start log without a declaration in that method scope.
- Adds a targeted structural regression check and mutation that removes the declaration, preventing the same source defect from escaping the release gate again.
- Clarifies that the Python verifier is a source-structure gate rather than a C# compiler; a successful Windows `BUILD.cmd` run remains mandatory before distributing binaries.
- Clarifies that `PUBLISH-WIN-X64.cmd` opens only the clean publish folder and does not launch the app. Users preparing a portable release are instructed to zip/copy it before first launch.
- Expands `.gitignore` coverage for runtime logs, settings, protected-key files, and generated transcripts.
- Adds a deterministic release-manifest generator and independent exact-file/hash validation; repairs stale inherited inventory/checksum evidence.
- Rechecks OpenAI file-transcription/diarization contracts, autosave, API-key handling, checkpoint safety, output publication, WPF bindings/resources, package hygiene, and documentation.

## 1.0.24 - debounced settings autosave and full documentation re-audit

- Adds a 900 ms dispatcher-based autosave for all ordinary user-configurable options.
- Adds a visible status card for pending, successful, partially valid, and failed saves.
- Preserves the last valid language, keyword, or price value when the corresponding live field is invalid, while still saving unrelated valid settings.
- Keeps the queue, progress, activity log, and session-only API key outside ordinary settings persistence.
- Continues to store an opted-in API key separately with current-user Windows DPAPI rather than in `settings.json`.
- Stops pending autosave before explicit run-start and final-close persistence so writes remain serialized on the UI dispatcher.
- Repairs stale v1.0.22/v1.0.23 README, manual, checklist, verifier, preview, inventory, and release lineage.
- Re-runs the complete API, diarization, checkpoint, export, filesystem, UI, documentation, package, and mutation audit.

## 1.0.23 - speaker-labelled transcription

- Adds `gpt-4o-transcribe-diarize` as an optional file-transcription mode while retaining `gpt-transcribe` as the default.
- Requests `diarized_json` with automatic server-side chunking and parses speaker/start/end/text segments.
- Exports speaker-labelled TXT and Markdown and preserves structured segments in integrity-protected checkpoints.
- Disables unsupported prompt, keyword, and previous-context transmission in diarization mode.
- Uses bounded client chunks with no overlap and scopes anonymous labels to each independent Part/request.
- Adds diarization-specific verification and mutation coverage.

## 1.0.22 - responsive layout, drag-and-drop recovery, and completion dialog

- Removes the outer horizontal `ScrollViewer` that could preserve a nonzero offset and shift/clamp the interface after resize or maximize.
- Reduces the default window footprint, rebalances the two-column minimums, enables layout rounding, and declares Per-Monitor-V2 DPI awareness.
- Adds a one-click **Restart normally** action for elevated launches. It clears only the current-user `RUNASADMIN` token when present and delegates relaunch to the normal Windows Explorer shell.
- Keeps the Paste fallback and explicit manual guidance for machine-wide compatibility rules that the app will not alter automatically.
- Replaces the plain end-of-run message box with a themed completion window showing run counts, elapsed time, output path, and an **Open output folder** button.
- Treats skipped items as a completed-with-issues outcome rather than a clean completion.
- Expands the mutation suite from 118 to 125 cases, including a DOCX visible-evidence regression.

## 1.0.21 - structured keyword context and round-three audit

- Adds dedicated one-term-per-line `keywords[]` support for `gpt-transcribe`.
- Separates literal terminology from general prompt/context guidance in the UI.
- Bounds, deduplicates, and preflights keyword values; rejects API-forbidden angle brackets and control characters.
- Rejects populated structured keywords for legacy transcription models before upload while keeping the field editable so persisted values can be cleared.
- Persists keyword guidance under settings schema 3.
- Includes normalized keyword guidance in checkpoint identity and manifests.
- Expands the adversarial gate from 112 to 118 cases and refreshes source, preview, manual, audit, inventory, and checksums.

## 1.0.20 - independent API-contract and reliability re-audit

- Repairs incomplete `gpt-transcribe` language support: the UI, settings, checkpoint identity, manifest, and multipart request now preserve multiple comma-separated hints.
- Language hints are bounded, deduplicated, and canonicalized. The current artifact additionally accepts documented selected ISO 639-3 and regional-Chinese hints for `gpt-transcribe`, while single-language models retain ISO 639-1 validation.
- Sends one `languages[]` form part per hint for `gpt-transcribe`; single-language GPT-4o transcription models reject multiple hints before any network request and retain the singular `language` field.
- Makes model price presets case-insensitive so differently cased persisted identifiers cannot fall through to the wrong estimate.
- Expands the adversarial gate to 112 deliberate mutations, including multi-language truncation, legacy guard removal, regional-Chinese removal, and price-case regressions.
- Corrects inherited audit evidence that incorrectly described the latest artifact as a fresh review of v1.0.17.
- Refreshes release identity, user/developer guidance, UI preview, comprehensive manual, inventory, checksums, and QA evidence.

## 1.0.19 - new GPT transcription models and corrective hardening

### Earlier 1.0.19 corrective work

- Preserves a resolved symbolic-link/junction prefix when the final path component does not yet exist, closing a cache-containment bypass for missing trailing files.
- Changes the `TranscriptionRequest` retry default from three to zero.
- Changes the UI retry parse-failure fallback from three to zero, matching the settings default and visible safest-default warning.
- Makes diagnostic-log folder creation and retention cleanup reject reparse points before use.
- Keeps the unique diagnostic `CreateNew` file handle open for all writes, eliminating the check-then-reopen pathname swap window.
- Corrects stale DOCX core metadata and embedded-image accessibility title, and adds a package-level provenance check.
- Adds six targeted verifier mutations for those regressions, increasing the suite from 95 to 101 cases.
- Post-release corrective review fixes the `ValidateRun` CS1628 compiler failure, tightens internal cleanup names to ASCII digits, and adds two regression mutations (103 total).
- Refreshes the manual, preview, QA/audit evidence, inventory, checksums, and release package for v1.0.20.

## Earlier releases

See prior packaged releases.
