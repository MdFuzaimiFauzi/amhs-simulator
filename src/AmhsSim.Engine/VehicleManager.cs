using AmhsSim.Protocol;

namespace AmhsSim.Engine;

public sealed class VehicleEndpoint
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; }
}

/// <summary>Owns one VehicleConnection per configured vehicle and keeps them running.</summary>
public sealed class VehicleManager : BackgroundService
{
    public VehicleManager(IConfiguration config, ILoggerFactory loggerFactory)
    {
        var frameLog = loggerFactory.CreateLogger(EngineSetup.FrameLogSource);
        var endpoints = config.GetSection("Vehicles").Get<List<VehicleEndpoint>>() ?? [];
        Connections = endpoints
            .Select(e => new VehicleConnection(e.Name, e.Host, e.Port, loggerFactory.CreateLogger<VehicleConnection>(), frameLog))
            .ToList();
        foreach (var v in Connections) v.Changed += c => VehicleChanged?.Invoke(c);
    }

    public IReadOnlyList<VehicleConnection> Connections { get; }

    public event Action<VehicleConnection>? VehicleChanged;

    public VehicleConnection? Find(string name) =>
        Connections.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sends a manual command (Home, ResetAlarm, Stop) from an operator screen.
    /// Returns null when the vehicle accepted it, otherwise the reason it didn't.
    /// </summary>
    public async Task<string?> SendManualCommandAsync(string vehicleName, MessageType type)
    {
        if (type is not (MessageType.Home or MessageType.ResetAlarm or MessageType.Stop))
            return $"{type} is not a manual command";
        var v = Find(vehicleName);
        if (v is null) return $"Unknown vehicle {vehicleName}";
        try
        {
            var reply = await v.SendAsync(type, Payloads.Empty);
            return reply.Type == MessageType.Nak ? $"{v.Name} rejected {type}: {(NakReason)reply.Payload[0]}" : null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    protected override Task ExecuteAsync(CancellationToken ct) =>
        Task.WhenAll(Connections.Select(v => v.RunAsync(ct)));
}
