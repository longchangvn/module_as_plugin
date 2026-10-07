using Auth.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Auth.Api.Infrastructure;

// v1: single logical tenant store. Production maps one database/schema per tenant
// (connection resolved from tenant resolution); the scaffold uses one database with
// the same shape so seed/resolve logic is identical.
public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options)
{
    public DbSet<TenantUser> Users => Set<TenantUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserAdditionalPermission> UserAdditionalPermissions => Set<UserAdditionalPermission>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<GatewayRule> GatewayRules => Set<GatewayRule>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TenantUser>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Email).IsUnique(); });
        b.Entity<Role>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Name).IsUnique(); });
        b.Entity<Permission>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Code).IsUnique(); });
        b.Entity<UserRole>(e => e.HasKey(x => new { x.UserId, x.RoleId }));
        b.Entity<RolePermission>(e => e.HasKey(x => new { x.RoleId, x.PermissionId }));
        b.Entity<UserAdditionalPermission>(e => e.HasKey(x => new { x.UserId, x.PermissionId }));
        b.Entity<Menu>(e => e.HasKey(x => x.Id));
        b.Entity<GatewayRule>(e => e.HasKey(x => x.Id));
    }
}
