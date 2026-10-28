using Xunit;
using Endpoints = global::Inventory.Api.Endpoints.ModuleEndpoints;

namespace SharedKernel.Tests.Modules;

// Inventory (scaffolded by tools/create-module): the manifest must pass
// platform validation and the module's seed preview must equal the manifest seed.
public sealed class InventoryModuleTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ModulePlatform.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("ModulePlatform.sln not found above test output.");
    }

    private static ManifestFile Load() =>
        ManifestParser.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "modules", Endpoints.ModuleId, "manifest.yml")));

    [Fact]
    public void Manifest_passes_platform_validation()
    {
        var failures = ManifestValidator.Validate(Load(), new HashSet<string> { "loyalty_remote" }, isDevelopment: true);
        Assert.Empty(failures);
    }

    [Fact]
    public void Manifest_identity_matches_api()
    {
        var m = Load();
        Assert.Equal(Endpoints.ModuleId, m.Module.Name);
        Assert.Equal(Endpoints.Version, m.Module.Version);
    }

    [Fact]
    public void Seed_preview_matches_manifest()
    {
        var m = Load();
        Assert.Equal(m.Api.Seed.Roles.Select(r => r.Name).Order(), Endpoints.SeedRoles.Order());
        Assert.Equal(m.Api.Seed.Permissions.Select(p => p.Code).Order(), Endpoints.SeedPermissions.Order());
        Assert.Equal(m.Frontend.Menus.Select(x => x.Id).Order(), Endpoints.SeedMenus.Order());
        Assert.Contains(Endpoints.ProtectedPermission, m.Frontend.UiPermissions);
    }
}
