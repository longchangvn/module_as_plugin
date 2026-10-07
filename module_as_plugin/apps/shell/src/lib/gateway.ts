import { api } from './api-client';

// Gateway admin client (ocelot_gateway_admin.md §9). Reads need any auth;
// writes need platform Admin (or dev API key in Development).
export interface GatewayService {
  id: string;
  name: string;
  host: string;
  port: number;
  scheme: string;
  description?: string;
  enabled: boolean;
  health?: string;
}

export interface GatewayRoute {
  Key?: string;
  UpstreamPathTemplate: string;
  DownstreamPathTemplate: string;
  UpstreamHttpMethod: string[];
  Priority: number;
  Metadata?: Record<string, string>;
}

export interface GatewaySummary {
  serviceName: string;
  environment: string;
  serviceCount: number;
  routeCount: number;
  totalRequests: number;
}

export const gatewayApi = {
  summary: () => api<GatewaySummary>('/api/v1/gateway/summary'),
  services: (health = false) => api<string[]>(`/api/v1/gateway/services?health=${health}`),
  addService: (body: { name: string; host: string; port: number; scheme: string; description?: string }) =>
    api<{ id: string }>('/api/v1/gateway/services', { method: 'POST', body: JSON.stringify(body) }),
  updateService: (id: string, body: { name: string; host: string; port: number; scheme: string; description?: string }) =>
    api(`/api/v1/gateway/services/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deleteService: (id: string) => api(`/api/v1/gateway/services/${id}`, { method: 'DELETE' }),
  routes: () => api<string[]>('/api/v1/gateway/routes'),
  addRoute: (body: { upstreamPathTemplate: string; downstreamPathTemplate: string; upstreamHttpMethod: string[]; priority: number; serviceId: string }) =>
    api<{ id: string }>('/api/v1/gateway/routes', { method: 'POST', body: JSON.stringify(body) }),
  updateRoute: (id: string, body: { upstreamPathTemplate: string; downstreamPathTemplate: string; upstreamHttpMethod: string[]; priority: number; serviceId: string }) =>
    api(`/api/v1/gateway/routes/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deleteRoute: (id: string, force = false) => api(`/api/v1/gateway/routes/${id}?force=${force}`, { method: 'DELETE' }),
  reload: () => api('/api/v1/gateway/config/reload', { method: 'POST' }),
  traffic: () => api<{ total: number; routes: { route: string; count: number }[] }>('/api/v1/gateway/traffic'),
  resetTraffic: () => api('/api/v1/gateway/traffic/reset', { method: 'POST' }),
};

// Services/routes come back as JSON-encoded strings (file-backed store); parse them here.
export function parseMany<T>(rows: string[]): T[] {
  return rows.map((r) => JSON.parse(r) as T);
}
