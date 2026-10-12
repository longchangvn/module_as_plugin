import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { KeyRound, UserCog, Users } from 'lucide-react';
import { api } from '../lib/api-client';
import { PageHeader } from '@/components/page-header';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useSession } from '../app/session';
import type { Permission, Role, TenantUser } from '../lib/types';

interface ResolveResponse {
  tenantId: string;
  userId: string;
  permissions: string[];
  version: number;
}

export function HomePage() {
  const { session } = useSession();
  const users = useQuery({ queryKey: ['users'], queryFn: () => api<TenantUser[]>('/api/v1/admin/users') });
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api<Role[]>('/api/v1/admin/roles') });
  const permissions = useQuery({ queryKey: ['permissions'], queryFn: () => api<Permission[]>('/api/v1/auth/permissions') });
  const resolve = useQuery({
    queryKey: ['resolve', session.tenantId, session.userId],
    queryFn: () =>
      api<ResolveResponse>(
        `/api/v1/auth/permissions/resolve?tenantId=${encodeURIComponent(session.tenantId ?? '')}&userId=${encodeURIComponent(session.userId ?? '')}`,
      ),
    enabled: Boolean(session.tenantId && session.userId),
  });

  const cards = [
    { to: '/users', title: 'Users', icon: Users, value: users.data?.length, loading: users.isLoading },
    { to: '/roles', title: 'Roles', icon: UserCog, value: roles.data?.length, loading: roles.isLoading },
    { to: '/permissions', title: 'Permissions', icon: KeyRound, value: permissions.data?.length, loading: permissions.isLoading },
  ];

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Home' }]}
        title="Tenant overview"
        description={`Signed in as ${session.tenantAdmin ? 'tenant admin' : 'tenant user'} · ${session.label ?? ''}`}
      />
      <div className="grid gap-4 sm:grid-cols-3">
        {cards.map((c) => (
          <Link key={c.to} to={c.to}>
            <Card className="transition-colors hover:border-primary/40">
              <CardHeader className="flex flex-row items-center gap-2 space-y-0 pb-2">
                <c.icon className="h-4 w-4 text-muted-foreground" aria-hidden />
                <CardTitle className="text-sm font-medium">{c.title}</CardTitle>
              </CardHeader>
              <CardContent>
                {c.loading ? (
                  <Skeleton className="h-8 w-16" />
                ) : (
                  <p className="text-3xl font-semibold tracking-tight">{c.value ?? '—'}</p>
                )}
              </CardContent>
            </Card>
          </Link>
        ))}
      </div>
      <Card className="mt-4">
        <CardHeader>
          <CardTitle className="text-sm font-medium">My effective permissions</CardTitle>
        </CardHeader>
        <CardContent>
          {resolve.isLoading ? (
            <Skeleton className="h-5 w-48" />
          ) : (
            <p className="text-sm text-muted-foreground">
              {(resolve.data?.permissions ?? []).length} permission(s) via roles and direct grants.
            </p>
          )}
          {(resolve.data?.permissions ?? []).length > 0 && (
            <div className="mt-2 flex flex-wrap gap-1.5">
              {resolve.data!.permissions.map((p) => (
                <code key={p} className="rounded bg-muted px-1.5 py-0.5 font-mono text-xs">
                  {p}
                </code>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
