# Bình chọn không cần đăng ký trước — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Parent tham gia hoạt động Bình chọn (`EventActivityType.Voting`) của sự kiện ActivityBased mà không cần bấm đăng ký trước; lượt tham gia được tạo ngầm ở phiếu đầu tiên, kèm xác nhận đồng ý thể lệ.

**Architecture:** Không đổi schema. `CastActivityVoteLockedAsync` (đã chạy trong transaction + `AcquireEventLockAsync`) tự tạo `EventParticipation` cho hoạt động Voting khi chưa có, chỉ khi request có `AcceptRules = true`. Danh sách truyện để bình chọn có endpoint riêng `GET /api/v1/events/{eventId}/voting-stories` để không phải đổi `mission-dashboard` (màn legacy `EventMissionView` cần `dashboard.participation` luôn khác null). Lịch đăng ký và giới hạn đăng ký của Voting bị backend chuẩn hoá (đăng ký = khung bình chọn, `MaxRegistrations = null`) và ẩn trên form.

**Tech Stack:** .NET 10 / EF Core PostgreSQL / xUnit (`InMemoryUnitOfWork`); Next.js 16 / React / Vitest.

**Spec:** Không có file spec riêng; các quyết định nằm ở "Global Constraints" (đã chốt trong hội thoại 27/11/2026 khi rà đề xuất "bình chọn tự tham gia").

## Global Constraints

- Không thêm migration, không đổi bảng/cột.
- Chỉ áp dụng cho `ConfigVersion == ActivityBased`; sự kiện legacy (track) giữ nguyên.
- Giữ nguyên mọi kiểm tra an toàn hiện có của phiếu: bài dự thi Approved đúng `LockedStoryVersionId`, truyện đã xuất bản, không tự bình chọn, bé (hồ sơ thuộc Parent) đọc xong đúng `StoryVersionId` trong `[StartAt, EndAt]` của hoạt động Voting.
- Lượt tham gia chỉ được tạo **sau khi** mọi kiểm tra của phiếu đã qua; phiếu lỗi không để lại `EventParticipation`.
- Lượt tham gia tạo ngầm: `AcceptedRuleVersionId` = thể lệ đang `IsActive`; không có thể lệ active → `ConflictException("Sự kiện chưa có phiên bản thể lệ hoạt động.")` (cùng câu với đăng ký).
- Chưa có lượt tham gia và `AcceptRules != true` → `BadRequestException("Bạn cần đồng ý thể lệ sự kiện trước khi gửi bình chọn lần đầu.")`.
- Lượt tham gia Voting `Disqualified` vẫn bị chặn với câu cũ `"Bạn chưa đăng ký hợp lệ cho hoạt động bình chọn."` (Forbidden).
- Endpoint đăng ký (`RegisterActivityLockedAsync`) vẫn nhận Voting (idempotent, tương thích client cũ) nhưng bỏ qua `MaxRegistrations` với Voting.
- Chữ hiển thị tiếng Việt; frontend theo `src/AGENTS.md` (biến màu, `.btn`, không `../`).
- Không thêm `Co-Authored-By`/"Generated with Claude" vào commit; không commit/push khi chưa được yêu cầu.

## Review Focus

1. Parent chưa có hồ sơ bé hoặc bé chưa đọc truyện gửi phiếu lần đầu (đã tích đồng ý) → lỗi đọc truyện như cũ và **không** sinh `EventParticipation` (Task 1 test `CastVote_Voting_FailedCheck_DoesNotCreateParticipation`).
2. Parent đã có lượt tham gia Voting (đăng ký kiểu cũ) gửi phiếu không kèm `AcceptRules` → vẫn thành công (Task 1 test `CastVote_Voting_ExistingParticipation_DoesNotNeedConsentAgain`).
3. Sự kiện đã kết thúc bình chọn: người chưa tham gia mở trang vẫn xem được danh sách truyện nhưng không có ô bình chọn/đồng ý (Task 2 `CanVote=false` ngoài khung; Task 4 UI test `ẩn ô đồng ý khi không thể bình chọn`).
4. Moderator sửa sự kiện cũ có lịch đăng ký Voting khác khung bình chọn → lưu lại được, lịch bị chuẩn hoá chứ không báo lỗi (Task 2 test `Update_NormalizesVotingRegistrationWindow`).
5. Tài khoản Creator/Moderator gọi `voting-stories` → Forbidden, không lộ danh sách (Task 3 test `GetVotingStories_NonParent_Forbidden`).

---

### Task 1: Tự tạo lượt tham gia khi gửi phiếu đầu tiên

**Việc này làm gì (nói dễ hiểu):**
Khi phụ huynh bấm "Gửi bình chọn" lần đầu mà chưa từng đăng ký hoạt động Bình chọn, hệ thống tự ghi nhận họ tham gia, với điều kiện họ đã tích "đồng ý thể lệ". Nếu phiếu bị từ chối vì bất kỳ lý do nào (bé chưa đọc, tự bình chọn…), sẽ không có bản ghi tham gia nào được tạo. Người đã đăng ký từ trước vẫn bình chọn như bình thường.

**Cần thêm / còn thiếu:**
- Đã có: `CastActivityVoteLockedAsync` chạy trong transaction có khoá sự kiện; ràng buộc duy nhất `(EventId, EventActivityId, UserId)`; cách tạo `EventParticipation` trong `RegisterActivityLockedAsync`.
- Cần thêm: trường `AcceptRules` trong `CastEventVoteRequestDto`; nhánh tạo lượt tham gia ngầm sau khi mọi kiểm tra đã qua; câu lỗi khi thiếu đồng ý thể lệ.
- Còn thiếu / ngoài phạm vi: trạng thái `CanVote` / `CanRegister` (Task 2), danh sách truyện cho người chưa tham gia (Task 3), giao diện (Task 4).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Events/DTOs/EventDtos.cs:642-648`
- Modify: `src/Core/StoryPlatform.Application/Features/Events/Services/EventParticipationService.Activities.cs:518-641` (and `:104-113` for the shared factory)
- Test: `tests/StoryPlatform.UnitTests/Features/Events/ActivityBasedEventTests.cs`

**Interfaces:**
- Produces: `CastEventVoteRequestDto.AcceptRules` (`bool`, default `false`); private helper `EventParticipation NewActivityParticipation(int eventId, int activityId, int userId, int ruleVersionId)` (Status `Registered`, `TrackId = null`) used by both register and vote paths.

- [ ] **Step 1: Write the failing tests** in `ActivityBasedEventTests.cs`, reusing the setup of `CastVote_ActivityBased_BlocksSelfVote_OutsideWindow_AndMismatchedVersionSession` (Voting activity id 20, approved submission story 100, active rule, completed `ReadingSession` in window) but **without** seeding a Voting `EventParticipation`:
  - `CastVote_Voting_WithoutRegistration_RequiresRuleConsent`: `CastVoteAsync(voter, 1, new() { StoryId = 100, Stars = 5 })` throws `BadRequestException` with message `"Bạn cần đồng ý thể lệ sự kiện trước khi gửi bình chọn lần đầu."`; `EventParticipation` count for (event 1, activity 20, voter) == 0.
  - `CastVote_Voting_WithoutRegistration_CreatesParticipationAndVote`: with `AcceptRules = true` succeeds; exactly one participation for (1, 20, voter) with `AcceptedRuleVersionId == activeRule.Id` and `Status == MissionCompleted`; the saved `EventVote.ParticipationId` equals that participation's id.
  - `CastVote_Voting_FailedCheck_DoesNotCreateParticipation`: `AcceptRules = true` but no `ReadingSession` → `ConflictException` ("Bé cần đọc xong…"); participation count == 0.
  - `CastVote_Voting_ExistingParticipation_DoesNotNeedConsentAgain`: seed participation (Registered), `AcceptRules = false` → succeeds.
  - `CastVote_Voting_DisqualifiedParticipation_StillForbidden`: seed participation `Disqualified`, `AcceptRules = true` → `ForbiddenException`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test StoryPlatform.sln --artifacts-path .tmp/verify-artifacts --filter "FullyQualifiedName~ActivityBasedEventTests.CastVote_Voting"`
Expected: FAIL (compile error on `AcceptRules`, then Forbidden instead of the expected outcomes).

- [ ] **Step 3: Implement**
  - Add `public bool AcceptRules { get; set; }` to `CastEventVoteRequestDto`.
  - Extract `NewActivityParticipation(...)` from `RegisterActivityLockedAsync` (`:104-112`) and use it there.
  - In `CastActivityVoteLockedAsync`: keep the `Disqualified` → Forbidden check only for an existing participation. If participation is null: throw the consent `BadRequestException` when `!request.AcceptRules` (before the other checks so the message is clear), load the active `EventRuleVersion` (Conflict message per Global Constraints). Run every existing check unchanged; only after `proof` is found and before building the `EventVote`, `AddAsync(NewActivityParticipation(...))` and `SaveChangesAsync` so `participation.Id` is set for `EventVote.ParticipationId`. The existing tail (`Registered → MissionCompleted`) then applies to the new row.

- [ ] **Step 4: Run to verify they pass** (same command, plus the existing `CastVote_ActivityBased_*` and `Dashboard_*` tests in that class)
Expected: PASS.

---

### Task 2: Trạng thái hoạt động và chuẩn hoá cấu hình Voting

**Việc này làm gì (nói dễ hiểu):**
Trang sự kiện cho phụ huynh không còn hiện nút "Đăng ký" ở hoạt động Bình chọn. Ô bình chọn mở cho mọi phụ huynh đủ điều kiện trong đúng giờ bình chọn. Khi Moderator tạo hoặc sửa sự kiện, thời gian đăng ký của Bình chọn tự lấy bằng thời gian bình chọn và không còn giới hạn số người, nên dữ liệu cũ vẫn lưu được.

**Cần thêm / còn thiếu:**
- Đã có: `BuildActivityUserStatesAsync` tính `CanRegister` / `CanVote`; `ValidateActivityBasedDraftRequest` và `SyncActivitiesAndAwardsAsync` xử lý request tạo/sửa.
- Cần thêm: với Voting thì `CanRegister = false`, `CanVote` không cần đã đăng ký (trừ khi bị loại); chuẩn hoá `RegistrationStartAt/EndAt = StartAt/EndAt` và `MaxRegistrations = null` cho Voting trước khi kiểm tra; bỏ áp `MaxRegistrations` với Voting khi đăng ký.
- Còn thiếu / ngoài phạm vi: sự kiện legacy (track) giữ nguyên; dữ liệu Voting đã có trong DB không chạy lại chuẩn hoá cho tới khi được sửa (không ảnh hưởng vì `CanVote` không còn phụ thuộc lịch đăng ký).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Events/Services/EventManagementService.Activities.cs:35` (`ValidateActivityBasedDraftRequest`), `:348-359` (`BuildActivityUserStatesAsync`)
- Modify: `src/Core/StoryPlatform.Application/Features/Events/Services/EventParticipationService.Activities.cs:63-69` (MaxRegistrations check)
- Test: `tests/StoryPlatform.UnitTests/Features/Events/ActivityBasedEventTests.cs`

**Interfaces:**
- Consumes: none from Task 1.
- Produces: `EventActivityUserStateDto` semantics for Voting — `CanRegister == false` always; `CanVote == Published && eligible && (part == null || part.Status != Disqualified) && StartAt <= now <= EndAt`; `IsRegistered` unchanged (`part != null`, frontend uses it to decide whether to show the consent checkbox).

- [ ] **Step 1: Write the failing tests**
  - `Detail_Voting_UnregisteredParent_CanVoteWithoutRegister`: `GetEventDetailAsync(1, currentUserId: parent, currentUserRole: User)` with Voting open now → state for Voting has `CanRegister == false`, `CanVote == true`, `IsRegistered == false`.
  - `Detail_Voting_OutsideWindow_CannotVote`: Voting `EndAt` in the past → `CanVote == false`.
  - `Detail_Voting_Disqualified_CannotVote`: seeded Voting participation `Disqualified` → `CanVote == false`.
  - `Create_NormalizesVotingRegistrationWindow`: `CreateEventAsync` with Voting `RegistrationStartAt = StartAt - 2d`, `RegistrationEndAt = StartAt - 1d`, `MaxRegistrations = 10` → stored activity has `RegistrationStartAt == StartAt`, `RegistrationEndAt == EndAt`, `MaxRegistrations == null`.
  - `Update_NormalizesVotingRegistrationWindow`: `UpdateEventAsync` with Voting `RegistrationEndAt > EndAt` (previously rejected as "Khoảng thời gian hoạt động Voting không hợp lệ.") → succeeds and is normalized.
  - `Register_Voting_IgnoresMaxRegistrations`: Voting entity seeded with `MaxRegistrations = 1` and one existing participation → second parent's register succeeds.

- [ ] **Step 2: Run to verify they fail**
Run: `dotnet test StoryPlatform.sln --artifacts-path .tmp/verify-artifacts --filter "FullyQualifiedName~ActivityBasedEventTests"`
Expected: the six new tests FAIL.

- [ ] **Step 3: Implement**
  - At the top of `ValidateActivityBasedDraftRequest`, for each activity with `ActivityType == Voting`: set `RegistrationStartAt = StartAt`, `RegistrationEndAt = EndAt`, `MaxRegistrations = null` (mutating the request so `SyncActivitiesAndAwardsAsync` persists the normalized values).
  - In `BuildActivityUserStatesAsync`: `CanRegister` adds `act.ActivityType != EventActivityType.Voting`; `CanVote` replaces `activePart` with `(part == null || part.Status != EventParticipationStatus.Disqualified)`.
  - In `RegisterActivityLockedAsync`: apply the `MaxRegistrations` cap only when `act.ActivityType != EventActivityType.Voting`.

- [ ] **Step 4: Run to verify they pass** (same command)
Expected: PASS, including existing tests in the class.

---

### Task 3: Endpoint danh sách truyện để bình chọn

**Việc này làm gì (nói dễ hiểu):**
Thêm một API để phụ huynh xem các truyện dự thi có thể bình chọn, kể cả khi họ chưa tham gia hoạt động nào. API cho biết bé đã đọc truyện nào trong giờ bình chọn và phiếu họ đã gửi. Truyện do chính họ viết không xuất hiện.

**Cần thêm / còn thiếu:**
- Đã có: `LoadActivityStoriesAsync(ev, participation, act, ct)` đã lọc đúng bài dự thi Approved, bỏ truyện của chính mình, tính `HasFamilyRead` và `MyVoteStars`; quyền `Permissions.Shared.EventParticipate`.
- Cần thêm: đổi `LoadActivityStoriesAsync` sang nhận `userId`; method `GetVotingStoriesAsync`; route `GET {eventId:int}/voting-stories`; cập nhật snapshot phân quyền.
- Còn thiếu / ngoài phạm vi: `mission-dashboard` giữ nguyên (vẫn trả `VotingStories` cho người đã tham gia).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Events/Interfaces/IEventServices.cs:42-44`
- Modify: `src/Core/StoryPlatform.Application/Features/Events/Services/EventParticipationService.Activities.cs:397,425-427` and `EventParticipationService.cs:349`
- Modify: `src/Core/StoryPlatform.Api/Controllers/EventsController.cs:209-213` (add the new action next to `MissionDashboard`)
- Modify: `tests/StoryPlatform.UnitTests/Authorization/endpoint-authorization.snapshot.json` (regenerated)
- Test: `tests/StoryPlatform.UnitTests/Features/Events/ActivityBasedEventTests.cs`

**Interfaces:**
- Produces: `Task<IReadOnlyList<EventStoryLibraryItemDto>> GetVotingStoriesAsync(int userId, int eventId, CancellationToken ct = default);` on `IEventParticipationService`; `LoadActivityStoriesAsync(Event ev, int userId, EventActivity act, CancellationToken ct)`; HTTP `GET /api/v1/events/{eventId}/voting-stories` → `ApiResponse<IReadOnlyList<EventStoryLibraryItemDto>>`, `[HasPermission(Permissions.Shared.EventParticipate)]`.
- Behaviour: event not found → `NotFoundException`; not ActivityBased or no Voting activity → `NotFoundException(nameof(EventActivity), eventId)`; user role not `User` → `ForbiddenException("Bản đầu hoạt động bình chọn chỉ mở cho Parent.")`; event not `Published` → empty list; otherwise the stories list (also after `EndAt`, so results remain browsable).

- [ ] **Step 1: Write the failing tests**
  - `GetVotingStories_UnregisteredParent_ListsApprovedSubmissionsWithReadState`: same seed as `Dashboard_KeepsCuratedReadingSeparateFromEligibleVotingStories` minus participations → one item, `HasFamilyRead == false`; add a completed `ReadingSession` in window → `HasFamilyRead == true`.
  - `GetVotingStories_ExcludesOwnStory`: parent is the author of story 100 → list empty.
  - `GetVotingStories_NonParent_Forbidden`: ContentCreator user → `ForbiddenException`.
  - `GetVotingStories_NoVotingActivity_NotFound`.

- [ ] **Step 2: Run to verify they fail**
Run: `dotnet test StoryPlatform.sln --artifacts-path .tmp/verify-artifacts --filter "FullyQualifiedName~ActivityBasedEventTests.GetVotingStories"`
Expected: FAIL (method missing).

- [ ] **Step 3: Implement** the signature change (callers pass `participation.UserId`), the service method and the controller action `VotingStories(int eventId, CancellationToken ct = default) => HandleResult(await _participation.GetVotingStoriesAsync(GetCurrentUserId(), eventId, ct));`.

- [ ] **Step 4: Regenerate the authorization snapshot**
Run (PowerShell): `$env:UPDATE_AUTHZ_SNAPSHOT='1'; dotnet test StoryPlatform.sln --artifacts-path .tmp/verify-artifacts --filter "FullyQualifiedName~EndpointAuthorizationMatrixTests"; Remove-Item Env:UPDATE_AUTHZ_SNAPSHOT`
Expected: snapshot gains `"EventsController.VotingStories#GET {eventId:int}/voting-stories"` with the same permission list as `MissionDashboard`; rerun without the variable → PASS.

- [ ] **Step 5: Full backend verification**
Run: `dotnet test StoryPlatform.sln --artifacts-path .tmp/verify-artifacts` then delete `.tmp/verify-artifacts`.
Expected: all unit/integration tests pass (integration skips unchanged).

---

### Task 4: Frontend — bình chọn không cần đăng ký

**Việc này làm gì (nói dễ hiểu):**
Trên trang sự kiện, hoạt động Bình chọn không còn nút "Chấp nhận thể lệ & Đăng ký" mà hiện ghi chú "Không cần đăng ký". Khối "Truyện dự thi để bình chọn" hiện cho mọi phụ huynh đủ điều kiện. Ở lần bình chọn đầu, phụ huynh phải tích "Tôi đã đọc và đồng ý thể lệ sự kiện" thì nút gửi mới bấm được.

**Cần thêm / còn thiếu:**
- Đã có: `eventService.castVote`, khối truyện bình chọn trong `EventActivityParticipantView`, `EventsWorkspace.loadSelected` tải dashboard.
- Cần thêm: `eventService.votingStories`; state `votingStories` trong `EventsWorkspace`; prop mới cho view; ô đồng ý thể lệ; gửi `acceptRules`; cập nhật `docs/core-api-coverage.json`.
- Còn thiếu / ngoài phạm vi: form Moderator (Task 5).

**Files:**
- Modify: `AI_Storytelling_Frontend/src/app/services/eventService.ts:325-336`
- Modify: `AI_Storytelling_Frontend/src/app/features/events/EventsWorkspace.tsx` (`loadSelected`, render of `EventActivityParticipantView`)
- Modify: `AI_Storytelling_Frontend/src/app/features/events/EventActivityParticipantView.tsx:178-191, 233-262, 377-450`
- Modify: `AI_Storytelling_Frontend/src/docs/core-api-coverage.json`
- Test: `AI_Storytelling_Frontend/src/tests/unit/events/eventsUi.test.tsx`

**Interfaces:**
- Consumes: Task 1 `acceptRules` body field; Task 2 state semantics; Task 3 `GET /events/{id}/voting-stories`.
- Produces: `eventService.votingStories(eventId: number): Promise<ApiResponse<EventStoryLibraryItemDto[]>>`; `castVote` payload type gains `acceptRules?: boolean`; `EventActivityParticipantView` prop `votingStories: EventStoryLibraryItemDto[] | null`.

- [ ] **Step 1: Write the failing UI tests** in `eventsUi.test.tsx` (render `EventActivityParticipantView` with a Voting state `{ isRegistered: false, isEligible: true, canRegister: false, canVote: true }`, `dashboard = null`, `votingStories = [story with hasFamilyRead: true, source: "CreatorSubmission"]`):
  - `bình chọn không có nút đăng ký và hiện ghi chú không cần đăng ký`: no text "Chấp nhận thể lệ & Đăng ký" on the Voting row; text "Không cần đăng ký" present.
  - `hiện truyện bình chọn cho phụ huynh chưa tham gia`: heading "Truyện dự thi để bình chọn" and the story title present.
  - `nút gửi bị khoá tới khi tích đồng ý thể lệ và gửi acceptRules`: button "Gửi bình chọn" disabled; after checking "Tôi đã đọc và đồng ý thể lệ sự kiện", clicking calls `eventService.castVote` with `expect.objectContaining({ storyId, acceptRules: true })`.
  - `không hiện ô đồng ý khi đã tham gia`: with `isRegistered: true` the checkbox is absent and the button enabled.
  - `ẩn ô đồng ý khi không thể bình chọn`: `canVote: false` → no checkbox, no vote controls, story list still visible.

- [ ] **Step 2: Run to verify they fail**
Run (PowerShell, from `AI_Storytelling_Frontend/src`): `npx vitest run tests/unit/events/eventsUi.test.tsx`
Expected: the new tests FAIL. (Do not run vitest from Git Bash: `/d/...` paths duplicate the module and every file fails with `reading 'config'`.)

- [ ] **Step 3: Implement**
  - `eventService.votingStories` → `apiRequest(\`/events/${eventId}/voting-stories\`, "Không tải được danh sách truyện bình chọn.")`.
  - `EventsWorkspace.loadSelected`: when `role === "User"`, `det.data.configVersion === "ActivityBased"` and the detail has a Voting activity, fetch `votingStories` in the same `Promise.all`; store in state, reset to `null` in `selectEvent`; pass to the view.
  - `EventActivityParticipantView`: for the Voting row render the note "Không cần đăng ký · Mở khi đến giờ bình chọn" instead of the register button; the "Truyện trong sự kiện" card renders when `readState?.isRegistered && dashboard` **or** `voteState?.isEligible && votingStories`; the voting list uses `votingStories ?? dashboard?.votingStories ?? []`; when `voteState.canVote && !voteState.isRegistered` render one checkbox (`.inp`-styled label) above the list, keep it in state, disable the vote buttons until checked, and send `acceptRules: true` in `castVote`.
  - Add the endpoint to `docs/core-api-coverage.json` following the existing `mission-dashboard` entry.

- [ ] **Step 4: Run to verify** — `npx tsc --noEmit -p .`, `npx eslint app/features/events app/services/eventService.ts`, `npx vitest run tests`
Expected: no TS errors, no ESLint errors, all tests pass.

---

### Task 5: Frontend — ẩn lịch đăng ký và giới hạn của Bình chọn trên form Moderator

**Việc này làm gì (nói dễ hiểu):**
Trong trình tạo hoặc sửa sự kiện, phần Bình chọn không còn hai ô "Mở đăng ký" / "Đóng đăng ký". Form tự gửi thời gian đăng ký bằng thời gian bình chọn, nên Moderator chỉ cần chọn khi nào bắt đầu và kết thúc bình chọn.

**Cần thêm / còn thiếu:**
- Đã có: `voteRegStart` / `voteRegEnd` và các trường ngày trong `EventActivityWizard`; payload Voting đã gửi `maxRegistrations: null`.
- Cần thêm: bỏ state, ô nhập và kiểm tra lỗi đăng ký của Voting; payload gửi `registrationStartAt` / `registrationEndAt` bằng giá trị bắt đầu / kết thúc bình chọn; trường "Bắt đầu" bỏ `min={voteRegStart}`.
- Còn thiếu / ngoài phạm vi: Submission và ReadingMissions giữ nguyên lịch đăng ký.

**Files:**
- Modify: `AI_Storytelling_Frontend/src/app/features/events/EventActivityWizard.tsx:168-169, 270, 350-351, 606-612`
- Test: `AI_Storytelling_Frontend/src/tests/unit/events/eventsUi.test.tsx` (existing `EventActivityWizard` render at `:490`)

**Interfaces:**
- Consumes: Task 2 backend normalization (frontend values are a convenience; backend is authoritative).

- [ ] **Step 1: Write the failing test** `wizard bình chọn không có ô đăng ký và gửi lịch đăng ký bằng khung bình chọn`: render the wizard with Voting enabled → no labels "Mở đăng ký (VN)" / "Đóng đăng ký (VN)" inside the Voting section; on submit the Voting activity in the payload has `registrationStartAt === startAt` and `registrationEndAt === endAt`.
- [ ] **Step 2: Run to verify it fails** — `npx vitest run tests/unit/events/eventsUi.test.tsx` → FAIL.
- [ ] **Step 3: Implement** the removals and payload mapping; drop `regStart` / `regEnd` from the Voting branch of `registrationDates(...)` (`:270`) so no hidden error can block saving.
- [ ] **Step 4: Run to verify** — `npx tsc --noEmit -p .`, `npx eslint app/features/events`, `npx vitest run tests` → all pass.
