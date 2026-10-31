# Cosmic Transcriber 1.0.27

This is the reviewed **source-only** Desktop repository. It is separate from CosmicTranscriberWeb and CloudScribe-Pro. No application binaries, runtime/SDK DLLs, profiles, API credentials, user recordings or transcripts are published here. Public binary redistribution remains held; see [source provenance](docs/SOURCE-PROVENANCE.md) and [withheld assets](WITHHELD-ASSETS.json).

The original icon/images/manual and textual UI preview are deliberately withheld pending rights or example-media provenance evidence. The project builds without the icon using the standard SDK default apphost icon; this does not replace artwork in the existing installed app. Do not re-add withheld files without the required review. Textual user/developer guides remain available.

Reviewed follow-ups include the keyboard-focus stability correction, live Night/Paper resource identity fix, dependency locks and safe correlated Theme operation diagnostics. Local Theme/App integration235checks and diagnostics/redaction26checks passed. Reference native testing used the separately preserved normal build b1134bd6f80ea4dc46ac23fa9050fcd7a6a1cbe5a3e28f5e00ec29717b09d8bc; this asset-free source build is not claimed byte-identical or every-viewport certified.

Build with the existing supported .NET10 Windows Desktop SDK (`dotnet build CosmicTranscriber.sln -c Release`). Source export checks: `python scripts/verify_source_export.py` and `python scripts/static_verify.py --source-only`. The stricter default release verifier and PUBLISH gate remain intact; source-only verification does not approve a redistributable binary package. No hosted workflow or paid provider request is required for these local checks.

Cosmic Transcriber is a Windows 10/11 WPF desktop application for transcribing long MP3 recordings with OpenAI speech-to-text models. It supports batch import, full-window drag-and-drop, a copied-file Paste fallback, frame-safe chunking, cancellation, resumable checkpoints, TXT/Markdown export, speaker-labelled transcription, editable cost estimates, optional current-user DPAPI key storage, persistent diagnostics, and dark/light themes.

## Isolated Core-only packaging candidate

The reduced candidate uses NAudio.Core 3.3.0 with unchanged Tensors 10.0.13 and
net10.0-windows. Windows 10 build 19041 or later remains the product support
policy. The versionless API target does not promise Windows 7 support; the
SupportedOSPlatform attribute is analyzer metadata, not runtime OS enforcement.
See [packaging evidence and gates](docs/CORE-ONLY-PACKAGING.md). This candidate
has not replaced the installed version or been publicly released.

## Proposed safe-error follow-up

Automatic paid retries are disabled, including for legacy profiles with a nonzero count. Structured operation stages, privacy-filtered provider correlation and bounded diagnostic completeness status are covered by offline regression tests. See [coverage and remaining recovery limits](docs/LOGGING-SAFE-ERROR-AUDIT.md). This source proposal does not update the installed app or clear binary distribution holds.

## What changed in 1.0.27

- **Readable, dependable selectors:** the model and retry controls now use a self-contained opaque WPF template that explicitly pairs the themed surface with the selected text foreground. It retains the required `PART_Popup`, a bounded scrolling `ItemsPresenter`, no transparent popup window, and no popup animation.
- **No misleading dead option:** the realtime-only `gpt-live-transcribe` entry was removed from the completed-file selector. Every visible model item is selectable and supported by the multipart file workflow.
- **Stale settings repaired safely:** an unknown, removed, or realtime-only model left in `settings.json` is normalized to `gpt-transcribe`, and its editable starting price is reset to that model's default estimate.
- **Cosmic Paper contrast repair:** normal-sized status/muted text colors were audited against the light hover surface, and the progress/control accent was darkened to retain at least 3:1 non-text contrast while keeping primary-button text above 4.5:1.
- **Regression gate expansion:** the release verifier now checks the complete dropdown template, selected/item foreground bindings, popup opacity/animation, exact file-model list, stale-model normalization, and light-theme contrast. The mutation suite contains 52 isolated regressions.
- **Wheel scrolling preserved:** a closed selector cannot silently change its value, and the wheel gesture is forwarded so the settings page continues to scroll.
- **Prior safeguards retained:** classified network diagnostics, request IDs, proxy support, speaker diarization, bounded chunking, durable settings/checkpoints/exports, drag-and-drop, clean portable sharing, and closed-dropdown wheel suppression with page-scroll forwarding remain covered.

## Quick start

1. Build or launch Cosmic Transcriber normally. Do not choose **Run as administrator**.
2. Open **API key**, enter your key, and choose **Test connection**.
3. Add MP3s with **+ Files**, **Folder**, drag-and-drop anywhere in the window, or copy files/folders in File Explorer and choose **Paste**.
4. Configure the output folder, model, optional languages, one-per-line keywords, prompt/context, chunk limits and export formats. The legacy automatic retry selector is disabled.
5. Watch the **Settings autosave** card. Valid changes save after a short pause.
6. Choose **Start transcription**. Completed chunks are checkpointed for safe resume.
7. At completion, review the styled summary and choose **Open output folder**.

See [docs/USER-GUIDE.md](docs/USER-GUIDE.md) for the full workflow and [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) for startup, drag-and-drop, settings, API, output, and checkpoint issues.

## What is and is not saved

Automatically saved settings include:

- output path and export options;
- selected transcription model;
- language and keyword hints;
- prompt/recording context;
- chunk size and duration;
- retries, previous-context behavior, and continue-on-error;
- merge preference and combined title;
- theme and editable price estimate;
- the preference to remember an API key securely.

Not stored as ordinary settings:

The queue, progress, activity log, and session-only API key are deliberately outside ordinary settings persistence.

- the current audio queue;
- run progress, current status, and activity-log text;
- a session-only API key when secure remembering is off;
- transcript/audio contents.

Transcription checkpoints are separate from settings and are written after successful chunks. A remembered key is stored separately as a Windows DPAPI-protected blob for the current user.

## Models

- `gpt-transcribe`: default general file-transcription model; supports multiple language hints, structured keywords, and prompt/context.
- `gpt-4o-transcribe-diarize`: speaker-labelled mode with speaker turns and start/end timestamps; prompts and structured keywords are not sent.
- `gpt-4o-transcribe`: retained legacy general file model.
- `gpt-4o-mini-transcribe`: lower-cost retained legacy model.

## Build from source

Requirements:

- Windows 10 version 2004/build 19041 or later, or Windows 11;
- .NET 10 SDK; `global.json` requests SDK `10.0.402` and permits a later servicing patch in that feature band;
- internet access for NuGet restore and OpenAI API use.

Run:

```text
BUILD.cmd
RUN.cmd
```

For a self-contained Windows x64 folder:

```text
PUBLISH-WIN-X64.cmd
```

The publish script opens the resulting folder in File Explorer but does **not** launch the app. For a clean shareable binary, zip or copy that folder before first launch.

For early-startup troubleshooting of the existing built bytes (build the solution separately first):

```text
RUN-DIAGNOSTIC.cmd
```

## Licensing scope (21 October 2026 candidate)

Soh Wei Yu's original code and documentation use the [MIT License](LICENSE), copyright (c) 2026 Soh Wei Yu. Commercial reuse and modified redistribution are permitted subject to the MIT terms. Third-party code, dependencies, Windows/.NET components, icons, screenshots and other binary assets retain their existing rights and are outside this grant. The Microsoft-derived Explorer-shell interop in `WindowsSecurity.cs` remains Apache-2.0. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and [the audit](docs/LICENSING_AUDIT_2026-10-21.md).

This reviewed source derives from the 1.0.27 source embedded in the historical public ZIP, with documented later fixes and a source-only export adaptation. It has not replaced that public ZIP. Authoritative repository publication is under the owner's explicit approval for sohweiyu2023/CosmicTranscriberDesktop. Redistributed output must retain LICENSE, THIRD-PARTY-NOTICES.md and licenses/ plus the notices and terms applicable to its actual runtime/SDK payload; repository publication alone grants no rights in withheld assets or proprietary SDK components.

## Security and privacy

- No API key is embedded in the source package.
- A key is persisted only when the user explicitly enables secure saving.
- Remembered keys are stored separately with Windows DPAPI for the current Windows user.
- `OPENAI_API_KEY` can be used instead of the local vault.
- Structured diagnostic logs omit arbitrary message bodies, transcript/audio/document content and exception messages. HTTP response IDs are hashed; focus records contain only control identifiers. See [diagnostic behavior](docs/DIAGNOSTICS.md).
- Local settings and protected-key files are size-bounded before reading.
- Audio is uploaded only when a transcription run starts; **Test connection** does not upload queued audio.
- Multipart uploads use a generic filename rather than disclosing the local source filename.
- API use and charges belong to the account associated with the user-supplied key.

## Verification status and limitation

The 1.0.27 package was subjected to source-structure, API-contract, settings-persistence, XAML/event/resource, checkpoint/export, filesystem, secret-pattern, package-hygiene, documentation, media-smoke, deterministic manifest generation, independent inventory/checksum, clean-extraction, and mutation checks.

The audit environment is Linux and cannot honestly claim a native Windows WPF compile, Explorer/OLE drop, DPAPI round trip, desktop render, or paid OpenAI request. `static_verify.py` cannot compile C#. Run `BUILD.cmd` successfully on Windows and complete the Windows acceptance checklist before public binary distribution. The current source repair specifically prevents the prior CS0103 build blocker.

## Documentation map

- [User Guide](docs/USER-GUIDE.md)
- [Troubleshooting Guide](docs/TROUBLESHOOTING.md)
- [Developer Guide](docs/DEVELOPER-GUIDE.md)
- [Release Checklist](docs/RELEASE-CHECKLIST.md)
- Comprehensive Word manual in `docs/`

