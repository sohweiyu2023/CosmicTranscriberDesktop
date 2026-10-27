using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CosmicTranscriber.Utilities;

/// <summary>Local metadata only; arbitrary messages and control/user content are never persisted.</summary>
public static class StartupDiagnostics
{
    private static readonly object Sync = new();
    private static readonly Guid Session = Guid.TryParse(Environment.GetEnvironmentVariable("COSMIC_DIAGNOSTIC_SESSION"), out var id) ? id : Guid.NewGuid();
    private static DiagnosticLogWriter? _writer;
    private static bool _initialized, _closed;
    public static string SessionId => Session.ToString("N");
    public static string LogPath { get { Initialize(); lock (Sync) return _writer?.Path ?? string.Empty; } }
    public static string LogDirectory => System.IO.Path.GetDirectoryName(LogPath) ?? string.Empty;

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized || _closed) return;
            _initialized = true;
            try
            {
                var directories = new[] { System.IO.Path.Combine(AppContext.BaseDirectory, "logs"),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CosmicTranscriber", "logs"),
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CosmicTranscriber", "logs") };
                foreach (var directory in directories) { try { _writer = new DiagnosticLogWriter(directory, Session); break; } catch { } }
                var assembly = Assembly.GetEntryAssembly() ?? typeof(StartupDiagnostics).Assembly;
                string hash = "unavailable";
                try { using var file = File.OpenRead(assembly.Location); hash = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); } catch { }
                WriteCore("STARTUP", "session_started", new { version = AppVersion.DisplayVersion, mvid = assembly.ManifestModule.ModuleVersionId,
                    assemblySha256 = hash, runtime = RuntimeInformation.FrameworkDescription, osVersion = Environment.OSVersion.Version.ToString(),
                    architecture = RuntimeInformation.ProcessArchitecture.ToString(), userInteractive = Environment.UserInteractive });
            }
            catch { /* Logging must never prevent startup. */ }
        }
    }

    // Existing callers may pass filenames, provider text or user content. Keep it in the UI only.
    public static void Write(string category, string message)
    { Initialize(); lock (Sync) WriteCore(category, "event", new { messageOmitted = true }); }

    public static void WriteLifecycle(string stage)
    { Initialize(); lock (Sync) WriteCore("LIFECYCLE", Identifier(stage), null); }

    public static void WriteWindow(string stage, bool visible, bool active, string state, double width, double height, double dpiX, double dpiY)
    { Initialize(); lock (Sync) WriteCore("WINDOW", Identifier(stage), new { visible, active, state = Identifier(state), width = Finite(width), height = Finite(height), dpiX = Finite(dpiX), dpiY = Finite(dpiY) }); }

    public static void WriteFocus(string controlId, string controlType)
    { Initialize(); lock (Sync) WriteCore("UI_FOCUS", "keyboard_focus", new { controlId = Identifier(controlId), controlType = Identifier(controlType) }); }

    public static void WriteThemeTransition(Guid operationId, string stage, string? requestedTheme, string? previousTheme,
        int dictionaryCount, int paletteIndex, double elapsedMs, Exception? failure = null)
    {
        try
        {
            static string Theme(string? value) => value is "Cosmic Night" ? "night" : value is "Cosmic Paper" ? "paper" : "other";
            Initialize(); lock (Sync) WriteCore("THEME", stage is "requested" or "applied" or "failed" ? stage : "other", new {
                operationId = operationId.ToString("N"), requestedTheme = Theme(requestedTheme), previousTheme = Theme(previousTheme),
                dictionaryCount = dictionaryCount is >= 0 and <= 64 ? (int?)dictionaryCount : null,
                paletteIndex = paletteIndex is >= -1 and < 64 ? (int?)paletteIndex : null,
                elapsedMs = elapsedMs >= 0 ? Finite(elapsedMs) : null,
                failureType = failure is null ? null : Identifier(failure.GetType().FullName ?? "unknown", 200),
                hresult = failure is null ? null : $"0x{failure.HResult:X8}", messagesOmitted = true });
        }
        catch { /* Diagnostics cannot change the result of a theme operation. */ }
    }

    internal static void WriteViewportFocus(FocusViewportTrace sample)
    {
        double? Number(double? value) => value.HasValue ? Finite(value.Value) : null;
        Initialize(); lock (Sync) WriteCore("UI_VISIBILITY", Identifier(sample.Stage), new {
            controlId = Identifier(sample.ControlId), viewportId = Identifier(sample.ViewportId),
            ticket = sample.Ticket, eventHandled = sample.EventHandled,
            offsetX = Number(sample.OffsetX), offsetY = Number(sample.OffsetY),
            desiredX = Number(sample.DesiredX), desiredY = Number(sample.DesiredY),
            targetLeft = Number(sample.TargetLeft), targetTop = Number(sample.TargetTop),
            targetWidth = Number(sample.TargetWidth), targetHeight = Number(sample.TargetHeight),
            viewportWidth = Number(sample.ViewportWidth), viewportHeight = Number(sample.ViewportHeight),
            expansionFactorResolved = sample.ExpansionFactorResolved, expansionFactor = Number(sample.ExpansionFactor),
            observationElapsedMs = Number(sample.ObservationElapsedMs), stabilityAgeMs = Number(sample.StabilityAgeMs),
            combinedVisibleFraction = Number(sample.CombinedVisibleFraction), visibleFraction = Number(sample.VisibleFraction) });
    }
    public static void WriteHttpResponse(string operation, string clientRequestId, int status, string? requestId, double elapsedMs, string? processingMs)
    {
        var client = clientRequestId.StartsWith("ct-", StringComparison.Ordinal) && Guid.TryParseExact(clientRequestId[3..], "N", out var id) ? "ct-" + id.ToString("N") : "invalid";
        string? hash = requestId is { Length: > 0 and <= 256 } ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestId))).ToLowerInvariant() : null;
        long? processing = long.TryParse(processingMs, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) && ms is >= 0 and <= 1_000_000_000 ? ms : null;
        Initialize(); lock (Sync) WriteCore("OPENAI_HTTP", "response", new { operation = operation is "model-access-test" or "audio-transcription" ? operation : "other",
            clientRequestId = client, status = status is >= 100 and <= 599 ? status : 0, serverRequestIdSha256 = hash, elapsedMs = Finite(elapsedMs), processingMs = processing });
    }

    public static void WriteException(string category, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            var errors = new List<object>();
            var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
            for (Exception? current = exception; current is not null && errors.Count < 4 && seen.Add(current); current = current.InnerException)
            {
                var frames = new StackTrace(current, false).GetFrames()?.Take(12).Select(f => {
                    var method = f.GetMethod(); return Identifier(method?.DeclaringType?.FullName ?? "unknown", 180) + "." + Identifier(method?.Name ?? "unknown", 100); }).ToArray() ?? [];
                errors.Add(new { type = Identifier(current.GetType().FullName ?? "unknown", 200), hresult = $"0x{current.HResult:X8}", methods = frames,
                    httpStatus = current is HttpRequestException http ? (int?)http.StatusCode : null,
                    nativeError = current is System.ComponentModel.Win32Exception win32 ? (int?)win32.NativeErrorCode : null,
                    socketError = current is System.Net.Sockets.SocketException socket ? (int?)socket.SocketErrorCode : null });
            }
            Initialize(); lock (Sync) WriteCore(category, "exception", new { errors, messagesOmitted = true });
        }
        catch { }
    }

    public static void Shutdown(int exitCode)
    {
        Initialize(); lock (Sync)
        {
            if (_closed) return;
            WriteCore("LIFECYCLE", "normal_exit", new { exitCode }); _closed = true;
            try { _writer?.Dispose(); } catch { } _writer = null;
        }
    }
    private static void WriteCore(string category, string name, object? data)
    { if (_closed) return; try { _writer?.Write(Identifier(category), Identifier(name), data); } catch { } }
    private static double? Finite(double value) => double.IsFinite(value) ? Math.Round(value, 3) : null;
    private static string Identifier(string value, int limit = 80)
        => new(value.Take(limit).Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' ? c : '_').ToArray());
}

