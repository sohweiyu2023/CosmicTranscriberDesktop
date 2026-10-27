# Cosmic Transcriber 1.0.26 — Independent Adversarial Audit

**Audit date:** 31 July 2026  
**Reviewed base:** 1.0.25 dropdown/network corrective source  
**Status:** **REPAIRED — native Windows compile/runtime acceptance still required**

## Executive result

A fresh pass did not merely endorse the previous package. It found and repaired three release-quality weaknesses:

1. The corrected executable still identified itself as 1.0.25, the same version shown by the faulty runtime log. This made binaries and logs operationally ambiguous.
2. Network diagnostics did not record request elapsed time or retain OpenAI response identity when headers arrived but body transport subsequently failed.
3. The complete mutation gate was serial and repeatedly copied/reverified the package, causing the all-suite run to exceed the execution window.

The audit then found one additional edge case: the 45-second **Test connection** timeout was owned by the WPF window, so it discarded the client request ID and structured network context. Timeout ownership is now inside `OpenAiTranscriptionClient` while application-lifetime cancellation remains distinguishable.

## Repairs applied

### 1. Unique 1.0.26 identity

The project, assembly/file/informational versions, application manifest, title/header, build/publish/diagnostic scripts, preview, manuals, guides, reports, verifier, and release checklist now identify 1.0.26. Historical changelog entries remain historical.

### 2. Stronger OpenAI transport diagnostics

`OpenAiTranscriptionClient` now:

- measures every model-test and transcription request with `Stopwatch`;
- logs `elapsed_ms` alongside HTTP status, client request ID, server request ID, and OpenAI processing time;
- retains server request ID and HTTP status for response-body I/O failures and timeouts after headers;
- records a bounded exception chain, including `SocketErrorCode` and native socket error where available;
- keeps the existing DNS, connection, TLS/certificate, proxy, protocol, early-response-end, configuration-limit, timeout, and unknown categories;
- continues to use a unique ASCII `X-Client-Request-Id` for every attempt;
- continues to use current-user credentials with the Windows system proxy;
- keeps automatic retries bounded and defaulted to zero because a lost response may still represent billable work.

### 3. Connection-test timeout ownership

`TestConnectionAsync` now accepts a bounded timeout, creates its own linked cancellation source, and converts its timeout into the same `OpenAiNetworkException` diagnostics used by uploads. External application shutdown cancellation is not misreported as a network failure.

### 4. Mutation-gate performance and scope

The mutation harness now supports `--jobs` from 1–16 and runs isolated temporary copies concurrently. The gate expands from 40 to 44 cases, adding regressions for:

- missing elapsed request diagnostics;
- lost server request ID after headers;
- removal of nested socket/exception diagnostics;
- removal of connection-test timeout classification.

## Old runtime-log finding

The supplied 1.0.25 log shows successful startup, drag-and-drop, MP3 queueing, and preparation of 20 chunks. Each upload then failed after roughly one minute with only `An error occurred while sending the request`, followed by backoff. Because that executable logged only the top-level exception, the historical record cannot distinguish DNS, TCP reset, TLS inspection, proxy/authentication, or another transport failure. The timing is evidence of a transport-path failure pattern, not proof of one exact root cause.

A rerun with 1.0.26 should preserve the exact failure category, elapsed duration, nested exception/socket data, client request ID, and any server request ID/status that existed.

## Scope re-reviewed

- model catalogue, editable price estimates, file-model restrictions, and realtime-only exclusion;
- OpenAI multipart fields, 25 MB boundary margin, response formats, language and keyword contracts, prompt restrictions, and diarization chunking;
- strict UTF-8/JSON parsing, response and segment bounds, timestamp validation, and malformed-response handling;
- MP3 metadata inspection, frame-aligned CBR/VBR chunking, VBR metadata repair, duration/size ceilings, and media smoke decoding;
- source revalidation before billable requests, cancellation, timeout, retry/backoff, continue-on-error, and systemic-stop policy;
- checkpoint fingerprinting, schema/hash/size validation, atomic durable writes, lock containment, chain context, and reparse-point defenses;
- TXT/Markdown speaker export, unique publication, rollback, output-folder checks, and completion dialog;
- debounced partial-valid settings autosave, migration, bounded reads, durable replacement, and DPAPI key separation;
- full-window drag-and-drop, elevated-process warning, copy/Paste fallback, folder recursion, duplicate handling, and cache exclusion;
- startup diagnostics, redaction, retention, fallback locations, application shutdown, DPI metadata, XAML parsing, event/resource resolution, and ComboBox behavior;
- build/publish scripts, package exclusions, secret-pattern scan, deterministic inventory/checksums, clean extraction, manuals, and release documentation.

## Verification results

- Static release verifier: **PASS**.
- Version identity: **1.0.26** across project, assembly/file/informational metadata, manifest, UI, scripts, preview, and current documentation.
- Source/XAML inventory: **28 C# files**, **7 XAML files**, **39 unique handlers across 44 bindings**, and **52 resolved resource references**.
- Adversarial release mutations: **44/44 detected**. The first independent run exposed three verifier blind spots; the gates were strengthened and the complete suite rerun successfully.
- Media smoke test: deterministic CBR and VBR fixtures produced **six frame-copied chunks**, all decoded successfully with FFmpeg.
- Comprehensive manual: **18 rendered pages visually inspected** after updates; accessibility scan reported zero high-, medium-, or low-severity findings.
- Package hygiene and deterministic artifact inventory/checksums: **PASS** before staging.

## Verification boundary

The available environment does not contain the .NET 10 Windows WPF SDK/compiler and cannot exercise Explorer/OLE drag-and-drop, DPAPI, desktop rendering, or a live paid OpenAI upload. Therefore this report does **not** claim a native build or runtime pass. Run `BUILD.cmd` on Windows with the pinned compatible .NET 10 SDK and complete `docs/RELEASE-CHECKLIST.md` before distributing binaries.
