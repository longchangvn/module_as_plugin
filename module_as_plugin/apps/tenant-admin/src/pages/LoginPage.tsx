import { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { LogIn, ShieldCheck } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { tenantLogin } from '../lib/auth';
import { useSession } from '../app/session';

// Tenant login only: tenant name/slug/id + email + password. No platform mode
// and deliberately no link to the platform portal.
export function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { refresh } = useSession();
  const [tenant, setTenant] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const from = (location.state as { from?: string } | null)?.from ?? '/';
  const valid = tenant.trim().length > 0 && email.trim().length > 0 && password.length > 0;

  async function submit(e: React.FormEvent): Promise<void> {
    e.preventDefault();
    if (!valid || pending) return;
    setPending(true);
    setError(null);
    try {
      await tenantLogin(tenant.trim(), email.trim(), password);
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
            <ShieldCheck className="h-5 w-5" aria-hidden />
          </div>
          <div>
            <p className="text-base font-semibold tracking-tight">Tenant Admin</p>
            <p className="text-sm text-muted-foreground">Tenant backoffice</p>
          </div>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>Login</CardTitle>
            <CardDescription>Sign in with your tenant credentials to continue.</CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={submit} className="space-y-4">
              <div className="space-y-1.5">
                <Label htmlFor="tenant">Tenant *</Label>
                <Input
                  id="tenant"
                  value={tenant}
                  onChange={(e) => setTenant(e.target.value)}
                  placeholder="Acme Retail"
                  autoComplete="organization"
                  required
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="email">Email *</Label>
                <Input
                  id="email"
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="admin@acme.example"
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
      </div>
    </div>
  );
}
