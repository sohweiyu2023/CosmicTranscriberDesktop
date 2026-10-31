# Local diagnostics and redacted support export

This describes the reviewed source candidate. It is not yet installed, publicly published or natively accepted. The existing installed b113 build does not have this export button or the newer draft safeguards.

Operational logs contain fixed stages, UTC, process/session/build identity, operation and parent IDs, numeric timing and bounded typed error metadata. They intentionally omit user audio/transcripts/documents, prompts, credentials, cookies, raw provider bodies and arbitrary exception/activity messages. No logging system can guarantee evidence survives a process/OS failure, inaccessible storage or retention loss.

## Exporting a local support projection

In Activity log, select **Export redacted support file** and choose a **new** `.json` filename. This action creates a local projection only. It does not upload anything or copy profiles, inputs, transcripts, archives or raw log files. Existing files are never replaced. If it fails, preserve any partial file and do not share it as a complete report.

The projection accepts only known current-schema owned diagnostic/launcher records. It excludes legacy/unknown records, arbitrary messages/paths and stack-method text. Provider error codes/types/parameter names and failure classifications use fixed vocabularies; parameter names such as `prompt` are metadata, never prompt values. Session/operation/client/MVID/server/build identities are pseudonymized with SHA256, preserving correlation without copying opaque identifiers. Exported build identity is a transformed identifier, not the original binary SHA256; the local release manifest retains exact binary identity.

Review `completeness` and `health` before sharing. Excluded, malformed, duplicate-key, oversized, unreadable, interrupted or capped records/files are counted or flagged. Prior-session reported health losses propagate into completeness. Empty/unterminated logs are incomplete. Collection reads a bounded initial-length prefix; changed/truncated source files are flagged, and the result is not a signed atomic snapshot. Historical/retained-out coverage is always unknown. An otherwise complete selected subset does not certify every past event.

If the sink has failed, writes stay disabled. The app retains the prior private directory only for read-only collection; it does not retry or redirect writes there. If no source is available, an explicit user export can create an incomplete health-only projection. Unexpected initialization failures preserve only a bounded typed reason/HRESULT in memory for such an export; that metadata cannot be guaranteed durable if no sink is writable and the process terminates.

Limits:80 observed top-level `.jsonl` entries,8 selected owned files,2MiB per input file,8MiB total consumed input,2000 inspected records,16KiB per record,8levels JSON depth,32array elements, and1MiB output. Selection prefers the current/last sink if observed, followed by deterministic bounded selection. These are not an entire-history archive or a promise to include every recent session. Raw logs remain local; **Open diagnostic logs** is not a redacted export.

## Logger failures and retention

The app's existing startup fallback order is unchanged. It records fixed selected storage class and bounded typed failure reasons/HRESULTs, not attempted paths. After a runtime sink failure it disables writes and retains a bounded32-event constant-label fallback plus loss counters. Atomic temporary-file cleanup failures increment an independent counter without replacing the primary write result or recursively calling the logger.

The development DiagnosticLauncher keeps the child/output-drain lifecycle independent of its sink, disables failed writes, counts dropped evidence and prints a fixed coverage summary. It no longer records raw executable paths. It distinguishes drained-to-EOF from task completion after a stream read error. Raw child stdout/stderr is never persisted; only counts and fixed failure classifications are retained. Launcher and app failure metadata do not prove a quarantine or a false positive.

App retention remains2MiB files and20 owned files per family/directory; launcher uses64KiB files and20 retained files. Active/legacy/unknown files are preserved, so this is not a global disk-usage bound. Omission/prune/cleanup/failed-sink counters disclose incomplete evidence. Do not change permissions, protection or storage context to make a denied log write succeed.

## Remaining acceptance and release boundary

Focused offline tests cover privacy/fault handling. Actual new picker/export/cancellation/close behavior still needs a coordinated network-blocked synthetic native session; earlier focus checks are not repeated or substituted. The installed app remains unchanged, and provider/recovery business files are unchanged by this logging follow-up. Public source/draft update, merge, controlled installation and public binary redistribution have separate review/authorization gates; artwork/SDK rights holds remain in force.
