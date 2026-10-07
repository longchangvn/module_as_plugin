import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api } from '../lib/api-client';

interface GatewaySummary {
  serviceCount: number;
  routeCount: number;
  totalRequests: number;
}

export function GatewayPage() {
  const summary = useQuery({
    queryKey: ['gateway-summary'],
    queryFn: () => api<GatewaySummary>('/api/v1/gateway/summary'),
    refetchInterval: 5000,
  });

  if (summary.isLoading) return <p>Loading gateway…</p>;
  if (summary.isError) return <p role="alert">Failed to load gateway summary.</p>;

  return (
    <div>
      <h1>Gateway</h1>
      <p>Services: {summary.data?.serviceCount}</p>
      <p>Routes: {summary.data?.routeCount}</p>
      <p>Requests: {summary.data?.totalRequests}</p>
      <nav>
        <Link to="/gateway/services">Services</Link> | <Link to="/gateway/routes">Routing</Link> |{' '}
        <Link to="/gateway/traffic">Traffic</Link>
      </nav>
    </div>
  );
}
