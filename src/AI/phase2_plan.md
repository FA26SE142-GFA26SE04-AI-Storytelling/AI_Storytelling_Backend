# Kế hoạch Triển khai Phase 2: Generate Outline

**Tài liệu giao việc cho coding agent — phiên bản 1 — ngày 12 tháng 9 năm 2026**

---

## Mục lục

1. [Tóm tắt Phase 2](#1-tóm-tắt-phase-2)
2. [Các câu hỏi cần làm rõ](#2-các-câu-hỏi-cần-làm-rõ)
3. [Các quy tắc nghiệp vụ (Business Rules)](#3-các-quy-tắc-nghiệp-vụ-business-rules)
4. [Kiến trúc & Data Flow](#4-kiến-trúc--data-flow)
5. [Contract & Data Models](#5-contract--data-models)
6. [Database Schema Changes](#6-database-schema-changes)
7. [API Endpoints](#7-api-endpoints)
8. [Step-by-step Implementation Plan](#8-step-by-step-implementation-plan)
9. [Files cần tạo/sửa](#9-files-cần-tạosửa)
10. [Test Cases](#10-test-cases)
11. [Phụ thuộc & Prerequisites](#11-phụ-thuộc--prerequisites)

---

## 1. Tóm tắt Phase 2

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           PHASE 2 OVERVIEW                              │
├─────────────────────────────────────────────────────────────────────────┤
│  Input:  Handoff từ Phase 1 (storyId, requestId, acceptedInputSnapshot) │
│  Output: StoryVersion V1 với Outline + status = OutlineReview           │
│          HOẶC: Handoff thất bại + thông báo lỗi                        │
│                                                                          │
│  User Flow:                                                             │
│  [Generate Outline] → [Review & Edit] → [Regenerate/Approve]            │
│                        ↓                                                │
│                   [Edit] → [Save Version] → [Continue]                   │
└─────────────────────────────────────────────────────────────────────────┘
```

### Mục tiêu chính:
1. Sinh outline từ accepted input snapshot
2. Tạo StoryVersion với outline (chưa có content)
3. Cho phép user review, edit, regenerate, approve
4. Handoff sang Phase 3 khi outline được approve

---

## 2. Các câu hỏi cần làm rõ

### 2.1 Trigger Mechanism

| Câu hỏi | Tùy chọn | Đề xuất |
|----------|-----------|----------|
| Khi nào outline được sinh? | A. User click button | A: User click button |
| | B. Auto-sau khi Phase 1 accept | |
| | C. Scheduled batch job | |

**Lý do**: User cần thấy được trạng thái "đang sinh outline" và có thể hủy nếu change ý định.

### 2.2 LLM Configuration

| Câu hỏi | Tùy chọn | Đề xuất |
|----------|-----------|----------|
| Model nào cho outline generation? | A. Same như content | A: Same model |
| | B. Cheaper model cho outline | |
| Temperature? | A. 0.7 | A: 0.7 (creative nhưng controlled) |
| | B. 0.5 | |
| | C. 0.3 | |
| Max tokens cho outline? | A. 2000 | A: 2000 |
| | B. 1000 | |
| | C. Configurable | |

### 2.3 Approval Mode

| Câu hỏi | Tùy chọn | Đề xuất |
|----------|-----------|----------|
| Auto-approve outline nếu policy = Auto? | A. Yes | A: Yes, matching Phase 1 design |
| | B. No, always manual | |
| Auto-approve threshold? | A. Same như content | A: Same threshold |
| | B. Lower threshold | |
| | C. No auto-approve for outline | |

### 2.4 Retry Configuration

| Câu hỏi | Tùy chọn | Đề xuất |
|----------|-----------|----------|
| Max retry cho outline generation? | A. 3 | A: 3 |
| | B. 2 | |
| | C. 5 | |
| Exponential backoff? | A. Yes | A: Yes |
| | B. No (fixed delay) | |
| Delay between retries? | A. 2s, 4s, 8s | A: 2s, 4s, 8s |
| | B. 1s, 2s, 4s | |

### 2.5 Version Management

| Câu hỏi | Tùy chọn | Đề xuất |
|----------|-----------|----------|
| Giữ tối đa bao nhiêu outline versions? | A. 10 | A: 10 |
| | B. Unlimited | |
| | C. 5 | |
| Archive old versions? | A. Yes, soft delete | A: Yes |
| | B. No, hard delete | |
| | C. Keep only approved | |

### 2.6 Edit Restrictions

| Câu hỏi | Tùy chọn | Đề xuất |
|----------|-----------|----------|
| User có thể edit những gì? | A. Title + all sections | A: Full edit (Title + 3 sections) |
| | B. Only sections, not title | |
| | C. Only title | |
| Edit có re-trigger AI validation? | A. Yes | A: No (trust user for outline) |
| | B. No | |

---

## 3. Các quy tắc nghiệp vụ (Business Rules)

| Mã | Quy tắc |
|----|---------|
| BR-P2-01 | Mỗi Story chỉ có một outline generation đang active tại một thời điểm. |
| BR-P2-02 | Outline chỉ được sinh khi Story đang ở status Draft và Source = AI. |
| BR-P2-03 | User phải có quyền GenerateStory trên child profile để regenerate. |
| BR-P2-04 | User phải có quyền GenerateStory HOẶC ApprovalMode = Auto để approve. |
| BR-P2-05 | StoryVersion.content LUÔN NULL trong Phase 2. |
| BR-P2-06 | Edit bởi human tạo StoryVersion mới với edit_type = HUMAN_EDITED. |
| BR-P2-07 | Regenerate tạo StoryVersion mới với edit_type = AI_REGENERATED. |
| BR-P2-08 | Chỉ version mới nhất mới có thể được edit hoặc approve. |
| BR-P2-09 | Approval chuyển Story status sang giá trị mới (OutlineApproved hoặc ReadyForContent). |
| BR-P2-10 | Handoff sang Phase 3 chỉ xảy ra khi outline đã được approve. |

---

## 4. Kiến trúc & Data Flow

### 4.1 Component Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              PHASE 2 COMPONENTS                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────┐         ┌───────────────────────┐                         │
│  │   FE/Client  │────────▶│  OutlineController    │                         │
│  └──────────────┘         │  (Core API)           │                         │
│                            └───────────┬───────────┘                         │
│                                        │                                     │
│                            ┌───────────▼───────────┐                         │
│                            │  IOutlineService      │                         │
│                            │  (Orchestrator)      │                         │
│                            └───────────┬───────────┘                         │
│                                        │                                     │
│         ┌──────────────────────────────┼──────────────────────────────┐       │
│         │                              │                              │       │
│         ▼                              ▼                              ▼       │
│  ┌──────────────┐           ┌──────────────────┐           ┌─────────────┐  │
│  │ Authorization│           │ OutlineGenerator  │           │  JobManager │  │
│  │   Service    │           │  (AI Module)      │           │             │  │
│  └──────────────┘           └──────────────────┘           └─────────────┘  │
│                                        │                              │       │
│                                        ▼                              ▼       │
│                               ┌──────────────────┐           ┌─────────────┐  │
│                               │ PromptProvider   │           │ StoryVersion│  │
│                               │ + LLM Client    │           │ Repository  │  │
│                               └──────────────────┘           └─────────────┘  │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.2 Sequence Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           SEQUENCE DIAGRAM                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  User        Controller      Service        AI Module      DB                │
│   │              │              │               │            │                │
│   │──[1] POST───▶│              │               │            │                │
│   │              │──[2] Validate Handoff ───────│            │                │
│   │              │◀──────────────│───────────────│            │                │
│   │              │              │               │            │                │
│   │              │──[3] Create Job (outline_pending)         │                │
│   │              │              │               │            │──[INSERT]──▶│  │
│   │              │              │──[4] GenerateOutlineCommand            │  │
│   │              │              │──────────────▶│            │                │
│   │              │              │               │──[5] Build Prompt         │
│   │              │              │               │──[6] Call LLM              │
│   │              │◀──────────────│──[7] OutlineResult ──│            │       │
│   │              │              │               │            │                │
│   │              │──[8] Validate & Create StoryVersion                   │
│   │              │              │               │            │──[INSERT]──▶│  │
│   │              │              │──[9] Update Story.status = OutlineReview │
│   │              │              │               │            │──[UPDATE]──▶│  │
│   │              │◀─────────────│───────────────│            │                │
│   │◀──[10] 202 ──│              │               │            │                │
│   │              │              │               │            │                │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.3 State Machine

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         STORY STATUS TRANSITIONS                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Draft ──────── (Phase 1 Accept) ────────▶ Draft                           │
│    │                                     │                                  │
│    │ (User click Generate Outline)        │                                  │
│    ▼                                     │                                  │
│  [outline_pending] ◀─────────────────────┘                                  │
│    │                                                                     │
│    │ (Outline generated)                                                 │
│    ▼                                                                     │
│  OutlineReview ◀──────────────────────────────────────                    │
│    │                                              │                        │
│    ├──[Edit]──▶ Save Version ─────────────────────┘                        │
│    │                                                                   │
│    ├──[Regenerate]──▶ [outline_pending] ──▶ OutlineReview                 │
│    │                                                                   │
│    └──[Approve] ──▶ OutlineApproved ──▶ [ReadyForContent]                 │
│                            │                                              │
│                            │ (Phase 3 starts)                             │
│                            ▼                                              │
│                      GeneratingContent                                     │
│                            │                                              │
│                            ▼                                              │
│                       Generated ──▶ Published                              │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 5. Contract & Data Models

### 5.1 Request DTOs

```csharp
// Trigger outline generation
public record GenerateOutlineRequestDto
{
    public int StoryId { get; init; }
    public int RequestId { get; init; } // Phase 1 request ID
    public string? IdempotencyKey { get; init; } // Optional, for retry
}

// Edit outline
public record EditOutlineRequestDto
{
    public int StoryId { get; init; }
    public int VersionNumber { get; init; }
    public string Title { get; init; }
    public string OutlineOpening { get; init; }
    public string OutlineDevelopment { get; init; }
    public string OutlineEnding { get; init; }
}

// Regenerate outline
public record RegenerateOutlineRequestDto
{
    public int StoryId { get; init; }
    public int VersionNumber { get; init; } // Version to replace
}

// Approve outline
public record ApproveOutlineRequestDto
{
    public int StoryId { get; init; }
    public int VersionNumber { get; init; }
}
```

### 5.2 Response DTOs

```csharp
// Get outline progress
public record OutlineProgressDto
{
    public int StoryId { get; init; }
    public int? CurrentVersionNumber { get; init; }
    public string OutlineStatus { get; init; } // generating, outline_review, approved
    public string? Title { get; init; }
    public string? OutlineOpening { get; init; }
    public string? OutlineDevelopment { get; init; }
    public string? OutlineEnding { get; init; }
    public string? EditType { get; init; } // ai_generated, human_edited, ai_regenerated
    public int VersionCount { get; init; }
    public DateTime? GeneratedAt { get; init; }
    public string? ReasonCode { get; init; }
    public string? FallbackMessage { get; init; }
    public bool CanRegenerate { get; init; }
    public bool CanApprove { get; init; }
}

// Version history item
public record OutlineVersionDto
{
    public int VersionNumber { get; init; }
    public string Title { get; init; }
    public string EditType { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

### 5.3 AI Command (Internal)

```csharp
// Command gửi sang AI Module
public record GenerateOutlineCommand
{
    public int StoryId { get; init; }
    public int RequestId { get; init; }
    public string StoryTitle { get; init; } // Nullable, null = chưa có title
    public string AgeBand { get; init; }
    public int ReadingLevel { get; init; }
    public string VocabularyLevel { get; init; }
    public string Language { get; init; }
    public string Topic { get; init; }
    public string? Genre { get; init; }
    public string CharacterMode { get; init; }
    public IReadOnlyList<string> Characters { get; init; }
    public string SettingMode { get; init; }
    public string? Setting { get; init; }
    public string Lesson { get; init; }
    public int TargetLength { get; init; }
    public string PromptVersion { get; init; }
}

// Response từ AI Module
public record GenerateOutlineResponse
{
    public int StoryId { get; init; }
    public string Title { get; init; }
    public string OutlineOpening { get; init; }
    public string OutlineDevelopment { get; init; }
    public string OutlineEnding { get; init; }
    public GenerationMetadataDto Metadata { get; init; }
}
```

### 5.4 Enums cần thêm

```csharp
// Thêm vào GenerationEnums.cs
public enum OutlineGenerationStatus
{
    PendingOutline,        // Mới tạo, đang chờ generation
    GeneratingOutline,    // Đang gọi LLM
    OutlineGenerated,     // Đã sinh xong, chờ review
    OutlineApproved,      // User đã approve
    OutlineFailed         // Generation thất bại
}

// Thêm vào VersionEditType enum
public enum VersionEditType
{
    // Hiện có
    AiGenerated,
    HumanEdited,
    // Thêm mới
    AiRegenerated
}

// Thêm vào StoryStatus enum
public enum StoryStatus
{
    // Hiện có
    Draft,
    // Thêm mới
    OutlineReview,
    OutlineApproved,
    ReadyForContent,
    GeneratingContent,
    Generated,
    Published
}
```

---

## 6. Database Schema Changes

### 6.1 StoryVersion Updates

**Bảng hiện có: `story_versions`**

Cần thêm columns:

| Column | Type | Nullable | Mô tả |
|--------|------|----------|--------|
| `title` | varchar(200) | ✅ | Title của outline |
| `outline_opening` | text | ✅ | Phần mở đầu |
| `outline_development` | text | ✅ | Phần phát triển |
| `outline_ending` | text | ✅ | Phần kết thúc |
| `outline_generation_metadata` | jsonb | ✅ | Metadata từ LLM |
| `edit_type` | enum | ❌ | AI_GENERATED, HUMAN_EDITED, AI_REGENERATED |

### 6.2 StoryGenerationJob Updates

**Bảng hiện có: `story_generation_jobs`**

Cần thêm values cho `stage`:

```sql
-- Migration
ALTER TYPE job_stage ADD VALUE IF NOT EXISTS 'outline_pending';
ALTER TYPE job_stage ADD VALUE IF NOT EXISTS 'outline_generating';
ALTER TYPE job_stage ADD VALUE IF NOT EXISTS 'outline_review';
ALTER TYPE job_stage ADD VALUE IF NOT EXISTS 'outline_approved';
ALTER TYPE job_stage ADD VALUE IF NOT EXISTS 'outline_failed';
```

### 6.3 Indexes cần tạo

```sql
-- Index cho việc query outline versions nhanh
CREATE INDEX idx_story_versions_story_id_version 
ON story_versions(story_id, version_number DESC);

-- Index cho job processing
CREATE INDEX idx_jobs_stage_pending 
ON story_generation_jobs(stage) 
WHERE stage IN ('outline_pending', 'outline_generating');
```

---

## 7. API Endpoints

### 7.1 Proposed Routes

| Method | Route | Mô tả | Auth |
|--------|-------|--------|------|
| `POST` | `/api/v1/stories/{storyId}/outline/generate` | Bắt đầu sinh outline | GenerateStory permission |
| `GET` | `/api/v1/stories/{storyId}/outline/progress` | Lấy tiến độ & outline hiện tại | GenerateStory permission |
| `GET` | `/api/v1/stories/{storyId}/outline/versions` | Lấy lịch sử các versions | GenerateStory permission |
| `GET` | `/api/v1/stories/{storyId}/outline/versions/{versionNumber}` | Lấy một version cụ thể | GenerateStory permission |
| `PUT` | `/api/v1/stories/{storyId}/outline/versions/{versionNumber}` | Edit một version | GenerateStory permission |
| `POST` | `/api/v1/stories/{storyId}/outline/versions/{versionNumber}/regenerate` | Regenerate outline | GenerateStory permission |
| `POST` | `/api/v1/stories/{storyId}/outline/versions/{versionNumber}/approve` | Approve outline | GenerateStory hoặc Auto-approve |

### 7.2 Response Codes

| Status | Ý nghĩa | Khi nào |
|--------|---------|---------|
| `200 OK` | Thành công | GET operations |
| `202 Accepted` | Đã nhận request, đang xử lý | Generate started |
| `400 Bad Request` | Input không hợp lệ | Validation failed |
| `401 Unauthorized` | Chưa đăng nhập | Missing auth |
| `403 Forbidden` | Không có quyền | Permission denied |
| `404 Not Found` | Resource không tồn tại | Story/Version not found |
| `409 Conflict` | Conflict với trạng thái hiện tại | Already generating, already approved |
| `422 Unprocessable Entity` | Không thể thực hiện operation | Invalid state transition |
| `503 Service Unavailable` | Lỗi tạm thời | LLM unavailable |

---

## 8. Step-by-step Implementation Plan

### Phase 2A: Core Infrastructure (Foundation)

#### Step 1: Database Migration
- [ ] Thêm columns vào `story_versions` table
- [ ] Thêm enum values cho `job_stage`, `story_status`, `version_edit_type`
- [ ] Tạo indexes cần thiết
- [ ] Viết rollback script

#### Step 2: Entity Updates
- [ ] Cập nhật `StoryVersion` entity với new properties
- [ ] Thêm `OutlineGenerationStatus` enum
- [ ] Cập nhật `StoryStatus` enum
- [ ] Cập nhật `VersionEditType` enum

#### Step 3: DTOs Creation
- [ ] Tạo `GenerateOutlineRequestDto`
- [ ] Tạo `EditOutlineRequestDto`
- [ ] Tạo `RegenerateOutlineRequestDto`
- [ ] Tạo `ApproveOutlineRequestDto`
- [ ] Tạo `OutlineProgressDto`
- [ ] Tạo `OutlineVersionDto`

---

### Phase 2B: AI Module Integration

#### Step 4: Command & Contract
- [ ] Tạo `GenerateOutlineCommand` record
- [ ] Kiểm tra & update `GenerateOutlineResponse`
- [ ] Thêm validation cho command

#### Step 5: Prompt Template
- [ ] Kiểm tra prompt template hiện có cho outline
- [ ] Cập nhật template nếu cần (context-aware)
- [ ] Thêm version tracking

#### Step 6: Handler Enhancement
- [ ] Review `GenerateOutlineHandler` hiện có
- [ ] Thêm error handling & retry logic
- [ ] Thêm structured logging
- [ ] Thêm validation cho output

---

### Phase 2C: Core Service Layer

#### Step 7: Service Implementation
- [ ] Tạo `IOutlineService` interface
- [ ] Implement `OutlineService` với:
  - `GenerateOutlineAsync()`
  - `GetOutlineProgressAsync()`
  - `GetOutlineVersionsAsync()`
  - `EditOutlineAsync()`
  - `RegenerateOutlineAsync()`
  - `ApproveOutlineAsync()`
- [ ] Implement authorization checks
- [ ] Implement state validation
- [ ] Implement idempotency

#### Step 8: State Management
- [ ] Implement state transitions
- [ ] Implement concurrency control (ConcurrencyToken)
- [ ] Implement stale request recovery
- [ ] Implement retry with backoff

---

### Phase 2D: Controller & API

#### Step 9: Controller Implementation
- [ ] Tạo `OutlineController` hoặc extend `StoryController`
- [ ] Implement all endpoints (Step 7.1)
- [ ] Add authorization attributes
- [ ] Add request validation
- [ ] Add response mapping

#### Step 10: Error Handling
- [ ] Map exceptions to HTTP codes
- [ ] Add error DTOs
- [ ] Add logging
- [ ] Add telemetry

---

### Phase 2E: Testing & Integration

#### Step 11: Unit Tests
- [ ] Test `OutlineService` methods
- [ ] Test state transitions
- [ ] Test authorization
- [ ] Test validation

#### Step 12: Integration Tests
- [ ] Test full flow: Generate → Review → Approve
- [ ] Test edit flow
- [ ] Test regenerate flow
- [ ] Test error scenarios

#### Step 13: Error Scenario Tests
- [ ] Test LLM failure handling
- [ ] Test concurrent requests
- [ ] Test stale data scenarios
- [ ] Test authorization bypass attempts

---

## 9. Files cần tạo/sửa

### 9.1 Core (StoryPlatform.Application)

| File | Action | Mô tả |
|------|--------|--------|
| `Features/Outline/DTOs/OutlineDtos.cs` | **CREATE** | All request/response DTOs |
| `Features/Outline/Interfaces/IOutlineService.cs` | **CREATE** | Service interface |
| `Features/Outline/Services/OutlineService.cs` | **CREATE** | Main service implementation |
| `Features/Outline/Models/OutlineModels.cs` | **CREATE** | Internal models |
| `Enums/OutlineGenerationStatus.cs` | **CREATE** | Status enum |
| `DependencyInjection.cs` | **MODIFY** | Register OutlineService |

### 9.2 Core (StoryPlatform.Domain)

| File | Action | Mô tả |
|------|--------|--------|
| `Entities/StoryVersion.cs` | **MODIFY** | Add outline fields |
| `Enums/StoryStatus.cs` | **MODIFY** | Add new statuses |
| `Enums/VersionEditType.cs` | **MODIFY** | Add AI_REGENERATED |

### 9.3 Core (StoryPlatform.Api)

| File | Action | Mô tả |
|------|--------|--------|
| `Controllers/OutlineController.cs` | **CREATE** | API endpoints |
| `Extensions/ServiceExtensions.cs` | **MODIFY** | Add DI registration |

### 9.4 AI Module (StoryPlatform.AI)

| File | Action | Mô tả |
|------|--------|--------|
| `OutlineGeneration/GenerateOutlineCommand.cs` | **CREATE** | Command record |
| `OutlineGeneration/GenerateOutlineHandler.cs` | **MODIFY** | Add retry, validation |
| `PromptCatalog/...` | **MODIFY** | Update prompt template |

### 9.5 Infrastructure (StoryPlatform.Infrastructure)

| File | Action | Mô tả |
|------|--------|--------|
| `Persistence/Configurations/StoryVersionConfiguration.cs` | **MODIFY** | Add new columns mapping |
| `Persistence/ApplicationDbContext.cs` | **MODIFY** | Add enum converters |
| `Migrations/..._Phase2.cs` | **CREATE** | Database migration |

### 9.6 Shared Contracts

| File | Action | Mô tả |
|------|--------|--------|
| `AI/Requests/GenerationRequests.cs` | **MODIFY** | Add GenerateOutlineRequest |
| `AI/Responses/GenerationResponses.cs` | **MODIFY** | Update response |

---

## 10. Test Cases

### 10.1 Happy Path Tests

| ID | Test | Expected Result |
|----|------|-----------------|
| TP-01 | Generate outline thành công | StoryVersion created, status = OutlineReview |
| TP-02 | Get outline progress | Returns current outline & status |
| TP-03 | Get version history | Returns all versions |
| TP-04 | Edit outline | New version created, old preserved |
| TP-05 | Regenerate outline | New version replaces old |
| TP-06 | Approve outline | Status changes to Approved |
| TP-07 | Idempotent generate | Same result, no duplicate |

### 10.2 Authorization Tests

| ID | Test | Expected Result |
|----|------|-----------------|
| TA-01 | User without permission | 403 Forbidden |
| TA-02 | User with GenerateStory permission | Can generate & edit |
| TA-03 | User with Auto-approve policy | Can approve without permission |
| TA-04 | Story belongs to different user | 404 Not Found |

### 10.3 Validation Tests

| ID | Test | Expected Result |
|----|------|-----------------|
| TV-01 | Empty title | 400 Bad Request |
| TV-02 | Title too long (>200 chars) | 400 Bad Request |
| TV-03 | Edit non-latest version | 422 Unprocessable Entity |
| TV-04 | Approve already approved | 409 Conflict |
| TV-05 | Generate when already generating | 409 Conflict |

### 10.4 Error Handling Tests

| ID | Test | Expected Result |
|----|------|-----------------|
| TE-01 | LLM timeout | Retry, then fail with reason |
| TE-02 | LLM returns invalid JSON | Retry, then fail |
| TE-03 | Story not in Draft status | 400 Bad Request |
| TE-04 | Request status != InputAccepted | 400 Bad Request |
| TE-05 | Concurrent regeneration | Only one proceeds |

### 10.5 Concurrency Tests

| ID | Test | Expected Result |
|----|------|-----------------|
| TC-01 | Two simultaneous generates | Only one succeeds |
| TC-02 | Generate while editing | Both succeed (separate versions) |
| TC-03 | Approve during generation | 409 Conflict |

---

## 11. Phụ thuộc & Prerequisites

### 11.1 Từ Phase 1 (đã có)

| Component | Status | Ghi chú |
|-----------|--------|---------|
| `AIStoryInputService` | ✅ Done | Cung cấp handoff data |
| `StoryGenerationRequest` | ✅ Done | Lưu accepted input |
| `StoryGenerationJob` | ✅ Done | Job tracking |
| `RuleBasedInputGuardrail` | ✅ Done | Input validation |
| `Story` entity | ✅ Done | Base entity |
| Authorization flow | ✅ Done | Permission checking |

### 11.2 Từ AI Module (đã có)

| Component | Status | Ghi chú |
|-----------|--------|---------|
| `GenerateOutlineHandler` | ✅ Exists | Cần review & enhance |
| `ILlmClient` | ✅ Exists | LLM abstraction |
| `IPromptTemplateProvider` | ✅ Exists | Prompt management |
| `GenerationSchemas` | ✅ Exists | Schema definitions |

### 11.3 External Dependencies

| Dependency | Required | Notes |
|------------|----------|-------|
| LLM Provider (OpenAI/Azure) | ✅ | Configured in AI Module |
| Database (PostgreSQL) | ✅ | Current DB |
| Message Queue | ❌ | Không cần (sync flow) |

### 11.4 Phase 3 Dependencies

Phase 2 tạo tiền đề cho Phase 3:

| What Phase 3 needs | Phase 2 provides |
|--------------------|------------------|
| StoryVersion với outline | ✅ Will be created |
| Approved outline | ✅ Will be marked |
| Prompt version for content | ✅ Will be stored |
| Context snapshot | ✅ From Phase 1 |

---

## 12. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| LLM quality issues | Medium | High | Retry + fallback message |
| Concurrent generation race | Low | Medium | ConcurrencyToken |
| Data inconsistency | Low | High | Transaction + validation |
| Prompt injection | Low | Critical | Input sanitization |
| Version explosion | Low | Medium | Limit max versions |

---

## 13. Success Criteria

| Criteria | Definition |
|----------|------------|
| Functional | User có thể generate, review, edit, regenerate, approve outline |
| Reliability | Retry mechanism hoạt động đúng |
| Security | Authorization được enforce đúng |
| Performance | Outline generation < 30s (typical) |
| Traceability | Mọi thay đổi có audit trail |
| Idempotency | Duplicate requests không tạo duplicate data |

---

## 14. Open Issues (cần theo dõi)

- [ ] **Q-P2-01**: LLM model selection - cần input từ team
- [ ] **Q-P2-02**: Auto-approve threshold - cần input từ product
- [ ] **Q-P2-03**: Max versions limit - cần input từ product
- [ ] **Q-P2-04**: Prompt template vị trí - dev hay config?

---

## Appendix A: Entity Relationship

```
┌─────────────────┐       ┌─────────────────────────┐       ┌──────────────────┐
│   Story         │       │  StoryGenerationRequest │       │ StoryGenerationJob│
├─────────────────┤       ├─────────────────────────┤       ├──────────────────┤
│ Id (PK)         │◀──────│ StoryId (FK)            │       │ Id (PK)           │
│ Status          │       │ Id (PK)                 │       │ StoryId (FK)      │
│ Source          │       │ AcceptedInputJson      │◀──────│ RequestId (FK)     │
│ AuthorUserId    │       │ Status                  │       │ Stage             │
│ ChildProfileId  │       │ InputFingerprint        │       │ CreatedAt         │
└─────────────────┘       └─────────────────────────┘       └──────────────────┘
         │
         │ 1:N
         ▼
┌─────────────────┐       ┌─────────────────────────┐
│ StoryVersion    │       │ StoryVersionMetadata    │
├─────────────────┤       ├─────────────────────────┤
│ Id (PK)         │       │ StoryVersionId (FK)    │
│ StoryId (FK)    │       │ PromptVersion          │
│ VersionNumber   │       │ LlmModel               │
│ Title           │       │ GenerationDuration     │
│ OutlineOpening  │       │ TokenCount             │
│ OutlineDevelopment│     │ RawResponse (nullable)  │
│ OutlineEnding   │       └─────────────────────────┘
│ Content (null)  │
│ EditType        │
│ CreatedAt       │
└─────────────────┘
```

---

## Appendix B: Configuration

```json
{
  "Phase2": {
    "OutlineGeneration": {
      "MaxRetries": 3,
      "RetryDelayMs": [2000, 4000, 8000],
      "TimeoutSeconds": 30,
      "MaxVersionsPerStory": 10
    },
    "Approval": {
      "AutoApproveOnThreshold": true,
      "RequireApprovalForChildrenUnderAge": 7
    }
  }
}
```

---

**Document Status**: Draft - Pending Clarification  
**Last Updated**: 2026-09-12  
**Next Steps**: 
1. Get answers to Section 2 questions
2. Review Phase 1 implementation
3. Start with Phase 2A (Database & Entities)
