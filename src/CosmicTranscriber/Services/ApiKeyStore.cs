using System.Security;
using System.Security.Cryptography;
using System.Text;
using CosmicTranscriber.Utilities;

namespace CosmicTranscriber.Services;

public sealed record ApiKeyLoadResult(string Key, string Source);

public sealed class ApiKeyStore
{
    private const int MaximumProtectedFileBytes = 32 * 1024;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CosmicTranscriber.OpenAI.ApiKey.v1");
    private readonly string _secretPath;
    private readonly Func<string?> _environmentKey = () => Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    public ApiKeyStore(string settingsDirectory)
    {
        _secretPath = Path.Combine(settingsDirectory, "openai-key.protected");
    }

    internal ApiKeyStore(string settingsDirectory, Func<string?> environmentKey) : this(settingsDirectory) { _environmentKey = environmentKey; }

    public ApiKeyLoadResult LoadPreferred()
    {
        using var diagnostic = StartupDiagnostics.Begin(DiagnosticArea.Authentication);
        try
        {
            try
            {
                var protectedKey = LoadProtected();
                if (!string.IsNullOrWhiteSpace(protectedKey))
                {
                    StartupDiagnostics.Write("API KEY", "Loaded a key from the secure local vault.");
                    return diagnostic.Complete(new ApiKeyLoadResult(protectedKey, "Secure local vault"));
                }
            }
            catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
            {
                // Do not describe access denial as absence. Continue with the environment
                // fallback, but retain an explicit diagnostic/source warning for the user.
                StartupDiagnostics.WriteException("API KEY VAULT UNREADABLE", ex);
                var environmentFallback = LoadEnvironment();
                return diagnostic.Complete(string.IsNullOrWhiteSpace(environmentFallback.Key)
                    ? new ApiKeyLoadResult(string.Empty, "Secure local vault could not be read")
                    : new ApiKeyLoadResult(environmentFallback.Key, "OPENAI_API_KEY (saved vault unreadable)"));
            }

            var environment = LoadEnvironment();
            StartupDiagnostics.Write("API KEY", string.IsNullOrWhiteSpace(environment.Key)
                ? "No usable saved or environment API key was found."
                : "Loaded a key from OPENAI_API_KEY.");
            return diagnostic.Complete(environment);
    
        }
        catch (OperationCanceledException) { diagnostic.Stage(DiagnosticStage.Cancelled, DiagnosticReason.UserCancelled); throw; }
        catch (Exception error) { diagnostic.Fail(error, DiagnosticReason.StorageFailure); throw; }
    }

    public ApiKeyLoadResult LoadEnvironment()
    {
        using var diagnostic = StartupDiagnostics.Begin(DiagnosticArea.Authentication);
        try
        {
            var raw = _environmentKey();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return diagnostic.Complete(new ApiKeyLoadResult(string.Empty, "OPENAI_API_KEY not found"), DiagnosticReason.Missing);
            }

            try
            {
                return diagnostic.Complete(new ApiKeyLoadResult(
                    InputValidation.NormalizeApiKey(raw),
                    "OPENAI_API_KEY environment variable"));
            }
            catch (ArgumentException)
            {
                return diagnostic.Complete(new ApiKeyLoadResult(string.Empty, "OPENAI_API_KEY is malformed"), DiagnosticReason.Validation);
            }
    
        }
        catch (OperationCanceledException) { diagnostic.Stage(DiagnosticStage.Cancelled, DiagnosticReason.UserCancelled); throw; }
        catch (Exception error) { diagnostic.Fail(error, DiagnosticReason.StorageFailure); throw; }
    }

    public void SaveProtected(string key)
    {
        using var diagnostic = StartupDiagnostics.Begin(DiagnosticArea.Authentication);
        try
        {
            var normalizedKey = InputValidation.NormalizeApiKey(key);
            var directory = Path.GetDirectoryName(_secretPath)
                ?? throw new InvalidOperationException("Cannot resolve the secure key directory.");
            SafeFileSystem.CreateDirectoryWithoutReparsePoint(directory, "The secure API-key folder");

            var plain = Encoding.UTF8.GetBytes(normalizedKey);
            byte[]? protectedBytes = null;
            try
            {
                protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                SafeFileSystem.WriteAllBytesDurableAtomic(
                    _secretPath,
                    protectedBytes,
                    "The protected API-key file");
                StartupDiagnostics.Write("API KEY", "Secure local API-key storage updated.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
                if (protectedBytes is not null)
                {
                    CryptographicOperations.ZeroMemory(protectedBytes);
                }
            }
    
            diagnostic.Stage(DiagnosticStage.Completed);

        }
        catch (OperationCanceledException) { diagnostic.Stage(DiagnosticStage.Cancelled, DiagnosticReason.UserCancelled); throw; }
        catch (Exception error) { diagnostic.Fail(error, DiagnosticReason.StorageFailure); throw; }
    }

    public string LoadProtected()
    {
        using var diagnostic = StartupDiagnostics.Begin(DiagnosticArea.Authentication);
        try
        {
            try
            {
                // Open directly. File.Exists can return false when access is denied and would
                // incorrectly make an existing saved key look absent. Refuse file-system links
                // so the vault cannot redirect reads outside its application-owned directory.
                SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(_secretPath, "The protected API-key file");
                var protectedBytes = BoundedFileReader.ReadAllBytes(_secretPath, MaximumProtectedFileBytes);
                byte[]? plain = null;
                try
                {
                    plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                    return diagnostic.Complete(InputValidation.NormalizeApiKey(new UTF8Encoding(false, true).GetString(plain)));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(protectedBytes);
                    if (plain is not null)
                    {
                        CryptographicOperations.ZeroMemory(plain);
                    }
                }
            }
            catch (FileNotFoundException)
            {
                return diagnostic.Complete(string.Empty, DiagnosticReason.Missing);
            }
            catch (DirectoryNotFoundException)
            {
                return diagnostic.Complete(string.Empty, DiagnosticReason.Missing);
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException or InvalidDataException or DecoderFallbackException)
            {
                StartupDiagnostics.WriteException("API KEY LOAD FALLBACK", ex);
                return diagnostic.Complete(string.Empty, DiagnosticReason.Corrupt);
            }
    
        }
        catch (OperationCanceledException) { diagnostic.Stage(DiagnosticStage.Cancelled, DiagnosticReason.UserCancelled); throw; }
        catch (Exception error) { diagnostic.Fail(error, DiagnosticReason.StorageFailure); throw; }
    }

    public bool HasProtectedKey
    {
        get
        {
            try
            {
                var attributes = File.GetAttributes(_secretPath);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                {
                    throw new IOException("The protected API-key path is not a regular file.");
                }
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
            {
                StartupDiagnostics.WriteException("API KEY VAULT PRESENCE UNKNOWN", ex);
                return true;
            }
        }
    }

    public void ForgetProtected()
    {
        using var diagnostic = StartupDiagnostics.Begin(DiagnosticArea.Authentication);
        try
        {
            // Deleting the primary protected secret is not best-effort: the UI must not
            // report success while the saved key still exists. Temporary crash remnants
            // are cosmetic cleanup and therefore remain best-effort.
            try
            {
                // Direct deletion avoids File.Exists converting access denial to apparent
                // absence. File.Delete is already a no-op when the file truly does not exist.
                SafeFileSystem.EnsureFileIsNotReparsePointIfPresent(_secretPath, "The protected API-key file");
                File.Delete(_secretPath);
            }
            catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
            {
                throw new IOException("The securely stored API key could not be removed.", ex);
            }

            TryDeleteTemporaryFiles();
            StartupDiagnostics.Write("API KEY", "Secure local API-key storage removed.");
    
            diagnostic.Stage(DiagnosticStage.Completed);

        }
        catch (OperationCanceledException) { diagnostic.Stage(DiagnosticStage.Cancelled, DiagnosticReason.UserCancelled); throw; }
        catch (Exception error) { diagnostic.Fail(error, DiagnosticReason.StorageFailure); throw; }
    }

    private void TryDeleteTemporaryFiles()
    {
        try
        {
            var directory = Path.GetDirectoryName(_secretPath);
            if (directory is null || !Directory.Exists(directory))
            {
                return;
            }

            foreach (var temporaryPath in Directory.EnumerateFiles(directory, "openai-key.protected.*.tmp"))
            {
                TryDelete(temporaryPath);
            }
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            StartupDiagnostics.CleanupFailure();
            // A deleted primary secret is authoritative. Stale temporary files contain
            // DPAPI-protected bytes only and can be retried on the next cleanup.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            StartupDiagnostics.CleanupFailure();
            // Best-effort cleanup; the caller's primary result remains authoritative.
        }
        catch (UnauthorizedAccessException)
        {
            StartupDiagnostics.CleanupFailure();
            // Best-effort cleanup; the caller's primary result remains authoritative.
        }
        catch (SecurityException)
        {
            StartupDiagnostics.CleanupFailure();
            // Best-effort cleanup; the caller's primary result remains authoritative.
        }
    }
}
