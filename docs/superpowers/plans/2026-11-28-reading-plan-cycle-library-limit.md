# Hạn mức truyện thư viện mới theo chu kỳ cho Gói Đọc — Implementation Plan

> **For agentic workers:** Thực hiện từng task theo thứ tự, mỗi task theo chu trình TDD (test fail → code → test pass). Steps dùng checkbox (`- [ ]`). Không commit — chủ repo tự commit.

**Goal:** Gói chu kỳ (Đọc/Sáng tạo) giới hạn số truyện thư viện mới theo chu kỳ thanh toán, chốt trên giao dịch; phụ huynh thấy đã dùng / hạn mức / ngày làm mới.

**Architecture:** Hạn mức chốt vào cột mới `PaymentTransaction.NewLibraryStoriesLimit` lúc tạo giao dịch. `PlanBenefitService` đọc hạn mức từ giao dịch của chu kỳ đang Active và trả kèm khung thời gian (tuần hoặc chu kỳ). `PlanUsageService` đếm theo khung bất kỳ; `ChildReadingService` chặn theo khung đó; `GET /TokenQuota/me` trả số đã dùng; Frontend hiển thị.

**Tech Stack:** .NET 10, EF Core (PostgreSQL), xUnit + `InMemoryUnitOfWork`; Next.js/React + TypeScript, Vitest.

**Spec:** `docs/superpowers/specs/2026-11-28-reading-plan-cycle-library-limit-design.md` (đọc cùng plan này).

## Global Constraints

- Giữ tên `WeeklyNewLibraryStories` ở entity, DTO, API; không xóa/đổi tên trường hiện có.
- Hằng period: `"Week"` và `"Cycle"`.
- Thông báo chu kỳ (chính xác): `"Đã mở đủ {0} truyện thư viện mới trong chu kỳ. Truyện mới mở lại từ ngày {1:dd/MM/yyyy} nếu gia hạn gói."` — `{1}` là `VietnamTime.LocalDate(cycle.EndAt)`.
- Thông báo tuần giữ nguyên `WeeklyLibraryLimitMessageFormat`.
- Giao dịch cũ có `NewLibraryStoriesLimit = null` → không giới hạn. Không backfill, không data migration.
- Seed Gói Đọc (Id 2) `WeeklyNewLibraryStories = 24`; Gói Sáng tạo giữ `NULL`.
- Không commit/push/branch/worktree. Không đọc `appsettings.*.json`, `.env`.

## Review Focus

1. Admin đặt Gói Đọc = 24 khi gia đình đang giữa chu kỳ (giao dịch cũ `null`) → vẫn không giới hạn tới hết chu kỳ. *(Task 2: `GetForUser_CycleFromLegacyTransaction_IsUnlimitedEvenIfPlanNowLimited`)*
2. Chu kỳ đã trả trước (Scheduled) mua trước khi Admin đổi gói → khi bắt đầu vẫn dùng giá trị đã chốt của chính giao dịch đó. *(Task 2: `GetForUser_ScheduledCycleStarted_UsesItsOwnTransactionLimit`)*
3. Ngày làm mới hiển thị theo ngày giờ Việt Nam, không theo ngày UTC (`EndAt` = 17:30 UTC ngày 15/12 → "16/12/2026"). *(Task 3: `StartReading_CycleLimitReached_MessageUsesVietnamDateOfCycleEnd`)*
4. Không lấy nhầm giá trị hiện tại của gói khi giao dịch không được load (thiếu include) → phải đọc đúng giao dịch theo `PaymentId`. *(phủ bởi test 1 và 2)*
5. Frontend gọi Backend cũ chưa có trường mới → không crash, không hiện dòng hạn mức. *(Task 5: `hides library line when benefits are missing`)*

---

### Task 1: Chốt hạn mức trên giao dịch + migration

**Việc này làm gì (nói dễ hiểu):**
Khi phụ huynh tạo đơn mua Gói Đọc hoặc Gói Sáng tạo, hệ thống ghi lại luôn "chu kỳ này được mở bao nhiêu truyện mới" lên đơn đó. Nhờ vậy Admin có đổi gói sau này thì người đã mua vẫn giữ đúng quyền lợi lúc mua, giống cách lượt AI đang được chốt.

**Cần thêm / còn thiếu:**
- Đã có: `PaymentTransaction.AiCredits`/`SubmitCredits` chốt lúc tạo giao dịch trong `PaymentService`; cột `SubscriptionPlan.WeeklyNewLibraryStories`.
- Cần thêm: cột `PaymentTransaction.NewLibraryStoriesLimit` (int?, nullable) + migration; gán giá trị ở mọi chỗ tạo giao dịch mới.
- Còn thiếu / ngoài phạm vi: đọc giá trị này khi tính quyền lợi (Task 2).

**Files:**
- Modify: `src/Core/StoryPlatform.Domain/Entities/PaymentTransaction.cs`
- Create: `src/Core/StoryPlatform.Infrastructure/Migrations/<timestamp>_AddNewLibraryStoriesLimitToPaymentTransactions.cs` (+ `.Designer.cs`), Modify: `ApplicationDbContextModelSnapshot.cs` — sinh bằng `dotnet ef migrations add` theo cách repo đang làm
- Modify: `src/Core/StoryPlatform.Application/Features/Payments/Services/PaymentService.cs` (các chỗ đặt `AiCredits = plan.AiCredits`, khoảng dòng 81, 160, 533)
- Test: `tests/StoryPlatform.UnitTests/Features/Payments/PaymentServiceTests.cs`

**Interfaces:**
- Produces: `PaymentTransaction.NewLibraryStoriesLimit : int?` — `plan.WeeklyNewLibraryStories` khi `plan.PlanType is Reading or Creative`, ngược lại `null`.

- [ ] **Step 1: Viết test fail** trong `PaymentServiceTests.cs`:
  - `CreatePayment_ReadingPlan_SnapshotsNewLibraryStoriesLimit` — gói Đọc `WeeklyNewLibraryStories = 24` → giao dịch tạo ra có `NewLibraryStoriesLimit == 24`.
  - `CreatePayment_CreativePlanUnlimited_SnapshotsNull` — gói Sáng tạo `null` → `null`.
  - `CreatePayment_AddOnAndCreatorPack_SnapshotNull` — gói AddOn và CreatorPack có đặt `WeeklyNewLibraryStories = 5` → vẫn `null`.
  - Nếu chỗ khoảng dòng 81 là luồng dùng lại giao dịch Pending: thêm `ReusedPendingPayment_KeepsOrRefreshesSnapshot_SameAsAiCredits` — hành vi giống hệt cách `AiCredits` đang được xử lý ở chỗ đó.
- [ ] **Step 2: Chạy** `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~PaymentServiceTests"` → FAIL (không biên dịch được / sai giá trị).
- [ ] **Step 3: Thêm property** `public int? NewLibraryStoriesLimit { get; set; }` (comment XML theo spec mục 6.1) và gán ở các chỗ tạo giao dịch.
- [ ] **Step 4: Sinh migration** `AddNewLibraryStoriesLimitToPaymentTransactions` (chỉ `AddColumn<int>(nullable: true)` trên bảng giao dịch thanh toán; `Down` xóa cột). Kiểm tra migration không có thay đổi lạ khác.
- [ ] **Step 5: Chạy lại test** → PASS; `dotnet build StoryPlatform.sln` → 0 lỗi.

---

### Task 2: Quyền lợi trả khung tuần / chu kỳ

**Việc này làm gì (nói dễ hiểu):**
Khi hệ thống hỏi "gia đình này được mở bao nhiêu truyện mới", câu trả lời giờ kèm theo "tính trong tuần hay trong chu kỳ, bắt đầu khi nào, làm mới khi nào". Với gói trả phí, con số lấy từ đơn đã mua chứ không lấy từ cấu hình gói hiện tại.

**Cần thêm / còn thiếu:**
- Đã có: `PlanBenefitService.GetForUserAsync`, `PlanBenefitsDto`, override `WeeklyNewLibraryStoriesOverride`.
- Cần thêm: 3 trường DTO; đọc hạn mức từ giao dịch của chu kỳ Active; khung thời gian.
- Còn thiếu / ngoài phạm vi: dùng khung này để đếm và chặn (Task 3).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/PlanBenefits/DTOs/PlanBenefitsDto.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/PlanBenefits/Services/PlanBenefitService.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/PlanBenefits/PlanBenefitServiceTests.cs`

**Interfaces:**
- Consumes: `PaymentTransaction.NewLibraryStoriesLimit` (Task 1).
- Produces trên `PlanBenefitsDto`:
  - `public const string WeekPeriod = "Week"; public const string CyclePeriod = "Cycle";`
  - `public string NewLibraryStoriesPeriod { get; set; } = WeekPeriod;`
  - `public DateTime NewLibraryStoriesPeriodStart { get; set; }` (UTC)
  - `public DateTime NewLibraryStoriesResetsAt { get; set; }` (UTC, mốc kết thúc độc quyền của khung)

- [ ] **Step 1: Viết test fail:**
  - `GetForUser_ActiveReadingCycle_UsesTransactionLimitAndCycleWindow` — giao dịch `NewLibraryStoriesLimit = 24`, gói hiện = 30 → `WeeklyNewLibraryStories == 24`, `Period == "Cycle"`, `PeriodStart == cycle.StartAt`, `ResetsAt == cycle.EndAt`.
  - `GetForUser_CycleFromLegacyTransaction_IsUnlimitedEvenIfPlanNowLimited` — giao dịch `null`, gói = 24 → `WeeklyNewLibraryStories == null`, `Period == "Cycle"`.
  - `GetForUser_ScheduledCycleStarted_UsesItsOwnTransactionLimit` — chu kỳ 1 (giao dịch 24) đã hết, chu kỳ 2 (giao dịch `null`) đang Active → `null`.
  - `GetForUser_Free_ReturnsWeekWindow` — không gói → `Period == "Week"`, `PeriodStart == VietnamTime.StartOfWeekUtc(now)`, `ResetsAt == VietnamTime.StartOfNextWeekUtc(now)`, hạn mức = `FreeWeeklyNewLibraryStories`.
  - `GetForUser_Creator_ReturnsWeekWindow`.
  - `GetForUser_OverrideRaisesCycleLimit` — chu kỳ 24, override 40 → 40.
- [ ] **Step 2: Chạy** `--filter "FullyQualifiedName~PlanBenefitServiceTests"` → FAIL.
- [ ] **Step 3: Sửa DTO + service:** nhánh có chu kỳ Reading/Creative lấy giao dịch theo `cycle.PaymentId` (include `Payment` hoặc `GetByIdAsync`); không dùng `plan.WeeklyNewLibraryStories` cho hạn mức. Nhánh Free/Creator dùng `_clock` để tính khung tuần. Cập nhật comment `WeeklyNewLibraryStories` theo spec mục 6.3.
- [ ] **Step 4: Chạy** test PlanBenefit → PASS; test cũ của file vẫn PASS.

---

### Task 3: Đếm theo khung và chặn khi mở truyện

**Việc này làm gì (nói dễ hiểu):**
Khi trẻ mở một truyện thư viện chưa từng mở, hệ thống đếm số truyện mới gia đình đã mở trong kỳ hiện tại (tuần với Miễn phí, chu kỳ với gói trả phí). Đủ hạn mức thì báo rõ ngày được mở lại. Đọc lại truyện cũ, truyện sự kiện và gói truyện gia đình vẫn như trước.

**Cần thêm / còn thiếu:**
- Đã có: `PlanUsageService.FirstOpenedLibraryStoriesAsync`, lọc waiver sự kiện, khóa giao dịch theo phụ huynh, `EnsureWeeklyLibraryAllowanceAsync`.
- Cần thêm: hàm đếm theo khung bất kỳ; chặn theo khung từ Task 2; thông báo chu kỳ.
- Còn thiếu / ngoài phạm vi: hiển thị số đã dùng (Task 4, 5).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/PlanBenefits/Interfaces/IPlanUsageService.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/PlanBenefits/Services/PlanUsageService.cs`
- Modify: `src/Core/StoryPlatform.Application/Features/ReadingSessions/Services/ChildReadingService.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/PlanBenefits/PlanUsageServiceTests.cs`, `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildReadingLibraryLimitTests.cs`

**Interfaces:**
- Consumes: `PlanBenefitsDto.NewLibraryStoriesPeriod/PeriodStart/ResetsAt` (Task 2).
- Produces:
  - `Task<int> CountNewLibraryStoriesInPeriodAsync(int ownerUserId, DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default)` — đếm lần mở đầu tiên có `StartedAt` trong `[periodStart, periodEnd)`, bỏ phiên có waiver.
  - `…ThisWeekAsync` giữ chữ ký, gọi lại hàm mới.
  - `public const string CycleLibraryLimitMessageFormat` trong `ChildReadingService` (chuỗi ở Global Constraints).

- [ ] **Step 1: Viết test fail** — `PlanUsageServiceTests`:
  - `CountInPeriod_IgnoresFirstOpensBeforePeriodStart` — truyện mở lần đầu trước `periodStart`, mở lại trong khung → 0.
  - `CountInPeriod_TwoChildrenSameStory_CountsOnce`.
  - `CountInPeriod_ExcludesEventWaiverAndFamilyPackageSessions`.
- [ ] **Step 2: Viết test fail** — `ChildReadingLibraryLimitTests` (gia đình có chu kỳ Đọc, giao dịch `NewLibraryStoriesLimit = 24`):
  - `StartReading_CycleLimit_TwentyFourthAllowed_TwentyFifthForbidden` — 403 với `string.Format(CycleLibraryLimitMessageFormat, 24, VietnamTime.LocalDate(cycle.EndAt))`.
  - `StartReading_CycleLimitReached_MessageUsesVietnamDateOfCycleEnd` — `EndAt = 2026-12-15T17:30:00Z` → thông báo chứa `16/12/2026`.
  - `StartReading_CycleLimitReached_ReopenAlreadyOpenedStory_Allowed`.
  - `StartReading_CycleLimitReached_EventExemptStory_Allowed`.
  - `StartReading_NewCycleStarted_CountResets` — 24 truyện ở chu kỳ 1, chu kỳ 2 (giao dịch 24) bắt đầu → mở truyện mới được.
  - `StartReading_UpgradedToCreativeAfterLimit_Allowed` — chu kỳ Đọc Superseded, chu kỳ Sáng tạo (giao dịch `null`) Active.
  - `StartReading_FreeWeeklyLimit_Unchanged` — mức Miễn phí vẫn 403 với thông báo tuần cũ.
- [ ] **Step 3: Chạy** `--filter "FullyQualifiedName~PlanUsageServiceTests|FullyQualifiedName~ChildReadingLibraryLimitTests"` → FAIL.
- [ ] **Step 4: Implement:** thêm hàm vào interface + service; trong `EnsureWeeklyLibraryAllowanceAsync` lấy benefits một lần, giữ thứ tự: `limit == null` → qua; khóa giao dịch; đã mở → qua; miễn sự kiện → waiver; đếm bằng `CountNewLibraryStoriesInPeriodAsync(owner, PeriodStart, ResetsAt)`; vượt → 403 với thông báo theo period.
- [ ] **Step 5: Chạy lại** → PASS; chạy toàn bộ `--filter "FullyQualifiedName~ChildReading|FullyQualifiedName~PlanUsage|FullyQualifiedName~PlanBenefit"` → PASS.

---

### Task 4: API quota, seed và tài liệu

**Việc này làm gì (nói dễ hiểu):**
Trang gói của phụ huynh lấy dữ liệu từ `GET /TokenQuota/me`. Task này cho API trả thêm "đã mở bao nhiêu truyện mới trong kỳ", đặt sẵn 24 cho Gói Đọc trong dữ liệu mẫu để test, và cập nhật tài liệu API.

**Cần thêm / còn thiếu:**
- Đã có: `MyQuotaDto.Benefits`, `MyQuotaDto.NewLibraryStoriesThisWeek`, seed 37.
- Cần thêm: `MyQuotaDto.NewLibraryStoriesUsed`; seed Gói Đọc = 24; tài liệu.
- Còn thiếu / ngoài phạm vi: bật 24 trên production (Admin làm, spec mục 9).

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/TokenQuota/DTOs/TokenQuotaDtos.cs`, `src/Core/StoryPlatform.Application/Features/TokenQuota/Services/TokenQuotaService.cs`
- Modify: `Database/Seed/37_subscription_plans.sql`
- Modify: `docs/PARENT_PLANS_PAYMENT_API_GUIDE.md`
- Test: `tests/StoryPlatform.UnitTests/Features/TokenQuota/TokenQuotaServiceTests.cs`

**Interfaces:**
- Consumes: Task 2 benefits, Task 3 `CountNewLibraryStoriesInPeriodAsync`.
- Produces: `MyQuotaDto.NewLibraryStoriesUsed : int` (JSON `newLibraryStoriesUsed`).

- [ ] **Step 1: Viết test fail:** `GetMyQuota_ReadingCycle_ReturnsUsedInCycleAndBenefitsWindow` — đã mở 5 truyện mới trong chu kỳ, 2 truyện trước chu kỳ → `NewLibraryStoriesUsed == 5`, `Benefits.WeeklyNewLibraryStories == 24`, `Benefits.NewLibraryStoriesPeriod == "Cycle"`, `Benefits.NewLibraryStoriesResetsAt == cycle.EndAt`; `NewLibraryStoriesThisWeek` giữ cách tính cũ.
- [ ] **Step 2: Chạy** `--filter "FullyQualifiedName~TokenQuotaServiceTests"` → FAIL.
- [ ] **Step 3: Implement** `NewLibraryStoriesUsed` trong `BuildQuotaAsync` (chỉ khi có `Benefits` và `_planUsage`).
- [ ] **Step 4: Seed:** dòng Gói Đọc (Id 2) đổi `NULL` của `WeeklyNewLibraryStories` thành `24`; sửa comment đầu file cho biết với gói chu kỳ là số truyện mỗi chu kỳ.
- [ ] **Step 5: Tài liệu** `PARENT_PLANS_PAYMENT_API_GUIDE.md`: quyền lợi gói (hạn mức theo chu kỳ, chốt trên giao dịch), response `GET /TokenQuota/me` (3 trường benefits mới + `newLibraryStoriesUsed`), lỗi 403 hai thông báo, ghi chú Admin (sửa gói chỉ áp dụng cho giao dịch tạo sau).
- [ ] **Step 6: Kiểm tra toàn bộ Backend:**
  - `dotnet test tests/StoryPlatform.UnitTests` → 0 fail.
  - `dotnet build StoryPlatform.sln` → 0 lỗi.
  - `dotnet test tests/StoryPlatform.IntegrationTests --filter "FullyQualifiedName~Payment|FullyQualifiedName~Subscription|FullyQualifiedName~TokenQuota|FullyQualifiedName~ChildReading"` → 0 fail (nếu môi trường chạy được; báo lại nếu không).

---

### Task 5: Frontend — hiển thị hạn mức cho phụ huynh và nhãn form Admin

**Việc này làm gì (nói dễ hiểu):**
Trong trang tài khoản, phần gói của phụ huynh hiện thêm một dòng "Truyện thư viện mới: 5/24 trong chu kỳ · làm mới ngày …". Hết lượt thì nhắc vẫn đọc lại được truyện cũ và gợi ý nâng gói. Form tạo/sửa gói của Admin đổi nhãn để Admin hiểu con số là theo chu kỳ.

**Cần thêm / còn thiếu:**
- Đã có: `paymentService.myQuota` (`GET /TokenQuota/me`), `PaymentSection.tsx`, `CreatePlanDialog.tsx`, luồng nâng gói trong `PaymentSection`.
- Cần thêm: type cho `benefits` + `newLibraryStoriesUsed`; dòng hiển thị; nhãn form.
- Còn thiếu / ngoài phạm vi: danh sách truyện đã mở; Mobile (repo trống).

**Files** (repo `AI_Storytelling_Frontend`, trong `src/`):
- Modify: `app/types/backendContracts.ts`
- Modify: `app/features/account/PaymentSection.tsx`
- Modify: `app/features/staff/CreatePlanDialog.tsx` (và form sửa gói nếu dùng nhãn riêng)
- Create: `tests/unit/account/paymentSectionLibraryLimit.test.tsx` — file mới vì `paymentSectionCreator.test.tsx` chỉ dành cho Creator; theo đúng cách dựng test của file đó.

**Interfaces:**
- Consumes (JSON từ Task 2, 4): `benefits.weeklyNewLibraryStories: number | null`, `benefits.newLibraryStoriesPeriod: 'Week' | 'Cycle'`, `benefits.newLibraryStoriesPeriodStart: string`, `benefits.newLibraryStoriesResetsAt: string`, `newLibraryStoriesUsed: number`. Khai báo `benefits?` và `newLibraryStoriesUsed?` optional.

- [ ] **Step 1: Viết test fail** trong file test mới:
  - `shows used/limit in cycle with reset date` — `{used: 5, limit: 24, period: 'Cycle', resetsAt: '2026-12-15T17:30:00Z'}` → có `Truyện thư viện mới: 5/24 trong chu kỳ` và `16/12/2026`.
  - `shows weekly wording for free tier` — `period: 'Week'` → `trong tuần`.
  - `shows unlimited when limit is null` → `Truyện thư viện mới: không giới hạn`.
  - `shows exhausted hint when used >= limit` → có `Đã hết lượt mở truyện mới. Bạn vẫn đọc lại được truyện đã mở.`
  - `hides library line when benefits are missing` → không có chữ `Truyện thư viện mới`.
- [ ] **Step 2: Chạy** `npx vitest run tests/unit/account/paymentSectionLibraryLimit.test.tsx` → FAIL.
- [ ] **Step 3: Implement** type + dòng hiển thị (ngày theo giờ Việt Nam, dùng hàm định dạng ngày có sẵn trong repo; nếu hàm sẵn có không chuyển múi giờ thì chuyển sang `Asia/Ho_Chi_Minh` trước khi định dạng). Gợi ý nâng gói chỉ dùng nút/luồng đã có, không tạo luồng mới.
- [ ] **Step 4: Nhãn Admin:** ô hạn mức thư viện của gói → `Truyện thư viện mới mỗi chu kỳ`, ghi chú `Áp dụng cho giao dịch tạo sau khi lưu`. Sửa test hiện có nếu nó kiểm tra đúng chuỗi nhãn cũ (chỉ đổi chuỗi nhãn, không đổi kỳ vọng khác).
- [ ] **Step 5: Kiểm tra:** `npx vitest run tests` → 0 fail; `npx eslint` cho các file đã sửa → pass; `npx tsc --noEmit -p . --incremental false` → pass.

---

### Task 6: Frontend — ô hạn mức thư viện trong form sửa gói của Admin

**Việc này làm gì (nói dễ hiểu):**
Để bật mốc 24 cho Gói Đọc đang bán, Admin cần sửa được hạn mức trên gói đã có. Task này thêm ô "Truyện thư viện mới mỗi chu kỳ" vào form sửa gói, giữ nguyên các quyền lợi khác khi lưu.

**Cần thêm / còn thiếu:**
- Đã có: API `PUT /Payment/admin/plans/{id}` nhận `benefits`; danh sách gói Admin trả `benefits`; type `PlanBenefitsInput`; form tạo gói có ô tương tự (`CreatePlanBenefitsSection.tsx`).
- Cần thêm: type `benefits` cho DTO Admin; ô nhập + "Không giới hạn" trong form sửa; gửi `benefits` đầy đủ khi đổi.
- Còn thiếu / ngoài phạm vi: sửa 4 quyền lợi còn lại trong form sửa.

**Files** (repo `AI_Storytelling_Frontend`, trong `src/`):
- Modify: `app/types/backendContracts.ts` (`AdminSubscriptionPlanDto`, `UpdateSubscriptionPlanRequestDto`)
- Modify: `app/features/staff/AdminSystem.tsx` (`AdminPlans`, `PlanForm`)
- Create: `tests/unit/staff/adminPlansLibraryLimit.test.tsx` — file mới vì chưa có test cho `AdminPlans`.

**Interfaces:**
- Consumes: `PlanBenefitsInput` (`app/types/planSettings.ts`), `pay.plans()`, `pay.updatePlan(id, body)`.
- Produces: `AdminSubscriptionPlanDto.benefits?: PlanBenefitsInput | null`, `UpdateSubscriptionPlanRequestDto.benefits?: PlanBenefitsInput`.

- [ ] **Step 1: Viết test fail** (mock `paymentService.plans` / `updatePlan`):
  - `shows library limit field prefilled for reading plan` — gói Reading `benefits.weeklyNewLibraryStories = null` → mở form sửa thấy ô `Truyện thư viện mới mỗi chu kỳ`, ô `Không giới hạn` đang tích.
  - `saves full benefits with new library limit` — bỏ tích, nhập 24, Lưu → `updatePlan` nhận `benefits` bằng đúng `benefits` cũ trừ `weeklyNewLibraryStories: 24`.
  - `does not send benefits when only name changes` — đổi tên, Lưu → body không có `benefits`.
  - `hides library field for add-on and when benefits missing`.
  - `blocks save for negative or empty library limit`.
- [ ] **Step 2: Chạy** `npx vitest run tests/unit/staff/adminPlansLibraryLimit.test.tsx` → FAIL.
- [ ] **Step 3: Implement** theo spec mục 7.5.
- [ ] **Step 4: Kiểm tra:** `npx vitest run tests` (chỉ còn lỗi có sẵn `coreCompletion.test.ts`), `npx eslint` các file đã sửa/tạo, `npx tsc --noEmit -p . --incremental false` → pass.
