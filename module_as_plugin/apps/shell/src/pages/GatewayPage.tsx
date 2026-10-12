import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { Activity, ArrowRight, Route as RouteIcon, Server } from 'lucide-react';
import { api } from '../lib/api-client';
import { PageHeader } from '@/components/page-header';
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

interface GatewaySummary {
  serviceName: string;
  environment: string;
  serviceCount: number;
  routeCount: number;
  totalRequests: number;
}

const SUB_PAGES = [
  { to: '/gateway/services', title: 'Services', description: 'Downstream services the gateway proxies to.', icon: Server },
  { to: '/gateway/routes', title: 'Routes', description: 'Upstream to downstream path mappings.', icon: RouteIcon },
  { to: '/gateway/traffic', title: 'Traffic', description: 'Request counters with live refresh.', icon: Activity },
];

export function GatewayPage() {
  const summary = useQuery({
    queryKey: ['gateway-summary'],
    queryFn: () => api<GatewaySummary>('/api/v1/gateway/summary'),
    refetchInterval: 5000,
  });

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Gateway' }]}
        title="Gateway"
        description="Reverse-proxy overview. Live counters refresh every 5 seconds."
      />

      {summary.isLoading ? (
        <div className="grid gap-4 sm:grid-cols-3" aria-label="Loading gateway summary">
          {[0, 1, 2].map((i) => (
            <div key={i} className="rounded-lg border p-6">
              <Skeleton className="h-4 w-24" />
              <Skeleton className="mt-2 h-8 w-16" />
            </div>
          ))}
        </div>
      ) : summary.isError ? (
        <Alert variant="destructive">
          <AlertTitle>Failed to load gateway summary</AlertTitle>
          <AlertDescription>
            Check your session and try again.{' '}
            <button className="underline underline-offset-4" onClick={() => summary.refetch()}>
              Retry
            </button>
          </AlertDescription>
        </Alert>
      ) : (
        <div className="grid gap-4 sm:grid-cols-3">
          {[
            { label: 'Services', value: summary.data?.serviceCount },
            { label: 'Routes', value: summary.data?.routeCount },
            { label: 'Total requests', value: summary.data?.totalRequests },
          ].map((s) => (
            <Card key={s.label}>
              <CardHeader className="pb-2">
                <CardDescription>{s.label}</CardDescription>
                <CardTitle className="text-3xl font-semibold tabular-nums">{s.value ?? '—'}</CardTitle>
              </CardHeader>
            </Card>
          ))}
        </div>
      )}

      <div className="mt-6 grid gap-4 sm:grid-cols-3">
        {SUB_PAGES.map((p) => (
          <Link key={p.to} to={p.to} className="rounded-lg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
            <Card className="h-full transition-colors hover:bg-muted/40">
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <p.icon className="h-4 w-4 text-muted-foreground" aria-hidden />
                  {p.title}
                  <ArrowRight className="ml-auto h-4 w-4 text-muted-foreground" aria-hidden />
                </CardTitle>
                <CardDescription>{p.description}</CardDescription>
              </CardHeader>
            </Card>
          </Link>
        ))}
      </div>
    </div>
  );
}
