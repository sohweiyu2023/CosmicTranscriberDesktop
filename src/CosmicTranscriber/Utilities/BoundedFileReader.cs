namespace CosmicTranscriber.Utilities;

public static class BoundedFileReader
{
    public static byte[] ReadAllBytes(string path, int maximumBytes)
    {
        ValidateMaximum(maximumBytes);
        using var stream = OpenBounded(path, maximumBytes, useAsync: false);
        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
        ReadExactly(stream, bytes);
        return bytes;
    }

    public static async Task<byte[]> ReadAllBytesAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ValidateMaximum(maximumBytes);
        await using var stream = OpenBounded(path, maximumBytes, useAsync: true);
        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }

    private static FileStream OpenBounded(string path, int maximumBytes, bool useAsync)
    {
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: useAsync);
        if (stream.Length is <= 0 || stream.Length > maximumBytes)
        {
            stream.Dispose();
            throw new InvalidDataException(
                $"The local data file must be between 1 and {maximumBytes:N0} bytes.");
        }

        return stream;
    }

    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var read = stream.Read(destination[offset..]);
            if (read == 0)
            {
                throw new EndOfStreamException("The local data file ended unexpectedly.");
            }

            offset += read;
        }
    }

    private static void ValidateMaximum(int maximumBytes)
    {
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }
    }
}
