namespace CosmicTranscriber.Models;

public static class TranscriptionModelCatalog
{
    public const string DefaultFileModel = "gpt-transcribe";
    public const string Gpt4oModel = "gpt-4o-transcribe";
    public const string DiarizeModel = "gpt-4o-transcribe-diarize";
    public const string Gpt4oMiniModel = "gpt-4o-mini-transcribe";

    private static readonly string[] FileModelIds =
    [
        DefaultFileModel,
        DiarizeModel,
        Gpt4oModel,
        Gpt4oMiniModel,
    ];

    public static IReadOnlyList<string> FileModels => FileModelIds;

    public static bool SupportsFileUploads(string model)
        => FileModelIds.Contains(model, StringComparer.OrdinalIgnoreCase);

    public static bool UsesPluralLanguageHints(string model)
        => string.Equals(model, DefaultFileModel, StringComparison.OrdinalIgnoreCase);

    public static bool SupportsKeywordHints(string model)
        => string.Equals(model, DefaultFileModel, StringComparison.OrdinalIgnoreCase);

    public static bool SupportsPrompt(string model)
        => !SupportsSpeakerDiarization(model);

    public static bool SupportsPreviousContext(string model)
        => SupportsPrompt(model);

    public static bool SupportsSpeakerDiarization(string model)
        => string.Equals(model, DiarizeModel, StringComparison.OrdinalIgnoreCase);

    public static double DefaultPricePerMinute(string model)
    {
        if (string.Equals(model, DefaultFileModel, StringComparison.OrdinalIgnoreCase))
        {
            return 0.0045;
        }

        if (string.Equals(model, Gpt4oMiniModel, StringComparison.OrdinalIgnoreCase))
        {
            return 0.003;
        }

        if (string.Equals(model, Gpt4oModel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(model, DiarizeModel, StringComparison.OrdinalIgnoreCase))
        {
            // The diarization model currently publishes the same audio-input and text-output
            // token rates as gpt-4o-transcribe. This remains an editable estimate, not a quote.
            return 0.006;
        }

        return 0.006;
    }
}
