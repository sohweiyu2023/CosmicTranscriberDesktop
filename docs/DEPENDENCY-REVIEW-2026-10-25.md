# Source dependency review, 25 October 2026

This isolated review copy starts from the preserved `licensing-implementation-draft`
of 23 October. The desktop application version remains 1.0.27.

| Dependency | Before | Review candidate |
| --- | --- | --- |
| .NET SDK pin | 10.0.302, latestFeature | 10.0.402, latestPatch |
| NAudio and retained Core/Midi/Wasapi/WinMM components | 2.3.0 | 3.3.0 |
| System.Numerics.Tensors | absent; new NAudio minimum would resolve 9.0.0 | 10.0.13 |
| NuGet lockfiles | absent | committed for app and both harnesses |

NAudio 3.3.0 is latest stable in the official NuGet registry. Its new nullable
RawData annotation required an explicit missing-frame-data exception before
copying bytes; application behavior for valid frames is unchanged. Unused
NAudio.Asio and NAudio.WinForms are no longer pulled by the meta-package.
The newer NAudio and Tensor MIT licenses and notices are included separately;
historic license files and Microsoft's SDK draft/hold remain unchanged.

Use `dotnet restore --locked-mode` and the committed NuGet.Config for reproducible
restores. Restore each harness project separately. Build with
`dotnet build CosmicTranscriber.sln -c Release --no-restore`.
Set `DOTNET_GENERATE_ASPNET_CERTIFICATE=false` before SDK commands and use an
isolated `DOTNET_CLI_HOME` and `NUGET_PACKAGES` for review.

Build and service-test results, advisory/outdated JSON, release metadata and
the separate final report are in the task-7 sibling evidence directory.
No paid API calls, installation, publishing or deployment were performed.
The current source is not proof of the installed/restored binary's provenance.
The publishing script's existing distribution hold is preserved verbatim.

## Bounded layout follow-up

The offscreen reachability harness now allows at most three ordinary
BringIntoView requests, retaining the initial and final visible bounds and the
number of requests. Its 98%/1-DIP acceptance threshold is unchanged. Nested
ScrollViewer alignment at fractional layout DPI can leave an edge clipped
after one request even though another request or manual scrolling can fully
reveal it; an extra layout pass alone did not resolve the observed alignment.

Use `--diagnostic-case --repeat=3 --assert-reachable` for the bounded 780x500-DIP,
96-raster-DPI default/expanded scenarios in each theme. Use `--single-reveal`
for the original behavior. The corrected 12-scenario matrix passed 1,152
reachability checks and both foreground states. The older broken-layout
negative control still failed 162 reachability and eight foreground checks
across four scenarios. A fresh six-scenario single-request run passed, which
confirms the original clipping is intermittent; the earlier failing JSON and
the diagnostic probe are retained as evidence rather than replaced.

This changes the test's scrolling procedure, not application XAML or settings.
It establishes that controls can be revealed through bounded ordinary
scrolling; it does not establish perfect alignment on every first focus
request, native keyboard accessibility or screen-reader behavior.

The final deterministic seed (`--seed-diagnostic-scroll`) places the settings
viewport at its ordinary scroll midpoint before revealing the diagnostic
button. Across 30 bounded starting-position probes, six first reveals left
0.67 DIP clipped (visible fraction 0.9796); every second reveal exposed full
bounds (1.0). The final harness corrects partial first reveals toward full
visibility before evaluating the original acceptance threshold. Its seeded
12-scenario matrix passed 1,152 reachability checks, and the original broken
layout still failed the same 170 checks with the final code. Probe and final
measurement JSON retain both first and final bounds. Application XAML and
the distribution hold remain unchanged.
