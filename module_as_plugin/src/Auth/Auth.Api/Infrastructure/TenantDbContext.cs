using Auth.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Auth.Api.Infrastructure;

// v1: single logical tenant store. Production maps one database/schema per tenant
// (connection resolved from tenant resolution); the scaffold uses one database with
// the same shape so seed/resolve logic is identical.
//
// Per-tenant mode (TenantDbProvider): one TenantDbContext per tenant DB, or one
// schema per shared tenant via SchemaName + TenantModelCacheKeyFactory (the EF
// model cache is keyed per schema, mirroring §27b-style schema stamping).
public sealed class TenantDbContext : DbContext
{
    private readonly DbContextOptions<TenantDbContext> _options;

    public string? SchemaName { get; }

    public TenantDbContext(DbContextOptions<TenantDbContext> options) : base(options)
    {
        _options = options;
    }

    public TenantDbContext(DbContextOptions<TenantDbContext> options, string? schemaName) : base(options)
    {
        _options = options;
        SchemaName = string.IsNullOrWhiteSpace(schemaName) || schemaName == "public" ? null : schemaName;
    }
    public DbSet<TenantUser> Users => Set<TenantUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserAdditionalPermission> UserAdditionalPermissions => Set<UserAdditionalPermission>();
    public DbSet<Menu> Menus => Set<Menu>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        if (!string.IsNullOrEmpty(SchemaName))
            b.HasDefaultSchema(SchemaName);
        b.Entity<TenantUser>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Email).IsUnique(); });
        b.Entity<Role>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Name).IsUnique(); });
        b.Entity<Permission>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Code).IsUnique(); });
        b.Entity<UserRole>(e => e.HasKey(x => new { x.UserId, x.RoleId }));
        b.Entity<RolePermission>(e => e.HasKey(x => new { x.RoleId, x.PermissionId }));
        b.Entity<UserAdditionalPermission>(e => e.HasKey(x => new { x.UserId, x.PermissionId }));
        b.Entity<Menu>(e => e.HasKey(x => x.Id));
    }
}
