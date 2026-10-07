using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.Infrastructure;

// Design-time factory so `dotnet ef` always scaffolds against the relational
// (PostgreSQL) provider, even though runtime falls back to InMemory locally.
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Username=module_platform;Password=module_platform;Database=module_platform")
            .Options;
        return new CatalogDbContext(options);
    }
}
