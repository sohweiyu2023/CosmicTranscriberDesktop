# Cosmic Transcriber 1.0.23 — Speaker Diarization Implementation and Audit

**Audit date:** 30 July 2026  
**Reviewed base:** Cosmic Transcriber 1.0.22  
**Disposition:** diarization added and packaged as 1.0.23.

## User-facing result

Cosmic Transcriber 1.0.22 could not distinguish speakers. Version 1.0.23 adds `gpt-4o-transcribe-diarize` to the model selector. Selecting it produces structured speaker turns, normally labelled Speaker A, Speaker B, and so forth, with start/end times in TXT and Markdown output.

`gpt-transcribe` remains the default. OpenAI recommends it for ordinary recorded-file transcription and reserves the specialized diarization model for speaker labels. This avoids degrading terminology-sensitive single-speaker work merely to obtain a capability that is unnecessary for those files.

## Official API contract implemented

The request uses:

```text
POST /v1/audio/transcriptions
model=gpt-4o-transcribe-diarize
response_format=diarized_json
chunking_strategy=auto
```

The response parser requires a top-level text field and a bounded segments array. Each accepted segment must contain a speaker string, finite nonnegative start/end seconds with end greater than or equal to start, and text.

The diarization model does not support prompts. Cosmic Transcriber therefore disables and omits the recording-context prompt, structured keyword hints, and previous-chunk prompt continuation in this mode. One optional singular language hint remains available.

OpenAI supports up to four known-speaker reference clips, but the 1.0.23 UI does not implement that optional workflow. This release provides anonymous speaker labels only.

## Reliability findings and repairs

### 1. No diarization existed in 1.0.22 — repaired

There was no model catalogue entry, `diarized_json` transport, speaker-segment representation, parser, checkpoint format, or speaker-labelled exporter. All were added.

### 2. Unsafe initial size-only design — repaired during review

An early implementation attempted to avoid splitting diarized files by disabling the duration ceiling. The model publishes a 2,000-token maximum output, so a large compressed MP3 could exceed practical response capacity despite fitting under the 25 MB upload limit.

The final implementation retains both the configured three-to-five-minute duration ceiling and the 24,000,000-byte safety ceiling. Every diarization request also sends server-side VAD auto chunking as required for inputs longer than 30 seconds.

### 3. Duplicate-turn risk from MP3 preroll — repaired

Normal transcription chunks use a short MP3 preroll to preserve decoder continuity and contextual audio. Diarization prompts cannot suppress repeated overlap, and duplicated overlap could create repeated speaker turns. Diarization chunks therefore retain frame-safe chunking but omit speech preroll.

### 4. Cross-request identity ambiguity — handled explicitly

Anonymous speaker labels are request-scoped. When one source requires several uploads, the application prefixes labels with the upload part, for example `Part 2 · Speaker A`, and writes a warning that the same letter in different Parts is not guaranteed to be the same person.

### 5. Checkpoint integrity and rebilling risk — repaired

Structured speaker segments are stored alongside transcript text and included in the checkpoint SHA-256 payload hash. Checkpoint schema changed to `cosmic-transcriber-checkpoint-1.11`.

Diarized JSON can serialize larger than plain transcript text. The bounded checkpoint ceiling was increased to 16 MiB, and the serialized size is checked before durable atomic publication. If a successful billable response cannot be checkpointed safely, the queue stops with an explicit storage exception rather than silently retranscribing it later.

### 6. Stale checkpoint identity after chunking change — repaired

Checkpoint fingerprints include model, language, effective duration/size settings, diarization flag, and a dedicated no-preroll chunker identity. A diarization run therefore cannot reuse incompatible ordinary-transcription checkpoints.

## Output format

Example TXT:

```text
[00:00:02–00:00:08] Speaker A:
What is meant by anatta?

[00:00:08–00:00:18] Speaker B:
It means that no independent agent or controller can be found.
```

Example Markdown:

```markdown
## Speaker A — 00:00:02–00:00:08

What is meant by anatta?
```

When timestamps are disabled, speaker names remain present.

## Pricing presentation

OpenAI publishes token pricing of US$2.50 per 1 million audio-input tokens and US$10 per 1 million output tokens for `gpt-4o-transcribe-diarize`. It does not publish a guaranteed diarization per-minute price. The app therefore uses an editable US$0.006/minute approximation and documents that this is an estimate rather than a quote.

## Verification completed

- Dedicated 1.0.23 diarization source gate: passed.
- 28 C# files and 7 XAML files structurally checked.
- XAML/XML parsing and code-behind event resolution: passed.
- C# delimiter checks: passed.
- Model routing, unsupported-field isolation, strict structured parsing, timestamps, chunking, no-preroll mode, checkpoint integrity, export, UI state, secret scanning, and package-residue checks: passed.
- 19/19 deliberate diarization regression mutations were detected.
- Deterministic 680-second CBR and VBR MP3 media smoke test: passed; six generated chunks decoded successfully through FFmpeg.

## Verification not possible in this environment

The review environment is Linux and has no .NET 10 SDK/WPF toolchain. It could not perform a native Windows WPF compilation, Windows DPAPI test, Explorer drag-and-drop test, or paid OpenAI API request. Those are not claimed as passed.

Required acceptance on Windows:

1. Run `BUILD.cmd`.
2. Select `gpt-4o-transcribe-diarize`.
3. Transcribe a short non-sensitive recording with two clearly alternating speakers.
4. Confirm TXT and Markdown contain distinct labels and correct timestamps.
5. Test cancel/resume to confirm completed speaker segments are restored from checkpoint.
6. Test a recording longer than one selected chunk duration and confirm Part-scoped labels and no duplicated overlap turns.
