import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { gatewayApi, parseMany, type GatewayService } from '../../lib/gateway';

// Gateway → Services page: list (+count), add, edit, delete; optional reachability (?health=true).
export function GatewayServicesPage() {
  const queryClient = useQueryClient();
  const [withHealth, setWithHealth] = useState(false);
  const [form, setForm] = useState({ name: '', host: 'localhost', port: 5121, scheme: 'http' });
  const [error, setError] = useState<string | null>(null);

  const services = useQuery({
    queryKey: ['gateway-services', withHealth],
    queryFn: async () => parseMany<GatewayService>(await gatewayApi.services(withHealth)),
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['gateway-services'] });

  const add = useMutation({
    mutationFn: () => gatewayApi.addService({ ...form, port: Number(form.port) }),
    onSuccess: refresh,
    onError: (e: Error) => setError(e.message),
  });
  const remove = useMutation({
    mutationFn: (id: string) => gatewayApi.deleteService(id),
    onSuccess: refresh,
    onError: (e: Error) => setError(e.message),
  });

  return (
    <div>
      <h1>Gateway Services ({(services.data ?? []).length} services)</h1>
      <label>
        <input type="checkbox" checked={withHealth} onChange={(e) => setWithHealth(e.target.checked)} />
        Check reachability
      </label>
      {services.isLoading && <p>Loading services…</p>}
      <ul>
        {(services.data ?? []).map((s) => (
          <li key={s.id}>
            {s.name} — {s.scheme}://{s.host}:{s.port}
            {s.health && ` (${s.health})`}
            <button onClick={() => remove.mutate(s.id)}>Delete</button>
          </li>
        ))}
      </ul>
      <h2>Add service</h2>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setError(null);
          add.mutate();
        }}
      >
        <input placeholder="name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
        <input placeholder="host" value={form.host} onChange={(e) => setForm({ ...form, host: e.target.value })} />
        <input placeholder="port" type="number" value={form.port} onChange={(e) => setForm({ ...form, port: Number(e.target.value) })} />
        <select value={form.scheme} onChange={(e) => setForm({ ...form, scheme: e.target.value })}>
          <option value="http">http</option>
          <option value="https">https</option>
        </select>
        <button type="submit" disabled={add.isPending}>Add</button>
      </form>
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
