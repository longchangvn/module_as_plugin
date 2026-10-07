// API envelope: { data } on success, { error: { code, message, details } } on failure (§9).
export interface ApiError {
  code: string;
  message: string;
  details?: unknown;
  retryable?: boolean;
}

const GATEWAY_BASE = '';

function authHeaders(): HeadersInit {
  const token = sessionStorage.getItem('access_token'); // JWT in memory/session only, never localStorage token (§SR-9)
  return token ? { Authorization: `Bearer ${token}` } : {};
}

function tenantHeaders(): HeadersInit {
  const tenantId = sessionStorage.getItem('tenant_id');
  return tenantId ? { 'X-Tenant-Id': tenantId } : {};
}

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${GATEWAY_BASE}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...authHeaders(), ...tenantHeaders(), ...(init?.headers ?? {}) },
  });
  const body = await res.json().catch(() => ({}));
  if (!res.ok) {
    const err: ApiError = body?.error ?? { code: 'UNKNOWN', message: res.statusText };
    throw Object.assign(new Error(err.message), { code: err.code, details: err.details, status: res.status });
  }
  return (body?.data ?? body) as T;
}
