import { useCallback, useEffect, useState } from 'react';
import { fetchBootstrap, type BootstrapModule } from '@/lib/bootstrap';

// Installed modules for the signed-in tenant. Remotes load lazily per route, never here.
export function useBootstrap() {
  const [modules, setModules] = useState<BootstrapModule[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!sessionStorage.getItem('access_token') || !sessionStorage.getItem('tenant_id')) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    fetchBootstrap()
      .then((b) => {
        if (!cancelled) setModules(b.modules ?? []);
      })
      .catch((e: Error) => {
        if (cancelled) return;
        setError(e);
        setModules([]);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [attempt]);

  const retry = useCallback(() => setAttempt((n) => n + 1), []);

  return { modules, loading, error, retry };
}
