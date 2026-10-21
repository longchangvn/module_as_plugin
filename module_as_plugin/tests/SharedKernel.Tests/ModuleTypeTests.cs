using Xunit;

namespace SharedKernel.Tests;

// Module types (specs/002-module-hosting-types FR-001..FR-003, FR-024..FR-026).
public sealed class ModuleTypeTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ModulePlatform.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("ModulePlatform.sln not found.");
    }

    private static ManifestFile Load(string module) =>
        ManifestParser.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "modules", module, "manifest.yml")));

    private static ManifestFile Plugin()
    {
        var m = Load("sample1");
        m.Module.Type = "plugin";
        m.Frontend.RemoteUrl = "";
        m.Plugin = new ManifestPlugin { Assembly = "Sample1.Plugin.dll" };
        return m;
    }

    private static IReadOnlyList<ManifestValidator.ValidationFailure> Validate(ManifestFile m, bool dev = true) =>
        ManifestValidator.Validate(m, new HashSet<string>(), dev);

    [Theory]
    [InlineData("loyalty")]
    [InlineData("sample1")]
    [InlineData("sample2")]
    public void Existing_manifests_default_to_remote_same_cluster_and_stay_valid(string module)
    {
        var m = Load(module);
        Assert.Equal(ModuleTypeNames.Remote, ModuleTypeNames.Normalize(m.Module.Type));
        Assert.Equal(ModuleNetworks.SameCluster, m.Service.Network);
        Assert.Empty(Validate(m));
    }

    [Fact]
    public void Unknown_type_rejected_and_lists_supported_types()
    {
        var m = Load("sample1");
        m.Module.Type = "lambda";
        var error = Assert.Single(Validate(m), e => e.Field == "module.type");
        Assert.Contains("plugin", error.Message);
        Assert.Contains("remote", error.Message);
    }

    [Fact]
    public void Valid_plugin_manifest_passes_without_remote_url()
    {
        Assert.Empty(Validate(Plugin()));
        Assert.Empty(Validate(Plugin(), dev: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../evil.dll")]
    [InlineData("Plugin.exe")]
    public void Plugin_requires_a_safe_assembly_file_name(string? assembly)
    {
        var m = Plugin();
        m.Plugin!.Assembly = assembly!;
        Assert.Contains(Validate(m), e => e.Field == "plugin.assembly");
    }

    [Fact]
    public void Plugin_without_plugin_section_rejected()
    {
        var m = Plugin();
        m.Plugin = null;
        Assert.Contains(Validate(m), e => e.Field == "plugin.assembly");
    }

    [Fact]
    public void Plugin_unsupported_contract_rejected()
    {
        var m = Plugin();
        m.Plugin!.Contract = "9.9";
        Assert.Contains(Validate(m), e => e.Field == "plugin.contract");
    }

    [Fact]
    public void Plugin_must_not_declare_service_base_url()
    {
        var m = Plugin();
        m.Service.BaseUrl = "http://inventory:8080";
        Assert.Contains(Validate(m), e => e.Field == "service.baseUrl");
    }

    [Fact]
    public void Remote_still_requires_remote_url()
    {
        var m = Load("sample1");
        m.Frontend.RemoteUrl = "";
        Assert.Contains(Validate(m), e => e.Field == "frontend.remoteUrl");
    }

    [Fact]
    public void Invalid_network_zone_rejected()
    {
        var m = Load("sample1");
        m.Service.Network = "mars";
        Assert.Contains(Validate(m), e => e.Field == "service.network");
    }

    [Theory]
    [InlineData("cross-cluster")]
    [InlineData("public")]
    public void Non_local_zones_require_https_outside_development(string zone)
    {
        var m = Load("sample1");
        m.Frontend.RemoteUrl = "https://cdn.example.com/remoteEntry.js";
        m.Service.Network = zone;
        m.Service.BaseUrl = "http://inventory.example.com";
        Assert.Contains(Validate(m, dev: false), e => e.Field == "service.baseUrl");
        Assert.DoesNotContain(Validate(m, dev: true), e => e.Field == "service.baseUrl");
        m.Service.BaseUrl = "https://inventory.example.com";
        Assert.DoesNotContain(Validate(m, dev: false), e => e.Field == "service.baseUrl");
        Assert.NotNull(RemoteModuleTypeRules.ValidateArtifactUrl(m, "http://inventory.example.com", isDevelopment: false));
        Assert.Null(RemoteModuleTypeRules.ValidateArtifactUrl(m, "https://inventory.example.com", isDevelopment: false));
    }

    [Fact]
    public void Same_cluster_allows_http_service_address()
    {
        var m = Load("sample1");
        m.Frontend.RemoteUrl = "https://cdn.example.com/remoteEntry.js";
        m.Service.BaseUrl = "http://sample1:8080";
        Assert.Empty(Validate(m, dev: false));
    }

    [Fact]
    public void New_type_can_be_registered_without_validator_changes()
    {
        ModuleTypeRegistry.Register(new TestOnlyTypeRules());
        var m = Load("sample1");
        m.Module.Type = TestOnlyTypeRules.Name;
        Assert.Contains(TestOnlyTypeRules.Name, ModuleTypeRegistry.Supported);
        Assert.Empty(Validate(m));
    }

    private sealed class TestOnlyTypeRules : IModuleTypeRules
    {
        public const string Name = "test-only-type";
        public string Type => Name;
        public IEnumerable<ManifestValidator.ValidationFailure> Validate(ManifestFile manifest, bool isDevelopment) => [];
    }
}
