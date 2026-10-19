import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { KeyRound, Save } from 'lucide-react';
import { api } from '../lib/api-client';
import { PageHeader } from '@/components/page-header';
import { StatusBadge } from '@/components/status-badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

interface PlatformProfile {
  id: string;
  email: string;
  name: string;
  role: string;
  isActive: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  updatedAt: string;
}

function apiError(e: unknown): string {
  return e instanceof Error ? e.message : 'Request failed.';
}

function fmt(v: string | null | undefined): string {
  if (!v) return '—';
  const d = new Date(v);
  return Number.isNaN(d.getTime()) ? v : d.toLocaleString();
}

// Profile of the logged-in platform operator: name (editable), email
// readonly, plus related info. Password changes via the action button.
export function ProfilePage() {
  const queryClient = useQueryClient();
  const profile = useQuery({ queryKey: ['platform-profile'], queryFn: () => api<PlatformProfile>('/api/v1/platform/auth/me') });
  const [name, setName] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [pwOpen, setPwOpen] = useState(false);

  const data = profile.data;
  const draft = name ?? data?.name ?? '';
  const dirty = data != null && draft.trim() !== data.name && draft.trim().length > 0;

  const save = useMutation({
    mutationFn: () => api<PlatformProfile>('/api/v1/platform/auth/me', { method: 'PATCH', body: JSON.stringify({ name: draft.trim() }) }),
    onSuccess: () => { setSaveError(null); setSaved(true); setName(null); queryClient.invalidateQueries({ queryKey: ['platform-profile'] }); },
    onError: (e) => { setSaveError(apiError(e)); setSaved(false); },
  });

  return (
    <div>
      <PageHeader
        crumbs={[{ label: 'Profile' }]}
        title="Profile"
        description="Your platform operator account."
        actions={
          <Button variant="outline" onClick={() => setPwOpen(true)}>
            <KeyRound className="h-4 w-4" aria-hidden />
            Change password
          </Button>
        }
      />
      {profile.isLoading ? (
        <div className="space-y-2" aria-label="Loading profile">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-40 w-full" />
        </div>
      ) : profile.isError || !data ? (
        <Alert variant="destructive">
          <AlertDescription>
            Failed to load profile.{' '}
            <button className="underline underline-offset-4" onClick={() => profile.refetch()}>
              Retry
            </button>
          </AlertDescription>
        </Alert>
      ) : (
        <div className="grid gap-4 lg:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle>Account</CardTitle>
              <CardDescription>Email is managed by an administrator and cannot be changed here.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-1.5">
                <Label htmlFor="profile-name">Name</Label>
                <Input id="profile-name" value={draft} onChange={(e) => { setName(e.target.value); setSaved(false); }} maxLength={200} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="profile-email">Email (readonly)</Label>
                <Input id="profile-email" value={data.email} readOnly disabled aria-readonly />
              </div>
              {saveError && (
                <Alert variant="destructive">
                  <AlertDescription>{saveError}</AlertDescription>
                </Alert>
              )}
              {saved && (
                <Alert>
                  <AlertDescription>Profile saved.</AlertDescription>
                </Alert>
              )}
              <Button disabled={!dirty || save.isPending} onClick={() => { setSaveError(null); save.mutate(); }}>
                <Save className="h-4 w-4" aria-hidden />
                {save.isPending ? 'Saving…' : 'Save name'}
              </Button>
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Details</CardTitle>
              <CardDescription>Related platform information for this account.</CardDescription>
            </CardHeader>
            <CardContent>
              <dl className="grid grid-cols-3 gap-x-4 gap-y-3 text-sm">
                <dt className="text-muted-foreground">Role</dt>
                <dd className="col-span-2 font-medium">{data.role}</dd>
                <dt className="text-muted-foreground">Status</dt>
                <dd className="col-span-2"><StatusBadge status={data.isActive ? 'Active' : 'Inactive'} /></dd>
                <dt className="text-muted-foreground">User ID</dt>
                <dd className="col-span-2 font-mono text-xs">{data.id}</dd>
                <dt className="text-muted-foreground">Last login</dt>
                <dd className="col-span-2">{fmt(data.lastLoginAt)}</dd>
                <dt className="text-muted-foreground">Created</dt>
                <dd className="col-span-2">{fmt(data.createdAt)}</dd>
                <dt className="text-muted-foreground">Updated</dt>
                <dd className="col-span-2">{fmt(data.updatedAt)}</dd>
              </dl>
            </CardContent>
          </Card>
        </div>
      )}
      <ChangePasswordDialog open={pwOpen} onOpenChange={setPwOpen} />
    </div>
  );
}

function ChangePasswordDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (v: boolean) => void }) {
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  const change = useMutation({
    mutationFn: () =>
      api('/api/v1/platform/auth/change-password', {
        method: 'POST',
        body: JSON.stringify({ currentPassword: current, newPassword: next }),
      }),
    onSuccess: () => { setError(null); setDone(true); setCurrent(''); setNext(''); setConfirm(''); },
    onError: (e) => { setError(apiError(e)); setDone(false); },
  });

  const match = next === confirm;
  const valid = current.length > 0 && next.length >= 8 && match;

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v) { setError(null); setDone(false); } onOpenChange(v); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Change password</DialogTitle>
          <DialogDescription>New password must be at least 8 characters.</DialogDescription>
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={(e) => { e.preventDefault(); if (!valid || change.isPending) return; setError(null); change.mutate(); }}
        >
          <div className="space-y-1.5">
            <Label htmlFor="pw-current">Current password *</Label>
            <Input id="pw-current" type="password" value={current} onChange={(e) => setCurrent(e.target.value)} autoComplete="current-password" required />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="pw-new">New password *</Label>
            <Input id="pw-new" type="password" value={next} onChange={(e) => setNext(e.target.value)} autoComplete="new-password" required />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="pw-confirm">Confirm new password *</Label>
            <Input id="pw-confirm" type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} autoComplete="new-password" required />
          </div>
          {!match && confirm.length > 0 && (
            <Alert variant="destructive">
              <AlertDescription>New passwords do not match.</AlertDescription>
            </Alert>
          )}
          {error && (
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}
          {done && (
            <Alert>
              <AlertDescription>Password changed.</AlertDescription>
            </Alert>
          )}
          <DialogFooter>
            <Button type="submit" disabled={!valid || change.isPending}>
              {change.isPending ? 'Changing…' : 'Change password'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
