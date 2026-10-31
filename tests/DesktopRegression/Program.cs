using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using CosmicTranscriber.Models;
using CosmicTranscriber.Services;
using CosmicTranscriber.Utilities;

// No network: every client is replaced with an in-memory handler before use.
// Input is a locally generated tone MP3, never a private recording.
var root = Path.GetFullPath(args[0]);
var audio = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
var passed = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAIL: " + name);
    passed++;
    Console.WriteLine("PASS: " + name);
}
async Task Reject<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); }
    catch (T) { Check(true, name); return; }
    throw new Exception("FAIL: expected " + typeof(T).Name + " for " + name);
}
foreach (var price in new string?[] { null, "", "abc", "NaN", "Infinity", "-0.01" })
    Check(RunPresentation.EstimatedCost(price, TimeSpan.FromMinutes(10)) == "Enter a valid price", "invalid price: " + (price ?? "null"));
Check(RunPresentation.EstimatedCost("0", TimeSpan.FromMinutes(10)) == "US$0.000 est.", "explicit zero estimate");
Check(RunPresentation.EstimatedCost("0.006", TimeSpan.FromMinutes(10)) == "US$0.060 est.", "valid estimate");
Check(RunPresentation.EstimatedCost("1e308", TimeSpan.FromMinutes(10)) == "Invalid estimate", "estimate overflow");
Check(RunPresentation.CompletionGuidance(0, true, CompletionEvidence.ReviewRequired).NextAction == CompletionNextAction.PreserveAndReview, "zero-export partial run message");
Check(RunPresentation.CompletionGuidance(2, true, CompletionEvidence.AcceptedCheckpoints).NextAction == CompletionNextAction.ReviewAndReuseAccepted, "partial run with exports message");
Check(RunPresentation.CompletionSubtitle(2, false) == "Your transcript files are ready.", "successful run message");
// Completion decisions use typed evidence, never provider/error message text.
foreach (var exportCount in new[] { 0, 2 }) {
    foreach (var state in new[] { CompletionEvidence.UnresolvedPaidAttempt,
        CompletionEvidence.UnresolvedPaidAttempt | CompletionEvidence.AcceptedCheckpoints,
        CompletionEvidence.UnresolvedPaidAttempt | CompletionEvidence.FailedBeforeDispatch | CompletionEvidence.ReviewRequired }) {
        var advice = RunPresentation.CompletionGuidance(exportCount, true, state);
        Check(advice.NextAction == CompletionNextAction.PreserveAndReconcile && advice.Message.Contains("Do not clear the cache or retry unchanged"), "uncertain message/action preserves evidence: " + state + "/" + exportCount);
        Check(!advice.Message.Contains("then retry") && !advice.Message.Contains("to resume"), "uncertain never recommends unchanged resume");
    }
}
var beforeEvidence = new RunCompletionEvidence();
beforeEvidence.Failed(new IOException("retry unchanged; accepted; fake provider text"), false, false);
Check(beforeEvidence.Value == CompletionEvidence.FailedBeforeDispatch && RunPresentation.CompletionGuidance(0, true, beforeEvidence.Value).NextAction == CompletionNextAction.CorrectInput, "known pre-coordinator rejection corrects input");
var uncertainEvidence = new RunCompletionEvidence();
uncertainEvidence.Accepted(); uncertainEvidence.Failed(new PendingTranscriptionException(), true, false);
Check(RunPresentation.CompletionGuidance(2, true, uncertainEvidence.Value).NextAction == CompletionNextAction.PreserveAndReconcile, "mixed accepted and pending never recommends reuse of unresolved work");
var storageEvidence = new RunCompletionEvidence();
storageEvidence.Failed(new CheckpointStorageException("synthetic", new IOException()), true, false);
Check(RunPresentation.CompletionGuidance(0, true, storageEvidence.Value).NextAction == CompletionNextAction.PreserveAndReconcile, "accepted-but-not-durably-reconciled storage failure needs review");
var earlyStorage = new RunCompletionEvidence(); earlyStorage.Failed(new CheckpointStorageException("pre-dispatch lease failure", new IOException()), true, false);
Check(RunPresentation.CompletionGuidance(0,true,earlyStorage.Value).Message.StartsWith("The paid outcome or checkpoint state") && !RunPresentation.CompletionGuidance(0,true,earlyStorage.Value).Message.StartsWith("A paid attempt is unresolved"), "pre-dispatch storage failure does not assert a paid attempt");
var unknownEvidence = new RunCompletionEvidence(); unknownEvidence.Failed(new IOException("definitely before dispatch"), true, false);
Check(unknownEvidence.Value == CompletionEvidence.ReviewRequired, "unclassified coordinator failure is conservative regardless of message");
var exportEvidence = new RunCompletionEvidence(); exportEvidence.Accepted(); exportEvidence.Failed(new IOException(), true, true);
Check(RunPresentation.CompletionGuidance(0, true, exportEvidence.Value).NextAction == CompletionNextAction.ReviewAndReuseAccepted, "validated accepted result followed by export failure reuses checkpoints");
var inputAdvice=RunPresentation.CompletionGuidance(0,true,beforeEvidence.Value);
Check(inputAdvice.Message.Contains("before its transcription request was dispatched") && inputAdvice.Message.Contains("other or earlier runs"), "predispatch wording does not claim global free billing");
Check(RunPresentation.CompletionGuidance(0,true,CompletionEvidence.None).NextAction == CompletionNextAction.PreserveAndReview && !RunPresentation.CompletionSubtitle(0,true).Contains("then retry"), "missing/legacy evidence cannot promise replay");
Check(RunPresentation.CompletionGuidance(2,false,CompletionEvidence.AcceptedCheckpoints).NextAction == CompletionNextAction.OpenOutputs, "successful accepted run exposes outputs");
var mainSource=File.ReadAllText(Path.Combine(root,"src/CosmicTranscriber/MainWindow.xaml.cs"));
Check(mainSource.Contains("completionEvidence.Value)") && mainSource.Contains("completionEvidence.Failed(ex, coordinatorStarted, coordinatorCompleted)") && mainSource.Contains("completionEvidence.Accepted()"), "actual completion path receives typed run evidence");
Check(!mainSource.Contains("retained for a safe retry") && mainSource.Contains("FooterDetailText.Text = RunPresentation.CompletionGuidance") && mainSource.Contains("RunPresentation.CompletionGuidance(writtenOutputs.Count, completedWithIssues, completionEvidence.Value).Message"), "outer failure and completion fallback retain typed conservative guidance");
var completionSource=File.ReadAllText(Path.Combine(root,"src/CosmicTranscriber/CompletionWindow.xaml.cs"));
Check(completionSource.Contains("RunPresentation.CompletionGuidance(outputFileCount, completedWithErrors, evidence).Message"), "actual completion window uses tested guidance");
var completionXaml=XDocument.Load(Path.Combine(root,"src/CosmicTranscriber/CompletionWindow.xaml"));
XNamespace completionNames="http://schemas.microsoft.com/winfx/2006/xaml";
var completionViewport=completionXaml.Descendants().Single(e=>(string?)e.Attribute(completionNames+"Name")=="CompletionViewport");
Check(completionViewport.Name.LocalName=="ScrollViewer" && completionViewport.Descendants().Any(e=>(string?)e.Attribute(completionNames+"Name")=="ReviewAttemptsButton"),"completion text and recovery actions share a real outer viewport");
var outputField=completionXaml.Descendants().Single(e=>(string?)e.Attribute(completionNames+"Name")=="OutputPathTextBox");
Check((string?)outputField.Attribute("MinHeight")=="48" && outputField.Ancestors().Any(e=>e==completionViewport),"completion path has useful height inside scrollable body");
var recoveryXaml=XDocument.Load(Path.Combine(root,"src/CosmicTranscriber/RecoveryWindow.xaml"));
Check(recoveryXaml.Descendants().Any(e=>e.Name.LocalName=="ScrollViewer") && recoveryXaml.Descendants().Any(e=>(string?)e.Attribute(completionNames+"Name")=="RecheckRecoveryButton"),"recovery actions live in a vertically scrollable window");
var xaml = XDocument.Load(Path.Combine(root, "src/CosmicTranscriber/MainWindow.xaml"));
XNamespace names = "http://schemas.microsoft.com/winfx/2006/xaml";
foreach (var field in new[] { "OutputPathTextBox", "CombinedTitleTextBox", "PricePerMinuteTextBox" })
    Check(xaml.Descendants().Single(e => (string?)e.Attribute(names + "Name") == field).Attribute("AutomationProperties.Name")?.Value.Length > 0, "accessible name: " + field);

var metadata = Mp3MetadataService.Inspect(audio);
Check(metadata.Duration.TotalSeconds > 610 && metadata.Duration.TotalSeconds < 700, "synthetic long MP3 metadata");
var empty = Path.Combine(output, "empty.mp3");
File.WriteAllBytes(empty, []);
await Reject<InvalidDataException>(() => Task.Run(() => Mp3MetadataService.Inspect(empty)), "empty MP3 rejected");
var invalid = Path.Combine(output, "invalid.mp3");
File.WriteAllText(invalid, "This is not audio.");
await Reject<InvalidDataException>(() => Task.Run(() => Mp3MetadataService.Inspect(invalid)), "invalid MP3 rejected");
var chunks = await new Mp3Chunker().CreateChunksAsync(audio, Path.Combine(output, "chunk-smoke"), 20, 5, metadata.Duration, true, true, null, CancellationToken.None);
Check(chunks.Count == 3, "long MP3 produces three chunks");
Check(chunks.All(c => c.SizeBytes <= OpenAiTranscriptionClient.MaximumAudioPayloadBytes && c.Duration <= TimeSpan.FromMinutes(5)), "chunk size and duration ceilings");
Check(Math.Abs(chunks.Sum(c => c.Duration.TotalSeconds) - metadata.Duration.TotalSeconds) < 0.1, "chunks preserve logical duration");
Check(chunks[1].PrerollDuration > TimeSpan.Zero, "later chunk preroll");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    await Reject<OperationCanceledException>(() => new Mp3Chunker().CreateChunksAsync(audio, Path.Combine(output, "cancelled-chunks"), 20, 5, metadata.Duration, true, true, null, cancelled.Token), "pre-cancelled chunk preparation");
}

var handler = new FakeHandler();
using var client = MockClient(handler);
var request = new TranscriptionRequest { ApiKey = "local-mock-key", Model = TranscriptionModelCatalog.DefaultFileModel, Prompt = "", UsePreviousContext = true };
var item = new AudioJobItem { SourcePath = audio, FileName = Path.GetFileName(audio), SizeBytes = metadata.SizeBytes, Duration = metadata.Duration, LastWriteTimeUtc = metadata.LastWriteTimeUtc, SourceSha256 = metadata.Sha256 };
var coordinator = new TranscriptionCoordinator(new Mp3Chunker(), client);
var stages = new List<string>();
var result = await coordinator.ProcessFileAsync(item, Path.Combine(output, "coordinator"), request, new InlineProgress<CoordinatorProgress>(p => stages.Add(p.Stage)), null, CancellationToken.None);
Check(handler.Calls == 3 && result.Chunks.Count == 3, "mocked full transcription");
Check(stages.Contains("Preparing") && stages.Contains("Transcribing"), "preparation and transcription status reported");
var resumed = await coordinator.ProcessFileAsync(item, Path.Combine(output, "coordinator"), request, null, null, CancellationToken.None);
Check(handler.Calls == 3 && resumed.Chunks.All(c => c.ReusedFromCheckpoint), "resume reuses all checkpoints without requests");
Check(handler.SawGenericFilename && handler.SawClientRequestId, "multipart generic filename and request ID");
var writer = new TranscriptOutputWriter();
var exports = writer.WriteIndividual(result, Path.Combine(output, "exports"), true, true, true);
Check(exports.Count == 2 && exports.All(File.Exists), "TXT and Markdown output location");
Check(exports.All(p => File.ReadAllText(p).Contains("Synthetic mocked transcript")), "export transcript contents");
var repeatedExports = writer.WriteIndividual(result, Path.Combine(output, "exports"), true, false, true);
Check(repeatedExports[0] != exports[0] && File.Exists(exports[0]), "export collision preserves original");
var combined = writer.WriteCombined([result, resumed], "CON", Path.Combine(output, "combined"), true, true, false);
Check(combined.Count == 2 && combined.All(File.Exists), "combined export and Windows reserved filename");

var unauthorized = new FakeHandler { Status = HttpStatusCode.Unauthorized };
using var unauthorizedClient = MockClient(unauthorized);
await Reject<OpenAiApiException>(() => unauthorizedClient.TranscribeAsync("local-mock-key", request.Model, chunks[0].Path, "", null, null, 2, null, CancellationToken.None), "401 shown as API error");
Check(unauthorized.Calls == 1, "401 not retried");
var retry = new FakeHandler { FirstTransient = true };
using var retryClient = MockClient(retry);
await Reject<OpenAiApiException>(() => retryClient.TranscribeAsync("local-mock-key", request.Model, chunks[0].Path, "", null, null, 1, null, CancellationToken.None), "503 surfaced without paid replay");
Check(retry.Calls == 1, "transient failure never automatically replays paid POST");
var malformed = new FakeHandler { Body = "{}" };
using var malformedClient = MockClient(malformed);
await Reject<OpenAiProtocolException>(() => malformedClient.TranscribeAsync("local-mock-key", request.Model, chunks[0].Path, "", null, null, 0, null, CancellationToken.None), "malformed response reports protocol error");
var silence = new FakeHandler { Body = "{\"text\":\"\"}" };
using var silenceClient = MockClient(silence);
Check((await silenceClient.TranscribeAsync("local-mock-key", request.Model, chunks[0].Path, "", null, null, 0, null, CancellationToken.None)).Text == "", "valid no-speech response accepted");
using (var cancellation = new CancellationTokenSource())
{
    var cancellable = new FakeHandler { OnRequest = () => cancellation.Cancel() };
    using var cancelClient = MockClient(cancellable);
    await Reject<OperationCanceledException>(() => cancelClient.TranscribeAsync("local-mock-key", request.Model, chunks[0].Path, "", null, null, 0, null, cancellation.Token), "in-flight cancellation propagated");
    Check(cancellable.Calls == 1, "cancel does not retry");
}
passed += CoreFramePreservation.Verify(audio, chunks);
Console.WriteLine($"DESKTOP REGRESSION PASSED: {passed} assertions; in-memory HTTP only.");

static OpenAiTranscriptionClient MockClient(HttpMessageHandler handler)
{
    var client = new OpenAiTranscriptionClient();
    var field = typeof(OpenAiTranscriptionClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!;
    ((HttpClient)field.GetValue(client)!).Dispose();
    field.SetValue(client, new HttpClient(handler) { BaseAddress = new Uri("https://mock.invalid/"), Timeout = Timeout.InfiniteTimeSpan });
    return client;
}

sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
sealed class FakeHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
    public bool FirstTransient { get; init; }
    public string Body { get; init; } = "{\"text\":\"Synthetic mocked transcript.\"}";
    public Action? OnRequest { get; init; }
    public bool SawGenericFilename { get; private set; }
    public bool SawClientRequestId { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Host != "mock.invalid") throw new Exception("Unexpected mock target");
        Calls++;
        SawClientRequestId |= request.Headers.Contains("X-Client-Request-Id");
        if (request.Content is MultipartFormDataContent form)
        {
            var file = form.Single(p => p.Headers.ContentDisposition?.Name?.Trim('"') == "file");
            SawGenericFilename |= file.Headers.ContentDisposition?.FileName?.Trim('"') == "audio.mp3";
            _ = await file.ReadAsByteArrayAsync(cancellationToken);
        }
        OnRequest?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        var status = FirstTransient && Calls == 1 ? HttpStatusCode.ServiceUnavailable : Status;
        var response = new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.OK ? Body : "{\"error\":{\"message\":\"Synthetic failure\"}}", Encoding.UTF8, "application/json") };
        response.Headers.TryAddWithoutValidation("Retry-After", "0");
        return response;
    }
}
