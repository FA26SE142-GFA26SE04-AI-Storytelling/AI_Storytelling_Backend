# Công bố kết quả sự kiện, khiếu nại sau công bố và pop-up kết quả — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admin duyệt = công bố kết quả; 48 giờ khiếu nại sau công bố (giải bị giữ, có thể thu hồi); thông báo + pop-up kết thúc / kết quả / thu hồi / khiếu nại được chấp nhận; trang kết quả cho phụ huynh và nhà sáng tạo.

**Architecture:** Mở rộng mô hình sự kiện theo hoạt động: trạng thái suất `AppealHold`/`Revoked`, `Event.ResultsPublishedAt`, khiếu nại có loại. Worker `EventResultSweepService` gửi thông báo kết thúc và mở nhận thưởng khi hết hạn khiếu nại. Frontend có host pop-up trong `ParentShell` và `StaffApp`, trang kết quả trong `EventsWorkspace`.

**Tech Stack:** .NET 10 Clean Architecture, EF Core PostgreSQL, xUnit + Moq (`InMemoryUnitOfWork`); Next.js + React, Vitest.

**Spec:** `docs/superpowers/specs/2026-12-02-event-results-publication-design.md` (đọc kèm `2026-12-02-event-auto-award-list-design.md`).

## Global Constraints

- Chỉ áp dụng `ConfigVersion = ActivityBased`; `LegacyRoleBased` giữ nguyên hành vi.
- Hạn khiếu nại = `ResultsPublishedAt + EventAppealWindow.Hours` (48). Hạn nhận = lúc mở nhận + `EventActivity.ClaimWindowHours` (mặc định 72).
- Không cho nhận thưởng khi suất ở `AppealHold`.
- Tên bé chỉ công khai khi `ShowChildNames = true`; chỉ nickname, không ảnh/tuổi.
- Thông tin nhận quà không đưa vào thông báo, pop-up hay API công khai.
- Endpoint mới dùng `[HasPermission]`; cập nhật `tests/StoryPlatform.UnitTests/Authorization/endpoint-authorization.snapshot.json` (chạy test với `UPDATE_AUTHZ_SNAPSHOT=1`) và `AI_Storytelling_Frontend/src/docs/core-api-coverage.json` (`node scripts/audit-core-api.mjs --write`).
- Chữ hiển thị tiếng Việt; frontend dùng `Dialog`, token màu, thang `--fs-*`, `@/app/...` import (theo `src/AGENTS.md`).
- Không commit (người dùng chưa yêu cầu). Chạy test backend với `--artifacts-path .tmp/verify-artifacts` (API đang chạy khoá DLL) và xoá thư mục đó khi xong.
- Không chạy API mới lên DB của người dùng (auto-migrate). Migration chỉ tạo file.

## Review Focus

1. Thu hồi đúng lúc hết hạn khiếu nại / worker mở nhận cùng lúc → không được để suất vừa `Revoked` vừa có suất thay thế `AwaitingClaim` trùng người. (Test ở Task 4: `Revoke_AfterWindowClosed_IsRejected`.)
2. Người thay thế đang giữ suất khác cùng nguồn → phải từ chối như chỉnh tay. (Task 4: `Revoke_ReplacementHoldingSameSource_IsRejected`.)
3. Worker chạy lặp → thông báo kết thúc không gửi lại lần hai. (Task 3: `Sweep_NotifiesEndedOnlyOnce`.)
4. Người giữ giải tự khiếu nại, hoặc khiếu nại trước khi công bố → 409. (Task 4: `CreateAppeal_ByWinner_IsRejected`, `CreateAppeal_BeforePublication_IsRejected`.)
5. Pop-up giải chưa nhận không hiện lặp trong cùng phiên nhưng hiện lại sau đăng xuất/đăng nhập. (Task 7: `pickEventPopups` tests.)

---

### Task 1: Dữ liệu — trạng thái, cột mới, migration

**Việc này làm gì (nói dễ hiểu):**
Thêm các "ô" dữ liệu cần cho luồng mới: suất giải có thể ở trạng thái "chờ hết hạn khiếu nại" hoặc "đã thu hồi" kèm lý do; sự kiện nhớ lúc công bố và lúc đã báo kết thúc; đơn khiếu nại biết mình thuộc loại nào và khiếu nại suất nào; phụ huynh có thể đồng ý hiện tên bé.

**Cần thêm / còn thiếu:**
- Đã có: `EventAwardStatus` (1–9), `NotificationType` (đến 41), bảng `event_awards`, `events`, `event_appeals`, migration `AddEventAwardManualAssignment`.
- Cần thêm: 2 trạng thái suất, 4 loại thông báo, enum loại khiếu nại, 8 cột mới, 1 migration.
- Còn thiếu / ngoài phạm vi: không đổi số enum cũ; không áp migration vào DB.

**Files:**
- Modify: `src/Core/StoryPlatform.Domain/Enums/EventEnums.cs`, `src/Core/StoryPlatform.Domain/Enums/NotificationType.cs`
- Modify: `src/Core/StoryPlatform.Domain/Entities/Events/EventEntities.cs` (`Event`, `EventAward`, `EventAppeal`)
- Modify: `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/Events/EventConfigurations.cs`
- Create: migration `AddEventResultsPublication` (lệnh `dotnet ef migrations add`) + cập nhật `ApplicationDbContextModelSnapshot.cs`

**Interfaces:**
- Produces:
  - `EventAwardStatus.AppealHold = 10`, `EventAwardStatus.Revoked = 11`
  - `NotificationType.EventEnded = 42`, `EventResultPublished = 43`, `EventAwardRevoked = 44`, `EventAppealAccepted = 45`
  - `enum EventAppealKind { Verification = 1, Ranking = 2 }`
  - `Event.ResultsPublishedAt : DateTime?`, `Event.EndedNotifiedAt : DateTime?`
  - `EventAward.RevokedReason : string?` (max 500), `RevokedByUserId : int?`, `RevokedAt : DateTime?`, `ShowChildNames : bool` (default false)
  - `EventAppeal.Kind : EventAppealKind` (default `Verification`, cột not null default 1), `EventAppeal.TargetAwardId : int?`

- [ ] **Step 1:** Thêm enum/cột như Interfaces; cấu hình `RevokedReason` `HasMaxLength(500)`, `Kind` `HasDefaultValue(EventAppealKind.Verification)` + conversion như enum khác trong file.
- [ ] **Step 2:** Tạo migration: `dotnet ef migrations add AddEventResultsPublication --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Api` (nếu lệnh cần DB, viết tay migration + snapshot theo mẫu `20261202063711_AddEventAwardManualAssignment`). Kiểm tra `Up` chỉ `AddColumn`.
- [ ] **Step 3:** Build: `dotnet build src/Core/StoryPlatform.Api --artifacts-path .tmp/verify-artifacts` → 0 error.
- [ ] **Step 4:** Chạy `dotnet test tests/StoryPlatform.UnitTests --artifacts-path .tmp/verify-artifacts` → các test hiện có vẫn PASS (bao gồm test kiểm migration/snapshot nếu có).

---

### Task 2: Công bố khi Admin duyệt, giữ giải trong hạn khiếu nại

**Việc này làm gì (nói dễ hiểu):**
Moderator xác nhận danh sách mà không phải chờ 48 giờ. Khi Admin duyệt, kết quả được công bố: người thắng biết mình có giải nhưng chưa nhận được ngay, mọi người tham gia đều nhận thông báo kết quả.

**Cần thêm / còn thiếu:**
- Đã có: `ConfirmAwardsAsync`, `ApproveAwardsLockedAsync`, `NotifyAwardWinnersAsync`, `ClaimRewardLockedAsync`.
- Cần thêm: bỏ chặn hạn khiếu nại ở xác nhận; duyệt → `AppealHold` + `ResultsPublishedAt`; thông báo `EventResultPublished` cho mọi người tham gia; nhận thưởng bị từ chối khi `AppealHold`.
- Còn thiếu / ngoài phạm vi: mở nhận thưởng (Task 3); khiếu nại & thu hồi (Task 4).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Events/Services/EventRewardService.cs` (approve, claim, notify)
- Modify: `src/Core/StoryPlatform.Application/Features/Events/Services/EventRewardService.Activities.cs` (`ConfirmAwardsAsync`)
- Modify: `src/Core/StoryPlatform.Application/Features/Events/Services/EventManagementService.cs` (`CloseEventAsync`: coi `Revoked` như suất kết thúc; `AppealHold` như suất chưa xong)
- Test: `tests/StoryPlatform.UnitTests/Features/Events/ActivityBasedEventTests.cs`
- Test: `tests/StoryPlatform.IntegrationTests/Api/ActivityBasedEventApiTests.cs` (Archetype2/3: sau approve, đặt `ResultsPublishedAt` lùi 49 giờ qua `factory.WithDbAsync` rồi gọi `IEventResultSweepService.SweepEndedEventsAsync` trong scope của factory, sau đó claim)

**Interfaces:**
- Consumes: Task 1 enums/cột.
- Produces: sau approve (activity-based) suất có người ở `AppealHold`, `ClaimDeadlineAt == null`, `Event.ResultsPublishedAt == now`. Payload thông báo `EventResultPublished`: `{ eventId, eventTitle }` (JSON camelCase).
  `EventDetailDto.ResultsPublishedAt : DateTime?` (mọi người xem); với activity-based đã công bố, `EventDetailDto.AppealWindowEndsAt = ResultsPublishedAt + 48h` (thay cách tính theo xác minh) — sửa trong `EventManagementService.cs`. Test thêm: `EventDetail_AfterPublication_ExposesPublishedAtAndAppealDeadline`.

- [ ] **Step 1: Viết test thất bại**
  - Sửa `ConfirmAwards_BlocksWhileAppealWindowOpen_...` thành `ConfirmAwards_DoesNotWaitForAppealWindow`: hồ sơ `VerificationFailed` 1 giờ trước, `ConfirmAwardsAsync` thành công.
  - `Approve_PublishesResults_HoldsAwards_AndNotifiesAllParticipants`: 3 hồ sơ (2 có giải, 1 không) → `aw.Status == AppealHold`, `aw.ClaimDeadlineAt == null`, `ev.ResultsPublishedAt != null`, mock `INotificationService.CreateAsync(userId, EventResultPublished, ...)` được gọi đúng 1 lần cho **mỗi** user tham gia (3 lần).
  - `Claim_DuringAppealHold_IsRejected`: `ClaimRewardAsync` → `ConflictException`, message chứa "sau khi hết hạn khiếu nại".
- [ ] **Step 2:** `dotnet test ... --filter "FullyQualifiedName~ActivityBasedEventTests"` → 3 test trên FAIL.
- [ ] **Step 3:** Cài đặt. Thông báo gửi sau commit transaction như `NotifyAwardWinnersAsync` (lỗi gửi chỉ log + audit `EventAwardNotificationFailed`, không làm hỏng duyệt). Người tham gia = `EventParticipation` của sự kiện có `EventActivityId`, không `IsDeleted`, distinct `UserId`. Message từ chối nhận: `"Giải sẽ mở nhận từ {deadline giờ VN:HH:mm dd/MM/yyyy} sau khi hết hạn khiếu nại."`.
- [ ] **Step 4:** Chạy lại → PASS; chạy toàn bộ `Features.Events` + integration `ActivityBasedEventApiTests` → PASS.

---

### Task 3: Worker — thông báo kết thúc và mở nhận thưởng

**Việc này làm gì (nói dễ hiểu):**
Ngay khi sự kiện hết giờ, mọi người tham gia nhận lời cảm ơn kèm tóm tắt việc họ đã làm để giữ hứng thú. Khi hết 48 giờ khiếu nại mà không còn đơn đang xử lý, hệ thống tự mở cho người thắng nhận thưởng trong 72 giờ.

**Cần thêm / còn thiếu:**
- Đã có: `EventResultSweepService.SweepEndedEventsAsync`, `EventResultSweepWorker` (5 phút).
- Cần thêm: gửi `EventEnded` một lần (theo `EndedNotifiedAt`); chuyển `AppealHold` → `AwaitingClaim` + hạn nhận + thông báo `EventAwardApproved`.
- Còn thiếu / ngoài phạm vi: không hé lộ danh sách tạm trong thông báo kết thúc.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Events/Services/EventResultSweepService.cs`
- Test: create `tests/StoryPlatform.UnitTests/Features/Events/EventResultSweepServiceTests.cs`

**Interfaces:**
- Consumes: Task 1, Task 2.
- Produces: constructor `EventResultSweepService(IUnitOfWork uow, IAuditLogWriter audit, INotificationService? notifications = null, ILogger<EventResultSweepService>? logger = null)`.
  `EventRecapDto { int StoriesRead; int Submissions; int Votes; int TasksCompleted; int TasksTotal }` (trong `EventDtos.cs`) và
  `internal static Task<EventRecapDto> EventRecap.BuildAsync(IUnitOfWork uow, Event ev, int userId, CancellationToken ct)`
  (create `src/Core/StoryPlatform.Application/Features/Events/Services/EventRecap.cs`; Task 5 dùng lại).
  Payload `EventEnded`: `{ eventId, eventTitle, storiesRead, submissions, votes, tasksCompleted, tasksTotal }` (số nguyên, từ `EventRecap.BuildAsync`).
  - `storiesRead`: số `ReadingSession` `Completed` của các bé thuộc user, `StoryId` thuộc `EventStory` của sự kiện, `CompletedAt` trong khoảng `OpensAt..ClosesAt`.
  - `submissions`: số `EventSubmission` của user trong sự kiện; `votes`: số `EventVote` không xoá của user; `tasksCompleted/tasksTotal`: từ `EventTaskProgress` của hồ sơ ReadingMissions (0 nếu không có).

- [ ] **Step 1: Viết test thất bại**
  - `Sweep_NotifiesEndedOnce_WithPersonalRecap`: sự kiện vừa kết thúc, 1 phụ huynh có bé đọc xong 2 truyện sự kiện, 1 phiếu → `CreateAsync(parentId, EventEnded, payload)` với `storiesRead == 2`, `votes == 1`; `ev.EndedNotifiedAt != null`.
  - `Sweep_NotifiesEndedOnlyOnce`: gọi 2 lần → `CreateAsync(..., EventEnded, ...)` đúng 1 lần mỗi user.
  - `Sweep_ReleasesHold_AfterWindow_WhenNoOpenAppeals`: `ResultsPublishedAt = now-49h`, suất `AppealHold` → `AwaitingClaim`, `ClaimDeadlineAt ≈ now + ClaimWindowHours`, `EventAwardApproved` gửi cho người thắng.
  - `Sweep_KeepsHold_WhileAppealOpen`: có `EventAppeal` `Open` → suất vẫn `AppealHold`.
  - `Sweep_KeepsHold_BeforeWindowEnds`: `ResultsPublishedAt = now-1h` → vẫn `AppealHold`.
- [ ] **Step 2:** Chạy `--filter "FullyQualifiedName~EventResultSweepServiceTests"` → FAIL.
- [ ] **Step 3:** Cài đặt. Bước thông báo kết thúc chạy cho sự kiện đã hết giờ có `EndedNotifiedAt == null` (kể cả khi không còn hồ sơ chờ xác minh); đặt `EndedNotifiedAt` rồi commit trước khi gửi. Bước mở nhận chạy cho sự kiện `ResultsPublishedAt != null`, quá hạn, không có appeal `Open/UnderReview/Escalated`. Mỗi bước trong transaction + `AcquireEventLockAsync`, lỗi một sự kiện không chặn sự kiện khác (giữ mẫu hiện có).
- [ ] **Step 4:** Chạy lại → PASS; `Features.Events` → PASS.

---

### Task 4: Khiếu nại sau công bố và thu hồi giải

**Việc này làm gì (nói dễ hiểu):**
Sau khi công bố, ai tham gia mà không có giải được khiếu nại trong 48 giờ (về kết quả xác minh hoặc về thứ hạng). Moderator chấp nhận thì người khiếu nại nhận thông báo; nếu cần, Moderator thu hồi suất của người đang giữ (ghi lý do) và trao cho người khác.

**Cần thêm / còn thiếu:**
- Đã có: `CreateAppealAsync` (chỉ `VerificationFailed`, khung 48h tính từ xác minh), `DecideAppealAsync`, API ứng viên, kiểm tra điều kiện chỉnh tay trong `ManualAssignAwardAsync`.
- Cần thêm: khung khiếu nại sau công bố + loại đơn; thông báo `EventAppealAccepted`; API thu hồi `POST /api/v1/events/awards/{awardId}/revoke`.
- Còn thiếu / ngoài phạm vi: sự kiện kiểu cũ giữ luồng khiếu nại cũ.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Events/DTOs/EventDtos.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Events/Services/EventParticipationService.cs` (`CreateAppealAsync`, `DecideAppealLockedAsync`, `MapAppeal`)
- Modify: `src/Core/StoryPlatform.Application/Features/Events/Services/EventRewardService.Activities.cs` (thêm `RevokeAwardAsync`; tách kiểm tra điều kiện người nhận của `ManualAssignAwardAsync` thành `private Task EnsureEligibleRecipientAsync(Event ev, EventAward award, IReadOnlyList<EventActivity> activities, int participationId, CancellationToken ct)` dùng chung)
- Modify: `src/Core/StoryPlatform.Application/Features/Events/Interfaces/IEventServices.cs`, `src/Core/StoryPlatform.Api/Controllers/EventsController.cs`
- Test: `ActivityBasedEventTests.cs`, authz snapshot

**Interfaces:**
- Consumes: Task 1–2.
- Produces:
  - `CreateAppealRequestDto.Kind : EventAppealKind` (default `Verification`), `TargetAwardId : int?`
  - `EventAppealDto.Kind`, `EventAppealDto.TargetAwardId`, `EventAppealDto.TargetAwardLabel : string?` ("<RankOrTier> · Suất #<SlotIndex>")
  - `RevokeAwardRequestDto { string Reason; int? ReplacementParticipationId }`
  - `Task<EventAwardSlotDto> IEventRewardService.RevokeAwardAsync(int moderatorUserId, int awardId, RevokeAwardRequestDto request, CancellationToken ct = default)` → trả suất mới (hoặc suất cũ đã thu hồi nếu không có người thay), qua `EventAwardEnricher.MapForOperatorsAsync`.
  - Controller: `[HttpPost("awards/{awardId:int}/revoke")] [HasPermission(Permissions.Moderation.EventManage)]`, message "Đã thu hồi suất giải."
  - Payload `EventAwardRevoked`: `{ eventId, eventTitle, rankOrTier, rewardName, reason }`; `EventAppealAccepted`: `{ eventId, eventTitle, appealId, resolutionNote }`.

- [ ] **Step 1: Viết test thất bại**
  - `CreateAppeal_BeforePublication_IsRejected` (409), `CreateAppeal_AfterWindow_IsRejected` (409, `ResultsPublishedAt = now-49h`), `CreateAppeal_ByWinner_IsRejected` (409, user giữ suất `AppealHold`).
  - `CreateAppeal_RankingByVerifiedNonWinner_Succeeds`: `Kind = Ranking`, `TargetAwardId` = suất của người khác → đơn `Open`, DTO trả `Kind` và `TargetAwardLabel`.
  - `CreateAppeal_VerificationKind_RequiresFailedParticipation` (409 nếu hồ sơ `Verified`).
  - `DecideAppeal_Accepted_NotifiesAppellant`: `CreateAsync(appellantId, EventAppealAccepted, ...)` gọi 1 lần.
  - `Revoke_WithReplacement_CreatesHeldAwardAndNotifiesBoth`: suất cũ `Revoked` + `RevokedReason`/`RevokedByUserId`/`RevokedAt`; suất mới cùng `RankOrTier`/`SlotIndex`/phần thưởng, `ReallocatedFromAwardId` = suất cũ, `AppealHold`, người thay thế; `EventAwardRevoked` gửi người cũ, `EventResultPublished` gửi người mới.
  - `Revoke_WithoutReplacement_LeavesNoNewAward`.
  - `Revoke_AfterWindowClosed_IsRejected` (409 khi quá hạn và không còn đơn mở, hoặc suất không ở `AppealHold`).
  - `Revoke_ReplacementHoldingSameSource_IsRejected` (409).
  - `Revoke_ByAssistantModerator_IsForbidden` (chỉ Moderator phụ trách chính).
  - `Revoke_RequiresReason` (400 khi rỗng, > 500 ký tự).
- [ ] **Step 2:** Chạy filter các test trên → FAIL.
- [ ] **Step 3:** Cài đặt. Điều kiện khiếu nại (activity-based): đã công bố, chưa quá 48h; user có hồ sơ ở hoạt động là nguồn của ít nhất một suất; user không giữ suất `AppealHold/AwaitingClaim/Claimed/FulfillmentProcessing/Fulfilled` nào của sự kiện; giữ BR-AP07 và quy tắc một đơn mở/hồ sơ. Thu hồi được khi suất `AppealHold` và (chưa quá hạn **hoặc** còn đơn mở). Thông báo gửi sau commit.
- [ ] **Step 4:** Chạy `Features.Events` → PASS. Chạy `UPDATE_AUTHZ_SNAPSHOT=1 dotnet test ... --filter "FullyQualifiedName~EndpointAuthorizationMatrixTests"`, rồi chạy lại không có biến → PASS; kiểm diff snapshot chỉ thêm route revoke (và route của Task 5 nếu làm cùng).

---

### Task 5: API kết quả cho người tham gia và đồng ý hiện tên bé

**Việc này làm gì (nói dễ hiểu):**
Cho phụ huynh/nhà sáng tạo lấy "kết quả của tôi" ở mọi sự kiện đã kết thúc (dùng cho pop-up và trang kết quả), và danh sách giải công bố của một sự kiện theo đúng quy tắc hiển thị tên. Khi nhận giải hoàn thành nhiệm vụ, phụ huynh chọn có cho hiện tên bé hay không.

**Cần thêm / còn thiếu:**
- Đã có: `GetMyRewardsAsync`, `ClaimRewardLockedAsync`, `VerificationNotesJson`, `EventAwardEnricher`.
- Cần thêm: `GET /api/v1/events/my-results`, `GET /api/v1/events/{eventId}/results`, `ClaimRewardRequestDto.ShowChildNames`.
- Còn thiếu / ngoài phạm vi: không trả thông tin nhận quà.

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/Events/Services/EventResultsQueryService.cs` (+ interface trong `IEventServices.cs`, đăng ký DI trong `Application/DependencyInjection.cs`)
- Modify: `EventDtos.cs`, `EventRewardService.cs` (claim lưu `ShowChildNames` khi `ResultSource == MissionCompletion` và user role `User`), `EventsController.cs`
- Test: create `tests/StoryPlatform.UnitTests/Features/Events/EventResultsQueryServiceTests.cs`; authz snapshot

**Interfaces:**
- Consumes: Task 1–4.
- Produces:
  - `Task<IReadOnlyList<MyEventResultDto>> IEventResultsQueryService.GetMyResultsAsync(int userId, CancellationToken ct = default)` — `[HttpGet("my-results")] [HasPermission(Permissions.Shared.EventParticipate)]`
  - `Task<PublicEventResultsDto> IEventResultsQueryService.GetPublicResultsAsync(int userId, int eventId, CancellationToken ct = default)` — `[HttpGet("{eventId:int}/results")] [HasPermission(Permissions.Shared.EventParticipate)]`; 409 "Kết quả sự kiện chưa được công bố." khi `ResultsPublishedAt == null`.
  - `MyEventResultDto { int EventId; string EventTitle; DateTime? EndedAt; DateTime? ResultsPublishedAt; DateTime? AppealDeadlineAt; bool CanAppeal; EventRecapDto Recap; List<MyActivityResultDto> Activities; List<MyAwardDto> Awards }`
  - `Recap` dùng `EventRecap.BuildAsync` của Task 3.
  - `MyActivityResultDto { EventActivityType ActivityType; EventParticipationStatus Status; string? Reason }` (Reason: câu ngắn từ `VerificationNotesJson`, ví dụ "Chưa xong nhiệm vụ: T02")
  - `MyAwardDto { int AwardId; string RankOrTier; int SlotIndex; string RewardName; EventRewardType RewardType; int CreditGrantAmount; int SubmitCreditGrantAmount; long UnitValueVnd; EventAwardStatus Status; DateTime? ClaimOpensAt; DateTime? ClaimDeadlineAt; string? RevokedReason; bool AsksChildNameConsent }`
  - `PublicEventResultsDto { int EventId; string EventTitle; DateTime ResultsPublishedAt; int ParticipantCount; int StoryCount; List<PublicAwardDto> Awards }`
  - `PublicAwardDto { string RankOrTier; int SlotIndex; string RewardName; EventAwardResultSource? ResultSource; string? StoryTitle; string? WinnerName; List<string> ChildNicknames; int ChildCount }`
  - `ClaimRewardRequestDto.ShowChildNames : bool?`

- [ ] **Step 1: Viết test thất bại**
  - `MyResults_ListsWinnerHeldAward_WithClaimOpensAt`: `ClaimOpensAt == ResultsPublishedAt + 48h`, `CanAppeal == false` cho người thắng.
  - `MyResults_NonWinner_HasReasonAndCanAppeal`.
  - `MyResults_IncludesRevokedAwardWithReason`.
  - `MyResults_BeforePublication_HasRecapButNoAwards`.
  - `PublicResults_StoryAward_ShowsStoryTitleAndAuthorName`.
  - `PublicResults_MissionAward_HidesChildNamesWithoutConsent` (`ChildNicknames` rỗng, `ChildCount == 2`) và `..._ShowsChildNicknamesWithConsent`.
  - `PublicResults_ExcludesRevokedAwards`, `PublicResults_BeforePublication_Is409`.
  - `Claim_MissionAward_StoresShowChildNames` (trong `ActivityBasedEventTests`).
- [ ] **Step 2:** Chạy filter → FAIL.
- [ ] **Step 3:** Cài đặt. "Bé tham gia" của một phụ huynh = bé có `ReadingSession` `Completed` với truyện sự kiện trong thời gian sự kiện. `WinnerName` = `EventAwardEnricher.FormatDisplayName`. Chỉ trả sự kiện `ActivityBased` mà user có hồ sơ có `EventActivityId`, và sự kiện đã hết thời gian.
- [ ] **Step 4:** Chạy lại → PASS; cập nhật authz snapshot như Task 4; `Features.Events` → PASS.

---

### Task 6: Frontend — kiểu dữ liệu, service, chữ thông báo

**Việc này làm gì (nói dễ hiểu):**
Dạy frontend hiểu dữ liệu mới (trạng thái giải, kết quả của tôi, danh sách công bố, thu hồi) và hiện đúng câu cho 4 loại thông báo mới trong chuông thông báo.

**Cần thêm / còn thiếu:**
- Đã có: `app/types/event.ts`, `app/services/eventService.ts`, `app/lib/notifications.ts` (`notificationKey`, `detailedText`).
- Cần thêm: kiểu khớp DTO Task 4–5; `myResults()`, `publicResults(eventId)`, `revokeAward(awardId, body)`, `createAppeal` thêm `kind/targetAwardId`, `claimReward` thêm `showChildNames`; nhãn `AppealHold` "Chờ hết hạn khiếu nại", `Revoked` "Đã thu hồi"; chữ cho `event_ended`, `event_result_published`, `event_award_revoked`, `event_appeal_accepted`.
- Còn thiếu / ngoài phạm vi: giao diện (Task 7–9).

**Files:**
- Modify: `src/app/types/event.ts`, `src/app/services/eventService.ts`, `src/app/lib/notifications.ts`
- Modify: `src/docs/core-api-coverage.json` (chạy `node scripts/audit-core-api.mjs --write` sau Task 7–9 khi các API đã được giao diện gọi)
- Test: `src/tests/unit/events/eventsUi.test.tsx` hoặc test notifications hiện có (`grep -rl notificationText src/tests`)

**Interfaces:**
- Produces (TS, camelCase của DTO backend): `MyEventResultDto`, `EventRecapDto`, `MyActivityResultDto`, `MyAwardDto`, `PublicEventResultsDto`, `PublicAwardDto`, `EventAppealKind = "Verification" | "Ranking"`; `eventService.myResults(signal?)`, `eventService.publicResults(eventId, signal?)`, `eventService.revokeAward(awardId, { reason, replacementParticipationId })`.

- [ ] **Step 1:** Test: `notificationText` cho `EventAwardRevoked` với payload `{ eventTitle: "Test 2", reason: "Sai điểm" }` chứa "Test 2" và "Sai điểm"; `EventEnded` chứa tên sự kiện; `AWARD_STATUS_LABEL.AppealHold === "Chờ hết hạn khiếu nại"`.
- [ ] **Step 2:** `npx vitest run <file>` (trong `src`) → FAIL.
- [ ] **Step 3:** Cài đặt.
- [ ] **Step 4:** `npx vitest run <file>` + `npx tsc --noEmit` → PASS.

---

### Task 7: Frontend — pop-up sự kiện trong khung ứng dụng

**Việc này làm gì (nói dễ hiểu):**
Khi phụ huynh/nhà sáng tạo vào nền tảng (vừa đăng nhập hoặc mở lại khi còn giữ đăng nhập), hiện pop-up: sự kiện vừa kết thúc, kết quả và giải chưa nhận (kèm khối Lưu ý), giải bị thu hồi, khiếu nại được chấp nhận. Pop-up giải chưa nhận nhắc lại mỗi lần vào cho đến khi nhận.

**Cần thêm / còn thiếu:**
- Đã có: `ParentShell` (phụ huynh), `StaffApp` (nhà sáng tạo), thông báo + API đánh dấu đã đọc, `Dialog`, `Frog`, `lib/motion`.
- Cần thêm: hàm thuần chọn pop-up, hook tải dữ liệu, component pop-up, gắn vào hai khung (chỉ role `User` và `ContentCreator`).
- Còn thiếu / ngoài phạm vi: trang kết quả (Task 8).

**Files:**
- Create: `src/app/features/events/eventPopups.ts` (hàm thuần, không JSX)
- Create: `src/app/features/events/useEventResultPopups.ts`
- Create: `src/app/features/events/EventResultPopup.tsx`
- Modify: `src/app/components/shell/ParentShell.tsx`, `src/app/features/staff/StaffApp.tsx`, `src/app/context/AuthContext*` (xoá khoá `sessionStorage` khi đăng xuất)
- Create: `src/app/styles/features/events/event-popup.css` (import theo quy ước styles)
- Test: create `src/tests/unit/events/eventPopups.test.ts`, thêm render test vào `eventsUi.test.tsx`

**Interfaces:**
- Consumes: Task 6.
- Produces:
  - `type EventPopup = { kind: "revoked" | "appealAccepted" | "award" | "noAward" | "ended"; eventId: number; notificationId?: number; result?: MyEventResultDto; award?: MyAwardDto; payload?: Record<string, unknown> }`
  - `pickEventPopups(results: MyEventResultDto[], unread: AppNotification[], shownThisSession: boolean): EventPopup[]` — thứ tự: revoked/appealAccepted (từ thông báo chưa đọc) → award (suất `AppealHold`/`AwaitingClaim`, **luôn** đưa vào khi `shownThisSession == false`, bỏ khi `true`) và noAward (chỉ từ thông báo `EventResultPublished` chưa đọc) → ended (từ `EventEnded` chưa đọc).
  - `const EVENT_POPUP_SESSION_KEY = "taletale-event-popups-shown"`.
  - Đích "Xem chi tiết": phụ huynh `/app/events?eventId=<id>#my-result`, nhà sáng tạo `/staff/events?eventId=<id>#my-result`.

- [ ] **Step 1: Viết test thất bại** (`eventPopups.test.ts`)
  - suất `AppealHold` + `shownThisSession=false` → có `award`; `shownThisSession=true` → không có `award`.
  - suất `Claimed` / `Expired` / `Revoked` → không có `award`.
  - thông báo `EventAwardRevoked` chưa đọc → `revoked` đứng đầu danh sách.
  - thông báo `EventEnded` đã đọc → không có `ended`.
  - Render: pop-up `award` với `AppealHold` chứa "Lưu ý", "Giải mở nhận từ", "có thể thay đổi"; với `AwaitingClaim` chứa "Hãy nhận trước" và nút "Nhận thưởng"; quà hiện vật nhắc "địa chỉ".
- [ ] **Step 2:** `npx vitest run tests/unit/events/eventPopups.test.ts` → FAIL.
- [ ] **Step 3:** Cài đặt. Hook: chạy khi user role `User`/`ContentCreator` đã sẵn sàng; gọi `myResults` + danh sách thông báo; đọc/ghi `sessionStorage` trong try/catch; đóng pop-up từ thông báo thì `PUT /Notification/{id}/read`; đặt khoá phiên sau khi đã hiện pop-up `award` đầu tiên. Hiệu ứng nhẹ qua `lib/motion`, tôn trọng `prefersReducedMotion()`. Nút "Nhận thưởng" trong pop-up mở luồng nhận ở trang kết quả (điều hướng tới đích kèm `&claim=<awardId>`).
- [ ] **Step 4:** Chạy test → PASS; `npx tsc --noEmit` → 0.

---

### Task 8: Frontend — trang kết quả của sự kiện đã kết thúc

**Việc này làm gì (nói dễ hiểu):**
Khi sự kiện đã kết thúc, trang sự kiện của phụ huynh/nhà sáng tạo đổi thành trang kết quả: "Kết quả của bạn" trên cùng (giải, trạng thái, hạn khiếu nại, nút khiếu nại/nhận thưởng, giải nằm ở đâu), rồi danh sách giải công bố và tổng kết; trước khi công bố thì hiện "Ban tổ chức đang xét kết quả" kèm tóm tắt đóng góp.

**Cần thêm / còn thiếu:**
- Đã có: `EventsWorkspace` (chọn sự kiện bằng state `selectedId`), `EventActivityParticipantView`, `EventCreatorView`, `EventMissionView`, hộp nhận thưởng + `EventClaimFields`, hộp khiếu nại.
- Cần thêm: đọc `?eventId=` để mở sẵn sự kiện và `&claim=` để mở hộp nhận; component trang kết quả; hộp khiếu nại có chọn loại; ô "Cho phép hiện tên bé trong danh sách giải" khi nhận giải hoàn thành nhiệm vụ của phụ huynh.
- Còn thiếu / ngoài phạm vi: màn Moderator (Task 9).

**Files:**
- Create: `src/app/features/events/EventResultsView.tsx` (≤ ~250 dòng; tách `EventMyResultCard.tsx` nếu vượt)
- Modify: `src/app/features/events/EventsWorkspace.tsx`, `EventActivityParticipantView.tsx` (dùng `EventResultsView` khi sự kiện đã kết thúc; thu gọn thẻ hoạt động cũ trong `<details>`), `EventCreatorView.tsx`/`EventMissionView.tsx` nếu chúng là đường hiển thị của sự kiện theo hoạt động
- Test: `src/tests/unit/events/eventsUi.test.tsx`

**Interfaces:**
- Consumes: Task 6 (`myResults`, `publicResults`, `createAppeal` có `kind`, `claimReward` có `showChildNames`).
- Produces: `EventResultsView({ detail, myResult, publicResults, onRefresh })`; phần tử `id="my-result"`.
  Chỗ giải nằm: lượt AI → "Tài khoản › Thông tin cá nhân" (`/app/account`; nhà sáng tạo: màn tài khoản có `CreatorQuotaPanel`); gói Creator → "Tài khoản › Nâng cấp gói" (`/app/account-payment`); quà hiện vật / tiền → "Điền thông tin nhận ngay trên trang này".

- [ ] **Step 1: Viết test thất bại**
  - chưa công bố → "Ban tổ chức đang xét kết quả" + số truyện đã đọc từ `recap`.
  - đã công bố, người thắng `AppealHold` → "Chờ hết hạn khiếu nại", không có nút "Nhận thưởng", không có nút "Khiếu nại".
  - người không có giải, `canAppeal` → nút "Khiếu nại" và hộp có 2 lựa chọn "Kết quả xác minh" / "Thứ hạng / người nhận giải".
  - giải hoàn thành nhiệm vụ của phụ huynh `AwaitingClaim` → hộp nhận có ô "Cho phép hiện tên bé trong danh sách giải" (mặc định không tích).
  - danh sách công bố: giải truyện hiện tên truyện + tác giả; giải nhiệm vụ không đồng ý hiện "cùng 2 bé".
- [ ] **Step 2:** `npx vitest run tests/unit/events/eventsUi.test.tsx` → FAIL.
- [ ] **Step 3:** Cài đặt. Khi có `#my-result` thì cuộn tới phần tử sau khi tải xong. Khiếu nại/nhận thưởng thông báo kết quả bằng toast.
- [ ] **Step 4:** Test → PASS; `npx tsc --noEmit` → 0.

---

### Task 9: Frontend — màn Moderator: giữ giải, thu hồi, loại khiếu nại

**Việc này làm gì (nói dễ hiểu):**
Moderator thấy suất đang "Chờ hết hạn khiếu nại" hoặc "Đã thu hồi" (kèm lý do), bấm "Thu hồi" để chọn người thay và ghi lý do, và thấy đơn khiếu nại thuộc loại nào, nhắm suất nào. Dòng trạng thái hiện "Đã công bố · hạn khiếu nại đến …".

**Cần thêm / còn thiếu:**
- Đã có: `EventAwardsTable` (cột Người nhận, hộp chỉnh suất dùng API ứng viên), `EventModeratorPanel` (dòng trạng thái, danh sách khiếu nại).
- Cần thêm: nút + hộp thoại Thu hồi; nhãn trạng thái; hiển thị `kind`/`targetAwardLabel` trên đơn; dòng trạng thái giai đoạn công bố.
- Còn thiếu / ngoài phạm vi: không cho thu hồi suất đã mở nhận.

**Files:**
- Modify: `src/app/features/events/EventAwardsTable.tsx`, `src/app/features/events/EventModeratorPanel.tsx`
- Test: `src/tests/unit/events/eventsUi.test.tsx`

**Interfaces:**
- Consumes: Task 6 `revokeAward`, `awardCandidates`.
- Produces: prop `canRevoke?: boolean` trên `EventAwardsTable` (true khi `canManageEvent && isActivityBased && detail.resultsPublishedAt` và chưa quá `detail.appealWindowEndsAt` hoặc còn đơn mở).

- [ ] **Step 1:** Test: suất `AppealHold` + `canRevoke` → nút "Thu hồi"; suất `Revoked` → "Đã thu hồi" và lý do; đơn `Ranking` hiện "Thứ hạng / người nhận giải" và nhãn suất; dòng trạng thái "Đã công bố" chứa "hạn khiếu nại đến".
- [ ] **Step 2:** Chạy → FAIL.
- [ ] **Step 3:** Cài đặt; hộp thu hồi bắt buộc lý do (≤ 500), lựa chọn "Không gán người thay"; xác nhận bằng `confirmDialog` trước khi gửi.
- [ ] **Step 4:** Test → PASS; `npx tsc --noEmit` → 0.

---

### Task 10: Kiểm tra toàn bộ

**Việc này làm gì (nói dễ hiểu):**
Chạy lại toàn bộ test hai repo, cập nhật các bảng kiểm kê API/phân quyền, và xem lại diff để chắc chỉ có thay đổi đúng phạm vi.

**Cần thêm / còn thiếu:**
- Đã có: lệnh test, script audit API, snapshot phân quyền.
- Cần thêm: không có tính năng mới.
- Còn thiếu / ngoài phạm vi: chạy thử trên trình duyệt cần backend mới + migration — báo người dùng, không tự áp vào DB của họ.

- [ ] **Step 1:** Frontend: `cd AI_Storytelling_Frontend/src && node scripts/audit-core-api.mjs --write && npx tsc --noEmit && npx vitest run` → 0 lỗi, mọi test PASS (nếu vitest báo `reading 'config'` hàng loạt ở `describe`, chạy lại với `--pool=threads`; đây là lỗi môi trường đã gặp).
- [ ] **Step 2:** Backend: `dotnet test tests/StoryPlatform.UnitTests --artifacts-path .tmp/verify-artifacts` và `dotnet test tests/StoryPlatform.IntegrationTests --artifacts-path .tmp/verify-artifacts` → 0 Failed; xoá `.tmp/verify-artifacts`.
- [ ] **Step 3:** `git status` + `git diff --stat` hai repo: chỉ các file trong plan, snapshot, coverage json, migration mới.
