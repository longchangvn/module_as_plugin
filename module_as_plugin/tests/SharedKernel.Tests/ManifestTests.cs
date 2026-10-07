using SharedKernel;
using Xunit;

namespace SharedKernel.Tests;

public sealed class ManifestValidatorTests
{
    private static ManifestFile Valid()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "modules", "loyalty", "manifest.yml")))
            dir = dir.Parent;
        if (dir is null) throw new FileNotFoundException("modules/loyalty/manifest.yml not found from test run dir.");
        return ManifestParser.Parse(File.ReadAllText(Path.Combine(dir.FullName, "modules", "loyalty", "manifest.yml")));
    }

    [Fact]
    public void Valid_manifest_has_no_errors()
    {
        var manifest = Valid();
        var errors = ManifestValidator.Validate(manifest, new HashSet<string>(), isDevelopment: true);
        Assert.Empty(errors);
    }

    [Fact]
    public void Reserved_name_rejected()
    {
        var manifest = Valid();
        manifest.Module.Name = "admin";
        var errors = ManifestValidator.Validate(manifest, new HashSet<string>(), isDevelopment: true);
        Assert.Contains(errors, e => e.Field == "module.name");
    }

    [Fact]
    public void Unknown_requiredPermission_rejected()
    {
        var manifest = Valid();
        manifest.Frontend.Routes[0].RequiredPermission = "NOPE_MISSING";
        var errors = ManifestValidator.Validate(manifest, new HashSet<string>(), isDevelopment: true);
        Assert.Contains(errors, e => e.Field.Contains("requiredPermission"));
    }

    [Fact]
    public void Duplicate_permission_code_rejected()
    {
        var manifest = Valid();
        manifest.Api.Seed.Permissions.Add(manifest.Api.Seed.Permissions[0]);
        var errors = ManifestValidator.Validate(manifest, new HashSet<string>(), isDevelopment: true);
        Assert.Contains(errors, e => e.Field.Contains("permissions"));
    }

    [Fact]
    public void Route_outside_modulePrefix_rejected()
    {
        var manifest = Valid();
        manifest.Frontend.Routes[0].Path = "/other/path";
        var errors = ManifestValidator.Validate(manifest, new HashSet<string>(), isDevelopment: true);
        Assert.Contains(errors, e => e.Field.Contains("frontend.routes"));
    }
}

public sealed class DependencyResolverTests
{
    [Fact]
    public void Topo_order_installs_dependencies_first()
    {
        // orders -> members
        IReadOnlyList<string> versions(string name) => name switch
        {
            "orders" => new[] { "1.0.0" },
            "members" => new[] { "2.1.0" },
            _ => Array.Empty<string>(),
        };
        IReadOnlyList<ManifestDependency> deps(string name, string ver) => name switch
        {
            "orders" => new[] { new ManifestDependency { Name = "members", VersionRange = "^2.0.0" } },
            _ => Array.Empty<ManifestDependency>(),
        };

        var plan = DependencyResolver.Resolve("orders", "=1.0.0", versions, deps);
        Assert.True(plan.Ok);
        Assert.Equal(new[] { "members", "orders" }, plan.Value!.Select(s => s.Name));
    }

    [Fact]
    public void Version_conflict_rejected_with_chain()
    {
        IReadOnlyList<string> versions(string name) => name == "members" ? new[] { "1.0.0" } : new[] { "1.0.0" };
        IReadOnlyList<ManifestDependency> deps(string name, string ver) => name == "orders"
            ? new[] { new ManifestDependency { Name = "members", VersionRange = ">=2.0.0" } }
            : Array.Empty<ManifestDependency>();

        var plan = DependencyResolver.Resolve("orders", "=1.0.0", versions, deps);
        Assert.False(plan.Ok);
        Assert.Equal(ErrorCodes.DependencyMissing, plan.Error!.Code);
    }

    [Fact]
    public void Latest_picks_highest_version()
    {
        Assert.Equal("2.1.0", SemverOrdering.Latest(new[] { "1.0.0", "2.1.0", "2.0.5" }));
        Assert.True(SemverOrdering.Compare(
            Semver.SemVersion.Parse("1.1.0", Semver.SemVersionStyles.Strict),
            Semver.SemVersion.Parse("1.0.0", Semver.SemVersionStyles.Strict)) > 0);
    }

    [Fact]
    public void Permission_union_dedupes()
    {
        var effective = PermissionService.Effective(
            new[] { "A_VIEW", "A_EDIT" }, new[] { "A_EDIT", "A_EXTRA" });
        Assert.Equal(new HashSet<string> { "A_VIEW", "A_EDIT", "A_EXTRA" }, effective);
    }

    [Fact]
    public void Menu_tree_parents_children()
    {
        var menus = new[]
        {
            new ManifestMenu { Id = "loyalty", Title = "Loyalty", Order = 20 },
            new ManifestMenu { Id = "loyalty.members", ParentId = "loyalty", Title = "Members", Order = 1 },
        };
        var tree = MenuTree.Build(menus);
        Assert.Single(tree);
        Assert.Single(tree[0].Children);
    }
}
