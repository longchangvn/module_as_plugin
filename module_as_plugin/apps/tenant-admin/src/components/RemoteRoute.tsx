import { lazy, Suspense, useMemo } from 'react';
import { ModuleErrorBoundary } from './ModuleErrorBoundary';
import { Skeleton } from './ui/skeleton';
import { loadRemoteComponent } from '@/lib/remotes';

interface RemoteRouteProps {
  moduleId: string;
  scope: string;
  remoteUrl: string;
  exposes: string;
}

export function RemoteRoute({ moduleId, scope, remoteUrl, exposes }: RemoteRouteProps) {
  const Remote = useMemo(
    () => lazy(() => loadRemoteComponent(scope, remoteUrl, exposes).then((C) => ({ default: C }))),
    [scope, remoteUrl, exposes],
  );

  return (
    <ModuleErrorBoundary key={moduleId} moduleId={moduleId}>
      <Suspense
        fallback={
          <div className="space-y-3" aria-busy="true">
            <p className="text-sm text-muted-foreground">Loading module…</p>
            <Skeleton className="h-8 w-48" />
            <Skeleton className="h-32 w-full" />
          </div>
        }
      >
        <Remote />
      </Suspense>
    </ModuleErrorBoundary>
  );
}
