using System.Diagnostics;
using System.Text.Json;

namespace CosmicTranscriber.Utilities;

/// <summary>New owned-prefix files only. Never prunes legacy diagnostic/evidence files.</summary>
internal enum DiagnosticLogFamily { App, Launcher }

internal sealed class DiagnosticLogWriter : IDisposable
{
    internal const int DefaultFileBytes = 2 * 1024 * 1024;
    internal const int DefaultRetainedFiles = 20;
    private readonly string _directory;
    private readonly Guid _session;
    private readonly int _fileBytes, _retainedFiles;
    private readonly string _prefix;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private FileStream _stream;
    private long _sequence;
    public string Path { get; private set; } = string.Empty;
    internal DiagnosticLogWriter(string directory, Guid session, int fileBytes = DefaultFileBytes, int retainedFiles = DefaultRetainedFiles, DiagnosticLogFamily family = DiagnosticLogFamily.App)
    {
        if (fileBytes < 512 || retainedFiles < 1) throw new ArgumentOutOfRangeException(nameof(fileBytes));
        _directory = System.IO.Path.GetFullPath(directory); _session = session; _fileBytes = fileBytes; _retainedFiles = retainedFiles;
        _prefix = family == DiagnosticLogFamily.Launcher ? "CosmicLaunch" : "CosmicDiag";
        SafeFileSystem.CreateDirectoryWithoutReparsePoint(_directory, "The diagnostic log folder");
        _stream = OpenNew(); RotateOld();
    }
    public void Write(string category, string eventName, object? data)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, utc = DateTimeOffset.UtcNow, sessionId = _session.ToString("N"),
            sequence = ++_sequence, elapsedMs = _clock.ElapsedMilliseconds, processId = Environment.ProcessId, category, eventName, data });
        if (bytes.Length + 1 > _fileBytes) return;
        if (_stream.Length + bytes.Length + 1 > _fileBytes) { _stream.Dispose(); _stream = OpenNew(); RotateOld(); }
        SafeFileSystem.EnsureDirectoryIsNotReparsePoint(_directory, "The diagnostic log folder");
        _stream.Write(bytes); _stream.WriteByte((byte)'\n'); _stream.Flush(flushToDisk: true);
    }
    private FileStream OpenNew()
    {
        SafeFileSystem.EnsureDirectoryIsNotReparsePoint(_directory, "The diagnostic log folder");
        Path = System.IO.Path.Combine(_directory, $"{_prefix}-v1-{_session:N}-{Guid.NewGuid():N}.jsonl");
        return new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
    }
    private bool Owned(FileInfo file)
    {
        if (file.Extension != ".jsonl") return false;
        var parts = System.IO.Path.GetFileNameWithoutExtension(file.Name).Split('-');
        return parts.Length == 4 && parts[0] == _prefix && parts[1] == "v1" && Guid.TryParseExact(parts[2], "N", out _) && Guid.TryParseExact(parts[3], "N", out _);
    }
    private void RotateOld()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, $"{_prefix}-v1-*.jsonl").Select(p => new FileInfo(p)).Where(Owned)
                .Where(f => !string.Equals(f.FullName, Path, StringComparison.OrdinalIgnoreCase)).OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenByDescending(f => f.Name).Skip(_retainedFiles - 1))
            {
                try { SafeFileSystem.EnsureDirectoryIsNotReparsePoint(_directory, "The diagnostic log folder");
                    SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(file.FullName, "The stale diagnostic log file"); file.Delete(); } catch { }
            }
        } catch { }
    }
    public void Dispose() => _stream.Dispose();
}
