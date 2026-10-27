# Cosmic Transcriber 1.0.27 - User Guide

## Speaker-labelled transcription

Choose `gpt-4o-transcribe-diarize` for interviews, meetings, dialogues, and Q&A recordings. Cosmic Transcriber displays each finalized turn as Speaker A, Speaker B, and so on, with start/end timestamps when **Chunk / speaker timestamps** is enabled.

In this mode:

- OpenAI's `diarized_json` response is used.
- Server VAD is enabled with `chunking_strategy=auto`.
- Prompt, structured keyword hints, and previous-chunk prompt context are disabled because the diarization model does not support prompts or `gpt-transcribe`'s structured keyword interface.
- One optional ISO-639-1 language hint may be supplied.
- Long files are split into bounded uploads without overlap. Labels become `Part N · Speaker X`; letters are not guaranteed to represent the same person across Parts.

For terminology-sensitive single-speaker talks, keep `gpt-transcribe`. For a discussion where knowing who spoke matters more, choose the diarization model. This release does not yet expose OpenAI's optional known-speaker reference clips.

## Keyword hints versus prompt context

With `gpt-transcribe`, place literal terms such as `prapañca`, `Bāhiya`, `Huangbo`, or `John Tan` in **Keyword hints**, one term per line. Use **Prompt / recording context** for topic, style, and faithfulness instructions. Keep keyword hints relevant: they are suggestions and excessive terms can cause words that were not spoken to appear. Legacy models do not accept the structured keyword field.

## 1. What the app does

Cosmic Transcriber turns one or more MP3 files into TXT and/or Markdown transcripts through the OpenAI transcription API. Long recordings are divided at MP3 frame boundaries into bounded uploads. Completed chunks are checkpointed so an interrupted identical job can resume.

The app is free to use, but OpenAI API usage is billed to the account connected to your API key.

## 2. Before starting

You need:

- Windows 10 build 19041 or later, or Windows 11;
- a built/published Cosmic Transcriber 1.0.27 folder;
- internet access;
- an OpenAI API key with access to the selected transcription model;
- enough free space in the output folder for transcripts and temporary/checkpoint data.

Launch the app normally by double-clicking it or using `RUN.cmd`. Do not use **Run as administrator**; Windows blocks ordinary File Explorer from dropping files into a higher-integrity application. If the warning banner appears, choose **Restart normally**. Cosmic Transcriber removes only a current-user `RUNASADMIN` compatibility token when one exists and asks the normal Explorer shell to reopen the same EXE. It never changes machine-wide compatibility rules automatically.

## 3. Confirm the version and About information

The main header shows `v1.0.27`, and the title bar reads `Cosmic Transcriber 1.0.27`. Choose **About** to see:

- the runtime version;
- “Created and maintained by Soh Wei Yu”;
- the Awakening to Reality website;
- optional PayPal support at `sohweiyu@outlook.com`;
- the independence and API-billing notice.

## 4. Settings autosave

Version 1.0.25 saves valid configuration changes automatically about 900 milliseconds after you stop typing or changing a control. The **Settings autosave** card shows whether changes are pending, saved, partially saved, or blocked by a storage error.

Automatically saved options include the output path, export formats, model, language hints, keyword hints, recording context, chunk limits, retry count, context continuation, continue-on-error, merge settings, combined title, theme, secure-key preference, and editable price estimate.

Persistence boundaries are deliberate:

- the audio queue is not restored after restart;
- progress and in-app activity-log text are not settings;
- a session-only API key is not written to `settings.json`;
- a remembered API key is stored separately with Windows DPAPI only after you opt in;
- completed-chunk checkpoints are separate and remain under the selected output folder.

If a language list, keyword list, or price value is currently invalid, Cosmic Transcriber saves the other valid options and retains the last valid on-disk value for only the invalid field. Correct the field and pause briefly to complete autosave.

## 5. Add MP3 files

Four equivalent routes feed the same import pipeline:

1. **+ Files** - select one or more MP3 files.
2. **Folder** - recursively scan one folder for MP3 files.
3. **Drag-and-drop** - release MP3 files or folders anywhere inside the idle app window.
4. **Paste** - in File Explorer, select files/folders and press `Ctrl+C`; return to the app and choose **Paste** or press `Ctrl+V` while a text/key field is not focused.

During a valid drag, the drop area changes to **Release anywhere in this window to add audio** and Windows should show a copy cursor.

Import behavior:

- inaccessible folder entries are skipped;
- reparse points are not followed;
- duplicate full paths are not queued twice;
- paths are naturally sorted;
- queue capacity is 10,000 files;
- each candidate is opened, hashed, and inspected before it becomes Ready;
- a file changing during inspection is rejected so you can add it again after copying/downloading finishes.

Use the up/down buttons to set merge order, `×` to remove one item, or **Clear** to empty the queue.

## 6. Choose the output

Choose an output folder that is local, reachable, and writable. The app verifies it before a run. When no custom path is saved, the default is an `output` folder beside the executable when possible, with a user-data fallback when necessary.

Select:

- **Plain text (.txt)**;
- **Markdown (.md)**;
- **Include chunk timestamps**;
- **Merge all queued files** and a combined title when desired.

Existing transcript filenames are not overwritten; a unique filename is chosen. Each final transcript is first written to a unique temporary file, flushed to storage, then atomically moved to the first unclaimed final name. Combined export is bounded to 1,000 files and 25,000,000 transcript characters.

## 7. Enter and manage the API key

Choose the header **API key** button to bring the key card into view.

Key sources are attempted in this order:

1. a securely saved current-user copy;
2. `OPENAI_API_KEY`;
3. the value entered for this session.

Controls:

- **Show/Hide** reveals or masks only the key currently loaded on this PC.
- **Remember securely for this Windows user** stores a Windows DPAPI-protected copy when settings are saved/run.
- **Use OPENAI_API_KEY** loads the environment variable.
- **Delete saved copy** removes the persisted protected copy but does not silently erase the current in-memory value.
- **Test connection** checks key/model access and uses indeterminate progress.

OpenAI normally does not let you retrieve a lost full secret after key creation. Create/rotate a key through your OpenAI account if needed. Never paste a key into a screenshot, support post, or diagnostic attachment.

## 8. Configure transcription

### Model

- `gpt-transcribe`: **default and recommended for completed MP3/audio files**. The app's editable starting estimate is US$0.0045 per audio minute.
- `gpt-4o-transcribe`: older accuracy-oriented file model, retained for compatibility; starting estimate US$0.006 per audio minute.
- `gpt-4o-mini-transcribe`: older lower-cost file model; starting estimate US$0.003 per audio minute.
Realtime-only models are intentionally omitted from this selector because this app uploads completed files. Every visible model entry can be selected.

The app currently shows exactly four completed-file models; a stale unsupported value in saved settings is repaired to `gpt-transcribe`. Model pricing can change. The displayed value is editable and is an estimate, not a billing guarantee.

### Language hints

Leave blank for automatic detection. With `gpt-transcribe`, you may enter multiple comma-separated ISO 639-1 hints such as `en` and `zh`, selected ISO 639-3 hints such as `eng`, `cmn`, and `yue`, or regional Chinese hints `zh-cn`, `zh-tw`, and `zh-hk`. Other file models accept one two-letter ISO 639-1 hint. Duplicate hints are removed and the saved form is normalized.

### Keyword hints

With `gpt-transcribe`, enter literal names, Buddhist terms, acronyms, and preferred spellings one per line. The app removes blank lines and case-insensitive duplicates, validates every term before upload, and sends each as a separate `keywords[]` field. Keep hints relevant: an unrelated hint can encourage an unspoken term. Legacy GPT-4o transcription models do not accept structured keywords; the field remains editable while idle so a persisted list can be cleared before using a legacy model.

### Prompt / recording context

Use this for free-form recording context and transcription instructions, such as asking for faithful, complete transcription, preserved negations, and natural paragraphing. Put literal names and specialist spellings in **Keyword hints** instead. The maximum is 12,000 characters.

### Chunk limits

- Upload-size range: 5-24 MB; default 20 MB.
- Duration range: 3-5 minutes; default 5 minutes.

A chunk must satisfy both limits and the internal 24,000,000-byte safety ceiling. Lower values create more requests but can help with fragile files or service limits.

### Context, retries, and continuation

- **Carry previous chunk context** helps terminology/continuity. Resume then requires an uninterrupted valid checkpoint prefix.
- **Automatic retries (cost risk)** can be 0-5 and default to **0**. A timeout or lost response can be ambiguous: OpenAI may already have completed billable work even though the app did not receive the response. Increasing retries can therefore repeat a paid chunk. Use retries only after weighing that risk.
- **Continue after a failed file** applies only to failures classified as file-specific. Authentication, storage, export, or other systemic failures stop the run.

## 9. Understand the cost estimate

The app multiplies total audio minutes by the editable **Estimated price / min (USD)** value. It is not a live quote, invoice, or usage meter. Pricing, model availability, rounding, and billing rules can change. Verify the current OpenAI pricing page and your usage dashboard before a large run.

## 10. Start, watch, and cancel

Choose **Start transcription** or press `Ctrl+Enter`.

Progress appears in:

- each queue item;
- the Run overview;
- the persistent footer, including action text and percentage.

Unknown-duration work is shown with an indeterminate bar. Known inspection/transcription work uses a percentage.

Choose **Cancel** or press `Esc` during transcription. The current request is cancelled, temporary chunk files are cleaned, and valid completed checkpoints are retained. Closing while background work is active asks for confirmation.

When a run finishes, a themed completion window shows succeeded, failed, and skipped counts, the number of output files, elapsed time, and the full output path. Choose **Open output folder** to open the folder directly in Windows Explorer, or **Close** to return to the queue. A run with failures or skipped items is labelled **completed with issues** rather than a clean success.

## 11. Resume from checkpoints

Restart with the same source content and relevant settings. Valid completed chunks are reused. Checkpoints are deliberately invalidated when identity-affecting inputs change, including source content, model, language, prompt, chunk settings, or context behavior.

Choose **Clear checkpoints** only when you intentionally want a fresh run. The app obtains an exclusive cache lease and validates the cache before deleting anything. It refuses unexpected files/folders and refuses symbolic links, junctions, mount points, or other reparse points.

Never place source MP3 files inside `.cosmic-transcriber-cache`. The app blocks both transcription and checkpoint clearing when queued source audio is inside the selected output folder's private cache. Store source audio in an ordinary folder separate from the output cache.

## 12. Logs and diagnostics

Expand **Activity log** for recent in-app events. Choose **Open diagnostic logs** for persistent logs. The preferred location is `logs` beside the executable, with Local AppData and temporary fallbacks.

For an app that fails before its window appears, use `RUN-DIAGNOSTIC.cmd`. It creates .NET host-trace output in addition to managed logs when possible.

Logs are designed to redact key/bearer patterns, but review any file before sharing it. Do not share private transcript or audio content unnecessarily.

## 13. Keyboard shortcuts

- `Ctrl+O`: choose MP3 files.
- `Ctrl+Shift+O`: choose a folder.
- `Ctrl+V`: paste a copied File Explorer file list when a text/key field is not focused.
- `Ctrl+Enter`: start transcription.
- `Esc`: cancel an active transcription.

## Build verification and clean sharing

The Python release verifier checks high-risk source, API, XAML, documentation, and package invariants, but it is not a C# compiler. Before using or sharing a binary, run `BUILD.cmd` on Windows and require a successful Release build. Version 1.0.25 fixes the earlier CS0103 build blocker in the run-start diarization log path.

`PUBLISH-WIN-X64.cmd` creates a clean self-contained folder and opens that folder in File Explorer; it does not run Cosmic Transcriber. To share a clean portable build, zip or copy the publish folder **before first launch**. After launch, the folder may contain local logs, output, settings, or a DPAPI-protected key file and should be inspected before sharing.

## 14. Getting help

Read [TROUBLESHOOTING.md](TROUBLESHOOTING.md). For build/runtime acceptance, follow [RELEASE-CHECKLIST.md](RELEASE-CHECKLIST.md). The About window contains the project website and optional support information.
