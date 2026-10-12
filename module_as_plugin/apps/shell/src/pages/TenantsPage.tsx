import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Building2, History, Pencil, Plus, Power, PowerOff } from 'lucide-react';
import { api } from '../lib/api-client';
import { PageHeader } from '@/components/page-header';
import { EmptyState } from '@/components/empty-state';
import { SearchInput } from '@/components/search-input';
import { StatusBadge } from '@/components/status-badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

interface Tenant {
  id: string;
  name: string;
  slug: string;
  status: string | number; // backend serializes TenantStatus enum as a number
  resolutionMode: string;
  domain?: string | null;
  pathPrefix?: string | null;
  deploymentMode: string;
  dbName: string;
  schema: string;
  dbStatus: string | number; // TenantDbStatus enum as a number
  dbError?: string | null;
  schemaVersion?: string | null;
  contactEmail?: string;
}

interface TenantAudit {
  id: string;
  action: string;
  actorId: string;
  actorRole: string;
  reason?: string | null;
  createdAt: string;
}

// TenantStatus enum order (Platform.Domain): Active, Disabled, Provisioning, Failed.
const TENANT_STATUS_LABELS = ['Active', 'Disabled', 'Provisioning', 'Failed'];

// TenantDbStatus enum order: NotStarted, Provisioning, Ready, Failed.
const DB_STATUS_LABELS = ['Not started', 'Provisioning', 'Ready', 'Failed'];

export function tenantStatusLabel(status: string | number): string {
  if (typeof status === 'number') return TENANT_STATUS_LABELS[status] ?? String(status);
  return status;
}

function dbStatusLabel(status: string | number): string {
  if (typeof status === 'number') return DB_STATUS_LABELS[status] ?? String(status);
  return status;
}

const isDisabled = (t: Tenant) => tenantStatusLabel(t.status).toUpperCase() === 'DISABLED';

async function updateTenant(id: string, body: Record<string, string | undefined>) {
  return api(`/api/v1/platform/tenants/${id}`, { method: 'PATCH', body: JSON.stringify(body) });
}

async function setTenantEnabled(id: string, enable: boolean, reason?: string) {
  return api(`/api/v1/platform/tenants/${id}/${enable ? 'enable' : 'disable'}`, {
    method: 'POST',
    body: JSON.stringify(enable ? {} : { reason }),
  });
}

function fieldErrors(e: unknown): string {
  const details = (e as { details?: { field?: string; message?: string }[] }).details;
  if (Array.isArray(details) && details.length > 0)
    return details.map((d) => `${d.field ?? 'field'}: ${d.message ?? 'invalid'}`).join(' ');
  return e instanceof Error ? e.message : 'Request failed.';
}

// Tenants (P-BR3): searchable table + create/edit dialogs + enable/disable
// with reason + audit trail. Disable blocks logins and tenant APIs (BR-R4).
export function TenantsPage() {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<Tenant | null>(null);
  const [disabling, setDisabling] = useState<Tenant | null>(null);
  const [auditing, setAuditing] = useState<Tenant | null>(null);
  const [error, setError] = useState<string | null>(null);

  const tenants = useQuery({ queryKey: ['tenants'], queryFn: () => api<Tenant[]>('/api/v1/platform/tenants') });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['tenants'] });

  const rows = (tenants.data ?? []).filter((t) => {
    const q = search.trim().toLowerCase();
    if (!q) return true;
    return t.name.toLowerCase().includes(q) || t.slug.toLowerCase().includes(q) || t.id.toLowerCase().includes(q);
  });

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Tenants' }]}
        title="Tenants"
        description="Isolated customer workspaces. Disabling a tenant blocks its logins and APIs immediately."
        actions={
          <Button onClick={() => { setError(null); setCreateOpen(true); }}>
            <Plus className="h-4 w-4" aria-hidden />
            New tenant
          </Button>
        }
      />

      <div className="mb-4">
        <SearchInput value={search} onChange={setSearch} placeholder="Search tenants…" className="max-w-sm" />
      </div>

      {tenants.isLoading ? (
        <div className="space-y-2" aria-label="Loading tenants">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </div>
      ) : tenants.isError ? (
        <Alert variant="destructive">
          <AlertTitle>Failed to load tenants</AlertTitle>
          <AlertDescription>
            Check your session and try again.{' '}
            <button className="underline underline-offset-4" onClick={() => tenants.refetch()}>
              Retry
            </button>
          </AlertDescription>
        </Alert>
      ) : rows.length === 0 ? (
        <EmptyState
          icon={Building2}
          title={search ? 'No tenants match your search' : 'No tenants yet'}
          description={search ? 'Try a different search term.' : 'Create your first tenant to get started.'}
        />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Slug</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Store</TableHead>
              <TableHead>Resolution</TableHead>
              <TableHead>Contact</TableHead>
              <TableHead className="w-36">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((t) => (
              <TableRow key={t.id}>
                <TableCell className="font-medium">{t.name}</TableCell>
                <TableCell className="text-muted-foreground">{t.slug}</TableCell>
                <TableCell>
                  <StatusBadge status={tenantStatusLabel(t.status)} />
                </TableCell>
                <TableCell>
                  <span title={t.dbStatus === 3 || dbStatusLabel(t.dbStatus) === 'Failed' ? (t.dbError ?? 'Provisioning failed') : `${t.deploymentMode === 'shared' ? `schema ${t.schema}` : `db ${t.dbName}`}`}>
                    <StatusBadge status={dbStatusLabel(t.dbStatus)} />
                  </span>
                </TableCell>
                <TableCell className="font-mono text-xs text-muted-foreground">
                  {t.resolutionMode === 'domain' ? t.domain : t.resolutionMode === 'path' ? t.pathPrefix : 'header'}
                </TableCell>
                <TableCell className="text-muted-foreground">{t.contactEmail ?? '—'}</TableCell>
                <TableCell>
                  <div className="flex items-center gap-1">
                    <Button variant="ghost" size="icon" onClick={() => { setError(null); setEditing(t); }} aria-label={`Edit ${t.name}`}>
                      <Pencil className="h-4 w-4" />
                    </Button>
                    <Button variant="ghost" size="icon" onClick={() => setAuditing(t)} aria-label={`Audit trail for ${t.name}`}>
                      <History className="h-4 w-4" />
                    </Button>
                    {isDisabled(t) ? (
                      <EnableButton tenant={t} onDone={refresh} />
                    ) : (
                      <Button variant="ghost" size="icon" onClick={() => { setError(null); setDisabling(t); }} aria-label={`Disable ${t.name}`}>
                        <PowerOff className="h-4 w-4 text-destructive" />
                      </Button>
                    )}
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      <TenantFormDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        error={error}
        setError={setError}
        onSaved={() => { setCreateOpen(false); refresh(); }}
      />
      {editing && (
        <TenantFormDialog
          open
          onOpenChange={(v) => { if (!v) setEditing(null); }}
          tenant={editing}
          error={error}
          setError={setError}
          onSaved={() => { setEditing(null); refresh(); }}
        />
      )}
      {disabling && (
        <DisableDialog
          tenant={disabling}
          onClose={() => setDisabling(null)}
          onDone={() => { setDisabling(null); refresh(); }}
        />
      )}
      {auditing && <AuditDialog tenant={auditing} onClose={() => setAuditing(null)} />}
    </div>
  );
}

function EnableButton({ tenant, onDone }: { tenant: Tenant; onDone: () => void }) {
  const [error, setError] = useState<string | null>(null);
  const enable = useMutation({
    mutationFn: () => setTenantEnabled(tenant.id, true),
    onSuccess: onDone,
    onError: (e) => setError(fieldErrors(e)),
  });
  return (
    <>
      <Button
        variant="ghost"
        size="icon"
        onClick={() => { setError(null); enable.mutate(); }}
        disabled={enable.isPending}
        aria-label={`Enable ${tenant.name}`}
        title={error ?? `Enable ${tenant.name}`}
      >
        <Power className="h-4 w-4 text-emerald-600" />
      </Button>
      {error && <span className="sr-only" role="alert">{error}</span>}
    </>
  );
}

function TenantFormDialog({ open, onOpenChange, tenant, error, setError, onSaved }: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  tenant?: Tenant;
  error: string | null;
  setError: (e: string | null) => void;
  onSaved: () => void;
}) {
  const isEdit = Boolean(tenant);
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [resolutionMode, setResolutionMode] = useState('path');
  const [deploymentMode, setDeploymentMode] = useState('dedicated');
  const [dbName, setDbName] = useState('');
  const [schema, setSchema] = useState('');
  const [domain, setDomain] = useState('');
  const [pathPrefix, setPathPrefix] = useState('');
  const [pathTouched, setPathTouched] = useState(false);
  const [contactEmail, setContactEmail] = useState('');
  const [bootstrapAdminEmail, setBootstrapAdminEmail] = useState('');
  const [bootstrapAdminName, setBootstrapAdminName] = useState('');

  // Reset fields (and stale errors) every time the dialog opens.
  useEffect(() => {
    if (!open) return;
    setName(tenant?.name ?? '');
    setSlug(tenant?.slug ?? '');
    setResolutionMode(tenant?.resolutionMode ?? 'path');
    setDeploymentMode(tenant?.deploymentMode ?? 'dedicated');
    setDbName('');
    setSchema(tenant?.schema && tenant.schema !== 'public' ? tenant.schema : '');
    setDomain(tenant?.domain ?? '');
    setPathPrefix(tenant?.pathPrefix ?? '');
    setPathTouched(false);
    setContactEmail(tenant?.contactEmail ?? '');
    setBootstrapAdminEmail('');
    setBootstrapAdminName('');
    setError(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const save = useMutation({
    mutationFn: () => {
      const base: Record<string, string | undefined> = {
        name: name.trim(),
        contactEmail: contactEmail.trim(),
        resolutionMode,
        domain: domain.trim() || undefined,
        pathPrefix: pathPrefix.trim() || undefined,
      };
      if (!isEdit) {
        base.slug = slug.trim().toLowerCase();
        base.deploymentMode = deploymentMode;
        base.dbName = dbName.trim() || undefined;
        if (deploymentMode === 'shared') base.schema = schema.trim();
        if (bootstrapAdminEmail.trim()) {
          base.bootstrapAdminEmail = bootstrapAdminEmail.trim();
          if (bootstrapAdminName.trim()) base.bootstrapAdminName = bootstrapAdminName.trim();
        }
      }
      return tenant ? updateTenant(tenant.id, base) : api('/api/v1/platform/tenants', { method: 'POST', body: JSON.stringify(base) });
    },
    onSuccess: onSaved,
    onError: (e) => setError(fieldErrors(e)),
  });

  const slugValid = isEdit || (/^[a-z0-9-]{3,50}$/.test(slug.trim()) && !['admin', 'api', 't', 'platform', 'health', 'metrics', 'docs', 'login', 'static'].includes(slug.trim()));
  const schemaValid = deploymentMode !== 'shared' || (/^[a-z_][a-z0-9_]*$/.test(schema.trim()) && !['public', 'information_schema'].includes(schema.trim()) && !schema.trim().startsWith('pg_'));
  const bootstrapValid = !bootstrapAdminEmail.trim() || /.+@.+\..+/.test(bootstrapAdminEmail.trim());
  const canSubmit = name.trim().length >= 3 && slugValid && schemaValid && bootstrapValid && /.+@.+\..+/.test(contactEmail.trim()) && !save.isPending;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{isEdit ? `Edit ${tenant?.name}` : 'Create tenant'}</DialogTitle>
          <DialogDescription>
            {isEdit
              ? 'Name, contact, and resolution can change. Slug, database, and schema are immutable.'
              : 'A new isolated workspace with its own catalog, users, and storefront.'}
          </DialogDescription>
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            if (!canSubmit) return;
            setError(null);
            save.mutate();
          }}
        >
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="tenant-name">Name *</Label>
              <Input id="tenant-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="Acme Inc." required />
            </div>
            {!isEdit && (
              <div className="space-y-1.5">
                <Label htmlFor="tenant-slug">Slug *</Label>
                <Input
                  id="tenant-slug"
                  value={slug}
                  onChange={(e) => {
                    const next = e.target.value.toLowerCase();
                    setSlug(next);
                    // Path prefix defaults from the slug until edited by hand.
                    if (!pathTouched) setPathPrefix(next.trim() ? `/t/${next.trim()}` : '');
                  }}
                  placeholder="acme"
                  required
                />
                {slug.length > 0 && !slugValid && (
                  <p className="text-xs text-destructive" role="alert">3–50 lowercase letters, numbers, hyphens; reserved words blocked.</p>
                )}
              </div>
            )}
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="tenant-mode">Resolution</Label>
              <Select value={resolutionMode} onValueChange={setResolutionMode}>
                <SelectTrigger id="tenant-mode">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="path">URL path (/t/slug)</SelectItem>
                  <SelectItem value="domain">Domain</SelectItem>
                  <SelectItem value="header">Tenant ID header</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="tenant-email">Contact email *</Label>
              <Input id="tenant-email" type="email" value={contactEmail} onChange={(e) => setContactEmail(e.target.value)} placeholder="ops@acme.example" required />
            </div>
          </div>
          {resolutionMode === 'domain' && (
            <div className="space-y-1.5">
              <Label htmlFor="tenant-domain">Domain *</Label>
              <Input id="tenant-domain" value={domain} onChange={(e) => setDomain(e.target.value.toLowerCase())} placeholder="acme.loyalty.com" />
            </div>
          )}
          {resolutionMode === 'path' && (
            <div className="space-y-1.5">
              <Label htmlFor="tenant-path">Path prefix</Label>
              <Input
                id="tenant-path"
                value={pathPrefix}
                onChange={(e) => { setPathPrefix(e.target.value); setPathTouched(true); }}
                placeholder={`/t/${slug || 'slug'} (auto)`}
              />
              <p className="text-xs text-muted-foreground">Defaults to /t/{'{slug}'} — blank means auto. Must be unique.</p>
            </div>
          )}
          {!isEdit && (
            <>
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-1.5">
                  <Label htmlFor="tenant-deploy">Database</Label>
                  <Select value={deploymentMode} onValueChange={setDeploymentMode}>
                    <SelectTrigger id="tenant-deploy">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="dedicated">Dedicated database</SelectItem>
                      <SelectItem value="shared">Shared schema</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="tenant-dbname">Database name</Label>
                  <Input id="tenant-dbname" value={dbName} onChange={(e) => setDbName(e.target.value.toLowerCase())} placeholder={`tenant_${slug.replace(/-/g, '_') || 'slug'} (auto)`} />
                </div>
              </div>
              {deploymentMode === 'shared' && (
                <div className="space-y-1.5">
                  <Label htmlFor="tenant-schema">Schema *</Label>
                  <Input id="tenant-schema" value={schema} onChange={(e) => setSchema(e.target.value.toLowerCase())} placeholder="acme" required={deploymentMode === 'shared'} />
                  {schema.length > 0 && !schemaValid && (
                    <p className="text-xs text-destructive" role="alert">Lowercase letters, numbers, underscores; not public/pg_*/information_schema.</p>
                  )}
                </div>
              )}
              <p className="text-xs text-muted-foreground">Database, schema, and slug are immutable after creation.</p>
            </>
          )}
          {!isEdit && (
            <div className="space-y-4 rounded-md border p-4">
              <div>
                <p className="text-sm font-medium">Bootstrap admin <span className="font-normal text-muted-foreground">(optional)</span></p>
                <p className="text-xs text-muted-foreground">First user of this tenant — created in its store on first use. Log in on the Tenant tab with this email (any password in v1).</p>
              </div>
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-1.5">
                  <Label htmlFor="tenant-admin-email">Admin email</Label>
                  <Input id="tenant-admin-email" type="email" value={bootstrapAdminEmail} onChange={(e) => setBootstrapAdminEmail(e.target.value)} placeholder="admin@acme.example" />
                  {bootstrapAdminEmail.trim() && !bootstrapValid && (
                    <p className="text-xs text-destructive" role="alert">Must be a valid email address.</p>
                  )}
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="tenant-admin-name">Admin name</Label>
                  <Input id="tenant-admin-name" value={bootstrapAdminName} onChange={(e) => setBootstrapAdminName(e.target.value)} placeholder="Acme Admin" />
                </div>
              </div>
            </div>
          )}
          {error && (
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}
          <DialogFooter>
            <Button type="submit" disabled={!canSubmit}>
              {save.isPending ? 'Saving…' : isEdit ? 'Save changes' : 'Create tenant'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function DisableDialog({ tenant, onClose, onDone }: { tenant: Tenant; onClose: () => void; onDone: () => void }) {
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const disable = useMutation({
    mutationFn: () => setTenantEnabled(tenant.id, false, reason.trim()),
    onSuccess: onDone,
    onError: (e) => setError(fieldErrors(e)),
  });
  return (
    <Dialog open onOpenChange={(v) => { if (!v) onClose(); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Disable {tenant.name}?</DialogTitle>
          <DialogDescription>
            Logins and all tenant APIs are blocked immediately. Data is retained. Re-enable anytime.
          </DialogDescription>
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            if (!reason.trim() || disable.isPending) return;
            setError(null);
            disable.mutate();
          }}
        >
          <div className="space-y-1.5">
            <Label htmlFor="disable-reason">Reason *</Label>
            <Input id="disable-reason" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="e.g. contract suspended" required />
          </div>
          {error && (
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>Cancel</Button>
            <Button type="submit" variant="destructive" disabled={!reason.trim() || disable.isPending}>
              {disable.isPending ? 'Disabling…' : 'Disable tenant'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function AuditDialog({ tenant, onClose }: { tenant: Tenant; onClose: () => void }) {
  const audits = useQuery({
    queryKey: ['tenant-audits', tenant.id],
    queryFn: () => api<TenantAudit[]>(`/api/v1/platform/tenants/${tenant.id}/audits`),
  });
  return (
    <Dialog open onOpenChange={(v) => { if (!v) onClose(); }}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Audit trail — {tenant.name}</DialogTitle>
          <DialogDescription>Most recent platform actions for this tenant.</DialogDescription>
        </DialogHeader>
        {audits.isLoading ? (
          <p className="text-sm text-muted-foreground">Loading audit trail…</p>
        ) : audits.isError || (audits.data ?? []).length === 0 ? (
          <p className="text-sm text-muted-foreground">No audit entries yet.</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Action</TableHead>
                <TableHead>Actor</TableHead>
                <TableHead>Reason</TableHead>
                <TableHead>When</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {(audits.data ?? []).map((a) => (
                <TableRow key={a.id}>
                  <TableCell className="font-mono text-xs">{a.action}</TableCell>
                  <TableCell className="text-muted-foreground">{a.actorId} ({a.actorRole})</TableCell>
                  <TableCell className="text-muted-foreground">{a.reason ?? '—'}</TableCell>
                  <TableCell className="text-muted-foreground">{new Date(a.createdAt).toLocaleString()}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </DialogContent>
    </Dialog>
  );
}
