// Tenant store — §10.2 / A-BR4. No tenantId column: isolation by DB/schema.
// Seeded/updated by each module's manifest seed section (upsert by code/name/id).
namespace Auth.Api.Domain;

public sealed class TenantUser
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string? PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Role
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? ModuleId { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Permission
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = ""; // OBJECT_ACTION, unique, immutable once seeded (R4)
    public string Object { get; set; } = "";
    public string Action { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ModuleId { get; set; }
    public bool IsMenuAccess { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class UserRole
{
    public string UserId { get; set; } = "";
    public string RoleId { get; set; } = "";
}

public sealed class RolePermission
{
    public string RoleId { get; set; } = "";
    public string PermissionId { get; set; } = "";
}

public sealed class UserAdditionalPermission
{
    public string UserId { get; set; } = "";
    public string PermissionId { get; set; } = "";
}

public sealed class Menu
{
    public string Id { get; set; } = ""; // slug, unique per tenant (R5)
    public string? ParentId { get; set; }
    public string Title { get; set; } = "";
    public string? Icon { get; set; }
    public string? Route { get; set; }
    public string? ModuleId { get; set; }
    public string? RequiredPermission { get; set; } // permissions.code
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class GatewayRule
{
    public string Id { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public string UpstreamTemplate { get; set; } = "";
    public string DownstreamTemplate { get; set; } = "";
    public string Methods { get; set; } = "[]"; // JSON array
    public string? Permission { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? AppliedAt { get; set; }
}
