# EXISTING STORY WORKFLOW — IMPLEMENTATION PLAN V2

**Nguồn tham chiếu:** `Existing_Story_Workflow_Implementation_Guide.md`
**Phạm vi:** Luồng 2 — Parent/Teacher đã có sẵn nội dung câu chuyện
**Quyết định:** Giữ nguyên pipeline generation Vocabulary → Quiz → Discussion trong Phase 3

---

# 1. Quyết định kiến trúc

Không tách Vocabulary, Quiz và Discussion khỏi `ContentGenerationService` trong đợt triển khai này.

Phase được xác định theo implementation hiện tại:

```text
PHASE 1 — INPUT / INTAKE
PHASE 2 — OUTLINE
PHASE 3 — CONTENT PACKAGE GENERATION
PHASE 4 — HUMAN REVIEW / VALIDATION / APPROVAL
PHASE 5 — MEDIA GENERATION
```

Trong đó Phase 3 tạo một **content package** gồm:

```text
Story Content
Vocabulary
Quiz
Discussion
```

Phase 4 không tạo artifact lần đầu. Phase 4 chịu trách nhiệm:

```text
Review
Manual Edit
AI Regenerate Proposal
Validation
Approval
Archive
```

---

# 2. Lý do giữ pipeline hiện tại

## 2.1. Pipeline tuần tự đã tồn tại

`ContentGenerationService` hiện đã có chuỗi operation:

```text
GenerateContent
→ GenerateVocabulary
→ GenerateQuiz
→ GenerateDiscussion
→ ContentPackageCompleted
```

Mỗi artifact đã có:

- Job riêng.
- Retry riêng.
- Trạng thái riêng.
- Validation riêng.
- Liên kết chính xác với `StoryVersionId`.

## 2.2. Giảm rủi ro regression

Nếu chuyển các generator sang service/worker mới thì phải thay đổi:

- Worker dispatch.
- Job handoff.
- Retry và idempotency.
- Progress API.
- Test Phase 3 và Phase 4.

Việc này không mang lại giá trị trực tiếp cho nhánh Existing Story.

## 2.3. Existing Story chỉ cần điểm nhập mới

Existing Story không cần một pipeline artifact riêng. Nó chỉ cần tạo ra một `StoryVersion` ổn định rồi nhập vào đoạn pipeline đã có:

```text
GenerateVocabulary
→ GenerateQuiz
→ GenerateDiscussion
```

## 2.4. Không phá vỡ nhánh AI Story

Nhánh AI Story tiếp tục hoạt động như hiện tại:

```text
Phase 1: Guided Input
Phase 2: Generate/Review Outline
Phase 3: Generate Content → Vocabulary → Quiz → Discussion
Phase 4: Human Review → Validation → Approval
Phase 5: Media
```

---

# 3. Luồng mục tiêu

## 3.1. AI Generated Story — giữ nguyên

```text
PHASE 1
Guided Input + Profile/Safety Context
↓
PHASE 2
Generate Outline → Human Approve Outline
↓
PHASE 3
Generate Story Content
→ Validate Content
→ Create Stable StoryVersion
→ Generate Vocabulary
→ Generate Quiz
→ Generate Discussion
→ ContentPackageCompleted
↓
PHASE 4
Human Review → Validation → Approval
↓
PHASE 5
Scene → Illustration/TTS → Ready
```

## 3.2. Existing Story — nhánh mới

```text
PHASE 1
Import/Paste
→ Validate Child/Profile/Permission
→ Resolve Safety Context
→ Normalize Content
→ Save Original StoryVersion v1 (EditType = Initial)
→ Basic Safety Guardrail
↓
PHASE 2
SKIP OUTLINE
↓
PHASE 3
┌──────────────┐    ┌──────────────┐    ┌──────────────────┐
│ Hard Safety  │───▶│ Profile Fit  │───▶│ Decision:         │
│ Check        │    │ Evaluation   │    │ SUITABLE          │
└──────────────┘    └──────────────┘    │ BLOCKED           │
                                       │ ADAPT_RECOMMENDED │
                                       └──────────────────┘

ADAPT_RECOMMENDED:
┌──────────────┐    ┌──────────────┐    ┌──────────────────┐
│ AI Adapt     │ or │ Manual Edit  │ or │ Keep Original    │
│ → v2 (AI)    │    │ → v2 (Human) │    │ + Override Reason │
└──────────────┘    └──────────────┘    └──────────────────┘

→ Stable StoryVersion
→ Generate Vocabulary
→ Generate Quiz
→ Generate Discussion
→ ContentPackageCompleted
↓
PHASE 4
Human Review → Validation → Approval
↓
PHASE 5
Scene → Illustration/TTS → Ready
```

Decision rules:

```text
SUITABLE:
    → giữ current version → handoff artifact trực tiếp

BLOCKED:
    → dừng tại đây, KHÔNG tạo artifact job
    → KHÔNG cho Keep Original hoặc Approval

ADAPT_RECOMMENDED:
    → chờ supervisor chọn một trong 3 hướng
    → AI Adapt: tạo v2 (AiRefined), re-run safety
    → Manual Edit: tạo v2 (HumanEdited)
    → Keep Original: giữ current + ghi Override Reason
    → cả 3 đều handoff artifact sau khi stable
```

---

# 4. Điểm hội tụ của hai nhánh

Hai nhánh hội tụ tại một contract duy nhất:

```text
StableStoryVersionHandoff
---------------------------------
storyId
storyVersionId
requestedByUserId
source
contentStable = true
safetyPassed = true
profileFitDecision
generationRequestId (nullable)
```

Sau handoff, cả hai nhánh sử dụng chung:

```text
GenerateVocabulary
GenerateQuiz
GenerateDiscussion
Phase 4 Review
Phase 5 Media
```

Hai nhánh vào Phase 3 ở **hai điểm khác nhau**:

```text
AI Story Content Stable (sau GenerateContent) ─────┐
                                                    ├→ StableVersionArtifactHandoffService
Existing Story Stable (sau Evaluate/Adapt) ──────────┘                       │
                                                                              ▼
                                              GenerateVocabulary → Quiz → Discussion
```

- **AI Story** vào **giữa** Phase 3: sau `GenerateContent` đã chạy xong, nhảy thẳng vào handoff để bắt đầu Vocabulary.
- **Existing Story** vào **đầu** Phase 3: bỏ qua `GenerateContent`, vào thẳng handoff.

Cả hai đều dùng chung `QueueArtifactsAsync(...)`. Handoff service không phân biệt nguồn — chỉ yêu cầu một `StoryVersion.IsCurrent = true` có `Content` ổn định và safety đã pass.

Không tạo pipeline artifact thứ hai cho Existing Story.

## 4.1. Ranh giới trách nhiệm giữa các service

Việc giữ Vocabulary/Quiz/Discussion trong Phase 3 không có nghĩa là đưa toàn bộ
Existing Story workflow vào `ContentGenerationService`.

```text
ExistingStoryService
├── Import/Paste
├── Normalize content
├── Resolve Profile + Safety Context
├── Evaluate Profile Fit
├── AI Adapt
├── Manual Edit Version
└── Keep-original Override
             ↓
StableVersionArtifactHandoffService
├── Validate current stable version
├── Validate safety/profile-fit decision
├── Lock + idempotency
└── Enqueue GenerateVocabulary
             ↓
ContentGenerationService
├── GenerateVocabulary
├── GenerateQuiz
├── GenerateDiscussion
└── Complete content package
```

Mục tiêu giảm trách nhiệm áp dụng ở **cấp class/service**, không thay đổi phạm vi
nghiệp vụ của Phase 3.

---

# 5. Thay đổi cần thiết trong Phase 3

## 5.1. Giữ nguyên generation pipeline

Tiếp tục sử dụng implementation hiện tại của `ContentGenerationService` cho:

- `ProcessNextAsync()`.
- `GenerateVocabulary`.
- `GenerateQuiz`.
- `GenerateDiscussion`.
- `ContentArtifactPending`.
- `ContentArtifactGenerating`.
- `ContentArtifactCompleted`.
- `ContentPackageCompleted`.

Không bổ sung vào `ContentGenerationService` các trách nhiệm:

- File upload/extraction.
- Content normalization.
- Resolve Child Profile/Safety Policy.
- Existing Story profile-fit evaluation.
- AI adaptation decision.
- Keep-original override.
- Quản lý version gốc.

## 5.2. Bổ sung StableVersionArtifactHandoffService

Tạo service riêng có trách nhiệm enqueue artifact chain cho một StoryVersion đã ổn định:

```csharp
public interface IStableVersionArtifactHandoffService
{
    Task QueueArtifactsAsync(
        int storyId,
        int storyVersionId,
        int requestedByUserId,
        int? generationRequestId,
        CancellationToken cancellationToken);
}
```

Method phải:

1. Lock theo `storyId`.
2. Load Story và current StoryVersion.
3. Kiểm tra version có content hợp lệ.
4. Kiểm tra safety đã pass.
5. Ngăn tạo trùng artifact jobs.
6. Tạo job `GenerateVocabulary` đầu tiên.
7. Giữ nguyên chuỗi Quiz → Discussion hiện tại.

Service này là điểm nhập dùng chung:

```text
AI Story Content Stable ─────┐
                             ├→ StableVersionArtifactHandoffService
Existing Story Stable ───────┘
```

**Điểm vào Phase 3 theo nguồn:**

- **AI Story** vào **giữa** Phase 3: sau `GenerateContent` đã chạy xong, vào thẳng handoff để bắt đầu Vocabulary.
- **Existing Story** vào **đầu** Phase 3: bỏ qua `GenerateContent`, vào thẳng handoff.

Cả hai đều gọi chung `QueueArtifactsAsync(...)`. Handoff service không phân biệt nguồn — chỉ yêu cầu một `StoryVersion.IsCurrent = true` có `Content` ổn định và safety đã pass.

## 5.3. Không giả lập Outline approval

Existing Story không được gán `OutlineApprovedAt` giả chỉ để vượt điều kiện Phase 3.

Điều kiện phải tách rõ:

```text
GenerateContent job:
    yêu cầu AI Story + approved outline

GenerateVocabulary/Quiz/Discussion job:
    yêu cầu current stable StoryVersion
    không yêu cầu outline
```

## 5.4. Trạng thái kết thúc Phase 3

Sau khi Discussion hoàn tất:

```text
Job.Stage = ContentPackageCompleted
Job.Status = Completed
Story.Status = ContentReview
```

Sau đó Phase 4 sử dụng các API review hiện có.

---

# 6. Existing Story Phase 1

## 6.1. API

```http
POST /api/v1/stories/import
```

Hỗ trợ giai đoạn đầu:

```text
paste
txt
```

DOCX triển khai sau khi Paste/TXT ổn định.

## 6.2. Validation

Backend phải tự load và kiểm tra:

- User authentication.
- ChildProfile tồn tại và Active.
- SupervisionRelationship.
- Permission `GenerateStory`.
- LearningProfile.
- SafetyPolicy cá nhân.
- Organization safety baseline nếu có.
- Maximum story length.
- Language.

Frontend không được truyền Safety/Profile values rồi yêu cầu backend tin trực tiếp.

## 6.3. Persistence transaction

Trong cùng transaction:

```text
Create Story
    Source = Manual
    Status = Draft
    ChildProfileId = validated child

Create StoryVersion v1
    EditType = Initial
    IsCurrent = true
    Content = normalized original content
```

`StoryVersion v1` là bản gốc và không được update trực tiếp.

---

# 7. Existing Story Phase 3

## 7.1. Evaluation

Đánh giá current StoryVersion theo:

- Hard safety policy.
- Maximum length.
- Reading level.
- Vocabulary difficulty.
- Age suitability.
- Narrative complexity.

Decision:

```text
SUITABLE
ADAPT_RECOMMENDED
BLOCKED
```

## 7.2. Supervisor decision

Với `ADAPT_RECOMMENDED`:

```text
AI Adapt
Manual Edit
Keep Original + Override Reason
Archive
```

`BLOCKED` không được Keep Original hoặc Approval.

## 7.3. Version rules

```text
Upload original    → v1 / Initial
Manual edit        → v2 / HumanEdited
AI adaptation      → v3 / AiRefined
```

Khi tạo version mới:

1. Lock Story.
2. Kiểm tra base version còn current.
3. Đặt base version `IsCurrent = false`.
4. Tạo version mới với `VersionNo + 1`.
5. Đặt version mới `IsCurrent = true`.
6. Re-run Safety và Profile Fit.
7. Chỉ handoff artifact khi version ổn định.

Không update trực tiếp content của version cũ.

---

# 8. Tận dụng schema hiện tại

## 8.1. Không bắt buộc migration trong iteration đầu

Có thể ánh xạ:

```text
Story.Source = Manual
StoryVersion.EditType = Initial / HumanEdited / AiRefined
StoryGenerationJob.BaseStoryVersionId = version nguồn
StoryGenerationJob.GuardrailResult = safety result
StoryGenerationJob.GenerationMetadataJson = profile-fit result + input method
AuditLog = import/edit/adapt/override history
```

## 8.2. Enum operation đề xuất

Có thể bổ sung C# enum:

```text
EvaluateExistingStory
AdaptExistingStory
```

`Operation` được lưu dạng string và giới hạn 30 ký tự; hai tên trên nằm trong giới hạn.
Việc thêm enum value không tự nó yêu cầu thay đổi cột database.

## 8.3. Migration chỉ xem xét sau

Chỉ tạo migration nếu business yêu cầu truy vấn trực tiếp:

- `stories.input_method`.
- `story_versions.parent_version_id`.
- Bảng profile evaluation riêng.

Không sửa migration Init hiện có.

---

# 9. Các vấn đề hiện tại phải xử lý

## 9.1. StoryService legacy

Không sử dụng `StoryService.CreateStoryAsync` hiện tại cho import vì:

- Chưa nhận `ChildProfileId`.
- Không tạo StoryVersion.
- Ghi content trực tiếp vào Story header.
- Không resolve Profile/Safety.

Tạo feature riêng `ExistingStories`.

## 9.2. Update đang overwrite

Các API chỉnh sửa content phải tạo StoryVersion mới. Không sửa trực tiếp current version.

## 9.3. Publish bypass

`PublishStoryAsync` hiện có thể đưa Story thẳng sang `Ready`.
Existing Story không được sử dụng endpoint này.

Luồng hợp lệ duy nhất:

```text
ContentReview → Approved → MediaProcessing → Ready
```

## 9.4. Review authorization

Mọi API đọc/sửa/regenerate/apply proposal phải gọi chung permission guard.
Không chỉ kiểm tra quyền tại Approve và Archive.

---

# 10. API plan

## Intake và Evaluation

```http
POST /api/v1/stories/import
GET  /api/v1/stories/{storyId}/existing/progress
POST /api/v1/stories/{storyId}/existing/evaluate
GET  /api/v1/stories/{storyId}/existing/evaluation
```

## Adapt/Edit

```http
POST /api/v1/stories/{storyId}/existing/adapt
PUT  /api/v1/stories/{storyId}/existing/content
POST /api/v1/stories/{storyId}/existing/keep-original
POST /api/v1/stories/{storyId}/existing/archive
```

## Phase 4 và Phase 5

Tái sử dụng API hiện có:

```text
/api/v1/stories/{storyId}/review/*
/api/v1/stories/{storyId}/media/*
```

---

# 11. Work packages

## WP1 — Existing Story Intake

- DTO import.
- Paste/TXT parser.
- Normalize content.
- Permission/Profile/Safety context.
- Atomic Story + StoryVersion creation.

## WP2 — Safety and Profile Evaluation

- Hard safety evaluator.
- Readability/length/vocabulary/age evaluation.
- Structured result trong `GenerationMetadataJson`.
- `SUITABLE / ADAPT_RECOMMENDED / BLOCKED`.

## WP3 — Immutable Version Workflow

- Manual edit tạo version mới.
- AI adapt tạo proposal/version mới.
- Current-version concurrency guard.
- Retry limit cho AI adaptation.

## WP4 — Phase 3 Artifact Handoff

- Tạo `IStableVersionArtifactHandoffService`.
- Tạo `StableVersionArtifactHandoffService`.
- Gọi handoff từ nhánh AI Story sau khi Content Stable.
- Gọi handoff từ nhánh Existing Story sau khi Evaluate/Adapt Stable.
- Bypass GenerateContent cho Existing Story.
- Không bypass Vocabulary/Quiz/Discussion.
- Idempotency và duplicate-job protection.

## WP5 — Phase 4 Hardening

- Authorization cho toàn bộ review APIs.
- Edit tạo version mới.
- Artifact binding/revalidation khi version thay đổi.
- Validation và approval.

## WP6 — Phase 5 Reuse

- Xác nhận approved current version.
- Reuse scene/media pipeline.
- Stale-result protection.

## WP7 — Legacy Endpoint Protection

- Không cho `publish` bypass Luồng 2.
- Không dùng direct Story content update cho canonical content.

---

# 12. Test plan

## Intake

- Paste hợp lệ.
- TXT UTF-8 hợp lệ.
- Empty/oversized/unsupported input.
- Child inactive.
- User không có permission.
- Story và original version rollback cùng nhau khi lỗi.

## Evaluation và Versioning

- Suitable handoff artifact trực tiếp.
- Blocked không tạo artifact job.
- AI adapt tạo version mới.
- Manual edit tạo version mới.
- Original version không đổi.
- Stale base version bị từ chối.
- Keep Original bắt buộc override reason.

## Artifact pipeline

- Existing Story không tạo Outline job.
- Existing Story không tạo GenerateContent job.
- Bắt đầu bằng GenerateVocabulary.
- Vocabulary → Quiz → Discussion đúng thứ tự.
- Retry không tạo duplicate artifact.
- Artifact liên kết đúng current StoryVersion.
- Version thay đổi làm artifact cũ không được tái sử dụng ngầm.

## Review và Media

- Content package hoàn chỉnh chuyển `ContentReview`.
- User không có quyền không thể đọc/sửa review package.
- Approval tạo đúng một media job.
- Story chỉ Ready khi mỗi scene đủ Illustration và TTS.

## End-to-end

```text
Paste
→ Original v1
→ Evaluate
→ AI Adapt v2
→ Generate Vocabulary
→ Generate Quiz
→ Generate Discussion
→ Human Review
→ Approval
→ Media
→ Ready
```

---

# 13. Definition of Done

Implementation hoàn thành khi:

- Existing Story skip Phase 2.
- Existing Story không gọi GenerateContent nếu current content đã ổn định.
- Vocabulary/Quiz/Discussion vẫn được tạo trong Phase 3 bằng pipeline hiện tại.
- Intake/Evaluate/Adapt không được đưa vào `ContentGenerationService`.
- Hai nhánh phải hội tụ qua `IStableVersionArtifactHandoffService`.
- Phase 4 chỉ thực hiện review, validation và approval.
- Original StoryVersion không bị overwrite.
- Hard safety không thể override.
- Profile mismatch có Adapt/Edit/Keep Original đúng quyền.
- Artifact luôn gắn đúng current StoryVersion.
- Approval tạo media handoff đúng một lần.
- Không endpoint nào đưa Story trực tiếp từ Draft sang Ready.
- AI Story hiện tại không bị regression.

---

# 14. Kết luận

Không refactor Vocabulary/Quiz/Discussion sang Phase 4 trong iteration này.

Thay đổi trọng tâm là tạo một entry point mới vào giữa Phase 3:

```text
AI Story:
GenerateContent ─┐
                 ├→ Vocabulary → Quiz → Discussion
Existing Story:  │
Evaluate/Adapt ──┘
```

Cách làm này giữ nguyên pipeline đã có, giảm regression và cho phép Existing Story dùng chung toàn bộ Phase 3 artifact generation, Phase 4 review và Phase 5 media.

---

# 15. Future Extension — AI Semantic Safety cho Existing Content

## 15.1. Mục tiêu

Rule-based guardrail tiếp tục là hard gate của Core. Semantic Safety được bổ sung
để phát hiện các tình huống khó xác định chỉ bằng keyword/category, ví dụ:

- Bạo lực được mô tả gián tiếp hoặc bình thường hóa.
- Hành vi nguy hiểm mà trẻ có thể bắt chước.
- Bắt nạt, đe dọa, thao túng hoặc lạm dụng.
- Mức độ sợ hãi không phù hợp độ tuổi.
- Chủ đề tự làm hại bản thân, chất gây nghiện hoặc tình dục được diễn đạt ẩn dụ.
- Nội dung có ý nghĩa khác khi xét toàn bộ ngữ cảnh câu chuyện.

Semantic Safety không thay thế SafetyPolicy và không được override hard block.

## 15.2. Vị trí trong pipeline

```text
Normalized Existing Content
↓
Core Rule-based Safety
├── BLOCKED → dừng ngay, không gọi model
└── PASS / NEEDS_SEMANTIC_REVIEW
              ↓
      AI Semantic Safety Evaluator
              ↓
      Core Decision Aggregator
      ├── PASS
      ├── MANUAL_REVIEW_REQUIRED
      └── BLOCKED
```

Ở chế độ enforcement, Existing Story chỉ được Profile Fit Evaluation khi Safety
Decision cuối cùng là `PASS`.

## 15.3. Boundary Core và AI Service

```text
Core
├── Resolve effective SafetyPolicy
├── Run deterministic hard rules
├── Build sanitized semantic request
├── Validate AI response schema
├── Aggregate final decision
└── Persist audit evidence

AI Service
├── Semantic classification
├── Context-aware harm detection
├── Age-suitability safety analysis
└── Structured response only
```

AI Service không được:

- Thay đổi SafetyPolicy.
- Tự approve StoryVersion.
- Tự cập nhật Story status.
- Tự rewrite nội dung để bypass vi phạm.
- Trả chain-of-thought hoặc suy luận nội bộ dài.

## 15.4. Interface đề xuất

```csharp
public interface IExistingStorySemanticSafetyEvaluator
{
    Task<SemanticSafetyResult> EvaluateAsync(
        SemanticSafetyRequest request,
        CancellationToken cancellationToken);
}
```

Request tối thiểu:

```text
requestId
storyId
storyVersionId
contentHash
policySnapshotHash
language
ageBand
readingLevel
effectiveCategoryRules
canonicalContent
```

Không gửi:

- Tên thật của trẻ.
- Email hoặc thông tin liên hệ.
- Ngày sinh đầy đủ.
- Dữ liệu supervisor không liên quan.

## 15.5. Structured output contract

```json
{
  "schemaVersion": 1,
  "decision": "manual_review_required",
  "findings": [
    {
      "categoryCode": "DANGEROUS_IMITATION",
      "severity": "medium",
      "ageSuitability": "not_suitable",
      "evidence": [
        {
          "start": 120,
          "endExclusive": 184,
          "excerpt": "Short evidence excerpt"
        }
      ],
      "reasonCode": "IMITATABLE_UNSAFE_BEHAVIOR"
    }
  ],
  "summaryCode": "SEMANTIC_RISK_FOUND"
}
```

Chỉ chấp nhận enum/code đã định nghĩa. Core phải reject:

- JSON sai schema.
- Category không tồn tại trong policy/catalog.
- Offset evidence nằm ngoài content.
- Decision mâu thuẫn findings.
- Kết quả không khớp `storyVersionId`, `contentHash` hoặc `policySnapshotHash`.

## 15.6. Final decision rules

Thứ tự ưu tiên:

```text
Hard Rule BLOCKED
    > Semantic BLOCKED
    > Semantic MANUAL_REVIEW_REQUIRED
    > PASS
```

Không dùng một confidence score duy nhất để auto-approve. Severity và decision
phải được quyết định bằng policy mapping trong Core.

Nếu model timeout, provider block request, response malformed hoặc không kết luận:

```text
decision = MANUAL_REVIEW_REQUIRED
canProgress = false
errorCode = SEMANTIC_SAFETY_UNAVAILABLE
```

Đây là fail-closed nhưng không tự động gắn nội dung là vi phạm.

## 15.7. Nội dung dài

Với content vượt context/size limit:

1. Chia theo paragraph/scene boundary.
2. Giữ overlap nhỏ giữa các chunk để không mất ngữ cảnh.
3. Đánh giá từng chunk.
4. Chạy một bước aggregate trên findings đã cấu trúc.
5. Lấy quyết định nghiêm ngặt nhất.
6. Lưu offset theo canonical content, không theo chuỗi đã normalize lần hai.

Không cắt âm thầm phần cuối câu chuyện.

## 15.8. Persistence và stale-result protection

Iteration đầu có thể lưu trong `StoryGenerationJob.GenerationMetadataJson`:

```text
semanticSafety.schemaVersion
semanticSafety.provider
semanticSafety.model
semanticSafety.promptVersion
semanticSafety.storyVersionId
semanticSafety.contentHash
semanticSafety.policySnapshotHash
semanticSafety.decision
semanticSafety.findings
semanticSafety.checkedAt
```

Không lưu chain-of-thought. Chỉ lưu decision, reason code và evidence ngắn cần
cho audit/review.

Kết quả phải bị loại nếu:

- Current StoryVersion đã thay đổi.
- Content hash thay đổi.
- Effective SafetyPolicy thay đổi.
- Job concurrency token thay đổi.

## 15.9. AI adaptation sau semantic finding

Semantic Safety không tự gọi Adapt.

```text
Semantic finding
↓
Supervisor xem category + evidence
├── Block/Archive
├── Manual Edit
└── Request AI Adapt
          ↓
Create new StoryVersion
          ↓
Run Rule-based Safety lại từ đầu
          ↓
Run Semantic Safety lại
```

Không được sửa version gốc và không được coi adapted output là an toàn nếu chưa
re-evaluate.

## 15.10. Rollout plan

### Stage 1 — Shadow

- AI evaluator chạy nhưng không chặn pipeline.
- So sánh với quyết định reviewer.
- Không hiển thị kết quả như quyết định chính thức.

### Stage 2 — Advisory

- Hiển thị findings và evidence cho Parent/Teacher.
- Risk bất định yêu cầu manual review.
- Hard rule vẫn là enforcement duy nhất.

### Stage 3 — Enforcement

- Semantic `BLOCKED` ngăn progression.
- `MANUAL_REVIEW_REQUIRED` bắt buộc reviewer quyết định.
- Chỉ bật sau khi evaluation dataset đạt tiêu chí chấp nhận.

Feature flag đề xuất:

```text
AI:ExistingStorySemanticSafety:Mode = Off | Shadow | Advisory | Enforcement
```

## 15.11. Test và evaluation

Tạo bộ test tiếng Việt theo age band, bao gồm:

- Safe content rõ ràng.
- Unsafe content rõ ràng.
- Ẩn dụ và ngữ cảnh vùng xám.
- Truyện cổ tích có xung đột nhưng không mô tả đồ họa.
- Hành vi nguy hiểm có kết quả tiêu cực so với nội dung khuyến khích bắt chước.
- Prompt injection nằm trong câu chuyện upload.
- Content dài nhiều chunk.
- Model timeout/malformed/provider safety block.
- StoryVersion hoặc policy thay đổi khi request đang chạy.

Theo dõi tối thiểu:

```text
false_negative_rate
false_positive_rate
manual_review_rate
provider_block_rate
malformed_response_rate
latency
cost_per_story
```

Ưu tiên giảm false negative cho nhóm nguy cơ nghiêm trọng; không tối ưu approval
rate bằng cách hạ policy threshold.
