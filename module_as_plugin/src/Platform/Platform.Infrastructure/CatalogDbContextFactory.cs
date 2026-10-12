using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.Infrastructure;

// Design-time factory so `dotnet ef` always scaffolds against the relational
// (PostgreSQL) provider.
//
// Uses the least-privilege migration role (LOGIN + CREATEDB). Override with
// MIGRATION_CONNECTION_STRING, e.g.:
//   MIGRATION_CONNECTION_STRING="Host=localhost;Port=5433;Username=module_migrator;Password=secret;Database=module_platform" \
//     dotnet ef migrations add MyChange --project src/Platform/Platform.Infrastructure
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var conn = System.Environment.GetEnvironmentVariable("MIGRATION_CONNECTION_STRING")
            ?? "Host=localhost;Username=module_migrator;Password=module_migrator;Database=module_platform";
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(conn)
            .Options;
        return new CatalogDbContext(options);
    }
}
