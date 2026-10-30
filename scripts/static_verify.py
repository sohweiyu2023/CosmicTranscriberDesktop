#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import BadZipFile, ZipFile

EXPECTED_VERSION = "1.0.27"
IGNORED_PARTS = {"bin", "obj", "publish", "__pycache__", ".git"}
TEXT_SUFFIXES = {".cs", ".xaml", ".json", ".md", ".cmd", ".props", ".sln", ".py", ".html"}

parser = argparse.ArgumentParser(description="Verify Cosmic Transcriber release, autosave, diarization, and package integrity.")
parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
parser.add_argument("--source-only", action="store_true", help="Validate the public source export; withheld media is not a release payload.")
args = parser.parse_args()
root = args.root.resolve()
src = root / "src" / "CosmicTranscriber"
errors: list[str] = []


def read(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except Exception as exc:  # noqa: BLE001
        errors.append(f"Cannot read {path.relative_to(root)}: {exc}")
        return ""


def require(condition: bool, message: str) -> None:
    if not condition:
        errors.append(message)


def strip_csharp_comments_and_strings(text: str) -> str:
    out: list[str] = []
    i = 0
    state = "code"
    while i < len(text):
        ch = text[i]
        nxt = text[i + 1] if i + 1 < len(text) else ""
        if state == "code":
            if ch == "/" and nxt == "/":
                state = "line_comment"; out.extend("  "); i += 2; continue
            if ch == "/" and nxt == "*":
                state = "block_comment"; out.extend("  "); i += 2; continue
            if ch == '"':
                # Detect ordinary/interpolated/verbatim strings well enough for delimiter checks.
                state = "verbatim_string" if i > 0 and text[i - 1] == "@" else "string"
                out.append(" "); i += 1; continue
            if ch == "'":
                state = "char"; out.append(" "); i += 1; continue
            out.append(ch); i += 1; continue
        if state == "line_comment":
            if ch == "\n": state = "code"; out.append("\n")
            else: out.append(" ")
            i += 1; continue
        if state == "block_comment":
            if ch == "*" and nxt == "/": state = "code"; out.extend("  "); i += 2
            else: out.append("\n" if ch == "\n" else " "); i += 1
            continue
        if state == "string":
            if ch == "\\": out.extend("  "); i += 2; continue
            if ch == '"': state = "code"
            out.append("\n" if ch == "\n" else " "); i += 1; continue
        if state == "verbatim_string":
            if ch == '"' and nxt == '"': out.extend("  "); i += 2; continue
            if ch == '"': state = "code"
            out.append("\n" if ch == "\n" else " "); i += 1; continue
        if state == "char":
            if ch == "\\": out.extend("  "); i += 2; continue
            if ch == "'": state = "code"
            out.append("\n" if ch == "\n" else " "); i += 1
    return "".join(out)


def balanced_csharp(path: Path) -> bool:
    stripped = strip_csharp_comments_and_strings(read(path))
    pairs = {"}": "{", ")": "(", "]": "["}
    stack: list[str] = []
    for ch in stripped:
        if ch in "{([": stack.append(ch)
        elif ch in pairs:
            if not stack or stack.pop() != pairs[ch]: return False
    return not stack


def parse_argb_color(text: str, key: str) -> tuple[int, int, int] | None:
    match = re.search(rf'<Color x:Key="{re.escape(key)}">#(?:[0-9A-Fa-f]{{2}})?([0-9A-Fa-f]{{6}})</Color>', text)
    if match is None:
        errors.append(f"Missing color resource: {key}")
        return None
    value = match.group(1)
    return tuple(int(value[index:index + 2], 16) for index in (0, 2, 4))


def relative_luminance(rgb: tuple[int, int, int]) -> float:
    channels = []
    for component in rgb:
        value = component / 255.0
        channels.append(value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4)
    return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2]


def contrast_ratio(first: tuple[int, int, int], second: tuple[int, int, int]) -> float:
    first_luminance = relative_luminance(first)
    second_luminance = relative_luminance(second)
    lighter = max(first_luminance, second_luminance)
    darker = min(first_luminance, second_luminance)
    return (lighter + 0.05) / (darker + 0.05)

# Package/version basics.
csproj = read(src / "CosmicTranscriber.csproj")
manifest = read(src / "app.manifest")
app_version = read(src / "Utilities" / "AppVersion.cs")
main_xaml = read(src / "MainWindow.xaml")
main_cs = read(src / "MainWindow.xaml.cs")
model_catalog = read(src / "Models" / "TranscriptionModelCatalog.cs")
models = read(src / "Models" / "TranscriptionModels.cs")
client = read(src / "Services" / "OpenAiTranscriptionClient.cs")
input_validation = read(src / "Utilities" / "InputValidation.cs")
shared_styles = read(src / "Themes" / "SharedStyles.xaml")
paper_theme = read(src / "Themes" / "CosmicPaper.xaml")
coordinator = read(src / "Services" / "TranscriptionCoordinator.cs")
chunker = read(src / "Services" / "Mp3Chunker.cs")
writer = read(src / "Services" / "TranscriptOutputWriter.cs")
settings_service = read(src / "Services" / "AppSettingsService.cs")
app_settings = read(src / "Models" / "AppSettings.cs")
safe_fs = read(src / "Utilities" / "SafeFileSystem.cs")
publish_cmd = read(root / "PUBLISH-WIN-X64.cmd")
gitignore = read(root / ".gitignore")
readme = read(root / "README.md")
changelog = read(root / "CHANGELOG.md")
audit_report = read(root / "AUDIT-REPORT.md")
qa_report = read(root / "QA-REPORT.md")
design_notes = read(root / "DESIGN-NOTES.md")
user_guide = read(root / "docs" / "USER-GUIDE.md")
troubleshooting = read(root / "docs" / "TROUBLESHOOTING.md")
developer_guide = read(root / "docs" / "DEVELOPER-GUIDE.md")
release_checklist = read(root / "docs" / "RELEASE-CHECKLIST.md")
preview_html_path = root / "docs" / f"UI_PREVIEW-{EXPECTED_VERSION}.html"
preview_png_path = root / "docs" / f"UI_PREVIEW-{EXPECTED_VERSION}.png"
manual_path = root / "docs" / f"CosmicTranscriber-{EXPECTED_VERSION}-Comprehensive-Manual.docx"

require(f"<Version>{EXPECTED_VERSION}</Version>" in csproj, f"Project Version is not {EXPECTED_VERSION}")
require(f"<AssemblyVersion>{EXPECTED_VERSION}.0</AssemblyVersion>" in csproj, "AssemblyVersion is stale")
require(f'version="{EXPECTED_VERSION}.0"' in manifest, "Application manifest identity is stale")
require('ResolveDisplayVersion()' in app_version and f'<InformationalVersion>{EXPECTED_VERSION}</InformationalVersion>' in csproj, "Displayed version source is stale")
require('Title="Cosmic Transcriber"' in main_xaml and f'Text="v{EXPECTED_VERSION}"' in main_xaml
        and 'Title = $"Cosmic Transcriber {AppVersion.DisplayVersion}";' in main_cs,
        "Main window title/header version is stale")
require(EXPECTED_VERSION in readme
        and "## 1.0.27 - dropdown contrast, model-list clarity, and settings hardening" in changelog
        and "## 1.0.26 - independent dropdown/network re-audit and diagnostic hardening" in changelog
        and "## 1.0.25 - compile-blocker repair and release-gate hardening" in changelog
        and "## 1.0.24 - debounced settings autosave" in changelog
        and "## 1.0.23 - speaker-labelled transcription" in changelog,
        "Current release documentation is missing")
for name, content in (("AUDIT-REPORT.md", audit_report), ("QA-REPORT.md", qa_report), ("DESIGN-NOTES.md", design_notes),
                      ("docs/USER-GUIDE.md", user_guide), ("docs/TROUBLESHOOTING.md", troubleshooting),
                      ("docs/DEVELOPER-GUIDE.md", developer_guide), ("docs/RELEASE-CHECKLIST.md", release_checklist)):
    require(EXPECTED_VERSION in content, f"{name} does not identify the current release")
require("900" in user_guide and "Settings autosave" in user_guide and "gpt-4o-transcribe-diarize" in user_guide,
        "User guide does not cover autosave and diarization")
require("test_release_verifier.py" in developer_guide and "51" in developer_guide,
        "Developer guide does not identify the current release mutation gate")
require("generate_release_manifests.py" in developer_guide
        and "generate_release_manifests.py" in release_checklist,
        "Release documentation does not describe deterministic manifest generation")
if args.source_only:
    require((root / "WITHHELD-ASSETS.json").is_file(), "Source export must document withheld assets")
    require(not preview_html_path.exists() and not preview_png_path.exists() and not manual_path.exists(),
            "Withheld preview/media must not be in the source export")
    require(not (src / "Assets" / "CosmicTranscriber.ico").exists()
            and not (src / "Assets" / "CosmicTranscriber.png").exists(), "Rights-pending artwork must not be in the source export")
else:
    require(preview_html_path.is_file() and preview_png_path.is_file(), "Current UI preview HTML/PNG is missing")
require(not (root / "docs" / "UI_PREVIEW-1.0.22.html").exists() and not (root / "docs" / "UI_PREVIEW-1.0.22.png").exists(),
        "Stale 1.0.22 UI preview remains in the live package")
if preview_html_path.is_file():
    preview_html = read(preview_html_path)
    require(f"v{EXPECTED_VERSION}" in preview_html and "Settings autosave" in preview_html,
            "Current UI preview does not show version/autosave state")
if not args.source_only:
    require(manual_path.is_file(), "Current comprehensive DOCX manual is missing")
require(not (root / "docs" / "CosmicTranscriber-1.0.22-Comprehensive-Manual.docx").exists(),
        "Stale 1.0.22 comprehensive manual remains in the live package")
if manual_path.is_file():
    try:
        with ZipFile(manual_path) as archive:
            document_xml = archive.read("word/document.xml").decode("utf-8", errors="strict")
            core_xml = archive.read("docProps/core.xml").decode("utf-8", errors="strict")
            require(EXPECTED_VERSION in document_xml and "Settings autosave" in document_xml and "gpt-4o-transcribe-diarize" in document_xml,
                    "DOCX manual content is stale or incomplete")
            require(EXPECTED_VERSION in core_xml, "DOCX core metadata is stale")
            require(f"Cosmic Transcriber v{EXPECTED_VERSION} interface with settings autosave" in document_xml,
                    "DOCX embedded preview accessibility title is stale")
    except (BadZipFile, KeyError, UnicodeDecodeError) as exc:
        errors.append(f"Cannot validate DOCX manual: {exc}")
for obsolete in ("comprehensive_verify.py", "static_verify_1_0_22.py", "test_static_verifier_1_0_22.py", "verify_diarization_update.py"):
    require(not (root / "scripts" / obsolete).exists(), f"Obsolete verifier remains in live package: scripts/{obsolete}")

# Build-blocker and clean-publish regression checks.
start_click_declaration = (
    "var selectedModel = CurrentModel;\n"
    "        var usesSpeakerDiarization =\n"
    "            TranscriptionModelCatalog.SupportsSpeakerDiarization(selectedModel);"
)
require(start_click_declaration in main_cs,
        "Start_Click uses speaker-diarization state without a scoped declaration (CS0103 regression)")
require(main_cs.index(start_click_declaration) < main_cs.index('(usesSpeakerDiarization ? " in speaker-labelled mode"'),
        "Start_Click declares the speaker-diarization state after it is used")
require('start "" "%OUT%"' in publish_cmd
        and 'start "" "%OUT%\\CosmicTranscriber.exe"' not in publish_cmd
        and "the app itself will not be launched" in publish_cmd.lower()
        and "zip or copy this folder before the first app launch" in publish_cmd.lower(),
        "Publish script can launch/contaminate the portable app or does not explain clean sharing")
for runtime_ignore in ("logs/", "CosmicTranscriber-settings/", "*.log", "openai-key.protected",
                       "*.transcript.txt", "*.transcript.md"):
    require(runtime_ignore in gitignore.splitlines(), f".gitignore omits runtime/private artifact: {runtime_ignore}")
require("BUILD.cmd" in release_checklist and "mandatory compiler gate" in release_checklist.lower(),
        "Release checklist does not distinguish the compiler gate from source-only verification")
require("zip or copy" in release_checklist.lower() and "before first launch" in release_checklist.lower(),
        "Release checklist does not protect clean portable-build sharing")

# Settings autosave and persistence boundaries.
require("SettingsAutoSaveDelay = TimeSpan.FromMilliseconds(900)" in main_cs
        and "DispatcherTimer _settingsAutoSaveTimer" in main_cs
        and "SettingsAutoSaveTimer_Tick" in main_cs,
        "Debounced settings autosave is missing")
require(main_cs.index("_constructionCompleted = true;") < main_cs.index("RegisterSettingsAutoSaveHandlers();"),
        "Autosave handlers can run while startup settings are still being applied")
for token in ("OutputPathTextBox.TextChanged", "LanguageTextBox.TextChanged", "KeywordsTextBox.TextChanged",
              "PromptTextBox.TextChanged", "CombinedTitleTextBox.TextChanged", "RetryComboBox.SelectionChanged",
              "ExportTextCheckBox", "ExportMarkdownCheckBox", "IncludeTimestampsCheckBox", "MergeFilesCheckBox",
              "PreviousContextCheckBox", "ContinueOnErrorCheckBox", "RememberKeyCheckBox"):
    require(token in main_cs, f"Autosave does not observe {token}")
require("ScheduleSettingsAutoSave();" in main_cs
        and "_settingsAutoSaveTimer.Stop();" in main_cs
        and "_settingsAutoSaveTimer.Start();" in main_cs,
        "Autosave debounce scheduling is incomplete")
require("CaptureSettingsForPersistence" in main_cs
        and 'invalidFields.Add("language hints")' in main_cs
        and 'invalidFields.Add("keyword hints")' in main_cs
        and 'invalidFields.Add("price estimate")' in main_cs,
        "Partial-invalid settings capture is missing")
require("_settingsService.Save(_settings, writeDiagnostic: !isAutoSave);" in main_cs
        and "public void Save(AppSettings settings, bool writeDiagnostic = true)" in settings_service,
        "Autosave cannot suppress repetitive success diagnostics")
require("WriteAllBytesDurableAtomic(SettingsPath" in settings_service
        and "File.Move(temporaryPath, targetPath, overwrite: true)" in safe_fs,
        "Settings persistence no longer uses the durable temporary-file publication path")
require('x:Name="SettingsAutoSaveStatusText"' in main_xaml
        and "Valid options save after a short pause" in main_xaml
        and "Saved automatically" in main_cs,
        "Visible autosave state or persistence-boundary guidance is missing")
require("ApiKey" not in app_settings and "Password" not in app_settings,
        "An API key or password field was added to ordinary settings")
require("queue, progress" in readme.lower() and "session-only api key" in readme.lower(),
        "README does not explain settings persistence boundaries")

# Model and API contract.
require('public const string DefaultFileModel = "gpt-transcribe";' in model_catalog,
        "gpt-transcribe is no longer the default")
require('public const string DiarizeModel = "gpt-4o-transcribe-diarize";' in model_catalog,
        "Diarization model constant is missing")
file_model_list = model_catalog.split("private static readonly string[] FileModelIds", 1)[1].split("public static IReadOnlyList<string> FileModels", 1)[0]
require("DiarizeModel" in file_model_list, "Diarization model cannot enter the file-upload path")
require("public static bool SupportsSpeakerDiarization" in model_catalog
        and "SupportsSpeakerDiarization(model)" in model_catalog,
        "Speaker-diarization capability detection is missing")
require("public static bool SupportsPrompt" in model_catalog
        and "!SupportsSpeakerDiarization(model)" in model_catalog,
        "Diarization prompt exclusion is missing")
require("public static bool SupportsPreviousContext" in model_catalog,
        "Previous-context capability isolation is missing")
require("DiarizeModel" in model_catalog.split("DefaultPricePerMinute", 1)[1]
        and "editable estimate, not a quote" in model_catalog,
        "Diarization cost estimate is missing or presented as authoritative")
require("FileModelIds" in model_catalog and "FileModels" in model_catalog
        and "Contains(model, StringComparer.OrdinalIgnoreCase)" in model_catalog,
        "The selectable file-model catalogue is not centralized or case-insensitive")
require("gpt-live-transcribe" not in main_xaml and "LiveModel" not in model_catalog,
        "A realtime-only model is still presented in the completed-file selector/catalogue")
require("settings.SettingsSchemaVersion = 4;" in settings_service
        and "if (!TranscriptionModelCatalog.SupportsFileUploads(settings.Model))" in settings_service
        and "settings.CustomPricePerMinute = TranscriptionModelCatalog.DefaultPricePerMinute(settings.Model);" in settings_service,
        "Unknown or stale model settings are not normalized to a supported file model and matching estimate")

require('usesSpeakerDiarization ? "diarized_json" : "json"' in client,
        "Diarization does not request diarized_json")
require('new StringContent("auto", Encoding.UTF8), "chunking_strategy"' in client,
        "Required automatic VAD chunking is missing")
require("The speaker-diarization model does not support the prompt field." in client,
        "Unsupported diarization prompts are not rejected")
require("usesSpeakerDiarization ? null : request.Keywords" in coordinator,
        "Structured keywords can leak into the diarization request")
require("var contextualPrompt = usesSpeakerDiarization" in coordinator and "? string.Empty" in coordinator,
        "Prompt/context continuation can leak into the diarization request")
require('new StringContent(normalizedLanguages[0], Encoding.UTF8), "language"' in client,
        "The optional singular language hint is not transported")
require("OptionalSingleLanguageList(InputValidation.NormalizeLanguage(language))" in client,
        "Single-language models do not use their stricter language validator")
require('language.Length is 2 or 3' in input_validation
        and 'language is "zh-cn" or "zh-tw" or "zh-hk"' in input_validation,
        "gpt-transcribe ISO-639-3 or regional-Chinese language support is missing")
require("language.Length != 2" in input_validation
        and "single-language transcription model requires a two-letter ISO 639-1 hint" in input_validation,
        "Single-language models do not enforce the documented ISO-639-1 contract")
require('TryAddWithoutValidation("X-Client-Request-Id"' in client
        and 'var requestId = ReadHeader(response, "x-request-id") ?? "unavailable";' in client
        and client.count('ReadHeader(response, "x-request-id")') >= 2,
        "OpenAI client/server request-ID diagnostics are missing from response logging or API errors")
require("HttpRequestError" in client
        and "DescribeNetworkFailure" in client
        and "operation.Fail(diagnostic, DiagnosticReason.ProviderOutcomeUnknown)" in client,
        "Network failures lack typed uncertain-outcome diagnostics")
transcription_client = client[client.index("public async Task<OpenAiTranscriptionResult> TranscribeAsync"):client.index("private static IReadOnlyList<string> OptionalSingleLanguageList")]
require(transcription_client.count("_httpClient.SendAsync(") == 1
        and "for (var attempt" not in transcription_client
        and "Task.Delay(" not in transcription_client
        and "_ = retryCount;" in transcription_client,
        "Paid transcription must have one dispatch and no automatic retry loop/timer")
require("DefaultProxyCredentials = CredentialCache.DefaultCredentials" in client
        and "UseProxy = true" in client,
        "Authenticated system-proxy support is missing")
require(re.search(r"StartupDiagnostics\.WriteHttpResponse\([^;]*elapsed\.TotalMilliseconds", client, re.S) is not None
        and "Elapsed { get; }" in client
        and "requestTimer.Elapsed" in client,
        "Elapsed OpenAI request timing is missing")
require("ServerRequestId { get; }" in client
        and client.count('serverRequestId = ReadHeader(response, "x-request-id")') >= 2
        and "responseStatusCode" in client,
        "Post-header network failures do not preserve OpenAI request/status context")
require("BuildTechnicalDetail" in client
        and "var technicalDetail = BuildTechnicalDetail(exception);" in client
        and "SocketError=" in client
        and "NativeError=" in client,
        "Nested network/socket diagnostics are missing or are not propagated into network errors")
require("attemptCancellation.CancelAfter(timeout);" in client
        and 'catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)\n        {\n            var diagnostic = CreateTimeoutException(\n                "connection test",' in client
        and client.count('"connection test"') >= 2
        and "TimeSpan.FromSeconds(45)" in main_cs,
        "Connection-test timeout is not classified with request diagnostics")

# Structured response and bounded parsing.
require("public sealed record SpeakerSegment" in models
        and "public sealed record OpenAiTranscriptionResult" in models
        and "SpeakerSegments { get; init; }" in models
        and "HasSpeakerDiarization { get; init; }" in models,
        "Structured speaker result models are incomplete")
for token in ('TryGetProperty("segments"', 'TryGetProperty("speaker"', 'TryGetProperty("start"',
              'TryGetProperty("end"', 'TryGetProperty("text"'):
    require(token in client, f"Diarized response parser is missing {token}")
require("segments.Count >= 50_000" in client and "MaximumResponseBytes = 4 * 1024 * 1024" in client,
        "Diarized response parsing is not bounded")
require("double.IsFinite(startSeconds)" in client and "endSeconds < startSeconds" in client,
        "Speaker timestamps are not validated")
require("non-empty diarized text without speaker segments" in client,
        "Non-empty diarized responses can silently lose all speaker labels")

# Chunking, checkpoint, and continuity invariants.
require("enforceDurationLimit: true" in coordinator,
        "Diarization uploads are not duration-bounded")
require("includePreroll: !usesSpeakerDiarization" in coordinator,
        "Diarization can include duplicated MP3 preroll")
require("maximum_chunk_minutes = request.MaximumChunkMinutes" in coordinator,
        "Checkpoint identity omits the effective diarization duration limit")
require('"mp3-frame-no-preroll-vbrmeta-1.6"' in coordinator,
        "Diarization chunker identity is stale")
require('CheckpointSchema = "cosmic-transcriber-checkpoint-1.11"' in coordinator,
        "Checkpoint schema was not changed for structured speaker data")
require("IReadOnlyList<SpeakerSegment> SpeakerSegments" in coordinator
        and "ComputeCheckpointPayloadSha256(envelope.Text, envelope.SpeakerSegments)" in coordinator,
        "Speaker segments are not integrity-protected in checkpoints")
require("MaximumCheckpointBytes = 16 * 1024 * 1024" in coordinator
        and "Encoding.UTF8.GetByteCount(content) > MaximumCheckpointBytes" in coordinator
        and "avoid silently repeating billable work" in coordinator,
        "Diarized checkpoints are not safely bounded/persisted")
require("OffsetAndScopeSpeakerSegments" in coordinator
        and '"Part {chunk.Index} · {baseSpeaker}"' in coordinator
        and "MergeAdjacentSpeakerSegments" in coordinator,
        "Global timing or request-scoped speaker labels are missing")
require("HasSpeakerDiarization = usesSpeakerDiarization" in coordinator,
        "Diarization state is not propagated to export")

# Output and UI.
require("return BuildDiarizedTextBody(result, includeTimestamps);" in writer
        and "return BuildDiarizedMarkdownBody(result, includeTimestamps, headingLevel);" in writer,
        "Speaker-labelled TXT/Markdown export is missing")
require("segment.Speaker" in writer and "segment.Start" in writer and "segment.End" in writer,
        "Speaker labels or timestamps are omitted from export")
require("Speaker labels are scoped to each Part" in writer,
        "Cross-request speaker identity warning is missing")
require('<ComboBoxItem Content="gpt-4o-transcribe-diarize"' in main_xaml,
        "Diarization model is absent from the UI")
require('Content="gpt-transcribe" IsSelected="True"' in main_xaml,
        "Diarization incorrectly replaced the general default")
require("KeywordsTextBox.IsEnabled = IsUiIdle && !diarizesSpeakers;" in main_cs
        and "PromptTextBox.IsEnabled = IsUiIdle && supportsPrompt;" in main_cs
        and "PreviousContextCheckBox.IsEnabled = IsUiIdle && supportsPreviousContext;" in main_cs,
        "Unsupported diarization guidance controls are not disabled")
require("ChunkDurationSlider.IsEnabled = IsUiIdle;" in main_cs
        and 'ChunkDurationText.Text = $"{(int)ChunkDurationSlider.Value} min";' in main_cs,
        "Diarization duration safety control is incorrectly disabled")
require("Long recordings are split into bounded uploads" in main_cs,
        "UI does not explain Part-scoped labels")
require("SupportsSpeakerDiarization(CurrentModel)" in main_cs
        and "InputValidation.NormalizeLanguage(LanguageTextBox.Text)" in main_cs,
        "Model-specific language preflight is incomplete")
combo_style = shared_styles.split('<Style TargetType="{x:Type ComboBox}">', 1)
require(len(combo_style) == 2, "The shared ComboBox style is missing")
if len(combo_style) == 2:
    combo_style_body = combo_style[1].split("</Style>", 1)[0]
    require('Property="Template"' in combo_style_body
            and '<ControlTemplate TargetType="{x:Type ComboBox}">' in combo_style_body,
            "The ComboBox does not own a deterministic application-themed template")
    require('x:Name="PART_Popup"' in combo_style_body,
            "The required ComboBox PART_Popup is missing")
    require('AllowsTransparency="False"' in combo_style_body
            and 'PopupAnimation="None"' in combo_style_body
            and 'AllowsTransparency="True"' not in combo_style_body
            and 'PopupAnimation="Fade"' not in combo_style_body,
            "The dropdown popup is not opaque/non-animated")
    require('Background="{DynamicResource SurfaceElevatedBrush}"' in combo_style_body
            and 'TextElement.Foreground="{TemplateBinding Foreground}"' in combo_style_body,
            "The closed selection field does not explicitly pair its themed surface and foreground")
    require('<ScrollViewer x:Name="DropDownScrollViewer"' in combo_style_body
            and '<ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"' in combo_style_body,
            "The dropdown does not provide a bounded scroll viewer with an ItemsPresenter")
combo_item_style = shared_styles.split('<Style TargetType="{x:Type ComboBoxItem}">', 1)
require(len(combo_item_style) == 2, "The shared ComboBoxItem style is missing")
if len(combo_item_style) == 2:
    combo_item_body = combo_item_style[1].split("</Style>", 1)[0]
    require('Property="Template"' in combo_item_body
            and 'TextElement.Foreground="{TemplateBinding Foreground}"' in combo_item_body,
            "Dropdown items do not explicitly render their themed foreground")
require(main_xaml.count('<ComboBoxItem Content="gpt-') == 4,
        "The model selector must contain exactly the four supported completed-file models")
require(main_xaml.count('PreviewMouseWheel="ComboBox_PreviewMouseWheel"') >= 2
        and "private void ComboBox_PreviewMouseWheel" in main_cs
        and "!comboBox.IsDropDownOpen" in main_cs
        and "e.Handled = true;" in main_cs,
        "Closed ComboBoxes can change silently while the page is being scrolled")
require("comboBox.Parent is UIElement parent" in main_cs
        and "parent.RaiseEvent(new MouseWheelEventArgs" in main_cs
        and "RoutedEvent = Mouse.MouseWheelEvent" in main_cs,
        "Closed ComboBox wheel suppression traps scrolling instead of forwarding it to the page")

# Light-theme normal-sized text must remain readable on the light hover surface.
# The accent also paints progress/slider/outline affordances and must retain 3:1
# non-text contrast against both the elevated and hover surfaces.
paper_hover = parse_argb_color(paper_theme, "SurfaceHoverColor")
paper_elevated = parse_argb_color(paper_theme, "SurfaceElevatedColor")
for foreground_key in (
    "TextSecondaryColor",
    "TextMutedColor",
    "AccentTextColor",
    "VioletColor",
    "SuccessColor",
    "WarningColor",
    "ErrorColor",
    "InfoColor",
):
    foreground = parse_argb_color(paper_theme, foreground_key)
    if foreground is not None and paper_hover is not None:
        require(contrast_ratio(foreground, paper_hover) >= 4.5,
                f"Cosmic Paper {foreground_key} contrast is below 4.5:1 on SurfaceHoverColor")

paper_accent = parse_argb_color(paper_theme, "AccentColor")
if paper_accent is not None and paper_hover is not None and paper_elevated is not None:
    require(contrast_ratio(paper_accent, paper_hover) >= 3.0,
            "Cosmic Paper AccentColor contrast is below 3:1 on SurfaceHoverColor")
    require(contrast_ratio(paper_accent, paper_elevated) >= 3.0,
            "Cosmic Paper AccentColor contrast is below 3:1 on SurfaceElevatedColor")
    require(contrast_ratio((0x1B, 0x14, 0x25), paper_accent) >= 4.5,
            "Primary-button foreground contrast is below 4.5:1 on Cosmic Paper AccentColor")

# XML and code-behind event integrity.
xaml_files = sorted(p for p in src.rglob("*.xaml") if not args.source_only or not any(part in IGNORED_PARTS for part in p.parts))
for path in xaml_files:
    try:
        ET.parse(path)
    except ET.ParseError as exc:
        errors.append(f"Malformed XAML {path.relative_to(root)}: {exc}")

handler_pattern = re.compile(r'\b(?:Click|Loaded|Closing|SelectionChanged|TextChanged|ValueChanged|PreviewDragOver|PreviewDrop|PreviewMouseLeftButtonDown|PreviewMouseWheel|PasswordChanged|PreviewKeyDown|Startup|Exit)="([A-Za-z_][A-Za-z0-9_]*)"')
method_pattern = re.compile(r'\b(?:private|protected|public)\s+(?:async\s+)?(?:void|Task|bool)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(')
for xaml_path in xaml_files:
    code_path = Path(str(xaml_path) + ".cs")
    if not code_path.is_file():
        continue
    handlers = set(handler_pattern.findall(read(xaml_path)))
    methods = set(method_pattern.findall(read(code_path)))
    missing = sorted(handlers - methods)
    require(not missing, f"Unresolved handlers in {xaml_path.name}: {', '.join(missing)}")

for path in sorted(src.rglob("*.cs")):
    if args.source_only and any(part in IGNORED_PARTS for part in path.parts):
        continue
    require(balanced_csharp(path), f"Unbalanced C# delimiters in {path.relative_to(root)}")

handler_bindings: list[str] = []
for xaml_path in xaml_files:
    handler_bindings.extend(handler_pattern.findall(read(xaml_path)))

resource_reference_pattern = re.compile(r"\{(?:DynamicResource|StaticResource)\s+([^}\s,]+)")
resource_key_pattern = re.compile(r'x:Key="([^"]+)"')
resource_references: list[str] = []
resource_keys: list[str] = []
for xaml_path in xaml_files:
    xaml_text = read(xaml_path)
    resource_references.extend(resource_reference_pattern.findall(xaml_text))
    resource_keys.extend(resource_key_pattern.findall(xaml_text))
missing_resources = sorted(set(resource_references) - set(resource_keys))
require(not missing_resources, f"Unresolved XAML resources: {', '.join(missing_resources)}")

# Package hygiene.
manifest_generator = root / "scripts" / "generate_release_manifests.py"
require(manifest_generator.is_file(), "Deterministic release-manifest generator is missing")
if manifest_generator.is_file():
    generator_text = read(manifest_generator)
    require('INVENTORY_NAME = "ARTIFACT-INVENTORY.json"' in generator_text
            and 'CHECKSUMS_NAME = "SHA256SUMS.txt"' in generator_text
            and 'relative.as_posix() in {INVENTORY_NAME, CHECKSUMS_NAME}' in generator_text,
            "Release-manifest generator does not use the required non-circular manifest scheme")

for path in root.rglob("*"):
    if not path.is_file():
        continue
    if args.source_only and any(part.lower() in IGNORED_PARTS for part in path.parts):
        continue
    require(not any(part.lower() in IGNORED_PARTS for part in path.parts),
            f"Build/cache residue included: {path.relative_to(root)}")
    require(path.suffix.lower() not in {".exe", ".dll", ".pdb", ".nupkg"},
            f"Compiled/package residue included: {path.relative_to(root)}")

secret_pattern = re.compile(r"\bsk-(?:proj-)?[A-Za-z0-9_-]{20,}\b")
for path in root.rglob("*"):
    if path.is_file() and path.suffix.lower() in TEXT_SUFFIXES and not any(part in IGNORED_PARTS for part in path.parts):
        require(secret_pattern.search(read(path)) is None, f"Possible embedded API key in {path.relative_to(root)}")

# Independently validate the deterministic inventory and checksum manifests.
def package_artifact_paths() -> list[Path]:
    result: list[Path] = []
    for candidate in root.rglob("*"):
        relative = candidate.relative_to(root)
        if any(part in IGNORED_PARTS for part in relative.parts):
            continue
        require(not candidate.is_symlink(), f"Symbolic link included in release: {relative.as_posix()}")
        if candidate.is_file() and relative.as_posix() not in {"ARTIFACT-INVENTORY.json", "SHA256SUMS.txt"}:
            result.append(candidate)
    return sorted(result, key=lambda item: item.relative_to(root).as_posix())


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    try:
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
    except OSError as exc:
        errors.append(f"Cannot hash {path.relative_to(root)}: {exc}")
        return ""
    return digest.hexdigest()


artifact_paths = package_artifact_paths()
expected_entries = [
    {
        "path": path.relative_to(root).as_posix(),
        "sha256": sha256_file(path),
        "bytes": path.stat().st_size,
    }
    for path in artifact_paths
]
expected_inventory_bytes = (json.dumps(expected_entries, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
expected_checksum_lines = [
    f"{hashlib.sha256(expected_inventory_bytes).hexdigest()}  ARTIFACT-INVENTORY.json",
    *(f"{entry['sha256']}  {entry['path']}" for entry in expected_entries),
]
expected_checksums_bytes = ("\n".join(expected_checksum_lines) + "\n").encode("utf-8")
inventory_path = root / "ARTIFACT-INVENTORY.json"
checksums_path = root / "SHA256SUMS.txt"
require(inventory_path.is_file(), "ARTIFACT-INVENTORY.json is missing")
require(checksums_path.is_file(), "SHA256SUMS.txt is missing")
if inventory_path.is_file():
    try:
        require(inventory_path.read_bytes() == expected_inventory_bytes,
                "ARTIFACT-INVENTORY.json is stale, incomplete, or malformed")
    except OSError as exc:
        errors.append(f"Cannot read ARTIFACT-INVENTORY.json: {exc}")
if checksums_path.is_file():
    try:
        require(checksums_path.read_bytes() == expected_checksums_bytes,
                "SHA256SUMS.txt is stale, incomplete, or malformed")
    except OSError as exc:
        errors.append(f"Cannot read SHA256SUMS.txt: {exc}")

if errors:
    print(f"COSMIC TRANSCRIBER VERIFY FAILED ({len(errors)} finding(s))")
    for index, error in enumerate(errors, 1):
        print(f"{index}. {error}")
    sys.exit(1)

print("COSMIC TRANSCRIBER VERIFY PASSED")
print(f"Version: {EXPECTED_VERSION}")
print(f"C# files: {len(list(src.rglob('*.cs')))}")
print(f"XAML files: {len(xaml_files)}")
print(f"Unique XAML handlers: {len(set(handler_bindings))} ({len(handler_bindings)} bindings)")
print(f"Resolved resource references: {len(set(resource_references))}")
print("Compile-regression, clean-publish, autosave, API/diarization, language-contract, network-diagnostic, dropdown/theme-contrast, model-sanitization, parsing, checkpoint, export, UI, XML, handler, resource, documentation, deterministic-manifest, secret, and package checks passed.")


