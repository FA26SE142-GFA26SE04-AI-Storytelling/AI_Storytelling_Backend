# Permission có phạm vi và điều kiện — Phần 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gom các cổng kiểm quyền của luồng kiểm duyệt (Luồng 5 – Phần I) về một `IAccessPolicy` có phạm vi và điều kiện có tên, sửa 2 chỗ lệch luật (Escalated, cấu hình M4) và hiện phạm vi/điều kiện trên trang Phân quyền.

**Architecture:** Tầng Application thêm `AccessScope`, bảng điều kiện theo (role, permission) và `AccessPolicy` (actor đang hoạt động → role có permission qua `IPermissionService` → điều kiện). Service kiểm duyệt gọi `EnsureAsync` thay `if (role == ...)`; các kiểm tra trên mục đã nhận (đúng người nhận, còn hạn 2 giờ) giữ trong từng service vì mỗi service có mã lỗi riêng mà client đang dùng.

**Tech Stack:** .NET 10, xUnit + SQLite (`AccessControlTestDb`), Next.js + Vitest.

**Spec:** `docs/superpowers/specs/2026-11-24-permission-policies-design.md`

## Global Constraints
- Message/mã lỗi hiện có của từng service giữ nguyên (client hiển thị chúng).
- `EndpointAuthorizationMatrixTests`: chỉ được đổi các endpoint dùng `template.roadmap.author` (thêm Administrator).
- Điều kiện định nghĩa trong code, không lưu DB, không sửa được trên giao diện.
- `AdminFallback` chỉ xét khi nhận một mục đang `Pending`; `NoActiveModerator` chỉ xét khi ghi (`AccessMode.Write`).
- Kích hoạt rubric: Moderator và Administrator đều được, không áp `NoActiveModerator` (Luồng 5, dòng 55).
- Tài khoản thao tác: không xoá, `Status == Active`, `LockedUntil` null hoặc đã qua.

## Review Focus
- Admin đang xử lý dở một mục đã nhận thì có Moderator mới kích hoạt → vẫn phải quyết định xong được (AdminFallback không xét mục đã `Claimed`).
- Moderator duy nhất còn hoạt động chính là người tạo mục → Admin vẫn nhận được mục đó.
- Admin thu lại `moderation.queue.process` của role Moderator trên trang Phân quyền → Moderator không vào được hàng đợi (policy đọc permission thật, không đọc role).
- Admin xem (không sửa) hướng dẫn / rubric / bộ truyện mẫu khi còn Moderator → vẫn xem được.
- Tài khoản bị khoá tạm (`LockedUntil` tương lai) → bị chặn ở mọi service đã chuyển.

---

### Task 1: Nền tảng `IAccessPolicy`, phạm vi và điều kiện

**Việc này làm gì (nói dễ hiểu):** Tạo một "người gác cổng" dùng chung. Mỗi khi nhân viên làm một việc, người gác cổng kiểm tài khoản còn hoạt động, role có quyền đó không, và các điều kiện kèm theo (ví dụ Admin chỉ nhận mục khi hết kiểm duyệt viên). Danh mục quyền cũng ghi thêm mỗi quyền áp trên phạm vi nào.

**Cần thêm / còn thiếu:**
- Đã có: `IPermissionService` (DB + cache), `StaticPermissionService`, `DefaultRolePermissions`, `PermissionCatalog`.
- Cần thêm: `AccessScope`, `AccessMode`, `AccessConditions` (tên + câu mô tả), `DefaultRolePermissions.GrantConditions`, `IAccessPolicy`/`AccessPolicy`, đăng ký DI; Administrator được cấp mặc định `template.roadmap.author` kèm `NoActiveModerator`.
- Còn thiếu / ngoài phạm vi: điều kiện cho Luồng 1–4 và Luồng 5 – Phần II (Phần 2, 3).

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/AccessConditions.cs` (AccessScope, AccessMode, tên + mô tả điều kiện)
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/Interfaces/IAccessPolicy.cs`
- Create: `src/Core/StoryPlatform.Application/Features/AccessControl/Services/AccessPolicy.cs`
- Modify: `PermissionCatalog.cs` (thêm `Scope`), `DefaultRolePermissions.cs` (`GrantConditions`, Admin + `RoadmapTemplateAuthor`), `DependencyInjection.cs`
- Test: `tests/StoryPlatform.UnitTests/AccessControl/AccessPolicyTests.cs`

**Interfaces:**
- Produces:
  - `enum AccessScope { Any, Own, Assigned }`, `enum AccessMode { Write, Read }`
  - `PermissionDefinition(Code, Group, Description, IsStaffGrantable, AccessScope Scope = AccessScope.Any)`
  - `DefaultRolePermissions.ConditionsFor(string roleCode, string permission) : IReadOnlyList<string>`
  - `AccessConditions.ActiveAccount|NotOwnContent|ClaimedBySelf|AdminFallback|NoActiveModerator`, `AccessConditions.Describe(string) : string`
  - `IAccessPolicy.EnsureAsync(int actorUserId, string permission, object? resource = null, AccessMode mode = AccessMode.Write, string? deniedMessage = null, CancellationToken ct = default) : Task<UserAccount>`
  - `AccessPolicy(IUnitOfWork uow, IPermissionService permissions)`; `AccessPolicy.ForTests(IUnitOfWork)` không cần — service dùng `policy ?? new AccessPolicy(uow, new StaticPermissionService())`.

- [ ] **Step 1: Viết test thất bại** `AccessPolicyTests` (SQLite):
  - `InactiveDeletedOrLockedActor_IsForbidden_WithDeniedMessage`
  - `RoleWithoutPermission_IsForbidden`
  - `Admin_ClaimPendingItem_ForbiddenWhileEligibleModeratorExists` (message "Quản trị viên chỉ xử lý hàng đợi khi không còn kiểm duyệt viên hợp lệ để nhận mục này.")
  - `Admin_ClaimPendingItem_AllowedWhenOnlyModeratorIsCreator`, `..._AllowedWhenEscalated` (`EscalatedAt != null`), `..._AllowedOnClaimedItem` (AdminFallback bỏ qua mục đã Claimed)
  - `AnyStaff_ClaimOwnItem_IsForbidden` ("Bạn không thể duyệt nội dung do chính mình tạo hoặc nộp.")
  - `Admin_WriteGuide_ForbiddenWhileActiveModerator_AllowedWhenNone`, `Admin_ReadGuide_AllowedWhileActiveModerator`
  - `Moderator_PermissionRevoked_IsForbidden` (IPermissionService giả trả false)
- [ ] **Step 2:** `dotnet test --filter AccessPolicyTests` → FAIL (chưa có kiểu).
- [ ] **Step 3:** Cài đặt. Bảng điều kiện:
  - Moderator + Administrator: `NotOwnContent` trên `moderation.queue.process`, `review.public_story`, `review.gray_zone`, `review.roadmap`, `review.creator_publication`; Moderator thêm trên `moderation.report.review`, `moderation.rating.moderate`.
  - Administrator: `AdminFallback` trên 5 quyền đầu; `NoActiveModerator` trên `rubric.manage`, `guide.manage`, `wishcard.manage`, `template.roadmap.author`.
  - Scope `Assigned`: 5 quyền đầu + `moderation.report.review`, `moderation.rating.moderate`, `moderation.published.review`.
  - `AccessMode.Read` chỉ kiểm tài khoản + permission.
- [ ] **Step 4:** Test PASS; `UPDATE_AUTHZ_SNAPSHOT=1` cập nhật snapshot, kiểm diff chỉ ở endpoint `template.roadmap.author`.

### Task 2: Hàng đợi kiểm duyệt dùng policy

**Việc này làm gì (nói dễ hiểu):** Khi nhân viên mở hoặc nhận một mục trong hàng đợi, người gác cổng quyết định thay cho đoạn kiểm role cũ. Admin nhận được mục đã được chuyển lên dù vẫn còn kiểm duyệt viên. Mỗi loại mục đòi đúng quyền của nó, nên báo cáo nội dung và đánh giá truyện tự động chỉ dành cho kiểm duyệt viên.

**Cần thêm / còn thiếu:**
- Đã có: `ModerationQueueService` (list/claim/open), `EnsureAdministratorMayClaimAsync`.
- Cần thêm: `ModerationQueuePolicy.ClaimPermission(ModerationItemType)`; claim gọi policy; xoá `EnsureAdministratorMayClaimAsync`.
- Còn thiếu / ngoài phạm vi: chưa có chức năng "chuyển lên" (không chỗ nào ghi `EscalatedAt`); điều kiện đã sẵn sàng khi có.

**Files:** Modify `Features/Moderation/Services/ModerationQueueService.cs`, `ModerationQueuePolicy.cs`; Test: test hàng đợi hiện có + `ModerationQueueServiceTests` thêm ca Escalated.

- [ ] Step 1: test `Admin_ClaimsEscalatedItem_WhileModeratorActive` FAIL.
- [ ] Step 2: `RequireStaffAsync` → `EnsureAsync(actor, QueueProcess, mode: Read, deniedMessage: <message cũ>)`; claim → `EnsureAsync(actor, ClaimPermission(type), item, deniedMessage: ContentReport/StoryRating ? "Report nội dung và đánh giá truyện chỉ do kiểm duyệt viên xử lý." : <message cũ>)`. Ánh xạ: PublicStoryReview→`review.public_story`, GrayZoneStory→`review.gray_zone`, ContentReport→`moderation.report.review`, RoadmapTemplateReview→`review.roadmap`, PublicStoryRemoval→`review.creator_publication`, StoryRating→`moderation.rating.moderate`, CommunitySubmission→`moderation.queue.process`.
- [ ] Step 3: toàn bộ test Moderation PASS.

### Task 3: Các service xử lý mục đã nhận dùng policy

**Việc này làm gì (nói dễ hiểu):** Các màn duyệt truyện công khai, vùng xám, bộ truyện mẫu, gỡ truyện, báo cáo, đánh giá, xem để rà soát và rà soát nội dung công khai đều hỏi chung người gác cổng xem nhân viên có quyền làm loại việc đó không, thay vì mỗi chỗ tự kiểm role.

**Cần thêm / còn thiếu:**
- Đã có: 8 service với cổng kiểm role riêng.
- Cần thêm: thay phần kiểm actor bằng `EnsureAsync(actor, <quyền>, mode: Read|Write, deniedMessage: <message cũ>)`; phần kiểm mục đã nhận giữ nguyên.
- Còn thiếu / ngoài phạm vi: không đổi luồng nghiệp vụ trong các service.

**Files (Modify):** `GrayZoneWorkflowService` (`review.gray_zone`), `PublicStoryReviewService` (`review.public_story`), `CreatorPublicationService` (`review.creator_publication`), `RoadmapReviewService` (`review.roadmap`), `ContentReportReviewService` (`moderation.report.review`), `StoryRatingModerationService` (`moderation.rating.moderate`), `ModerationReviewViewService` (`moderation.published.view`, Read), `PublishedContentReviewService` (`moderation.published.view`, Read).

- [ ] Step 1: thêm constructor tham số tuỳ chọn `IAccessPolicy? accessPolicy = null` (DI tự truyền).
- [ ] Step 2: thay cổng; chạy test của từng service PASS (message cũ giữ nguyên).

### Task 4: Cấu hình M4 theo luật "Admin chỉ sửa khi hết Moderator"

**Việc này làm gì (nói dễ hiểu):** Rubric, hướng dẫn sáng tác, thẻ điều ước và bộ truyện mẫu do kiểm duyệt viên quản lý. Admin vẫn xem được, nhưng chỉ sửa khi không còn kiểm duyệt viên nào hoạt động. Kích hoạt rubric thì cả hai đều làm được. Admin được soạn bộ truyện mẫu khi hết kiểm duyệt viên.

**Cần thêm / còn thiếu:**
- Đã có: `ModerationRubricService.Guard`, `AuthoringGuideManagementService.RequireActorAsync`, `RoadmapTemplateService.RequireModeratorAsync/RequireStaffAsync`, thẻ điều ước không kiểm actor ở service.
- Cần thêm: đọc → `AccessMode.Read`; ghi → `AccessMode.Write`; thẻ điều ước (tạo/sửa/ẩn, ghép thủ công, ghép AI) gọi policy Write.
- Còn thiếu / ngoài phạm vi: prod đã có bản gán quyền trong DB nên Admin chưa tự có `template.roadmap.author`; bật trên trang Phân quyền sau khi deploy.

**Files (Modify):** `ModerationRubricService` (Activate/Validate/Evaluate/List/Get = Read; Create/Clone/Update/Delete = Write), `AuthoringGuideManagementService*.cs`, `RoadmapTemplateService*.cs` (`RequireModeratorAsync` → `template.roadmap.author` Write; `RequireStaffAsync` → `template.roadmap.view` Read), `WishCardManagementService`, `WishCardMatchService`, `WishCardAiMatchService`.
- Test: `Admin_UpdatesGuide_ForbiddenWhileModeratorActive`, `Admin_CreatesWishCard_ForbiddenWhileModeratorActive`, `Admin_CreatesRubric_Forbidden…`, `Admin_ActivatesRubric_Allowed…`, `Admin_CreatesRoadmapTemplate_AllowedWhenNoModerator`.

- [ ] Step 1: test FAIL → Step 2: cài đặt → Step 3: test PASS; sửa test cũ nào đang dựa vào "Admin sửa bất cứ lúc nào" (đây là thay đổi có chủ đích).

### Task 5: Trang Phân quyền hiện phạm vi và điều kiện

**Việc này làm gì (nói dễ hiểu):** Trên trang Phân quyền, mỗi quyền ghi rõ áp cho "Tất cả", "Của mình" hay "Mục đã nhận", và dưới mô tả có dòng điều kiện, ví dụ "Chỉ khi không còn kiểm duyệt viên hoạt động". Admin nhìn là biết quyền đó thực tế dùng được khi nào.

**Cần thêm / còn thiếu:**
- Đã có: `GET admin/permissions`, `GET admin/roles/{code}/permissions`, `AdminPermissions.tsx`.
- Cần thêm: `PermissionDto.Scope`; `RolePermissionsDto.Conditions : Dictionary<permission, string[]>` (câu tiếng Việt); frontend hiện cột phạm vi + dòng điều kiện.
- Còn thiếu / ngoài phạm vi: không sửa điều kiện trên giao diện.

**Files:** Modify `AccessControlDtos.cs`, `RolePermissionAdminService.cs`; frontend `src/app/types/accessControl.ts`, `src/app/features/staff/permissionMatrix.ts` (`scopeLabel`), `AdminPermissions.tsx`; Test: `RolePermissionAdminServiceTests` (Conditions của Administrator có `AdminFallback` trên `moderation.queue.process`), `permissionMatrix.test.ts` (`scopeLabel`).

- [ ] Step 1: test FAIL → Step 2: cài đặt → Step 3: PASS.

### Task 6: Kiểm toàn bộ
- [ ] `dotnet build` + `dotnet test` toàn bộ; `npm run test` + `npx tsc --noEmit` ở frontend.
- [ ] Cập nhật spec mục 5.3–5.5 cho khớp quyết định khi làm (kiểm mục đã nhận giữ trong service; `AccessMode`).
