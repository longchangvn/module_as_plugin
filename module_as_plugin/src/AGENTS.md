# Backend AGENTS.md — .NET Engineering Standards

## 1. Mandatory Preparation

Before modifying backend code:

1. Read the repository-root `AGENTS.md`.
2. Read this file completely.
3. Read any additional instructions in affected directories.
4. Read relevant Skills before implementation.
5. Inspect the target framework, solution/project structure, dependencies, and existing conventions.
6. Inspect relevant controllers, endpoints, services, domain logic, data access, configuration, and tests.
7. Check Git status and preserve unrelated changes.

Follow the root instructions for task planning, subagent delegation, security, verification, and reporting.

## 2. Architecture

- Follow the existing architecture and dependency boundaries.
- Keep controllers and endpoint handlers focused on HTTP concerns.
- Place business rules in the appropriate application or domain layer.
- Keep infrastructure concerns separate from business logic.
- Use dependency injection and appropriate service lifetimes.
- Reuse existing services, repositories, validators, and shared components.
- Avoid introducing new architectural patterns without a concrete benefit.

## 3. C# and .NET

- Follow the project's target framework, language version, and conventions.
- Use asynchronous APIs for I/O-bound work.
- Propagate `CancellationToken` where appropriate.
- Avoid `.Result`, `.Wait()`, and unnecessary synchronous blocking.
- Handle resource disposal and asynchronous disposal correctly.
- Use nullable reference types and appropriate input validation.
- Avoid swallowing exceptions or using exceptions for normal control flow.
- Avoid unbounded concurrency and unmanaged fire-and-forget tasks.
- Follow existing logging and exception-handling conventions.

## 4. API Design

- Preserve existing routes, HTTP methods, status codes, and response formats unless explicitly requested otherwise.
- Validate requests at the appropriate boundary.
- Follow existing API versioning and documentation conventions.
- Use consistent error responses.
- Apply authentication and authorization policies to protected operations.
- Never trust client-supplied identities, tenant identifiers, or permissions without validation.
- Consider pagination, idempotency, rate limits, timeouts, and compatibility where relevant.
- Update relevant tests and API contracts when behavior changes.

## 5. Database and Data Access

- Inspect the schema, relationships, ORM mappings, queries, indexes, and transaction boundaries.
- Use parameterized queries and safe ORM APIs.
- Avoid N+1 queries and unnecessary database round trips.
- Consider concurrent updates, locks, transaction isolation, and deadlocks.
- Review migration safety, table size, index creation, and deployment compatibility.
- Use transactions when required by consistency rules.
- Avoid loading large datasets into memory unnecessarily.
- Consider execution plans and existing indexes for performance-sensitive queries.
- Never execute destructive production database operations without explicit authorization.
- Do not assume a migration is safe simply because it works on a small local dataset.

## 6. Dependency Injection and Configuration

- Use the existing configuration providers and environment conventions.
- Never hardcode secrets or credentials.
- Validate required configuration at startup where appropriate.
- Avoid singleton services capturing scoped dependencies.
- Use appropriate HTTP client and connection-pooling patterns.
- Preserve environment-specific behavior.

## 7. Security and Observability

- Enforce authorization on the server.
- Validate and sanitize untrusted inputs.
- Avoid SQL injection, insecure deserialization, path traversal, and unsafe redirects.
- Never log passwords, tokens, secrets, or sensitive personal data unnecessarily.
- Preserve existing tracing, metrics, and structured logging conventions.
- Consider failure visibility, correlation IDs, and useful diagnostic context.

## 8. Testing and Verification

Inspect the repository's existing build and test commands before running checks.

Use applicable checks, such as:

- `dotnet restore`
- `dotnet build`
- `dotnet test`
- Existing analyzers, formatters, and integration tests

Prefer targeted tests first, then broader verification when warranted.

Test:
- Success and failure paths.
- Validation and authorization.
- Boundary conditions and important edge cases.
- Database behavior and transaction boundaries when relevant.
- Backward compatibility for changed APIs.

Never claim a check passed unless it actually ran successfully.

## 9. Backend Subagents

Use subagents when they can materially accelerate independent backend work, such as investigating separate services, reviewing database risks, analyzing API contracts, or writing independent tests.

Before delegation:
- Provide the relevant paths, objective, constraints, and expected output.
- Require the subagent to read the root and backend instructions.
- Avoid conflicting edits to shared files.
- Review findings and changes before integration.

## 10. Completion Report

Report:
- Backend behavior implemented or investigated.
- Important files changed.
- Database or API contract implications.
- Subagents actually used, if any.
- Tests and checks executed with actual results.
- Remaining risks and unverified behavior.
