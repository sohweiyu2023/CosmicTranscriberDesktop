using CosmicTranscriber.Utilities;

namespace CosmicTranscriber.Models;

public enum AudioJobStatus
{
    Ready,
    Preparing,
    Transcribing,
    Completed,
    Failed,
    Cancelled,
    Skipped
}

public sealed class AudioJobItem : ObservableObject
{
    private AudioJobStatus _status = AudioJobStatus.Ready;
    private double _progress;
    private string _detail = "Ready";

    public required string SourcePath { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
    public required TimeSpan Duration { get; init; }
    public required DateTime LastWriteTimeUtc { get; init; }
    public required string SourceSha256 { get; init; }

    public string SizeText => Formatters.FileSize(SizeBytes);
    public string DurationText => Formatters.Duration(Duration);
    public string MetadataText => $"{DurationText}  •  {SizeText}";

    public AudioJobStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => Status switch
    {
        AudioJobStatus.Ready => "READY",
        AudioJobStatus.Preparing => "PREPARING",
        AudioJobStatus.Transcribing => "TRANSCRIBING",
        AudioJobStatus.Completed => "COMPLETED",
        AudioJobStatus.Failed => "FAILED",
        AudioJobStatus.Cancelled => "CANCELLED",
        AudioJobStatus.Skipped => "SKIPPED",
        _ => Status.ToString().ToUpperInvariant()
    };

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, Math.Clamp(value, 0, 100));
    }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }
}
