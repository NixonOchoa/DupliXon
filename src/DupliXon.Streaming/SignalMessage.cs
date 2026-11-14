using System.Text.Json.Serialization;

namespace DupliXon.Streaming;

/// <summary>JSON envelope exchanged over the signaling WebSocket.</summary>
public sealed class SignalMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("sdp")] public string? Sdp { get; set; }
    [JsonPropertyName("candidate")] public string? Candidate { get; set; }
    [JsonPropertyName("sdpMid")] public string? SdpMid { get; set; }
    [JsonPropertyName("sdpMLineIndex")] public ushort? SdpMLineIndex { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("dpr")] public double? Dpr { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("info")] public string? Info { get; set; }
}
