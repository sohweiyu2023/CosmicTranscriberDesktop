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

    // Legacy callers without typed outcome evidence must remain conservative.
    public static string CompletionSubtitle(int outputFileCount, bool completedWithErrors)
        => CompletionGuidance(outputFileCount, completedWithErrors,
            completedWithErrors ? CompletionEvidence.ReviewRequired : CompletionEvidence.None).Message;

    public static CompletionAdvice CompletionGuidance(int outputFileCount, bool completedWithErrors, CompletionEvidence evidence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outputFileCount);
        if ((evidence & CompletionEvidence.UnresolvedPaidAttempt) != 0)
            return new("The paid outcome or checkpoint state could not be established safely. " +
                "Preserve checkpoints and review the existing attempt and possible charges. Do not clear the cache or retry unchanged. " +
                (outputFileCount > 0 ? "Previously exported transcripts remain available." : "No final transcript files were exported."),
                CompletionNextAction.PreserveAndReconcile);
        if ((evidence & CompletionEvidence.ReviewRequired) != 0 || (completedWithErrors && evidence == CompletionEvidence.None))
            return new("Review the failed or skipped queue items before another request. The outcome has not been established as safe to replay. " +
                "Preserve checkpoints and review any existing attempts and possible charges. " +
                (outputFileCount > 0 ? "Completed transcripts were saved." : "No final transcript files were exported."),
                CompletionNextAction.PreserveAndReview);
        if ((evidence & CompletionEvidence.AcceptedCheckpoints) != 0 && (outputFileCount == 0 || completedWithErrors))
            return new("Accepted transcript checkpoints are available for reuse with the same files, settings and output folder. " +
                "Review the queue and correct any input or export problem first; unresolved attempts remain blocked. " +
                (outputFileCount > 0 ? "Existing exports remain available." : "No final transcript files were exported."),
                CompletionNextAction.ReviewAndReuseAccepted);
        if ((evidence & CompletionEvidence.FailedBeforeDispatch) != 0)
            return new("The failed input was rejected before its transcription request was dispatched. Correct that input problem before starting again. " +
                "This does not establish the billing status of other or earlier runs.", CompletionNextAction.CorrectInput);
        return outputFileCount > 0 && !completedWithErrors
            ? new("Your transcript files are ready.", CompletionNextAction.OpenOutputs)
            : new("No final transcript files were exported. Review the queue and preserve checkpoints before another request.", CompletionNextAction.PreserveAndReview);
    }
}

[Flags]
public enum CompletionEvidence { None = 0, AcceptedCheckpoints = 1, FailedBeforeDispatch = 2, ReviewRequired = 4, UnresolvedPaidAttempt = 8 }
public enum CompletionNextAction { OpenOutputs, CorrectInput, ReviewAndReuseAccepted, PreserveAndReview, PreserveAndReconcile }
public sealed record CompletionAdvice(string Message, CompletionNextAction NextAction);

public sealed class RunCompletionEvidence
{
    public CompletionEvidence Value { get; private set; }
    public void Accepted() => Value |= CompletionEvidence.AcceptedCheckpoints;
    public void Failed(Exception error, bool coordinatorStarted, bool coordinatorCompleted)
    {
        ArgumentNullException.ThrowIfNull(error);
        Value |= error is CosmicTranscriber.Services.PendingTranscriptionException or CosmicTranscriber.Services.CheckpointStorageException
            ? CompletionEvidence.UnresolvedPaidAttempt
            : !coordinatorStarted ? CompletionEvidence.FailedBeforeDispatch
            : coordinatorCompleted ? CompletionEvidence.AcceptedCheckpoints : CompletionEvidence.ReviewRequired;
    }
}
