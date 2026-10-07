import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api-client';
import { fetchPlatformCatalog, fetchCatalog } from '../lib/bootstrap';
import { ModuleCard } from '../components/ModuleCard';

// Modules / Catalog page (§8.2): cards with name, description, owner,
// Installed/Enabled/Available badge + Install/Enable/Disable/Uninstall/Upgrade actions.
// Tenant session → tenant endpoints; platform session → platform endpoints with tenant picker.
export function CatalogPage() {
  const queryClient = useQueryClient();
  const tenantId = sessionStorage.getItem('tenant_id');
  const platformTid = sessionStorage.getItem('catalog_tenant') ?? '';
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['catalog'] });
  const catalog = useQuery({
    queryKey: ['catalog', tenantId ?? platformTid],
    queryFn: () => (tenantId ? fetchCatalog() : fetchPlatformCatalog(platformTid)),
    enabled: Boolean(tenantId || platformTid),
    retry: false,
  });

  if (!tenantId && !platformTid)
    return (
      <div>
        <h1>Module Catalog</h1>
        <TenantPicker />
        <p>Select a tenant to browse its catalog.</p>
      </div>
    );
  if (catalog.isLoading) return <p>Loading catalog…</p>;
  if (catalog.isError) return <p role="alert">Failed to load catalog. Login first.</p>;

  return (
    <div>
      <h1>Module Catalog</h1>
      {!tenantId && <TenantPicker />}
      {(catalog.data ?? []).map((m) => (
        <ModuleCard key={m.id} module={m} tenantId={tenantId} onChanged={refresh} />
      ))}
    </div>
  );
}

function TenantPicker() {
  const queryClient = useQueryClient();
  const tenants = useQuery({
    queryKey: ['tenants'],
    queryFn: () => api<{ id: string; name: string; slug: string }[]>('/api/v1/platform/tenants'),
  });
  return (
    <label>
      Tenant
      <select
        value={sessionStorage.getItem('catalog_tenant') ?? ''}
        onChange={(e) => {
          sessionStorage.setItem('catalog_tenant', e.target.value);
          queryClient.invalidateQueries({ queryKey: ['catalog'] });
        }}
      >
        <option value="">Select tenant…</option>
        {(tenants.data ?? []).map((t) => (
          <option key={t.id} value={t.id}>{t.name}</option>
        ))}
      </select>
    </label>
  );
}
