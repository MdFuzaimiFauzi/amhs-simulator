namespace AmhsSim.Vehicle;

public sealed class VehicleConfig
{
    public string Name { get; set; } = "";
    public int Port { get; set; }
    public int StartPositionMm { get; set; }
    public bool StartWithCarrier { get; set; }
}

/// <summary>Creates the vehicles from config, runs their TCP servers and one shared motion clock.</summary>
public sealed class SimulatorHost : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    public SimulatorHost(IConfiguration config, ILoggerFactory loggerFactory)
    {
        var vehicles = config.GetSection("Vehicles").Get<List<VehicleConfig>>() ?? [];
        Servers = vehicles
            .Select(v => new VehicleServer(
                new SimulatedVehicle(v.Name, v.StartPositionMm, v.StartWithCarrier),
                v.Port,
                loggerFactory.CreateLogger($"Vehicle.{v.Name}")))
            .ToList();
    }

    public IReadOnlyList<VehicleServer> Servers { get; }

    public VehicleServer? Find(string name) =>
        Servers.FirstOrDefault(s => s.Vehicle.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var tasks = Servers.Select(s => s.RunAsync(ct)).ToList();
        tasks.Add(MotionLoopAsync(ct));
        await Task.WhenAll(tasks);
    }

    private async Task MotionLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                foreach (var s in Servers) s.Vehicle.Tick(TickInterval);
        }
        catch (OperationCanceledException) { }
    }
}
