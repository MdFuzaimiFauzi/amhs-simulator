using System.Buffers;
using System.Buffers.Binary;

namespace AmhsSim.Protocol;

/// <summary>
/// Frame layout (big-endian):
/// [0] STX 0x02 | [1] type | [2..3] payload length | [4] tx id | [5..] payload | [last] XOR checksum of bytes 1..end of payload
/// </summary>
public static class FrameCodec
{
    public const byte Stx = 0x02;
    public const int HeaderSize = 5;
    public const int MaxPayload = 1024;

    public static byte[] Encode(Frame frame)
    {
        if (frame.Payload.Length > MaxPayload)
            throw new ArgumentException($"Payload too long: {frame.Payload.Length}");

        var bytes = new byte[HeaderSize + frame.Payload.Length + 1];
        bytes[0] = Stx;
        bytes[1] = (byte)frame.Type;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2, 2), (ushort)frame.Payload.Length);
        bytes[4] = frame.TxId;
        frame.Payload.CopyTo(bytes, HeaderSize);
        bytes[^1] = Checksum(bytes.AsSpan(1, bytes.Length - 2));
        return bytes;
    }

    public static byte Checksum(ReadOnlySpan<byte> data)
    {
        byte x = 0;
        foreach (var b in data) x ^= b;
        return x;
    }

    /// <summary>
    /// Tries to cut one complete frame from the front of <paramref name="buffer"/>.
    /// On success the buffer is advanced past the frame. Bytes before an STX, and frames with a
    /// bad checksum or impossible length, are skipped so the reader re-synchronises.
    /// Returns false when more bytes are needed.
    /// </summary>
    public static bool TryRead(ref ReadOnlySequence<byte> buffer, out Frame? frame)
    {
        frame = null;
        while (true)
        {
            SequencePosition? stx = buffer.PositionOf(Stx);
            if (stx is null)
            {
                buffer = buffer.Slice(buffer.End); // nothing useful, drop it all
                return false;
            }
            buffer = buffer.Slice(stx.Value);

            if (buffer.Length < HeaderSize) return false;

            Span<byte> header = stackalloc byte[HeaderSize];
            buffer.Slice(0, HeaderSize).CopyTo(header);
            int length = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(2, 2));
            if (length > MaxPayload)
            {
                buffer = buffer.Slice(1); // not a real frame start, keep scanning
                continue;
            }

            long total = HeaderSize + length + 1;
            if (buffer.Length < total) return false;

            byte[] bytes = buffer.Slice(0, total).ToArray();
            if (Checksum(bytes.AsSpan(1, bytes.Length - 2)) != bytes[^1])
            {
                buffer = buffer.Slice(1);
                continue;
            }

            frame = new Frame((MessageType)bytes[1], bytes[4], bytes.AsSpan(HeaderSize, length).ToArray());
            buffer = buffer.Slice(total);
            return true;
        }
    }
}
