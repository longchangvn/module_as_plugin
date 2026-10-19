using Microsoft.AspNetCore.DataProtection;

namespace Platform.Api.Services;

// Encrypts per-tenant database connection strings at rest (catalog Tenants
// table). Keys come from the DataProtection ring persisted via
// DataProtection:KeyDirectory — without persistence a restart orphans every
// stored secret. Decrypt failures are fail-safe (null), never fatal.
public sealed class TenantConnectionProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("TenantDbConnection:v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string? TryUnprotect(string? payload)
    {
        if (string.IsNullOrEmpty(payload)) return null;
        try
        {
            return _protector.Unprotect(payload);
        }
        catch
        {
            return null;
        }
    }
}
