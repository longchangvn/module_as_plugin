# Feature Specification: Sample Test Modules

**Feature Branch**: `001-sample-test-modules`

**Created**: 2026-10-19

**Status**: Draft

**Input**: User description: "create 2 sample module so that i can test install with 1 simple ui: home=> module1 (include 1 button i can click to call to api retrieve sample json content and button 2 with permission if permission existing then show otherwise disable). same for module 2."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Install a sample module and see it appear (Priority: P1)

A platform tester publishes the two sample modules to the module catalog, installs **Sample Module 1** for a test tenant, signs in as a user of that tenant, and sees "Sample Module 1" appear in the navigation next to Home. Opening it shows the module's page without any rebuild or redeploy of the platform.

**Why this priority**: The whole point of the samples is to prove the install lifecycle end to end. Without a visible result after install, nothing else can be tested.

**Independent Test**: Publish and install Sample Module 1 only for a fresh tenant, sign in as that tenant's user, and confirm the Home page links to the module and the module page opens.

**Acceptance Scenarios**:

1. **Given** Sample Module 1 is published but not installed for the tenant, **When** a tenant user opens Home, **Then** no entry for Sample Module 1 is shown.
2. **Given** a tenant admin installs Sample Module 1 from the catalog, **When** the tenant user refreshes Home, **Then** an entry for Sample Module 1 is shown and opening it displays the module page.
3. **Given** Sample Module 1 is installed, **When** it is disabled, **Then** its entry disappears from navigation and its data calls are refused as "module disabled"; **When** re-enabled, **Then** it returns without reinstalling.
4. **Given** Sample Module 1 is installed, **When** it is uninstalled, **Then** its entry, page, and data calls are no longer available and its seeded permissions are removed.

---

### User Story 2 - Fetch sample data with the open button (Priority: P1)

On a sample module page, any signed-in user of the tenant clicks **"Get sample data"** and sees the sample content returned by that module's service, displayed as readable formatted JSON.

**Why this priority**: Confirms that requests travel from the user interface, through the platform's single entry point, to the installed module's own service and back.

**Independent Test**: With the module installed, click "Get sample data" and confirm the module's sample content (including the module name and a timestamp) is shown.

**Acceptance Scenarios**:

1. **Given** the module is installed and enabled, **When** the user clicks "Get sample data", **Then** a loading indicator appears and is replaced by the module's sample content within 2 seconds.
2. **Given** the module's service is unreachable, **When** the user clicks "Get sample data", **Then** a clear error message is shown in place of the content and the rest of the page keeps working.
3. **Given** Sample Module 1 and Sample Module 2 are both installed, **When** the user fetches sample data on each page, **Then** each page shows content that clearly identifies its own module.

---

### User Story 3 - Permission-gated button (Priority: P2)

On a sample module page, a second button, **"Get protected data"**, is enabled only when the signed-in user holds that module's protected permission. Without the permission, the button is visible but disabled, with a hint naming the missing permission. When enabled and clicked, it retrieves protected sample content from the module's service.

**Why this priority**: Demonstrates permission seeding on install, granting through roles or direct grants, and UI gating backed by server-side enforcement. It depends on Story 1 being in place.

**Independent Test**: With the module installed, sign in as a user without the protected permission (button disabled), grant the permission through a role, sign in again (button enabled), and click it to see protected content.

**Acceptance Scenarios**:

1. **Given** the user does not hold the module's protected permission, **When** the module page opens, **Then** "Get protected data" is shown disabled with a hint such as "Requires SAMPLE_ONE_DATA_PROTECTED".
2. **Given** the user holds the permission through a role or a direct grant, **When** the module page opens, **Then** "Get protected data" is enabled, and clicking it shows the protected sample content.
3. **Given** a user without the permission calls the protected data directly, bypassing the page, **When** the request reaches the module, **Then** it is refused with a "forbidden" error and no protected content is returned.
4. **Given** a user's permission is revoked, **When** they reload the module page, **Then** the button is disabled again and direct protected calls are refused.

---

### User Story 4 - Two independent modules (Priority: P3)

Sample Module 2 behaves exactly like Sample Module 1 (same page layout, same two buttons) but has its own name, its own navigation entry, its own sample content, and its own protected permission, so the two can be installed, enabled, disabled, and uninstalled independently.

**Why this priority**: Proves isolation between modules, so that one module's state or permissions never affect the other.

**Independent Test**: Install both modules, grant only Sample Module 2's protected permission, and confirm only Module 2's protected button is enabled. Then uninstall Module 1 and confirm Module 2 is unaffected.

**Acceptance Scenarios**:

1. **Given** both modules are installed, **When** the user holds only Module 2's protected permission, **Then** Module 2's protected button is enabled and Module 1's is disabled.
2. **Given** both modules are installed, **When** Module 1 is uninstalled, **Then** Module 2's entry, buttons, and data remain fully functional.

---

### Edge Cases

- A module is disabled while its page is open: the next button click shows a "module disabled" message instead of content.
- A module is uninstalled while its page is open: the next click shows a "module not available" message, and navigation drops the entry after refresh.
- The user's session expires: a button click leads to sign-in rather than a broken page.
- A module's page assets fail to load: Home and the other module keep working, and the failed module shows an "unavailable" card.
- The user double-clicks a button: only one request is in flight per button, and the result reflects the latest response.
- A user signed in under tenant A cannot retrieve sample or protected data for tenant B.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The deliverable MUST include two sample modules, "Sample Module 1" and "Sample Module 2", each publishable to the module catalog and installable per tenant through the existing install, enable, disable, uninstall, and upgrade lifecycle.
- **FR-002**: Each sample module MUST declare one navigation entry under the tenant's Home that opens that module's page. The entry MUST appear only while the module is installed and enabled for the tenant.
- **FR-003**: Each module page MUST show the module name, the installed version, and two buttons: "Get sample data" and "Get protected data".
- **FR-004**: "Get sample data" MUST be available to any signed-in user of a tenant that has the module installed and enabled. It MUST retrieve sample content from that module's own service and display it as formatted JSON.
- **FR-005**: The sample content MUST identify the module (name and version), include a server-generated timestamp, and include a small list of illustrative records so the response is visibly distinct per module.
- **FR-006**: Each module MUST seed, on install, one protected permission unique to it (`SAMPLE_ONE_DATA_PROTECTED` for Module 1, `SAMPLE_TWO_DATA_PROTECTED` for Module 2). It MUST also seed one role that grants that permission ("Sample 1 Viewer" and "Sample 2 Viewer").
- **FR-007**: "Get protected data" MUST be enabled only when the signed-in user's effective permissions (role permissions plus direct grants) include the module's protected permission. Otherwise it MUST be shown disabled with a hint naming the missing permission.
- **FR-008**: The module service MUST enforce the protected permission on every protected request, independently of the page. Requests without it MUST be refused with a "forbidden" error.
- **FR-009**: Every data call MUST go through the platform's single entry point, never directly to the module's service. Calls to a disabled module MUST be refused as "module disabled", and calls to an uninstalled module as "unknown module".
- **FR-010**: Each module page MUST show distinct states for loading, success (content), and failure (error message with the reason) for each button, independently of the other button.
- **FR-011**: Each sample module MUST follow the standard module template: a module service, a module user interface, and a module manifest. It MUST pass the platform's manifest validation on publish.
- **FR-012**: Uninstalling a sample module MUST remove its navigation entry, its seeded permission, and its seeded role grants per existing rule R6, without affecting the other sample module.
- **FR-013**: The two modules MUST have no dependency on each other; each MUST install, enable, disable, and uninstall independently.
- **FR-014**: The deliverable MUST include step-by-step test instructions: publish both, install, sign in, grant or revoke the permission, and disable/uninstall. Each step MUST state its expected observable result.

### Key Entities

- **Sample Module**: An installable plugin with a name, display name, version, one navigation entry, one page, sample data and protected data it can serve, one seeded protected permission, and one seeded role.
- **Sample Content**: Read-only illustrative data returned by a module, including module identity, version, a timestamp, and a short list of records. Protected content is additionally marked as protected.
- **Protected Permission**: A permission code unique to each module that controls the second button and the protected data endpoint.
- **Seeded Role**: A role created on install that grants the module's protected permission, used to test granting through roles.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A tester can go from "both modules published" to "Sample Module 1 visible and working for a tenant" in under 3 minutes, following only the provided instructions.
- **SC-002**: 100% of the acceptance scenarios in User Stories 1–4 pass when executed manually against a running platform.
- **SC-003**: "Get sample data" shows content within 2 seconds for 95% of clicks on a local environment.
- **SC-004**: In 100% of tested cases, the protected button's enabled state matches the user's actual permission after a page reload, and server-side refusal occurs in 100% of direct unauthorized calls.
- **SC-005**: Installing, disabling, or uninstalling one sample module changes nothing visible or callable in the other module (0 cross-module side effects observed).
- **SC-006**: No platform redeploy or user interface rebuild is needed at any point between publishing, installing, and using either module.

## Assumptions

- "Home" means the home page of the existing **Tenant Admin portal**, where tenant users already sign in. That portal gains the ability to show installed modules: navigation entries and module pages built from installed and enabled modules. No new portal is created. (The platform operator portal has no tenant sign-in, so it cannot host tenant module pages.)
- The two modules are independent (no dependency between them). Install-dependency testing is out of scope; the existing lifecycle already covers it in automated tests.
- Sample content is static or generated in memory. No module database or persistent business data is needed.
- The "permission exists" check for the second button uses the signed-in user's effective permissions from the existing permission service. A reload after a grant or revoke is acceptable for the button state to update.
- "Disabled" means visible but not clickable, with a hint. The button is not hidden.
- Testers use the existing tenant admin portal to assign the seeded role or a direct permission grant. No new permission-management screens are required.
- The existing Loyalty sample module stays unchanged. The new modules use their own names, ports, and paths so all three can coexist.
- The modules run locally alongside the existing services. Production hosting, CDN publishing, and visual polish beyond a clean, simple layout are out of scope.
