import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { SessionProvider, useSession } from './session';
import { TenantLayout } from './layout';
import { useBootstrap } from './useBootstrap';
import { RemoteRoute } from '../components/RemoteRoute';
import { LoginPage } from '../pages/LoginPage';
import { HomePage } from '../pages/HomePage';
import { UsersPage } from '../pages/UsersPage';
import { UserDetailPage } from '../pages/UserDetailPage';
import { RolesPage } from '../pages/RolesPage';
import { RoleDetailPage } from '../pages/RoleDetailPage';
import { PermissionsPage } from '../pages/PermissionsPage';
import { ProfilePage } from '../pages/ProfilePage';

const queryClient = new QueryClient();

function RequireAuth({ children }: { children: React.ReactNode }) {
  const { session } = useSession();
  const location = useLocation();
  if (!session.loggedIn) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  return <>{children}</>;
}

function AuthedApp() {
  const { modules } = useBootstrap();
  return (
    <TenantLayout modules={modules}>
      <Routes>
        <Route path="/" element={<HomePage modules={modules} />} />
        <Route path="/users" element={<UsersPage />} />
        <Route path="/users/:id" element={<UserDetailPage />} />
        <Route path="/roles" element={<RolesPage />} />
        <Route path="/roles/:id" element={<RoleDetailPage />} />
        <Route path="/permissions" element={<PermissionsPage />} />
        <Route path="/profile" element={<ProfilePage />} />
        {modules.flatMap((m) =>
          m.routes.map((r) => (
            <Route
              key={`${m.moduleId}:${r.path}`}
              path={r.path}
              element={<RemoteRoute moduleId={m.moduleId} scope={m.scope} remoteUrl={m.remoteUrl} exposes={r.exposes} />}
            />
          )),
        )}
      </Routes>
    </TenantLayout>
  );
}

function AdminRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="*"
        element={
          <RequireAuth>
            <AuthedApp />
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
          <AdminRoutes />
        </SessionProvider>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
