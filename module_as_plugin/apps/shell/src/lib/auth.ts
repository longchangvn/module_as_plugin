// Auth storage: JWT in sessionStorage only (memory/session, never localStorage — §SR-9).
// Two sessions: platform (Admin/Maintainer, no tenant) and tenant (tenantId + tenant_admin).
const PLATFORM_TOKEN = 'access_token';
const TENANT_TOKEN = 'tenant_access_token';

export interface PlatformLoginResponse {
  accessToken: string;
  role: string;
}

export interface TenantLoginResponse {
  accessToken: string;
  userId: string;
  tenantAdmin: boolean;
}

async function postJson<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw Object.assign(new Error(data?.error?.message ?? res.statusText), { code: data?.error?.code });
  return data.data as T;
}

export async function platformLogin(email: string, password: string): Promise<void> {
  const data = await postJson<PlatformLoginResponse>('/api/v1/platform/auth/login', { email, password });
  sessionStorage.setItem(PLATFORM_TOKEN, data.accessToken);
  sessionStorage.setItem('platform_role', data.role);
  sessionStorage.removeItem('tenant_id');
}

export async function tenantLogin(tenantId: string, email: string, password: string): Promise<void> {
  const data = await postJson<TenantLoginResponse>('/api/v1/auth/login', { tenantId, email, password });
  sessionStorage.setItem(TENANT_TOKEN, data.accessToken);
  sessionStorage.setItem('tenant_id', tenantId);
  // api-client sends PLATFORM_TOKEN as Bearer; for tenant calls swap it in.
  sessionStorage.setItem(PLATFORM_TOKEN, data.accessToken);
}

export function logout(): void {
  sessionStorage.removeItem(PLATFORM_TOKEN);
  sessionStorage.removeItem(TENANT_TOKEN);
  sessionStorage.removeItem('tenant_id');
  sessionStorage.removeItem('platform_role');
}

export function platformRole(): string | null {
  return sessionStorage.getItem('platform_role');
}
