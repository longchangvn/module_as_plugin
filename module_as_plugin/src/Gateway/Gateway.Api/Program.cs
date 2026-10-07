// Gateway API :5100 — Ocelot proxy + admin API (/api/v1/gateway/**).
// Pipeline: CORS -> TrafficMiddleware -> admin branch (auth) -> tenant/module guard -> Ocelot.
// Config: ocelot.json (persistent) + gateway-services.json registry; single writer (SemaphoreSlim).
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOcelot(builder.Configuration);
builder.Services.AddSingleton<GatewayStore>();
builder.Services.AddSingleton<TrafficRecorder>();

var jwtSecret = builder.Configuration["Jwt:SecretKey"] ?? "dev-only-secret-key-min-32-chars!!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.FromMinutes(1),
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("GatewayViewer", p => p.RequireAuthenticatedUser())
    .AddPolicy("GatewayAdmin", p => p.RequireClaim("platform_role", "Admin"));

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

bool UseDevKey(HttpContext ctx, IConfiguration cfg) =>
    app.Environment.IsDevelopment() &&
    ctx.Request.Headers.TryGetValue("X-API-Key", out var key) &&
    key == (cfg["Gateway:DevApiKey"] ?? "dev-key");

app.UseMiddleware<TrafficMiddleware>();

// Admin branch — reserved prefix, never proxied (SR-1).
app.MapGet("/api/v1/gateway/summary", (GatewayStore store, TrafficRecorder traffic) =>
{
    var summary = store.GetSummary();
    return Results.Json(new { data = summary with { TotalRequests = traffic.Total } });
});
app.MapGet("/api/v1/gateway/services", async (GatewayStore store, bool health = false) =>
    Results.Json(new { data = await store.ServicesWithHealthAsync(health), count = store.Services().Count }));
app.MapPost("/api/v1/gateway/services", async (GatewayStore store, HttpContext ctx, ServiceUpsert req) =>
{
    if (!IsWriter(ctx, app.Configuration, UseDevKey(ctx, app.Configuration)))
        return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
    var result = await store.AddServiceAsync(req);
    return result.Ok ? Results.Json(new { data = result.Value }, statusCode: 201)
        : Results.Json(new { error = new { code = result.ErrorCode, message = result.Error } }, statusCode: 409);
});
app.MapPut("/api/v1/gateway/services/{id}", async (GatewayStore store, HttpContext ctx, string id, ServiceUpsert req) =>
{
    if (!IsWriter(ctx, app.Configuration, UseDevKey(ctx, app.Configuration)))
        return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
    var result = await store.UpdateServiceAsync(id, req);
    return result.Ok ? Results.Json(new { data = result.Value })
        : Results.Json(new { error = new { code = result.ErrorCode, message = result.Error } }, statusCode: 404);
});
app.MapDelete("/api/v1/gateway/services/{id}", async (GatewayStore store, HttpContext ctx, string id) =>
{
    if (!IsWriter(ctx, app.Configuration, UseDevKey(ctx, app.Configuration)))
        return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
    var result = await store.DeleteServiceAsync(id);
    return result.Ok ? Results.Json(new { data = new { id } })
        : Results.Json(new { error = new { code = result.ErrorCode, message = result.Error, routes = result.Value } },
            statusCode: result.ErrorCode == "NOT_FOUND" ? 404 : 409);
});
app.MapGet("/api/v1/gateway/routes", (GatewayStore store) =>
    Results.Json(new { data = store.Routes() }));
app.MapPost("/api/v1/gateway/routes", async (GatewayStore store, HttpContext ctx, RouteUpsert req) =>
{
    if (!IsWriter(ctx, app.Configuration, UseDevKey(ctx, app.Configuration)))
        return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
    var result = await store.AddRouteAsync(req);
    if (!result.Ok)
    {
        var status = result.ErrorCode is "ROUTE_EXISTS" ? 409 : 400;
        return Results.Json(new { error = new { code = result.ErrorCode, message = result.Error, retryable = false } }, statusCode: status);
    }
    return Results.Json(new { data = result.Value }, statusCode: 201);
});
app.MapPut("/api/v1/gateway/routes/{id}", async (GatewayStore store, HttpContext ctx, string id, RouteUpsert req) =>
{
    if (!IsWriter(ctx, app.Configuration, UseDevKey(ctx, app.Configuration)))
        return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
    var result = await store.UpdateRouteAsync(id, req);
    if (!result.Ok)
    {
        var status = result.ErrorCode == "NOT_FOUND" ? 404 : result.ErrorCode == "ROUTE_EXISTS" ? 409 : 400;
        return Results.Json(new { error = new { code = result.ErrorCode, message = result.Error, retryable = false } }, statusCode: status);
    }
    return Results.Json(new { data = result.Value });
});
app.MapDelete("/api/v1/gateway/routes/{id}", async (GatewayStore store, HttpContext ctx, string id, bool force = false) =>
{
    if (!IsWriter(ctx, app.Configuration, UseDevKey(ctx, app.Configuration)))
        return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
    var result = await store.DeleteRouteAsync(id, force);
    return result.Ok ? Results.Json(new { data = new { id } })
        : Results.Json(new { error = new { code = result.ErrorCode, message = result.Error } }, statusCode: result.ErrorCode == "NOT_FOUND" ? 404 : 409);
});
app.MapPost("/api/v1/gateway/config/reload", async (GatewayStore store, HttpContext ctx) =>
{
    if (!IsWriter(ctx, app.Configuration, UseDevKey(ctx, app.Configuration)))
        return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
    await store.ReloadAsync();
    return Results.Json(new { data = new { reloaded = true } });
});
app.MapGet("/api/v1/gateway/traffic", (TrafficRecorder traffic, int windowMinutes = 60) =>
    Results.Json(new { data = traffic.Snapshot(windowMinutes) }));
app.MapPost("/api/v1/gateway/traffic/reset", (HttpContext ctx, TrafficRecorder traffic) =>
{
    if (!IsWriter(ctx, app.Configuration, UseDevKey(ctx, app.Configuration)))
        return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
    traffic.Clear();
    return Results.Json(new { data = new { reset = true } });
});

// Internal: platform lifecycle writes module routes (source=module-install, audited).
app.MapPost("/api/v1/gateway/internal/module-routes", async (GatewayStore store, ModuleRoutesRequest req) =>
{
    var result = await store.ApplyModuleRoutesAsync(req.ModuleId, req.Upstreams, req.Enabled, req.Remove);
    return result.Ok ? Results.Json(new { data = new { applied = result.Value } })
        : Results.Json(new { error = new { code = "GATEWAY_APPLY_FAILED", message = result.Error } }, statusCode: 502);
});

// Tenant + module-enabled guard before proxying (SR-4, G-BR5).
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/v1/gateway") || ctx.Request.Path is { Value: "/healthz" or "/readyz" or "/swagger" })
    { await next(); return; }
    var store = ctx.RequestServices.GetRequiredService<GatewayStore>();
    var moduleId = ModuleFromPath(ctx.Request.Path.Value ?? "");
    if (moduleId is not null)
    {
        var state = store.GetModuleState(moduleId);
        if (state == ModuleState.NotInstalled)
        {
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsJsonAsync(new { error = new { code = "UNKNOWN_MODULE_ROUTE", message = $"No route for module '{moduleId}'." } });
            return;
        }
        if (state == ModuleState.Disabled)
        {
            ctx.Response.StatusCode = 503;
            await ctx.Response.WriteAsJsonAsync(new { error = new { code = "MODULE_DISABLED", message = $"Module '{moduleId}' is disabled." } });
            return;
        }
    }
    await next();
});

app.MapGet("/healthz", () => Results.Json(new { status = "ok", service = "gateway" }));

// Ocelot is terminal: run it on a branch for non-admin paths only, so the admin API
// (/api/v1/gateway/**), /healthz and /swagger are answered by endpoints above.
// (Fallback pattern from the gateway requirement: MapWhen branch with its own routing.)
app.MapWhen(
    ctx => !IsLocalPath(ctx.Request.Path),
    branch => { branch.UseOcelot().GetAwaiter().GetResult(); });
app.Run();

static bool IsLocalPath(PathString path) =>
    path.StartsWithSegments("/api/v1/gateway") ||
    path.StartsWithSegments("/swagger") ||
    string.Equals(path.Value, "/healthz", StringComparison.OrdinalIgnoreCase);

static bool IsWriter(HttpContext ctx, IConfiguration cfg, bool devKey)
{
    if (devKey) return true;
    return ctx.User.Identity?.IsAuthenticated == true &&
        ctx.User.FindFirst("platform_role")?.Value == "Admin";
}

static string? ModuleFromPath(string path)
{
    // /api/v1/{moduleId}/{rest} — platform + auth are always installed+enabled (A5).
    var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length >= 3 && segments[0] == "api" && segments[1] == "v1")
    {
        var candidate = segments[2];
        if (candidate is "platform" or "auth" or "tenant" or "gateway") return null;
        return candidate;
    }
    return null;
}

public enum ModuleState { NotInstalled, Enabled, Disabled }

public sealed record GatewaySummary(
    string ServiceName,
    string Environment,
    int ServiceCount,
    int RouteCount,
    long TotalRequests,
    string ConfigPath,
    bool ExternalFileModified);

public sealed record ServiceUpsert(string Name, string Host, int Port, string Scheme, string? Description);
public sealed record RouteUpsert(string UpstreamPathTemplate, string DownstreamPathTemplate, string[] Methods, int Priority, string ServiceId);
public sealed record ModuleRoutesRequest(string ModuleId, List<ModuleUpstream> Upstreams, bool Enabled, bool Remove = false);
public sealed record ModuleUpstream(string Upstream, string Downstream, string[] Methods);

// File-backed store: single writer, atomic temp+move, hot-apply notes in README.
public sealed class GatewayStore : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _ocelotPath;
    private readonly string _servicesPath;
    private readonly string _modulesPath;
    private readonly IHostEnvironment _env;
    private readonly ILogger<GatewayStore> _log;
    private readonly ConcurrentDictionary<string, ModuleState> _modules = new(StringComparer.OrdinalIgnoreCase);

    public GatewayStore(IHostEnvironment env, ILogger<GatewayStore> log)
    {
        _env = env;
        _log = log;
        _ocelotPath = Path.Combine(env.ContentRootPath, "ocelot.json");
        _servicesPath = Path.Combine(env.ContentRootPath, "gateway-services.json");
        _modulesPath = Path.Combine(env.ContentRootPath, "gateway-modules.json");
        LoadModuleStates();
    }

    public void Dispose() => _gate.Dispose();

    private void LoadModuleStates()
    {
        try
        {
            if (!File.Exists(_modulesPath)) return;
            var doc = JsonNode.Parse(File.ReadAllText(_modulesPath))?.AsObject();
            foreach (var (key, node) in doc?["modules"]?.AsObject() ?? new JsonObject())
            {
                if (Enum.TryParse<ModuleState>(node?.GetValue<string>(), out var state))
                    _modules[key] = state;
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not load {path}; module states start empty", _modulesPath); }
    }

    private async Task PersistModuleStatesAsync()
    {
        var doc = new JsonObject
        {
            ["modules"] = new JsonObject(_modules.Select(kv =>
                new KeyValuePair<string, JsonNode?>(kv.Key, JsonValue.Create(kv.Value.ToString())))),
        };
        await WriteJsonAsync(_modulesPath, doc);
    }

    public List<object> Services()
    {
        var doc = ReadServices();
        return doc["services"]?.AsArray().Select(n => (object)n!.ToJsonString()).ToList() ?? new();
    }

    public List<object> Routes()
    {
        var json = File.ReadAllText(_ocelotPath);
        var doc = JsonNode.Parse(json)?.AsObject();
        return doc?["Routes"]?.AsArray().Select(n => (object)n!.ToJsonString()).ToList() ?? new();
    }

    public GatewaySummary GetSummary()
    {
        var routes = Routes();
        return new GatewaySummary(
            "ModulePlatform gateway",
            _env.EnvironmentName,
            Services().Count,
            routes.Count,
            0L,
            _ocelotPath,
            false);
    }

    public async Task<(bool Ok, object? Value, string? Error, string? ErrorCode)> AddServiceAsync(ServiceUpsert req)
    {
        if (string.IsNullOrWhiteSpace(req.Host) || req.Port is < 1 or > 65535)
            return (false, null, "Host required, port 1..65535.", "CONFIG_INVALID");
        if (req.Scheme is not ("http" or "https"))
            return (false, null, "Scheme must be http|https.", "CONFIG_INVALID");
        await _gate.WaitAsync();
        try
        {
            var doc = ReadServices();
            var arr = doc["services"]!.AsArray();
            var id = Slug(req.Name);
            if (arr.Any(n => n?["id"]?.GetValue<string>() == id))
                return (false, null, $"Service '{id}' exists.", "SERVICE_EXISTS");
            arr.Add(JsonNode.Parse(JsonSerializer.Serialize(new
            {
                id, name = req.Name, host = req.Host, port = req.Port,
                scheme = req.Scheme, description = req.Description, enabled = true,
                createdAt = DateTimeOffset.UtcNow, updatedAt = DateTimeOffset.UtcNow,
            })));
            await WriteJsonAsync(_servicesPath, doc);
            _log.LogInformation("gateway service added {id}", id);
            return (true, new { id }, null, null);
        }
        finally { _gate.Release(); }
    }

    public async Task<(bool Ok, object? Value, string? Error, string? ErrorCode)> AddRouteAsync(RouteUpsert req)
    {
        if (!req.UpstreamPathTemplate.StartsWith('/'))
            return (false, null, "UpstreamPathTemplate must start with '/'.", "CONFIG_INVALID");
        if (req.UpstreamPathTemplate.StartsWith("/api/v1/gateway", StringComparison.OrdinalIgnoreCase))
            return (false, null, "Reserved admin prefix.", "CONFIG_INVALID");
        if (req.Priority is < 0 or > 999)
            return (false, null, "Priority 0..999.", "CONFIG_INVALID");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };
        if (req.Methods.Any(m => !allowed.Contains(m)))
            return (false, null, "Invalid method.", "CONFIG_INVALID");

        await _gate.WaitAsync();
        try
        {
            var services = ReadServices();
            var svc = services["services"]!.AsArray()
                .FirstOrDefault(n => n?["id"]?.GetValue<string>() == req.ServiceId);
            if (svc is null) return (false, null, $"Unknown service '{req.ServiceId}'.", "NOT_FOUND");

            var ocelot = JsonNode.Parse(await File.ReadAllTextAsync(_ocelotPath))!.AsObject();
            var routes = ocelot["Routes"]!.AsArray();
            if (routes.Any(n => string.Equals(n?["UpstreamPathTemplate"]?.GetValue<string>(), req.UpstreamPathTemplate, StringComparison.OrdinalIgnoreCase)))
                return (false, null, "Duplicate upstream template.", "ROUTE_EXISTS");

            var id = "rt_" + Guid.NewGuid().ToString("N")[..8];
            routes.Add(JsonNode.Parse(JsonSerializer.Serialize(new
            {
                Key = id,
                UpstreamPathTemplate = req.UpstreamPathTemplate,
                UpstreamHttpMethod = req.Methods,
                DownstreamPathTemplate = req.DownstreamPathTemplate,
                DownstreamScheme = svc["scheme"]!.GetValue<string>(),
                DownstreamHostAndPorts = new[] { new { Host = svc["host"]!.GetValue<string>(), Port = svc["port"]!.GetValue<int>() } },
                Priority = req.Priority,
                Metadata = new Dictionary<string, string> { ["serviceId"] = req.ServiceId, ["source"] = "admin-ui" },
            })));
            await WriteJsonAsync(_ocelotPath, ocelot);
            _log.LogInformation("gateway route added {id} {upstream}", id, req.UpstreamPathTemplate);
            return (true, new { id }, null, null);
        }
        finally { _gate.Release(); }
    }

    public async Task<(bool Ok, object? Value, string? Error, string? ErrorCode)> DeleteRouteAsync(string id, bool force)
    {
        await _gate.WaitAsync();
        try
        {
            var ocelot = JsonNode.Parse(await File.ReadAllTextAsync(_ocelotPath))!.AsObject();
            var routes = ocelot["Routes"]!.AsArray();
            var node = routes.FirstOrDefault(n => n?["Key"]?.GetValue<string>() == id);
            if (node is null) return (false, null, "Not found.", "NOT_FOUND");
            if (routes.Count == 1 && !force)
                return (false, null, "Refusing to delete the last route without force=true.", "LAST_ROUTE");
            routes.Remove(node);
            await WriteJsonAsync(_ocelotPath, ocelot);
            return (true, new { id }, null, null);
        }
        finally { _gate.Release(); }
    }

    public Task ReloadAsync() => Task.CompletedTask; // reloadOnChange:true picks up manual edits; hot-swap documented in README.

    public async Task<(bool Ok, int Value, string? Error)> ApplyModuleRoutesAsync(string moduleId, List<ModuleUpstream> upstreams, bool enabled, bool remove = false)
    {
        if (remove) _modules.TryRemove(moduleId, out _);
        else _modules[moduleId] = enabled ? ModuleState.Enabled : ModuleState.Disabled;
        await _gate.WaitAsync();
        try
        {
            var ocelot = JsonNode.Parse(await File.ReadAllTextAsync(_ocelotPath))!.AsObject();
            var routes = ocelot["Routes"]!.AsArray();
            // remove previous module-install rows for this module, then re-add when enabled
            foreach (var old in routes.Where(n => n?["Metadata"]?["managedBy"]?.GetValue<string>() == $"module:{moduleId}").ToList())
                routes.Remove(old);
            if (enabled)
            {
                foreach (var u in upstreams)
                {
                    routes.Add(JsonNode.Parse(JsonSerializer.Serialize(new
                    {
                        Key = $"rt_{moduleId}_{Math.Abs(u.Upstream.GetHashCode()):x8}",
                        UpstreamPathTemplate = u.Upstream,
                        UpstreamHttpMethod = u.Methods,
                        DownstreamPathTemplate = u.Downstream,
                        DownstreamScheme = "http",
                        DownstreamHostAndPorts = new[] { new { Host = "localhost", Port = 5200 } },
                        Priority = 10,
                        Metadata = new Dictionary<string, string> { ["serviceId"] = moduleId, ["source"] = "module-install", ["managedBy"] = $"module:{moduleId}" },
                    })));
                }
            }
            await WriteJsonAsync(_ocelotPath, ocelot);
            await PersistModuleStatesAsync();
            return (true, upstreams.Count, null);
        }
        catch (Exception ex) { return (false, 0, ex.Message); }
        finally { _gate.Release(); }
    }

    public ModuleState GetModuleState(string moduleId) =>
        _modules.TryGetValue(moduleId, out var s) ? s : ModuleState.NotInstalled;

    public async Task<List<object>> ServicesWithHealthAsync(bool checkHealth)
    {
        var doc = ReadServices();
        var list = doc["services"]?.AsArray().Select(n => n!.AsObject()).ToList() ?? new();
        if (!checkHealth) return list.Select(n => (object)n.ToJsonString()).ToList();
        var results = new List<object>();
        foreach (var svc in list)
        {
            var health = await ProbeAsync(svc["host"]?.GetValue<string>() ?? "", svc["port"]?.GetValue<int>() ?? 0);
            svc["health"] = health;
            results.Add(svc.ToJsonString());
        }
        return results;
    }

    private static async Task<string> ProbeAsync(string host, int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await client.ConnectAsync(host, port, cts.Token);
            return "up";
        }
        catch (OperationCanceledException) { return "unknown"; }
        catch { return "down"; }
    }

    public async Task<(bool Ok, object? Value, string? Error, string? ErrorCode)> UpdateServiceAsync(string id, ServiceUpsert req)
    {
        if (string.IsNullOrWhiteSpace(req.Host) || req.Port is < 1 or > 65535)
            return (false, null, "Host required, port 1..65535.", "CONFIG_INVALID");
        if (req.Scheme is not ("http" or "https"))
            return (false, null, "Scheme must be http|https.", "CONFIG_INVALID");
        await _gate.WaitAsync();
        try
        {
            var doc = ReadServices();
            var node = doc["services"]!.AsArray().FirstOrDefault(n => n?["id"]?.GetValue<string>() == id)?.AsObject();
            if (node is null) return (false, null, $"Unknown service '{id}'.", "NOT_FOUND");
            node["host"] = req.Host; node["port"] = req.Port; node["scheme"] = req.Scheme;
            if (req.Description is not null) node["description"] = req.Description;
            node["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
            await WriteJsonAsync(_servicesPath, doc);
            _log.LogInformation("gateway service updated {id}", id);
            return (true, new { id }, null, null);
        }
        finally { _gate.Release(); }
    }

    public async Task<(bool Ok, object? Value, string? Error, string? ErrorCode)> DeleteServiceAsync(string id)
    {
        await _gate.WaitAsync();
        try
        {
            var ocelot = JsonNode.Parse(await File.ReadAllTextAsync(_ocelotPath))!.AsObject();
            var refs = ocelot["Routes"]!.AsArray()
                .Where(n => n?["Metadata"]?["serviceId"]?.GetValue<string>() == id)
                .Select(n => n?["Key"]?.GetValue<string>() ?? n?["UpstreamPathTemplate"]?.GetValue<string>() ?? "?")
                .ToList();
            if (refs.Count > 0) // BR-R2
                return (false, refs, $"Service '{id}' is referenced by routes.", "SERVICE_IN_USE");
            var doc = ReadServices();
            var arr = doc["services"]!.AsArray();
            var node = arr.FirstOrDefault(n => n?["id"]?.GetValue<string>() == id);
            if (node is null) return (false, null, $"Unknown service '{id}'.", "NOT_FOUND");
            arr.Remove(node);
            await WriteJsonAsync(_servicesPath, doc);
            _log.LogInformation("gateway service deleted {id}", id);
            return (true, new { id }, null, null);
        }
        finally { _gate.Release(); }
    }

    public async Task<(bool Ok, object? Value, string? Error, string? ErrorCode)> UpdateRouteAsync(string id, RouteUpsert req)
    {
        if (!req.UpstreamPathTemplate.StartsWith('/'))
            return (false, null, "UpstreamPathTemplate must start with '/'.", "CONFIG_INVALID");
        if (req.UpstreamPathTemplate.StartsWith("/api/v1/gateway", StringComparison.OrdinalIgnoreCase))
            return (false, null, "Reserved admin prefix.", "CONFIG_INVALID");
        if (req.Priority is < 0 or > 999)
            return (false, null, "Priority 0..999.", "CONFIG_INVALID");
        await _gate.WaitAsync();
        try
        {
            var services = ReadServices();
            var svc = services["services"]!.AsArray()
                .FirstOrDefault(n => n?["id"]?.GetValue<string>() == req.ServiceId);
            if (svc is null) return (false, null, $"Unknown service '{req.ServiceId}'.", "NOT_FOUND");
            var ocelot = JsonNode.Parse(await File.ReadAllTextAsync(_ocelotPath))!.AsObject();
            var routes = ocelot["Routes"]!.AsArray();
            var node = routes.FirstOrDefault(n => n?["Key"]?.GetValue<string>() == id)?.AsObject();
            if (node is null) return (false, null, $"Unknown route '{id}'.", "NOT_FOUND");
            if (routes.Any(n => n != node && string.Equals(n?["UpstreamPathTemplate"]?.GetValue<string>(),
                req.UpstreamPathTemplate, StringComparison.OrdinalIgnoreCase)))
                return (false, null, "Duplicate upstream template.", "ROUTE_EXISTS");
            node["UpstreamPathTemplate"] = req.UpstreamPathTemplate;
            node["DownstreamPathTemplate"] = req.DownstreamPathTemplate;
            node["UpstreamHttpMethod"] = new JsonArray(req.Methods.Select(m => JsonValue.Create<string>(m)).ToArray());
            node["Priority"] = req.Priority;
            node["DownstreamScheme"] = svc["scheme"]!.GetValue<string>();
            node["DownstreamHostAndPorts"] = new JsonArray(
                new JsonObject { ["Host"] = svc["host"]!.GetValue<string>(), ["Port"] = svc["port"]!.GetValue<int>() });
            await WriteJsonAsync(_ocelotPath, ocelot);
            _log.LogInformation("gateway route updated {id}", id);
            return (true, new { id }, null, null);
        }
        finally { _gate.Release(); }
    }

    private JsonObject ReadServices()
    {
        if (!File.Exists(_servicesPath))
            return new JsonObject { ["services"] = new JsonArray() };
        return JsonNode.Parse(File.ReadAllText(_servicesPath))!.AsObject();
    }

    private static async Task WriteJsonAsync(string path, JsonNode doc)
    {
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }

    private static string Slug(string name)
    {
        var slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}

public sealed class TrafficRecorder
{
    private long _total;
    private readonly ConcurrentDictionary<string, long> _byRoute = new();
    public long Total => _total;
    public void Record(string route) { Interlocked.Increment(ref _total); _byRoute.AddOrUpdate(route, 1, (_, v) => v + 1); }
    public void Clear() { Interlocked.Exchange(ref _total, 0); _byRoute.Clear(); }
    public object Snapshot(int windowMinutes) => new
    {
        windowMinutes,
        total = _total,
        routes = _byRoute.Select(kv => new { route = kv.Key, count = kv.Value }).ToList(),
    };
}

public sealed class TrafficMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, TrafficRecorder recorder)
    {
        await next(ctx);
        recorder.Record(ctx.Request.Path.Value ?? "(unknown)");
    }
}

public partial class Program;
