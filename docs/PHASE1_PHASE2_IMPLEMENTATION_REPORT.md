# Báo cáo triển khai Phase 1 và Phase 2 — Guided AI Story Generation

**Ngày báo cáo:** 13/09/2026

**Repository:** AI Storytelling Backend

**Phạm vi:** Luồng 2, từ tiếp nhận input đến duyệt outline và tạo handoff sang Phase 3

## 1. Kết quả tổng quan

Backend đã triển khai luồng liên tục từ Phase 1 sang Phase 2:

1. Parent hoặc Teacher chọn Child Profile và gửi creative input.
2. Core xác thực quyền trên đúng Child Profile, tải cấu hình học tập và Safety Policy có hiệu lực.
3. Input Guardrail kiểm tra dữ liệu trước khi cho phép gọi AI sinh outline.
4. Core lưu Accepted Input Snapshot, Context Snapshot và durable outline job trong cùng transaction.
5. Background worker lấy job, dựng request chỉ từ snapshot đã chấp nhận và gọi AI Service.
6. AI Service sinh outline có cấu trúc, thực hiện retry kỹ thuật và output guardrail.
7. Core kiểm tra output lần hai và tạo StoryVersion bất biến.
8. Parent hoặc Teacher có thể xem, edit, regenerate, approve hoặc reject outline theo quyền.
9. Approval ghi nhận chính xác StoryVersion được duyệt và tạo job `ContentPending` cho Phase 3.

Phase 1 và Phase 2 không sinh full story content, vocabulary, quiz, discussion questions, hình ảnh hoặc TTS.

## 2. Phân chia trách nhiệm

### Core Service

Core là nơi quyết định nghiệp vụ và chịu trách nhiệm:

- Xác thực tài khoản, quan hệ giám sát và permission trên Child Profile.
- Tải Learning Profile, Safety Policy và Organization Policy.
- Chuẩn hóa input, kiểm tra giới hạn và chống gửi trùng.
- Tạo Story draft, Generation Request và các snapshot bất biến.
- Quản lý trạng thái job, transaction, concurrency, lease và idempotency.
- Quản lý StoryVersion, current version và lịch sử chỉnh sửa.
- Kiểm tra quyền `GenerateStory` và `ApproveStory` riêng biệt.
- Tạo durable handoff sang Phase 3.

### AI Service

AI Service chỉ chịu trách nhiệm thực thi AI:

- Chọn prompt template theo ngôn ngữ và age band.
- Gọi LLM để sinh JSON outline có schema cố định.
- Retry các lỗi kỹ thuật tạm thời.
- Kiểm tra output: trường bắt buộc, độ dài, PII, prompt leakage và policy terms.
- Trả metadata gồm model, prompt version, token, latency và số attempt.

AI Service không tự tải entity hoặc profile từ Core database và không quản lý migration nghiệp vụ.

## 3. Công việc đã thực hiện — Phase 1

### 3.1 Context API

Đã triển khai API chỉ đọc:

`GET /api/v1/ai-story-input/children/{childProfileId}/context`

API trả về context tối thiểu cần thiết cho form:

- Child Profile được chọn.
- Age band và reading level.
- Vocabulary level và language.
- Maximum story length.
- Required approval mode.
- Interests và content category codes.

Việc mở form hoặc tải context không tạo Story, Generation Request hay AI job.

### 3.2 Submit và tạo Story draft

Đã triển khai:

`POST /api/v1/ai-story-input/requests`

Luồng submit thực hiện:

- Data annotation validation và business validation.
- Resource authorization theo Child Profile.
- Chuẩn hóa topic, genre, character, setting, lesson, language và vocabulary level.
- Kiểm tra Target Length không vượt Maximum Length.
- Tạo input fingerprint và context fingerprint.
- Chống gửi trùng bằng idempotency key.
- Tạo hoặc sử dụng lại đúng AI Story draft.
- Tạo StoryGenerationRequest ở trạng thái `CheckingInput`.

### 3.3 Input Guardrail

Guardrail kiểm tra:

- Nội dung bị chặn hoặc hạn chế theo policy.
- Mã category và tên hiển thị bằng ngôn ngữ người dùng.
- PII cơ bản.
- Timeout và lỗi provider.

Chỉ quyết định `Allow` rõ ràng mới được tạo handoff. `Block`, timeout hoặc lỗi kỹ thuật không được coi là nội dung hợp lệ.

### 3.4 Snapshot và handoff

Khi Core thực hiện trong cùng transaction:

- Chuyển request sang `InputAccepted`.
- Lưu Accepted Input Snapshot.
- Lưu Context Snapshot có policy codes và policy match terms.
- Tạo StoryGenerationJob với `GenerateOutline`, `OutlinePending`, `Pending`.
- Giữ Story ở trạng thái `Draft`.

Phase 1 không tạo StoryVersion.

### 3.5 Theo dõi và retry input

Đã triển khai:

- `GET /api/v1/ai-story-input/stories/{storyId}/requests/{requestId}`
- `POST /api/v1/ai-story-input/stories/{storyId}/requests/{requestId}/retry`

Retry input giữ nguyên request và yêu cầu input/context fingerprint không thay đổi.

## 4. Công việc đã thực hiện — Phase 2

### 4.1 Background worker

`OutlineGenerationWorker` thực hiện polling durable job bằng `IHostedService`.

Worker:

- Nhận job `OutlinePending/Pending`.
- Có thể reclaim job `OutlineGenerating/Processing` khi lease đã hết hạn.
- Claim job bằng optimistic concurrency token.
- Không giữ database transaction trong thời gian gọi AI Service.
- Chỉ dựng AI request từ Accepted Input Snapshot và Context Snapshot.

### 4.2 Sinh outline tại AI Service

`GenerateOutlineHandler` trả về:

- Title.
- Opening.
- Development.
- Ending.
- Generation metadata.

AI handler áp dụng:

- Strict structured JSON schema.
- Timeout có cấu hình.
- Tối đa ba technical attempts.
- Exponential backoff.
- Retry cho invalid JSON, timeout, HTTP 408, 429 và 5xx.
- Không retry policy rejection.

### 4.3 Output safety

Output được kiểm tra tại hai lớp:

1. AI Service kiểm tra schema và safety trước khi trả response.
2. Core kiểm tra lại trước khi ghi StoryVersion.

Guardrail sử dụng policy match terms trong Context Snapshot, đồng thời tương thích ngược với snapshot cũ chỉ có category codes.

AI safety rejection trả HTTP `422` kèm stable `errorCode`. Core giữ nguyên reason code này trong generation job.

### 4.4 Lưu StoryVersion

Khi outline hợp lệ, Core thực hiện transaction:

- Đánh dấu current version cũ thành false nếu có.
- Tạo StoryVersion mới với `Content = null`.
- Dùng `Initial`, `HumanEdited` hoặc `AiRegenerated` để phân biệt nguồn version.
- Bảo đảm chỉ một current version trên mỗi Story.
- Chuyển Story sang `OutlineReview`.
- Chuyển generation job sang `OutlineGenerated/Completed`.
- Lưu generation metadata và liên kết job với StoryVersion.

### 4.5 Human Review API

Đã triển khai:

- `GET /api/v1/stories/{storyId}/outline`
- `GET /api/v1/stories/{storyId}/outline/versions`
- `GET /api/v1/stories/{storyId}/outline/versions/{versionNo}`
- `PUT /api/v1/stories/{storyId}/outline/versions/{versionNo}`
- `POST /api/v1/stories/{storyId}/outline/versions/{versionNo}/regenerate`
- `POST /api/v1/stories/{storyId}/outline/retry`
- `POST /api/v1/stories/{storyId}/outline/versions/{versionNo}/approve`
- `POST /api/v1/stories/{storyId}/outline/versions/{versionNo}/reject`

Edit và regenerate yêu cầu `GenerateStory`. Approve và reject yêu cầu permission độc lập `ApproveStory`.

### 4.6 Approval và Phase 3 handoff

Approval thực hiện trong transaction theo Story:

- Kiểm tra version được yêu cầu vẫn là current version.
- Ghi `OutlineApprovedByUserId` và `OutlineApprovedAt`.
- Chuyển source outline job sang `OutlineApproved`.
- Tạo job `GenerateContent/ContentPending` gắn đúng StoryVersion.
- Ngăn approve trùng bằng approval key và unique constraint.

Phase 3 consumer chưa nằm trong phạm vi triển khai hiện tại.

## 5. Các lỗi Bugbot đã được xử lý

### Case 1 — Thiếu migration Phase 2

Đã tạo migration `20260913083400_AddPhase2OutlineWorkflow`.

Migration có backfill để:

- Chuyển job cũ sang operation/status hợp lệ.
- Gắn Generation Request, requested user và operation key khi có dữ liệu.
- Sinh concurrency token cho job cũ.
- Đặt MaxAttempts hợp lệ.
- Xử lý duplicate active jobs trước khi tạo unique index.
- Xử lý duplicate current versions và version numbers.

Migration đã được apply thành công vào PostgreSQL local ngày 13/09/2026. EF migration history đã ghi nhận `20260913083400_AddPhase2OutlineWorkflow` và không còn trạng thái `Pending`.

### Case 2 — Job bị kẹt ở Processing

Đã thêm `LeaseExpiresAt`. Worker có thể reclaim job khi lease bốn phút hết hạn.

### Case 3 — Không thể retry initial outline

Đã thêm endpoint retry initial outline. Endpoint yêu cầu:

- Story AI vẫn là Draft.
- Story chưa có outline version.
- Có initial outline job đã thất bại.
- Không có outline operation khác đang chạy.
- Có operation key để chống retry trùng.

### Case 4 — Race condition giữa Edit và Approve

Đã bổ sung PostgreSQL transaction-level advisory lock theo `storyId`.

Edit, regenerate, retry, approve và reject đều revalidate current version sau khi giữ lock. Một request dùng stale version sẽ nhận conflict thay vì tạo handoff sai.

### Case 5 — Guardrail chỉ kiểm tra category code

Context Snapshot hiện lưu thêm effective category match terms. Organization/personal policy được merge theo nguyên tắc giới hạn nghiêm ngặt hơn trước khi tạo danh sách term.

### Case 6 — Mất AI safety reason code

Đã chuẩn hóa error contract giữa AI Service và Core:

- AI trả `422` cùng `errorCode` cho output safety rejection.
- Core HTTP adapter parse error response và ném typed exception.
- Worker ghi reason code ổn định vào `StoryGenerationJob.ErrorCode`.

### Case 7 — Rollback làm job tiếp tục kẹt

Đã tạo `IOutlineJobFailureFinalizer`. Failure update sử dụng ApplicationDbContext scope mới và chỉ cập nhật khi committed concurrency token vẫn khớp.

## 6. Thay đổi database

### StoryGenerationJob

Các thông tin Phase 2 được bổ sung:

- GenerationRequestId.
- StoryVersionId.
- BaseStoryVersionId.
- RequestedByUserId.
- Operation và OperationKey.
- Status.
- AttemptNo và MaxAttempts.
- ConcurrencyToken.
- LeaseExpiresAt.
- ErrorCode.
- GenerationMetadataJson.

Các filtered unique index bảo đảm:

- Chỉ một active outline operation trên mỗi Story.
- Không tạo trùng operation trên cùng StoryVersion.
- Không dùng lại operation key trong cùng phạm vi user/operation.

### StoryVersion

Đã bổ sung:

- OutlineApprovedByUserId.
- OutlineApprovedAt.
- Unique `(StoryId, VersionNo)`.
- Filtered unique current version trên mỗi Story.

## 7. Kiểm thử và xác minh

Các lệnh xác minh đã chạy:

```powershell
dotnet build src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj
dotnet build src/AI/StoryPlatform.AI.Api/StoryPlatform.AI.Api.csproj
dotnet test StoryPlatform.sln
dotnet ef database update 20260913083400_AddPhase2OutlineWorkflow `
  --project src/Core/StoryPlatform.Infrastructure/StoryPlatform.Infrastructure.csproj `
  --startup-project src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj `
  --context ApplicationDbContext
dotnet ef migrations has-pending-model-changes `
  --project src/Core/StoryPlatform.Infrastructure/StoryPlatform.Infrastructure.csproj `
  --startup-project src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj `
  --context ApplicationDbContext
```

Kết quả gần nhất:

- Core build: thành công, 0 warning, 0 error.
- AI build: thành công, 0 warning, 0 error.
- Core Unit Tests: 67 passed.
- Core Integration Tests: 18 passed.
- AI Unit Tests: 1 passed.
- AI Integration Tests: 4 passed.
- Tổng cộng: 90 passed, 0 failed.
- EF model: không còn thay đổi chưa được migration ghi nhận.
- Migration SQL: sinh thành công.
- PostgreSQL local: migration Phase 2 đã apply thành công và migration history không còn `Pending`.

Các regression test mới bao gồm:

- Reclaim job hết lease.
- Retry initial outline có idempotency.
- Policy term tiếng Việt chặn human edit.
- Preserve AI safety reason code.
- Failure finalization bằng committed concurrency token.
- Approval gắn đúng version và tạo ContentPending job.

## 8. Tài liệu và sơ đồ

- Kế hoạch xử lý Bugbot: `src/AI/Phase1_Phase2_Bugfix_Plan.md`.
- Tài liệu module Phase 1: `src/Core/StoryPlatform.Application/Features/AIStoryInput/README.md`.
- Sơ đồ HTML Phase 1–2: `src/AI/phase1-phase2-workflow.html`.
- Kế hoạch gốc Phase 2: `src/AI/Phase2_AI_Outline_Generation_Human_Review_Plan.md`.

## 9. Công việc chưa thực hiện

### Chưa kiểm thử LLM thật

Các test hiện dùng test double. Việc gọi model thật cần cấu hình provider/API key ở runtime và không nên lưu secret trong source control.

### Semantic moderation chưa được tích hợp

Guardrail hiện tại là rule-based. Provider semantic moderation trong thiết kế hybrid chưa tồn tại trong repository.

### Phase 3 chưa được triển khai

Job `ContentPending` đã được tạo bền vững nhưng chưa có consumer sinh:

- Full story content.
- Vocabulary.
- Quiz.
- Discussion questions.
- Image prompts và media assets.
- TTS.

### Frontend chưa được tích hợp

Repository hiện tại là backend. UI thực tế cần tích hợp các endpoint context, progress, review, retry, regenerate, approve và reject.

## 10. Trạng thái bàn giao

- Source đã được triển khai và kiểm tra build/test.
- Migration Phase 2 đã được tạo và apply thành công vào PostgreSQL local.
- Chưa thực hiện Git commit.
- Báo cáo không bao gồm hoặc thay đổi các file người dùng đã chỉnh sửa trước phạm vi Phase 1–2.
