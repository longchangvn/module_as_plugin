import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { gatewayApi } from '../../lib/gateway';

// Gateway → Traffic page: totals + per-route breakdown, 5s auto-refresh, reset.
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
      <h1>Gateway Traffic</h1>
      {traffic.isLoading && <p>Loading traffic…</p>}
      {traffic.isError && <p role="alert">Failed to load traffic.</p>}
      {traffic.data && (
        <>
          <p>Total requests: {traffic.data.total}</p>
          <ul>
            {traffic.data.routes.map((r) => (
              <li key={r.route}>{r.route}: {r.count}</li>
            ))}
          </ul>
        </>
      )}
      <button onClick={() => reset.mutate()}>Reset counters</button>
    </div>
  );
}
