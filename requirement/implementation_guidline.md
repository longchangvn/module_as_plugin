# Feature Implementation Agent

## Objective

Implement the requested feature based on the provided requirement in:

* **Backend:** .NET (`api-dotnet`)
* **Admin UI:** ReactJS and related components

## Instructions

1. **Confirm the Requirement**

   * First, identify and confirm the **requirement file name** before doing any implementation.
   * Read and fully understand the requirement, including business rules, API behavior, validations, edge cases, and UI expectations.
   * Inspect the existing codebase to understand the current architecture and implementation patterns.

2. **Validate Implementability**

   * Determine whether the requirement is technically implementable in the existing system.
   * Identify any ambiguity, missing information, conflicting requirements, or decisions that cannot be determined from the codebase.
   * **Do not make assumptions. Ask for approval before proceeding whenever clarification or a business decision is required.**

3. **Plan the Implementation**

   * Identify all affected components, APIs, models, services, database changes, and ReactJS Admin UI components.
   * Consider existing reusable code and patterns before creating new implementations.
   * Choose the simplest design that satisfies the requirement.

4. **Implement**

   * Implement the feature in the appropriate `.NET` API and ReactJS Admin UI components.
   * Preserve existing business logic and backward compatibility unless the requirement explicitly requires a change.
   * Reuse existing components, services, utilities, and patterns whenever possible.
   * Avoid unnecessary abstractions, duplicated logic, or over-engineering.
   * Keep the implementation clean, readable, maintainable, and minimal.

5. **Code Quality**

   * Before implementing, consider multiple reasonable implementation approaches.
   * Select the approach that provides the best balance of:

     * Minimal code
     * Reusability
     * Maintainability
     * Consistency with the existing architecture
     * Correctness
   * Do not introduce a new pattern or abstraction unless it provides clear value.
   * Keep business logic in the appropriate layer and avoid duplicating the same logic between API and UI.

6. **Verification**

   * Review the implementation against every requirement.
   * Check edge cases and existing functionality that could be affected.
   * Run relevant tests/build/lint/type checks where available.
   * Fix issues discovered during verification.

## Before Coding

Do **not** start implementation immediately.

First provide:

1. Confirmed requirement file name.
2. Your understanding of the requirement.
3. Whether the requirement is implementable.
4. Any ambiguities or decisions requiring approval.
5. Proposed implementation approach.
6. List of files/components expected to change.
7. consider existing opensource library instead of self implement
8. consider multiple implement solution and choose the best option with: less code, simple in architecture and easy to understand
If anything is unclear or requires a business decision, **stop and ask for approval before modifying code.**

## After Implementation

Provide a concise summary containing:

### Changes Made

* Backend/API changes
* Database/data changes, if any
* ReactJS Admin UI changes
* Shared/reusable component changes
* Tests added or updated

### Verification

* Build/test results
* Requirement coverage
* Any remaining limitations or follow-up items

Do not modify unrelated code or files.

# Coding Agent — Implementation & Documentation Guideline

## 1. Documentation Is Required

Every implementation must include appropriate documentation.

**No implementation is considered complete until the related documentation has been updated.**

Documentation must accurately reflect the implemented behavior, architecture, APIs, configuration, and business rules.

## 2. Before Implementation

Before changing code:

- Understand the requirement completely.
- Identify existing documentation related to the feature.
- Review the current architecture and implementation.
- Determine which documents will be affected.
- If the requirement is unclear or conflicts with existing behavior, ask for clarification before implementing.

## 3. During Implementation

For every implementation, document changes that affect:

- Business behavior or business rules
- API contracts
- Request/response models
- Database schema or data relationships
- Authentication/authorization
- Configuration
- Integration with external systems
- Events/messages
- Background jobs
- Architecture or component responsibilities
- Deployment or operational behavior
- Breaking or backward-incompatible changes

Do not create unnecessary documentation for trivial code changes.

## 4. Documentation Location

Prefer updating existing documentation rather than creating duplicate documents.

Use the project's established documentation structure and conventions.

If no appropriate document exists, create one in the project's standard documentation location.

Documentation should be easy for another developer to discover and understand without reading the entire codebase.

## 5. What to Document

Depending on the implementation, update the appropriate documentation such as:

### Requirements
- What the feature does
- Business rules
- Supported scenarios
- Validation rules
- Edge cases
- Acceptance criteria

### API
- Endpoint
- HTTP method
- Request
- Response
- Error responses
- Authentication/authorization
- Idempotency behavior
- Important business rules

### Architecture
- Components involved
- Data flow
- Dependencies
- Integration points
- Important design decisions
- Trade-offs

### Database
- Tables
- Columns
- Relationships
- Indexes
- Constraints
- Migration requirements

### Configuration
- New settings
- Environment variables
- Default values
- Required/optional settings
- Environment-specific considerations

### Operations
- Deployment requirements
- Feature flags
- Monitoring/logging
- Troubleshooting
- Rollback considerations

## 6. Keep Documentation Consistent With Code

After implementation:

1. Compare the implementation against the documentation.
2. Ensure examples and API contracts are accurate.
3. Remove outdated information.
4. Do not document behavior that is not actually implemented.
5. Do not leave TODOs for documentation unless explicitly approved.

**Code and documentation must describe the same system behavior.**

## 7. Implementation Summary

Every completed implementation should provide a summary containing:

- What was changed
- Why it was changed
- Files/components changed
- API/database/configuration changes
- Documentation updated
- Tests added or updated
- Known limitations or risks
- Any follow-up work

## 8. Validation Before Completion

Before declaring the task complete, verify:

- [ ] Requirement is implemented.
- [ ] Existing functionality is not unintentionally broken.
- [ ] Tests are added/updated where appropriate.
- [ ] Documentation is updated.
- [ ] API/documentation matches the actual implementation.
- [ ] Database documentation matches migrations/schema.
- [ ] Configuration documentation matches actual configuration.
- [ ] No obsolete documentation remains.
- [ ] No undocumented significant behavior was introduced.

## 9. Important Rule

**Documentation is part of the implementation, not a separate optional task.**

A feature is considered **DONE only when both code and required documentation are complete and consistent.**
