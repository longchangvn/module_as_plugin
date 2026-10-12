import { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { LogIn, Puzzle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { platformLogin, tenantLogin } from '../lib/auth';
import { useSession } from '../app/session';

// Login: platform (Admin/Maintainer) or tenant session. Card-centered,
// one primary action, inline error feedback, values preserved on failure. (§5)
export function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { refresh } = useSession();
  const [mode, setMode] = useState<'platform' | 'tenant'>('platform');
  const [tenantId, setTenantId] = useState('tnt_demo');
  const [email, setEmail] = useState('admin@local');
  // Dev-seeded credentials prefilled (password verification is stubbed in v1,
  // see Program.cs P5 note) so login is one click in local development.
  const [password, setPassword] = useState('admin');
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const from = (location.state as { from?: string } | null)?.from ?? '/modules';
  const valid = email.trim().length > 0 && password.length > 0 && (mode === 'platform' || tenantId.trim().length > 0);

  async function submit(e: React.FormEvent): Promise<void> {
    e.preventDefault();
    if (!valid || pending) return;
    setPending(true);
    setError(null);
    try {
      if (mode === 'platform') await platformLogin(email.trim(), password);
      else await tenantLogin(tenantId.trim(), email.trim(), password);
      refresh();
      navigate(from, { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login failed. Check your credentials and try again.');
    } finally {
      setPending(false);
    }
  }

  return (
    <div className="min-h-screen bg-muted/40">
      <div className="mx-auto flex min-h-screen w-full max-w-md flex-col justify-center px-4 py-8">
      <div className="mb-6 flex items-center gap-2.5">
        <div className="flex h-9 w-9 items-center justify-center rounded-md bg-primary text-primary-foreground">
          <Puzzle className="h-5 w-5" aria-hidden />
        </div>
        <div>
          <p className="text-base font-semibold tracking-tight">Module Platform</p>
          <p className="text-sm text-muted-foreground">Backoffice portal</p>
        </div>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Login</CardTitle>
          <CardDescription>Choose a session type, then sign in to continue.</CardDescription>
        </CardHeader>
        <CardContent>
          <Tabs value={mode} onValueChange={(v) => { setMode(v as 'platform' | 'tenant'); setError(null); }}>
            <TabsList className="grid w-full grid-cols-2" aria-label="Session type">
              <TabsTrigger value="platform">Platform</TabsTrigger>
              <TabsTrigger value="tenant">Tenant</TabsTrigger>
            </TabsList>
            <TabsContent value="platform">
              <p className="mb-4 text-sm text-muted-foreground">
                Platform operators manage tenants, the module registry, and the gateway.
              </p>
            </TabsContent>
            <TabsContent value="tenant">
              <p className="mb-4 text-sm text-muted-foreground">
                Tenant users manage their own modules, theme, and storefront content.
              </p>
            </TabsContent>
          </Tabs>

          <form onSubmit={submit} className="mt-2 space-y-4">
            {mode === 'tenant' && (
              <div className="space-y-1.5">
                <Label htmlFor="tenant-id">Tenant ID *</Label>
                <Input
                  id="tenant-id"
                  value={tenantId}
                  onChange={(e) => setTenantId(e.target.value)}
                  placeholder="tnt_demo"
                  autoComplete="organization"
                  required
                />
              </div>
            )}
            <div className="space-y-1.5">
              <Label htmlFor="email">Email *</Label>
              <Input
                id="email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="admin@local"
                autoComplete="email"
                required
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="password">Password *</Label>
              <Input
                id="password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
                required
              />
            </div>
            {error && (
              <Alert variant="destructive">
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            )}
            <Button type="submit" className="w-full" disabled={pending || !valid}>
              <LogIn className="h-4 w-4" aria-hidden />
              {pending ? 'Signing in…' : 'Login'}
            </Button>
          </form>
        </CardContent>
      </Card>
      <p className="mt-4 text-center text-xs text-muted-foreground">
        Development defaults are prefilled — just press Login.
      </p>
      </div>
    </div>
  );
}
