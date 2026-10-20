# AGENTS.md — Repository-Wide Agent Instructions

## 1. Mandatory Preparation

Before starting any task, read this file completely and follow its instructions.

1. Read the applicable `AGENTS.md` files for the directories involved.
2. Read relevant Skills and project-specific instructions before implementation.
3. Inspect the repository structure, Git status, existing code, and tests.
4. Understand the task, acceptance criteria, dependencies, and potential regressions.
5. Identify which applications and components are affected.
6. Plan the smallest complete change before editing code.

Do not begin implementation before completing the required preparation.

If an instruction file or Skill is missing or unreadable, report the issue. Continue following the instructions that are available without inventing missing content.

## 2. Subagent Orchestration

**If a subagent can help complete the task faster, create and use a subagent.**

Before substantial work, evaluate whether the task can be divided into independent workstreams.

Use subagents when beneficial for:
- Investigating independent services, modules, or applications.
- Analyzing backend and frontend implementations in parallel.
- Reviewing API contracts and their consumers.
- Investigating independent bugs or performance issues.
- Writing or reviewing independent test suites.
- Performing security reviews or architectural research.
- Reviewing changes independently.

### Delegation rules

1. Define a specific objective and expected deliverable for each subagent.
2. Require each subagent to read this root file and applicable directory instructions before working.
3. Delegate independent investigations in parallel when possible.
4. Prefer read-only investigation when multiple agents work concurrently.
5. Assign clear file ownership before allowing parallel modifications.
6. Avoid having multiple agents modify the same files simultaneously.
7. Establish shared API contracts before parallel backend/frontend implementation.
8. Review subagent findings and code changes before integrating them.
9. Run appropriate verification against the combined result.
10. If subagents are unavailable, continue using the best supported workflow.

Do not create subagents for trivial tasks when delegation overhead exceeds the benefit. Never claim a subagent was used unless it actually ran.

## 3. Engineering Principles

- Understand existing architecture before making changes.
- Prefer minimal, focused, maintainable changes.
- Preserve existing behavior unless explicitly requested otherwise.
- Follow existing naming, formatting, testing, and architectural conventions.
- Reuse existing components, utilities, services, and abstractions.
- Avoid unnecessary dependencies, duplicate implementations, and unrelated refactoring.
- Consider backward compatibility, security, performance, reliability, and maintainability.
- Do not overwrite unrelated user changes.
- Do not commit, push, deploy, or perform destructive operations without authorization.

## 4. Cross-Stack Changes

For tasks affecting multiple applications or layers:

1. Identify all affected components and their dependencies.
2. Inspect the existing API contracts and data flow.
3. Confirm validation, authorization, error handling, and compatibility requirements.
4. Delegate independent investigations to subagents when beneficial.
5. Agree on shared contracts before parallel implementation.
6. Coordinate file ownership and integration.
7. Update relevant tests and documentation.
8. Verify the complete integration, not just individual components.

Consider deployment order and backward compatibility when changing shared contracts.

## 5. Security and Reliability

- Follow least-privilege principles.
- Never commit credentials, tokens, passwords, or secrets.
- Do not expose sensitive data in logs, errors, or telemetry.
- Validate inputs and enforce authorization on the server.
- Consider concurrency, timeouts, retries, duplicate requests, and partial failures.
- Never disable security controls merely to make tests pass.
- Report material security risks and unresolved issues.

## 6. Verification

Before declaring a task complete:

1. Run relevant tests and static checks when feasible.
2. Validate behavior against acceptance criteria.
3. Inspect the Git diff for unintended changes.
4. Check affected callers and dependent components.
5. Review important error paths and edge cases.
6. Confirm unrelated changes were not introduced.
7. Report checks that could not be executed and explain why.

Never fabricate test results or claim checks passed without running them.

## 7. Final Response

Summarize:
- What changed and why.
- Important files created or modified.
- Subagents actually used and their contributions, if any.
- Tests and checks actually executed and their results.
- Remaining risks, limitations, or unverified behavior.

Keep the response proportional to the task.

## Definition of Done

A task is complete when the requested outcome is implemented or the investigation is fully reported, applicable instructions have been followed, changes have been reviewed, relevant verification has been performed where feasible, and remaining limitations have been disclosed.
