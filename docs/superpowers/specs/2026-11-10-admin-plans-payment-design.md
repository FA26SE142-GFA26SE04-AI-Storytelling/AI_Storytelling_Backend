# Admin: giao dịch, lượt, đối soát và gói — Design Spec (Luồng 2 + Luồng 5 mục 5a, backend)

Ngày: 2026-11-10
Nguồn spec: `Luồng 2 – Mua gói và quota.docx` (UC-L2-07, UC-L2-08, UC-L2-13) và
`Luồng 5 – Vận hành, kiểm duyệt, quản trị và thống kê.docx` (mục 5a, bước 1, 3, 4, 6).
Nối tiếp: `2026-11-10-parent-plans-payment-design.md` (đợt 1, đã implement trên nhánh `feat/parent-plans-payment`).
Phạm vi repo: chỉ `AI_Storytelling_Backend`. Giao diện Admin trên web làm sau.

## 1. Mục đích

Ngân hàng đang trục trặc nên SePay chưa gọi webhook về được: phụ huynh tạo giao dịch nhưng gói không tự kích hoạt.
Đợt này cho Admin đủ công cụ để vận hành và demo mà không cần tiền về thật, đồng thời hoàn tất phần việc của Admin
trong Luồng 2:

1. Xem toàn bộ giao dịch và xác nhận tay giao dịch đang chờ.
2. Cấp hoặc trừ lượt cho một tài khoản, có lý do và lịch sử (UC-L2-08).
3. Xử lý giao dịch đã Paid nhưng áp gói lỗi (UC-L2-13).
4. Xem, sửa và bật / tắt bán gói (5a, phần cơ bản).

## 2. Phạm vi

### Làm

| # | Việc | Use case |
|---|---|---|
| A | Danh sách toàn bộ giao dịch cho Admin, lọc và tìm | UC-L2-07 (mở rộng) |
| B | Cấp / trừ lượt cho một tài khoản phụ huynh; lịch sử điều chỉnh; xem gói và lượt của một tài khoản | UC-L2-08 |
| C | Job đối soát tạo ngoại lệ; Admin xem và xử lý ngoại lệ | UC-L2-13 |
| D | Admin xem mọi gói, sửa tên / giá / số lượt, bật / tắt bán | 5a bước 1, 3, 4, 6 |

### Không làm

- Giao diện Admin trên web.
- Tạo gói mới (5a bước 2): các loại gói hiện cố định (Gói Đọc, Gói Sáng tạo, Add-on); tạo gói có ý nghĩa khi có Gói
  Creator và cột quyền lợi.
- Gói Creator, lượt gửi duyệt, điều chỉnh lượt cho Creator (scope Creator của UC-L2-08).
- Cấu hình tỷ lệ quỹ Creator, hạn mức Creator, lượt dùng thử, giới hạn sinh lại (5a bước 5).
- Thống kê tài khoản, doanh thu, Creator (5c–5e); duyệt đơn Creator (M5).
- Hoàn tiền.

### Điều không được xảy ra

- Xử lý một ngoại lệ làm cộng lượt hoặc tạo chu kỳ lần hai.
- Trừ lượt làm số lượt của tài khoản thành âm.
- Sửa gói làm đổi số tiền hoặc số lượt của giao dịch đã tạo trước đó.
- Người không phải Administrator gọi được bất kỳ endpoint nào của đợt này.

## 3. Quyết định thiết kế

Người dùng đã chốt:

| # | Quyết định |
|---|---|
| U1 | Tập trung phần Admin ở backend trước; thanh toán thật và giao diện để sau. |

Do người viết thiết kế tự quyết (cần người dùng xem lại):

| # | Quyết định | Lý do |
|---|---|---|
| D1 | Phạm vi "Admin" của đợt này là bốn việc A–D, đều xoay quanh gói và thanh toán. Các nhóm Admin khác (duyệt Creator, thống kê) để đợt sau. | Đây là phần Admin bị chặn bởi sự cố ngân hàng và nối trực tiếp với đợt 1. |
| D2 | Admin xác nhận tay một giao dịch Pending vẫn dùng `mark-paid` sẵn có; không thêm chức năng "tặng gói" không qua giao dịch. | Giữ một đường áp gói duy nhất; doanh thu và lịch sử vẫn khớp với giao dịch. |
| D3 | Cấp lượt (+) tạo một lô mới nguồn `AdminAdjust`, không hết hạn. Trừ lượt (−) lấy từ các lô còn hạn, lô sắp hết hạn trước. | Tài liệu không nêu hạn dùng của lượt Admin cấp. Lượt bù lỗi không nên tự mất. |
| D4 | Chỉ điều chỉnh được tài khoản role `User`. Tài khoản khác → 400. | Scope Creator thuộc đợt Gói Creator. |
| D5 | Lượt `AdminAdjust` hiển thị chung trong `otherCredits` của `GET tokenquota/me` (cùng lượt Legacy). | Không đổi hợp đồng API đợt 1. |
| D6 | Job đối soát chỉ xét giao dịch Paid đã quá 5 phút và bỏ qua tài khoản đã xoá. | Tránh báo nhầm giao dịch đang được áp gói; chu kỳ và lô của tài khoản đã xoá bị huỷ có chủ đích. |
| D7 | Job đối soát chạy chung trong `SubscriptionCycleWorker` (mỗi 15 phút), sau bước chuyển chu kỳ. | Không thêm worker mới. |
| D8 | Số lượt của một giao dịch được chốt lúc tạo: thêm cột `AiCredits` (nullable) vào `payment_transactions`. Áp gói dùng số đã chốt; giao dịch cũ (null) dùng số của gói. | Luồng 5: sửa gói chỉ áp dụng cho giao dịch tạo sau đó. Số tiền đã được chốt sẵn ở `Amount`. |
| D9 | Không cho bật bán gói `Legacy`; không cho đổi `PlanType` của gói. | Gói Personal đã ngừng theo đợt 1; đổi loại gói làm sai nghĩa các chu kỳ đang chạy. |
| D10 | Endpoint Admin đặt trong `PaymentController` và `TokenQuotaController` sẵn có, dưới nhánh `admin/...`. | Theo cách `mismatched` và `mark-paid` đang làm; không thêm controller. |

## 4. Mô hình dữ liệu

Một migration `AddCreditAdjustmentsAndReconciliationExceptions`.

- `CreditSource`: thêm `AdminAdjust = 5`.
- Enum mới `ReconciliationStatus`: `Open = 1`, `Resolved = 2`.
- `payment_transactions`: thêm `AiCredits` (int, nullable).
- `credit_adjustments` (mới): `UserId`, `Amount` (dương = cấp, âm = trừ, khác 0), `Reason` (bắt buộc, tối đa 500),
  `AdminId`. FK `Restrict` tới `user_accounts` cho cả hai. Index `UserId`.
- `payment_reconciliation_exceptions` (mới): `PaymentId` (unique), `Status`, `Reason` (tối đa 500), `ResolvedBy`
  (nullable), `ResolvedAt` (nullable). FK `Restrict`. Index `Status`.

Thêm file seed và cập nhật `.dbml` cho hai bảng mới (bắt buộc bởi `SeedDataAlignmentTests`).

## 5. Hành vi

Mọi endpoint dưới đây: `[Authorize(Roles = "Administrator")]`.

### A. Danh sách giao dịch — `GET payment/admin/transactions`

Query: `pageIndex` (mặc định 1), `pageSize` (mặc định 20, tối đa 100), `status` (tuỳ chọn), `search` (tuỳ chọn: khớp
một phần mã giao dịch, email hoặc tên đăng nhập của người mua, không phân biệt hoa thường). Mới nhất trước.

Mỗi dòng gồm các trường của `PaymentTransactionDto` cộng `payerUserId`, `payerEmail`, `payerName`.
Khác với lịch sử của phụ huynh: `status` trả đúng giá trị lưu (không tự đổi Pending quá hạn thành Expired) để Admin
thấy trạng thái thật trước khi `mark-paid`. `status` không hợp lệ → 400.

`mark-paid` giữ nguyên: nhận mọi giao dịch chưa Paid.

### B. Điều chỉnh lượt

- `POST tokenquota/admin/adjustments` — body `{ userId, amount, reason }`.
  - `reason` rỗng → 400. `amount = 0` → 400. Không tìm thấy tài khoản → 404. Tài khoản không phải role `User` → 400.
  - `amount > 0`: tạo lô `AdminAdjust` (D3).
  - `amount < 0`: nếu vượt tổng lượt còn hạn → 400, không đổi gì; ngược lại trừ dần từ các lô (D3).
  - Chạy trong transaction, khoá theo tài khoản. Ghi một dòng `credit_adjustments` và audit `CreditAdjusted`
    (trước / sau: tổng lượt, kèm lý do).
  - Trả `CreditAdjustmentDto`: `id`, `userId`, `amount`, `reason`, `adminId`, `adminName`, `createdAt`,
    `totalRemainingAfter`.
- `GET tokenquota/admin/adjustments?userId=&pageIndex=&pageSize=` — lịch sử, mới nhất trước; `userId` tuỳ chọn.
- `GET tokenquota/admin/users/{userId}` — gói và lượt của một tài khoản, cùng dạng `MyQuotaDto`. Không cấp lượt dùng
  thử khi Admin xem.

### C. Đối soát

Job (D6, D7) tìm giao dịch `Paid`, `PaidAt` đã quá 5 phút, người mua chưa bị xoá, chưa có ngoại lệ, và **chưa được áp gói**:

- Gói Đọc / Gói Sáng tạo: không có dòng `user_subscriptions` gắn `PaymentId`.
- Add-on / Legacy với số lượt > 0: không có lô `credit_lots` gắn `PaymentId`.

Mỗi giao dịch như vậy → một ngoại lệ `Open`. `Reason` lấy từ audit `PaymentCreditFailed` gần nhất của giao dịch nếu
có, nếu không là `"NotApplied"`. Job không tự áp gói.

- `GET payment/admin/reconciliation?status=Open&pageIndex=&pageSize=` — mặc định `Open`. Mỗi dòng: `id`, `status`,
  `reason`, `createdAt`, `resolvedAt`, `resolvedByName`, và giao dịch (mã, người mua, gói, số tiền, `paidAt`).
- `POST payment/admin/reconciliation/{id}/resolve`
  - Ngoại lệ đã `Resolved` → 409.
  - Gọi lại đúng bước áp gói của webhook (`ApplyPaidTransactionAsync`, idempotent). Thành công, hoặc gói đã được áp
    từ trước → `Resolved`, ghi `ResolvedBy`, `ResolvedAt`, audit `PaymentReconciliationResolved`.
  - Áp gói vẫn lỗi → ngoại lệ giữ `Open`, cập nhật `Reason`, trả 409 kèm thông báo.

### D. Quản lý gói

- `GET payment/admin/plans` — mọi gói, gồm gói tắt bán và `Legacy`: `id`, `name`, `planType`, `priceVnd`, `aiCredits`, `isActive`.
- `PUT payment/admin/plans/{id}` — body `{ name, priceVnd, aiCredits, isActive }`.
  - `name` rỗng hoặc quá 150 ký tự → 400; `priceVnd <= 0` → 400; `aiCredits < 0` → 400.
  - Gói `Legacy` với `isActive = true` → 400 (D9).
  - Ghi audit `SubscriptionPlanUpdated` với trạng thái trước / sau.
- Giao dịch tạo trước khi sửa giữ `Amount` và `AiCredits` đã chốt (D8). Chu kỳ `Scheduled` khi kích hoạt cũng dùng số
  lượt đã chốt trên giao dịch của nó.

## 6. Kiểm thử

- Unit test theo mẫu đợt 1 (`InMemoryUnitOfWork`): từng nhánh ở mục 5, gồm tiêu chí UC-L2-08 AC1–AC2 và
  UC-L2-13 AC1–AC3.
- Test phân quyền controller: mọi action mới chỉ cho `Administrator`.
- Integration test qua HTTP: phụ huynh tạo giao dịch → Admin tìm trong danh sách → `mark-paid` → gói kích hoạt;
  Admin cấp 3 lượt → phụ huynh thấy thêm 3; Admin sửa giá gói → giao dịch cũ giữ giá cũ, giao dịch mới theo giá mới.
- PostgreSQL (opt-in): migration áp được trên database đã có dữ liệu đợt 1.

## 7. Để người làm tự quyết

Tên lớp và cách tách service; dạng DTO chi tiết trong khuôn khổ mục 5; cách ghép job đối soát vào worker.
