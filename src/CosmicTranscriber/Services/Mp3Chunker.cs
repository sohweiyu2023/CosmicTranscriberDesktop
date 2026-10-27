using CosmicTranscriber.Models;
using CosmicTranscriber.Utilities;
using NAudio.Wave;

namespace CosmicTranscriber.Services;

public sealed class AudioPreparationStorageException : IOException
{
    public AudioPreparationStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class Mp3Chunker
{
    private static readonly TimeSpan TargetPreroll = TimeSpan.FromSeconds(1.25);

    public async Task<IReadOnlyList<AudioChunk>> CreateChunksAsync(
        string sourcePath,
        string chunkDirectory,
        int targetMegabytes,
        int maximumChunkMinutes,
        TimeSpan knownDuration,
        bool enforceDurationLimit,
        bool includePreroll,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var sourceInfo = new FileInfo(sourcePath);
        var targetBytes = Math.Min(
            Math.Clamp(targetMegabytes, 5, 24) * 1_000_000L,
            OpenAiTranscriptionClient.MaximumAudioPayloadBytes);
        var targetDuration = enforceDurationLimit
            ? TimeSpan.FromMinutes(Math.Clamp(maximumChunkMinutes, 3, 5))
            : TimeSpan.MaxValue;

        if (sourceInfo.Length <= targetBytes
            && knownDuration > TimeSpan.Zero
            && knownDuration <= targetDuration)
        {
            progress?.Report(1);
            return
            [
                new AudioChunk(1, sourcePath, TimeSpan.Zero, knownDuration, TimeSpan.Zero, sourceInfo.Length, true)
            ];
        }

        PrepareChunkDirectory(chunkDirectory, cancellationToken);

        var chunks = new List<AudioChunk>();
        await using var input = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 256,
            useAsync: true);
        Mp3AudioUtilities.SkipLeadingId3V2(input);

        FileStream? output = null;
        string? outputPath = null;
        long currentBytes = 0;
        var logicalStart = TimeSpan.Zero;
        var logicalDuration = TimeSpan.Zero;
        var currentPreroll = TimeSpan.Zero;
        var chunkIndex = 1;
        var lastReportedFraction = -1.0;
        var recentFrames = new Queue<(byte[] Data, TimeSpan Duration)>();
        var recentDuration = TimeSpan.Zero;
        var currentFrameOffsets = new List<long>();
        byte[]? firstChunkMetadataFrame = null;
        var firstFrame = true;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = Mp3Frame.LoadFromStream(input);
                if (frame is null)
                {
                    break;
                }

                if (frame.SampleRate <= 0 || frame.SampleCount <= 0)
                {
                    throw new InvalidDataException("The MP3 contains an invalid audio frame.");
                }

                var frameData = (frame.RawData
                    ?? throw new InvalidDataException("The MP3 frame contains no audio data.")).ToArray();
                var frameDuration = TimeSpan.FromSeconds((double)frame.SampleCount / frame.SampleRate);

                if (firstFrame && Mp3AudioUtilities.IsVbrMetadataFrame(frame))
                {
                    firstFrame = false;
                    outputPath = Path.Combine(chunkDirectory, $"chunk-{chunkIndex:000}.mp3");
                    output = OpenOutput(outputPath);

                    // LAME-compatible decoders retain this frame because the following audio frame may
                    // reference its Layer III bit reservoir. Keep it as a decoder reservoir carrier, but
                    // do not count its whole-file Xing/Info/VBRI metadata in logical timestamps or duration.
                    firstChunkMetadataFrame = frameData;
                    currentFrameOffsets.Add(currentBytes);
                    await WriteOutputAsync(output, frameData, cancellationToken);
                    currentBytes += frameData.LongLength;
                    continue;
                }

                firstFrame = false;

                if (output is null)
                {
                    outputPath = Path.Combine(chunkDirectory, $"chunk-{chunkIndex:000}.mp3");
                    output = OpenOutput(outputPath);
                }

                var exceedsByteLimit = currentBytes + frameData.Length > targetBytes;
                var exceedsDurationLimit = logicalDuration + frameDuration > targetDuration;
                if (logicalDuration > TimeSpan.Zero && (exceedsByteLimit || exceedsDurationLimit))
                {
                    await FinalizeOutputAsync(
                        output,
                        cancellationToken,
                        firstChunkMetadataFrame,
                        currentFrameOffsets,
                        currentBytes);
                    output = null;
                    chunks.Add(new AudioChunk(
                        chunkIndex,
                        outputPath!,
                        logicalStart,
                        logicalDuration,
                        currentPreroll,
                        currentBytes,
                        false));

                    logicalStart += logicalDuration;
                    logicalDuration = TimeSpan.Zero;
                    currentBytes = 0;
                    currentPreroll = TimeSpan.Zero;
                    currentFrameOffsets.Clear();
                    firstChunkMetadataFrame = null;
                    chunkIndex++;
                    outputPath = Path.Combine(chunkDirectory, $"chunk-{chunkIndex:000}.mp3");
                    output = OpenOutput(outputPath);

                    if (includePreroll)
                    {
                        var availablePrerollBytes = Math.Max(0L, targetBytes - frameData.LongLength);
                        foreach (var prerollFrame in SelectContiguousPrerollSuffix(recentFrames, availablePrerollBytes))
                        {
                            currentFrameOffsets.Add(currentBytes);
                            await WriteOutputAsync(output, prerollFrame.Data, cancellationToken);
                            currentBytes += prerollFrame.Data.LongLength;
                            currentPreroll += prerollFrame.Duration;
                        }
                    }

                    recentFrames.Clear();
                    recentDuration = TimeSpan.Zero;
                }

                currentFrameOffsets.Add(currentBytes);
                await WriteOutputAsync(output, frameData, cancellationToken);
                currentBytes += frameData.Length;
                logicalDuration += frameDuration;

                recentFrames.Enqueue((frameData, frameDuration));
                recentDuration += frameDuration;
                while (recentFrames.Count > 1 && recentDuration - recentFrames.Peek().Duration >= TargetPreroll)
                {
                    recentDuration -= recentFrames.Dequeue().Duration;
                }

                var fraction = input.Length == 0
                    ? 0
                    : Math.Clamp((double)input.Position / input.Length, 0, 1);
                if (fraction - lastReportedFraction >= 0.005)
                {
                    lastReportedFraction = fraction;
                    progress?.Report(fraction);
                }
            }

            if (output is not null && logicalDuration > TimeSpan.Zero)
            {
                await FinalizeOutputAsync(
                    output,
                    cancellationToken,
                    firstChunkMetadataFrame,
                    currentFrameOffsets,
                    currentBytes);
                output = null;
                chunks.Add(new AudioChunk(
                    chunkIndex,
                    outputPath!,
                    logicalStart,
                    logicalDuration,
                    currentPreroll,
                    currentBytes,
                    false));
            }
        }
        finally
        {
            if (output is not null)
            {
                await DisposeOutputQuietlyAsync(output);
            }
        }

        if (chunks.Count == 0)
        {
            throw new InvalidDataException("No valid MP3 audio frames could be read from the selected file.");
        }

        if (chunks.Any(chunk => chunk.SizeBytes > OpenAiTranscriptionClient.MaximumAudioPayloadBytes))
        {
            throw new InvalidDataException("A prepared MP3 chunk exceeded the safe OpenAI upload ceiling.");
        }

        progress?.Report(1);
        return chunks;
    }


    private static IReadOnlyList<(byte[] Data, TimeSpan Duration)> SelectContiguousPrerollSuffix(
        Queue<(byte[] Data, TimeSpan Duration)> recentFrames,
        long availableBytes)
    {
        var candidates = recentFrames.ToArray();
        var firstSelectedIndex = candidates.Length;
        long selectedBytes = 0;

        for (var index = candidates.Length - 1; index >= 0; index--)
        {
            var frameBytes = candidates[index].Data.LongLength;
            if (frameBytes > availableBytes - selectedBytes)
            {
                break;
            }

            selectedBytes += frameBytes;
            firstSelectedIndex = index;
        }

        return candidates[firstSelectedIndex..];
    }

    private static void PrepareChunkDirectory(string chunkDirectory, CancellationToken cancellationToken)
    {
        try
        {
            SafeFileSystem.CreateDirectoryWithoutReparsePoint(
                chunkDirectory,
                "The temporary MP3 chunk folder");
            foreach (var entry in Directory.EnumerateFileSystemEntries(chunkDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                var name = Path.GetFileName(entry);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
                    || !IsChunkOutputName(name))
                {
                    throw new IOException(
                        "The temporary MP3 chunk folder contains an unexpected item and was not reused: " + entry);
                }

                File.Delete(entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new AudioPreparationStorageException(
                "Cosmic Transcriber could not prepare its temporary MP3 chunk folder. Check free space and permissions in the output folder.",
                ex);
        }
    }

    private static bool IsChunkOutputName(string? name)
    {
        const string prefix = "chunk-";
        const string extension = ".mp3";
        if (string.IsNullOrWhiteSpace(name)
            || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var digits = name[prefix.Length..^extension.Length];
        return digits.Length >= 3 && digits.All(static ch => ch is >= '0' and <= '9');
    }

    private static FileStream OpenOutput(string path)
    {
        try
        {
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 256, useAsync: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AudioPreparationStorageException(
                "Cosmic Transcriber could not create a temporary MP3 chunk. Check free space and permissions in the output folder.",
                ex);
        }
    }

    private static async Task WriteOutputAsync(
        FileStream output,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        try
        {
            await output.WriteAsync(data, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AudioPreparationStorageException(
                "Cosmic Transcriber could not write a temporary MP3 chunk. Check free space and permissions in the output folder.",
                ex);
        }
    }

    private static async Task FinalizeOutputAsync(
        FileStream output,
        CancellationToken cancellationToken,
        byte[]? metadataFrame,
        IReadOnlyList<long> frameOffsets,
        long totalBytes)
    {
        try
        {
            if (metadataFrame is not null)
            {
                var rewrittenMetadata = metadataFrame.ToArray();
                Mp3AudioUtilities.RewriteVbrMetadataForChunk(
                    rewrittenMetadata,
                    frameOffsets.Count,
                    totalBytes,
                    frameOffsets);

                // Flush buffered frame data before seeking back to replace the first frame.
                // The frame length is unchanged, so all later MPEG frame offsets remain stable.
                await output.FlushAsync(cancellationToken);
                output.Position = 0;
                await output.WriteAsync(rewrittenMetadata, cancellationToken);
                output.Position = totalBytes;
            }

            await output.FlushAsync(cancellationToken);
            await output.DisposeAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AudioPreparationStorageException(
                "Cosmic Transcriber could not finalize a temporary MP3 chunk. Check free space and output-folder permissions.",
                ex);
        }
    }

    private static async Task DisposeOutputQuietlyAsync(FileStream output)
    {
        try
        {
            await output.DisposeAsync();
        }
        catch (IOException)
        {
            // Preserve the primary source/cancellation/preparation exception. Successful paths
            // always use FinalizeOutputAsync, which reports flush/dispose failures explicitly.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale as above.
        }
    }
}
