namespace CosmicTranscriber.Utilities;

// Only compile-time vocabulary crosses the operational logging boundary.
public enum DiagnosticArea { Other, Startup, Settings, Authentication, Transcription, ConnectionTest, Checkpoint, Export, OutputDirectory, Cleanup }
public enum DiagnosticStorageKind { LocalApplicationData, Temporary, Executable, Unavailable }
public enum DiagnosticStage { Started, Validated, Dispatched, ResponseHeaders, ResponseBody, Accepted, Completed, Failed, Cancelled, Reused, Invalidated, Rollback, CleanupFailed, Ended }
public enum DiagnosticReason { None, Other, Missing, Corrupt, Validation, StorageFailure, ProviderRejected, ProviderOutcomeUnknown, UserCancelled, MalformedResponse, Incomplete, LocalContention }

public interface IProviderDiagnosticFailure
{
    int? DiagnosticStatus { get; }
    string? DiagnosticCode { get; }
    string? DiagnosticType { get; }
    string? DiagnosticParam { get; }
    string? DiagnosticClientId { get; }
    string? DiagnosticServerId { get; }
    string? DiagnosticFailureKind { get; }
    double? DiagnosticElapsedMs { get; }
}

public sealed class DiagnosticOperation : IDisposable
{
    private readonly DiagnosticArea _area;
    private bool _terminal;
    private readonly bool _enabled;
    private readonly Guid? _parent;
    public Guid Id { get; } = Guid.NewGuid();
    internal DiagnosticOperation(DiagnosticArea area, bool enabled = true) { _area = area; _enabled = enabled; _parent = StartupDiagnostics.ActiveOperationId; if (enabled) { StartupDiagnostics.ActiveOperationId = Id; Stage(DiagnosticStage.Started); } }
    public void Stage(DiagnosticStage stage, DiagnosticReason reason = DiagnosticReason.None)
    {
        if (!_enabled || _terminal) return;
        StartupDiagnostics.WriteOperation(_area, stage, Id, reason, _parent);
        if (stage is DiagnosticStage.Completed or DiagnosticStage.Failed or DiagnosticStage.Cancelled) _terminal = true;
    }
    public T Complete<T>(T value, DiagnosticReason reason = DiagnosticReason.None) { Stage(DiagnosticStage.Completed, reason); return value; }
    public void Fail(Exception error, DiagnosticReason reason = DiagnosticReason.Other)
    {
        Stage(DiagnosticStage.Failed, reason);
        StartupDiagnostics.WriteException(_area.ToString(), error, Id);
    }
    public void Dispose() { if (!_enabled) return; if (!_terminal) Stage(DiagnosticStage.Ended, DiagnosticReason.Incomplete); StartupDiagnostics.ActiveOperationId = _parent; }
}

public sealed record DiagnosticHealth(bool SinkUnavailable, long SinkFailures, long OversizedRecords, long FallbackEventsDropped, long CleanupFailures, long PruneFailures, IReadOnlyList<string> RecentFallbackEvents);
