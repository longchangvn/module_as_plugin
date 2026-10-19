import { api } from './api-client';

// Bootstrap payload (SR-6b): Vite-only fields for installed+enabled modules.
// Tenant Admin registers Federation remotes + builds nav from this — never manifest.yml (R10).
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
