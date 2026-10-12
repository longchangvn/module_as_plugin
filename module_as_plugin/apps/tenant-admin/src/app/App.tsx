import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { SessionProvider, useSession } from './session';
import { TenantLayout } from './layout';
import { LoginPage } from '../pages/LoginPage';
import { HomePage } from '../pages/HomePage';
import { UsersPage } from '../pages/UsersPage';
import { UserDetailPage } from '../pages/UserDetailPage';
import { RolesPage } from '../pages/RolesPage';
import { RoleDetailPage } from '../pages/RoleDetailPage';
import { PermissionsPage } from '../pages/PermissionsPage';

const queryClient = new QueryClient();

function RequireAuth({ children }: { children: React.ReactNode }) {
  const { session } = useSession();
  const location = useLocation();
  if (!session.loggedIn) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  return <>{children}</>;
}

function AdminRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="*"
        element={
          <RequireAuth>
            <TenantLayout>
              <Routes>
                <Route path="/" element={<HomePage />} />
                <Route path="/users" element={<UsersPage />} />
                <Route path="/users/:id" element={<UserDetailPage />} />
                <Route path="/roles" element={<RolesPage />} />
                <Route path="/roles/:id" element={<RoleDetailPage />} />
                <Route path="/permissions" element={<PermissionsPage />} />
              </Routes>
            </TenantLayout>
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
