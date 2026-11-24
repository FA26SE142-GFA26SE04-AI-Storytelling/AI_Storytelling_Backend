# Permission có phạm vi và điều kiện — Phần 2: Luồng 2 + Luồng 5 phần II (quản trị) — Thiết kế ngắn

Ngày: 2026-11-24. Nối tiếp `2026-11-24-permission-policies-design.md` (Phần 1, đã deploy). User duyệt 24/11/2026.

## Mục đích
Luật đã chốt (Luồng 5, 09/11/2026): M5 và toàn bộ Phần II (nhân sự, duyệt Creator, gói, quỹ, hạn mức, báo cáo, thu hồi cấp đối tác) và đối soát Luồng 2 **chỉ Administrator đang hoạt động**. Đưa các cổng này về `IAccessPolicy` của Phần 1 để service đọc permission thật và chặn tài khoản bị khoá, thay cho kiểm role rải rác hoặc không kiểm.

## Hiện trạng
- Đã có, giữ nguyên: lý do bắt buộc (điều chỉnh lượt, huỷ chi trả, thu hồi cấp đối tác), mã giao dịch ngân hàng khi Đã chi, không tự vô hiệu hoá, không vô hiệu hoá quản trị viên cuối, ràng buộc gói (Add-on cần gói đang hoạt động; gói Creator chỉ cho Creator Active).
- Kiểm role trong service: `StaffAccountService`, `CreatorAccountService`, `CreatorApplicationService`, `CreatorTierService`, `CreatorPayoutService`, `TokenQuotaService.GetStatusForChildAsync` (Admin xem lượt mọi hồ sơ).
- Không kiểm trong service (chỉ endpoint): `PaymentService` (tạo/sửa gói, đánh dấu đã thanh toán), `PaymentReconciliationService.ResolveAsync`, `PlanSettingsService.UpdateAsync`, `TokenQuotaService.AdjustAsync`, `BusinessReportService` (tạo/công bố báo cáo).

## Phương án
| | **Dùng lại `IAccessPolicy` (chọn)** | Thêm điều kiện mới (ví dụ hai người duyệt) | Giữ như cũ |
|---|---|---|---|
| | Một cổng chung, đọc permission DB, chặn khoá | Chưa có trong luật đã chốt | Admin bị khoá vẫn thao tác tới khi hết phiên |

## Thiết kế
1. Các thao tác **ghi** có `adminUserId` gọi `EnsureAsync(admin, <quyền>, deniedMessage: <message cũ>)`:
   - `staff.account.manage`: `StaffAccountService`
   - `creator.account.manage`: `CreatorAccountService`
   - `creator.application.review`: `CreatorApplicationService`
   - `creator.tier.manage`: `CreatorTierService`
   - `creator.payout.manage`: `CreatorPayoutService`
   - `payment.manage`: `PaymentService` (tạo/sửa gói, đánh dấu đã thanh toán), `PaymentReconciliationService.ResolveAsync`, `PlanSettingsService.UpdateAsync`
   - `quota.manage`: `TokenQuotaService.AdjustAsync`; nhánh Admin của `GetStatusForChildAsync` (Read)
   - `report.business.view`: `BusinessReportService.GenerateAsync/PublishAsync`
2. Danh mục: phạm vi `Own` cho quyền chỉ xem dữ liệu của chính mình: `account.self.manage`, `quota.parent.view`, `creator.application.view_own`, `creator.rating.view_own`, `creator.community.manage`, `creator.payout.view_own`, `quota.creator.view`.
3. Không thêm điều kiện mới; ma trận endpoint không đổi.

## Không làm
- Thao tác **đọc** không nhận id người gọi (liệt kê giao dịch, gói, báo cáo, nhật ký): vẫn do `[HasPermission]` ở endpoint chặn; đổi chữ ký interface/controller không đáng cho Phần 2.
- Luồng 1, 3, 4: Phần 3.

## Kiểm chứng
- Test policy cho từng service ghi: Admin bị khoá (`LockedUntil` tương lai) → 403 message cũ; role thiếu quyền → 403.
- Toàn bộ test backend pass; snapshot endpoint không đổi.
