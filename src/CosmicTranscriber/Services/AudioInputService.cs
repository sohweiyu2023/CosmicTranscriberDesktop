using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CosmicTranscriber.Models;
using CosmicTranscriber.Utilities;

namespace CosmicTranscriber.Services;

public enum AudioInputFailure { ConverterUnavailable, UnsupportedOrCorrupt, NoAudioStream, Empty, ResourceLimit, Timeout, ConversionFailed }
public sealed class AudioInputException(AudioInputFailure failure, string message, int? exitCode = null) : IOException(message)
{
    public AudioInputFailure Failure { get; } = failure;
    public int? ToolExitCode { get; } = exitCode;
}
public sealed record AudioInputInspection(AudioFileMetadata Metadata, bool RequiresConversion);

/// <summary>Local media probing and first-audio-stream normalization. No tool binaries are bundled/downloaded.</summary>
public sealed class AudioInputService
{
    public const long MaximumSourceBytes = 20L * 1024 * 1024 * 1024;
    public const long MaximumPreparedBytes = 2L * 1024 * 1024 * 1024;
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromHours(24);
    public const string NormalizationPolicy = "local-first-audio-libmp3lame-128k-44100-stereo-bitexact-v1";
    public const string PickerFilter = "Audio/video (common formats)|*.mp3;*.mp4;*.m4a;*.m4b;*.aac;*.wav;*.w64;*.flac;*.ogg;*.oga;*.opus;*.ogv;*.webm;*.mkv;*.mka;*.avi;*.mov;*.3gp;*.3g2;*.wma;*.wmv;*.asf;*.aiff;*.aif;*.aifc;*.caf;*.au;*.amr;*.ac3;*.eac3;*.mp2;*.mpga;*.mpeg;*.mpg;*.vob;*.ts;*.mts;*.m2ts;*.ape;*.tta;*.wv|All files (*.*)|*.*";
    // Deliberately exclude playlists, concat, image sequences, devices and network demuxers.
    public const string Demuxers = "aac,ac3,aiff,amr,asf,au,avi,caf,eac3,flac,matroska,webm,mov,mp3,mpeg,mpegts,ogg,wav,w64,ape,tta,wv";
    private readonly string? _ffmpeg, _ffprobe;
    public AudioInputService(string? ffmpeg = null, string? ffprobe = null) { _ffmpeg = ffmpeg; _ffprobe = ffprobe; }
    private static string Tool(string? explicitPath, string name)
    {
        if (explicitPath is not null) {
            if (Path.IsPathFullyQualified(explicitPath) && File.Exists(explicitPath)) return Path.GetFullPath(explicitPath);
            throw new AudioInputException(AudioInputFailure.ConverterUnavailable, "The configured media tool is unavailable. MP3 still works without it.");
        }
        // Search explicit absolute PATH directories only; no implicit/relative current-directory or bundled-tool lookup.
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
            var trimmed = directory.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(trimmed)) continue;
            var path = Path.Combine(trimmed, name + ".exe"); if (File.Exists(path)) return path;
        }
        throw new AudioInputException(AudioInputFailure.ConverterUnavailable, "Non-MP3 media requires separately installed FFmpeg and ffprobe on PATH. This app does not download them. MP3 works without them.");
    }
    private static string LocalPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || full.Length > 4096)
            throw new AudioInputException(AudioInputFailure.UnsupportedOrCorrupt, "Conversion accepts local files, not network URLs or shares. Copy the media locally first.");
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current)) {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Conversion paths cannot traverse filesystem links."); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
        }
        SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(full, "The media input"); return full;
    }
    public static IEnumerable<string> ExpandPaths(IEnumerable<string> paths, string? excludedCacheRoot, CancellationToken token)
    {
        foreach (var path in paths) {
            token.ThrowIfCancellationRequested(); if (string.IsNullOrWhiteSpace(path)) continue;
            if (File.Exists(path)) {
                var full = Path.GetFullPath(path);
                if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0 && !Inside(full, excludedCacheRoot)) yield return full;
            } else if (Directory.Exists(path)) {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, ReturnSpecialDirectories = false, AttributesToSkip = FileAttributes.ReparsePoint };
                // Extension is a picker hint, not a promise of codec support or the final gate.
                foreach (var file in Directory.EnumerateFiles(path, "*", options)) {
                    token.ThrowIfCancellationRequested(); var full = Path.GetFullPath(file);
                    if (!Inside(full, excludedCacheRoot)) yield return full;
                }
            }
        }
    }
    private static bool Inside(string path, string? directory) => !string.IsNullOrWhiteSpace(directory) && SafeFileSystem.IsPathWithinDirectory(path, directory);
    public async Task<AudioInputInspection> InspectAsync(string path, CancellationToken token)
    {
        using var diagnostic = StartupDiagnostics.Begin(DiagnosticArea.AudioImport);
        try {
            token.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase)) {
                try { var native = await Task.Run(() => Mp3MetadataService.Inspect(path), token); diagnostic.Stage(DiagnosticStage.Completed); return new(native, false); }
                catch (InvalidDataException) { /* A misleading extension can still contain safe decodable media. */ }
            }
            var full = LocalPath(path);
            await using var lease = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 262144, true);
            if (lease.Length == 0) throw new AudioInputException(AudioInputFailure.Empty, "The media file is empty.");
            if (lease.Length > MaximumSourceBytes) throw new AudioInputException(AudioInputFailure.ResourceLimit, "Media exceeds the 20 GiB local-conversion input limit.");
            var write = File.GetLastWriteTimeUtc(full); var length = lease.Length;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(lease, token));
            var args = InputArguments(full);
            args.InsertRange(0, new[] { "-v", "error", "-max_alloc", "67108864", "-threads", "2", "-probesize", "8388608", "-analyzeduration", "10000000" });
            args.AddRange(new[] { "-select_streams", "a:0", "-show_entries", "stream=codec_type,duration:format=duration,format_name", "-of", "json" });
            var probe = await RunAsync(Tool(_ffprobe, "ffprobe"), args, TimeSpan.FromSeconds(30), null, token);
            if (probe.ExitCode != 0 || probe.Truncated) throw new AudioInputException(AudioInputFailure.UnsupportedOrCorrupt, "Local media inspection failed. " + Detail(probe.Failure) + " No provider request was sent.", probe.ExitCode);
            using var doc = JsonDocument.Parse(probe.Output, new JsonDocumentOptions { MaxDepth = 8 });
            if (!doc.RootElement.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
                throw new AudioInputException(AudioInputFailure.NoAudioStream, "This file has no readable audio stream. Video-only files cannot be transcribed.");
            var stream = streams[0]; string? duration = stream.TryGetProperty("duration", out var sd) ? sd.GetString() : null;
            if (!double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && doc.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var fd))
                double.TryParse(fd.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
            if (!double.IsFinite(seconds) || seconds <= 0) throw new AudioInputException(AudioInputFailure.UnsupportedOrCorrupt, "A finite audio duration could not be established. No provider request was sent.");
            if (seconds > MaximumDuration.TotalSeconds) throw new AudioInputException(AudioInputFailure.ResourceLimit, "Media exceeds the 24-hour local-conversion duration limit. Split it locally first.");
            if (lease.Length != length || File.GetLastWriteTimeUtc(full) != write) throw new IOException("The media changed during inspection. Wait for copying to finish, then add it again.");
            diagnostic.Stage(DiagnosticStage.Completed); return new(new AudioFileMetadata(TimeSpan.FromSeconds(seconds), length, write, hash), true);
        } catch (OperationCanceledException) { diagnostic.Stage(DiagnosticStage.Cancelled, DiagnosticReason.UserCancelled); throw; }
        catch (AudioInputException ex) { diagnostic.Fail(ex, Reason(ex.Failure)); throw; }
        catch (Exception ex) { diagnostic.Fail(ex, DiagnosticReason.Validation); throw; }
    }
    private static DiagnosticReason Reason(AudioInputFailure failure) => failure switch {
        AudioInputFailure.ConverterUnavailable => DiagnosticReason.ConverterUnavailable,
        AudioInputFailure.NoAudioStream => DiagnosticReason.NoAudioStream,
        AudioInputFailure.ResourceLimit => DiagnosticReason.ResourceLimit,
        AudioInputFailure.Timeout => DiagnosticReason.Timeout,
        AudioInputFailure.ConversionFailed => DiagnosticReason.ConversionFailed,
        _ => DiagnosticReason.UnsupportedMedia };
    private static List<string> InputArguments(string path) => new() { "-protocol_whitelist", "file", "-format_whitelist", Demuxers, "-i", path };
    public async Task<AudioFileMetadata> ConvertAsync(string source, string destination, CancellationToken token)
    {
        using var operation = StartupDiagnostics.Begin(DiagnosticArea.AudioImport);
        try {
            token.ThrowIfCancellationRequested(); var full = LocalPath(source); destination = LocalPath(destination);
            await using var sourceLease = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 262144, true);
            if (sourceLease.Length > MaximumSourceBytes) throw new AudioInputException(AudioInputFailure.ResourceLimit, "Media exceeds the 20 GiB conversion input limit.");
            var verified = await InspectAsync(full, token);
            if (verified.Metadata.Duration > MaximumDuration) throw new AudioInputException(AudioInputFailure.ResourceLimit, "Media exceeds the 24-hour conversion limit.");
            if (File.Exists(destination)) throw new IOException("Prepared audio already exists; it was not overwritten.");
            SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(destination, "The prepared audio");
            var args = InputArguments(full); args.InsertRange(0, new[] { "-nostdin", "-hide_banner", "-v", "error", "-xerror", "-n", "-max_alloc", "67108864", "-threads", "2", "-filter_threads", "1", "-probesize", "8388608", "-analyzeduration", "10000000" });
            args.AddRange(new[] { "-map", "0:a:0", "-vn", "-sn", "-dn", "-map_metadata", "-1", "-map_chapters", "-1", "-flags", "+bitexact", "-fflags", "+bitexact", "-threads", "2", "-t", "86401", "-fs", MaximumPreparedBytes.ToString(CultureInfo.InvariantCulture), "-ac", "2", "-ar", "44100", "-c:a", "libmp3lame", "-b:a", "128k", "-f", "mp3", destination });
            var converted = await RunAsync(Tool(_ffmpeg, "ffmpeg"), args, TimeSpan.FromHours(2), destination, token);
            if (converted.ExitCode != 0) throw new AudioInputException(AudioInputFailure.ConversionFailed, "Local audio decoding/conversion failed. " + Detail(converted.Failure) + " No provider request was sent.", converted.ExitCode);
            var metadata = await Task.Run(() => Mp3MetadataService.Inspect(destination), token);
            if (metadata.Duration > MaximumDuration || metadata.SizeBytes >= MaximumPreparedBytes - 8192) throw new AudioInputException(AudioInputFailure.ResourceLimit, "Prepared audio reached the 24-hour / 2 GiB limit; it was not uploaded as a complete recording.");
            operation.Stage(DiagnosticStage.Completed); return metadata;
        } catch (OperationCanceledException) { operation.Stage(DiagnosticStage.Cancelled, DiagnosticReason.UserCancelled); throw; }
        catch (AudioInputException ex) { operation.Fail(ex, Reason(ex.Failure)); throw; }
        catch (Exception ex) { operation.Fail(ex, DiagnosticReason.StorageFailure); throw; }
    }
    private sealed record ToolResult(int ExitCode, string Output, bool Truncated, MediaToolFailure Failure);
    private static string Detail(MediaToolFailure failure) => failure switch {
        MediaToolFailure.UnsupportedCodec => "The installed converter does not support the required codec/encoder.",
        MediaToolFailure.InvalidContainer => "The container is invalid, damaged, or outside the safe local media allowlist.",
        MediaToolFailure.ProtectedMedia => "The converter reported protected/encrypted media; this app does not decrypt it.",
        MediaToolFailure.StorageFailure => "The converter reported a local file access or storage failure.",
        MediaToolFailure.NoAudio => "The file contains no usable selected audio stream.",
        _ => "The converter could not establish usable audio; it did not identify a specific supported cause." };
    private static MediaToolFailure Classify(string text)
    {
        // Error prose may contain arbitrary filenames and metadata. Recognize only complete
        // tool-generated fixed lines; never infer encryption/corruption from a substring.
        foreach (var line in text.Split('\n').Select(s => s.TrimEnd('\r'))) {
            if (line is "Unknown encoder 'libmp3lame'" or "Encoder (codec mp3) not found for output stream #0:0") return MediaToolFailure.UnsupportedCodec;
            if (line is "Output file #0 does not contain any stream" or "Stream map '0:a:0' matches no streams.") return MediaToolFailure.NoAudio;
        }
        return MediaToolFailure.Unknown;
    }
    private static async Task<(string Text, bool Truncated)> DrainAsync(StreamReader reader)
    {
        var text = new StringBuilder(); var buffer = new char[4096]; int read; bool truncated = false;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0) { var keep = Math.Min(read, 65536 - text.Length); if (keep > 0) text.Append(buffer, 0, keep); if (keep < read) truncated = true; }
        return (text.ToString(), truncated);
    }
    private static async Task<ToolResult> RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, string? output, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(timeout);
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        try { if (!process.Start()) throw new AudioInputException(AudioInputFailure.ConverterUnavailable, "The media converter could not start."); }
        catch (System.ComponentModel.Win32Exception) { throw new AudioInputException(AudioInputFailure.ConverterUnavailable, "The media tool could not start; verify the separately installed converter. No provider request was sent."); }
        var clock = Stopwatch.StartNew(); var kind = output is null ? MediaToolKind.Probe : MediaToolKind.Convert;
        process.StandardInput.Close(); var stdout = DrainAsync(process.StandardOutput); var stderr = DrainAsync(process.StandardError);
        var exited = process.WaitForExitAsync();
        try {
            while (!process.HasExited) {
                deadline.Token.ThrowIfCancellationRequested();
                if (output is not null && File.Exists(output) && new FileInfo(output).Length > MaximumPreparedBytes) throw new AudioInputException(AudioInputFailure.ResourceLimit, "Prepared audio exceeded the 2 GiB limit; local conversion stopped before upload.");
                await Task.WhenAny(exited, Task.Delay(100, deadline.Token));
            }
            var drains = Task.WhenAll(stdout, stderr);
            if (await Task.WhenAny(drains, Task.Delay(TimeSpan.FromSeconds(5), deadline.Token)) != drains) { deadline.Token.ThrowIfCancellationRequested(); throw new IOException("Media tool output did not close; conversion result is unavailable."); }
            var result = await stdout; var error = await stderr;
            StartupDiagnostics.WriteMediaToolResult(kind, process.ExitCode, process.ExitCode == 0 ? MediaToolFailure.None : Classify(error.Text), clock.Elapsed.TotalMilliseconds, detailsTruncated: result.Truncated || error.Truncated);
            return new(process.ExitCode, result.Text, result.Truncated, process.ExitCode == 0 ? MediaToolFailure.None : Classify(error.Text));
        } catch {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); if (!process.WaitForExit(5000)) throw new IOException("The owned media conversion process did not stop. No provider request was sent."); }
            await Task.WhenAny(Task.WhenAll(stdout, stderr), Task.Delay(5000));
            StartupDiagnostics.WriteMediaToolResult(kind, process.HasExited ? process.ExitCode : null, MediaToolFailure.Unknown, clock.Elapsed.TotalMilliseconds, token.IsCancellationRequested, !token.IsCancellationRequested && deadline.IsCancellationRequested);
            if (!token.IsCancellationRequested && deadline.IsCancellationRequested) throw new AudioInputException(AudioInputFailure.Timeout, "Local media inspection/conversion timed out. No provider request was sent.");
            throw;
        }
    }
}
