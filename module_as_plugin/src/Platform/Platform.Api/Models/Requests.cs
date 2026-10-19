namespace Platform.Api.Models;

public sealed record CreateUserRequest(string Email, string Name, string Role, string? Password = null);
public sealed record ResetPlatformPasswordRequest(string NewPassword);
public sealed record CreateTenantRequest(string Name, string Slug, string? Domain, string? ContactEmail, string? ResolutionMode = null, string? PathPrefix = null, string? DeploymentMode = null, string? DbName = null, string? Schema = null, string? BootstrapAdminEmail = null, string? BootstrapAdminName = null, string? DbConnectionString = null);
public sealed record PublishModuleRequest(string ManifestYaml, string? ArtifactApiUrl, string? ArtifactRemoteUrl);
public sealed record InstallRequest(string? Version);
public sealed record UpgradeRequest(string ToVersion);
public sealed record DisableRequest(string? Reason);
public sealed record PlatformLoginRequest(string Email, string Password);
public sealed record UpdatePlatformProfileRequest(string Name);
public sealed record ChangePlatformPasswordRequest(string CurrentPassword, string NewPassword);
