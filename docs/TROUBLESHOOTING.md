# Cosmic Transcriber 1.0.27 - Troubleshooting

## “Invalid keyword hint” or legacy-model keyword warning

Use one literal term per line. Remove `<`, `>`, control characters, and overlong entries. Structured keywords are available only with `gpt-transcribe`; clear the field or change the model before starting.

## 1. Nothing appears when I launch the executable

1. Extract the complete build/publish folder; do not run the EXE from inside a ZIP.
2. Use `RUN.cmd` from the source/build folder.
3. Check `logs` beside the executable.
4. Build the solution separately, then run `RUN-DIAGNOSTIC.cmd` to capture structured startup/exit evidence for the existing bytes. See [diagnostic behavior](DIAGNOSTICS.md).
5. Confirm Windows meets build 19041+ and the correct architecture is used.
6. For a framework-dependent build, install the required .NET 10 Windows Desktop runtime/SDK. A self-contained publish should not need a separate runtime.
7. Check Windows Security, SmartScreen, antivirus quarantine, and corporate application-control logs.
8. Ensure the folder path is not excessively long and the user can read/execute it.

Do not launch a `_wpftmp` intermediate file from `obj`; launch the final output under `bin\Release\net10.0-windows10.0.19041.0` or the publish folder.

## 2. Dragging shows a forbidden/cancel cursor everywhere

The most common cause is a Windows privilege mismatch: Cosmic Transcriber is elevated while File Explorer is not.

1. Choose **Restart normally** in the warning banner. The app asks the normal Windows Explorer shell to reopen the same executable without administrator privileges.
2. If a current-user `RUNASADMIN` compatibility token exists, the app removes only that token while preserving other compatibility options.
3. If Windows reports a machine-wide compatibility rule, close the app, right-click `CosmicTranscriber.exe`, choose **Properties > Compatibility > Change settings for all users**, and clear **Run this program as an administrator**.
4. Do not start the app from an elevated Command Prompt, PowerShell, task, launcher, installer, or elevated Visual Studio instance.
5. After restart, the elevation warning should disappear and Explorer should show a copy cursor over the window.

Reliable fallback: select MP3 files/folders in File Explorer, press `Ctrl+C`, then choose **Paste** in the app or press `Ctrl+V` away from text/key fields. The **+ Files** and **Folder** buttons use normal picker APIs and do not depend on OLE drag-and-drop.

The application intentionally does not bypass Windows User Interface Privilege Isolation.

## 3. Drag is accepted but no files are added

- Confirm the dragged data contains actual filesystem files/folders, not a browser attachment, Outlook virtual item, archive member, or cloud placeholder that has not downloaded.
- Confirm the files are `.mp3`.
- Wait for copy/download/synchronisation to finish and add the file again.
- Check whether the app is busy importing, testing the key, or transcribing; drops are accepted only while idle.
- Confirm queue capacity has not reached 10,000.
- Use Paste or the pickers to distinguish shell/OLE problems from MP3 inspection problems.
- Expand Activity log and inspect the persistent log.

## 4. Paste says there is nothing to paste

File Explorer must place a file-drop list on the clipboard. Select files/folders and press `Ctrl+C`; copying text that merely contains a path is not sufficient. Some remote/virtual shells do not expose a normal file list; use **+ Files** or **Folder**.

## 5. The Start button text or other controls are hard to read

- Confirm the header says `v1.0.27`; older builds may retain the original theme defect.
- Switch between Cosmic Night and Cosmic Paper once.
- Close and restart after changing Windows contrast/theme settings.
- Test Windows display/text scaling at standard values.
- Do not combine a new executable with old theme resource files.
- Capture the entire window, theme, scaling percentage, and state (enabled/disabled) when reporting a remaining problem.

## 6. The window is clipped on a small screen or remote desktop

Version 1.0.25 removes the outer horizontal scroll surface that could retain a nonzero offset after resize/maximize and make the whole interface appear shifted or cut off. It also declares Per-Monitor-V2 DPI awareness and uses a smaller default footprint.

1. Maximise the window.
2. Use the outer horizontal scrollbar when the two-column dashboard cannot fit.
3. Scroll the right settings panel vertically.
4. Reduce Windows display scaling temporarily if the remote session is extremely small.
5. About is resizable and contains its own vertical scrolling.

The app fits its requested size to the Windows work area, but Windows may still enforce system minimum tracking dimensions in exceptionally tiny sessions.

## 7. Settings or the saved API key do not persist

Confirm the header shows `v1.0.27` and watch the **Settings autosave** card after changing a value. A successful save displays a timestamp. If the card reports a storage failure, choose **Open diagnostic logs** and correct the permissions for the reported settings directory.

Version 1.0.25 saves valid options about 900 milliseconds after input settles. An invalid language list, keyword entry, or negative/non-numeric price does not overwrite the last valid value for that field; unrelated valid settings still save. Correct the input and pause briefly.

The queue, progress, and activity-log text are intentionally not restored. A session-only API key is not an ordinary setting. Enable **Remember securely for this Windows user** to store it separately with current-user Windows DPAPI. If the app fell back to `CosmicTranscriber-settings` beside the EXE, confirm that folder remains writable and was not removed when replacing the application.

## Build fails with CS0103 for `usesSpeakerDiarization`

This identifies the superseded 1.0.24 source. In that package, `Start_Click` used `usesSpeakerDiarization` without declaring it in the method scope. Use version 1.0.25 or later, then run `BUILD.cmd` again. Do not treat a passing Python source verifier as proof that C# compilation succeeded.

## Preparing a clean portable ZIP

Run `PUBLISH-WIN-X64.cmd`. It opens the output folder in File Explorer but does not launch the application. Zip or copy that folder before first launch. If the app has already been run from the publish folder, inspect and remove private/runtime material such as `logs`, `output`, `CosmicTranscriber-settings`, `openai-key.protected`, and transcript/audio files before sharing.

## 8. API key or network test fails

- Re-enter the key; remove leading/trailing spaces.
- Confirm it is an OpenAI API key, not a ChatGPT password/subscription identifier.
- Confirm the selected model is available to the key/project.
- For queued MP3/files, select one of the four visible file-transcription models. Realtime-only models are intentionally not shown.
- Check billing/project limits and organisation policy.
- Check proxy, TLS inspection, firewall, DNS, VPN, system clock, and endpoint-security logs.
- Test `OPENAI_API_KEY` only after starting a new process with the variable correctly set.
- Read both the bounded dialog and the diagnostic log under `logs` beside the executable.

Version 1.0.27's corrective audit records elapsed request time, the nested exception chain, and a failure category such as `DNS/name resolution`, `connection`, `TLS/certificate`, `proxy tunnel`, `network authentication`, `HTTP protocol`, `response ended early`, `timeout`, or `unknown network failure`. Every request also receives a local client request ID. When OpenAI returns an HTTP response, the structured log records a SHA-256 of the returned server request ID and the status. A pure DNS/TLS/connection failure has no server request ID because no OpenAI response arrived.

When requesting support, include the relevant diagnostic-log excerpt, the on-screen failure category, the client request ID, and any server request ID. Remove API keys, audio/transcript content, personal paths, and unrelated private data before sharing. Do not repeatedly retry a large transcription until the cause is understood: a response can be lost after the server has accepted billable work.

A ChatGPT subscription does not automatically include API credits.

## 9. Cost estimate looks wrong

The estimate is total audio minutes multiplied by the editable USD-per-minute value. It is not fetched live. Update the field to the current price for your chosen model, verify the official pricing page, and compare actual usage in your OpenAI dashboard. Do not treat the estimate as a billing guarantee.

## 10. A file is rejected as changed or inaccessible

- Wait for the download/copy/cloud synchronisation to finish.
- Close editors, players, taggers, backup agents, or antivirus operations that lock the file.
- Copy the MP3 to a normal local folder and add the copy.
- Confirm the file is not empty and contains valid MP3 frames.
- Avoid unstable network shares for long runs.

## 11. Transcription fails partway through

- Check whether the error is file-specific or systemic.
- Authentication, rate/billing, output/storage, and systemic protocol failures intentionally stop later files.
- Lower chunk size/duration for marginal connections.
- Automatic paid retries are disabled regardless of saved retry count. A lost/timeout response may represent billable completed work. Review the existing attempt and possible charges before any later manually initiated request; the proposed same-context pending-intent guard blocks unresolved work across restart but cannot prove provider exactly-once billing. Preserve unresolved evidence; do not clear the cache as a workaround.
- Confirm enough disk space remains for temporary chunks, checkpoints, and output.
- Keep the source and output paths available for the whole run.
- Resume with identical settings to reuse valid checkpoints.

## 12. Resume does not reuse a checkpoint

Checkpoint identity changes when source content or relevant settings change. Reuse may also fail after a corrupt/truncated checkpoint, transcript-digest mismatch, context dependency gap, or cache clear. Restore the exact original settings/source or allow the chunk to regenerate.


## 13. Clearing checkpoints is blocked or reports an unexpected cache item

Cosmic Transcriber clears only application-recognized work folders under `.cosmic-transcriber-cache`. It deliberately stops before deleting anything when it finds an unknown file/folder or any symbolic link, junction, mount point, or other reparse point.

- Close all Cosmic Transcriber instances and retry.
- Move any source audio out of `.cosmic-transcriber-cache`.
- Do not put personal files in the hidden cache folder.
- Inspect the on-screen storage error for the refused path; structured logs omit exception messages and personal paths.
- If the path is a junction/symlink or contains unfamiliar data, preserve it and investigate rather than forcing deletion.
- After confirming the data is disposable, remove the unexpected item manually with normal Windows tools and retry **Clear checkpoints**.

The app intentionally refuses a broad “delete everything recursively” fallback because it could traverse filesystem links into unrelated storage.

## 14. A checkpoint or transcript write fails after OpenAI returns text

The app physically flushes completed-response checkpoints before continuing and treats failure as systemic to reduce the chance of repeating billable work. Check:

- free space and quota in the output location;
- write permissions and Controlled Folder Access;
- antivirus/backup software holding temporary files;
- availability of removable/network storage;
- read-only attributes or damaged filesystem entries under the private cache.

Do not simply rerun until storage is healthy. A failed or ambiguous network response can make repeated attempts costly.

## 15. Output is missing, duplicated, or cannot be written

- Confirm at least one of TXT/Markdown is selected.
- Confirm the output folder is writable and remains connected.
- Existing names are not overwritten; look for a numbered unique filename.
- Combined output is created only when the run has the required successful results and remains within merge limits.
- Check for long paths, invalid Windows filename characters, antivirus interference, or a full disk.
- Treat export/storage errors as systemic and resolve them before rerunning.

## 16. Logs are not beside the executable

The preferred location is executable-adjacent `logs`, but installed/read-only locations may reject it. The app then uses Local AppData or temp. The Activity log states the actual path. `RUN-DIAGNOSTIC.cmd` prints the shared session ID and launcher-record path after child exit.

## 17. What to provide in a bug report

Provide:

- version shown in the header/About;
- Windows version and display scaling;
- framework-dependent build or self-contained publish;
- exact reproduction steps;
- whether the app/Explorer is elevated;
- screenshot of the whole window and warning banner;
- redacted newest diagnostic log;
- whether Files/Folder/Paste work when drag fails;
- a non-sensitive sample MP3 only when needed and permitted.

Never provide the API key. Review logs and screenshots for private paths/transcript content before sharing.

## Speaker labels do not identify the person's real name

Anonymous diarization normally returns labels such as Speaker A and Speaker B. Version 1.0.27 does not yet provide the optional known-speaker-reference UI, so rename labels during editing when needed.

## Speaker A changes identity in a later Part

A long recording can require several independent uploads. OpenAI does not promise that anonymous labels remain stable across requests. Cosmic Transcriber therefore prefixes them with `Part N ·` and warns that the same letter in different Parts may be a different person.

## Prompt and keyword boxes are disabled

This is expected after selecting `gpt-4o-transcribe-diarize`: OpenAI documents that this model does not support prompts, and structured keyword hints are a `gpt-transcribe` feature. Return to `gpt-transcribe` for terminology-heavy recordings where speaker separation is unnecessary.

