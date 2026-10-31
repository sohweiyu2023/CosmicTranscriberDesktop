using CosmicTranscriber.Utilities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
int passed=0;void Check(bool condition,string name){if(!condition)throw new Exception("FAIL "+name);passed++;Console.WriteLine("PASS "+name);}
const string secret="PRIVATE_AUDIO_TRANSCRIPT_DOCUMENT_SENTINEL sk-fake-secret Bearer cookie-private";
string Folder(string name){var p=Path.Combine(root,name);Directory.CreateDirectory(p);return p;}
string Owned(string folder)=>Path.Combine(folder,$"CosmicDiag-v1-{Guid.NewGuid():N}-{Guid.NewGuid():N}.jsonl");
string Line(string category="OPERATION",string name="stage",object? data=null)=>JsonSerializer.Serialize(new{schemaVersion=1,utc=DateTimeOffset.UtcNow,sessionId="11111111111111111111111111111111",sequence=1,elapsedMs=1,processId=123,category,eventName=name,data=data??new{area="RecoveryRecheck",stage="Failed",reason="ProviderOutcomeUnknown",operationId="22222222222222222222222222222222"}});
JsonDocument Bundle(string path)=>JsonDocument.Parse(File.ReadAllText(path));

var factoryRoot=Folder("factory");
var opened=DiagnosticSinkFactory.Open(new[]{
 (DiagnosticSinkLocation.Executable,(Func<DiagnosticLogWriter>)(()=>throw new IOException(secret))),
 (DiagnosticSinkLocation.Temporary,(Func<DiagnosticLogWriter>)(()=>new DiagnosticLogWriter(factoryRoot,Guid.NewGuid()))) });
Check(opened.Writer is not null&&opened.Location==DiagnosticSinkLocation.Temporary&&opened.Attempts==2&&opened.Failures==1,"startup fallback reports fixed selected class and failed attempts");opened.Writer?.Dispose();
var unavailable=DiagnosticSinkFactory.Open(Enumerable.Range(0,4).Select(_=>(DiagnosticSinkLocation.Executable,(Func<DiagnosticLogWriter>)(()=>throw new UnauthorizedAccessException(secret)))));
Check(unavailable.Writer is null&&unavailable.Attempts==3&&unavailable.Failures==3,"all failed sinks bounded at three startup attempts");
IEnumerable<(DiagnosticSinkLocation,Func<DiagnosticLogWriter>)> Broken(){yield return (DiagnosticSinkLocation.Executable,()=>throw new IOException(secret));throw new IOException(secret);}
var broken=DiagnosticSinkFactory.Open(Broken());Check(broken.Writer is null&&broken.EnumerationFailed&&broken.Failures==1,"unexpected initialization enumeration failure is visible without exception text");

var launcherWriter=new DiagnosticLogWriter(Folder("launcher"),Guid.NewGuid());
var launcher=new LauncherDiagnosticSink(launcherWriter);launcher.Record("process_started",new{childProcessId=1});launcherWriter.Dispose();
launcher.Record("process_exited",new{exitCode=0});for(int i=0;i<40;i++)launcher.Record("process_exited",new{exitCode=0});
Check(launcher.Health.SinkUnavailable&&launcher.Health.WriteFailures==1&&launcher.Health.DroppedRecords==41,"launcher disables failed sink and counts subsequent loss without repeated writes");launcher.Dispose();Check(launcher.Path==launcherWriter.Path,"launcher keeps read-only log location after sink failure without retry");
var noLauncher=new LauncherDiagnosticSink(null);noLauncher.Record("process_started",new{childProcessId=1});Check(noLauncher.Health.SinkUnavailable&&noLauncher.Health.DroppedRecords==1,"launcher reports unavailable initial sink and dropped evidence");

StartupDiagnostics.Initialize();var initialCleanup=StartupDiagnostics.Health.CleanupFailures;
var target=Path.Combine(Folder("atomic"),"settings-synthetic.json");
SafeFileSystem.WriteAllBytesDurableAtomic(target,Encoding.UTF8.GetBytes("synthetic"),"synthetic settings",_=>throw new IOException(secret));
Check(File.ReadAllText(target)=="synthetic"&&StartupDiagnostics.Health.CleanupFailures==initialCleanup+1,"successful atomic write survives injected cleanup failure and increments health");
var directoryTarget=Folder("atomic-target-directory");Exception? primary=null;
try{SafeFileSystem.WriteAllBytesDurableAtomic(directoryTarget,new byte[]{1},"synthetic",_=>throw new UnauthorizedAccessException(secret));}catch(Exception error){primary=error;}
Check(primary is IOException&&primary.Message!=secret&&StartupDiagnostics.Health.CleanupFailures==initialCleanup+2,"atomic cleanup cannot replace original move failure");

var activeDir=Folder("active");using(var active=new DiagnosticLogWriter(activeDir,Guid.NewGuid())){
 active.Write("OPERATION","stage",new{area="RecoveryRecheck",stage="Failed",reason="ProviderOutcomeUnknown",operationId="22222222222222222222222222222222"});
 var dest=Path.Combine(root,"active-export.json");var result=DiagnosticSupportExporter.Export(activeDir,dest,CancellationToken.None);
 using var doc=Bundle(dest);Check(result.IncludedRecords==1,"export reads active owned log with compatible sharing");
 Check(!File.ReadAllText(dest).Contains("22222222222222222222222222222222")&&doc.RootElement.GetProperty("completeness").GetProperty("historicalCoverageUnknown").GetBoolean(),"support identifiers pseudonymized and historical coverage explicit");
 Check(!File.ReadAllText(dest).Contains(root,StringComparison.OrdinalIgnoreCase),"support output excludes directory/account paths");
}
var hostileDir=Folder("hostile");var hostile=Owned(hostileDir);
var good=Line();var duplicate=good.Replace("\"sequence\":1","\"sequence\":1,\"sequence\":2");
var deep="0";for(int i=0;i<20;i++)deep="{\"nested\":"+deep+"}";
File.WriteAllText(hostile,string.Join("\n",new[]{
 good,Line("OPERATION","stage",new{area=secret,stage=secret,reason=secret,operationId=secret,message=secret,token=secret}),
 Line("STARTUP","session_started",new{version=secret,architecture=secret,assemblySha256=new string('a',64),mvid=secret,logStorageKind=secret,executablePath=secret}),
 Line("LAUNCHER","launch_requested",new{executablePath=secret,assemblySha256=new string('a',64),requestedFrameworks=secret}),
 Line("RecoveryHistory","exception",new{operationId=secret,errors=new[]{new{type=secret,message=secret,methods=new[]{secret},httpStatus=403}}}),
 duplicate,good.Replace("\"schemaVersion\":1","\"schemaVersion\":\"secret\""),Line(secret,"exception"),"[]",deep,
 Line("ACTIVITY","activity_omitted",new{message=secret}),Line("OPERATION","stage",new{message=new string('x',20000)}),"{\"partial\":"}));
var hostileOutput=Path.Combine(root,"hostile-export.json");var hostileResult=DiagnosticSupportExporter.Export(hostileDir,hostileOutput,CancellationToken.None);var exported=File.ReadAllText(hostileOutput);
Check(!exported.Contains("PRIVATE_")&&!exported.Contains("sk-fake")&&!exported.Contains("Bearer")&&!exported.Contains("cookie-private")&&!exported.Contains(new string('a',64)),"secrets under allowed/unknown fields and raw build identities are not copied");
Check(hostileResult.IncludedRecords==5&&hostileResult.DroppedRecords==8&&hostileResult.Incomplete,"malformed duplicate deep oversized unknown and partial records counted as incomplete");
using(var doc=Bundle(hostileOutput))Check(doc.RootElement.GetProperty("records")[1].GetProperty("data").GetProperty("area").GetString()=="Other","untrusted enum text reduced to compiled vocabulary");

var original=File.ReadAllBytes(hostile);bool existingRejected=false;try{DiagnosticSupportExporter.Export(hostileDir,hostile,CancellationToken.None);}catch(IOException){existingRejected=true;}
Check(existingRejected&&File.ReadAllBytes(hostile).SequenceEqual(original),"export cannot overwrite source log or existing evidence");
var cancelledOutput=Path.Combine(root,"cancelled.json");bool cancelled=false;using(var stop=new CancellationTokenSource()){stop.Cancel();try{DiagnosticSupportExporter.Export(hostileDir,cancelledOutput,stop.Token);}catch(OperationCanceledException){cancelled=true;}}
Check(cancelled&&!File.Exists(cancelledOutput),"cancelled export creates no destination");

var lockedDir=Folder("locked");var locked=Owned(lockedDir);File.WriteAllText(locked,good+"\n");
using(var lease=new FileStream(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
 var dest=Path.Combine(root,"locked-export.json");DiagnosticSupportExporter.Export(lockedDir,dest,CancellationToken.None);using var doc=Bundle(dest);Check(doc.RootElement.GetProperty("completeness").GetProperty("unreadableFiles").GetInt32()==1,"locked input omission reported without retry or permissions change");
}
var oversizedDir=Folder("oversized");using(var file=new FileStream(Owned(oversizedDir),FileMode.CreateNew,FileAccess.Write))file.SetLength(DiagnosticSupportExporter.MaxFileBytes+1);
var oversizedOutput=Path.Combine(root,"oversized-export.json");DiagnosticSupportExporter.Export(oversizedDir,oversizedOutput,CancellationToken.None);using(var doc=Bundle(oversizedOutput))Check(doc.RootElement.GetProperty("completeness").GetProperty("oversizedFiles").GetInt32()==1,"oversized file rejected before content collection");
var limitsDir=Folder("limits");for(int i=0;i<81;i++)File.WriteAllText(Owned(limitsDir),good+"\n");
File.WriteAllText(Path.Combine(limitsDir,"profile.json"),secret);File.WriteAllText(Path.Combine(limitsDir,"legacy.jsonl"),secret);
var limitsOutput=Path.Combine(root,"limits-export.json");DiagnosticSupportExporter.Export(limitsDir,limitsOutput,CancellationToken.None);using(var doc=Bundle(limitsOutput)){var c=doc.RootElement.GetProperty("completeness");Check(c.GetProperty("enumerationLimited").GetBoolean()&&c.GetProperty("selectedFiles").GetInt32()==8,"candidate enumeration and file selection bounded with completeness flag");}Check(!File.ReadAllText(limitsOutput).Contains(secret),"profile and legacy content never collected");
var recordsDir=Folder("records");File.WriteAllText(Owned(recordsDir),string.Join("\n",Enumerable.Repeat(good,DiagnosticSupportExporter.MaxRecords+1))+"\n");
var recordsOutput=Path.Combine(root,"records-export.json");DiagnosticSupportExporter.Export(recordsDir,recordsOutput,CancellationToken.None);using(var doc=Bundle(recordsOutput))Check(doc.RootElement.GetProperty("completeness").GetProperty("recordLimited").GetBoolean()&&new FileInfo(recordsOutput).Length<=DiagnosticSupportExporter.MaxOutputBytes,"record and output bounds enforced without heavy input");

var emptyDir=Folder("empty");File.WriteAllBytes(Owned(emptyDir),Array.Empty<byte>());
var emptyOutput=Path.Combine(root,"empty-export.json");var emptyResult=DiagnosticSupportExporter.Export(emptyDir,emptyOutput,CancellationToken.None);using(var doc=Bundle(emptyOutput))Check(emptyResult.Incomplete&&doc.RootElement.GetProperty("completeness").GetProperty("emptyFiles").GetInt32()==1,"empty owned log is incomplete evidence");
var unterminatedDir=Folder("unterminated");File.WriteAllText(Owned(unterminatedDir),good);
var unterminatedOutput=Path.Combine(root,"unterminated-export.json");var unterminatedResult=DiagnosticSupportExporter.Export(unterminatedDir,unterminatedOutput,CancellationToken.None);
Check(unterminatedResult.IncludedRecords==0&&unterminatedResult.DroppedRecords==1&&unterminatedResult.Incomplete,"valid JSON without writer newline is excluded as an interrupted record");
int consumed=0;using(var failing=new LateFailureStream()) {bool failed=false;try{DiagnosticSupportExporter.ReadPrefix(failing,100,ref consumed,CancellationToken.None);}catch(IOException){failed=true;}Check(failed&&consumed==7,"late input failure retains consumed-byte budget");}
using(var growing=new MemoryStream(Encoding.UTF8.GetBytes("originalplusgrowth"))){consumed=0;var prefix=DiagnosticSupportExporter.ReadPrefix(growing,8,ref consumed,CancellationToken.None);Check(Encoding.UTF8.GetString(prefix)=="original"&&consumed==8,"snapshot reads frozen initial prefix rather than growth");}
using(var partial=new MemoryStream(new byte[]{1,2,3})){consumed=0;var prefix=DiagnosticSupportExporter.ReadPrefix(partial,10,ref consumed,CancellationToken.None);Check(prefix.Length==3&&consumed==3,"shrunken snapshot is detectable by actual prefix length");}
using(var cap=new MemoryStream(new byte[]{1})){consumed=DiagnosticSupportExporter.MaxInputBytes;bool rejected=false;try{DiagnosticSupportExporter.ReadPrefix(cap,1,ref consumed,CancellationToken.None);}catch(IOException){rejected=true;}Check(rejected&&consumed==DiagnosticSupportExporter.MaxInputBytes,"input byte cap rejects further collection");}
var diagnosisDir=Folder("diagnosis");var providerId="ct-33333333333333333333333333333333";
var providerLine=Line("Transcription","exception",new{operationId="22222222222222222222222222222222",errors=new[]{new{type="CosmicTranscriber.Services.OpenAiNetworkException",hresult="0x80131500",provider=new{status=503,code="rate_limit_exceeded",type="server_error",param="model",failureKind="timeout",clientRequestId=providerId,serverRequestIdSha256=new string('b',64),elapsedMs=10}}}});
var maliciousProviderLine=Line("Transcription","exception",new{errors=new[]{new{type=secret,provider=new{status=403,code=secret,type=secret,param=secret,failureKind=secret,clientRequestId=secret,serverRequestIdSha256=secret}}}});
File.WriteAllText(Owned(diagnosisDir),providerLine+"\n"+maliciousProviderLine+"\n"+Line("THEME","failed",new{requestedTheme="paper",previousTheme="night",elapsedMs=12,operationId="22222222222222222222222222222222"})+"\n"+Line("UI_VISIBILITY","stable_visible",new{controlId="OpenDiagnosticLogsButton",viewportId="SettingsViewport",combinedVisibleFraction=1.0,observationElapsedMs=20})+"\n");
var diagnosisOutput=Path.Combine(root,"diagnosis-export.json");DiagnosticSupportExporter.Export(diagnosisDir,diagnosisOutput,CancellationToken.None);
using(var doc=Bundle(diagnosisOutput)){var rows=doc.RootElement.GetProperty("records");var error=rows[0].GetProperty("data").GetProperty("errors")[0];var provider=error.GetProperty("provider");Check(error.GetProperty("type").GetString()=="CosmicTranscriber.Services.OpenAiNetworkException"&&provider.GetProperty("failureKind").GetString()=="timeout"&&provider.GetProperty("status").GetInt32()==503&&provider.GetProperty("clientRequestId").GetString()!.Length==64,"typed provider diagnosis and pseudonym correlation preserved");Check(rows[2].GetProperty("category").GetString()=="THEME"&&rows[3].GetProperty("data").GetProperty("combinedVisibleFraction").GetDouble()==1,"theme and native visibility support evidence retained");}
Check(!File.ReadAllText(diagnosisOutput).Contains("PRIVATE_")&&!File.ReadAllText(diagnosisOutput).Contains(providerId),"hostile provider fields omitted and raw correlation ID not exported");
var drains=new DiagnosticOutputDrain();using(var reader=new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(secret+" BadImageFormatException "+secret))))await drains.Drain(reader,true);
Check(drains.Complete&&drains.FailureKind=="bad_image_format"&&drains.Characters>0,"launcher drain keeps fixed classification and complete count without raw output persistence");
var failingDrain=new DiagnosticOutputDrain();using(var reader=new FailingReader())await failingDrain.Drain(reader,false);
Check(!failingDrain.Complete&&failingDrain.FailureKind=="stream_read_error","stdout read failure cannot be reported as fully drained");

var wireCategories=new[]{"FATAL STARTUP","UNHANDLED DISPATCHER","UNHANDLED APPDOMAIN","UNOBSERVED TASK","OPENAI API","TRANSCRIPTION FILE FAILURE","TRANSCRIPTION RUN FAILURE","API KEY LOAD FALLBACK","API KEY VAULT UNREADABLE","SETTINGS FALLBACK","SETTINGS DIRECTORY REJECTED","COMPLETION OPEN OUTPUT","OUTPUT PATH FALLBACK"};
foreach(var category in wireCategories)StartupDiagnostics.WriteException(category,new BadImageFormatException(secret));
var policyOperation=Guid.NewGuid();StartupDiagnostics.WriteImportPolicy(policyOperation,false);
var wireOutput=Path.Combine(root,"wire-roundtrip.json");DiagnosticSupportExporter.Export(StartupDiagnostics.LogDirectory,wireOutput,CancellationToken.None);
using(var doc=Bundle(wireOutput)){var exportedCategories=doc.RootElement.GetProperty("records").EnumerateArray().Where(x=>x.GetProperty("eventName").GetString()=="exception").Select(x=>x.GetProperty("category").GetString()).ToHashSet();Check(wireCategories.All(x=>exportedCategories.Contains(x.Replace(' ','_'))),"actual logger fatal async auth storage and run exception wire categories survive support export");}
using(var doc=Bundle(wireOutput)){var policy=doc.RootElement.GetProperty("records").EnumerateArray().Single(x=>x.GetProperty("eventName").GetString()=="scan_policy"&&x.GetProperty("data").GetProperty("operationId").GetString()==Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(policyOperation.ToString("N")))).ToLowerInvariant()).GetProperty("data");Check(policy.GetProperty("skipInaccessible").GetBoolean()&&!policy.GetProperty("cacheExclusionResolved").GetBoolean(),"non-exhaustive import scan policy and unresolved cache exclusion are explicit without paths");}
using(var doc=Bundle(wireOutput)){var mvid=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(typeof(StartupDiagnostics).Assembly.ManifestModule.ModuleVersionId.ToString("N")))).ToLowerInvariant();Check(doc.RootElement.GetProperty("records").EnumerateArray().Any(x=>x.GetProperty("category").GetString()=="STARTUP"&&x.GetProperty("data").GetProperty("mvid").GetString()==mvid),"actual serialized hyphenated MVID is normalized and pseudonymized in support projection");}
Check(!File.ReadAllText(wireOutput).Contains("PRIVATE_")&&DiagnosticVocabulary.Category(secret)=="OTHER"&&!DiagnosticVocabulary.IsWireCategory(secret),"shared wire vocabulary still rejects arbitrary error categories and content");

Check(opened.FailureDetails.Count==1&&opened.FailureDetails[0].Reason==DiagnosticSinkFailureReason.StorageFailure&&unavailable.FailureDetails.All(x=>x.Reason==DiagnosticSinkFailureReason.AccessDenied),"startup open failures retain bounded typed cause and HRESULT without raw messages");
var lossDir=Folder("source-loss");File.WriteAllText(Owned(lossDir),Line("LIFECYCLE","normal_exit",new{exitCode=0,coverage=new{SinkUnavailable=false,SinkFailures=7}})+"\n");
var lossOutput=Path.Combine(root,"source-loss-export.json");DiagnosticSupportExporter.Export(lossDir,lossOutput,CancellationToken.None);using(var doc=Bundle(lossOutput))Check(doc.RootElement.GetProperty("completeness").GetProperty("sourceReportedLoss").GetBoolean(),"prior-session diagnostic loss propagates into support completeness");
var unknownHealthDir=Folder("unknown-health");File.WriteAllText(Owned(unknownHealthDir),Line("LAUNCHER","diagnostic_health",new{SinkUnavailable=secret,WriteFailures=0})+"\n");
var unknownHealthOutput=Path.Combine(root,"unknown-health-export.json");DiagnosticSupportExporter.Export(unknownHealthDir,unknownHealthOutput,CancellationToken.None);using(var doc=Bundle(unknownHealthOutput))Check(doc.RootElement.GetProperty("completeness").GetProperty("sourceReportedLoss").GetBoolean()&&!File.ReadAllText(unknownHealthOutput).Contains(secret),"malformed source-health state stays incomplete and redacted");

var outputBudgetDir=Folder("output-budget");
var budgetError=new{type="CosmicTranscriber.Services.OpenAiNetworkException",hresult="0x80131500",httpStatus=503,nativeError=0,socketError=0,requestError=1,provider=new{status=503,code="rate_limit_exceeded",type="server_error",param="model",failureKind="timeout",clientRequestId=providerId,serverRequestIdSha256=new string('b',64),elapsedMs=10}};
var budgetLine=Line("Transcription","exception",new{operationId="22222222222222222222222222222222",errors=Enumerable.Repeat(budgetError,4).ToArray()});
var budgetRows=Math.Min(1500,(DiagnosticSupportExporter.MaxFileBytes-1)/Encoding.UTF8.GetByteCount(budgetLine+"\n"));
File.WriteAllText(Owned(outputBudgetDir),string.Concat(Enumerable.Repeat(budgetLine+"\n",budgetRows)));
var budgetOutput=Path.Combine(root,"output-budget-export.json");DiagnosticSupportExporter.Export(outputBudgetDir,budgetOutput,CancellationToken.None);using(var doc=Bundle(budgetOutput))Check(doc.RootElement.GetProperty("completeness").GetProperty("oversizedFiles").GetInt32()==0&&doc.RootElement.GetProperty("completeness").GetProperty("droppedRecords").GetInt32()>0&&new FileInfo(budgetOutput).Length<=DiagnosticSupportExporter.MaxOutputBytes,"actual output-byte budget drops projected rows before the hard cap");
using(var cancelRead=new CancellationTokenSource())using(var stream=new CancellingReadStream(cancelRead)){consumed=0;bool interrupted=false;try{DiagnosticSupportExporter.ReadPrefix(stream,100,ref consumed,cancelRead.Token);}catch(OperationCanceledException){interrupted=true;}Check(interrupted&&consumed==7,"mid-read cancellation preserves bounded progress and unwinds");}
var blankDir=Folder("blank-budget");File.WriteAllText(Owned(blankDir),new string('\n',DiagnosticSupportExporter.MaxRecords+1));
var blankOutput=Path.Combine(root,"blank-budget-export.json");DiagnosticSupportExporter.Export(blankDir,blankOutput,CancellationToken.None);using(var doc=Bundle(blankOutput))Check(doc.RootElement.GetProperty("completeness").GetProperty("recordLimited").GetBoolean()&&doc.RootElement.GetProperty("completeness").GetProperty("scannedRecords").GetInt32()==DiagnosticSupportExporter.MaxRecords,"blank record padding cannot bypass inspection-count budget");
var priorPath=StartupDiagnostics.LogPath;var field=typeof(StartupDiagnostics).GetField("_writer",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
((IDisposable?)field.GetValue(null))?.Dispose();StartupDiagnostics.WriteLifecycle("concurrent_event");
Check(StartupDiagnostics.Health.SinkUnavailable&&StartupDiagnostics.LogDirectory==Path.GetDirectoryName(priorPath),"failed app sink retains read-only prior source location without retry");
var failedSinkOutput=Path.Combine(root,"failed-sink-export.json");DiagnosticSupportExporter.Export(StartupDiagnostics.LogDirectory,failedSinkOutput,CancellationToken.None);using(var doc=Bundle(failedSinkOutput))Check(doc.RootElement.GetProperty("health").GetProperty("SinkUnavailable").GetBoolean(),"support projection remains available after app sink failure");
var noSinkOutput=Path.Combine(root,"health-only-export.json");var noSink=DiagnosticSupportExporter.Export("",noSinkOutput,CancellationToken.None);Check(noSink.IncludedRecords==0&&noSink.Incomplete,"no source yields explicit incomplete health-only projection without scanning alternatives");

var beforeInitializationFault=File.ReadAllBytes(priorPath);
StartupDiagnostics.InitializationFailed(new IOException(secret));
var initializationFailure=StartupDiagnostics.Health.InitializationFailure;
Check(initializationFailure?.Reason==DiagnosticReason.StorageFailure&&File.ReadAllBytes(priorPath).SequenceEqual(beforeInitializationFault),"unexpected initialization failure retains typed bounded cause without another sink write");
var initializationOutput=Path.Combine(root,"initialization-failure-export.json");DiagnosticSupportExporter.Export("",initializationOutput,CancellationToken.None);using(var doc=Bundle(initializationOutput))Check(doc.RootElement.GetProperty("health").GetProperty("initializationFailure").GetProperty("reason").GetString()=="StorageFailure"&&!File.ReadAllText(initializationOutput).Contains("PRIVATE_"),"initialization cause is available in redacted incomplete health-only export");
StartupDiagnostics.Shutdown(0);var operational=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"logs"),"CosmicDiag-v1-*.jsonl").SelectMany(File.ReadLines);Check(operational.All(x=>!x.Contains("PRIVATE_")&&!x.Contains("sk-fake")&&!x.Contains("Bearer")),"new sink cleanup and support operations do not persist fake secrets");
Console.WriteLine($"LOGGING_COVERAGE_ASSERTIONS={passed}; synthetic local files only; no network/profile/foreground");

sealed class LateFailureStream : MemoryStream {
 private bool first=true;
 public override int Read(byte[] buffer,int offset,int count){if(!first)throw new IOException("PRIVATE_LATE_READ_FAILURE");first=false;Array.Fill(buffer,(byte)1,offset,7);return 7;}
}
sealed class FailingReader : StreamReader {
 public FailingReader():base(new MemoryStream()){}
 public override ValueTask<int> ReadAsync(Memory<char> buffer,CancellationToken cancellationToken=default)=>ValueTask.FromException<int>(new IOException("PRIVATE_DRAIN_FAILURE"));
}

sealed class CancellingReadStream(CancellationTokenSource stop) : MemoryStream {
 public override int Read(byte[] buffer,int offset,int count){Array.Fill(buffer,(byte)1,offset,7);stop.Cancel();return 7;}
}
