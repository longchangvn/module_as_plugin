using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Auth.Api.Infrastructure;

// EF model cache key includes the tenant schema: shared-schema tenants each
// get their own model (HasDefaultSchema), dedicated tenants share the default.
public sealed class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is TenantDbContext t
            ? (context.GetType(), t.SchemaName ?? "")
            : context.GetType();
}
