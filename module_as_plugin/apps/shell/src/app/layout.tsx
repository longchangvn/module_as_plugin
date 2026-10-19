import { useState } from "react";
import { Link, NavLink, useLocation, useNavigate } from "react-router-dom";
import {
  Activity,
  Building2,
  Globe,
  LayoutGrid,
  LogOut,
  Menu,
  Puzzle,
  Route as RouteIcon,
  Server,
  UserRound,
  X,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { useSession } from "@/app/session";
import type { BootstrapModule } from "@/lib/bootstrap";
import { cn } from "@/lib/utils";

interface NavItem {
  to: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  end?: boolean;
}

const CORE_NAV: NavItem[] = [
  { to: "/modules", label: "Catalog", icon: LayoutGrid },
  { to: "/tenants", label: "Tenants", icon: Building2 },
  { to: "/gateway", label: "Gateway", icon: Globe, end: true },
  { to: "/profile", label: "Profile", icon: UserRound },
];

const GATEWAY_NAV: NavItem[] = [
  { to: "/gateway/services", label: "Services", icon: Server },
  { to: "/gateway/routes", label: "Routes", icon: RouteIcon },
  { to: "/gateway/traffic", label: "Traffic", icon: Activity },
];

function navClass({ isActive }: { isActive: boolean }) {
  return cn(
    "flex items-center gap-2.5 rounded-md px-3 py-2 text-sm transition-colors",
    "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
    isActive ? "bg-accent font-medium text-accent-foreground" : "text-muted-foreground hover:bg-accent/60 hover:text-foreground"
  );
}

function SidebarContent({ modules, onNavigate }: { modules: BootstrapModule[]; onNavigate?: () => void }) {
  const { session, logout } = useSession();
  const navigate = useNavigate();
  const moduleLinks = modules.flatMap((m) => m.menus.filter((menu) => menu.route));

  return (
    <div className="flex h-full flex-col">
      <div className="flex h-14 items-center gap-2 border-b px-4">
        <div className="flex h-7 w-7 items-center justify-center rounded-md bg-primary text-primary-foreground">
          <Puzzle className="h-4 w-4" aria-hidden />
        </div>
        <span className="text-sm font-semibold tracking-tight">Module Platform</span>
      </div>

      <nav aria-label="Primary" className="flex-1 space-y-6 overflow-y-auto p-3">
        <div className="space-y-1">
          {CORE_NAV.map((item) => (
            <NavLink key={item.to} to={item.to} end={item.end} className={navClass} onClick={onNavigate}>
              <item.icon className="h-4 w-4" aria-hidden />
              {item.label}
            </NavLink>
          ))}
        </div>

        <div>
          <p className="mb-1 px-3 text-xs font-medium uppercase tracking-wider text-muted-foreground">Gateway</p>
          <div className="space-y-1">
            {GATEWAY_NAV.map((item) => (
              <NavLink key={item.to} to={item.to} className={navClass} onClick={onNavigate}>
                <item.icon className="h-4 w-4" aria-hidden />
                {item.label}
              </NavLink>
            ))}
          </div>
        </div>

        {moduleLinks.length > 0 && (
          <div>
            <p className="mb-1 px-3 text-xs font-medium uppercase tracking-wider text-muted-foreground">Modules</p>
            <div className="space-y-1">
              {moduleLinks.map((menu) => (
                <NavLink key={menu.id} to={menu.route as string} className={navClass} onClick={onNavigate}>
                  <Puzzle className="h-4 w-4" aria-hidden />
                  {menu.title}
                </NavLink>
              ))}
            </div>
          </div>
        )}
      </nav>

      <div className="border-t p-3">
        {session.label && (
          <div className="mb-2 px-1">
            <Badge variant="secondary" className="max-w-full truncate">
              {session.label}
            </Badge>
          </div>
        )}
        {session.loggedIn ? (
          <Button
            variant="ghost"
            size="sm"
            className="w-full justify-start text-muted-foreground"
            onClick={() => {
              logout();
              onNavigate?.();
              navigate("/login");
            }}
          >
            <LogOut className="h-4 w-4" aria-hidden />
            Logout
          </Button>
        ) : (
          <Button variant="outline" size="sm" className="w-full" asChild>
            <Link to="/login" onClick={onNavigate}>
              Login
            </Link>
          </Button>
        )}
      </div>
    </div>
  );
}

export function BackofficeLayout({ modules, children }: { modules: BootstrapModule[]; children: React.ReactNode }) {
  const [mobileOpen, setMobileOpen] = useState(false);
  const location = useLocation();
  const { session } = useSession();

  // Close the mobile drawer on navigation.
  const closeMobile = () => setMobileOpen(false);

  return (
    <div className="min-h-screen bg-muted/40">
      {/* Desktop sidebar */}
      <aside className="fixed inset-y-0 left-0 z-30 hidden w-60 border-r bg-background md:block">
        <SidebarContent modules={modules} />
      </aside>

      {/* Mobile drawer */}
      {mobileOpen && (
        <div className="fixed inset-0 z-40 md:hidden" role="dialog" aria-modal="true" aria-label="Navigation">
          <div className="absolute inset-0 bg-black/60" onClick={closeMobile} />
          <aside className="absolute inset-y-0 left-0 w-72 border-r bg-background">
            <Button variant="ghost" size="icon" className="absolute right-2 top-3" onClick={closeMobile} aria-label="Close navigation">
              <X className="h-4 w-4" />
            </Button>
            <SidebarContent modules={modules} onNavigate={closeMobile} />
          </aside>
        </div>
      )}

      <div className="md:pl-60">
        {/* Topbar */}
        <header className="sticky top-0 z-20 flex h-14 items-center gap-3 border-b bg-background/95 px-4 backdrop-blur sm:px-6">
          <Button variant="ghost" size="icon" className="md:hidden" onClick={() => setMobileOpen(true)} aria-label="Open navigation">
            <Menu className="h-4 w-4" />
          </Button>
          <span className="text-sm text-muted-foreground md:hidden">Module Platform</span>
          <div className="ml-auto flex items-center gap-2">
            {session.label && (
              <Badge variant="secondary" className="hidden sm:inline-flex">
                {session.label}
              </Badge>
            )}
          </div>
        </header>

        <main key={location.pathname} className="mx-auto w-full max-w-6xl px-4 py-6 sm:px-6">
          {children}
        </main>
      </div>
    </div>
  );
}
