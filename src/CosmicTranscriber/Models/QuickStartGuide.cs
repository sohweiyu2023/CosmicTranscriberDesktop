namespace CosmicTranscriber.Models;

/// <summary>Read-only tutorial content. Navigation never executes an application action.</summary>
public sealed record QuickStartStep(string Title, string Body, string Tip, string Area);

public sealed class QuickStartGuide
{
    public static IReadOnlyList<QuickStartStep> Steps { get; } = Array.AsReadOnly(new[]
    {
        new QuickStartStep("Welcome: your first transcript",
            "Turn recordings into editable text in six steps. You can keep this guide open while using the app, go back, or close it whenever you like. Reopen it with Quick start or F1.",
            "This tutorial sends no audio, makes no API requests, and does not authorize spending. Nothing starts until you choose Start transcription in the main window.", "queue"),
        new QuickStartStep("1. Connect your OpenAI account",
            "Choose API key near the top of the main window. Enter your own OpenAI API key, then choose Test connection when you are ready. ChatGPT subscriptions do not include API usage; transcription is billed to the account behind that key.",
            "The guide does not read or test your key. Secure remembering is optional and uses Windows protection for your current user. Never paste a key into a transcript, screenshot or support message.", "key"),
        new QuickStartStep("2. Add your recordings",
            "Drag audio/video files or folders onto the queue or anywhere in the main window. You can also use + Files, Folder, or copy files in File Explorer and choose Paste. Check the queue order before merging recordings.",
            "MP3 works directly. Other formats, including M4A, need separately installed FFmpeg and ffprobe. A file extension alone does not guarantee its codec is supported. If dragging fails, launch normally, not as administrator, or use Paste.", "queue"),
        new QuickStartStep("3. Choose what you need",
            "Use gpt-transcribe for general transcription. For multiple speakers, choose the speaker-labelled model. Language hints, names and context help supported models; speaker-labelled mode does not send prompts or structured keywords.",
            "Speaker labels can change between chunks and are not verified identities. API availability and prices can change. Leave advanced chunk settings at their defaults for your first short recording.", "model"),
        new QuickStartStep("4. Choose where text will go",
            "Choose an output folder you can write to, then TXT, Markdown or both. Enable Merge files only if you want one combined transcript, and check the queue order. Settings autosave, but the current queue is not saved as ordinary settings.",
            "Review the displayed cost estimate and its editable price per minute. It is an estimate, not a spending cap or the provider's final bill. Try a short recording first.", "output"),
        new QuickStartStep("5. Start, watch, and stop safely",
            "When your files and settings are ready, choose Start transcription in the main window. This uploads audio to OpenAI and may incur charges. Follow per-file progress and the activity log. Cancel stops further work but cannot undo a request already sent.",
            "Successful chunks are checkpointed. After a network failure, review any uncertain attempt before retrying: it may already have been charged. Do not delete the cache or repeatedly start the same job to force it through.", "progress"),
        new QuickStartStep("6. Find and check your transcript",
            "Read the completion summary, then choose Open output folder. Review names, terminology, speaker labels and missing or uncertain passages against the recording. A successful run does not guarantee a perfect transcript.",
            "For problems, use the diagnostic/recovery controls in the app. Review diagnostics before sharing them. You can reopen this tutorial at any time with Quick start or F1. Finish closes only this guide.", "progress")
    });

    public int Index { get; private set; }
    public QuickStartStep Current => Steps[Index];
    public bool CanGoBack => Index > 0;
    public bool IsLast => Index == Steps.Count - 1;
    public void Back() { if (CanGoBack) Index--; }
    public void Next() { if (!IsLast) Index++; }
}
