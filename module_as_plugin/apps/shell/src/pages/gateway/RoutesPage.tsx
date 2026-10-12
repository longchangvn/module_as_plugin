import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, Route as RouteIcon, Trash2 } from 'lucide-react';
import { gatewayApi, parseMany, type GatewayRoute } from '../../lib/gateway';
import { PageHeader } from '@/components/page-header';
import { EmptyState } from '@/components/empty-state';
import { SearchInput } from '@/components/search-input';
import { Badge } from '@/components/ui/badge';
import { ConfirmDialog } from '@/components/confirm-dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

const EMPTY_FORM = {
  upstreamPathTemplate: '',
  downstreamPathTemplate: '',
  upstreamHttpMethod: 'GET',
  priority: 1,
  serviceId: 'platform',
};

// Gateway → Routing: searchable route table, create dialog, delete with
// confirmation (force for module-managed leftovers via the row note).
export function GatewayRoutesPage() {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState(EMPTY_FORM);
  const [error, setError] = useState<string | null>(null);

  const routes = useQuery({
    queryKey: ['gateway-routes'],
    queryFn: async () => parseMany<GatewayRoute>(await gatewayApi.routes()),
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['gateway-routes'] });

  const save = useMutation({
    mutationFn: () =>
      gatewayApi.addRoute({
        ...form,
        priority: Number(form.priority),
        upstreamHttpMethod: form.upstreamHttpMethod ? [form.upstreamHttpMethod] : [],
      }),
    onSuccess: () => {
      setOpen(false);
      setForm(EMPTY_FORM);
      refresh();
    },
    onError: (e: Error) => setError(e.message),
  });
  const remove = useMutation({
    mutationFn: (id: string) => gatewayApi.deleteRoute(id),
    onSuccess: refresh,
    onError: (e: Error) => setError(e.message),
  });

  const rows = (routes.data ?? []).filter((r) => {
    const q = search.trim().toLowerCase();
    if (!q) return true;
    return r.UpstreamPathTemplate.toLowerCase().includes(q) || r.DownstreamPathTemplate.toLowerCase().includes(q);
  });

  const canSubmit =
    form.upstreamPathTemplate.trim().length > 0 &&
    form.downstreamPathTemplate.trim().length > 0 &&
    form.serviceId.trim().length > 0 &&
    !save.isPending;

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Gateway', to: '/gateway' }, { label: 'Routes' }]}
        title="Gateway Routes"
        description="Upstream paths the gateway accepts and where they are proxied."
        actions={
          <Dialog open={open} onOpenChange={(v) => { setOpen(v); setError(null); }}>
            <DialogTrigger asChild>
              <Button>
                <Plus className="h-4 w-4" aria-hidden />
                New route
              </Button>
            </DialogTrigger>
            <DialogContent>
              <DialogHeader>
                <DialogTitle>New route</DialogTitle>
                <DialogDescription>Map an upstream path to a downstream service path.</DialogDescription>
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
                <div className="space-y-1.5">
                  <Label htmlFor="route-upstream">Upstream path *</Label>
                  <Input
                    id="route-upstream"
                    value={form.upstreamPathTemplate}
                    onChange={(e) => setForm({ ...form, upstreamPathTemplate: e.target.value })}
                    placeholder="/api/v1/reporting/{rest}"
                    className="font-mono text-xs"
                    required
                  />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="route-downstream">Downstream path *</Label>
                  <Input
                    id="route-downstream"
                    value={form.downstreamPathTemplate}
                    onChange={(e) => setForm({ ...form, downstreamPathTemplate: e.target.value })}
                    placeholder="/api/v1/{rest}"
                    className="font-mono text-xs"
                    required
                  />
                </div>
                <div className="grid gap-4 sm:grid-cols-3">
                  <div className="space-y-1.5">
                    <Label htmlFor="route-method">Method</Label>
                    <Input
                      id="route-method"
                      value={form.upstreamHttpMethod}
                      onChange={(e) => setForm({ ...form, upstreamHttpMethod: e.target.value })}
                      placeholder="Empty = all"
                    />
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="route-priority">Priority</Label>
                    <Input
                      id="route-priority"
                      type="number"
                      value={form.priority}
                      onChange={(e) => setForm({ ...form, priority: Number(e.target.value) })}
                    />
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="route-service">Service ID *</Label>
                    <Input
                      id="route-service"
                      value={form.serviceId}
                      onChange={(e) => setForm({ ...form, serviceId: e.target.value })}
                      placeholder="platform"
                      required
                    />
                  </div>
                </div>
                {error && (
                  <Alert variant="destructive">
                    <AlertDescription>{error}</AlertDescription>
                  </Alert>
                )}
                <DialogFooter>
                  <Button type="submit" disabled={!canSubmit}>
                    {save.isPending ? 'Creating…' : 'Create route'}
                  </Button>
                </DialogFooter>
              </form>
            </DialogContent>
          </Dialog>
        }
      />

      <div className="mb-4">
        <SearchInput value={search} onChange={setSearch} placeholder="Search routes…" className="max-w-sm" />
      </div>

      {remove.isError && (
        <Alert variant="destructive" className="mb-4">
          <AlertDescription>Delete failed. The route may be managed by an installed module.</AlertDescription>
        </Alert>
      )}

      {routes.isLoading ? (
        <div className="space-y-2" aria-label="Loading routes">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </div>
      ) : routes.isError ? (
        <Alert variant="destructive">
          <AlertTitle>Failed to load routes</AlertTitle>
          <AlertDescription>
            Check your session and try again.{' '}
            <button className="underline underline-offset-4" onClick={() => routes.refetch()}>
              Retry
            </button>
          </AlertDescription>
        </Alert>
      ) : rows.length === 0 ? (
        <EmptyState
          icon={RouteIcon}
          title={search ? 'No routes match your search' : 'No routes defined'}
          description={search ? 'Try a different search term.' : 'Create your first route to start proxying traffic.'}
        />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Upstream</TableHead>
              <TableHead>Downstream</TableHead>
              <TableHead>Methods</TableHead>
              <TableHead>Priority</TableHead>
              <TableHead>Managed by</TableHead>
              <TableHead className="w-20">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((r) => (
              <TableRow key={r.Key ?? r.UpstreamPathTemplate}>
                <TableCell className="font-mono text-xs">{r.UpstreamPathTemplate}</TableCell>
                <TableCell className="font-mono text-xs text-muted-foreground">{r.DownstreamPathTemplate}</TableCell>
                <TableCell>
                  {(r.UpstreamHttpMethod ?? []).length > 0 ? (
                    <span className="flex gap-1">
                      {r.UpstreamHttpMethod.map((m) => (
                        <Badge key={m} variant="secondary">{m}</Badge>
                      ))}
                    </span>
                  ) : (
                    <span className="text-muted-foreground">all</span>
                  )}
                </TableCell>
                <TableCell className="tabular-nums">{r.Priority}</TableCell>
                <TableCell className="text-muted-foreground">{r.Metadata?.managedBy ?? '—'}</TableCell>
                <TableCell>
                  {r.Key ? (
                    <ConfirmDialog
                      trigger={
                        <Button variant="ghost" size="icon" aria-label={`Delete route ${r.UpstreamPathTemplate}`}>
                          <Trash2 className="h-4 w-4 text-destructive" />
                        </Button>
                      }
                      title="Delete route?"
                      description={`${r.UpstreamPathTemplate} stops resolving. Module-managed routes are normally removed by uninstalling the module.`}
                      confirmLabel="Delete route"
                      pending={remove.isPending}
                      onConfirm={() => remove.mutate(r.Key as string)}
                    />
                  ) : (
                    <span className="text-muted-foreground">—</span>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
