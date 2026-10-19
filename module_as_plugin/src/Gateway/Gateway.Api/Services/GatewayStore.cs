using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gateway.Api.Models;
using Ocelot.Configuration.Creator;
using Ocelot.Configuration.File;
using Ocelot.Configuration.Repository;

namespace Gateway.Api.Services;

// File-backed store: single writer, atomic temp+move, true hot-apply (SR-3):
// every successful write is validated + swapped into the running route table
// via Ocelot's IInternalConfigurationCreator + IInternalConfigurationRepository
// (no restart, no file-poll latency).
public sealed class GatewayStore : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _ocelotPath;
    private readonly string _servicesPath;
    private readonly string _modulesPath;
    private readonly IHostEnvironment _env;
    private readonly ILogger<GatewayStore> _log;
    private readonly IInternalConfigurationCreator _creator;
    private readonly IInternalConfigurationRepository _repo;
    private readonly ConcurrentDictionary<string, ModuleState> _modules = new(StringComparer.OrdinalIgnoreCase);

    public GatewayStore(IHostEnvironment env, ILogger<GatewayStore> log,
        IInternalConfigurationCreator creator, IInternalConfigurationRepository repo)
    {
        _env = env;
        _log = log;
        _creator = creator;
        _repo = repo;
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

    // SR-3 hot apply: validate the candidate config with Ocelot's own creator,
    // persist atomically, then swap the running route table. Any validation
    // failure leaves both the file and the running config untouched (BR-R6).
    private async Task<(bool Ok, string? Error)> ValidatePersistSwapAsync(string source, JsonNode ocelot)
    {
        FileConfiguration candidate;
        try { candidate = ocelot.Deserialize<FileConfiguration>() ?? throw new InvalidOperationException("empty config"); }
        catch (Exception ex) { return (false, $"Invalid Ocelot config JSON: {ex.Message}"); }
        var created = await _creator.Create(candidate);
        if (created.IsError)
            return (false, string.Join("; ", created.Errors.Select(e => e.Message)));
        await WriteJsonAsync(_ocelotPath, ocelot);
        _repo.AddOrReplace(created.Data);
        _log.LogInformation("gateway config applied source={source} routes={routes}", source, created.Data.Routes.Length);
        return (true, null);
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
            var (ok, err) = await ValidatePersistSwapAsync("admin-ui", ocelot);
            if (!ok) return (false, null, err, "CONFIG_INVALID");
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
            var (ok, err) = await ValidatePersistSwapAsync("admin-ui", ocelot);
            if (!ok) return (false, null, err, "CONFIG_INVALID");
            return (true, new { id }, null, null);
        }
        finally { _gate.Release(); }
    }

    // Reload: re-read ocelot.json, validate, swap (SR-7). Manual edits are
    // picked up without a restart; invalid files are rejected, running config
    // untouched.
    public async Task<(bool Ok, string? Error)> ReloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var ocelot = JsonNode.Parse(await File.ReadAllTextAsync(_ocelotPath))!.AsObject();
            var (ok, err) = await ValidatePersistSwapAsync("reload", ocelot);
            return (ok, err);
        }
        catch (Exception ex) { return (false, ex.Message); }
        finally { _gate.Release(); }
    }

    // Downstream target for module routes (A3 fix — no more hardcoded
    // localhost:5200): explicit baseUrl from the platform (module artifact URL)
    // wins, else a registered service with id == moduleId, else the
    // ModuleRoutes:DefaultBaseUrl config, else localhost:5200 (dev fallback).
    private (string Scheme, string Host, int Port) ResolveModuleDownstream(string moduleId, string? baseUrl, JsonNode services)
    {
        if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return (uri.Scheme, uri.Host, uri.IsDefaultPort ? (uri.Scheme == "https" ? 443 : 80) : uri.Port);
        var svc = services["services"]?.AsArray().FirstOrDefault(n => n?["id"]?.GetValue<string>() == moduleId);
        if (svc is not null)
            return (svc["scheme"]?.GetValue<string>() ?? "http", svc["host"]!.GetValue<string>(), svc["port"]!.GetValue<int>());
        var fallback = Environment.GetEnvironmentVariable("GATEWAY_MODULE_DEFAULT_BASEURL") ?? "http://localhost:5200";
        var f = Uri.TryCreate(fallback, UriKind.Absolute, out var fu) ? fu : new Uri("http://localhost:5200");
        return (f.Scheme, f.Host, f.IsDefaultPort ? 80 : f.Port);
    }

    public async Task<(bool Ok, int Value, string? Error)> ApplyModuleRoutesAsync(string moduleId, List<ModuleUpstream> upstreams, bool enabled, bool remove = false, string? baseUrl = null)
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
                var (scheme, host, port) = ResolveModuleDownstream(moduleId, baseUrl, ReadServices());
                foreach (var u in upstreams)
                {
                    routes.Add(JsonNode.Parse(JsonSerializer.Serialize(new
                    {
                        Key = $"rt_{moduleId}_{Math.Abs(u.Upstream.GetHashCode()):x8}",
                        UpstreamPathTemplate = u.Upstream,
                        UpstreamHttpMethod = u.Methods,
                        DownstreamPathTemplate = u.Downstream,
                        DownstreamScheme = scheme,
                        DownstreamHostAndPorts = new[] { new { Host = host, Port = port } },
                        Priority = 10,
                        Metadata = new Dictionary<string, string> { ["serviceId"] = moduleId, ["source"] = "module-install", ["managedBy"] = $"module:{moduleId}" },
                    })));
                }
            }
            var (ok, err) = await ValidatePersistSwapAsync("module-install", ocelot);
            if (!ok) return (false, 0, err);
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
            var (ok, err) = await ValidatePersistSwapAsync("admin-ui", ocelot);
            if (!ok) return (false, null, err, "CONFIG_INVALID");
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
