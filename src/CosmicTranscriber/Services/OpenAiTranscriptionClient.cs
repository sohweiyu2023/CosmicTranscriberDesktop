using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using CosmicTranscriber.Models;
using CosmicTranscriber.Utilities;

namespace CosmicTranscriber.Services;

public sealed class OpenAiProtocolException : Exception
{
    public OpenAiProtocolException(string message) : base(message) { }
    public OpenAiProtocolException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class OpenAiApiException : Exception
{
    public OpenAiApiException(
        HttpStatusCode statusCode,
        string message,
        string? errorType = null,
        string? errorParam = null,
        string? errorCode = null,
        string? requestId = null,
        string? clientRequestId = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
        ErrorParam = errorParam;
        ErrorCode = errorCode;
        RequestId = requestId;
        ClientRequestId = clientRequestId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ErrorType { get; }
    public string? ErrorParam { get; }
    public string? ErrorCode { get; }
    public string? RequestId { get; }
    public string? ClientRequestId { get; }
}

public sealed class OpenAiNetworkException : HttpRequestException
{
    public OpenAiNetworkException(
        HttpRequestError requestError,
        string message,
        string failureKind,
        string clientRequestId,
        string? serverRequestId,
        HttpStatusCode? responseStatusCode,
        TimeSpan elapsed,
        Exception innerException)
        : base(requestError, message, innerException, responseStatusCode)
    {
        FailureKind = failureKind;
        ClientRequestId = clientRequestId;
        ServerRequestId = serverRequestId;
        Elapsed = elapsed;
    }

    public string FailureKind { get; }
    public string ClientRequestId { get; }
    public string? ServerRequestId { get; }
    public TimeSpan Elapsed { get; }
}

public sealed class OpenAiTranscriptionClient : IDisposable
{
    public const long MaximumAudioPayloadBytes = 24_000_000;
    public const int MaximumResponseBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(30);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly HttpClient _httpClient;

    public OpenAiTranscriptionClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            MaxResponseHeadersLength = 64,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseCookies = false,
            UseProxy = true
        };

        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("https://api.openai.com/", UriKind.Absolute),
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"CosmicTranscriber/{AppVersion.DisplayVersion}");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task TestConnectionAsync(
        string apiKey,
        string model,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Connection-test timeout must be between zero and five minutes.");
        }

        var normalizedKey = InputValidation.NormalizeApiKey(apiKey);
        var normalizedModel = NormalizeModel(model);
        var clientRequestId = CreateClientRequestId();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"v1/models/{Uri.EscapeDataString(normalizedModel)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", normalizedKey);
        AddClientRequestId(request, clientRequestId);
        using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptCancellation.CancelAfter(timeout);
        var requestTimer = Stopwatch.StartNew();
        string? serverRequestId = null;
        HttpStatusCode? responseStatusCode = null;

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                attemptCancellation.Token);
            serverRequestId = ReadHeader(response, "x-request-id");
            responseStatusCode = response.StatusCode;
            WriteResponseDiagnostics("model-access-test", clientRequestId, response, requestTimer.Elapsed);
            if (!response.IsSuccessStatusCode)
            {
                var body = await ReadResponseBodyAsync(response.Content, attemptCancellation.Token);
                throw CreateException(response, body, clientRequestId);
            }
        }
        catch (HttpRequestException ex)
        {
            var diagnostic = CreateNetworkException(
                "connection test",
                attempt: 1,
                attempts: 1,
                clientRequestId,
                serverRequestId,
                responseStatusCode,
                requestTimer.Elapsed,
                ex);
            StartupDiagnostics.WriteException("OPENAI NETWORK", diagnostic);
            throw diagnostic;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var diagnostic = CreateTimeoutException(
                "connection test",
                attempt: 1,
                attempts: 1,
                clientRequestId,
                serverRequestId,
                responseStatusCode,
                requestTimer.Elapsed,
                ex);
            StartupDiagnostics.WriteException("OPENAI TIMEOUT", diagnostic);
            throw diagnostic;
        }
    }

    public async Task<OpenAiTranscriptionResult> TranscribeAsync(
        string apiKey,
        string model,
        string audioPath,
        string prompt,
        string? language,
        string? keywords,
        int retryCount,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var normalizedKey = InputValidation.NormalizeApiKey(apiKey);
        var normalizedModel = NormalizeModel(model);
        if (!TranscriptionModelCatalog.SupportsFileUploads(normalizedModel))
        {
            throw new ArgumentException(
                "The selected model is not a file-transcription model. Use gpt-transcribe for uploaded audio files.",
                nameof(model));
        }

        var usesPluralLanguageHints = TranscriptionModelCatalog.UsesPluralLanguageHints(normalizedModel);
        var usesSpeakerDiarization = TranscriptionModelCatalog.SupportsSpeakerDiarization(normalizedModel);
        var normalizedLanguages = usesPluralLanguageHints
            ? InputValidation.NormalizeLanguages(language)
            : OptionalSingleLanguageList(InputValidation.NormalizeLanguage(language));
        var normalizedKeywords = InputValidation.NormalizeKeywords(keywords);
        if (normalizedKeywords.Count > 0 && !TranscriptionModelCatalog.SupportsKeywordHints(normalizedModel))
        {
            throw new ArgumentException(
                "Structured keyword hints are supported only by gpt-transcribe.",
                nameof(keywords));
        }
        if (!TranscriptionModelCatalog.SupportsPrompt(normalizedModel) && !string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException(
                "The speaker-diarization model does not support the prompt field.",
                nameof(prompt));
        }
        if (prompt.Length > InputValidation.MaximumFinalPromptCharacters)
        {
            throw new ArgumentException("The final transcription prompt is too long.", nameof(prompt));
        }

        var fileInfo = new FileInfo(audioPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("The audio upload file no longer exists.", audioPath);
        }

        if (fileInfo.Length <= 0)
        {
            throw new InvalidDataException("The audio upload file is empty.");
        }

        if (fileInfo.Length > MaximumAudioPayloadBytes)
        {
            throw new InvalidDataException(
                $"The prepared audio chunk is {fileInfo.Length:N0} bytes, above Cosmic Transcriber's safe " +
                $"{MaximumAudioPayloadBytes:N0}-byte upload ceiling. Lower the chunk-size setting and try again.");
        }

        var attempts = Math.Clamp(retryCount, 0, 5) + 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellation.CancelAfter(RequestTimeout);
            var clientRequestId = CreateClientRequestId();
            var requestTimer = Stopwatch.StartNew();
            string? serverRequestId = null;
            HttpStatusCode? responseStatusCode = null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "v1/audio/transcriptions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", normalizedKey);
                AddClientRequestId(request, clientRequestId);

                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(normalizedModel, Encoding.UTF8), "model");
                form.Add(
                    new StringContent(usesSpeakerDiarization ? "diarized_json" : "json", Encoding.UTF8),
                    "response_format");
                if (usesSpeakerDiarization)
                {
                    // OpenAI requires server-side VAD chunking for diarized inputs longer than
                    // 30 seconds. Sending auto for every diarization request is valid and keeps
                    // the request safe when duration metadata is approximate.
                    form.Add(new StringContent("auto", Encoding.UTF8), "chunking_strategy");
                }

                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    form.Add(new StringContent(prompt, Encoding.UTF8), "prompt");
                }

                foreach (var keyword in normalizedKeywords)
                {
                    form.Add(new StringContent(keyword, Encoding.UTF8), "keywords[]");
                }

                if (usesPluralLanguageHints)
                {
                    foreach (var languageHint in normalizedLanguages)
                    {
                        form.Add(new StringContent(languageHint, Encoding.UTF8), "languages[]");
                    }
                }
                else if (normalizedLanguages.Count == 1)
                {
                    form.Add(new StringContent(normalizedLanguages[0], Encoding.UTF8), "language");
                }

                await using var stream = new FileStream(
                    audioPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 1024 * 256,
                    useAsync: true);
                using var audioContent = new StreamContent(stream);
                audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
                form.Add(audioContent, "file", "audio.mp3");
                request.Content = form;

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    attemptCancellation.Token);
                serverRequestId = ReadHeader(response, "x-request-id");
                responseStatusCode = response.StatusCode;
                WriteResponseDiagnostics("audio-transcription", clientRequestId, response, requestTimer.Elapsed);
                var body = await ReadResponseBodyAsync(response.Content, attemptCancellation.Token);

                if (response.IsSuccessStatusCode)
                {
                    return ReadTranscriptionResult(body, usesSpeakerDiarization);
                }

                var exception = CreateException(response, body, clientRequestId);
                if (!IsTransient(response.StatusCode) || attempt == attempts)
                {
                    throw exception;
                }

                var delay = RetryDelay(attempt, response.Headers.RetryAfter);
                var requestReference = exception.RequestId ?? exception.ClientRequestId ?? "unavailable";
                log?.Invoke(
                    $"OpenAI returned HTTP {(int)response.StatusCode} (request {requestReference}). " +
                    $"Retrying in {delay.TotalSeconds:0} seconds.");
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                var diagnostic = CreateNetworkException(
                    "transcription upload",
                    attempt,
                    attempts,
                    clientRequestId,
                    serverRequestId,
                    responseStatusCode,
                    requestTimer.Elapsed,
                    ex);
                StartupDiagnostics.WriteException("OPENAI NETWORK", diagnostic);
                if (attempt == attempts)
                {
                    throw diagnostic;
                }

                var delay = NetworkRetryDelay(attempt);
                log?.Invoke(
                    $"Network error ({diagnostic.FailureKind}; client request {clientRequestId}; " +
                    $"elapsed {diagnostic.Elapsed.TotalSeconds:0.0}s). Retrying in {delay.TotalSeconds:0} seconds.");
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                var diagnostic = CreateTimeoutException(
                    "transcription upload",
                    attempt,
                    attempts,
                    clientRequestId,
                    serverRequestId,
                    responseStatusCode,
                    requestTimer.Elapsed,
                    ex);
                StartupDiagnostics.WriteException("OPENAI TIMEOUT", diagnostic);
                if (attempt == attempts)
                {
                    throw diagnostic;
                }

                var delay = NetworkRetryDelay(attempt);
                log?.Invoke(
                    $"OpenAI request timed out (client request {clientRequestId}; " +
                    $"elapsed {diagnostic.Elapsed.TotalSeconds:0.0}s). Retrying in {delay.TotalSeconds:0} seconds.");
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException("Transcription failed after all retry attempts.");
    }

    private static IReadOnlyList<string> OptionalSingleLanguageList(string? language)
        => language is null ? Array.Empty<string>() : [language];

    private static string CreateClientRequestId()
        => "ct-" + Guid.NewGuid().ToString("N");

    private static void AddClientRequestId(HttpRequestMessage request, string clientRequestId)
    {
        if (!request.Headers.TryAddWithoutValidation("X-Client-Request-Id", clientRequestId))
        {
            throw new InvalidOperationException("Could not attach the OpenAI client request identifier.");
        }
    }

    private static void WriteResponseDiagnostics(
        string operation,
        string clientRequestId,
        HttpResponseMessage response,
        TimeSpan elapsed)
    {
        var requestId = ReadHeader(response, "x-request-id") ?? "unavailable";
        var processingMilliseconds = ReadHeader(response, "openai-processing-ms") ?? "unavailable";
        StartupDiagnostics.WriteHttpResponse(operation, clientRequestId, (int)response.StatusCode,
            requestId, elapsed.TotalMilliseconds, processingMilliseconds);
    }

    private static string? ReadHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        return string.IsNullOrWhiteSpace(value)
            ? null
            : InputValidation.SingleLineForLog(value, 512);
    }

    private static OpenAiNetworkException CreateNetworkException(
        string operation,
        int attempt,
        int attempts,
        string clientRequestId,
        string? serverRequestId,
        HttpStatusCode? responseStatusCode,
        TimeSpan elapsed,
        HttpRequestException exception)
    {
        var (failureKind, guidance) = DescribeNetworkFailure(exception);
        var technicalDetail = BuildTechnicalDetail(exception);
        var serverReference = string.IsNullOrWhiteSpace(serverRequestId)
            ? "No OpenAI server request ID was returned."
            : $"OpenAI server request ID: {serverRequestId}.";
        var statusReference = responseStatusCode is null
            ? string.Empty
            : $" HTTP status: {(int)responseStatusCode.Value} ({responseStatusCode.Value}).";
        var message =
            $"OpenAI {operation} failed ({failureKind}) on attempt {attempt}/{attempts} " +
            $"after {elapsed.TotalSeconds:0.0} seconds. {guidance} " +
            $"Client request ID: {clientRequestId}. {serverReference}{statusReference} " +
            $"Technical detail: {technicalDetail}";
        return new OpenAiNetworkException(
            exception.HttpRequestError,
            message,
            failureKind,
            clientRequestId,
            serverRequestId,
            responseStatusCode,
            elapsed,
            exception);
    }

    private static OpenAiNetworkException CreateTimeoutException(
        string operation,
        int attempt,
        int attempts,
        string clientRequestId,
        string? serverRequestId,
        HttpStatusCode? responseStatusCode,
        TimeSpan elapsed,
        OperationCanceledException exception)
    {
        var serverReference = string.IsNullOrWhiteSpace(serverRequestId)
            ? "No OpenAI server request ID was returned."
            : $"OpenAI server request ID: {serverRequestId}.";
        var statusReference = responseStatusCode is null
            ? string.Empty
            : $" HTTP status: {(int)responseStatusCode.Value} ({responseStatusCode.Value}).";
        var message =
            $"OpenAI {operation} timed out on attempt {attempt}/{attempts} " +
            $"after {elapsed.TotalSeconds:0.0} seconds. " +
            $"The server may still have received a billable audio request, so automatic retries can repeat work. " +
            $"Check VPN/proxy stability and the diagnostic log. Client request ID: {clientRequestId}. " +
            $"{serverReference}{statusReference}";
        return new OpenAiNetworkException(
            HttpRequestError.ConnectionError,
            message,
            "timeout",
            clientRequestId,
            serverRequestId,
            responseStatusCode,
            elapsed,
            exception);
    }

    private static string BuildTechnicalDetail(Exception exception)
    {
        var parts = new List<string>();
        for (Exception? current = exception; current is not null && parts.Count < 5; current = current.InnerException)
        {
            var message = InputValidation.SingleLineForLog(current.Message, 240);
            var detail = $"{current.GetType().Name}: {message}";
            if (current is SocketException socketException)
            {
                detail += $" (SocketError={socketException.SocketErrorCode}; NativeError={socketException.NativeErrorCode})";
            }

            parts.Add(detail);
        }

        return InputValidation.SingleLineForLog(string.Join(" -> ", parts), 900);
    }

    private static (string FailureKind, string Guidance) DescribeNetworkFailure(HttpRequestException exception)
    {
        var socketError = FindInnerException<SocketException>(exception)?.SocketErrorCode;
        if (socketError is SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData)
        {
            return ("DNS/name resolution", "Windows could not resolve api.openai.com. Check DNS, VPN, and proxy settings.");
        }

        return exception.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError =>
                ("DNS/name resolution", "Windows could not resolve api.openai.com. Check DNS, VPN, and proxy settings."),
            HttpRequestError.ConnectionError =>
                ("connection", "The TCP connection could not be established or was reset. Check firewall, VPN, proxy, and internet stability."),
            HttpRequestError.SecureConnectionError =>
                ("TLS/certificate", "The secure TLS connection failed. Check the system clock, TLS inspection, antivirus HTTPS scanning, and corporate certificates."),
            HttpRequestError.HttpProtocolError =>
                ("HTTP protocol", "The HTTP conversation failed. A proxy, gateway, or interrupted connection may have returned an invalid protocol response."),
            HttpRequestError.ExtendedConnectNotSupported =>
                ("extended CONNECT unsupported", "A proxy or gateway does not support the requested HTTP tunnel behavior."),
            HttpRequestError.VersionNegotiationError =>
                ("HTTP version negotiation", "HTTP version negotiation failed. Check proxy, gateway, and TLS inspection compatibility."),
            HttpRequestError.UserAuthenticationError =>
                ("network authentication", "Network or proxy authentication failed before OpenAI returned an API response."),
            HttpRequestError.ProxyTunnelError =>
                ("proxy tunnel", "The system proxy could not create a tunnel to api.openai.com. Check proxy access and credentials."),
            HttpRequestError.InvalidResponse =>
                ("invalid response", "A proxy, gateway, or interrupted connection returned an invalid HTTP response."),
            HttpRequestError.ResponseEnded =>
                ("response ended early", "The connection closed before the full OpenAI response arrived. Check network stability, VPN, proxy, and TLS inspection."),
            HttpRequestError.ConfigurationLimitExceeded =>
                ("HTTP configuration limit", "The response exceeded a .NET HTTP safety limit. Check proxy/gateway headers and diagnostics."),
            _ =>
                ("unknown network failure", "Check internet access, DNS, VPN, proxy, firewall, and the full diagnostic log.")
        };
    }

    private static TException? FindInnerException<TException>(Exception exception)
        where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException match)
            {
                return match;
            }
        }

        return null;
    }

    private static string NormalizeModel(string model)
    {
        var normalized = model?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 128 || normalized.Any(char.IsWhiteSpace) || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("The selected OpenAI model name is invalid.", nameof(model));
        }

        return normalized;
    }

    private static async Task<string> ReadResponseBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        try
        {
            await using var stream = await content.ReadAsStreamAsync(cancellationToken);
            var block = new byte[16 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(block.AsMemory(), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (buffer.Length + read > MaximumResponseBytes)
                {
                    throw new OpenAiProtocolException("OpenAI returned an unexpectedly large response body.");
                }

                await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken);
            }
        }
        catch (IOException ex)
        {
            // ResponseHeadersRead leaves the body on the network stream. A connection reset or
            // truncated TLS stream can therefore surface as IOException rather than
            // HttpRequestException. Convert only response-body I/O failures so the existing
            // bounded retry policy handles them; local MP3 read failures remain file-specific.
            throw new HttpRequestException("The OpenAI response stream failed while being read.", ex);
        }

        try
        {
            return StrictUtf8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
        }
        catch (DecoderFallbackException ex)
        {
            throw new OpenAiProtocolException("OpenAI returned a response that is not valid UTF-8.", ex);
        }
    }

    private static OpenAiTranscriptionResult ReadTranscriptionResult(
        string body,
        bool expectSpeakerSegments)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new OpenAiProtocolException("OpenAI returned an empty transcription response.");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("text", out var textElement)
                || textElement.ValueKind != JsonValueKind.String)
            {
                throw new OpenAiProtocolException(
                    "OpenAI returned a transcription response without a string text field.");
            }

            var text = textElement.GetString()?.Trim() ?? string.Empty;
            if (!expectSpeakerSegments)
            {
                return new OpenAiTranscriptionResult(text, []);
            }

            if (!document.RootElement.TryGetProperty("segments", out var segmentsElement)
                || segmentsElement.ValueKind != JsonValueKind.Array)
            {
                throw new OpenAiProtocolException(
                    "OpenAI returned diarized transcription JSON without a segments array.");
            }

            var segments = new List<SpeakerSegment>();
            foreach (var segmentElement in segmentsElement.EnumerateArray())
            {
                if (segments.Count >= 50_000)
                {
                    throw new OpenAiProtocolException(
                        "OpenAI returned too many diarization segments.");
                }

                if (segmentElement.ValueKind != JsonValueKind.Object
                    || !segmentElement.TryGetProperty("speaker", out var speakerElement)
                    || speakerElement.ValueKind != JsonValueKind.String
                    || !segmentElement.TryGetProperty("start", out var startElement)
                    || !segmentElement.TryGetProperty("end", out var endElement)
                    || !segmentElement.TryGetProperty("text", out var segmentTextElement)
                    || segmentTextElement.ValueKind != JsonValueKind.String
                    || !startElement.TryGetDouble(out var startSeconds)
                    || !endElement.TryGetDouble(out var endSeconds)
                    || !double.IsFinite(startSeconds)
                    || !double.IsFinite(endSeconds)
                    || startSeconds < 0
                    || endSeconds < startSeconds
                    || endSeconds > TimeSpan.FromDays(7).TotalSeconds)
                {
                    throw new OpenAiProtocolException(
                        "OpenAI returned an invalid speaker segment in diarized transcription JSON.");
                }

                var speaker = InputValidation.SingleLineForLog(
                    speakerElement.GetString() ?? string.Empty,
                    128).Trim();
                if (string.IsNullOrWhiteSpace(speaker))
                {
                    speaker = "Unknown speaker";
                }

                var segmentText = segmentTextElement.GetString()?.Trim() ?? string.Empty;
                if (segmentText.Length == 0)
                {
                    continue;
                }

                segments.Add(new SpeakerSegment(
                    speaker,
                    TimeSpan.FromSeconds(startSeconds),
                    TimeSpan.FromSeconds(endSeconds),
                    segmentText));
            }

            if (!string.IsNullOrWhiteSpace(text) && segments.Count == 0)
            {
                throw new OpenAiProtocolException(
                    "OpenAI returned non-empty diarized text without speaker segments.");
            }

            return new OpenAiTranscriptionResult(text, segments);
        }
        catch (JsonException ex)
        {
            throw new OpenAiProtocolException("OpenAI returned malformed transcription JSON.", ex);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        var numeric = (int)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests
            || numeric == 425
            || numeric >= 500;
    }

    private static TimeSpan RetryDelay(int attempt, RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta > TimeSpan.FromMinutes(2) ? TimeSpan.FromMinutes(2) : delta;
        }

        if (retryAfter?.Date is { } retryDate)
        {
            var dateDelay = retryDate - DateTimeOffset.UtcNow;
            if (dateDelay > TimeSpan.Zero)
            {
                return dateDelay > TimeSpan.FromMinutes(2) ? TimeSpan.FromMinutes(2) : dateDelay;
            }
        }

        return NetworkRetryDelay(attempt);
    }

    private static TimeSpan NetworkRetryDelay(int attempt)
    {
        var baseSeconds = Math.Min(30, 2 * Math.Pow(2, Math.Max(0, attempt - 1)));
        return TimeSpan.FromSeconds(baseSeconds + (Random.Shared.NextDouble() * 1.5));
    }

    private static OpenAiApiException CreateException(
        HttpResponseMessage response,
        string body,
        string clientRequestId)
    {
        var statusCode = response.StatusCode;
        var requestId = ReadHeader(response, "x-request-id");
        var details = TryReadErrorDetails(body);
        var message = details.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"OpenAI request failed with HTTP {(int)statusCode} ({statusCode}).";
        }

        var diagnosticParts = new List<string>();
        AddDiagnosticPart(diagnosticParts, "type", details.Type);
        AddDiagnosticPart(diagnosticParts, "param", details.Param);
        AddDiagnosticPart(diagnosticParts, "code", details.Code);
        AddDiagnosticPart(diagnosticParts, "request_id", requestId);
        AddDiagnosticPart(diagnosticParts, "client_request_id", clientRequestId);
        if (diagnosticParts.Count > 0)
        {
            message += " [" + string.Join(", ", diagnosticParts) + "]";
        }

        var exception = new OpenAiApiException(
            statusCode,
            message,
            details.Type,
            details.Param,
            details.Code,
            requestId,
            clientRequestId);
        StartupDiagnostics.WriteException("OPENAI API", exception);
        return exception;
    }

    private static OpenAiErrorDetails TryReadErrorDetails(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                return new OpenAiErrorDetails(
                    ReadBoundedScalar(error, "message", 500),
                    ReadBoundedScalar(error, "type", 160),
                    ReadBoundedScalar(error, "param", 160),
                    ReadBoundedScalar(error, "code", 160));
            }
        }
        catch (JsonException)
        {
            // A proxy can return plain text; use the bounded, sanitised fallback below.
        }

        return new OpenAiErrorDetails(
            InputValidation.SingleLineForLog(body, 500),
            null,
            null,
            null);
    }

    private static string? ReadBoundedScalar(JsonElement owner, string propertyName, int maximumCharacters)
    {
        if (!owner.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        var scalar = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(scalar))
        {
            return null;
        }

        return InputValidation.SingleLineForLog(scalar, maximumCharacters);
    }

    private static void AddDiagnosticPart(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(name + "=" + value);
        }
    }

    private sealed record OpenAiErrorDetails(
        string? Message,
        string? Type,
        string? Param,
        string? Code);

    public void Dispose() => _httpClient.Dispose();
}
