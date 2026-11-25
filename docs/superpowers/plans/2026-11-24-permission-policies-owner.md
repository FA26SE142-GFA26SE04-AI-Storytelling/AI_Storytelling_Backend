# Permission Phần 3 (chủ sở hữu) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Khoá lỗi truy cập chéo của phụ huynh/Creator bằng test API và hiện luật chủ sở hữu, gói, ủy quyền, Parental Gate trên trang Phân quyền.

**Architecture:** Test tích hợp tự liệt kê endpoint từ `EndpointDataSource` của ứng dụng thật; điều kiện mô tả thêm vào `DefaultRolePermissions.GrantConditions` (không đánh giá trong `AccessPolicy`).

**Tech Stack:** .NET 10, xUnit, `WebApplicationFactory` + SQLite (`ApiFactory`).

**Spec:** `docs/superpowers/specs/2026-11-24-permission-policies-owner-design.md`

## Global Constraints
- Không đổi luật gói, tự duyệt, Parental Gate; snapshot endpoint không đổi.

## Review Focus
- Endpoint nhận `{storyId}` của truyện Creator khi gọi bằng phụ huynh (khác nhóm) — phải 403/404.
- Endpoint ghi trả 400 vì body rỗng trước khi kiểm chủ sở hữu — không lộ dữ liệu nhưng không chứng minh kiểm chủ; ghi nhận trong danh sách.
- Endpoint mới thêm sau này có `{childProfileId}` — test tự quét, không cần cập nhật tay.

---

### Task 1: Test chặn truy cập chéo

**Việc này làm gì (nói dễ hiểu):** Giả làm một phụ huynh (hoặc Creator) khác, thử mở và sửa hồ sơ trẻ, truyện, điều ước không phải của mình qua mọi API có id. Không API nào được trả thành công. Sau này ai thêm API mới mà quên kiểm chủ sở hữu thì test sẽ báo.

**Cần thêm / còn thiếu:**
- Đã có: `ApiFactory`, `ApiSeed`, `IChildProfileAccessGuard`.
- Cần thêm: `CrossOwnerAccessApiTests`; sửa service nếu test lộ lỗi.
- Còn thiếu / ngoài phạm vi: endpoint của trẻ (Child Session) và nhân viên.

**Files:** Create `tests/StoryPlatform.IntegrationTests/Api/CrossOwnerAccessApiTests.cs`; Modify service nếu cần.

- [ ] Viết test; chạy; ghi nhận từng endpoint trả 2xx (lỗi) hoặc GET không phải 403/404.
- [ ] Sửa lỗi thật trong service; test pass.

### Task 2: Điều kiện mô tả cho quyền phụ huynh/Creator

**Việc này làm gì (nói dễ hiểu):** Trang Phân quyền ghi rõ: phụ huynh chỉ thao tác trên hồ sơ trẻ, truyện của mình; giới hạn theo gói; tự duyệt cần ủy quyền; vào khu quản lý từ phiên của trẻ phải qua Parental Gate; Creator chỉ sửa truyện của mình.

**Cần thêm / còn thiếu:**
- Đã có: `AccessConditions`, `GrantConditions`, cột điều kiện ở frontend.
- Cần thêm: 5 điều kiện (`OwnChildProfile`, `PlanLimits`, `DelegatedApproval`, `ParentalGate`, `OwnCreatorStory`) kèm câu mô tả; gắn cho User / ContentCreator.

**Files:** Modify `AccessConditions.cs`, `DefaultRolePermissions.cs`; Test `RolePermissionAdminServiceTests`.

- [ ] Test thất bại → cài đặt → `dotnet test StoryPlatform.slnx` pass.
