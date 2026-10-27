# Isolated WPF layout regression

This Windows/.NET 10 harness renders the production `MainWindow.xaml` and theme dictionaries using WPF `Measure`, `Arrange`, and `RenderTargetBitmap`. It does not run the application or its code-behind. It removes `x:Class` and event handlers, supplies only the enum needed by XAML style triggers, and seeds synthetic queue data, a synthetic output path, and status/log text. It does not open a native window, inject input, read application settings or credentials, record audio, or call a network service.

Run from the source directory in PowerShell. The following environment variables are **process scoped**. Set them before every SDK invocation; in particular, `DOTNET_GENERATE_ASPNET_CERTIFICATE=false` suppresses SDK first-run development-certificate generation without changing the certificate store or host security settings.

```powershell
$reviewRoot = Split-Path (Get-Location)
$env:DOTNET_CLI_HOME = Join-Path $reviewRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:MSBuildEnableWorkloadResolver = 'false'
$env:NUGET_PACKAGES = Join-Path $reviewRoot 'packages'

# Uses the existing local package cache. Do not add a network feed for this review.
dotnet restore .\tests\LayoutRegression\LayoutRegression.csproj --source $env:NUGET_PACKAGES -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Offline restore failed; use a machine with the required SDK/cache.' }

dotnet run --project .\tests\LayoutRegression\LayoutRegression.csproj -c Release --no-restore -- . ..\evidence\layout-final-night CosmicNight --assert-reachable
if ($LASTEXITCODE -ne 0) { throw 'Night layout assertions failed.' }
dotnet run --project .\tests\LayoutRegression\LayoutRegression.csproj -c Release --no-build --no-restore -- . ..\evidence\layout-final-paper CosmicPaper --assert-reachable
if ($LASTEXITCODE -ne 0) { throw 'Paper layout assertions failed.' }
```

The first argument is the source tree to inspect; the second is the evidence directory. An optional theme is `CosmicNight` (default) or `CosmicPaper`. `--quick` limits the matrix to 960 x 620 and 640 x 420 DIPs at 96 raster DPI. `--assert-reachable` returns a nonzero exit code for any required missing, zero-size, clipped, or incorrectly colored Start-button text. Omitting that flag produces evidence without a pass/fail exit code, useful for examining a known-broken baseline.

For the bounded nested-scrolling regression, use `--diagnostic-case --repeat=3
--assert-reachable`. This runs default/expanded states at 780 x 500 DIPs and
96 raster DPI three times per selected theme. `--repeat=N` accepts 1 through 10.
Use `--single-reveal` to reproduce the original one-request measurement.
Add `--seed-diagnostic-scroll` to place the inner viewport at its ordinary
midpoint and outer viewport at the top before revealing the diagnostic button.
The bounded 30-seed probe reproduced a 0.67-DIP edge clip from this state; a
second reveal exposed the full bounds. This does not change the XAML or scale.

The reachability check now permits at most three ordinary `BringIntoView`
requests, attempting to reveal full bounds before applying the original
acceptance threshold. At fractional WPF layout DPI, a nested ScrollViewer's first reveal
can leave a small edge clipped, even though another reveal or ordinary manual
scroll fully exposes it. Merely running another layout pass did not correct the
observed case. Each JSON records the first bounds/visible bounds, final bounds,
and request count in `revealMeasurements`; the 98%/1-DIP threshold is unchanged.
Persistent clipping, missing and zero-size controls still fail. This evaluates
whether scrolling can reveal a control, not whether the first focus/reveal
request always aligns its bounds perfectly. Keep the earlier broken-layout
negative control when changing this check.

The full matrix uses six **client-area** sizes: 1060 x 700, 1044 x 660, 960 x 620, 944 x 600, 780 x 500, and 640 x 420 DIPs. These exclude native title bars and borders; 1044 x 660 and 944 x 600 approximate usable client areas of larger outer windows. For each size, two scenarios run: a populated default queue, and expanded Activity log with the longest listed model (`gpt-4o-transcribe-diarize`) and longer status text. Raster densities are 96, 144, and 192 DPI.

`layoutDpi` records the actual WPF process layout DPI separately. Changing `RenderTargetBitmap` density does **not** change monitor DPI or emulate a per-monitor DPI transition. The smaller client-area cases model reduced effective work area, not a real monitor configuration. Window inherited layout rounding, pixel snapping, font, text rendering, and foreground are copied to the off-screen surface.

Assertions use an explicit inventory of source controls and labels, excluding deliberately hidden elements, intentionally ellipsized text, and collapsed log descendants in the default scenario. Each expected element is brought into view through ordinary in-memory WPF scrolling. It passes when at least 98% of its bounds is visible, or all clipped edges are within 1 DIP (rounding tolerance); missing/zero-size elements always fail. JSON records the bounds, clipped bounds, scroll ancestors, and failures. Start-button generated text must match its intended foreground in both enabled and disabled states for each theme.

This establishes **control-bounds reachability**, not all text fit inside control templates, keyboard or screen-reader usability, native chrome behavior, actual pointer scrolling, or application lifecycle behavior. Review the PNGs, including selected model/log/Start scroll positions, for internal text fit. Overlap candidates refer only to the initial viewport; they are evidence, not an exhaustive overlap assertion at every scroll position. Decorative background ellipses are excluded. No real macOS, Safari, or monitor DPI validation is implied.

For a negative control, point the same current harness at an unchanged source tree with `--quick --assert-reachable`, write to a separate evidence directory, and confirm it exits nonzero with concrete clipped controls and/or foreground failures. Do not replace the baseline sources with the candidate.
