using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SharedKernel;

// Normative manifest.yml contract — §10.3. Deserialised with YamlDotNet (open-source,
// instead of a hand-rolled YAML parser).
public sealed class ManifestFile
{
    public string ManifestVersion { get; set; } = "1.0";
    public ManifestModule Module { get; set; } = new();
    public List<ManifestDependency> Dependencies { get; set; } = new();
    public ManifestService Service { get; set; } = new();
    public ManifestFrontend Frontend { get; set; } = new();
    public ManifestApi Api { get; set; } = new();
    public ManifestUpgrade Upgrade { get; set; } = new();
    // Required when module.type = plugin (in-process, Module Host).
    public ManifestPlugin? Plugin { get; set; }
}

public sealed class ManifestModule
{
    public string Name { get; set; } = "";
    // Module type (hosting model): remote (standalone host, default) | plugin. See ModuleTypes.
    public string Type { get; set; } = ModuleTypeNames.Remote;
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "";
    public string Owner { get; set; } = "";
    public string? License { get; set; }
    public string? Icon { get; set; }
    public string? Category { get; set; }
}

public sealed class ManifestDependency
{
    public string Name { get; set; } = "";
    public string VersionRange { get; set; } = "";
}

public sealed class ManifestService
{
    public string HealthCheck { get; set; } = "/healthz";
    public string ReadinessCheck { get; set; } = "/readyz";
    public string? OpenapiUrl { get; set; }
    public string? MinPlatformVersion { get; set; }
    // Remote only: where the standalone host runs relative to the platform.
    public string Network { get; set; } = ModuleNetworks.SameCluster;
    // Remote only: default artifact API base URL when none is supplied at publish.
    public string? BaseUrl { get; set; }
}

public sealed class ManifestPlugin
{
    // Entry assembly file name inside the package's plugin/ folder.
    public string Assembly { get; set; } = "";
    // Optional full type name of the IModulePlugin implementation.
    public string? EntryType { get; set; }
    // Plugin contract version (ModulePlatform.Plugin.Abstractions).
    public string Contract { get; set; } = "1.0";
}

public static class ModuleTypeNames
{
    public const string Remote = "remote";
    public const string Plugin = "plugin";

    public static string Normalize(string? type) =>
        string.IsNullOrWhiteSpace(type) ? Remote : type.Trim().ToLowerInvariant();
}

public static class ModuleNetworks
{
    public const string SameCluster = "same-cluster";
    public const string CrossCluster = "cross-cluster";
    public const string Public = "public";
    public static readonly string[] All = [SameCluster, CrossCluster, Public];
}

public sealed class ManifestFrontend
{
    public string RemoteUrl { get; set; } = "";
    public string Scope { get; set; } = "";
    public string ModulePrefix { get; set; } = "";
    public List<ManifestRoute> Routes { get; set; } = new();
    public List<ManifestMenu> Menus { get; set; } = new();
    public List<string> UiPermissions { get; set; } = new();
}

public sealed class ManifestRoute
{
    public string Path { get; set; } = "";
    public string Exposes { get; set; } = "";
    public string? RequiredPermission { get; set; }
    public string? MenuId { get; set; }
}

public sealed class ManifestMenu
{
    public string Id { get; set; } = "";
    public string? ParentId { get; set; }
    public string Title { get; set; } = "";
    public string? Icon { get; set; }
    public string? Route { get; set; }
    public string? RequiredPermission { get; set; }
    public int Order { get; set; }
}

public sealed class ManifestApi
{
    public List<ManifestApiRoute> Routes { get; set; } = new();
    public ManifestSeed Seed { get; set; } = new();
}

public sealed class ManifestApiRoute
{
    public string Upstream { get; set; } = "";
    public string Downstream { get; set; } = "";
    public List<string> Methods { get; set; } = new();
    public bool Auth { get; set; } = true;
    public string? RequiredPermission { get; set; }
    public int Priority { get; set; }
}

public sealed class ManifestSeed
{
    public List<ManifestSeedRole> Roles { get; set; } = new();
    public List<ManifestSeedPermission> Permissions { get; set; } = new();
    public List<ManifestMenu> Menus { get; set; } = new();
    public List<ManifestUserRole> UserRoles { get; set; } = new();
    public List<ManifestRolePermission> RolePermissions { get; set; } = new();
    public List<ManifestUserPermission> UserAdditionalPermissions { get; set; } = new();
    public List<ManifestSeedUser> Users { get; set; } = new();
    public List<object> GatewayRules { get; set; } = new();
}

public sealed class ManifestSeedRole
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
}

public sealed class ManifestSeedPermission
{
    public string Code { get; set; } = "";
    public string Object { get; set; } = "";
    public string Action { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsMenuAccess { get; set; }
}

public sealed class ManifestUserRole
{
    public string UserEmail { get; set; } = "";
    public string RoleName { get; set; } = "";
}

public sealed class ManifestRolePermission
{
    public string RoleName { get; set; } = "";
    public List<string> Permissions { get; set; } = new();
}

public sealed class ManifestUserPermission
{
    public string UserEmail { get; set; } = "";
    public List<string> Permissions { get; set; } = new();
}

public sealed class ManifestSeedUser
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class ManifestUpgrade
{
    public string? Notes { get; set; }
    public string? MigrateEndpoint { get; set; }
}

public static class ManifestParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ManifestFile Parse(string yaml) => Deserializer.Deserialize<ManifestFile>(yaml);
}
