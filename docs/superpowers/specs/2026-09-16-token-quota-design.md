# Token Quota Service — Design Spec (Luồng 5, Bước 5.1c)

Ngày: 2026-09-16
Nguồn spec: `Luong5_Administrator_AIGovernance_DeepDive_v4.docx`, Bước 5.1c, Bước 5.6, Mục 9.

## 1. Bối cảnh & vấn đề

Bảng `token_quota_configs` đã tồn tại trong schema (`TokenQuotaConfig` entity, `TokenQuotaScope` enum:
`System | Organization | Child`) nhưng **không có bất kỳ service/controller nào** đọc hoặc ghi vào đó.
Hệ quả:

- Luồng 2 (sinh truyện AI) không kiểm tra quota trước khi tạo `StoryGenerationRequest` — không giới hạn.
- Payment (Bước 5.6.2) xác nhận thanh toán SePay thành công nhưng không cộng quota vào đâu cả.

Ngoài ra, spec Bước 5.6 mô tả gói **Personal** phải cộng quota "vào chính Parent mua gói, dùng chung cho
mọi ChildProfile scope=personal của gia đình" — nhưng schema hiện tại không có khái niệm quota theo
Parent, chỉ có theo Child/Organization/System.

## 2. Quyết định thiết kế đã chốt (đã hỏi & được duyệt)

| Câu hỏi | Quyết định |
|---|---|
| Quota cá nhân gắn vào đâu? | Thêm `TokenQuotaScope.Personal` + cột `UserId` (nullable) vào `TokenQuotaConfig`. Cần 1 migration mới. |
| Hành vi khi chưa có config cho 1 scope? | **Fail-open**: không giới hạn cho tới khi Admin (hoặc một giao dịch thanh toán) tạo config cho scope đó. |
| Đơn vị "1 lượt" quota? | Mỗi `StoryGenerationRequest` **mới** được tạo (Bước 2.1, `AIStoryInputService.SubmitAsync`). Không tính khi idempotency-key trùng (trả về request cũ), không tính retry. |
| Concurrency cho `QuotaUsed` | Đọc-tăng-lưu đơn giản, không thêm optimistic concurrency — chấp nhận rủi ro race condition nhỏ ở quy mô capstone, nhất quán với phần còn lại của codebase. |

## 3. Domain changes

- `TokenQuotaScope` (`src/Core/StoryPlatform.Domain/Enums/TokenQuotaScope.cs`): thêm `Personal = 4`.
- `TokenQuotaConfig` (`src/Core/StoryPlatform.Domain/Entities/TokenQuotaConfig.cs`): thêm
  `public int? UserId { get; set; }` + `public virtual UserAccount? User { get; set; }`.
- `TokenQuotaConfigConfiguration`: thêm `HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict)`.
- Migration mới: `AddPersonalScopeToTokenQuota` (cột nullable, không cần backfill).

## 4. Logic phân giải quota áp dụng (resolve hierarchy)

Cho một `ChildProfile`, quota áp dụng được chọn theo thứ tự cụ thể nhất trước — dừng ở record đầu tiên tìm thấy:

1. `Scope=Child` với `ChildProfileId` = chính trẻ đó.
2. Nếu `child.Scope == Organization`: `Scope=Organization` với `OrganizationId = child.OrganizationId`.
   Nếu `child.Scope == Personal`: `Scope=Personal` với `UserId = child.OwnerUserId`.
3. `Scope=System` (record duy nhất, không có target id).
4. Không tìm thấy gì → **unlimited** (không chặn).

**Lazy period rollover**: khi đọc 1 config, nếu `PeriodEnd < UtcNow.Date` → reset `QuotaUsed = 0`, dời
`PeriodStart`/`PeriodEnd` sang chu kỳ kế tiếp có cùng độ dài với chu kỳ cũ, lưu lại ngay. Không cần thêm
background worker riêng cho việc reset.

## 5. Application layer — `Features/TokenQuota/`

Theo đúng cấu trúc feature folder hiện có (`DTOs/`, `Interfaces/`, `Services/`).

`ITokenQuotaService`:

```
Task<TokenQuotaConfigDto> SetConfigAsync(int adminUserId, SetTokenQuotaConfigRequestDto request, CancellationToken ct);
Task<List<TokenQuotaConfigDto>> ListConfigsAsync(CancellationToken ct);
Task<TokenQuotaStatusDto> GetStatusForChildAsync(int childProfileId, CancellationToken ct);
Task EnsureWithinQuotaAsync(int childProfileId, CancellationToken ct); // throws ConflictException nếu vượt
Task IncrementUsageAsync(int childProfileId, CancellationToken ct);
Task CreditAsync(ProfileScope planScope, int payerUserId, int? organizationId, int quotaAmount, CancellationToken ct);
```

- `SetConfigAsync`: Admin tạo/sửa 1 config (scope + target id tương ứng + quotaLimit + periodStart/periodEnd),
  ghi audit log theo pattern `AdminAccountService`/`DataRequestService` đã dùng (`IAuditLogWriter`).
- `EnsureWithinQuotaAsync`: resolve theo Mục 4; nếu record tồn tại và `QuotaUsed >= QuotaLimit` → ném
  `ConflictException` (409, đã có sẵn trong `AppException` hierarchy) với message tiếng Việt thân thiện,
  không phải lỗi kỹ thuật (đúng yêu cầu Fast Fail của spec).
- `IncrementUsageAsync`: resolve lại đúng config đã áp dụng (cùng logic Mục 4), `QuotaUsed += 1`, save.
- `CreditAsync`: dùng bởi `PaymentService` sau khi `status=Paid`.
  - `planScope == Personal` → tìm config `Scope=Personal, UserId=payerUserId` còn hiệu lực (chưa hết
    `PeriodEnd`); nếu có → `QuotaLimit += quotaAmount` (**không** reset `QuotaUsed`, đúng yêu cầu "cộng dồn"
    của Bước 5.6.2); nếu chưa có → tạo mới với `PeriodStart = hôm nay`, `PeriodEnd = hôm nay + 1 tháng`,
    `QuotaLimit = quotaAmount`, `QuotaUsed = 0`.
  - `planScope == Organization` → tương tự nhưng theo `Scope=Organization, OrganizationId=organizationId`.

## 6. Nối vào code đã có

- **`AIStoryInputService.SubmitAsync`**
  (`src/Core/StoryPlatform.Application/Features/AIStoryInput/Services/AIStoryInputService.cs`):
  - Sau bước kiểm tra idempotency-key (dòng ~67-77, đã có logic return sớm cho request trùng key) và
    trước khi quyết định tạo `Story`/`StoryGenerationRequest` mới (khoảng dòng 79) → gọi
    `EnsureWithinQuotaAsync(request.ChildProfileId, cancellationToken)`.
  - Trong transaction đang mở (`BeginTransactionAsync`...`CommitTransactionAsync`, dòng 127-137), sau
    `requestRepository.AddAsync(generationRequest, ...)` và trước `CommitTransactionAsync` → gọi
    `IncrementUsageAsync(request.ChildProfileId, cancellationToken)` để tăng đếm cùng transaction với
    việc tạo request (atomic với nhau).
- **`PaymentService`**
  (`src/Core/StoryPlatform.Application/Features/Payments/Services/PaymentService.cs`):
  - `HandleWebhookAsync`: trong nhánh `transaction.Amount == payload.TransferAmount` (paid), ngay sau khi
    lưu `transaction.Status = Paid` → gọi
    `CreditAsync(plan.ApplicableScope, transaction.PayerUserId, transaction.OrganizationId, plan.QuotaAmount, ct)`.
  - `MarkPaidManuallyAsync`: tương tự, sau khi set `Paid`.
  - **Quan trọng (phát hiện khi self-review, đã xác minh trong code)**: cả hai method này lấy
    `PaymentTransaction` mà **không load navigation `Plan`** — `HandleWebhookAsync` dùng
    `FindAsync` không truyền `includeProperties`, còn `MarkPaidManuallyAsync` dùng `GetByIdAsync`
    (gọi thẳng `DbSet.FindAsync`, **hoàn toàn không hỗ trợ include**). Nếu cứ dùng `transaction.Plan`
    trực tiếp sẽ luôn là `null` → không lấy được `ApplicableScope`/`QuotaAmount`. Vì vậy ở cả 2 chỗ, sau khi
    xác định đúng `transaction` cần xử lý, phải fetch riêng:
    `var plan = await _unitOfWork.Repository<SubscriptionPlan>().GetByIdAsync(transaction.PlanId, ct);`
    rồi mới gọi `CreditAsync` với `plan.ApplicableScope`/`plan.QuotaAmount`.

## 7. API mới — `TokenQuotaController`

`src/Core/StoryPlatform.Api/Controllers/TokenQuotaController.cs`:

- `POST /api/token-quota/config` — `[Authorize(Roles = "Administrator")]` → `SetConfigAsync`.
- `GET /api/token-quota/config` — `[Authorize(Roles = "Administrator")]` → `ListConfigsAsync`.
- `GET /api/token-quota/child/{childProfileId}` — `[Authorize]` (service tự kiểm tra quyền: chủ sở hữu
  Parent, Teacher đang giám sát, School Admin của Organization đó, hoặc Administrator) → `GetStatusForChildAsync`.

## 8. Testing

- `TokenQuotaServiceTests`: resolve hierarchy (Child > Org/Personal > System > unlimited), lazy rollover
  (period hết hạn → reset đúng), `EnsureWithinQuotaAsync` chặn đúng lúc quota đầy / cho qua khi còn quota
  hoặc unlimited, `CreditAsync` cộng dồn đúng (tạo mới vs top-up, không reset `QuotaUsed`).
- `AIStoryInputServiceTests` (bổ sung case mới): bị chặn `ConflictException` khi hết quota; không bị chặn
  khi unlimited/còn quota; không bị trừ 2 lần khi gọi lại với cùng idempotency key.
- `PaymentServiceTests` (bổ sung case mới): webhook paid / mark-paid-manually đều gọi đúng `CreditAsync`
  với đúng scope/target; top-up cộng dồn không reset usage.
- `TokenQuotaControllerAuthorizationTests`: theo đúng pattern các `*ControllerAuthorizationTests.cs` khác
  trong repo (chỉ Administrator gọi được 2 endpoint config).

## 8b. Ghi chú kỹ thuật quan trọng (phát hiện khi tự review)

- `GenericRepository.FindAsync`/`FirstOrDefaultAsync`/`GetAllAsync` đều dùng `.AsNoTracking()` — entity trả
  về **không được EF theo dõi**. Mọi chỗ mutate (`IncrementUsageAsync`, lazy rollover, `CreditAsync`) phải
  gọi `_unitOfWork.Repository<T>().Update(entity)` (Attach + đánh dấu Modified) trước khi save — đúng pattern
  đã dùng ở `AdminAccountService`/`DataRequestService`/`PaymentService` hiện có, không phải pattern mới.
- `GetByIdAsync` gọi thẳng `DbSet.FindAsync` của EF (có tracking, nhưng **không hỗ trợ include** dù có
  navigation property) — khác với `FindAsync` của chính repository (không tracking, có hỗ trợ include qua
  chuỗi string). Cần phân biệt rõ khi viết `TokenQuotaService` để tránh giả định sai entity nào được track.
- `IUnitOfWork.CommitTransactionAsync()` tự gọi `SaveChangesAsync()` một lần rồi mới `CommitAsync()` transaction
  DB. Gọi `SaveChangesAsync()` thêm lần nữa ở giữa (bên trong `IncrementUsageAsync`) là an toàn — EF cho phép
  nhiều lần `SaveChangesAsync()` trong cùng 1 transaction DB đang mở, không tự ý commit/kết thúc transaction.
  Vì vậy `IncrementUsageAsync` có thể tự gọi `SaveChangesAsync()` bên trong, dùng được cả khi gọi độc lập lẫn
  khi gọi từ trong transaction đang mở của `SubmitAsync`, không cần tách logic "stage vs save" riêng.
- Đã đọc `AIStoryInputService.RetryAsync` (dòng 182+): xác nhận method này chỉ `Update()` lại đúng
  `StoryGenerationRequest` cũ (không `AddAsync` request mới) — đúng như giả định ở Mục 2, retry không tính
  thêm quota.

## 9. Ngoài phạm vi (out of scope) của v1 này

- Không thêm background worker riêng để reset quota theo lịch — dùng lazy rollover khi đọc.
- Không thêm optimistic concurrency cho `QuotaUsed`.
- Không thêm UI cho Admin/Supervisor xem quota (chỉ API).
- Không xử lý "giá trị quota mặc định" cụ thể (spec để "còn treo") — vì đã chọn fail-open, Admin/giao dịch
  thanh toán tự tạo config khi cần, không cần seed giá trị mặc định.
