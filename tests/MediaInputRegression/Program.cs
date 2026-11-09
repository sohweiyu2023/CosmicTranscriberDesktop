using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CosmicTranscriber.Models;
using CosmicTranscriber.Services;
using CosmicTranscriber.Utilities;

var fixtures = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output); int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); passed++; Console.WriteLine("PASS " + name); }
async Task Fail(Func<Task> action, Func<Exception,bool> predicate, string name) { try { await action(); } catch (Exception e) when (predicate(e)) { Check(true,name); return; } throw new Exception("FAIL expected rejection: " + name); }
string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
var before = Directory.GetFiles(fixtures).ToDictionary(p=>p,Hash);
var service = new AudioInputService();
string[] names = ["tone.wav","tone.flac","tone.m4a","lossless.m4a","tone.aac","tone.ogg","tone.opus","tone.webm","tone.mp4","tone.mkv","tone.avi","tone.wma","tone.wmv","tone.aiff","tone.caf","tone.ac3","tone.mpg","tone.ts","tone.m4b","tone.wv","tone.tta","tone.w64","tone.mp3","unusual.extension","misnamed.mp3","tone.mov","tone.3gp","tone.au","video-audio.mp4","video-audio.avi","video-audio.mkv"];
var rows = new List<object>();
if (args.Length > 2 && args[2] == "m4a-only") names = ["tone.m4a"];
foreach(var name in names) {
    var path=Path.Combine(fixtures,name); var inspection=await service.InspectAsync(path,CancellationToken.None);
    Check(inspection.Metadata.Duration.TotalSeconds is > 1 and < 4,"probe audio: "+name);
    var handler = new FakeHandler(); using var client=Mock(handler);
    var m=inspection.Metadata; var item=new AudioJobItem { SourcePath=path,FileName=name,SizeBytes=m.SizeBytes,Duration=m.Duration,LastWriteTimeUtc=m.LastWriteTimeUtc,SourceSha256=m.Sha256,RequiresConversion=inspection.RequiresConversion };
    var destination=Path.Combine(output,Path.GetFileNameWithoutExtension(name)+"-"+Guid.NewGuid().ToString("N"));
    var coordinator=new TranscriptionCoordinator(new Mp3Chunker(),client,service);
    var request=new TranscriptionRequest { ApiKey="local-mock-key", Model=TranscriptionModelCatalog.DefaultFileModel,Prompt="",UsePreviousContext=false };
    var result=await coordinator.ProcessFileAsync(item,destination,request,null,null,CancellationToken.None);
    Check(handler.Calls==1&&handler.ValidMp3&&result.Chunks.Count==1,"decoded/converted and mocked submitted valid MP3: "+name);
    Check(result.SourcePath==path,"original source identity retained: "+name);
    var again=await coordinator.ProcessFileAsync(item,destination,request,null,null,CancellationToken.None);
    Check(handler.Calls==1&&again.Chunks.All(c=>c.ReusedFromCheckpoint),"repeat reuses accepted checkpoint without dispatch: "+name);
    Check(!Directory.EnumerateFiles(destination,"*.mp3",SearchOption.AllDirectories).Any(),"prepared audio cleaned: "+name);
    rows.Add(new{file=name,inspection.RequiresConversion,decodedAndMockSubmitted=true,acceptedCheckpointReused=true});
    if(name=="tone.m4a") {
        var drifted=new AudioJobItem { SourcePath=path,FileName=name,SizeBytes=m.SizeBytes,Duration=m.Duration+TimeSpan.FromTicks(1),LastWriteTimeUtc=m.LastWriteTimeUtc,SourceSha256=m.Sha256,RequiresConversion=true };
        var driftResult=await coordinator.ProcessFileAsync(drifted,destination,request,null,null,CancellationToken.None);
        Check(handler.Calls==1&&driftResult.Chunks.All(x=>x.ReusedFromCheckpoint),"probe-duration rounding drift cannot bypass accepted normalized audio identity");
        var manifest=Directory.GetFiles(destination,"manifest.json",SearchOption.AllDirectories).Single();
        var original=File.ReadAllText(manifest); using var doc=JsonDocument.Parse(original);
        var digest=doc.RootElement.GetProperty("prepared_audio_sha256").GetString()!;
        File.WriteAllText(manifest,original.Replace(digest,new string('0',64)));
        await Fail(()=>coordinator.ProcessFileAsync(item,destination,request,null,null,CancellationToken.None),e=>e is IOException,"changed prepared identity blocks paid replay rather than reusing mismatched boundaries");
        Check(handler.Calls==1,"identity mismatch sends no new provider request");
        File.WriteAllText(manifest,original);
        var uncertainHandler=new FakeHandler{Uncertain=true};using var uncertainClient=Mock(uncertainHandler);
        var uncertainCoordinator=new TranscriptionCoordinator(new Mp3Chunker(),uncertainClient,service);var uncertainOutput=destination+"-unknown";
        await Fail(()=>uncertainCoordinator.ProcessFileAsync(item,uncertainOutput,request,null,null,CancellationToken.None),e=>e is PendingTranscriptionException,"unknown converted request leaves durable intent");
        var intent=Directory.GetFiles(uncertainOutput,"*.pending",SearchOption.AllDirectories).Single();var intentBytes=File.ReadAllBytes(intent);
        await Fail(()=>uncertainCoordinator.ProcessFileAsync(drifted,uncertainOutput,request,null,null,CancellationToken.None),e=>e is PendingTranscriptionException,"probe rounding drift cannot bypass unresolved converted request");
        Check(uncertainHandler.Calls==1&&File.ReadAllBytes(intent).SequenceEqual(intentBytes),"duration drift preserves uncertainty and sends no second request");
    }
}
var missing=new AudioInputService(Path.Combine(output,"missing-ffmpeg.exe"),Path.Combine(output,"missing-ffprobe.exe"));
var mp3=await missing.InspectAsync(Path.Combine(fixtures,"tone.mp3"),CancellationToken.None);
Check(!mp3.RequiresConversion,"legacy MP3 remains independent of FFmpeg");
await Fail(()=>missing.InspectAsync(Path.Combine(fixtures,"tone.m4a"),CancellationToken.None),e=>e is AudioInputException {Failure:AudioInputFailure.ConverterUnavailable},"missing converter reports setup requirement");
await Fail(()=>service.InspectAsync(Path.Combine(fixtures,"video-only.mp4"),CancellationToken.None),e=>e is AudioInputException {Failure:AudioInputFailure.NoAudioStream},"video-only file has specific no-audio diagnostic");
await Fail(()=>service.InspectAsync(Path.Combine(fixtures,"corrupt.m4a"),CancellationToken.None),e=>e is AudioInputException {Failure:AudioInputFailure.UnsupportedOrCorrupt},"corrupt container rejected before provider dispatch");
await Fail(()=>service.InspectAsync(Path.Combine(fixtures,"playlist.m4a"),CancellationToken.None),e=>e is AudioInputException {Failure:AudioInputFailure.UnsupportedOrCorrupt},"disguised remote playlist excluded by demuxer/protocol policy");
var special=Directory.GetFiles(fixtures,"space*.m4a").Single();
Check((await service.InspectAsync(special,CancellationToken.None)).RequiresConversion,"spaces shell metacharacters and Unicode handled as literal path");
using(var stop=new CancellationTokenSource()) { stop.Cancel(); await Fail(()=>service.ConvertAsync(Path.Combine(fixtures,"tone.m4a"),Path.Combine(output,"cancelled.mp3"),stop.Token),e=>e is OperationCanceledException,"pre-cancel conversion does not start tool"); }
var longPath=Path.Combine(fixtures,"long.m4a");var longInspection=await service.InspectAsync(longPath,CancellationToken.None);var lm=longInspection.Metadata;
var longItem=new AudioJobItem { SourcePath=longPath,FileName="long.m4a",SizeBytes=lm.SizeBytes,Duration=lm.Duration,LastWriteTimeUtc=lm.LastWriteTimeUtc,SourceSha256=lm.Sha256,RequiresConversion=true };
var longHandler=new FakeHandler();using var longClient=Mock(longHandler);var longCoordinator=new TranscriptionCoordinator(new Mp3Chunker(),longClient,service);
var longRequest=new TranscriptionRequest { ApiKey="local-mock-key",Model=TranscriptionModelCatalog.DefaultFileModel,Prompt="",UsePreviousContext=false };
var longOutput=Path.Combine(output,"long");var longResult=await longCoordinator.ProcessFileAsync(longItem,longOutput,longRequest,null,null,CancellationToken.None);
Check(longResult.Chunks.Count==3&&longHandler.Calls==3,"650-second M4A is actually converted then split into three bounded upload chunks");
var longAgain=await longCoordinator.ProcessFileAsync(longItem,longOutput,longRequest,null,null,CancellationToken.None);
Check(longHandler.Calls==3&&longAgain.Chunks.All(x=>x.ReusedFromCheckpoint),"long converted input resumes without replaying provider calls");
var cancelOutput=Path.Combine(output,"cancel-in-conversion");using(var stop=new CancellationTokenSource()) {
    var started=false;
    await Fail(()=>longCoordinator.ProcessFileAsync(longItem,cancelOutput,longRequest,new InlineProgress<CoordinatorProgress>(p=>{if(!started&&p.Stage=="Preparing"){started=true;stop.CancelAfter(350);}}),null,stop.Token),e=>e is OperationCanceledException,"mid-preparation cancellation unwinds owned tool process");
    Check(longHandler.Calls==3&&!Directory.EnumerateFiles(cancelOutput,"*.mp3",SearchOption.AllDirectories).Any(),"cancelled conversion cleans prepared files without provider dispatch");
}
var cache=Path.Combine(fixtures,".cosmic-transcriber-cache"); Directory.CreateDirectory(cache); File.WriteAllText(Path.Combine(cache,"do-not-import.mp3"),"synthetic");
var expanded=AudioInputService.ExpandPaths([fixtures],cache,CancellationToken.None).ToArray();
Check(expanded.Contains(Path.Combine(fixtures,"tone.m4a"))&&expanded.Contains(Path.Combine(fixtures,"unusual.extension"))&&!expanded.Any(p=>p.StartsWith(cache,StringComparison.OrdinalIgnoreCase)),"folder scanner admits formats and unknown extensions consistently, excludes app cache");
Check(AudioInputService.ExpandPaths([Path.Combine(fixtures,"tone.m4a")],null,CancellationToken.None).Single().EndsWith("tone.m4a"),"direct file/drop/paste shared scanner accepts M4A");
Check(before.All(p=>Hash(p.Key)==p.Value),"all original synthetic files remain byte-identical");
var support=Path.Combine(output,"media-support.json");DiagnosticSupportExporter.Export(StartupDiagnostics.LogDirectory,support,CancellationToken.None);
using(var doc=JsonDocument.Parse(File.ReadAllBytes(support))) {
    var records=doc.RootElement.GetProperty("records").EnumerateArray().ToArray();
    Check(records.Any(r=>r.GetProperty("eventName").GetString()=="media_tool_result"&&r.GetProperty("data").GetProperty("tool").GetString()=="Convert"&&r.GetProperty("data").GetProperty("exitCode").GetInt32()==0),"actual persisted safe tool exit/duration/correlation metadata survives support projection");
    Check(records.Any(r=>r.GetProperty("eventName").GetString()=="stage"&&r.GetProperty("data").GetProperty("reason").GetString()=="NoAudioStream"),"no-audio failure reason remains structured, not just a generic message");
    Check(records.Any(r=>r.GetProperty("eventName").GetString()=="media_tool_result"&&r.GetProperty("data").GetProperty("cancelled").GetBoolean()),"actual persisted tool cancellation evidence observed");
}
var classify=typeof(AudioInputService).GetMethod("Classify",BindingFlags.NonPublic|BindingFlags.Static)!;
Check((MediaToolFailure)classify.Invoke(null,["Error opening input C:/encrypted unsupported codec Invalid data.mp4"])! == MediaToolFailure.Unknown,"filenames cannot spoof encryption or codec diagnostic classification");
Check(!File.ReadAllText(support).Contains(fixtures)&&!File.ReadAllText(support).Contains("PRIVATE_")&&!File.ReadAllText(support).Contains("local-mock-key"),"exported tool evidence omits paths, raw stderr and synthetic key/content canaries");
File.WriteAllText(Path.Combine(output,"format-results.json"),JsonSerializer.Serialize(new{tested=rows,assertions=passed,noRealProviderCalls=true,fullNativeJourneyPassed=false},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"MEDIA_INPUT_ASSERTIONS={passed}; TESTED_CONTAINERS={names.Length}; all provider responses in-memory; no app/window/profile");
StartupDiagnostics.Shutdown(0);

static OpenAiTranscriptionClient Mock(FakeHandler handler) {
    var client=new OpenAiTranscriptionClient(); var field=typeof(OpenAiTranscriptionClient).GetField("_httpClient",BindingFlags.Instance|BindingFlags.NonPublic)!;
    ((HttpClient)field.GetValue(client)!).Dispose(); field.SetValue(client,new HttpClient(handler){BaseAddress=new Uri("https://mock.invalid/"),Timeout=Timeout.InfiniteTimeSpan}); return client;
}
sealed class InlineProgress<T>(Action<T> callback) : IProgress<T> {public void Report(T value)=>callback(value);}
sealed class FakeHandler : HttpMessageHandler {
    public int Calls; public bool ValidMp3; public bool Uncertain;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
        Calls++; if(Uncertain) throw new HttpRequestException(HttpRequestError.ResponseEnded,"synthetic unknown response"); var form=(MultipartFormDataContent)request.Content!;
        var audio=form.Single(x=>x.Headers.ContentDisposition?.Name?.Trim('"')=="file");
        var bytes=await audio.ReadAsByteArrayAsync(token); var temporary=Path.Combine(Path.GetTempPath(),"cosmic-synthetic-"+Guid.NewGuid().ToString("N")+".mp3");
        try { await File.WriteAllBytesAsync(temporary,bytes,token); ValidMp3=Mp3MetadataService.Inspect(temporary).Duration>TimeSpan.Zero&&audio.Headers.ContentType?.MediaType=="audio/mpeg"; }
        finally { File.Delete(temporary); }
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"text\":\"Synthetic media transcript.\"}",Encoding.UTF8,"application/json")};
    }
}
