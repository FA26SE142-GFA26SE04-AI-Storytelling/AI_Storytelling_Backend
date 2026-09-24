# Phase 2 AI Outline Generation and Human Review

## Runtime flow

Phase 1 persists a `StoryGenerationJob` with `Operation=GenerateOutline`, `Stage=OutlinePending` and `Status=Pending`. `OutlineGenerationWorker` polls the durable job and delegates one item at a time to `IOutlineJobProcessor`.

Core rebuilds `GenerateOutlineRequest` only from `AcceptedInputJson` and `ContextSnapshotJson`. It does not reload the creative form or replace the accepted snapshot with current child-profile values. The AI module resolves the prompt, requests strict structured output, performs bounded technical retry and applies output validation. Core validates the returned outline again before persisting business state.

On success, Core performs one transaction that retires the previous current version, creates an immutable `StoryVersion`, links the job, changes the Story to `OutlineReview`, and completes the job. `StoryVersion.Content` and `StoryVersion.Lesson` remain null throughout Phase 2.

## Human review API

- `GET /api/v1/stories/{storyId}/outline`
- `GET /api/v1/stories/{storyId}/outline/versions`
- `GET /api/v1/stories/{storyId}/outline/versions/{versionNo}`
- `PUT /api/v1/stories/{storyId}/outline/versions/{versionNo}`
- `POST /api/v1/stories/{storyId}/outline/versions/{versionNo}/regenerate`
- `POST /api/v1/stories/{storyId}/outline/retry`
- `POST /api/v1/stories/{storyId}/outline/versions/{versionNo}/approve`
- `POST /api/v1/stories/{storyId}/outline/versions/{versionNo}/reject`

Edit and regenerate require `GenerateStory` on the Story's child. Approval requires that the Story's child is under the active supervision of a Parent or Teacher account (explicit `ApproveStory` permission check is bypassed). Rejection requires the separate `ApproveStory` permission. Edit creates a new version. Regenerate creates a new durable outline job tied to the current base version. A stale AI result cannot replace a newer current version.

Approval records the exact approved StoryVersion and creates a `GenerateContent`/`ContentPending` job in the same transaction. The Phase 3 consumer is not implemented here.

## Safety and retries

The AI handler validates strict structured JSON, required fields, configured lengths, PII, blocked/restricted terms and prompt-leakage markers. Invalid JSON, provider timeout, HTTP 408/429 and 5xx are retried up to three attempts with exponential backoff. Schema/policy failures are not retried. Core preserves typed AI safety reason codes instead of reducing them to a generic HTTP failure.

Processing jobs carry a four-minute lease. A worker may reclaim an expired lease after a process crash, while normal generation failures are finalized through a fresh database scope so rollback tracking state cannot strand the job.

The configured semantic moderation provider from the approved design is not present in the repository. This implementation therefore supplies the rule-based layer and fails closed for its explicit policy decisions; a provider-backed `IOutlineOutputGuardrail` extension is still required before claiming the full hybrid guardrail is complete.

## Required migration

Migration `AddPhase2OutlineWorkflow` was created after `AddRefreshAndEmailVerificationTokens`. It adds the Phase 2 fields, lease and foreign keys on `story_generation_jobs`, approval audit fields on `story_versions`, filtered uniqueness constraints and idempotency indexes. Its data backfill converts legacy jobs to valid operation/status values, supplies concurrency tokens, resolves duplicate active jobs and normalizes duplicate current/version-number rows before adding unique indexes.

The migration has not been applied to a database in this change.
