namespace Auth.Api.Models;

public sealed record SeedApplyRequest(string ModuleId, string ManifestYaml, string TenantId = "");
public sealed record SeedRevokeRequest(string ModuleId, string TenantId = "");
public sealed record LoginRequest(string TenantId, string Email, string Password);

public sealed record CreateUser(string Email, string Name, string? Password = null);
public sealed record CreateRole(string Name, string? Description);
public sealed record AssignRoleRequest(string RoleId);
public sealed record AssignPermissionRequest(string PermissionId);
public sealed record PatchUserRequest(string? Name, bool? IsActive);
public sealed record PatchRoleRequest(string? Description, bool? IsActive);
public sealed record UpdateMyProfileRequest(string Name);
public sealed record ChangeMyPasswordRequest(string CurrentPassword, string NewPassword);
