# Autonomous Spec Kit Workflow

## Purpose

When the user requests a feature, bug fix, enhancement, refactor, or other implementation task, execute the complete Spec Kit lifecycle autonomously.

The user should provide the requirement once.

Do not require the user to manually invoke each Spec Kit command.

## Workflow

Execute:

SPECIFY
→ PLAN
→ TASKS
→ ANALYZE
→ IMPLEMENT
→ VERIFY
→ CONVERGE
→ DOCUMENT
→ DONE

Use `/speckit.clarify` only when a material ambiguity cannot be safely resolved from the repository, existing requirements, architecture, or reasonable engineering assumptions.

Do not stop for routine approval between stages.

---

## 1. SPECIFY

Run the equivalent of:

`/speckit.specify <user requirement>`

Create or update the feature specification.

The specification must describe:

- User goals
- Business requirements
- Functional requirements
- Non-functional requirements
- User stories
- Acceptance criteria
- Edge cases
- Constraints
- Out-of-scope behavior

Focus on WHAT and WHY.

Do not prematurely design the implementation.

### Autonomous clarification

Inspect the repository and existing documentation before asking questions.

If ambiguity can be safely resolved from:

- Existing code
- Existing patterns
- AGENTS.md
- Existing specifications
- Existing API contracts
- Existing database schema
- Project conventions

resolve it automatically.

Document important assumptions in the specification.

Ask the user only when an unresolved decision would materially change business behavior, security, data integrity, or the public contract.

---

## 2. PLAN

After specification succeeds, immediately continue to planning.

Run the equivalent of:

`/speckit.plan`

Read all applicable:

- Root `AGENTS.md`
- Backend `AGENTS.md`
- Frontend `AGENTS.md`
- Relevant Skills
- Existing architecture

Inspect the actual codebase before designing the solution.

The plan must identify:

- Architecture
- Components affected
- Backend changes
- Frontend changes
- API changes
- Database changes
- Security considerations
- Error handling
- Observability
- Testing strategy
- Migration considerations
- Backward compatibility

Prefer existing project patterns over introducing new architecture.

---

## 3. TASKS

After planning succeeds, immediately generate tasks.

Run the equivalent of:

`/speckit.tasks`

Tasks must:

- Be dependency ordered.
- Be independently verifiable where possible.
- Reference concrete files or components.
- Include tests where required.
- Include backend/frontend integration work.
- Include database migration work when applicable.
- Include documentation work.
- Include final verification.

Mark independent tasks as parallelizable.

---

## 4. SUBAGENT DELEGATION

Before implementation, inspect the generated task graph.

If subagents can complete independent tasks faster, create subagents.

Examples:

Backend Agent
- .NET implementation
- API
- database
- integration tests

Frontend Agent
- React implementation
- UI
- API integration
- frontend tests

Test/Review Agent
- test coverage
- regression analysis
- security review
- contract verification

Documentation Agent
- API documentation
- architecture documentation
- feature documentation

Only parallelize tasks without blocking dependencies.

Do not allow multiple agents to modify the same files concurrently unless explicitly coordinated.

Every subagent must read the applicable `AGENTS.md` files before working.

The primary agent remains responsible for integration and final correctness.

---

## 5. ANALYZE

Run the equivalent of:

`/speckit.analyze`

Validate consistency across:

- spec.md
- plan.md
- tasks.md
- constitution
- repository architecture

Check for:

- Missing requirements
- Missing implementation tasks
- Contradictions
- Unhandled edge cases
- Missing tests
- Security gaps
- API contract inconsistencies
- Database migration risks

### Automatic repair

If analysis identifies a problem that can be safely corrected:

1. Update the appropriate artifact.
2. Regenerate downstream artifacts when necessary.
3. Run analysis again.

Repeat until no material inconsistencies remain.

Do not begin implementation with known critical inconsistencies.

---

## 6. IMPLEMENT

Run the equivalent of:

`/speckit.implement`

Execute tasks in dependency order.

Parallelize independent tasks using subagents when beneficial.

For every task:

1. Read relevant existing code.
2. Implement the smallest complete change.
3. Follow existing architecture.
4. Preserve unrelated behavior.
5. Add or update tests.
6. Run targeted verification.
7. Mark the task complete only after verification.

Never mark unfinished work complete.

---

## 7. VERIFY

After implementation, perform repository-level verification.

### Backend

When applicable run:

- restore
- build
- unit tests
- integration tests
- analyzers
- formatting/lint checks

For .NET projects this commonly includes:

`dotnet restore`

`dotnet build`

`dotnet test`

Use repository-specific commands when they differ.

### Frontend

Detect the package manager from the repository.

Run applicable:

- dependency validation
- lint
- typecheck
- unit tests
- component tests
- production build
- end-to-end tests

For visual changes, perform screenshot or browser-based validation when tooling is available.

### Integration

Verify:

Frontend
→ API
→ Business Logic
→ Database

Validate important:

- Success paths
- Failure paths
- Authorization
- Validation
- Edge cases
- Backward compatibility

Never claim a verification succeeded unless it actually ran.

---

## 8. CONVERGE

After verification run:

`/speckit.converge`

Compare the implementation against:

- Specification
- Plan
- Tasks
- Constitution
- Acceptance criteria

If convergence identifies missing work:

1. Allow the missing tasks to be added to tasks.md.
2. Implement those tasks.
3. Verify them.
4. Run converge again.

Loop:

IMPLEMENT
→ VERIFY
→ CONVERGE

until the feature converges or a genuine blocker prevents completion.

Do not stop simply because the original tasks.md was completed.

---

## 9. DOCUMENTATION

Documentation is part of implementation, not optional cleanup.

After the implementation converges, inspect what changed and update applicable documentation.

Examples:

- README
- API documentation
- OpenAPI/Swagger
- Architecture documentation
- Database documentation
- Configuration documentation
- Environment variables
- Deployment instructions
- Developer setup
- Feature documentation
- Important design decisions

Do not create unnecessary documentation files.

Update existing documentation when an appropriate document already exists.

Documentation must describe the final implementation rather than the original plan.

---

## 10. FINAL DIFF REVIEW

Before declaring completion:

Run or inspect:

`git status`

`git diff`

Verify:

- No accidental changes
- No secrets
- No debug code
- No temporary files
- No commented-out implementation
- No unrelated formatting changes
- No unfinished TODOs introduced by this task
- Generated artifacts are intentional

---

## 11. Completion

The workflow completes only when:

- Specification is complete.
- Plan matches the specification.
- Tasks cover the plan.
- Analyze reports no critical inconsistencies.
- Implementation is complete.
- Relevant tests pass.
- Builds succeed.
- Convergence finds no remaining required implementation.
- Documentation reflects the implementation.
- Final diff has been reviewed.

If something cannot be completed because of an external blocker, stop and report the exact blocker rather than pretending the workflow completed.

---

## Autonomous Execution Rule

Once the user provides an implementation requirement, continue through the entire workflow without requesting routine approval.

Do NOT stop after:

- specify
- plan
- tasks
- analyze
- implementation

Continue automatically to the next stage.

Only interrupt the user for:

1. A business decision that cannot reasonably be inferred.
2. Destructive or irreversible operations requiring approval.
3. Missing credentials or external access.
4. A security-sensitive decision requiring explicit authorization.
5. A genuine blocker that prevents further progress.

Otherwise make the safest reasonable assumption, document it, and continue.

---

## Final Response

When finished, report:

### Implemented
What was implemented.

### Specification
Where the Spec Kit artifacts are located.

### Architecture
Important implementation decisions.

### Files
Important files changed.

### Subagents
Subagents actually used and their contributions.

### Verification
Commands/tests actually executed and their results.

### Documentation
Documentation created or updated.

### Assumptions
Important assumptions made automatically.

### Remaining Issues
Only genuine remaining issues or blockers.