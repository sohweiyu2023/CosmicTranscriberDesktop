using System.Security.Cryptography;
using System.Text;
using CosmicTranscriber.Models;
using CosmicTranscriber.Services;
using NAudio.Wave;

internal static class CoreFramePreservation
{
    internal static int Verify(string source, IReadOnlyList<AudioChunk> chunks)
    {
        int checks=0;
        void Check(bool ok,string name){if(!ok)throw new Exception("FAIL Core frame preservation: "+name);checks++;Console.WriteLine("PASS Core frame preservation: "+name);}
        static List<Mp3Frame> Frames(string path){using var stream=File.OpenRead(path);Mp3AudioUtilities.SkipLeadingId3V2(stream);var result=new List<Mp3Frame>();Mp3Frame? frame;while((frame=Mp3Frame.LoadFromStream(stream)) is not null)result.Add(frame);return result;}
        var before=SHA256.HashData(File.ReadAllBytes(source));
        var original=Frames(source);
        Check(original.Count>1 && Mp3AudioUtilities.IsVbrMetadataFrame(original[0]),"synthetic VBR fixture has reservoir carrier");
        var logical=original.Skip(1).ToArray();int position=0;
        foreach(var chunk in chunks){
            var frames=Frames(chunk.Path);bool carrier=frames.Count>0 && Mp3AudioUtilities.IsVbrMetadataFrame(frames[0]);
            Check(carrier==(chunk.Index==1),"only first chunk retains VBR carrier "+chunk.Index);
            if(carrier){
                var old=original[0].RawData!;var current=frames[0].RawData!;
                int tag=Encoding.ASCII.GetString(old).IndexOf("Xing",StringComparison.Ordinal);
                if(tag<0)tag=Encoding.ASCII.GetString(old).IndexOf("Info",StringComparison.Ordinal);
                Check(tag>=0,"fixture Xing/Info header recognized");
                uint flags=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(old.AsSpan(tag+4,4));int cursor=tag+8;
                var allowedChanges=new HashSet<int>();
                if((flags&1)!=0){Check(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(current.AsSpan(cursor,4))==frames.Count,"rewritten VBR frame count is chunk-local");for(int n=0;n<4;n++)allowedChanges.Add(cursor++);}
                if((flags&2)!=0){Check(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(current.AsSpan(cursor,4))==new FileInfo(chunk.Path).Length,"rewritten VBR byte count is chunk-local");for(int n=0;n<4;n++)allowedChanges.Add(cursor++);}
                if((flags&4)!=0)for(int n=0;n<100;n++)allowedChanges.Add(cursor++);
                Check(old.Length==current.Length && Enumerable.Range(0,old.Length).All(i=>allowedChanges.Contains(i)||old[i]==current[i]),"carrier bytes outside count/TOC fields preserved");
                frames.RemoveAt(0);
            }
            int preroll=0;var duration=TimeSpan.Zero;
            while(preroll<frames.Count && duration<chunk.PrerollDuration){var frame=frames[preroll++];duration+=TimeSpan.FromSeconds((double)frame.SampleCount/frame.SampleRate);}
            Check(Math.Abs((duration-chunk.PrerollDuration).TotalMilliseconds)<0.01,"whole-frame preroll accounting "+chunk.Index);
            Check(position>=preroll && Enumerable.Range(0,preroll).All(i=>frames[i].RawData!.SequenceEqual(logical[position-preroll+i].RawData!)),"preroll retains preceding source bytes "+chunk.Index);
            var actual=frames.Skip(preroll).ToArray();
            Check(position+actual.Length<=logical.Length && Enumerable.Range(0,actual.Length).All(i=>actual[i].RawData!.SequenceEqual(logical[position+i].RawData!)),"logical audio frames byte-identical and ordered "+chunk.Index);
            position+=actual.Length;
            Check(Math.Abs((Mp3AudioUtilities.ReadDuration(chunk.Path)-chunk.Duration-chunk.PrerollDuration).TotalMilliseconds)<1,"standalone chunk duration includes only logical plus preroll "+chunk.Index);
        }
        Check(position==logical.Length,"every source audio frame retained exactly once after overlap removal");
        Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))),"source fixture unchanged");
        return checks;
    }
}
