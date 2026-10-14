using System.Net;
using System.Net.Http.Json;

namespace AmhsSim.Engine;

public sealed record SimFaults(bool Hang, int LatencyMs, bool CorruptNextReply, bool JamAxis, bool LoseCarrier);

public sealed record SimVehicle(string Name, int Port, SimFaults Faults);

/// <summary>Typed HttpClient for the vehicle simulator's fault API, used by both programs.</summary>
public sealed class SimulatorClient(HttpClient http)
{
    public static readonly IReadOnlyList<string> FaultNames = ["drop", "hang", "latency", "corrupt", "jam", "losecarrier", "clear"];

    public async Task<List<SimVehicle>> GetVehiclesAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<SimVehicle>>("vehicles", ct) ?? [];

    public async Task<HttpStatusCode> InjectAsync(string vehicle, string fault, int? value = null, CancellationToken ct = default)
    {
        var url = $"vehicles/{Uri.EscapeDataString(vehicle)}/faults/{Uri.EscapeDataString(fault)}"
                  + (value is null ? "" : $"?value={value}");
        using var response = await http.PostAsync(url, null, ct);
        return response.StatusCode;
    }
}