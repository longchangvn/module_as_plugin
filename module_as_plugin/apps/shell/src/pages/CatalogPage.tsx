import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { PackageOpen } from 'lucide-react';
import { api } from '../lib/api-client';
import { fetchPlatformCatalog, fetchCatalog } from '../lib/bootstrap';
import { ModuleCard } from '../components/ModuleCard';
import { PageHeader } from '@/components/page-header';
import { EmptyState } from '@/components/empty-state';
import { SearchInput } from '@/components/search-input';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

// Modules / Catalog (§8.2): searchable grid of module cards with status
// badges + lifecycle actions. Tenant session → tenant endpoints; platform
// session → platform endpoints with a tenant picker.
export function CatalogPage() {
  const queryClient = useQueryClient();
  const tenantId = sessionStorage.getItem('tenant_id');
  const [platformTid, setPlatformTid] = useState(() => sessionStorage.getItem('catalog_tenant') ?? '');
  const [search, setSearch] = useState('');
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['catalog'] });
  const catalog = useQuery({
    queryKey: ['catalog', tenantId ?? platformTid],
    queryFn: () => (tenantId ? fetchCatalog() : fetchPlatformCatalog(platformTid)),
    enabled: Boolean(tenantId || platformTid),
    retry: false,
  });

  const pickTenant = (id: string) => {
    sessionStorage.setItem('catalog_tenant', id);
    setPlatformTid(id);
    queryClient.invalidateQueries({ queryKey: ['catalog'] });
  };

  const items = (catalog.data ?? []).filter((m) => {
    const q = search.trim().toLowerCase();
    if (!q) return true;
    return m.displayName.toLowerCase().includes(q) || m.id.toLowerCase().includes(q) || m.owner.toLowerCase().includes(q);
  });

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Catalog' }]}
        title="Module Catalog"
        description={
          tenantId
            ? 'Browse available modules and manage their lifecycle for your tenant.'
            : 'Pick a tenant to browse its catalog and manage module lifecycles.'
        }
        actions={
          !tenantId && (
            <div className="flex items-center gap-2">
              <Label htmlFor="catalog-tenant" className="whitespace-nowrap">Tenant</Label>
              <TenantSelect value={platformTid} onChange={pickTenant} />
            </div>
          )
        }
      />

      {!tenantId && !platformTid ? (
        <EmptyState
          icon={PackageOpen}
          title="No tenant selected"
          description="Select a tenant above to browse its module catalog."
        />
      ) : catalog.isLoading ? (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3" aria-label="Loading catalog">
          {[0, 1, 2].map((i) => (
            <div key={i} className="space-y-3 rounded-lg border p-6">
              <Skeleton className="h-5 w-2/3" />
              <Skeleton className="h-4 w-full" />
              <Skeleton className="h-4 w-1/3" />
            </div>
          ))}
        </div>
      ) : catalog.isError ? (
        <Alert variant="destructive">
          <AlertTitle>Failed to load catalog</AlertTitle>
          <AlertDescription>
            Check your session and try again.{' '}
            <button className="underline underline-offset-4" onClick={() => catalog.refetch()}>
              Retry
            </button>
          </AlertDescription>
        </Alert>
      ) : items.length === 0 ? (
        <EmptyState
          icon={PackageOpen}
          title={search ? 'No modules match your search' : 'No modules published'}
          description={
            search
              ? 'Try a different search term.'
              : 'Publish a module version from the registry to get started.'
          }
        />
      ) : (
        <>
          <div className="mb-4">
            <SearchInput value={search} onChange={setSearch} placeholder="Search modules…" className="max-w-sm" />
          </div>
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {items.map((m) => (
              <ModuleCard key={m.id} module={m} tenantId={tenantId} onChanged={refresh} />
            ))}
          </div>
        </>
      )}
    </div>
  );
}

function TenantSelect({ value, onChange }: { value: string; onChange: (id: string) => void }) {
  const tenants = useQuery({
    queryKey: ['tenants'],
    queryFn: () => api<{ id: string; name: string; slug: string }[]>('/api/v1/platform/tenants'),
  });
  return (
    <Select value={value || undefined} onValueChange={onChange}>
      <SelectTrigger id="catalog-tenant" className="w-52" aria-label="Tenant">
        <SelectValue placeholder="Select tenant…" />
      </SelectTrigger>
      <SelectContent>
        {(tenants.data ?? []).map((t) => (
          <SelectItem key={t.id} value={t.id}>
            {t.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
