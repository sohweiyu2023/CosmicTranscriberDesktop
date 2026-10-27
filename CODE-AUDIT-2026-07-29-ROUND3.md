# Cosmic Transcriber 1.0.21 — Independent Adversarial Audit Report

**Audit date:** 2026-07-29  
**Reviewed base:** Cosmic Transcriber 1.0.20 round-two reviewed source  
**Result:** one medium functional/API-contract gap and associated low-severity persistence/documentation gaps repaired; no blocker or high-severity defect found in this pass.

## Finding repaired: structured `keywords[]` context was absent (Medium)

The application exposed a single free-form “Prompt / glossary” box but did not implement the structured `keywords[]` field supported by `gpt-transcribe`. OpenAI distinguishes unstructured recording context (`prompt`) from literal expected terms (`keywords`). This matters for AtR recordings containing names, Sanskrit, Tibetan, Chinese, and specialist Buddhist vocabulary.

Version 1.0.21 adds a dedicated one-keyword-per-line field and sends every normalized entry as a separate `keywords[]` multipart part. Input is bounded to 8,000 characters, 200 unique entries, and 200 characters per entry. Empty lines are removed, duplicates are collapsed case-insensitively, and angle brackets/control characters are rejected before audio upload.

## Related correctness repairs

- Structured keywords are accepted only for `gpt-transcribe`; a populated keyword field with a legacy model fails locally before a billable request.
- Keywords persist through settings schema 3 with defensive normalization on load.
- Normalized keywords are included in both checkpoint identity and the local manifest, preventing stale resume data from being reused after glossary changes.
- The UI now distinguishes “Keyword hints” from “Prompt / recording context” and explains the hallucination risk of irrelevant hints.
- Legacy-model selection no longer disables the keyword field while idle; users can clear persisted hints instead of being trapped by a preflight error. Keyword-only guidance also no longer triggers the “empty guidance” confirmation.
- Changelog lineage was corrected after a broad version refresh had duplicated the 1.0.21 heading over the historical 1.0.20 release.
- Static and mutation gates now protect keyword transport, list completeness, forbidden-character validation, legacy isolation, settings persistence, and checkpoint identity.

## Fresh review scope

The pass re-inspected model selection, multipart construction, validation, settings migration, prompt/context chaining, checkpoint durability and reuse, cancellation, retry billing risk, MP3 parsing/chunking, cache containment, reparse handling, output publication, key protection, diagnostic redaction, XAML event/resource wiring, build/publish scripts, package hygiene, documentation identity, and dependency currency.

The sole package dependency remains NAudio 2.3.0, the current stable NAudio 2 release at the time of review. NAudio 3 is pre-release and was not adopted in a corrective maintenance release.

## Verification

- Static source gate: PASS.
- C#/XAML inventory: 27 C# files and 6 XAML files; all 34 referenced event handlers and 52 dynamic/static resources resolved.
- Adversarial mutation suite: PASS — 118/118 cases.
- Deterministic CBR/VBR media smoke evidence: PASS.
- DOCX manual render and accessibility inspection: PASS after regeneration.
- Secret-pattern, generated-residue, inventory, checksum, ZIP CRC, and clean-extraction checks: PASS.

## Residual platform acceptance

The Linux audit environment cannot execute the Windows WPF runtime, Explorer/OLE drag-and-drop, DPAPI, or a native Windows desktop launch. Microsoft documents cross-targeting with `EnableWindowsTargeting`, but recommends release builds on Windows because non-Windows output can differ. Therefore `BUILD.cmd`, normal-user launch, Test connection, and a short real MP3 transcription remain mandatory on the target Windows PC.
