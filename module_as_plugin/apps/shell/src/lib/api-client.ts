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

// Multipart upload: same auth/envelope/error handling as api(), but no Content-Type
// header so the browser sets the multipart boundary itself.
export async function apiUpload<T>(path: string, formData: FormData, init?: Omit<RequestInit, 'body'>): Promise<T> {
  const res = await fetch(`${GATEWAY_BASE}${path}`, {
    method: 'POST',
    ...init,
    body: formData,
    headers: { ...authHeaders(), ...tenantHeaders(), ...(init?.headers ?? {}) },
  });
  const body = await res.json().catch(() => ({}));
  if (!res.ok) {
    const fallback: ApiError =
      res.status === 413
        ? { code: 'PAYLOAD_TOO_LARGE', message: 'Package is too large' }
        : { code: 'UNKNOWN', message: res.statusText || `Request failed (${res.status})` };
    const err: ApiError = body?.error ?? fallback;
    throw Object.assign(new Error(err.message), { code: err.code, details: err.details, status: res.status });
  }
  return (body?.data ?? body) as T;
}
