using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CosmicTranscriber.Utilities;

// A normal compiled development helper, not a PowerShell host or policy override.
string? exe=null;int? qaSeconds=null;
for(int i=0;i<args.Length;i++){
    if(args[i]=="--exe" && i+1<args.Length){try{exe=Path.GetFullPath(args[++i]);}catch{Console.Error.WriteLine("Invalid executable path.");return 2;}}
    else if(args[i]=="--qa-timeout-seconds" && i+1<args.Length && int.TryParse(args[i+1],NumberStyles.None,CultureInfo.InvariantCulture,out var n) && n is >=1 and <=3600){qaSeconds=n;i++;}
    else {Console.Error.WriteLine("Use --exe <existing application> [--qa-timeout-seconds 1..3600]. No rebuild is performed.");return 2;}
}
if(exe is null || !File.Exists(exe)){Console.Error.WriteLine("The existing executable was not found. Build separately before diagnosing.");return 2;}
var session=Guid.NewGuid();LauncherDiagnosticSink? log=null;Process? app=null;bool cleanup=false;
void Record(string name,object? data){log?.Record(name,data);}
try {
    var directory=Path.Combine(Path.GetDirectoryName(exe)!,"logs");
    var opened=DiagnosticSinkFactory.Open(new[]{(DiagnosticSinkLocation.Executable,(Func<DiagnosticLogWriter>)(()=>new DiagnosticLogWriter(directory,session,65536,20,DiagnosticLogFamily.Launcher)))});
    log=new LauncherDiagnosticSink(opened.Writer);
    Record("diagnostic_sink_initialized",new{location=opened.Location.ToString(),attempts=opened.Attempts,failures=opened.Failures,enumerationFailed=opened.EnumerationFailed,failureDetails=opened.FailureDetails.Select(f=>new{location=f.Location.ToString(),reason=f.Reason.ToString(),hresult=f.HResult}).ToArray()});
    string hash="unavailable";var dll=Path.ChangeExtension(exe,".dll");
    try{using var file=File.OpenRead(dll);hash=Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();}catch{}
    var requested=new List<object>();var config=Path.ChangeExtension(exe,".runtimeconfig.json");
    if(File.Exists(config) && new FileInfo(config).Length<=65536){
        try{using var doc=JsonDocument.Parse(File.ReadAllBytes(config));var options=doc.RootElement.GetProperty("runtimeOptions");
            var frameworks=new List<JsonElement>();if(options.TryGetProperty("framework",out var single))frameworks.Add(single);
            if(options.TryGetProperty("frameworks",out var list) && list.ValueKind==JsonValueKind.Array)frameworks.AddRange(list.EnumerateArray().Take(4));
            foreach(var framework in frameworks.Take(4)){
                var name=framework.GetProperty("name").GetString();
                if(name is "Microsoft.NETCore.App" or "Microsoft.WindowsDesktop.App" && Version.TryParse(framework.GetProperty("version").GetString(),out var version))requested.Add(new{name,version=version.ToString()});
            }
        }catch{}
    }
    Record("launch_requested",new{assemblySha256=hash,requestedFrameworks=requested,
        qaTimeoutSeconds=qaSeconds,rawOutputPersisted=false,launcherWaitsForChild=true,missingExitRecordIsUnknown=true});
    var info=new ProcessStartInfo(exe){WorkingDirectory=Path.GetDirectoryName(exe)!,UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
    info.Environment["COSMIC_DIAGNOSTIC_SESSION"]=session.ToString("N");
    app=new Process{StartInfo=info};if(!app.Start())throw new InvalidOperationException();
    var childClock=Stopwatch.StartNew();Record("process_started",new{childProcessId=app.Id});
    var stderr=new DiagnosticOutputDrain();var stdout=new DiagnosticOutputDrain();var drains=Task.WhenAll(stderr.Drain(app.StandardError,true),stdout.Drain(app.StandardOutput,false));
    bool window=false;
    while(!app.WaitForExit(200)){
        app.Refresh();if(!window && app.MainWindowHandle!=IntPtr.Zero){window=true;Record("native_window_observed",new{childProcessId=app.Id});}
        if(qaSeconds.HasValue && childClock.Elapsed.TotalSeconds>=qaSeconds.Value){
            cleanup=true;Record("qa_timeout_cleanup",new{childProcessId=app.Id,reason="explicit_owned_process_lifetime_bound"});app.Kill();if(!app.WaitForExit(2000)){Record("qa_cleanup_unconfirmed",new{childProcessId=app.Id});return 124;}break;
        }
    }
    var drained=await Task.WhenAny(drains,Task.Delay(2000))==drains;
    Record("process_exited",new{childProcessId=app.Id,exitCode=app.ExitCode,windowObserved=window,ownedQaCleanup=cleanup,
        outputDrained=drained&&stderr.Complete&&stdout.Complete,outputDrainTasksEnded=drained,stderrCharacters=stderr.Characters,stdoutCharacters=stdout.Characters,classifiedFailureKind=stderr.FailureKind,stdoutFailureKind=stdout.FailureKind});
    Console.WriteLine("Diagnostic session: "+session.ToString("N"));Console.WriteLine("Launch record: "+log.Path);return app.ExitCode;
}catch(Exception ex){
    if(qaSeconds.HasValue && app is not null){try{if(!app.HasExited){cleanup=true;app.Kill();var confirmed=app.WaitForExit(2000);Record("qa_exception_cleanup",new{confirmed});}}catch{Record("qa_exception_cleanup",new{confirmed=false});}}
    try{var type=ex.GetType().FullName;Record("launcher_exception",new{type=type is "System.ComponentModel.Win32Exception" or "System.IO.IOException" or "System.UnauthorizedAccessException" or "System.InvalidOperationException" or "System.ArgumentException" or "System.ObjectDisposedException" ? type : "Other",hresult=$"0x{ex.HResult:X8}",nativeError=ex is System.ComponentModel.Win32Exception native ? (int?)native.NativeErrorCode : null,ownedQaCleanup=cleanup});}catch{}
    Console.Error.WriteLine("Diagnostic launcher failed; inspect the local structured launcher record if available.");return 1;
}finally{
    try{app?.Dispose();}catch{Record("launcher_cleanup_failed",new{stage="process_dispose"});}
    Record("diagnostic_health",log?.Health);
    log?.Dispose();
    var health=log?.Health;
    try{Console.Error.WriteLine($"Diagnostic coverage: sinkUnavailable={health?.SinkUnavailable ?? true}; writeFailures={health?.WriteFailures ?? 0}; droppedRecords={health?.DroppedRecords ?? 0}; oversizedRecords={health?.OversizedRecords ?? 0}; pruneFailures={health?.PruneFailures ?? 0}; disposeFailures={health?.DisposeFailures ?? 0}. Missing or dropped evidence is incomplete.");}catch{}
}

