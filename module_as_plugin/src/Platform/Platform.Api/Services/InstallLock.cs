// Distributed per-(tenant,module) install lock — R8 across instances.
// The in-process SemaphoreSlim only serialises one gateway/platform instance;
// this takes a Postgres transaction-scoped advisory lock
// (pg_advisory_xact_lock) so two platform replicas can never run the same
// tenant+module lifecycle concurrently. The lock key is deterministic
// (SHA-256 of "tenant:module" → two int32s) so every instance derives the
// same pair. Released automatically at commit/rollback/dispose.
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Platform.Infrastructure;

namespace Platform.Api.Services;

public static class InstallLock
{
    public static async Task<IAsyncDisposable> AcquireAsync(CatalogDbContext db, string tenantId, string moduleId, CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        var (ns, key) = DeterministicKey($"{tenantId}:{moduleId.ToLowerInvariant()}");
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})", new object[] { ns, key }, ct);
        // Pass the commit/dispose as delegates so the concrete transaction type
        // (IDbContextTransaction, resolved transitively) is never named here.
        return new LockHandle(() => tx.CommitAsync(ct), () => tx.DisposeAsync());
    }

    // The advisory-lock transaction exists only to serialise the (tenant,module)
    // lifecycle across replicas — the service already records its own terminal
    // state (Installed, or Failed on error per SR-5) and audits. So we COMMIT on
    // dispose to persist whatever the service wrote and to release the
    // pg_advisory_xact_lock (which is freed at commit/rollback). Without an
    // explicit Commit, EF's DbContextTransaction rolls back on dispose and every
    // SaveChanges (install row, audit) is lost while out-of-band side effects
    // (gateway HTTP, tenant-DB seed) are not — an inconsistent half-apply.
    private sealed class LockHandle(Func<Task> commit, Func<ValueTask> dispose) : IAsyncDisposable
    {
        private bool _released;

        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;
            await commit();
            await dispose();
        }
    }

    private static (int Ns, int Key) DeterministicKey(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return (BitConverter.ToInt32(hash, 0), BitConverter.ToInt32(hash, 4));
    }
}
