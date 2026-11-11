> **1.0.29 tutorial candidate:** See docs/TUTORIAL-CANDIDATE-2026-11-11.md for current native and automated evidence. The earlier release findings below are historical and are not claimed as new acceptance.

# Cosmic Transcriber 1.0.27 — QA Report

> **1.0.28 note (10 November 2026):** the selector fixed here still failed to open on mouse click in field builds; see CHANGELOG 1.0.28 for the reproduced cause, fix and real-input acceptance. The findings below are the historical 1.0.27 record.

**Result:** SOURCE-LEVEL PASS; NATIVE WINDOWS ACCEPTANCE PENDING

## Corrective checks

- Selected `ComboBox` value has an explicit themed foreground and opaque themed surface.
- Popup retains `PART_Popup`, uses no transparency or animation, and contains a bounded scroll viewer plus items presenter.
- Dropdown items explicitly render their themed foreground and selected/highlighted states.
- Model selector contains four enabled file models and no disabled realtime-only row.
- Unknown/stale saved model IDs normalize to the default file model with a matching estimate.
- Cosmic Paper normal-sized text foregrounds meet at least 4.5:1 against the hover surface; progress/control accent meets at least 3:1 against light control surfaces.
- Closed model and retry dropdowns suppress value changes and forward the wheel gesture to the surrounding settings page.

## Automated evidence

- Static verifier: passed after final manifest generation.
- Adversarial mutation suite: 52/52 detected.
- C# source files: 28.
- XAML files: 7.
- Unique handlers/bindings: 39/44.
- Resolved resources: 52.
- Secret and runtime-artifact scan: passed.
- Fresh CBR/VBR media smoke test: six of six generated chunks decoded; first-chunk PCM prefixes matched.
- Deterministic inventory/checksums and clean extraction: passed.
- Comprehensive 16-page manual render/accessibility audit: passed with zero findings.

## Limitation

No .NET/WPF compiler or Windows desktop is available in this audit environment. `BUILD.cmd` and the release checklist remain mandatory before publishing executables.
