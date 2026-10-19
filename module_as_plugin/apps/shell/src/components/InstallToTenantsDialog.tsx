import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api-client';
import { fetchModuleInstalls, installToTenant, type ModuleInstall, type RegistryModule } from '../lib/bootstrap';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

interface TenantRow {
  id: string;
  name: string;
  slug: string;
  status: string | number; // TenantStatus enum as a number
}

type RunResult = { status: 'pending' | 'running' | 'ok' | 'error'; message?: string };

// TenantStatus enum order (Platform.Domain): Active, Disabled, Provisioning, Failed.
const TENANT_STATUS_LABELS = ['Active', 'Disabled', 'Provisioning', 'Failed'];

function tenantStatus(status: string | number): string {
  if (typeof status === 'number') return TENANT_STATUS_LABELS[status] ?? String(status);
  return status;
}

function installLabel(i: ModuleInstall): string {
  switch (i.state) {
    case 'INSTALLED':
      return `Installed v${i.version}`;
    case 'DISABLED':
      return 'Disabled';
    case 'FAILED':
      return 'Failed';
    case 'INSTALLING':
      return 'Installing…';
    case 'UNINSTALLING':
      return 'Uninstalling…';
    default:
      return String(i.state);
  }
}

// Installs one registry module into many tenants, one request at a time,
// with a per-tenant result list. Stays open until the operator closes it.
export function InstallToTenantsDialog({
  module,
  open,
  onOpenChange,
  onDone,
}: {
  module: RegistryModule;
  open: boolean;
  onOpenChange: (v: boolean) => void;
  onDone: () => void;
}) {
  const [selected, setSelected] = useState<string[]>([]);
  const [version, setVersion] = useState('');
  const [results, setResults] = useState<Record<string, RunResult>>({});
  const [running, setRunning] = useState(false);
  const [finished, setFinished] = useState(false);

  const tenants = useQuery({
    queryKey: ['tenants'],
    queryFn: () => api<TenantRow[]>('/api/v1/platform/tenants'),
    enabled: open,
  });
  const installs = useQuery({
    queryKey: ['module-installs', module.id],
    queryFn: () => fetchModuleInstalls(module.id),
    enabled: open,
    retry: false,
  });

  useEffect(() => {
    if (!open) return;
    setSelected([]);
    setVersion(module.latestVersion ?? module.versions[0] ?? '');
    setResults({});
    setRunning(false);
    setFinished(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, module.id]);

  const rows = (tenants.data ?? []).map((t) => {
    const install = (installs.data ?? []).find((i) => i.tenantId === t.id);
    const status = tenantStatus(t.status);
    const active = status.toUpperCase() === 'ACTIVE';
    return { tenant: t, install, status, eligible: active && !install };
  });
  const eligibleIds = rows.filter((r) => r.eligible).map((r) => r.tenant.id);
  const nameOf = (id: string) => rows.find((r) => r.tenant.id === id)?.tenant.name ?? id;

  const toggle = (id: string, checked: boolean) =>
    setSelected((s) => (checked ? [...s, id] : s.filter((x) => x !== id)));

  const run = async () => {
    const targets = selected.filter((id) => eligibleIds.includes(id));
    if (targets.length === 0) return;
    setRunning(true);
    setFinished(false);
    setResults(Object.fromEntries(targets.map((id) => [id, { status: 'pending' } as RunResult])));
    for (const id of targets) {
      setResults((r) => ({ ...r, [id]: { status: 'running' } }));
      try {
        await installToTenant(id, module.id, version || undefined);
        setResults((r) => ({ ...r, [id]: { status: 'ok' } }));
      } catch (e) {
        const err = e as Error & { code?: string };
        setResults((r) => ({ ...r, [id]: { status: 'error', message: `${err.code ?? 'ERROR'}: ${err.message}` } }));
      }
    }
    setSelected([]);
    setRunning(false);
    setFinished(true);
    onDone();
  };

  const resultIds = Object.keys(results);
  const loading = tenants.isLoading || installs.isLoading;

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!running) onOpenChange(v); }}>
      <DialogContent className="max-w-xl">
        <DialogHeader>
          <DialogTitle>Install {module.displayName} to tenants</DialogTitle>
          <DialogDescription>
            Pick the tenants and version. Installs run one tenant at a time; dependencies must already be installed.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-1.5">
          <Label htmlFor={`install-version-${module.id}`}>Version</Label>
          <Select value={version} onValueChange={setVersion} disabled={running}>
            <SelectTrigger id={`install-version-${module.id}`} className="w-48">
              <SelectValue placeholder="Select version…" />
            </SelectTrigger>
            <SelectContent>
              {module.versions.map((v) => (
                <SelectItem key={v} value={v}>
                  {v}
                  {v === module.latestVersion ? ' (latest)' : ''}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="space-y-2">
          <div className="flex items-center justify-between">
            <Label>Tenants</Label>
            <button
              type="button"
              className="text-sm underline underline-offset-4 disabled:opacity-50"
              disabled={running || eligibleIds.length === 0}
              onClick={() => setSelected(eligibleIds)}
            >
              Select all eligible
            </button>
          </div>
          {loading ? (
            <div className="space-y-2" aria-label="Loading tenants">
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
            </div>
          ) : tenants.isError || installs.isError ? (
            <Alert variant="destructive">
              <AlertDescription>
                Failed to load tenants or installs.{' '}
                <button
                  className="underline underline-offset-4"
                  onClick={() => { tenants.refetch(); installs.refetch(); }}
                >
                  Retry
                </button>
              </AlertDescription>
            </Alert>
          ) : rows.length === 0 ? (
            <p className="text-sm text-muted-foreground">No tenants yet.</p>
          ) : (
            <ul className="max-h-64 divide-y overflow-y-auto rounded-md border">
              {rows.map(({ tenant, install, status, eligible }) => {
                const id = `install-tenant-${module.id}-${tenant.id}`;
                return (
                  <li key={tenant.id} className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
                    <label htmlFor={id} className={`flex items-center gap-2 ${eligible ? 'cursor-pointer' : 'opacity-60'}`}>
                      <input
                        id={id}
                        type="checkbox"
                        className="h-4 w-4 rounded border-input accent-primary"
                        disabled={!eligible || running}
                        checked={selected.includes(tenant.id)}
                        onChange={(e) => toggle(tenant.id, e.target.checked)}
                      />
                      <span className="font-medium">{tenant.name}</span>
                      <span className="text-muted-foreground">{tenant.slug}</span>
                    </label>
                    <span className="text-xs text-muted-foreground">
                      {install ? installLabel(install) : status.toUpperCase() !== 'ACTIVE' ? status : ''}
                    </span>
                  </li>
                );
              })}
            </ul>
          )}
        </div>

        {resultIds.length > 0 && (
          <div className="space-y-1.5">
            <Label>Results</Label>
            <ul className="space-y-1 rounded-md border p-3 text-sm" aria-live="polite">
              {resultIds.map((id) => {
                const r = results[id];
                return (
                  <li key={id} className="flex items-start justify-between gap-3">
                    <span className="font-medium">{nameOf(id)}</span>
                    <span
                      className={
                        r.status === 'ok' ? 'text-emerald-700' : r.status === 'error' ? 'text-destructive' : 'text-muted-foreground'
                      }
                    >
                      {r.status === 'ok'
                        ? '✓ Installed'
                        : r.status === 'error'
                          ? `✗ ${r.message}`
                          : r.status === 'running'
                            ? 'Installing…'
                            : 'Pending'}
                    </span>
                  </li>
                );
              })}
            </ul>
          </div>
        )}

        <DialogFooter>
          {finished && selected.length === 0 ? (
            <Button onClick={() => onOpenChange(false)}>Done</Button>
          ) : (
            <>
              <Button variant="outline" disabled={running} onClick={() => onOpenChange(false)}>
                {finished ? 'Done' : 'Cancel'}
              </Button>
              <Button disabled={running || selected.length === 0 || !version} onClick={run}>
                {running ? 'Installing…' : `Install${selected.length > 0 ? ` (${selected.length})` : ''}`}
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
