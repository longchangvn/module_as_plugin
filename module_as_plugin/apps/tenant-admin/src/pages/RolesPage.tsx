import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Power, PowerOff, UserCog } from 'lucide-react';
import { api } from '../lib/api-client';
import { PageHeader } from '@/components/page-header';
import { EmptyState } from '@/components/empty-state';
import { SearchInput } from '@/components/search-input';
import { StatusBadge } from '@/components/status-badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { apiError, type Role } from '../lib/types';

export function RolesPage() {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api<Role[]>('/api/v1/admin/roles') });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['roles'] });

  const active = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      api(`/api/v1/admin/roles/${id}`, { method: 'PATCH', body: JSON.stringify({ isActive }) }),
    onSuccess: refresh,
    onError: (e) => setError(apiError(e)),
  });

  const rows = (roles.data ?? []).filter((r) => {
    const q = search.trim().toLowerCase();
    if (!q) return true;
    return r.name.toLowerCase().includes(q);
  });

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Roles' }]}
        title="Roles"
        description="Grouped permission sets. Assign roles to users on the user detail page."
        actions={
          <Button onClick={() => { setError(null); setCreateOpen(true); }}>
            <Plus className="h-4 w-4" aria-hidden />
            New role
          </Button>
        }
      />
      {error && (
        <Alert variant="destructive" className="mb-4">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      <div className="mb-4">
        <SearchInput value={search} onChange={setSearch} placeholder="Search roles…" className="max-w-sm" />
      </div>
      {roles.isLoading ? (
        <div className="space-y-2" aria-label="Loading roles">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </div>
      ) : roles.isError ? (
        <Alert variant="destructive">
          <AlertDescription>
            Failed to load roles.{' '}
            <button className="underline underline-offset-4" onClick={() => roles.refetch()}>
              Retry
            </button>
          </AlertDescription>
        </Alert>
      ) : rows.length === 0 ? (
        <EmptyState icon={UserCog} title="No roles yet" description="Create the first role to get started." />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Description</TableHead>
              <TableHead>System</TableHead>
              <TableHead>Status</TableHead>
              <TableHead className="w-36">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((r) => (
              <TableRow key={r.id}>
                <TableCell className="font-medium">{r.name}</TableCell>
                <TableCell className="text-muted-foreground">{r.description ?? '—'}</TableCell>
                <TableCell className="text-muted-foreground">{r.isSystem ? 'Yes' : 'No'}</TableCell>
                <TableCell>
                  <StatusBadge status={r.isActive ? 'Active' : 'Inactive'} />
                </TableCell>
                <TableCell>
                  <div className="flex items-center gap-1">
                    <Link to={`/roles/${r.id}`}>
                      <Button variant="ghost" size="icon" aria-label={`Manage ${r.name}`}>
                        <Pencil className="h-4 w-4" />
                      </Button>
                    </Link>
                    <Button
                      variant="ghost"
                      size="icon"
                      disabled={active.isPending}
                      onClick={() => { setError(null); active.mutate({ id: r.id, isActive: !r.isActive }); }}
                      aria-label={r.isActive ? `Deactivate ${r.name}` : `Activate ${r.name}`}
                    >
                      {r.isActive ? <PowerOff className="h-4 w-4 text-destructive" /> : <Power className="h-4 w-4 text-emerald-600" />}
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      <CreateRoleDialog open={createOpen} onOpenChange={setCreateOpen} onSaved={() => { setCreateOpen(false); refresh(); }} />
    </div>
  );
}

function CreateRoleDialog({ open, onOpenChange, onSaved }: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [error, setError] = useState<string | null>(null);

  const create = useMutation({
    mutationFn: () => api('/api/v1/admin/roles', {
      method: 'POST',
      body: JSON.stringify({ name: name.trim(), description: description.trim() || undefined }),
    }),
    onSuccess: () => { setName(''); setDescription(''); onSaved(); },
    onError: (e) => setError(apiError(e)),
  });

  const valid = name.trim().length > 0;

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v) setError(null); onOpenChange(v); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Create role</DialogTitle>
          <DialogDescription>Permissions are granted on the role detail page.</DialogDescription>
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={(e) => { e.preventDefault(); if (!valid || create.isPending) return; setError(null); create.mutate(); }}
        >
          <div className="space-y-1.5">
            <Label htmlFor="role-name">Name *</Label>
            <Input id="role-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="Support" required />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="role-description">Description</Label>
            <Input id="role-description" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="What this role is for" />
          </div>
          {error && (
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}
          <DialogFooter>
            <Button type="submit" disabled={!valid || create.isPending}>
              {create.isPending ? 'Creating…' : 'Create role'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
