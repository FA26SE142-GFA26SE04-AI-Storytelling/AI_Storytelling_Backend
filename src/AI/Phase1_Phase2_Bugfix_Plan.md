# Phase 1–2 Bugfix Execution Plan

## Objective

Close the seven Bugbot findings across the accepted-input handoff, asynchronous outline generation and human-review workflow without implementing Phase 3 content generation.

## Case-by-case changes

### 1. Runtime schema compatibility

- Create incremental migration `AddPhase2OutlineWorkflow` after `AddRefreshAndEmailVerificationTokens`.
- Add job operation/status/idempotency/traceability/concurrency/lease fields and outline approval audit fields.
- Backfill legacy Phase 1 handoff jobs with valid enum strings, request/user links, retry limits and 32-character concurrency tokens.
- Resolve duplicate active jobs, current versions, version numbers and idempotency keys before creating unique indexes.
- Keep database application as a separate explicit deployment step.

### 2. Abandoned Processing jobs

- Write a lease expiration when a worker claims an outline job.
- Allow a worker to reclaim only `OutlineGenerating` jobs whose lease has expired.
- Clear the lease when generation completes or is finalized as failed.
- Preserve optimistic concurrency so only one claimant wins.

### 3. Initial generation retry

- Add `POST /api/v1/stories/{storyId}/outline/retry`.
- Require `GenerateStory`, an AI draft, no outline version and a prior failed initial job.
- Persist a new durable job and require a caller-provided idempotency key.

### 4. Edit/approve race

- Acquire a PostgreSQL transaction-level advisory lock scoped to the Story.
- Reload and validate current version, permissions and active jobs only after the lock is held.
- Apply the same serialization boundary to edit, regenerate, retry, approve and reject.
- Create approval audit data and the exact-version Phase 3 handoff in the same transaction.

### 5. Policy term matching

- Persist effective category codes and human-language match terms in the immutable Context Snapshot.
- Resolve organization/personal policy precedence before generating term lists.
- Use snapshot terms in AI constraints and the Core defense-in-depth guardrail.
- Fall back to category codes for snapshots created before this change.

### 6. AI error contract

- Return HTTP 422 plus a stable `errorCode` for output-safety rejection.
- Parse non-success AI responses in the Core HTTP adapter and throw a typed exception.
- Persist the upstream safety reason code in `StoryGenerationJob.ErrorCode`.

### 7. Failure finalization after rollback

- Move failure persistence to `IOutlineJobFailureFinalizer`.
- Resolve a fresh `ApplicationDbContext` scope for failure updates.
- Match the claimed concurrency token before changing the job, preventing an old worker from overwriting a recovered job.

## Verification gates

- Both Core and AI APIs compile with zero errors.
- Unit tests cover expired-lease recovery, initial retry idempotency, Vietnamese policy terms, typed AI errors and failure finalization.
- EF model tests cover Phase 2 indexes and lease metadata.
- All solution tests pass.
- `dotnet ef migrations has-pending-model-changes` returns no pending model changes.
- Migration is generated but is not applied automatically.
