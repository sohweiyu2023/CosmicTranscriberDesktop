namespace CosmicTranscriber.Utilities;

internal enum DiagnosticSinkLocation { Unavailable, Executable, LocalApplicationData, Temporary }
internal enum DiagnosticSinkFailureReason { AccessDenied, StorageFailure, InvalidConfiguration, Other }
internal sealed record DiagnosticSinkFailure(DiagnosticSinkLocation Location, DiagnosticSinkFailureReason Reason, int HResult);
internal sealed record DiagnosticSinkOpenResult(DiagnosticLogWriter? Writer, DiagnosticSinkLocation Location, int Attempts, int Failures, bool EnumerationFailed, IReadOnlyList<DiagnosticSinkFailure> FailureDetails);

internal static class DiagnosticSinkFactory
{
    // Only startup tries the existing ordered locations. A running failed sink
    // is disabled, never retried or redirected by this factory.
    internal static DiagnosticSinkOpenResult Open(IEnumerable<(DiagnosticSinkLocation Location, Func<DiagnosticLogWriter> Open)> candidates)
    {
        int attempts=0, failures=0;var details=new List<DiagnosticSinkFailure>();
        try {
            foreach(var candidate in candidates.Take(3)) {
                attempts++;
                try { return new(candidate.Open(), candidate.Location, attempts, failures, false,details.ToArray()); }
                catch(Exception error) { failures++;details.Add(Failure(candidate.Location,error)); }
            }
        } catch(Exception error) {details.Add(Failure(DiagnosticSinkLocation.Unavailable,error));return new(null, DiagnosticSinkLocation.Unavailable, attempts, failures, true,details.Take(4).ToArray()); }
        return new(null, DiagnosticSinkLocation.Unavailable, attempts, failures, false,details.ToArray());
    }
    private static DiagnosticSinkFailure Failure(DiagnosticSinkLocation location,Exception error)=>new(location,error switch {UnauthorizedAccessException or System.Security.SecurityException=>DiagnosticSinkFailureReason.AccessDenied,IOException=>DiagnosticSinkFailureReason.StorageFailure,ArgumentException=>DiagnosticSinkFailureReason.InvalidConfiguration,_=>DiagnosticSinkFailureReason.Other},error.HResult);
}

internal sealed record LauncherDiagnosticHealth(bool SinkUnavailable, long WriteFailures, long DroppedRecords, long OversizedRecords, long PruneFailures, long DisposeFailures);
internal sealed class LauncherDiagnosticSink : IDisposable
{
    private DiagnosticLogWriter? _writer;
    private string _lastPath;
    private long _failures,_dropped,_oversized,_prune,_dispose;
    private bool _unavailable;
    internal LauncherDiagnosticSink(DiagnosticLogWriter? writer) { _writer=writer;_lastPath=writer?.Path??string.Empty;_unavailable=writer is null; }
    internal LauncherDiagnosticHealth Health => new(_unavailable,_failures,_dropped,_oversized+(_writer?.OmittedRecords??0),_prune+(_writer?.PruneFailures??0),_dispose);
    internal string Path => _writer?.Path ?? _lastPath;
    internal void Record(string name, object? data) {
        if(_writer is null){_dropped++;return;}
        try {_writer.Write("LAUNCHER",name,data);_lastPath=_writer.Path;}
        catch {
            _lastPath=_writer.Path;_unavailable=true;_failures++;_dropped++;
            _oversized+=_writer.OmittedRecords;_prune+=_writer.PruneFailures;
            try{_writer.Dispose();}catch{_dispose++;}_writer=null;
        }
    }
    public void Dispose(){if(_writer is null)return;_lastPath=_writer.Path;_oversized+=_writer.OmittedRecords;_prune+=_writer.PruneFailures;try{_writer.Dispose();}catch{_dispose++;}_writer=null;}
}
