# Feature Specification: Module Hosting Types (Standalone + In-Process Plugin)

**Feature Branch**: `002-module-hosting-types`

**Created**: 2026-10-20

**Status**: Draft

**Input**: User description: "There are 2 different types of module, designed so I can add a different type later easily without changing the code or with minimum code change. 1. Different hosting module: API hosted in a different standalone host; think about server-to-service communication (same cluster, different cluster, public API). 2. Plugin module: installed in the same host that is already running and runs in-process as a plugin (1 host can run multiple tenants; a module is only available if the tenant has that module installed). When registering a module, allow uploading a zip file; inside the zip check the manifest for the module type; if it's a plugin, extract and load the DLL for the plugin. Register the gateway and make sure the plugin works without downtime. Think and propose how to manage multiple tenants installing a plugin without impacting other tenants. Think of a .NET shell to see if each tenant is a shell (all available modules, shared dependencies and services). The CLI must reflect this requirement too."

## Overview

Today every module is a separately hosted service. Operators need a second way to deliver modules: a **plugin** that is uploaded as a package and runs inside a shared, already-running module host that serves many tenants. Adding a third delivery type later (for example a serverless function or a container that the platform starts on demand) must not require reworking the install lifecycle.

Terms used in this spec:

- **Module type**: how a module's backend is delivered and reached. Initial types: **remote** (standalone host, today's behaviour) and **plugin** (runs inside the shared module host).
- **Module package**: a single zip file that carries a module's contract (manifest) and, for plugins, the backend binaries and optional UI assets.
- **Tenant shell**: the per-tenant view of the shared module host. It is the set of plugin modules (and their versions) that a tenant has installed and enabled, composed on top of dependencies and services shared by every tenant.
- **Network zone** (remote modules only): where the standalone host lives relative to the platform: same cluster, different cluster, or public internet.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register a module by uploading a package (Priority: P1)

A platform administrator registers a module by uploading one zip file. The platform reads the manifest inside the package, determines the module type, validates the package against the rules for that type, and adds the version to the module catalog. Remote modules can still be registered the existing way (manifest + service URL), so nothing that works today breaks.

**Why this priority**: Package upload is the entry point for plugins and the single most visible change. Without it no plugin can reach the platform.

**Independent Test**: Upload a valid remote package and a valid plugin package; both appear in the catalog with their type. Upload broken packages; each is rejected with a field-level reason and nothing is stored.

**Acceptance Scenarios**:

1. **Given** an administrator and a zip containing a valid plugin manifest and the backend binary it names, **When** they upload it, **Then** the version is registered with type "plugin", its package fingerprint is recorded, and it is listed in the catalog as installable.
2. **Given** a zip containing only a valid remote manifest whose service address is declared, **When** it is uploaded, **Then** the version is registered with type "remote" and behaves exactly like a module registered the existing way.
3. **Given** a zip whose manifest is missing, unparseable, declares an unknown module type, or names a plugin binary that is not in the package, **When** it is uploaded, **Then** it is rejected with a reason naming the failing field or file and nothing is stored.
4. **Given** a zip with an entry that tries to escape the extraction folder, exceeds the size or entry-count limits, or duplicates a version that is already registered, **When** it is uploaded, **Then** it is rejected and nothing is stored.
5. **Given** a non-administrator (Maintainer or tenant user), **When** they try to upload a package, **Then** the request is refused.
6. **Given** an administrator in the platform console, **When** they choose "Register module", **Then** they can either upload a package file or paste a manifest, and validation errors are shown inline.

---

### User Story 2 - Install a plugin for one tenant without affecting others (Priority: P1)

An administrator installs a registered plugin for a tenant. The shared module host loads that plugin version (once, even if many tenants use it), and the module's API becomes reachable through the gateway only for tenants that have it installed and enabled. Other tenants, other plugins, and the host itself keep serving requests throughout.

**Why this priority**: This is the core value of the plugin type: a single shared host, tenant-scoped availability, and no downtime.

**Independent Test**: With two tenants, install the plugin for tenant A only. Tenant A's users reach the plugin's API; tenant B's users get "not installed". A continuous request loop against another module and against the host's health check sees no failures during the install.

**Acceptance Scenarios**:

1. **Given** a registered plugin and tenants A and B, **When** the plugin is installed for A, **Then** A's requests to the plugin's API succeed and B's requests are answered "module not installed".
2. **Given** the plugin is installed for A, **When** it is also installed for B, **Then** the host reuses the already-loaded plugin version and both tenants are served.
3. **Given** the plugin is installed and enabled for A and B, **When** an administrator disables it for A, **Then** A gets "module disabled", B is unaffected, and no reload of the host happens.
4. **Given** the plugin is installed for A and B, **When** it is uninstalled for A, **Then** A gets "module not installed", B keeps working, and the plugin stays loaded because B still uses it; when the last tenant uninstalls it, the plugin is unloaded from the host.
5. **Given** a plugin version that fails to load (for example a corrupt binary or a missing dependency), **When** it is installed for a tenant, **Then** that install is marked failed with the reason, and every other tenant and plugin keeps working.
6. **Given** requests in flight to a plugin version, **When** that version is unloaded or replaced, **Then** in-flight requests complete normally before the old version is released.
7. **Given** the module host restarts, **When** it comes back, **Then** it restores every tenant's installed plugins from the platform's records without operator action.
8. **Given** the plugin package includes UI assets, **When** a tenant with the plugin installed opens the tenant console, **Then** the plugin's pages appear in navigation and load, with no separate UI hosting step.

---

### User Story 3 - Upgrade a plugin per tenant (side-by-side versions) (Priority: P2)

An administrator upgrades a plugin for one tenant to a newer version while other tenants stay on the older version. Both versions run side by side in the shared host; each tenant is routed to its own version.

**Why this priority**: Per-tenant upgrades are what make "install without impacting other tenants" true over time, but initial installs (US2) deliver value first.

**Independent Test**: Register v1.0.0 and v1.1.0 of a plugin; install v1.0.0 for A and B; upgrade A to v1.1.0. A's responses report v1.1.0, B's report v1.0.0.

**Acceptance Scenarios**:

1. **Given** A and B on v1.0.0, **When** A is upgraded to v1.1.0, **Then** A is served by v1.1.0 and B by v1.0.0, with no failed requests for either during the switch.
2. **Given** A was the last tenant on v1.0.0, **When** A moves to v1.1.0, **Then** v1.0.0 is unloaded after its in-flight requests finish.
3. **Given** the new version fails to load, **When** the upgrade runs, **Then** the tenant stays on its previous working version and the upgrade is reported as failed.

---

### User Story 4 - Standalone modules declare how they are reached (Priority: P2)

A module developer publishing a standalone (remote) module declares which network zone it runs in (same cluster, different cluster, or public) and its service address. The platform enforces the transport rules for that zone and routes gateway traffic accordingly.

**Why this priority**: Standalone modules already work; this story makes the communication model explicit and safe for cross-cluster and public deployments.

**Independent Test**: Register remote manifests for each zone with secure and insecure addresses; insecure addresses outside the same cluster are rejected outside development; existing manifests without a zone keep working as "same cluster".

**Acceptance Scenarios**:

1. **Given** a remote manifest without a declared zone, **When** it is registered, **Then** it is treated as "same cluster" and behaves as today.
2. **Given** a remote manifest declaring "different cluster" or "public" with a non-encrypted service address, **When** it is registered outside development, **Then** it is rejected with a reason naming the address field.
3. **Given** a remote manifest declaring a service address, **When** it is registered without a separately supplied address, **Then** the declared address is used for gateway routing.

---

### User Story 5 - Developer tooling for both module types (Priority: P2)

A developer uses the module CLI to scaffold either a remote or a plugin module, build a package for it, and publish it. The CLI chooses the right template, wiring, and publish path for the chosen type.

**Why this priority**: The CLI is how developers adopt the plugin type; it must not lag behind the platform.

**Independent Test**: Scaffold a plugin module with the CLI, package it, upload it, install it for a tenant, and call its API through the gateway.

**Acceptance Scenarios**:

1. **Given** the CLI, **When** a developer creates a module and selects the plugin type, **Then** a plugin project, manifest declaring the plugin type, UI remote, and parity test are generated, and no standalone host or container entry is created.
2. **Given** a scaffolded module of either type, **When** the developer runs the package command, **Then** a single zip is produced containing the manifest and, for plugins, the backend binaries (excluding shared platform libraries) and built UI assets.
3. **Given** a package, **When** the developer runs publish, **Then** the CLI uploads the package; for remote modules without a package the existing manifest publish still works.
4. **Given** a developer lists modules, **When** the list is shown, **Then** each module shows its type.

---

### User Story 6 - Add a new module type with minimal change (Priority: P3)

A platform engineer adds a third module type. They implement the type's rules (manifest validation, how its backend is reached, how it is activated and deactivated per tenant) and register it. The install, enable, disable, upgrade, and uninstall flows, catalog, and gateway are not modified.

**Why this priority**: It is a design property that protects future work; it is verified by structure and tests rather than delivered to end users.

**Independent Test**: A test registers a fake third type and runs the install lifecycle against it without any change to lifecycle code.

**Acceptance Scenarios**:

1. **Given** a new type registered with the platform, **When** a manifest declaring it is validated and installed, **Then** the type's own validation, routing target, and activation are used and no lifecycle code changes were needed.
2. **Given** a manifest that declares a type the platform does not know, **When** it is registered, **Then** it is rejected and the error lists the supported types.

### Edge Cases

- Two administrators upload the same module version at the same moment: exactly one succeeds; the other is told the version already exists.
- A plugin package bundles its own copy of a shared platform library: the host uses the shared copy so that contracts stay compatible, and the plugin's private dependencies stay isolated from other plugins.
- Two plugins depend on different versions of the same third-party library: each loads its own version without conflict.
- A plugin declares a contract version the host does not support: the install fails with a clear reason; nothing else is affected.
- The module host is unreachable during install: the install is marked failed and retryable; no gateway route is published for that tenant.
- The gateway route publish fails after the plugin was activated: the install is marked failed and the tenant is not served; other tenants are unaffected.
- A tenant is disabled while it has plugins installed: its requests are refused as for remote modules.
- A request carries a tenant header that does not match the caller's tenant: refused, as for remote modules.
- A plugin registered through the existing manifest-only path (no package): rejected with "plugin modules must be registered by package upload".
- A package larger than the limit or containing executables outside the plugin folder: rejected.
- The host restarts while a plugin is being installed: on restart the host restores from platform records; an install left half-done is reconciled to the platform's recorded state.

## Requirements *(mandatory)*

### Functional Requirements

**Module types (extensibility)**

- **FR-001**: Every module version MUST have exactly one module type. Manifests that do not declare a type MUST be treated as "remote" so existing modules keep working unchanged.
- **FR-002**: The platform MUST support the types "remote" and "plugin" at launch, and MUST reject manifests declaring any other type with an error that lists the supported types.
- **FR-003**: Each module type MUST own its type-specific rules: manifest validation, how its backend is reached by the gateway, and how it is activated, deactivated, enabled, disabled, and migrated per tenant. Adding a type MUST NOT require changes to the install/enable/disable/upgrade/uninstall flow, the catalog, or the gateway.

**Package registration**

- **FR-004**: Administrators MUST be able to register a module version by uploading a single zip package that contains the manifest at its root.
- **FR-005**: The platform MUST read the module type from the packaged manifest and apply that type's package rules. For plugins the package MUST contain the backend binary named in the manifest; for remote modules a manifest-only package is valid.
- **FR-006**: Package upload MUST reject packages that: lack a manifest; have an invalid manifest; contain entries that would escape the extraction folder (absolute paths, parent-folder segments); exceed the configured total size, uncompressed size, or entry count; or re-use an already registered module version. A rejected package MUST leave no stored files or catalog rows.
- **FR-007**: The platform MUST record the package fingerprint (cryptographic hash) and module type with each registered version, keep the package for later retrieval by the module host, and expose the type in the catalog.
- **FR-008**: The existing manifest + service URL registration MUST keep working for remote modules and MUST reject plugin manifests.
- **FR-009**: Only platform administrators MUST be able to register modules by either path; every registration MUST be audited.
- **FR-010**: The platform console MUST let administrators register a module by uploading a package file as well as by pasting a manifest, show validation errors inline, and show each module's type in the catalog.

**Plugin hosting (shared multi-tenant host)**

- **FR-011**: The platform MUST provide a shared module host that runs plugin modules in-process and serves all tenants.
- **FR-012**: A plugin version MUST be loaded at most once in the host, regardless of how many tenants use it, and loaded plugin versions MUST be isolated from each other so that their private dependencies cannot conflict.
- **FR-013**: Shared platform contracts and framework libraries MUST be provided by the host, not by the plugin, so plugins built against the published contract interoperate with the host.
- **FR-014**: For each request to a plugin, the host MUST determine the caller's tenant from the authenticated identity and serve the request only if that tenant has the plugin installed and enabled; otherwise it MUST answer "module not installed" or "module disabled" with the same codes the gateway uses for remote modules.
- **FR-015**: The host MUST route each tenant to the plugin version that tenant has installed, allowing different tenants to run different versions of the same plugin side by side.
- **FR-016**: Installing, enabling, disabling, upgrading, or uninstalling a plugin for one tenant MUST NOT interrupt requests of other tenants, other plugins, or the host's own endpoints, and MUST NOT require a host restart.
- **FR-017**: The host MUST verify a plugin package's fingerprint against the platform's record before loading it, and MUST refuse plugins built for an unsupported contract version.
- **FR-018**: A plugin version MUST be activated in the host before its gateway route is published for a tenant, and the gateway route MUST be removed before the plugin is deactivated for that tenant, so no tenant is ever routed to a plugin that is not ready.
- **FR-019**: When no tenant uses a plugin version any longer, the host MUST release it after in-flight requests complete (bounded by a drain timeout).
- **FR-020**: A plugin that fails to load MUST fail only the install/upgrade that requested it; for upgrades the tenant MUST remain on its previous version.
- **FR-021**: On start-up and periodically, the host MUST reconcile its tenant shells with the platform's recorded installs so that restarts and missed updates self-heal.
- **FR-022**: When a plugin package contains UI assets, the host MUST serve them so the tenant console can load the plugin's pages, and the platform MUST advertise their location to tenants that have the plugin installed.
- **FR-023**: Plugins MUST be able to use the same tenant and permission guards available to remote modules, and permission checks MUST fail closed.

**Remote module connectivity**

- **FR-024**: Remote manifests MUST be able to declare a network zone ("same cluster", "different cluster", "public"; default "same cluster") and a service address.
- **FR-025**: Outside development, remote modules in the "different cluster" or "public" zones MUST use an encrypted service address; the platform MUST reject other addresses at registration.
- **FR-026**: When no service address is supplied at registration, the manifest's declared service address MUST be used for gateway routing.

**Developer tooling**

- **FR-027**: The module CLI MUST let developers choose the module type when scaffolding, generate the matching template (plugin project with no standalone host for plugins), and wire only what that type needs.
- **FR-028**: The CLI MUST build a package for a module (manifest; for plugins the backend binaries without shared platform libraries plus built UI assets) and MUST publish packages by upload; manifest-only publish MUST remain for remote modules.
- **FR-029**: The CLI module listing MUST show each module's type.

**Operations**

- **FR-030**: Every plugin load, unload, activation, deactivation, and load failure MUST be logged with module, version, and tenant, and the host MUST expose a read-only view of loaded plugin versions and tenant shells to operators.

### Key Entities

- **Module version**: an immutable registered version of a module; gains a module type, a package fingerprint, and a stored package (plugins).
- **Module package**: the uploaded zip; contains the manifest, plus for plugins a backend folder and optional UI folder.
- **Module type**: a named delivery model with its own validation, routing, and activation rules (initially remote and plugin).
- **Plugin runtime**: one loaded plugin version inside the shared host, isolated from other runtimes, shared by all tenants using that version.
- **Tenant shell**: for one tenant, the map of installed plugin modules to the runtime (version) serving them and whether each is enabled, layered over the host's shared services.
- **Network zone**: where a remote module's host runs (same cluster, different cluster, public), driving transport rules.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can register a plugin by uploading one file and install it for a tenant in under 2 minutes, with no host restart or redeploy.
- **SC-002**: During plugin install, enable, disable, upgrade, and uninstall for one tenant, 100% of requests from other tenants and to the host's health check succeed.
- **SC-003**: After install, a tenant's first successful call to the plugin's API happens within 10 seconds; the other tenant receives "module not installed" 100% of the time.
- **SC-004**: Two tenants on different versions of the same plugin are each served by their own version 100% of the time.
- **SC-005**: 100% of malformed or unsafe packages in the test suite (missing manifest, unknown type, missing binary, path escape, oversize, duplicate version) are rejected with no stored artifacts.
- **SC-006**: Adding a test-only third module type requires no edits to lifecycle, catalog, or gateway code (verified by a test that registers it and installs a module of that type).
- **SC-007**: Existing remote modules (loyalty, sample1, sample2) keep registering, installing, and serving traffic with no manifest changes.
- **SC-008**: A developer can scaffold, package, publish, and install a plugin module with the CLI using at most 4 commands.

## Assumptions

- "The same host as current running" means a shared, platform-operated **module host** process that is always running and serves every tenant. Plugins are not loaded into the platform control plane, the gateway, or the auth service, so a faulty plugin cannot take down catalog, login, or routing.
- Plugin code is trusted: only platform administrators can register packages, and packages are integrity-checked. In-process plugins are not sandboxed; modules from untrusted parties should use the remote type.
- Tenant shells share one loaded runtime per plugin version (shared dependencies and services) instead of building a full private copy of every plugin per tenant; this bounds memory as tenants grow. Plugins must therefore treat tenant identity as request data and must not keep cross-tenant state.
- Plugins keep the existing tenant seed (roles, permissions) and frontend contract unchanged; only the backend delivery differs.
- Plugin data migrations during upgrade run through an optional plugin hook; plugins without one are treated as additive, as remote modules are today.
- A single module host instance is in scope; running several host replicas behind a load balancer is supported by the reconcile design but not load-tested here.
- Service-to-service trust for cross-cluster and public remote modules continues to rely on the platform's signed user tokens; mutual TLS and private connectivity are deployment concerns documented as recommendations.
- Package limits default to 50 MB compressed, 200 MB uncompressed, and 2,000 entries, configurable by operators.

## Out of Scope

- Sandboxing or resource quotas for plugin code (CPU, memory per plugin).
- Starting or scaling standalone module hosts on demand.
- Signing packages with publisher certificates.
- Per-tenant private copies of plugin services (a full container per tenant per plugin).
- Plugin-owned databases provisioning; plugins use whatever storage they configure themselves.
