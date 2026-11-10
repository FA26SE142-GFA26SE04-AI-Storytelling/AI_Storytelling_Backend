# Gói và thanh toán cho phụ huynh — Design Spec (Luồng 2, đợt 1)

Ngày: 2026-11-10
Nguồn spec: `Luồng 2 – Mua gói và quota.docx` (bản cập nhật 09/11/2026), các mục 2a, 2b, 2d (một phần), 2e, 2g, 2h (một phần).
Phạm vi repo: `AI_Storytelling_Backend` (Core API) và `AI_Storytelling_Frontend` (web). Repo Mobile đang trống, không làm.

## 1. Mục đích

Code hiện tại bán một gói duy nhất: **Personal – 49.000 đ – +50 lượt, mua một lần**. Tài liệu Luồng 2 đã chốt mô
hình mới gồm gói tháng và lượt theo chu kỳ. Đợt 1 làm đủ để một phụ huynh **mua gói và dùng được gói**:

| Gói | Giá | Quyền lợi liên quan đợt 1 |
|---|---|---|
| Miễn phí | 0 đ | 1 lượt AI dùng thử, cấp một lần, không hết hạn |
| Gói Đọc | 49.000 đ / tháng | Không có lượt AI |
| Gói Sáng tạo | 99.000 đ / tháng | 4 lượt AI mỗi chu kỳ, không cộng dồn |
| Add-on | 29.000 đ | +2 lượt AI, chỉ mua khi đang có gói, hết hạn cùng chu kỳ |

## 2. Phạm vi

### Làm ở đợt 1

- UC-L2-01 Xem gói đang bán (phần phụ huynh).
- UC-L2-02 Đăng ký / gia hạn Gói Đọc, Gói Sáng tạo, gồm nâng gói và hạ gói.
- UC-L2-03 Mua Add-on.
- UC-L2-05, UC-L2-07 Webhook và mark-paid áp gói theo loại (thay cho cộng `QuotaAmount`).
- UC-L2-11 Chuyển chu kỳ, hết hạn gói, nhắc gia hạn trước 3 ngày (phần phụ huynh).
- UC-L2-12 `GET tokenquota/me` và lịch sử giao dịch của phụ huynh.
- Sổ lượt `credit_lots` thay cho `token_quota_configs`; lượt sắp hết hạn dùng trước.
- Cấp 1 lượt dùng thử (xem mục 3, quyết định D1).

### Không làm ở đợt 1

- Gói Creator, hạn mức tháng và lượt gửi duyệt của Creator (UC-L2-04, UC-L2-10, 2f). Creator vẫn bỏ qua quota như hiện tại.
- Giữ – trừ – trả lượt (`credit_holds`). Lượt vẫn bị trừ ngay khi tạo yêu cầu AI như hiện tại.
- Admin cấp / điều chỉnh lượt có lý do (UC-L2-08, `credit_adjustments`).
- Đối soát giao dịch Paid nhưng áp gói lỗi (UC-L2-13, `payment_reconciliation_exceptions`). Vẫn ghi audit `PaymentCreditFailed` như hiện tại.
- Giới hạn theo gói ở Luồng 1 và Luồng 4: số hồ sơ trẻ, 3 truyện thư viện mỗi tuần, sao chép bộ truyện mẫu, đọc to, tiến độ.
- Giới hạn trợ lý viết AI theo gói.
- Cảnh báo `delete-preview` khi xoá tài khoản còn gói.
- Quản lý gói của Admin, báo cáo doanh thu, hoàn tiền.

### Điều không được xảy ra

- Một giao dịch Paid áp gói hoặc cộng lượt hai lần (webhook gửi lại, mark-paid sau webhook).
- Gói hết hạn mà phụ huynh vẫn dùng được lượt chu kỳ hoặc lượt Add-on.
- Phụ huynh xem được giao dịch của người khác.

## 3. Quyết định thiết kế

Người dùng đã chốt:

| # | Quyết định |
|---|---|
| U1 | Chia hai đợt; đợt 1 chỉ gồm phần phụ huynh như mục 2. |
| U2 | Đợt 1 giữ cách trừ lượt ngay khi gửi yêu cầu AI; chỉ đổi nguồn lượt sang `credit_lots`. |
| U3 | Worker chuyển chu kỳ, hết hạn gói và nhắc gia hạn làm trong đợt 1. |

Do người viết thiết kế tự quyết (cần người dùng xem lại):

| # | Quyết định | Lý do |
|---|---|---|
| D1 | Cấp 1 lượt dùng thử ngay trong đợt 1, dù mục "giới hạn Miễn phí" để đợt sau. Cấp theo kiểu lười: lần đầu hệ thống tính lượt cho một tài khoản User có ít nhất một hồ sơ trẻ Active mà chưa có lô `Trial` thì tạo lô. | Hiện tại tài khoản chưa có config là **không giới hạn** (fail-open). Sang sổ lượt, không có lô nghĩa là 0 lượt. Thiếu lượt dùng thử thì tài khoản Miễn phí không tạo được truyện AI nào. |
| D2 | Gói Personal cũ (Id 1) giữ lại, chuyển `IsActive = false`, `PlanType = Legacy`. Ba gói mới dùng Id 2, 3, 4. | Giao dịch cũ vẫn hiện đúng tên gói trong lịch sử. |
| D3 | Lượt còn dư của mô hình cũ (`QuotaLimit − QuotaUsed` của config `Personal`) chuyển thành lô `Legacy`, **không hết hạn**. Config scope `System` không chuyển. | Tài liệu yêu cầu chuyển phần dư thành lô Legacy và đề xuất bỏ scope System; không nêu hạn dùng. Người đã trả tiền không nên mất lượt. |
| D4 | Bảng `token_quota_configs` giữ nguyên dữ liệu nhưng không còn được đọc. Hai endpoint Admin `POST/GET tokenquota/config` và mục cấu hình quota ở màn Admin bị gỡ. Admin tạm thời không cấp lượt riêng được cho tới đợt 2 (UC-L2-08). | Để lại một nút cấu hình không còn tác dụng sẽ gây hiểu nhầm. Không drop bảng vì quy tắc an toàn CSDL. |
| D5 | `GET tokenquota/child/{id}` giữ lại, trả lượt của tài khoản chủ hồ sơ theo đúng dạng DTO cũ. | Tài liệu yêu cầu giữ tạm khi chuyển đổi; màn Trang chủ và Studio đang dùng. |
| D6 | Add-on được thanh toán khi gói đã hết hạn (gói hết trong 15 phút chờ, hoặc Admin mark-paid muộn): vẫn cộng 2 lượt, hết hạn sau 1 tháng kể từ `PaidAt`. | Tài liệu không nêu. Khách đã trả tiền thì nhận được lượt. |
| D7 | Nâng Gói Đọc lên Gói Sáng tạo khi đang có dòng `Scheduled` (đã gia hạn sớm trước đó): các dòng `Scheduled` được dời nối tiếp sau chu kỳ Gói Sáng tạo mới. | Tài liệu không nêu. Không làm mất chu kỳ đã trả tiền. |
| D8 | Mỗi tài khoản chỉ có 1 giao dịch Add-on Pending cùng lúc (giữ quy tắc "không trùng Pending cùng gói" đang có). | Tài liệu chỉ nói Add-on tính riêng với giao dịch gói. |
| D9 | Chu kỳ 1 tháng tính bằng `AddMonths(1)` trên giờ UTC. | Nhất quán với code hiện tại. |
| D10 | Thêm `NotificationType.SubscriptionExpiring = 32` cho thông báo nhắc gia hạn. Mỗi chu kỳ nhắc một lần. | Chưa có loại thông báo phù hợp. |
| D11 | Xoá tài khoản: huỷ luôn các dòng `user_subscriptions` chưa kết thúc và các lô lượt còn dư (soft delete) trong cùng transaction xoá. | Tài liệu yêu cầu huỷ chu kỳ và lô lượt; phần cảnh báo trước để đợt sau. |
| D12 | Không thêm cột `Audience`, `SubmitCredits`, `MaxChildProfiles`, `Benefits` ở đợt 1. | Chưa có code nào đọc; thêm khi làm Gói Creator và giới hạn theo gói. |

## 4. Mô hình dữ liệu

### 4.1 Enum mới (`StoryPlatform.Domain/Enums`)

- `PlanType`: `Legacy = 0`, `Reading = 1`, `Creative = 2`, `AddOn = 3`. (`CreatorPack` thêm ở đợt Creator.)
- `SubscriptionStatus`: `Scheduled = 1`, `Active = 2`, `Superseded = 3`, `Expired = 4`.
- `CreditType`: `AI = 1`. (`Submit` thêm ở đợt Creator.)
- `CreditSource`: `Trial = 1`, `Cycle = 2`, `AddOn = 3`, `Legacy = 4`.

Enum lưu dạng chuỗi như các bảng hiện có.

### 4.2 `subscription_plans` (sửa)

- Thêm `PlanType` (bắt buộc, mặc định `Legacy` cho dòng cũ).
- Đổi tên cột `QuotaAmount` → `AiCredits` (rename, không mất dữ liệu).
- Seed:

| Id | Name | PlanType | PriceVnd | AiCredits | IsActive |
|---|---|---|---|---|---|
| 1 | Personal | Legacy | 49000 | 50 | false |
| 2 | Gói Đọc | Reading | 49000 | 0 | true |
| 3 | Gói Sáng tạo | Creative | 99000 | 4 | true |
| 4 | Add-on | AddOn | 29000 | 2 | true |

### 4.3 `user_subscriptions` (mới)

`UserId`, `PlanId`, `StartAt`, `EndAt`, `Status`, `PaymentId`, `RenewalReminderSentAt` (nullable).

- `PaymentId` unique: mỗi giao dịch tạo tối đa một chu kỳ.
- Index `(UserId, Status)`.
- Gói hiện tại của một tài khoản = dòng `Active` có `EndAt > now`.

### 4.4 `credit_lots` (mới)

`UserId`, `CreditType`, `Source`, `Total`, `Remaining`, `ExpiresAt` (nullable), `PaymentId` (nullable).

- Unique có điều kiện trên `PaymentId` khi khác null: lô gắn giao dịch chỉ cấp một lần.
- Unique có điều kiện trên `UserId` khi `Source = Trial`: mỗi tài khoản một lô dùng thử.
- Index `(UserId, CreditType)`.
- Lô còn hạn = `Remaining > 0` và (`ExpiresAt` null hoặc `ExpiresAt > now`). Hết hạn được xét khi đọc, không cần worker xoá lô.

### 4.5 `payment_transactions`

Không đổi cấu trúc.

### 4.6 Migration

Một migration `AddSubscriptionCyclesAndCreditLots`:

1. Sửa `subscription_plans`, cập nhật seed.
2. Tạo `user_subscriptions`, `credit_lots`.
3. Chuyển dữ liệu: với mỗi `token_quota_configs` scope `Personal` chưa xoá và còn dư, chèn một lô `Legacy` (`Total = Remaining = QuotaLimit − QuotaUsed`, `ExpiresAt` null).
4. Không drop `token_quota_configs`.

`Down` xoá hai bảng mới và hoàn nguyên cột; không khôi phục dữ liệu lượt đã dùng sau khi chuyển.

## 5. Hành vi backend

### 5.1 Xem gói — `GET payment/plans`

Trả các gói `IsActive` có `PlanType` khác `Legacy`. Mỗi gói thêm `planType`, `aiCredits`, `isPurchasable`,
`lockedReason`. Add-on có `isPurchasable = false` và lời nhắc đăng ký gói trước khi người gọi chưa có gói còn hiệu lực.
Bỏ `quotaAmount` khỏi DTO (frontend đổi theo).

### 5.2 Tạo giao dịch — `POST payment/transactions`

Giữ: mã `SEPAY` + 10 ký tự, QR, Pending, hết hạn 15 phút, chỉ role `User`, gói phải `IsActive`.

Thêm:

- Gói `Legacy` không mua được (400).
- Gói `Reading` / `Creative`: từ chối (409) nếu tài khoản đã có một giao dịch Pending của `Reading` hoặc `Creative`.
- Gói `AddOn`: từ chối (409) nếu không có gói còn hiệu lực; từ chối (409) nếu đã có giao dịch Add-on Pending.

### 5.3 Áp gói khi Paid

Thay `CreditQuotaSafelyAsync` bằng một bước áp gói dùng chung cho webhook và mark-paid. Vẫn giữ nguyên tắc hiện có:
giao dịch đã Paid thì không hoàn tác; lỗi áp gói chỉ ghi audit `PaymentCreditFailed`.

Bước áp gói chạy trong một DB transaction, khoá theo tài khoản, và **idempotent theo `PaymentId`**: nếu đã có
`user_subscriptions` hoặc `credit_lots` gắn `PaymentId` này thì không làm gì.

Trước khi áp, chạy bước "cập nhật chu kỳ" (5.5) cho tài khoản đó để trạng thái chu kỳ đúng với thời điểm hiện tại.

**Gói Đọc / Gói Sáng tạo**

| Tình huống lúc Paid | Kết quả |
|---|---|
| Không có gói còn hiệu lực | Dòng `Active` mới, `StartAt = PaidAt`, `EndAt = PaidAt + 1 tháng`. Gói Sáng tạo: lô `Cycle` 4 lượt, `ExpiresAt = EndAt`. |
| Đang có Gói Đọc, mua Gói Sáng tạo (nâng gói) | Dòng Gói Đọc → `Superseded`. Dòng Gói Sáng tạo `Active` mới từ `PaidAt`, 1 tháng, lô `Cycle` 4 lượt. Các lô `AddOn` còn hạn: `ExpiresAt` = `EndAt` mới. Các dòng `Scheduled` có sẵn dời nối tiếp sau chu kỳ mới (D7). |
| Mọi trường hợp còn lại khi đang có gói (gia hạn cùng gói, hạ Gói Sáng tạo xuống Gói Đọc, mua Gói Sáng tạo khi đang Gói Sáng tạo) | Dòng `Scheduled` mới, `StartAt` = `EndAt` của dòng cuối chuỗi (Active hoặc Scheduled muộn nhất), dài 1 tháng. Chưa cấp lượt; lượt chu kỳ hiện tại không đổi. |

**Add-on**

- Có gói còn hiệu lực: lô `AddOn` 2 lượt, `ExpiresAt` = `EndAt` của chu kỳ Active. Ngày kết thúc gói không đổi.
- Không còn gói (D6): lô `AddOn` 2 lượt, `ExpiresAt = PaidAt + 1 tháng`.

**Legacy** (giao dịch Pending cũ được Admin mark-paid): lô `Legacy` bằng `AiCredits` của gói, không hết hạn.

Số lượt lấy từ `AiCredits` của gói, không viết cứng trong code.

### 5.4 Dùng lượt AI

`ITokenQuotaService` giữ hai lời gọi mà `AIStoryInputService` đang dùng, đổi phần ruột:

- `EnsureWithinQuotaAsync(childProfileId)`: tìm chủ hồ sơ, cấp lượt dùng thử nếu đủ điều kiện (D1), tính tổng
  `Remaining` của các lô AI còn hạn. Bằng 0 → `ConflictException` (409) kèm gợi ý nâng gói hoặc mua Add-on.
- `IncrementUsageAsync(childProfileId)`: trừ 1 từ lô còn hạn có `ExpiresAt` sớm nhất (lô không hết hạn xếp cuối).
  Chạy trong transaction sẵn có của `SubmitAsync`, khoá theo tài khoản để hai yêu cầu đồng thời không cùng tiêu một lượt.
  Nếu không còn lô nào → 409.

Creator vẫn không bị kiểm tra (không đổi).

### 5.5 Chu kỳ và nhắc gia hạn

Thêm một service "cập nhật chu kỳ" và một worker nền gọi nó định kỳ (mặc định 15 phút, cấu hình được, theo mẫu
`PaymentExpirySweepWorker`).

Với mỗi tài khoản có chu kỳ đến hạn:

1. Dòng `Active` có `EndAt <= now` → `Expired`.
2. Dòng `Scheduled` có `StartAt <= now` và tài khoản không còn dòng `Active` → `Active`. Gói Sáng tạo: cấp lô `Cycle`
   theo `AiCredits`, `ExpiresAt = EndAt`, gắn `PaymentId` của dòng đó (idempotent).
3. Lượt chu kỳ và Add-on của chu kỳ cũ tự hết hạn theo `ExpiresAt`; không chuyển sang chu kỳ mới.

Không gia hạn → không còn dòng `Active` → tài khoản ở mức Miễn phí. Truyện đã tạo không bị ảnh hưởng.

Nhắc gia hạn: dòng `Active` còn không quá 3 ngày, tài khoản chưa có dòng `Scheduled`, `RenewalReminderSentAt` còn null
→ tạo thông báo `SubscriptionExpiring` (payload: `subscriptionId`, `planName`, `endAt`) và ghi `RenewalReminderSentAt`.

Vì mọi phép đọc đều xét `EndAt > now` và `ExpiresAt > now`, worker chạy trễ không làm phụ huynh dùng quá hạn.

### 5.6 Xem lượt — `GET tokenquota/me`

Chỉ role `User`. Trả:

- `plan`: `planType`, `planName`, `startAt`, `endAt`; null khi ở mức Miễn phí.
- `nextPlan`: gói `Scheduled` gần nhất (`planType`, `planName`, `startAt`, `endAt`); null nếu không có.
- `trialCredits`, `cycleCredits`, `addOnCredits`, `otherCredits` (lô Legacy), `totalRemaining`.

`GET tokenquota/child/{id}` (D5): giữ kiểm tra quyền hiện có; trả `IsUnlimited = false`, `Remaining` và `QuotaLimit`
bằng tổng lượt còn lại của chủ hồ sơ, `QuotaUsed = 0`, `PeriodEnd` = ngày kết thúc gói (nếu có).

### 5.7 Lịch sử giao dịch — `GET payment/transactions/me`

Giao dịch của chính người gọi, mới nhất trước, có phân trang theo mẫu phân trang hiện có của dự án. Mỗi dòng: tên gói,
loại gói, số tiền, mã, trạng thái, thời điểm tạo, `expiresAt`, `paidAt`. `qrCodeUrl` chỉ trả khi giao dịch Pending còn hạn.
Giao dịch Pending đã quá hạn nhưng worker chưa quét được hiển thị là `Expired`.

Màn hình QR dùng chính endpoint này để hỏi lại trạng thái trong lúc chờ chuyển khoản.

### 5.8 Xoá tài khoản (D11)

Trong transaction của `AccountDeletionService`: các dòng `user_subscriptions` `Active` / `Scheduled` → `Expired` và soft
delete; các lô `credit_lots` soft delete. Giao dịch thanh toán giữ nguyên như hiện tại.

### 5.9 Gỡ bỏ (D4)

`POST tokenquota/config`, `GET tokenquota/config`, `SetConfigAsync`, `ListConfigsAsync`, `CreditAsync`, logic rollover và
DTO liên quan. Entity và bảng `token_quota_configs` giữ lại.

## 6. Frontend (web)

Màn **Tài khoản → Thanh toán** (`features/account/PaymentSection.tsx`) làm lại, tách thành các phần nhỏ theo quy ước
~250 dòng mỗi file:

1. **Gói của tôi**: gói hiện tại và ngày kết thúc, hoặc "Miễn phí"; gói kế tiếp nếu đã gia hạn sớm; số lượt tách theo
   dùng thử, chu kỳ, Add-on.
2. **Chọn gói**: ba thẻ Gói Đọc, Gói Sáng tạo, Add-on với giá và quyền lợi. Add-on bị khoá kèm lời nhắc khi chưa có gói.
   Nhãn nút theo ngữ cảnh: Đăng ký, Gia hạn, Nâng gói, Chuyển sang Gói Đọc.
3. **Xác nhận** trước khi tạo giao dịch, nêu rõ hệ quả:
   - Nâng gói: "Những ngày Gói Đọc còn lại không được quy đổi."
   - Gia hạn sớm / hạ gói: gói mới bắt đầu sau ngày kết thúc hiện tại.
4. **Thanh toán**: ảnh QR, số tiền, nội dung chuyển khoản, đếm ngược 15 phút. Tự hỏi lại trạng thái vài giây một lần;
   khi Paid thì báo thành công và tải lại "Gói của tôi"; khi hết hạn thì cho tạo giao dịch mới.
5. **Lịch sử giao dịch**: danh sách mới nhất trước. Pending còn hạn có nút mở lại QR và đếm ngược. `MismatchAmount`
   hiện "Đang chờ quản trị viên xử lý, bạn không cần chuyển lại."

Thay đổi khác:

- `services/paymentService.ts`: thêm lịch sử giao dịch và `tokenquota/me`.
- `types/backendContracts.ts`: cập nhật `SubscriptionPlanDto`, thêm DTO mới.
- `lib/notifications.ts`: hiển thị thông báo `SubscriptionExpiring`.
- Màn Admin (`features/staff/AdminSystem.tsx`, `services/staffService.ts`): gỡ mục cấu hình Token Quota (D4).
- Trang chủ và Studio tiếp tục dùng `tokenquota/child/{id}`, không đổi.

Giao diện dùng biến màu trong `styles/tokens.css`, kiểm tra cả theme sáng và tối. Không thêm mock vào `app/`.

## 7. Kiểm thử

Backend (xUnit, thư mục test hiện có):

- Áp gói: từng dòng của bảng 5.3, gồm các tiêu chí nghiệm thu UC-L2-02 AC1–AC4 và UC-L2-03 AC1–AC3.
- Idempotent: webhook gửi lại, mark-paid sau khi đã áp (UC-L2-05 AC4).
- Tạo giao dịch: quy tắc Pending, Add-on khi không có gói, gói Legacy.
- Sổ lượt: thứ tự dùng lô, lô hết hạn không tính, hết lượt → 409, cấp lượt dùng thử đúng một lần.
- Chu kỳ: hết hạn, kích hoạt dòng Scheduled và cấp lượt, lượt Add-on cũ không chuyển sang (UC-L2-11 AC1–AC3), nhắc một lần.
- Lịch sử: chỉ thấy giao dịch của mình (UC-L2-12 AC1), QR chỉ có khi Pending còn hạn.
- Cập nhật các test đang có của `PaymentService`, `TokenQuotaService`, `AIStoryInputService`, `AccountDeletionService`
  và test phân quyền controller theo hành vi mới; gỡ test của các endpoint bị gỡ.

Frontend (Vitest): service gọi đúng endpoint; logic thuần cho nhãn nút, lời xác nhận và đếm ngược.

Kiểm tra tay: chạy backend cục bộ, giả lập webhook SePay bằng request có header xác thực của môi trường dev, đi hết luồng
mua Gói Sáng tạo → dùng lượt → mua Add-on trên web.

## 8. Để người làm tự quyết

- Tên lớp, cách tách service (ví dụ một `SubscriptionService` riêng hay nằm trong `PaymentService`), miễn theo cấu trúc
  `Features/<tên>/{Interfaces,Services,DTOs}` hiện có.
- Cách khoá theo tài khoản (advisory lock sẵn có trong `IUnitOfWork` hoặc tương đương).
- Bố cục và chữ trên màn hình, trong khuôn khổ mục 6.
- Chu kỳ hỏi lại trạng thái giao dịch ở frontend.

## 9. Việc của các đợt sau

Gói Creator và lượt gửi duyệt; `credit_holds`; UC-L2-08; UC-L2-13; giới hạn theo gói ở Luồng 1 và Luồng 4; cảnh báo khi
xoá tài khoản còn gói; quản lý gói và báo cáo doanh thu của Admin.
