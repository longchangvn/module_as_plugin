import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Activity, RotateCcw } from 'lucide-react';
import { gatewayApi } from '../../lib/gateway';
import { PageHeader } from '@/components/page-header';
import { EmptyState } from '@/components/empty-state';
import { ConfirmDialog } from '@/components/confirm-dialog';
import { Button } from '@/components/ui/button';
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

// Gateway → Traffic: total + per-route breakdown, 5s auto-refresh, reset
// behind a confirmation.
export function GatewayTrafficPage() {
  const queryClient = useQueryClient();
  const traffic = useQuery({
    queryKey: ['gateway-traffic'],
    queryFn: gatewayApi.traffic,
    refetchInterval: 5000,
  });
  const reset = useMutation({
    mutationFn: gatewayApi.resetTraffic,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['gateway-traffic'] }),
  });

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Gateway', to: '/gateway' }, { label: 'Traffic' }]}
        title="Gateway Traffic"
        description="Live request counters per route. Refreshes every 5 seconds."
        actions={
          <ConfirmDialog
            trigger={
              <Button variant="outline" disabled={reset.isPending}>
                <RotateCcw className="h-4 w-4" aria-hidden />
                Reset counters
              </Button>
            }
            title="Reset traffic counters?"
            description="All per-route counters return to zero. Historical data is not recoverable."
            confirmLabel="Reset counters"
            pending={reset.isPending}
            onConfirm={() => reset.mutate()}
          />
        }
      />

      {traffic.isLoading ? (
        <div className="space-y-4" aria-label="Loading traffic">
          <div className="rounded-lg border p-6">
            <Skeleton className="h-4 w-32" />
            <Skeleton className="mt-2 h-8 w-20" />
          </div>
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </div>
      ) : traffic.isError ? (
        <Alert variant="destructive">
          <AlertTitle>Failed to load traffic</AlertTitle>
          <AlertDescription>
            Check your session and try again.{' '}
            <button className="underline underline-offset-4" onClick={() => traffic.refetch()}>
              Retry
            </button>
          </AlertDescription>
        </Alert>
      ) : (
        <>
          <div className="grid gap-4 sm:grid-cols-3">
            <Card>
              <CardHeader className="pb-2">
                <CardDescription>Total requests</CardDescription>
                <CardTitle className="text-3xl font-semibold tabular-nums">{traffic.data?.total ?? 0}</CardTitle>
              </CardHeader>
            </Card>
          </div>

          <h2 className="mb-3 mt-6 text-sm font-medium text-muted-foreground">Per-route breakdown</h2>
          {(traffic.data?.routes ?? []).length === 0 ? (
            <EmptyState
              icon={Activity}
              title="No traffic yet"
              description="Counters appear here once requests flow through the gateway."
            />
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Route</TableHead>
                  <TableHead className="text-right">Requests</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(traffic.data?.routes ?? []).map((r) => (
                  <TableRow key={r.route}>
                    <TableCell className="font-mono text-xs">{r.route}</TableCell>
                    <TableCell className="text-right tabular-nums">{r.count}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </>
      )}
    </div>
  );
}
