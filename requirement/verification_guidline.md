# Requirement Implementation Verification Guidelines

## 1. Purpose

This guideline defines how to verify that an **existing implementation correctly satisfies a requirement**.

The objective is not to create a test plan. The objective is to inspect the implementation and determine:

> **Does the current implementation fully and correctly implement the requirement?**

Verification must use three levels of implementation inspection:

1. **Black Box** — verify what the implemented system actually does from the outside.
2. **Gray Box** — verify the implementation across services, APIs, integrations, configuration, and persistence.
3. **White Box** — inspect the source code and internal logic to verify that the requirement is correctly implemented.

When the requirement involves data persistence, database verification MUST include both:

* **Database schema**
* **Actual database data/state**

---

# 2. Verification Principle

Do not ask:

> "Does the code look correct?"

Ask:

> "What evidence demonstrates that every part of this requirement is implemented correctly?"

The verification must identify:

* What the requirement requires
* Where each requirement rule is implemented
* How the implementation behaves
* What data is affected
* What dependencies are involved
* What happens in success and failure scenarios
* Whether anything required by the requirement is missing
* Whether the implementation contains behavior that contradicts the requirement

---

# 3. Verification Levels

## 3.1 Black-Box Implementation Verification

Verify the implemented system from its external behavior.

Focus on:

* API behavior
* Input validation
* Output
* HTTP status
* Business behavior
* State transitions
* Error behavior
* Authorization behavior
* Observable side effects

Do not rely on source-code assumptions.

### Questions

* Does the implementation produce the required result?
* Does it reject invalid input correctly?
* Does it return the required error?
* Does it enforce required business rules?
* Does it produce the required side effects?
* Does it behave correctly for boundary conditions?
* Does it prevent behavior explicitly prohibited by the requirement?

---

# 4. Gray-Box Implementation Verification

Verify how the implementation works across internal components without inspecting every line of code.

Focus on:

```text
API
 ↓
Controller / Handler
 ↓
Service
 ↓
Repository
 ↓
Database
```

And where applicable:

```text
Service
 ↓
External Service
```

```text
Service
 ↓
Queue / Event
 ↓
Consumer
```

Verify:

* Correct component is used
* Correct service is called
* Correct dependency is called
* Correct data is passed between components
* Correct configuration is used
* Correct database operation occurs
* Correct event is produced
* Correct external API is called
* Errors propagate correctly
* Transactions occur at the correct boundary

---

# 5. White-Box Implementation Verification

Inspect the actual source code.

The objective is to determine whether the implementation **contains the complete business logic required by the requirement**.

Verify:

### Business Logic

* Every business rule is implemented.
* Conditions match the requirement.
* No required condition is missing.
* No extra condition incorrectly prevents valid behavior.
* Default behavior is correct.
* State transitions are correct.
* All relevant branches are handled.

### Control Flow

Inspect:

* `if / else`
* `switch`
* loops
* early returns
* exception branches
* retry branches
* transaction boundaries
* async behavior

Determine whether any path can bypass required business logic.

### Error Handling

Verify:

* Expected errors are handled.
* Unexpected errors do not produce incorrect state.
* Errors are mapped correctly.
* Transactions are rolled back when required.
* Errors contain sufficient diagnostic information.
* Sensitive information is not exposed.

### Data Handling

Verify:

* Correct fields are read.
* Correct fields are written.
* Correct transformations are applied.
* Null handling is correct.
* Default values are correct.
* Data types are appropriate.
* Precision is preserved.
* Time zones are handled correctly.

---

# 6. Requirement-to-Code Traceability

For every requirement, map each requirement rule to its implementation.

Use:

| Requirement Rule | Implementation Location | Implementation Behavior | Status |
| ---------------- | ----------------------- | ----------------------- | ------ |
| Rule 1           | `service.py:123`        | Checks user status      | PASS   |
| Rule 2           | `repository.py:45`      | Updates account         | PASS   |
| Rule 3           | —                       | Not implemented         | FAIL   |

Every significant requirement statement should have an implementation mapping.

### Important

If you cannot identify where a requirement is implemented, do not assume it is implemented.

Mark it:

**NOT FOUND / BLOCKED**

depending on whether the source code is available.

---

# 7. Implementation Completeness

For each requirement determine whether the implementation covers:

```text
Requirement
    │
    ├── Main behavior
    ├── Preconditions
    ├── Validation
    ├── Business rules
    ├── State changes
    ├── Persistence
    ├── Integration
    ├── Authorization
    ├── Error handling
    ├── Edge cases
    └── Observability
```

A requirement is not fully implemented if only the main happy path exists while required supporting behavior is missing.

---

# 8. Database Implementation Verification

If the implementation interacts with a database, perform BOTH:

```text
Implementation
      │
      ├── Schema Verification
      │
      └── Data Verification
```

## 8.1 Schema Verification

Verify the implementation against the actual database schema.

Check:

* Table
* Column
* Data type
* Nullable
* Default
* Primary key
* Foreign key
* Unique constraint
* Check constraint
* Index
* Enum/reference values
* Audit columns

Determine whether the implementation correctly matches the schema.

### Example

Requirement:

> A user can have multiple roles.

Verify:

```text
User
  ↓
UserRole
  ↓
Role
```

Do not accept an implementation that stores only a single `role_id` if the requirement explicitly requires multiple roles.

---

# 9. Database Data Verification

Inspect actual persisted data when possible.

Verify:

### Create

Does the implementation create the required record?

### Update

Does it update the correct record and only the intended fields?

### Delete / Deactivate

Does it correctly remove or deactivate the record according to the requirement?

### Relationships

Are relationships correctly persisted?

### Data Integrity

Check:

* Duplicate records
* Orphan records
* Incorrect foreign keys
* Invalid states
* Missing required fields
* Incorrect timestamps
* Incorrect tenant IDs
* Incorrect ownership

---

# 10. Transaction Verification

If the requirement requires multiple changes to be atomic, verify the implementation's transaction boundary.

Example:

```text
Requirement:
Create User + Assign Role
must succeed or fail together.
```

Verify:

```text
BEGIN
  Create User
  Assign Role
COMMIT
```

and:

```text
BEGIN
  Create User
  Assign Role → FAIL
ROLLBACK
```

Look specifically for implementations that accidentally do:

```text
Create User
COMMIT

Assign Role
FAIL
```

which leaves partial state.

---

# 11. API Implementation Verification

For API requirements verify:

### Contract

* HTTP method
* Path
* Version
* Request schema
* Response schema
* Required headers
* Authentication
* Authorization
* Status codes
* Error format

### Implementation

Trace:

```text
HTTP Request
    ↓
Router
    ↓
Controller
    ↓
Validation
    ↓
Authorization
    ↓
Service
    ↓
Repository
    ↓
Database
```

Confirm that no required logic is skipped.

---

# 12. Authorization Verification

Do not treat authentication as authorization.

For requirements involving roles or permissions verify:

```text
User
 ↓
Authentication
 ↓
Identity
 ↓
Tenant
 ↓
Role
 ↓
Permission
 ↓
Resource
 ↓
Action
```

Verify:

* Permission is actually checked.
* Correct permission is checked.
* Correct tenant is checked.
* Resource ownership is checked where required.
* Users cannot manipulate IDs to bypass authorization.
* Inactive users/roles/permissions are handled correctly.

---

# 13. Multi-Tenant Verification

For multi-tenant requirements, explicitly inspect tenant propagation.

Verify:

```text
Request
 ↓
Authenticated User
 ↓
Tenant Context
 ↓
Service
 ↓
Repository
 ↓
Database Query
```

The tenant restriction must not exist only at the UI/API layer.

Verify that database queries cannot accidentally return another tenant's data.

Example:

```sql
SELECT *
FROM users
WHERE id = :user_id
  AND tenant_id = :tenant_id;
```

A query such as:

```sql
SELECT *
FROM users
WHERE id = :user_id;
```

may represent a security defect if `user_id` is not globally unique or tenant isolation is required.

---

# 14. Integration Verification

If the requirement involves another service, verify the complete implementation flow.

Check:

* Correct endpoint
* Correct HTTP method
* Correct authentication
* Correct request payload
* Correct response handling
* Timeout
* Retry
* Error handling
* Idempotency
* Correlation ID
* Failure behavior

Verify both:

```text
Success
External Service → 2xx
```

and:

```text
Failure
External Service → timeout / 4xx / 5xx
```

Determine whether the implementation behaves according to the requirement in both cases.

---

# 15. Event / Queue Verification

If events or queues are involved, verify:

* Event creation
* Event type
* Payload
* Headers
* Routing
* Queue/topic
* Consumer
* Retry
* Dead-letter behavior
* Duplicate processing
* Ordering if required
* Idempotency

Trace:

```text
Business Operation
      ↓
Database Change
      ↓
Event
      ↓
Queue
      ↓
Consumer
      ↓
Downstream Action
```

Verify that the implementation does not produce an inconsistent state between the database and event system.

---

# 16. Error Path Verification

Do not verify only the happy path.

For every major business operation inspect:

```text
Success
Validation Failure
Authentication Failure
Authorization Failure
Not Found
Conflict
Dependency Failure
Database Failure
Timeout
Unexpected Exception
Retry
Duplicate Request
```

For each path determine:

* Correct response
* Correct state
* Correct transaction behavior
* Correct logging
* Correct retry behavior

---

# 17. Edge-Case Implementation Verification

Look for implementation gaps around:

* `null`
* Empty string
* Empty collection
* Zero
* Negative values
* Maximum values
* Duplicate requests
* Concurrent requests
* Missing records
* Deleted records
* Inactive records
* Expired records
* Invalid state
* Partial failure
* Retry

Do not require an edge case if it is genuinely irrelevant to the requirement.

---

# 18. Idempotency Verification

For operations that can be retried, verify whether the requirement requires idempotency.

Example:

```text
POST /users
```

If the same request is processed twice:

```text
Request 1 → User created
Request 2 → ?
```

Determine whether the implementation:

* Creates duplicate data
* Returns existing result
* Rejects duplicate
* Uses an idempotency key

Verify against the actual requirement.

---

# 19. Concurrency Verification

When multiple requests can modify the same resource, inspect:

* Race conditions
* Locking
* Optimistic concurrency
* Version fields
* Lost updates
* Duplicate processing
* Transaction isolation

Example:

```text
Request A → Read Balance = 100
Request B → Read Balance = 100
Request A → Write 150
Request B → Write 120
```

Determine whether the implementation can produce incorrect final state.

---

# 20. Observability Verification

Where the requirement requires operational visibility, verify implementation of:

* Logs
* Metrics
* Traces
* Correlation ID
* Request ID
* Error details
* Audit records
* Business events

Verify that sensitive information is not logged.

---

# 21. Implementation vs Requirement Gap Analysis

For every requirement compare:

```text
Requirement
     ↓
Expected Implementation
     ↓
Actual Implementation
     ↓
Gap
```

Classify the result:

### Fully Implemented

All required behavior exists and is correctly implemented.

### Partially Implemented

Some required behavior exists, but one or more rules are missing or incorrect.

### Incorrectly Implemented

The implementation exists but behaves differently from the requirement.

### Not Implemented

No implementation exists.

### Cannot Verify

Required source/database/system evidence is unavailable.

---

# 22. Verification Result

Use the following status:

| Status              | Meaning                                                |
| ------------------- | ------------------------------------------------------ |
| **PASS**            | Implementation fully satisfies the requirement         |
| **PARTIAL**         | Implementation satisfies only part of the requirement  |
| **FAIL**            | Implementation contradicts or violates the requirement |
| **NOT IMPLEMENTED** | Required functionality is missing                      |
| **BLOCKED**         | Cannot verify due to missing evidence/access           |
| **N/A**             | Verification category does not apply                   |

---

# 23. Implementation Verification Record

Use this template for every requirement.

```markdown
## REQ-XXX — [Requirement Name]

### Requirement

[Original requirement]

### Implementation Summary

[Brief description of how the current implementation attempts to satisfy the requirement.]

### Requirement Rules

| # | Required Behavior | Implementation Location | Status |
|---|---|---|---|
| 1 | | | |
| 2 | | | |
| 3 | | | |

### Black-Box Verification

| Area | Expected | Actual | Status |
|---|---|---|---|
| Input | | | |
| Output | | | |
| Business Behavior | | | |
| Error Behavior | | | |
| State Change | | | |

### Gray-Box Verification

| Component | Expected | Actual | Status |
|---|---|---|---|
| API | | | |
| Service | | | |
| Repository | | | |
| External Service | | | |
| Event / Queue | | | |
| Configuration | | | |

### White-Box Verification

| Area | Expected | Actual | Status |
|---|---|---|---|
| Business Logic | | | |
| Control Flow | | | |
| Validation | | | |
| Error Handling | | | |
| Authorization | | | |
| Concurrency | | | |
| Idempotency | | | |

### Database Schema Verification

| Object | Expected | Actual | Status |
|---|---|---|---|
| Table | | | |
| Column | | | |
| Primary Key | | | |
| Foreign Key | | | |
| Constraint | | | |
| Index | | | |

### Database Data Verification

| Data | Expected | Actual | Status |
|---|---|---|---|
| Record | | | |
| Values | | | |
| Relationships | | | |
| State | | | |
| Audit Fields | | | |

### Security Verification

| Area | Expected | Actual | Status |
|---|---|---|---|
| Authentication | | | |
| Authorization | | | |
| Tenant Isolation | | | |
| Data Exposure | | | |

### Implementation Gaps

| Gap | Severity | Evidence | Recommendation |
|---|---|---|---|
| | | | |

### Final Result

**PASS / PARTIAL / FAIL / NOT IMPLEMENTED / BLOCKED / N/A**

### Evidence

- Source:
- API:
- Database:
- Tests:
- Configuration:
- Logs:
```

---

# 24. Multi-Requirement Summary

When reviewing multiple requirements, maintain one summary:

| Req ID  | Requirement | Black Box | Gray Box | White Box | DB Schema | DB Data | Security | Final   |
| ------- | ----------- | --------- | -------- | --------- | --------- | ------- | -------- | ------- |
| REQ-001 |             | PASS      | PASS     | PASS      | N/A       | N/A     | PASS     | PASS    |
| REQ-002 |             | PASS      | PARTIAL  | FAIL      | PASS      | FAIL    | PASS     | FAIL    |
| REQ-003 |             | PASS      | BLOCKED  | BLOCKED   | BLOCKED   | BLOCKED | N/A      | BLOCKED |

---

# 25. Final Implementation Verification Report

After reviewing all requirements, produce:

## Summary

```text
Total Requirements:
Fully Implemented:
Partially Implemented:
Failed:
Not Implemented:
Blocked:
```

## Critical Implementation Gaps

List the highest-impact issues first.

For each:

```text
Requirement:
Problem:
Expected:
Actual:
Evidence:
Impact:
Recommended Fix:
Severity:
```

## Architecture / Design Issues

Identify issues that affect multiple requirements, such as:

* Incorrect service ownership
* Incorrect authorization architecture
* Missing tenant isolation
* Incorrect transaction boundary
* Incorrect data model
* Missing abstraction
* Incorrect API versioning
* Missing idempotency
* Incorrect event architecture

## Database Issues

Separate:

### Schema Issues

### Data Issues

### Integrity Issues

### Transaction Issues

## Security Issues

Identify:

* Authentication gaps
* Authorization gaps
* Tenant isolation gaps
* Data exposure
* Privilege escalation
* Audit gaps

## Test Gaps

Identify tests that should exist to prove the implementation.

---

# 26. Final Decision Rule

The implementation can be marked **PASS** only when:

```text
Every requirement rule
        ↓
Has an implementation
        ↓
Implementation behavior is correct
        ↓
Required branches are handled
        ↓
Required data changes are correct
        ↓
Database schema is correct
        ↓
Database state is correct
        ↓
Security is correctly enforced
        ↓
Integration behavior is correct
        ↓
Failure behavior is correct
        ↓
Evidence is available
        ↓
PASS
```

If any required functionality is missing:

**NOT IMPLEMENTED / PARTIAL**

If the implementation exists but violates the requirement:

**FAIL**

If implementation cannot be inspected or verified:

**BLOCKED**

---

# 27. Core Rule

> **Do not verify whether the implementation "looks reasonable." Verify whether the implementation provides every behavior required by the requirement, and identify concrete evidence for each behavior.**

The verification should always answer four questions:

1. **Where is this requirement implemented?**
2. **Does the implementation correctly implement every rule?**
3. **Does the resulting system/data state match the requirement?**
4. **What evidence proves the conclusion?**

```
```
