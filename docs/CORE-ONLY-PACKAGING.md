# Core-only packaging candidate — 31 October 2026

This isolated source candidate is based on public main b6246e8972ffb0c90b6687acfd22acdc73e4c83d. It has not replaced the verified private installation (DLL a13407c6a8eb3640baa2d88555147e2be3de2b7b05fdef3ce57de2725947b583), and no public binary has been uploaded.

Only Mp3Frame parsing in Mp3Chunker and Mp3AudioInspector requires NAudio. NAudio.Core 3.3.0 supplies that API without the unused audio/MIDI backends. Tensors stays 10.0.13. Versionless net10.0-windows is the documented WPF framework shape; SDK 10.0.403 built the candidate with zero warnings/errors. Windows 10 build 19041 or later remains product policy. The explicit SupportedOSPlatform declaration preserves analyzer metadata, not runtime minimum-version enforcement. The existing manifest, asInvoker and PerMonitorV2 remain unchanged. No WinRT call was substituted or silently disabled.

The clean restore has only Core and Tensors package dependencies. The reduced output omits Microsoft.Windows.SDK.NET.dll, WinRT.Runtime.dll and unused NAudio backends; historical SDK terms remain source evidence and still apply if those files are shipped again. Neither assembly was manually deleted from output. Licenses copied into the current app output are allowlisted to active MIT/Tensors notices and the Apache-2.0 attribution for adapted WindowsSecurity source. Framework-dependent packaging also requires the matching .NET apphost notices; no runtime/installer or user artwork is bundled.

Focused offline evidence: Desktop 88 checks (including 22 production chunk frame/VBR/preroll assertions), Safety 79, PendingRecovery 92 and NormalSettings 13. All HTTP responses are fake. Three actual synthetic production chunks decode with FFmpeg with exit 0 and empty error logs. The first recovery command omitted its required long fixture and stopped; its corrected invocation used a fresh output directory and passed. This was harness invocation error, not an app defect.

Remaining gates: exact artifact and notice review, coordinated synthetic native startup/chunk/export smoke of the reduced framework/dependency graph, and release approval. Offline tests are not native acceptance or configured CI. No provider request, real profile mutation, installation, public release or legal acceptance is authorized by this document. Five withheld artwork/manual/preview assets, PDBs, fixtures, credentials, profiles and private evidence must remain excluded. Self-contained output would have a different inventory and is not validated by this framework-dependent plan. PUBLISH-WIN-X64 remains blocked.

Official references:
- https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop
- https://www.nuget.org/packages/NAudio.Core/3.3.0
- https://raw.githubusercontent.com/naudio/NAudio/4f8889e2bd34c3978a25652c6c172fa1502ccba3/src/NAudio.Core/NAudio.Core.csproj
- https://learn.microsoft.com/en-us/legal/windows-sdk/redist#microsoftwindowssdknetref
