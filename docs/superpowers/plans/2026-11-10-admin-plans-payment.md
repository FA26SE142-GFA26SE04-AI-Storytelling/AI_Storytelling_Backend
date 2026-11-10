# Admin: giao dịch, lượt, đối soát và gói (backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admin xem và xác nhận tay mọi giao dịch, cấp / trừ lượt cho tài khoản phụ huynh, xử lý giao dịch Paid chưa được áp gói, và sửa / bật tắt gói — tất cả ở backend.

**Architecture:** Mở rộng các service của đợt 1. `PaymentService` thêm danh sách giao dịch và quản lý gói cho Admin; `TokenQuotaService` thêm điều chỉnh lượt; một `PaymentReconciliationService` mới phát hiện và xử lý ngoại lệ, được `SubscriptionCycleWorker` gọi sau bước chuyển chu kỳ. Hai bảng mới và một cột chốt số lượt trên giao dịch.

**Tech Stack:** .NET 8 / EF Core + PostgreSQL, xUnit. Unit test dùng `tests/StoryPlatform.UnitTests/Features/TokenQuota/InMemoryUnitOfWork.cs` như đợt 1.

**Spec:** `docs/superpowers/specs/2026-11-10-admin-plans-payment-design.md`. Các mã D1–D10 trỏ về mục 3 của spec.

## Global Constraints

- Chỉ repo `AI_Storytelling_Backend`, tiếp tục trên nhánh `feat/parent-plans-payment`. Không commit, không push.
- Mọi endpoint mới: `[Authorize(Roles = "Administrator")]`, đặt dưới `payment/admin/...` và `tokenquota/admin/...` (D10).
- Chỉ có một đường áp gói: `ISubscriptionService.ApplyPaidTransactionAsync` (D2). Không thêm cách cấp gói khác.
- Lượt Admin cấp: lô `AdminAdjust`, không hết hạn; trừ lượt lấy lô sắp hết hạn trước (D3). Chỉ tài khoản role `User` (D4).
- Job đối soát: chỉ giao dịch Paid quá 5 phút, bỏ qua người mua đã xoá, không tự áp gói (D6).
- Số lượt của giao dịch chốt lúc tạo trong `PaymentTransaction.AiCredits`; null thì dùng số của gói (D8).
- Thông báo lỗi tiếng Việt, định danh tiếng Anh. Không thêm dependency. Không in secret.

## Review Focus

1. Admin trừ lượt đúng lúc phụ huynh gửi yêu cầu tạo truyện → tổng lượt không bao giờ âm (Task 3, khoá theo tài khoản).
2. Admin bấm xử lý cùng một ngoại lệ hai lần liên tiếp → lần hai nhận 409, lượt không đổi (Task 4).
3. Sửa giá và số lượt của gói khi đang có giao dịch Pending → giao dịch đó khi Paid vẫn theo giá và số lượt cũ (Task 1, Task 2).
4. Tìm giao dịch với chuỗi tìm kiếm có ký tự đặc biệt của `LIKE` (`%`, `_`) hoặc chỉ có khoảng trắng → không lỗi, không trả sai tập (Task 2).
5. Giao dịch của tài khoản đã xoá (chu kỳ và lô đã bị huỷ) → không bị job báo thành ngoại lệ (Task 4).

---

### Task 1: Dữ liệu — bảng điều chỉnh lượt, bảng ngoại lệ, chốt số lượt trên giao dịch

**Việc này làm gì (nói dễ hiểu):** Tạo chỗ lưu cho hai loại ghi chép mới của Admin: mỗi lần cấp hay trừ lượt, và mỗi giao dịch đã trả tiền nhưng chưa được áp gói. Đồng thời mỗi giao dịch từ nay ghi nhớ số lượt của gói tại thời điểm tạo, để sau này Admin sửa gói thì giao dịch cũ không bị đổi theo.

**Cần thêm / còn thiếu:**
- Đã có: `payment_transactions` (đã chốt số tiền ở `Amount`), `credit_lots`, `CreditSource`, seed và `.dbml`.
- Cần thêm: `CreditSource.AdminAdjust`; enum `ReconciliationStatus`; cột `PaymentTransaction.AiCredits`; bảng `credit_adjustments`, `payment_reconciliation_exceptions`; áp gói dùng số lượt đã chốt.
- Còn thiếu / ngoài phạm vi: cột quyền lợi và `Audience` của gói (đợt Gói Creator).

**Files:**
- Create: `src/Core/StoryPlatform.Domain/Enums/ReconciliationStatus.cs`
- Create: `src/Core/StoryPlatform.Domain/Entities/CreditAdjustment.cs`, `PaymentReconciliationException.cs`
- Create: `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/CreditAdjustmentConfiguration.cs`, `PaymentReconciliationExceptionConfiguration.cs`
- Create: migration `AddCreditAdjustmentsAndReconciliationExceptions`; `Database/Seed/69_credit_adjustments.sql`, `70_payment_reconciliation_exceptions.sql`
- Modify: `CreditSource.cs`, `PaymentTransaction.cs`, `ApplicationDbContext.cs`, `Database/ai_storytelling_platform.dbml`, `Database/Seed/39_payment_transactions.sql`
- Modify: `PaymentService.CreateTransactionAsync` (ghi `AiCredits = plan.AiCredits`), `SubscriptionService` (dùng số đã chốt)
- Test: `tests/StoryPlatform.UnitTests/Features/Payments/PaymentServiceTests.cs`, `SubscriptionServiceTests.cs`

**Interfaces:**
- Produces:
  - `CreditSource.AdminAdjust = 5`; `enum ReconciliationStatus { Open = 1, Resolved = 2 }`
  - `PaymentTransaction.AiCredits : int?`
  - `CreditAdjustment : BaseEntity { int UserId; UserAccount? User; int Amount; string Reason; int AdminId; UserAccount? Admin; }`
  - `PaymentReconciliationException : BaseEntity { int PaymentId; PaymentTransaction? Payment; ReconciliationStatus Status; string Reason; int? ResolvedBy; UserAccount? ResolvedByUser; DateTime? ResolvedAt; }`
  - `DbSet<CreditAdjustment> CreditAdjustments`, `DbSet<PaymentReconciliationException> PaymentReconciliationExceptions`

- [ ] **Step 1: Test fail.**
  - `PaymentServiceTests.CreateTransactionAsync_SnapshotsPlanCredits`: tạo giao dịch Gói Sáng tạo → `stored.AiCredits == 4`.
  - `SubscriptionServiceTests.Apply_UsesCreditsSnapshottedOnTheTransaction`: giao dịch Creative có `AiCredits = 4`, gói đã bị sửa thành `AiCredits = 10` → lô `Cycle` có `Remaining = 4`.
  - `SubscriptionServiceTests.Apply_TransactionWithoutSnapshot_UsesPlanCredits`: `AiCredits = null` → lô 4 lượt theo gói.
  - `SubscriptionServiceTests.Advance_ScheduledCycle_UsesItsTransactionSnapshot`: dòng `Scheduled` gắn giao dịch có `AiCredits = 4`, gói sửa thành 10 → kích hoạt cấp 4.
- [ ] **Step 2:** Run `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~PaymentServiceTests|FullyQualifiedName~SubscriptionServiceTests"` → FAIL (chưa có `AiCredits`).
- [ ] **Step 3:** Thêm enum, entity, cấu hình EF (`Status` lưu chuỗi max 20; `Reason` max 500 bắt buộc; unique `PaymentId`; index `UserId`, `Status`; mọi FK `Restrict`; query filter `!IsDeleted`), DbSet, cột `AiCredits`. Sửa `CreateTransactionAsync` và `SubscriptionService` (một helper `CreditsFor(transaction, plan) => transaction.AiCredits ?? plan.AiCredits`; khi kích hoạt dòng `Scheduled` thì nạp giao dịch theo `PaymentId`).
- [ ] **Step 4:** `dotnet ef migrations add AddCreditAdjustmentsAndReconciliationExceptions --project src/Core/StoryPlatform.Infrastructure --startup-project src/Core/StoryPlatform.Api`. Không viết SQL tay.
- [ ] **Step 5:** Seed và `.dbml` cho hai bảng mới; thêm cột `AiCredits` vào seed giao dịch.
- [ ] **Step 6:** Run `dotnet build StoryPlatform.sln` → 0 lỗi; `dotnet ef migrations has-pending-model-changes ...` → sạch; lệnh test ở Step 2 và `--filter FullyQualifiedName~SeedDataAlignment` → PASS.

---

### Task 2: Admin xem mọi giao dịch và quản lý gói

**Việc này làm gì (nói dễ hiểu):** Admin có một danh sách đầy đủ các giao dịch để tìm giao dịch đang chờ và xác nhận tay khi ngân hàng không báo về. Admin cũng xem được mọi gói, sửa tên, giá, số lượt và bật hoặc tắt bán; mỗi lần sửa đều được ghi lại.

**Cần thêm / còn thiếu:**
- Đã có: `GET payment/mismatched`, `POST payment/transactions/{id}/mark-paid`, `GET payment/plans` (cho người mua).
- Cần thêm: `GET payment/admin/transactions`, `GET payment/admin/plans`, `PUT payment/admin/plans/{id}`, audit `SubscriptionPlanUpdated`.
- Còn thiếu / ngoài phạm vi: tạo gói mới, quyền lợi theo gói, thống kê doanh thu.

**Files:**
- Modify: `Features/Payments/Services/PaymentService.cs`, `Interfaces/IPaymentService.cs`, `DTOs/PaymentDtos.cs`, `src/Core/StoryPlatform.Api/Controllers/PaymentController.cs`
- Test: `PaymentServiceTests.cs`, `PaymentControllerAuthorizationTests.cs`

**Interfaces:**
- Produces (`IPaymentService`):
  - `Task<PagedResult<AdminPaymentTransactionDto>> ListAllTransactionsAsync(AdminTransactionQueryDto query, CancellationToken ct = default)`
  - `Task<List<AdminSubscriptionPlanDto>> ListAllPlansAsync(CancellationToken ct = default)`
  - `Task<AdminSubscriptionPlanDto> UpdatePlanAsync(int adminUserId, int planId, UpdateSubscriptionPlanRequestDto request, CancellationToken ct = default)`
- DTO:
  - `AdminTransactionQueryDto { int PageIndex = 1; int PageSize = 20; string? Status; string? Search; }`
  - `AdminPaymentTransactionDto : PaymentTransactionDto { int PayerUserId; string PayerEmail; string PayerName; }`
  - `AdminSubscriptionPlanDto { int Id; string Name; string PlanType; int PriceVnd; int AiCredits; bool IsActive; }`
  - `UpdateSubscriptionPlanRequestDto { string Name; int PriceVnd; int AiCredits; bool IsActive; }`

- [ ] **Step 1: Test fail** (`PaymentServiceTests`):

| Test | Khẳng định |
|---|---|
| `ListAllTransactionsAsync_ReturnsEveryPayersNewestFirstWithPayerInfo` | giao dịch của 2 phụ huynh, mới nhất trước; có `PayerEmail`, `PayerName` |
| `ListAllTransactionsAsync_FiltersByStatus` | `Status="Pending"` → chỉ Pending |
| `ListAllTransactionsAsync_InvalidStatus_ThrowsBadRequest` | `Status="Done"` → `BadRequestException` |
| `ListAllTransactionsAsync_StalePendingKeepsStoredStatus` | Pending quá hạn vẫn trả `"Pending"` |
| `ListAllTransactionsAsync_SearchMatchesCodeOrPayerEmailCaseInsensitive` (theory) | `"abcdef"` khớp mã `SEPAYABCDEF0123`; `"P2@X"` khớp email `p2@x.com` |
| `ListAllTransactionsAsync_SearchWithWildcardsOrBlank_IsLiteral` (theory: `"%"`, `"_"`, `"   "`) | `"%"`, `"_"` → 0 dòng; khoảng trắng → như không tìm |
| `ListAllPlansAsync_IncludesInactiveAndLegacy` | đủ 4 gói, theo `Id` |
| `UpdatePlanAsync_ChangesFieldsAndWritesAudit` | gói 3 → giá 109000, 5 lượt; audit `SubscriptionPlanUpdated` với `adminUserId` |
| `UpdatePlanAsync_InvalidInput_ThrowsBadRequest` (theory) | tên rỗng; tên 151 ký tự; giá 0; lượt −1 |
| `UpdatePlanAsync_ActivatingLegacyPlan_ThrowsBadRequest` | gói 1, `IsActive=true` |
| `UpdatePlanAsync_PlanNotFound_ThrowsNotFound` | `NotFoundException` |
| `UpdatePlanAsync_DeactivatedPlan_DisappearsFromBuyerList` | tắt gói 2 → `ListActivePlansAsync` không còn gói 2 |
| `UpdatePlanAsync_DoesNotTouchExistingPendingTransaction` | Pending Creative 99000 / `AiCredits=4`; sửa gói → giao dịch giữ nguyên |

  `PaymentControllerAuthorizationTests`: `ListAllTransactions`, `ListAllPlans`, `UpdatePlan` chỉ `Administrator`.
- [ ] **Step 2:** Run `--filter "FullyQualifiedName~PaymentServiceTests|FullyQualifiedName~PaymentControllerAuthorization"` → FAIL.
- [ ] **Step 3:** Implement. Tìm kiếm so khớp bằng `Contains` sau khi `Trim` và đưa về chữ thường, dịch được sang SQL (không dùng `EF.Functions.Like` với chuỗi người dùng). `PageSize` kẹp 1–100.
- [ ] **Step 4:** Controller: 3 action mới.
- [ ] **Step 5:** Run lệnh Step 2 → PASS.

---

### Task 3: Admin cấp và trừ lượt cho một tài khoản

**Việc này làm gì (nói dễ hiểu):** Khi cần bù lỗi hay hỗ trợ khách, Admin cộng thêm hoặc trừ bớt lượt tạo truyện của một phụ huynh, bắt buộc ghi lý do. Mọi lần điều chỉnh được lưu lại để xem về sau. Admin cũng xem được phụ huynh đó đang ở gói nào và còn bao nhiêu lượt.

**Cần thêm / còn thiếu:**
- Đã có: sổ lượt `credit_lots`, `GrantAsync`, `GetMyQuotaAsync`, khoá theo tài khoản.
- Cần thêm: `POST/GET tokenquota/admin/adjustments`, `GET tokenquota/admin/users/{userId}`, bảng `credit_adjustments`, audit `CreditAdjusted`.
- Còn thiếu / ngoài phạm vi: điều chỉnh lượt cho Creator; giá trị chung (lượt dùng thử, hạn mức) thuộc 5a bước 5.

**Files:**
- Modify: `Features/TokenQuota/Services/TokenQuotaService.cs`, `Interfaces/ITokenQuotaService.cs`, `DTOs/TokenQuotaDtos.cs`, `Controllers/TokenQuotaController.cs`, `Application/DependencyInjection.cs` (nếu constructor đổi)
- Test: `TokenQuotaServiceTests.cs`, `TokenQuotaControllerAuthorizationTests.cs`; sửa các chỗ `new TokenQuotaService(...)` trong test khác.

**Interfaces:**
- Produces (`ITokenQuotaService`):
  - `Task<CreditAdjustmentDto> AdjustAsync(int adminUserId, AdjustCreditsRequestDto request, CancellationToken ct = default)`
  - `Task<PagedResult<CreditAdjustmentDto>> ListAdjustmentsAsync(int? userId, int pageIndex, int pageSize, CancellationToken ct = default)`
  - `Task<MyQuotaDto> GetQuotaForUserAsync(int userId, CancellationToken ct = default)` — 404 nếu không có tài khoản; không cấp lượt dùng thử
- DTO: `AdjustCreditsRequestDto { int UserId; int Amount; string Reason; }`;
  `CreditAdjustmentDto { int Id; int UserId; int Amount; string Reason; int AdminId; string AdminName; DateTime CreatedAt; int? TotalRemainingAfter; }` (`TotalRemainingAfter` chỉ có ở response của `AdjustAsync`)
- `TokenQuotaService` nhận thêm `IAuditLogWriter` ở constructor.

- [ ] **Step 1: Test fail** (`TokenQuotaServiceTests`):

| Test | Khẳng định |
|---|---|
| `AdjustAsync_Grant_CreatesNonExpiringAdminLot_AndRecordsAdjustment` (UC-L2-08 AC1) | +3 → lô `AdminAdjust` 3 lượt, `ExpiresAt=null`; 1 dòng `CreditAdjustment` (`Amount=3`, lý do, `AdminId`); `TotalRemainingAfter` tăng 3; tài khoản khác không đổi |
| `AdjustAsync_MissingReason_ThrowsBadRequest` (AC2; theory `""`, `"  "`, null) | không tạo lô, không ghi dòng |
| `AdjustAsync_ZeroAmount_ThrowsBadRequest` | — |
| `AdjustAsync_UserNotFound_ThrowsNotFound` | — |
| `AdjustAsync_NonParentAccount_ThrowsBadRequest` | role `ContentCreator` |
| `AdjustAsync_Deduct_TakesFromSoonestExpiringLotsFirst` | lô AddOn 2 (hạn +3 ngày), Cycle 4 (hạn +10 ngày), Legacy 5; −3 → AddOn 0, Cycle 3, Legacy 5 |
| `AdjustAsync_DeductMoreThanRemaining_ThrowsBadRequestAndChangesNothing` | còn 2, −3 |
| `AdjustAsync_DeductIgnoresExpiredLots` | lô hết hạn 4 + lô còn hạn 1; −2 → `BadRequestException` |
| `AdjustAsync_RunsInTransactionWithAccountLock` | `Calls == ["begin", "lock:1", "commit"]`; khi lỗi giữa chừng → `rollback` |
| `AdjustAsync_WritesAuditWithBeforeAndAfterTotals` | audit `CreditAdjusted` |
| `GetMyQuotaAsync_AdminLotsCountAsOtherCredits` (D5) | lô `AdminAdjust` 3 → `OtherCredits=3` |
| `ListAdjustmentsAsync_FiltersByUserNewestFirst` | — |
| `GetQuotaForUserAsync_DoesNotGrantTrial` | phụ huynh có hồ sơ Active, chưa có lô → `TotalRemaining=0`, không tạo lô |

  Authorization: `Adjust`, `ListAdjustments`, `GetUserQuota` chỉ `Administrator`.
- [ ] **Step 2:** Run `--filter FullyQualifiedName~TokenQuota` → FAIL.
- [ ] **Step 3:** Implement. Thông báo: thiếu lý do → `"Cần ghi lý do điều chỉnh lượt."`; trừ quá → `"Không thể trừ {n} lượt: tài khoản chỉ còn {m} lượt."`.
- [ ] **Step 4:** Run `dotnet test tests/StoryPlatform.UnitTests` → PASS toàn bộ.

---

### Task 4: Đối soát giao dịch đã trả tiền nhưng chưa được áp gói

**Việc này làm gì (nói dễ hiểu):** Đôi khi tiền đã về nhưng hệ thống gặp lỗi lúc kích hoạt gói. Một tiến trình nền tự tìm những giao dịch như vậy và đưa vào danh sách cần xử lý. Admin mở danh sách, bấm xử lý, hệ thống kích hoạt lại gói cho đúng một lần.

**Cần thêm / còn thiếu:**
- Đã có: audit `PaymentCreditFailed`, `ApplyPaidTransactionAsync` idempotent, `SubscriptionCycleWorker`.
- Cần thêm: `PaymentReconciliationService` (phát hiện, liệt kê, xử lý), `GET payment/admin/reconciliation`, `POST payment/admin/reconciliation/{id}/resolve`, audit `PaymentReconciliationResolved`.
- Còn thiếu / ngoài phạm vi: Gói Creator; hoàn tiền; giao dịch `MismatchAmount` / `Expired` vẫn xử lý bằng `mark-paid`.

**Files:**
- Create: `Features/Payments/Interfaces/IPaymentReconciliationService.cs`, `Services/PaymentReconciliationService.cs`
- Modify: `DTOs/PaymentDtos.cs`, `PaymentController.cs`, `Application/DependencyInjection.cs`, `Infrastructure/BackgroundServices/SubscriptionCycleWorker.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/Payments/PaymentReconciliationServiceTests.cs`, `PaymentControllerAuthorizationTests.cs`

**Interfaces:**
- Produces (`IPaymentReconciliationService`):
  - `Task<int> DetectAsync(DateTime now, CancellationToken ct = default)` — trả số ngoại lệ mới
  - `Task<PagedResult<ReconciliationExceptionDto>> ListAsync(string? status, int pageIndex, int pageSize, CancellationToken ct = default)` — `status` null → `Open`
  - `Task<ReconciliationExceptionDto> ResolveAsync(int adminUserId, int exceptionId, CancellationToken ct = default)`
- DTO: `ReconciliationExceptionDto { int Id; string Status; string Reason; DateTime CreatedAt; DateTime? ResolvedAt; string? ResolvedByName; AdminPaymentTransactionDto Transaction; }`

- [ ] **Step 1: Test fail** (`PaymentReconciliationServiceTests`, `now` cố định, service thật `SubscriptionService` + `TokenQuotaService` trên `InMemoryUnitOfWork`):

| Test | Khẳng định |
|---|---|
| `Detect_PaidCreativeWithoutSubscription_CreatesOpenException` (UC-L2-13 AC1) | 1 ngoại lệ `Open`, `Reason="NotApplied"`; không có lô / chu kỳ nào được tạo |
| `Detect_PaidAddOnWithoutLot_CreatesOpenException` | — |
| `Detect_UsesLatestCreditFailedAuditAsReason` | có audit `PaymentCreditFailed` cho giao dịch → `Reason` chứa nội dung audit |
| `Detect_AppliedTransactions_AreIgnored` | Creative có chu kỳ; Add-on có lô → 0 ngoại lệ |
| `Detect_ReadingPlanWithSubscription_IsIgnoredEvenWithZeroCredits` | — |
| `Detect_PaidWithinLastFiveMinutes_IsIgnored` (D6) | `PaidAt = now-4m` |
| `Detect_NonPaidTransactions_AreIgnored` (theory Pending / Expired / MismatchAmount) | — |
| `Detect_DeletedPayer_IsIgnored` (Review Focus 5) | người mua `IsDeleted=true` |
| `Detect_Twice_CreatesOneException` | — |
| `Resolve_OpenException_AppliesPlanAndMarksResolved` (AC2) | chu kỳ Creative 1 tháng + lô 4 lượt; `Resolved`, `ResolvedBy`, `ResolvedAt`; audit `PaymentReconciliationResolved` |
| `Resolve_AlreadyResolved_ThrowsConflictAndChangesNothing` (AC3, Review Focus 2) | số lô không đổi |
| `Resolve_PlanAlreadyAppliedMeanwhile_MarksResolvedWithoutDoubleGrant` | vẫn 1 chu kỳ, 1 lô |
| `Resolve_ApplyStillFails_KeepsOpenUpdatesReasonAndThrowsConflict` | `ISubscriptionService` giả ném lỗi |
| `Resolve_NotFound_ThrowsNotFound` | — |
| `List_DefaultsToOpen_NewestFirst_WithTransactionAndPayer` | — |
| `List_InvalidStatus_ThrowsBadRequest` | — |

- [ ] **Step 2:** Run `--filter FullyQualifiedName~PaymentReconciliation` → FAIL.
- [ ] **Step 3:** Implement service; đăng ký DI; `SubscriptionCycleWorker` gọi `DetectAsync(DateTime.UtcNow)` sau `SweepAsync`, lỗi của bước này được log và không chặn vòng lặp.
- [ ] **Step 4:** Controller: 2 action mới + test phân quyền.
- [ ] **Step 5:** Run `dotnet test tests/StoryPlatform.UnitTests` → PASS toàn bộ.

---

### Task 5: Kiểm tra đầu-cuối và tài liệu API

**Việc này làm gì (nói dễ hiểu):** Đi thử các việc của Admin qua API thật như một người dùng: phụ huynh tạo giao dịch, Admin tìm và xác nhận tay, gói bật lên; Admin cấp lượt; Admin sửa giá gói. Rồi ghi lại cách gọi API để người làm giao diện dùng.

**Cần thêm / còn thiếu:**
- Đã có: `SubscriptionPurchaseApiTests`, `SubscriptionPostgresTests`, `docs/PARENT_PLANS_PAYMENT_API_GUIDE.md`.
- Cần thêm: test HTTP cho luồng Admin; kiểm migration mới trên PostgreSQL; mục "Admin" trong tài liệu API.
- Còn thiếu / ngoài phạm vi: giao diện Admin trên web.

**Files:**
- Create: `tests/StoryPlatform.IntegrationTests/Api/AdminPaymentApiTests.cs`
- Modify: `tests/StoryPlatform.IntegrationTests/Postgres/SubscriptionPostgresTests.cs`, `docs/PARENT_PLANS_PAYMENT_API_GUIDE.md`

- [ ] **Step 1: Test HTTP** (`AdminPaymentApiTests`):
  - `Admin_FindsPendingTransactionAndMarksPaid_PlanActivatesWithoutWebhook`
  - `Admin_GrantsThenDeductsCredits_ParentSeesBalance_AndHistoryIsKept` (thiếu lý do → 400; trừ quá → 400)
  - `Admin_UpdatesPlanPrice_OldPendingKeepsOldPrice_NewPurchaseUsesNewPrice` (dùng `ApiFactory` riêng của lớp test để không ảnh hưởng lớp khác)
  - `Admin_ResolvesReconciliationException_PlanApplied_SecondResolveIs409`
  - `AdminEndpoints_RejectParents` (theory: mọi route mới → 403)
- [ ] **Step 2:** PostgreSQL: thêm vào `SubscriptionPostgresTests` test `AdminDeductRacingWithStoryRequest_NeverGoesNegative` (Review Focus 1). Chạy với `STORYPLATFORM_TEST_PG` → `SubscriptionPostgresTests` PASS (migration mới áp được).
- [ ] **Step 3:** Run `dotnet test StoryPlatform.sln` → PASS; ghi rõ test bị bỏ qua.
- [ ] **Step 4:** Thêm mục Admin vào tài liệu API.
