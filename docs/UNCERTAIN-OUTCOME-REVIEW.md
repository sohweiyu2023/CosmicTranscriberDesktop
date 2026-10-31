# Review unresolved transcription attempts

Open **Review unresolved attempts** under Output, or **Review attempts** in an unsuccessful completion dialog. The review uses the currently selected output folder; it does not change the queue, settings, credentials or provider account.

1. Refresh the local inventory. Inspection takes coordination leases and may create empty lock metadata in the owned cache. It does not alter pending intents or accepted checkpoints. A busy, inaccessible, unexpected or oversized inventory is reported as incomplete, never as no unresolved work.
2. Select a row. The screen shows only context/chunk identifiers, attempt/client request ID and creation time. Corrupt or unreadable intents show unavailable identifiers and remain unknown. No audio, transcript, prompt or credential is displayed by this workflow.
3. Choose **Recheck selected local response**. This performs local validation under the same cache/work leases used to exclude conflicting work. It sends no network request. It rereads the current intent and validates the checkpoint checksum, index and exact attempt/client identity; stale selections, older/legacy checkpoints and mismatches cannot unlock it.
4. If exact accepted evidence matches, the app creates a durable metadata receipt before reconciling the redundant pending marker. The accepted checkpoint stays available for reuse with the same source/configuration/output context. Existing receipts are verified and never rewritten, even after a previous deletion failure. Receipt failure retains the pending gate.
5. If evidence is unknown, preserve the cache. Review provider history separately using the client request ID. The app has no verified external-evidence importer, refund decision, budget release, override or automatic retry. Do not delete markers or switch settings/output folders to bypass an unresolved outcome.

Accepted local evidence is not a billing refund, and reusing one accepted checkpoint does not authorize new requests for other chunks or configurations. Review the queue before starting anything else. The protection is scoped to the existing local source/configuration/output cache context, not global exactly-once delivery or provider idempotency.

## Local history and diagnostics

Manual successful-evidence review creates `CosmicTranscriber-RecoveryHistory/accepted-<attempt-id>.json` beneath the selected output directory, separately from `.cosmic-transcriber-cache`. A receipt records the original intent identifiers/time, cache context, validated checkpoint hash, evidence type and recording time. It never contains audio/transcript/prompt/key material. This workflow and checkpoint clearing do not remove or rewrite receipts; a partial/locked/conflicting receipt blocks reconciliation. Receipt files are create-once application evidence, not a signed tamper-proof journal. No automatic retention deletion is applied to this history.

This history covers the explicit manual workflow. Earlier automatic normal-run reconciliation remains receipt-free and is not reconstructed retroactively. Normal startup has no inventory/recheck side effect until the user opens the review; opening inventories local metadata only, and recheck requires the explicit button.

Structured local diagnostics distinguish RecoveryInventory, RecoveryRecheck, RecoveryHistory and RecoveryReconcile with operation/parent IDs and fixed reason codes. Unknown results use ProviderOutcomeUnknown. Exceptions omit messages and private payloads. Geometry logs identify the Recovery window without persisting selected identifiers or screen content.

Storage access failures require a legitimate storage/permission diagnosis; the app does not change ACLs, security settings or storage locations to bypass them. No paid provider call is required to inspect or reconcile local accepted evidence.
