using CosmicTranscriber.Models;

namespace CosmicTranscriber.Services;

public static class Mp3MetadataService
{
    public static AudioFileMetadata Inspect(string path) => Mp3AudioUtilities.Inspect(path);
}
