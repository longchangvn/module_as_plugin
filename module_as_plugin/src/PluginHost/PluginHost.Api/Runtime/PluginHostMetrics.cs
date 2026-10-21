using System.Diagnostics.Metrics;

namespace PluginHost.Api.Runtime;

// Meter "ModulePlatform.PluginHost" (exported by ServiceDefaults' OpenTelemetry when configured).
public static class PluginHostMetrics
{
    public const string MeterName = "ModulePlatform.PluginHost";
    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> LoadCount = Meter.CreateCounter<long>("plugin.load.count", description: "Plugin runtime loads by result.");
    public static readonly Histogram<double> LoadDuration = Meter.CreateHistogram<double>("plugin.load.duration", unit: "s", description: "Plugin runtime load duration.");
    public static readonly Counter<long> UnloadCount = Meter.CreateCounter<long>("plugin.unload.count", description: "Plugin runtimes released.");
}
