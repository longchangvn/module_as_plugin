import { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { ArrowRight, LogIn, Puzzle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { platformLogin } from '../lib/auth';
import { useSession } from '../app/session';

// Platform login only. Tenant users sign in on the Tenant Admin portal
// (VITE_TENANT_ADMIN_URL, default http://localhost:5175/login) — linked below,
// never embedded here.
const TENANT_ADMIN_URL =
  (import.meta.env.VITE_TENANT_ADMIN_URL as string | undefined) ?? 'http://localhost:5175/login';

export function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { refresh } = useSession();
  const [email, setEmail] = useState('admin@local');
  // Dev-seeded credentials prefilled (password verification is stubbed in v1,
  // see Program.cs P5 note) so login is one click in local development.
  const [password, setPassword] = useState('admin');
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const from = (location.state as { from?: string } | null)?.from ?? '/modules';
  const valid = email.trim().length > 0 && password.length > 0;

  async function submit(e: React.FormEvent): Promise<void> {
    e.preventDefault();
    if (!valid || pending) return;
    setPending(true);
    setError(null);
    try {
      await platformLogin(email.trim(), password);
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
          <CardTitle>Platform login</CardTitle>
          <CardDescription>Platform operators manage tenants, the module registry, and the gateway.</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={submit} className="space-y-4">
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
          <p className="mt-4 text-center text-sm text-muted-foreground">
            Tenant user?{' '}
            <a className="inline-flex items-center gap-1 font-medium text-foreground underline underline-offset-4" href={TENANT_ADMIN_URL}>
              Sign in to Tenant Admin
              <ArrowRight className="h-3.5 w-3.5" aria-hidden />
            </a>
          </p>
        </CardContent>
      </Card>
      <p className="mt-4 text-center text-xs text-muted-foreground">
        Development defaults are prefilled — just press Login.
      </p>
      </div>
    </div>
  );
}
