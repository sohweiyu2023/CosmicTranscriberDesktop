using System.Security;

namespace CosmicTranscriber.Utilities;

/// <summary>
/// Defensive helpers for application-owned directories. Recursive deletion must never
/// traverse a directory symbolic link, junction, mount point, or other reparse point.
/// </summary>
public static class SafeFileSystem
{
    public static void CreateDirectoryWithoutReparsePoint(string path, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(path);
        EnsureDirectoryIsNotReparsePoint(path, description);
    }

    public static void EnsureDirectoryIsNotReparsePoint(string path, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new IOException($"{description} is not a directory: {path}");
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                $"{description} cannot be a symbolic link, junction, mount point, or other reparse point: {path}");
        }
    }

    public static void EnsureFileIsNotReparsePointIfPresent(string path, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                throw new IOException($"{description} is unexpectedly a directory: {path}");
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"{description} cannot be a symbolic link or other reparse point: {path}");
            }
        }
        catch (FileNotFoundException)
        {
            // Absence is valid for a file that is about to be created.
        }
        catch (DirectoryNotFoundException)
        {
            // Absence is valid for a file whose parent has not been created yet.
        }
    }

    public static bool IsPathWithinDirectory(string path, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        // Compare resolved existing paths when possible. A purely lexical comparison can
        // be bypassed when an outside junction or symbolic link aliases a file inside the
        // checkpoint cache. Missing paths fall back to normalized lexical comparison.
        var candidate = ResolveExistingPathOrNormalized(path);
        var root = Path.TrimEndingDirectorySeparator(ResolveExistingPathOrNormalized(directory));
        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar
                && candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    public static void ValidateDirectoryTreeWithoutReparsePoints(
        string rootPath,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(rootPath));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            EnsureDirectoryIsNotReparsePoint(directory, "The application-owned directory");

            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        "Cosmic Transcriber refused to traverse a reparse point inside its application-owned storage: " + entry);
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
            }
        }
    }

    public static void WriteAllBytesDurableAtomic(
        string targetPath,
        ReadOnlySpan<byte> payload,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Cannot resolve the parent directory for " + description + ".");
        CreateDirectoryWithoutReparsePoint(directory, description + " directory");
        var temporaryPath = targetPath + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";

        try
        {
            EnsureFileIsNotReparsePointIfPresent(targetPath, description);
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

            EnsureFileIsNotReparsePointIfPresent(temporaryPath, "The temporary " + description);
            EnsureFileIsNotReparsePointIfPresent(targetPath, description);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // Best-effort cleanup only; the primary write result remains authoritative.
            }
        }
    }

    public static int DeleteDirectoryTreeWithoutFollowingReparsePoints(
        string rootPath,
        CancellationToken cancellationToken)
    {
        ValidateDirectoryTreeWithoutReparsePoints(rootPath, cancellationToken);
        return DeleteValidatedTree(rootPath, deleteRoot: true, preservedFilePath: null, cancellationToken);
    }

    public static int DeleteDirectoryContentsWithoutFollowingReparsePoints(
        string rootPath,
        string? preservedFilePath,
        CancellationToken cancellationToken)
    {
        ValidateDirectoryTreeWithoutReparsePoints(rootPath, cancellationToken);
        var normalizedPreservedPath = string.IsNullOrWhiteSpace(preservedFilePath)
            ? null
            : Path.GetFullPath(preservedFilePath);
        return DeleteValidatedTree(rootPath, deleteRoot: false, normalizedPreservedPath, cancellationToken);
    }

    private static string ResolveExistingPathOrNormalized(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return fullPath;
        }

        var current = root;
        var relative = fullPath[root.Length..];
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < segments.Length; index++)
        {
            var candidate = Path.Combine(current, segments[index]);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(candidate);
            }
            catch (FileNotFoundException)
            {
                return AppendUnresolvedSuffix(current, segments, index);
            }
            catch (DirectoryNotFoundException)
            {
                return AppendUnresolvedSuffix(current, segments, index);
            }

            current = candidate;
            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                continue;
            }

            FileSystemInfo info = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            var target = info.ResolveLinkTarget(returnFinalTarget: true)
                ?? throw new IOException("Could not resolve a file-system link: " + current);
            current = Path.GetFullPath(target.FullName);
        }

        return Path.GetFullPath(current);
    }


    private static string AppendUnresolvedSuffix(
        string resolvedPrefix,
        IReadOnlyList<string> segments,
        int firstMissingIndex)
    {
        var current = resolvedPrefix;
        for (var index = firstMissingIndex; index < segments.Count; index++)
        {
            current = Path.Combine(current, segments[index]);
        }

        return Path.GetFullPath(current);
    }

    private static int DeleteValidatedTree(
        string rootPath,
        bool deleteRoot,
        string? preservedFilePath,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(rootPath);
        var stack = new Stack<(string Directory, bool ChildrenVisited)>();
        stack.Push((root, false));
        var deleted = 0;

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, childrenVisited) = stack.Pop();
            EnsureDirectoryIsNotReparsePoint(directory, "The application-owned directory");

            if (!childrenVisited)
            {
                stack.Push((directory, true));
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fullEntry = Path.GetFullPath(entry);
                    if (preservedFilePath is not null
                        && string.Equals(fullEntry, preservedFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Re-check each entry immediately before scheduling or deleting it.
                    // This narrows the race after the validation pass and fails closed if
                    // an attacker or another process swaps a normal entry for a link.
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new IOException(
                            "Cosmic Transcriber refused to delete through a reparse point inside its application-owned storage: " + entry);
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        stack.Push((entry, false));
                    }
                    else
                    {
                        File.Delete(entry);
                        deleted++;
                    }
                }

                continue;
            }

            if (deleteRoot || !string.Equals(directory, root, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(directory, recursive: false);
                deleted++;
            }
        }

        return deleted;
    }
}
