# Close confirmation reentrancy regression

Run `./Run.ps1` from PowerShell. It extracts the actual `Window_Closing` handler from the app source, then compiles it with synthetic modal/cancellation/persistence stubs. No app assembly, WPF window, network, profile or filesystem persistence service is loaded. `GeneratedWindow.cs` is local generated input and must not be published.

The confirmation callback simulates asynchronous work finishing or being replaced while the nested dispatcher loop is open. Cases cover each of run/import/key-test/support-export flags, Yes/No, still-active work and ordinary idle close.

Negative control: pass `-SourcePath` pointing to the unchanged PR3 `MainWindow.xaml.cs`; it must fail at the completed-during-confirmation assertion. Afterwards rerun without that argument to restore generated input to the corrected source. Do not use direct `dotnet run` as the authoritative check because it can reuse a previously generated handler.

This source-level regression is not corrected-build native acceptance.
