using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using CosmicTranscriber.Models;
using CosmicTranscriber.Services;
using CosmicTranscriber.Utilities;

// Synthetic tone + fake handler only. Never constructs normal profile/key services.
var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
var audio = Path.GetFullPath(args[1]); var metadata = Mp3MetadataService.Inspect(audio);
if (metadata.Duration >= TimeSpan.FromMinutes(3)) throw new Exception("Use a dedicated short synthetic fixture for one-chunk scenarios");
var item = new AudioJobItem { SourcePath = audio, FileName = "synthetic.mp3", SizeBytes = metadata.SizeBytes,
    Duration = metadata.Duration, LastWriteTimeUtc = metadata.LastWriteTimeUtc, SourceSha256 = metadata.Sha256 };
var request = new TranscriptionRequest { ApiKey = "local-mock-key", Model = TranscriptionModelCatalog.DefaultFileModel,
    Prompt = "", UsePreviousContext = true, MaximumChunkMinutes = 30, ChunkMegabytes = 20 };
var type = typeof(TranscriptionCoordinator);
var resolve = type.GetMethod("ResolveWorkDirectory", BindingFlags.NonPublic | BindingFlags.Static)!;
var write = type.GetMethod("WriteCheckpointAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
var passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); passed++; Console.WriteLine("PASS " + name); }
async Task<Exception> Reject(Func<Task> call, string name) { try { await call(); } catch (Exception e) { Check(true, name); return e; } throw new Exception("Expected failure " + name); }
(string Output, string Work, string Transcript, string Checkpoint, string Pending) Context(string name) {
    var output = Path.Combine(root, name);
    var work = (string)resolve.Invoke(null, new object?[] { item, output, request })!;
    var transcripts = Path.Combine(work, "transcripts"); Directory.CreateDirectory(transcripts);
    return (output, work, transcripts, Path.Combine(transcripts, "chunk-001.json"), PendingTranscriptionLedger.PathFor(transcripts, 1));
}
PendingTranscriptionIntent Intent(string path) => PendingTranscriptionLedger.Create(path, 1, "ct-" + Guid.NewGuid().ToString("N"), CancellationToken.None);
Task Save(string path, PendingTranscriptionIntent? intent) => (Task)write.Invoke(null, new object?[] { path, 1, "accepted synthetic transcript", Array.Empty<SpeakerSegment>(), CancellationToken.None, intent })!;
Task<FileTranscriptionResult> Run(string output, Handler handler, CancellationToken token = default) => RunCore(output, handler, token);
async Task<FileTranscriptionResult> RunCore(string output, Handler handler, CancellationToken token) {
    using var client = new OpenAiTranscriptionClient(handler);
    return await new TranscriptionCoordinator(new Mp3Chunker(), client).ProcessFileAsync(item, output, request, null, null, token);
}

{
    var c = Context("before-send"); var intent = Intent(c.Pending); var before = File.ReadAllBytes(c.Pending); var h = new Handler();
    var oldManifest = Path.Combine(c.Work, "manifest.json"); File.WriteAllText(oldManifest, "synthetic original manifest evidence");
    Check(await Reject(() => Run(c.Output, h), "intent before send blocks restarted coordinator") is PendingTranscriptionException, "unresolved intent is explicit systemic failure");
    Check(h.Calls == 0 && File.ReadAllBytes(c.Pending).SequenceEqual(before), "crash before dispatch preserves evidence and sends nothing");
    Check(File.ReadAllText(oldManifest) == "synthetic original manifest evidence", "blocked restart preserves original manifest evidence");
    Check(!Encoding.UTF8.GetString(before).Contains("local-mock-key") && !Encoding.UTF8.GetString(before).Contains("transcript"), "intent contains no credential/transcript payload");
    var json = JsonNode.Parse(before)!; json["CreatedUtc"] = "1900-01-01T00:00:00+00:00"; File.WriteAllText(c.Pending, json.ToJsonString());
    var second = new Handler(); await Reject(() => Run(c.Output, second), "old intent is not age-cleared"); Check(second.Calls == 0, "age never authorizes replay");
}
foreach (var partial in new[] { "", "{partial", "{}" }) {
    var c = Context("partial-" + passed); File.WriteAllText(c.Pending, partial); var h = new Handler();
    await Reject(() => Run(c.Output, h), "partial/corrupt intent blocks"); Check(h.Calls == 0 && File.ReadAllText(c.Pending) == partial, "partial intent retained, no send");
}
{
    var c = Context("during-upload"); var h = new Handler { FailUpload = true };
    Check(await Reject(() => Run(c.Output, h), "upload failure") is PendingTranscriptionException && h.Calls == 1, "uncertain dispatch leaves systemic pending intent");
    var before = File.ReadAllBytes(c.Pending); var retry = new Handler();
    await Reject(() => Run(c.Output, retry), "restart after upload failure"); Check(retry.Calls == 0 && before.SequenceEqual(File.ReadAllBytes(c.Pending)), "no replay after uncertain upload");
    Check(h.ClientId == PendingTranscriptionLedger.Read(c.Pending, 1)!.ClientRequestId, "intent ID is exact actual dispatched client ID");
}
{
    var c = Context("checkpoint-write-failure"); FileStream? blocker = null;
    var h = new Handler { OnResponse = () => blocker = new FileStream(c.Checkpoint, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None) };
    await Reject(() => Run(c.Output, h), "accepted response then checkpoint write failure"); blocker!.Dispose();
    Check(h.Calls == 1 && File.Exists(c.Pending), "accepted work with failed checkpoint retains pending");
    var before = File.ReadAllBytes(c.Checkpoint); var retry = new Handler(); await Reject(() => Run(c.Output, retry), "restart with incomplete checkpoint");
    Check(retry.Calls == 0 && File.ReadAllBytes(c.Checkpoint).SequenceEqual(before), "pending prevents deletion of invalid response evidence or replay");
}
{
    var c = Context("durable-response-before-reconcile"); var intent = Intent(c.Pending); await Save(c.Checkpoint, intent);
    var h = new Handler(); var result = await Run(c.Output, h);
    Check(h.Calls == 0 && result.Chunks.All(x => x.ReusedFromCheckpoint), "restart after durable response reconciles with zero HTTP calls");
    Check(!File.Exists(c.Pending), "matching accepted checkpoint clears only its own intent");
}
{
    var c = Context("stale-accepted-mismatch"); var old = Intent(c.Pending); await Save(c.Checkpoint, old);
    PendingTranscriptionLedger.ReconcileValidatedCheckpoint(c.Pending, old, old.AttemptId, old.ClientRequestId);
    var fresh = Intent(c.Pending); var before = File.ReadAllBytes(c.Checkpoint); var h = new Handler();
    await Reject(() => Run(c.Output, h), "older valid checkpoint cannot clear new pending");
    Check(h.Calls == 0 && PendingTranscriptionLedger.Read(c.Pending, 1) == fresh && before.SequenceEqual(File.ReadAllBytes(c.Checkpoint)), "attempt mismatch retains both old accepted response and new uncertainty");
}
{
    var c = Context("legacy-mismatch"); await Save(c.Checkpoint, null); _ = Intent(c.Pending); var h = new Handler();
    await Reject(() => Run(c.Output, h), "legacy checkpoint cannot corroborate intent"); Check(h.Calls == 0 && File.Exists(c.Pending), "legacy never clears pending");
    var legacy = Context("legacy-reuse"); await Save(legacy.Checkpoint, null); var reuse = new Handler(); var result = await Run(legacy.Output, reuse);
    Check(reuse.Calls == 0 && result.Chunks.Single().ReusedFromCheckpoint, "legacy checkpoints still reuse normally without pending evidence");
}
{
    var c = Context("tampered-attempt"); var intent = Intent(c.Pending); await Save(c.Checkpoint, intent);
    var json = JsonNode.Parse(File.ReadAllText(c.Checkpoint))!; var other = Guid.NewGuid(); json["AttemptId"] = other.ToString(); json["ClientRequestId"] = "ct-" + other.ToString("N");
    File.WriteAllText(c.Checkpoint, json.ToJsonString()); var h = new Handler(); await Reject(() => Run(c.Output, h), "checkpoint identity checksum tamper");
    Check(h.Calls == 0 && File.Exists(c.Pending) && File.Exists(c.Checkpoint), "attempt metadata is checksum-bound; evidence retained");
}
{
    var c = Context("read-delete-denied"); var intent = Intent(c.Pending); await Save(c.Checkpoint, intent);
    using (var locked = new FileStream(c.Pending, FileMode.Open, FileAccess.Read, FileShare.None)) {
        var h = new Handler(); await Reject(() => Run(c.Output, h), "unreadable intent"); Check(h.Calls == 0, "unreadable is not absent");
    }
    using (var locked = new FileStream(c.Pending, FileMode.Open, FileAccess.Read, FileShare.Read)) {
        await Reject(() => Task.Run(() => PendingTranscriptionLedger.ReconcileValidatedCheckpoint(c.Pending, intent, intent.AttemptId, intent.ClientRequestId)), "intent deletion failure");
        Check(File.Exists(c.Pending), "failed reconciliation deletion preserves blocking marker");
    }
}
{
    var c = Context("competing-intents"); var won = 0; var blocked = 0;
    await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => { try { Intent(c.Pending); Interlocked.Increment(ref won); } catch (PendingTranscriptionException) { Interlocked.Increment(ref blocked); } })));
    Check(won == 1 && blocked == 1, "CreateNew arbitrates competing dispatch reservations");
}
{
    var c = Context("clear-preflight"); await Save(c.Checkpoint, null); var secondWork = Path.Combine(Path.GetDirectoryName(c.Work)!, new string('A', 32));
    var secondTranscripts = Path.Combine(secondWork, "transcripts"); Directory.CreateDirectory(secondTranscripts); var pending = PendingTranscriptionLedger.PathFor(secondTranscripts, 1); Intent(pending);
    await Reject(() => CheckpointCacheService.ClearAsync(c.Output, CancellationToken.None), "cache clear refuses any pending before deleting anything");
    Check(Directory.Exists(c.Work) && Directory.Exists(secondWork) && File.Exists(c.Checkpoint) && File.Exists(pending), "all cache work preserved across pending clear refusal");
    using (var lease = CheckpointCacheService.AcquireUsage(c.Output)) await Reject(() => CheckpointCacheService.ClearAsync(c.Output, CancellationToken.None), "active cache lease excludes clearing");
}
{
    var c = Context("accepted-then-user-cancel"); using var stop = new CancellationTokenSource();
    var h = new Handler { StopOnDispose = stop };
    Check(await Reject(() => Run(c.Output, h, stop.Token), "user cancellation just after accepted body") is OperationCanceledException, "user cancellation propagated after durable storage");
    Check(h.Calls == 1 && File.Exists(c.Checkpoint) && !File.Exists(c.Pending), "storage-only token preserves accepted response and reconciles before cancellation");
    var next = new Handler(); var result = await Run(c.Output, next); Check(next.Calls == 0 && result.Chunks.Single().ReusedFromCheckpoint, "cancelled run resumes accepted transcript with zero new requests");
}
{
    var c = Context("callback-failure"); var h = new Handler(); using var client = new OpenAiTranscriptionClient(h);
    await Reject(() => client.TranscribeAsync("local-mock-key", request.Model, audio, "", null, null, 5, null, CancellationToken.None,
        (id, token) => { PendingTranscriptionLedger.Create(c.Pending, 1, id, token); throw new IOException("synthetic interruption before SendAsync"); }), "durable callback then pre-send interruption");
    Check(h.Calls == 0 && File.Exists(c.Pending), "awaited callback failure aborts send and retains conservative false-positive intent");
}
{
    var longAudio = Path.GetFullPath(args[2]); var m = Mp3MetadataService.Inspect(longAudio);
    var longItem = new AudioJobItem { SourcePath = longAudio, FileName = "synthetic-long.mp3", SizeBytes = m.SizeBytes, Duration = m.Duration, LastWriteTimeUtc = m.LastWriteTimeUtc, SourceSha256 = m.Sha256 };
    var output = Path.Combine(root, "later-pending-blocks-earlier-new-work");
    var work = (string)resolve.Invoke(null, new object?[] { longItem, output, request })!;
    var transcripts = Path.Combine(work, "transcripts"); Directory.CreateDirectory(transcripts);
    var later = PendingTranscriptionLedger.PathFor(transcripts, 2);
    PendingTranscriptionLedger.Create(later, 2, "ct-" + Guid.NewGuid().ToString("N"), CancellationToken.None);
    var h = new Handler(); using var client = new OpenAiTranscriptionClient(h);
    await Reject(() => new TranscriptionCoordinator(new Mp3Chunker(), client).ProcessFileAsync(longItem, output, request, null, null, CancellationToken.None), "later pending chunk blocks whole-context new dispatch");
    Check(h.Calls == 0 && File.Exists(later), "all markers preflight before earlier unpaid chunk1 can dispatch");
}
{
    var c = Context("diagnostic-sink-unavailable");
    var field = typeof(StartupDiagnostics).GetField("_writer", BindingFlags.NonPublic | BindingFlags.Static)!;
    ((IDisposable?)field.GetValue(null))?.Dispose();
    var h = new Handler(); var result = await Run(c.Output, h);
    Check(h.Calls == 1 && result.Chunks.Count == 1 && File.Exists(c.Checkpoint) && !File.Exists(c.Pending), "diagnostic sink failure cannot discard accepted response or ledger reconciliation");
    var next = new Handler(); var reused = await Run(c.Output, next);
    Check(next.Calls == 0 && reused.Chunks.Single().ReusedFromCheckpoint, "recovery remains safe without a working diagnostic sink");
}
StartupDiagnostics.Shutdown(0);
Console.WriteLine($"PENDING_RECOVERY_ASSERTIONS={passed}; fake HTTP only, no profile/credential/foreground");

sealed class Handler : HttpMessageHandler {
    public int Calls; public string? ClientId; public bool FailUpload; public Action? OnResponse; public CancellationTokenSource? StopOnDispose;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        if (request.RequestUri?.Host != "api.openai.com") throw new Exception("Unexpected fake target");
        Calls++; ClientId = request.Headers.GetValues("X-Client-Request-Id").Single();
        if (FailUpload) throw new HttpRequestException(HttpRequestError.ResponseEnded, "synthetic uncertain upload");
        OnResponse?.Invoke();
        HttpContent content = StopOnDispose is null ? new StringContent("{\"text\":\"accepted synthetic transcript\"}") : new CancelOnDispose(StopOnDispose);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
sealed class CancelOnDispose(CancellationTokenSource stop) : ByteArrayContent(Encoding.UTF8.GetBytes("{\"text\":\"accepted synthetic transcript\"}")) {
    protected override void Dispose(bool disposing) { if (disposing) stop.Cancel(); base.Dispose(disposing); }
}
