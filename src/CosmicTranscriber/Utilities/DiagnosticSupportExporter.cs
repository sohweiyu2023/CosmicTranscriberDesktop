using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CosmicTranscriber.Utilities;

/// <summary>Local create-new support projection, never a raw log/profile archive or uploader.</summary>
internal static class DiagnosticSupportExporter
{
    internal const int MaxCandidates=80,MaxFiles=8,MaxFileBytes=2*1024*1024,MaxInputBytes=8*1024*1024,MaxRecords=2000,MaxRecordBytes=16384,MaxOutputBytes=1024*1024;
    internal sealed record Result(int IncludedRecords,int DroppedRecords,bool Incomplete);
    internal static Result Export(string? sourceDirectory,string destination,CancellationToken token)
    {
        using var operation=StartupDiagnostics.Begin(DiagnosticArea.SupportExport);
        try {
            if(!string.IsNullOrWhiteSpace(sourceDirectory))Guard(sourceDirectory);Guard(destination);
            var candidates=new List<string>();bool enumerationLimited=false;int entriesObserved=0,ignoredFiles=0;
            foreach(var path in string.IsNullOrWhiteSpace(sourceDirectory)?Enumerable.Empty<string>():Directory.EnumerateFiles(sourceDirectory,"*.jsonl",SearchOption.TopDirectoryOnly)) {
                token.ThrowIfCancellationRequested();
                if(entriesObserved==MaxCandidates){enumerationLimited=true;break;}entriesObserved++;
                if(!Owned(System.IO.Path.GetFileName(path))){ignoredFiles++;continue;}candidates.Add(path);
            }
            // Prefer this process's current/last sink, then deterministic bounded
            // selection. No timestamp/path metadata is exported or recursively scanned.
            var currentLog=StartupDiagnostics.LogPath;
            var selected=candidates.OrderByDescending(x=>string.Equals(x,currentLog,StringComparison.OrdinalIgnoreCase)).ThenBy(x=>x,StringComparer.Ordinal).Take(MaxFiles).ToArray();
            int dropped=0,unreadable=0,oversized=0,scanned=0,inputBytes=0,outputBytes=0,emptyFiles=0,unterminatedFiles=0,changedFiles=0;bool recordLimited=false,inputLimited=false,sourceReportedLoss=false;
            var records=new List<object>();
            foreach(var path in selected) {
                token.ThrowIfCancellationRequested();byte[] bytes;
                try {
                    Guard(path);
                    using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                    var initialLength=stream.Length;
                    if(initialLength>MaxFileBytes){oversized++;continue;}
                    var remaining=Math.Min(MaxFileBytes,MaxInputBytes-inputBytes);
                    if(remaining<=0){inputLimited=true;break;}
                    if(initialLength>remaining){inputLimited=true;break;}
                    bytes=ReadPrefix(stream,initialLength,ref inputBytes,token);
                    if(bytes.Length!=initialLength||stream.Length!=initialLength)changedFiles++;
                } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) {unreadable++;continue;}
                string text;try{text=new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){unreadable++;continue;}
                if(bytes.Length==0){emptyFiles++;continue;}
                if(bytes[^1]!=(byte)'\n'){unterminatedFiles++;dropped++;var last=text.LastIndexOf('\n');text=last<0?"":text[..(last+1)];}
                using var reader=new StringReader(text);string? line;
                while((line=reader.ReadLine()) is not null) {
                    token.ThrowIfCancellationRequested();
                    if(scanned==MaxRecords){recordLimited=true;break;}scanned++;
                    if(line.Length==0){dropped++;continue;}
                    if(Encoding.UTF8.GetByteCount(line)>MaxRecordBytes){dropped++;continue;}
                    object? projected=null;
                    try {using var doc=JsonDocument.Parse(line,new JsonDocumentOptions{MaxDepth=8});if(Unique(doc.RootElement)){projected=Project(doc.RootElement);if(projected is not null)sourceReportedLoss|=ReportsLoss(doc.RootElement);}}catch(JsonException){}
                    if(projected is null){dropped++;continue;}
                    var size=JsonSerializer.SerializeToUtf8Bytes(projected).Length;
                    if(outputBytes+size+1>MaxOutputBytes-16384){dropped++;continue;}
                    records.Add(projected);outputBytes+=size+1;
                }
                if(recordLimited)break;
            }
            var health=StartupDiagnostics.Health;
            bool incomplete=sourceReportedLoss||candidates.Count==0||emptyFiles>0||changedFiles>0||enumerationLimited||candidates.Count>selected.Length||dropped>0||unreadable>0||oversized>0||recordLimited||inputLimited||health.SinkUnavailable||health.SinkFailures>0||health.OversizedRecords>0||health.FallbackEventsDropped>0||health.CleanupFailures>0||health.PruneFailures>0;
            var payload=JsonSerializer.SerializeToUtf8Bytes(new {
                schemaVersion=1,kind="redacted_support_projection",createdUtc=DateTimeOffset.UtcNow,
                completeness=new{incomplete,sourceReportedLoss,enumerationLimited,entriesObserved,ignoredFiles,candidateFilesObserved=candidates.Count,selectedFiles=selected.Length,unselectedObservedFiles=candidates.Count-selected.Length,unreadableFiles=unreadable,oversizedFiles=oversized,emptyFiles,unterminatedFiles,changedFiles,scannedRecords=scanned,includedRecords=records.Count,droppedRecords=dropped,recordLimited,inputLimited,historicalCoverageUnknown=true,sourceSnapshot="bounded_initial_length_prefix_mutable_not_signed",
                    historicalCoverage="selected_owned_current_schema_only_retained_out_and_legacy_unknown",identifiers="pseudonymized_sha256",rawMessagesPathsAndPayloadsExcluded=true},
                health=new{health.SinkUnavailable,health.SinkFailures,health.OversizedRecords,health.FallbackEventsDropped,health.CleanupFailures,health.PruneFailures,initializationFailure=health.InitializationFailure is {} f ? new{reason=f.Reason.ToString(),hresult=f.HResult}:null},records});
            if(payload.Length>MaxOutputBytes)throw new IOException("Support projection size limit");
            token.ThrowIfCancellationRequested();Guard(destination);
            // CreateNew rejects existing logs, evidence or user output. No overwrite/delete/rollback.
            using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(payload);output.Flush(true);}
            operation.Stage(DiagnosticStage.Completed,incomplete?DiagnosticReason.Incomplete:DiagnosticReason.None);
            return new(records.Count,dropped,incomplete);
        }catch(OperationCanceledException){operation.Stage(DiagnosticStage.Cancelled,DiagnosticReason.UserCancelled);throw;}
        catch(Exception error){operation.Fail(error,DiagnosticReason.Other);throw;}
    }
    private static void Guard(string path) {
        for(string? current=System.IO.Path.GetFullPath(path);current is not null;current=System.IO.Path.GetDirectoryName(current)) {
            try{if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Reparse support path");}
            catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
        }
    }
    internal static byte[] ReadPrefix(Stream stream,long initialLength,ref int consumed,CancellationToken token) {
        if(initialLength<0||initialLength>MaxFileBytes||consumed<0||initialLength>MaxInputBytes-consumed)throw new IOException("Support input budget");
        using var buffer=new MemoryStream();var block=new byte[8192];int count;
        while(buffer.Length<initialLength) {
            token.ThrowIfCancellationRequested();
            count=stream.Read(block,0,(int)Math.Min(block.Length,initialLength-buffer.Length));if(count==0)break;
            consumed+=count;buffer.Write(block,0,count);
        }
        return buffer.ToArray();
    }
    private static bool Owned(string name) {
        var p=System.IO.Path.GetFileNameWithoutExtension(name).Split('-');
        return p.Length==4 && p[0] is "CosmicDiag" or "CosmicLaunch" && p[1]=="v1" && Guid.TryParseExact(p[2],"N",out _) && Guid.TryParseExact(p[3],"N",out _);
    }
    private static bool Unique(JsonElement e) {
        if(e.ValueKind==JsonValueKind.Object){var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject()){if(!seen.Add(p.Name)||!Unique(p.Value))return false;}}
        else if(e.ValueKind==JsonValueKind.Array){if(e.GetArrayLength()>32)return false;foreach(var item in e.EnumerateArray())if(!Unique(item))return false;}
        return true;
    }
    private static string? S(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
    private static object? N(JsonElement e,string key,double min=0,double max=1e12)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out var n)&&double.IsFinite(n)&&n>=min&&n<=max?Math.Round(n,3):null;
    private static object? B(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var v)&&v.ValueKind is JsonValueKind.True or JsonValueKind.False?v.GetBoolean():null;
    private static string? Id(JsonElement e,string key){var s=S(e,key);if(s?.StartsWith("ct-",StringComparison.Ordinal)==true)s=s[3..];return s is not null&&(Guid.TryParseExact(s,"N",out var id)||Guid.TryParseExact(s,"D",out id))?Hash(id.ToString("N")):null;}
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string? Identity(JsonElement e,string key){var s=S(e,key);return s is {Length:64}&&s.All(Uri.IsHexDigit)?Hash(s):null;}
    private static string EnumName<T>(JsonElement e,string key) where T:struct,Enum {var s=S(e,key);return Enum.TryParse<T>(s,out var v)&&Enum.IsDefined(v)&&v.ToString()==s?s!:"Other";}
    private static string? VersionText(JsonElement e,string key){var s=S(e,key);return s is {Length:<=32}&&Regex.IsMatch(s,@"^\d{1,4}\.\d{1,4}\.\d{1,6}(\.\d{1,6})?$")?s:null;}
    private static string Known(JsonElement e,string key,params string[] allowed){var s=S(e,key);return s is not null&&allowed.Contains(s,StringComparer.Ordinal)?s:"Other";}
    private static int? HResult(JsonElement e){var s=S(e,"hresult");return s is {Length:10}&&s.StartsWith("0x",StringComparison.Ordinal)&&uint.TryParse(s[2..],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var value)?unchecked((int)value):null;}
    private static object? Provider(JsonElement e) {
        if(e.ValueKind!=JsonValueKind.Object||!e.TryGetProperty("provider",out var p)||p.ValueKind!=JsonValueKind.Object)return null;
        return new{status=N(p,"status",100,599),
            code=Known(p,"code","invalid_api_key","insufficient_quota","rate_limit_exceeded","model_not_found","context_length_exceeded","invalid_value","server_error","unsupported_value"),
            type=Known(p,"type","invalid_request_error","authentication_error","permission_error","rate_limit_error","server_error","insufficient_quota"),
            param=Known(p,"param","model","file","language","languages","prompt","keywords","response_format","chunking_strategy"),
            clientRequestId=Id(p,"clientRequestId"),serverIdentitySha256=Identity(p,"serverRequestIdSha256"),
            failureKind=Known(p,"failureKind","DNS/name resolution","connection","TLS/certificate","HTTP protocol","extended CONNECT unsupported","HTTP version negotiation","network authentication","proxy tunnel","invalid response","response ended early","HTTP configuration limit","unknown network failure","timeout"),elapsedMs=N(p,"elapsedMs")};
    }
    private static object Error(JsonElement e)=>new {
        type=Known(e,"type","System.Exception","System.IO.IOException","System.IO.InvalidDataException","System.IO.FileNotFoundException","System.UnauthorizedAccessException","System.OperationCanceledException","System.Threading.Tasks.TaskCanceledException","System.Net.Http.HttpRequestException","System.Net.Sockets.SocketException","System.BadImageFormatException","System.TypeInitializationException","System.DllNotFoundException","System.ComponentModel.Win32Exception","CosmicTranscriber.Services.OpenAiApiException","CosmicTranscriber.Services.OpenAiNetworkException","CosmicTranscriber.Services.OpenAiProtocolException","CosmicTranscriber.Services.PendingTranscriptionException","CosmicTranscriber.Services.CheckpointStorageException","CosmicTranscriber.Services.AudioPreparationStorageException","CosmicTranscriber.Services.TranscriptSizeLimitException","CosmicTranscriber.Services.TranscriptExportException"),
        hresult=HResult(e),httpStatus=N(e,"httpStatus",100,599),nativeError=N(e,"nativeError",int.MinValue,int.MaxValue),socketError=N(e,"socketError",int.MinValue,int.MaxValue),requestError=N(e,"requestError",0,64),provider=Provider(e)};
    private static string? RuntimeVersion(JsonElement d) {var s=S(d,"runtime");return s is {Length:<=40}&&s.StartsWith(".NET ",StringComparison.Ordinal)&&System.Version.TryParse(s[5..],out var version)?".NET "+version:null;}
    private static object[] SinkFailures(JsonElement d,string key) {
        if(d.ValueKind!=JsonValueKind.Object||!d.TryGetProperty(key,out var items)||items.ValueKind!=JsonValueKind.Array)return Array.Empty<object>();
        return items.EnumerateArray().Take(4).Select(x=>(object)new{location=EnumName<DiagnosticSinkLocation>(x,"location"),reason=EnumName<DiagnosticSinkFailureReason>(x,"reason"),hresult=N(x,"hresult",int.MinValue,int.MaxValue)}).ToArray();
    }
    private static bool HealthLoss(JsonElement h) {
        if(h.ValueKind!=JsonValueKind.Object)return false;
        if(h.TryGetProperty("SinkUnavailable",out var unavailable)&&unavailable.ValueKind!=JsonValueKind.False)return true;
        if(h.TryGetProperty("InitializationFailure",out var initialization)&&initialization.ValueKind!=JsonValueKind.Null)return true;
        foreach(var key in new[]{"SinkFailures","OversizedRecords","FallbackEventsDropped","CleanupFailures","PruneFailures","WriteFailures","DroppedRecords","DisposeFailures"})
            if(h.TryGetProperty(key,out var value)&&(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out var count)||!double.IsFinite(count)||count!=0))return true;
        return false;
    }
    private static bool ReportsLoss(JsonElement r) {
        var category=S(r,"category");var name=S(r,"eventName");r.TryGetProperty("data",out var d);
        if(category=="DIAGNOSTICS"&&name is "record_omitted" or "initialization_failed")return true;
        if(category=="LAUNCHER"&&name=="diagnostic_health")return HealthLoss(d);
        return category=="LIFECYCLE"&&name=="normal_exit"&&d.ValueKind==JsonValueKind.Object&&d.TryGetProperty("coverage",out var h)&&HealthLoss(h);
    }
    private static object[] Frameworks(JsonElement d) {
        if(d.ValueKind!=JsonValueKind.Object||!d.TryGetProperty("requestedFrameworks",out var items)||items.ValueKind!=JsonValueKind.Array)return Array.Empty<object>();
        return items.EnumerateArray().Take(4).Where(x=>S(x,"name") is "Microsoft.NETCore.App" or "Microsoft.WindowsDesktop.App").Select(x=>(object)new{name=S(x,"name"),version=VersionText(x,"version")}).ToArray();
    }
    private static object? Project(JsonElement r) {
        if(r.ValueKind!=JsonValueKind.Object||!r.TryGetProperty("schemaVersion",out var schema)||schema.ValueKind!=JsonValueKind.Number||!schema.TryGetInt32(out var version)||version!=1)return null;
        var rootKeys=new HashSet<string>(new[]{"schemaVersion","utc","sessionId","sequence","elapsedMs","processId","category","eventName","data"},StringComparer.Ordinal);
        if(r.EnumerateObject().Any(x=>!rootKeys.Contains(x.Name)))return null;
        if(!DateTimeOffset.TryParse(S(r,"utc"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var utc)||Id(r,"sessionId") is not string session)return null;
        var category=S(r,"category");var name=S(r,"eventName");r.TryGetProperty("data",out var d);object? data=null;
        if(category=="OPERATION"&&name=="stage")data=new{area=EnumName<DiagnosticArea>(d,"area"),stage=EnumName<DiagnosticStage>(d,"stage"),reason=EnumName<DiagnosticReason>(d,"reason"),operationId=Id(d,"operationId"),parentOperationId=Id(d,"parentOperationId"),operationElapsedMs=N(d,"operationElapsedMs"),count=N(d,"count")};
        else if(category=="OPERATION"&&name=="storage_selected")data=new{area=EnumName<DiagnosticArea>(d,"area"),locationKind=EnumName<DiagnosticStorageKind>(d,"locationKind"),operationId=Id(d,"operationId")};
        else if(category=="OPERATION"&&name=="scan_policy")data=new{area="AudioImport",operationId=Id(d,"operationId"),recursive=B(d,"recursive"),skipInaccessible=B(d,"skipInaccessible"),skipReparse=B(d,"skipReparse"),cacheExclusionResolved=B(d,"cacheExclusionResolved"),boundedQueue=B(d,"boundedQueue")};
        else if(category=="STARTUP"&&name=="session_started")data=new{version=VersionText(d,"version"),buildIdentitySha256=Identity(d,"assemblySha256"),mvid=Id(d,"mvid"),runtimeVersion=RuntimeVersion(d),osVersion=VersionText(d,"osVersion"),architecture=S(d,"architecture") is "X64" or "X86" or "Arm64"?S(d,"architecture"):"Other",logStorageKind=EnumName<DiagnosticSinkLocation>(d,"logStorageKind"),logOpenAttempts=N(d,"logOpenAttempts",0,3),logOpenFailures=N(d,"logOpenFailures",0,3),logEnumerationFailed=B(d,"logEnumerationFailed"),logOpenFailureDetails=SinkFailures(d,"logOpenFailureDetails")};
        else if(category=="OPENAI_HTTP"&&name=="response")data=new{operation=S(d,"operation") is "model-access-test" or "audio-transcription"?S(d,"operation"):"other",operationId=Id(d,"operationId"),clientRequestId=Id(d,"clientRequestId"),serverIdentitySha256=Identity(d,"serverRequestIdSha256"),status=N(d,"status",100,599),elapsedMs=N(d,"elapsedMs"),processingMs=N(d,"processingMs")};
        else if(name=="exception" && DiagnosticVocabulary.IsWireCategory(category)) {
            if(d.ValueKind!=JsonValueKind.Object||!d.TryGetProperty("errors",out var errors)||errors.ValueKind!=JsonValueKind.Array||errors.GetArrayLength()>4)return null;
            data=new{operationId=Id(d,"operationId"),detailsOmitted=true,errors=errors.EnumerateArray().Select(Error).ToArray()};
        }
        else if(category=="WINDOW"&&DiagnosticVocabulary.WindowStage(name)!="other")data=new{windowKind=EnumName<DiagnosticWindowKind>(d,"windowKind"),visible=B(d,"visible"),active=B(d,"active"),state=S(d,"state") is "Normal" or "Maximized" or "Minimized"?S(d,"state"):"Other",width=N(d,"width",0,100000),height=N(d,"height",0,100000),dpiX=N(d,"dpiX",0,10000),dpiY=N(d,"dpiY",0,10000)};
        else if(category=="UI_VISIBILITY"&&DiagnosticVocabulary.ViewportStage(name)!="other") {
            var projected=new Dictionary<string,object?> { ["controlId"]=DiagnosticVocabulary.Control(S(d,"controlId")),["viewportId"]=DiagnosticVocabulary.Control(S(d,"viewportId")),["eventHandled"]=B(d,"eventHandled"),["expansionFactorResolved"]=B(d,"expansionFactorResolved") };
            foreach(var key in new[]{"ticket","offsetX","offsetY","desiredX","desiredY","targetLeft","targetTop","targetWidth","targetHeight","viewportWidth","viewportHeight","expansionFactor","observationElapsedMs","stabilityAgeMs","combinedVisibleFraction","visibleFraction"})projected[key]=N(d,key,-1e9,1e12);
            data=projected;
        }
        else if(category=="THEME"&&name is "requested" or "applied" or "failed")data=new{operationId=Id(d,"operationId"),requestedTheme=Known(d,"requestedTheme","night","paper"),previousTheme=Known(d,"previousTheme","night","paper"),dictionaryCount=N(d,"dictionaryCount",0,1000),paletteIndex=N(d,"paletteIndex",-1,1000),elapsedMs=N(d,"elapsedMs"),failureType=Known(d,"failureType","System.IO.IOException","System.IO.InvalidDataException","System.InvalidOperationException","System.Windows.Markup.XamlParseException"),hresult=HResult(d)};
        else if(category=="UI_FOCUS"&&name=="keyboard_focus")data=new{controlId=DiagnosticVocabulary.Control(S(d,"controlId")),controlType=DiagnosticVocabulary.Type(S(d,"controlType"))};
        else if(category=="LIFECYCLE"&&name=="normal_exit")data=new{exitCode=N(d,"exitCode",int.MinValue,int.MaxValue),coverage=Health(d,"coverage")};
        else if(category=="LIFECYCLE"&&DiagnosticVocabulary.Lifecycle(name)!="other")data=new{detailsOmitted=true};
        else if(category=="DIAGNOSTICS"&&name=="record_omitted")data=new{reason="record_too_large",omittedRecords=N(d,"omittedRecords")};
        else if(category=="DIAGNOSTICS"&&name=="initialization_failed")data=new{reason="startup_metadata_failure"};
        else if(DiagnosticVocabulary.IsWireCategory(category)&&name is "constructor_entered" or "constructor_completed" or "settings_loaded" or "settings_saved" or "defaults_missing_file" or "defaults_missing_directory" or "key_loaded_vault" or "key_loaded_environment" or "key_missing" or "key_saved" or "key_removed" or "partial_window_close" or "close_deferred" or "deferred_close_completed" or "constructor_entered_creating_application_services" or "initializing_xaml" or "initializecomponent_completed" or "applying_settings_to_controls" or "loading_initial_api_key_state")data=new{messageOmitted=true};
        else if(category=="LAUNCHER" && name is "launch_requested" or "process_started" or "native_window_observed" or "process_exited" or "qa_timeout_cleanup" or "qa_cleanup_unconfirmed" or "qa_exception_cleanup" or "launcher_exception" or "launcher_cleanup_failed" or "diagnostic_health" or "diagnostic_sink_initialized") {
            data=new{childProcessId=N(d,"childProcessId",1,int.MaxValue),exitCode=N(d,"exitCode",int.MinValue,int.MaxValue),windowObserved=B(d,"windowObserved"),ownedQaCleanup=B(d,"ownedQaCleanup"),outputDrained=B(d,"outputDrained"),outputDrainTasksEnded=B(d,"outputDrainTasksEnded"),stderrCharacters=N(d,"stderrCharacters"),stdoutCharacters=N(d,"stdoutCharacters"),classifiedFailureKind=Known(d,"classifiedFailureKind","bad_image_format","file_not_found","dll_not_found","type_initialization","primary_assembly_missing","required_runtime_missing","stream_read_error"),stdoutFailureKind=Known(d,"stdoutFailureKind","stream_read_error"),cleanupStage=Known(d,"stage","process_dispose"),buildIdentitySha256=Identity(d,"assemblySha256"),requestedFrameworks=Frameworks(d),qaTimeoutSeconds=N(d,"qaTimeoutSeconds",1,3600),exceptionType=Known(d,"type","System.ComponentModel.Win32Exception","System.IO.IOException","System.UnauthorizedAccessException","System.InvalidOperationException","System.ArgumentException","System.ObjectDisposedException"),hresult=HResult(d),nativeError=N(d,"nativeError",int.MinValue,int.MaxValue),sinkUnavailable=B(d,"SinkUnavailable"),writeFailures=N(d,"WriteFailures"),droppedRecords=N(d,"DroppedRecords"),oversizedRecords=N(d,"OversizedRecords"),pruneFailures=N(d,"PruneFailures"),disposeFailures=N(d,"DisposeFailures"),location=EnumName<DiagnosticSinkLocation>(d,"location"),attempts=N(d,"attempts",0,3),failures=N(d,"failures",0,3),failureDetails=SinkFailures(d,"failureDetails")};
        }
        else return null;
        return new{schemaVersion=1,utc=utc.ToUniversalTime(),sessionId=session,sequence=N(r,"sequence"),elapsedMs=N(r,"elapsedMs"),processId=N(r,"processId",1,int.MaxValue),category,eventName=name,data};
    }
    private static object? Health(JsonElement parent,string key) {
        if(parent.ValueKind!=JsonValueKind.Object||!parent.TryGetProperty(key,out var h)||h.ValueKind!=JsonValueKind.Object)return null;
        return new{sinkUnavailable=B(h,"SinkUnavailable"),sinkFailures=N(h,"SinkFailures"),oversizedRecords=N(h,"OversizedRecords"),fallbackEventsDropped=N(h,"FallbackEventsDropped"),cleanupFailures=N(h,"CleanupFailures"),pruneFailures=N(h,"PruneFailures")};
    }
}
