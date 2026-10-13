using System.Buffers;

namespace AmhsSim.Protocol;

public sealed record DecodedFrame(MessageType Type, byte TypeCode, byte TxId, int PayloadLength, string? Detail);

public sealed record DecodeResult(IReadOnlyList<DecodedFrame> Frames, string? Error);

/// <summary>
/// Turns hex copied from logs/frames-*.log into readable frames.
/// Lives in the protocol library so both UIs show exactly the same decoding.
/// </summary>
public static class HexDecoder
{
    public static DecodeResult Decode(string hex)
    {
        try
        {
            var clean = new string(hex.Where(Uri.IsHexDigit).ToArray());
            var buffer = new ReadOnlySequence<byte>(Convert.FromHexString(clean));
            var frames = new List<DecodedFrame>();
            while (FrameCodec.TryRead(ref buffer, out var frame))
                frames.Add(new DecodedFrame(frame!.Type, (byte)frame.Type, frame.TxId, frame.Payload.Length, Describe(frame)));

            string? error = frames.Count == 0 ? "No complete frame with a valid checksum found."
                : buffer.Length > 0 ? $"{buffer.Length} trailing bytes are an incomplete frame."
                : null;
            return new DecodeResult(frames, error);
        }
        catch (FormatException ex)
        {
            return new DecodeResult([], ex.Message);
        }
    }

    private static string? Describe(Frame f) => f switch
    {
        { Type: MessageType.Status, Payload.Length: VehicleStatus.Size } => VehicleStatus.FromPayload(f.Payload).ToString(),
        { Type: MessageType.Nak, Payload.Length: 1 } => $"Reason: {(NakReason)f.Payload[0]}",
        { Type: MessageType.MoveTo, Payload.Length: 4 } => $"Target: {Payloads.ReadMoveTo(f.Payload)} mm",
        { Payload.Length: 1 } => $"Port: {TrackLayout.FindPort(f.Payload[0])?.Name ?? "?"}",
        _ => null,
    };
}
