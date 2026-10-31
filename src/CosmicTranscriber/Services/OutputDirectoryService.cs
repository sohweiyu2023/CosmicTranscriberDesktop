using CosmicTranscriber.Utilities;
namespace CosmicTranscriber.Services;

public static class OutputDirectoryService
{
    public static string ResolveDefault()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "output"),
            BuildUserFallback(Environment.SpecialFolder.MyDocuments),
            BuildUserFallback(Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (CanWrite(candidate))
            {
                return candidate;
            }
        }

        throw new IOException(
            "Cosmic Transcriber could not create a writable default output folder beside the executable, in Documents, or in Local AppData. Choose a writable folder manually.");
    }

    public static bool CanWrite(string path)
    {
        using var diagnostic = StartupDiagnostics.Begin(DiagnosticArea.OutputDirectory);
        try
        {
            string? probe = null;
            try
            {
                Directory.CreateDirectory(path);
                probe = Path.Combine(path, $".write-test-{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                probe = null;
                return diagnostic.Complete(true);
            }
            catch (Exception probeError)
            {
                diagnostic.Fail(probeError, DiagnosticReason.StorageFailure);
                return false;
            }
            finally
            {
                if (probe is not null && File.Exists(probe))
                {
                    try { File.Delete(probe); } catch { StartupDiagnostics.CleanupFailure(); /* Primary result preserved. */ }
                }
            }
    
        }
        catch (OperationCanceledException) { diagnostic.Stage(DiagnosticStage.Cancelled, DiagnosticReason.UserCancelled); throw; }
        catch (Exception error) { diagnostic.Fail(error, DiagnosticReason.StorageFailure); throw; }
    }

    private static string BuildUserFallback(Environment.SpecialFolder folder)
    {
        var root = Environment.GetFolderPath(folder);
        return string.IsNullOrWhiteSpace(root)
            ? string.Empty
            : Path.Combine(root, "Cosmic Transcriber", "output");
    }
}
