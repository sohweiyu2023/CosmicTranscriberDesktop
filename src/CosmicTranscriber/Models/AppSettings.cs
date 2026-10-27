namespace CosmicTranscriber.Models;

public sealed class AppSettings
{
    public string OutputDirectory { get; set; } = string.Empty;
    public int SettingsSchemaVersion { get; set; }
    public string Model { get; set; } = TranscriptionModelCatalog.DefaultFileModel;
    public string Language { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public string Prompt { get; set; } = DefaultPrompt;
    public int ChunkMegabytes { get; set; } = 20;
    public int MaximumChunkMinutes { get; set; } = 5;
    public int RetryCount { get; set; } = 0;
    public bool UsePreviousContext { get; set; } = true;
    public bool IncludeTimestamps { get; set; } = true;
    public bool ExportText { get; set; } = true;
    public bool ExportMarkdown { get; set; } = true;
    public bool MergeFiles { get; set; }
    public bool ContinueOnError { get; set; } = true;
    public string CombinedTitle { get; set; } = "Combined Transcript";
    public string Theme { get; set; } = "Cosmic Night";
    public bool RememberKeySecurely { get; set; }
    public double CustomPricePerMinute { get; set; } = 0.0045;

    public const string DefaultPrompt =
        "Transcribe faithfully and completely. Do not summarize, omit, paraphrase, or add commentary. " +
        "Preserve sentence order and meaning. Add sensible punctuation and paragraph breaks. " +
        "Spell names and technical terms carefully.";
}
