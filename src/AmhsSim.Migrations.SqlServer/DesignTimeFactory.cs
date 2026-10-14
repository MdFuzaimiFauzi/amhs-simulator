using AmhsSim.Engine.Data;
using Microsoft.EntityFrameworkCore.Design;

namespace AmhsSim.Migrations.SqlServer;

/// <summary>
/// Lets "dotnet ef migrations add" build an AppDbContext for SQL Server without starting a program.
/// The connection string is only used to pick the SQL dialect; nothing connects to it.
/// </summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        DatabaseSetup.CreateContext(DatabaseProvider.SqlServer, "Server=localhost;Database=Sim;TrustServerCertificate=True");
}