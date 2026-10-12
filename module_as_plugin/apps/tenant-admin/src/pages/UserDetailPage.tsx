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
import { apiError, type Permission, type Role, type TenantUser } from '../lib/types';

// User detail: roles assigned to the user + direct (additional) permissions.
export function UserDetailPage() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [roleToAdd, setRoleToAdd] = useState('');
  const [permToAdd, setPermToAdd] = useState('');

  const users = useQuery({ queryKey: ['users'], queryFn: () => api<TenantUser[]>('/api/v1/admin/users') });
  const userRoles = useQuery({ queryKey: ['user-roles', id], queryFn: () => api<Role[]>(`/api/v1/admin/users/${id}/roles`) });
  const userPerms = useQuery({ queryKey: ['user-perms', id], queryFn: () => api<Permission[]>(`/api/v1/admin/users/${id}/permissions`) });
  const allRoles = useQuery({ queryKey: ['roles'], queryFn: () => api<Role[]>('/api/v1/admin/roles') });
  const allPerms = useQuery({ queryKey: ['permissions'], queryFn: () => api<Permission[]>('/api/v1/auth/permissions') });

  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ['user-roles', id] });
    queryClient.invalidateQueries({ queryKey: ['user-perms', id] });
  };

  const assignRole = useMutation({
    mutationFn: (roleId: string) => api(`/api/v1/admin/users/${id}/roles`, { method: 'POST', body: JSON.stringify({ roleId }) }),
    onSuccess: () => { setRoleToAdd(''); refresh(); },
    onError: (e) => setError(apiError(e)),
  });
  const unassignRole = useMutation({
    mutationFn: (roleId: string) => api(`/api/v1/admin/users/${id}/roles/${roleId}`, { method: 'DELETE' }),
    onSuccess: refresh,
    onError: (e) => setError(apiError(e)),
  });
  const grantPerm = useMutation({
    mutationFn: (permissionId: string) => api(`/api/v1/admin/users/${id}/permissions`, { method: 'POST', body: JSON.stringify({ permissionId }) }),
    onSuccess: () => { setPermToAdd(''); refresh(); },
    onError: (e) => setError(apiError(e)),
  });
  const revokePerm = useMutation({
    mutationFn: (permissionId: string) => api(`/api/v1/admin/users/${id}/permissions/${permissionId}`, { method: 'DELETE' }),
    onSuccess: refresh,
    onError: (e) => setError(apiError(e)),
  });

  const user = (users.data ?? []).find((u) => u.id === id);
  const assignedRoleIds = new Set((userRoles.data ?? []).map((r) => r.id));
  const grantedPermIds = new Set((userPerms.data ?? []).map((p) => p.id));
  const availableRoles = (allRoles.data ?? []).filter((r) => r.isActive && !assignedRoleIds.has(r.id));
  const availablePerms = (allPerms.data ?? []).filter((p) => !grantedPermIds.has(p.id));

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Users', to: '/users' }, { label: user?.email ?? id }]}
        title={user?.email ?? 'User'}
        description={user ? `${user.name} · ` : ''}
      />
      {error && (
        <Alert variant="destructive" className="mb-4">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      {users.isLoading ? (
        <Skeleton className="h-10 w-full" />
      ) : !user ? (
        <Alert variant="destructive">
          <AlertDescription>
            Unknown user. <Link to="/users" className="underline underline-offset-4">Back to users</Link>
          </AlertDescription>
        </Alert>
      ) : (
        <div className="space-y-6">
          <div className="flex items-center gap-2">
            <StatusBadge status={user.isActive ? 'Active' : 'Inactive'} />
            <span className="text-sm text-muted-foreground">{user.name}</span>
          </div>

          <section>
            <h2 className="mb-2 text-sm font-semibold">Roles</h2>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Role</TableHead>
                  <TableHead>System</TableHead>
                  <TableHead className="w-24">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(userRoles.data ?? []).map((r) => (
                  <TableRow key={r.id}>
                    <TableCell className="font-medium">{r.name}</TableCell>
                    <TableCell className="text-muted-foreground">{r.isSystem ? 'Yes' : 'No'}</TableCell>
                    <TableCell>
                      <Button variant="ghost" size="icon" aria-label={`Remove role ${r.name}`}
                        onClick={() => { setError(null); unassignRole.mutate(r.id); }}>
                        <X className="h-4 w-4" />
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <div className="mt-3 flex items-end gap-2">
              <div className="w-64 space-y-1.5">
                <Label htmlFor="assign-role">Assign role</Label>
                <Select value={roleToAdd} onValueChange={setRoleToAdd}>
                  <SelectTrigger id="assign-role">
                    <SelectValue placeholder="Select a role" />
                  </SelectTrigger>
                  <SelectContent>
                    {availableRoles.map((r) => (
                      <SelectItem key={r.id} value={r.id}>{r.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <Button disabled={!roleToAdd || assignRole.isPending}
                onClick={() => { setError(null); assignRole.mutate(roleToAdd); }}>
                <Plus className="h-4 w-4" aria-hidden />
                Assign
              </Button>
            </div>
          </section>

          <section>
            <h2 className="mb-2 text-sm font-semibold">Additional permissions (direct grants)</h2>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Code</TableHead>
                  <TableHead>Name</TableHead>
                  <TableHead className="w-24">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(userPerms.data ?? []).map((p) => (
                  <TableRow key={p.id}>
                    <TableCell className="font-mono text-xs">{p.code}</TableCell>
                    <TableCell className="text-muted-foreground">{p.name}</TableCell>
                    <TableCell>
                      <Button variant="ghost" size="icon" aria-label={`Revoke permission ${p.code}`}
                        onClick={() => { setError(null); revokePerm.mutate(p.id); }}>
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
                    {availablePerms.map((p) => (
                      <SelectItem key={p.id} value={p.id}>{p.code}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <Button disabled={!permToAdd || grantPerm.isPending}
                onClick={() => { setError(null); grantPerm.mutate(permToAdd); }}>
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
