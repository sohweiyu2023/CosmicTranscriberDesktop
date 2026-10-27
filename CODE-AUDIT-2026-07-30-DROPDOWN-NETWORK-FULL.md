# Cosmic Transcriber 1.0.25 — Dropdown, Network, and Full Corrective Audit

**Audit date:** 30 July 2026  
**Input artifact:** `CosmicTranscriber-1.0.25-compile-full-audit-reviewed(1).zip`  
**Result:** repaired source package, expanded release gate, and refreshed deterministic manifests

## Executive result

The reported invisible drop-down is a confirmed UI implementation defect. Both affected controls used a hand-built WPF `ComboBox` template whose `PART_Popup` was transparent and animated. Selection state remained functional, which explains why the mouse wheel could move to another item even though no drop-down window was visible. The custom template has been removed; WPF now supplies its platform/theme template, and closed model/retry controls ignore wheel input.

The reported network error is a separate issue. The uploaded ZIP contains source and documentation but no runtime `.log` file. The exact earlier DNS, TLS, proxy, socket, timeout, or HTTP failure therefore cannot be reconstructed from this archive. The old network path preserved too little diagnostic context. The client now classifies network failures, records full inner exceptions in the diagnostic log, attaches a unique client request ID, records OpenAI's server request ID when present, and supports credentials for an authenticated system proxy.

## Repairs implemented

### 1. Drop-down visibility and accidental selection

- Removed the custom transparent/animated `ComboBox` control template from `Themes/SharedStyles.xaml`.
- Retained only safe visual property setters so WPF's native themed `ComboBox` template owns `PART_Popup`, placement, keyboard handling, and accessibility behavior.
- Added `PreviewMouseWheel` guards to the model and automatic-retry controls.
- A closed drop-down no longer changes silently while the user scrolls the settings page; wheel navigation remains available after the drop-down is opened.
- Added verifier and mutation coverage that rejects reintroduction of a custom `PART_Popup`, removal of either wheel guard, or reversal of the closed-state condition.

### 2. Network diagnosis and reliability

- Added a unique `X-Client-Request-Id` to connection tests and transcription uploads.
- Logs returned `x-request-id`, HTTP status, and OpenAI processing time when a response exists.
- Classifies name-resolution, connection, TLS/certificate, protocol, proxy-tunnel, network-authentication, version-negotiation, invalid-response, prematurely-ended-response, configuration-limit, timeout, and unknown failures.
- Preserves the full exception and inner-exception chain in `StartupDiagnostics`; the UI receives a bounded actionable message without exposing the API key.
- Converts response-stream `IOException` failures into bounded network retry handling while leaving local audio-file I/O failures file-specific.
- Enables current-user credentials for the configured/default system proxy.
- Adds decompression, connection timeout, pool lifetime/idle timeout, response-header limit, redirect disablement, and cookie disablement.
- Timeout messaging warns that a lost response can make a retry duplicate already accepted billable work. The safest default remains zero automatic retries.

### 3. Language-contract correction

An interim audit idea would have restricted every model to two-letter ISO 639-1 hints. Current OpenAI transcription guidance is model-specific, so that change was rejected. The final repair:

- keeps `gpt-transcribe` support for ISO 639-1, documented selected ISO 639-3 codes, and `zh-cn` / `zh-tw` / `zh-hk`;
- keeps multiple `languages[]` fields only for `gpt-transcribe`;
- requires a single two-letter ISO 639-1 `language` value for single-language file models, including diarization;
- normalizes case, removes duplicates, bounds count/length, and fails before upload on invalid combinations.

## Wider adversarial audit

The pass also rechecked:

- model/file-upload eligibility and realtime-model rejection;
- diarized response format, automatic server VAD, segment parsing, timestamp validation, label scoping, exports, and checkpoint integrity;
- upload-size and duration ceilings, no-overlap diarization splitting, retry cost behavior, and response-size/UTF-8 validation;
- API-key normalization, DPAPI separation, diagnostic redaction, runtime-file ignore rules, and shareable-package hygiene;
- path containment, reparse-point defenses, atomic settings/output/checkpoint publication, cleanup boundaries, and deterministic manifests;
- XAML parseability, named-event handlers, static resources, accessibility names, version identity, clean publish behavior, and documentation consistency.

No embedded OpenAI key, runtime log, protected key file, transcript, audio file, `bin`, `obj`, or publish output was found in the uploaded source archive.

## Verification performed

- Python source verifier and manifest validator.
- All XAML files parsed as XML.
- Event-handler and resource-reference cross-checks.
- C# delimiter/preprocessor sanity checks.
- Python script byte-compilation.
- Secret/runtime-artifact/package-hygiene scans.
- Deterministic inventory and SHA-256 regeneration.
- Forty independent release mutations, including the new drop-down, wheel, request-ID, proxy, network-classification, and language-contract regressions.
- Clean ZIP integrity and clean-extraction re-verification.

## Verification boundary

This Linux audit container does not provide the Windows Desktop SDK/WPF toolchain, so it cannot execute the native Windows build or render the control on the user's exact graphics/DPI path. Before distributing binaries, run `BUILD.cmd` on Windows with the SDK pinned by `global.json`, then manually verify both drop-downs at the user's normal display scaling and run one real connection test. Source-level and adversarial gates are not a substitute for that native build/runtime check.

## Files materially changed

- `src/CosmicTranscriber/Themes/SharedStyles.xaml`
- `src/CosmicTranscriber/MainWindow.xaml`
- `src/CosmicTranscriber/MainWindow.xaml.cs`
- `src/CosmicTranscriber/Services/OpenAiTranscriptionClient.cs`
- `src/CosmicTranscriber/Utilities/InputValidation.cs`
- `scripts/static_verify.py`
- `scripts/test_release_verifier.py`
- `docs/DEVELOPER-GUIDE.md`
- `docs/RELEASE-CHECKLIST.md`
- `docs/TROUBLESHOOTING.md`
- `CHANGELOG.md`
- release manifests and this audit report
