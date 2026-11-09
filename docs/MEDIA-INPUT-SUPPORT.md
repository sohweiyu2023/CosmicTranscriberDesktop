# Local media input support - isolated candidate, 9 November 2026

This is source-only feature documentation, not a released capability. The recorded installed Core-only app remains MP3-only. The separate November4 journey and November9 streaming-export candidates are preserved; the streaming-export follow-up has not silently been merged into this feature.

## Import and actual media processing

Files, Folder, Paste and Drop reach the same importer. The picker offers common audio/video filename hints plus All files. Folder scanning and direct input do not treat an extension as proof of usable audio; unknown/misnamed files may be probed. The local scan stops at10,000 distinct candidates and reports the limit. This is separate from accepted queue capacity, so unsupported files do not consume the remaining accepted slots. Cancel/Escape can stop local inspection; close also cancels it. Files and Folder were exercised through the actual latest-source window using pinned synthetic inputs and an in-memory non-forwarding provider. Paste used a fixed synthetic surrogate. Real OS clipboard and cross-window drag remain unverified; all four handlers share the reviewed importer.

Valid MP3 remains on the existing frame-preserving path and needs neither FFmpeg nor ffprobe. Other inputs require separately installed FFmpeg/ffprobe on an explicit absolute PATH directory. Nothing is downloaded, bundled, installed or configured by this feature. The app uses argument arrays without a shell. Conversion accepts local regular files, rejecting network shares/URLs and filesystem-link ancestors. Legacy MP3 behavior is retained separately.

Safe content probing uses a media-demuxer allowlist, not an arbitrary file-extension gate: AAC, AC3/EAC3, AIFF, AMR, ASF, AU, AVI, CAF, FLAC, Matroska/WebM, MOV/MP4-family, MP3, MPEG/MPEG-TS, Ogg, WAV/Wave64, APE, TTA and WavPack. Playlists, concat, image sequences, devices and network protocols are excluded. MOV external-track loading remains disabled by the converter's default; no option enables it. Unsupported codecs, DRM or broken media are not promised to work.

Conversion selects the **first audio stream**, discards video/subtitles/data/chapters and source metadata, and writes temporary128kbps,44.1kHz stereo MP3. This is lossy normalization, including for lossless sources; it does not retain multiple audio tracks or lossless output. Original source bytes are never modified. Chunking, generic multipart filename/audio-mpeg MIME, checkpoints, recovery and TXT/Markdown export then use the existing MP3 pipeline. No provider request happens during import/probing/conversion. Test connection/Start still have their usual provider cost implications.

## Tested support matrix

Actual synthetic decoding/conversion and in-memory provider submission passed, not just filename selection:

| Input/container | Actual tested audio codec/content |
| --- | --- |
| MP3 | MP3; existing frame-preserving path, no converter required |
| M4A | AAC and ALAC in the same container family |
| AAC/ADTS, M4B | AAC |
| WAV, Wave64 | PCM signed16-bit little-endian |
| FLAC | FLAC |
| Ogg, Opus | Vorbis and Opus respectively |
| WebM | Opus |
| MP4 | AAC alone and AAC with MPEG4 video |
| MOV,3GP | PCM and AAC respectively |
| Matroska/MKV | FLAC alone and FLAC with MPEG4 video |
| AVI | PCM alone and PCM with MPEG4 video |
| WMA/WMV (ASF) | WMAv2 audio |
| AIFF, CAF, AU | PCM big-endian, PCM little-endian and mu-law respectively |
| AC3 | AC3 |
| MPEG program stream/MPG, MPEG-TS | MP2 and AAC respectively |
| WavPack, TTA | WavPack and TTA |
| Unknown extension, misleading `.mp3` | Actual FLAC and WAV detected/converted, not accepted by suffix alone |

There are31 container/codec/filename cases, not31 unique container formats. Separate tests include a650-second AAC/M4A producing three upload chunks, repeated accepted-checkpoint reuse with no new mock dispatch, video without audio, corrupted M4A, a disguised remote playlist, Unicode/spaces/shell-metacharacter filenames, missing tools and cancellation. No speech-accuracy or live-provider pass is inferred from generated tones/mock text.

AMR, APE, EAC3 and other codecs within allowed demuxers remain **conditional, not fixture-certified**: capability depends on the installed converter build. Unsupported/protected/corrupt media and video without usable audio are rejected before dispatch. Unknown failures remain unknown; error prose and filenames are not treated as reliable evidence of encryption/corruption. Only a few complete fixed tool diagnostics receive specific safe classifications; structured exit code, stage, operation correlation, elapsed time and omission/truncation flags remain available without raw media/tool prose.

## Bounds and durable safety

For conversion:20GiB source,24-hour duration,2GiB prepared audio; probe deadline30seconds and conversion deadline2hours. Tools use2 codec threads/1filter thread,64MiB per allocation,8MiB probe and10-second analysis settings. An output time ceiling of24hours+1second and a2GiB file ceiling stop excessive output; reached limits are rejected before upload, not treated as a complete transcript. File-size polling may overshoot by a small amount. These settings are not a hard total-process RAM/CPU sandbox, and the boundary-sized files were not generated to fill disk in testing. Storage/timeout/resource failures are surfaced without automatic retry. Cancellation stops only the owned converter process tree and waits; it does not kill an existing app or change security policy.

Prepared audio is removed on success/failure/cancellation; cleanup failures retain the existing loss reporting. The original source hash is revalidated under a read lease. A normalization policy and prepared-audio hash bind cache reuse; changed/missing/corrupt conversion identity with prior transcript evidence blocks new dispatch and preserves accepted/uncertain work. Converted-file work identity excludes probed duration rounding, so a tiny timing change cannot silently bypass existing work; actual prepared bytes still have to match. Legacy MP3 fingerprint input remains unchanged. No budget is released, pending marker deleted or paid request replayed merely because conversion was attempted.

FFmpeg was already present at C:\ProgramData\chocolatey\bin\ffmpeg.exe with ffprobe alongside it. Tests used7.1.1-essentials_build-www.gyan.dev. It is an external development/runtime prerequisite, not part of the app output or proposed public ZIP. Redistributing any converter build requires a separate exact-build/license/inventory review; this feature adds no such rights or binary payload.

Provider documentation lists direct MP3/MP4/MPEG/MPGA/M4A/WAV/WebM inputs and a25MB upload limit. That does not make the installed Desktop parser support them. This candidate normalizes locally and submits bounded MP3 chunks instead; all test HTTP was in-memory. Reference: https://developers.openai.com/api/docs/guides/speech-to-text . Converter protocol and MOV external-track behavior: https://ffmpeg.org/ffmpeg-protocols.html and https://ffmpeg.org/ffmpeg-formats.html .

## Scoped native acceptance and remaining delivery

November9 actual latest-source WPF checks used a separately reviewed sealed host derived from normal DLL `4e960ffbb4ce321be607cd6ea67b5695b4c5b11f91038e2f94fee726f6962741`, disposable settings and pinned synthetic media. Files chooser cancellation/import, Folder import of AAC/ALAC/video-with-audio/650-second audio, local conversion, four-chunk long-file output, visible completion, TXT/Markdown export and accepted checkpoint reuse passed. Corrupt and no-audio inputs produced useful visible warnings. Cancel during a pending fake response preserved its intent; transport uncertainty, malformed response and restart remained blocked without replay. Support-export Save/Cancel and incomplete-evidence notice, live Night/Paper change, settings persistence/restoration and Open output folder passed. Every owned session closed normally with application lifecycle exit0.

These are synthetic native/service checks, not speech-recognition or live-provider certification. OS clipboard was intentionally disabled; Paste used a pinned surrogate. Real cross-window drag, protected credential storage and external-editor integration were not certified. A normal lifecycle record is not an independently captured OS process exit code. Earlier PowerShell-script restrictions remain unchanged; no policy or security setting was changed.

Main, installed app and public ZIP remain separate delivery states until verified integration and release. A final hosted workflow verifies build and offline safety/diagnostic/settings regressions only; it does not repeat or replace local real-codec/native evidence. The proposed ZIP excludes external FFmpeg and withheld artwork. No public converter redistribution rights are implied. Web's genuine Safari gate remains separate from these Windows checks.
