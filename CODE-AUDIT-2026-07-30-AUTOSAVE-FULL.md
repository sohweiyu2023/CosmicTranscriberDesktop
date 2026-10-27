# Cosmic Transcriber 1.0.24 — Autosave and Full-Application Audit

**Date:** 2026-07-30  
**Input:** Cosmic Transcriber 1.0.23 diarization-reviewed source  
**Output:** Cosmic Transcriber 1.0.24 autosave/full-audit reviewed source

## Direct answer: what is saved?

In 1.0.23, ordinary configuration was not saved after every edit. It was captured at run start, after a run, and during a normal close. A crash or forced process termination before those points could lose recent changes.

In 1.0.24, valid ordinary configuration autosaves after about 900 milliseconds without another edit. The visible Settings autosave card reports pending, saved, partial, or failed state.

Autosaved ordinary settings:

- output folder and export formats;
- selected model;
- language hints, keyword hints, and prompt/recording context;
- chunk size and maximum duration;
- retries, previous-context, continue-on-error;
- merge preference and combined title;
- theme and editable price estimate;
- the preference to remember the API key securely.

Deliberately not stored as ordinary settings:

- the audio queue;
- current progress/status and activity-log text;
- a session-only API key;
- audio or transcript content.

The actual API key remains separate from `settings.json`. It is persisted only through the secure-key workflow when the user opts in, using a Windows current-user DPAPI-protected file. Checkpoints are also separate and are written after successful transcription chunks.

## Findings repaired

### Medium — lifecycle-only settings persistence

Recent edits could be lost after a crash/kill. Added a debounced WPF `DispatcherTimer` on the UI dispatcher and a visible autosave status card. Pending timers are stopped before explicit run-start and final-close persistence so settings writes remain serialized.

### Low — invalid-field save coupling

One invalid language list, keyword list, or price value could abort the whole capture and prevent unrelated valid settings from saving. Capture is now field-isolated: valid options persist, and only the invalid field retains its last valid stored value.

### Low — stale current documentation and release identity

The inherited current README, reports, manual, preview, checklist, and verifier identity still contained 1.0.22/1.0.23 current-release residue. All current materials now identify 1.0.24 and explain autosave, persistence boundaries, and speaker-labelled mode.

### Low — obsolete verifier scripts

Historical and experimental verifier copies no longer matched the current source and could mislead users with irrelevant failures. They were removed. The current gates are `scripts/static_verify.py` and `scripts/test_release_verifier.py`.

## Review scope

The pass independently re-inspected model/API contracts, diarized response parsing, response bounds, retries, language/keyword/prompt isolation, speaker segments, checkpoints, MP3 frame parsing/chunking, source revalidation, cancellation, output/merge publication, settings/key storage, path/reparse protections, logs/redaction, WPF wiring/resources/layout/themes, drag/drop recovery, completion UI, build/publish scripts, documentation, and package hygiene.

## Final evidence

- 28 C# files inspected.
- 7 XAML files parsed.
- 38 unique XAML handlers resolved across 42 bindings.
- 52 unique referenced resources resolved.
- 28/28 release-specific deliberate mutations detected.
- Deterministic 680-second CBR and VBR media evidence passed; all six generated chunks decoded with FFmpeg.
- 18-page comprehensive DOCX rendered and inspected page by page.
- DOCX accessibility audit: zero high, medium, or low findings.
- Source/package secret and compiled-residue scans passed.
- Clean extraction, archive CRC, deterministic inventory, and checksums passed.

## Residual limitation

The audit environment does not contain the Windows .NET 10/WPF SDK/runtime and cannot execute DPAPI, Explorer/OLE drag-and-drop, monitor-DPI transitions, or a paid OpenAI request. `BUILD.cmd` and the Windows acceptance checklist remain mandatory before describing compiled binaries as production-verified.
