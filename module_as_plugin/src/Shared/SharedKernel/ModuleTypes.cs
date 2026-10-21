using System.Text.RegularExpressions;

namespace SharedKernel;

// Type-specific manifest rules (FR-001..FR-003). ManifestValidator delegates to the rules of
// the manifest's module.type; adding a type = one IModuleTypeRules + ModuleTypeRegistry.Register
// (plus a Platform IModuleHostingStrategy). Pure functions, no I/O.
public interface IModuleTypeRules
{
    string Type { get; }
    IEnumerable<ManifestValidator.ValidationFailure> Validate(ManifestFile manifest, bool isDevelopment);
}

public static class ModuleTypeRegistry
{
    private static readonly Dictionary<string, IModuleTypeRules> Rules = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    static ModuleTypeRegistry()
    {
        Register(new RemoteModuleTypeRules());
        Register(new PluginModuleTypeRules());
    }

    public static void Register(IModuleTypeRules rules)
    {
        lock (Gate) Rules[rules.Type] = rules;
    }

    public static IModuleTypeRules? Find(string? type)
    {
        lock (Gate) return Rules.GetValueOrDefault(ModuleTypeNames.Normalize(type));
    }

    public static IReadOnlyList<string> Supported
    {
        get { lock (Gate) return Rules.Keys.Order(StringComparer.Ordinal).ToList(); }
    }

    // Shared by remote rules: absolute http(s); https outside Development unless allowed.
    internal static bool IsAbsoluteUrl(string? url, bool requireHttps, out Uri? uri) =>
        Uri.TryCreate(url, UriKind.Absolute, out uri) &&
        (uri.Scheme == "https" || (!requireHttps && uri.Scheme == "http"));
}

// Standalone host reached over the network (today's behaviour + network zones, FR-024..FR-026).
public sealed class RemoteModuleTypeRules : IModuleTypeRules
{
    public string Type => ModuleTypeNames.Remote;

    public IEnumerable<ManifestValidator.ValidationFailure> Validate(ManifestFile m, bool isDevelopment)
    {
        if (!Uri.TryCreate(m.Frontend.RemoteUrl, UriKind.Absolute, out var remoteUri)
            || (remoteUri.Scheme != "https" && !(isDevelopment && remoteUri.Scheme == "http")))
            yield return new("frontend.remoteUrl", "Must be absolute https (http allowed in Development only).");

        var network = string.IsNullOrWhiteSpace(m.Service.Network) ? ModuleNetworks.SameCluster : m.Service.Network.Trim().ToLowerInvariant();
        if (!ModuleNetworks.All.Contains(network))
        {
            yield return new("service.network", "Must be same-cluster, cross-cluster or public.");
            yield break;
        }
        if (string.IsNullOrWhiteSpace(m.Service.BaseUrl)) yield break;
        var requireHttps = network != ModuleNetworks.SameCluster && !isDevelopment;
        if (!ModuleTypeRegistry.IsAbsoluteUrl(m.Service.BaseUrl, requireHttps, out _))
            yield return new("service.baseUrl", requireHttps
                ? $"Must be an absolute https URL for {network} modules (http allowed in Development only)."
                : "Must be an absolute http(s) URL.");
    }

    // Transport rule for an artifact URL supplied at publish (overrides service.baseUrl).
    public static string? ValidateArtifactUrl(ManifestFile m, string? artifactApiUrl, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(artifactApiUrl)) return null;
        var network = string.IsNullOrWhiteSpace(m.Service.Network) ? ModuleNetworks.SameCluster : m.Service.Network.Trim().ToLowerInvariant();
        var requireHttps = network != ModuleNetworks.SameCluster && !isDevelopment;
        return ModuleTypeRegistry.IsAbsoluteUrl(artifactApiUrl, requireHttps, out _)
            ? null
            : requireHttps
                ? $"artifactApiUrl must be an absolute https URL for {network} modules (http allowed in Development only)."
                : "artifactApiUrl must be an absolute http(s) URL.";
    }
}

// In-process plugin loaded by the Module Host (FR-011..FR-023).
public sealed partial class PluginModuleTypeRules : IModuleTypeRules
{
    public static readonly string[] SupportedContracts = ["1.0"];

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*\\.dll$")]
    private static partial Regex AssemblyRegex();

    public string Type => ModuleTypeNames.Plugin;

    public static bool IsValidAssemblyName(string? name) => !string.IsNullOrWhiteSpace(name) && AssemblyRegex().IsMatch(name);

    public IEnumerable<ManifestValidator.ValidationFailure> Validate(ManifestFile m, bool isDevelopment)
    {
        if (!IsValidAssemblyName(m.Plugin?.Assembly))
            yield return new("plugin.assembly", "Required for plugin modules: file name of the entry assembly (*.dll).");
        var contract = string.IsNullOrWhiteSpace(m.Plugin?.Contract) ? "1.0" : m.Plugin!.Contract.Trim();
        if (!SupportedContracts.Contains(contract))
            yield return new("plugin.contract", $"Unsupported plugin contract '{contract}'. Supported: {string.Join(", ", SupportedContracts)}.");
        if (!string.IsNullOrWhiteSpace(m.Service.BaseUrl))
            yield return new("service.baseUrl", "Plugin modules are hosted by the platform; remove service.baseUrl.");
        // Empty remoteUrl = served by the Module Host from the package's web/ folder.
        if (!string.IsNullOrWhiteSpace(m.Frontend.RemoteUrl) &&
            (!Uri.TryCreate(m.Frontend.RemoteUrl, UriKind.Absolute, out var remoteUri)
             || (remoteUri.Scheme != "https" && !(isDevelopment && remoteUri.Scheme == "http"))))
            yield return new("frontend.remoteUrl", "Must be empty (served by the Module Host) or absolute https (http allowed in Development only).");
    }
}
