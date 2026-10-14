using AmhsSim.Protocol;

namespace AmhsSim.Engine;

public sealed record VehicleCandidate(string Name, bool Online, bool Busy, VehicleStatus? Status);

public static class VehiclePicker
{
    /// <summary>Picks the free vehicle with the shortest forward distance to the source port.</summary>
    public static string? PickNearest(IEnumerable<VehicleCandidate> candidates, int sourceMm) =>
        candidates
            .Where(c => c.Online && !c.Busy && c.Status is { State: VehicleState.Idle, CarrierPresent: false })
            .OrderBy(c => TrackLayout.ForwardDistance(c.Status!.PositionMm, sourceMm))
            .Select(c => c.Name)
            .FirstOrDefault();
}