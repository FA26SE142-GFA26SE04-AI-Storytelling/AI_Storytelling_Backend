# PHASE 5 — PRE-CODING DISCOVERY PLAN
## .NET Backend ↔ Supabase Storage Integration

**Project:** AI Storytelling Platform for Children  
**Flow:** Luồng 2 — Guided AI Story Generation Pipeline  
**Phase:** Phase 5 — Media Generation & Story Continuity  
**Task type:** Discovery / Readiness Assessment  
**Status:** KHẢO SÁT TRƯỚC KHI CODE — Không chỉnh source trong bước này  
**Primary goal:** Khảo sát codebase hiện tại để xác định cách tích hợp Supabase Storage an toàn, tối thiểu thay đổi và phù hợp với kiến trúc Phase 5 đã chốt.

---

## 1. Mục tiêu khảo sát

Trước khi code Supabase Storage, agent phải xác minh:

```text
1. Backend hiện tại đang tổ chức layer như thế nào?
2. MediaAsset hiện được dùng ở đâu?
3. StoryScene đã được tạo chưa?
4. Phase 5 handoff đã tồn tại chưa?
5. Có abstraction storage/file service nào sẵn không?
6. Có package Supabase hoặc S3 client nào đã cài chưa?
7. Configuration / environment variables đang tổ chức thế nào?
8. Dependency Injection đang đăng ký service ở đâu?
9. Job worker hiện tại chạy theo pattern nào?
10. Media file hiện đang lưu kiểu gì?
11. Signed URL / download flow hiện đã tồn tại chưa?
12. Database migration hiện tại đã hỗ trợ StoragePath chưa?
13. Có test infrastructure để test upload/download không?
14. Có secret handling phù hợp không?
15. Có nguy cơ duplicate asset / stale upload / orphan object không?
```

---

## 2. Nguyên tắc bắt buộc

Trong nhiệm vụ khảo sát này:

```text
KHÔNG sửa code
KHÔNG tạo migration
KHÔNG cài package
KHÔNG thêm dependency
KHÔNG upload file thử
KHÔNG gọi Supabase Storage
KHÔNG đổi appsettings
KHÔNG commit secret
KHÔNG chỉnh environment variables
```

Chỉ:

```text
Read
Trace
Search
Compare
Document
Recommend
```

---

## 3. Output bắt buộc

Agent phải tạo:

```text
Phase5_Supabase_Storage_Discovery_Report.md
```

Báo cáo phải đủ để quyết định:

```text
Reuse gì?
Extend gì?
Add gì?
Không nên thêm gì?
Migration nào cần thiết?
Class/interface nào cần tạo?
Package nào cần cài?
Test nào cần viết?
```

---

## 4. DS-00 — Xác minh repository structure

Ghi:

```text
Solution file:
Projects:
Domain project:
Application project:
Infrastructure project:
API project:
Test projects:
Worker project:
```

Tạo tree ngắn của source thực tế.

---

## 5. DS-01 — Khảo sát MediaAsset

Tìm:

```text
class MediaAsset
MediaAssetConfiguration
DbSet<MediaAsset>
MediaType
MediaStatus
```

Ghi toàn bộ field, đặc biệt:

```text
Id
StoryVersionId
StorySceneId?
SceneIndex
Type
Status
Url
StoragePath?
StorageBucket?
StorageProvider?
MimeType?
WordTimings
CreatedAt
UpdatedAt
IsDeleted
```

Phải trả lời:

1. `Url` hiện có semantic gì?
2. Code nào assume `Url` là public URL?
3. Đã có logic insert/update `MediaAsset` chưa?
4. Có query theo `SceneIndex` chưa?
5. Có unique constraint per scene/type chưa?
6. Có FK tới `StoryScene` chưa?

---

## 6. DS-02 — Khảo sát StoryScene

Search:

```text
StoryScene
story_scenes
SceneIndex
TextRangeStart
TextRangeEnd
SceneText
VisualDescription
```

Nếu chưa có, đánh:

```text
P0:
Phase 5 storage integration chưa nên gắn trực tiếp vào StoryVersion.
Cần StoryScene foundation trước khi media persistence hoàn chỉnh.
```

---

## 7. DS-03 — Khảo sát Phase 4 → Phase 5 handoff

Search:

```text
ApproveAsync
StoryStatus.Approved
MediaProcessing
GenerateMedia
Phase5
Outbox
Handoff
StoryGenerationJob
```

Phải trả lời:

1. Sau approval có tạo durable job không?
2. Có exact `ApprovedStoryVersionId` không?
3. Có `OperationKey` không?
4. Có stale protection không?
5. Story chuyển `MediaProcessing` ở đâu?

Nếu chưa có:

```text
P0:
Storage integration không được tự polling mọi Story Approved.
Cần durable handoff trước.
```

---

## 8. DS-04 — Khảo sát storage/file abstraction hiện có

Search:

```text
IStorage
IFileStorage
IMediaStorage
IBlobStorage
IObjectStorage
FileService
UploadAsync
DownloadAsync
SignedUrl
Presigned
S3
Blob
Bucket
```

Phải xác định:

- Có abstraction có thể reuse không?
- Có implementation local/cloud cũ không?
- Có service generic file không?
- Có nên extend hay tạo mới?

Không tạo `IMediaStorage` mới nếu existing abstraction đã đủ.

---

## 9. DS-05 — Khảo sát package dependencies

Kiểm tra `.csproj`:

```text
Supabase
Postgrest
Storage
S3
AWSSDK.S3
Azure.Storage.Blobs
Polly
HttpClient
```

Ghi:

```text
Package:
Version:
Project:
Currently used by:
```

Mục tiêu cuối:

```text
Quyết định dùng Supabase C# client
hay direct Supabase Storage REST API qua HttpClient.
```

Chưa cài package.

---

## 10. DS-06 — Khảo sát external HTTP pattern

Search:

```text
AddHttpClient
IHttpClientFactory
TypedClient
DelegatingHandler
RetryPolicy
Timeout
BaseAddress
Authorization
```

Phải trả lời:

1. External provider client đang tổ chức thế nào?
2. Có typed client pattern không?
3. Retry/timeouts đặt ở đâu?
4. Có common exception mapping không?

---

## 11. DS-07 — Khảo sát configuration pattern

Search:

```text
IOptions
appsettings.json
appsettings.Development.json
UserSecrets
AddOptions
ValidateOnStart
Environment.GetEnvironmentVariable
```

Ghi các Options class hiện có để `SupabaseStorageOptions` đi cùng convention.

Fields dự kiến để đánh giá:

```text
Url
SecretKey
Bucket
SignedUrlLifetimeSeconds
```

---

## 12. DS-08 — Khảo sát Dependency Injection

Search:

```text
AddInfrastructure
DependencyInjection
ConfigureServices
AddScoped
AddSingleton
AddTransient
```

Trả lời:

- Storage service nên đăng ký ở đâu?
- Lifetime phù hợp?
- External client có typed/singleton pattern không?
- Có module DI riêng cho AI/media không?

---

## 13. DS-09 — Khảo sát secret management

Xác minh:

```text
dotnet user-secrets
.env
environment variables
CI/CD secrets
Docker secrets
secret manager
```

Search literal:

```text
sb_secret_
service_role
SUPABASE_SECRET_KEY
SUPABASE_URL
```

Phải bảo đảm secret không commit vào source.

Output:

```text
Secret strategy:
Local:
CI:
Production:
```

---

## 14. DS-10 — Database impact cho MediaAsset

Kiểm tra migration/entity đã có:

```text
StorageProvider
StorageBucket
StoragePath
MimeType
ProviderAssetId
Checksum
ByteSize
StorySceneId
```

Nếu thiếu, chỉ đề xuất migration tối thiểu:

```text
media_assets
+ StorySceneId
+ StorageProvider
+ StorageBucket
+ StoragePath
+ MimeType
```

Không xóa `Url` nếu code cũ còn dùng.

---

## 15. DS-11 — Idempotency / uniqueness

Kiểm tra DB có bảo vệ:

```text
1 StoryScene + Illustration = 1 logical asset slot
1 StoryScene + TtsAudio = 1 logical asset slot
```

Candidate:

```text
UNIQUE(StorySceneId, Type)
```

Kiểm tra race:

```text
Worker A upload
Worker B retry cùng operation
```

Có sinh duplicate `MediaAsset` không?

---

## 16. DS-12 — Job/worker pattern

Search:

```text
BackgroundService
StoryGenerationWorker
Claim
LeaseExpiresAt
AttemptNo
MaxAttempts
ConcurrencyToken
OperationKey
```

Trả lời:

1. Claim job ở đâu?
2. External API call có nằm ngoài DB transaction không?
3. Retry ở đâu?
4. Stale job bị loại thế nào?
5. Timeout bao lâu?

Supabase upload phải follow cùng pattern.

---

## 17. DS-13 — Media generation flow hiện tại

Search:

```text
GenerateIllustration
GenerateTts
ImageProvider
TtsProvider
MediaProcessing
```

Xác định:

```text
đã có provider abstraction chưa?
đã có handler chưa?
đã scene-aware chưa?
đã validation trước upload chưa?
```

Target:

```text
Generate
→ Validate
→ Upload
→ Persist metadata
```

---

## 18. DS-14 — Signed URL / read flow

Search:

```text
ReadingSession
MediaUrl
AudioUrl
IllustrationUrl
SignedUrl
Download
```

Phải trả lời:

1. Frontend hiện nhận media URL ra sao?
2. API response đã có URL field chưa?
3. Có persist URL dài hạn không?

Target:

```text
DB stores StoragePath
Backend generates temporary signed URL
Frontend receives signed URL
```

---

## 19. DS-15 — API/DTO impact

Tìm:

```text
StorySceneDto
ReadingPageDto
MediaAssetDto
StoryDetailsResponse
ReadingSessionResponse
```

Xác định DTO nào cần field:

```text
IllustrationUrl
AudioUrl
```

Frontend không được thấy:

```text
SecretKey
Storage credentials
```

---

## 20. DS-16 — Test infrastructure

Search:

```text
IntegrationTests
InfrastructureTests
WebApplicationFactory
Testcontainers
MockHttpMessageHandler
FakeStorage
```

Đánh giá khả năng test:

```text
IMediaStorage contract
path generation
upload result mapping
signed URL generation
failure mapping
retry/idempotency
```

---

## 21. DS-17 — Supabase assumptions

Project đã cấu hình:

```text
Project: AI-Storytelling_media
Bucket: story-media
Visibility: private
```

Application chỉ nên phụ thuộc config:

```text
SUPABASE_URL
SUPABASE_SECRET_KEY
SUPABASE_MEDIA_BUCKET
```

Không hard-code project ref.

---

## 22. DS-18 — Storage path convention

Nếu codebase chưa có naming convention, đánh giá:

```text
stories/{storyId}/versions/{versionId}/scenes/{sceneId}/illustration/{assetId}.webp

stories/{storyId}/versions/{versionId}/scenes/{sceneId}/audio/{assetId}.mp3
```

Kiểm tra:

- path deterministic?
- retry có overwrite không?
- assetId có tồn tại trước upload?
- extension map từ MIME?
- path có chứa user input nguy hiểm không?

---

## 23. DS-19 — Transaction boundary

Target:

```text
1. Claim/Create MediaAsset Processing
2. COMMIT
3. Generate + Validate
4. Upload external storage
5. Short DB transaction
6. Persist StoragePath + Ready
```

Không giữ DB transaction mở trong network upload.

---

## 24. DS-20 — Orphan object scenario

Case:

```text
Supabase upload PASS
        ↓
DB update FAIL
```

Agent phải đề xuất một chiến lược MVP:

```text
cleanup ngay
hoặc
retry deterministic path
hoặc
orphan cleanup job
```

---

## 25. DS-21 — DB Ready nhưng file thiếu

Case:

```text
MediaAsset.Status = Ready
Storage object missing
```

Đề xuất:

```text
upload
→ verify success
→ DB Ready
```

Không cần HEAD check mỗi reader request trừ khi có lỗi thực tế.

---

## 26. DS-22 — Candidate media lifecycle

Đánh giá flow:

```text
Generate Candidate
→ Validate
→ Upload only PASS
```

ưu tiên hơn:

```text
Generate
→ Upload
→ Validate
```

Agent phải xác minh provider trả:

```text
bytes
Stream
base64
temporary URL
file path
```

---

## 27. DS-23 — Audio lifecycle

TTS provider output có thể là:

```text
byte[]
Stream
temporary file
provider URL
```

Storage abstraction nên hướng tới:

```text
Stream
ContentType
StoragePath
```

để không phụ thuộc provider.

---

## 28. DS-24 — MIME mapping

Đề xuất mapping tập trung:

```text
image/webp -> .webp
image/png  -> .png
image/jpeg -> .jpg
audio/mpeg -> .mp3
audio/wav  -> .wav
audio/ogg  -> .ogg
```

Không lấy extension từ filename do user cung cấp.

---

## 29. DS-25 — Error mapping

Xem codebase map external errors thế nào.

Supabase errors nên được map về abstraction:

```text
Unauthorized
Forbidden
BucketNotFound
ObjectConflict
PayloadTooLarge
InvalidMimeType
NetworkError
Timeout
UnknownStorageError
```

Application không phụ thuộc raw Supabase exception.

---

## 30. DS-26 — Logging / observability

Search:

```text
ILogger
Serilog
OpenTelemetry
CorrelationId
JobId
StoryId
```

Storage log nên có:

```text
StoryId
StoryVersionId
StorySceneId
MediaAssetId
StoragePath
JobId
AttemptNo
```

Không log:

```text
SecretKey
binary content
signed URL token đầy đủ
```

---

## 31. DS-27 — Security review

Xác minh:

```text
Secret key chỉ backend
Bucket private
Frontend không có secret
Signed URL có expiration
Storage path không chứa PII không cần thiết
Không cho client cung cấp arbitrary storage path
Không public upload endpoint trực tiếp
```

---

## 32. DS-28 — Reader compatibility

Target:

```text
Database:
StoragePath

Backend DTO:
temporary IllustrationUrl
temporary AudioUrl
```

Frontend vẫn dùng URL như bình thường.

---

## 33. DS-29 — Migration compatibility

Kiểm tra:

```text
MediaAsset.Url existing
existing media row count
ModelSnapshot
migration baseline
```

Nếu Phase 5 chưa có dữ liệu thật:

```text
StorySceneId có thể nullable ở migration đầu
```

sau đó tighten constraint nếu phù hợp.

---

## 34. DS-30 — SDK vs REST decision

Báo cáo phải so sánh:

| Option | Ưu điểm | Nhược điểm | Fit với codebase |
|---|---|---|---|
| Supabase C# client | nhanh triển khai | thêm dependency, community-maintained | |
| Direct Storage REST + HttpClient | kiểm soát rõ, fit typed client | phải tự implement calls | |

Quyết định phải dựa vào codebase thực tế.

---

## 35. Evidence format

Mỗi finding:

```text
Evidence ID:
Layer:
File:
Symbol:
Line:
Finding:
Impact:
Recommendation:
Confidence:
```

---

## 36. Priority

### P0

```text
Không có exact approved StoryVersion handoff
Không có StoryScene
Không có storage abstraction
Secret handling không an toàn
MediaAsset không map được về Scene
```

### P1

```text
Không có StoragePath fields
Không có unique media slot
Không có signed URL flow
Không có orphan cleanup strategy
Không có integration test
```

### P2

```text
Checksum
ByteSize
Storage analytics
Provider metadata
Advanced cleanup policy
```

---

## 37. Reuse / Extend / Add Matrix

Agent phải tạo:

| Component | Existing | Reuse | Extend | Add New | Reason |
|---|---:|---:|---:|---:|---|
| MediaAsset | ? | ? | ? | ? | |
| StoryScene | ? | ? | ? | ? | |
| Storage abstraction | ? | ? | ? | ? | |
| Job Worker | ? | ? | ? | ? | |
| Config Options | ? | ? | ? | ? | |
| HttpClient pattern | ? | ? | ? | ? | |
| Reader DTO | ? | ? | ? | ? | |
| Integration Tests | ? | ? | ? | ? | |

---

## 38. Proposed interface để agent đánh giá

Không implement trong discovery:

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

---

## 39. Proposed configuration để agent đánh giá

```csharp
public sealed class SupabaseStorageOptions
{
    public string Url { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string Bucket { get; init; } = "story-media";
    public int SignedUrlLifetimeSeconds { get; init; } = 900;
}
```

Map theo convention hiện tại của project.

---

## 40. Target upload flow

```text
StoryScene
    ↓
Generate Media
    ↓
Validate
    ↓
Create/Load MediaAsset Processing
    ↓
Build deterministic StoragePath
    ↓
IMediaStorage.UploadAsync
    ↓
Supabase Storage
    ↓
Persist StoragePath
    ↓
MediaAsset.Ready
```

---

## 41. Target read flow

```text
Reader API
    ↓
MediaAsset.StoragePath
    ↓
IMediaStorage.CreateSignedUrlAsync
    ↓
Temporary URL
    ↓
Frontend
```

---

## 42. Integration tests cần đề xuất

```text
1. Upload image
2. Upload audio
3. Unsupported MIME bị reject
4. Signed URL đọc được object private
5. Object private không public-readable
6. Duplicate logical asset bị chặn
7. Retry không duplicate DB row
8. Supabase failure không mark MediaAsset Ready
9. DB failure sau upload có recovery
10. Stale/Archived Story không persist media result
```

---

## 43. Final readiness status

Chọn một:

```text
READY_TO_IMPLEMENT_SUPABASE_STORAGE
READY_WITH_REQUIRED_DATABASE_MIGRATION
READY_WITH_ARCHITECTURE_CHANGES
BLOCKED_BY_PHASE5_FOUNDATION
BLOCKED_BY_SECRET_OR_CONFIG_RISK
```

---

## 44. Definition of Done

```text
[ ] Map solution/project structure
[ ] Khảo sát MediaAsset
[ ] Xác minh StoryScene
[ ] Xác minh Phase 4 → Phase 5 handoff
[ ] Search storage abstraction hiện có
[ ] Kiểm tra packages
[ ] Khảo sát HttpClient pattern
[ ] Khảo sát configuration pattern
[ ] Khảo sát DI registration
[ ] Khảo sát secret management
[ ] Đánh giá migration needs
[ ] Đánh giá idempotency
[ ] Đánh giá job worker
[ ] Khảo sát image/TTS flow
[ ] Khảo sát signed URL/read flow
[ ] Khảo sát API/DTO impact
[ ] Khảo sát tests
[ ] Đánh giá path convention
[ ] Đánh giá transaction boundary
[ ] Đánh giá orphan object scenario
[ ] Đánh giá candidate media lifecycle
[ ] Đánh giá MIME mapping
[ ] Đánh giá error mapping
[ ] Đánh giá logging
[ ] Security review
[ ] Reader compatibility review
[ ] Migration compatibility review
[ ] So sánh SDK vs REST
[ ] Tạo Reuse/Extend/Add matrix
[ ] Tạo implementation recommendation
[ ] KHÔNG sửa code
[ ] KHÔNG tạo migration
[ ] KHÔNG upload file
[ ] Tạo Phase5_Supabase_Storage_Discovery_Report.md
```

---

## 45. Prompt giao trực tiếp cho Agent

```text
Hãy thực hiện PRE-CODING DISCOVERY cho việc tích hợp Supabase Storage vào Phase 5 của AI Storytelling Platform.

MỤC TIÊU:
Xác định chính xác cách tích hợp .NET Backend với Supabase Storage để lưu:
- Illustration theo từng StoryScene
- TTS Audio theo từng StoryScene

SUPABASE HIỆN TẠI:
- Project: AI-Storytelling_media
- Private bucket: story-media
- Backend dùng server-side secret key
- Frontend không được nhận secret key
- Application database lưu storage path/metadata
- Reader nhận signed URL tạm thời

ĐÂY LÀ NHIỆM VỤ KHẢO SÁT.
KHÔNG code.
KHÔNG cài package.
KHÔNG tạo migration.
KHÔNG chỉnh appsettings.
KHÔNG upload file.
KHÔNG commit secret.

BẮT BUỘC KHẢO SÁT:
- solution/project structure
- MediaAsset entity/configuration/migration
- StoryScene availability
- Phase 4 → Phase 5 handoff
- storage/file abstraction hiện có
- package dependencies
- HttpClient/external provider pattern
- configuration/options pattern
- dependency injection pattern
- secret management
- job/worker lease/retry/idempotency
- image/TTS generation flow
- signed URL / reader flow
- DTO/API impact
- integration test infrastructure
- transaction boundaries
- stale result handling
- orphan object handling
- MIME mapping
- logging/security
- SDK vs REST decision

MỌI FINDING QUAN TRỌNG PHẢI CÓ:
file → symbol → line → impact → recommendation.

PHẢI TẠO:
Phase5_Supabase_Storage_Discovery_Report.md

BÁO CÁO PHẢI CÓ:
1. Current architecture
2. Existing reusable components
3. Missing components
4. Database impact
5. Storage abstraction recommendation
6. SDK vs REST decision
7. Config + secret strategy
8. DI strategy
9. Upload flow
10. Signed URL flow
11. Retry/idempotency/stale protection
12. Orphan cleanup strategy
13. Reader compatibility
14. Required migration proposal
15. Required classes/interfaces
16. Package recommendation
17. Tests
18. Reuse/Extend/Add matrix
19. Risks P0/P1/P2
20. Final readiness status

KHÔNG IMPLEMENT SUPABASE STORAGE TRONG TASK NÀY.
```

---

## 46. Kết luận

Chỉ bắt đầu implementation khi agent chứng minh được đường đi:

```text
Exact Approved StoryVersion
        ↓
StoryScene
        ↓
Generate + Validate Media
        ↓
IMediaStorage
        ↓
Supabase Storage
        ↓
MediaAsset.StoragePath
        ↓
Signed URL
        ↓
Reader
```

Mục tiêu của discovery là tránh tích hợp Supabase trước rồi mới phát hiện thiếu Scene FK, thiếu handoff, sai transaction boundary hoặc duplicate asset khi worker retry.
