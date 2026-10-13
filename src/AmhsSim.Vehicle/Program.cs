using AmhsSim.Vehicle;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(builder.Configuration).WriteTo.Console());

builder.Services.AddSingleton<SimulatorHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SimulatorHost>());

var app = builder.Build();

// Small HTTP API so the controller's Fault page can break things on purpose.
app.MapGet("/vehicles", (SimulatorHost host) =>
    host.Servers.Select(s => new { s.Vehicle.Name, s.Port, Status = s.Vehicle.GetStatus(), s.Vehicle.Faults }));

app.MapPost("/vehicles/{name}/faults/{fault}", (string name, string fault, int? value, SimulatorHost host) =>
{
    var server = host.Find(name);
    if (server is null) return Results.NotFound();
    var f = server.Vehicle.Faults;
    switch (fault.ToLowerInvariant())
    {
        case "drop": server.DropConnection(); break;
        case "hang" : f.Hang = !f.Hang; break;
        case "Latency" : f.LatencyMs = value ?? 0; break; 
        case "corrupt" : f.CorruptNextReply = true; break;
        case "jam" : f.JamAxis = !f.JamAxis; break;
        case "losecarrier" : f.LoseCarrier = true; break;
        case "clear": 
            f.Hang = false;
            f.LatencyMs = 0;
            f.CorruptNextReply = false;
            f.JamAxis = false;
            f.LoseCarrier = false;
            break;
        default: return Results.BadRequest($"Unknown fault '{fault}'");
    }

    return Results.Ok(f);
});

app.Run();
