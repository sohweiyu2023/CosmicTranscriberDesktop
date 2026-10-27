# Third-party notices

The root MIT license applies only to Soh Wei Yu's original work. It does not replace any third-party license.

## Current source dependency graph (25 October 2026)

The dependency review source uses NAudio, NAudio.Core, NAudio.Midi, NAudio.Wasapi
and NAudio.WinMM 3.3.0, from official NuGet packages. Package metadata identifies
upstream commit `4f8889e2bd34c3978a25652c6c172fa1502ccba3` and MIT licensing.
The exact upstream license, including `Copyright 2008-2026 Mark Heath`, is
preserved in [licenses/NAudio-3.3.0-MIT.txt](licenses/NAudio-3.3.0-MIT.txt).
NAudio 3 no longer brings in the unused Asio, Dmo and WinForms packages.

System.Numerics.Tensors 10.0.13 is explicitly pinned to bring NAudio.Core's
transitive dependency to the current .NET 10 servicing release. It is by
Microsoft, under MIT, from official NuGet metadata repository commit
`4099f83977f4d29604a9aeb971fb0a7bc1d5f699`. Preserve the accompanying
[MIT license](licenses/System.Numerics.Tensors-10.0.13-MIT.txt) and
[third-party notices](licenses/System.Numerics.Tensors-10.0.13-THIRD-PARTY-NOTICES.txt).

All historic licensing/provenance evidence below describes earlier artifacts.
Those artifacts were not replaced, and the distribution hold remains active.

## Historical NAudio 2.3.0 family

NAudio, NAudio.Asio, NAudio.Core, NAudio.Midi, NAudio.Wasapi, NAudio.WinForms and NAudio.WinMM are by Mark Heath and contributors. Restored 2.3.0 NuGet metadata declares MIT and copyright `c Mark Heath 2026`, with repository commit `c89fee940ee6f8d7374d18714a6b85d8b7a18ab0`. The upstream license at that exact commit retains `Copyright 2020 Mark Heath`; it is reproduced without rewriting in [licenses/NAudio-MIT.txt](licenses/NAudio-MIT.txt). Preserve both this notice and the full grant when distributing the assemblies.

## Microsoft Node.js Tools for Visual Studio

The Explorer-desktop COM relaunch interop in `src/CosmicTranscriber/Utilities/WindowsSecurity.cs` is adapted and modified from Microsoft's `SystemUtilities.cs` pattern. It remains under Apache License, Version 2.0; copyright (c) Microsoft. The source retains its attribution header, and [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt) is preserved. The Cosmic version changes namespace, startup/elevation handling and surrounding integration. These are project modifications, not an upstream Microsoft release. Upstream: https://github.com/microsoft/nodejstools (Apache LICENSE at repository root). The upstream root directory inspected on 21 October 2026 had no separate NOTICE file.

## Platform and runtime components

Windows DPAPI ProtectedData uses the targeted .NET Windows framework; it is not a separately referenced ProtectedData NuGet package in this source. Framework-dependent and self-contained outputs have different runtime payloads. Preserve their Microsoft/.NET license and third-party notice files when distributing runtime files.

The Windows binary candidate includes unmodified Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll from Microsoft.Windows.SDK.NET.Ref 10.0.19041.57, lib/net8.0. Copyright Microsoft Corporation. All rights reserved. They match the official package bytes, respectively SHA-256 `0ec371d93798852e36461c8adddbeadce0f963a04752f0b64e54fe19c1c834a7` and `bcf3a14e8712a90837fc5c0d8c8a24696af2bd7f74e9767597ec81ededfb23db`. They are not relicensed under Cosmic Transcriber's MIT license.

Conditional redistribution permission is listed in the [official SDK REDIST list](https://learn.microsoft.com/en-us/legal/windows-sdk/redist#microsoftwindowssdknetref), subject to the full SDK terms. Preserve the [official linked SDK license RTF](licenses/Microsoft-Windows-SDK-original.rtf), the separate [Microsoft text mirror](licenses/Microsoft-Windows-SDK.txt), and this attribution with the components. The RTF and mirror are preserved independently; their equivalence is not asserted. The existing MSBuild license-copy rule covers both files.

Notices do not by themselves satisfy the requirement for protective agreements with distributors and external end users. [The component-terms draft](docs/MICROSOFT-COMPONENT-TERMS-DRAFT.md) is not effective terms or evidence of assent. Publisher obligations, recipient-agreement design, runtime payload notices and separate provenance issues remain pending. This source-only draft is held from binary distribution.

Icons, screenshots and other binary assets are outside the new MIT clarification because their independent provenance was not established. OpenAI service use remains subject to OpenAI terms; API access, names and trademarks are not granted by the source license.
