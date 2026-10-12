import { useEffect, useState } from 'react';
import { fetchBootstrap, type BootstrapModule } from '../lib/bootstrap';

// Loads federation remoteEntry.js scripts for enabled modules only.
async function loadRemote(scope: string, remoteUrl: string): Promise<void> {
  // @ts-expect-error federation container cache
  if (window[scope]) return;
  await new Promise<void>((resolve, reject) => {
    const script = document.createElement('script');
    script.src = remoteUrl;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error(`Failed to load remote ${scope} from ${remoteUrl}`));
    document.head.appendChild(script);
  });
}

export function useBootstrap() {
  const [modules, setModules] = useState<BootstrapModule[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    // Bootstrap needs a tenant JWT (R10). Platform sessions carry no tenant
    // context, so there is nothing to bootstrap — skip instead of 403ing.
    if (!sessionStorage.getItem('access_token') || !sessionStorage.getItem('tenant_id')) {
      setLoading(false);
      return;
    }
    setLoading(true);
    fetchBootstrap()
      .then(async (bootstrap) => {
        for (const m of bootstrap.modules) {
          try {
            await loadRemote(m.scope, m.remoteUrl);
          } catch (e) {
            // Remote failure is isolated: keep the module out of nav, rest of SPA works.
            console.error(e);
          }
        }
        setModules(bootstrap.modules);
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, [attempt]);

  return { modules, loading, error, retry: () => { setError(null); setAttempt((a) => a + 1); } };
}
