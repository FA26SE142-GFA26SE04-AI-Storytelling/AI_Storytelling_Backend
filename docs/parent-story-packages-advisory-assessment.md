# Parent Story Packages: Advisory AI Assessment & Acknowledgement

## Overview
For Parent Manual `/packages` (single-child or multi-child), deterministic backend checks (readability, language, length, blocked terms, consent, ownership, source/context consistency) and valid safety blockers (`CONTENT_SAFETY_NOT_PASSED`, `MODERATION_HARD_BLOCK`) remain blocking.

Gray-zone warnings (`ModerationZone.GrayZone`) and evaluator unavailability (`Unavailable` for Content Safety AI and/or Gray-Zone AI) are advisory:
- Valid `GrayZone` results become a parent warning with redacted `EvidenceQuote` items and do **not** create a new `ModerationItem`.
- Content Safety and Gray-Zone evaluators run independently; failure of one preserves the completed result of the other.
- If an evaluator is unavailable, `Check.Passed` remains `true` (eligible) as long as deterministic backend checks pass and no valid blocker exists.

## Status Vocabulary
`ParentPackageCheck` includes:
- `BackendPassed`: `bool?` (`null` for legacy checks)
- `SafetyStatus`: `Passed | Warning | Blocked | Unavailable | NotRun`
- `GrayZoneStatus`: `Passed | Warning | Blocked | Unavailable | NotRun`
- `Warnings`: `ParentPackageAssessmentWarning[]` (`source`, `code`, `reason`, `evidence`, `occurredAtUtc`)
- `RequiresParentAcknowledgement`: `bool`
- `Acknowledgement`: `ParentPackageAssessmentAcknowledgement?`

## Confirm Request & 409 Error
`POST /api/v1/parent/stories/{storyId}/packages/{packageId}/confirm` accepts `ParentPackageConfirmRequest`:
```json
{
  "operationId": "uuid",
  "packageToken": "current-token",
  "acknowledgeAssessmentWarnings": true
}
```
When `RequiresParentAcknowledgement` is `true` and `acknowledgeAssessmentWarnings` is `false` (or omitted), the endpoint returns `409 Conflict` with error code `PACKAGE_ASSESSMENT_ACKNOWLEDGEMENT_REQUIRED`.

When `acknowledgeAssessmentWarnings` is `true`, the backend persists `Acknowledgement` (`packageId`, `packageRevision`, `acknowledgedByUserId`, `acknowledgedAtUtc`, `sourceFingerprint`, `contextFingerprint`, `warnings`) inside `CheckResultJson` and the returned `ParentPackageDto` (stored in `StoryAuthoringOperation.OperationResponseJson`).

## Delivery, Revisions, and Legacy Behavior
- `POST /api/v1/parent/stories/{storyId}/family-confirmations/batch` verifies per-package eligibility, absence of package-specific `ModerationItem` holds (`Pending`, `Claimed`, `Blocked`), and an exact matching `Acknowledgement` for new-format packages requiring acknowledgement.
- Package revisions created via `revise` start with a fresh `CheckResultJson` and never inherit prior confirmations or acknowledgements.
- Legacy packages without the new `ParentPackageCheck` fields retain existing `Passed == true` behavior. Existing pending/claimed/blocked `ModerationItem` holds remain enforced under lock.
