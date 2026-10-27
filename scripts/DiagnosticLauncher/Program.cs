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
var session=Guid.NewGuid();DiagnosticLogWriter? log=null;Process? app=null;bool cleanup=false;
void Record(string name,object? data){try{log?.Write("LAUNCHER",name,data);}catch{}}
try {
    var directory=Path.Combine(Path.GetDirectoryName(exe)!,"logs");
    log=new DiagnosticLogWriter(directory,session,65536,20,DiagnosticLogFamily.Launcher);
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
    Record("launch_requested",new{executablePath=exe.Length<=1024?exe:"path_omitted",assemblySha256=hash,requestedFrameworks=requested,
        qaTimeoutSeconds=qaSeconds,rawOutputPersisted=false,launcherWaitsForChild=true,missingExitRecordIsUnknown=true});
    var info=new ProcessStartInfo(exe){WorkingDirectory=Path.GetDirectoryName(exe)!,UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
    info.Environment["COSMIC_DIAGNOSTIC_SESSION"]=session.ToString("N");
    app=new Process{StartInfo=info};if(!app.Start())throw new InvalidOperationException();
    var childClock=Stopwatch.StartNew();Record("process_started",new{childProcessId=app.Id});
    var stderr=new OutputDrain();var stdout=new OutputDrain();var drains=Task.WhenAll(stderr.Drain(app.StandardError,true),stdout.Drain(app.StandardOutput,false));
    bool window=false;
    while(!app.WaitForExit(200)){
        app.Refresh();if(!window && app.MainWindowHandle!=IntPtr.Zero){window=true;Record("native_window_observed",new{childProcessId=app.Id});}
        if(qaSeconds.HasValue && childClock.Elapsed.TotalSeconds>=qaSeconds.Value){
            cleanup=true;Record("qa_timeout_cleanup",new{childProcessId=app.Id,reason="explicit_owned_process_lifetime_bound"});app.Kill();if(!app.WaitForExit(2000)){Record("qa_cleanup_unconfirmed",new{childProcessId=app.Id});return 124;}break;
        }
    }
    var drained=await Task.WhenAny(drains,Task.Delay(2000))==drains;
    Record("process_exited",new{childProcessId=app.Id,exitCode=app.ExitCode,windowObserved=window,ownedQaCleanup=cleanup,
        outputDrained=drained,stderrCharacters=stderr.Characters,stdoutCharacters=stdout.Characters,classifiedFailureKind=stderr.FailureKind});
    Console.WriteLine("Diagnostic session: "+session.ToString("N"));Console.WriteLine("Launch record: "+log.Path);return app.ExitCode;
}catch(Exception ex){
    if(qaSeconds.HasValue && app is not null){try{if(!app.HasExited){cleanup=true;app.Kill();var confirmed=app.WaitForExit(2000);Record("qa_exception_cleanup",new{confirmed});}}catch{Record("qa_exception_cleanup",new{confirmed=false});}}
    try{Record("launcher_exception",new{type=ex.GetType().FullName,hresult=$"0x{ex.HResult:X8}",ownedQaCleanup=cleanup});}catch{}
    Console.Error.WriteLine("Diagnostic launcher failed; inspect the local structured launcher record if available.");return 1;
}finally{try{app?.Dispose();}catch{}try{log?.Dispose();}catch{}}

internal sealed class OutputDrain {
    public long Characters;public string? FailureKind;
    public async Task Drain(StreamReader reader,bool classify){
        var buffer=new char[512];string tail="";
        try{int count;while((count=await reader.ReadAsync(buffer.AsMemory()))>0){
            Characters+=count;
            if(classify){var sample=tail+new string(buffer,0,count);
                if(FailureKind is null){
                    if(sample.Contains("BadImageFormatException",StringComparison.Ordinal))FailureKind="bad_image_format";
                    else if(sample.Contains("FileNotFoundException",StringComparison.Ordinal))FailureKind="file_not_found";
                    else if(sample.Contains("DllNotFoundException",StringComparison.Ordinal))FailureKind="dll_not_found";
                    else if(sample.Contains("TypeInitializationException",StringComparison.Ordinal))FailureKind="type_initialization";
                    else if(sample.Contains("application to execute does not exist",StringComparison.OrdinalIgnoreCase))FailureKind="primary_assembly_missing";
                    else if(sample.Contains("You must install or update .NET",StringComparison.OrdinalIgnoreCase))FailureKind="required_runtime_missing";
                }
                tail=sample.Length>128?sample[^128..]:sample;
            }
        }}catch{FailureKind??="stream_read_error";}finally{tail="";}
    }
}

