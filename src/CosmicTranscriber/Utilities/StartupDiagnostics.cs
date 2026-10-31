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
    private static bool _initialized, _closed, _sinkUnavailable;
    private static long _sinkFailures, _oversized, _fallbackDropped, _cleanupFailures, _pruneFailures;
    private static readonly Queue<string> Fallback = new();
    private static readonly AsyncLocal<Guid?> Active = new();
    internal static Guid? ActiveOperationId { get => Active.Value; set => Active.Value = value; }
    public static DiagnosticOperation Begin(DiagnosticArea area, bool enabled = true) => new(area, enabled);
    public static DiagnosticHealth Health { get { lock (Sync) return new(_sinkUnavailable, _sinkFailures,
        _oversized + (_writer?.OmittedRecords ?? 0), _fallbackDropped, _cleanupFailures, _pruneFailures + (_writer?.PruneFailures ?? 0), Fallback.ToArray()); } }
    public static void WriteOperation(DiagnosticArea area, DiagnosticStage stage, Guid operationId, DiagnosticReason reason = DiagnosticReason.None, Guid? parentOperationId = null)
    {
        try { Initialize(); lock (Sync) WriteCore("OPERATION", "stage", new {
            area = Enum.IsDefined(area) ? area.ToString() : "Other", stage = Enum.IsDefined(stage) ? stage.ToString() : "Ended",
            operationId = operationId.ToString("N"), parentOperationId = parentOperationId?.ToString("N"), reason = Enum.IsDefined(reason) ? reason.ToString() : "Other" }); } catch { }
    }
    private static void SinkFailure()
    {
        _sinkUnavailable = true; _sinkFailures++;
        if (Fallback.Count == 32) { Fallback.Dequeue(); _fallbackDropped++; }
        Fallback.Enqueue("diagnostic_sink_unavailable");
        // No alternate-path write after failure, no exception text, no recursive logger call.
        if (_sinkFailures == 1) { try { Trace.WriteLine("Cosmic diagnostic sink unavailable; local incident evidence is incomplete."); } catch { } }
    }

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
                if (_writer is null) SinkFailure();
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
    {
        // Only exact source-controlled messages get a durable meaning. Unknown activity stays omitted.
        var stage = message switch {
            "Constructor entered." => "constructor_entered", "Constructor completed." => "constructor_completed",
            "Settings loaded successfully." => "settings_loaded", "Settings saved successfully." => "settings_saved",
            "No settings file exists; using defaults." => "defaults_missing_file",
            "No settings directory exists; using defaults." => "defaults_missing_directory",
            "Loaded a key from the secure local vault." => "key_loaded_vault",
            "Loaded a key from OPENAI_API_KEY." => "key_loaded_environment",
            "No usable saved or environment API key was found." => "key_missing",
            "Secure local API-key storage updated." => "key_saved",
            "Secure local API-key storage removed." => "key_removed",
            "Closing a partially constructed window; control-dependent cleanup was skipped." => "partial_window_close",
            "Close deferred until active work finishes unwinding." => "close_deferred",
            "Background work finished unwinding; completing deferred close." => "deferred_close_completed",
            "Constructor entered; creating application services." => "constructor_entered_creating_application_services",
            "Initializing XAML." => "initializing_xaml",
            "InitializeComponent completed." => "initializecomponent_completed",
            "Applying settings to controls." => "applying_settings_to_controls",
            "Loading initial API-key state." => "loading_initial_api_key_state",
            _ => "activity_omitted" };
        Initialize(); lock (Sync) WriteCore(category, stage, new { messageOmitted = true });
    }

    public static void WriteStorageSelection(DiagnosticArea area, DiagnosticStorageKind kind)
    {
        try { Initialize(); lock (Sync) WriteCore("OPERATION", "storage_selected", new {
            operationId = ActiveOperationId?.ToString("N"), area = Enum.IsDefined(area) ? area.ToString() : "Other",
            locationKind = Enum.IsDefined(kind) ? kind.ToString() : "Unavailable" }); } catch { }
    }
    public static void WriteLifecycle(string stage)
    { Initialize(); lock (Sync) WriteCore("LIFECYCLE", DiagnosticVocabulary.Lifecycle(stage), null); }

    public static void WriteWindow(string stage, bool visible, bool active, string state, double width, double height, double dpiX, double dpiY)
    { Initialize(); lock (Sync) WriteCore("WINDOW", DiagnosticVocabulary.WindowStage(stage), new { visible, active, state = state is "Normal" or "Maximized" or "Minimized" ? state : "other", width = Finite(width), height = Finite(height), dpiX = Finite(dpiX), dpiY = Finite(dpiY) }); }

    public static void WriteFocus(string controlId, string controlType)
    { Initialize(); lock (Sync) WriteCore("UI_FOCUS", "keyboard_focus", new { controlId = DiagnosticVocabulary.Control(controlId), controlType = DiagnosticVocabulary.Type(controlType) }); }

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
        Initialize(); lock (Sync) WriteCore("UI_VISIBILITY", DiagnosticVocabulary.ViewportStage(sample.Stage), new {
            controlId = DiagnosticVocabulary.Control(sample.ControlId), viewportId = DiagnosticVocabulary.Control(sample.ViewportId),
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
        var client = clientRequestId.Length == 35 && clientRequestId.StartsWith("ct-", StringComparison.Ordinal) && Guid.TryParseExact(clientRequestId[3..], "N", out var id) ? "ct-" + id.ToString("N") : "invalid";
        string? hash = requestId is { Length: > 0 and <= 256 } ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestId))).ToLowerInvariant() : null;
        long? processing = long.TryParse(processingMs, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) && ms is >= 0 and <= 1_000_000_000 ? ms : null;
        Initialize(); lock (Sync) WriteCore("OPENAI_HTTP", "response", new { operation = operation is "model-access-test" or "audio-transcription" ? operation : "other",
            operationId = ActiveOperationId?.ToString("N"), clientRequestId = client, status = status is >= 100 and <= 599 ? status : 0, serverRequestIdSha256 = hash, elapsedMs = Finite(elapsedMs), processingMs = processing });
    }

    public static void WriteException(string category, Exception exception, Guid? operationId = null)
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
                    socketError = current is System.Net.Sockets.SocketException socket ? (int?)socket.SocketErrorCode : null,
                    requestError = current is HttpRequestException transport ? (int?)transport.HttpRequestError : null,
                    provider = current is IProviderDiagnosticFailure provider ? Provider(provider) : null });
            }
            Initialize(); lock (Sync) WriteCore(category, "exception", new { operationId = (operationId ?? ActiveOperationId)?.ToString("N"), errors, messagesOmitted = true });
        }
        catch { }
    }

    public static void CleanupFailure()
    {
        try { Initialize(); lock (Sync) {
            if (_cleanupFailures < long.MaxValue) _cleanupFailures++;
            // Sparse samples plus exact count: cleanup cannot flood logs or mask its primary result.
            if ((_cleanupFailures & (_cleanupFailures - 1)) == 0)
                WriteCore("OPERATION", "stage", new { area = "Cleanup", stage = "CleanupFailed",
                    reason = "StorageFailure", operationId = ActiveOperationId?.ToString("N"), count = _cleanupFailures });
        } } catch { }
    }
    public static string HealthSummary {
        get { var h = Health; return $"Diagnostic coverage: sinkUnavailable={h.SinkUnavailable}; sinkFailures={h.SinkFailures}; oversizedRecords={h.OversizedRecords}; fallbackEventsDropped={h.FallbackEventsDropped}; cleanupFailures={h.CleanupFailures}; pruneFailures={h.PruneFailures}. Coverage is incomplete if any loss is reported."; }
    }
    public static void Shutdown(int exitCode)
    {
        Initialize(); lock (Sync)
        {
            if (_closed) return;
            WriteCore("LIFECYCLE", "normal_exit", new { exitCode, coverage = Health }); _closed = true;
            _oversized += _writer?.OmittedRecords ?? 0;
            _pruneFailures += _writer?.PruneFailures ?? 0;
            try { _writer?.Dispose(); } catch { } _writer = null;
        }
    }
    private static void WriteCore(string category, string name, object? data)
    {
        if (_closed) return;
        if (_sinkUnavailable || _writer is null) {
            if (Fallback.Count == 32) { Fallback.Dequeue(); _fallbackDropped++; }
            Fallback.Enqueue("event_not_persisted"); return;
        }
        try { _writer.Write(Category(category), Identifier(name), data); }
        catch {
            _oversized += _writer.OmittedRecords; _pruneFailures += _writer.PruneFailures; SinkFailure();
            try { _writer.Dispose(); } catch { } _writer = null;
        }
    }
    private static string Category(string value) => value is
        "ABOUT" or "CLIPBOARD" or "ACTIVITY" or "API KEY" or "API KEY LOAD FALLBACK" or "API KEY VAULT PRESENCE UNKNOWN" or "API KEY VAULT UNREADABLE" or "Authentication" or "CLEAR ELEVATION COMPATIBILITY" or "COMPLETION" or "COMPLETION OPEN OUTPUT" or "Checkpoint" or "Cleanup" or "ConnectionTest" or "ELEVATION" or "ELEVATION COMPATIBILITY CHECK" or "Export" or "FATAL STARTUP" or "LIFECYCLE" or "MAINWINDOW" or "NATIVE_TEST" or "OPENAI API" or "OPENAI CONNECTION TEST" or "OPENAI_HTTP" or "OPERATION" or "OUTPUT PATH FALLBACK" or "OutputDirectory" or "SETTINGS" or "SETTINGS DIRECTORY" or "SETTINGS DIRECTORY REJECTED" or "SETTINGS FALLBACK" or "SETTINGS SAVE" or "SETTINGS TEMP FALLBACK" or "SOCKET_TEST" or "STARTUP" or "Settings" or "Startup" or "TEST" or "THEME" or "TRANSCRIPTION FILE FAILURE" or "TRANSCRIPTION RUN FAILURE" or "Transcription" or "UI_FOCUS" or "UI_VISIBILITY" or "UNELEVATED RELAUNCH" or "UNHANDLED APPDOMAIN" or "UNHANDLED DISPATCHER" or "UNOBSERVED TASK" or "WINDOW"
        ? Identifier(value) : "OTHER";
    private static object Provider(IProviderDiagnosticFailure value)
    {
        static string? Known(string? s, params string[] allowed) => s is null ? null : allowed.Contains(s, StringComparer.Ordinal) ? s : "other";
        var c = value.DiagnosticClientId;
        var valid = c is { Length: 35 } && c.StartsWith("ct-", StringComparison.Ordinal) && Guid.TryParseExact(c[3..], "N", out _);
        var server = value.DiagnosticServerId;
        var elapsed = value.DiagnosticElapsedMs;
        return new {
            status = value.DiagnosticStatus is >= 100 and <= 599 ? value.DiagnosticStatus : null,
            code = Known(value.DiagnosticCode, "invalid_api_key", "insufficient_quota", "rate_limit_exceeded", "model_not_found", "context_length_exceeded", "invalid_value", "server_error", "unsupported_value"),
            type = Known(value.DiagnosticType, "invalid_request_error", "authentication_error", "permission_error", "rate_limit_error", "server_error", "insufficient_quota"),
            param = Known(value.DiagnosticParam, "model", "file", "language", "languages", "prompt", "keywords", "response_format", "chunking_strategy"),
            clientRequestId = valid ? c : null,
            serverRequestIdSha256 = server is { Length: > 0 and <= 256 } ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server))).ToLowerInvariant() : null,
            failureKind = Known(value.DiagnosticFailureKind, "DNS/name resolution", "connection", "TLS/certificate", "HTTP protocol", "extended CONNECT unsupported", "HTTP version negotiation", "network authentication", "proxy tunnel", "invalid response", "response ended early", "HTTP configuration limit", "unknown network failure", "timeout"),
            elapsedMs = elapsed.HasValue && elapsed >= 0 ? Finite(elapsed.Value) : null };
    }
    private static double? Finite(double value) => double.IsFinite(value) ? Math.Round(value, 3) : null;
    private static string Identifier(string value, int limit = 80)
        => new(value.Take(limit).Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' ? c : '_').ToArray());
}

