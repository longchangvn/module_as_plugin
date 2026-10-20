using System.Security.Claims;
using System.Text.Json.Nodes;
using Gateway.Api.Endpoints;
using Gateway.Api.Models;
using Gateway.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gateway.Api.Tests;

// R-GW1: per-tenant module state at the gateway.
public sealed class ModuleTenantStateTests
{
    private static readonly List<ModuleUpstream> V1Routes = [new("/api/v1/sample1/{rest}", "/api/v1/{rest}", ["GET"])];
    private static readonly List<ModuleUpstream> V2Routes = [new("/api/v1/sample1/{rest}", "/api/v2/{rest}", ["GET", "POST"])];

    private static ModuleTenantState TwoTenants()
    {
        var s = new ModuleTenantState();
        s.Upsert("sample1", "tnt_a", true, "1.0.0", "http://localhost:5202", V1Routes);
        s.Upsert("sample1", "tnt_b", true, "1.0.0", "http://localhost:5202", V1Routes);
        return s;
    }

    [Fact]
    public void Unknown_module_or_tenant_is_not_installed()
    {
        var s = TwoTenants();
        Assert.Equal(ModuleState.NotInstalled, s.Decide("sample2", "tnt_a"));
        Assert.Equal(ModuleState.NotInstalled, s.Decide("sample1", "tnt_c"));
        Assert.Equal(ModuleState.Enabled, s.Decide("SAMPLE1", "TNT_A"));
    }

    [Fact]
    public void Disable_in_one_tenant_does_not_affect_another()
    {
        var s = TwoTenants();
        s.Upsert("sample1", "tnt_a", false, "1.0.0", "http://localhost:5202", V1Routes);
        Assert.Equal(ModuleState.Disabled, s.Decide("sample1", "tnt_a"));
        Assert.Equal(ModuleState.Enabled, s.Decide("sample1", "tnt_b"));
    }

    [Fact]
    public void Install_in_another_tenant_keeps_disabled_tenant_disabled()
    {
        var s = new ModuleTenantState();
        s.Upsert("sample1", "tnt_a", false, "1.0.0", null, V1Routes);
        s.Upsert("sample1", "tnt_b", true, "1.0.0", null, V1Routes);
        Assert.Equal(ModuleState.Disabled, s.Decide("sample1", "tnt_a"));
        Assert.Equal(ModuleState.Enabled, s.Decide("sample1", "tnt_b"));
    }

    [Fact]
    public void Uninstall_keeps_routes_while_another_tenant_has_module()
    {
        var s = TwoTenants();
        Assert.True(s.Remove("sample1", "tnt_a"));
        Assert.Equal(ModuleState.NotInstalled, s.Decide("sample1", "tnt_a"));
        Assert.Equal(ModuleState.Enabled, s.Decide("sample1", "tnt_b"));
        Assert.NotNull(s.Effective("sample1"));
    }

    [Fact]
    public void Last_tenant_uninstall_removes_module_and_routes()
    {
        var s = TwoTenants();
        s.Remove("sample1", "tnt_a");
        s.Remove("sample1", "tnt_b");
        Assert.False(s.HasModule("sample1"));
        Assert.Null(s.Effective("sample1"));
        Assert.False(s.Remove("sample1", "tnt_b")); // idempotent
    }

    [Fact]
    public void Routes_follow_newest_installed_version_and_fall_back_when_it_leaves()
    {
        var s = TwoTenants();
        s.Upsert("sample1", "tnt_b", true, "1.10.0", "http://v2:5202", V2Routes);
        Assert.Equal("http://v2:5202", s.Effective("sample1")!.BaseUrl);   // semver: 1.10.0 > 1.0.0
        s.Remove("sample1", "tnt_b");
        Assert.Equal("http://localhost:5202", s.Effective("sample1")!.BaseUrl);
        Assert.Single(s.Modules["sample1"].Versions); // unused version pruned
    }

    [Fact]
    public void Clone_is_independent()
    {
        var s = TwoTenants();
        var c = s.Clone();
        c.Upsert("sample1", "tnt_a", false, "1.0.0", null, V1Routes);
        Assert.Equal(ModuleState.Enabled, s.Decide("sample1", "tnt_a"));
    }

    [Fact]
    public void Json_round_trip_v2()
    {
        var s = TwoTenants();
        s.Upsert("sample1", "tnt_a", false, "1.0.0", "http://localhost:5202", V1Routes);
        var back = ModuleTenantState.FromJson(JsonNode.Parse(s.ToJson().ToJsonString()), out var legacy);
        Assert.False(legacy);
        Assert.Equal(ModuleState.Disabled, back.Decide("sample1", "TNT_A"));
        Assert.Equal(ModuleState.Enabled, back.Decide("sample1", "tnt_b"));
        Assert.Equal("/api/v1/{rest}", back.Effective("sample1")!.Upstreams[0].Downstream);
        Assert.Equal(0, ModuleTenantState.Drift(s, back));
    }

    [Fact]
    public void Legacy_v1_file_loads_empty_and_flags_legacy()
    {
        var s = ModuleTenantState.FromJson(JsonNode.Parse("""{"modules":{"sample1":"Enabled"}}"""), out var legacy);
        Assert.True(legacy);
        Assert.False(s.HasModule("sample1"));
    }

    [Fact]
    public void Snapshot_builds_state_and_drift_counts_changes()
    {
        var snap = new List<ModuleSnapshot>
        {
            new("sample1", [new VersionSnapshot("1.0.0", "http://localhost:5202", V1Routes)],
                [new TenantSnapshot("tnt_a", false, "1.0.0"), new TenantSnapshot("tnt_b", true, "1.0.0"),
                 new TenantSnapshot("tnt_x", true, "9.9.9")]), // unknown version → skipped
        };
        var s = ModuleTenantState.FromSnapshot(snap);
        Assert.Equal(ModuleState.Disabled, s.Decide("sample1", "tnt_a"));
        Assert.Equal(ModuleState.NotInstalled, s.Decide("sample1", "tnt_x"));
        Assert.Equal(2, ModuleTenantState.Drift(TwoTenants(), s)); // tnt_a enabled→disabled = 1 removed + 1 added
    }

    [Theory]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.0.0", "1.0.0-rc.1", 1)]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.10", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.0.0+build", "1.0.0", 0)]
    [InlineData("garbage", "0.0.1", -1)]
    public void Semver_precedence(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(SemverComparer.Instance.Compare(a, b)));
}

// R-GW2 + G2: internal key, push validation, request guard.
public sealed class GatewaySecurityTests
{
    private static ModuleTenantState State()
    {
        var s = new ModuleTenantState();
        s.Upsert("sample1", "tnt_a", true, "1.0.0", null, []);
        s.Upsert("sample1", "tnt_b", false, "1.0.0", null, []);
        return s;
    }

    private static HttpContext Ctx(string? tenantId, bool authenticated = true, string method = "GET", string? header = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        if (header is not null) ctx.Request.Headers["X-Tenant-Id"] = header;
        var claims = tenantId is null ? new List<Claim>() : [new Claim("tenantId", tenantId)];
        ctx.User = new ClaimsPrincipal(authenticated ? new ClaimsIdentity(claims, "Bearer") : new ClaimsIdentity(claims));
        return ctx;
    }

    [Theory]
    [InlineData("tnt_a", null, null)]                         // enabled → proxy
    [InlineData("tnt_b", null, 503)]                          // disabled in B only
    [InlineData("tnt_c", null, 404)]                          // not installed in C
    [InlineData("tnt_a", "tnt_b", 403)]                       // header/claim mismatch
    [InlineData("tnt_a", "TNT_A", null)]                      // header may repeat claim
    [InlineData(null, null, 403)]                             // platform JWT (no tenant)
    public void Guard_decides_per_tenant(string? tenant, string? header, int? expected) =>
        Assert.Equal(expected, GatewayHelpers.ModuleGuard(Ctx(tenant, header: header), State(), "sample1")?.Status);

    [Fact]
    public void Guard_rejects_unauthenticated_and_forged_header()
    {
        var anon = Ctx(null, authenticated: false, header: "tnt_a");
        Assert.Equal(401, GatewayHelpers.ModuleGuard(anon, State(), "sample1")?.Status);
    }

    [Fact]
    public void Guard_lets_preflight_through_only_for_known_modules()
    {
        Assert.Null(GatewayHelpers.ModuleGuard(Ctx(null, false, "OPTIONS"), State(), "sample1"));
        Assert.Equal(404, GatewayHelpers.ModuleGuard(Ctx(null, false, "OPTIONS"), State(), "nope")?.Status);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("wrong-key", false)]
    [InlineData("the-internal-key", true)]
    public void Internal_key_required(string? presented, bool ok)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["InternalApi:Key"] = "the-internal-key" }).Build();
        var ctx = new DefaultHttpContext();
        if (presented is not null) ctx.Request.Headers["X-Internal-Key"] = presented;
        Assert.Equal(ok, GatewayHelpers.IsInternalCaller(ctx, cfg));
    }

    [Fact]
    public void Internal_key_unconfigured_fails_closed()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Internal-Key"] = "";
        Assert.False(GatewayHelpers.IsInternalCaller(ctx, new ConfigurationBuilder().Build()));
    }

    [Theory]
    [InlineData("sample1", "tnt_a", "1.0.0", "http://localhost:5202", "/api/v1/sample1/{rest}", true)]
    [InlineData("sample1", "tnt_a", "1.0.0", null, "/api/v1/sample1/{rest}", true)]
    [InlineData("sample1", "tnt_a", "1.0.0", null, "/api/v1/platform/{rest}", false)]   // route squatting
    [InlineData("sample1", "tnt_a", "1.0.0", null, "/api/v1/sample10/{rest}", false)]
    [InlineData("platform", "tnt_a", "1.0.0", null, "/api/v1/platform/{rest}", false)]  // reserved module id
    [InlineData("../x", "tnt_a", "1.0.0", null, "/api/v1/sample1/{rest}", false)]
    [InlineData("sample1", "tnt/a", "1.0.0", null, "/api/v1/sample1/{rest}", false)]
    [InlineData("sample1", "tnt_a", "", null, "/api/v1/sample1/{rest}", false)]
    [InlineData("sample1", "tnt_a", "1.0.0", "file:///etc/passwd", "/api/v1/sample1/{rest}", false)]
    [InlineData("sample1", "tnt_a", "1.0.0", "not a url", "/api/v1/sample1/{rest}", false)]
    public void Push_validation(string module, string tenant, string version, string? baseUrl, string upstream, bool valid) =>
        Assert.Equal(valid, GatewayStore.ValidateModuleSpec(module, tenant, version, baseUrl,
            [new ModuleUpstream(upstream, "/api/v1/{rest}", ["GET"])]) is null);
}
