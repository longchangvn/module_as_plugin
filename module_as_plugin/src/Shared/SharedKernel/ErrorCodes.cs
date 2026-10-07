// Shared error codes — §12 of 01-module-platform-requirements.md
using Microsoft.AspNetCore.Http;

namespace SharedKernel;

public static class ErrorCodes
{
    public const string UnknownTenant = "UNKNOWN_TENANT";
    public const string TenantDisabled = "TENANT_DISABLED";
    public const string TenantNotReady = "TENANT_NOT_READY";
    public const string UnknownModule = "UNKNOWN_MODULE";
    public const string UnknownVersion = "UNKNOWN_VERSION";
    public const string ModuleAlreadyInstalled = "MODULE_ALREADY_INSTALLED";
    public const string ModuleNotInstalled = "MODULE_NOT_INSTALLED";
    public const string ModuleDisabled = "MODULE_DISABLED";
    public const string ModuleNotReady = "MODULE_NOT_READY";
    public const string DependencyMissing = "DEPENDENCY_MISSING";
    public const string DependencyInUse = "DEPENDENCY_IN_USE";
    public const string VersionConflict = "VERSION_CONFLICT";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string ImmutableField = "IMMUTABLE_FIELD";
    public const string Forbidden = "FORBIDDEN";
    public const string ForbiddenTenant = "FORBIDDEN_TENANT";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string ProvisionFailed = "PROVISION_FAILED";
    public const string GatewayApplyFailed = "GATEWAY_APPLY_FAILED";
    public const string AuthzUnavailable = "AUTHZ_UNAVAILABLE";
    public const string UpgradePathUnsupported = "UPGRADE_PATH_UNSUPPORTED";
    public const string LastAdmin = "LAST_ADMIN";
}

public sealed record ErrorEnvelope(string Code, string Message, object? Details = null, bool Retryable = false);

public static class ApiErrors
{
    public static IResult Problem(string code, string message, int status, object? details = null, bool retryable = false) =>
        Results.Json(new { error = new { code, message, details, retryable } }, statusCode: status);
}
