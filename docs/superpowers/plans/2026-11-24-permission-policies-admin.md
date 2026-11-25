# Permission Phần 2 (quản trị) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Các thao tác ghi của Admin ở Luồng 2 và Luồng 5 phần II đi qua `IAccessPolicy` (tài khoản hoạt động, không khoá, role có permission).

**Architecture:** Mỗi service nhận thêm `IAccessPolicy? accessPolicy = null` (mặc định `AccessPolicy.Default(uow)`), thay kiểm role bằng `EnsureAsync(admin, <quyền>, deniedMessage: <message cũ>)`.

**Tech Stack:** .NET 10, xUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-11-24-permission-policies-admin-design.md`

## Global Constraints
- Message 403 cũ giữ nguyên; service trước đây không kiểm thì dùng `"Chỉ quản trị viên đang hoạt động mới thực hiện được thao tác này."`.
- Snapshot endpoint không đổi.

## Review Focus
- Admin bị tạm khoá đăng nhập (`LockedUntil` tương lai) gọi đánh dấu đã thanh toán → 403, giao dịch không đổi.
- Webhook SePay áp gói (không có admin) không đi qua cổng mới.
- Phụ huynh xem lượt của hồ sơ con mình vẫn được; Admin bị thu `quota.manage` thì không xem được hồ sơ người khác.

---

### Task 1: Service đang kiểm role → policy

**Việc này làm gì (nói dễ hiểu):** Các màn quản lý nhân sự, Creator, đơn đăng ký, cấp đối tác và chi trả đang tự kiểm "có phải Admin không". Đổi sang hỏi người gác cổng chung, để quyền bị thu trên trang Phân quyền và tài khoản bị khoá đều bị chặn ngay ở đây.

**Cần thêm / còn thiếu:**
- Đã có: kiểm role + trạng thái trong 5 service; `IAccessPolicy` (Phần 1).
- Cần thêm: gọi policy với quyền tương ứng; nhánh Admin của `TokenQuotaService.GetStatusForChildAsync` kiểm `quota.manage` (Read).
- Còn thiếu / ngoài phạm vi: luồng phụ huynh/Creator (Phần 3).

**Files:** Modify `StaffAccountService`, `CreatorAccountService`, `CreatorApplicationService`, `CreatorTierService`, `CreatorPayoutService`, `TokenQuotaService`.

- [ ] Thay cổng; chạy test của từng service (message cũ giữ nguyên).

### Task 2: Service chưa kiểm → thêm cổng

**Việc này làm gì (nói dễ hiểu):** Tạo/sửa gói, cấu hình chung, đánh dấu đã thanh toán, xử lý ngoại lệ đối soát, điều chỉnh lượt và tạo/công bố báo cáo trước đây chỉ dựa vào bước chặn ở cửa API. Thêm kiểm tra trong service để Admin bị khoá không thao tác được dù phiên đăng nhập còn hạn.

**Cần thêm / còn thiếu:**
- Đã có: endpoint `[HasPermission]`, audit log.
- Cần thêm: `EnsureAsync` đầu mỗi thao tác ghi.
- Còn thiếu / ngoài phạm vi: thao tác đọc không nhận id người gọi.

**Files:** Modify `PaymentService`, `PaymentReconciliationService`, `PlanSettingsService`, `TokenQuotaService.AdjustAsync`, `BusinessReportService`; Test: `tests/StoryPlatform.UnitTests/Features/AccessControl/AdminActionAccessTests.cs`.

- [ ] Test thất bại: Admin `LockedUntil` tương lai bị 403 ở `MarkPaidManuallyAsync`, `ResolveAsync`, `UpdateAsync` (cấu hình), `AdjustAsync`, `GenerateAsync`; Moderator bị 403.
- [ ] Cài đặt; test pass; sửa test cũ thiếu tài khoản Admin trong dữ liệu giả.

### Task 3: Phạm vi `Own` trong danh mục + kiểm toàn bộ

**Việc này làm gì (nói dễ hiểu):** Trang Phân quyền ghi rõ các quyền phụ huynh/Creator chỉ áp trên dữ liệu của chính họ.

**Cần thêm / còn thiếu:**
- Đã có: `AccessScope`, cột Phạm vi ở frontend.
- Cần thêm: đánh dấu `Own` cho 7 quyền ở spec mục 2.

**Files:** Modify `PermissionCatalog.cs`; Test `AccessPolicyTests.Catalog_*`.

- [ ] `dotnet test StoryPlatform.slnx` pass; snapshot endpoint không đổi.
