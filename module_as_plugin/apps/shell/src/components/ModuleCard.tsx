import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowUpToLine, Download, MoreVertical, Pause, Play, Trash2 } from 'lucide-react';
import { api } from '../lib/api-client';
import type { CatalogItem } from '../lib/bootstrap';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { StatusBadge } from '@/components/status-badge';

interface ModuleDetail {
  installState: string;
  installedVersion: string | null;
  versions: string[];
  installPlan: { module: string; version: string }[];
}

// One module card: status badge + lifecycle actions in a row menu.
// Install ⇄ enable/disable → uninstall, upgrade via dialog. (§6, §8)
export function ModuleCard({ module, tenantId, onChanged }: { module: CatalogItem; tenantId: string | null; onChanged: () => void }) {
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [upgradeOpen, setUpgradeOpen] = useState(false);
  const [uninstallOpen, setUninstallOpen] = useState(false);
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
  const canAct = Boolean(tenantId || platformTid);
  const upgradeOptions = (detail.data?.versions ?? []).filter((v) => v !== detail.data?.installedVersion);

  const run = (path: string, method: string, body?: unknown) => mutate.mutate({ path, method, body });

  return (
    <Card>
      <CardHeader className="pb-3">
        <div className="flex items-start justify-between gap-2">
          <div>
            <CardTitle className="text-base">{module.displayName}</CardTitle>
            <CardDescription className="mt-1">
              {module.description} · Owner: {module.owner}
            </CardDescription>
          </div>
          <StatusBadge status={module.installState} />
        </div>
      </CardHeader>
      <CardContent className="space-y-3 text-sm">
        {detail.data && (
          <p className="text-muted-foreground">
            Install plan:{' '}
            {detail.data.installPlan.length > 0 ? (
              <span className="text-foreground">
                {detail.data.installPlan.map((s) => `${s.module}@${s.version}`).join(' → ')}
              </span>
            ) : (
              'nothing to install'
            )}
          </p>
        )}
        {detail.data?.installedVersion && (
          <p className="text-muted-foreground">
            Installed version: <Badge variant="secondary">{detail.data.installedVersion}</Badge>
          </p>
        )}
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
      </CardContent>
      <CardFooter className="gap-2">
        {state === 'NOT_INSTALLED' && (
          <Button size="sm" disabled={busy || !canAct} onClick={() => run(`${base}/${module.id}/install`, 'POST')}>
            <Download className="h-4 w-4" aria-hidden />
            Install
          </Button>
        )}
        {disabled && (
          <Button size="sm" disabled={busy} onClick={() => run(`${base}/${module.id}/enable`, 'POST')}>
            <Play className="h-4 w-4" aria-hidden />
            Enable
          </Button>
        )}
        {installed && (
          <Button size="sm" variant="secondary" disabled={busy} onClick={() => run(`${base}/${module.id}/disable`, 'POST')}>
            <Pause className="h-4 w-4" aria-hidden />
            Disable
          </Button>
        )}
        {(installed || disabled) && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button size="sm" variant="outline" aria-label={`More actions for ${module.displayName}`}>
                <MoreVertical className="h-4 w-4" aria-hidden />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem
                disabled={busy || upgradeOptions.length === 0}
                onSelect={() => {
                  setTargetVersion('');
                  setUpgradeOpen(true);
                }}
              >
                <ArrowUpToLine className="h-4 w-4" aria-hidden />
                Upgrade…
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem
                disabled={busy}
                className="text-destructive focus:text-destructive"
                onSelect={() => setUninstallOpen(true)}
              >
                <Trash2 className="h-4 w-4" aria-hidden />
                Uninstall
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </CardFooter>

      <AlertDialog open={uninstallOpen} onOpenChange={setUninstallOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Uninstall {module.displayName}?</AlertDialogTitle>
            <AlertDialogDescription>
              The module is removed for this tenant and its routes and permissions are revoked.
              Data is archived per retention policy. This cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Cancel</AlertDialogCancel>
            <AlertDialogAction
              disabled={busy}
              className="bg-destructive text-destructive-foreground shadow-sm hover:bg-destructive/90"
              onClick={(e) => {
                e.preventDefault();
                run(`${base}/${module.id}/uninstall`, 'POST');
                setUninstallOpen(false);
              }}
            >
              {busy ? 'Working…' : 'Uninstall'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <Dialog open={upgradeOpen} onOpenChange={setUpgradeOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Upgrade {module.displayName}</DialogTitle>
            <DialogDescription>
              {detail.data?.installedVersion
                ? `Currently at ${detail.data.installedVersion}. Only newer versions are accepted.`
                : 'Choose a version to upgrade to.'}
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-1.5">
            <Label htmlFor={`upgrade-${module.id}`}>Target version</Label>
            <Select value={targetVersion} onValueChange={setTargetVersion}>
              <SelectTrigger id={`upgrade-${module.id}`}>
                <SelectValue placeholder="Select version…" />
              </SelectTrigger>
              <SelectContent>
                {upgradeOptions.map((v) => (
                  <SelectItem key={v} value={v}>
                    {v}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <DialogFooter>
            <Button
              disabled={busy || !targetVersion}
              onClick={() => {
                run(`${base}/${module.id}/upgrade`, 'POST', { toVersion: targetVersion });
                setUpgradeOpen(false);
              }}
            >
              {busy ? 'Upgrading…' : 'Upgrade'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Card>
  );
}
