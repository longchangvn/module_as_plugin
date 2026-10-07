import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api-client';

interface Tenant {
  id: string;
  name: string;
  slug: string;
  status: string;
  contactEmail?: string;
}

// Tenants page (P-BR3): list + create. Maintainer sessions are read-only server-side (R1)
// so the form simply fails with FORBIDDEN for them.
export function TenantsPage() {
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [error, setError] = useState<string | null>(null);

  const tenants = useQuery({ queryKey: ['tenants'], queryFn: () => api<Tenant[]>('/api/v1/platform/tenants') });
  const create = useMutation({
    mutationFn: () => api('/api/v1/platform/tenants', { method: 'POST', body: JSON.stringify({ name, slug }) }),
    onSuccess: () => {
      setName('');
      setSlug('');
      queryClient.invalidateQueries({ queryKey: ['tenants'] });
    },
    onError: (e: Error) => setError(e.message),
  });

  return (
    <div>
      <h1>Tenants</h1>
      {tenants.isLoading && <p>Loading tenants…</p>}
      {tenants.isError && <p role="alert">Failed to load tenants.</p>}
      <ul>
        {(tenants.data ?? []).map((t) => (
          <li key={t.id}>{t.name} ({t.slug}) — {t.status}</li>
        ))}
      </ul>
      <h2>Create tenant</h2>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setError(null);
          create.mutate();
        }}
      >
        <label>
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} />
        </label>
        <label>
          Slug
          <input value={slug} onChange={(e) => setSlug(e.target.value)} />
        </label>
        <button type="submit" disabled={create.isPending || !name || !slug}>Create</button>
      </form>
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
