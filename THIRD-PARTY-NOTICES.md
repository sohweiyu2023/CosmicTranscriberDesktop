# Third-party notices

The root MIT license applies only to Soh Wei Yu's original work. It does not replace any third-party license.

## Core-only candidate dependency graph (31 October 2026)

The reduced candidate references NAudio.Core 3.3.0 only, from official NuGet,
upstream commit 4f8889e2bd34c3978a25652c6c172fa1502ccba3. The unchanged
Mp3Frame parser preserves MP3 frames; no playback, MIDI or WinRT API is used.
Preserve the exact MIT grant and Copyright 2008-2026 Mark Heath in
licenses/NAudio-3.3.0-MIT.txt.

System.Numerics.Tensors remains pinned at 10.0.13, Microsoft MIT, repository
commit 4099f83977f4d29604a9aeb971fb0a7bc1d5f699. Preserve its MIT license and
third-party notices in the two corresponding licenses/System.Numerics.Tensors-10.0.13 files.

The clean reduced output excludes Microsoft.Windows.SDK.NET.dll, WinRT.Runtime.dll
and unused NAudio backends. Historical SDK conditions below apply if those
components are shipped again, not to absent files. Exact artifact review and
native acceptance are still required before public release.
## Historical NAudio 2.3.0 family

NAudio, NAudio.Asio, NAudio.Core, NAudio.Midi, NAudio.Wasapi, NAudio.WinForms and NAudio.WinMM are by Mark Heath and contributors. Restored 2.3.0 NuGet metadata declares MIT and copyright `c Mark Heath 2026`, with repository commit `c89fee940ee6f8d7374d18714a6b85d8b7a18ab0`. The upstream license at that exact commit retains `Copyright 2020 Mark Heath`; it is reproduced without rewriting in [licenses/NAudio-MIT.txt](licenses/NAudio-MIT.txt). Preserve both this notice and the full grant when distributing the assemblies.

## Microsoft Node.js Tools for Visual Studio

The Explorer-desktop COM relaunch interop in `src/CosmicTranscriber/Utilities/WindowsSecurity.cs` is adapted and modified from Microsoft's `SystemUtilities.cs` pattern. It remains under Apache License, Version 2.0; copyright (c) Microsoft. The source retains its attribution header, and [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt) is preserved. The Cosmic version changes namespace, startup/elevation handling and surrounding integration. These are project modifications, not an upstream Microsoft release. Upstream: https://github.com/microsoft/nodejstools (Apache LICENSE at repository root). The upstream root directory inspected on 21 October 2026 had no separate NOTICE file.

## Platform and runtime components

Windows DPAPI ProtectedData uses the targeted .NET Windows framework; it is not a separately referenced ProtectedData NuGet package in this source. Framework-dependent and self-contained outputs have different runtime payloads. Preserve their Microsoft/.NET license and third-party notice files when distributing runtime files.

## Historical SDK-containing candidate

The previously installed/private Windows binary candidate includes unmodified Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll from Microsoft.Windows.SDK.NET.Ref 10.0.19041.57, lib/net8.0. Copyright Microsoft Corporation. All rights reserved. They match the official package bytes, respectively SHA-256 `0ec371d93798852e36461c8adddbeadce0f963a04752f0b64e54fe19c1c834a7` and `bcf3a14e8712a90837fc5c0d8c8a24696af2bd7f74e9767597ec81ededfb23db`. They are not relicensed under Cosmic Transcriber's MIT license.

Conditional redistribution permission is listed in the [official SDK REDIST list](https://learn.microsoft.com/en-us/legal/windows-sdk/redist#microsoftwindowssdknetref), subject to the full SDK terms. Preserve the [official linked SDK license RTF](licenses/Microsoft-Windows-SDK-original.rtf), the separate [Microsoft text mirror](licenses/Microsoft-Windows-SDK.txt), and this attribution with the components. The RTF and mirror are preserved independently; their equivalence is not asserted. The reduced candidate retains these as source evidence but does not copy either SDK license into its output, because neither SDK component is shipped.

Notices do not by themselves satisfy the requirement for protective agreements with distributors and external end users. [The component-terms draft](docs/MICROSOFT-COMPONENT-TERMS-DRAFT.md) is not effective terms or evidence of assent. Publisher obligations, recipient-agreement design, runtime payload notices and separate provenance issues remain pending. That SDK-containing binary distribution remains held; assess the reduced candidate from its own exact inventory.

Icons, screenshots and other binary assets are outside the new MIT clarification because their independent provenance was not established. OpenAI service use remains subject to OpenAI terms; API access, names and trademarks are not granted by the source license.
