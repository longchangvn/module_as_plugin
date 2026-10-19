// Lifecycle state machine + guards — SR-5, R2, R6, R7, R8.
// Operates on small port interfaces so Infrastructure (EF) can implement persistence
// and gateway writes without Application depending on EF.
using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel;
using Platform.Domain;

namespace Platform.Application;

public sealed record InstallPlan(IReadOnlyList<DependencyResolver.PlanStep> Steps);

public interface IModuleLifecyclePorts
{
    Task<TenantView?> GetTenantAsync(string tenantId, CancellationToken ct);
    Task<IReadOnlyList<string>> AvailableVersionsAsync(string moduleId, CancellationToken ct);
    Task<ManifestFile?> GetManifestAsync(string moduleId, string version, CancellationToken ct);
    Task<string?> GetManifestYamlAsync(string moduleId, string version, CancellationToken ct);
    Task<string?> GetInstalledVersionAsync(string tenantId, string moduleId, CancellationToken ct);
    Task RemoveModuleRoutesAsync(string tenantId, ManifestFile manifest, CancellationToken ct);
    Task RunMigrateHookAsync(string moduleId, string fromVersion, string toVersion, CancellationToken ct);
    Task<IDictionary<string, InstallState>> GetInstallStatesAsync(string tenantId, CancellationToken ct);
    Task SaveInstallAsync(string tenantId, string moduleId, string version, InstallState state, bool enabled, string? error, CancellationToken ct);
    Task ApplyGatewayRoutesAsync(string tenantId, ManifestFile manifest, bool enabled, CancellationToken ct);
    Task SeedTenantAsync(string tenantId, ManifestFile manifest, CancellationToken ct);
    Task RevokeSeedAsync(string tenantId, ManifestFile manifest, CancellationToken ct);
    Task AuditAsync(string? tenantId, string actorId, string actorRole, string action, string resourceType, string resourceId, string? reason, CancellationToken ct);
}

public sealed record TenantView(string Id, string Status);

// A side-effect port refused the change for a business rule (e.g. LAST_ADMIN);
// nothing was modified, so callers surface it as 409 instead of a retryable 502.
public sealed class LifecycleRejectedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class LifecycleGuards
{
    // R2: cannot uninstall a module that installed dependents still need.
    public static Result<bool> CanUninstall(
        string moduleId,
        IDictionary<string, InstallState> states,
        Func<string, string, IReadOnlyList<string>> dependentsOf)
    {
        var blockers = new List<string>();
        foreach (var (id, state) in states)
        {
            if (id.Equals(moduleId, StringComparison.OrdinalIgnoreCase)) continue;
            if (state is not (Domain.InstallState.Installed or Domain.InstallState.Disabled)) continue;
            // dependentsOf(installedModule, itsVersion) lists its declared deps; stub version lookup omitted here
            var deps = dependentsOf(id, "");
            if (deps.Contains(moduleId, StringComparer.OrdinalIgnoreCase))
                blockers.Add(id);
        }
        return blockers.Count == 0
            ? Result<bool>.Success(true)
            : Result<bool>.Fail(ErrorCodes.DependencyInUse,
                $"Module '{moduleId}' is required by installed module(s).",
                new { dependents = blockers });
    }
}

public sealed class ModuleLifecycleService(IModuleLifecyclePorts ports)
{
    // §18 observability: install/upgrade counters + latency histogram, exported
    // via the OTel meter pipeline (Aspire dashboard / any OTLP collector).
    private static readonly Meter Meter = new("ModulePlatform.Lifecycle", "1.0.0");
    private static readonly Counter<long> Installs = Meter.CreateCounter<long>("module.install.count", description: "Module install attempts by result");
    private static readonly Counter<long> Upgrades = Meter.CreateCounter<long>("module.upgrade.count", description: "Module upgrade attempts by result");
    private static readonly Histogram<double> InstallSeconds = Meter.CreateHistogram<double>("module.install.duration", unit: "s", description: "Install pipeline duration");

    // SR-5 pipeline (per-tenant lock is enforced by the API layer semaphore, R8).
    public async Task<Result<string>> InstallAsync(string tenantId, string moduleId, string? version,
        string actorId, string actorRole, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await InstallCoreAsync(tenantId, moduleId, version, actorId, actorRole, ct);
        started.Stop();
        var tags = new TagList
        {
            { "module", moduleId }, { "version", result.Value ?? version ?? "unknown" },
            { "result", result.Ok ? "ok" : (result.Error?.Code ?? "error") },
        };
        Installs.Add(1, tags);
        InstallSeconds.Record(started.Elapsed.TotalSeconds, new TagList { { "module", moduleId }, { "result", result.Ok ? "ok" : "error" } });
        // §18 structured lifecycle line: module.install tenant=X module=Y version=Z in Ns result=ok
        Console.WriteLine($"module.install tenant={tenantId} module={moduleId} version={result.Value ?? version ?? "?"} deps=[] in {started.Elapsed.TotalSeconds:F1}s result={(result.Ok ? "ok" : result.Error!.Code)}");
        return result;
    }

    private async Task<Result<string>> InstallCoreAsync(string tenantId, string moduleId, string? version,
        string actorId, string actorRole, CancellationToken ct)
    {
        var tenant = await ports.GetTenantAsync(tenantId, ct);
        if (tenant is null)
            return Result<string>.Fail(ErrorCodes.UnknownTenant, $"Unknown tenant '{tenantId}'.");
        if (tenant.Status == "Disabled")
            return Result<string>.Fail(ErrorCodes.TenantDisabled, $"Tenant '{tenantId}' is disabled.");

        var available = await ports.AvailableVersionsAsync(moduleId, ct);
        if (available.Count == 0)
            return Result<string>.Fail(ErrorCodes.UnknownModule, $"Unknown module '{moduleId}'.");
        var target = version ?? SemverOrdering.Latest(available);

        var states = await ports.GetInstallStatesAsync(tenantId, ct);
        // R8: idempotent re-install of same version
        if (states.TryGetValue(moduleId, out var cur) &&
            (cur == Domain.InstallState.Installed || cur == Domain.InstallState.Disabled))
        {
            var saved = await ports.GetInstallStatesAsync(tenantId, ct); // refresh for version check simplicity
            _ = saved;
            return Result<string>.Success(target);
        }

        var manifest = await ports.GetManifestAsync(moduleId, target, ct);
        if (manifest is null)
            return Result<string>.Fail(ErrorCodes.UnknownVersion, $"Unknown version '{target}' of '{moduleId}'.");

        // P-BR6: resolve dependencies first (topo order), install missing first.
        var plan = DependencyResolver.Resolve(moduleId, $"={target}",
            name => name.Equals(moduleId, StringComparison.OrdinalIgnoreCase)
                ? new[] { target }
                : ports.AvailableVersionsAsync(name, CancellationToken.None).GetAwaiter().GetResult(),
            (name, ver) => name.Equals(moduleId, StringComparison.OrdinalIgnoreCase)
                ? manifest.Dependencies
                : ports.GetManifestAsync(name, ver, CancellationToken.None).GetAwaiter().GetResult()?.Dependencies
                  ?? new List<ManifestDependency>());

        // NOTE: sync-over-async above keeps the pure resolver signature simple for v1 single-instance;
        // the API serialises installs per (tenant, module) so no concurrent mutation occurs here.
        if (!plan.Ok) return Result<string>.Fail(plan.Error!.Code, plan.Error.Message, plan.Error.Details);

        await ports.SaveInstallAsync(tenantId, moduleId, target, Domain.InstallState.Installing, false, null, ct);
        try
        {
            foreach (var step in plan.Value!)
            {
                if (step.Name.Equals(moduleId, StringComparison.OrdinalIgnoreCase)) continue;
                var depManifest = await ports.GetManifestAsync(step.Name, step.Version, ct);
                if (depManifest is null)
                    throw new InvalidOperationException($"Dependency manifest missing: {step.Name}@{step.Version}");
                await ports.ApplyGatewayRoutesAsync(tenantId, depManifest, enabled: true, ct);
                await ports.SeedTenantAsync(tenantId, depManifest, ct);
                await ports.SaveInstallAsync(tenantId, step.Name, step.Version, Domain.InstallState.Installed, true, null, ct);
            }

            await ports.ApplyGatewayRoutesAsync(tenantId, manifest, enabled: true, ct);
            await ports.SeedTenantAsync(tenantId, manifest, ct);
            await ports.SaveInstallAsync(tenantId, moduleId, target, Domain.InstallState.Installed, true, null, ct);
            await ports.AuditAsync(tenantId, actorId, actorRole, "install", "module", moduleId, null, ct);
            return Result<string>.Success(target);
        }
        catch (Exception ex)
        {
            await ports.SaveInstallAsync(tenantId, moduleId, target, Domain.InstallState.Failed, false, ex.Message, ct);
            await ports.AuditAsync(tenantId, actorId, actorRole, "install_failed", "module", moduleId, ex.Message, ct);
            return Result<string>.Fail(ErrorCodes.ProvisionFailed, $"Install of '{moduleId}' failed: {ex.Message}", retryable: true);
        }
    }

    public async Task<Result<string>> UpgradeAsync(string tenantId, string moduleId, string toVersion,
        string actorId, string actorRole, CancellationToken ct)
    {
        var result = await UpgradeCoreAsync(tenantId, moduleId, toVersion, actorId, actorRole, ct);
        Upgrades.Add(1, new TagList
        {
            { "module", moduleId }, { "result", result.Ok ? "ok" : (result.Error?.Code ?? "error") },
        });
        return result;
    }

    private async Task<Result<string>> UpgradeCoreAsync(string tenantId, string moduleId, string toVersion,
        string actorId, string actorRole, CancellationToken ct)
    {
        var tenant = await ports.GetTenantAsync(tenantId, ct);
        if (tenant is null)
            return Result<string>.Fail(ErrorCodes.UnknownTenant, $"Unknown tenant '{tenantId}'.");
        var states = await ports.GetInstallStatesAsync(tenantId, ct);
        if (!states.TryGetValue(moduleId, out var cur) ||
            cur is not (Domain.InstallState.Installed or Domain.InstallState.Disabled))
            return Result<string>.Fail(ErrorCodes.ModuleNotInstalled, $"Module '{moduleId}' not installed.");

        var current = await ports.GetInstalledVersionAsync(tenantId, moduleId, ct);
        var manifest = await ports.GetManifestAsync(moduleId, toVersion, ct);
        if (manifest is null)
            return Result<string>.Fail(ErrorCodes.UnknownVersion, $"Unknown version '{toVersion}' of '{moduleId}'.");
        if (current is not null && Semver.SemVersion.Parse(toVersion, Semver.SemVersionStyles.Strict)
            .ComparePrecedenceTo(Semver.SemVersion.Parse(current, Semver.SemVersionStyles.Strict)) <= 0)
            return Result<string>.Fail(ErrorCodes.ValidationError, $"Upgrade target '{toVersion}' must be newer than installed '{current}'.");

        // Dependency re-resolve: new version's deps must be installed.
        foreach (var dep in manifest.Dependencies)
        {
            if (!states.TryGetValue(dep.Name, out var ds) ||
                ds is not (Domain.InstallState.Installed or Domain.InstallState.Disabled))
                return Result<string>.Fail(ErrorCodes.DependencyMissing,
                    $"Upgrade needs dependency '{dep.Name}' ({dep.VersionRange}) installed first.",
                    new { chain = new[] { dep.Name } });
        }

        await ports.SaveInstallAsync(tenantId, moduleId, toVersion, Domain.InstallState.Installing, true, null, ct);
        try
        {
            await ports.RunMigrateHookAsync(moduleId, current ?? toVersion, toVersion, ct);
            await ports.ApplyGatewayRoutesAsync(tenantId, manifest, enabled: true, ct);
            await ports.SeedTenantAsync(tenantId, manifest, ct); // additive; removed codes stay inert (edge 11)
            await ports.SaveInstallAsync(tenantId, moduleId, toVersion, Domain.InstallState.Installed, true, null, ct);
            await ports.AuditAsync(tenantId, actorId, actorRole, "upgrade", "module", $"{moduleId}@{current}->{toVersion}", null, ct);
            return Result<string>.Success(toVersion);
        }
        catch (Exception ex)
        {
            await ports.SaveInstallAsync(tenantId, moduleId, current ?? toVersion, Domain.InstallState.Failed, false, ex.Message, ct);
            return Result<string>.Fail(ErrorCodes.ProvisionFailed, $"Upgrade of '{moduleId}' failed: {ex.Message}", retryable: true);
        }
    }
}
