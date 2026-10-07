using System.Text.RegularExpressions;
using Semver;

namespace SharedKernel;

// Manifest validation — §10.3 validation rules. Pure function, fully unit-tested.
public static partial class ManifestValidator
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "api", "platform", "auth", "gateway", "t",
        "health", "metrics", "docs", "login", "static"
    };

    private static readonly HashSet<string> ValidMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"
    };

    [GeneratedRegex("^[a-z0-9-]{3,50}$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^[A-Z]+_[A-Z_]+$")]
    private static partial Regex PermissionCodeRegex();

    public sealed record ValidationFailure(string Field, string Message);

    public static IReadOnlyList<ValidationFailure> Validate(ManifestFile m, ISet<string>? usedScopes = null, bool isDevelopment = false)
    {
        var errors = new List<ValidationFailure>();
        void Add(string field, string message) => errors.Add(new ValidationFailure(field, message));

        // module.name
        if (string.IsNullOrWhiteSpace(m.Module.Name) || !SlugRegex().IsMatch(m.Module.Name))
            Add("module.name", "Must match ^[a-z0-9-]{3,50}$.");
        else if (ReservedNames.Contains(m.Module.Name))
            Add("module.name", $"Reserved name '{m.Module.Name}'.");

        if (string.IsNullOrWhiteSpace(m.Module.DisplayName) || m.Module.DisplayName.Length is < 3 or > 100)
            Add("module.displayName", "Must be 3-100 chars.");

        if (!SemVersion.TryParse(m.Module.Version, SemVersionStyles.Strict, out _))
            Add("module.version", "Must be valid semver.");

        if (string.IsNullOrWhiteSpace(m.Module.Owner))
            Add("module.owner", "Required.");

        // dependencies
        var depNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in m.Dependencies)
        {
            if (!depNames.Add(d.Name))
                Add($"dependencies[{d.Name}]", "Duplicate dependency.");
            if (string.Equals(d.Name, m.Module.Name, StringComparison.OrdinalIgnoreCase))
                Add($"dependencies[{d.Name}]", "Self-dependency is forbidden.");
            try { SemVersionRange.Parse(d.VersionRange); }
            catch { Add($"dependencies[{d.Name}].versionRange", "Invalid semver range."); }
        }

        // frontend
        if (string.IsNullOrWhiteSpace(m.Frontend.Scope))
            Add("frontend.scope", "Required, globally unique.");
        else if (usedScopes is not null && usedScopes.Contains(m.Frontend.Scope))
            Add("frontend.scope", $"Scope '{m.Frontend.Scope}' already registered.");

        if (!Uri.TryCreate(m.Frontend.RemoteUrl, UriKind.Absolute, out var remoteUri)
            || (remoteUri.Scheme != "https" && !(isDevelopment && remoteUri.Scheme == "http")))
            Add("frontend.remoteUrl", "Must be absolute https (http allowed in Development only).");

        if (string.IsNullOrWhiteSpace(m.Frontend.ModulePrefix) || !m.Frontend.ModulePrefix.StartsWith('/'))
            Add("frontend.modulePrefix", "Must start with '/'.");

        // seed permissions index
        var permCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in m.Api.Seed.Permissions)
        {
            if (!permCodes.Add(p.Code))
                Add($"api.seed.permissions[{p.Code}]", "Duplicate permission code.");
            else if (!PermissionCodeRegex().IsMatch(p.Code))
                Add($"api.seed.permissions[{p.Code}]", "Must match ^[A-Z]+_[A-Z_]+$.");
        }

        void RequirePerm(string field, string? code)
        {
            if (code is not null && !permCodes.Contains(code))
                Add(field, $"Unknown requiredPermission '{code}'. Must exist in api.seed.permissions[].");
        }

        // frontend routes
        foreach (var r in m.Frontend.Routes)
        {
            if (!r.Path.StartsWith(m.Frontend.ModulePrefix))
                Add($"frontend.routes[{r.Path}]", $"Must start with modulePrefix '{m.Frontend.ModulePrefix}'.");
            RequirePerm($"frontend.routes[{r.Path}].requiredPermission", r.RequiredPermission);
        }

        // menus: unique id + parent exists (R5)
        var menuIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var menu in m.Frontend.Menus.Concat(m.Api.Seed.Menus))
        {
            if (!menuIds.Add(menu.Id))
                Add($"menus[{menu.Id}]", "Duplicate menu id.");
            RequirePerm($"menus[{menu.Id}].requiredPermission", menu.RequiredPermission);
        }
        foreach (var menu in m.Frontend.Menus.Concat(m.Api.Seed.Menus))
        {
            if (menu.ParentId is not null && !menuIds.Contains(menu.ParentId))
                Add($"menus[{menu.Id}].parentId", $"Parent '{menu.ParentId}' does not exist (or must be null).");
        }

        foreach (var code in m.Frontend.UiPermissions)
            RequirePerm("frontend.uiPermissions", code);

        // api routes: unique upstream, valid methods, priority range
        var upstreams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in m.Api.Routes)
        {
            if (!r.Upstream.StartsWith('/'))
                Add($"api.routes[{r.Upstream}]", "Upstream must start with '/'.");
            else if (!upstreams.Add(r.Upstream))
                Add($"api.routes[{r.Upstream}]", "Duplicate upstream template.");
            foreach (var method in r.Methods)
                if (!ValidMethods.Contains(method))
                    Add($"api.routes[{r.Upstream}].methods", $"Invalid method '{method}'.");
            if (r.Priority is < 0 or > 999)
                Add($"api.routes[{r.Upstream}].priority", "Must be 0..999.");
            RequirePerm($"api.routes[{r.Upstream}].requiredPermission", r.RequiredPermission);
        }

        return errors;
    }
}

// Ordering helper: Semver 3.x SemVersion has no operators/IComparable;
// precedence comparison goes through ComparePrecedenceTo.
public static class SemverOrdering
{
    public static int Compare(SemVersion a, SemVersion b) => a.ComparePrecedenceTo(b);

    public static readonly IComparer<SemVersion> Comparer =
        System.Collections.Generic.Comparer<SemVersion>.Create(Compare);

    public static string Latest(IEnumerable<string> versions) =>
        versions.OrderByDescending(v => SemVersion.Parse(v, SemVersionStyles.Strict), Comparer)
            .First().ToString();
}

// Dependency resolver — P-BR6 / R2 / R3: topo-sort, cycle detection, range conflicts.
public static class DependencyResolver
{
    public sealed record PlanStep(string Name, string Version);

// available: module -> sorted ascending versions available in registry.
    public static Result<IReadOnlyList<PlanStep>> Resolve(
        string rootModule,
        string rootRange,
        Func<string, IReadOnlyList<string>> availableVersions,
        Func<string, string, IReadOnlyList<ManifestDependency>> getDependencies)
    {
        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<PlanStep>();

        Result<Unit> Visit(string name, string range)
        {
            if (selected.TryGetValue(name, out var existing))
            {
                // already selected: verify it still satisfies the new range
                if (!Satisfies(existing, range))
                    return Result<Unit>.Fail(ErrorCodes.VersionConflict,
                        $"Version conflict on '{name}': selected {existing} does not satisfy '{range}'.");
                return Result<Unit>.Success(Unit.Value);
            }
            if (!visiting.Add(name))
                return Result<Unit>.Fail(ErrorCodes.ValidationError,
                    $"Circular dependency detected at '{name}'.", retryable: false);

            var candidates = availableVersions(name)
                .Where(v => Satisfies(v, range))
                .OrderByDescending(v => SemVersion.Parse(v, SemVersionStyles.Strict), SemverOrdering.Comparer)
                .ToList();
            if (candidates.Count == 0)
                return Result<Unit>.Fail(ErrorCodes.DependencyMissing,
                    $"No registry version of '{name}' satisfies '{range}'.",
                    new { chain = new[] { name } });

            var picked = candidates[0];
            selected[name] = picked;
            foreach (var dep in getDependencies(name, picked))
            {
                var r = Visit(dep.Name, dep.VersionRange);
                if (!r.Ok) return r;
            }
            visiting.Remove(name);
            order.Add(new PlanStep(name, picked));
            return Result<Unit>.Success(Unit.Value);
        }

        var root = Visit(rootModule, rootRange);
        if (!root.Ok) return Result<IReadOnlyList<PlanStep>>.Fail(root.Error!.Code, root.Error.Message, root.Error.Details);
        return Result<IReadOnlyList<PlanStep>>.Success(order);
    }

    public static bool Satisfies(string version, string range)
    {
        try
        {
            return SemVersionRange.Parse(range).Contains(SemVersion.Parse(version, SemVersionStyles.Strict));
        }
        catch { return false; }
    }

    public sealed class Unit
    {
        public static readonly Unit Value = new();
    }
}

// Permission union — A-BR3: role permissions ∪ user additional, ACTIVE only, deduped.
public static class PermissionService
{
    public static IReadOnlySet<string> Effective(
        IEnumerable<string> rolePermissions,
        IEnumerable<string> additionalPermissions) =>
        rolePermissions.Concat(additionalPermissions).ToHashSet(StringComparer.Ordinal);
}

// Menu tree builder — R5: parentId must exist or be null; disabled modules excluded upstream.
public static class MenuTree
{
    public sealed record MenuNode(string Id, string Title, string? Route, int Order, List<MenuNode> Children);

    public static List<MenuNode> Build(IEnumerable<ManifestMenu> menus)
    {
        var nodes = menus.ToDictionary(m => m.Id,
            m => new MenuNode(m.Id, m.Title, m.Route, m.Order, new()),
            StringComparer.Ordinal);
        var roots = new List<MenuNode>();
        foreach (var m in menus.OrderBy(x => x.Order))
        {
            if (m.ParentId is null || !nodes.TryGetValue(m.ParentId, out var parent))
            {
                if (m.ParentId is null) roots.Add(nodes[m.Id]);
                // orphan with non-null parent is rejected by validator; skipped here defensively
            }
            else
            {
                parent.Children.Add(nodes[m.Id]);
            }
        }
        return roots;
    }
}
