# Frontend AGENTS.md — React Engineering Standards

## 1. Mandatory Preparation

Before modifying frontend code:

1. Read the repository-root `AGENTS.md`.
2. Read this file completely.
3. Read any additional instructions in affected directories.
4. Read relevant Skills before implementation.
5. Inspect the application structure, routes, components, dependencies, package manager, and scripts.
6. Inspect existing API clients, state management, design system, styles, and tests.
7. Check Git status and preserve unrelated changes.

Follow the root instructions for task planning, subagent delegation, security, verification, and reporting.

## 2. React Architecture

- Follow the installed React version and existing project conventions.
- Reuse existing components, hooks, utilities, and design-system elements.
- Keep components focused and maintainable.
- Separate complex business logic from presentation where appropriate.
- Use custom hooks for reusable stateful behavior when consistent with the codebase.
- Avoid unnecessary global state and duplicated state.
- Preserve existing routes, navigation, and business behavior.
- Avoid unrelated refactoring.

## 3. React and TypeScript

- Follow the project's JavaScript or TypeScript conventions.
- Follow React's Rules of Hooks.
- Use stable keys for rendered collections.
- Avoid unnecessary effects, duplicated derived state, and avoidable re-renders.
- Clean up subscriptions, event listeners, and asynchronous effects appropriately.
- Avoid stale closures and race conditions.
- Handle loading, empty, error, and success states when relevant.
- Prefer explicit types for important shared contracts.
- Avoid `any` unless justified.
- Remember that TypeScript types do not validate untrusted runtime data.

## 4. UI and Styling

- Preserve the existing design system, typography, colors, spacing, and responsive behavior.
- Reuse existing Tailwind CSS utilities and shared components where applicable.
- Avoid adding another styling framework without a clear need.
- Support relevant screen sizes and accessible interactions.
- Provide appropriate labels, semantic HTML, keyboard navigation, and focus states.
- Preserve business logic when implementing designs or mockups.
- Avoid unrelated visual changes.
- Match the supplied design as closely as possible without unnecessary rewrites.

For mockup-driven tasks:
1. Inspect the mockup and existing pages.
2. Identify reusable components and shared layout patterns.
3. Implement the smallest set of changes.
4. Compare screenshots before and after when tooling is available.
5. Fix meaningful visual discrepancies without changing unrelated functionality.

## 5. API Integration

- Reuse existing API clients, authentication, and request utilities.
- Follow existing request, response, and error-handling conventions.
- Handle loading, cancellation, stale responses, and duplicate submissions where appropriate.
- Avoid duplicating backend business rules in the frontend.
- Treat API responses as potentially invalid or incomplete.
- Never expose server-side secrets in client code or bundled environment variables.
- Coordinate API contract changes with backend implementations.

## 6. Performance and Reliability

- Avoid unnecessary component re-renders and expensive calculations.
- Use memoization only when it provides a meaningful benefit.
- Avoid loading large datasets into the browser unnecessarily.
- Consider pagination, virtualization, and lazy loading where appropriate.
- Clean up asynchronous operations and event subscriptions.
- Handle network failures and unexpected API responses gracefully.

## 7. Testing and Verification

Inspect the project's package manager, lockfile, and available scripts before running commands. Do not assume npm is used.

Run relevant checks where available:
- Unit and component tests.
- ESLint.
- TypeScript type checking.
- Production build.
- End-to-end tests.
- Screenshot comparison for visual changes.

Verify:
- Existing functionality remains intact.
- Loading, empty, error, and success states behave correctly.
- Responsive layouts remain usable.
- Accessibility is not regressed.
- API integrations use the correct contract.

Never claim a check passed unless it actually ran successfully.

## 8. Frontend Subagents

Use subagents when they can materially accelerate independent frontend work, such as investigating separate pages, reviewing shared components, analyzing UI regressions, or writing independent tests.

For cross-stack tasks, coordinate with backend workstreams and agree on API contracts before parallel implementation.

Before delegation:
- Provide the relevant paths, objective, constraints, and expected output.
- Require the subagent to read the root and frontend instructions.
- Avoid concurrent modifications to the same components or shared files.
- Review findings and changes before integration.

## 9. Completion Report

Report:
- UI or behavior implemented.
- Important files changed.
- API contract or shared component implications.
- Subagents actually used, if any.
- Tests, builds, linting, and visual checks actually executed.
- Remaining risks and unverified behavior.
