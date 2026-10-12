import { BrowserRouter, Navigate, Route, Routes, useLocation } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SessionProvider, useSession } from "./session";
import { BackofficeLayout } from "./layout";
import { useBootstrap } from "./useBootstrap";
import type { BootstrapModule } from "../lib/bootstrap";
import { RemoteRoute } from "../components/RemoteRoute";
import { Alert, AlertDescription, AlertTitle } from "../components/ui/alert";
import { CatalogPage } from "../pages/CatalogPage";
import { TenantsPage } from "../pages/TenantsPage";
import { GatewayPage } from "../pages/GatewayPage";
import { LoginPage } from "../pages/LoginPage";
import { GatewayServicesPage } from "../pages/gateway/ServicesPage";
import { GatewayRoutesPage } from "../pages/gateway/RoutesPage";
import { GatewayTrafficPage } from "../pages/gateway/TrafficPage";

const queryClient = new QueryClient();

function RequireAuth({ children }: { children: React.ReactNode }) {
  const { session } = useSession();
  const location = useLocation();
  if (!session.loggedIn) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  return <>{children}</>;
}

// Authenticated area: sidebar layout is only rendered for logged-in sessions.
// Anonymous users see the chromeless login page.
function AuthedApp({ modules, bootstrapError, onRetryBootstrap }: {
  modules: BootstrapModule[];
  bootstrapError: string | null;
  onRetryBootstrap: () => void;
}) {
  return (
    <BackofficeLayout modules={modules}>
      {bootstrapError && (
        <Alert variant="destructive" className="mb-4">
          <AlertTitle>Failed to load installed modules</AlertTitle>
          <AlertDescription>
            Module pages may be unavailable. {bootstrapError}{' '}
            <button className="underline underline-offset-4" onClick={onRetryBootstrap}>
              Retry
            </button>
          </AlertDescription>
        </Alert>
      )}
      <Routes>
        <Route path="/" element={<CatalogPage />} />
        <Route path="/modules" element={<CatalogPage />} />
        <Route path="/tenants" element={<TenantsPage />} />
        <Route path="/gateway" element={<GatewayPage />} />
        <Route path="/gateway/services" element={<GatewayServicesPage />} />
        <Route path="/gateway/routes" element={<GatewayRoutesPage />} />
        <Route path="/gateway/traffic" element={<GatewayTrafficPage />} />
        {modules.flatMap((m) =>
          m.routes.map((r) => (
            <Route
              key={`${m.moduleId}:${r.path}`}
              path={r.path}
              element={
                <RemoteRoute moduleId={m.moduleId} scope={m.scope} remoteUrl={m.remoteUrl} exposes={r.exposes} />
              }
            />
          )),
        )}
      </Routes>
    </BackofficeLayout>
  );
}

function ShellRoutes() {
  const { modules, error: bootstrapError, retry: retryBootstrap } = useBootstrap();

  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="*"
        element={
          <RequireAuth>
            <AuthedApp modules={modules} bootstrapError={bootstrapError} onRetryBootstrap={retryBootstrap} />
          </RequireAuth>
        }
      />
    </Routes>
  );
}

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <SessionProvider>
          <ShellRoutes />
        </SessionProvider>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
