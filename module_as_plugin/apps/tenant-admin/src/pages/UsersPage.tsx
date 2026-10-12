import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Power, PowerOff, Users } from 'lucide-react';
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
import { apiError, type TenantUser } from '../lib/types';

export async function setUserActive(id: string, isActive: boolean): Promise<void> {
  await api(`/api/v1/admin/users/${id}`, { method: 'PATCH', body: JSON.stringify({ isActive }) });
}

export function UsersPage() {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const users = useQuery({ queryKey: ['users'], queryFn: () => api<TenantUser[]>('/api/v1/admin/users') });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['users'] });

  const active = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) => setUserActive(id, isActive),
    onSuccess: refresh,
    onError: (e) => setError(apiError(e)),
  });

  const rows = (users.data ?? []).filter((u) => {
    const q = search.trim().toLowerCase();
    if (!q) return true;
    return u.email.toLowerCase().includes(q) || u.name.toLowerCase().includes(q);
  });

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Users' }]}
        title="Users"
        description="Tenant users. Deactivated users cannot log in."
        actions={
          <Button onClick={() => { setError(null); setCreateOpen(true); }}>
            <Plus className="h-4 w-4" aria-hidden />
            New user
          </Button>
        }
      />
      {error && (
        <Alert variant="destructive" className="mb-4">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      <div className="mb-4">
        <SearchInput value={search} onChange={setSearch} placeholder="Search users…" className="max-w-sm" />
      </div>
      {users.isLoading ? (
        <div className="space-y-2" aria-label="Loading users">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </div>
      ) : users.isError ? (
        <Alert variant="destructive">
          <AlertDescription>
            Failed to load users.{' '}
            <button className="underline underline-offset-4" onClick={() => users.refetch()}>
              Retry
            </button>
          </AlertDescription>
        </Alert>
      ) : rows.length === 0 ? (
        <EmptyState icon={Users} title="No users yet" description="Create the first user to get started." />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Email</TableHead>
              <TableHead>Name</TableHead>
              <TableHead>Status</TableHead>
              <TableHead className="w-36">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((u) => (
              <TableRow key={u.id}>
                <TableCell className="font-medium">{u.email}</TableCell>
                <TableCell className="text-muted-foreground">{u.name}</TableCell>
                <TableCell>
                  <StatusBadge status={u.isActive ? 'Active' : 'Inactive'} />
                </TableCell>
                <TableCell>
                  <div className="flex items-center gap-1">
                    <Link to={`/users/${u.id}`}>
                      <Button variant="ghost" size="icon" aria-label={`Manage ${u.email}`}>
                        <Pencil className="h-4 w-4" />
                      </Button>
                    </Link>
                    <Button
                      variant="ghost"
                      size="icon"
                      disabled={active.isPending}
                      onClick={() => { setError(null); active.mutate({ id: u.id, isActive: !u.isActive }); }}
                      aria-label={u.isActive ? `Deactivate ${u.email}` : `Activate ${u.email}`}
                    >
                      {u.isActive ? <PowerOff className="h-4 w-4 text-destructive" /> : <Power className="h-4 w-4 text-emerald-600" />}
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      <CreateUserDialog open={createOpen} onOpenChange={setCreateOpen} onSaved={() => { setCreateOpen(false); refresh(); }} />
    </div>
  );
}

function CreateUserDialog({ open, onOpenChange, onSaved }: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  onSaved: () => void;
}) {
  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);

  const create = useMutation({
    mutationFn: () => api('/api/v1/admin/users', { method: 'POST', body: JSON.stringify({ email: email.trim(), name: name.trim() }) }),
    onSuccess: () => { setEmail(''); setName(''); onSaved(); },
    onError: (e) => setError(apiError(e)),
  });

  const valid = /.+@.+\..+/.test(email.trim()) && name.trim().length > 0;

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v) setError(null); onOpenChange(v); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Create user</DialogTitle>
          <DialogDescription>Roles and direct permissions are assigned on the user detail page.</DialogDescription>
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={(e) => { e.preventDefault(); if (!valid || create.isPending) return; setError(null); create.mutate(); }}
        >
          <div className="space-y-1.5">
            <Label htmlFor="user-email">Email *</Label>
            <Input id="user-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="user@acme.example" required />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="user-name">Name *</Label>
            <Input id="user-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="Full name" required />
          </div>
          {error && (
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}
          <DialogFooter>
            <Button type="submit" disabled={!valid || create.isPending}>
              {create.isPending ? 'Creating…' : 'Create user'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
