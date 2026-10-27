using System.Globalization;

namespace CosmicTranscriber.Utilities;

public static class Formatters
{
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string FileSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double size = Math.Max(0, bytes);
        var order = 0;
        while (size >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{size:0.##} {suffixes[order]}");
    }

    public static string Duration(TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return Timestamp(value, padHours: false);
        }

        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    public static string Timestamp(TimeSpan value)
        => Timestamp(value, padHours: true);

    public static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var characters = (value ?? string.Empty)
            .Select(ch => char.IsControl(ch) || invalid.Contains(ch) ? '_' : ch)
            .ToArray();

        var safe = new string(characters).Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "transcript";
        }

        var firstDot = safe.IndexOf('.');
        var deviceStem = (firstDot >= 0 ? safe[..firstDot] : safe).TrimEnd('.', ' ')
            .Replace('¹', '1')
            .Replace('²', '2')
            .Replace('³', '3');
        if (ReservedWindowsNames.Contains(deviceStem))
        {
            safe = "_" + safe;
        }

        safe = InputValidation.TruncateWithoutSplittingSurrogatePair(safe, 100).TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "transcript" : safe;
    }

    private static string Timestamp(TimeSpan value, bool padHours)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        var totalHours = (long)value.TotalHours;
        var hourText = padHours
            ? totalHours.ToString("00", CultureInfo.InvariantCulture)
            : totalHours.ToString(CultureInfo.InvariantCulture);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hourText}:{value.Minutes:00}:{value.Seconds:00}");
    }
}
