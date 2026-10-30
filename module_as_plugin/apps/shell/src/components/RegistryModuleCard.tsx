import { Download } from 'lucide-react';
import { moduleType, type ModuleType, type RegistryModule } from '../lib/bootstrap';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { StatusBadge } from '@/components/status-badge';

const TYPE_BADGE: Record<ModuleType, { variant: 'default' | 'secondary' | 'outline'; title: string }> = {
  plugin: { variant: 'default', title: 'Plugin module (hosted in-process by the platform)' },
  hybrid: { variant: 'secondary', title: 'Hybrid module (UI hosted by the platform, API on a deployed service)' },
  remote: { variant: 'outline', title: 'Remote module (deployed service)' },
};

// Registry card (all-modules view): metadata, dependencies, install count,
// and the multi-tenant install entry point for Admins.
export function RegistryModuleCard({
  module,
  canManage,
  onInstall,
}: {
  module: RegistryModule;
  canManage: boolean;
  onInstall: () => void;
}) {
  const deprecated = module.status === 'Deprecated';
  const count = module.installedTenantCount;
  const type = moduleType(module);

  return (
    <Card>
      <CardHeader className="pb-3">
        <div className="flex items-start justify-between gap-2">
          <div>
            <CardTitle className="flex items-center gap-2 text-base">
              {module.displayName}
              {module.latestVersion && <Badge variant="secondary">v{module.latestVersion}</Badge>}
              <Badge variant={TYPE_BADGE[type].variant} title={TYPE_BADGE[type].title}>
                {type}
              </Badge>
            </CardTitle>
            <CardDescription className="mt-1">
              {module.description} · Owner: {module.owner}
            </CardDescription>
          </div>
          <StatusBadge status={module.status} />
        </div>
      </CardHeader>
      <CardContent className="space-y-2 text-sm text-muted-foreground">
        <p>
          {module.dependencies.length > 0 ? (
            <>
              Depends on:{' '}
              <span className="text-foreground">
                {module.dependencies.map((d) => `${d.name} ${d.versionRange}`).join(', ')}
              </span>
            </>
          ) : (
            'No dependencies'
          )}
        </p>
        <p>
          Installed in {count} {count === 1 ? 'tenant' : 'tenants'}
        </p>
      </CardContent>
      {canManage && (
        <CardFooter>
          <span title={deprecated ? "Deprecated modules can't be installed" : undefined}>
            <Button size="sm" disabled={deprecated} onClick={onInstall}>
              <Download className="h-4 w-4" aria-hidden />
              Install to tenants…
            </Button>
          </span>
        </CardFooter>
      )}
    </Card>
  );
}
