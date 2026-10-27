using System.Security;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using CosmicTranscriber.Models;
using CosmicTranscriber.Utilities;

namespace CosmicTranscriber.Services;

public sealed class CheckpointStorageException : IOException
{
    public CheckpointStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class TranscriptSizeLimitException : Exception
{
    public TranscriptSizeLimitException(string message) : base(message) { }
}

public sealed class TranscriptionCoordinator
{
    public const long MaximumTranscriptCharactersPerFile = 25_000_000;
    private const string CheckpointSchema = "cosmic-transcriber-checkpoint-1.11";
    private const int MaximumCheckpointBytes = 16 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly Mp3Chunker _chunker;
    private readonly OpenAiTranscriptionClient _client;

    public TranscriptionCoordinator(Mp3Chunker chunker, OpenAiTranscriptionClient client)
    {
        _chunker = chunker;
        _client = client;
    }

    public async Task<FileTranscriptionResult> ProcessFileAsync(
        AudioJobItem item,
        string outputDirectory,
        TranscriptionRequest request,
        IProgress<CoordinatorProgress>? progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        using var cacheUsage = AcquireCacheUsage(outputDirectory);
        await using var sourceLease = new FileStream(
            item.SourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 256,
            useAsync: true);

        var currentHash = Convert.ToHexString(await SHA256.HashDataAsync(sourceLease, cancellationToken));
        if (!string.Equals(currentHash, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                "The source MP3 content changed after it was queued. Remove it, add it again, and retry.");
        }

        sourceLease.Position = 0;
        var workDirectory = ResolveWorkDirectory(item, outputDirectory, request);
        CreateCheckpointDirectory(workDirectory);
        CheckpointCacheService.ValidateWorkDirectoryForUse(workDirectory, cancellationToken);
        await using var workLock = await AcquireWorkLockAsync(workDirectory, cancellationToken);
        CheckpointCacheService.ValidateWorkDirectoryForUse(workDirectory, cancellationToken);
        var chunkDirectory = Path.Combine(workDirectory, $"chunks-{Environment.ProcessId}");
        var usesSpeakerDiarization = TranscriptionModelCatalog.SupportsSpeakerDiarization(request.Model);
        var transcriptDirectory = Path.Combine(workDirectory, "transcripts");
        CreateCheckpointDirectory(transcriptDirectory);

        IReadOnlyList<AudioChunk>? chunks = null;
        try
        {
            progress?.Report(new CoordinatorProgress(
                "Preparing",
                "Inspecting and safely chunking the MP3…",
                0,
                0,
                0));

            var chunkProgress = new CallbackProgress<double>(fraction =>
                progress?.Report(new CoordinatorProgress(
                    "Preparing",
                    $"Preparing audio… {fraction:P0}",
                    Math.Clamp(fraction * 0.12, 0, 0.12),
                    0,
                    0)));

            chunks = await Task.Run(
                () => _chunker.CreateChunksAsync(
                    item.SourcePath,
                    chunkDirectory,
                    request.ChunkMegabytes,
                    request.MaximumChunkMinutes,
                    item.Duration,
                    enforceDurationLimit: true,
                    includePreroll: !usesSpeakerDiarization,
                    progress: chunkProgress,
                    cancellationToken: cancellationToken),
                cancellationToken);

            await WriteManifestAsync(workDirectory, item, request, chunks, cancellationToken);
            log?.Invoke($"{item.FileName}: {chunks.Count} upload chunk(s) ready.");

            var results = new List<ChunkTranscript>(chunks.Count);
            long transcriptCharacters = 0;
            var previousMeaningfulText = string.Empty;
            var contextCheckpointChainIntact = true;

            for (var index = 0; index < chunks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = chunks[index];
                var checkpointPath = Path.Combine(transcriptDirectory, $"chunk-{chunk.Index:000}.json");
                var canReuse = !request.UsePreviousContext || contextCheckpointChainIntact;
                var checkpoint = canReuse
                    ? await TryReadCheckpointAsync(checkpointPath, chunk.Index, cancellationToken)
                    : null;
                var reused = checkpoint is not null;
                string text;
                IReadOnlyList<SpeakerSegment> localSpeakerSegments;

                if (checkpoint is not null)
                {
                    text = checkpoint.Text;
                    localSpeakerSegments = checkpoint.SpeakerSegments;
                    log?.Invoke($"{item.FileName}: reused checkpoint for chunk {chunk.Index}/{chunks.Count}.");
                }
                else
                {
                    if (request.UsePreviousContext)
                    {
                        contextCheckpointChainIntact = false;
                    }

                    var contextualPrompt = usesSpeakerDiarization
                        ? string.Empty
                        : BuildPrompt(
                            request,
                            previousMeaningfulText,
                            chunk.Index > 1 ? chunk.PrerollDuration : TimeSpan.Zero);
                    progress?.Report(new CoordinatorProgress(
                        "Transcribing",
                        $"Uploading chunk {chunk.Index} of {chunks.Count}…",
                        0.12 + (0.88 * index / chunks.Count),
                        chunk.Index,
                        chunks.Count));

                    var response = await _client.TranscribeAsync(
                        request.ApiKey,
                        request.Model,
                        chunk.Path,
                        contextualPrompt,
                        request.Language,
                        usesSpeakerDiarization ? null : request.Keywords,
                        request.RetryCount,
                        log,
                        cancellationToken);
                    text = response.Text;
                    localSpeakerSegments = response.SpeakerSegments;

                    // Once OpenAI has returned a successful billable response, persist it with
                    // a short storage-only token before observing ordinary run cancellation.
                    // This prevents a cancel click in the narrow post-response window from
                    // discarding paid work and retranscribing the same chunk on the next run.
                    await SaveCompletedResponseCheckpointAsync(
                        checkpointPath,
                        chunk.Index,
                        text,
                        localSpeakerSegments);
                    cancellationToken.ThrowIfCancellationRequested();
                    log?.Invoke($"{item.FileName}: checkpoint saved for chunk {chunk.Index}/{chunks.Count}.");
                }

                if (text.Length > MaximumTranscriptCharactersPerFile - transcriptCharacters)
                {
                    throw new TranscriptSizeLimitException(
                        $"The transcript for {item.FileName} exceeded the safe {MaximumTranscriptCharactersPerFile:N0}-character in-memory limit. " +
                        "Split the source recording into smaller files and retry; completed chunk checkpoints were retained.");
                }

                transcriptCharacters += text.Length;
                var end = chunk.Start + chunk.Duration;
                var globalSpeakerSegments = OffsetAndScopeSpeakerSegments(
                    localSpeakerSegments,
                    chunk,
                    chunks.Count);
                results.Add(new ChunkTranscript(chunk.Index, chunk.Start, end, text, reused)
                {
                    SpeakerSegments = globalSpeakerSegments
                });
                if (!usesSpeakerDiarization && !string.IsNullOrWhiteSpace(text))
                {
                    previousMeaningfulText = text;
                }

                progress?.Report(new CoordinatorProgress(
                    "Transcribing",
                    reused
                        ? $"Restored chunk {chunk.Index} of {chunks.Count} from checkpoint."
                        : $"Completed chunk {chunk.Index} of {chunks.Count}.",
                    0.12 + (0.88 * (index + 1) / chunks.Count),
                    chunk.Index,
                    chunks.Count));
            }

            return new FileTranscriptionResult(
                item.SourcePath,
                Path.GetFileNameWithoutExtension(item.SourcePath) ?? "audio",
                item.Duration,
                results)
            {
                HasSpeakerDiarization = usesSpeakerDiarization
            };
        }
        finally
        {
            // Prepared MP3 audio is temporary. Remove it after success, failure, or cancellation;
            // only the small transcript checkpoints are retained for resume.
            TryDeleteDirectory(chunkDirectory, log);
        }
    }

    private static string BuildPrompt(
        TranscriptionRequest request,
        string previousText,
        TimeSpan prerollDuration)
    {
        var builder = new StringBuilder(request.Prompt.Trim());
        if (prerollDuration > TimeSpan.Zero)
        {
            builder.Append("\n\nThe audio begins with a brief overlap from the preceding chunk. ")
                .Append("Use it only for continuity and do not repeat words already transcribed.");
        }

        if (request.UsePreviousContext && !string.IsNullOrWhiteSpace(previousText))
        {
            var tail = InputValidation.TakeTailWithoutSplittingSurrogatePair(previousText, 1_800);
            builder.Append("\n\nThe immediately preceding transcript ended with this context. ")
                .Append("Continue naturally without repeating it:\n")
                .Append(tail);
        }

        return InputValidation.TruncateWithoutSplittingSurrogatePair(
            builder.ToString(),
            InputValidation.MaximumFinalPromptCharacters);
    }

    private static string ResolveWorkDirectory(
        AudioJobItem item,
        string outputDirectory,
        TranscriptionRequest request)
    {
        var fingerprint = new
        {
            schema = CheckpointSchema,
            source_path = Path.GetFullPath(item.SourcePath),
            source_sha256 = item.SourceSha256,
            source_size = item.SizeBytes,
            source_duration_ticks = item.Duration.Ticks,
            model = request.Model.Trim(),
            languages = InputValidation.NormalizeLanguages(request.Language),
            keywords = InputValidation.NormalizeKeywords(request.Keywords),
            prompt = request.Prompt,
            chunk_megabytes = request.ChunkMegabytes,
            maximum_chunk_minutes = request.MaximumChunkMinutes,
            use_previous_context = request.UsePreviousContext,
            speaker_diarization = TranscriptionModelCatalog.SupportsSpeakerDiarization(request.Model),
            chunker = TranscriptionModelCatalog.SupportsSpeakerDiarization(request.Model)
                ? "mp3-frame-no-preroll-vbrmeta-1.6"
                : "mp3-frame-preroll-vbrmeta-1.6"
        };
        var canonicalJson = JsonSerializer.Serialize(fingerprint);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))[..32];
        return Path.Combine(CheckpointCacheService.GetCacheRoot(outputDirectory), hash);
    }

    private static async Task WriteManifestAsync(
        string workDirectory,
        AudioJobItem item,
        TranscriptionRequest request,
        IReadOnlyList<AudioChunk> chunks,
        CancellationToken cancellationToken)
    {
        var manifest = new
        {
            schema = CheckpointSchema,
            source = item.SourcePath,
            source_sha256 = item.SourceSha256,
            item.SizeBytes,
            duration_seconds = item.Duration.TotalSeconds,
            request.Model,
            languages = InputValidation.NormalizeLanguages(request.Language),
            keywords = InputValidation.NormalizeKeywords(request.Keywords),
            request.ChunkMegabytes,
            request.MaximumChunkMinutes,
            request.UsePreviousContext,
            speaker_diarization = TranscriptionModelCatalog.SupportsSpeakerDiarization(request.Model),
            chunks = chunks.Select(chunk => new
            {
                chunk.Index,
                start_seconds = chunk.Start.TotalSeconds,
                duration_seconds = chunk.Duration.TotalSeconds,
                preroll_seconds = chunk.PrerollDuration.TotalSeconds,
                chunk.SizeBytes
            }),
            updated_utc = DateTimeOffset.UtcNow
        };

        var path = Path.Combine(workDirectory, "manifest.json");
        await WriteAtomicTextAsync(path, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
    }

    private static async Task<CheckpointEnvelope?> TryReadCheckpointAsync(
        string path,
        int expectedIndex,
        CancellationToken cancellationToken)
    {
        try
        {
            // Open directly. File.Exists can return false for access-denied paths, which
            // would make an existing completed checkpoint look absent and risk rebilling.
            // Refuse a file-system link so a cache checkpoint cannot redirect reads to
            // unrelated storage outside the application-owned work directory.
            SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(path, "The saved checkpoint");
            var bytes = await BoundedFileReader.ReadAllBytesAsync(
                path,
                MaximumCheckpointBytes,
                cancellationToken);
            var json = StrictUtf8.GetString(bytes);
            var envelope = JsonSerializer.Deserialize<CheckpointEnvelope>(json);
            if (envelope is null
                || !string.Equals(envelope.Schema, CheckpointSchema, StringComparison.Ordinal)
                || envelope.Index != expectedIndex
                || envelope.Text is null
                || envelope.SpeakerSegments is null
                || string.IsNullOrWhiteSpace(envelope.PayloadSha256)
                || !string.Equals(
                    envelope.PayloadSha256,
                    ComputeCheckpointPayloadSha256(envelope.Text, envelope.SpeakerSegments),
                    StringComparison.OrdinalIgnoreCase))
            {
                DeleteInvalidCheckpointOrThrow(path);
                return null;
            }

            return envelope;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidDataException)
        {
            DeleteInvalidCheckpointOrThrow(path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new CheckpointStorageException(
                "Cosmic Transcriber found checkpoint data but could not read it safely. " +
                "Check permissions and free space in the output folder before retrying.",
                ex);
        }
    }

    private static async Task SaveCompletedResponseCheckpointAsync(
        string path,
        int index,
        string text,
        IReadOnlyList<SpeakerSegment> speakerSegments)
    {
        using var storageTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await WriteCheckpointAsync(path, index, text, speakerSegments, storageTimeout.Token);
        }
        catch (OperationCanceledException ex) when (storageTimeout.IsCancellationRequested)
        {
            throw new CheckpointStorageException(
                "OpenAI returned a completed transcription, but saving its checkpoint did not finish within 30 seconds. " +
                "The queue was stopped to avoid repeating billable work.",
                ex);
        }
    }

    private static Task WriteCheckpointAsync(
        string path,
        int index,
        string text,
        IReadOnlyList<SpeakerSegment> speakerSegments,
        CancellationToken cancellationToken)
    {
        var envelope = new CheckpointEnvelope(
            CheckpointSchema,
            index,
            text,
            speakerSegments,
            ComputeCheckpointPayloadSha256(text, speakerSegments));
        var content = JsonSerializer.Serialize(envelope, JsonOptions);
        if (Encoding.UTF8.GetByteCount(content) > MaximumCheckpointBytes)
        {
            throw new CheckpointStorageException(
                "OpenAI returned a completed transcription, but its resume checkpoint exceeded the safe local size limit. " +
                "The queue was stopped to avoid silently repeating billable work.",
                new InvalidDataException("The serialized checkpoint exceeded the configured size limit."));
        }

        return WriteAtomicTextAsync(path, content, cancellationToken);
    }

    private static async Task WriteAtomicTextAsync(
        string targetPath,
        string content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = targetPath + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            CreateCheckpointDirectory(Path.GetDirectoryName(targetPath)
                ?? throw new InvalidOperationException("Cannot resolve the checkpoint directory."));
            SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(targetPath, "The checkpoint file");

            var payload = new UTF8Encoding(false).GetBytes(content.TrimEnd() + Environment.NewLine);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            // Re-check immediately before replacement in case the destination was changed
            // while the durable temporary file was being written.
            SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(temporaryPath, "The temporary checkpoint file");
            SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(targetPath, "The checkpoint file");
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch (CheckpointStorageException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new CheckpointStorageException(
                "Cosmic Transcriber could not write its resume checkpoint data. Check free space and permissions in the output folder.",
                ex);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static IDisposable AcquireCacheUsage(string outputDirectory)
    {
        try
        {
            return CheckpointCacheService.AcquireUsage(outputDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new CheckpointStorageException(
                "Cosmic Transcriber could not open its resume checkpoint cache in the output folder.",
                ex);
        }
    }

    private static void CreateCheckpointDirectory(string path)
    {
        try
        {
            SafeFileSystem.CreateDirectoryWithoutReparsePoint(path, "The checkpoint folder");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new CheckpointStorageException(
                "Cosmic Transcriber could not create its resume checkpoint folder.",
                ex);
        }
    }

    private static async Task<FileStream> AcquireWorkLockAsync(
        string workDirectory,
        CancellationToken cancellationToken)
    {
        SafeFileSystem.EnsureDirectoryIsNotReparsePoint(workDirectory, "The checkpoint work folder");
        var path = Path.Combine(workDirectory, ".work.lock");
        SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(path, "The checkpoint work lock");
        for (var attempt = 0; attempt < 60; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var lease = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    SafeFileSystem.EnsureDirectoryIsNotReparsePoint(workDirectory, "The checkpoint work folder");
                    SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(path, "The checkpoint work lock");
                    return lease;
                }
                catch
                {
                    lease.Dispose();
                    throw;
                }
            }
            catch (IOException ex) when (IsSharingOrLockViolation(ex) && attempt < 59)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
            catch (IOException ex) when (IsSharingOrLockViolation(ex))
            {
                throw new IOException(
                    "This same source/configuration is already being processed by another Cosmic Transcriber instance.",
                    ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                throw new CheckpointStorageException(
                    "Cosmic Transcriber could not open its per-file checkpoint work lock. Check permissions and storage health in the output folder.",
                    ex);
            }
        }

        throw new InvalidOperationException("The work-lock retry loop ended unexpectedly.");
    }

    private static void TryDeleteDirectory(string path, Action<string>? log)
    {
        try
        {
            if (Directory.Exists(path))
            {
                SafeFileSystem.DeleteDirectoryTreeWithoutFollowingReparsePoints(path, CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            log?.Invoke("Temporary MP3 chunk cleanup warning: " + InputValidation.SingleLineForLog(ex.Message));
        }
    }

    private static void DeleteInvalidCheckpointOrThrow(string path)
    {
        try
        {
            // File.Delete is intentionally direct: unlike File.Exists, it does not turn
            // access denial into apparent absence. A vanished file is already clean.
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new CheckpointStorageException(
                "A saved checkpoint was invalid, but Cosmic Transcriber could not remove it. " +
                "Correct the output-folder permissions before retrying so paid work is not repeated.",
                ex);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Temporary-file cleanup is best-effort only; the primary operation has
            // already either succeeded or surfaced its own authoritative failure.
        }
    }

    private static bool IsSharingOrLockViolation(IOException exception)
    {
        var win32Code = exception.HResult & 0xFFFF;
        return win32Code is 32 or 33;
    }

    private static string ComputeCheckpointPayloadSha256(
        string text,
        IReadOnlyList<SpeakerSegment> speakerSegments)
    {
        var payload = JsonSerializer.Serialize(new
        {
            text,
            speaker_segments = speakerSegments
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static IReadOnlyList<SpeakerSegment> OffsetAndScopeSpeakerSegments(
        IReadOnlyList<SpeakerSegment> segments,
        AudioChunk chunk,
        int totalChunks)
    {
        if (segments.Count == 0)
        {
            return [];
        }

        var result = new List<SpeakerSegment>(segments.Count);
        foreach (var segment in segments)
        {
            var start = chunk.Start + segment.Start;
            var end = chunk.Start + segment.End;
            var maximumEnd = chunk.Start + chunk.Duration;
            if (start > maximumEnd)
            {
                continue;
            }

            start = start < chunk.Start ? chunk.Start : start;
            end = end > maximumEnd ? maximumEnd : end;
            if (end < start)
            {
                end = start;
            }

            var baseSpeaker = FormatSpeakerLabel(segment.Speaker);
            var speaker = totalChunks > 1
                ? $"Part {chunk.Index} · {baseSpeaker}"
                : baseSpeaker;
            result.Add(new SpeakerSegment(speaker, start, end, segment.Text));
        }

        return MergeAdjacentSpeakerSegments(result);
    }


    private static string FormatSpeakerLabel(string rawSpeaker)
    {
        var trimmed = rawSpeaker.Trim();
        if (trimmed.StartsWith("Speaker ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.Length <= 3 && trimmed.All(character => char.IsLetterOrDigit(character)))
        {
            return "Speaker " + trimmed.ToUpperInvariant();
        }

        return trimmed;
    }

    private static IReadOnlyList<SpeakerSegment> MergeAdjacentSpeakerSegments(
        IReadOnlyList<SpeakerSegment> segments)
    {
        if (segments.Count < 2)
        {
            return segments;
        }

        var merged = new List<SpeakerSegment>(segments.Count);
        foreach (var segment in segments)
        {
            if (merged.Count > 0)
            {
                var previous = merged[^1];
                var gap = segment.Start - previous.End;
                if (string.Equals(previous.Speaker, segment.Speaker, StringComparison.Ordinal)
                    && gap >= TimeSpan.Zero
                    && gap <= TimeSpan.FromSeconds(1.5))
                {
                    merged[^1] = previous with
                    {
                        End = segment.End > previous.End ? segment.End : previous.End,
                        Text = JoinSegmentText(previous.Text, segment.Text)
                    };
                    continue;
                }
            }

            merged.Add(segment);
        }

        return merged;
    }

    private static string JoinSegmentText(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return second.Trim();
        }

        if (string.IsNullOrWhiteSpace(second))
        {
            return first.Trim();
        }

        return first.TrimEnd() + " " + second.TrimStart();
    }

    private sealed record CheckpointEnvelope(
        string Schema,
        int Index,
        string Text,
        IReadOnlyList<SpeakerSegment> SpeakerSegments,
        string PayloadSha256);

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        private readonly Action<T> _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        public void Report(T value) => _callback(value);
    }
}
