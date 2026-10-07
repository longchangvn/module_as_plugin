import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { platformLogin, tenantLogin } from '../lib/auth';

// Login page: platform (Admin/Maintainer) or tenant (tenant users).
export function LoginPage() {
  const navigate = useNavigate();
  const [mode, setMode] = useState<'platform' | 'tenant'>('platform');
  const [tenantId, setTenantId] = useState('tnt_demo');
  const [email, setEmail] = useState('admin@local');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  async function submit(e: React.FormEvent): Promise<void> {
    e.preventDefault();
    setPending(true);
    setError(null);
    try {
      if (mode === 'platform') await platformLogin(email, password);
      else await tenantLogin(tenantId, email, password);
      navigate('/modules');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login failed');
    } finally {
      setPending(false);
    }
  }

  return (
    <div>
      <h1>Login</h1>
      <div>
        <button onClick={() => setMode('platform')} disabled={mode === 'platform'}>Platform</button>
        <button onClick={() => setMode('tenant')} disabled={mode === 'tenant'}>Tenant</button>
      </div>
      <form onSubmit={submit}>
        {mode === 'tenant' && (
          <label>
            Tenant ID
            <input value={tenantId} onChange={(e) => setTenantId(e.target.value)} />
          </label>
        )}
        <label>
          Email
          <input value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label>
          Password
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        <button type="submit" disabled={pending}>Login</button>
      </form>
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
