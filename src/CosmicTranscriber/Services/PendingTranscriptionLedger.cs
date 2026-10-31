using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using CosmicTranscriber.Utilities;

namespace CosmicTranscriber.Services;

public sealed class PendingTranscriptionException : IOException
{
    public PendingTranscriptionException(Exception? inner = null)
        : base("A paid transcription intent is unresolved or could not be reconciled safely. This run will not send another request. Preserve the cache and review the existing attempt and possible charges; do not clear it or retry unchanged.", inner) { }
}

// Metadata only: no key, source path, prompt, audio or transcript. Scope is the existing cache work directory.
internal sealed record PendingTranscriptionIntent(string Schema, int Index, Guid AttemptId, string ClientRequestId, DateTimeOffset CreatedUtc);

internal static class PendingTranscriptionLedger
{
    private const string Schema = "cosmic-paid-intent-1";
    private static readonly JsonSerializerOptions JsonOptions = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal static string PathFor(string transcriptDirectory, int index)
    {
        if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
        return Path.Combine(transcriptDirectory, $"chunk-{index:000}.pending");
    }
    internal static bool IsMarkerFileName(string name)
        => TryMarkerIndex(name, out _);
    internal static bool TryMarkerIndex(string name, out int index)
    {
        index = 0;
        if (!name.StartsWith("chunk-", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".pending", StringComparison.OrdinalIgnoreCase)) return false;
        var digits = name[6..^8];
        return digits.Length >= 3 && digits.All(char.IsAsciiDigit) && int.TryParse(digits, out index) && index > 0;
    }
    internal static PendingTranscriptionIntent? Read(string path, int index)
    {
        try {
            SafeFileSystem.EnsureDirectoryIsNotReparsePoint(Path.GetDirectoryName(path)!, "The request-intent folder");
            SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(path, "The request intent");
            var bytes = BoundedFileReader.ReadAllBytes(path, 4096);
            var intent = JsonSerializer.Deserialize<PendingTranscriptionIntent>(bytes, JsonOptions);
            if (intent is null || intent.Schema != Schema || intent.Index != index || intent.AttemptId == Guid.Empty
                || intent.ClientRequestId != "ct-" + intent.AttemptId.ToString("N") || intent.CreatedUtc == default)
                throw new PendingTranscriptionException();
            return intent;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (PendingTranscriptionException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or JsonException or NotSupportedException)
        { throw new PendingTranscriptionException(error); }
    }
    internal static PendingTranscriptionIntent Create(string path, int index, string actualClientRequestId, CancellationToken token)
    {
        if (actualClientRequestId.Length != 35 || !actualClientRequestId.StartsWith("ct-", StringComparison.Ordinal)
            || !Guid.TryParseExact(actualClientRequestId[3..], "N", out var id) || id == Guid.Empty)
            throw new PendingTranscriptionException();
        token.ThrowIfCancellationRequested();
        var intent = new PendingTranscriptionIntent(Schema, index, id, actualClientRequestId, DateTimeOffset.UtcNow);
        try {
            SafeFileSystem.EnsureDirectoryIsNotReparsePoint(Path.GetDirectoryName(path)!, "The request-intent folder");
            SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(path, "The request intent");
            // CreateNew and the enclosing work/cache leases prevent concurrent application dispatch.
            // Never overwrite/delete an existing, partial or failed-write intent.
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(intent, JsonOptions);
            file.Write(bytes); file.Flush(flushToDisk: true);
            StartupDiagnostics.WriteOperation(DiagnosticArea.Checkpoint, DiagnosticStage.Started, intent.AttemptId, DiagnosticReason.None, StartupDiagnostics.ActiveOperationId);
            return intent;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        { throw new PendingTranscriptionException(error); }
    }
    internal static void ReconcileValidatedCheckpoint(string path, PendingTranscriptionIntent expected, Guid? checkpointAttempt, string? checkpointClientId)
    {
        if (checkpointAttempt != expected.AttemptId || checkpointClientId != expected.ClientRequestId) {
            StartupDiagnostics.WriteOperation(DiagnosticArea.Checkpoint, DiagnosticStage.Failed, expected.AttemptId, DiagnosticReason.ProviderOutcomeUnknown, StartupDiagnostics.ActiveOperationId);
            throw new PendingTranscriptionException();
        }
        var current = Read(path, expected.Index);
        if (current != expected) throw new PendingTranscriptionException();
        try {
            SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(path, "The reconciled request intent");
            File.Delete(path);
            StartupDiagnostics.WriteOperation(DiagnosticArea.Checkpoint, DiagnosticStage.Completed, expected.AttemptId, DiagnosticReason.None, StartupDiagnostics.ActiveOperationId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        { throw new PendingTranscriptionException(error); }
    }
    internal static void RefuseClearWithPending(string workDirectory, CancellationToken token)
    {
        // Layout validation already checks links/unexpected items; enumerate directly, not File.Exists.
        foreach (var directory in Directory.EnumerateDirectories(workDirectory)) {
            token.ThrowIfCancellationRequested();
            if (!string.Equals(Path.GetFileName(directory), "transcripts", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory)) {
                token.ThrowIfCancellationRequested();
                if (IsMarkerFileName(Path.GetFileName(entry))) throw new PendingTranscriptionException();
            }
        }
    }
}
