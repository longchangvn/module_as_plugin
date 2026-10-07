import { BrowserRouter, Link, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useBootstrap } from './useBootstrap';
import { RemoteRoute } from '../components/RemoteRoute';
import { CatalogPage } from '../pages/CatalogPage';
import { TenantsPage } from '../pages/TenantsPage';
import { GatewayPage } from '../pages/GatewayPage';
import { LoginPage } from '../pages/LoginPage';
import { GatewayServicesPage } from '../pages/gateway/ServicesPage';
import { GatewayRoutesPage } from '../pages/gateway/RoutesPage';
import { GatewayTrafficPage } from '../pages/gateway/TrafficPage';
import { logout } from '../lib/auth';

const queryClient = new QueryClient();

function ShellRoutes() {
  const { modules, loading, error } = useBootstrap();
  const loggedIn = Boolean(sessionStorage.getItem('access_token'));

  return (
    <div>
      <nav>
        <Link to="/login">Login</Link> | <Link to="/modules">Catalog</Link> | <Link to="/tenants">Tenants</Link> |{' '}
        <Link to="/gateway">Gateway</Link>
        {modules.flatMap((m) => m.menus.filter((menu) => menu.route).map((menu) => (
          <span key={menu.id}> | <Link to={menu.route as string}>{menu.title}</Link></span>
        )))}
        {loggedIn && (
          <button onClick={() => { logout(); window.location.href = '/login'; }}>Logout</button>
        )}
      </nav>
      {loading && <p>Loading modules…</p>}
      {error && <p role="alert">{error}</p>}
      <Routes>
        <Route path="/login" element={<LoginPage />} />
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
              element={<RemoteRoute moduleId={m.moduleId} scope={m.scope} remoteUrl={m.remoteUrl} exposes={r.exposes} />}
            />
          )),
        )}
      </Routes>
    </div>
  );
}

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <ShellRoutes />
      </BrowserRouter>
    </QueryClientProvider>
  );
}
