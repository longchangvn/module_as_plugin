import { useEffect, useRef, useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { registerModule, registerModulePackage, type ModuleType } from '../lib/bootstrap';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';

// Client-side guard only; the server enforces the authoritative limit (ModulePackages:MaxPackageBytes).
const MAX_PACKAGE_BYTES = 50 * 1024 * 1024;

type Mode = 'package' | 'manifest';

export interface RegisteredModule {
  id: string;
  version: string;
  type?: ModuleType;
}

// PACKAGE_INVALID / VALIDATION_ERROR details come as [{ field, message }] or a { field: message } map.
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

function toErrorState(e: Error) {
  const code = (e as Error & { code?: string }).code;
  return { title: code ? `${code}: ${e.message}` : e.message, items: validationMessages(e) };
}

function checkPackageFile(file: File | null): string | null {
  if (!file) return null;
  const isZip = /\.zip$/i.test(file.name);
  if (!isZip) return 'Only .zip packages are accepted.';
  if (file.size === 0) return 'The selected file is empty.';
  if (file.size > MAX_PACKAGE_BYTES) return 'Package is too large (maximum 50 MB).';
  return null;
}

function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

const urlValid = (v: string) => /^https?:\/\/\S+$/.test(v.trim());

// Registers a module version (Admin only) either from a .zip package (remote or plugin)
// or from a pasted manifest.yml + artifact URLs (remote modules only).
export function RegisterModuleDialog({
  open,
  onOpenChange,
  onRegistered,
}: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  onRegistered: (result: RegisteredModule) => void;
}) {
  const [mode, setMode] = useState<Mode>('package');
  // Package mode
  const [file, setFile] = useState<File | null>(null);
  const [pkgApiUrl, setPkgApiUrl] = useState('');
  const [pkgRemoteUrl, setPkgRemoteUrl] = useState('');
  const fileInputRef = useRef<HTMLInputElement>(null);
  // Manifest mode
  const [manifestYaml, setManifestYaml] = useState('');
  const [apiUrl, setApiUrl] = useState('');
  const [remoteUrl, setRemoteUrl] = useState('');
  const [error, setError] = useState<{ title: string; items: string[] } | null>(null);
  const submitting = useRef(false); // guards against double submit before isPending re-renders

  useEffect(() => {
    if (!open) return;
    setMode('package');
    setFile(null);
    setPkgApiUrl('');
    setPkgRemoteUrl('');
    if (fileInputRef.current) fileInputRef.current.value = '';
    setManifestYaml('');
    setApiUrl('');
    setRemoteUrl('');
    setError(null);
  }, [open]);

  const saveManifest = useMutation({
    mutationFn: () =>
      registerModule({
        manifestYaml,
        artifactApiUrl: apiUrl.trim(),
        artifactRemoteUrl: remoteUrl.trim() || undefined,
      }),
    onSuccess: (result) => onRegistered({ id: result.id, version: result.version }),
    onError: (e: Error) => setError(toErrorState(e)),
    onSettled: () => { submitting.current = false; },
  });

  const savePackage = useMutation({
    mutationFn: (pkg: File) =>
      registerModulePackage({
        file: pkg,
        artifactApiUrl: pkgApiUrl.trim() || undefined,
        artifactRemoteUrl: pkgRemoteUrl.trim() || undefined,
      }),
    onSuccess: (result) => onRegistered({ id: result.id, version: result.version, type: result.type }),
    onError: (e: Error) => setError(toErrorState(e)),
    onSettled: () => { submitting.current = false; },
  });

  const pending = saveManifest.isPending || savePackage.isPending;

  const readManifestFile = async (f: File | undefined) => {
    if (!f) return;
    setManifestYaml(await f.text());
  };

  const fileError = checkPackageFile(file);
  const canSubmitPackage =
    !!file &&
    !fileError &&
    (!pkgApiUrl.trim() || urlValid(pkgApiUrl)) &&
    (!pkgRemoteUrl.trim() || urlValid(pkgRemoteUrl)) &&
    !pending;
  const canSubmitManifest =
    manifestYaml.trim().length > 0 &&
    urlValid(apiUrl) &&
    (!remoteUrl.trim() || urlValid(remoteUrl)) &&
    !pending;
  const canSubmit = mode === 'package' ? canSubmitPackage : canSubmitManifest;

  const submit = () => {
    if (!canSubmit || submitting.current) return;
    submitting.current = true;
    setError(null);
    if (mode === 'package' && file) savePackage.mutate(file);
    else saveManifest.mutate();
  };

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!pending) onOpenChange(v); }}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Register module</DialogTitle>
          <DialogDescription>
            Upload a module package (.zip) — remote or plugin — or paste a remote module's manifest.yml.
          </DialogDescription>
        </DialogHeader>
        <form
          className="space-y-4"
          aria-busy={pending}
          onSubmit={(e) => {
            e.preventDefault();
            submit();
          }}
        >
          <Tabs
            value={mode}
            onValueChange={(v) => {
              if (pending) return;
              setMode(v as Mode);
              setError(null);
            }}
          >
            <TabsList aria-label="Registration method">
              <TabsTrigger value="package" disabled={pending}>Upload package (.zip)</TabsTrigger>
              <TabsTrigger value="manifest" disabled={pending}>Paste manifest</TabsTrigger>
            </TabsList>

            {/* forceMount keeps each tab's inputs (incl. the chosen file) intact when switching tabs */}
            <TabsContent value="package" forceMount className="space-y-4 data-[state=inactive]:hidden">
              <div className="space-y-1.5">
                <Label htmlFor="register-package-file">Module package (.zip) *</Label>
                <Input
                  ref={fileInputRef}
                  id="register-package-file"
                  type="file"
                  accept=".zip,application/zip"
                  disabled={pending}
                  aria-describedby="register-package-help"
                  aria-invalid={!!fileError}
                  className="file:mr-3 file:border-0 file:bg-transparent file:text-sm file:font-medium"
                  onChange={(e) => {
                    setFile(e.target.files?.[0] ?? null);
                    setError(null);
                  }}
                />
                <p id="register-package-help" className="text-xs text-muted-foreground">
                  {file && !fileError
                    ? `${file.name} · ${formatSize(file.size)}`
                    : 'Zip containing manifest.yml at its root (max 50 MB).'}
                </p>
                {fileError && (
                  <p className="text-xs text-destructive" role="alert">{fileError}</p>
                )}
              </div>
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-1.5">
                  <Label htmlFor="register-package-api-url">Module API URL (remote modules only)</Label>
                  <Input
                    id="register-package-api-url"
                    value={pkgApiUrl}
                    disabled={pending}
                    onChange={(e) => setPkgApiUrl(e.target.value)}
                    placeholder="Defaults to manifest service.baseUrl"
                  />
                  {pkgApiUrl.trim().length > 0 && !urlValid(pkgApiUrl) && (
                    <p className="text-xs text-destructive" role="alert">Must be an http(s) URL.</p>
                  )}
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="register-package-remote-url">Remote URL (remote modules only)</Label>
                  <Input
                    id="register-package-remote-url"
                    value={pkgRemoteUrl}
                    disabled={pending}
                    onChange={(e) => setPkgRemoteUrl(e.target.value)}
                    placeholder="http://localhost:5174/remoteEntry.js"
                  />
                  {pkgRemoteUrl.trim().length > 0 && !urlValid(pkgRemoteUrl) && (
                    <p className="text-xs text-destructive" role="alert">Must be an http(s) URL.</p>
                  )}
                </div>
              </div>
            </TabsContent>

            <TabsContent value="manifest" forceMount className="space-y-4 data-[state=inactive]:hidden">
              <div className="space-y-1.5">
                <Label htmlFor="register-manifest">manifest.yml *</Label>
                <Input
                  id="register-manifest-file"
                  type="file"
                  accept=".yml,.yaml"
                  aria-label="Upload manifest.yml"
                  disabled={pending}
                  className="file:mr-3 file:border-0 file:bg-transparent file:text-sm file:font-medium"
                  onChange={(e) => readManifestFile(e.target.files?.[0])}
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
                <p className="text-xs text-muted-foreground">Plugin modules must be registered by package upload.</p>
              </div>
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-1.5">
                  <Label htmlFor="register-api-url">Module API URL *</Label>
                  <Input
                    id="register-api-url"
                    value={apiUrl}
                    onChange={(e) => setApiUrl(e.target.value)}
                    placeholder="http://localhost:5202"
                    required={mode === 'manifest'}
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
            </TabsContent>
          </Tabs>

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
            <Button type="button" variant="outline" disabled={pending} onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={!canSubmit}>
              {pending ? (mode === 'package' ? 'Uploading…' : 'Registering…') : mode === 'package' ? 'Upload & register' : 'Register'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
