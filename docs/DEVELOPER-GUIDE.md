# Cosmic Transcriber 1.0.27 - Developer Guide

## 1.0.27 UI/model-normalization audit additions

Release 1.0.27 uses an explicit opaque `ComboBox`/`ComboBoxItem` template rather than mixing application foreground resources with the platform Fluent template's internal surfaces. The template retains `PART_Popup`, `AllowsTransparency="False"`, `PopupAnimation="None"`, and a direct `ItemsPresenter` inside a bounded `ScrollViewer`. Both selected content and item content bind `TextElement.Foreground` from the templated control.

The completed-file selector contains exactly the four IDs centralized by `TranscriptionModelCatalog.FileModels`. Realtime-only models are absent rather than disabled. `AppSettingsService` schema 4 normalizes an unsupported saved ID to `gpt-transcribe` and resets its paired price estimate. The release mutation gate now contains 52 isolated mutations, including template, contrast, model-list, and settings-sanitization regressions.

## 1.0.25 compile-regression and release pipeline

`MainWindow.Start_Click` derives `usesSpeakerDiarization` from the selected model before constructing and logging the run. This local is required because the run-start message uses it outside the request initializer; omitting it is a deterministic CS0103 compile error. `static_verify.py` now protects the declaration and ordering, and `test_release_verifier.py` deliberately removes it.

The Python verifier remains a structural source gate, not a Roslyn compiler. `BUILD.cmd` on Windows is the mandatory compiler gate. `PUBLISH-WIN-X64.cmd` opens only the completed publish directory, never the executable, so a release operator can archive a clean portable folder before runtime artifacts exist.

## 1.0.25 settings-persistence pipeline

`MainWindow` registers setting-control handlers only after startup values and the API-key state have been applied. A `DispatcherTimer` debounces changes for 900 ms, keeping capture and write operations serialized on the UI dispatcher. Explicit run-start and final-close persistence stops the pending timer first.

`CaptureSettingsForPersistence` captures fields independently. Invalid language, keyword, or price input leaves the prior valid value intact while unrelated options continue to save. `AppSettingsService.Save` normalizes and serializes settings, then `SafeFileSystem.WriteAllBytesDurableAtomic` writes through a unique temporary file, flushes it, and moves it over the destination. Autosave suppresses repetitive success diagnostics but records failures.

The API key remains outside `AppSettings`: `ApiKeyStore` uses Windows DPAPI only when secure remembering is enabled. Queue items, progress, activity logs, transcripts, and checkpoints are separate state domains.

## 1.0.25 diarization pipeline

`TranscriptionModelCatalog.DiarizeModel` selects the specialist endpoint contract. `OpenAiTranscriptionClient` sends `response_format=diarized_json` and `chunking_strategy=auto`, rejects unsupported prompt/keyword input, and validates a bounded segment array containing `speaker`, `start`, `end`, and `text`.

`TranscriptionCoordinator` keeps the normal duration/size ceiling but disables MP3 preroll in diarization mode. It offsets request-local segment times into file-global times, scopes labels by Part when more than one request is needed, includes speaker segments in checkpoint integrity hashes, and persists them using checkpoint schema `1.11`.

`TranscriptOutputWriter` emits speaker turns in TXT and Markdown. Anonymous labels are intentionally request-scoped. Known-speaker references are not part of 1.0.27.

## 1.0.25 UI and shell integration changes

The main window no longer wraps its entire layout in a horizontal `ScrollViewer`; this prevents retained horizontal offsets after maximize/resize. The manifest declares `PerMonitorV2,PerMonitor` DPI awareness. Elevated launches expose a one-click normal restart path through `WindowsSecurity`, and end-of-run reporting is implemented by `CompletionWindow.xaml`.

## GPT Transcribe keyword contract

`InputValidation.NormalizeKeywords` is the single normalization boundary. `MainWindow` preflights and persists the list, `OpenAiTranscriptionClient` repeats `keywords[]`, and `TranscriptionCoordinator` includes the normalized list in fingerprint and manifest data. Any new caller must preserve all three layers.

## 1. Solution structure

- `App.xaml(.cs)`: resource startup, global exception boundaries, theme application.
- `MainWindow.xaml(.cs)`: WPF presentation, queue/import, settings/key UX, orchestration, progress, cancellation.
- `AboutWindow.xaml(.cs)`: version, creator/site/support content, work-area fit.
- `Models/`: persisted settings, queue items, transcription records.
- `Services/`: settings/key persistence, OpenAI HTTP, MP3 inspection/chunking, checkpoints, coordination, output.
- `Utilities/`: version, bounded reads, validation, formatting, diagnostics, Windows elevation/compatibility checks.
- `Themes/`: semantic palettes and shared WPF templates.
- `scripts/`: static verifier, mutation runner, reproducible media smoke.

## 2. Build contract

The project targets `net10.0-windows10.0.19041.0`, enables WPF and Windows targeting, uses nullable analysis, disables implicit usings, treats warnings as errors, and requests deterministic output. `global.json` requests .NET SDK `10.0.302` with `latestFeature` roll-forward inside .NET 10.

Explicit package references:

- NAudio 2.3.0.

Do not add a direct `System.Security.Cryptography.ProtectedData` package for this target. DPAPI is supplied by the .NET 10 Windows framework, and a redundant framework-provided package can be pruned/reported as `NU1510`; warnings-as-errors would fail the build.

## 3. Versioning contract

Release 1.0.27 is represented as:

- `Version`: `1.0.27`;
- `AssemblyVersion`: `1.0.27.0`;
- `FileVersion`: `1.0.27.0`;
- `InformationalVersion`: `1.0.27`;
- manifest identity: `1.0.27.0`;
- `IncludeSourceRevisionInInformationalVersion`: `false`.

`AppVersion.DisplayVersion` is the runtime display source for header/title/About/log/User-Agent. Update every version-bearing script/document and run the verifier before release.

## 4. Startup and exception boundaries

`StartupDiagnostics.Initialize` runs before WPF resources. `App` registers dispatcher, AppDomain, and unobserved-task handlers. Constructor/activation failures are logged and shown without claiming normal startup.

Keep `async void` restricted to WPF event handlers. Every async event must contain its own error/cancellation boundary or delegate into a method that does. Closing uses lifetime cancellation and skips control-dependent cleanup when construction did not finish.

## 5. Settings and key persistence

`AppSettingsService` selects storage only after a real writability probe. Candidate order is Local AppData, user temp, then an executable-folder subdirectory. `settings.json` is bounded to 1 MiB before strict UTF-8/JSON parsing. Normalization clamps paths, prompt, language, retries, chunk limits, title, theme, and finite nonnegative price.

`ApiKeyStore` stores only a DPAPI-protected blob using `CurrentUser`. Settings never contain a key. Protected reads are bounded. Plain/protected byte arrays are zeroed. Save uses a unique temp file and replacement. Primary deletion is authoritative; temp cleanup is best-effort and tolerates IO, access, and security-policy failures.

Never log key contents, authorization headers, raw remote bodies, or unbounded exception data.

## 6. Drag-and-drop architecture

The Window declares `AllowDrop` and directly wires preview enter/over/leave/drop handlers in XAML. Major hit-test surfaces also set `AllowDrop` to avoid native-control target inconsistencies.

`CanAcceptFileDrop` requires idle state, Copy permission, and a `DataFormats.FileDrop` list. The preview handler always sets `Effects` and `Handled`. Drop and Paste both call `AddInputPathsAsync`; do not create a second import path with different bounds or validation.

The manifest must remain `asInvoker`. `WindowsSecurity` detects an elevated token and common AppCompat `RUNASADMIN` flags. Never solve cross-integrity drag by weakening UIPI. Preserve picker and clipboard fallbacks.

## 7. Import and UI state

The queue is capped at 10,000. Expansion uses `EnumerationOptions` with recursion, inaccessible-entry ignore, and reparse-point skip. Candidate processing is cancellation-aware and bounded before full inspection.

Busy state is represented by `_isRunning`, `_isImporting`, `_isTestingKey`, and `_isClosing`; `UpdateInteractiveState` is the single control-enablement authority. New operations must restore cursor, progress, and state in `finally`.

## 8. OpenAI client

Maintain these invariants:

- bounded 24,000,000-byte audio payload;
- bounded 4 MiB response;
- no automatic redirects/cookies;
- finite timeout linked to caller cancellation;
- generic multipart filename;
- strict UTF-8 and JSON shape handling;
- bounded/single-line remote error display;
- never automatically replay an audio POST, including transient transport/status conditions;
- clear distinction between file-specific and systemic failures.

The key test is operational model-access validation. Pricing is not fetched by the client.

The file client supports `gpt-transcribe`, `gpt-4o-transcribe`, and `gpt-4o-mini-transcribe` on `POST /v1/audio/transcriptions`. Realtime-only models are not exposed by the completed-file selector, and the client still rejects any unsupported model defense-in-depth. `gpt-transcribe` receives optional language guidance through repeated `languages[]` form fields; the single-language GPT-4o transcription models retain the singular `language` field. Keep this model-specific protocol split covered by the static gate.


Automatic paid retries are disabled. The legacy0-5 setting remains in profiles for compatibility but is ignored by the client; the UI selector is disabled. Any post-dispatch timeout, body-read or HTTP failure may be ambiguous. Preserve request correlation and keep user cancellation distinct from internal timeout; see LOGGING-SAFE-ERROR-AUDIT.md.

## 9. MP3 inspection and chunking

Inspection holds one read stream while hashing and measuring duration, then verifies length and last-write time. Chunking reads frame boundaries, handles ID3v2, excludes metadata-carrier duration from logical speech, retains/rewrites the carrier for chunk one, observes byte/time ceilings, and includes contiguous speech overlap for later chunks.

Temporary chunk creation/deletion must remain contained. Do not estimate duration solely from bitrate or container metadata.

## 10. Checkpoints, locks, and filesystem links

The checkpoint schema is `cosmic-transcriber-checkpoint-1.11`. Identity includes source SHA-256 and relevant request/chunk settings. Transcript text is digest-verified. Previous-context reuse requires an uninterrupted valid prefix.

Cross-process usage and per-job work locks protect clearing/processing races. Bounded checkpoint reads and atomic writes are mandatory. Preserve timeout/cancellation behavior when changing lock acquisition.

Treat every application-owned directory below a user-writable output folder as adversarial. `SafeFileSystem` rejects reparse points, validates a tree before deletion, rechecks immediately before each action, and removes directories only with `recursive: false`. Cache clear must reject unknown root items before any deletion. Do not reintroduce `Directory.Delete(..., recursive: true)` anywhere in application cleanup.

Completed-response checkpoint writes use `CreateNew`, `WriteThrough`, `FlushAsync`, `Flush(flushToDisk: true)`, destination-link rechecks, and atomic replacement. The separate 30-second storage token remains deliberate: ordinary run cancellation must not discard a response already returned by the API. Source files under the selected cache root must remain blocked.

## 11. Output

`TranscriptOutputWriter` creates unique `CreateNew` temp files, uses `WriteThrough` and `Flush(flushToDisk: true)`, then atomically moves to the first unclaimed final name. Multi-format output tracks created files and attempts rollback after a later failure. Directory creation and output failures are wrapped as systemic `TranscriptExportException`.

Merge bounds are 1,000 files and 25,000,000 transcript characters. Preserve Windows-safe filename normalization and path containment.

## 12. WPF styling and accessibility

Shared templates must bind content foreground/font weight from the templated control. The ComboBox invariant is stricter: keep the explicit selected-value and item foreground bindings, opaque/non-animated `PART_Popup`, application-owned background, and direct scrollable `ItemsPresenter`. Do not return to a transparent popup or to a platform template combined with app-owned foreground resources. Do not put a local foreground on Start; it overrides disabled style triggers. Keep explicit disabled colors without global opacity reduction.

Use dynamic semantic resources. New interactive controls need accessible names when their visible content is ambiguous, and changing status text should use polite live regions. Test both themes, keyboard focus, minimum sizes, and high DPI on Windows.

## 13. Diagnostics

Preferred log path order is executable-adjacent, Local AppData, then temp. Creation, append, and retention must reject reparse points. Retain bounded recent logs. Use `StartupDiagnostics.WriteException` for contained failures and `InputValidation.SingleLineForLog` for user/remote messages.

`RUN-DIAGNOSTIC.cmd` enables host tracing for pre-managed failures. Keep batch scripts ASCII/CRLF, label-driven, and free of fragile multiline parenthesised branches around expandable values.

## 14. Verification workflow

From the source root:

```text
python scripts/generate_release_manifests.py --root .
python scripts/generate_release_manifests.py --root . --check
python scripts/static_verify.py --root .
python scripts/test_release_verifier.py --root .
python scripts/media_smoke.py
python scripts/generate_release_manifests.py --root .
python scripts/static_verify.py --root .
```

`generate_release_manifests.py` deterministically writes `ARTIFACT-INVENTORY.json` and `SHA256SUMS.txt`; `static_verify.py` independently validates both files. The 52-case current release mutation suite must pass. The suite includes dedicated cases for the gpt-transcribe default, multi-value languages[] emission, ISO-639-3/regional-Chinese validation, legacy singular-language isolation, case-insensitive price lookup, realtime/file separation, current price presets, settings migration, CS1628 out-parameter capture, ASCII-only cleanup names, retry defaults, alias containment, diagnostic durability, responsive layout, Per-Monitor-V2, Explorer-shell relaunch, completion-dialog behavior, DOCX provenance, visible verification evidence, and deterministic manifest completeness, native ComboBox popup retention, wheel-selection suppression with page-scroll forwarding, request-ID logging, proxy authentication, and model-specific language validation, elapsed request timing, post-header server request-ID preservation, nested socket diagnostics, and connection-test timeout classification. Remove `bin`, `obj`, `publish`, and `__pycache__` before packaging.

For the DOCX manual, render with the repository's document-tool workflow, inspect every page PNG, and run the accessibility audit.

These gates are not a Windows compiler/runtime. Complete `docs/RELEASE-CHECKLIST.md` on Windows.

## 15. Change checklist

- Keep warnings-as-errors/nullable clean.
- Bound external/local input before allocating or deserializing.
- Preserve cancellation and `finally` restoration.
- Deliberately classify file-specific/systemic failures.
- Keep secrets out of settings/logs/source.
- Keep all final output creation atomic and non-overwriting.
- Resolve every new XAML handler/resource exactly once.
- Add verifier coverage and a mutation for high-risk invariants.
- Update all version-bearing docs/artifacts.
- Build, launch, drag, paste, key-test, transcribe, cancel/resume, publish, and high-DPI-test on Windows.
