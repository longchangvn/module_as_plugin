// EF Core catalog store — §10.1. PostgreSQL (Npgsql) in production;
// Platform.Api wires UseNpgsql or an InMemory fallback via configuration.
using Microsoft.EntityFrameworkCore;
using Platform.Domain;

namespace Platform.Infrastructure;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<PlatformModule> Modules => Set<PlatformModule>();
    public DbSet<ModuleVersion> ModuleVersions => Set<ModuleVersion>();
    public DbSet<TenantModuleInstall> Installs => Set<TenantModuleInstall>();
    public DbSet<TenantAudit> Audits => Set<TenantAudit>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PlatformUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320);
        });
        b.Entity<Tenant>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.Slug).IsUnique();
        });
        b.Entity<PlatformModule>(e => e.HasKey(x => x.Id));
        b.Entity<ModuleVersion>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ModuleId, x.Version }).IsUnique();
            e.Property(x => x.ManifestYaml).HasColumnType("text");
        });
        b.Entity<TenantModuleInstall>(e => e.HasKey(x => new { x.TenantId, x.ModuleId }));
        b.Entity<TenantAudit>(e => e.HasKey(x => x.Id));
    }
}
