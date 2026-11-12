# F-17 – Quản lý gói, quyền lợi theo gói và cấu hình chung (thiết kế)

Ngày: 2026-11-11 · Owner: Đặng Lê Anh Tuấn · Nguồn: Commitment F-17, Luồng 2 (2h), Luồng 1 (1e), Luồng 4 (4a, 4b), Luồng 5 (5a)

## 1. Mục đích

Hiện Admin chỉ sửa được giá, số lượt và bật/tắt bán gói. Quyền lợi của gói (số hồ sơ trẻ, thư viện, bộ truyện mẫu, trợ lý viết) chưa tồn tại nên tài khoản Miễn phí và tài khoản trả phí dùng như nhau; các giá trị chung (tỷ lệ quỹ Creator, hạn mức tháng của Creator, lượt dùng thử, thời gian đọc tối thiểu) đang ghi cứng trong code.

Sau thay đổi này:
- Admin tạo gói mới, sửa giá, số lượt và quyền lợi, bật/tắt bán; cấu hình các giá trị chung và quyền lợi của mức Miễn phí. Mọi thay đổi ghi audit log trước/sau.
- Hệ thống **chặn thật** theo quyền lợi của gói đang hiệu lực (người dùng yêu cầu ngày 11/11).
- Khi hết gói, tài khoản về Miễn phí: phụ huynh chọn hồ sơ trẻ được tiếp tục đọc, các hồ sơ khác chỉ xem.

## 2. Mục tiêu / Không làm / Không được xảy ra

**Mục tiêu**
1. API Admin tạo gói; mở rộng API sửa gói với quyền lợi.
2. Bảng cấu hình chung + API Admin xem/sửa.
3. Áp quyền lợi: số hồ sơ trẻ, 3 truyện thư viện mới mỗi tuần, sao chép bộ truyện mẫu, số lần trợ lý viết mỗi ngày.
4. Hồ sơ được đọc khi vượt số hồ sơ cho phép (về Miễn phí): chọn, mặc định, đổi tối đa 1 lần mỗi tuần.
5. Các dịch vụ đang ghi cứng đọc giá trị từ cấu hình.

**Không làm (ghi rõ trong hướng dẫn API)**
- Giới hạn đọc to: chỉ lưu số lần theo gói, chưa chặn (Luồng 2/4 ghi "để sau Defense 1.1").
- Giới hạn xem tiến độ 7 ngày của Miễn phí (cùng lý do).
- Giới hạn sinh lại trong một truyện (dàn ý/nội dung/ảnh/audio): có ở Luồng 5 mục 5 nhưng không nằm trong mô tả F-17 của Commitment; giữ nguyên cấu hình hiện tại.
- Hạng Creator đối tác (xét cấp ở 5g): chỉ lưu hạn mức 6 + 10 và trợ lý viết 40 trong cấu hình; chưa có cách nào để một Creator là đối tác nên chưa dùng.
- Frontend.

**Không được xảy ra**
- Chặn nhầm truyện gia đình (truyện phụ huynh tạo, gói truyện đã xác nhận cho gia đình) bằng giới hạn thư viện.
- Đọc lại truyện đã mở bị tính thêm vào 3 truyện mỗi tuần.
- Hồ sơ trẻ bị xoá hoặc đổi trạng thái khi về Miễn phí.
- Giao dịch đã tạo đổi giá hoặc số lượt khi Admin sửa gói (giữ hành vi hiện có).
- Mở lại khả năng tạo/bán gói Legacy.

## 3. Dữ liệu

### 3.1 `subscription_plans` – thêm cột quyền lợi

| Cột | Kiểu | Ý nghĩa | Seed Gói Đọc / Sáng tạo |
|---|---|---|---|
| `MaxChildProfiles` | int | Số hồ sơ trẻ tối đa | 4 / 4 |
| `WeeklyNewLibraryStories` | int, null | Số truyện thư viện mới mỗi tuần; null = không giới hạn | null / null |
| `CanCopyRoadmapTemplates` | bool | Được sao chép bộ truyện mẫu | true / true |
| `ReadAloudPerMonth` | int, null | Số lần đọc to mỗi tháng (chỉ lưu) | 60 / 60 |
| `WritingAssistPerDay` | int | Số lần trợ lý viết mỗi ngày | 5 / 20 |

Quyền lợi chỉ có nghĩa với gói chu kỳ (Reading, Creative). Add-on, Gói Creator, Legacy lưu giá trị mặc định (0/null/false) và API bỏ qua phần quyền lợi của chúng.

### 3.2 Bảng mới `plan_settings` – một dòng duy nhất (Id = 1)

| Nhóm | Cột | Mặc định |
|---|---|---|
| Quỹ Creator | `ReadingFundPercent`, `CreativeFundPercent` | 30, 10 |
| Hạn mức tháng Creator | `CreatorMonthlyAiCredits`, `CreatorMonthlySubmitCredits` | 2, 5 |
| Hạn mức Creator đối tác (chỉ lưu) | `PartnerMonthlyAiCredits`, `PartnerMonthlySubmitCredits` | 6, 10 |
| Lượt đọc hợp lệ | `MinimumReadSeconds` | 60 |
| Lượt dùng thử | `TrialAiCredits` | 1 |
| Mức Miễn phí | `FreeMaxChildProfiles`, `FreeWeeklyNewLibraryStories`, `FreeCanCopyRoadmapTemplates`, `FreeReadAloudPerMonth`, `FreeWritingAssistPerDay` | 1, 3, false, 5, 5 |
| Trợ lý viết của Creator | `CreatorWritingAssistPerDay`, `PartnerWritingAssistPerDay` | 20, 40 |

Migration tạo bảng và chèn dòng mặc định; seed SQL trong `Database/Seed` cập nhật tương ứng. Code luôn đọc dòng Id = 1; nếu thiếu (DB test) dùng giá trị mặc định ở trên.

### 3.3 Chọn hồ sơ được đọc

- `child_profiles.ReadingSlotSelectedAt` (timestamp, null): phụ huynh đã chọn hồ sơ này để đọc.
- Lần đổi gần nhất = `ReadingSlotSelectedAt` lớn nhất trên các hồ sơ của tài khoản (không cần cột trên `user_accounts`).

## 4. Quyền lợi hiệu lực của một tài khoản

`IPlanBenefitService.GetForUserAsync(userId)` (Application, đặt cạnh TokenQuota) trả về:
- Nếu tài khoản có chu kỳ `user_subscriptions` đang Active (Reading hoặc Creative): quyền lợi đọc **trực tiếp từ dòng gói hiện tại** của chu kỳ đó. Admin sửa quyền lợi thì người đang dùng gói nhận ngay (khác với giá và lượt, vốn chốt theo giao dịch).
- Nếu không: quyền lợi Miễn phí từ `plan_settings`.
- Content Creator: chỉ dùng `CreatorWritingAssistPerDay`; các quyền lợi khác không áp dụng.

Kết quả gồm cả tên mức ("Free" / tên gói) để trả về cho frontend.

## 5. Các chỗ chặn

Mọi lỗi dùng exception sẵn có của project; thông điệp tiếng Việt kèm gợi ý nâng gói.

| # | Chỗ | Điều kiện chặn | Lỗi |
|---|---|---|---|
| 1 | Tạo hồ sơ trẻ (`CreateChildProfileAsync`), khôi phục hồ sơ (`RestoreChildProfileAsync`) | Số hồ sơ chưa Archived/Deleted của tài khoản ≥ `MaxChildProfiles` | 409 |
| 2 | Trẻ vào đọc: đăng nhập EasyLogin, phiên có giám sát, giao truyện (handover), bắt đầu phiên đọc | Hồ sơ không thuộc nhóm "được đọc" (mục 6) | 403 "Hồ sơ đang ở chế độ chỉ xem" |
| 3 | Bắt đầu phiên đọc (`ChildReadingService`) với truyện thư viện công khai | Mức có giới hạn tuần; truyện chưa từng được hồ sơ nào của tài khoản mở; số truyện thư viện mới đã mở trong tuần ≥ giới hạn | 403 "Hết lượt truyện mới tuần này, mở lại vào thứ Hai" |
| 4 | Sao chép bộ truyện mẫu (`RoadmapLibraryService.CopyAsync`) | `CanCopyRoadmapTemplates = false` | 403; xem trước vẫn được |
| 5 | Tạo yêu cầu trợ lý viết (Parent và Creator assistance) | Số yêu cầu của tài khoản trong ngày ≥ `WritingAssistPerDay` | 429 |

Chi tiết:
- **Truyện thư viện công khai** = truyện `Scope = Creator` và `IsPublished`. Phiên mở qua `FamilyConfirmationId` hoặc truyện phụ huynh không bị tính.
- **Tuần** bắt đầu 00:00 thứ Hai giờ Việt Nam; **ngày** của trợ lý viết là ngày giờ Việt Nam. Hiện `VietnamOffset` (UTC+7) đang khai báo riêng trong `TokenQuotaService.Creator` và `CreatorStatisticsService`; gom vào một helper dùng chung (đầu ngày, đầu tuần, đầu tháng giờ Việt Nam).
- **Một lần trợ lý viết** = một `StoryAssistanceTask` mới do tài khoản tạo (`OwnerUserId`, `CreatedAt` trong ngày). Gửi lại cùng `CreateOperationId` (idempotent replay) không tính thêm. Áp dụng cho cả Parent và Creator vì cùng dùng bảng này.
- **"Truyện mới trong tuần"** = truyện thư viện có phiên đọc đầu tiên (của mọi hồ sơ trong tài khoản) bắt đầu trong tuần. Đọc lại hoặc tiếp tục phiên dở không tính.
- Mục 3 khoá theo tài khoản (`AcquireTransactionLockAsync`) để hai trẻ mở cùng lúc không vượt giới hạn.
- Mục 1 đếm cả Draft và Suspended, vì đó vẫn là hồ sơ đang chiếm chỗ.

## 6. Hồ sơ được đọc khi vượt số hồ sơ cho phép

Áp dụng khi số hồ sơ **Active** của tài khoản lớn hơn `MaxChildProfiles` (thường là lúc về Miễn phí). Nếu không vượt, mọi hồ sơ Active đều đọc được như hiện nay.

- **Nhóm được đọc** gồm tối đa `MaxChildProfiles` hồ sơ Active:
  1. hồ sơ phụ huynh đã chọn (`ReadingSlotSelectedAt` gần nhất trước);
  2. nếu còn chỗ, hồ sơ có phiên đọc gần nhất;
  3. nếu vẫn còn chỗ, hồ sơ tạo sớm nhất.
- Các hồ sơ còn lại là **chỉ xem**: phụ huynh vẫn xem, sửa hồ sơ, tiến độ; trẻ không vào được (chặn ở mục 5.2). Trạng thái hồ sơ không đổi.
- **API cho phụ huynh:**
  - `GET api/v1/ChildProfile/reading-slots`: số chỗ, danh sách hồ sơ kèm cờ được đọc / chỉ xem, ngày được đổi tiếp theo.
  - `PUT api/v1/ChildProfile/reading-slots` với `{ childProfileIds: [...] }`: số hồ sơ ≤ `MaxChildProfiles`, đều là hồ sơ Active của chính tài khoản.
- **Giới hạn đổi:** tối đa 1 lần mỗi tuần (tuần giờ Việt Nam). Nếu lần đổi gần nhất nằm trong tuần hiện tại → 409 kèm ngày được đổi tiếp. Lần chọn đầu tiên cũng tính là một lần đổi.
- Khi đổi, phiên trẻ đang mở của hồ sơ chuyển sang chỉ xem bị thu hồi bằng `IChildSessionRevoker` sẵn có.
- Gia hạn gói: số chỗ tăng lên nên mọi hồ sơ đọc lại được; lựa chọn cũ giữ nguyên trong DB, không ảnh hưởng.

## 7. Giá trị đang ghi cứng chuyển sang cấu hình

| Chỗ | Hằng hiện tại | Đọc từ |
|---|---|---|
| `RevenueStatisticsService` | 30 / 10 | `ReadingFundPercent`, `CreativeFundPercent` |
| `CreatorStatisticsService` | 30 / 10, `MinimumReadSeconds = 60` | như trên + `MinimumReadSeconds` |
| `TokenQuotaService.Creator` | 2 / 5 | `CreatorMonthlyAiCredits`, `CreatorMonthlySubmitCredits` |
| `TokenQuotaService` cấp lượt dùng thử | 1 | `TrialAiCredits` |

Báo cáo kỳ đã Published giữ snapshot cũ (đã lưu tỷ lệ trong JSON). Hạn mức tháng của Creator chỉ đổi từ tháng được cấp sau thời điểm sửa; lô tháng đã cấp giữ nguyên.

## 8. API

**Admin** (`[Authorize(Roles = "Administrator")]`, đều ghi audit log trước/sau):
- `POST api/v1/Payment/admin/plans`: tạo gói. Trường: tên, đối tượng, loại, giá, lượt AI, lượt gửi duyệt, quyền lợi, `IsActive`.
  - Kiểm tra cặp đối tượng/loại: User ↔ Reading / Creative / AddOn; Creator ↔ CreatorPack; không tạo Legacy.
  - Giá > 0, số lượt ≥ 0, `MaxChildProfiles` ≥ 1 với gói chu kỳ.
- `PUT api/v1/Payment/admin/plans/{id}`: thêm phần quyền lợi (bỏ trống thì giữ nguyên).
- `GET api/v1/Payment/admin/settings`, `PUT api/v1/Payment/admin/settings`.
  - Tỷ lệ quỹ 0–100.
  - Số lượt, số lần, số hồ sơ ≥ 0; `FreeMaxChildProfiles` ≥ 1.
  - `MinimumReadSeconds` 1–3600.

**Người dùng:**
- `GET api/v1/Payment/plans`: thêm quyền lợi của từng gói.
- `GET api/v1/TokenQuota/me`: thêm quyền lợi hiệu lực, số truyện thư viện mới đã mở trong tuần và số lần trợ lý viết đã dùng hôm nay.
- `GET/PUT api/v1/ChildProfile/reading-slots` (mục 6).

## 9. Kiểm thử

- **Unit:**
  - validation tạo/sửa gói và cấu hình; audit trước/sau;
  - quyền lợi hiệu lực (có gói / hết gói / Creator);
  - từng chỗ chặn ở mục 5, kể cả trường hợp không được chặn (đọc lại, truyện gia đình, tiếp tục phiên dở);
  - nhóm được đọc và giới hạn đổi theo tuần;
  - các dịch vụ thống kê và cấp lượt đọc giá trị từ cấu hình.
- **Integration (HTTP):**
  - Admin tạo gói rồi phụ huynh thấy gói;
  - Admin đổi tỷ lệ quỹ làm doanh thu đổi theo;
  - tài khoản Miễn phí: tạo hồ sơ thứ hai bị 409; mở truyện thư viện thứ 4 trong tuần bị 403; sao chép bộ truyện mẫu bị 403;
  - về Miễn phí với 2 hồ sơ: hồ sơ chỉ xem không vào đọc được, đổi lựa chọn lần hai trong tuần bị 409.
- **PostgreSQL:** hai trẻ cùng mở truyện thư viện mới thứ 3 và thứ 4 cùng lúc, chỉ một được mở.
- Cập nhật các harness/test sẵn có đang dựng tài khoản không có gói mà mong đọc truyện thư viện, tạo nhiều hồ sơ hoặc sao chép bộ truyện mẫu: cho tài khoản đó một chu kỳ Active hoặc cấu hình Miễn phí rộng hơn trong test.

## 10. Tài liệu

Thêm mục 10 vào `docs/PARENT_PLANS_PAYMENT_API_GUIDE.md` (API mới, mã lỗi, quy tắc tuần/ngày) và sửa mục 8.7 / 9 (những gì vẫn chưa làm).

## 11. Quyết định do người viết thiết kế tự chốt (cần người dùng xem lại)

1. Quyền lợi đọc từ gói hiện tại, nên Admin sửa thì người đang dùng nhận ngay.
2. Cấu hình chung là một bảng một dòng `plan_settings`, không dùng bảng key–value.
3. Lần chọn hồ sơ đầu tiên cũng tính là lần đổi trong tuần.
4. Mã lỗi: 409 cho số hồ sơ, 403 cho thư viện và bộ truyện mẫu, 429 cho trợ lý viết.
5. Cho phép tạo nhiều gói cùng loại; quy tắc áp gói (gia hạn nối chu kỳ, Add-on cần gói đang hiệu lực) vẫn tính theo loại gói.
6. Giới hạn sinh lại trong một truyện để ngoài phạm vi.
7. Một lần trợ lý viết tính theo một task mới, không theo từng bước gợi ý bên trong task.
8. Chuẩn bị nhánh mới từ `origin/main` (nhánh local `feat/parent-plans-payment` đã merge và đang chậm 9 commit).
