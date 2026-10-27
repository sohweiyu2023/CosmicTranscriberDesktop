using System.Buffers.Binary;
using System.Security.Cryptography;
using CosmicTranscriber.Models;
using NAudio.Wave;

namespace CosmicTranscriber.Services;

public static class Mp3AudioUtilities
{
    private const int Id3HeaderLength = 10;
    private const int MaximumId3TagBytes = 256 * 1024 * 1024;

    public static AudioFileMetadata Inspect(string path)
    {
        var fullPath = Path.GetFullPath(path);

        // Open directly. File.Exists can report false for access-denied paths and would
        // misclassify a permissions problem as a missing file.
        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 256,
            options: FileOptions.SequentialScan);

        var initialLength = stream.Length;
        if (initialLength <= 0)
        {
            throw new InvalidDataException("The MP3 file is empty.");
        }

        var initialLastWriteTimeUtc = File.GetLastWriteTimeUtc(fullPath);
        var sha256 = Convert.ToHexString(SHA256.HashData(stream));
        stream.Position = 0;

        var duration = ReadDuration(stream);
        if (duration <= TimeSpan.Zero)
        {
            throw new InvalidDataException("No valid MP3 audio frames were found.");
        }

        var finalLength = stream.Length;
        var finalLastWriteTimeUtc = File.GetLastWriteTimeUtc(fullPath);
        if (initialLength != finalLength || initialLastWriteTimeUtc != finalLastWriteTimeUtc)
        {
            throw new IOException("The MP3 changed while it was being inspected. Wait for the file copy or download to finish, then add it again.");
        }

        return new AudioFileMetadata(duration, finalLength, finalLastWriteTimeUtc, sha256);
    }

    public static TimeSpan ReadDuration(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 256, options: FileOptions.SequentialScan);
        return ReadDuration(stream);
    }

    private static TimeSpan ReadDuration(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("The MP3 stream must be readable and seekable.", nameof(stream));
        }

        stream.Position = 0;
        SkipLeadingId3V2(stream);

        var duration = TimeSpan.Zero;
        var firstFrame = true;
        while (true)
        {
            var frame = Mp3Frame.LoadFromStream(stream);
            if (frame is null)
            {
                break;
            }

            if (frame.SampleRate <= 0 || frame.SampleCount <= 0)
            {
                throw new InvalidDataException("The MP3 contains an invalid audio frame.");
            }

            // Encoders commonly place a Xing/Info or VBRI seek header in the first MPEG Layer III
            // frame. It describes the complete source and contributes no logical speech duration.
            // Mp3Chunker still retains its raw bytes in chunk 1 because a following Layer III frame
            // can depend on reservoir data buffered from this carrier frame.
            if (firstFrame && IsVbrMetadataFrame(frame))
            {
                firstFrame = false;
                continue;
            }

            firstFrame = false;
            duration += TimeSpan.FromSeconds((double)frame.SampleCount / frame.SampleRate);
        }

        return duration;
    }

    public static bool IsVbrMetadataFrame(Mp3Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return TryFindVbrMetadataTag(frame.RawData, out _, out _);
    }

    public static void RewriteVbrMetadataForChunk(
        Span<byte> metadataFrame,
        int frameCount,
        long totalBytes,
        IReadOnlyList<long> frameOffsets)
    {
        if (frameCount <= 0 || frameOffsets.Count != frameCount)
        {
            throw new ArgumentException("Chunk frame offsets do not match the frame count.", nameof(frameOffsets));
        }

        if (totalBytes <= 0 || totalBytes > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytes));
        }

        if (!TryFindVbrMetadataTag(metadataFrame, out var tagOffset, out var isVbri))
        {
            throw new InvalidDataException("The expected Xing/Info/VBRI metadata tag could not be found.");
        }

        if (isVbri)
        {
            // VBRI stores whole-file byte and frame totals immediately after its
            // version, delay, and quality fields. Its seek table can remain only a
            // hint; sequential decoders use these corrected totals for duration.
            EnsureAvailable(metadataFrame, tagOffset + 10, 8);
            BinaryPrimitives.WriteUInt32BigEndian(metadataFrame.Slice(tagOffset + 10, 4), checked((uint)totalBytes));
            BinaryPrimitives.WriteUInt32BigEndian(metadataFrame.Slice(tagOffset + 14, 4), checked((uint)frameCount));
            return;
        }

        EnsureAvailable(metadataFrame, tagOffset + 4, 4);
        var flags = BinaryPrimitives.ReadUInt32BigEndian(metadataFrame.Slice(tagOffset + 4, 4));
        var cursor = tagOffset + 8;

        if ((flags & 0x0001) != 0)
        {
            EnsureAvailable(metadataFrame, cursor, 4);
            BinaryPrimitives.WriteUInt32BigEndian(metadataFrame.Slice(cursor, 4), checked((uint)frameCount));
            cursor += 4;
        }

        if ((flags & 0x0002) != 0)
        {
            EnsureAvailable(metadataFrame, cursor, 4);
            BinaryPrimitives.WriteUInt32BigEndian(metadataFrame.Slice(cursor, 4), checked((uint)totalBytes));
            cursor += 4;
        }

        if ((flags & 0x0004) != 0)
        {
            const int tocEntries = 100;
            EnsureAvailable(metadataFrame, cursor, tocEntries);
            for (var percentile = 0; percentile < tocEntries; percentile++)
            {
                var frameIndex = Math.Min(frameCount - 1, (int)((long)percentile * frameCount / tocEntries));
                var frameOffset = frameOffsets[frameIndex];
                if (frameOffset < 0 || frameOffset >= totalBytes)
                {
                    throw new InvalidDataException("The chunk contains an invalid MPEG frame offset.");
                }

                metadataFrame[cursor + percentile] = (byte)Math.Clamp(frameOffset * 256 / totalBytes, 0, 255);
            }
        }
    }

    public static void SkipLeadingId3V2(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new ArgumentException("The MP3 stream must be seekable.", nameof(stream));
        }

        var originalPosition = stream.Position;
        Span<byte> header = stackalloc byte[Id3HeaderLength];
        var read = ReadAtMost(stream, header);
        if (read < Id3HeaderLength || header[0] != (byte)'I' || header[1] != (byte)'D' || header[2] != (byte)'3')
        {
            stream.Position = originalPosition;
            return;
        }

        var majorVersion = header[3];
        var revision = header[4];
        if (majorVersion is < 2 or > 4 || revision == 0xFF)
        {
            throw new InvalidDataException("The MP3 uses an unsupported or invalid ID3v2 tag version.");
        }

        var flags = header[5];
        var undefinedFlagMask = majorVersion switch
        {
            2 => 0x3F,
            3 => 0x1F,
            _ => 0x0F
        };
        if ((flags & undefinedFlagMask) != 0
            || (header[6] | header[7] | header[8] | header[9]) >= 0x80)
        {
            throw new InvalidDataException("The MP3 has invalid ID3v2 header flags or tag size.");
        }

        var payloadSize = (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];
        var footerLength = majorVersion == 4 && (flags & 0x10) != 0 ? Id3HeaderLength : 0;
        var totalTagLength = checked((long)Id3HeaderLength + payloadSize + footerLength);
        if (totalTagLength > MaximumId3TagBytes || originalPosition + totalTagLength > stream.Length)
        {
            throw new InvalidDataException("The MP3 has a truncated or unreasonably large ID3v2 tag.");
        }

        stream.Position = originalPosition + totalTagLength;
    }

    private static bool TryFindVbrMetadataTag(
        ReadOnlySpan<byte> data,
        out int tagOffset,
        out bool isVbri)
    {
        tagOffset = -1;
        isVbri = false;
        if (data.Length < 4)
        {
            return false;
        }

        // MPEG header: version bits are 20-19, layer bits are 18-17, and channel mode is 7-6
        // of the fourth byte. Layer III is encoded as binary 01. Version binary 01 is reserved.
        var versionBits = (data[1] >> 3) & 0x03;
        var layerBits = (data[1] >> 1) & 0x03;
        if (versionBits == 0x01 || layerBits != 0x01)
        {
            return false;
        }

        var mono = ((data[3] >> 6) & 0x03) == 0x03;
        var sideInfoBytes = versionBits == 0x03
            ? (mono ? 17 : 32)
            : (mono ? 9 : 17);
        var optionalCrcBytes = (data[1] & 0x01) == 0 ? 2 : 0;
        foreach (var offset in new[] { 4 + sideInfoBytes, 4 + sideInfoBytes + optionalCrcBytes })
        {
            if (MatchesAscii(data, offset, "Xing"u8) || MatchesAscii(data, offset, "Info"u8))
            {
                tagOffset = offset;
                return true;
            }
        }

        foreach (var offset in new[] { 4 + 32, 4 + 32 + optionalCrcBytes })
        {
            if (MatchesAscii(data, offset, "VBRI"u8))
            {
                tagOffset = offset;
                isVbri = true;
                return true;
            }
        }

        return false;
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException("The MP3 contains a truncated Xing/Info/VBRI metadata header.");
        }
    }

    private static bool MatchesAscii(ReadOnlySpan<byte> data, int offset, ReadOnlySpan<byte> tag)
    {
        return offset >= 0
            && offset <= data.Length - tag.Length
            && data.Slice(offset, tag.Length).SequenceEqual(tag);
    }

    private static int ReadAtMost(Stream stream, Span<byte> destination)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var read = stream.Read(destination[total..]);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
