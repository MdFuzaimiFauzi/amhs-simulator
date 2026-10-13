using System.Buffers.Binary;

namespace AmhsSim.Protocol;

/// <summary>The status every vehicle reports. 12 bytes on the wire.</summary>
public sealed record VehicleStatus(
    VehicleState State,
    SubStep Step,
    int PositionMm,
    int HoistMm,
    bool CarrierPresent,
    bool GripperClosed,
    AlarmCode Alarm)
{
    public const int Size = 12;

    public byte[] ToPayload()
    {
        var p = new byte[Size];
        p[0] = (byte)State;
        p[1] = (byte)Step;
        BinaryPrimitives.WriteInt32BigEndian(p.AsSpan(2, 4), PositionMm);
        BinaryPrimitives.WriteInt32BigEndian(p.AsSpan(6, 4), HoistMm);
        p[10] = (byte)((CarrierPresent ? 0b01 : 0) | (GripperClosed ? 0b10 : 0)); // digital inputs as bits
        p[11] = (byte)Alarm;
        return p;
    }

    public static VehicleStatus FromPayload(ReadOnlySpan<byte> p)
    {
        if (p.Length != Size) throw new FormatException($"Status payload must be {Size} bytes, got {p.Length}");
        return new VehicleStatus(
            (VehicleState)p[0],
            (SubStep)p[1],
            BinaryPrimitives.ReadInt32BigEndian(p.Slice(2, 4)),
            BinaryPrimitives.ReadInt32BigEndian(p.Slice(6, 4)),
            (p[10] & 0b01) != 0,
            (p[10] & 0b10) != 0,
            (AlarmCode)p[11]);
    }
}

/// <summary>Builders for request payloads, so callers never hand-pack bytes.</summary>
public static class Payloads
{
    public static byte[] Empty => [];

    public static byte[] MoveTo(int targetMm)
    {
        var p = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(p, targetMm);
        return p;
    }

    public static int ReadMoveTo(ReadOnlySpan<byte> p) => BinaryPrimitives.ReadInt32BigEndian(p);

    public static byte[] Port(byte portId) => [portId];

    public static byte[] Nak(NakReason reason) => [(byte)reason];
}