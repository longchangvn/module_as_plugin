import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Server, Trash2 } from 'lucide-react';
import { gatewayApi, parseMany, type GatewayService } from '../../lib/gateway';
import { PageHeader } from '@/components/page-header';
import { EmptyState } from '@/components/empty-state';
import { SearchInput } from '@/components/search-input';
import { StatusBadge } from '@/components/status-badge';
import { ConfirmDialog } from '@/components/confirm-dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Switch } from '@/components/ui/switch';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

const EMPTY_FORM = { name: '', host: 'localhost', port: 5121, scheme: 'http', description: '' };

// Gateway → Services: searchable table (+reachability probe), add/edit dialog,
// delete with confirmation. Writes need platform Admin. (§6, §8)
export function GatewayServicesPage() {
  const queryClient = useQueryClient();
  const [withHealth, setWithHealth] = useState(false);
  const [search, setSearch] = useState('');
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<GatewayService | null>(null);
  const [form, setForm] = useState(EMPTY_FORM);
  const [error, setError] = useState<string | null>(null);

  const services = useQuery({
    queryKey: ['gateway-services', withHealth],
    queryFn: async () => parseMany<GatewayService>(await gatewayApi.services(withHealth)),
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['gateway-services'] });

  const save = useMutation({
    mutationFn: () => {
      const body = { ...form, port: Number(form.port), description: form.description || undefined };
      return editing ? gatewayApi.updateService(editing.id, body) : gatewayApi.addService(body);
    },
    onSuccess: () => {
      setDialogOpen(false);
      setEditing(null);
      refresh();
    },
    onError: (e: Error) => setError(e.message),
  });
  const remove = useMutation({
    mutationFn: (id: string) => gatewayApi.deleteService(id),
    onSuccess: refresh,
    onError: (e: Error) => setError(e.message),
  });

  const openAdd = () => {
    setEditing(null);
    setForm(EMPTY_FORM);
    setError(null);
    setDialogOpen(true);
  };
  const openEdit = (s: GatewayService) => {
    setEditing(s);
    setForm({ name: s.name, host: s.host, port: s.port, scheme: s.scheme, description: s.description ?? '' });
    setError(null);
    setDialogOpen(true);
  };

  const rows = (services.data ?? []).filter((s) => {
    const q = search.trim().toLowerCase();
    if (!q) return true;
    return s.name.toLowerCase().includes(q) || s.host.toLowerCase().includes(q);
  });

  const portValid = Number.isInteger(Number(form.port)) && Number(form.port) > 0 && Number(form.port) < 65536;
  const canSubmit = form.name.trim().length > 0 && form.host.trim().length > 0 && portValid && !save.isPending;

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Gateway', to: '/gateway' }, { label: 'Services' }]}
        title={`Gateway Services`}
        description="Downstream services the gateway proxies to. Changes apply on save."
        actions={
          <Button onClick={openAdd}>
            <Plus className="h-4 w-4" aria-hidden />
            Add service
          </Button>
        }
      />

      <div className="mb-4 flex flex-wrap items-center gap-4">
        <SearchInput value={search} onChange={setSearch} placeholder="Search services…" className="max-w-sm flex-1" />
        <div className="flex items-center gap-2">
          <Switch id="health-check" checked={withHealth} onCheckedChange={setWithHealth} />
          <Label htmlFor="health-check">Check reachability</Label>
        </div>
      </div>

      {(save.isError || remove.isError) && error && (
        <Alert variant="destructive" className="mb-4">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}

      {services.isLoading ? (
        <div className="space-y-2" aria-label="Loading services">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </div>
      ) : services.isError ? (
        <Alert variant="destructive">
          <AlertTitle>Failed to load services</AlertTitle>
          <AlertDescription>
            Check your session and try again.{' '}
            <button className="underline underline-offset-4" onClick={() => services.refetch()}>
              Retry
            </button>
          </AlertDescription>
        </Alert>
      ) : rows.length === 0 ? (
        <EmptyState
          icon={Server}
          title={search ? 'No services match your search' : 'No services registered'}
          description={search ? 'Try a different search term.' : 'Add your first downstream service to get started.'}
          actionLabel={search ? undefined : 'Add service'}
          onAction={search ? undefined : openAdd}
        />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Target</TableHead>
              <TableHead>Status</TableHead>
              {withHealth && <TableHead>Health</TableHead>}
              <TableHead className="w-24">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((s) => (
              <TableRow key={s.id}>
                <TableCell className="font-medium">{s.name}</TableCell>
                <TableCell className="font-mono text-xs text-muted-foreground">
                  {s.scheme}://{s.host}:{s.port}
                </TableCell>
                <TableCell>
                  <StatusBadge status={s.enabled ? 'Enabled' : 'Disabled'} />
                </TableCell>
                {withHealth && (
                  <TableCell>
                    {s.health ? <StatusBadge status={s.health} /> : <span className="text-muted-foreground">—</span>}
                  </TableCell>
                )}
                <TableCell>
                  <div className="flex items-center gap-1">
                    <Button variant="ghost" size="icon" onClick={() => openEdit(s)} aria-label={`Edit ${s.name}`}>
                      <Pencil className="h-4 w-4" />
                    </Button>
                    <ConfirmDialog
                      trigger={
                        <Button variant="ghost" size="icon" aria-label={`Delete ${s.name}`}>
                          <Trash2 className="h-4 w-4 text-destructive" />
                        </Button>
                      }
                      title={`Delete service “${s.name}”?`}
                      description="Routes pointing at this service stop resolving. This cannot be undone."
                      confirmLabel="Delete service"
                      pending={remove.isPending}
                      onConfirm={() => remove.mutate(s.id)}
                    />
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      <Dialog open={dialogOpen} onOpenChange={setDialogOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{editing ? `Edit ${editing.name}` : 'Add service'}</DialogTitle>
            <DialogDescription>
              {editing ? 'Update the downstream target. Routes follow the service.' : 'Register a downstream service for routes to target.'}
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
                <Label htmlFor="svc-name">Name *</Label>
                <Input id="svc-name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="platform" required />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="svc-scheme">Scheme *</Label>
                <Select value={form.scheme} onValueChange={(v) => setForm({ ...form, scheme: v })}>
                  <SelectTrigger id="svc-scheme">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="http">http</SelectItem>
                    <SelectItem value="https">https</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="svc-host">Host *</Label>
                <Input id="svc-host" value={form.host} onChange={(e) => setForm({ ...form, host: e.target.value })} placeholder="localhost" required />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="svc-port">Port *</Label>
                <Input
                  id="svc-port"
                  type="number"
                  min={1}
                  max={65535}
                  value={form.port}
                  onChange={(e) => setForm({ ...form, port: Number(e.target.value) })}
                  required
                />
                {!portValid && (
                  <p className="text-xs text-destructive" role="alert">Port must be 1–65535.</p>
                )}
              </div>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="svc-desc">Description</Label>
              <Input id="svc-desc" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} placeholder="Optional" />
            </div>
            {error && (
              <Alert variant="destructive">
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            )}
            <DialogFooter>
              <Button type="submit" disabled={!canSubmit}>
                {save.isPending ? 'Saving…' : editing ? 'Save changes' : 'Add service'}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}
