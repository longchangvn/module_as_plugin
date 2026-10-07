// Platform catalog domain — §10.1. Pure entities, no EF attributes (configured in Infrastructure).
namespace Platform.Domain;

public enum PlatformRole { Admin, Maintainer }

public enum TenantStatus { Active, Disabled, Provisioning, Failed }

public enum ModuleStatus { Available, Deprecated }

public enum InstallState { NotInstalled, Installing, Installed, Disabled, Failed, Uninstalling }

public sealed class PlatformUser
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public PlatformRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Tenant
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public string ResolutionMode { get; set; } = "slug"; // domain | path | header
    public string? Domain { get; set; }
    public string? PathPrefix { get; set; }
    public string DbName { get; set; } = "";
    public string Schema { get; set; } = "public";
    public string? ContactEmail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PlatformModule
{
    public string Id { get; set; } = ""; // slug, immutable
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Owner { get; set; } = "";
    public string? Icon { get; set; }
    public string? Category { get; set; }
    public ModuleStatus Status { get; set; } = ModuleStatus.Available;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ModuleVersion
{
    public string Id { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public string Version { get; set; } = "";
    public string ManifestYaml { get; set; } = "";
    public string ManifestHash { get; set; } = "";
    public string? ArtifactApiUrl { get; set; }
    public string? ArtifactRemoteUrl { get; set; }
    public string Status { get; set; } = "AVAILABLE";
    public string? PublishedBy { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}

public sealed class TenantModuleInstall
{
    public string TenantId { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public string Version { get; set; } = "";
    public InstallState State { get; set; } = InstallState.NotInstalled;
    public bool Enabled { get; set; }
    public DateTimeOffset? InstalledAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? Error { get; set; }
}

public sealed class TenantAudit
{
    public string Id { get; set; } = "";
    public string? TenantId { get; set; }
    public string ActorId { get; set; } = "";
    public string ActorRole { get; set; } = "";
    public string Action { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public string? Diff { get; set; } // JSONB
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? RequestId { get; set; }
}
