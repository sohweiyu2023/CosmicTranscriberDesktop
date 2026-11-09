using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CosmicTranscriber.Services;
using CosmicTranscriber.Models;
using CosmicTranscriber.Utilities;

// No network, environment key, user settings or audio decoder. All files are synthetic.
var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
var audio = Path.Combine(root, "synthetic-upload.mp3"); File.WriteAllBytes(audio, [1, 2, 3]);
var passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); passed++; Console.WriteLine("PASS " + name); }
async Task<Exception> Failure(Func<Task> call, string name) {
    try { await call(); } catch (Exception e) { Check(true, name); return e; }
    throw new Exception("Expected failure: " + name);
}
Task Call(OpenAiTranscriptionClient client, CancellationToken token = default) =>
    client.TranscribeAsync("local-mock-key", "gpt-transcribe", audio, "", null, null, 5, null, token);

StartupDiagnostics.Initialize(); var logPath = StartupDiagnostics.LogPath;
foreach (var status in new[] { 400, 401, 403, 408, 409, 425, 429, 500, 502, 503, 504 }) {
    var h = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) {
        Content = new StringContent("{\"error\":{\"message\":\"private response\",\"type\":\"invalid_request_error\",\"code\":\"invalid_value\",\"param\":\"model\"}}") }));
    using var client = new OpenAiTranscriptionClient(h);
    var error = await Failure(() => Call(client), "HTTP " + status + " surfaced");
    Check(error is OpenAiApiException && h.Calls == 1, "HTTP " + status + " single paid dispatch with legacy retryCount=5");
}
foreach (var kind in new[] { HttpRequestError.NameResolutionError, HttpRequestError.ConnectionError, HttpRequestError.SecureConnectionError, HttpRequestError.ResponseEnded }) {
    var h = new FakeHandler((_, _) => throw new HttpRequestException(kind, "PRIVATE_TRANSPORT", null));
    using var client = new OpenAiTranscriptionClient(h);
    Check(await Failure(() => Call(client), "transport " + kind) is OpenAiNetworkException && h.Calls == 1, "transport is uncertain, never replayed: " + kind);
}
foreach (var bytes in new[] { Encoding.UTF8.GetBytes("<html>PRIVATE_PROVIDER_BODY</html>"), Encoding.UTF8.GetBytes("{\"text\":{\"nested\":true}}"), new byte[] { 0xff, 0xfe }, new byte[OpenAiTranscriptionClient.MaximumResponseBytes + 1] }) {
    var h = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
    using var client = new OpenAiTranscriptionClient(h);
    Check(await Failure(() => Call(client), "malformed/oversized successful response") is OpenAiProtocolException && h.Calls == 1, "protocol failure no paid replay");
}
{
    var h = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new InterruptedContent() }));
    using var client = new OpenAiTranscriptionClient(h);
    await Failure(() => Call(client), "response interrupted after headers"); Check(h.Calls == 1, "interrupted response no paid replay");
}
{
    var h = new FakeHandler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); });
    using var client = new OpenAiTranscriptionClient(h, TimeSpan.FromMilliseconds(40));
    Check(await Failure(() => Call(client), "timeout after dispatch") is OpenAiNetworkException && h.Calls == 1, "timeout retained as uncertain, no paid replay");
}
{
    var h = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
    using var client = new OpenAiTranscriptionClient(h); using var stop = new CancellationTokenSource(); stop.Cancel();
    await Failure(() => Call(client, stop.Token), "pre-dispatch cancellation"); Check(h.Calls == 0, "proven pre-dispatch cancellation sends nothing");
    File.WriteAllBytes(audio, []); await Failure(() => Call(client), "pre-dispatch invalid file"); Check(h.Calls == 0, "validation sends nothing"); File.WriteAllBytes(audio, [1,2,3]);
}
{
    using var stop = new CancellationTokenSource();
    var h = new FakeHandler((_, _) => { stop.Cancel(); throw new OperationCanceledException(stop.Token); });
    using var client = new OpenAiTranscriptionClient(h);
    Check(await Failure(() => Call(client, stop.Token), "cancelled after dispatch") is OperationCanceledException && h.Calls == 1, "post-dispatch user cancellation never replays");
}
{
    var h = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new ByteArrayContent(new byte[] { 0xff }) }));
    using var client = new OpenAiTranscriptionClient(h);
    Check(await Failure(() => client.TestConnectionAsync("local-mock-key", "gpt-transcribe", TimeSpan.FromSeconds(1), CancellationToken.None), "connection-test malformed body") is OpenAiProtocolException && h.Calls == 1, "nonpaid protocol failure classified without retry");
}
{
    using var stop = new CancellationTokenSource();
    var h = new FakeHandler((_, _) => { stop.Cancel(); throw new OperationCanceledException(stop.Token); });
    using var client = new OpenAiTranscriptionClient(h);
    Check(await Failure(() => client.TestConnectionAsync("local-mock-key", "gpt-transcribe", TimeSpan.FromSeconds(1), stop.Token), "connection-test cancellation") is OperationCanceledException && h.Calls == 1, "nonpaid cancellation terminal retained");
}


// Routine evidence never copies arbitrary prose, including unlabelled audio-derived/Unicode text.
var proseCanary="unlabelled private fragment \u8bca\u65ad\u9690\u79c1 " + Guid.NewGuid().ToString("N");
var privateBody=JsonSerializer.Serialize(new{error=new{message=proseCanary,code="invalid_value",type="invalid_request_error",param="model",details=new[]{new{reason="nested " + proseCanary}},transcript="PAYLOAD_FIELD_CANARY"}});
var reject=new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent(privateBody)}));
using(var c=new OpenAiTranscriptionClient(reject))await Failure(()=>Call(c),"provider rejection persisted safely");
var persistedSafe=ReadShared(logPath);
Check(!persistedSafe.Contains(proseCanary)&&!persistedSafe.Contains("PAYLOAD_FIELD_CANARY")&&!persistedSafe.Contains("unlabelled private fragment"),"arbitrary provider prose absent from actual persisted bytes");
using(var record=ReadShared(logPath).Split('\n',StringSplitOptions.RemoveEmptyEntries).Where(x=>x.Contains("provider_failure")).Select(x=>JsonDocument.Parse(x)).Last()){
 var d=record.RootElement.GetProperty("data");var e=d.GetProperty("evidence");
 Check(e.GetProperty("ExplanationSource").GetString()=="FixedCodeClassification"&&e.GetProperty("OriginalExplanationOmitted").GetBoolean()&&e.GetProperty("ProviderProseObserved").GetBoolean(),"fixed classification and omission provenance explicit");
 Check(e.GetProperty("Explanations")[0].GetString()=="The provider classified a request value as invalid.","classification uses fixed local text, never a claimed provider quote");
 Check(e.GetProperty("ExplanationFieldsObserved").GetInt32()==2&&e.GetProperty("Coverage").GetString()=="AllowlistedFieldsOnly","nested observations retained without content or full-coverage claim");
 Check(d.GetProperty("status").GetInt32()==403&&d.GetProperty("endpoint").GetString()=="audio-transcription"&&d.GetProperty("elapsedMs").GetDouble()>=0&&reject.Calls==1,"outcome timing endpoint and no replay preserved");
}
foreach(var (body,state,format) in new[]{(proseCanary,"Received","PlainText"),("<html>"+proseCanary+"</html>","Received","HtmlOmitted"),("{\"error\":","MalformedOrDepthLimited","Json"),("","Empty","Unavailable"),(new string('x',ProviderFailureEvidence.MaximumBodyCharacters+1),"Oversized","Unavailable")}){
 var h=new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway){Content=new StringContent(body)}));using var c=new OpenAiTranscriptionClient(h);await Failure(()=>Call(c),"body state "+state+"/"+format);
 using var record=ReadShared(logPath).Split('\n',StringSplitOptions.RemoveEmptyEntries).Where(x=>x.Contains("provider_failure")).Select(x=>JsonDocument.Parse(x)).Last();var e=record.RootElement.GetProperty("data").GetProperty("evidence");
 Check(e.GetProperty("BodyState").GetString()==state&&e.GetProperty("Format").GetString()==format&&h.Calls==1,"actual body availability "+state+"/"+format);
 Check(!ReadShared(logPath).Contains("unlabelled private fragment"),"nonJSON or HTML never copied into local logs");
}
{
 var h=new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent(JsonSerializer.Serialize(new{error=new{message=proseCanary,code="customersecret123",param="patient diagnosis",type="credential-canary"}}))}));using var c=new OpenAiTranscriptionClient(h);var safeUiError=await Failure(()=>c.TestConnectionAsync("local-mock-key","gpt-transcribe",TimeSpan.FromSeconds(1),CancellationToken.None),"model-access unknown-field failure");
 Check(!safeUiError.Message.Contains("customersecret123")&&!safeUiError.Message.Contains("patient diagnosis")&&!safeUiError.Message.Contains("credential-canary")&&!safeUiError.Message.Contains("unlabelled private fragment"),"provider exception UI message also excludes raw structured fields and prose");
 var text=ReadShared(logPath);Check(!text.Contains("customersecret123")&&!text.Contains("patient diagnosis")&&!text.Contains("credential-canary")&&!text.Contains("unlabelled private fragment")&&h.Calls==1,"actual model-access structured and prose canaries absent");
 var e=ProviderFailureEvidence.Capture("{\"error\":{\"code\":\"secret-code\",\"message\":\"private prose\"}}");Check(e.Fields["code"]=="Other"&&e.UnrecognizedFields.Contains("code")&&e.Explanations.Count==0&&e.OriginalExplanationOmitted,"unknown codes marked without fabricating explanation");
 Check(e.OriginalExplanationState=="OmittedByPolicy","observed prose omission state explicit");
 var absent=ProviderFailureEvidence.Capture("{\"error\":{\"code\":\"invalid_value\"}}");Check(absent.MissingExplanation&&!absent.ProviderProseObserved&&!absent.OriginalExplanationOmitted,"missing and deliberately omitted explanations distinct");
 Check(ProviderFailureEvidence.Unavailable("Oversized").OriginalExplanationState=="UnavailableOrUninspected"&&absent.OriginalExplanationState=="NotObservedInAllowlistedFields","uninspected bodies never claimed to lack original prose");
 var many=ProviderFailureEvidence.Capture(JsonSerializer.Serialize(new{error=new{details=Enumerable.Repeat(proseCanary,20).ToArray()}}));Check(many.Truncated&&many.ExplanationFieldsObserved==ProviderFailureEvidence.MaximumExplanations,"observation count bounded and truncation marked");
 Check(typeof(ProviderFailureEvidence).GetConstructors().Length==0,"immutable evidence factory prevents raw metadata forgery");
 var support=Path.Combine(root,"safe-provider-support.json");DiagnosticSupportExporter.Export(Path.GetDirectoryName(logPath),support,CancellationToken.None);var supportText=File.ReadAllText(support);Check(!supportText.Contains("unlabelled private fragment")&&supportText.Contains("RedactedForSupportExport")&&supportText.Contains("AllowlistedFieldsOnly"),"support export retains safe omission marker without prose");
}
const string secret = "PRIVATE_DOCUMENT_SENTINEL Bearer private-credential";
StartupDiagnostics.Write(secret, secret); StartupDiagnostics.WriteLifecycle(secret); StartupDiagnostics.WriteWindow(secret, true, true, secret, 1, 2, 96, 96);
StartupDiagnostics.WriteFocus(secret, secret);
StartupDiagnostics.WriteException(secret, new OpenAiApiException(HttpStatusCode.BadRequest, secret, secret, secret, secret, secret, secret));
var correlated = "ct-" + Guid.NewGuid().ToString("N");
StartupDiagnostics.WriteException("OPENAI API", new OpenAiApiException(HttpStatusCode.Forbidden, secret, "invalid_request_error", "model", "invalid_value", secret, correlated));
var oversizedPath = Path.Combine(root, "oversized");
using (var writer = new DiagnosticLogWriter(oversizedPath, Guid.NewGuid(), 512, 2)) {
    writer.Write(secret, secret, new { secret = new string('x', 4000) });
    Check(writer.OmittedRecords == 1, "oversized record counted");
    var text = ReadShared(writer.Path); Check(text.Contains("record_omitted") && !text.Contains(secret), "oversized record leaves bounded safe completeness marker");
}
foreach (var category in new[] { "FATAL STARTUP", "UNHANDLED DISPATCHER", "UNHANDLED APPDOMAIN", "UNOBSERVED TASK", "COMPLETION" })
    StartupDiagnostics.WriteException(category, new InvalidOperationException(secret));
StartupDiagnostics.Write("MAINWINDOW", "Constructor entered; creating application services.");
StartupDiagnostics.WriteWindow("main_window_shown", true, true, "Normal", 100, 100, 96, 96);
for (var i = 0; i < 100; i++) StartupDiagnostics.CleanupFailure();
// Storage/auth/export paths use explicit fresh fixtures, never the default profile or environment.
using (var parent = StartupDiagnostics.Begin(DiagnosticArea.Settings)) { using var child = StartupDiagnostics.Begin(DiagnosticArea.Authentication); child.Complete(true); parent.Complete(true); }
var settingsRoot = Path.Combine(root, "settings"); Directory.CreateDirectory(settingsRoot);
var settings = new AppSettingsService(settingsRoot);
_ = settings.Load();
File.WriteAllText(settings.SettingsPath, "{bad PRIVATE_DOCUMENT_SENTINEL"); _ = settings.Load();
settings.Save(new AppSettings()); _ = settings.Load();
var beforeQuietSave = new FileInfo(logPath).Length;
settings.Save(new AppSettings(), writeDiagnostic: false);
Check(new FileInfo(logPath).Length == beforeQuietSave, "explicit quiet-save diagnostic preference preserved");
StartupDiagnostics.WriteStorageSelection(DiagnosticArea.Settings, DiagnosticStorageKind.Temporary);
StartupDiagnostics.WriteStorageSelection((DiagnosticArea)999, (DiagnosticStorageKind)999);
var keyStore = new ApiKeyStore(settingsRoot, () => "bad\nPRIVATE_DOCUMENT_SENTINEL");
Check(keyStore.LoadEnvironment().Key.Length == 0, "malformed synthetic environment key falls back without false success");
var syntheticKeyStore = new ApiKeyStore(settingsRoot, () => "local-mock-key");
Check(syntheticKeyStore.LoadEnvironment().Key == "local-mock-key", "synthetic auth source evaluated before completion");
var checkpoint = Path.Combine(root, "checkpoint.json");
var readCheckpoint = typeof(TranscriptionCoordinator).GetMethod("TryReadCheckpointAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
async Task ReadCheckpoint() { await (Task)readCheckpoint.Invoke(null, new object?[] { checkpoint, 1, CancellationToken.None, false })!; }
await ReadCheckpoint(); File.WriteAllText(checkpoint, "{bad PRIVATE_DOCUMENT_SENTINEL"); await ReadCheckpoint();
Check(!File.Exists(checkpoint), "invalid checkpoint removal retains existing behavior and explicit reason");
var writeCheckpoint = typeof(TranscriptionCoordinator).GetMethod("WriteCheckpointAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
await (Task)writeCheckpoint.Invoke(null, new object?[] { checkpoint, 1, secret, Array.Empty<SpeakerSegment>(), CancellationToken.None, null })!;
await ReadCheckpoint();
var blockedPath = Path.Combine(root, "blocked-output"); File.WriteAllText(blockedPath, "synthetic blocker");
Check(!OutputDirectoryService.CanWrite(blockedPath), "output probe failure remains false with typed evidence");
var transcript = new FileTranscriptionResult("synthetic-source.mp3", "Synthetic", TimeSpan.FromSeconds(1),
    new[] { new ChunkTranscript(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), secret, false) });
var export = new TranscriptOutputWriter();
Check(await Failure(() => Task.Run(() => export.WriteIndividual(transcript, blockedPath, true, false, false)), "export failure") is TranscriptExportException, "export retains original wrapper after rollback");
var exported = export.WriteIndividual(transcript, Path.Combine(root, "exports"), true, false, false);
Check(exported.Count == 1, "successful export retains result despite added diagnostics");

var pruneRoot = Path.Combine(root, "prune-failure"); string stale;
using (var oldWriter = new DiagnosticLogWriter(pruneRoot, Guid.NewGuid(), 512, 1)) { oldWriter.Write("TEST", "synthetic", null); stale = oldWriter.Path; }
using (var locked = new FileStream(stale, FileMode.Open, FileAccess.Read, FileShare.None))
using (var newWriter = new DiagnosticLogWriter(pruneRoot, Guid.NewGuid(), 512, 1)) {
    Check(newWriter.PruneFailures > 0, "locked owned-log prune failure is counted without permission changes");
}
var recorded = ReadShared(logPath);
Check(!recorded.Contains("PRIVATE_") && !recorded.Contains("private-credential") && !recorded.Contains("Bearer"), "secrets under category/stage/IDs/provider fields absent");
var records = recorded.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line)).ToArray();
Check(records.Any(d => d.RootElement.GetProperty("category").GetString() == "OPERATION" && d.RootElement.GetProperty("eventName").GetString() == "stage" && d.RootElement.GetProperty("data").GetProperty("reason").GetString() == "ProviderOutcomeUnknown"), "uncertain reason is explicit");
Check(records.Where(d => d.RootElement.GetProperty("category").GetString() == "OPENAI_HTTP").All(d => d.RootElement.GetProperty("data").GetProperty("operationId").ValueKind == JsonValueKind.String), "HTTP records carry operation correlation");
Check(recorded.Contains(correlated) && recorded.Contains("invalid_value") && recorded.Contains("invalid_request_error"), "safe provider code/type/param/client correlation retained");
Check(records.Any(d => d.RootElement.GetProperty("category").GetString() == "FATAL_STARTUP"), "fatal category retained");
Check(recorded.Contains("main_window_shown"), "startup window stage retained");
Check(StartupDiagnostics.Health.CleanupFailures == 100, "cleanup failures counted without message/path exposure");
Check(records.Count(d => d.RootElement.GetProperty("eventName").GetString() == "stage" && d.RootElement.GetProperty("data").TryGetProperty("stage", out var stage) && stage.GetString() == "CleanupFailed") < 10, "cleanup samples sparse and bounded");
Check(records.Any(d => d.RootElement.GetProperty("category").GetString() == "OPERATION" && d.RootElement.GetProperty("eventName").GetString() == "stage" && d.RootElement.GetProperty("data").GetProperty("stage").GetString() == "Invalidated" && d.RootElement.GetProperty("data").GetProperty("reason").GetString() == "Corrupt"), "corrupt checkpoint explicitly invalidated");
Check(records.Any(d => d.RootElement.GetProperty("category").GetString() == "OPERATION" && d.RootElement.GetProperty("eventName").GetString() == "stage" && d.RootElement.GetProperty("data").GetProperty("stage").GetString() == "Reused"), "accepted checkpoint reuse is durable");
Check(records.Any(d => d.RootElement.GetProperty("category").GetString() == "OPERATION" && d.RootElement.GetProperty("eventName").GetString() == "stage" && d.RootElement.GetProperty("data").GetProperty("stage").GetString() == "Rollback"), "export rollback correlated");
Check(records.Any(d => d.RootElement.GetProperty("category").GetString() == "OPERATION" && d.RootElement.GetProperty("data").TryGetProperty("parentOperationId", out var parent) && parent.ValueKind == JsonValueKind.String), "nested storage/auth scopes preserve parent correlation");
foreach (var d in records) d.Dispose();

// A failed local logging sink must not convert accepted paid work into an error/replay.
var writerField = typeof(StartupDiagnostics).GetField("_writer", BindingFlags.NonPublic | BindingFlags.Static)!;
((IDisposable)writerField.GetValue(null)!).Dispose();
var accepted = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"text\":\"accepted synthetic result\"}") }));
using (var client = new OpenAiTranscriptionClient(accepted)) {
    var result = await client.TranscribeAsync("local-mock-key", "gpt-transcribe", audio, "", null, null, 5, null, CancellationToken.None);
    Check(result.Text == "accepted synthetic result" && accepted.Calls == 1, "sink failure cannot discard provider success or cause replay");
}
var failedSinkHandler=new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent("{\"error\":{\"message\":\"Organization verification is required\"}} ")}));
using(var failedSinkClient=new OpenAiTranscriptionClient(failedSinkHandler)) { Check(await Failure(()=>Call(failedSinkClient),"failed logger still surfaces provider rejection") is OpenAiApiException && failedSinkHandler.Calls==1,"provider evidence logger failure cannot change rejection or replay"); }
for (var i = 0; i < 200; i++) StartupDiagnostics.Write("ACTIVITY", secret);
var health = StartupDiagnostics.Health;
Check(health.SinkUnavailable && health.SinkFailures == 1, "sink disabled after first failure, no repeated filesystem retry");
Check(health.RecentFallbackEvents.Count <= 32 && health.FallbackEventsDropped > 0, "fallback memory and incident incompleteness bounded/counted");
Check(!JsonSerializer.Serialize(health).Contains(secret), "fallback never stores payload or exception messages");
StartupDiagnostics.Shutdown(0);
Console.WriteLine($"SAFETY_ASSERTIONS={passed}; fake HTTP only, no provider/profile access");

static string ReadShared(string path) {
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream); return reader.ReadToEnd();
}

sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler {
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        if (request.RequestUri?.Host != "api.openai.com") throw new Exception("Unexpected fake target");
        Calls++; return respond(request, token);
    }
}
sealed class InterruptedContent : HttpContent {
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new IOException("PRIVATE_BODY_READ");
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new InterruptedStream());
}
sealed class InterruptedStream : MemoryStream {
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => throw new IOException("PRIVATE_BODY_READ");
}



