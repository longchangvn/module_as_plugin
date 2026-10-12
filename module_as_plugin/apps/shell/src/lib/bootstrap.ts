import { api } from './api-client';

// Bootstrap payload (SR-6b): Vite-only fields for installed+enabled modules.
// Shell registers Federation remotes + builds nav from this — never manifest.yml (R10).
export interface BootstrapRoute {
  path: string;
  exposes: string;
  requiredPermission: string | null;
  menuId?: string | null;
}

export interface BootstrapMenu {
  id: string;
  parentId: string | null;
  title: string;
  icon?: string | null;
  route?: string | null;
  requiredPermission: string | null;
  order: number;
}

export interface BootstrapModule {
  moduleId: string;
  version: string;
  scope: string;
  remoteUrl: string;
  modulePrefix: string;
  routes: BootstrapRoute[];
  menus: BootstrapMenu[];
  uiPermissions: string[];
}

export interface FrontendBootstrap {
  tenantId: string;
  modules: BootstrapModule[];
}

export function fetchBootstrap(): Promise<FrontendBootstrap> {
  return api<FrontendBootstrap>('/api/v1/tenant/frontend-bootstrap');
}

export interface CatalogItem {
  id: string;
  displayName: string;
  description: string;
  owner: string;
  installState: string;
}

export function fetchCatalog(): Promise<CatalogItem[]> {
  return api<CatalogItem[]>('/api/v1/tenant/modules');
}

interface RegistryItem {
  id: string;
  displayName: string;
  description: string;
  owner: string;
  latestVersion?: string;
}

interface InstallState {
  moduleId: string;
  state: string | number; // backend serializes InstallState enum as a number
}

// InstallState enum order (Platform.Domain):
// NotInstalled, Installing, Installed, Disabled, Failed, Uninstalling.
const INSTALL_STATE_LABELS = ['NOT_INSTALLED', 'INSTALLING', 'INSTALLED', 'DISABLED', 'FAILED', 'UNINSTALLING'];

function installStateLabel(state: string | number): string {
  if (typeof state === 'number') return INSTALL_STATE_LABELS[state] ?? String(state);
  return String(state).toUpperCase();
}

// Platform-session catalog: registry joined with one tenant's install states.
export async function fetchPlatformCatalog(tenantId: string): Promise<CatalogItem[]> {
  const [modules, installs] = await Promise.all([
    api<RegistryItem[]>('/api/v1/platform/modules'),
    api<InstallState[]>(`/api/v1/platform/tenants/${tenantId}/modules`),
  ]);
  return modules.map((m) => ({
    id: m.id,
    displayName: m.displayName,
    description: m.description,
    owner: m.owner,
    installState: installStateLabel(installs.find((i) => i.moduleId === m.id)?.state ?? 'NOT_INSTALLED'),
  }));
}

export function installModule(id: string): Promise<unknown> {
  return api(`/api/v1/tenant/modules/${id}/install`, { method: 'POST', body: JSON.stringify({}) });
}
