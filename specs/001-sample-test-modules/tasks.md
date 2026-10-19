---

description: "Task list for Sample Test Modules"
---

# Tasks: Sample Test Modules

**Input**: Design documents from `specs/001-sample-test-modules/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (sample-module-api.md, remote-ui.md, manifest-sample1.yml, manifest-sample2.yml), quickstart.md

**Tests**: The spec does not request TDD. This list includes only the small automated checks named in plan.md (manifest validity, seed-preview parity, `ModuleGuards` logic), because the project guideline requires tests "where appropriate". End-to-end verification is the manual quickstart matrix.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US4 from spec.md
- All paths are relative to `module_as_plugin/` unless they start with `specs/`

## Global conventions (apply to every task)

- Module 1: slug `sample1`, display "Sample Module 1", API `src/Modules/Sample1/Sample1.Api` on **:5202**, remote `modules/sample1/web` on **:5176**, scope `sample1_remote`, route/menu `/sample1` / `sample1`, permissions `SAMPLE_ONE_DATA_VIEW`, `SAMPLE_ONE_DATA_PROTECTED`, role "Sample 1 Viewer".
- Module 2: same with `sample2`, "Sample Module 2", **:5203**, **:5177**, `sample2_remote`, `SAMPLE_TWO_*`, "Sample 2 Viewer".
- Permission codes MUST match `^[A-Z]+_[A-Z_]+$` (no digits) (data-model.md).
- Error envelope `{ "error": { "code", "message", "details"?, "retryable"? } }`; use `SharedKernel.ApiErrors.Problem` and `ErrorCodes` constants.
- Do not modify Loyalty (`src/Modules/Loyalty`, `modules/loyalty`) or `apps/shell`.
- Reuse the Loyalty API as the structural reference (`src/Modules/Loyalty/Loyalty.Api/Program.cs`, `Endpoints/ModuleEndpoints.cs`, `Loyalty.Api.csproj`, `Dockerfile`).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create projects and folders so later tasks only fill in code.

- [X] T001 Create `src/Modules/Sample1/Sample1.Api/Sample1.Api.csproj`. Copy `src/Modules/Loyalty/Loyalty.Api/Loyalty.Api.csproj`: net8.0, same package refs (JwtBearer 8.0.11, OpenApi 8.0.11, Swashbuckle 6.6.2), and ProjectReferences to `src/Shared/SharedKernel/SharedKernel.csproj` and `src/Aspire/ModulePlatform.ServiceDefaults/ModulePlatform.ServiceDefaults.csproj` with correct relative paths. Add `Properties/launchSettings.json` (profile `http`, `applicationUrl` `http://localhost:5202`, `ASPNETCORE_ENVIRONMENT=Development`) and `appsettings.json` / `appsettings.Development.json` (logging only, like Loyalty).
- [X] T002 [P] Create `src/Modules/Sample2/Sample2.Api/` with the same files as T001, using `Sample2.Api` names and port **5203**.
- [X] T003 Add both projects to `ModulePlatform.sln`: `dotnet sln ModulePlatform.sln add src/Modules/Sample1/Sample1.Api/Sample1.Api.csproj src/Modules/Sample2/Sample2.Api/Sample2.Api.csproj` (depends on T001, T002).
- [X] T004 [P] Scaffold the remote `modules/sample1/web/`. Copy `package.json`, `tsconfig.json`, `index.html` and `src/dev-main.tsx` from `modules/loyalty/web/`. Rename the package to `@modules/sample1-web`. Set the scripts to `"dev": "vite --port 5176"`, `"build": "tsc --noEmit && vite build"`, `"preview": "vite preview --port 5176 --strictPort"`, `"serve": "npm run build && npm run preview"` and `"typecheck": "tsc --noEmit"`. Keep the same dependency versions. The originjs federation remote only works from a build (research R2).
- [X] T005 [P] Scaffold `modules/sample2/web/` the same way as T004, with name `@modules/sample2-web` and port **5177**.
- [X] T006 [P] Add `@originjs/vite-plugin-federation` to `apps/tenant-admin/package.json` devDependencies using the same range as `apps/shell/package.json` (`^1.3.9`), then run `npm install` in `apps/tenant-admin`.

**Checkpoint**: `dotnet build ModulePlatform.sln` compiles (empty Program.cs files allowed), and the npm installs succeed.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Prove runtime remote loading (highest risk) and add the shared tenant guard used by every module endpoint.

**⚠️ CRITICAL**: No user story work starts until T007–T010 pass.

- [X] T007 Configure the tenant-admin federation host in `apps/tenant-admin/vite.config.ts`. Add `federation({ name: 'tenant_admin', remotes: { placeholder: 'http://localhost:0/placeholder.js' }, shared: ['react', 'react-dom'] })` after `react()` and `tailwindcss()`. The placeholder remote exists only so the plugin generates `virtual:__federation__`. Set `build.target: 'esnext'` (already present). Add a `src/types/federation.d.ts` that declares module `'virtual:__federation__'` with `__federation_method_setRemote(name: string, cfg: { url: string | (() => Promise<string>); format: 'esm' | 'var'; from: 'vite' | 'webpack' }): void`, `__federation_method_getRemote(name: string, exposed: string): Promise<unknown>` and `__federation_method_unwrapDefault(mod: unknown): unknown`.
- [X] T008 Create `apps/tenant-admin/src/lib/remotes.ts` with `loadRemoteComponent(scope: string, remoteUrl: string, exposes: string): Promise<React.ComponentType>`. It calls `__federation_method_setRemote(scope, { url: () => Promise.resolve(remoteUrl), format: 'esm', from: 'vite' })` once per scope (memoise in a `Map`), then `__federation_method_getRemote(scope, exposes)`, and returns `__federation_method_unwrapDefault(mod)` as the component (depends on T007).
- [ ] T009 SPIKE (research R2). Make the sample1 remote build a minimal federated component:
  - In `modules/sample1/web/vite.config.ts`, add `react()` and `federation({ name: 'sample1_remote', filename: 'remoteEntry.js', exposes: { './Page': './src/Page.tsx' }, shared: ['react', 'react-dom'] })`, with `build: { target: 'esnext', cssCodeSplit: false, minify: true }` and `preview: { port: 5176, cors: true }`.
  - Create `modules/sample1/web/src/Page.tsx` as a default-export component using `useState` (a click counter).
  - Temporarily render it in tenant-admin from a throwaway route, using `loadRemoteComponent('sample1_remote', 'http://localhost:5176/assets/remoteEntry.js', './Page')` with `React.lazy`.
  - Run `npm run serve` in the remote and `npm run dev` in tenant-admin.
  - **Pass**: the counter increments with no "Invalid hook call" error, in both host `vite dev` and host `vite build && vite preview`.
  - **If it fails**: switch the contract to the `./mount` fallback from `specs/001-sample-test-modules/contracts/remote-ui.md` (the remote exposes `./mount` = `(el) => { createRoot(el).render(<Page/>); return () => root.unmount(); }`), update `remotes.ts` to return a wrapper component that calls `mount` in `useEffect`, and record the decision in `specs/001-sample-test-modules/research.md` R2.
  - Remove the throwaway route afterwards (depends on T004, T008).
- [X] T010 [P] Create `src/Shared/SharedKernel/ModuleGuards.cs` (namespace `SharedKernel`) with `public static Task<IResult?> RequireTenantAsync(HttpContext ctx, IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg)`. It returns null when OK, otherwise an error result.
  - Read the claim `tenantId`. If missing → 403 `FORBIDDEN_TENANT` "Tenant context required."
  - If an `X-Tenant-Id` header is present and differs (case-insensitive, trimmed) → 403 `FORBIDDEN_TENANT`.
  - Then tenant usability, with the same logic as `src/Modules/Loyalty/Loyalty.Api/Endpoints/LoyaltyHelpers.cs`: `GET {Platform:BaseUrl ?? http://localhost:5121}/internal/tenants/{id}/status` via named client `"platform"`, cached 30s under `tenant-usable:{id}`. 404 → `UNKNOWN_TENANT` 404. Non-`Active` → `TENANT_DISABLED` 403. `HttpRequestException`/`TaskCanceledException` → `AUTHZ_UNAVAILABLE` 503 with `retryable: true`.
  - Also add `public static string? UserId(HttpContext ctx)`, returning claim `userId`, else `sub`, else `ClaimTypes.NameIdentifier`.
  - Do not change Loyalty.

**Checkpoint**: A federated remote component renders inside Tenant Admin, and `ModuleGuards` compiles.

---

## Phase 3: User Story 1 - Install a sample module and see it appear (Priority: P1) 🎯 MVP

**Goal**: After publishing and installing `sample1`, a tenant user sees "Sample Module 1" on Tenant Admin Home and in the nav, and opening it shows the module page (name and version). Disable, enable and uninstall are reflected after a reload.

**Independent Test**: quickstart.md scenarios 1, 2, 9, 10, 11, using only sample1.

- [X] T011 [US1] Implement `src/Modules/Sample1/Sample1.Api/Program.cs` following `src/Modules/Loyalty/Loyalty.Api/Program.cs`:
  - `builder.AddServiceDefaults()`, Swagger, `var jwtSecret = builder.RequireSecret("Jwt:SecretKey", Extensions.DevJwtSecret);`, and JwtBearer with the same `TokenValidationParameters`.
  - `AddAuthorization()`, `AddMemoryCache()`, `AddHttpClient("platform", c => c.Timeout = TimeSpan.FromSeconds(5))`, `AddHttpClient("auth", c => c.Timeout = TimeSpan.FromSeconds(5))`.
  - `app.MapDefaultEndpoints()`, `UseAuthentication()`, `UseAuthorization()`, `ModuleEndpoints.Map(app)`, and `public partial class Program;`.
- [X] T012 [US1] Create `src/Modules/Sample1/Sample1.Api/Endpoints/ModuleEndpoints.cs` with `public const string ModuleId = "sample1"; public const string Version = "1.0.0";` and the template endpoints from `specs/001-sample-test-modules/contracts/sample-module-api.md`:
  - `GET /healthz` → `{ status: "ok", module, version }`
  - `GET /readyz` → `{ ready: true, checks: { db: "n/a", dependencies: "ok" } }`
  - `POST /internal/seed:preview` → `{ data: { roles: ["Sample 1 Viewer"], permissions: ["SAMPLE_ONE_DATA_VIEW","SAMPLE_ONE_DATA_PROTECTED"], menus: ["sample1"] } }`
  - `POST /internal/migrate` taking a `record MigrateRequest(string FromVersion, string ToVersion)` in `Models/Requests.cs`, with behaviour identical to Loyalty.
- [X] T013 [P] [US1] Create `modules/sample1/manifest.yml` as a byte-for-byte copy of `specs/001-sample-test-modules/contracts/manifest-sample1.yml` (already validated). Remove only its first comment line, replacing it with `# manifest.yml — Sample Module 1 (test module).`.
- [X] T014 [P] [US1] Create `tests/SharedKernel.Tests/SampleModuleTests.cs` (xUnit) with:
  - (a) a theory over `modules/sample1/manifest.yml` and `modules/sample2/manifest.yml`, resolving the repo root by walking up from `AppContext.BaseDirectory` until `ModulePlatform.sln` is found. It must `ManifestParser.Parse` the file and assert `ManifestValidator.Validate(m, new HashSet<string>{"loyalty_remote"}, isDevelopment: true)` is empty.
  - (b) a parity fact that each manifest's `api.seed` role names, permission codes and `frontend.menus` ids equal the constants returned by the module's seed preview. Assert against the literal arrays from T012/T037.
  
  Skip sample2 cases with `Skip` until T037/T039 exist, and then remove the skip.
- [X] T015 [US1] Create `apps/tenant-admin/src/lib/bootstrap.ts` with the `BootstrapRoute`, `BootstrapMenu`, `BootstrapModule` and `FrontendBootstrap` types copied from `apps/shell/src/lib/bootstrap.ts`, and `fetchBootstrap()` = `api<FrontendBootstrap>('/api/v1/tenant/frontend-bootstrap')` using `apps/tenant-admin/src/lib/api-client.ts`.
- [X] T016 [US1] Create `apps/tenant-admin/src/app/useBootstrap.ts` returning `{ modules, loading, error, retry }`. It fetches on mount only when `sessionStorage.access_token` and `tenant_id` exist, and on error sets `error` and leaves `modules` empty. Do not pre-load remotes (lazy per route, NFR2). (Depends on T015.)
- [X] T017 [P] [US1] Create `apps/tenant-admin/src/components/ModuleErrorBoundary.tsx` by copying `apps/shell/src/components/ModuleErrorBoundary.tsx`. Use tenant-admin's existing alert or card UI components (check `apps/tenant-admin/src/components/ui/`). If no Alert exists, use a bordered `div` with Tailwind classes and the same copy: "Module unavailable" plus "“{moduleId}” failed to load. The rest of the portal keeps working — try reloading the page."
- [X] T018 [US1] Create `apps/tenant-admin/src/components/RemoteRoute.tsx` with props `{ moduleId, scope, remoteUrl, exposes }`. Use `React.lazy(() => loadRemoteComponent(scope, remoteUrl, exposes).then(C => ({ default: C })))`, memoised per `scope+exposes` with `useMemo`, and wrap it in `ModuleErrorBoundary` plus `Suspense` with a "Loading module…" fallback. Use the fallback-mount variant if T009 decided so. (Depends on T008, T017.)
- [X] T019 [US1] Update `apps/tenant-admin/src/app/App.tsx`. Call `useBootstrap()` inside the authed branch and pass `modules` to `TenantLayout`. Inside the inner `<Routes>`, add one `<Route path={r.path} element={<RemoteRoute moduleId={m.moduleId} scope={m.scope} remoteUrl={m.remoteUrl} exposes={r.exposes} />} />` per `m.routes`. Pass `modules` to `HomePage`. Keep all existing routes. (Depends on T016, T018.)
- [X] T020 [US1] Update `apps/tenant-admin/src/app/layout.tsx`. `TenantLayout` accepts an optional `modules: BootstrapModule[]` (default `[]`) and renders, after the core `NAV` links, one `NavLink` per `modules.flatMap(m => m.menus.filter(x => x.route))`, sorted by `order`, using the label `menu.title` and the `Puzzle` icon from lucide-react, with the same classes as the core links. (Depends on T015.)
- [X] T021 [US1] Update `apps/tenant-admin/src/pages/HomePage.tsx`. Add an "Installed modules" section that accepts `modules: BootstrapModule[]`:
  - One card per module: title = the module's top-level menu title (the menu with `parentId === null`, else `moduleId`), a `v{version}` badge, and an "Open" `Link` to `routes[0].path`.
  - Empty state text: "No modules installed. Ask a platform admin to install one from the Catalog."
  
  Keep the existing counts and permissions content. (Depends on T015.)
- [X] T022 [US1] Replace the spike content of `modules/sample1/web/src/Page.tsx` with the page header only: `<h1>Sample Module 1</h1>` and `<p>Version 1.0.0</p>`, plus a container `div` with class `s1-page`. Create `modules/sample1/web/src/page.css` (plain CSS, no Tailwind, research R7) with simple spacing, button and `pre` styles, prefixed `.s1-` to avoid collisions, and import it in `Page.tsx`. (Depends on T009.)
- [X] T023 [US1] Wire local orchestration in `src/Aspire/ModulePlatform.AppHost/Program.cs`. Add `builder.AddProject<Projects.Sample1_Api>("sample1")` with `.WithEndpoint("http", e => { e.Port = 5202; e.IsProxied = false; })`, `.WithEnvironment("ASPNETCORE_URLS", "http://localhost:5202")`, `.WithReference(platform)` and `.WithReference(auth)`, and `builder.AddNpmApp("sample1-web", "../../../modules/sample1/web", "serve")` with endpoint port 5176 (`IsProxied = false`) and `.WithExternalHttpEndpoints()`. Add a ProjectReference to `src/Modules/Sample1/Sample1.Api/Sample1.Api.csproj` in `src/Aspire/ModulePlatform.AppHost/ModulePlatform.AppHost.csproj`.
- [ ] T024 [US1] Build and verify: `dotnet build ModulePlatform.sln` (0 errors), `dotnet test` (T014 sample1 cases pass), `cd apps/tenant-admin && npx tsc --noEmit && npx vite build`, `cd modules/sample1/web && npx tsc --noEmit`. Then run quickstart.md scenarios 1, 2, 9, 10, 11 for sample1 and record the results.

**Checkpoint**: Sample Module 1 is installable and visible; its page shows its name and version.

---

## Phase 4: User Story 2 - Fetch sample data with the open button (Priority: P1)

**Goal**: "Get sample data" calls `GET /api/v1/sample1/data` through the gateway and shows formatted JSON, with loading and error states.

**Independent Test**: quickstart.md scenarios 3 and 13 (sample1).

- [X] T025 [US2] In `src/Modules/Sample1/Sample1.Api/Endpoints/ModuleEndpoints.cs`, add `GET /api/v1/data` with `.RequireAuthorization()`:
  - `if (await ModuleGuards.RequireTenantAsync(ctx, http, cache, cfg) is { } err) return err;`
  - Then return `{ data: { module: "sample1", version: "1.0.0", kind: "sample", generatedAt: DateTimeOffset.UtcNow (ISO-8601 UTC), tenantId, items: [ {id:"s1-1",name:"Sample 1 · Alpha",value:10}, {id:"s1-2",name:"Sample 1 · Beta",value:20}, {id:"s1-3",name:"Sample 1 · Gamma",value:30} ] } }` (data-model.md SampleContent).
  
  Put the item builder in a private static method `Items(string kind)` so the protected endpoint reuses it.
- [X] T026 [P] [US2] Create `modules/sample1/web/src/api.ts` with `call<T>(path: string): Promise<T>`:
  - `fetch(path, { headers: { Authorization: 'Bearer ' + sessionStorage.getItem('access_token'), 'X-Tenant-Id': sessionStorage.getItem('tenant_id') ?? '' } })`.
  - On `401`: `window.location.assign('/login')` and throw.
  - On non-2xx: parse the envelope and throw `Error` with `code` and `message` (`"{code}: {message}"`, falling back to `HTTP {status}`).
  - On a network error: throw "Service unreachable".
  - Return `body.data`.
- [X] T027 [US2] In `modules/sample1/web/src/Page.tsx`, add the **Get sample data** button with its own state `idle | loading | success | error`:
  - Disabled while loading (prevents double-click).
  - On click, `call('/api/v1/sample1/data')`.
  - Render "Loading…", then `<pre className="s1-json">{JSON.stringify(data, null, 2)}</pre>` or `<p role="alert" className="s1-error">{message}</p>`.
  
  (Depends on T022, T026.)
- [ ] T028 [US2] Verify: rebuild the remote (`npm run serve`), restart Sample1.Api, and run quickstart scenarios 3 and 13. Confirm `generatedAt` changes on each click and the content appears in < 2 s (SC-003).

**Checkpoint**: The open button works end to end through the gateway.

---

## Phase 5: User Story 3 - Permission-gated button (Priority: P2)

**Goal**: "Get protected data" is enabled only with `SAMPLE_ONE_DATA_PROTECTED`, and the server enforces it on every call (403 `FORBIDDEN`, fail closed 503).

**Independent Test**: quickstart.md scenarios 4–7 (sample1).

- [X] T029 [US3] In `src/Shared/SharedKernel/ModuleGuards.cs`, add `public static async Task<IResult?> RequirePermissionAsync(HttpContext ctx, IHttpClientFactory http, IConfiguration cfg, string tenantId, string permission)`:
  - Call `GET {Auth:BaseUrl ?? http://localhost:5122}/api/v1/auth/permissions/resolve?tenantId={tenantId}&userId={UserId(ctx)}` with named client `"auth"`.
  - Missing user id → 403 `FORBIDDEN`.
  - Non-2xx or a network error → 503 `AUTHZ_UNAVAILABLE` with `retryable: true` (fail closed).
  - Read `data.permissions` (string array). If it doesn't contain `permission` (ordinal) → `ApiErrors.Problem(ErrorCodes.Forbidden, $"Requires {permission}.", 403, new { requiredPermission = permission })`.
  - **No module-side cache** (research R4).
  
  Add a pure helper `public static bool HasPermission(IEnumerable<string>? permissions, string code)` and use it.
- [X] T030 [P] [US3] Add `tests/SharedKernel.Tests/ModuleGuardsTests.cs` covering `HasPermission`: present → true; absent → false; null list → false; case-sensitive (`sample_one_data_protected` ≠ `SAMPLE_ONE_DATA_PROTECTED`) → false. Also cover `UserId` claim precedence (`userId` > `sub` > NameIdentifier) using a `DefaultHttpContext` with a `ClaimsPrincipal`.
- [X] T031 [US3] In `src/Modules/Sample1/Sample1.Api/Endpoints/ModuleEndpoints.cs`, add `GET /api/v1/protected` with `.RequireAuthorization()`:
  - `RequireTenantAsync`, then `RequirePermissionAsync(..., tenantId, "SAMPLE_ONE_DATA_PROTECTED")`.
  - Return the same shape as `/data` with `kind: "protected"` and item names suffixed ` (protected)`.
  
  Define `const string ProtectedPermission = "SAMPLE_ONE_DATA_PROTECTED";`. (Depends on T029.)
- [X] T032 [US3] Add `Auth:BaseUrl` = `http://localhost:5122` and `Platform:BaseUrl` = `http://localhost:5121` to `src/Modules/Sample1/Sample1.Api/appsettings.Development.json`. Ensure the AppHost references from T023 supply the service URLs (or add `.WithEnvironment("Auth__BaseUrl", "http://localhost:5122").WithEnvironment("Platform__BaseUrl", "http://localhost:5121")`).
- [X] T033 [US3] In `modules/sample1/web/src/Page.tsx`, on mount call `call<{ permissions: string[] }>('/api/v1/auth/me')` and set `canProtected = permissions.includes('SAMPLE_ONE_DATA_PROTECTED')`. Render a **Get protected data** button:
  - `disabled={!canProtected || state === 'loading'}`.
  - When not permitted, `title="Requires SAMPLE_ONE_DATA_PROTECTED"` and a visible hint `<small className="s1-hint">Requires SAMPLE_ONE_DATA_PROTECTED</small>`.
  - If `/auth/me` fails, keep it disabled with the hint "Permission check failed".
  - On click, `call('/api/v1/sample1/protected')`, with the same independent state rendering as T027.
- [ ] T034 [US3] Verify quickstart scenarios 4–7. Without the role, the button is disabled and a direct curl returns 403 `FORBIDDEN` with `details.requiredPermission`. After assigning "Sample 1 Viewer" in Tenant Admin and reloading, it is enabled and returns `kind: protected`. After revoking and reloading, it is disabled and the curl returns 403 immediately.

**Checkpoint**: Sample Module 1 fully meets the spec (US1–US3).

---

## Phase 6: User Story 4 - Two independent modules (Priority: P3)

**Goal**: Sample Module 2 behaves the same as Module 1 with its own identity, permission and role, and the two are fully independent.

**Independent Test**: quickstart.md scenarios 8, 11 (Module 2 unaffected), 12 and 14.

- [X] T035 [P] [US4] Create `src/Modules/Sample2/Sample2.Api/Program.cs` identical to the Sample1 version (T011).
- [X] T036 [P] [US4] Create `src/Modules/Sample2/Sample2.Api/Models/Requests.cs` with `record MigrateRequest(string FromVersion, string ToVersion)`.
- [X] T037 [US4] Create `src/Modules/Sample2/Sample2.Api/Endpoints/ModuleEndpoints.cs` by mirroring the final Sample1 file (T012, T025, T031) with these values:
  - `ModuleId = "sample2"`
  - seed preview roles `["Sample 2 Viewer"]`, permissions `["SAMPLE_TWO_DATA_VIEW","SAMPLE_TWO_DATA_PROTECTED"]`, menus `["sample2"]`
  - items `s2-1..3` named `Sample 2 · Alpha/Beta/Gamma`, values 100/200/300
  - `ProtectedPermission = "SAMPLE_TWO_DATA_PROTECTED"`
- [X] T038 [P] [US4] Create `src/Modules/Sample2/Sample2.Api/appsettings.Development.json` with `Auth:BaseUrl` and `Platform:BaseUrl`, as in T032.
- [X] T039 [P] [US4] Create `modules/sample2/manifest.yml` from `specs/001-sample-test-modules/contracts/manifest-sample2.yml`, with header comment `# manifest.yml — Sample Module 2 (test module).`. Then remove the `Skip` from the sample2 cases in `tests/SharedKernel.Tests/SampleModuleTests.cs` (T014).
- [X] T040 [P] [US4] Create `modules/sample2/web/vite.config.ts` like sample1 (T009) with `name: 'sample2_remote'` and preview port 5177.
- [X] T041 [P] [US4] Create `modules/sample2/web/src/api.ts` identical to sample1's (T026).
- [X] T042 [US4] Create `modules/sample2/web/src/Page.tsx` and `src/page.css` mirroring sample1's final page (T022, T027, T033) with these values: title "Sample Module 2", CSS prefix `.s2-`, endpoints `/api/v1/sample2/data` and `/api/v1/sample2/protected`, permission `SAMPLE_TWO_DATA_PROTECTED`. (Depends on T040, T041.)
- [X] T043 [US4] Wire Module 2 into `src/Aspire/ModulePlatform.AppHost/Program.cs` and `ModulePlatform.AppHost.csproj`, mirroring T023 with ports 5203 and 5177 and names `sample2` / `sample2-web`.
- [ ] T044 [US4] Verify: run `dotnet test` (all SampleModuleTests cases pass, no skips) and `npx tsc --noEmit` in `modules/sample2/web`, then run quickstart scenarios 8, 11 (Module 2 still works after uninstalling Module 1), 12 and 14.

**Checkpoint**: Both modules work independently (SC-005).

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T045 [P] Add Dockerfiles:
  - `src/Modules/Sample1/Sample1.Api/Dockerfile` and `src/Modules/Sample2/Sample2.Api/Dockerfile`, copied from `src/Modules/Loyalty/Loyalty.Api/Dockerfile`, keeping the ServiceDefaults csproj COPY **before** `dotnet restore`.
  - `modules/sample1/web/Dockerfile` and `modules/sample2/web/Dockerfile`, copied from `modules/loyalty/web/Dockerfile`.
- [X] T046 Add services to `docker-compose.yml`:
  - `sample1` (:5202→8080) and `sample2` (:5203→8080) with `ASPNETCORE_ENVIRONMENT: Production`, `Jwt__SecretKey: ${JWT_SECRET:?set JWT_SECRET (>=32 chars, see .env.example)}`, `Platform__BaseUrl: http://platform:8080` and `Auth__BaseUrl: http://auth:8080`.
  - `sample1-web` (:5176→80) and `sample2-web` (:5177→80).
  
  Verify `JWT_SECRET=x INTERNAL_API_KEY=y docker compose config -q`.
- [X] T047 Confirm the sample1 and sample2 seed-preview constants match the manifests (`SampleModuleTests` parity passes). If any value drifted during implementation, fix the code, not the manifest.
- [X] T048 [P] Create `modules/sample1/README.md` and `modules/sample2/README.md` covering purpose, ports, run commands (`dotnet run --project …`, `npm run serve`), the publish curl with `artifactApiUrl` `http://localhost:5202` or `:5203`, the permission and role names, and a link to `specs/001-sample-test-modules/quickstart.md`.
- [X] T049 Update docs (implementation_guidline.md: documentation is part of done):
  - `README.md` and `SETUP.md`: add a "Sample test modules" section with ports (5202/5203/5176/5177), the Tenant Admin acting as the Federation host (Home "Installed modules", nav, module routes), and the publish/install steps.
  - `PROGRESS.md`: add a P20 entry. In Notes/decisions, record the research R9 findings: global gateway module state; the anonymous resolve endpoint; the shell remote loader incompatible with originjs ESM.
- [ ] T050 Final verification: `dotnet build ModulePlatform.sln` (0 errors, no new warnings), `dotnet test ModulePlatform.sln` (all pass), `npx tsc --noEmit` in `apps/tenant-admin`, `modules/sample1/web` and `modules/sample2/web`, and `npx vite build` in `apps/tenant-admin`. Then run the full quickstart.md matrix (scenarios 1–14) and record the results in `PROGRESS.md`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: needs Setup. T009 (spike) gates all UI work. T010 gates all module endpoints.
- **US1 (Phase 3)**: needs Phase 2. It is the MVP.
- **US2 (Phase 4)**: needs T011/T012 (API host) and T022 (page shell) from US1. It adds one endpoint and one button, and can be validated alone with curl plus the page.
- **US3 (Phase 5)**: needs US2's `api.ts` (T026) and page state pattern (T027). T029/T030 can start right after Phase 2.
- **US4 (Phase 6)**: mirrors the final Sample1 files, so it runs after US3. T035, T036, T038, T039, T040, T041 can run in parallel.
- **Polish (Phase 7)**: after the stories you want to ship.

### User Story Dependencies

- US1 → US2 → US3 → US4 (sequential: each story extends the same sample1 files, and US4 copies the result).
- US1 alone is a usable increment: install, nav and page appear, and disable/enable/uninstall behave correctly.

### Within Each Story

- API endpoint before the page button that calls it; shared helper (`ModuleGuards`) before endpoints; build/typecheck before manual quickstart checks.

## Parallel Opportunities

- Phase 1: T002, T004, T005, T006 in parallel after T001.
- Phase 2: T010 in parallel with T007–T009.
- US1: T013, T014, T017 in parallel; T020 and T021 in parallel once T015 is done.
- US3: T029 and T030 can start alongside US1/US2 (SharedKernel only).
- US4: T035, T036, T038, T039, T040, T041 in parallel.
- Polish: T045, T048 in parallel.

## Parallel Example: User Story 4

```bash
Task: "T035 Create src/Modules/Sample2/Sample2.Api/Program.cs identical to Sample1"
Task: "T038 Create src/Modules/Sample2/Sample2.Api/appsettings.Development.json"
Task: "T039 Create modules/sample2/manifest.yml from contracts/manifest-sample2.yml"
Task: "T040 Create modules/sample2/web/vite.config.ts (sample2_remote, :5177)"
Task: "T041 Create modules/sample2/web/src/api.ts"
```

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 → Phase 2 (stop and resolve if the T009 spike fails; pick the fallback) → Phase 3.
2. **Validate**: quickstart scenarios 1, 2, 9, 10, 11 with sample1.

### Incremental Delivery

1. US1: the module appears after install (MVP).
2. US2: the open data button.
3. US3: the permission-gated button with server enforcement.
4. US4: Module 2 copy plus the independence checks.
5. Polish: containers, docs, the full quickstart.

## Notes

- Postgres is required for any live check. Docker Desktop had disk I/O errors in the previous session, so restart it first.
- Use a single tenant for testing: gateway module enable/disable state is global (research R9.1).
- The registry is immutable: re-publishing the same version returns a duplicate error. Bump `module.version` if a manifest must change after publishing.
