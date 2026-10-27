> **1.0.27 supersession:** `gpt-live-transcribe` remains a valid realtime model, but it is no longer displayed as a disabled row in Cosmic Transcriber's completed-file selector. This avoids presenting an intentionally unclickable final item.

# Cosmic Transcriber 1.0.21 — GPT Transcription Model Update

Date: 2026-07-29

## Implemented

- Added `gpt-transcribe` to the model catalogue and made it the default for queued MP3/file transcription.
- Retained `gpt-4o-transcribe` and `gpt-4o-mini-transcribe` for compatibility.
- Added `gpt-live-transcribe` to the catalogue and UI as a clearly disabled realtime-only option. The multipart file client rejects it as defense in depth.
- Added model-specific language request behavior: `languages[]` for `gpt-transcribe`, legacy `language` for the GPT-4o transcription models.
- Added editable price presets: US$0.0045/min, US$0.006/min, US$0.003/min, and US$0.017/min respectively.
- Added a one-time schema migration for an untouched previous `gpt-4o-transcribe` + US$0.006 default pair. Intentional custom/legacy selections remain unchanged.
- Updated the UI preview, user/developer/troubleshooting documentation, comprehensive DOCX manual, release metadata, and package evidence.

## Defensive review findings

- A settings property initialized directly to schema 2 would have prevented old JSON files (where the property is absent) from entering migration. The initializer was removed; normalization writes schema 2 after migration.
- Sending the legacy singular `language` field to `gpt-transcribe` would not follow the new API contract. The client now chooses the field by model.
- A realtime model must not be passed to `POST /v1/audio/transcriptions`. Both the UI and client enforce that boundary.

## Verification

- Static gate: PASS — 27 C# files, 6 XAML files, 34 event handlers, and 52 resource references.
- Adversarial mutation suite: PASS — 118/118 cases across bounded isolated range runs.
- DOCX manual: rendered to 18 PNG pages; every page inspected. The final numeric-only update changed pages 15–17; all other rendered page hashes remained identical and the changed pages were re-inspected.
- UI preview: regenerated from the updated HTML at 1500×920 and visually inspected.
- ZIP/package integrity and deterministic manifests are generated after the final source checks.

## Residual acceptance boundary

This Linux environment has no Windows .NET 10 WPF compiler/runtime. `BUILD.cmd` and a short real API transcription on the target Windows PC remain mandatory before calling the release production-ready.
