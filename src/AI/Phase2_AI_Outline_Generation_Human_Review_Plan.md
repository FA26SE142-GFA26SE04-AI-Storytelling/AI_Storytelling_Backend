# PHASE 2 — AI OUTLINE GENERATION & HUMAN REVIEW

## 1. Mục tiêu

Phase 2 chuyển `Accepted Input Snapshot` từ Phase 1 thành một **Outline đã được Parent/Teacher review và approve**, sau đó tạo **durable handoff** sang Phase 3 để sinh nội dung truyện.

```text
INPUT
Accepted Input Snapshot
+ Story Draft
+ Generation Request
        ↓
AI Generate Outline
        ↓
Validate
        ↓
Persist StoryVersion
        ↓
Human Review
        ↓
Edit / Regenerate / Approve
        ↓
Exact Approved StoryVersion
        ↓
Durable GenerateContent Handoff
        ↓
PHASE 3
```

### Ngoài phạm vi Phase 2

Phase 2 **không xử lý**:

- Full Story Content
- Lesson hoàn chỉnh
- Vocabulary
- Quiz
- Discussion Questions
- Readability final
- Content Refine
- Image
- TTS
- Final Story Approval
- Ready

---

## 2. Boundary giữa Core Backend và AI Module

### Core Backend

Core Backend là **Business Authority**.

Core chịu trách nhiệm:

- Nhận handoff từ Phase 1
- Kiểm tra Story / Generation Request
- Authorization
- Concurrency
- Tạo / quản lý Generation Job
- Tạo `GenerateOutlineCommand`
- Persist `StoryVersion`
- Quản lý `is_current`
- Quản lý `Story.status`
- Human Edit
- Human Regenerate Request
- Human Approval
- Durable handoff sang Phase 3

### AI Module

AI Module là **AI Execution Engine**.

AI Module chịu trách nhiệm:

- Resolve prompt
- Build prompt
- Call LLM
- Parse structured output
- Schema validation cho AI output
- Output safety validation
- Technical retry
- Generation telemetry

### Nguyên tắc

```text
Core Backend = Business Authority
AI Module    = AI Execution
```

AI Module **không trực tiếp**:

- INSERT/UPDATE `stories`
- INSERT/UPDATE `story_versions`
- Set `is_current`
- Approve outline
- Set business status

---

## 3. Input chính thức của Phase 2

Phase 2 nhận từ Phase 1:

```text
storyId
generationRequestId
acceptedInputSnapshotId
acceptedInputFingerprint/version
correlationId
handoffId/jobId
```

Accepted Input Snapshot chứa tối thiểu:

```text
AgeBand
ReadingLevel
VocabularyLevel
Language
Topic
Genre
CharacterMode
Characters
SettingMode
Setting
Lesson
TargetLength
MaximumLength
EffectiveSafetyConstraints
```

### Rule

Phase 2 **không được đọc lại raw form** để build prompt.

Phase 2 cũng không tự tải lại toàn bộ Child Profile để thay thế snapshot đã được Phase 1 chấp nhận.

---

## 4. Phase 2A — Receive & Validate Handoff

### P2.1 — Nhận Generate Outline Handoff

Phase 1 hoàn tất:

```text
input_accepted
```

và tạo durable handoff:

```text
operation = generate_outline
```

Phase 2 worker/orchestrator consume handoff này.

### P2.2 — Revalidate Business State

Trước khi gọi AI, Core kiểm tra:

- Story tồn tại
- `source = ai`
- Story chưa archived
- Generation Request tồn tại
- Request đang ở trạng thái `input_accepted`
- Accepted Snapshot tồn tại
- Handoff chưa được xử lý
- Không có outline generation khác đang active

Nếu fail:

```text
Không gọi AI
→ đánh dấu job failed/cancelled phù hợp
```

---

## 5. Phase 2B — Outline Generation Job

Core tạo hoặc claim `story_generation_jobs`.

Các field nên có tối thiểu:

```text
story_id
generation_request_id
operation
stage
status
attempt_no
prompt_catalog_version_id
story_version_id nullable
started_at
completed_at
error_code nullable
```

Ví dụ:

```text
operation = generate_outline
stage = outline_generating
status = processing
attempt_no = 1
```

### Business Rule

Một Story chỉ được có **một Outline Generation Operation đang active** tại cùng một thời điểm.

---

## 6. Phase 2C — Build GenerateOutlineCommand

Core tạo command từ Accepted Snapshot.

```csharp
public sealed record GenerateOutlineCommand
{
    public int StoryId { get; init; }
    public int GenerationRequestId { get; init; }
    public int AcceptedSnapshotId { get; init; }

    public string AgeBand { get; init; } = default!;
    public int ReadingLevel { get; init; }
    public string VocabularyLevel { get; init; } = default!;
    public string Language { get; init; } = default!;

    public string Topic { get; init; } = default!;
    public string? Genre { get; init; }

    public string CharacterMode { get; init; } = default!;
    public IReadOnlyList<string> Characters { get; init; } = [];

    public string SettingMode { get; init; } = default!;
    public string? Setting { get; init; }

    public string Lesson { get; init; } = default!;

    public int TargetLength { get; init; }
    public int MaximumLength { get; init; }
}
```

Không nên đưa vào command:

- `StoryStatus` do client gửi
- `UserId` nếu AI không cần
- `ChildId` nếu AI không cần
- `PromptVersion` do FE/Core tự chọn

Prompt version được AI Module resolve từ Prompt Catalog.

---

## 7. Phase 2D — Resolve Prompt Configuration

AI Module nhận command:

```text
GenerateOutlineCommand
        ↓
Prompt Template Resolver
        ↓
Active Outline Prompt Version
        ↓
Model Configuration
```

Model configuration theo operation:

```text
operation = outline_generation
```

Có thể gồm:

```text
Provider
Model
Temperature
MaxOutputTokens
Timeout
MaxAttempts
```

Không hard-code các giá trị này vào business service.

---

## 8. Phase 2E — Build Outline Prompt

Prompt nên tách thành:

```text
SYSTEM INSTRUCTION
+
ACCEPTED BUSINESS CONTEXT
+
OUTPUT SCHEMA
```

AI chỉ được yêu cầu sinh:

```text
Title
Opening
Development
Ending
```

Không sinh:

```text
Full Story
Vocabulary
Quiz
Discussion
Image
Audio
```

`TargetLength` dùng để định hướng quy mô Story cuối cùng, không phải độ dài của Outline.

---

## 9. Phase 2F — Generate Outline

AI Module gọi LLM:

```text
Prompt
 ↓
LLM
 ↓
Structured Outline Response
```

Ví dụ:

```json
{
  "title": "Chú Thỏ và Chiếc Cầu Nhỏ",
  "opening": "...",
  "development": "...",
  "ending": "..."
}
```

---

## 10. Phase 2G — Parse Structured Output

Các trường hợp invalid:

- Invalid JSON
- Missing field
- Wrong type
- Unexpected schema
- Empty required field

Không persist StoryVersion nếu parse/schema fail.

---

## 11. Phase 2H — Outline Schema Validation

Validation tối thiểu:

```text
title != empty
opening != empty
development != empty
ending != empty

title <= configured length
section <= configured length
language hợp lệ
schema đúng
```

---

## 12. Phase 2I — Output Safety Validation

Input đã pass Phase 1 không có nghĩa AI Output luôn an toàn.

```text
LLM Outline
   ↓
Output Safety Validation
```

Kiểm tra:

- Blocked categories
- Restricted content
- Age appropriateness
- Unsafe semantic content
- Prompt leakage / system-instruction leakage

Nếu fail:

```text
Không tạo usable StoryVersion
```

---

## 13. Technical Retry

Phải phân biệt:

```text
Technical Retry
≠
User Regenerate
```

### Technical Retry dùng cho

- Provider timeout
- Transient provider error
- Invalid structured response
- Schema invalid

Khuyến nghị:

```text
MaxAttempts = 3
```

Không dùng technical retry cho việc user không thích outline.

---

## 14. Retry Exhausted

Nếu tất cả attempts fail:

```text
stage = outline_failed
status = failed
```

Không tạo StoryVersion giả.

Story vẫn:

```text
status = draft
```

---

## 15. Phase 2J — AI trả OutlineResult

AI Module trả về:

```text
Title
Opening
Development
Ending
GenerationMetadata
```

Metadata có thể gồm:

```text
provider
model
promptVersion
tokenUsage
duration
attemptCount
```

---

## 16. Phase 2K — Persist StoryVersion

Outline đầu tiên:

```text
StoryVersion V1
```

Ví dụ:

```text
story_id = 100
version_no = 1
edit_type = initial

title = ...
outline_opening = ...
outline_development = ...
outline_ending = ...

content = NULL
lesson = NULL

is_current = true
```

---

## 17. Current Version Invariant

Khi tạo version mới:

```text
V1.is_current = false
V2.is_current = true
```

phải chạy trong cùng transaction.

PostgreSQL nên có:

```sql
CREATE UNIQUE INDEX story_versions_one_current_per_story
ON story_versions(story_id)
WHERE is_current = true;
```

---

## 18. Link Job → StoryVersion

Sau khi persist thành công:

```text
story_generation_jobs.story_version_id = createdStoryVersionId
```

Trace:

```text
Generation Request
      ↓
Generation Job
      ↓
StoryVersion
```

---

## 19. Story chuyển sang Outline Review

Sau khi outline được tạo:

```text
stories.status = outline_review
```

Job:

```text
stage = outline_generated
status = completed
```

---

## 20. Phase 2L — Human Review

Frontend hiển thị:

```text
Title
Opening
Development
Ending
```

Actions:

```text
Edit
Regenerate
Approve
View Version History
```

---

## 21. Edit Outline

Parent/Teacher được edit:

- Title
- Opening
- Development
- Ending

Không update trực tiếp version cũ.

```text
V1
 ↓
Human Edit
 ↓
Validate
 ↓
V2
```

---

## 22. Validate Human Edit

Human Edit phải qua:

```text
Basic Validation
↓
Safety Validation
↓
PASS?
```

Human input không được bypass Safety vì người nhập là Parent/Teacher.

---

## 23. Save Human Edited Version

Nếu pass:

```text
V2.edit_type = human_edited
V2.editor_user_id = currentUser
V2.content = NULL
V2.is_current = true
```

Version cũ được giữ lại.

---

## 24. Regenerate Outline

User bấm `Regenerate`.

Core kiểm tra:

- Base Version vẫn current
- Story vẫn `outline_review`
- Không có generation khác đang active
- User có `GenerateStory`

Sau đó tạo:

```text
operation = regenerate_outline
```

---

## 25. Regenerate phải dùng Base Version

Command cần biết:

```text
baseStoryVersionId
```

Trước khi commit AI result:

```text
baseStoryVersionId vẫn current?
```

Nếu không:

```text
STALE RESULT
→ không overwrite current version
```

---

## 26. Regenerated Version

Nếu valid:

```text
V3.edit_type = ai_regenerated
V3.content = NULL
V3.is_current = true
```

Sau đó quay lại `Outline Review`.

---

## 27. Version Retention

Không hard delete hoặc soft delete StoryVersion chỉ vì vượt số lượng.

StoryVersion là immutable audit history.

Nếu cần chống abuse:

```text
Rate Limit
MaxRegenerationPerTimeWindow
```

---

## 28. Approve Outline

Core kiểm tra:

- Story status = `outline_review`
- Version tồn tại
- Version là current
- `content = NULL`
- Outline validation đã pass
- User có `approve_story`
- Không có regenerate đang chạy

---

## 29. Outline luôn cần Human Approval

Phase 2 **không dùng auto-publish policy** để auto-approve outline.

```text
AI Generate
 ↓
Human Review
 ↓
Approve
```

---

## 30. Ghi đúng Version đã Approve

Cần truy vết:

```text
approved_story_version_id
approved_by
approved_at
```

Có thể dùng:

```text
story_versions.outline_approved_at
story_versions.outline_approved_by_user_id
```

hoặc approval/audit mechanism sẵn có.

---

## 31. Phase 2M — Prepare Phase 3 Handoff

Sau approval:

```text
Approved StoryVersion
```

Core tạo:

```text
GenerateContentCommand
```

Payload:

```text
storyId
generationRequestId
approvedStoryVersionId
acceptedInputSnapshotId
correlationId
```

Phase 3 phải nhận chính xác `approvedStoryVersionId`.

---

## 32. Durable Handoff sang Phase 3

Nên:

```text
Approve Version
+
Create GenerateContent Handoff
```

trong transaction/outbox/durable job mechanism.

Phase 2 hoàn thành khi:

```text
Outline Approved
+
Exact Approved Version Known
+
GenerateContent Handoff Persisted
```

---

## 33. State Model

### Story Business Status

```text
draft
outline_review
content_review
approved
media_processing
ready
archived
rejected
```

Trong Phase 2:

```text
draft
→ outline_review
```

### Job Stage

```text
outline_pending
outline_generating
outline_generated
outline_failed
outline_approved
```

---

## 34. Business Rules

| Mã | Business Rule |
|---|---|
| BR-P2-01 | Chỉ Story `source=ai` được vào AI Outline Generation. |
| BR-P2-02 | Phase 2 chỉ bắt đầu từ Generation Request đã `input_accepted`. |
| BR-P2-03 | AI phải dùng Accepted Input Snapshot của Phase 1. |
| BR-P2-04 | Không được sinh outline từ dữ liệu form chưa accepted. |
| BR-P2-05 | Một Story chỉ có một outline generation operation active tại một thời điểm. |
| BR-P2-06 | StoryVersion chỉ được tạo sau khi AI output qua schema và safety validation. |
| BR-P2-07 | `StoryVersion.content` luôn `NULL` trong Phase 2. |
| BR-P2-08 | Outline đầu tiên có `edit_type=initial`. |
| BR-P2-09 | Human Edit tạo StoryVersion mới, không overwrite version cũ. |
| BR-P2-10 | Human Edit phải được validate lại trước khi trở thành current. |
| BR-P2-11 | User Regenerate khác Technical Retry. |
| BR-P2-12 | Regenerate tạo StoryVersion mới với `edit_type=ai_regenerated`. |
| BR-P2-13 | Chỉ current StoryVersion mới được edit/regenerate/approve. |
| BR-P2-14 | Kết quả AI dựa trên stale base version không được trở thành current. |
| BR-P2-15 | Outline luôn cần human approval. |
| BR-P2-16 | `GenerateStory` không đồng nghĩa `ApproveStory`. |
| BR-P2-17 | Chỉ user có `approve_story` trên child tương ứng mới được approve outline. |
| BR-P2-18 | Approval phải xác định chính xác StoryVersion được approve. |
| BR-P2-19 | Không xóa version cũ để giới hạn số lượng version. |
| BR-P2-20 | Phase 3 chỉ được handoff sau khi outline được approve. |
| BR-P2-21 | Handoff Phase 3 phải durable và idempotent. |
| BR-P2-22 | Phase 2 không sinh full content hoặc learning artifacts. |

---

## 35. API đề xuất

```http
GET /api/v1/stories/{storyId}/outline
GET /api/v1/stories/{storyId}/outline/versions
GET /api/v1/stories/{storyId}/outline/versions/{versionNumber}
PUT /api/v1/stories/{storyId}/outline/versions/{versionNumber}
POST /api/v1/stories/{storyId}/outline/versions/{versionNumber}/regenerate
POST /api/v1/stories/{storyId}/outline/versions/{versionNumber}/approve
```

Retry technical failure, nếu cần:

```http
POST /api/v1/stories/{storyId}/outline/retry
```

---

## 36. Implementation Plan cho Coding Agent

### Phase 2A — Foundation

#### Step 1 — Inspect Current Implementation

Tìm và đọc:

```text
Story
StoryVersion
StoryGenerationRequest
StoryGenerationJob
GenerateOutlineHandler
ILlmClient
PromptProvider
GenerationSchemas
Authorization
Outbox / Background Worker
```

Không tạo duplicate abstraction nếu repo đã có.

#### Step 2 — Freeze Contracts

Chốt:

```text
GenerateOutlineCommand
GenerateOutlineResult
OutlineDto
EditOutlineRequest
RegenerateOutlineRequest
ApproveOutlineRequest
```

#### Step 3 — Database Alignment

Kiểm tra schema hiện tại và chỉ migration các field thực sự thiếu.

Không thêm các StoryStatus không thuộc business lifecycle như:

```text
ReadyForContent
Generated
Published
```

### Phase 2B — AI Module

#### Step 4 — Prompt Resolver

Input:

```text
Accepted Snapshot
```

Output:

```text
Prompt
PromptVersion
Model Configuration
```

#### Step 5 — GenerateOutlineHandler

```text
Validate Command
↓
Resolve Prompt
↓
Call LLM
↓
Parse
↓
Schema Validate
↓
Output Safety
↓
Return Result
```

Không persist Core entities.

#### Step 6 — Technical Retry

Implement:

```text
MaxAttempts
Timeout
Backoff
Attempt Telemetry
Late Response Protection
```

### Phase 2C — Core Orchestration

#### Step 7 — Outline Generation Consumer

Consume Phase 1 handoff và validate Story / Request / Snapshot / Current Operation.

#### Step 8 — Persist Generated Version

Transaction:

```text
Create StoryVersion
Switch is_current
Link Job → StoryVersion
Story → outline_review
Complete Job
```

#### Step 9 — Edit Outline

Implement authorization, current-version check, optimistic concurrency, validation và new StoryVersion.

#### Step 10 — Regenerate Outline

Implement authorization, base-version check, regeneration job, AI call, stale-result check và new StoryVersion.

#### Step 11 — Approve Outline

Implement:

```text
approve_story Authorization
Current Version Check
No Active Generation
Valid State
Mark Exact Version Approved
Create Durable Phase 3 Handoff
```

---

## 37. Test Plan

### Happy Path

```text
Phase 1 Accepted
→ Job Created
→ AI Generates Valid Outline
→ V1 Created
→ outline_review
→ User Approves
→ Exact V1 Approved
→ Phase 3 Handoff Exists
```

### Edit

```text
V1
→ User Edit
→ Validation Pass
→ V2
→ V1 Preserved
→ V2 Current
```

### Regenerate

```text
V2
→ Regenerate
→ AI Valid
→ V3
→ V3 Current
```

### Invalid AI Schema

```text
Attempt 1 Invalid
→ Retry
→ Attempt 2 Valid
→ Only One StoryVersion Created
```

### Retry Exhausted

```text
3 Attempts Fail
→ outline_failed
→ No StoryVersion
```

### Unsafe Human Edit

```text
Edit
→ Safety Fail
→ Unsafe Version Không Trở Thành Current
```

### Authorization

```text
generate_story = true
approve_story = false

→ Generate/Regenerate Allowed
→ Approve Denied
```

### Stale Regeneration

```text
Regenerate từ V2 bắt đầu
↓
V3 được tạo bởi thao tác khác
↓
Old Regenerate Result Return
↓
Reject As Stale
```

### Concurrent Regenerate

```text
Regenerate A
Regenerate B
↓
Only One Active Operation
```

### Approve During Generation

```text
Regeneration Active
↓
Approve
↓
409 Conflict
```

### Phase 3 Handoff Failure

Approval + Handoff phải atomic/durable.

Không được tồn tại trạng thái:

```text
Outline Approved
nhưng mất GenerateContent Handoff
```

---

## 38. Definition of Done

Phase 2 chỉ hoàn thành khi:

- [ ] Consume Phase 1 handoff an toàn
- [ ] Generate Outline từ Accepted Snapshot
- [ ] Schema validation hoạt động
- [ ] Output safety validation hoạt động
- [ ] Technical retry bounded
- [ ] StoryVersion được tạo immutable
- [ ] `is_current` invariant được giữ
- [ ] Human Edit tạo version mới
- [ ] Human Edit được revalidate
- [ ] Regenerate tạo version mới
- [ ] Stale result không overwrite current
- [ ] `approve_story` được enforce
- [ ] Exact StoryVersion được approve
- [ ] Durable Phase 3 handoff được tạo
- [ ] Không sinh Full Content trong Phase 2
- [ ] Build và test pass

---

## 39. Sơ đồ tổng kết

```text
PHASE 1
input_accepted
       ↓
Accepted Snapshot
       ↓
Durable GenerateOutline Handoff
       ↓
──────────────────────────────────
             PHASE 2
──────────────────────────────────
       ↓
Validate Handoff
       ↓
Create / Claim Outline Job
       ↓
GenerateOutlineCommand
       ↓
┌─────────────────────────────┐
│         AI MODULE           │
│ Resolve Prompt              │
│       ↓                     │
│ Build Prompt                │
│       ↓                     │
│ LLM Generate                │
│       ↓                     │
│ Parse                       │
│       ↓                     │
│ Schema Validation           │
│       ↓                     │
│ Output Safety               │
│       ↓                     │
│ Technical Retry             │
└──────────────┬──────────────┘
               ↓
         OutlineResult
               ↓
┌─────────────────────────────┐
│        CORE BACKEND         │
│ Create StoryVersion         │
│       ↓                     │
│ Set Current                 │
│       ↓                     │
│ outline_review              │
└──────────────┬──────────────┘
               ↓
         HUMAN REVIEW
       ┌───────┼─────────┐
       │       │         │
      Edit  Regenerate  Approve
       │       │         │
       ↓       ↓         │
   Validate    AI        │
       │       │         │
       ↓       ↓         │
 New Version New Version │
       └───────┬─────────┘
               ↓
          Review Again
               │
               └──── Approve
                       ↓
               Mark Exact Version
                       ↓
             Durable Content Handoff
                       ↓
──────────────────────────────────
             PHASE 3
──────────────────────────────────
```
