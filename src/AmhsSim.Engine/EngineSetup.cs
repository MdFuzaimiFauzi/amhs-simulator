using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using AmhsSim.Engine.Data;
using Serilog;
using Serilog.Filters;

namespace AmhsSim.Engine;

/// <summary>
/// What a host program calls to run the engine. Both front-end programs use these three methods,
/// so the engine is set up the same way whichever UI sits in front of it.
/// </summary>
public static class EngineSetup
{
    public const string FrameLogSource = "AmhsSim.Frames";

    /// <summary>App log to console + logs/{appName}-*.log, raw frames to logs/frames-*.log.</summary>
    public static void AddAmhsLogging(this WebApplicationBuilder builder, string appName)
    {
        builder.Host.UseSerilog((ctx, lc) => lc
            .ReadFrom.Configuration(ctx.Configuration)
            .WriteTo.Logger(app => app
                .Filter.ByExcluding(Matching.FromSource(FrameLogSource))
                .WriteTo.Console()
                .WriteTo.File($"logs/{appName}-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7))
            .WriteTo.Logger(frames => frames
                .Filter.ByIncludingOnly(Matching.FromSource(FrameLogSource))
                .WriteTo.File("logs/frames-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 3,
                    outputTemplate: "{Timestamp:HH:mm:ss.fff} {Message:lj}{NewLine}")));
    }

    /// <summary>Database, the three engine services, and the simulator client.</summary>
    public static IServiceCollection AddAmhsEngine(this IServiceCollection services, IConfiguration config)
    {
        // Which database is set in appsettings.json. A factory, because the engine services are
        // singletons and a DbContext is not thread-safe.
        var provider = DatabaseSetup.GetProvider(config);
        var connectionString = DatabaseSetup.GetConnectionString(config, provider);
        services.AddDbContextFactory<AppDbContext>(o => o.UseProvider(provider, connectionString));

        // Register each singleton once, then run that same instance as a hosted service.
        services.AddSingleton<VehicleManager>();
        services.AddHostedService(sp => sp.GetRequiredService<VehicleManager>());
        services.AddSingleton<AlarmService>();
        services.AddHostedService(sp => sp.GetRequiredService<AlarmService>());
        services.AddSingleton<JobManager>();
        services.AddHostedService(sp => sp.GetRequiredService<JobManager>());

        services.AddHttpClient<SimulatorClient>(c =>
            c.BaseAddress = new Uri(config["SimulatorUrl"] ?? "http://localhost:5100"));
        return services;
    }

    /// <summary>Creates or upgrades the database before anything uses it.</summary>
    public static void MigrateAmhsDatabase(this IServiceProvider services)
    {
        using var db = services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        db.Database.Migrate();
    }
}
