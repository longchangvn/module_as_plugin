import { useEffect, useState } from 'react';
import { call } from './api';
import './page.css';

const TITLE = '__DISPLAY_NAME__';
const VERSION = '1.0.0';
const API = '/api/v1/__MODULE_NAME__';
const PROTECTED_PERMISSION = '__PERM___DATA_PROTECTED';

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
  if (result.status === 'loading') return <p className="__CSS__-muted">Loading…</p>;
  if (result.status === 'error') return <p role="alert" className="__CSS__-error">{result.message}</p>;
  if (result.status === 'success') return <pre className="__CSS__-json">{JSON.stringify(result.data, null, 2)}</pre>;
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
    <div className="__CSS__-page">
      <header className="__CSS__-header">
        <h1>{TITLE}</h1>
        <span className="__CSS__-badge">v{VERSION}</span>
      </header>

      <section className="__CSS__-card">
        <h2>Sample data</h2>
        <p className="__CSS__-muted">Available to every signed-in user of this tenant.</p>
        <button className="__CSS__-button" onClick={sample.run} disabled={sample.result.status === 'loading'}>
          Get sample data
        </button>
        <Output result={sample.result} />
      </section>

      <section className="__CSS__-card">
        <h2>Protected data</h2>
        <p className="__CSS__-muted">Requires the {PROTECTED_PERMISSION} permission.</p>
        <button
          className="__CSS__-button"
          onClick={secured.run}
          disabled={permission !== 'granted' || secured.result.status === 'loading'}
          title={hint ?? undefined}
        >
          Get protected data
        </button>
        {hint && <small className="__CSS__-hint">{hint}</small>}
        <Output result={secured.result} />
      </section>
    </div>
  );
}
