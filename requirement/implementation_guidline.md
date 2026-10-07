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
