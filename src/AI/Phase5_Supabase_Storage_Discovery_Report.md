# Phase 5 — Supabase Storage Pre-Coding Discovery Report

**Project:** AI Storytelling Platform for Children  
**Flow:** Luồng 2 — Guided AI Story Generation  
**Scope:** Core .NET backend, Phase 4 → Phase 5 handoff, media persistence and reader delivery  
**Discovery date:** 2026-09-17  
**Implementation performed:** No  
**Final readiness:** `READY_WITH_ARCHITECTURE_CHANGES`

## 1. Executive conclusion

Phase 5 foundation is already present and is strong enough to integrate object storage:

- Phase 4 approval creates a durable `GenerateMediaPackage` job pinned to the exact current `StoryVersion`.
- `StoryScene` exists and preserves canonical text ranges.
- Each scene has database protection for one illustration and one TTS asset slot.
- The media worker has lease, attempt count, concurrency token, stale-result checks and fail-closed providers.
- External generation/evaluation calls already happen after the short job-claim transaction is committed.

Supabase Storage cannot be added safely as configuration only. The codebase still needs:

1. A provider-neutral `IMediaStorage` abstraction.
2. Storage metadata fields on `MediaAsset` and a new EF migration.
3. A candidate media contract that exposes bytes/stream/content type, not only a provider URL.
4. Upload orchestration between validation and `MediaAsset.Ready`.
5. A signed-URL read service and reader DTO/API.
6. Explicit orphan-object recovery and storage error mapping.
7. Contract tests and an opt-in live Supabase integration test suite.

Recommended transport is **direct Supabase Storage REST through a typed `HttpClient`**, not the full Supabase C# client. This reuses the established Core infrastructure pattern, avoids bringing Auth/PostgREST/Realtime dependencies into a backend that needs only upload/sign/delete, and keeps raw Supabase failures inside Infrastructure.

No source code, package, appsettings, migration, environment variable or Supabase resource was changed during this discovery. Only this report was created.

## 2. Repository architecture (DS-00)

### Solution and projects

```text
StoryPlatform.sln / StoryPlatform.slnx
├── src/Core
│   ├── StoryPlatform.Domain
│   ├── StoryPlatform.Application
│   ├── StoryPlatform.Infrastructure
│   └── StoryPlatform.Api
├── src/AI
│   ├── StoryPlatform.AI.Domain
│   ├── StoryPlatform.AI.Application
│   ├── StoryPlatform.AI.Infrastructure
│   └── StoryPlatform.AI.Api
├── src/Shared
│   └── StoryPlatform.Contracts
└── tests
    ├── StoryPlatform.UnitTests
    ├── StoryPlatform.IntegrationTests
    ├── StoryPlatform.AI.UnitTests
    └── StoryPlatform.AI.IntegrationTests
```

- Domain project: `src/Core/StoryPlatform.Domain`
- Application project: `src/Core/StoryPlatform.Application`
- Infrastructure project: `src/Core/StoryPlatform.Infrastructure`
- API project: `src/Core/StoryPlatform.Api`
- Worker project: no separate worker project; hosted workers live in Core Infrastructure and run inside Core API.
- AI provider boundary: separate `src/AI` service, reached from Core through HTTP contracts.

Storage belongs in Core Infrastructure. The abstraction and provider-neutral request/result models belong in Core Application. Storage credentials and Supabase REST details must not cross into Domain or API DTOs.

## 3. Current media domain (DS-01, DS-02)

### `MediaAsset`

Current fields:

```text
Id                 int, inherited
StoryVersionId     int, required
StorySceneId       int?, nullable
SceneIndex         int?, nullable
Type               MediaType
Status             MediaStatus
Url                string?, max 500
WordTimings        string?
CreatedAt          DateTime, inherited
UpdatedAt          DateTime?, inherited
IsDeleted          bool, inherited
```

Missing storage metadata:

```text
StorageProvider
StorageBucket
StoragePath
MimeType
ProviderAssetId
Checksum
ByteSize
```

`Url` currently means “provider-returned media URL persisted as the ready asset location.” The media service writes `GeneratedIllustration.Url` and `GeneratedAudio.Url` directly into this field. No reader currently consumes it, so no existing code proves that it is public, signed, permanent or renewable. Tests use permanent-looking fake HTTPS URLs, which reinforces the current long-lived URL assumption.

Insert/update logic already exists in `MediaGenerationService.GetOrCreateAssetAsync`. It queries by `StorySceneId + Type`, creates a queued row, then moves it through `Processing`, `Ready` or `Failed`.

The database uniqueness rule is stronger than the candidate in the plan:

```text
UNIQUE(StoryVersionId, StorySceneId, Type)
WHERE StorySceneId IS NOT NULL AND IsDeleted = false
```

This protects one logical illustration and one logical TTS slot per scene. `SceneIndex` is copied into `MediaAsset`, but current media queries are primarily version/scene/type based; `SceneIndex` is not used as the asset identity.

### `StoryScene`

`StoryScene` exists with:

- exact `StoryVersionId` FK;
- stable `SceneIndex`;
- `TextRangeStart` / `TextRangeEnd`;
- canonical `SceneText`;
- optional `VisualDescription`;
- unique `(StoryVersionId, SceneIndex)`;
- check constraints for non-negative index and valid text range.

The service validates persisted scenes against the canonical story content before reuse. Phase 5 storage therefore must remain scene-aware and must not attach generated files directly to only `StoryVersion`.

## 4. Phase 4 → Phase 5 handoff (DS-03)

The handoff is durable and exact:

1. `ApproveAsync` obtains a transaction lock for the story.
2. It revalidates Phase 4 artifacts and authorization.
3. It loads the current story version with non-empty content.
4. It creates a `StoryGenerationJob` with both `StoryVersionId` and `BaseStoryVersionId` set to the approved version.
5. It assigns deterministic operation key `p5:{storyId}:{approvedVersionId}`.
6. It writes operation `GenerateMediaPackage`, stage `MediaPending`, status `Pending`.
7. It changes the story to `Approved` in the same database transaction.

The worker changes the story from `Approved` to `MediaProcessing` only after successfully claiming the job. There is no unsafe polling of all approved stories.

Freshness protection already checks:

- job is still `Processing`;
- concurrency token matches;
- pinned story version still exists and is current;
- job version equals the approved version;
- media context revision is still current;
- story remains `MediaProcessing` and is not archived.

The storage upload must run under these same checks immediately before upload and again before persisting the selected path.

## 5. Existing storage abstraction and packages (DS-04, DS-05)

### Abstraction search result

No existing abstraction or implementation was found for:

- `IStorage`, `IFileStorage`, `IMediaStorage`, `IBlobStorage`, `IObjectStorage`;
- upload/download/signed URL;
- S3, Azure Blob or Supabase Storage.

Creating a narrow `IMediaStorage` is justified; there is no reusable file service to extend.

### Package inventory

Core Infrastructure currently includes:

- `Microsoft.Extensions.Http` 8.0.1;
- `Microsoft.Extensions.Options.ConfigurationExtensions` 8.0.0;
- EF Core/Npgsql packages;
- no Supabase, PostgREST, S3 or Azure Blob package;
- no Polly package.

The Supabase C# ecosystem is community-maintained. In August 2026, the standalone `storage-csharp` repository was archived and its code moved into the `supabase-csharp` monorepo; the `Supabase.Storage` NuGet package remains available. This is not a blocker, but it increases dependency and maintenance surface compared with a three-operation REST adapter.

### SDK vs REST decision

| Option | Advantages | Disadvantages | Fit |
|---|---|---|---|
| Supabase C# client / `Supabase.Storage` | Quick wrappers for upload, signed URL and delete | Community-maintained; adds external API/version surface; full client is broader than required; separate storage repository recently moved | Medium |
| Direct Storage REST + typed `HttpClient` | Reuses current Core pattern; no new package; explicit timeouts/error mapping; easy stub-handler tests; minimal dependency | Must implement three REST operations and response parsing; contract tests become important | **High — recommended** |

Package recommendation: **do not install a Supabase package for the MVP**. Reuse `Microsoft.Extensions.Http`. If the team later chooses the SDK, use only the narrow `Supabase.Storage` package, pin its exact version and commit the resolved dependency files; do not add the full `Supabase` client unless other Supabase products are genuinely used.

## 6. External HTTP and exception patterns (DS-06, DS-25)

Core already uses typed clients:

- `IEmailSender → ResendEmailSender`;
- `IAIStoryGenerationClient → AIStoryGenerationClient`.

`AIStoryGenerationClient` configures base address, timeout and a server-side authentication header in its constructor, uses `HttpClient`, parses structured upstream errors and maps them to a Core-owned exception. Unit/integration tests inject stub `HttpMessageHandler` implementations.

Supabase storage should follow the same pattern:

```text
IMediaStorage (Application)
    ↓
SupabaseMediaStorage (Infrastructure typed HttpClient)
    ↓
Supabase Storage REST
```

Do not let `HttpRequestException`, raw Supabase JSON or SDK-specific exceptions escape Infrastructure. Proposed application-level error codes:

```text
STORAGE_UNAUTHORIZED
STORAGE_FORBIDDEN
STORAGE_BUCKET_NOT_FOUND
STORAGE_OBJECT_CONFLICT
STORAGE_PAYLOAD_TOO_LARGE
STORAGE_INVALID_MIME_TYPE
STORAGE_NETWORK_ERROR
STORAGE_TIMEOUT
STORAGE_UNKNOWN_ERROR
```

Retry only transient network, timeout and eligible 5xx/429 errors. Do not retry authentication, forbidden, invalid MIME, payload-too-large or stale-job errors.

## 7. Configuration and secrets (DS-07, DS-09, DS-17, DS-27)

### Current pattern

- Options classes expose a `SectionName` and are registered in Infrastructure.
- Core API has a `UserSecretsId`.
- ASP.NET `WebApplication.CreateBuilder` loads normal JSON, user-secrets in Development and process environment variables.
- The root `.env` exists, contains the three expected key names and is ignored by Git.
- `.env` is not loaded automatically by current code; no dotenv loader exists.
- No tracked source contains a Supabase secret literal.
- No CI workflow, Docker Compose secret mapping or production secret manager configuration was found in the repository.

### Required configuration contract

The deployed application should depend on only:

```text
SUPABASE_URL
SUPABASE_SECRET_KEY
SUPABASE_MEDIA_BUCKET
```

`SignedUrlLifetimeSeconds` can safely default to 900 seconds in options. An optional override may be added later, but it should not be required for startup.

Recommended options:

```csharp
public sealed class SupabaseStorageOptions
{
    public string Url { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string Bucket { get; init; } = "story-media";
    public int SignedUrlLifetimeSeconds { get; init; } = 900;
}
```

Because the requested environment keys are flat rather than `SupabaseStorage__Url`, Infrastructure must map them explicitly into the options object and validate them on start when media storage is enabled.

### Secret strategy

- Local: .NET User Secrets is preferred. The existing `.env` may be consumed by an external launcher/IDE/Docker, but the API itself does not currently parse it.
- CI: inject the three values from protected CI secrets as process environment variables.
- Production: inject through the hosting platform secret manager; never bake into image, appsettings or frontend build.
- Frontend: never receive `SUPABASE_SECRET_KEY` or directly construct privileged Storage requests.
- Logs: never emit secret headers, binary media or a full signed URL token.

The private bucket assumption is correct. A Supabase secret/service key bypasses Storage RLS, so Core authorization becomes the security boundary for upload and signed-URL issuance. Every read endpoint must first authorize the current user against the story/child relationship.

## 8. Dependency injection strategy (DS-08)

Recommended registrations in Core Infrastructure:

```text
Options binding + startup validation
AddHttpClient<IMediaStorage, SupabaseMediaStorage>()
```

Recommended lifetimes:

- typed `HttpClient`: managed by `IHttpClientFactory`;
- `IMediaStorage`: transient typed-client instance created by the factory;
- pure path builder/MIME mapper: singleton;
- media orchestration service: remain scoped;
- worker: remain hosted singleton creating a new scope per loop iteration.

Do not register a mutable full Supabase client as a global singleton. The existing worker correctly resolves a fresh scoped processor/DbContext for each iteration.

## 9. Database impact and migration proposal (DS-10, DS-29)

### Existing migration state

The current Phase 5 migration adds nullable `StorySceneId`, `story_scenes`, the scene FK and the filtered unique logical-slot index. The model snapshot contains these changes.

However, the repository currently has a dirty migration history: two previous migration pairs are deleted while the replacement initial migration and Phase 5 migration are untracked. This does not invalidate the architecture discovery, but it makes a new storage migration unsafe to create until the team stabilizes the migration baseline on the feature branch.

The actual local/remote `media_assets` row count was not queried because no PostgreSQL CLI/test database connector is available in the repository discovery environment. Source and snapshot confirm only the modeled schema, not deployed data contents.

### Minimal new migration

Keep `Url` for backward compatibility. Add:

```text
media_assets
+ StorageProvider varchar(30) null initially
+ StorageBucket   varchar(100) null initially
+ StoragePath     varchar(1000) null initially
+ MimeType        varchar(100) null initially
```

Recommended P1 addition:

```text
+ ProviderAssetId varchar(500) null
```

Recommended P2 additions:

```text
+ Checksum varchar(128) null
+ ByteSize bigint null
```

Why nullable first:

- legacy/manual media rows may contain only `Url`;
- `StorySceneId` is already nullable for compatibility;
- a phased rollout can backfill or regenerate AI media before tightening constraints.

Do not remove or repurpose `Url` in the first migration. For private Supabase objects, `Url` must not store an expiring signed URL. It may remain null or hold a legacy/provider URL while `StoragePath` becomes the authoritative location for new managed assets.

## 10. Idempotency, uniqueness and race analysis (DS-11)

Existing protections:

- one `(StoryVersionId, StorySceneId, Type)` active media row;
- one `(StoryVersionId, SceneIndex)` scene;
- unique `(Operation, StoryVersionId)` job index for non-deleted jobs;
- operation key scoped by requester/operation;
- advisory transaction lock during job claim;
- lease expiration and rotating concurrency token;
- freshness checks before persistence.

Remaining race:

```text
Worker A passes freshness check
Worker A uploads object
lease expires / Worker B claims
Worker A fails freshness or DB update
object uploaded by A is orphaned or overwrites B's path
```

Therefore, database uniqueness alone does not protect external object storage.

MVP rule:

- create/load `MediaAsset` first so `assetId` exists;
- derive storage path only from numeric system IDs and MIME mapping;
- perform a freshness check immediately before upload;
- upload with create-only semantics (`upsert = false`);
- perform another freshness check before saving `StoragePath`/`Ready`;
- on stale result or DB failure after upload, execute best-effort delete;
- on retry conflict for a non-ready asset, treat it as an orphan recovery case, delete under current job ownership and retry once.

Do not blindly use `upsert = true`: a stale worker could overwrite the object selected by a newer job.

## 11. Worker and transaction boundaries (DS-12, DS-19)

Current job claim transaction is short:

```text
BEGIN
Acquire story transaction lock
Validate job/story
Set story MediaProcessing
Set job Processing + lease + new concurrency token
COMMIT
Generate/evaluate outside transaction
```

Media asset state changes are saved in short operations around external calls. Generation and evaluation are not executed while the claim transaction is open.

Required upload boundary:

```text
1. Load/claim job and MediaAsset slot
2. Set MediaAsset Processing; save
3. Generate candidate
4. Validate alignment and safety
5. AssertFresh
6. Upload to Supabase outside DB transaction
7. AssertFresh again
8. Short DB save/transaction: StoragePath + metadata + Ready
9. On step 7/8 failure: best-effort object delete
```

No DB transaction should span generation, validation, upload or signed-URL network calls.

Job timing currently includes:

- job lease default: 5 minutes;
- asset attempts: 3;
- worker idle delay: 2 seconds;
- transient failure delay: 2 seconds;
- permanent failure delay: 300 seconds;
- no independent media provider/upload timeout yet.

Add an explicit storage HTTP timeout shorter than the job lease and pass cancellation tokens through every operation.

## 12. Current media generation lifecycle (DS-13, DS-22, DS-23)

The current illustration flow is:

```text
Create/load MediaAsset
→ Processing
→ image provider returns URL
→ alignment evaluation
→ safety evaluation
→ stale check
→ persist URL + Ready
```

The current audio flow is:

```text
Create/load MediaAsset
→ stale check
→ Processing
→ TTS provider returns URL + WordTimings
→ stale check
→ persist URL + Ready
```

Provider abstractions exist and are scene-aware, but both output records expose only a URL. No byte array, stream, base64, temporary-file path or MIME type is available. Default providers intentionally fail with configuration errors.

Target lifecycle must be:

```text
Generate candidate bytes/stream
→ Validate candidate
→ AssertFresh
→ Upload only PASS candidate
→ Persist storage metadata
```

Do not upload before alignment/safety validation. Child-facing rejected candidates should never enter the durable bucket.

Recommended provider-neutral candidate type:

```text
Stream/byte source
ContentType
Optional provider temporary URL
Optional provider metadata
WordTimings for audio
```

For MVP, a bounded `byte[]` is acceptable if bucket size limits are small. A `Stream` contract is preferred so providers and storage are not coupled to base64 or local files. The orchestrator owns disposal.

## 13. Storage path and MIME convention (DS-18, DS-24)

Recommended stable logical paths:

```text
stories/{storyId}/versions/{versionId}/scenes/{sceneId}/illustration/{assetId}.webp
stories/{storyId}/versions/{versionId}/scenes/{sceneId}/audio/{assetId}.mp3
```

Properties:

- deterministic because `MediaAsset.Id` is created before upload;
- no nickname, title, prompt or other user input;
- no PII;
- no arbitrary path accepted from API clients;
- retry resolves to the same logical object slot;
- folder hierarchy supports lifecycle cleanup per story/version.

Central MIME mapping:

```text
image/webp → .webp
image/png  → .png
image/jpeg → .jpg
audio/mpeg → .mp3
audio/wav  → .wav
audio/ogg  → .ogg
```

Reject unsupported MIME types before upload. Never derive extension from a user filename. Configure the private bucket with matching allowed content types and a conservative maximum file size.

## 14. Orphan and missing-object handling (DS-20, DS-21)

### Upload succeeds, DB save fails

MVP strategy:

1. Catch DB/stale failure after successful upload.
2. Call `DeleteAsync(storagePath)` best-effort with a separate bounded cancellation token.
3. Preserve the original failure as the job error.
4. Log object path and asset/job IDs, but not credentials or signed URL.
5. On a later upload conflict for a non-ready asset, run owned-orphan recovery rather than marking the row ready.

P2 strategy: scheduled reconciliation that lists the controlled prefix and removes objects not referenced by a non-deleted `MediaAsset`. Do not directly delete rows from Supabase `storage` schema; use Storage API operations.

### DB says Ready, object is missing

Normal write order prevents this:

```text
upload success → response verified → DB Ready
```

Do not perform HEAD/download on every reader request. If signed-URL delivery reports a missing object, mark/requeue the asset through an explicit repair path and emit an operational alert.

## 15. Signed URL and reader compatibility (DS-14, DS-15, DS-28)

Current API exposes only Phase 5 aggregate progress. It does not expose scenes or asset URLs. Generic `StoryDto` exposes only `CoverImageUrl`; no `StorySceneDto`, `ReadingPageDto`, `MediaAssetDto`, `IllustrationUrl` or `AudioUrl` exists. ReadingSession stores IDs/progress but has no reader response service carrying media.

Required read flow:

```text
Authorized Reader API
→ load current/approved StoryVersion and StoryScene rows
→ load Ready MediaAsset rows with StoragePath
→ IMediaStorage.CreateSignedUrlAsync(path, 15 minutes)
→ return temporary IllustrationUrl and AudioUrl
```

Add provider-neutral DTOs such as:

```text
StorySceneMediaDto
- SceneId
- SceneIndex
- SceneText
- IllustrationUrl
- AudioUrl
- WordTimings
```

Never expose `StorageBucket`, `StoragePath`, provider response metadata, secret key or upload endpoints unless a later requirement explicitly needs them. Frontend remains URL-based and does not need Supabase credentials.

Signed URLs expire; do not persist them in `MediaAsset.Url`. Generate them near response time. The reader may request refreshed URLs after expiration.

## 16. Proposed storage contract and required classes (DS-04, DS-30)

### Application

```csharp
public interface IMediaStorage
{
    Task<StoredMediaResult> UploadAsync(
        MediaUploadRequest request,
        CancellationToken cancellationToken);

    Task<string> CreateSignedUrlAsync(
        string storagePath,
        TimeSpan expiresIn,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string storagePath,
        CancellationToken cancellationToken);
}
```

Required provider-neutral models:

- `MediaUploadRequest`: storage path, readable stream/bytes, content type, overwrite policy.
- `StoredMediaResult`: path, content type, optional provider object ID, optional byte size/checksum.
- `MediaStorageException`: stable error code, retryable flag, HTTP status where safe.
- `IMediaStoragePathBuilder`: builds paths only from trusted numeric IDs and MIME mapping.
- `IMediaMimeTypePolicy`: allowlist and extension mapping.

### Infrastructure

- `SupabaseStorageOptions`.
- `SupabaseMediaStorage`: typed REST client implementing upload/sign/delete.
- Supabase response/error DTOs kept internal to Infrastructure.
- DI registration and startup validation.

### Application orchestration changes

- Extend candidate image/audio output to carry content and MIME type.
- Inject `IMediaStorage` and path/MIME policy into `MediaGenerationService`.
- Insert upload between validation and Ready persistence.
- Add cleanup behavior for post-upload failures.
- Add reader query/service that signs URLs only after authorization.

## 17. Test strategy (DS-16)

Existing reusable test infrastructure:

- media foundation tests for canonical text/scene validation;
- nine `MediaGenerationService` tests for readiness, retry, duplicate slots, validation failure, stale result and provider classification;
- fake/in-memory Unit of Work used by workflow and media tests;
- stub `HttpMessageHandler` pattern used by AI and Resend clients;
- EF/Npgsql model inspection tests;
- no `WebApplicationFactory`, Testcontainers, real PostgreSQL integration harness or storage fake exists.

Required unit/contract tests:

1. Upload image request maps URL, bucket, headers, content type and body correctly.
2. Upload audio request maps MIME and path correctly.
3. Unsupported MIME is rejected before HTTP.
4. Signed URL response maps correctly and enforces bounded expiry.
5. Delete maps correctly and is idempotent for object-not-found where appropriate.
6. Supabase error statuses map to stable storage codes and retryability.
7. Duplicate logical asset remains blocked by DB model/index.
8. Retry does not create a second `MediaAsset` row.
9. Upload failure never marks `MediaAsset.Ready`.
10. DB failure after upload invokes cleanup.
11. Stale/archived story does not persist storage metadata and attempts cleanup.
12. Signed URL service authorizes story/child access before calling storage.
13. Logs never include secret key or full signed URL query.

Opt-in live integration tests, never part of default unit CI:

1. Upload small image into a dedicated test prefix.
2. Upload small audio.
3. Create signed URL and download object.
4. Confirm private object is not publicly readable.
5. Delete all objects created by the test in `finally`.
6. Use a separate test bucket/project or isolated prefix and secrets supplied only by CI.

## 18. Logging and observability (DS-26)

Current workers use `ILogger`, but Phase 5 worker messages mostly expose only error code/delay and unhandled exception. No Serilog or OpenTelemetry configuration was found in Core.

Storage operations should log structured fields:

```text
JobId
StoryId
StoryVersionId
StorySceneId
MediaAssetId
StoragePath
AttemptNo
Operation
DurationMs
Result/ErrorCode
```

Never log:

- `SUPABASE_SECRET_KEY`;
- Authorization/apikey headers;
- binary/base64 media;
- full signed URL including token;
- child profile snapshot or prompt unless separately redacted.

Log signed URL events using asset/path IDs and expiration seconds only.

## 19. Reuse / Extend / Add matrix

| Component | Existing | Reuse | Extend | Add new | Reason |
|---|---:|---:|---:|---:|---|
| `MediaAsset` | Yes | Yes | Yes | No | Keep lifecycle/slot; add storage metadata |
| `StoryScene` | Yes | Yes | No | No | Already canonical and version-pinned |
| Storage abstraction | No | No | No | Yes | Prevent Application depending on Supabase |
| Media worker | Yes | Yes | Yes | No | Insert upload/cleanup into existing orchestration |
| Config options | Pattern exists | Yes | Yes | Yes | Add validated `SupabaseStorageOptions` following convention |
| HTTP client pattern | Yes | Yes | No | Yes | Add typed Supabase REST client using existing factory pattern |
| Provider candidate models | Yes | Yes | Yes | No | Extend URL-only outputs to stream/bytes + MIME |
| Reader DTO/API | No | No | No | Yes | Generate authorized temporary signed URLs |
| Unit test infrastructure | Yes | Yes | Yes | No | Reuse stub handlers and fake UoW |
| Live storage integration fixture | No | No | No | Yes | Verify private bucket behavior without default CI dependency |

## 20. Priority and risks

### P0 — required before enabling the worker

1. Add provider-neutral storage abstraction and implementation.
2. Change candidate output from URL-only to uploadable content + MIME.
3. Keep secret strictly server-side and validate required configuration.
4. Upload only after illustration alignment/safety validation.
5. Add stale checks around upload and cleanup on stale/DB failure.

Existing P0 foundations already satisfied:

- exact approved `StoryVersion` handoff;
- `StoryScene` foundation;
- scene FK;
- logical asset uniqueness;
- durable job and stale protection.

### P1 — required for complete Supabase feature

1. Stabilize current migration history before creating storage migration.
2. Add `StorageProvider`, `StorageBucket`, `StoragePath`, `MimeType`.
3. Add signed URL service and authorized reader DTO/API.
4. Add storage error mapping, timeout and retry classification.
5. Add deterministic path/MIME policy.
6. Implement immediate orphan cleanup.
7. Add REST contract tests and optional live integration tests.
8. Add structured storage logs.

### P2 — hardening after MVP

1. `Checksum` and `ByteSize` persistence.
2. Scheduled orphan reconciliation.
3. Storage analytics/metrics and alerting.
4. Provider asset metadata/audit trail.
5. Bucket lifecycle rules for superseded/soft-deleted stories.

## 21. Evidence register

### E-01 — MediaAsset lacks storage metadata

```text
Evidence ID: E-01
Layer: Domain
File: src/Core/StoryPlatform.Domain/Entities/MediaAsset.cs
Symbol: MediaAsset
Line: 5-17
Finding: Entity stores scene link, type/status, Url and WordTimings only.
Impact: Cannot safely persist private bucket identity/path/MIME or renew signed URLs.
Recommendation: Extend entity with minimal nullable storage metadata; retain Url.
Confidence: High
```

### E-02 — Logical asset uniqueness exists

```text
Evidence ID: E-02
Layer: Infrastructure/Persistence
File: src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/MediaAssetConfiguration.cs
Symbol: MediaAssetConfiguration.Configure
Line: 33-40
Finding: FK to StoryScene and filtered unique (StoryVersionId, StorySceneId, Type).
Impact: Protects one illustration/TTS DB slot per scene, including retries.
Recommendation: Reuse unchanged; handle external object idempotency separately.
Confidence: High
```

### E-03 — StoryScene canonical foundation exists

```text
Evidence ID: E-03
Layer: Domain/Persistence
File: src/Core/StoryPlatform.Domain/Entities/StoryScene.cs; StorySceneConfiguration.cs
Symbol: StoryScene; StorySceneConfiguration.Configure
Line: entity 7-15; configuration 13-20
Finding: Scene is version-pinned with exact ranges/text and unique index.
Impact: Storage paths and media rows can be scene-aware without new foundation.
Recommendation: Reuse as authoritative media unit.
Confidence: High
```

### E-04 — Durable exact-version handoff exists

```text
Evidence ID: E-04
Layer: Application
File: src/Core/StoryPlatform.Application/Features/StoryReview/Services/StoryReviewService.cs
Symbol: ApproveAsync
Line: 658, 685-719
Finding: Approval creates GenerateMediaPackage pinned to approvedVersion with deterministic operation key.
Impact: Storage worker does not need to scan approved stories or infer a version.
Recommendation: Reuse handoff unchanged.
Confidence: High
```

### E-05 — Worker lease and stale protection exist

```text
Evidence ID: E-05
Layer: Application
File: src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.cs
Symbol: ProcessNextAsync; AssertFreshAsync
Line: 78-124, 357-379
Finding: Job claim uses transaction lock, lease, attempt and rotating concurrency token; results are freshness-checked.
Impact: Strong base for external upload, but checks must surround upload too.
Recommendation: Reuse and add pre/post-upload freshness checks plus cleanup.
Confidence: High
```

### E-06 — External work is outside claim transaction

```text
Evidence ID: E-06
Layer: Application
File: src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.cs
Symbol: ProcessNextAsync; EnsureIllustrationAsync; EnsureAudioAsync
Line: 91-139, 246-305
Finding: Claim commits at line 124; provider/evaluator calls happen later.
Impact: Supabase upload can follow the safe short-transaction pattern.
Recommendation: Never open a DB transaction across upload.
Confidence: High
```

### E-07 — Provider result is URL-only

```text
Evidence ID: E-07
Layer: Application
File: src/Core/StoryPlatform.Application/Features/MediaGeneration/Models/MediaGenerationModels.cs
Symbol: GeneratedIllustration; GeneratedAudio
Line: 34-35
Finding: Candidate contracts contain URL and metadata, not bytes/stream/MIME.
Impact: Backend cannot upload validated media to Supabase without downloading or changing provider contract.
Recommendation: Extend candidate result to provider-neutral content + ContentType.
Confidence: High
```

### E-08 — Current persistence directly copies provider URL

```text
Evidence ID: E-08
Layer: Application
File: src/Core/StoryPlatform.Application/Features/MediaGeneration/Services/MediaGenerationService.cs
Symbol: EnsureIllustrationAsync; EnsureAudioAsync
Line: 249-260, 290-297
Finding: Ready status is persisted with provider URL immediately after generation/validation.
Impact: URL expiry/provider lifecycle is coupled to application data.
Recommendation: Upload PASS candidate, persist StoragePath, then mark Ready.
Confidence: High
```

### E-09 — No real media provider is registered

```text
Evidence ID: E-09
Layer: Infrastructure
File: src/Core/StoryPlatform.Infrastructure/AI/UnavailableMediaProviders.cs
Symbol: UnavailableImageGenerationProvider; UnavailableTtsProvider; FailClosedMediaEvaluator
Line: 8-29
Finding: Default implementations fail closed as not configured.
Impact: Storage integration cannot produce files until provider adapters return uploadable content.
Recommendation: Keep fail-closed defaults; implement providers separately before enabling worker.
Confidence: High
```

### E-10 — Typed HttpClient convention exists

```text
Evidence ID: E-10
Layer: Infrastructure
File: src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs; AI/AIStoryGenerationClient.cs
Symbol: AddInfrastructure; AIStoryGenerationClient
Line: DI 42-46; client 19-23, 62-78
Finding: External services use typed HttpClient, options, timeout and mapped errors.
Impact: Direct Supabase REST is architecturally consistent.
Recommendation: Add typed IMediaStorage client in Infrastructure.
Confidence: High
```

### E-11 — `.env` is safe but not loaded by the API

```text
Evidence ID: E-11
Layer: Configuration
File: .gitignore; src/Core/StoryPlatform.Api/Program.cs; StoryPlatform.Api.csproj
Symbol: configuration bootstrap
Line: .gitignore 6-7; Program 13; csproj 21
Finding: .env is ignored; API uses CreateBuilder and has UserSecretsId but no dotenv loader.
Impact: Merely creating .env does not populate IConfiguration when running dotnet directly.
Recommendation: Use User Secrets/process env or an external .env-aware launcher; do not add dotenv only for production.
Confidence: High
```

### E-12 — Reader media DTO/API is absent

```text
Evidence ID: E-12
Layer: API/Application
File: MediaGenerationController.cs; Features/Stories/DTOs/StoryDtos.cs
Symbol: GetProgress; StoryDto
Line: controller 18-25; DTO 5-20
Finding: API exposes aggregate progress only; StoryDto has CoverImageUrl but no scene illustration/audio URLs.
Impact: Frontend has no authorized signed-URL delivery path.
Recommendation: Add reader query/DTO that signs Ready assets at response time.
Confidence: High
```

### E-13 — Migration lacks storage fields and baseline is dirty

```text
Evidence ID: E-13
Layer: Infrastructure/Migrations
File: 20260916094512_AddPhase5MediaGenerationWorkflow.cs; ApplicationDbContextModelSnapshot.cs
Symbol: Up; MediaAsset snapshot
Line: migration 20, 51-107; snapshot 958-985
Finding: Scene FK/index exists, but snapshot only has Url for media location; migration files are currently deleted/untracked in Git status.
Impact: New migration risks conflict or wrong baseline until history is stabilized.
Recommendation: Resolve migration lineage first, then add a new forward-only storage metadata migration.
Confidence: High
```

### E-14 — Test seams are reusable but no live storage harness exists

```text
Evidence ID: E-14
Layer: Tests
File: MediaGenerationServiceTests.cs; AIStoryGenerationClientTests.cs; ResendEmailSenderTests.cs
Symbol: media tests; StubHandler/FakeHttpMessageHandler
Line: media 15-136; AI handler 15-34; Resend handler 15-41
Finding: Unit tests cover retry/stale/duplicates and HTTP clients can be stubbed; no Testcontainers/WebApplicationFactory/storage fixture.
Impact: REST contract can be unit-tested immediately, while live private-bucket behavior needs a new opt-in fixture.
Recommendation: Reuse handlers/fake UoW and add isolated live storage tests.
Confidence: High
```

## 22. Supabase documentation findings

- Private buckets require authorized download or temporary signed URLs; public bucket URLs bypass read access controls.
- Server secret/service keys bypass Storage RLS and must never be exposed publicly.
- Standard uploads default to conflict when the path already exists; overwrite requires explicit upsert behavior.
- Upsert under RLS needs INSERT, SELECT and UPDATE policies. This backend plans to use a server secret, but create-only semantics are still preferred for stale-worker safety.
- Since 2025, Supabase restricts direct SQL changes/destructive operations in the managed `storage` schema. Object lifecycle must use Storage APIs rather than direct table manipulation.
- Supabase shipped Storage reliability/security changes in March 2026, including protections against orphaning caused by direct SQL deletes.

References:

- [Storage access control](https://supabase.com/docs/guides/storage/security/access-control)
- [Private buckets and signed URLs](https://supabase.com/docs/guides/storage/buckets/fundamentals)
- [Serving private assets](https://supabase.com/docs/guides/storage/serving/downloads)
- [Standard uploads and overwrite behavior](https://supabase.com/docs/guides/storage/uploads/standard-uploads)
- [Supabase C# client repository](https://github.com/supabase-community/supabase-csharp)
- [Storage C# repository move](https://github.com/supabase-community/storage-csharp)
- [Storage schema restrictions](https://supabase.com/changelog/34270-restricting-access-on-auth-storage-and-realtime-schemas-on-april-21-2025)
- [March 2026 Storage reliability/security update](https://supabase.com/blog/supabase-storage-performance-security-reliability-updates)

## 23. Recommended implementation order

```text
0. Stabilize migration baseline/history
1. Add provider-neutral storage interface/models/errors
2. Add Supabase options + startup validation
3. Add typed REST storage client and contract tests
4. Extend MediaAsset + create one new forward migration
5. Extend media candidate contracts to stream/bytes + MIME
6. Add deterministic path and MIME policies
7. Insert validate → upload → persist flow with cleanup
8. Add signed-URL reader service/DTO/API
9. Add orchestration, authorization and orphan tests
10. Run opt-in live private-bucket integration tests
11. Enable media worker only after providers, evaluators and storage pass readiness checks
```

## 24. Definition of Done assessment

- [x] Map solution/project structure
- [x] Survey `MediaAsset`
- [x] Verify `StoryScene`
- [x] Verify Phase 4 → Phase 5 handoff
- [x] Search existing storage abstraction
- [x] Check packages
- [x] Survey typed HTTP pattern
- [x] Survey configuration/options
- [x] Survey DI registration
- [x] Survey secret management without exposing values
- [x] Evaluate migration needs
- [x] Evaluate idempotency and race behavior
- [x] Evaluate worker lease/retry/stale behavior
- [x] Survey image/TTS lifecycle
- [x] Survey signed URL/read flow
- [x] Survey API/DTO impact
- [x] Survey test infrastructure
- [x] Evaluate storage path and MIME convention
- [x] Evaluate transaction boundary
- [x] Propose orphan strategy
- [x] Review logging/security/reader compatibility
- [x] Compare SDK vs REST
- [x] Create Reuse/Extend/Add matrix
- [x] Create implementation recommendation
- [x] No source implementation
- [x] No package installation
- [x] No migration creation/application
- [x] No Supabase upload/API call
- [x] No appsettings/environment changes

## 25. Final readiness status

`READY_WITH_ARCHITECTURE_CHANGES`

Rationale:

- Not `BLOCKED_BY_PHASE5_FOUNDATION`: exact version handoff, scenes, jobs, uniqueness and stale protection already exist.
- Not `BLOCKED_BY_SECRET_OR_CONFIG_RISK`: secrets are untracked and a safe server-side strategy is available, although `.env` loading must be understood.
- Not merely `READY_WITH_REQUIRED_DATABASE_MIGRATION`: a migration is required, but so are storage abstraction, uploadable candidate contracts and reader signed-URL flow.
- Implementation should start only after migration history is stabilized and the team accepts direct REST as the transport decision.
