# Safe-error and diagnostic coverage review

This is a proposed source change for offline review. It does not update an installed application or certify a provider, native UI, public binary package or every possible failure. No raw request, response body, audio, transcript, prompt, credential or personal path is written to the structured diagnostic stream.

## Paid requests

Transcription performs one POST only, regardless of a saved legacy retry count. HTTP408/409/425/429/5xx, network errors, interrupted/oversized/malformed response bodies, internal timeouts and user cancellation after dispatch do not trigger automatic replay. HttpRequestError alone does not prove that nothing was dispatched. Validation and cancellation proven before dispatch send nothing. Existing local filesystem lock/collision retries remain; they do not call the provider.

The legacy retry setting is retained for profile compatibility but its selector is disabled and its count ignored by the client. A client request ID is correlation, not an idempotency guarantee. A provider response/error does not establish zero billing. Review the existing attempt and possible charges before a later user-initiated request.

**Proposed restart guard:** the follow-up in [PENDING-REQUEST-RECOVERY.md](PENDING-REQUEST-RECOVERY.md) records actual attempt intents and requires exact checkpoint binding before reconciliation. It blocks uncertain work only in the same local cache context; conservative pre-send false positives and the lack of an ordinary resolution UI remain. Do not claim global exactly-once provider processing or infer free failure. Existing completed-response checkpoint persistence remains authoritative and is not cancelled by the user's post-response cancellation token.

## Coverage and privacy boundary

| Area | Recorded metadata | Excluded / limit |
| --- | --- | --- |
| Startup/window | Version, assembly hash/MVID, runtime/architecture, exact constructor/lifecycle/fatal category, finite dimensions/DPI | No document/control contents or filesystem paths; native behavior not retested here |
| Settings/config | Correlated begin/result/failure, missing/corrupt/storage reason, fixed storage location kind | No settings payload, prompt, user directory or saved values; explicit quiet-save option preserved |
| Authentication | Nested load/save/remove operations, malformed/missing/corrupt/fallback and exception type/HRESULT | No environment key, DPAPI bytes, credential or key prefix; offline tests inject fake source strings |
| Model access | Operation/client correlation, status, hashed server request ID, terminal failure metadata | Model GET is distinct from paid audio POST; no raw provider body |
| Transcription | Dispatch/header/body/accepted/terminal stages, enum reason, status, HttpRequestError, safe provider code/type/param | Unknown provider strings become other, never character-sanitized secrets; server IDs hashed and client IDs validated |
| Checkpoints | Read/save operations, missing/corrupt invalidation, accepted reuse, storage failure | No checkpoint transcript, original path or content hash emitted; invalidation still may require later paid work |
| Export | Begin/result/failure, rollback and sparse cleanup-failure evidence | No transcript, filename/title, destination path or raw exception text |
| Output probe/cleanup | Typed failed probe, bounded cleanup count and sparse samples | No ACL changes, fallback permission bypass or deletion outside existing application behavior |
| Diagnostics itself | Oversized-record completeness marker; sink-failure status, bounded fallback/drop count and normal-exit coverage snapshot | No alternate-path retry after sink write failure; process-local fallback is lost if the process dies |

Operations have generated IDs; nested service operations include their parent ID. HTTP and exception metadata include the active operation ID. Stages/reasons/storage kinds use enums; legacy activity messages map only exact source-controlled literals. Arbitrary activity messages remain intentionally omitted. Focus/window vocabulary is fixed from source, never copied from free text. Provider code/type/param and failure categories use fixed allowlists; unknown values are explicitly other. Safe client request IDs are ct- plus an exact32hexGUID; bounded server IDs are SHA256 only. Provider/network exception messages and stack file/line paths are excluded; bounded type/HRESULT/method metadata remains.

Each owned log file is at most2MiB. Retention targets20owned files when pruning succeeds; locked or denied stale-file removal can exceed that count, with bounded PruneFailures health metadata. This change does not bypass permissions or delete legacy evidence to enforce a cap. Oversized records are replaced by a small safe record_omitted marker, with a counter. A sink failure disables further filesystem writes for that session and emits only a constant nonthrowing Trace warning plus a32-entry in-memory fallback queue with a dropped-event count. It does not create a new sink path or mask a successful provider result. Cleanup failures are counted exactly with sparse power-of-two samples to avoid floods. Opening diagnostics shows a coverage summary; normal exit persists health when the sink works. A crash or an unavailable sink can leave incomplete evidence, which must be reported rather than hidden.

## Offline verification

Run from the source root using fresh synthetic fixture directories:

```powershell
dotnet build CosmicTranscriber.sln -c Release
dotnet run --project tests/SafetyRegression -c Release -- synthetic-safety-output
dotnet run --project tests/DiagnosticsRegression -c Release -- synthetic-diagnostics-output
dotnet run --project tests/ThemeRegression -c Release
```

The DesktopRegression README documents the generated-tone integration fixture. SafetyRegression uses fake HTTP only and rejects unexpected targets. It covers transient/error statuses, DNS/TLS/reset/end-of-response errors, successful malformed/nested/HTML/invalidUTF8/oversized bodies, interrupted reads, timeout, cancellation before/after dispatch, unknown provider fields containing fake secrets, status/client/operation correlation, synthetic settings/auth/checkpoint/export failures and sink failure after success. Original diagnostics redaction/retention and Theme tests remain. A test-only client source override permits an external negative-control mutation; that file is not production source or a public artifact. A deliberate fake503replay must fail the one-dispatch assertion.

No paid provider test, real credential/profile access, merge, install or deployment is included. A separate native review is required before shipping the changed disabled selector/diagnostic UI. Existing asset/SDK binary redistribution holds remain unchanged. Web has separate source, diagnostics and billing-ambiguity tests and is not covered by Desktop tests or this proposed PR.
