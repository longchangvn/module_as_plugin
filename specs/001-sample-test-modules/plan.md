# Implementation Plan: Sample Test Modules

**Branch**: `001-sample-test-modules` (no git branch created; spec dir only) | **Date**: 2026-10-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/001-sample-test-modules/spec.md`

## Summary

Add two standard-template modules, `sample1` and `sample2`. Each has:
- a .NET 8 minimal API serving `/data` (open) and `/protected` (permission-gated, enforced server-side through Auth resolve)
- a Vite + Module Federation remote page with "Get sample data" and "Get protected data"
- a validated `manifest.yml` that seeds one protected permission and one role

Because the platform shell is operator-only (no tenant sign-in), the **Tenant Admin portal (:5175) becomes the Federation host**. Home lists the installed modules, the nav links to them, and each module route renders the module's remote page from the frontend bootstrap. Testers publish, install, grant or revoke the permission, and disable or uninstall the modules end to end with no platform redeploy. Full rationale: [research.md](./research.md).

## Technical Context

**Language/Version**: C# / .NET 8 (module APIs); TypeScript 5.7 / React 18 (remotes, tenant-admin host)

**Primary Dependencies**:
- Module APIs: ASP.NET Core minimal APIs, `Microsoft.AspNetCore.Authentication.JwtBearer`, `SharedKernel`, `ModulePlatform.ServiceDefaults`
- Frontend: Vite 5, `@originjs/vite-plugin-federation` ^1.3.9 (already used by shell and loyalty), react-router-dom 6

**Storage**: N/A for the modules (in-memory responses). The platform catalog and tenant stores are reused via the existing seed and lifecycle.

**Testing**:
- xUnit (`tests/SharedKernel.Tests`): manifest validation of `modules/sample*/manifest.yml`, `ModuleGuards` decision logic, seed-preview parity
- `tsc --noEmit` + `vite build` for the host and remotes
- Manual E2E per [quickstart.md](./quickstart.md)

**Target Platform**: Local dev (Aspire AppHost or manual `dotnet run` / `npm`), plus docker-compose parity

**Project Type**: Web, multi-service (gateway + platform + auth + module services + federated SPA)

**Performance Goals**: Sample data visible < 2 s on local (SC-003); remotes lazy-load per route (NFR2)

**Constraints**:
- All browser calls go through the gateway (G-BR3)
- Permission enforced server-side, fail closed `503 AUTHZ_UNAVAILABLE` (§17, §26)
- No manifest in the browser (R10)
- JWT only in session storage (SR-9)
- Loyalty module unchanged
- Permission codes match `^[A-Z]+_[A-Z_]+$` (no digits), hence `SAMPLE_ONE_*` / `SAMPLE_TWO_*`

**Scale/Scope**: 2 modules × (1 API + 1 remote page + 1 manifest), 1 host change, 1 shared helper

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is still the **unfilled template**, so there are no ratified principles to gate against. In its place, the plan is checked against the project's binding guidelines:

| Gate (source) | Status |
|---|---|
| Use existing patterns and libraries, no self-implementation (`implementation_guidline.md` §7) | PASS: reuses originjs federation, JwtBearer, Auth resolve, the existing seed/lifecycle, and the manifest validator |
| Simplest design / minimal code (§Code Quality) | PASS: one shared `ModuleGuards` helper instead of per-module copies; no new tables or endpoints in core services |
| Documentation is part of done (§Documentation) | PASS (planned): README/SETUP/PROGRESS and the module READMEs updated in the same change |
| Requirement contracts: G-BR3, R10, SR-6b, SR-9, M-BR1/5/7, R4 | PASS: see the contracts; both manifests validated against `ManifestValidator` (dev mode) during planning |
| Don't modify unrelated code | PASS: Loyalty and shell untouched; findings in research R9 are recorded, not fixed |

**Post-design re-check**: PASS. There are no violations, so Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/001-sample-test-modules/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R9 decisions + findings
├── data-model.md        # manifest-defined entities, permissions, roles, DTO, UI state
├── quickstart.md        # E2E validation guide
├── contracts/
│   ├── sample-module-api.md
│   ├── remote-ui.md
│   ├── manifest-sample1.yml   # validated
│   └── manifest-sample2.yml   # validated
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks (not created here)
```

### Source Code (`module_as_plugin/`)

```text
src/Shared/SharedKernel/
└── ModuleGuards.cs                 # NEW: RequireTenantAsync + RequirePermissionAsync (Auth resolve, fail closed)

src/Modules/Sample1/Sample1.Api/    # NEW (:5202), template layout like Loyalty.Api
├── Program.cs                      # RequireSecret, JwtBearer, ServiceDefaults
├── Endpoints/ModuleEndpoints.cs    # /healthz /readyz /internal/seed:preview /internal/migrate /api/v1/data /api/v1/protected
├── Properties/launchSettings.json
├── appsettings*.json  Dockerfile  Sample1.Api.csproj
src/Modules/Sample2/Sample2.Api/    # NEW (:5203), same shape

modules/sample1/                    # NEW
├── manifest.yml                    # = contracts/manifest-sample1.yml
├── README.md
└── web/                            # Vite + federation remote (:5176), exposes ./Page
    ├── vite.config.ts  package.json  tsconfig.json  index.html  Dockerfile
    └── src/Page.tsx  src/page.css  src/api.ts  src/dev-main.tsx
modules/sample2/                    # NEW, same shape (:5177)

apps/tenant-admin/                  # CHANGED: becomes Federation host
├── vite.config.ts                  # + federation({ name: 'tenant_admin', remotes: {placeholder}, shared })
├── package.json                    # + @originjs/vite-plugin-federation (same range as shell)
└── src/
    ├── lib/bootstrap.ts            # NEW: fetch frontend-bootstrap (types as shell)
    ├── lib/remotes.ts              # NEW: register + load remote via virtual:__federation__
    ├── app/useBootstrap.ts         # NEW
    ├── components/RemoteRoute.tsx  # NEW (+ ModuleErrorBoundary)
    ├── app/App.tsx                 # + bootstrap routes
    ├── app/layout.tsx              # + module nav links
    └── pages/HomePage.tsx          # + "Installed modules" section

src/Aspire/ModulePlatform.AppHost/Program.cs  # + sample1/sample2 APIs + 2 npm remotes
ModulePlatform.sln                             # + 2 projects
docker-compose.yml                             # + sample1, sample2, sample1-web, sample2-web
tests/SharedKernel.Tests/SampleModuleTests.cs  # NEW: manifests valid, seed-preview parity, guard logic
```

**Structure Decision**: Follow the existing repo layout: module APIs under `src/Modules/{Name}`, module manifest and remote under `modules/{name}`, which is exactly where Loyalty lives (§10.5/M-BR1 as practised here). The host change is confined to `apps/tenant-admin`.

## Implementation Order (input for /speckit-tasks)

1. **Spike (R2)**: tenant-admin federation host plus a minimal `sample1` remote with `useState`. Prove the runtime load and the shared React. If it fails, switch to the `./mount` fallback (research R2) before building further.
2. `SharedKernel/ModuleGuards.cs` + unit tests.
3. `Sample1.Api` + `manifest.yml` + manifest/parity tests, then `Sample2.Api` as a copy.
4. Remote pages (sample1, then sample2) per [remote-ui.md](./contracts/remote-ui.md).
5. Tenant-admin host: bootstrap, nav, Home section, routes, error boundary.
6. Wiring: sln, AppHost, compose, Dockerfiles.
7. Docs: README/SETUP/PROGRESS + module READMEs. Run the [quickstart.md](./quickstart.md) matrix.

## Risks

| Risk | Mitigation |
|---|---|
| originjs dynamic remote plus shared React fails with the host in `vite dev` | Spike first; fallback `./mount` pattern (research R2) |
| Global gateway module state confuses multi-tenant testing | Quickstart: use one tenant; finding recorded (R9.1) |
| Anonymous permission resolve endpoint exposed via gateway | Out of scope; added to the Phase 2 security list (R9.2) |
| Docker Desktop still broken (Phase 1) | Needed for Postgres; restart before E2E |

## Complexity Tracking

No constitution violations; nothing to justify.
