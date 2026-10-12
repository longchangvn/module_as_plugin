import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, X } from 'lucide-react';
import { api } from '../lib/api-client';
import { PageHeader } from '@/components/page-header';
import { StatusBadge } from '@/components/status-badge';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { apiError, type Permission, type Role } from '../lib/types';

// Role detail: permissions granted to the role.
export function RoleDetailPage() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [permToAdd, setPermToAdd] = useState('');

  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api<Role[]>('/api/v1/admin/roles') });
  const granted = useQuery({ queryKey: ['role-perms', id], queryFn: () => api<Permission[]>(`/api/v1/admin/roles/${id}/permissions`) });
  const allPerms = useQuery({ queryKey: ['permissions'], queryFn: () => api<Permission[]>('/api/v1/auth/permissions') });

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['role-perms', id] });

  const grant = useMutation({
    mutationFn: (permissionId: string) => api(`/api/v1/admin/roles/${id}/permissions`, { method: 'POST', body: JSON.stringify({ permissionId }) }),
    onSuccess: () => { setPermToAdd(''); refresh(); },
    onError: (e) => setError(apiError(e)),
  });
  const revoke = useMutation({
    mutationFn: (permissionId: string) => api(`/api/v1/admin/roles/${id}/permissions/${permissionId}`, { method: 'DELETE' }),
    onSuccess: refresh,
    onError: (e) => setError(apiError(e)),
  });

  const role = (roles.data ?? []).find((r) => r.id === id);
  const grantedIds = new Set((granted.data ?? []).map((p) => p.id));
  const available = (allPerms.data ?? []).filter((p) => !grantedIds.has(p.id));

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Roles', to: '/roles' }, { label: role?.name ?? id }]}
        title={role?.name ?? 'Role'}
        description={role?.description ?? ''}
      />
      {error && (
        <Alert variant="destructive" className="mb-4">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      {roles.isLoading ? (
        <Skeleton className="h-10 w-full" />
      ) : !role ? (
        <Alert variant="destructive">
          <AlertDescription>
            Unknown role. <Link to="/roles" className="underline underline-offset-4">Back to roles</Link>
          </AlertDescription>
        </Alert>
      ) : (
        <div className="space-y-6">
          <div className="flex items-center gap-2">
            <StatusBadge status={role.isActive ? 'Active' : 'Inactive'} />
            {role.isSystem && <StatusBadge status="System" />}
          </div>
          <section>
            <h2 className="mb-2 text-sm font-semibold">Granted permissions</h2>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Code</TableHead>
                  <TableHead>Name</TableHead>
                  <TableHead className="w-24">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(granted.data ?? []).map((p) => (
                  <TableRow key={p.id}>
                    <TableCell className="font-mono text-xs">{p.code}</TableCell>
                    <TableCell className="text-muted-foreground">{p.name}</TableCell>
                    <TableCell>
                      <Button variant="ghost" size="icon" aria-label={`Revoke permission ${p.code}`}
                        onClick={() => { setError(null); revoke.mutate(p.id); }}>
                        <X className="h-4 w-4" />
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <div className="mt-3 flex items-end gap-2">
              <div className="w-64 space-y-1.5">
                <Label htmlFor="grant-perm">Grant permission</Label>
                <Select value={permToAdd} onValueChange={setPermToAdd}>
                  <SelectTrigger id="grant-perm">
                    <SelectValue placeholder="Select a permission" />
                  </SelectTrigger>
                  <SelectContent>
                    {available.map((p) => (
                      <SelectItem key={p.id} value={p.id}>{p.code}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <Button disabled={!permToAdd || grant.isPending}
                onClick={() => { setError(null); grant.mutate(permToAdd); }}>
                <Plus className="h-4 w-4" aria-hidden />
                Grant
              </Button>
            </div>
          </section>
        </div>
      )}
    </div>
  );
}
