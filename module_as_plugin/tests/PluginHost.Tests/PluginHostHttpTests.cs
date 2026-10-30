using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModulePlatform.ServiceDefaults;
using PluginHost.Api;
using PluginHost.Api.Runtime;
using SharedKernel;
using Xunit;

namespace PluginHost.Tests;

// End-to-end through the real Module Host pipeline (JWT → tenant shell → routing + matcher policy →
// authorization → plugin DI) on a random port, with the in-memory package source instead of the
// Platform (specs/002 US2/US3, FR-014..FR-016, SC-002, SC-004).
public sealed class PluginHostHttpTests : IAsyncLifetime
{
    private const string Module = TestPlugins.ModuleId;
    private const string TenantA = "tnt_a";
    private const string TenantB = "tnt_b";

    private readonly string cacheRoot = TestPlugins.NewTempDir();
    private WebApplication app = null!;
    private HttpClient http = null!;

    public async Task InitializeAsync()
    {
        app = PluginHostApp.Build(
            ["--urls", "http://127.0.0.1:0", "--environment", "Development",
             "--PluginHost:ReconcileEnabled=false", $"--PluginHost:CacheRoot={cacheRoot}", "--PluginHost:DrainTimeoutSeconds=5"],
            b =>
            {
                var source = new FolderPackageSource(cacheRoot);
                b.Services.Replace(ServiceDescriptor.Singleton<IPluginPackageSource>(source));
                b.Services.Replace(ServiceDescriptor.Singleton<IModuleAssetSource>(source));
            });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        http = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        http.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static string Token(string tenantId) =>
        JwtMint.Mint(Extensions.DevJwtSecret, new Dictionary<string, string> { ["sub"] = $"u_{tenantId}", ["userId"] = $"u_{tenantId}", ["tenantId"] = tenantId });

    private Task<HttpResponseMessage> Data(string? tenantId, string? header = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/plugins/{Module}/api/v1/data");
        if (tenantId is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenantId));
        if (header is not null) req.Headers.Add("X-Tenant-Id", header);
        return http.SendAsync(req);
    }

    private async Task<HttpResponseMessage> Activate(string tenantId, string version, bool enabled = true, bool withKey = true)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/internal/plugins/{Module}/tenants/{tenantId}")
        {
            Content = JsonContent.Create(new { version, enabled, packageSha256 = TestPlugins.Sha($"{Module}@{version}") }),
        };
        if (withKey) req.Headers.Add("X-Internal-Key", Extensions.DevInternalKey);
        return await http.SendAsync(req);
    }

    private async Task<HttpResponseMessage> Deactivate(string tenantId)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/internal/plugins/{Module}/tenants/{tenantId}");
        req.Headers.Add("X-Internal-Key", Extensions.DevInternalKey);
        return await http.SendAsync(req);
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString();

    private static async Task<JsonElement> DataOf(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("data");

    [Fact]
    public async Task Tenant_shell_gates_access_per_tenant()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Data(null)).StatusCode);
        var notInstalled = await Data(TenantA);
        Assert.Equal(HttpStatusCode.NotFound, notInstalled.StatusCode);
        Assert.Equal(ErrorCodes.UnknownModuleRoute, await ErrorCode(notInstalled));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Activate(TenantA, "1.0.0", withKey: false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Activate(TenantA, "1.0.0")).StatusCode);

        var ok = await Data(TenantA);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var data = await DataOf(ok);
        Assert.Equal(Module, data.GetProperty("module").GetString());
        Assert.Equal("1.0.0", data.GetProperty("version").GetString());
        Assert.Equal(TenantA, data.GetProperty("tenant").GetString());
        Assert.Equal("hello from fixture@1.0.0", data.GetProperty("greeting").GetString()); // plugin DI service

        var other = await Data(TenantB);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(ErrorCodes.UnknownModuleRoute, await ErrorCode(other));

        var mismatch = await Data(TenantA, header: TenantB);
        Assert.Equal(HttpStatusCode.Forbidden, mismatch.StatusCode);
        Assert.Equal(ErrorCodes.ForbiddenTenant, await ErrorCode(mismatch));

        Assert.Equal(HttpStatusCode.OK, (await Activate(TenantA, "1.0.0", enabled: false)).StatusCode);
        var disabled = await Data(TenantA);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
        Assert.Equal(ErrorCodes.ModuleDisabled, await ErrorCode(disabled));

        Assert.Equal(HttpStatusCode.OK, (await Deactivate(TenantA)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Data(TenantA)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Deactivate(TenantA)).StatusCode); // idempotent
    }

    [Fact]
    public async Task Tenants_on_different_versions_are_each_served_by_their_own_version()
    {
        Assert.Equal(HttpStatusCode.OK, (await Activate(TenantA, "2.0.0")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Activate(TenantB, "1.0.0")).StatusCode);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal("2.0.0", (await DataOf(await Data(TenantA))).GetProperty("version").GetString());
            Assert.Equal("1.0.0", (await DataOf(await Data(TenantB))).GetProperty("version").GetString());
        }
    }

    [Fact]
    public async Task Load_failure_returns_422_and_keeps_the_previous_version()
    {
        var source = (FolderPackageSource)app.Services.GetRequiredService<IPluginPackageSource>();
        source.Contracts[$"{Module}@3.0.0"] = "9.9";
        Assert.Equal(HttpStatusCode.OK, (await Activate(TenantA, "1.0.0")).StatusCode);

        var failed = await Activate(TenantA, "3.0.0");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failed.StatusCode);
        Assert.Equal(ErrorCodes.PluginLoadFailed, await ErrorCode(failed));
        Assert.Equal("1.0.0", (await DataOf(await Data(TenantA))).GetProperty("version").GetString());
    }

    [Fact]
    public async Task Internal_api_validates_input_and_lists_state()
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/internal/plugins/{Module}/tenants/{TenantA}")
        {
            Content = JsonContent.Create(new { version = "../1", enabled = true, packageSha256 = "nope" }),
        };
        req.Headers.Add("X-Internal-Key", Extensions.DevInternalKey);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(req)).StatusCode);

        await Activate(TenantA, "1.0.0");
        var view = new HttpRequestMessage(HttpMethod.Get, "/internal/plugins");
        view.Headers.Add("X-Internal-Key", Extensions.DevInternalKey);
        var body = await DataOf(await http.SendAsync(view));
        var runtime = Assert.Single(body.GetProperty("runtimes").EnumerateArray());
        Assert.Equal("1.0.0", runtime.GetProperty("version").GetString());
        Assert.Contains(TenantA, runtime.GetProperty("tenants").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public async Task Plugin_assets_are_served_anonymously_without_path_escape()
    {
        await Activate(TenantA, "1.0.0");
        var entry = await http.GetAsync($"/plugin-assets/{Module}/1.0.0/assets/remoteEntry.js");
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        Assert.Equal("*", entry.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("no-cache", entry.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/plugin-assets/{Module}/1.0.0/%2e%2e/manifest.yml")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/plugin-assets/{Module}/1.0.0/..%2Fmanifest.yml")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/plugin-assets/{Module}/9.9.9/assets/remoteEntry.js")).StatusCode);
    }

    // Hybrid modules: the host only caches + serves the UI; no tenant shell, no plugin API.
    [Fact]
    public async Task Hybrid_assets_are_staged_and_served_without_a_tenant_shell()
    {
        const string hybrid = "hybrid-sample";
        HttpRequestMessage Stage(string version, string? sha, bool withKey = true)
        {
            var req = new HttpRequestMessage(HttpMethod.Put, $"/internal/plugins/{hybrid}/versions/{version}/assets")
            {
                Content = JsonContent.Create(new { packageSha256 = sha }),
            };
            if (withKey) req.Headers.Add("X-Internal-Key", Extensions.DevInternalKey);
            return req;
        }

        var sha = TestPlugins.Sha($"{hybrid}@1.0.0");
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/plugin-assets/{hybrid}/1.0.0/assets/remoteEntry.js")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Stage("1.0.0", sha, withKey: false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(Stage("1.0.0", "nope"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(Stage("1.0.0", sha))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(Stage("1.0.0", sha))).StatusCode); // idempotent
        var entry = await http.GetAsync($"/plugin-assets/{hybrid}/1.0.0/assets/remoteEntry.js");
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        Assert.Contains("hybrid", await entry.Content.ReadAsStringAsync());

        var view = new HttpRequestMessage(HttpMethod.Get, "/internal/plugins");
        view.Headers.Add("X-Internal-Key", Extensions.DevInternalKey);
        Assert.Empty((await DataOf(await http.SendAsync(view))).GetProperty("runtimes").EnumerateArray());

        var source = (FolderPackageSource)app.Services.GetRequiredService<IModuleAssetSource>();
        source.Unavailable[$"{hybrid}@2.0.0"] = true;
        var down = await http.SendAsync(Stage("2.0.0", TestPlugins.Sha($"{hybrid}@2.0.0")));
        Assert.Equal(HttpStatusCode.BadGateway, down.StatusCode);
        Assert.Equal(ErrorCodes.PluginHostUnavailable, await ErrorCode(down));
    }

    // SC-002: while tenant A's plugin is installed, upgraded, disabled and removed repeatedly,
    // tenant B and the host health check never see a failure.
    [Fact]
    public async Task Changes_for_one_tenant_never_interrupt_another_tenant()
    {
        Assert.Equal(HttpStatusCode.OK, (await Activate(TenantB, "1.0.0")).StatusCode);
        using var stop = new CancellationTokenSource();
        var failures = 0;
        var served = 0;

        async Task Hammer()
        {
            while (!stop.IsCancellationRequested)
            {
                using var res = await Data(TenantB);
                var health = await http.GetAsync("/healthz");
                if (res.StatusCode != HttpStatusCode.OK || health.StatusCode != HttpStatusCode.OK) Interlocked.Increment(ref failures);
                else Interlocked.Increment(ref served);
            }
        }

        var load = Enumerable.Range(0, 4).Select(_ => Task.Run(Hammer)).ToArray();
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Activate(TenantA, i % 2 == 0 ? "1.0.0" : "2.0.0")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Activate(TenantA, "2.0.0", enabled: false)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Deactivate(TenantA)).StatusCode);
        }
        await Task.Delay(200);
        stop.Cancel();
        await Task.WhenAll(load);

        Assert.Equal(0, failures);
        Assert.True(served > 20, $"only {served} requests were served");
        Assert.Equal("1.0.0", (await DataOf(await Data(TenantB))).GetProperty("version").GetString());
    }
}
