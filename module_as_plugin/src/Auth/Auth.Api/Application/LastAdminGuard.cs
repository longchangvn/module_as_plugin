// Edge case 12 (LAST_ADMIN): no mutation may remove the tenant's last
// TENANT_ADMIN-equivalent grant. "Admin grant" = active user holding an active
// role whose name contains "Admin" — the same rule login uses to mint the
// tenant_admin claim. A tenant that has no admin grant today is not blocked.
using Auth.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Auth.Api.Application;

public sealed record AdminGrant(string UserId, string RoleId);

public static class LastAdminGuard
{
    public static bool IsAdminRoleName(string name) => name.Contains("Admin", StringComparison.OrdinalIgnoreCase);

    public static bool WouldLockOut(IReadOnlyCollection<AdminGrant> grants, Func<AdminGrant, bool> removes) =>
        grants.Count > 0 && grants.All(removes);

    public static async Task<bool> WouldLockOutAsync(TenantDbContext db, Func<AdminGrant, bool> removes, CancellationToken ct = default)
    {
        var grants = await (
            from ur in db.UserRoles
            join u in db.Users on ur.UserId equals u.Id
            join r in db.Roles on ur.RoleId equals r.Id
            where u.IsActive && r.IsActive && r.Name.ToLower().Contains("admin")
            select new AdminGrant(ur.UserId, ur.RoleId)).ToListAsync(ct);
        return WouldLockOut(grants, removes);
    }
}
