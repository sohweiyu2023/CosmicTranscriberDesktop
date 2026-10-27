# Live Theme palette correction

The Theme button previously persisted its new value while existing controls kept the old palette until reopening. `App.ApplyTheme` assumed `Resources.MergedDictionaries[0]` was the Cosmic palette. WPF Fluent can insert its own dictionary at index zero; replacing that entry left the old Cosmic dictionary later in the collection, where it continued to win resource lookup. Repeated switching accumulated stale Cosmic dictionaries.

`App` now retains the actual application-palette dictionary reference and replaces that owned entry at its current index. It leaves the Fluent, SharedStyles and unrelated dictionaries intact. It loads the replacement before changing the active collection; storage, profile selection, permissions and provider behavior are unchanged.

`tests/ThemeRegression` links the production `App.xaml.cs` methods and compiled real palettes/styles, with stub diagnostics/MainWindow. It creates a never-shown Window logical tree to exercise existing controls' dynamic resource updates; it never invokes the app Startup handler or real profile/provider code. Its assertions cover both directions, repeated/same-theme selection, case normalization/default selection, every palette color, existing gradient/card/text resources and retained dictionary identities/counts.

The final identical harness passes233checks with the fix and fails113of233 with the preserved baseline App source. The earlier detached-tree fixture is historical and is not the final result. A full native window check is still required to certify the app's actual live Theme pixels; this offscreen regression is not native acceptance.

Microsoft WPF source explaining Fluent resource insertion: https://github.com/dotnet/wpf/blob/v10.0.0/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/ThemeManager.cs (AddOrUpdateThemeResources). Application.ThemeMode also documents its resource synchronization: https://github.com/dotnet/wpf/blob/v10.0.0/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Application.cs. These framework sources support the mechanism; the regression against the installed toolchain reproduces the actual ordering failure.

Run from a developer environment using the existing supported .NET toolchain:

    dotnet run --project tests/ThemeRegression/ThemeRegression.csproj -c Release

For the negative control, build the same project with ThemeAppSource set to the preserved baseline App.xaml.cs path, then execute that test binary once. Do not execute historical application binaries or use a negative-control app build as an everyday payload.
