using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CosmicTranscriber.Utilities;

int assertions=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAILED: "+name);assertions++;Console.WriteLine("PASS "+name);}
var root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
var rotation=Path.Combine(root,"rotation");Directory.CreateDirectory(rotation);
var legacy=Path.Combine(rotation,"CosmicTranscriber-legacy.log");File.WriteAllText(legacy,"preserved evidence");
var unknown=Path.Combine(rotation,"CosmicDiag-v1-not-a-guid.jsonl");File.WriteAllText(unknown,"preserved unknown");
var active=new DiagnosticLogWriter(rotation,Guid.NewGuid(),512,3);active.Write("TEST","active",null);var activePath=active.Path;
var session=Guid.NewGuid();
using(var writer=new DiagnosticLogWriter(rotation,session,512,3)){
    for(int i=0;i<40;i++)writer.Write("TEST","volume",new{count=i});
    Check(Directory.GetFiles(rotation,"CosmicDiag-v1-*.jsonl").Where(p=>p!=unknown).All(p=>new FileInfo(p).Length<=512),"encoded-byte cap including newline");
    Check(File.Exists(activePath),"active log handle preserved during retention");
    var shareDenied=false;try{using var bad=new FileStream(writer.Path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite);}catch(IOException){shareDenied=true;}
    Check(shareDenied,"exclusive writer handle denies another writer");
}
active.Dispose();using(var final=new DiagnosticLogWriter(rotation,Guid.NewGuid(),512,3)){final.Write("TEST","final",null);}
Check(File.ReadAllText(legacy)=="preserved evidence","legacy logs untouched");Check(File.ReadAllText(unknown)=="preserved unknown","unknown-prefix file untouched");
Check(Directory.GetFiles(rotation,"CosmicDiag-v1-*.jsonl").Count(p=>p!=unknown)<=3,"closed owned retention bound");
var retained=Directory.GetFiles(rotation,"CosmicDiag-v1-*.jsonl").Where(p=>p!=unknown).SelectMany(File.ReadLines).Where(l=>l.Length>0).Select(l=>JsonDocument.Parse(l)).ToArray();
Check(retained.All(d=>d.RootElement.GetProperty("schemaVersion").GetInt32()==1),"every retained line valid schema JSON");
var sameSession=retained.Where(d=>d.RootElement.GetProperty("sessionId").GetString()==session.ToString("N")).Select(d=>d.RootElement.GetProperty("sequence").GetInt64()).OrderBy(n=>n).ToArray();
Check(sameSession.Length>0 && sameSession.Distinct().Count()==sameSession.Length && sameSession[0]>1,"session sequence survives rollover");
foreach(var d in retained)d.Dispose();
const string secret="PRIVATE_TRANSCRIPT_AUDIO_DOCUMENT_SENTINEL sk-private-test Bearer private-token-987654321";
StartupDiagnostics.Initialize();var log=StartupDiagnostics.LogPath;
StartupDiagnostics.Write("ACTIVITY",secret);StartupDiagnostics.Write("MAINWINDOW",secret);
StartupDiagnostics.WriteException("TEST",new HttpRequestException(secret,new InvalidDataException(secret),HttpStatusCode.Forbidden));
var client="ct-"+Guid.NewGuid().ToString("N");StartupDiagnostics.WriteHttpResponse("audio-transcription",client,200,secret,12.4,"34");
StartupDiagnostics.WriteHttpResponse(secret,secret,999,secret,double.NaN,secret);
StartupDiagnostics.WriteWindow("test",true,true,"Normal",double.NaN,double.PositiveInfinity,144,144);
StartupDiagnostics.WriteFocus(new string('a',10000),"TextBox");
var themeOperation=Guid.NewGuid();
StartupDiagnostics.WriteThemeTransition(themeOperation,"requested","Cosmic Paper","Cosmic Night",3,1,0);
StartupDiagnostics.WriteThemeTransition(themeOperation,"applied","Cosmic Paper","Cosmic Night",3,1,4.1259);
StartupDiagnostics.WriteThemeTransition(themeOperation,"failed","Cosmic Paper","Cosmic Night",3,1,5,new InvalidDataException(secret));
StartupDiagnostics.WriteThemeTransition(Guid.Empty,secret,secret,secret,int.MaxValue,int.MinValue,double.PositiveInfinity,new Exception(secret));
Parallel.For(0,80,_=>StartupDiagnostics.WriteLifecycle("concurrent_event"));
StartupDiagnostics.WriteException("NATIVE_TEST",new System.ComponentModel.Win32Exception(5));
StartupDiagnostics.WriteException("SOCKET_TEST",new System.Net.Sockets.SocketException(10061));
StartupDiagnostics.WriteViewportFocus(new FocusViewportTrace("before", "OpenDiagnosticLogsButton", "SettingsViewport", 7, true) { TargetTop=double.NaN, TargetHeight=38, ViewportHeight=400, OffsetY=200, ExpansionFactorResolved=true, ExpansionFactor=.25, ObservationElapsedMs=double.PositiveInfinity, StabilityAgeMs=80 });
StartupDiagnostics.Shutdown(0);
var text=File.ReadAllText(log);
Check(!text.Contains("PRIVATE_")&&!text.Contains("sk-private")&&!text.Contains("Bearer")&&!text.Contains("private-token"),"no raw message/header/exception/user content");
var records=File.ReadLines(log).Select(l=>JsonDocument.Parse(l)).ToArray();
Check(records[0].RootElement.GetProperty("data").GetProperty("assemblySha256").GetString()!.Length==64,"build hash logged");
Check(records[0].RootElement.GetProperty("data").TryGetProperty("mvid",out _) && records[0].RootElement.GetProperty("data").TryGetProperty("runtime",out _),"runtime and build identity logged");
var errors=records.Single(d=>d.RootElement.GetProperty("category").GetString()=="TEST").RootElement.GetProperty("data").GetProperty("errors");
Check(errors[0].GetProperty("httpStatus").GetInt32()==403 && errors.GetArrayLength()==2,"exception type/status and bounded chain preserved");
var response=records.First(d=>d.RootElement.GetProperty("category").GetString()=="OPENAI_HTTP").RootElement.GetProperty("data");
Check(response.GetProperty("clientRequestId").GetString()==client && response.GetProperty("processingMs").GetInt64()==34,"typed request/timing metadata");
Check(response.GetProperty("serverRequestIdSha256").GetString()==Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant(),"untrusted response identifier hashed");
Check(records.Single(d=>d.RootElement.GetProperty("category").GetString()=="UI_FOCUS").RootElement.GetProperty("data").GetProperty("controlId").GetString()=="other","unlisted focus identifier omitted");
var sequences=records.Select(d=>d.RootElement.GetProperty("sequence").GetInt64()).ToArray();Check(sequences.SequenceEqual(Enumerable.Range(1,sequences.Length).Select(n=>(long)n)),"concurrent writes retain monotonic sequence");
Check(records[^1].RootElement.GetProperty("eventName").GetString()=="normal_exit","normal exit recorded and flushed");
using(var reopened=new FileStream(log,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){Check(true,"shutdown releases handle");}
Check(records.Single(d=>d.RootElement.GetProperty("category").GetString()=="NATIVE_TEST").RootElement.GetProperty("data").GetProperty("errors")[0].GetProperty("nativeError").GetInt32()==5,"native error code retained");
Check(records.Single(d=>d.RootElement.GetProperty("category").GetString()=="SOCKET_TEST").RootElement.GetProperty("data").GetProperty("errors")[0].GetProperty("socketError").GetInt32()==10061,"socket error code retained");
var visibility=records.Single(d=>d.RootElement.GetProperty("category").GetString()=="UI_VISIBILITY").RootElement.GetProperty("data");
Check(visibility.GetProperty("targetTop").ValueKind==JsonValueKind.Null && visibility.GetProperty("targetHeight").GetDouble()==38 && visibility.GetProperty("eventHandled").GetBoolean(),"bounded numeric visibility diagnostics, no content");
Check(visibility.GetProperty("expansionFactorResolved").GetBoolean() && visibility.GetProperty("expansionFactor").GetDouble()==.25 && visibility.GetProperty("observationElapsedMs").ValueKind==JsonValueKind.Null && visibility.GetProperty("stabilityAgeMs").GetDouble()==80,"animation/stability diagnostics numeric with nonfinite omitted");
var themeEvents=records.Where(d=>d.RootElement.GetProperty("category").GetString()=="THEME").Select(d=>d.RootElement).ToArray();
Check(themeEvents.Take(3).All(d=>d.GetProperty("data").GetProperty("operationId").GetString()==themeOperation.ToString("N")) && themeEvents.Take(3).Select(d=>d.GetProperty("eventName").GetString()).SequenceEqual(new[]{"requested","applied","failed"}),"Theme operation ID correlates exact stages");
var appliedTheme=themeEvents[1].GetProperty("data");
Check(appliedTheme.GetProperty("requestedTheme").GetString()=="paper" && appliedTheme.GetProperty("previousTheme").GetString()=="night" && appliedTheme.GetProperty("dictionaryCount").GetInt32()==3 && appliedTheme.GetProperty("paletteIndex").GetInt32()==1 && appliedTheme.GetProperty("elapsedMs").GetDouble()==4.126,"Theme allowlisted palette state and timing retained");
Check(themeEvents[2].GetProperty("data").GetProperty("failureType").GetString()=="System.IO.InvalidDataException" && themeEvents[2].GetProperty("data").GetProperty("hresult").GetString()!.StartsWith("0x"),"Theme failure type and HRESULT without exception content");
var untrustedTheme=themeEvents[3].GetProperty("data");
Check(themeEvents[3].GetProperty("eventName").GetString()=="other" && untrustedTheme.GetProperty("requestedTheme").GetString()=="other" && untrustedTheme.GetProperty("previousTheme").GetString()=="other" && untrustedTheme.GetProperty("dictionaryCount").ValueKind==JsonValueKind.Null && untrustedTheme.GetProperty("paletteIndex").ValueKind==JsonValueKind.Null && untrustedTheme.GetProperty("elapsedMs").ValueKind==JsonValueKind.Null,"Untrusted Theme names/stages and invalid counts/timing omitted");
foreach(var d in records)d.Dispose();Console.WriteLine($"DIAGNOSTICS_ASSERTIONS={assertions}");



