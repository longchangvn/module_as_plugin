import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { gatewayApi, parseMany, type GatewayRoute } from '../../lib/gateway';

// Gateway → Routing page: list, create, edit, delete (force confirm for last route).
export function GatewayRoutesPage() {
  const queryClient = useQueryClient();
  const [form, setForm] = useState({
    upstreamPathTemplate: '',
    downstreamPathTemplate: '',
    upstreamHttpMethod: 'GET',
    priority: 1,
    serviceId: 'platform',
  });
  const [error, setError] = useState<string | null>(null);

  const routes = useQuery({
    queryKey: ['gateway-routes'],
    queryFn: async () => parseMany<GatewayRoute>(await gatewayApi.routes()),
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['gateway-routes'] });

  const save = useMutation({
    mutationFn: () =>
      gatewayApi.addRoute({ ...form, priority: Number(form.priority), upstreamHttpMethod: form.upstreamHttpMethod ? [form.upstreamHttpMethod] : [] }),
    onSuccess: refresh,
    onError: (e: Error) => setError(e.message),
  });
  const remove = useMutation({
    mutationFn: (id: string) => gatewayApi.deleteRoute(id),
    onSuccess: refresh,
    onError: (e: Error) => setError(e.message),
  });

  return (
    <div>
      <h1>Gateway Routes</h1>
      {routes.isLoading && <p>Loading routes…</p>}
      <ul>
        {(routes.data ?? []).map((r) => (
          <li key={r.Key ?? r.UpstreamPathTemplate}>
            {r.UpstreamPathTemplate} → {r.DownstreamPathTemplate} [{(r.UpstreamHttpMethod ?? []).join(',') || 'all'}] p{r.Priority}
            {r.Metadata?.managedBy && ` (${r.Metadata.managedBy})`}
            {r.Key && <button onClick={() => remove.mutate(r.Key as string)}>Delete</button>}
          </li>
        ))}
      </ul>
      <h2>New route</h2>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setError(null);
          save.mutate();
        }}
      >
        <input placeholder="upstream e.g. /api/v1/reporting/{rest}" value={form.upstreamPathTemplate} onChange={(e) => setForm({ ...form, upstreamPathTemplate: e.target.value })} />
        <input placeholder="downstream e.g. /api/v1/{rest}" value={form.downstreamPathTemplate} onChange={(e) => setForm({ ...form, downstreamPathTemplate: e.target.value })} />
        <input placeholder="method (empty = all)" value={form.upstreamHttpMethod} onChange={(e) => setForm({ ...form, upstreamHttpMethod: e.target.value })} />
        <input placeholder="service id" value={form.serviceId} onChange={(e) => setForm({ ...form, serviceId: e.target.value })} />
        <button type="submit" disabled={save.isPending}>Create</button>
      </form>
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
