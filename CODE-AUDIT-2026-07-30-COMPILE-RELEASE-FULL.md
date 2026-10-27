# Cosmic Transcriber 1.0.25 — Compile-Blocker and Full Release Audit

This report records the fresh independent audit performed on the 1.0.24 autosave/full-audit package.

## Material repair

`MainWindow.Start_Click` referenced `usesSpeakerDiarization` when composing the run-start log but did not declare the variable in that method. C# therefore reports CS0103. Version 1.0.25 declares the value from `selectedModel` before request construction and use.

## Why the previous gate missed it

The Python release verifier performs structural string/XML/package checks; it does not compile C#. It had many high-value invariants but no rule for this exact undefined-local regression. The verifier and mutation suite now protect the required declaration/order, and documentation identifies Windows `BUILD.cmd` as the mandatory compiler gate.

## Additional hardening

- Publish output messaging explicitly distinguishes opening the folder from launching the app.
- Clean portable builds are to be archived before first launch.
- `.gitignore` now covers logs, settings, protected-key files, and generated transcripts.
- Current README, guides, checklist, reports, preview, manual, inventory, and checksums were refreshed.

## Full review scope

The audit re-read model eligibility, OpenAI multipart fields, diarized parsing, retries/timeouts/cancellation, checkpoint continuity and durability, MP3 frame processing, output publication, settings/key persistence, drag/drop recovery, completion dialog, logging/redaction, filesystem boundaries, XAML events/resources, build/publish scripts, documentation, and package contents.

No additional blocker or high-severity production defect was found.

## Verification

- Static release gate: PASS
- Release mutations: 32/32 detected
- Media smoke: PASS
- DOCX render/accessibility: PASS, zero findings
- Inventory/checksums: PASS (81 inventoried artifacts; 82 checksum entries including the inventory)
- Clean extraction: PASS

## Limitation

No .NET 10 SDK/Roslyn Windows WPF compiler is available in this Linux audit environment. Run `BUILD.cmd` on Windows before treating the source as binary-release accepted.

## Finding 4 — Low: release manifests had no reproducible generator

The inherited inventory and checksum files were stale, while documentation incorrectly implied that the source verifier generated them. Version 1.0.25 adds `scripts/generate_release_manifests.py`, uses a non-circular deterministic format, and makes `static_verify.py` independently compare the exact package file set, byte sizes, and SHA-256 values. The mutation suite also verifies that omitting an artifact from the generated inventory is detected.
