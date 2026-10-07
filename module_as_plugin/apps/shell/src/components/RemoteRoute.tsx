import { lazy, Suspense } from 'react';
import { ModuleErrorBoundary } from './ModuleErrorBoundary';

// Federation runtime container shape (injected by remoteEntry.js at runtime).
interface FederationContainer {
  init: (shareScope: unknown) => Promise<void>;
  get: (module: string) => Promise<() => { default: React.ComponentType }>;
}

declare const __webpack_share_scopes__: { default: unknown };

const defaultShareScope: unknown = typeof __webpack_share_scopes__ !== 'undefined' ? __webpack_share_scopes__.default : {};

// Lazy-load a Federation remote only when its route is visited (NFR2: never all upfront).
// Version-pinned remoteUrl comes from the bootstrap payload.
export function RemoteRoute({ moduleId, scope, remoteUrl, exposes }: { moduleId: string; scope: string; remoteUrl: string; exposes: string }) {
  void remoteUrl;
  const Remote = lazy(async () => {
    const container = (window as unknown as Record<string, FederationContainer>)[scope];
    if (!container) throw new Error(`Remote scope '${scope}' not loaded`);
    await container.init(defaultShareScope);
    const factory = await container.get(exposes);
    return factory();
  });

  return (
    <ModuleErrorBoundary moduleId={moduleId}>
      <Suspense fallback={<div>Loading module…</div>}>
        <Remote />
      </Suspense>
    </ModuleErrorBoundary>
  );
}
