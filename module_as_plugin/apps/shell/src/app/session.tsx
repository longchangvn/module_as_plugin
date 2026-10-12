import { createContext, useCallback, useContext, useMemo, useState } from "react";
import { logout as clearSession } from "@/lib/auth";

export interface Session {
  loggedIn: boolean;
  platformRole: string | null;
  tenantId: string | null;
  /** Human label for the topbar, e.g. "Platform · Admin" or "Tenant · demo". */
  label: string | null;
}

function readSession(): Session {
  const token = sessionStorage.getItem("access_token");
  const platformRole = sessionStorage.getItem("platform_role");
  const tenantId = sessionStorage.getItem("tenant_id");
  if (!token) return { loggedIn: false, platformRole: null, tenantId: null, label: null };
  if (tenantId) return { loggedIn: true, platformRole, tenantId, label: `Tenant · ${tenantId}` };
  return { loggedIn: true, platformRole, tenantId: null, label: platformRole ? `Platform · ${platformRole}` : "Platform" };
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
