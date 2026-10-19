// Module seed applier — SR-5: idempotent upserts keyed on
// permissions.code / roles.name / menus.id / link PKs. Rerun-safe (edge case 6).
// Uninstall revokes per R6: module permission grants removed, module menus +
// module system roles removed; tenant custom roles and unknown codes are kept
// as inert history (never auto-deleted). Gateway rules are platform-level and
// are recorded in the catalog by the module lifecycle, not here.
using Auth.Api.Domain;
using Auth.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Api.Application;

public sealed class SeedApplier(TenantDbContext db)
{
    public async Task<SeedDiff> ApplyAsync(string moduleId, ManifestFile m, CancellationToken ct)
    {
        var added = new List<string>();

        foreach (var p in m.Api.Seed.Permissions)
        {
            var existing = await db.Permissions.FirstOrDefaultAsync(x => x.Code == p.Code, ct);
            if (existing is null)
            {
                db.Permissions.Add(new Permission
                {
                    Id = Cuid.New("perm"), Code = p.Code, Object = p.Object, Action = p.Action,
                    Name = p.Name, ModuleId = moduleId, IsMenuAccess = p.IsMenuAccess,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                added.Add($"permission:{p.Code}");
            }
        }

        foreach (var r in m.Api.Seed.Roles)
        {
            var existing = await db.Roles.FirstOrDefaultAsync(x => x.Name.ToLower() == r.Name.ToLower(), ct);
            if (existing is null)
            {
                existing = new Role
                {
                    Id = Cuid.New("role"), Name = r.Name, Description = r.Description,
                    ModuleId = moduleId, IsSystem = r.IsSystem,
                    CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                };
                db.Roles.Add(existing);
                added.Add($"role:{r.Name}");
            }
        }

        string? PermId(string code) => db.Permissions.Local
            .FirstOrDefault(p => p.Code == code)?.Id
            ?? db.Permissions.AsNoTracking().FirstOrDefault(p => p.Code == code)?.Id;

        foreach (var u in m.Api.Seed.Users)
        {
            var existing = await db.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == u.Email.ToLower(), ct);
            if (existing is null)
            {
                existing = new TenantUser
                {
                    Id = Cuid.New("u"), Email = u.Email, Name = u.Name,
                    CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                };
                db.Users.Add(existing);
                added.Add($"user:{u.Email}");
            }
        }

        await db.SaveChangesAsync(ct); // flush so PermId/role/user lookups below see new rows

        async Task<string?> RoleIdAsync(string name) =>
            (await db.Roles.FirstOrDefaultAsync(x => x.Name.ToLower() == name.ToLower(), ct))?.Id;
        async Task<string?> UserIdAsync(string email) =>
            (await db.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == email.ToLower(), ct))?.Id;

        foreach (var ur in m.Api.Seed.UserRoles)
        {
            var uid = await UserIdAsync(ur.UserEmail);
            var rid = await RoleIdAsync(ur.RoleName);
            if (uid is null || rid is null) continue;
            if (!await db.UserRoles.AnyAsync(x => x.UserId == uid && x.RoleId == rid, ct))
            {
                db.UserRoles.Add(new UserRole { UserId = uid, RoleId = rid });
                added.Add($"user-role:{ur.UserEmail}->{ur.RoleName}");
            }
        }

        foreach (var rp in m.Api.Seed.RolePermissions)
        {
            var rid = await RoleIdAsync(rp.RoleName);
            if (rid is null) continue;
            foreach (var code in rp.Permissions)
            {
                var pid = PermId(code);
                if (pid is null) continue;
                if (!await db.RolePermissions.AnyAsync(x => x.RoleId == rid && x.PermissionId == pid, ct))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = rid, PermissionId = pid });
                    added.Add($"role-permission:{rp.RoleName}->{code}");
                }
            }
        }

        foreach (var up in m.Api.Seed.UserAdditionalPermissions)
        {
            var uid = await UserIdAsync(up.UserEmail);
            if (uid is null) continue;
            foreach (var code in up.Permissions)
            {
                var pid = PermId(code);
                if (pid is null) continue;
                if (!await db.UserAdditionalPermissions.AnyAsync(x => x.UserId == uid && x.PermissionId == pid, ct))
                {
                    db.UserAdditionalPermissions.Add(new UserAdditionalPermission { UserId = uid, PermissionId = pid });
                    added.Add($"user-permission:{up.UserEmail}->{code}");
                }
            }
        }

        // Menus: frontend.menus wins over api.seed.menus on conflict (validator warns; applier dedupes by id).
        var menus = m.Frontend.Menus.Concat(m.Api.Seed.Menus)
            .GroupBy(x => x.Id, StringComparer.Ordinal).Select(g => g.First());
        foreach (var menu in menus)
        {
            var existing = await db.Menus.FindAsync([menu.Id], ct);
            if (existing is null)
            {
                db.Menus.Add(new Menu
                {
                    Id = menu.Id, ParentId = menu.ParentId, Title = menu.Title, Icon = menu.Icon,
                    Route = menu.Route, ModuleId = moduleId, RequiredPermission = menu.RequiredPermission,
                    SortOrder = menu.Order,
                });
                added.Add($"menu:{menu.Id}");
            }
        }

        // Gateway rules are platform-level (Platform.Domain.GatewayRule, recorded
        // in the catalog by the module lifecycle) — never provisioned into
        // tenant databases.

        await db.SaveChangesAsync(ct);
        return new SeedDiff(added);
    }

    // Returns false (nothing changed) when removing the module's system roles
    // would remove the tenant's last admin grant (edge case 12, LAST_ADMIN).
    public async Task<bool> RevokeAsync(string moduleId, CancellationToken ct)
    {
        var moduleRoleIds = await db.Roles.Where(r => r.ModuleId == moduleId && r.IsSystem).Select(r => r.Id).ToListAsync(ct);
        if (moduleRoleIds.Count > 0 && await LastAdminGuard.WouldLockOutAsync(db, g => moduleRoleIds.Contains(g.RoleId), ct))
            return false;
        var permIds = await db.Permissions.Where(p => p.ModuleId == moduleId).Select(p => p.Id).ToListAsync(ct);

        // Remove grants of module permissions (roles + direct), keep custom roles themselves.
        db.RolePermissions.RemoveRange(db.RolePermissions.Where(rp => permIds.Contains(rp.PermissionId)));
        db.UserAdditionalPermissions.RemoveRange(db.UserAdditionalPermissions.Where(u => permIds.Contains(u.PermissionId)));
        await db.SaveChangesAsync(ct);

        db.Permissions.RemoveRange(db.Permissions.Where(p => p.ModuleId == moduleId));
        db.Roles.RemoveRange(db.Roles.Where(r => r.ModuleId == moduleId && r.IsSystem));
        db.Menus.RemoveRange(db.Menus.Where(x => x.ModuleId == moduleId));
        await db.SaveChangesAsync(ct);
        return true;
    }

    public SeedDiff Preview(ManifestFile m) => new(
        m.Api.Seed.Permissions.Select(p => $"permission:{p.Code}")
            .Concat(m.Api.Seed.Roles.Select(r => $"role:{r.Name}"))
            .Concat(m.Api.Seed.Users.Select(u => $"user:{u.Email}"))
            .Concat(m.Frontend.Menus.Select(x => $"menu:{x.Id}")).ToList());
}

public sealed record SeedDiff(IReadOnlyList<string> Applied);
