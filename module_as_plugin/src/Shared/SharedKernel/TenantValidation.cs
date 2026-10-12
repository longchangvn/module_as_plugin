// Tenant validation (creation + update) per §4.1 / V-C1..V-C5.
// Pure logic (no EF) so it is unit-testable and shared by API + tests.
// Uniqueness (BR-T1) is a DB check done by the caller; this covers shape.
using System.Text.RegularExpressions;

namespace SharedKernel;

public sealed record TenantFieldError(string Field, string Message);

public static class TenantValidator
{
    public static readonly string[] ReservedSlugs =
        ["admin", "api", "t", "platform", "health", "metrics", "docs", "login", "static"];

    public static readonly string[] ResolutionModes = ["domain", "path", "header"];

    public static readonly string[] DeploymentModes = ["dedicated", "shared"];

    private static readonly Regex SlugRegex = new("^[a-z0-9-]+$", RegexOptions.Compiled);
    private static readonly Regex PathPrefixRegex = new(@"^/t/[a-z0-9-]+$", RegexOptions.Compiled);
    private static readonly Regex DbNameRegex = new("^[a-z_][a-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex SchemaRegex = new("^[a-z_][a-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex HostLabelRegex = new(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.Compiled);

    public static List<TenantFieldError> ValidateCreate(
        string? name, string? slug, string? resolutionMode,
        string? domain, string? pathPrefix, string? contactEmail,
        string? deploymentMode = null, string? dbName = null, string? schema = null,
        string? bootstrapAdminEmail = null) =>
        Validate(name, slug, resolutionMode, domain, pathPrefix, contactEmail, deploymentMode, dbName, schema, bootstrapAdminEmail, isUpdate: false);

    public static List<TenantFieldError> ValidateUpdate(
        string? name, string? resolutionMode,
        string? domain, string? pathPrefix, string? contactEmail) =>
        Validate(name, null, resolutionMode, domain, pathPrefix, contactEmail, null, null, null, null, isUpdate: true);

    private static List<TenantFieldError> Validate(
        string? name, string? slug, string? resolutionMode,
        string? domain, string? pathPrefix, string? contactEmail,
        string? deploymentMode, string? dbName, string? schema, string? bootstrapAdminEmail, bool isUpdate)
    {
        var errors = new List<TenantFieldError>();

        // V-C1 name 3–100 (required on create; if supplied on update).
        if (!isUpdate || name is not null)
        {
            if (string.IsNullOrWhiteSpace(name) || name!.Trim().Length < 3 || name.Trim().Length > 100)
                errors.Add(new TenantFieldError("name", "Name must be 3–100 characters."));
        }

        // V-C2 slug 3–50 + charset + reserved (create only; immutable after).
        if (!isUpdate)
        {
            if (string.IsNullOrWhiteSpace(slug) || slug!.Trim().Length < 3 || slug.Trim().Length > 50)
                errors.Add(new TenantFieldError("slug", "Slug must be 3–50 characters."));
            else if (!SlugRegex.IsMatch(slug.Trim()))
                errors.Add(new TenantFieldError("slug", "Slug may contain only lowercase letters, numbers, and hyphens."));
            else if (ReservedSlugs.Contains(slug.Trim()))
                errors.Add(new TenantFieldError("slug", $"Slug '{slug.Trim()}' is reserved."));
        }

        // resolutionMode must be known when supplied.
        if (resolutionMode is not null && !ResolutionModes.Contains(resolutionMode))
            errors.Add(new TenantFieldError("resolutionMode", "Must be one of: domain, path, header."));

        // V-C3 domain: hostname ≤253, lowercase, no scheme/path/port.
        if (!string.IsNullOrWhiteSpace(domain))
        {
            var d = domain.Trim();
            if (d.Length > 253 || d != d.ToLowerInvariant() || d.Contains("://") || d.Contains('/') || d.Contains(':') || d.Contains(' '))
                errors.Add(new TenantFieldError("domain", "Domain must be a lowercase hostname without scheme, path, or port."));
            else if (!d.Split('.').All(l => HostLabelRegex.IsMatch(l)))
                errors.Add(new TenantFieldError("domain", "Domain contains an invalid hostname label."));
        }
        else if (resolutionMode == "domain")
        {
            errors.Add(new TenantFieldError("domain", "Domain is required when resolutionMode is 'domain'."));
        }

        // V-C4 pathPrefix /t/{slug} form when supplied.
        if (!string.IsNullOrWhiteSpace(pathPrefix) && !PathPrefixRegex.IsMatch(pathPrefix.Trim()))
            errors.Add(new TenantFieldError("pathPrefix", "Path prefix must have the form /t/{slug}."));

        // V-C5 contact email.
        if (!isUpdate || contactEmail is not null)
        {
            if (string.IsNullOrWhiteSpace(contactEmail) || !EmailRegex.IsMatch(contactEmail.Trim()))
                errors.Add(new TenantFieldError("contactEmail", "Contact email must be a valid email address."));
        }

        // Bootstrap admin email (optional; seeded as first tenant user on provision).
        if (bootstrapAdminEmail is not null && !string.IsNullOrWhiteSpace(bootstrapAdminEmail) &&
            !EmailRegex.IsMatch(bootstrapAdminEmail.Trim()))
            errors.Add(new TenantFieldError("bootstrapAdminEmail", "Bootstrap admin email must be a valid email address."));

        // V-C6/V-C7 provisioning shape (create only; immutable after).
        var mode = (deploymentMode ?? "dedicated").Trim().ToLowerInvariant();
        if (!isUpdate)
        {
            if (!DeploymentModes.Contains(mode))
                errors.Add(new TenantFieldError("deploymentMode", "Must be one of: dedicated, shared."));
            if (!string.IsNullOrWhiteSpace(dbName))
            {
                var d = dbName.Trim();
                if (d.Length > 63 || !DbNameRegex.IsMatch(d))
                    errors.Add(new TenantFieldError("dbName", "Database name must match ^[a-z_][a-z0-9_]*$ and be ≤63 chars."));
            }
            if (mode == "shared")
            {
                if (string.IsNullOrWhiteSpace(schema))
                    errors.Add(new TenantFieldError("schema", "Schema is required for shared deployment."));
                else
                {
                    var s = schema.Trim();
                    if (s.Length > 63 || !SchemaRegex.IsMatch(s) || s == "public" || s.StartsWith("pg_") || s == "information_schema")
                        errors.Add(new TenantFieldError("schema", "Schema must match ^[a-z_][a-z0-9_]*$, ≤63 chars, not public/pg_*/information_schema."));
                }
            }
        }

        return errors;
    }

    public static string DefaultPathPrefix(string slug) => $"/t/{slug.Trim().ToLowerInvariant()}";
}
