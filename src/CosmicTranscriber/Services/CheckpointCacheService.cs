using CosmicTranscriber.Utilities;

namespace CosmicTranscriber.Services;

public static class CheckpointCacheService
{
    public const string CacheFolderName = ".cosmic-transcriber-cache";
    private const string UsageLockFileName = ".cache-usage.lock";

    public static string GetCacheRoot(string outputDirectory)
        => Path.Combine(Path.GetFullPath(outputDirectory), CacheFolderName);

    public static bool IsPathInsideCache(string path, string outputDirectory)
        => SafeFileSystem.IsPathWithinDirectory(path, GetCacheRoot(outputDirectory));

    public static bool CacheExists(string outputDirectory)
    {
        var cacheRoot = GetCacheRoot(outputDirectory);
        try
        {
            var attributes = File.GetAttributes(cacheRoot);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new IOException("The checkpoint cache path exists but is not a directory: " + cacheRoot);
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "The checkpoint cache folder cannot be a symbolic link, junction, mount point, or other reparse point: " + cacheRoot);
            }

            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    public static void ValidateWorkDirectoryForUse(
        string workDirectory,
        CancellationToken cancellationToken)
    {
        SafeFileSystem.ValidateDirectoryTreeWithoutReparsePoints(workDirectory, cancellationToken);
        ValidateKnownWorkDirectoryLayout(workDirectory, cancellationToken);
    }

    public static IDisposable AcquireUsage(string outputDirectory)
    {
        var cacheRoot = GetCacheRoot(outputDirectory);
        SafeFileSystem.CreateDirectoryWithoutReparsePoint(cacheRoot, "The checkpoint cache folder");
        TryHide(cacheRoot);
        var lockPath = Path.Combine(cacheRoot, UsageLockFileName);
        SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(lockPath, "The checkpoint cache usage lock");

        try
        {
            using var creator = new FileStream(
                lockPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read);
            creator.Flush(flushToDisk: true);
        }
        catch (IOException ex) when (IsAlreadyExists(ex))
        {
            // Another process won the one-time lock-file creation race.
        }

        SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(lockPath, "The checkpoint cache usage lock");

        // Many active transcription processes may hold compatible shared read leases.
        // A cache clear requests an exclusive read/write lease and therefore fails safely.
        // Retry only actual sharing/lock violations; full disks, damaged paths and other
        // storage failures must surface immediately instead of being disguised as contention.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                var lease = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                try
                {
                    SafeFileSystem.EnsureDirectoryIsNotReparsePoint(cacheRoot, "The checkpoint cache folder");
                    SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(lockPath, "The checkpoint cache usage lock");
                    return lease;
                }
                catch
                {
                    lease.Dispose();
                    throw;
                }
            }
            catch (IOException ex) when (IsSharingOrLockViolation(ex) && attempt < 19)
            {
                Thread.Sleep(25);
            }
            catch (IOException ex) when (IsSharingOrLockViolation(ex))
            {
                throw new IOException("The checkpoint cache usage lock is held by another operation.", ex);
            }
        }

        throw new InvalidOperationException("The checkpoint cache lock retry loop ended unexpectedly.");
    }

    public static Task<int> ClearAsync(string outputDirectory, CancellationToken cancellationToken)
        => Task.Run(() => Clear(outputDirectory, cancellationToken), cancellationToken);

    private static int Clear(string outputDirectory, CancellationToken cancellationToken)
    {
        var cacheRoot = GetCacheRoot(outputDirectory);
        if (!CacheExists(outputDirectory))
        {
            return 0;
        }

        SafeFileSystem.EnsureDirectoryIsNotReparsePoint(cacheRoot, "The checkpoint cache folder");
        var lockPath = Path.Combine(cacheRoot, UsageLockFileName);
        SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(lockPath, "The checkpoint cache usage lock");
        using var exclusive = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        SafeFileSystem.EnsureDirectoryIsNotReparsePoint(cacheRoot, "The checkpoint cache folder");
        SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(lockPath, "The checkpoint cache usage lock");

        var directories = Directory.EnumerateDirectories(cacheRoot).ToArray();
        var files = Directory.EnumerateFiles(cacheRoot).ToArray();

        // Fail before deleting anything when the cache root contains an unexpected item.
        // This prevents a user file, junction, or foreign application folder from being
        // silently removed merely because it was placed under our hidden cache directory.
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCheckpointWorkDirectoryName(Path.GetFileName(directory)))
            {
                throw new IOException(
                    "The checkpoint cache contains an unexpected folder and was not cleared: " + directory);
            }

            SafeFileSystem.ValidateDirectoryTreeWithoutReparsePoints(directory, cancellationToken);
            ValidateKnownWorkDirectoryLayout(directory, cancellationToken);
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(lockPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "The checkpoint cache contains an unexpected file and was not cleared: " + file);
            }
        }

        var deleted = 0;
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateKnownWorkDirectoryLayout(directory, cancellationToken);
            deleted += SafeFileSystem.DeleteDirectoryTreeWithoutFollowingReparsePoints(
                directory,
                cancellationToken);
        }

        return deleted;
    }

    private static void ValidateKnownWorkDirectoryLayout(
        string workDirectory,
        CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(workDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "The checkpoint work folder contains a file-system link and was not used or cleared: " + entry);
            }

            var name = Path.GetFileName(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (string.Equals(name, "transcripts", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateTranscriptDirectory(entry, cancellationToken);
                    continue;
                }

                if (name.StartsWith("chunks-", StringComparison.OrdinalIgnoreCase)
                    && IsAsciiDigits(name["chunks-".Length..]))
                {
                    ValidateChunkDirectory(entry, cancellationToken);
                    continue;
                }

                throw new IOException(
                    "The checkpoint work folder contains an unexpected directory and was not used or cleared: " + entry);
            }

            if (string.Equals(name, ".work.lock", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase)
                || IsAtomicTemporaryName(name, "manifest", ".json"))
            {
                continue;
            }

            throw new IOException(
                "The checkpoint work folder contains an unexpected file and was not used or cleared: " + entry);
        }
    }

    private static void ValidateTranscriptDirectory(
        string directory,
        CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(entry);
            var name = Path.GetFileName(entry);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
                || (!IsChunkFileName(name, ".json")
                    && !IsAtomicChunkTemporaryName(name, ".json")))
            {
                throw new IOException(
                    "The checkpoint transcript folder contains an unexpected item and was not used or cleared: " + entry);
            }
        }
    }

    private static void ValidateChunkDirectory(
        string directory,
        CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(entry);
            var name = Path.GetFileName(entry);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
                || !IsChunkFileName(name, ".mp3"))
            {
                throw new IOException(
                    "The temporary MP3 chunk folder contains an unexpected item and was not used or cleared: " + entry);
            }
        }
    }

    private static bool IsChunkFileName(string? name, string extension)
    {
        if (string.IsNullOrWhiteSpace(name)
            || !name.StartsWith("chunk-", StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var digits = name["chunk-".Length..^extension.Length];
        return digits.Length >= 3 && IsAsciiDigits(digits);
    }

    private static bool IsAtomicChunkTemporaryName(string? name, string extension)
    {
        if (string.IsNullOrWhiteSpace(name)
            || !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = name.Split('.');
        return parts.Length == 5
            && IsChunkFileName(parts[0] + "." + parts[1], extension)
            && IsAsciiDigits(parts[2])
            && IsGuidToken(parts[3])
            && string.Equals(parts[4], "tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAtomicTemporaryName(string? name, string stem, string extension)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var parts = name.Split('.');
        return parts.Length == 5
            && string.Equals(parts[0], stem, StringComparison.OrdinalIgnoreCase)
            && string.Equals("." + parts[1], extension, StringComparison.OrdinalIgnoreCase)
            && IsAsciiDigits(parts[2])
            && IsGuidToken(parts[3])
            && string.Equals(parts[4], "tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGuidToken(string value)
        => value.Length == 32 && value.All(Uri.IsHexDigit);

    private static bool IsAsciiDigits(string value)
        => value.Length > 0 && value.All(static ch => ch is >= '0' and <= '9');

    private static bool IsCheckpointWorkDirectoryName(string? name)
        => name is { Length: 32 } && name.All(Uri.IsHexDigit);

    private static bool IsAlreadyExists(IOException exception)
    {
        var win32Code = exception.HResult & 0xFFFF;
        return win32Code is 80 or 183;
    }

    private static bool IsSharingOrLockViolation(IOException exception)
    {
        var win32Code = exception.HResult & 0xFFFF;
        return win32Code is 32 or 33;
    }

    private static void TryHide(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            info.Attributes |= FileAttributes.Hidden;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Hidden is cosmetic. Cache safety must not depend on it.
        }
    }
}
