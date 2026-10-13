// Tenant-only session. JWT in sessionStorage only, never localStorage (§SR-9).
// There is deliberately no platform login here and no link to it.
const TOKEN = 'access_token';
const TENANT_ID = 'tenant_id';
const USER_ID = 'user_id';

export interface TenantLoginResponse {
  accessToken: string;
  userId: string;
  tenantAdmin: boolean;
  tenantId: string;
}

async function postJson<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const data = (await res.json().catch(() => ({}))) as {
    data?: T;
    error?: { code?: string; message?: string };
  };
  if (!res.ok) throw new Error(data?.error?.message ?? res.statusText);
  return data.data as T;
}

export async function tenantLogin(tenant: string, email: string, password: string): Promise<TenantLoginResponse> {
  const data = await postJson<TenantLoginResponse>('/api/v1/auth/login', { tenantId: tenant, email, password });
  sessionStorage.setItem(TOKEN, data.accessToken);
  // Backend resolves name/slug/id to the opaque id — store the resolved id
  // so tenant-scoped calls use the canonical value.
  sessionStorage.setItem(TENANT_ID, data.tenantId ?? tenant);
  sessionStorage.setItem(USER_ID, data.userId);
  sessionStorage.setItem('tenant_admin', data.tenantAdmin ? 'true' : 'false');
  return data;
}

export function logout(): void {
  sessionStorage.removeItem(TOKEN);
  sessionStorage.removeItem(TENANT_ID);
  sessionStorage.removeItem(USER_ID);
  sessionStorage.removeItem('tenant_admin');
}
