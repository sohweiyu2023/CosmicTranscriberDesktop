param([string]$SourcePath=(Join-Path $PSScriptRoot '../../src/CosmicTranscriber/MainWindow.xaml.cs'))
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path $SourcePath))
$start=$source.IndexOf('    private void Window_Closing(')
$end=$source.IndexOf('    private void TryCompleteDeferredClose()', $start)
if($start -lt 0 -or $end -le $start){throw 'Actual handler boundary not found'}
$method=$source.Substring($start,$end-$start).Trim()
$generated='using System.ComponentModel;'+[Environment]::NewLine+'partial class TestWindow {'+[Environment]::NewLine+$method+[Environment]::NewLine+'}'
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'GeneratedWindow.cs'),$generated)
dotnet run --project (Join-Path $PSScriptRoot 'CloseRaceRegression.csproj') --configuration Release --nologo
if($LASTEXITCODE -ne 0){throw "Close-race regression failed: $LASTEXITCODE"}
