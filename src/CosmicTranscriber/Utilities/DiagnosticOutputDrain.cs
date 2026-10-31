namespace CosmicTranscriber.Utilities;

internal sealed class DiagnosticOutputDrain
{
    private long _characters;private int _complete;private volatile string? _failureKind;
    internal long Characters=>Interlocked.Read(ref _characters);
    internal bool Complete=>Volatile.Read(ref _complete)!=0;
    internal string? FailureKind=>_failureKind;
    internal async Task Drain(StreamReader reader,bool classify)
    {
        var buffer=new char[512];string tail="";
        try {
            int count;
            while((count=await reader.ReadAsync(buffer.AsMemory()))>0) {
                Interlocked.Add(ref _characters,count);
                if(classify) {
                    var sample=tail+new string(buffer,0,count);
                    if(_failureKind is null) {
                        if(sample.Contains("BadImageFormatException",StringComparison.Ordinal))_failureKind="bad_image_format";
                        else if(sample.Contains("FileNotFoundException",StringComparison.Ordinal))_failureKind="file_not_found";
                        else if(sample.Contains("DllNotFoundException",StringComparison.Ordinal))_failureKind="dll_not_found";
                        else if(sample.Contains("TypeInitializationException",StringComparison.Ordinal))_failureKind="type_initialization";
                        else if(sample.Contains("application to execute does not exist",StringComparison.OrdinalIgnoreCase))_failureKind="primary_assembly_missing";
                        else if(sample.Contains("You must install or update .NET",StringComparison.OrdinalIgnoreCase))_failureKind="required_runtime_missing";
                    }
                    tail=sample.Length>128?sample[^128..]:sample;
                }
            }
            Volatile.Write(ref _complete,1);
        } catch {_failureKind??="stream_read_error";}
        finally {tail="";Array.Clear(buffer);}
    }
}
