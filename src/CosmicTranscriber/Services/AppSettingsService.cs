using System.Security;
using System.Text.Json;
using CosmicTranscriber.Models;
using CosmicTranscriber.Utilities;

namespace CosmicTranscriber.Services;

public sealed class AppSettingsService
{
    private const int MaximumSettingsFileBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppSettingsService()
    {
        SettingsDirectory = ResolveSettingsDirectory();
    }

    internal AppSettingsService(string directory) { SettingsDirectory = Path.GetFullPath(directory); }

    public string SettingsDirectory { get; }

    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public AppSettings Load()
    {
        using var diagnostic = StartupDiagnostics.Begin(DiagnosticArea.Settings);
        try
        {
            try
            {
                // Read directly so access denial is recorded as a storage problem rather than
                // being flattened into the same path as a genuinely absent settings file.
                SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(SettingsPath, "The settings file");
                var bytes = BoundedFileReader.ReadAllBytes(SettingsPath, MaximumSettingsFileBytes);
                var json = new UTF8Encoding(false, true).GetString(bytes);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                Normalize(settings);
                StartupDiagnostics.Write("SETTINGS", "Settings loaded successfully.");
                return diagnostic.Complete(settings);
            }
            catch (FileNotFoundException)
            {
                StartupDiagnostics.Write("SETTINGS", "No settings file exists; using defaults.");
                return diagnostic.Complete(new AppSettings(), DiagnosticReason.Missing);
            }
            catch (DirectoryNotFoundException)
            {
                StartupDiagnostics.Write("SETTINGS", "No settings directory exists; using defaults.");
                return diagnostic.Complete(new AppSettings(), DiagnosticReason.Missing);
            }
            catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidDataException or DecoderFallbackException)
            {
                StartupDiagnostics.WriteException("SETTINGS FALLBACK", ex);
                return diagnostic.Complete(new AppSettings(), ex is JsonException or InvalidDataException or DecoderFallbackException ? DiagnosticReason.Corrupt : DiagnosticReason.StorageFailure);
            }
    
        }
        catch (OperationCanceledException) { diagnostic.Stage(DiagnosticStage.Cancelled, DiagnosticReason.UserCancelled); throw; }
        catch (Exception error) { diagnostic.Fail(error, DiagnosticReason.StorageFailure); throw; }
    }

    public void Save(AppSettings settings, bool writeDiagnostic = true)
    {
        using var diagnostic = StartupDiagnostics.Begin(DiagnosticArea.Settings, writeDiagnostic);
        try
        {
            ArgumentNullException.ThrowIfNull(settings);
            Normalize(settings);
            var payload = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(settings, JsonOptions));
            SafeFileSystem.WriteAllBytesDurableAtomic(SettingsPath, payload, "The settings file");
            if (writeDiagnostic)
            {
                StartupDiagnostics.Write("SETTINGS", "Settings saved successfully.");
            }
    
            diagnostic.Stage(DiagnosticStage.Completed);

        }
        catch (OperationCanceledException) { diagnostic.Stage(DiagnosticStage.Cancelled, DiagnosticReason.UserCancelled); throw; }
        catch (Exception error) { diagnostic.Fail(error, DiagnosticReason.StorageFailure); throw; }
    }


    private static string ResolveSettingsDirectory()
    {
        using var diagnostic = StartupDiagnostics.Begin(DiagnosticArea.Settings);
        var candidates = new List<(string Path, string Description)>();

        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                candidates.Add((Path.Combine(localAppData, "CosmicTranscriber"), "Local AppData"));
            }
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            StartupDiagnostics.WriteException("SETTINGS DIRECTORY", ex);
        }

        try
        {
            var temporaryRoot = Path.GetTempPath();
            if (!string.IsNullOrWhiteSpace(temporaryRoot))
            {
                candidates.Add((Path.Combine(temporaryRoot, "CosmicTranscriber"), "the current user's temporary folder"));
            }
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            StartupDiagnostics.WriteException("SETTINGS TEMP FALLBACK", ex);
        }

        var executableFallback = Path.Combine(AppContext.BaseDirectory, "CosmicTranscriber-settings");
        candidates.Add((executableFallback, "the executable folder"));

        foreach (var candidate in candidates)
        {
            if (CanUseSettingsDirectory(candidate.Path, out var failure))
            {
                if (!string.Equals(candidate.Description, "Local AppData", StringComparison.Ordinal))
                {
                    StartupDiagnostics.Write(
                        "SETTINGS FALLBACK",
                        $"Local AppData was unavailable or not writable; settings will use {candidate.Description}.");
                }

                StartupDiagnostics.WriteStorageSelection(DiagnosticArea.Settings, candidate.Description is "Local AppData" ? DiagnosticStorageKind.LocalApplicationData : candidate.Description is "the current user's temporary folder" ? DiagnosticStorageKind.Temporary : DiagnosticStorageKind.Executable);
                return diagnostic.Complete(candidate.Path);
            }

            if (failure is not null)
            {
                StartupDiagnostics.WriteException(
                    "SETTINGS DIRECTORY REJECTED",
                    new IOException($"{candidate.Description} is not writable: {candidate.Path}", failure));
            }
        }

        StartupDiagnostics.Write(
            "SETTINGS FALLBACK",
            "No writable settings directory was found. The app will remain usable, but settings and secure key persistence may fail until Windows folder permissions are corrected.");
        StartupDiagnostics.WriteStorageSelection(DiagnosticArea.Settings, DiagnosticStorageKind.Unavailable);
        diagnostic.Stage(DiagnosticStage.Failed, DiagnosticReason.StorageFailure);
        return executableFallback;
    }

    private static bool CanUseSettingsDirectory(string path, out Exception? failure)
    {
        string? probePath = null;
        try
        {
            SafeFileSystem.CreateDirectoryWithoutReparsePoint(path, "The settings folder");
            probePath = Path.Combine(path, $".settings-write-test-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
            using (var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                probe.WriteByte(0x43);
                probe.Flush(flushToDisk: true);
            }

            File.Delete(probePath);
            probePath = null;
            failure = null;
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            failure = ex;
            return false;
        }
        finally
        {
            if (probePath is not null)
            {
                TryDelete(probePath);
            }
        }
    }

    private static void Normalize(AppSettings settings)
    {
        // Schema 2 changes the default file model from gpt-4o-transcribe to gpt-transcribe.
        // Migrate only the untouched pre-schema default pair so an explicitly customized
        // legacy model/price remains respected.
        if (settings.SettingsSchemaVersion < 2
            && string.Equals(settings.Model, TranscriptionModelCatalog.Gpt4oModel, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(settings.CustomPricePerMinute - 0.006) < 0.0000001)
        {
            settings.Model = TranscriptionModelCatalog.DefaultFileModel;
            settings.CustomPricePerMinute = TranscriptionModelCatalog.DefaultPricePerMinute(settings.Model);
        }

        settings.SettingsSchemaVersion = 4;
        settings.OutputDirectory = InputValidation.TruncateWithoutSplittingSurrogatePair(
            settings.OutputDirectory ?? string.Empty,
            InputValidation.MaximumOutputPathCharacters);
        settings.Model = string.IsNullOrWhiteSpace(settings.Model)
            ? TranscriptionModelCatalog.DefaultFileModel
            : InputValidation.TruncateWithoutSplittingSurrogatePair(settings.Model.Trim(), 128);
        if (!TranscriptionModelCatalog.SupportsFileUploads(settings.Model))
        {
            settings.Model = TranscriptionModelCatalog.DefaultFileModel;
            settings.CustomPricePerMinute = TranscriptionModelCatalog.DefaultPricePerMinute(settings.Model);
        }
        try
        {
            settings.Language = InputValidation.NormalizeLanguageList(settings.Language);
        }
        catch (ArgumentException)
        {
            settings.Language = string.Empty;
        }
        try
        {
            settings.Keywords = InputValidation.NormalizeKeywordList(settings.Keywords);
        }
        catch (ArgumentException)
        {
            settings.Keywords = string.Empty;
        }
        settings.Prompt = InputValidation.TruncateWithoutSplittingSurrogatePair(
            settings.Prompt ?? AppSettings.DefaultPrompt,
            InputValidation.MaximumBasePromptCharacters);
        settings.ChunkMegabytes = Math.Clamp(settings.ChunkMegabytes, 5, 24);
        settings.MaximumChunkMinutes = Math.Clamp(settings.MaximumChunkMinutes, 3, 5);
        settings.RetryCount = Math.Clamp(settings.RetryCount, 0, 5);
        settings.CombinedTitle = InputValidation.TruncateWithoutSplittingSurrogatePair(
            settings.CombinedTitle ?? "Combined Transcript",
            InputValidation.MaximumCombinedTitleCharacters);
        settings.Theme = string.Equals(settings.Theme, "Cosmic Paper", StringComparison.OrdinalIgnoreCase)
            ? "Cosmic Paper"
            : "Cosmic Night";
        if (!double.IsFinite(settings.CustomPricePerMinute) || settings.CustomPricePerMinute < 0)
        {
            settings.CustomPricePerMinute = TranscriptionModelCatalog.DefaultPricePerMinute(settings.Model);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup only.
        }
        catch (SecurityException)
        {
            // Best-effort cleanup only.
        }
    }
}
