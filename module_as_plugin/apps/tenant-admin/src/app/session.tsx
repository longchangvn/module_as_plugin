import { createContext, useCallback, useContext, useMemo, useState } from 'react';
import { logout as clearSession } from '@/lib/auth';

export interface Session {
  loggedIn: boolean;
  tenantId: string | null;
  userId: string | null;
  tenantAdmin: boolean;
  label: string | null;
}

function readSession(): Session {
  const token = sessionStorage.getItem('access_token');
  const tenantId = sessionStorage.getItem('tenant_id');
  const userId = sessionStorage.getItem('user_id');
  if (!token || !tenantId) return { loggedIn: false, tenantId: null, userId: null, tenantAdmin: false, label: null };
  return {
    loggedIn: true,
    tenantId,
    userId,
    tenantAdmin: sessionStorage.getItem('tenant_admin') === 'true',
    label: `Tenant · ${tenantId}`,
  };
}

const SessionContext = createContext<{ session: Session; refresh: () => void; logout: () => void }>({
  session: readSession(),
  refresh: () => {},
  logout: () => {},
});

export function SessionProvider({ children }: { children: React.ReactNode }) {
  const [session, setSession] = useState<Session>(readSession);
  const refresh = useCallback(() => setSession(readSession()), []);
  const logout = useCallback(() => {
    clearSession();
    setSession(readSession());
  }, []);
  const value = useMemo(() => ({ session, refresh, logout }), [session, refresh, logout]);
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession() {
  return useContext(SessionContext);
}
