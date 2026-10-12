using Auth.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Auth.Api.Infrastructure;

// Design-time factory so `dotnet ef` always scaffolds against PostgreSQL,
// even though runtime falls back to InMemory locally.
//
// Uses the least-privilege migration role (LOGIN + CREATEDB). Override with
// MIGRATION_CONNECTION_STRING (same variable as the catalog factory).
public sealed class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var conn = System.Environment.GetEnvironmentVariable("MIGRATION_CONNECTION_STRING")
            ?? "Host=localhost;Username=module_migrator;Password=module_migrator;Database=module_platform";
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(conn)
            .Options;
        return new TenantDbContext(options);
    }
}
