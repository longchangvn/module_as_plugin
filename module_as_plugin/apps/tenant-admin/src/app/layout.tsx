import { Link, NavLink, useNavigate } from 'react-router-dom';
import { Home, KeyRound, LogOut, Puzzle, ShieldCheck, UserCog, UserRound, Users } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useSession } from './session';
import { cn } from '@/lib/utils';
import type { BootstrapModule } from '@/lib/bootstrap';

const NAV = [
  { to: '/', label: 'Home', icon: Home, end: true },
  { to: '/users', label: 'Users', icon: Users, end: false },
  { to: '/roles', label: 'Roles', icon: UserCog, end: false },
  { to: '/permissions', label: 'Permissions', icon: KeyRound, end: false },
  { to: '/profile', label: 'Profile', icon: UserRound, end: false },
];

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  cn(
    'flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm',
    isActive ? 'bg-muted font-medium' : 'text-muted-foreground hover:bg-muted/60',
  );

export function TenantLayout({ children, modules = [] }: { children: React.ReactNode; modules?: BootstrapModule[] }) {
  const { session, logout } = useSession();
  const navigate = useNavigate();
  const moduleMenus = modules
    .flatMap((m) => m.menus.filter((x) => x.route).map((menu) => ({ moduleId: m.moduleId, menu })))
    .sort((a, b) => a.menu.order - b.menu.order);

  function signOut() {
    logout();
    navigate('/login', { replace: true });
  }

  return (
    <div className="min-h-screen bg-muted/40">
      <header className="border-b bg-background">
        <div className="mx-auto flex h-14 w-full max-w-6xl items-center gap-4 px-4">
          <Link to="/" className="flex items-center gap-2.5">
            <div className="flex h-8 w-8 items-center justify-center rounded-md bg-primary text-primary-foreground">
              <ShieldCheck className="h-4 w-4" aria-hidden />
            </div>
            <div className="leading-tight">
              <p className="text-sm font-semibold tracking-tight">Tenant Admin</p>
              <p className="text-xs text-muted-foreground">{session.label}</p>
            </div>
          </Link>
          <nav className="ml-4 flex items-center gap-1" aria-label="Tenant admin">
            {NAV.map((item) => (
              <NavLink key={item.to} to={item.to} end={item.end} className={navLinkClass}>
                <item.icon className="h-4 w-4" aria-hidden />
                {item.label}
              </NavLink>
            ))}
            {moduleMenus.map(({ moduleId, menu }) => (
              <NavLink key={`${moduleId}:${menu.id}`} to={menu.route!} className={navLinkClass}>
                <Puzzle className="h-4 w-4" aria-hidden />
                {menu.title}
              </NavLink>
            ))}
          </nav>
          <div className="ml-auto">
            <Button variant="ghost" size="sm" onClick={signOut}>
              <LogOut className="h-4 w-4" aria-hidden />
              Logout
            </Button>
          </div>
        </div>
      </header>
      <main className="mx-auto w-full max-w-6xl px-4 py-6">{children}</main>
    </div>
  );
}
