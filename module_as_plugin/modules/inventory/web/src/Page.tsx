import { useEffect, useState } from 'react';
import { call } from './api';
import './page.css';

const TITLE = 'Inventory';
const VERSION = '1.0.0';
const API = '/api/v1/inventory';
const PROTECTED_PERMISSION = 'INVENTORY_DATA_PROTECTED';

type Result = { status: 'idle' } | { status: 'loading' } | { status: 'success'; data: unknown } | { status: 'error'; message: string };

function useFetcher(path: string) {
  const [result, setResult] = useState<Result>({ status: 'idle' });
  const run = async () => {
    setResult({ status: 'loading' });
    try {
      setResult({ status: 'success', data: await call(path) });
    } catch (e) {
      setResult({ status: 'error', message: (e as Error).message });
    }
  };
  return { result, run };
}

function Output({ result }: { result: Result }) {
  if (result.status === 'loading') return <p className="mod-inventory-muted">Loading…</p>;
  if (result.status === 'error') return <p role="alert" className="mod-inventory-error">{result.message}</p>;
  if (result.status === 'success') return <pre className="mod-inventory-json">{JSON.stringify(result.data, null, 2)}</pre>;
  return null;
}

export default function Page() {
  const sample = useFetcher(`${API}/data`);
  const secured = useFetcher(`${API}/protected`);
  const [permission, setPermission] = useState<'checking' | 'granted' | 'missing' | 'failed'>('checking');

  useEffect(() => {
    call<{ permissions?: string[] }>('/api/v1/auth/me')
      .then((me) => setPermission(me.permissions?.includes(PROTECTED_PERMISSION) ? 'granted' : 'missing'))
      .catch(() => setPermission('failed'));
  }, []);

  const hint =
    permission === 'missing' ? `Requires ${PROTECTED_PERMISSION}`
      : permission === 'failed' ? 'Permission check failed'
        : permission === 'checking' ? 'Checking permission…' : null;

  return (
    <div className="mod-inventory-page">
      <header className="mod-inventory-header">
        <h1>{TITLE}</h1>
        <span className="mod-inventory-badge">v{VERSION}</span>
      </header>

      <section className="mod-inventory-card">
        <h2>Sample data</h2>
        <p className="mod-inventory-muted">Available to every signed-in user of this tenant.</p>
        <button className="mod-inventory-button" onClick={sample.run} disabled={sample.result.status === 'loading'}>
          Get sample data
        </button>
        <Output result={sample.result} />
      </section>

      <section className="mod-inventory-card">
        <h2>Protected data</h2>
        <p className="mod-inventory-muted">Requires the {PROTECTED_PERMISSION} permission.</p>
        <button
          className="mod-inventory-button"
          onClick={secured.run}
          disabled={permission !== 'granted' || secured.result.status === 'loading'}
          title={hint ?? undefined}
        >
          Get protected data
        </button>
        {hint && <small className="mod-inventory-hint">{hint}</small>}
        <Output result={secured.result} />
      </section>
    </div>
  );
}
