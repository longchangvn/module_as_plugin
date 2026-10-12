// Backend shapes (ASP.NET camelCase JSON).
export interface TenantUser {
  id: string;
  email: string;
  name: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface Role {
  id: string;
  name: string;
  description?: string | null;
  moduleId?: string | null;
  isSystem: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface Permission {
  id: string;
  code: string;
  object: string;
  action: string;
  name: string;
  moduleId?: string | null;
  isMenuAccess: boolean;
  createdAt: string;
}

export function apiError(e: unknown): string {
  return e instanceof Error ? e.message : 'Request failed.';
}
