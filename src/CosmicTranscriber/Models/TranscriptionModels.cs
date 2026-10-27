namespace CosmicTranscriber.Models;

public sealed record AudioFileMetadata(
    TimeSpan Duration,
    long SizeBytes,
    DateTime LastWriteTimeUtc,
    string Sha256);

public sealed record AudioChunk(
    int Index,
    string Path,
    TimeSpan Start,
    TimeSpan Duration,
    TimeSpan PrerollDuration,
    long SizeBytes,
    bool IsOriginalFile);

public sealed record SpeakerSegment(
    string Speaker,
    TimeSpan Start,
    TimeSpan End,
    string Text);

public sealed record OpenAiTranscriptionResult(
    string Text,
    IReadOnlyList<SpeakerSegment> SpeakerSegments);

public sealed record ChunkTranscript(
    int Index,
    TimeSpan Start,
    TimeSpan End,
    string Text,
    bool ReusedFromCheckpoint)
{
    public IReadOnlyList<SpeakerSegment> SpeakerSegments { get; init; } = [];
}

public sealed record FileTranscriptionResult(
    string SourcePath,
    string SourceName,
    TimeSpan Duration,
    IReadOnlyList<ChunkTranscript> Chunks)
{
    public bool HasSpeakerDiarization { get; init; }
}

public sealed record CoordinatorProgress(
    string Stage,
    string Message,
    double Fraction,
    int CurrentChunk,
    int TotalChunks);

public sealed class TranscriptionRequest
{
    public required string ApiKey { get; init; }
    public required string Model { get; init; }
    public required string Prompt { get; init; }
    public string? Language { get; init; }
    public string? Keywords { get; init; }
    public int ChunkMegabytes { get; init; } = 20;
    public int MaximumChunkMinutes { get; init; } = 5;
    public int RetryCount { get; init; } = 0;
    public bool UsePreviousContext { get; init; } = true;
}
