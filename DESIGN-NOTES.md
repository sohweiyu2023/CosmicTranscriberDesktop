# Cosmic Transcriber 1.0.27 — Design Notes

## 1.0.27 selector rendering design

The application does not mix its own foreground resources with the internal colors of WPF's Fluent `ComboBox` template. Both the closed selector and dropdown items explicitly render the control foreground against application-owned surfaces. The popup remains a standard required `PART_Popup`, but is opaque and non-animated because it is hosted in a separate WPF popup window. A direct `ItemsPresenter` inside a bounded `ScrollViewer` preserves keyboard navigation and access to the final item.

Realtime-only models are intentionally absent from the completed-file selector. Unsupported or obsolete saved model IDs are repaired at the settings normalization boundary instead of being represented as disabled UI rows.

## Compile-safe run setup

`Start_Click` snapshots the selected model once, derives `usesSpeakerDiarization` from that snapshot, and uses the same state for request creation and run-start messaging. The local must remain in method scope; using it without a declaration is CS0103 and prevents WPF compilation.

The source verifier contains a targeted regression rule, but it intentionally does not pretend to be Roslyn. The Windows `BUILD.cmd` path is the authoritative compiler gate.

## Model separation

`gpt-transcribe` remains the default for completed files, multiple language hints, structured keywords, prompt/context, and continuation. `gpt-4o-transcribe-diarize` is a specialized optional path using `diarized_json`, `chunking_strategy=auto`, request-scoped speaker labels, and structured speaker timestamps. Unsupported prompt/keyword/context fields are excluded.

## Persistence and privacy boundaries

Ordinary configuration is saved through a 900 ms WPF-dispatcher debounce. Invalid language, keyword, or price input preserves only that field's last valid saved value while unrelated valid settings persist. API keys are never stored in ordinary JSON; optional remembered keys use Windows DPAPI CurrentUser storage.

The queue, progress, activity window, audio, and transcripts are not ordinary settings. Runtime logs, settings, output, transcripts, and protected-key files are excluded from source-control handoff patterns.

## Clean portable release

`PUBLISH-WIN-X64.cmd` deletes and recreates the output, publishes a self-contained single-file build, verifies the EXE, and opens only the directory in File Explorer. It does not launch Cosmic Transcriber. A clean portable ZIP should be made before the first executable launch; any already-run folder must be inspected for private/runtime data.

## Safety invariants retained

- source hash revalidation before billing;
- bounded upload, response, checkpoint, and transcript sizes;
- paid response checkpointed before ordinary cancellation;
- unreadable existing checkpoint treated as systemic, never absent;
- durable same-directory temporary writes plus atomic publication;
- no-follow cache deletion and reparse-point checks;
- generic upload filename rather than local path disclosure;
- zero automatic retries by default because lost responses can make retries duplicate billable work;
- part-scoped anonymous speaker labels across independent diarization requests.

## Evidence model

The current release gate combines source/API/XAML/document/package invariants, 52 deliberate mutations, deterministic decoder-level media evidence, DOCX render/accessibility review, deterministic manifest generation and independent inventory/checksum validation, and clean extraction. Native Windows compilation and runtime acceptance remain separate mandatory evidence.

## Deterministic manifest design

`ARTIFACT-INVENTORY.json` lists every package file except the two manifest files. `SHA256SUMS.txt` then hashes the inventory itself plus every inventoried artifact, while excluding only itself. `generate_release_manifests.py` writes this deterministic non-circular representation; `static_verify.py` calculates the expected representation independently so a generator defect cannot validate itself.

## 1.0.27 request-diagnostic design

Network diagnostics preserve two different identities: the locally generated `X-Client-Request-Id` exists even when no response arrives, while `x-request-id` exists only after OpenAI returns response headers. Elapsed timing begins before request construction/upload and therefore represents the full attempt as experienced by the app. Bounded nested exception text is user-diagnostic data and remains subject to central log redaction.

Connection-test timeout is implemented inside the client with a linked token so timeout can be classified without converting application shutdown into an error.
