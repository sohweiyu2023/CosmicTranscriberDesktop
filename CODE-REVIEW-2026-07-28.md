# Cosmic Transcriber 1.0.21 — Corrective Full Code Review

> **2026-07-29 follow-up:** Integrated the newly released `gpt-transcribe` file model as default and safely catalogued `gpt-live-transcribe` as realtime-only. See `CHANGELOG.md` for protocol and pricing changes.

**Review date:** 2026-07-28  
**Reviewed artifact:** `CosmicTranscriber-1.0.21-audited-source(1).zip`  
**Disposition:** Source repaired; static, mutation, media, documentation, and package-integrity gates pass. A Windows WPF build remains the final environment-specific acceptance gate.

## Scope

The review covered all 26 C# files, all 6 XAML files, the project/solution and .NET SDK pin, Windows build/publish/diagnostic scripts, startup and shutdown handling, UI event wiring and drag-and-drop, API-key storage and visibility, OpenAI request/response handling, MP3 inspection and chunking, cancellation and retry semantics, checkpoint durability and cleanup, transcript publication, settings, logging, path containment/reparse-point controls, documentation, media evidence, and release-package integrity.

## Findings repaired

### CT-CR-001 — Blocker — `CS1628` in `ValidateRun`

`ValidateRun` declared `outputDirectory` as an `out` parameter and then captured it in the LINQ predicate used to reject source files inside the checkpoint cache. C# forbids capturing `ref`, `out`, or `in` parameters inside lambdas, so the Windows build failed at `MainWindow.xaml.cs` line 1529.

**Repair:** resolve and validate the path into a normal local variable named `validatedOutputDirectory`; assign that value to the `out` parameter only after validation; use the local variable inside the LINQ predicate.

**Regression protection:** the static verifier now requires the safe local-variable pattern, and a new mutation deliberately changes the predicate back to `outputDirectory`. The gate rejects it with the `CS1628` diagnostic marker.

### CT-CR-002 — Low security/reliability hardening — Unicode digits in destructive cleanup allowlists

The checkpoint/chunk cleanup recognisers used `char.IsDigit`. That API accepts many Unicode decimal digits, although Cosmic Transcriber generates these internal filenames using ASCII `0`–`9`. A specially planted Unicode-digit filename could therefore be treated as app-owned cleanup content.

**Repair:** internal checkpoint and temporary-chunk numeric tokens now accept ASCII digits only (`'0'` through `'9'`).

**Regression protection:** a new mutation restores `char.IsDigit`; the verifier rejects the weakened allowlist.

### CT-CR-003 — Audit-process gap — source gate did not cover this compile-time rule

The prior static gate covered many architectural invariants but did not compile C# and did not encode the illegal `out`-parameter capture rule. This allowed an artifact described as audited to fail its first real Windows compilation.

**Repair:** added a targeted compile-regression invariant and mutation, raised the mutation suite from 101 to 103 cases, and updated release evidence to disclose the Windows-only build boundary clearly.

## Adversarial review outcome

No additional blocker, high-severity, or clearly actionable production-code defect was found in the remaining reviewed paths. In particular, the pass rechecked:

- bounded audio uploads and response bodies, strict UTF-8 and structured API errors;
- opt-in retries, cancellation boundaries, and post-response durable checkpointing;
- source-content hashing under a stable read lease;
- CBR/VBR MP3 frame handling, decoder-reservoir continuity, overlap and chunk ceilings;
- checkpoint locks, schema/digest checks, safe cleanup names and source-inside-cache guards;
- atomic/durable settings, protected-key, checkpoint and transcript publication;
- DPAPI CurrentUser key protection, secret redaction and UI reveal synchronisation;
- startup diagnostics, executable-adjacent logs, unique persistent handles and retention;
- XAML event/resource resolution, theme contrast safeguards, progress visibility and full-window drag routing;
- queue/import bounds, transcript-memory bounds, shutdown/cancellation and multi-instance behaviour.

## Verification evidence

- `scripts/static_verify.py`: **PASS** — 26 C# files, 6 XAML files, 34 XAML handlers and 52 resource references.
- `scripts/test_static_verifier.py`: **103/103 corrective-era mutation cases passed at that review point. The current expanded 108-case result is in `docs/MUTATION-RESULTS-2026-07-29.txt`.
- `scripts/media_smoke.py`: **PASS** for deterministic 680-second CBR and VBR sources; all generated chunks decoded successfully and first-chunk PCM matched the source prefix.
- Python/XML/XAML/documentation/secret-pattern/inventory/checksum checks: **PASS** through the static release gate.
- ZIP CRC/integrity check: performed on the final package after manifest regeneration.

## Residual acceptance gate

This Linux sandbox has no installed .NET 10 SDK or Windows WPF runtime, and its download policy prevented installing the SDK. Therefore this review does **not** claim a local WPF compile, executable launch, Explorer/OLE drag-and-drop test, high-DPI render, or live OpenAI transcription.

On Windows, run:

```bat
BUILD.cmd
```

Expected result: `BUILD PASSED`, followed by the executable path under `src\CosmicTranscriber\bin\Release\net10.0-windows10.0.19041.0`.
