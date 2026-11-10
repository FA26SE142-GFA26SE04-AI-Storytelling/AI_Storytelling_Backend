# Gói và thanh toán cho phụ huynh (Luồng 2, đợt 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Phụ huynh mua được Gói Đọc, Gói Sáng tạo, Add-on qua QR SePay; gói chạy theo chu kỳ 1 tháng; lượt AI lấy từ sổ lượt `credit_lots`; xem được lượt còn lại và lịch sử giao dịch.

**Architecture:** Backend thêm hai bảng `user_subscriptions` và `credit_lots`. `TokenQuotaService` được viết lại thành sổ lượt (cấp, đếm, trừ). Một `SubscriptionService` mới giữ toàn bộ quy tắc chu kỳ (áp gói khi Paid, chuyển chu kỳ, nhắc gia hạn) và được `PaymentService` cùng một worker nền gọi. Frontend làm lại màn Tài khoản → Thanh toán trên các endpoint mới.

**Tech Stack:** .NET 8, EF Core + PostgreSQL (Npgsql), xUnit + Moq; Next.js (App Router), TypeScript, Vitest.

**Spec:** `docs/superpowers/specs/2026-11-10-parent-plans-payment-design.md` (repo `AI_Storytelling_Backend`). Các mã D1–D12 trong plan này trỏ về bảng quyết định ở mục 3 của spec.

## Global Constraints

- Hai repo: `AI_Storytelling_Backend` (Task 1–6) và `AI_Storytelling_Frontend/src` (Task 7–8). Làm trên nhánh `feat/parent-plans-payment` tạo từ `main` ở mỗi repo. Không commit, không push khi người dùng chưa yêu cầu.
- Giá và lượt: Gói Đọc 49.000 đ / 0 lượt; Gói Sáng tạo 99.000 đ / 4 lượt mỗi chu kỳ; Add-on 29.000 đ / 2 lượt. Số lượt đọc từ `SubscriptionPlan.AiCredits`, không viết cứng.
- Chu kỳ dài 1 tháng, tính bằng `AddMonths(1)` trên UTC (D9). Giao dịch Pending hết hạn sau 15 phút. Nhắc gia hạn khi còn không quá 3 ngày.
- Mọi phép đọc "gói còn hiệu lực" là `Status == Active && EndAt > now`; "lô còn hạn" là `Remaining > 0 && (ExpiresAt == null || ExpiresAt > now)`.
- Một giao dịch Paid không bao giờ áp gói hoặc cộng lượt hai lần.
- Creator không bị kiểm tra quota (không đổi). Không drop bảng `token_quota_configs` (D4).
- Không thêm dependency mới. Thông báo lỗi cho người dùng viết tiếng Việt như code hiện có; tên định danh tiếng Anh.
- Backend: theo cấu trúc `Features/<tên>/{Interfaces,Services,DTOs}`, test đặt trong `tests/StoryPlatform.UnitTests/Features/...`, dựng mock theo mẫu `PaymentServiceTests`.
- Frontend: import bằng alias `@/app/...`, gọi API qua `services/`, không mock trong `app/`, màu lấy từ `styles/tokens.css`, mỗi component không quá ~250 dòng. Đọc `node_modules/next/dist/docs/` trước khi dùng API Next.js không quen.
- Không in nội dung `appsettings.*.json` / `.env` ra chat.

## Review Focus

1. Webhook và mark-paid cùng đến cho một giao dịch → chỉ một chu kỳ / một lô được tạo (Task 3).
2. Hai yêu cầu tạo truyện AI đồng thời khi còn 1 lượt → yêu cầu thứ hai nhận 409 (Task 2).
3. Worker ngừng chạy lâu hơn một chu kỳ: dòng `Scheduled` có cả khoảng thời gian đã trôi qua → kết thúc ở `Expired`, không để lại lượt dùng được (Task 3).
4. Giao dịch Pending đã quá `ExpiresAt` nhưng worker chưa quét → không được chặn phụ huynh tạo giao dịch mới (Task 4).
5. Thanh toán ngày 31/01 → chu kỳ kết thúc 28/02 (hoặc 29/02); dòng `Scheduled` nối đúng vào `EndAt` đó, không hở và không chồng (Task 3).

---

### Task 1: Dữ liệu — enum, entity, cấu hình EF, migration

**Việc này làm gì (nói dễ hiểu):** Tạo chỗ lưu cho mô hình gói mới. Bảng gói có thêm "loại gói" và ba gói mới được đưa vào sẵn. Hai bảng mới ghi lại mỗi phụ huynh đang ở chu kỳ gói nào và đang có những "lô lượt" nào. Lượt còn dư của người đã mua gói cũ được chuyển sang bảng mới để không ai mất lượt.

**Cần thêm / còn thiếu:**
- Đã có: bảng `subscription_plans` (seed gói Personal), `payment_transactions`, `token_quota_configs`; mẫu cấu hình EF và migration.
- Cần thêm: enum `PlanType`, `SubscriptionStatus`, `CreditType`, `CreditSource`; cột `PlanType`; đổi tên `QuotaAmount` → `AiCredits`; seed Gói Đọc, Gói Sáng tạo, Add-on; bảng `user_subscriptions`, `credit_lots`; chuyển lượt cũ thành lô `Legacy`; `NotificationType.SubscriptionExpiring`.
- Còn thiếu / ngoài phạm vi: cột `Audience`, `SubmitCredits`, `MaxChildProfiles`, `Benefits` (D12, đợt Creator và đợt giới hạn theo gói); bảng `credit_holds`, `credit_adjustments`, `payment_reconciliation_exceptions` (đợt 2).

**Files:**
- Create: `src/Core/StoryPlatform.Domain/Enums/PlanType.cs`, `SubscriptionStatus.cs`, `CreditType.cs`, `CreditSource.cs`
- Create: `src/Core/StoryPlatform.Domain/Entities/UserSubscription.cs`, `CreditLot.cs`
- Create: `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/UserSubscriptionConfiguration.cs`, `CreditLotConfiguration.cs`
- Create: migration `AddSubscriptionCyclesAndCreditLots` (sinh bằng `dotnet ef`)
- Modify: `src/Core/StoryPlatform.Domain/Entities/SubscriptionPlan.cs`, `src/Core/StoryPlatform.Domain/Enums/NotificationType.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/SubscriptionPlanConfiguration.cs`, `src/Core/StoryPlatform.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify (đổi tên cơ học `QuotaAmount` → `AiCredits`): `PaymentService.cs`, `PaymentDtos.cs`, `tests/.../PaymentServiceTests.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/Notifications/NotificationGroups.cs`, `tests/.../Notifications/NotificationGroupsTests.cs` (xếp `SubscriptionExpiring` cùng nhóm với `PaymentConfirmed`)

**Interfaces:**
- Produces:
  - `enum PlanType { Legacy = 0, Reading = 1, Creative = 2, AddOn = 3 }`
  - `enum SubscriptionStatus { Scheduled = 1, Active = 2, Superseded = 3, Expired = 4 }`
  - `enum CreditType { AI = 1 }`
  - `enum CreditSource { Trial = 1, Cycle = 2, AddOn = 3, Legacy = 4 }`
  - `SubscriptionPlan`: thêm `PlanType PlanType`, `int AiCredits` (thay `QuotaAmount`)
  - `UserSubscription : BaseEntity { int UserId; UserAccount? User; int PlanId; SubscriptionPlan? Plan; DateTime StartAt; DateTime EndAt; SubscriptionStatus Status; int PaymentId; PaymentTransaction? Payment; DateTime? RenewalReminderSentAt; }`
  - `CreditLot : BaseEntity { int UserId; UserAccount? User; CreditType CreditType; CreditSource Source; int Total; int Remaining; DateTime? ExpiresAt; int? PaymentId; PaymentTransaction? Payment; }`
  - `NotificationType.SubscriptionExpiring = 32`
  - `DbSet<UserSubscription> UserSubscriptions`, `DbSet<CreditLot> CreditLots`

- [ ] **Step 1:** Tạo nhánh `feat/parent-plans-payment` từ `main` (`git switch -c feat/parent-plans-payment main`). File spec và plan đang untracked sẽ đi theo.
- [ ] **Step 2:** Thêm 4 enum và 2 entity đúng như khối Interfaces. Thêm `PlanType`, đổi `QuotaAmount` → `AiCredits` trong `SubscriptionPlan`; sửa các chỗ dùng cho build được (DTO tạm giữ tên trường `QuotaAmount` tới Task 4).
- [ ] **Step 3:** Cấu hình EF.
  - `user_subscriptions`: `Status` lưu chuỗi (max 20); FK `User`, `Plan`, `Payment` đều `Restrict`; unique index `PaymentId`; index `(UserId, Status)`; query filter `!IsDeleted`.
  - `credit_lots`: `CreditType`, `Source` lưu chuỗi (max 20); FK `User`, `Payment` `Restrict`; unique index `PaymentId` có filter `"PaymentId" IS NOT NULL`; unique index `UserId` có filter `"Source" = 'Trial' AND "IsDeleted" = false`; index `(UserId, CreditType)`; query filter `!IsDeleted`.
  - `subscription_plans`: `PlanType` lưu chuỗi (max 20), mặc định `Legacy`. Seed theo bảng mục 4.2 của spec (Id 1 Personal/Legacy/49000/50/inactive; Id 2 "Gói Đọc"/Reading/49000/0; Id 3 "Gói Sáng tạo"/Creative/99000/4; Id 4 "Add-on"/AddOn/29000/2), cùng `SeedCreatedAt`.
- [ ] **Step 4:** Sinh migration:
  `dotnet ef migrations add AddSubscriptionCyclesAndCreditLots --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Api`
  Kiểm tra file sinh ra dùng `RenameColumn` cho `QuotaAmount` → `AiCredits` (nếu EF sinh drop + add thì sửa tay thành rename).
- [ ] **Step 5:** Thêm vào cuối `Up` câu SQL chuyển dữ liệu (D3), và không có lệnh nào drop `token_quota_configs`:

```sql
INSERT INTO credit_lots ("UserId", "CreditType", "Source", "Total", "Remaining", "ExpiresAt", "PaymentId", "CreatedAt", "IsDeleted")
SELECT "UserId", 'AI', 'Legacy', "QuotaLimit" - "QuotaUsed", "QuotaLimit" - "QuotaUsed", NULL, NULL, NOW() AT TIME ZONE 'UTC', FALSE
FROM token_quota_configs
WHERE "Scope" = 'Personal' AND "IsDeleted" = FALSE AND "UserId" IS NOT NULL AND "QuotaLimit" - "QuotaUsed" > 0;
```

- [ ] **Step 6:** Kiểm tra.
  Run: `dotnet build StoryPlatform.sln` → 0 lỗi.
  Run: `dotnet ef migrations has-pending-model-changes --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Api` → báo không còn thay đổi chờ.
  Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~Payments|FullyQualifiedName~NotificationGroups"` → PASS.

---

### Task 2: Sổ lượt — viết lại `TokenQuotaService`

**Việc này làm gì (nói dễ hiểu):** Đổi cách hệ thống đếm lượt tạo truyện AI. Thay vì một con số "hạn mức / đã dùng", mỗi lần được cấp lượt là một "lô" có hạn dùng riêng. Khi phụ huynh tạo truyện, hệ thống trừ 1 lượt ở lô sắp hết hạn nhất. Tài khoản mới có hồ sơ trẻ được tặng 1 lượt dùng thử. Phụ huynh có một địa chỉ mới để xem mình còn bao nhiêu lượt, tách theo từng loại.

**Cần thêm / còn thiếu:**
- Đã có: `ITokenQuotaService.EnsureWithinQuotaAsync` / `IncrementUsageAsync` được `AIStoryInputService` gọi; `GET tokenquota/child/{id}`.
- Cần thêm: đếm và trừ lượt theo `credit_lots`; cấp lượt dùng thử kiểu lười (D1); `GrantAsync` để nơi khác cấp lô; `GET tokenquota/me`; hết lượt → 409 kèm gợi ý nâng gói.
- Còn thiếu / ngoài phạm vi: giữ – trừ – trả lượt (`credit_holds`, đợt 2); Admin cấp lượt (UC-L2-08, đợt 2) — vì vậy gỡ `POST/GET tokenquota/config` (D4); quota của Creator.

**Files:**
- Modify (viết lại): `src/Core/StoryPlatform.Application/Features/TokenQuota/Services/TokenQuotaService.cs`, `Interfaces/ITokenQuotaService.cs`, `DTOs/TokenQuotaDtos.cs`
- Modify: `src/Core/StoryPlatform.Api/Controllers/TokenQuotaController.cs`
- Test (viết lại): `tests/StoryPlatform.UnitTests/Features/TokenQuota/TokenQuotaServiceTests.cs`, `TokenQuotaControllerAuthorizationTests.cs`
- Modify nếu vỡ build: `tests/StoryPlatform.UnitTests/AIStoryInputService*Tests.cs`, `Phase1To4SequentialWorkflowTests.cs`, `tests/StoryPlatform.IntegrationTests/Api/ProtectedEndpointApiTests.cs`

**Interfaces:**
- Consumes: `CreditLot`, `UserSubscription`, các enum của Task 1.
- Produces (`ITokenQuotaService`):
  - `Task EnsureWithinQuotaAsync(int childProfileId, CancellationToken ct = default)` — giữ chữ ký
  - `Task IncrementUsageAsync(int childProfileId, CancellationToken ct = default)` — giữ chữ ký
  - `Task<TokenQuotaStatusDto> GetStatusForChildAsync(int requestingUserId, int childProfileId, CancellationToken ct = default)` — giữ chữ ký
  - `Task<MyQuotaDto> GetMyQuotaAsync(int userId, CancellationToken ct = default)`
  - `Task<bool> GrantAsync(int userId, CreditSource source, int amount, DateTime? expiresAt, int? paymentId, CancellationToken ct = default)` — trả `false` và không làm gì nếu đã có lô gắn `paymentId` đó, hoặc `amount <= 0`
  - Gỡ: `SetConfigAsync`, `ListConfigsAsync`, `CreditAsync`
- DTO mới:
  - `MyQuotaDto { QuotaPlanDto? Plan; QuotaPlanDto? NextPlan; int TrialCredits; int CycleCredits; int AddOnCredits; int OtherCredits; int TotalRemaining; }`
  - `QuotaPlanDto { string PlanType; string PlanName; DateTime StartAt; DateTime EndAt; }`
  - Gỡ `SetTokenQuotaConfigRequestDto`, `TokenQuotaConfigDto`; giữ `TokenQuotaStatusDto`.
- Endpoint: `GET api/v1/TokenQuota/me` `[Authorize(Roles = "User")]`; gỡ `POST config`, `GET config`.

- [ ] **Step 1: Viết test fail** trong `TokenQuotaServiceTests` (thay toàn bộ test cũ). Mỗi test dựng danh sách `CreditLot` / `ChildProfile` / `UserSubscription` trong bộ nhớ, repository mock lọc bằng `predicate.Compile()` như mẫu hiện có:

| Test | Sắp đặt | Khẳng định |
|---|---|---|
| `EnsureWithinQuotaAsync_NoLotsAndNoActiveChild_ThrowsConflict` | chủ hồ sơ không có lô, hồ sơ `Draft` | `ConflictException` |
| `EnsureWithinQuotaAsync_FirstCallWithActiveChild_GrantsOneTrialCredit` | không có lô, hồ sơ `Active` | không ném; thêm đúng 1 lô `Source=Trial, Total=1, Remaining=1, ExpiresAt=null` |
| `EnsureWithinQuotaAsync_TrialAlreadyUsed_DoesNotGrantAgain` | có lô `Trial` `Remaining=0` | `ConflictException`; không `AddAsync` lô nào |
| `EnsureWithinQuotaAsync_OnlyExpiredLots_ThrowsConflict` | lô `Cycle` `Remaining=4`, `ExpiresAt` = hôm qua; lô `Trial` `Remaining=0` | `ConflictException` |
| `IncrementUsageAsync_ConsumesLotExpiringSoonestFirst` | lô A `Trial` không hạn `Remaining=1`; lô B `Cycle` hạn +10 ngày `Remaining=4`; lô C `AddOn` hạn +3 ngày `Remaining=2` | sau 1 lần: C=1, B=4, A=1 |
| `IncrementUsageAsync_NoUsableLot_ThrowsConflict` | mọi lô `Remaining=0` | `ConflictException` |
| `IncrementUsageAsync_AcquiresAccountLock` | 1 lô còn lượt | `AcquireTransactionLockAsync(ownerUserId, ...)` được gọi 1 lần trước khi cập nhật lô |
| `TwoChildrenOfSameOwner_ShareOneBalance` | 2 hồ sơ cùng chủ, 1 lô `Remaining=1` | trừ qua hồ sơ 1 xong, `Ensure` qua hồ sơ 2 ném `ConflictException` |
| `GrantAsync_SamePaymentIdTwice_GrantsOnce` | gọi 2 lần `paymentId=7` | lần 1 `true`, lần 2 `false`; chỉ 1 `AddAsync` |
| `GetMyQuotaAsync_SplitsCreditsBySource` | `Trial` 1, `Cycle` 3, `AddOn` 2, `Legacy` 5, một lô `Cycle` đã hết hạn 4; subscription `Active` Creative + `Scheduled` Reading | `TrialCredits=1, CycleCredits=3, AddOnCredits=2, OtherCredits=5, TotalRemaining=11`; `Plan.PlanType="Creative"`, `NextPlan.PlanType="Reading"` |
| `GetMyQuotaAsync_FreeAccount_PlanIsNull` | không có subscription | `Plan == null`, `NextPlan == null` |
| `GetStatusForChildAsync_Owner_ReturnsAccountBalance` | chủ có tổng 6 lượt, gói Active tới ngày X | `IsUnlimited=false, Remaining=6, QuotaLimit=6, QuotaUsed=0, PeriodEnd=X` |
| `GetStatusForChildAsync_NonOwnerNonAdmin_ThrowsForbidden` | giữ như test cũ | `ForbiddenException` |

- [ ] **Step 2:** Run `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~TokenQuotaServiceTests` → FAIL (chưa có thành viên mới).
- [ ] **Step 3: Viết `TokenQuotaService`.** Các quyết định cần giữ:
  - Lượt dùng thử chỉ cấp cho tài khoản role `User` có ít nhất một `ChildProfile` `Status == Active`, chưa xoá, và chưa có lô `Trial` nào (kể cả lô đã dùng hết). Cấp ở cả `EnsureWithinQuotaAsync` và `GetMyQuotaAsync`.
  - Thứ tự trừ: `ExpiresAt` tăng dần, null xếp cuối, rồi `Id` tăng dần.
  - `IncrementUsageAsync` gọi `AcquireTransactionLockAsync(ownerUserId)` rồi mới đọc lô (transaction đã được `AIStoryInputService.SubmitAsync` mở). Khoá dùng chung không gian số với khoá truyện, chấp nhận được vì chỉ gây chờ thừa.
  - Thông báo 409: `"Bạn đã hết lượt tạo truyện AI. Hãy nâng lên Gói Sáng tạo hoặc mua Add-on để tạo thêm."`
  - `EnsureWithinQuotaAsync` không ghi gì ngoài lô dùng thử (giữ ràng buộc "một lần attach mỗi dòng" đã ghi chú trong code cũ).
- [ ] **Step 4:** Sửa `TokenQuotaController`: thêm `GET me`, gỡ 2 endpoint config. Viết lại `TokenQuotaControllerAuthorizationTests`: `me` yêu cầu role `User`; `child/{id}` yêu cầu đăng nhập; không còn action `SetConfig`, `ListConfigs`.
- [ ] **Step 5:** Sửa các test khác bị vỡ do đổi interface (chỉ chỉnh mock, không đổi ý nghĩa test).
- [ ] **Step 6:** Run `dotnet test tests/StoryPlatform.UnitTests` → PASS toàn bộ.

---

### Task 3: Chu kỳ gói — `SubscriptionService` và worker

**Việc này làm gì (nói dễ hiểu):** Đây là "bộ não" của gói tháng. Khi tiền về, nó quyết định gói bắt đầu ngay hay nối sau gói đang chạy, cấp lượt cho Gói Sáng tạo, cộng lượt Add-on. Một tiến trình chạy nền định kỳ cho gói hết hạn, bật gói đã gia hạn sớm, và gửi thông báo nhắc trước 3 ngày.

**Cần thêm / còn thiếu:**
- Đã có: `PaymentExpirySweepWorker` làm mẫu worker; `INotificationService.CreateAsync`; `IUnitOfWork` có transaction và advisory lock.
- Cần thêm: áp gói theo loại (bảng 5.3 của spec), idempotent theo `PaymentId`; chuyển chu kỳ; nhắc gia hạn; worker `SubscriptionCycleWorker`.
- Còn thiếu / ngoài phạm vi: Gói Creator và hạn mức tháng Creator; danh sách ngoại lệ đối soát (UC-L2-13); chuyển hồ sơ trẻ sang "chỉ xem" khi về Miễn phí (Luồng 1).

**Files:**
- Create: `src/Core/StoryPlatform.Application/Features/Payments/Interfaces/ISubscriptionService.cs`
- Create: `src/Core/StoryPlatform.Application/Features/Payments/Services/SubscriptionService.cs`
- Create: `src/Core/StoryPlatform.Infrastructure/BackgroundServices/SubscriptionCycleWorker.cs`
- Modify: `src/Core/StoryPlatform.Application/DependencyInjection.cs`, `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/Payments/SubscriptionServiceTests.cs`

**Interfaces:**
- Consumes: `ITokenQuotaService.GrantAsync` (Task 2); entity và enum của Task 1.
- Produces (`ISubscriptionService`):
  - `Task<UserSubscription?> GetActiveAsync(int userId, DateTime now, CancellationToken ct = default)`
  - `Task ApplyPaidTransactionAsync(PaymentTransaction transaction, SubscriptionPlan plan, DateTime now, CancellationToken ct = default)`
  - `Task AdvanceCyclesAsync(int userId, DateTime now, CancellationToken ct = default)`
  - `Task<int> SweepAsync(DateTime now, CancellationToken ct = default)` — chuyển chu kỳ cho mọi tài khoản đến hạn rồi gửi nhắc; trả số tài khoản đã xử lý
- Worker: `SubscriptionCycleWorkerOptions { const string SectionName = "Payments:SubscriptionCycleWorker"; int IntervalMinutes = 15; }`, kẹp 1–60 phút, cấu trúc y hệt `PaymentExpirySweepWorker`.

- [ ] **Step 1: Viết test fail** `SubscriptionServiceTests`. Dùng `now = 2026-11-10T03:00:00Z` trừ khi ghi khác; `ITokenQuotaService` là mock, các lô `AddOn` có sẵn nằm trong repository mock của `CreditLot`.

| Test | Sắp đặt | Khẳng định |
|---|---|---|
| `Apply_Creative_NoCurrentPlan_StartsCycleAndGrantsCredits` (UC-L2-02 AC1) | không có subscription; plan Creative `AiCredits=4`; `PaidAt=now` | thêm 1 dòng `Active`, `StartAt=now`, `EndAt=2026-12-10T03:00:00Z`, `PaymentId=tx.Id`; `GrantAsync(userId, Cycle, 4, EndAt, tx.Id)` 1 lần |
| `Apply_Reading_NoCurrentPlan_StartsCycleWithoutCredits` | plan Reading `AiCredits=0` | 1 dòng `Active` 1 tháng; `GrantAsync` không được gọi |
| `Apply_Creative_WhileCreativeActive_SchedulesAfterCurrentEnd` (AC2) | Creative `Active` còn 5 ngày (`EndAt=now+5d`) | dòng mới `Scheduled`, `StartAt=now+5d`, `EndAt=StartAt.AddMonths(1)`; dòng cũ giữ `Active`; `GrantAsync` không được gọi |
| `Apply_Reading_WhileCreativeActive_SchedulesDowngradeAfterCurrentEnd` | Creative `Active` | dòng Reading `Scheduled` nối sau `EndAt` hiện tại |
| `Apply_Renewal_WithExistingScheduled_AppendsToEndOfChain` | `Active` kết thúc T1, `Scheduled` T1→T2 | dòng mới `Scheduled`, `StartAt=T2` |
| `Apply_Creative_WhileReadingActive_UpgradesImmediately` (AC4) | Reading `Active` còn 10 ngày; 1 lô `AddOn` `Remaining=1`, `ExpiresAt`=EndAt cũ | dòng Reading → `Superseded`; dòng Creative `Active`, `StartAt=now`, `EndAt=now.AddMonths(1)`; `GrantAsync(…, Cycle, 4, EndAt mới, tx.Id)`; lô `AddOn` có `ExpiresAt`=EndAt mới |
| `Apply_Upgrade_ShiftsExistingScheduledRowsAfterNewCycle` (D7) | Reading `Active` + Reading `Scheduled` T1→T2 | dòng `Scheduled` thành `StartAt`=EndAt Creative mới, `EndAt=StartAt.AddMonths(1)` |
| `Apply_AddOn_WithActivePlan_GrantsLotExpiringAtCycleEnd` (UC-L2-03 AC1, AC3) | Reading `Active`, `EndAt=E`; plan AddOn `AiCredits=2` | `GrantAsync(userId, AddOn, 2, E, tx.Id)`; `EndAt` của subscription không đổi; không thêm subscription |
| `Apply_AddOn_NoActivePlan_GrantsLotValidOneMonthFromPaidAt` (D6) | không có gói; `PaidAt=now` | `GrantAsync(userId, AddOn, 2, now.AddMonths(1), tx.Id)` |
| `Apply_LegacyPlan_GrantsNonExpiringLegacyLot` | plan Legacy `AiCredits=50` | `GrantAsync(userId, Legacy, 50, null, tx.Id)` |
| `Apply_SameTransactionTwice_AppliesOnce` (Review Focus 1) | đã có subscription `PaymentId=tx.Id` | không thêm dòng, không `GrantAsync` |
| `Apply_PaidOnJan31_EndsOnLastDayOfFebruary` (Review Focus 5) | `now=2027-01-31T10:00:00Z` | `EndAt=2027-02-28T10:00:00Z`; gia hạn tiếp theo có `StartAt` đúng bằng `EndAt` đó |
| `Apply_RunsInsideTransactionWithAccountLock` | bất kỳ | `BeginTransactionAsync`, `AcquireTransactionLockAsync(userId)`, `CommitTransactionAsync` theo thứ tự; khi thân ném lỗi thì `RollbackTransactionAsync` và lỗi được ném lại |
| `Advance_ActivePastEnd_NoRenewal_Expires` (UC-L2-11 AC2) | `Active`, `EndAt=now-1m` | → `Expired`; `GetActiveAsync` trả null |
| `Advance_ScheduledDue_ActivatesAndGrantsCycleCredits` (AC1) | Creative `Active` đã hết, Creative `Scheduled` `StartAt<=now<EndAt`, `PaymentId=9` | cũ → `Expired`; mới → `Active`; `GrantAsync(userId, Cycle, 4, EndAt mới, 9)` |
| `Advance_ScheduledReading_ActivatesWithoutCredits` | Reading `Scheduled` đến hạn | `Active`; `GrantAsync` không được gọi |
| `Advance_WholeScheduledWindowAlreadyPassed_EndsExpired` (Review Focus 3) | `Active` hết từ lâu; `Scheduled` có `EndAt < now` | cả hai `Expired`; không còn dòng `Active` |
| `GetActiveAsync_ActiveRowPastEnd_ReturnsNull` | `Status=Active`, `EndAt=now-1s`, worker chưa chạy | null |
| `Sweep_ActiveEndingWithinThreeDays_SendsReminderOnce` (AC3) | `Active`, `EndAt=now+3d`, không có `Scheduled`, `RenewalReminderSentAt=null` | `CreateAsync(userId, SubscriptionExpiring, payload có subscriptionId/planName/endAt)` 1 lần; `RenewalReminderSentAt=now`; chạy `SweepAsync` lần hai không gửi thêm |
| `Sweep_AlreadyRenewed_DoesNotRemind` | `Active` còn 2 ngày + có `Scheduled` | không gửi |
| `Sweep_EndingInFourDays_DoesNotRemind` | `EndAt=now+4d` | không gửi |

- [ ] **Step 2:** Run `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~SubscriptionServiceTests` → FAIL.
- [ ] **Step 3: Viết `SubscriptionService`.** Các quyết định cần giữ:
  - `ApplyPaidTransactionAsync` : mở transaction → khoá theo `PayerUserId` → kiểm tra idempotent (đã có `UserSubscription` với `PaymentId`, hoặc để `GrantAsync` tự chặn với Add-on / Legacy) → gọi `AdvanceCyclesAsync` → rẽ nhánh theo `plan.PlanType` → commit. Lỗi thì rollback và ném lại (nơi gọi ở Task 4 bắt và ghi audit).
  - Mốc thời gian chu kỳ lấy từ `transaction.PaidAt ?? now`.
  - Quy tắc rẽ nhánh Reading / Creative: không có gói còn hiệu lực → Active mới; gói hiện tại là Reading và gói mua là Creative → nâng gói ngay; còn lại → `Scheduled` nối vào `EndAt` muộn nhất trong các dòng `Active` + `Scheduled` của tài khoản.
  - Nâng gói: chỉ cập nhật `ExpiresAt` cho các lô `Source == AddOn` còn hạn tại thời điểm đó.
  - `AdvanceCyclesAsync` lặp cho tới khi ổn định: hết hạn dòng `Active` quá `EndAt`; nếu không còn `Active` thì lấy dòng `Scheduled` có `StartAt` sớm nhất với `StartAt <= now` để kích hoạt (cấp lô `Cycle` gắn `PaymentId` của dòng đó), rồi kiểm tra lại.
  - `SweepAsync`: tập tài khoản = có dòng `Active` với `EndAt <= now` hoặc dòng `Scheduled` với `StartAt <= now`. Mỗi tài khoản xử lý trong transaction + khoá riêng; lỗi ở một tài khoản không chặn tài khoản khác. Sau đó mới quét nhắc gia hạn (`EndAt - now <= 3 ngày`).
- [ ] **Step 4:** Viết `SubscriptionCycleWorker` (gọi `SweepAsync(DateTime.UtcNow, token)`), đăng ký `ISubscriptionService` (scoped) ở Application DI, options + hosted service ở Infrastructure DI ngay sau `PaymentExpirySweepWorker`.
- [ ] **Step 5:** Run `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~SubscriptionServiceTests` → PASS. Run `dotnet build StoryPlatform.sln` → 0 lỗi.

---

### Task 4: Thanh toán — xem gói, tạo giao dịch, áp gói, lịch sử

**Việc này làm gì (nói dễ hiểu):** Nối phần mua hàng với "bộ não" gói. Màn Mua gói nhận được ba gói mới và biết Add-on có đang bị khoá không. Hệ thống chặn việc tạo hai giao dịch gói cùng lúc. Khi tiền về (tự động hoặc Admin xác nhận), gói được áp đúng theo loại. Phụ huynh có thêm địa chỉ để xem lại các lần mua của mình.

**Cần thêm / còn thiếu:**
- Đã có: `GET payment/plans`, `POST payment/transactions`, webhook SePay, `mismatched`, `mark-paid`, audit `PaymentCreditFailed`, thông báo `PaymentConfirmed`.
- Cần thêm: thông tin `planType` / `isPurchasable` / `lockedReason` ở danh sách gói; quy tắc Pending mới; điều kiện mua Add-on; áp gói qua `ISubscriptionService`; `GET payment/transactions/me`.
- Còn thiếu / ngoài phạm vi: lọc gói theo `Audience` và cho Creator mua (đợt Creator); danh sách ngoại lệ và thao tác áp lại gói (UC-L2-13); hoàn tiền.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Payments/Services/PaymentService.cs`, `Interfaces/IPaymentService.cs`, `DTOs/PaymentDtos.cs`
- Modify: `src/Core/StoryPlatform.Api/Controllers/PaymentController.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/Payments/PaymentServiceTests.cs`, `PaymentControllerAuthorizationTests.cs`

**Interfaces:**
- Consumes: `ISubscriptionService.GetActiveAsync`, `ApplyPaidTransactionAsync` (Task 3). `PaymentService` không còn phụ thuộc `ITokenQuotaService`.
- Produces (`IPaymentService`):
  - `Task<List<SubscriptionPlanDto>> ListActivePlansAsync(int userId, CancellationToken ct = default)` — thêm tham số `userId`
  - `Task<PagedResult<PaymentTransactionDto>> ListMyTransactionsAsync(int userId, int pageIndex, int pageSize, CancellationToken ct = default)` — dùng `Common/Models/PagedResult.cs` sẵn có
  - các phương thức khác giữ chữ ký
- DTO:
  - `SubscriptionPlanDto { int Id; string Name; string PlanType; int PriceVnd; int AiCredits; bool IsPurchasable; string? LockedReason; }` (bỏ `QuotaAmount`)
  - `PaymentTransactionDto` thêm `string PlanType`, `DateTime CreatedAt`
- Endpoint: `GET api/v1/Payment/transactions/me?pageIndex=1&pageSize=20` `[Authorize]`.

- [ ] **Step 1: Viết / sửa test fail** trong `PaymentServiceTests` (đổi mock `ITokenQuotaService` thành `ISubscriptionService`; `MakePlan` nhận thêm `PlanType`):

| Test | Sắp đặt | Khẳng định |
|---|---|---|
| `ListActivePlansAsync_ExcludesLegacyAndInactive` | Personal Legacy inactive, Reading, Creative, AddOn, một gói Creative inactive | trả đúng 3 gói Reading/Creative/AddOn với `PlanType`, `AiCredits` đúng |
| `ListActivePlansAsync_FreeAccount_AddOnIsLocked` (UC-L2-01 AC1) | `GetActiveAsync` → null | AddOn `IsPurchasable=false`, `LockedReason="Hãy đăng ký Gói Đọc hoặc Gói Sáng tạo trước khi mua Add-on."`; hai gói kia `IsPurchasable=true`, `LockedReason=null` |
| `ListActivePlansAsync_WithActivePlan_AddOnIsPurchasable` | `GetActiveAsync` → dòng Active | AddOn `IsPurchasable=true` |
| `CreateTransactionAsync_LegacyPlan_ThrowsBadRequest` | plan Legacy, `IsActive=true` | `BadRequestException` |
| `CreateTransactionAsync_PendingCreativeExists_BuyingReading_ThrowsConflict` (UC-L2-02 AC3) | giao dịch Pending còn hạn của plan Creative | `ConflictException` |
| `CreateTransactionAsync_PendingAddOnExists_BuyingReading_Succeeds` | Pending Add-on còn hạn | tạo được giao dịch Reading |
| `CreateTransactionAsync_StalePendingPastExpiry_DoesNotBlock` (Review Focus 4) | Pending Reading có `ExpiresAt = now-1m` | tạo được giao dịch mới |
| `CreateTransactionAsync_AddOn_NoActivePlan_ThrowsConflict` (UC-L2-03 AC2) | `GetActiveAsync` → null | `ConflictException` |
| `CreateTransactionAsync_AddOn_PendingAddOnExists_ThrowsConflict` (D8) | Pending Add-on còn hạn, có gói Active | `ConflictException` |
| `CreateTransactionAsync_AddOn_WithActivePlan_CreatesPending29000` | gói Active | giao dịch `Amount=29000`, `Status=Pending`, mã bắt đầu `SEPAY` dài 15 ký tự, `ExpiresAt` ≈ now+15 phút |
| `HandleWebhookAsync_AmountMatches_AppliesPlanViaSubscriptionService` (UC-L2-05 AC1) | Pending Creative 99000, webhook 99000 | `Status=Paid`; `ApplyPaidTransactionAsync(tx, plan, …)` 1 lần; `PaymentConfirmed` gửi 1 lần |
| `HandleWebhookAsync_AmountMismatch_DoesNotApplyPlan` (AC3) | webhook 90000 | `MismatchAmount`; `ApplyPaidTransactionAsync` không được gọi |
| `HandleWebhookAsync_ApplyThrows_StaysPaidAndWritesCreditFailedAudit` | `ApplyPaidTransactionAsync` ném lỗi | giao dịch vẫn `Paid`; audit `PaymentCreditFailed` với `reason="ApplyPlanThrew"`; không ném ra ngoài |
| `MarkPaidManuallyAsync_ExpiredTransaction_AppliesPlan` | giao dịch `Expired` | `Paid`; `ApplyPaidTransactionAsync` 1 lần |
| `ListMyTransactionsAsync_ReturnsOnlyOwnNewestFirst` (UC-L2-12 AC1) | giao dịch của user A và B | chỉ của A, sắp theo `CreatedAt` giảm dần |
| `ListMyTransactionsAsync_QrOnlyForUnexpiredPending` (AC2) | Pending còn hạn, Pending quá hạn, Paid | chỉ dòng Pending còn hạn có `QrCodeUrl`; dòng Pending quá hạn có `Status="Expired"`, `QrCodeUrl=null` |

  Giữ các test cũ còn đúng (webhook sai header, không khớp mã, đã Paid thì bỏ qua, mark-paid giao dịch đã Paid → Conflict, plan không tìm thấy → audit); xoá các test `CreditAsync`.
- [ ] **Step 2:** Run `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~PaymentServiceTests` → FAIL.
- [ ] **Step 3: Sửa `PaymentService`.** Các quyết định cần giữ:
  - "Pending còn hạn" = `Status == Pending && ExpiresAt > now`. Quy tắc trùng: gói mua thuộc {Reading, Creative} → chặn nếu có Pending còn hạn của plan thuộc {Reading, Creative}; gói mua là AddOn → chặn nếu có Pending còn hạn của plan AddOn.
  - Thông báo: gói đang chờ → `"Bạn đang có một giao dịch gói chờ thanh toán. Hãy hoàn tất hoặc chờ giao dịch đó hết hạn."`; Add-on không có gói → dùng đúng câu `LockedReason` ở trên; role khác `User` → `"Chỉ tài khoản phụ huynh mới được mua gói này."`
  - `CreditQuotaSafelyAsync` đổi tên thành `ApplyPlanSafelyAsync`, gọi `_subscriptionService.ApplyPaidTransactionAsync(transaction, plan, DateTime.UtcNow, ct)`; giữ nguyên hành vi "không ném, ghi audit `PaymentCreditFailed`".
  - Thứ tự trong webhook / mark-paid giữ như cũ: lưu Paid → audit → thông báo → áp gói.
- [ ] **Step 4:** Sửa `PaymentController`: `ListActivePlans` truyền `GetCurrentUserId()`; thêm action `ListMyTransactions` (`pageSize` kẹp 1–50). Cập nhật `PaymentControllerAuthorizationTests`: action mới yêu cầu đăng nhập.
- [ ] **Step 5:** Run `dotnet test tests/StoryPlatform.UnitTests` → PASS toàn bộ.

---

### Task 5: Xoá tài khoản — huỷ chu kỳ và lô lượt

**Việc này làm gì (nói dễ hiểu):** Khi phụ huynh xoá tài khoản, gói đang chạy, gói đã gia hạn sớm và các lượt còn dư phải bị huỷ theo, để không còn dữ liệu "sống" gắn với một tài khoản đã xoá.

**Cần thêm / còn thiếu:**
- Đã có: `AccountDeletionService` đã chuyển giao dịch Pending sang Expired trong transaction xoá.
- Cần thêm: chuyển `user_subscriptions` Active / Scheduled sang Expired và soft delete; soft delete `credit_lots` (D11).
- Còn thiếu / ngoài phạm vi: cảnh báo ở `delete-preview` (Luồng 1, đợt sau); ẩn danh giao dịch (không đổi so với hiện tại).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/Auth/Services/AccountDeletionService.cs` (ngay sau khối xử lý `pendingPayments`)
- Test: `tests/StoryPlatform.UnitTests/Features/Auth/AccountDeletionServiceTests.cs`

**Interfaces:**
- Consumes: `UserSubscription`, `CreditLot` (Task 1). Không tạo interface mới.

- [ ] **Step 1: Viết test fail** `DeleteAsync_CancelsSubscriptionsAndCreditLots` (đặt tên theo phương thức xoá thật trong file test): tài khoản có 1 dòng `Active`, 1 dòng `Scheduled`, 1 dòng `Expired` cũ, 2 lô lượt → sau khi xoá: hai dòng đầu `Status=Expired`, `IsDeleted=true`, `UpdatedAt=now`; dòng `Expired` cũ không bị đụng; cả 2 lô `IsDeleted=true`.
- [ ] **Step 2:** Run `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~AccountDeletionServiceTests` → FAIL.
- [ ] **Step 3:** Thêm khối xử lý vào transaction xoá, theo đúng kiểu của khối `pendingPayments`.
- [ ] **Step 4:** Run lại lệnh ở Step 2 → PASS.

---

### Task 6: Kiểm tra backend và tài liệu API

**Việc này làm gì (nói dễ hiểu):** Chạy toàn bộ kiểm thử, thử đi một vòng mua gói thật trên máy, rồi cập nhật tài liệu hướng dẫn API để người làm frontend và người chấm đọc đúng mô hình mới.

**Cần thêm / còn thiếu:**
- Đã có: `docs/HUONG_DAN_SU_DUNG_API_LUONG_2.md`, bộ test đơn vị và tích hợp.
- Cần thêm: mô tả 3 gói, `tokenquota/me`, `payment/transactions/me`, các endpoint đã gỡ.
- Còn thiếu / ngoài phạm vi: Postman collection (chỉ cập nhật nếu file hiện có đã chứa nhóm Payment).

**Files:**
- Modify: `docs/HUONG_DAN_SU_DUNG_API_LUONG_2.md` (chỉ phần Payment / TokenQuota)

- [ ] **Step 1:** Run `dotnet build StoryPlatform.sln` → 0 lỗi, không thêm cảnh báo mới.
- [ ] **Step 2:** Run `dotnet test StoryPlatform.sln` → PASS. Test tích hợp cần hạ tầng không có sẵn thì ghi rõ test nào bị bỏ và vì sao.
- [ ] **Step 3:** Chạy thử cục bộ theo `README.md` / `DOCKER.md` (áp migration lên CSDL dev cục bộ, không phải AWS). Đi luồng: đăng nhập phụ huynh → `GET Payment/plans` (Add-on khoá) → `POST Payment/transactions` Gói Sáng tạo → gọi `POST Payment/webhook/sepay` với header xác thực của môi trường dev và `content` chứa mã → `GET TokenQuota/me` (Creative, 4 lượt chu kỳ) → mua Add-on tương tự (thêm 2 lượt) → gửi lại webhook cũ (lượt không đổi) → `GET Payment/transactions/me`.
- [ ] **Step 4:** Cập nhật tài liệu API.

---

### Task 7: Frontend — service, kiểu dữ liệu, thông báo, gỡ cấu hình quota Admin

**Việc này làm gì (nói dễ hiểu):** Dạy cho web biết các địa chỉ API mới và hình dạng dữ liệu mới, hiển thị được thông báo "gói sắp hết hạn", và bỏ màn cấu hình quota của Admin vì backend không còn hỗ trợ.

**Cần thêm / còn thiếu:**
- Đã có: `services/paymentService.ts` (plans, create), `types/backendContracts.ts`, màn Admin Token Quota, bộ kiểm tra độ phủ API (`npm run audit:api`).
- Cần thêm: gọi `Payment/transactions/me`, `TokenQuota/me`; kiểu `MyQuotaDto`, `QuotaPlanDto`; cập nhật `SubscriptionPlanDto`, `PaymentTransactionDto`; nhãn thông báo `SubscriptionExpiring`.
- Còn thiếu / ngoài phạm vi: màn Admin cấp lượt có lý do (đợt 2).

**Files (gốc `AI_Storytelling_Frontend/src`):**
- Modify: `app/services/paymentService.ts`, `app/types/backendContracts.ts`, `app/types/auth.ts` (kiểu `SubscriptionPlan` cũ nếu còn dùng)
- Modify: `app/lib/notifications.ts` (hoặc nơi thật sự ánh xạ loại thông báo sang chữ — tìm bằng `StorySummarySent`)
- Modify: `app/features/staff/AdminSystem.tsx` (gỡ `AdminQuota`), `app/features/staff/StaffApp.tsx` (gỡ mục `quota`), `app/services/staffService.ts` (gỡ `quotaConfigs`, `setQuota`, `QuotaInput`), `app/types/staff.ts` (gỡ `TokenQuotaConfig`)
- Modify: `docs/core-api-coverage.json` và các file kiểm kê controller nếu `npm run audit:api` yêu cầu
- Test: `tests/unit/services/luong2Contracts.test.ts` (thêm), `tests/unit/services/luong5Contracts.test.ts` và `coreCompletion.test.ts` (sửa theo phần gỡ), `tests/unit/guardian/notifications.test.ts`

**Interfaces:**
- Consumes: endpoint backend của Task 2 và Task 4.
- Produces:
  - `paymentService.plans(signal?)`, `paymentService.create(planId)` — giữ
  - `paymentService.myTransactions(pageIndex = 1, pageSize = 20, signal?)` → `ApiResponse<B.PagedResult<B.PaymentTransactionDto>>` gọi `GET /Payment/transactions/me`
  - `paymentService.myQuota(signal?)` → `ApiResponse<B.MyQuotaDto>` gọi `GET /TokenQuota/me`
  - Kiểu: `SubscriptionPlanDto { id; name; planType: 'Reading'|'Creative'|'AddOn'; priceVnd; aiCredits; isPurchasable; lockedReason: string|null }`; `PaymentTransactionDto` thêm `planType: string`, `createdAt: string`; `QuotaPlanDto { planType; planName; startAt; endAt }`; `MyQuotaDto { plan: QuotaPlanDto|null; nextPlan: QuotaPlanDto|null; trialCredits; cycleCredits; addOnCredits; otherCredits; totalRemaining }`. Dùng kiểu phân trang đã có trong `backendContracts.ts` nếu có, nếu không thì thêm `PagedResult<T>` khớp JSON của backend.

- [ ] **Step 1:** Tạo nhánh `feat/parent-plans-payment` từ `main` trong repo frontend.
- [ ] **Step 2: Viết test fail** (theo kiểu bảng `[tên, lời gọi, đường dẫn, body]` của `luong5Contracts.test.ts`):
  - `myTransactions()` gọi `GET /Payment/transactions/me?pageIndex=1&pageSize=20`
  - `myQuota()` gọi `GET /TokenQuota/me`
  - `staffService` không còn `quotaConfigs` / `setQuota` (xoá dòng test cũ)
  - thông báo loại `SubscriptionExpiring` với payload `{ planName: 'Gói Sáng tạo', endAt: '2026-12-10T03:00:00Z' }` cho ra câu có chứa `Gói Sáng tạo` và `sắp hết hạn`
- [ ] **Step 3:** Run `npm test` (trong `src`) → các test mới FAIL.
- [ ] **Step 4:** Sửa service, kiểu, thông báo; gỡ phần quota của Admin.
- [ ] **Step 5:** Run `npm test` → PASS. Run `npm run lint` → sạch. Run `npm run audit:api` → sạch (cập nhật file kiểm kê nếu công cụ yêu cầu).

---

### Task 8: Frontend — màn Thanh toán mới

**Việc này làm gì (nói dễ hiểu):** Làm lại màn Tài khoản → Thanh toán. Phụ huynh thấy mình đang ở gói nào và còn bao nhiêu lượt, chọn gói trên ba thẻ, được nhắc rõ hệ quả trước khi xác nhận, quét QR có đồng hồ đếm ngược và tự biết khi tiền về, và xem lại lịch sử các lần mua.

**Cần thêm / còn thiếu:**
- Đã có: `PaymentSection.tsx` kiểu cũ (danh sách gói + một liên kết mở QR), `confirmDialog`, các lớp `.sec`, `.btn`, `.notice`, `.pane`.
- Cần thêm: 5 vùng giao diện của mục 6 trong spec; hỏi lại trạng thái giao dịch; mở lại QR từ lịch sử.
- Còn thiếu / ngoài phạm vi: màn mua gói cho Creator; hiển thị quyền lợi chưa được backend áp dụng (số hồ sơ trẻ, thư viện) chỉ là chữ mô tả tĩnh.

**Files (gốc `AI_Storytelling_Frontend/src`):**
- Create: `app/features/account/payment/paymentRules.ts` — hàm thuần
- Create: `app/features/account/payment/usePayment.ts` — tải dữ liệu, tạo giao dịch, hỏi lại trạng thái
- Create: `app/features/account/payment/MyPlanCard.tsx`, `PlanCards.tsx`, `PayPanel.tsx`, `TransactionHistory.tsx`, `payment.css`
- Modify: `app/features/account/PaymentSection.tsx` (chỉ còn ghép các phần trên)
- Test: `tests/unit/features/paymentRules.test.ts`

**Interfaces:**
- Consumes: `paymentService` và kiểu của Task 7.
- Produces (`paymentRules.ts`):
  - `planAction(plan: SubscriptionPlanDto, current: QuotaPlanDto | null): 'subscribe' | 'renew' | 'upgrade' | 'downgrade' | 'buy-addon' | 'locked'`
  - `actionLabel(action): string` — `subscribe` "Đăng ký", `renew` "Gia hạn", `upgrade` "Nâng gói", `downgrade` "Chuyển sang Gói Đọc", `buy-addon` "Mua thêm lượt", `locked` "Cần có gói"
  - `confirmMessage(plan, current, action): string`
  - `secondsLeft(expiresAt: string, nowMs: number): number` — không âm
  - `formatCountdown(seconds: number): string` — `mm:ss`
  - `statusLabel(status: string): string` — `Pending` "Chờ thanh toán", `Paid` "Đã thanh toán", `Expired` "Đã hết hạn", `MismatchAmount` "Đang chờ quản trị viên xử lý, bạn không cần chuyển lại."
  - `PLAN_BENEFITS: Record<'Reading' | 'Creative' | 'AddOn', string[]>` — chữ quyền lợi lấy từ mục "Mục đích và phạm vi" của tài liệu Luồng 2

- [ ] **Step 1: Viết test fail** `paymentRules.test.ts`:

| Trường hợp | Kỳ vọng |
|---|---|
| `planAction(Reading, null)` | `'subscribe'` |
| `planAction(Creative, {planType:'Creative'})` | `'renew'` |
| `planAction(Creative, {planType:'Reading'})` | `'upgrade'` |
| `planAction(Reading, {planType:'Creative'})` | `'downgrade'` |
| `planAction(AddOn có isPurchasable=true, {planType:'Reading'})` | `'buy-addon'` |
| `planAction(AddOn có isPurchasable=false, null)` | `'locked'` |
| `confirmMessage(…, 'upgrade')` | chứa `99.000` và `Những ngày Gói Đọc còn lại không được quy đổi` |
| `confirmMessage(…, 'renew')` với `endAt=2026-12-10` | chứa `bắt đầu sau ngày` và ngày kết thúc hiện tại theo `vi-VN` |
| `confirmMessage(…, 'downgrade')` | chứa `Gói Đọc sẽ bắt đầu sau khi Gói Sáng tạo hiện tại kết thúc` |
| `secondsLeft('2026-11-10T03:15:00Z', Date.parse('2026-11-10T03:10:00Z'))` | `300` |
| `secondsLeft` với thời điểm đã qua | `0` |
| `formatCountdown(300)` / `formatCountdown(5)` | `'05:00'` / `'00:05'` |
| `statusLabel('MismatchAmount')` | chứa `không cần chuyển lại` |

- [ ] **Step 2:** Run `npm test -- paymentRules` → FAIL.
- [ ] **Step 3:** Viết `paymentRules.ts` → Run lại → PASS.
- [ ] **Step 4: Viết `usePayment.ts`.** Quyết định cần giữ:
  - Tải song song `plans`, `myQuota`, `myTransactions`; huỷ bằng `AbortController` khi unmount (theo mẫu `PaymentSection` cũ).
  - Giao dịch đang hiển thị QR = giao dịch vừa tạo, hoặc giao dịch Pending còn hạn mới nhất trong lịch sử khi mở màn.
  - Trong lúc có giao dịch Pending còn hạn: gọi lại `myTransactions` mỗi 5 giây; dừng khi giao dịch đổi trạng thái, hết giờ, hoặc unmount. Khi thành `Paid` → tải lại `myQuota` và `plans`.
  - Giữ hành vi cũ: lỗi mạng / 5xx khi tạo giao dịch → không cho tạo tiếp, hiện cảnh báo "chưa rõ máy chủ đã tạo giao dịch"; nhưng thay vì bắt liên hệ hỗ trợ, cho nút "Kiểm tra lại" để tải lịch sử.
- [ ] **Step 5: Viết các component** theo mục 6 của spec:
  - `MyPlanCard`: tên gói + "đến hết ngày …", hoặc "Miễn phí"; dòng "Gói kế tiếp: … từ ngày …" nếu có `nextPlan`; ba số lượt (dùng thử, chu kỳ, Add-on) và tổng; `otherCredits > 0` thì thêm dòng "Lượt từ gói cũ".
  - `PlanCards`: 3 thẻ, giá định dạng `vi-VN` + "đ", danh sách `PLAN_BENEFITS`, nút theo `actionLabel`; thẻ khoá hiện `lockedReason`; mọi nút tắt khi đang có giao dịch gói chờ thanh toán (Add-on xét riêng).
  - `PayPanel`: ảnh QR (`<img>` từ `qrCodeUrl`, có `alt`), số tiền, nội dung chuyển khoản in đậm, đếm ngược `mm:ss`, dòng trạng thái; khi Paid hiện xác nhận thành công; khi hết hạn hiện nút tạo giao dịch mới.
  - `TransactionHistory`: bảng mới nhất trước (gói, số tiền, mã, trạng thái theo `statusLabel`, tạo lúc, trả lúc); dòng Pending còn hạn có nút "Mở lại mã QR"; có nút "Xem thêm" khi còn trang.
  - `payment.css`: chỉ dùng biến trong `styles/tokens.css`; bố cục một cột dưới 640px.
- [ ] **Step 6:** Run `npm test` → PASS. Run `npm run lint` → sạch. Run `npm run build` → thành công.
- [ ] **Step 7: Kiểm tra tay** với backend cục bộ của Task 6: mở màn Thanh toán ở cả theme sáng và tối, đi luồng đăng ký Gói Sáng tạo (giả lập webhook) → thấy thông báo thành công và số lượt cập nhật mà không cần tải lại trang → mua Add-on → mở lại QR từ lịch sử → để một giao dịch hết 15 phút (hoặc sửa `ExpiresAt` trong CSDL dev) và xem màn chuyển sang "Đã hết hạn". Kiểm tra thêm Trang chủ và Studio vẫn hiện đúng số lượt.
