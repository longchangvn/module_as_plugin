// EF Core catalog store — §10.1. PostgreSQL (Npgsql) only.
// Platform.Api requires ConnectionStrings:Catalog (no InMemory fallback).
// Gateway rules are recorded here (platform level), never in tenant databases.
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
    public DbSet<GatewayRule> GatewayRules => Set<GatewayRule>();

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
            e.Ignore(x => x.HasCustomConnection);
        });
        b.Entity<PlatformModule>(e => e.HasKey(x => x.Id));
        b.Entity<ModuleVersion>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ModuleId, x.Version }).IsUnique();
            e.Property(x => x.ManifestYaml).HasColumnType("text");
            e.Property(x => x.ModuleType).HasDefaultValue("remote");
        });
        b.Entity<TenantModuleInstall>(e => e.HasKey(x => new { x.TenantId, x.ModuleId }));
        b.Entity<TenantAudit>(e => e.HasKey(x => x.Id));
        b.Entity<GatewayRule>(e => { e.HasKey(x => x.Id); e.ToTable("PlatformGatewayRules"); });
    }
}
