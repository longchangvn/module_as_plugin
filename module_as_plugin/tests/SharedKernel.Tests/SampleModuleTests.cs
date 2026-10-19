using Xunit;

namespace SharedKernel.Tests;

// Sample test modules (specs/001-sample-test-modules): manifests must pass
// platform validation and the module's seed preview must equal the manifest seed.
public sealed class SampleModuleTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ModulePlatform.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("ModulePlatform.sln not found above test output.");
    }

    private static ManifestFile Load(string module) =>
        ManifestParser.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "modules", module, "manifest.yml")));

    [Theory]
    [InlineData("sample1")]
    [InlineData("sample2")]
    public void Manifest_passes_platform_validation(string module)
    {
        var failures = ManifestValidator.Validate(Load(module), new HashSet<string> { "loyalty_remote" }, isDevelopment: true);
        Assert.Empty(failures);
    }

    public static TheoryData<string, string[], string[], string[]> SeedPreviews => new()
    {
        { "sample1", Sample1.Api.Endpoints.ModuleEndpoints.SeedRoles, Sample1.Api.Endpoints.ModuleEndpoints.SeedPermissions, Sample1.Api.Endpoints.ModuleEndpoints.SeedMenus },
        { "sample2", Sample2.Api.Endpoints.ModuleEndpoints.SeedRoles, Sample2.Api.Endpoints.ModuleEndpoints.SeedPermissions, Sample2.Api.Endpoints.ModuleEndpoints.SeedMenus },
    };

    [Theory]
    [MemberData(nameof(SeedPreviews))]
    public void Seed_preview_matches_manifest(string module, string[] roles, string[] permissions, string[] menus)
    {
        var m = Load(module);
        Assert.Equal(m.Api.Seed.Roles.Select(r => r.Name).Order(), roles.Order());
        Assert.Equal(m.Api.Seed.Permissions.Select(p => p.Code).Order(), permissions.Order());
        Assert.Equal(m.Frontend.Menus.Select(x => x.Id).Order(), menus.Order());
        Assert.Equal(m.Frontend.UiPermissions, permissions.Where(p => p.EndsWith("_PROTECTED")));
    }
}
