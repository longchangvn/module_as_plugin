import { useEffect, useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { registerModule } from '../lib/bootstrap';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';

// VALIDATION_ERROR details come as [{ field, message }] or a { field: message } map.
function validationMessages(e: unknown): string[] {
  const details = (e as { details?: unknown }).details;
  if (Array.isArray(details))
    return details.map((d) => {
      if (typeof d === 'string') return d;
      const { field, message } = (d ?? {}) as { field?: string; message?: string };
      return field ? `${field}: ${message ?? 'invalid'}` : (message ?? 'invalid');
    });
  if (details && typeof details === 'object') {
    const obj = details as Record<string, unknown>;
    if (typeof obj.field === 'string')
      return [`${obj.field}: ${typeof obj.message === 'string' ? obj.message : (e as Error).message}`];
    return Object.entries(obj).map(([k, v]) => `${k}: ${Array.isArray(v) ? v.join(', ') : String(v)}`);
  }
  return [];
}

// Registers a module version from its manifest.yml + artifact URLs (Admin only).
export function RegisterModuleDialog({
  open,
  onOpenChange,
  onRegistered,
}: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  onRegistered: (result: { id: string; version: string }) => void;
}) {
  const [manifestYaml, setManifestYaml] = useState('');
  const [apiUrl, setApiUrl] = useState('');
  const [remoteUrl, setRemoteUrl] = useState('');
  const [error, setError] = useState<{ title: string; items: string[] } | null>(null);

  useEffect(() => {
    if (!open) return;
    setManifestYaml('');
    setApiUrl('');
    setRemoteUrl('');
    setError(null);
  }, [open]);

  const save = useMutation({
    mutationFn: () =>
      registerModule({
        manifestYaml,
        artifactApiUrl: apiUrl.trim(),
        artifactRemoteUrl: remoteUrl.trim() || undefined,
      }),
    onSuccess: (result) => onRegistered(result),
    onError: (e: Error) => {
      const code = (e as Error & { code?: string }).code;
      setError({ title: code ? `${code}: ${e.message}` : e.message, items: validationMessages(e) });
    },
  });

  const readFile = async (file: File | undefined) => {
    if (!file) return;
    setManifestYaml(await file.text());
  };

  const urlValid = (v: string) => /^https?:\/\/\S+$/.test(v.trim());
  const canSubmit =
    manifestYaml.trim().length > 0 &&
    urlValid(apiUrl) &&
    (!remoteUrl.trim() || urlValid(remoteUrl)) &&
    !save.isPending;

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!save.isPending) onOpenChange(v); }}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Register module</DialogTitle>
          <DialogDescription>
            Upload or paste the module's manifest.yml and point at its deployed API (and optional UI remote).
          </DialogDescription>
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            if (!canSubmit) return;
            setError(null);
            save.mutate();
          }}
        >
          <div className="space-y-1.5">
            <Label htmlFor="register-manifest">manifest.yml *</Label>
            <Input
              id="register-manifest-file"
              type="file"
              accept=".yml,.yaml"
              aria-label="Upload manifest.yml"
              className="file:mr-3 file:border-0 file:bg-transparent file:text-sm file:font-medium"
              onChange={(e) => readFile(e.target.files?.[0])}
            />
            <textarea
              id="register-manifest"
              value={manifestYaml}
              onChange={(e) => setManifestYaml(e.target.value)}
              placeholder={'id: rewards\nversion: 1.0.0\n…'}
              rows={12}
              spellCheck={false}
              className="flex min-h-[200px] w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-xs ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
            />
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="register-api-url">Module API URL *</Label>
              <Input
                id="register-api-url"
                value={apiUrl}
                onChange={(e) => setApiUrl(e.target.value)}
                placeholder="http://localhost:5202"
                required
              />
              {apiUrl.trim().length > 0 && !urlValid(apiUrl) && (
                <p className="text-xs text-destructive" role="alert">Must be an http(s) URL.</p>
              )}
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="register-remote-url">Remote URL</Label>
              <Input
                id="register-remote-url"
                value={remoteUrl}
                onChange={(e) => setRemoteUrl(e.target.value)}
                placeholder="http://localhost:5174/remoteEntry.js"
              />
              {remoteUrl.trim().length > 0 && !urlValid(remoteUrl) && (
                <p className="text-xs text-destructive" role="alert">Must be an http(s) URL.</p>
              )}
            </div>
          </div>
          {error && (
            <Alert variant="destructive">
              <AlertTitle>{error.title}</AlertTitle>
              {error.items.length > 0 && (
                <AlertDescription>
                  <ul className="list-disc space-y-0.5 pl-4">
                    {error.items.map((m, i) => (
                      <li key={i}>{m}</li>
                    ))}
                  </ul>
                </AlertDescription>
              )}
            </Alert>
          )}
          <DialogFooter>
            <Button type="button" variant="outline" disabled={save.isPending} onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={!canSubmit}>
              {save.isPending ? 'Registering…' : 'Register'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
