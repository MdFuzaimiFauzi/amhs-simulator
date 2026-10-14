using AmhsSim.Engine.Data;
using Microsoft.EntityFrameworkCore.Design;

namespace AmhsSim.Migrations.Sqlite;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        DatabaseSetup.CreateContext(DatabaseProvider.Sqlite, "Data Source=design-time.db");
}