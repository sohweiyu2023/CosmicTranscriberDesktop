using System.Text.Json;

namespace CosmicTranscriber.Utilities;

/// <summary>Only fixed classifications/metadata persist. Arbitrary provider prose is never diagnostic data.</summary>
public sealed class ProviderFailureEvidence
{
    public string BodyState { get; }
    public string Format { get; }
    public IReadOnlyList<string> Explanations { get; }
    public bool Redacted { get; }
    public bool Truncated { get; }
    public bool MissingExplanation { get; }
    public IReadOnlyList<string> MissingFields { get; }
    public IReadOnlyDictionary<string,string> Fields { get; }
    public IReadOnlyList<string> UnrecognizedFields { get; }
    public bool ProviderProseObserved { get; }
    public int ExplanationFieldsObserved { get; }
    public bool OriginalExplanationOmitted => ProviderProseObserved;
    public string OriginalExplanationState => ProviderProseObserved ? "OmittedByPolicy" : BodyState == "Received" && Format == "Json" && !Truncated ? "NotObservedInAllowlistedFields" : "UnavailableOrUninspected";
    public string ExplanationSource => Explanations.Count > 0 ? "FixedCodeClassification" : "Unavailable";
    public string Coverage => "AllowlistedFieldsOnly";
    public const int MaximumBodyCharacters=65536, MaximumExplanations=8;

    private ProviderFailureEvidence(string state,string format,Dictionary<string,string> fields,
        bool prose,int observed,bool truncated)
    {
        BodyState=state;Format=format;Fields=new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(fields);
        UnrecognizedFields=Array.AsReadOnly(fields.Where(x=>x.Value=="Other").Select(x=>x.Key).ToArray());
        ProviderProseObserved=prose;ExplanationFieldsObserved=observed;Truncated=truncated;
        MissingExplanation=!prose;Redacted=prose||UnrecognizedFields.Count>0;
        MissingFields=Array.AsReadOnly(new[]{"explanation","code","type","param"}.Where(k=>k=="explanation"?!prose:!fields.ContainsKey(k)).ToArray());
        var classification=fields.TryGetValue("code",out var code)?code switch {
            "invalid_api_key"=>"The provider classified the credential as invalid.",
            "insufficient_quota"=>"The provider classified the failure as insufficient quota.",
            "rate_limit_exceeded"=>"The provider classified the failure as a rate limit.",
            "model_not_found"=>"The provider classified the requested model as not found.",
            "context_length_exceeded"=>"The provider classified the request as exceeding context length.",
            "invalid_value"=>"The provider classified a request value as invalid.",
            "server_error"=>"The provider classified the failure as a server error.",
            "unsupported_value"=>"The provider classified a request value as unsupported.",
            _=>null }:null;
        Explanations=Array.AsReadOnly(classification is null?Array.Empty<string>():new[]{classification});
    }
    public static ProviderFailureEvidence Unavailable(string state)=>new(
        state is "Timeout" or "Cancelled" or "BodyReadFailed" or "TransportUnavailable" or "Malformed" or "Oversized" or "InvalidUtf8"?state:"Unavailable",
        "Unavailable",new(),false,0,state=="Oversized");
    public static ProviderFailureEvidence Capture(string body)
    {
        try{return CaptureCore(body);}catch{return new("InspectionUnavailable","Unavailable",new(),false,0,true);}
    }
    private static ProviderFailureEvidence CaptureCore(string body)
    {
        if(body.Length==0)return new("Empty","Unavailable",new(),false,0,false);
        if(body.Length>MaximumBodyCharacters)return new("Oversized","Unavailable",new(),false,0,true);
        var fields=new Dictionary<string,string>();bool prose=false,limited=false;int observed=0,visited=0;
        void Observe(string text) {
            if(string.IsNullOrWhiteSpace(text))return;
            prose=true;
            if(observed<MaximumExplanations)observed++;else limited=true;
        }
        void Visit(JsonElement node,int depth,bool explanation=false) {
            if(++visited>128||depth>6){limited=true;return;}
            if(node.ValueKind==JsonValueKind.String){if(explanation)Observe(node.GetString()??"");return;}
            if(node.ValueKind==JsonValueKind.Array){foreach(var value in node.EnumerateArray()){if(visited>=128){limited=true;break;}Visit(value,depth+1,explanation);}return;}
            if(node.ValueKind!=JsonValueKind.Object)return;
            foreach(var property in node.EnumerateObject()) {
                var name=property.Name.ToLowerInvariant();
                if(name is "code" or "type" or "param") {
                    if(fields.ContainsKey(name)||property.Value.ValueKind is not(JsonValueKind.String or JsonValueKind.Number))continue;
                    var raw=property.Value.ToString();
                    var allowed=name switch {
                        "code"=>new[]{"invalid_api_key","insufficient_quota","rate_limit_exceeded","model_not_found","context_length_exceeded","invalid_value","server_error","unsupported_value"},
                        "type"=>new[]{"invalid_request_error","authentication_error","permission_error","rate_limit_error","server_error","insufficient_quota"},
                        _=>new[]{"model","file","language","languages","prompt","keywords","response_format","chunking_strategy"}};
                    fields[name]=allowed.Contains(raw,StringComparer.Ordinal)?raw:"Other";
                }
                else if(name is "message" or "reason" or "description" or "detail" or "error" or "errors" or "details" or "cause" or "innererror")Visit(property.Value,depth+1,true);
            }
        }
        try {using var document=JsonDocument.Parse(body,new JsonDocumentOptions{MaxDepth=16});Visit(document.RootElement,0,true);}
        catch(JsonException) {
            if(body.TrimStart().StartsWith('{')||body.TrimStart().StartsWith('['))return new("MalformedOrDepthLimited","Json",new(),false,0,true);
            if(body.Contains('<')||body.Contains('>'))return new("Received","HtmlOmitted",new(),true,1,false);
            return new("Received","PlainText",new(),true,1,false);
        }
        return new("Received","Json",fields,prose,observed,limited);
    }
}
