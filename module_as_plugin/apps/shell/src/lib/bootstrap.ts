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
  installedVersion?: string | null;
  versions?: string[];
  error?: string | null;
}

export function fetchCatalog(): Promise<CatalogItem[]> {
  return api<CatalogItem[]>('/api/v1/tenant/modules');
}

export interface RegistryModule {
  id: string;
  displayName: string;
  description: string;
  owner: string;
  icon: string | null;
  category: string | null;
  status: 'Available' | 'Deprecated';
  latestVersion: string | null;
  versions: string[]; // newest first
  dependencies: { name: string; versionRange: string }[];
  installedTenantCount: number;
}

export type ModuleInstallState = 'INSTALLING' | 'INSTALLED' | 'DISABLED' | 'FAILED' | 'UNINSTALLING';

export interface ModuleInstall {
  tenantId: string;
  tenantName: string;
  state: ModuleInstallState;
  version: string;
  enabled: boolean;
}

export interface TenantInstall {
  tenantId: string;
  moduleId: string;
  version: string;
  state: string | number; // backend serializes InstallState enum as a number
  enabled: boolean;
  installedAt: string;
  updatedAt: string;
  error: string | null;
}

// InstallState enum order (Platform.Domain):
// NotInstalled, Installing, Installed, Disabled, Failed, Uninstalling.
const INSTALL_STATE_LABELS = ['NOT_INSTALLED', 'INSTALLING', 'INSTALLED', 'DISABLED', 'FAILED', 'UNINSTALLING'];

export function installStateLabel(state: string | number): string {
  if (typeof state === 'number') return INSTALL_STATE_LABELS[state] ?? String(state);
  return String(state).toUpperCase();
}

export function fetchRegistry(): Promise<RegistryModule[]> {
  return api<RegistryModule[]>('/api/v1/platform/modules');
}

export function fetchModuleInstalls(id: string): Promise<ModuleInstall[]> {
  return api<ModuleInstall[]>(`/api/v1/platform/modules/${id}/installs`);
}

export function fetchTenantInstalls(tenantId: string): Promise<TenantInstall[]> {
  return api<TenantInstall[]>(`/api/v1/platform/tenants/${tenantId}/modules`);
}

export interface RegisterModuleBody {
  manifestYaml: string;
  artifactApiUrl: string;
  artifactRemoteUrl?: string;
}

export function registerModule(body: RegisterModuleBody): Promise<{ id: string; version: string }> {
  return api<{ id: string; version: string }>('/api/v1/platform/modules', { method: 'POST', body: JSON.stringify(body) });
}

export function installToTenant(tenantId: string, moduleId: string, version?: string): Promise<unknown> {
  return api(`/api/v1/platform/tenants/${tenantId}/modules/${moduleId}/install`, {
    method: 'POST',
    body: JSON.stringify(version ? { version } : {}),
  });
}

// Platform-session tenant view: the tenant's install rows (not-installed excluded)
// joined with the registry for display fields and upgrade versions.
export async function fetchPlatformCatalog(tenantId: string): Promise<CatalogItem[]> {
  const [modules, installs] = await Promise.all([fetchRegistry(), fetchTenantInstalls(tenantId)]);
  return installs
    .map((i) => ({ install: i, state: installStateLabel(i.state) }))
    .filter(({ state }) => state !== 'NOT_INSTALLED')
    .map(({ install, state }) => {
      const m = modules.find((r) => r.id === install.moduleId);
      return {
        id: install.moduleId,
        displayName: m?.displayName ?? install.moduleId,
        description: m?.description ?? '',
        owner: m?.owner ?? '',
        installState: state,
        installedVersion: install.version,
        versions: m?.versions ?? [],
        error: install.error,
      };
    });
}

export function installModule(id: string): Promise<unknown> {
  return api(`/api/v1/tenant/modules/${id}/install`, { method: 'POST', body: JSON.stringify({}) });
}
