# Cosmic Transcriber 1.0.22 — QA Report

## Automated source gate

`python scripts/static_verify.py --no-write-manifests` validates project/version identity, C#/XAML structure, event handlers, resources, API invariants, retry/checkpoint safety, filesystem containment, responsive-layout invariants, DPI declaration, elevation recovery, completion-dialog behavior, documentation provenance, secret patterns, and package residue.

## Adversarial mutation gate

The final gate **passes all 125 mutation cases**. Seven new cases deliberately regress:

- the scroll-offset-safe main layout;
- Per-Monitor-V2 DPI awareness;
- the warning banner’s normal-restart action;
- delegation of relaunch to Windows Explorer;
- the designed completion dialog;
- the completion dialog’s shell-based output-folder action; and
- stale visible verification counts in the DOCX manual.

Every mutation runs in an isolated temporary tree with bounded execution. The prior 118 cases continue to cover transcription models, multilingual and keyword request fields, CS1628, retry billing defaults, successful-response checkpoint ordering, durable writes, cache alias containment, reparse-safe cleanup, settings/key failures, drag/drop routed events, UI contrast, logging, documentation, and generated residue.

## Platform limitation

The current environment cannot perform a native Windows WPF build, high-DPI monitor transition, Explorer/OLE drop, DPAPI round-trip, desktop launch, or live OpenAI upload. Windows acceptance requires `BUILD.cmd`, normal-user launch, a resize/maximize check, one real Explorer drop, and one short non-sensitive transcription through the completion window.
