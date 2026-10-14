using Microsoft.EntityFrameworkCore;

namespace AmhsSim.Engine.Data;

public enum DatabaseProvider { Sqlite, SqlServer, Postgres, Oracle }

/// <summary>
/// The only class that knows which database brands exist. Everything else just uses AppDbContext.
/// </summary>
public static class DatabaseSetup
{
    /// <summary>"Database:Provider" in appsettings.json; SQLite when it's missing.</summary>
    public static DatabaseProvider GetProvider(IConfiguration config)
    {
        var name = config["Database:Provider"] ?? nameof(DatabaseProvider.Sqlite);
        return Enum.TryParse<DatabaseProvider>(name, ignoreCase: true, out var provider)
            ? provider
            : throw new InvalidOperationException(
                $"Unknown Database:Provider '{name}'. Use one of: {string.Join(", ", Enum.GetNames<DatabaseProvider>())}");
    }

    /// <summary>Each provider has its own entry under ConnectionStrings, named after it.</summary>
    public static string GetConnectionString(IConfiguration config, DatabaseProvider provider) =>
        config.GetConnectionString(provider.ToString())
        ?? throw new InvalidOperationException($"ConnectionStrings:{provider} is missing from appsettings.json");

    /// <summary>
    /// Each provider keeps its migrations in its own project, because the SQL they contain is different.
    /// </summary>
    public static string MigrationsAssembly(DatabaseProvider provider) => $"AmhsSim.Migrations.{provider}";

    public static DbContextOptionsBuilder UseProvider(this DbContextOptionsBuilder options, DatabaseProvider provider, string connectionString)
    {
        var migrations = MigrationsAssembly(provider);
        return provider switch
        {
            DatabaseProvider.Sqlite => options.UseSqlite(connectionString, o => o.MigrationsAssembly(migrations)),
            DatabaseProvider.SqlServer => options.UseSqlServer(connectionString, o => o.MigrationsAssembly(migrations)),
            DatabaseProvider.Postgres => options.UseNpgsql(connectionString, o => o.MigrationsAssembly(migrations)),
            DatabaseProvider.Oracle => options.UseOracle(connectionString, o => o.MigrationsAssembly(migrations)),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
    }

    public static AppDbContext CreateContext(DatabaseProvider provider, string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseProvider(provider, connectionString);
        return new AppDbContext(options.Options);
    }
}