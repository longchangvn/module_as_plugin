import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { KeyRound } from 'lucide-react';
import { api } from '../lib/api-client';
import { PageHeader } from '@/components/page-header';
import { EmptyState } from '@/components/empty-state';
import { SearchInput } from '@/components/search-input';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import type { Permission } from '../lib/types';

// Permission catalog (read-only): seeded by installed modules.
export function PermissionsPage() {
  const [search, setSearch] = useState('');
  const permissions = useQuery({ queryKey: ['permissions'], queryFn: () => api<Permission[]>('/api/v1/auth/permissions') });

  const rows = (permissions.data ?? []).filter((p) => {
    const q = search.trim().toLowerCase();
    if (!q) return true;
    return p.code.toLowerCase().includes(q) || p.name.toLowerCase().includes(q);
  });

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Permissions' }]}
        title="Permissions"
        description="Read-only catalog. Grant permissions to roles, or directly to users for exceptions."
      />
      <div className="mb-4">
        <SearchInput value={search} onChange={setSearch} placeholder="Search permissions…" className="max-w-sm" />
      </div>
      {permissions.isLoading ? (
        <div className="space-y-2" aria-label="Loading permissions">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </div>
      ) : permissions.isError ? (
        <Alert variant="destructive">
          <AlertDescription>
            Failed to load permissions.{' '}
            <button className="underline underline-offset-4" onClick={() => permissions.refetch()}>
              Retry
            </button>
          </AlertDescription>
        </Alert>
      ) : rows.length === 0 ? (
        <EmptyState icon={KeyRound} title="No permissions yet" description="Install a module to seed permissions." />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Code</TableHead>
              <TableHead>Name</TableHead>
              <TableHead>Object</TableHead>
              <TableHead>Action</TableHead>
              <TableHead>Menu access</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((p) => (
              <TableRow key={p.id}>
                <TableCell className="font-mono text-xs">{p.code}</TableCell>
                <TableCell className="font-medium">{p.name}</TableCell>
                <TableCell className="text-muted-foreground">{p.object}</TableCell>
                <TableCell className="text-muted-foreground">{p.action}</TableCell>
                <TableCell className="text-muted-foreground">{p.isMenuAccess ? 'Yes' : 'No'}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
