using System.Globalization;

namespace CosmicTranscriber.Utilities;

public static class RunPresentation
{
    public static string EstimatedCost(string? priceText, TimeSpan duration)
    {
        if (!double.TryParse(priceText, NumberStyles.Float, CultureInfo.InvariantCulture, out var price)
            || !double.IsFinite(price)
            || price < 0)
        {
            return "Enter a valid price";
        }

        var estimate = Math.Max(0, duration.TotalMinutes) * price;
        return double.IsFinite(estimate)
            ? string.Create(CultureInfo.InvariantCulture, $"US${estimate:0.000} est.")
            : "Invalid estimate";
    }

    public static string CompletionSubtitle(int outputFileCount, bool completedWithErrors)
    {
        if (outputFileCount == 0)
        {
            return "No final transcript files were exported. Completed chunks remain in checkpoints. " +
                "Review the queue, then retry with the same files, settings and output folder to resume.";
        }

        return completedWithErrors
            ? "Completed transcripts were saved; review the failed or skipped queue items."
            : "Your transcript files are ready.";
    }
}
