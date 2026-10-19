// Runs inside the host page (same origin as Tenant Admin): reuse its session
// (JWT in sessionStorage, SR-9) and call the gateway via relative /api/v1 paths.
const FRIENDLY: Record<string, string> = {
  MODULE_DISABLED: 'This module is disabled for your tenant.',
  UNKNOWN_MODULE_ROUTE: 'This module is not available (not installed).',
};

export async function call<T>(path: string): Promise<T> {
  let res: Response;
  try {
    res = await fetch(path, {
      headers: {
        Authorization: `Bearer ${sessionStorage.getItem('access_token') ?? ''}`,
        'X-Tenant-Id': sessionStorage.getItem('tenant_id') ?? '',
      },
    });
  } catch {
    throw new Error('Service unreachable.');
  }
  if (res.status === 401) {
    window.location.assign('/login');
    throw new Error('Session expired.');
  }
  const body = await res.json().catch(() => ({}));
  if (!res.ok) {
    const code: string | undefined = body?.error?.code;
    const message: string = (code && FRIENDLY[code]) ?? body?.error?.message ?? `HTTP ${res.status}`;
    throw new Error(code ? `${code}: ${message}` : message);
  }
  return body.data as T;
}
