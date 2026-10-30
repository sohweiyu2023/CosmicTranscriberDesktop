using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using CosmicTranscriber.Utilities;

namespace CosmicTranscriber.Services;

public sealed record RecoveryAttempt(string WorkId, int Chunk, Guid? AttemptId, string? ClientRequestId, DateTimeOffset? CreatedUtc, string State);
public sealed record RecoveryResolution(bool Reconciled, string Message, Guid? ReceiptId = null);

/// <summary>Local-only evidence review. No HTTP client, credential, billing adjustment or override path.</summary>
public sealed class RecoveryReviewService
{
    public const string HistoryFolderName = "CosmicTranscriber-RecoveryHistory";
    private const int MaximumRows = 256;
    private readonly string _output;
    private static readonly JsonSerializerOptions JsonOptions = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record Receipt(string Schema, string WorkId, int Chunk, Guid AttemptId, string ClientRequestId,
        DateTimeOffset IntentCreatedUtc, string CheckpointSha256, DateTimeOffset RecordedUtc, string Evidence);

    public RecoveryReviewService(string outputDirectory) => _output = Path.GetFullPath(outputDirectory);
    public string HistoryDirectory => Path.Combine(_output, HistoryFolderName);
    public Task<IReadOnlyList<RecoveryAttempt>> InspectAsync(CancellationToken token)
        => Task.Run(() => Inspect(token), token);

    private IReadOnlyList<RecoveryAttempt> Inspect(CancellationToken token)
    {
        using var diagnostic = StartupDiagnostics.Begin(DiagnosticArea.RecoveryInventory);
        var rows = new List<RecoveryAttempt>();
        try
        {
            GuardAncestors(_output);
            if (!CheckpointCacheService.CacheExists(_output)) return diagnostic.Complete<IReadOnlyList<RecoveryAttempt>>(rows);
            using var usage = CheckpointCacheService.AcquireUsage(_output);
            var cache = CheckpointCacheService.GetCacheRoot(_output);
            var rootFileCount = 0;
            foreach (var file in Directory.EnumerateFiles(cache)) {
                token.ThrowIfCancellationRequested();
                if (++rootFileCount > MaximumRows || Path.GetFileName(file) != ".cache-usage.lock")
                    throw new IOException("Unexpected cache-root evidence; recovery inventory is incomplete.");
                SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(file,"The cache usage lock");
            }
            var workCount = 0;
            foreach (var work in Directory.EnumerateDirectories(cache))
            {
                token.ThrowIfCancellationRequested();
                if (++workCount > MaximumRows) throw new IOException("Recovery review limit reached; the inventory is incomplete.");
                var id = Path.GetFileName(work);
                ValidateWorkId(id);
                GuardAncestors(work);
                CheckpointCacheService.ValidateWorkDirectoryForUse(work, token);
                using var lease = WorkLease(work);
                CheckpointCacheService.ValidateWorkDirectoryForUse(work, token);
                var transcripts = Path.Combine(work, "transcripts");
                if (!DirectoryPresent(transcripts)) continue;
                foreach (var marker in Directory.EnumerateFiles(transcripts))
                {
                    token.ThrowIfCancellationRequested();
                    if (!PendingTranscriptionLedger.TryMarkerIndex(Path.GetFileName(marker), out var index)) continue;
                    if (rows.Count == MaximumRows) throw new IOException("Recovery review limit reached; the inventory is incomplete.");
                    try
                    {
                        var intent = PendingTranscriptionLedger.Read(marker, index) ?? throw new PendingTranscriptionException();
                        rows.Add(new(id, index, intent.AttemptId, intent.ClientRequestId, intent.CreatedUtc, "Unresolved — local response not yet verified"));
                    }
                    catch (PendingTranscriptionException)
                    {
                        rows.Add(new(id, index, null, null, null, "Unknown — intent is unreadable or invalid; still blocked"));
                    }
                }
            }
            return diagnostic.Complete<IReadOnlyList<RecoveryAttempt>>(rows);
        }
        catch (Exception error) { diagnostic.Fail(error, DiagnosticReason.StorageFailure); throw; }
    }

    public Task<RecoveryResolution> RecheckAsync(RecoveryAttempt selected, CancellationToken token)
        => Task.Run(async () =>
        {
            using var diagnostic = StartupDiagnostics.Begin(DiagnosticArea.RecoveryRecheck);
            try
            {
                ArgumentNullException.ThrowIfNull(selected);
                ValidateWorkId(selected.WorkId);
                if (selected.Chunk < 1) throw new ArgumentOutOfRangeException(nameof(selected));
                GuardAncestors(_output);
                if (!CheckpointCacheService.CacheExists(_output)) return diagnostic.Complete(Unknown(), DiagnosticReason.ProviderOutcomeUnknown);
                using var usage = CheckpointCacheService.AcquireUsage(_output);
                var work = Path.Combine(CheckpointCacheService.GetCacheRoot(_output), selected.WorkId);
                GuardAncestors(work);
                CheckpointCacheService.ValidateWorkDirectoryForUse(work, token);
                using var lease = WorkLease(work);
                CheckpointCacheService.ValidateWorkDirectoryForUse(work, token);
                var directory = Path.Combine(work, "transcripts");
                var marker = PendingTranscriptionLedger.PathFor(directory, selected.Chunk);
                var intent = PendingTranscriptionLedger.Read(marker, selected.Chunk);
                // A stale selection, invalid intent or mismatched response never authorizes deletion/replay.
                if (intent is null || selected.AttemptId != intent.AttemptId || selected.ClientRequestId != intent.ClientRequestId)
                    return diagnostic.Complete(Unknown(), DiagnosticReason.ProviderOutcomeUnknown);
                var checkpoint = await TranscriptionCoordinator.ReadRecoveryCheckpointAsync(
                    Path.Combine(directory, $"chunk-{selected.Chunk:000}.json"), selected.Chunk, token);
                if (checkpoint is null || checkpoint.Value.Attempt != intent.AttemptId || checkpoint.Value.Client != intent.ClientRequestId)
                    return diagnostic.Complete(Unknown(), DiagnosticReason.ProviderOutcomeUnknown);
                token.ThrowIfCancellationRequested();
                // Durable, create-once evidence must exist before redundant pending metadata can be removed.
                WriteReceipt(selected.WorkId, intent, checkpoint.Value.Hash);
                using (var reconcile = StartupDiagnostics.Begin(DiagnosticArea.RecoveryReconcile)) {
                    try { PendingTranscriptionLedger.ReconcileValidatedCheckpoint(marker, intent, checkpoint.Value.Attempt, checkpoint.Value.Client); reconcile.Stage(DiagnosticStage.Completed); }
                    catch(Exception error) { reconcile.Fail(error,DiagnosticReason.StorageFailure); throw; }
                }
                return diagnostic.Complete(new RecoveryResolution(true,
                    "A validated local response matches this exact attempt. Its checkpoint was preserved for reuse. " +
                    "A create-once recovery receipt preserves the original intent separately from the cache. " +
                    "No provider request was made and no charge or budget was reversed.", intent.AttemptId));
            }
            catch (PendingTranscriptionException error) { diagnostic.Fail(error, DiagnosticReason.ProviderOutcomeUnknown); return Unknown(); }
            catch (Exception error) { diagnostic.Fail(error, DiagnosticReason.StorageFailure); throw; }
        }, token);

    private static RecoveryResolution Unknown() => new(false,
        "No safe reconciliation was completed by this review. Unknown outcomes remain blocked; refresh the inventory to inspect current local state. " +
        "Preserve the cache and review provider history using the request ID. Do not clear evidence, switch output/settings to bypass the block, or retry unchanged. " +
        "This app cannot verify external billing evidence, declare a refund, release budget or authorize another paid request.");

    private void WriteReceipt(string workId, PendingTranscriptionIntent intent, string checkpointHash)
    {
        using var diagnostic = StartupDiagnostics.Begin(DiagnosticArea.RecoveryHistory);
        try {
        GuardAncestors(HistoryDirectory);
        SafeFileSystem.CreateDirectoryWithoutReparsePoint(HistoryDirectory, "The recovery history folder");
        var path = Path.Combine(HistoryDirectory, $"accepted-{intent.AttemptId:N}.json");
        SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(path, "The recovery receipt");
        Receipt? existing = null;
        try { existing = JsonSerializer.Deserialize<Receipt>(BoundedFileReader.ReadAllBytes(path, 4096), JsonOptions); }
        catch (FileNotFoundException) { }
        if (existing is not null)
        {
            if (existing.Schema != "cosmic-recovery-receipt-1" || existing.WorkId != workId || existing.Chunk != intent.Index
                || existing.AttemptId != intent.AttemptId || existing.ClientRequestId != intent.ClientRequestId
                || existing.IntentCreatedUtc != intent.CreatedUtc || existing.CheckpointSha256 != checkpointHash
                || existing.RecordedUtc == default || existing.Evidence != "matching-validated-local-checkpoint")
                throw new PendingTranscriptionException();
            diagnostic.Stage(DiagnosticStage.Reused); diagnostic.Stage(DiagnosticStage.Completed);
            return; // No rewrite, including on a retry after a prior marker-delete failure.
        }
        // A JSON null/empty/partial record must never be overwritten or treated as a new receipt.
        SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(path, "The recovery receipt");
        var receipt = new Receipt("cosmic-recovery-receipt-1", workId, intent.Index, intent.AttemptId,
            intent.ClientRequestId, intent.CreatedUtc, checkpointHash, DateTimeOffset.UtcNow, "matching-validated-local-checkpoint");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions);
        stream.Write(bytes); stream.Flush(flushToDisk: true);
        diagnostic.Stage(DiagnosticStage.Completed);
        }
        catch(Exception error) { diagnostic.Fail(error,DiagnosticReason.StorageFailure); throw; }
    }

    private static IDisposable WorkLease(string work)
    {
        SafeFileSystem.EnsureDirectoryIsNotReparsePoint(work, "The recovery work folder");
        var path = Path.Combine(work, ".work.lock");
        SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(path, "The recovery work lock");
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static bool DirectoryPresent(string path)
    {
        try { SafeFileSystem.EnsureDirectoryIsNotReparsePoint(path, "The recovery transcripts folder"); return true; }
        catch (DirectoryNotFoundException) { return false; }
        catch (FileNotFoundException) { return false; }
    }
    private static void ValidateWorkId(string value)
    {
        if (value.Length != 32 || !value.All(char.IsAsciiHexDigit)) throw new IOException("Unexpected recovery work folder.");
    }
    private static void GuardAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Recovery cannot traverse a file-system link."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
