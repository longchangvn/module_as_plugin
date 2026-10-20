using System.Text.Json;
using System.Text.Json.Nodes;
using Gateway.Api.Models;

namespace Gateway.Api.Services;

// Pure per-tenant module state (R-GW1). Immutable once published: GatewayStore
// mutates a Clone() under its write gate and swaps the reference, so the
// request guard reads without locks.
//   modules[moduleId].Tenants[tenantId]  = { enabled, version }
//   modules[moduleId].Versions[version]  = { baseUrl, upstreams }  (only versions some tenant uses)
// Ocelot routes for a module come from the newest version any tenant has
// installed (D2) and exist while >= 1 tenant has the module.
public sealed class ModuleTenantState
{
    public sealed record TenantEntry(bool Enabled, string Version);
    public sealed record VersionSpec(string? BaseUrl, List<ModuleUpstream> Upstreams);

    public sealed class ModuleEntry
    {
        public Dictionary<string, TenantEntry> Tenants { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, VersionSpec> Versions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly Dictionary<string, ModuleEntry> _modules = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ModuleEntry> Modules => _modules;

    public ModuleTenantState Clone()
    {
        var copy = new ModuleTenantState();
        foreach (var (id, m) in _modules)
            copy._modules[id] = new ModuleEntry
            {
                Tenants = new(m.Tenants, StringComparer.OrdinalIgnoreCase),
                Versions = new(m.Versions, StringComparer.OrdinalIgnoreCase),
            };
        return copy;
    }

    public void Upsert(string moduleId, string tenantId, bool enabled, string version, string? baseUrl, List<ModuleUpstream> upstreams)
    {
        if (!_modules.TryGetValue(moduleId, out var m)) _modules[moduleId] = m = new ModuleEntry();
        m.Tenants[tenantId] = new TenantEntry(enabled, version);
        m.Versions[version] = new VersionSpec(baseUrl, upstreams);
        Prune(m);
    }

    // Returns false when the tenant had no entry (idempotent delete).
    public bool Remove(string moduleId, string tenantId)
    {
        if (!_modules.TryGetValue(moduleId, out var m) || !m.Tenants.Remove(tenantId)) return false;
        if (m.Tenants.Count == 0) _modules.Remove(moduleId);
        else Prune(m);
        return true;
    }

    public ModuleState Decide(string moduleId, string tenantId) =>
        _modules.TryGetValue(moduleId, out var m) && m.Tenants.TryGetValue(tenantId, out var t)
            ? t.Enabled ? ModuleState.Enabled : ModuleState.Disabled
            : ModuleState.NotInstalled;

    public bool HasModule(string moduleId) => _modules.ContainsKey(moduleId);

    // Routing spec = newest semver among versions installed by any tenant.
    public VersionSpec? Effective(string moduleId)
    {
        if (!_modules.TryGetValue(moduleId, out var m) || m.Tenants.Count == 0) return null;
        var newest = m.Tenants.Values.Select(t => t.Version).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(v => v, SemverComparer.Instance).First();
        return m.Versions.GetValueOrDefault(newest);
    }

    private static void Prune(ModuleEntry m)
    {
        var used = m.Tenants.Values.Select(t => t.Version).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var v in m.Versions.Keys.Where(v => !used.Contains(v)).ToList()) m.Versions.Remove(v);
    }

    // Number of (module, tenant) entries that differ between two states — reconcile drift metric.
    public static int Drift(ModuleTenantState a, ModuleTenantState b)
    {
        static IEnumerable<string> Keys(ModuleTenantState s) => s._modules.SelectMany(m =>
            m.Value.Tenants.Select(t => $"{m.Key.ToLowerInvariant()}|{t.Key.ToLowerInvariant()}|{t.Value.Enabled}|{t.Value.Version}|" +
                JsonSerializer.Serialize(m.Value.Versions.GetValueOrDefault(t.Value.Version))));
        var ka = Keys(a).ToHashSet();
        var kb = Keys(b).ToHashSet();
        return ka.Count + kb.Count - 2 * ka.Intersect(kb).Count();
    }

    // ---- persistence (gateway-modules.json, v2) ----
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public JsonNode ToJson() => new JsonObject
    {
        ["version"] = 2,
        ["modules"] = JsonSerializer.SerializeToNode(_modules, Json),
    };

    // v1 files ({"modules": {"id": "Enabled"}}) carry no tenant dimension:
    // they load as empty and the reconciler rebuilds state from the Platform.
    public static ModuleTenantState FromJson(JsonNode? doc, out bool legacy)
    {
        var state = new ModuleTenantState();
        legacy = false;
        if (doc?["modules"] is not JsonObject modules) return state;
        if (doc["version"]?.GetValue<int>() != 2) { legacy = modules.Count > 0; return state; }
        foreach (var (id, node) in modules)
        {
            var m = node.Deserialize<ModuleEntry>(Json);
            if (m is null || m.Tenants.Count == 0) continue;
            state._modules[id] = new ModuleEntry
            {
                Tenants = new(m.Tenants, StringComparer.OrdinalIgnoreCase),
                Versions = new(m.Versions, StringComparer.OrdinalIgnoreCase),
            };
            Prune(state._modules[id]);
        }
        return state;
    }

    public static ModuleTenantState FromSnapshot(IEnumerable<ModuleSnapshot> modules)
    {
        var state = new ModuleTenantState();
        foreach (var m in modules)
        {
            var versions = m.Versions.ToDictionary(v => v.Version, v => v, StringComparer.OrdinalIgnoreCase);
            foreach (var t in m.Tenants)
            {
                if (!versions.TryGetValue(t.Version, out var v)) continue;
                state.Upsert(m.ModuleId, t.TenantId, t.Enabled, t.Version, v.BaseUrl, v.Upstreams);
            }
        }
        return state;
    }
}

// Minimal SemVer 2.0 precedence: numeric major.minor.patch, release > pre-release,
// pre-release identifiers compared numerically or ordinally; build metadata ignored.
// Unparseable versions sort lowest.
public sealed class SemverComparer : IComparer<string>
{
    public static readonly SemverComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        var (cx, px) = Split(x);
        var (cy, py) = Split(y);
        if (cx is null || cy is null) return (cx is null ? 0 : 1) - (cy is null ? 0 : 1);
        var c = cx.CompareTo(cy);
        if (c != 0) return c;
        if (px is null || py is null) return (px is null ? 1 : 0) - (py is null ? 1 : 0);
        var ax = px.Split('.');
        var ay = py.Split('.');
        for (var i = 0; i < Math.Min(ax.Length, ay.Length); i++)
        {
            var nx = long.TryParse(ax[i], out var lx);
            var ny = long.TryParse(ay[i], out var ly);
            c = nx && ny ? lx.CompareTo(ly) : nx ? -1 : ny ? 1 : string.CompareOrdinal(ax[i], ay[i]);
            if (c != 0) return c;
        }
        return ax.Length.CompareTo(ay.Length);
    }

    private static (Version? Core, string? Pre) Split(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return (null, null);
        var s = v.Split('+')[0];
        var dash = s.IndexOf('-');
        var core = dash < 0 ? s : s[..dash];
        return (Version.TryParse(core, out var ver) ? ver : null, dash < 0 ? null : s[(dash + 1)..]);
    }
}
