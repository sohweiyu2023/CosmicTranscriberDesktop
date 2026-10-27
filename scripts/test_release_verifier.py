#!/usr/bin/env python3
from __future__ import annotations

import argparse
import concurrent.futures
import os
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class Mutation:
    name: str
    relative_path: str
    old: str
    new: str


MUTATIONS = [
    Mutation("remove diarization model", "src/CosmicTranscriber/Models/TranscriptionModelCatalog.cs",
             'public const string DiarizeModel = "gpt-4o-transcribe-diarize";',
             'public const string RemovedDiarizeModel = "removed";'),
    Mutation("remove file-upload eligibility", "src/CosmicTranscriber/Models/TranscriptionModelCatalog.cs",
             '        DiarizeModel,\n', ''),
    Mutation("replace diarized response format", "src/CosmicTranscriber/Services/OpenAiTranscriptionClient.cs",
             'usesSpeakerDiarization ? "diarized_json" : "json"', '"json"'),
    Mutation("remove VAD chunking", "src/CosmicTranscriber/Services/OpenAiTranscriptionClient.cs",
             'form.Add(new StringContent("auto", Encoding.UTF8), "chunking_strategy");',
             '// chunking strategy removed'),
    Mutation("allow unsupported prompt", "src/CosmicTranscriber/Services/OpenAiTranscriptionClient.cs",
             '"The speaker-diarization model does not support the prompt field."',
             '"Prompt accepted."'),
    Mutation("send keywords to diarization", "src/CosmicTranscriber/Services/TranscriptionCoordinator.cs",
             'usesSpeakerDiarization ? null : request.Keywords', 'request.Keywords'),
    Mutation("remove speaker property parser", "src/CosmicTranscriber/Services/OpenAiTranscriptionClient.cs",
             'TryGetProperty("speaker", out var speakerElement)',
             'TryGetProperty("removed_speaker", out var speakerElement)'),
    Mutation("remove timestamp validation", "src/CosmicTranscriber/Services/OpenAiTranscriptionClient.cs",
             '|| endSeconds < startSeconds', '|| false'),
    Mutation("disable duration ceiling", "src/CosmicTranscriber/Services/TranscriptionCoordinator.cs",
             'enforceDurationLimit: true', 'enforceDurationLimit: !usesSpeakerDiarization'),
    Mutation("restore diarization preroll", "src/CosmicTranscriber/Services/TranscriptionCoordinator.cs",
             'includePreroll: !usesSpeakerDiarization', 'includePreroll: true'),
    Mutation("remove speaker checkpoint hash", "src/CosmicTranscriber/Services/TranscriptionCoordinator.cs",
             'ComputeCheckpointPayloadSha256(envelope.Text, envelope.SpeakerSegments)',
             'ComputeCheckpointPayloadSha256(envelope.Text, [])'),
    Mutation("remove checkpoint size guard", "src/CosmicTranscriber/Services/TranscriptionCoordinator.cs",
             'Encoding.UTF8.GetByteCount(content) > MaximumCheckpointBytes', 'false'),
    Mutation("remove Part label scoping", "src/CosmicTranscriber/Services/TranscriptionCoordinator.cs",
             '$"Part {chunk.Index} · {baseSpeaker}"', 'baseSpeaker'),
    Mutation("remove diarized text export", "src/CosmicTranscriber/Services/TranscriptOutputWriter.cs",
             'BuildDiarizedTextBody(result, includeTimestamps)', 'BuildTextBody(result, includeTimestamps)'),
    Mutation("remove diarization UI option", "src/CosmicTranscriber/MainWindow.xaml",
             '<ComboBoxItem Content="gpt-4o-transcribe-diarize"',
             '<ComboBoxItem Content="removed-diarization-model"'),
    Mutation("enable unsupported keyword box", "src/CosmicTranscriber/MainWindow.xaml.cs",
             'KeywordsTextBox.IsEnabled = IsUiIdle && !diarizesSpeakers;',
             'KeywordsTextBox.IsEnabled = IsUiIdle;'),
    Mutation("disable duration safety control", "src/CosmicTranscriber/MainWindow.xaml.cs",
             'ChunkDurationSlider.IsEnabled = IsUiIdle;',
             'ChunkDurationSlider.IsEnabled = IsUiIdle && !diarizesSpeakers;'),
    Mutation("replace default model", "src/CosmicTranscriber/MainWindow.xaml",
             'Content="gpt-transcribe" IsSelected="True"',
             'Content="gpt-transcribe"'),
    Mutation("remove autosave delay", "src/CosmicTranscriber/MainWindow.xaml.cs",
             'SettingsAutoSaveDelay = TimeSpan.FromMilliseconds(900)',
             'SettingsAutoSaveDelay = TimeSpan.FromHours(9)'),
    Mutation("register autosave before startup complete", "src/CosmicTranscriber/MainWindow.xaml.cs",
             '_constructionCompleted = true;\n        RegisterSettingsAutoSaveHandlers();',
             'RegisterSettingsAutoSaveHandlers();\n        _constructionCompleted = true;'),
    Mutation("remove output path autosave", "src/CosmicTranscriber/MainWindow.xaml.cs",
             'OutputPathTextBox.TextChanged += SettingsTextChanged;',
             '// output path autosave removed'),
    Mutation("remove autosave timer restart", "src/CosmicTranscriber/MainWindow.xaml.cs",
             '_settingsAutoSaveTimer.Start();',
             '// autosave timer start removed'),
    Mutation("make invalid language block all saves", "src/CosmicTranscriber/MainWindow.xaml.cs",
             'invalidFields.Add("language hints");',
             'throw;'),
    Mutation("remove invalid price isolation", "src/CosmicTranscriber/MainWindow.xaml.cs",
             'invalidFields.Add("price estimate");',
             '// invalid price ignored'),
    Mutation("remove autosave status control", "src/CosmicTranscriber/MainWindow.xaml",
             'x:Name="SettingsAutoSaveStatusText"',
             'x:Name="RemovedSettingsAutoSaveStatusText"'),
    Mutation("put API key in ordinary settings", "src/CosmicTranscriber/Models/AppSettings.cs",
             'public string OutputDirectory { get; set; } = string.Empty;',
             'public string OutputDirectory { get; set; } = string.Empty;\n    public string ApiKey { get; set; } = string.Empty;'),
    Mutation("remove durable settings publication", "src/CosmicTranscriber/Services/AppSettingsService.cs",
             'SafeFileSystem.WriteAllBytesDurableAtomic(SettingsPath, payload, "The settings file");',
             'File.WriteAllBytes(SettingsPath, payload);'),
    Mutation("stale version", "src/CosmicTranscriber/CosmicTranscriber.csproj",
             '<Version>1.0.27</Version>', '<Version>1.0.26</Version>'),
    Mutation("remove Start_Click diarization local", "src/CosmicTranscriber/MainWindow.xaml.cs",
             '        var usesSpeakerDiarization =\n            TranscriptionModelCatalog.SupportsSpeakerDiarization(selectedModel);\n',
             ''),
    Mutation("launch portable app during publish", "PUBLISH-WIN-X64.cmd",
             'start "" "%OUT%"', 'start "" "%OUT%\\CosmicTranscriber.exe"'),
    Mutation("remove runtime-log ignore", ".gitignore",
             'logs/\n', ''),
    Mutation("omit gitignore from generated inventory", "scripts/generate_release_manifests.py",
             'if relative.as_posix() in {INVENTORY_NAME, CHECKSUMS_NAME}:',
             'if relative.as_posix() in {INVENTORY_NAME, CHECKSUMS_NAME, ".gitignore"}:'),
    Mutation("restore transparent ComboBox popup", "src/CosmicTranscriber/Themes/SharedStyles.xaml",
             'AllowsTransparency="False"', 'AllowsTransparency="True"'),
    Mutation("remove model wheel guard", "src/CosmicTranscriber/MainWindow.xaml",
             'SelectionChanged="ModelComboBox_SelectionChanged"\n                                                  PreviewMouseWheel="ComboBox_PreviewMouseWheel"',
             'SelectionChanged="ModelComboBox_SelectionChanged"'),
    Mutation("disable closed ComboBox wheel suppression", "src/CosmicTranscriber/MainWindow.xaml.cs",
             'if (sender is ComboBox comboBox && !comboBox.IsDropDownOpen)',
             'if (sender is ComboBox comboBox && comboBox.IsDropDownOpen)'),
    Mutation("remove client request identifier", "src/CosmicTranscriber/Services/OpenAiTranscriptionClient.cs",
             'request.Headers.TryAddWithoutValidation("X-Client-Request-Id", clientRequestId)',
             'request.Headers.TryAddWithoutValidation("X-Removed-Request-Id", clientRequestId)'),
    Mutation("remove server request identifier logging", "src/CosmicTranscriber/Services/OpenAiTranscriptionClient.cs",
             'var requestId = ReadHeader(response, "x-request-id") ?? "unavailable";',
             'var requestId = ReadHeader(response, "x-removed-request-id") ?? "unavailable";'),
    Mutation("remove authenticated system proxy support", "src/CosmicTranscriber/Services/OpenAiTranscriptionClient.cs",
             'DefaultProxyCredentials = CredentialCache.DefaultCredentials,',
             '// default proxy credentials removed'),
    Mutation("remove regional Chinese language support", "src/CosmicTranscriber/Utilities/InputValidation.cs",
             'var isRegionalChinese = language is "zh-cn" or "zh-tw" or "zh-hk";',
             'var isRegionalChinese = false;'),
    Mutation("remove singular ISO-639-1 enforcement", "src/CosmicTranscriber/Utilities/InputValidation.cs",
             "if (language.Length != 2 || language.Any(ch => ch is < 'a' or > 'z'))",
             'if (false)'),
    Mutation("remove request elapsed diagnostics", "src/CosmicTranscriber/Services/OpenAiTranscriptionClient.cs",
             '$"elapsed_ms={elapsed.TotalMilliseconds:0}; processing_ms={processingMilliseconds}"',
             '$"processing_ms={processingMilliseconds}"'),
    Mutation("discard server request ID after response headers", "src/CosmicTranscriber/Services/OpenAiTranscriptionClient.cs",
             'serverRequestId = ReadHeader(response, "x-request-id");\n                responseStatusCode = response.StatusCode;\n                WriteResponseDiagnostics("audio-transcription", clientRequestId, response, requestTimer.Elapsed);',
             'serverRequestId = null;\n                responseStatusCode = response.StatusCode;\n                WriteResponseDiagnostics("audio-transcription", clientRequestId, response, requestTimer.Elapsed);'),
    Mutation("remove inner network exception chain", "src/CosmicTranscriber/Services/OpenAiTranscriptionClient.cs",
             'var technicalDetail = BuildTechnicalDetail(exception);',
             'var technicalDetail = InputValidation.SingleLineForLog(exception.Message, 500);'),
    Mutation("remove connection-test timeout classification", "src/CosmicTranscriber/Services/OpenAiTranscriptionClient.cs",
             'catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)\n        {\n            var diagnostic = CreateTimeoutException(\n                "connection test",',
             'catch (OperationCanceledException ex) when (false)\n        {\n            var diagnostic = CreateTimeoutException(\n                "connection test",'),
    Mutation("restore animated ComboBox popup", "src/CosmicTranscriber/Themes/SharedStyles.xaml",
             'PopupAnimation="None"', 'PopupAnimation="Fade"'),
    Mutation("remove selected-value foreground binding", "src/CosmicTranscriber/Themes/SharedStyles.xaml",
             '                                                  TextElement.Foreground="{TemplateBinding Foreground}"\n                                                  TextElement.FontFamily="{TemplateBinding FontFamily}"',
             '                                                  TextElement.FontFamily="{TemplateBinding FontFamily}"'),
    Mutation("remove dropdown-item foreground binding", "src/CosmicTranscriber/Themes/SharedStyles.xaml",
             '                                          TextElement.Foreground="{TemplateBinding Foreground}"\n                                          RecognizesAccessKey="True" />',
             '                                          RecognizesAccessKey="True" />'),
    Mutation("remove required ComboBox popup part", "src/CosmicTranscriber/Themes/SharedStyles.xaml",
             'x:Name="PART_Popup"', 'x:Name="REMOVED_Popup"'),
    Mutation("restore misleading disabled realtime option", "src/CosmicTranscriber/MainWindow.xaml",
             '                                            <ComboBoxItem Content="gpt-4o-mini-transcribe"\n                                                          ToolTip="Alternative lower-cost file transcription model." />',
             '                                            <ComboBoxItem Content="gpt-4o-mini-transcribe"\n                                                          ToolTip="Alternative lower-cost file transcription model." />\n                                            <ComboBoxItem Content="gpt-live-transcribe" IsEnabled="False" />'),
    Mutation("remove stale model-setting sanitization", "src/CosmicTranscriber/Services/AppSettingsService.cs",
             '        if (!TranscriptionModelCatalog.SupportsFileUploads(settings.Model))\n        {\n            settings.Model = TranscriptionModelCatalog.DefaultFileModel;\n            settings.CustomPricePerMinute = TranscriptionModelCatalog.DefaultPricePerMinute(settings.Model);\n        }\n',
             ''),
    Mutation("weaken Cosmic Paper control-accent contrast", "src/CosmicTranscriber/Themes/CosmicPaper.xaml",
             '#FFB47A00', '#FFD29A24'),
    Mutation("trap wheel scrolling on a closed selector", "src/CosmicTranscriber/MainWindow.xaml.cs",
             '            if (comboBox.Parent is UIElement parent)\n            {\n                parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)\n                {\n                    RoutedEvent = Mouse.MouseWheelEvent\n                });\n            }\n',
             ''),
]


def run_mutation(source: Path, verifier: Path, index: int, mutation: Mutation) -> tuple[int, bool, str]:
    try:
        with tempfile.TemporaryDirectory(prefix=f"ct-release-mut-{index:02d}-") as temp_dir:
            target_root = Path(temp_dir) / "source"
            shutil.copytree(
                source,
                target_root,
                ignore=shutil.ignore_patterns("bin", "obj", "publish", "__pycache__", ".git"))
            path = target_root / mutation.relative_path
            text = path.read_text(encoding="utf-8")
            occurrences = text.count(mutation.old)
            if occurrences != 1:
                return index, False, (
                    f"MUTATION SETUP FAILED {index}: {mutation.name}; "
                    f"expected one target, found {occurrences}")
            path.write_text(text.replace(mutation.old, mutation.new, 1), encoding="utf-8", newline="\n")
            manifest_result = subprocess.run(
                [sys.executable, str(target_root / "scripts/generate_release_manifests.py"),
                 "--root", str(target_root)],
                text=True, capture_output=True, timeout=45)
            if manifest_result.returncode != 0:
                return index, True, (
                    f"PASS {index:02d}/{len(MUTATIONS)}: {mutation.name} "
                    "(manifest generation failed safely)")
            result = subprocess.run(
                [sys.executable, str(target_root / verifier), "--root", str(target_root)],
                text=True, capture_output=True, timeout=45)
            if result.returncode == 0:
                return index, False, f"MUTATION ESCAPED {index}: {mutation.name}"
            return index, True, f"PASS {index:02d}/{len(MUTATIONS)}: {mutation.name}"
    except subprocess.TimeoutExpired:
        return index, False, f"MUTATION TIMEOUT {index}: {mutation.name}"
    except Exception as exc:  # noqa: BLE001
        return index, False, f"MUTATION ERROR {index}: {mutation.name}: {exc}"


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Mutation-test the release, compile regression, autosave, diarization, network, and packaging verifier.")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--start", type=int, default=1, help="First 1-based mutation index to run.")
    parser.add_argument("--end", type=int, default=len(MUTATIONS), help="Last 1-based mutation index to run.")
    parser.add_argument(
        "--jobs",
        type=int,
        default=min(4, os.cpu_count() or 1),
        help="Number of isolated mutations to verify concurrently (default: up to 4).")
    args = parser.parse_args()
    source = args.root.resolve()
    if args.start < 1 or args.end > len(MUTATIONS) or args.start > args.end:
        parser.error(f"Mutation range must satisfy 1 <= start <= end <= {len(MUTATIONS)}")
    if args.jobs < 1 or args.jobs > 16:
        parser.error("--jobs must be between 1 and 16")
    verifier = Path("scripts/static_verify.py")

    baseline = subprocess.run(
        [sys.executable, str(source / verifier), "--root", str(source)],
        text=True, capture_output=True, timeout=45)
    if baseline.returncode != 0:
        print("BASELINE FAILED")
        print(baseline.stdout)
        print(baseline.stderr)
        return 1

    selected = [
        (index, mutation)
        for index, mutation in enumerate(MUTATIONS, 1)
        if args.start <= index <= args.end
    ]
    results: dict[int, tuple[bool, str]] = {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=min(args.jobs, len(selected))) as executor:
        futures = {
            executor.submit(run_mutation, source, verifier, index, mutation): index
            for index, mutation in selected
        }
        for future in concurrent.futures.as_completed(futures):
            index, passed, message = future.result()
            results[index] = (passed, message)
            print(message, flush=True)

    failed = [index for index, (passed, _) in results.items() if not passed]
    if failed:
        print("FAILED MUTATIONS: " + ", ".join(str(index) for index in sorted(failed)))
        return 1

    if args.start == 1 and args.end == len(MUTATIONS):
        print(f"ALL {len(MUTATIONS)} RELEASE MUTATIONS DETECTED")
    else:
        print(f"RELEASE MUTATIONS {args.start}-{args.end} DETECTED")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
