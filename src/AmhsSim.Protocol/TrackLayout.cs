namespace AmhsSim.Protocol;

public sealed record Port(byte Id, string Name, int PositionMm);

/// <summary>
/// A one-way loop rail, like a real OHT bay. Vehicles only drive forward, so the distance
/// from A to B is measured forward around the loop.
/// </summary>
public static class TrackLayout
{
    public const int LengthMm = 120_000;
    public const int PositionToleranceMm = 5;

    public static readonly IReadOnlyList<Port> Ports =
    [
        new(1, "A", 5_000),
        new(2, "B", 25_000),
        new(3, "C", 45_000),
        new(4, "D", 65_000),
        new(5, "E", 85_000),
        new(6, "F", 105_000),
    ];

    public static Port? FindPort(byte id) => Ports.FirstOrDefault(p => p.Id == id);

    public static Port? FindPort(string name) =>
        Ports.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static int ForwardDistance(int fromMm, int toMm) =>
        ((toMm - fromMm) % LengthMm + LengthMm) % LengthMm;

    public static bool IsAt(int positionMm, int targetMm) =>
        Math.Min(ForwardDistance(positionMm, targetMm), ForwardDistance(targetMm, positionMm)) <= PositionToleranceMm;
}