import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { PackageOpen, Plus } from 'lucide-react';
import { api } from '../lib/api-client';
import { platformRole } from '../lib/auth';
import { fetchPlatformCatalog, fetchCatalog, fetchRegistry, type RegistryModule } from '../lib/bootstrap';
import { ModuleCard } from '../components/ModuleCard';
import { RegistryModuleCard } from '../components/RegistryModuleCard';
import { InstallToTenantsDialog } from '../components/InstallToTenantsDialog';
import { RegisterModuleDialog } from '../components/RegisterModuleDialog';
import { PageHeader } from '@/components/page-header';
import { EmptyState } from '@/components/empty-state';
import { SearchInput } from '@/components/search-input';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

const ALL_MODULES = '__all__'; // Radix Select can't use "" as an item value

const matches = (m: { id: string; displayName: string; owner: string }, search: string) => {
  const q = search.trim().toLowerCase();
  if (!q) return true;
  return m.displayName.toLowerCase().includes(q) || m.id.toLowerCase().includes(q) || m.owner.toLowerCase().includes(q);
};

// Modules / Catalog (§8.2). Platform session: "All modules" shows the registry
// with multi-tenant install; picking a tenant shows only its installed modules
// with lifecycle actions. Tenant session → tenant endpoints (legacy).
export function CatalogPage() {
  const queryClient = useQueryClient();
  const tenantId = sessionStorage.getItem('tenant_id');
  const isAdmin = platformRole() === 'Admin';
  const canManage = tenantId ? true : isAdmin;
  const [platformTid, setPlatformTid] = useState(() => sessionStorage.getItem('catalog_tenant') ?? '');
  const [search, setSearch] = useState('');
  const [installing, setInstalling] = useState<RegistryModule | null>(null);
  const [registerOpen, setRegisterOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const allView = !tenantId && !platformTid;

  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ['catalog'] });
    queryClient.invalidateQueries({ queryKey: ['registry'] });
    queryClient.invalidateQueries({ queryKey: ['module-installs'] });
  };

  const registry = useQuery({
    queryKey: ['registry'],
    queryFn: fetchRegistry,
    enabled: allView,
    retry: false,
  });
  const catalog = useQuery({
    queryKey: ['catalog', tenantId ?? platformTid],
    queryFn: () => (tenantId ? fetchCatalog() : fetchPlatformCatalog(platformTid)),
    enabled: !allView,
    retry: false,
  });
  const active = allView ? registry : catalog;

  const pickTenant = (id: string) => {
    sessionStorage.setItem('catalog_tenant', id);
    setPlatformTid(id);
    queryClient.invalidateQueries({ queryKey: ['catalog'] });
  };

  const registryItems = (registry.data ?? []).filter((m) => matches(m, search));
  const catalogItems = (catalog.data ?? []).filter((m) => matches(m, search));
  const total = allView ? (registry.data ?? []).length : (catalog.data ?? []).length;
  const shown = allView ? registryItems.length : catalogItems.length;

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Catalog' }]}
        title="Module Catalog"
        description={
          tenantId
            ? 'Browse available modules and manage their lifecycle for your tenant.'
            : allView
              ? 'All registered modules. Install a module into one or more tenants.'
              : "Modules installed for the selected tenant. Manage their lifecycle here."
        }
        actions={
          !tenantId && (
            <div className="flex items-center gap-2">
              <Label htmlFor="catalog-tenant" className="whitespace-nowrap">Tenant</Label>
              <TenantSelect value={platformTid} onChange={pickTenant} />
              {isAdmin && (
                <Button onClick={() => setRegisterOpen(true)}>
                  <Plus className="h-4 w-4" aria-hidden />
                  Register module
                </Button>
              )}
            </div>
          )
        }
      />

      {notice && (
        <Alert variant="success" className="mb-4">
          <AlertDescription>
            {notice}{' '}
            <button className="underline underline-offset-4" onClick={() => setNotice(null)}>
              Dismiss
            </button>
          </AlertDescription>
        </Alert>
      )}

      {active.isLoading ? (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3" aria-label="Loading catalog">
          {[0, 1, 2].map((i) => (
            <div key={i} className="space-y-3 rounded-lg border p-6">
              <Skeleton className="h-5 w-2/3" />
              <Skeleton className="h-4 w-full" />
              <Skeleton className="h-4 w-1/3" />
            </div>
          ))}
        </div>
      ) : active.isError ? (
        <Alert variant="destructive">
          <AlertTitle>Failed to load catalog</AlertTitle>
          <AlertDescription>
            Check your session and try again.{' '}
            <button className="underline underline-offset-4" onClick={() => active.refetch()}>
              Retry
            </button>
          </AlertDescription>
        </Alert>
      ) : total === 0 ? (
        allView ? (
          <EmptyState
            icon={PackageOpen}
            title="No modules registered"
            description={isAdmin ? 'Register a module from its manifest.yml to get started.' : 'No modules have been registered yet.'}
            actionLabel={isAdmin ? 'Register module' : undefined}
            onAction={isAdmin ? () => setRegisterOpen(true) : undefined}
          />
        ) : tenantId ? (
          <EmptyState
            icon={PackageOpen}
            title="No modules published"
            description="Publish a module version from the registry to get started."
          />
        ) : (
          <EmptyState
            icon={PackageOpen}
            title="No modules installed for this tenant"
            description="Switch to All modules to install one."
            actionLabel="Show all modules"
            onAction={() => pickTenant('')}
          />
        )
      ) : (
        <>
          <div className="mb-4">
            <SearchInput value={search} onChange={setSearch} placeholder="Search modules…" className="max-w-sm" />
          </div>
          {shown === 0 ? (
            <EmptyState icon={PackageOpen} title="No modules match your search" description="Try a different search term." />
          ) : allView ? (
            <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
              {registryItems.map((m) => (
                <RegistryModuleCard key={m.id} module={m} canManage={isAdmin} onInstall={() => setInstalling(m)} />
              ))}
            </div>
          ) : (
            <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
              {catalogItems.map((m) => (
                <ModuleCard
                  key={m.id}
                  module={m}
                  tenantId={tenantId}
                  versions={m.versions}
                  readOnly={!canManage}
                  onChanged={refresh}
                />
              ))}
            </div>
          )}
        </>
      )}

      {installing && (
        <InstallToTenantsDialog
          module={installing}
          open
          onOpenChange={(v) => { if (!v) setInstalling(null); }}
          onDone={refresh}
        />
      )}
      {isAdmin && (
        <RegisterModuleDialog
          open={registerOpen}
          onOpenChange={setRegisterOpen}
          onRegistered={(r) => {
            setRegisterOpen(false);
            setNotice(`Registered ${r.id} v${r.version}.`);
            refresh();
          }}
        />
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
    <Select value={value || ALL_MODULES} onValueChange={(v) => onChange(v === ALL_MODULES ? '' : v)}>
      <SelectTrigger id="catalog-tenant" className="w-52" aria-label="Tenant">
        <SelectValue placeholder="Select tenant…" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={ALL_MODULES}>All modules</SelectItem>
        {(tenants.data ?? []).map((t) => (
          <SelectItem key={t.id} value={t.id}>
            {t.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
