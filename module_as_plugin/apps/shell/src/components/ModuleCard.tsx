import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api-client';
import type { CatalogItem } from '../lib/bootstrap';

interface ModuleDetail {
  installState: string;
  installedVersion: string | null;
  versions: string[];
  installPlan: { module: string; version: string }[];
}

// One module card: badge + lifecycle actions (install ⇄ enable/disable → uninstall, upgrade).
export function ModuleCard({ module, tenantId, onChanged }: { module: CatalogItem; tenantId: string | null; onChanged: () => void }) {
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [targetVersion, setTargetVersion] = useState('');

  // Platform session acts on the picked tenant via platform endpoints.
  const platformTid = tenantId ?? sessionStorage.getItem('catalog_tenant') ?? '';
  const base = tenantId ? '/api/v1/tenant/modules' : `/api/v1/platform/tenants/${platformTid}/modules`;

  const detail = useQuery({
    queryKey: ['module-detail', module.id, tenantId],
    queryFn: () => api<ModuleDetail>(`${base}/${module.id}`),
    enabled: Boolean(tenantId), // plan preview is a tenant-endpoint feature
    retry: false,
  });

  const mutate = useMutation({
    mutationFn: async (action: { path: string; method: string; body?: unknown }) => {
      setError(null);
      return api(action.path, { method: action.method, body: JSON.stringify(action.body ?? {}) });
    },
    onSuccess: () => {
      onChanged();
      queryClient.invalidateQueries({ queryKey: ['module-detail', module.id] });
    },
    onError: (e: Error) => setError(e.message),
  });

  const busy = mutate.isPending;
  const state = module.installState.toUpperCase();
  const installed = state === 'INSTALLED';
  const disabled = state === 'DISABLED';

  return (
    <div>
      <h2>{module.displayName}</h2>
      <p>{module.description}</p>
      <span>{module.installState}</span>
      {detail.data && (
        <p>Plan: {detail.data.installPlan.map((s) => `${s.module}@${s.version}`).join(' → ') || 'nothing to install'}</p>
      )}
      <div>
        {state === 'NOT_INSTALLED' && (
          <button disabled={busy || (!tenantId && !platformTid)} onClick={() => mutate.mutate({ path: `${base}/${module.id}/install`, method: 'POST' })}>
            Install
          </button>
        )}
        {disabled && (
          <button disabled={busy} onClick={() => mutate.mutate({ path: `${base}/${module.id}/enable`, method: 'POST' })}>
            Enable
          </button>
        )}
        {installed && (
          <button disabled={busy} onClick={() => mutate.mutate({ path: `${base}/${module.id}/disable`, method: 'POST' })}>
            Disable
          </button>
        )}
        {(installed || disabled) && (
          <>
            <button disabled={busy} onClick={() => mutate.mutate({ path: `${base}/${module.id}/uninstall`, method: 'POST' })}>
              Uninstall
            </button>
            <select value={targetVersion} onChange={(e) => setTargetVersion(e.target.value)}>
              <option value="">Upgrade to…</option>
              {(detail.data?.versions ?? []).filter((v) => v !== detail.data?.installedVersion).map((v) => (
                <option key={v} value={v}>{v}</option>
              ))}
            </select>
            <button
              disabled={busy || !targetVersion}
              onClick={() => mutate.mutate({ path: `${base}/${module.id}/upgrade`, method: 'POST', body: { toVersion: targetVersion } })}
            >
              Upgrade
            </button>
          </>
        )}
      </div>
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
