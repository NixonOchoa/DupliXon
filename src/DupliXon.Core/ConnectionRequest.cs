namespace DupliXon.Core;

/// <summary>A receiver asking to mirror this PC; the user must accept it.</summary>
public sealed record ConnectionRequest(Guid Id, DeviceInfo Device, string RemoteAddress);

public enum SessionState { Pending, Connecting, Streaming, Ended }
