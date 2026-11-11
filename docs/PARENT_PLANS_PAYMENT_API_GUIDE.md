# Gói và thanh toán cho phụ huynh — Hướng dẫn tích hợp API (Luồng 2, đợt 1)

Cập nhật: 10/11/2026. Thiết kế: [2026-11-10-parent-plans-payment-design.md](superpowers/specs/2026-11-10-parent-plans-payment-design.md).
Cần áp migration `AddSubscriptionCyclesAndCreditLots` trên môi trường đích trước khi dùng.

Tài liệu này thay cho các mô tả `Payment` và `TokenQuota` trong
[API_FRONTEND_ACTOR_FLOWS.md](API_FRONTEND_ACTOR_FLOWS.md). Mọi response dùng envelope `ApiResponse` chung.

## 1. Mô hình gói

| Id | Tên | `planType` | Giá | `aiCredits` | Ghi chú |
|---|---|---|---|---|---|
| 1 | Personal | `Legacy` | 49.000 đ | 50 | Ngừng bán, chỉ còn trong lịch sử giao dịch cũ |
| 2 | Gói Đọc | `Reading` | 49.000 đ / tháng | 0 | Không có lượt AI |
| 3 | Gói Sáng tạo | `Creative` | 99.000 đ / tháng | 4 | Lượt cấp mỗi chu kỳ, không cộng dồn |
| 4 | Add-on | `AddOn` | 29.000 đ | 2 | Chỉ mua khi đang có gói; hết hạn cùng chu kỳ |

- Tài khoản không có gói còn hiệu lực ở mức **Miễn phí**. Tài khoản phụ huynh có hồ sơ trẻ Active đầu tiên được cấp
  **1 lượt dùng thử**, một lần duy nhất, không hết hạn.
- Chu kỳ dài 1 tháng tính từ lúc giao dịch Paid (UTC).
- Lượt AI được trừ ngay khi gửi yêu cầu tạo truyện (chưa có giữ – trả lượt). Lô sắp hết hạn được dùng trước.

## 2. Endpoint

### GET /api/v1/Payment/plans — đăng nhập

Trả các gói đang bán (không gồm `Legacy`).

```json
[
  { "id": 2, "name": "Gói Đọc", "planType": "Reading", "priceVnd": 49000, "aiCredits": 0, "isPurchasable": true, "lockedReason": null },
  { "id": 3, "name": "Gói Sáng tạo", "planType": "Creative", "priceVnd": 99000, "aiCredits": 4, "isPurchasable": true, "lockedReason": null },
  { "id": 4, "name": "Add-on", "planType": "AddOn", "priceVnd": 29000, "aiCredits": 2, "isPurchasable": false,
    "lockedReason": "Hãy đăng ký Gói Đọc hoặc Gói Sáng tạo trước khi mua Add-on." }
]
```

Thay đổi so với bản cũ: bỏ `quotaAmount`; thêm `planType`, `aiCredits`, `isPurchasable`, `lockedReason`.

### POST /api/v1/Payment/transactions — role `User`

Body `{ "planId": 3 }`. Trả 201 với `PaymentTransactionDto` (Pending, hết hạn sau 15 phút, có `qrCodeUrl`).

| Tình huống | HTTP |
|---|---|
| Gói không tồn tại | 404 |
| Gói ngừng bán hoặc là gói `Legacy` | 400 |
| Người gọi không phải role `User` | 403 |
| Mua Gói Đọc / Gói Sáng tạo khi đang có một giao dịch gói Pending còn hạn | 409 |
| Mua Add-on khi không có gói còn hiệu lực | 409 |
| Mua Add-on khi đang có một giao dịch Add-on Pending còn hạn | 409 |

Giao dịch gói và giao dịch Add-on được tính riêng: có thể cùng lúc có một giao dịch gói và một giao dịch Add-on Pending.

### GET /api/v1/Payment/transactions/me?pageIndex=1&pageSize=20 — đăng nhập

Lịch sử giao dịch của chính người gọi, mới nhất trước. `pageSize` tối đa 50. Trả `PagedResult<PaymentTransactionDto>`
(`items`, `pageIndex`, `pageSize`, `totalCount`, `totalPages`, `hasPreviousPage`, `hasNextPage`).

`PaymentTransactionDto`: `id`, `planId`, `planName`, `planType`, `amount`, `transactionCode`, `qrCodeUrl`, `status`,
`createdAt`, `expiresAt`, `paidAt`, `sepayTransactionId`.

- `status`: `Pending`, `Paid`, `Expired`, `MismatchAmount`. Giao dịch Pending đã quá `expiresAt` được trả là `Expired`
  ngay cả khi worker chưa quét.
- `qrCodeUrl` chỉ có khi giao dịch Pending còn hạn; các trường hợp khác là `null`.
- Màn hình QR gọi lại endpoint này để biết giao dịch đã `Paid` hay chưa.
- `MismatchAmount`: hiển thị "đang chờ quản trị viên xử lý"; phụ huynh không cần chuyển lại.

### GET /api/v1/TokenQuota/me — role `User`

```json
{
  "plan": { "planType": "Creative", "planName": "Gói Sáng tạo", "startAt": "2026-11-10T03:00:00Z", "endAt": "2026-12-10T03:00:00Z" },
  "nextPlan": null,
  "trialCredits": 0,
  "cycleCredits": 4,
  "addOnCredits": 2,
  "otherCredits": 0,
  "totalRemaining": 6
}
```

- `plan` là `null` khi ở mức Miễn phí. `nextPlan` là chu kỳ đã thanh toán trước (gia hạn sớm hoặc hạ gói), bắt đầu khi
  chu kỳ hiện tại kết thúc.
- `otherCredits`: lượt còn dư chuyển từ gói Personal cũ, không hết hạn.

### GET /api/v1/TokenQuota/child/{childProfileId} — đăng nhập (giữ tạm)

Vẫn trả `TokenQuotaStatusDto` cho màn Trang chủ và Studio. Lượt tính theo tài khoản chủ hồ sơ: `isUnlimited = false`,
`remaining` = `quotaLimit` = tổng lượt còn lại, `quotaUsed = 0`, `periodEnd` = ngày kết thúc gói (nếu có).

### Tạo truyện AI khi hết lượt

`POST /api/v1/AIStoryInput/...` trả **409** với thông báo
"Bạn đã hết lượt tạo truyện AI. Hãy nâng lên Gói Sáng tạo hoặc mua Add-on để tạo thêm." Content Creator không bị kiểm tra.

### Webhook và Admin (không đổi đường dẫn)

- `POST /api/v1/Payment/webhook/sepay` — SePay gọi; đúng số tiền thì Paid và áp gói theo loại.
- `GET /api/v1/Payment/mismatched`, `POST /api/v1/Payment/transactions/{id}/mark-paid` — Admin; mark-paid áp gói như webhook.

### Đã gỡ

`POST /api/v1/TokenQuota/config` và `GET /api/v1/TokenQuota/config`. Bảng `token_quota_configs` còn giữ dữ liệu nhưng
không còn được đọc. Admin cấp / điều chỉnh lượt cho từng tài khoản: xem mục 5.2.

## 3. Quy tắc áp gói khi Paid

| Gói mua | Tình trạng lúc Paid | Kết quả |
|---|---|---|
| Gói Đọc / Gói Sáng tạo | Chưa có gói hoặc gói đã hết | Chu kỳ mới 1 tháng từ lúc Paid; Gói Sáng tạo cấp 4 lượt |
| Gói Sáng tạo | Đang có Gói Đọc | Nâng gói ngay: chu kỳ mới 1 tháng, 4 lượt; ngày Gói Đọc còn lại không quy đổi; lượt Add-on còn dư chuyển sang chu kỳ mới |
| Các trường hợp khác khi đang có gói | Gia hạn sớm, hạ Gói Sáng tạo xuống Gói Đọc | Chu kỳ kế tiếp nối ngay sau chu kỳ cuối đã trả; lượt hiện tại không đổi |
| Add-on | Đang có gói | +2 lượt, hết hạn cùng ngày kết thúc chu kỳ |
| Add-on | Gói vừa hết lúc tiền về | +2 lượt, dùng trong 1 tháng từ lúc Paid |

Một giao dịch chỉ được áp một lần: webhook gửi lại hoặc mark-paid sau đó không tạo thêm chu kỳ hay lượt.
Áp gói lỗi thì giao dịch vẫn Paid và có audit `PaymentCreditFailed`; Admin xử lý ở mục 5.3.

## 4. Chu kỳ và thông báo

Worker `SubscriptionCycleWorker` chạy mỗi 15 phút (`Payments:SubscriptionCycleWorker:IntervalMinutes`):

- Gói quá ngày kết thúc mà không gia hạn → về mức Miễn phí. Lượt chu kỳ và lượt Add-on của chu kỳ cũ hết hạn.
- Chu kỳ đã gia hạn sớm đến ngày bắt đầu → kích hoạt; Gói Sáng tạo được cấp 4 lượt mới.
- Gói còn không quá 3 ngày và chưa gia hạn → thông báo `SubscriptionExpiring` (type 32), mỗi chu kỳ một lần.
  Payload: `{ "subscriptionId": 12, "planName": "Gói Sáng tạo", "endAt": "2026-12-10T03:00:00Z" }`.

Thông báo `PaymentConfirmed` (type 17) không đổi.

## 5. Admin (bổ sung 10/11/2026)

Thiết kế: [2026-11-10-admin-plans-payment-design.md](superpowers/specs/2026-11-10-admin-plans-payment-design.md).
Cần thêm migration `AddCreditAdjustmentsAndReconciliationExceptions`. Mọi endpoint trong mục này chỉ cho role
`Administrator` (người khác nhận 403).

### 5.1 Xem và xác nhận tay giao dịch

`GET /api/v1/Payment/admin/transactions?pageIndex=1&pageSize=20&status=Pending&search=abc`

- `status` (tuỳ chọn): `Pending`, `Paid`, `Expired`, `MismatchAmount`; giá trị khác → 400.
- `search` (tuỳ chọn): khớp một phần mã giao dịch, email hoặc tên đăng nhập người mua, không phân biệt hoa thường.
- `pageSize` tối đa 100. Mới nhất trước.
- Mỗi dòng là `PaymentTransactionDto` cộng `payerUserId`, `payerEmail`, `payerName`. Khác với lịch sử của phụ huynh,
  `status` và `qrCodeUrl` là giá trị đang lưu: giao dịch Pending quá hạn mà worker chưa quét vẫn hiện `Pending`.

Khi SePay chưa gọi webhook về được, Admin tìm giao dịch ở đây rồi gọi
`POST /api/v1/Payment/transactions/{id}/mark-paid` (nhận mọi giao dịch chưa Paid). Gói được áp y như webhook.

### 5.2 Cấp hoặc trừ lượt cho một tài khoản (UC-L2-08)

`POST /api/v1/TokenQuota/admin/adjustments` — body `{ "userId": 12, "amount": 3, "reason": "Bù lỗi sinh truyện" }`

- `amount` dương là cấp, âm là trừ. Lượt được cấp không hết hạn và nằm trong `otherCredits` của `GET TokenQuota/me`.
- Trừ lượt lấy từ lô sắp hết hạn trước.

| Tình huống | HTTP |
|---|---|
| `reason` rỗng hoặc dài quá 500 ký tự; `amount = 0` | 400 |
| Tài khoản không phải phụ huynh (role `User`) | 400 |
| Trừ nhiều hơn số lượt còn lại | 400, không đổi gì |
| Không tìm thấy tài khoản | 404 |

Response `CreditAdjustmentDto`: `id`, `userId`, `amount`, `reason`, `adminId`, `adminName`, `createdAt`,
`totalRemainingAfter`. Mỗi lần điều chỉnh ghi audit `CreditAdjusted`.

`GET /api/v1/TokenQuota/admin/adjustments?userId=12&pageIndex=1&pageSize=20` — lịch sử, mới nhất trước; bỏ `userId` để
xem mọi tài khoản. `totalRemainingAfter` là `null` trong danh sách.

`GET /api/v1/TokenQuota/admin/users/{userId}` — gói và lượt của một tài khoản, cùng dạng với `GET TokenQuota/me`.

### 5.3 Giao dịch đã trả tiền nhưng chưa được áp gói (UC-L2-13)

Worker (mỗi 15 phút) tìm giao dịch `Paid` đã quá 5 phút mà chưa có chu kỳ (Gói Đọc, Gói Sáng tạo) hoặc chưa có lô lượt
(Add-on) và tạo một ngoại lệ `Open`. Worker không tự áp gói. Tài khoản đã xoá được bỏ qua.

`GET /api/v1/Payment/admin/reconciliation?status=Open&pageIndex=1&pageSize=20` — mặc định `Open`; `status` khác
`Open` / `Resolved` → 400. Mỗi dòng: `id`, `status`, `reason`, `createdAt`, `resolvedAt`, `resolvedByName`,
`transaction` (dạng ở mục 5.1). `reason` là nội dung audit `PaymentCreditFailed` gần nhất, hoặc `NotApplied`.

`POST /api/v1/Payment/admin/reconciliation/{id}/resolve`

| Tình huống | Kết quả |
|---|---|
| Áp lại gói thành công, hoặc gói đã được áp từ trước | 200, ngoại lệ thành `Resolved`, audit `PaymentReconciliationResolved`; không cộng hai lần |
| Ngoại lệ đã `Resolved` | 409 |
| Áp lại gói vẫn lỗi | 409, ngoại lệ giữ `Open`, `reason` được cập nhật |

### 5.4 Quản lý gói (Luồng 5, 5a)

`GET /api/v1/Payment/admin/plans` — mọi gói, gồm gói tắt bán và gói `Legacy`: `id`, `name`, `planType`, `priceVnd`,
`aiCredits`, `isActive`.

`PUT /api/v1/Payment/admin/plans/{id}` — body `{ "name": "Gói Sáng tạo", "priceVnd": 109000, "aiCredits": 5, "isActive": true }`

- 400 khi: tên rỗng hoặc quá 150 ký tự; `priceVnd <= 0`; `aiCredits < 0`; bật bán gói `Legacy`.
- Không đổi được loại gói. Chưa có API tạo gói mới.
- Thay đổi chỉ áp dụng cho giao dịch tạo sau đó: mỗi giao dịch chốt số tiền và số lượt lúc tạo.
- Gói tắt bán biến mất khỏi `GET Payment/plans` và không mua được nữa. Ghi audit `SubscriptionPlanUpdated`.

## 6. Thống kê doanh thu và tài khoản cho Admin (F-15, F-16, bổ sung 10/11/2026)

Cần thêm migration `AddCreditUsagesAndRevenueSnapshot`. Các endpoint `admin/...` chỉ cho role `Administrator`.

**Kỳ báo cáo** tính trọn ngày theo UTC và gồm cả ngày `periodEnd`: gửi `2026-11-01` và `2026-11-30` là từ 00:00 ngày 01
đến hết ngày 30. `periodEnd` trước `periodStart` → 400.

### 6.1 Xem doanh thu của một kỳ bất kỳ

`GET /api/v1/BusinessReport/admin/revenue?periodStart=2026-11-01&periodEnd=2026-11-30` — tính trực tiếp, không cần tạo báo cáo.

| Trường | Ý nghĩa |
|---|---|
| `totalRevenueVnd`, `paidTransactionCount` | Chỉ giao dịch `Paid`, theo `paidAt`, dùng số tiền lưu trên giao dịch (đổi giá gói về sau không ảnh hưởng) |
| `transactionsByStatus` | `paid` đếm theo `paidAt`; `pending`, `expired`, `mismatchAmount` đếm theo thời điểm tạo giao dịch |
| `revenueByPlan[]` | Mỗi gói có giao dịch Paid trong kỳ: `planId`, `planName`, `planType`, `transactionCount`, `revenueVnd` |
| `revenueByPlanType[]` | Luôn có `Reading`, `Creative`, `AddOn` (kể cả khi bằng 0); thêm `Legacy` nếu có |
| `buyingParents` | Số phụ huynh có ít nhất một giao dịch Paid trong kỳ |
| `repeatBuyingParents` | Trong số đó, người tính đến cuối kỳ đã có từ 2 giao dịch Paid |
| `manuallyMarkedPaidCount`, `manuallyMarkedPaidRevenueVnd` | Giao dịch do Admin xác nhận tay: đã nằm trong doanh thu, đếm riêng |
| `creditsGranted`, `creditsGrantedBySource[]` | Tổng lượt của các lô được cấp trong kỳ, tách theo nguồn (`Trial`, `Cycle`, `AddOn`, `AdminAdjust`, `Legacy`) |
| `creditsUsed` | Tổng lượt bị trừ trong kỳ (tạo truyện AI và Admin trừ), theo bảng `credit_usages` |
| `readingFundRate`, `creativeFundRate`, `creatorFundVnd` | Quỹ Creator = 30% doanh thu Gói Đọc + 10% doanh thu Gói Sáng tạo, làm tròn xuống. Add-on không tính |

Lưu ý: `creditsUsed` chỉ có dữ liệu từ khi migration này được áp; các lần trừ lượt trước đó không được ghi lại.
Chưa có dòng Gói Creator vì gói này chưa tồn tại. Tỷ lệ quỹ đang cố định, chưa cấu hình được.

### 6.2 Báo cáo kỳ

- `POST /api/v1/BusinessReport/generate` — body `{ "periodStart": "2026-11-01", "periodEnd": "2026-11-30" }`. Tạo báo cáo
  `Compiling` và chụp số liệu doanh thu của kỳ vào trường `revenue` (cùng dạng mục 6.1). Kỳ trùng hoặc chồng lên một
  báo cáo đã có → 409; kỳ kế tiếp phải bắt đầu vào ngày sau ngày kết thúc của kỳ trước.
- `POST /api/v1/BusinessReport/{id}/publish` — `Compiling` → `Published`; publish lại → 409.
- `GET /api/v1/BusinessReport/admin?pageIndex=1&pageSize=20` — mọi báo cáo, kỳ mới nhất trước.
- `GET /api/v1/BusinessReport/admin/{id}` — chi tiết một báo cáo.

Số liệu đã chụp không đổi khi có giao dịch đến sau; muốn số mới nhất thì gọi mục 6.1. Báo cáo tạo trước đợt này có
`revenue = null`.

`GET /api/v1/BusinessReport/latest-published` vẫn mở cho mọi tài khoản đã đăng nhập và **không** trả `revenue`.

### 6.3 Thống kê tài khoản người dùng (F-16)

Cần thêm migration `AddBusinessReportAccountSnapshot`.

`GET /api/v1/BusinessReport/admin/accounts?periodStart=2026-11-01&periodEnd=2026-11-30` — tính trực tiếp, cùng quy tắc kỳ
như mục 6. Chỉ tính tài khoản phụ huynh (role `User`) chưa bị xoá.

| Trường | Ý nghĩa |
|---|---|
| `totalParentAccounts` | Số tài khoản phụ huynh đã đăng ký tính đến cuối kỳ |
| `newAccounts` | Số tài khoản đăng ký trong kỳ |
| `activeAccounts` | Số tài khoản có đăng nhập trong kỳ, hoặc có phiên đọc của một hồ sơ trẻ thuộc tài khoản trong kỳ |
| `lockedOrDisabledAccounts` | Số tài khoản đang ở trạng thái `Suspended` tại thời điểm tính |
| `childProfiles` | Số hồ sơ trẻ đã tạo tính đến cuối kỳ, không gồm hồ sơ đã xoá |

Khi tạo báo cáo kỳ (`POST BusinessReport/generate`), các số này được chụp vào trường `accounts` của báo cáo, bên cạnh
`revenue`. `latest-published` không trả `accounts`.

Lưu ý: tài khoản đã xoá không còn được đếm, kể cả cho các kỳ trước khi nó bị xoá. `lockedOrDisabledAccounts` là trạng
thái lúc tính, không phải trạng thái ở cuối kỳ; khoá tạm 15 phút do nhập sai mật khẩu không được tính.

## 7. Thống kê của Content Creator (F-20, F-21, bổ sung 11/11/2026)

Chỉ role `ContentCreator`. Mã Creator luôn lấy từ token đăng nhập, không nhận từ request. Kết quả chỉ là số tổng hợp theo
truyện, không có dữ liệu của trẻ. Không cần migration mới.

Tham số chung: `periodStart`, `periodEnd` (kỳ tính trọn ngày theo UTC, gồm cả ngày cuối, như mục 6) và `storyId` (tuỳ chọn).
Không truyền `storyId` thì trả các truyện đang Published của Creator. Truyền `storyId` của Creator khác → 403; truyện
không tồn tại → 404; `periodEnd` trước `periodStart` → 400.

### 7.1 Lượt đọc — `GET /api/v1/CreatorStatistics/reading`

Trả tổng của kỳ và `stories[]`, mỗi truyện gồm:

| Trường | Ý nghĩa |
|---|---|
| `views` | Số phiên đọc được mở trong kỳ |
| `favorites` | Số trẻ đang đánh dấu yêu thích truyện (số hiện tại, không theo kỳ) |
| `completedReadings` | Trong các phiên mở trong kỳ, số phiên đã đọc hết |
| `validReads` | Lượt đọc hợp lệ của kỳ (định nghĩa bên dưới) |
| `completionRate` | `completedReadings ÷ views`, từ 0 đến 1; bằng 0 khi chưa có lượt xem |

### 7.2 Thu nhập dự kiến — `GET /api/v1/CreatorStatistics/revenue`

| Trường | Ý nghĩa |
|---|---|
| `creatorFundVnd` | Quỹ Creator của kỳ, đúng bằng số ở thống kê doanh thu của Admin (mục 6.1) |
| `readingFundRate`, `creativeFundRate` | 0.30 và 0.10 |
| `minimumReadSeconds` | Thời gian đọc tối thiểu của một lượt hợp lệ, hiện là 60 giây |
| `totalValidReadsAllCreators` | Tổng lượt đọc hợp lệ của mọi Creator trong kỳ |
| `myValidReads` | Lượt đọc hợp lệ của Creator đang gọi |
| `estimatedEarningsVnd` | `myValidReads × creatorFundVnd ÷ totalValidReadsAllCreators`, làm tròn xuống đến đồng |
| `stories[]` | Mỗi truyện: `storyId`, `title`, `validReads`, `estimatedEarningsVnd` (cùng công thức) |

Đây là số dự kiến; việc chi trả thực hiện ngoài hệ thống. Vì làm tròn xuống, tổng phần chia của các Creator có thể nhỏ
hơn quỹ vài đồng.

### 7.3 Lượt đọc hợp lệ

Một phiên đọc được tính là lượt đọc hợp lệ khi thoả tất cả:

1. Phiên đã `Completed`, thời điểm hoàn thành nằm trong kỳ.
2. Truyện là truyện Creator và đang Published lúc đọc.
3. Tài khoản của trẻ có gói trả phí đang hiệu lực lúc đọc.
4. Thời gian đọc từ 60 giây trở lên.
5. Trẻ không phải con của chính Creator.
6. Mỗi trẻ tối đa 1 lượt cho mỗi truyện mỗi ngày, và tối đa 10 lượt mỗi ngày trên mọi truyện. "Ngày" ở đây tính theo
   giờ Việt Nam.
7. Truyện bị gỡ vì vi phạm trong kỳ mất toàn bộ lượt đọc hợp lệ của kỳ đó; truyện bị gỡ sau kỳ vẫn giữ lượt của kỳ.

## 8. Gói Creator, hạn mức tháng và lượt gửi duyệt của Content Creator (Luồng 2, 2f, bổ sung 11/11/2026)

Cần thêm migration `AddCreatorPackAndSubmissionCredits`.

### 8.1 Gói theo đối tượng

- Mỗi gói có `audience`: `User` (phụ huynh) hoặc `Creator`. `GET /Payment/plans` chỉ trả gói đang bán của đối tượng người
  gọi; Admin và Moderator nhận danh sách rỗng.
- Gói mới: **Gói Creator** (Id 5, `planType = CreatorPack`, `audience = Creator`): 99.000 đ, +6 lượt AI, +20 lượt gửi
  duyệt, mua một lần, không hết hạn. Trường mới `submitCredits` có trên `SubscriptionPlanDto` và DTO gói của Admin.
- `POST /Payment/transactions`:
  - Mua gói khác đối tượng (phụ huynh mua Gói Creator hoặc ngược lại) → 403.
  - Tài khoản chưa Active → 403: phụ huynh chưa xác thực email, hoặc Creator chưa được Admin duyệt. Thực tế tài khoản
    chưa Active thường bị chặn ngay ở bước xác thực (401).
  - Mỗi Creator chỉ có một giao dịch Gói Creator đang chờ thanh toán.
  - Giao dịch chốt số lượt AI và số lượt gửi duyệt lúc tạo.
- Khi giao dịch Gói Creator `Paid` (webhook hoặc Admin xác nhận tay): cộng 6 lượt AI và 20 lượt gửi duyệt, không hết hạn,
  không cộng hai lần.
- `PUT /Payment/admin/plans/{id}` nhận thêm `submitCredits` (tuỳ chọn; bỏ trống thì giữ nguyên).

### 8.2 Hạn mức tháng

- Mỗi Creator đang Active có **2 lượt AI và 5 lượt gửi duyệt** mỗi tháng, không cộng dồn. Tháng tính theo giờ Việt Nam;
  hạn mức hết hạn lúc 00:00 ngày 1 tháng sau.
- Hạn mức được cấp ở lần đầu Creator cần dùng trong tháng (khi xem lượt, gửi yêu cầu AI hoặc gửi duyệt).
- Hạn mức tháng được dùng trước, lượt mua từ Gói Creator dùng sau.

### 8.3 Lượt AI của Creator

`POST /creator/ai-story-input/requests`: hết lượt → **409**; còn lượt thì trừ 1 lượt ngay khi gửi (như phụ huynh).
Gửi lại cùng `idempotencyKey` không trừ thêm.

### 8.4 Lượt gửi duyệt

| Việc | Lượt |
|---|---|
| `POST /PublicStorySubmission/submit` | Trừ 1 lượt; hết lượt → **409** kèm gợi ý mua Gói Creator, không tạo yêu cầu duyệt |
| Gửi lại sau khi bị yêu cầu sửa (`ChangesRequested`) | Không trừ thêm |
| Gửi lại sau khi bị từ chối | Trừ 1 lượt (lượt cũ đã được hoàn) |
| Moderator từ chối (`Rejected`) | Hoàn 1 lượt |
| Creator rút yêu cầu khi còn chờ (`withdraw`) | Hoàn 1 lượt |
| Hệ thống rút yêu cầu đang chờ khi truyện bị lưu trữ | Hoàn 1 lượt |
| Yêu cầu đã có Moderator nhận rồi bị đóng vì truyện bị lưu trữ | Không hoàn |
| Moderator duyệt (`Approved`) | Không đổi |

Lượt được hoàn về đúng lô đã trừ. Nếu đó là lô hạn mức của tháng đã qua thì lượt hoàn cũng đã hết hạn.

### 8.5 Creator xem lượt — `GET /api/v1/TokenQuota/creator/me` (role `ContentCreator`)

```json
{
  "aiCredits":     { "monthlyAllowance": 2, "monthlyRemaining": 2, "purchasedRemaining": 6,  "otherRemaining": 0, "totalRemaining": 8 },
  "submitCredits": { "monthlyAllowance": 5, "monthlyRemaining": 5, "purchasedRemaining": 20, "otherRemaining": 0, "totalRemaining": 25 },
  "monthlyAllowanceResetsAt": "2026-11-30T17:00:00Z"
}
```

Lịch sử mua của Creator dùng chung `GET /Payment/transactions/me`.

### 8.6 Thống kê doanh thu

`revenueByPlanType` có thêm dòng `CreatorPack`. Gói Creator tính vào doanh thu nhưng không vào quỹ Creator, và người mua
Gói Creator không được đếm vào `buyingParents`. `creditsGranted` và `creditsUsed` chỉ tính lượt AI.

### 8.7 Lệch so với tài liệu Luồng 2 (để đợt sau)

1. Tài liệu muốn **giữ – trừ – trả** lượt AI của Creator: trừ khi AI sinh xong, trả lại khi lỗi hoặc bị chặn. Hiện vẫn
   trừ ngay khi gửi, giống phụ huynh, và không trả lại khi lỗi.
2. Tài liệu muốn **worker cấp hạn mức tháng vào ngày 1**. Hiện cấp ở lần dùng đầu trong tháng.
3. Tài liệu tính 1 lượt AI khi **sinh ảnh và audio bằng AI cho truyện tự viết**. Hiện chưa tính.
4. **Hệ thống rút yêu cầu đang chờ** (khi truyện bị lưu trữ) cũng được hoàn lượt. Tài liệu chỉ nói tới việc Creator tự rút.

Chưa có Creator đối tác (6 lượt AI và 10 lượt gửi duyệt): mọi Creator dùng mức 2 và 5. Admin chưa cấp hoặc điều chỉnh
được lượt cho Creator, và chưa cấu hình được con số hạn mức.

## 9. Ngoài phạm vi

Giữ – trừ – trả lượt; Creator đối tác; Admin điều chỉnh lượt cho Creator; giới hạn theo gói (số hồ sơ trẻ, 3 truyện mỗi tuần, đọc to);
cảnh báo khi xoá tài khoản còn gói; tạo gói mới; cấu hình tỷ lệ quỹ Creator và thời gian đọc tối thiểu;
giao diện Admin trên web.
