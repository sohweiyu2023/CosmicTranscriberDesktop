using CosmicTranscriber.Models;
using CosmicTranscriber.Utilities;

namespace CosmicTranscriber.Services;

public sealed class TranscriptExportException : Exception
{
    public TranscriptExportException(string message) : base(message) { }

    public TranscriptExportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class TranscriptOutputWriter
{
    public IReadOnlyList<string> WriteIndividual(
        FileTranscriptionResult result,
        string outputDirectory,
        bool exportText,
        bool exportMarkdown,
        bool includeTimestamps)
    {
        ArgumentNullException.ThrowIfNull(result);
        EnsureTranscriptSize([result]);
        var written = new List<string>();

        try
        {
            Directory.CreateDirectory(outputDirectory);
            var baseName = Formatters.SafeFileName(result.SourceName) + ".transcript";

            if (exportText)
            {
                var path = WriteUniqueAtomic(
                    Path.Combine(outputDirectory, baseName + ".txt"),
                    BuildText(result, includeTimestamps));
                written.Add(path);
            }

            if (exportMarkdown)
            {
                var path = WriteUniqueAtomic(
                    Path.Combine(outputDirectory, baseName + ".md"),
                    BuildMarkdown(result, includeTimestamps));
                written.Add(path);
            }

            return written;
        }
        catch (Exception ex)
        {
            DeletePartialOutputs(written);
            throw new TranscriptExportException("Could not export the transcript files.", ex);
        }
    }

    public IReadOnlyList<string> WriteCombined(
        IReadOnlyList<FileTranscriptionResult> results,
        string title,
        string outputDirectory,
        bool exportText,
        bool exportMarkdown,
        bool includeTimestamps)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
        {
            throw new ArgumentException("At least one completed transcript is required for a combined export.", nameof(results));
        }

        EnsureTranscriptSize(results);
        var written = new List<string>();

        try
        {
            Directory.CreateDirectory(outputDirectory);
            var effectiveTitle = string.IsNullOrWhiteSpace(title)
                ? "Combined Transcript"
                : InputValidation.SingleLineForLog(
                    InputValidation.TruncateWithoutSplittingSurrogatePair(
                        title.Trim(),
                        InputValidation.MaximumCombinedTitleCharacters),
                    InputValidation.MaximumCombinedTitleCharacters);
            if (string.IsNullOrWhiteSpace(effectiveTitle))
            {
                effectiveTitle = "Combined Transcript";
            }

            var safeTitle = Formatters.SafeFileName(effectiveTitle);

            if (exportText)
            {
                var builder = new StringBuilder();
                builder.AppendLine(effectiveTitle);
                builder.AppendLine(new string('=', Math.Max(8, Math.Min(effectiveTitle.Length, 120))));
                builder.AppendLine();
                foreach (var result in results)
                {
                    builder.AppendLine(result.SourceName);
                    builder.AppendLine(new string('-', Math.Max(8, Math.Min(result.SourceName.Length, 120))));
                    builder.AppendLine(BuildTextBody(result, includeTimestamps));
                    builder.AppendLine();
                }

                var path = WriteUniqueAtomic(
                    Path.Combine(outputDirectory, safeTitle + ".txt"),
                    builder.ToString());
                written.Add(path);
            }

            if (exportMarkdown)
            {
                var builder = new StringBuilder();
                builder.Append("# ").AppendLine(MarkdownHeading(effectiveTitle));
                builder.AppendLine();
                foreach (var result in results)
                {
                    builder.Append("## ").AppendLine(MarkdownHeading(result.SourceName));
                    builder.AppendLine();
                    builder.AppendLine(BuildMarkdownBody(result, includeTimestamps, headingLevel: 3));
                    builder.AppendLine();
                }

                var path = WriteUniqueAtomic(
                    Path.Combine(outputDirectory, safeTitle + ".md"),
                    builder.ToString());
                written.Add(path);
            }

            return written;
        }
        catch (Exception ex)
        {
            DeletePartialOutputs(written);
            throw new TranscriptExportException("Could not export the combined transcript files.", ex);
        }
    }

    private static string BuildText(FileTranscriptionResult result, bool includeTimestamps)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.SourceName);
        builder.AppendLine(new string('=', Math.Max(8, Math.Min(result.SourceName.Length, 120))));
        builder.AppendLine();
        builder.Append(BuildTextBody(result, includeTimestamps));
        return builder.ToString();
    }

    private static string BuildTextBody(FileTranscriptionResult result, bool includeTimestamps)
    {
        if (result.HasSpeakerDiarization
            && result.Chunks.Any(chunk => chunk.SpeakerSegments.Count > 0))
        {
            return BuildDiarizedTextBody(result, includeTimestamps);
        }

        var builder = new StringBuilder();
        foreach (var chunk in result.Chunks)
        {
            if (includeTimestamps)
            {
                builder.Append('[')
                    .Append(Formatters.Timestamp(chunk.Start))
                    .Append('–')
                    .Append(Formatters.Timestamp(chunk.End))
                    .AppendLine("]");
            }

            builder.AppendLine(chunk.Text.Trim());
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildDiarizedTextBody(
        FileTranscriptionResult result,
        bool includeTimestamps)
    {
        var builder = new StringBuilder();
        AppendChunkScopedSpeakerNotice(builder, result);

        foreach (var chunk in result.Chunks)
        {
            if (chunk.SpeakerSegments.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(chunk.Text))
                {
                    if (includeTimestamps)
                    {
                        builder.Append('[')
                            .Append(Formatters.Timestamp(chunk.Start))
                            .Append('–')
                            .Append(Formatters.Timestamp(chunk.End))
                            .AppendLine("]");
                    }

                    builder.AppendLine(chunk.Text.Trim());
                    builder.AppendLine();
                }

                continue;
            }

            foreach (var segment in chunk.SpeakerSegments)
            {
                if (includeTimestamps)
                {
                    builder.Append('[')
                        .Append(Formatters.Timestamp(segment.Start))
                        .Append('–')
                        .Append(Formatters.Timestamp(segment.End))
                        .Append("] ");
                }

                builder.Append(segment.Speaker).AppendLine(":");
                builder.AppendLine(segment.Text.Trim());
                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendChunkScopedSpeakerNotice(
        StringBuilder builder,
        FileTranscriptionResult result)
    {
        if (result.Chunks.Count <= 1)
        {
            return;
        }

        builder.AppendLine(
            "Note: This recording exceeded one upload. Speaker labels are scoped to each Part; " +
            "the same letter in different Parts does not guarantee the same person.");
        builder.AppendLine();
    }

    private static string BuildMarkdown(FileTranscriptionResult result, bool includeTimestamps)
    {
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(MarkdownHeading(result.SourceName));
        builder.AppendLine();
        builder.Append(BuildMarkdownBody(result, includeTimestamps, headingLevel: 2));
        return builder.ToString();
    }

    private static string BuildMarkdownBody(
        FileTranscriptionResult result,
        bool includeTimestamps,
        int headingLevel)
    {
        if (result.HasSpeakerDiarization
            && result.Chunks.Any(chunk => chunk.SpeakerSegments.Count > 0))
        {
            return BuildDiarizedMarkdownBody(result, includeTimestamps, headingLevel);
        }

        var builder = new StringBuilder();
        foreach (var chunk in result.Chunks)
        {
            if (includeTimestamps)
            {
                builder.Append(new string('#', headingLevel))
                    .Append(' ')
                    .Append(Formatters.Timestamp(chunk.Start))
                    .Append('–')
                    .AppendLine(Formatters.Timestamp(chunk.End));
                builder.AppendLine();
            }

            builder.AppendLine(chunk.Text.Trim());
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildDiarizedMarkdownBody(
        FileTranscriptionResult result,
        bool includeTimestamps,
        int headingLevel)
    {
        var builder = new StringBuilder();
        if (result.Chunks.Count > 1)
        {
            builder.AppendLine(
                "> **Speaker-label scope:** This recording exceeded one upload. Labels are scoped to each Part; " +
                "the same letter in different Parts does not guarantee the same person.");
            builder.AppendLine();
        }

        foreach (var chunk in result.Chunks)
        {
            if (chunk.SpeakerSegments.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(chunk.Text))
                {
                    if (includeTimestamps)
                    {
                        builder.Append(new string('#', headingLevel))
                            .Append(' ')
                            .Append(Formatters.Timestamp(chunk.Start))
                            .Append('–')
                            .AppendLine(Formatters.Timestamp(chunk.End));
                        builder.AppendLine();
                    }

                    builder.AppendLine(chunk.Text.Trim());
                    builder.AppendLine();
                }

                continue;
            }

            foreach (var segment in chunk.SpeakerSegments)
            {
                builder.Append(new string('#', headingLevel))
                    .Append(' ')
                    .Append(MarkdownHeading(segment.Speaker));
                if (includeTimestamps)
                {
                    builder.Append(" — ")
                        .Append(Formatters.Timestamp(segment.Start))
                        .Append('–')
                        .Append(Formatters.Timestamp(segment.End));
                }

                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine(segment.Text.Trim());
                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string WriteUniqueAtomic(string requestedPath, string content)
    {
        var directory = Path.GetDirectoryName(requestedPath)
            ?? throw new InvalidOperationException("Cannot resolve the transcript output directory.");
        Directory.CreateDirectory(directory);
        var name = Path.GetFileNameWithoutExtension(requestedPath) ?? "transcript";
        var extension = Path.GetExtension(requestedPath);
        var temporaryPath = Path.Combine(
            directory,
            $".{name}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");

        try
        {
            var payload = new UTF8Encoding(false).GetBytes(content.TrimEnd() + Environment.NewLine);
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            for (var index = 1; index < 10_000; index++)
            {
                var candidate = index == 1
                    ? requestedPath
                    : Path.Combine(directory, $"{name}-{index}{extension}");
                try
                {
                    SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(
                        temporaryPath,
                        "The temporary transcript output file");
                    File.Move(temporaryPath, candidate, overwrite: false);
                    return candidate;
                }
                catch (IOException) when (File.Exists(candidate))
                {
                    // Another process already claimed this exact name. Try the next suffix.
                }
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }

        throw new IOException("Could not create a unique output filename.");
    }

    private static void EnsureTranscriptSize(IEnumerable<FileTranscriptionResult> results)
    {
        long totalCharacters = 0;
        foreach (var result in results)
        {
            foreach (var chunk in result.Chunks)
            {
                if (chunk.Text.Length > TranscriptionCoordinator.MaximumTranscriptCharactersPerFile - totalCharacters)
                {
                    throw new TranscriptExportException(
                        $"The requested export exceeds the safe {TranscriptionCoordinator.MaximumTranscriptCharactersPerFile:N0}-character in-memory limit. " +
                        "Export fewer files together or split the source recording.");
                }

                totalCharacters += chunk.Text.Length;
            }
        }
    }

    private static string MarkdownHeading(string value)
        => InputValidation.SingleLineForLog(value, 500).Replace("#", "\\#", StringComparison.Ordinal);

    private static void DeletePartialOutputs(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            TryDelete(path);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort rollback; the original error remains authoritative.
        }
    }
}
