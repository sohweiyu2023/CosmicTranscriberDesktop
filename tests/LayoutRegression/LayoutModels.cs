// Only the enum required by the source XAML's style triggers; no app code is loaded.
namespace CosmicTranscriber.Models;
public enum AudioJobStatus { Ready, Preparing, Transcribing, Completed, Failed, Cancelled, Skipped }
