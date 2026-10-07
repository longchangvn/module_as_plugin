using Auth.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Auth.Api.Infrastructure;

// Design-time factory so `dotnet ef` always scaffolds against PostgreSQL,
// even though runtime falls back to InMemory locally.
public sealed class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql("Host=localhost;Username=module_platform;Password=module_platform;Database=module_platform")
            .Options;
        return new TenantDbContext(options);
    }
}
