# F-17 – Quản lý gói, quyền lợi theo gói và cấu hình chung · Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admin tạo/sửa gói kèm quyền lợi và cấu hình giá trị chung. Hệ thống chặn thật theo quyền lợi của gói đang hiệu lực, kể cả chọn hồ sơ được đọc khi về Miễn phí.

**Architecture:**
- Một bảng một dòng `plan_settings` làm nguồn cho mọi giá trị chung, đọc qua `IPlanSettingsProvider`.
- `IPlanBenefitService` trả quyền lợi hiệu lực của một tài khoản (gói chu kỳ Active hoặc mức Miễn phí).
- Các dịch vụ hiện có nhận thêm phụ thuộc **tùy chọn** (`= null` ⇒ không chặn, dùng mặc định), theo đúng mẫu đã dùng cho `ITokenQuotaService` ở các vòng trước. Nhờ vậy unit test cũ không phải đổi; DI thật luôn truyền đủ.

**Tech Stack:** .NET, EF Core + Npgsql, xUnit + Moq, `InMemoryUnitOfWork` (tests/StoryPlatform.UnitTests/Features/TokenQuota), `ApiFactory` (SQLite) cho integration, `[PostgresFact]` cho PostgreSQL.

**Spec:** `docs/superpowers/specs/2026-11-11-plan-management-benefits-design.md`

## Global Constraints

- Nhánh mới `feat/plan-management-benefits` tạo từ `origin/main`.
- **Mặc định `plan_settings`:**
  - quỹ Creator: ReadingFundPercent 30, CreativeFundPercent 10;
  - hạn mức tháng Creator: CreatorMonthlyAiCredits 2, CreatorMonthlySubmitCredits 5;
  - Creator đối tác: PartnerMonthlyAiCredits 6, PartnerMonthlySubmitCredits 10;
  - MinimumReadSeconds 60, TrialAiCredits 1;
  - mức Miễn phí: FreeMaxChildProfiles 1, FreeWeeklyNewLibraryStories 3, FreeCanCopyRoadmapTemplates false, FreeReadAloudPerMonth 5, FreeWritingAssistPerDay 5;
  - trợ lý viết Creator: CreatorWritingAssistPerDay 20, PartnerWritingAssistPerDay 40.
- **Seed quyền lợi gói:**
  - Gói Đọc: MaxChildProfiles 4, WeeklyNewLibraryStories null, CanCopyRoadmapTemplates true, ReadAloudPerMonth 60, WritingAssistPerDay 5;
  - Gói Sáng tạo: như Gói Đọc nhưng WritingAssistPerDay 20;
  - Add-on, Gói Creator, Legacy: 0 / null / false / null / 0.
- **Giờ Việt Nam** = UTC+7: tuần bắt đầu 00:00 thứ Hai, ngày bắt đầu 00:00.
- **Mã lỗi:**
  - số hồ sơ: `ConflictException` (409);
  - hồ sơ chỉ xem, thư viện, bộ truyện mẫu: `ForbiddenException` (403);
  - trợ lý viết: `AssistanceException("ASSISTANCE_DAILY_LIMIT", 429)`;
  - đổi hồ sơ được đọc lần hai trong tuần: `ConflictException` (409).
- **Thông điệp lỗi tiếng Việt, cố định:**
  - "Gói hiện tại cho phép tối đa {n} hồ sơ trẻ. Nâng cấp gói để thêm hồ sơ."
  - "Hồ sơ đang ở chế độ chỉ xem. Phụ huynh có thể chọn hồ sơ được đọc hoặc nâng cấp gói."
  - "Đã mở đủ {n} truyện thư viện mới trong tuần. Truyện mới mở lại vào thứ Hai."
  - "Gói hiện tại không hỗ trợ sao chép bộ truyện mẫu. Nâng cấp gói để sao chép."
  - "Chỉ được đổi hồ sơ được đọc 1 lần mỗi tuần. Lần đổi tiếp theo: {dd/MM/yyyy}."
- Truyện thư viện công khai = `Story.Scope == StoryScope.Creator && Story.IsPublished`. Phiên có `FamilyConfirmationId` không tính.
- Mọi thay đổi gói/cấu hình ghi `IAuditLogWriter.LogAsync(admin, action, entity, id, before, after)`. Tên action: `SubscriptionPlanCreated`, `SubscriptionPlanUpdated`, `PlanSettingsUpdated`, `ReadingSlotsChanged`.
- Không đổi hành vi giá/lượt chốt theo giao dịch; không cho tạo hoặc mở bán Legacy.

## Review Focus

1. **Đọc lại truyện thư viện đã mở** (kể cả trẻ khác trong cùng tài khoản đã mở) khi đã hết 3 truyện/tuần → phải đọc được. Test: Task 7 `StartAsync_ReopeningAStoryAnotherChildOpened_IsNotCounted`.
2. **Tài khoản trả phí có hơn 1 hồ sơ, chưa từng chọn hồ sơ** → mọi hồ sơ Active vẫn vào được; nhóm "được đọc" chỉ bị thu hẹp khi số hồ sơ Active vượt giới hạn. Test: Task 6 `Readable_WhenActiveCountWithinLimit_ReturnsAll`.
3. **Ranh giới tuần lúc 23:59 Chủ nhật / 00:00 thứ Hai giờ Việt Nam** (tức 16:59 / 17:00 UTC Chủ nhật). Test: Task 2 `StartOfWeekUtc_SundayLateEveningVietnam_StaysInSameWeek`.
4. **Hồ sơ Archived hoặc Deleted** không chiếm chỗ khi tạo hồ sơ mới; hồ sơ Draft và Suspended có chiếm chỗ. Test: Task 5 `Create_IgnoresArchivedProfiles_CountsDraftAndSuspended`.
5. **Admin sửa gói nhưng bỏ trống phần quyền lợi** → quyền lợi giữ nguyên. Test: Task 4 `UpdatePlan_WithoutBenefits_KeepsExistingBenefits`.

---

### Task 1: Dữ liệu – bảng cấu hình, cột quyền lợi, cột chọn hồ sơ

**Việc này làm gì (nói dễ hiểu):**
Tạo chỗ lưu cho mọi thứ Admin sẽ cấu hình: quyền lợi của từng gói và một bảng cấu hình chung (tỷ lệ quỹ, hạn mức Creator, lượt dùng thử, thời gian đọc tối thiểu, quyền lợi mức Miễn phí). Thêm hai cột để nhớ phụ huynh đã chọn hồ sơ nào được đọc và lần đổi gần nhất. Chưa có hành vi mới nào; chỉ là dữ liệu và seed.

**Cần thêm / còn thiếu:**
- Đã có:
  - `subscription_plans` với PlanType, Audience, PriceVnd, AiCredits, SubmitCredits, IsActive;
  - migration gần nhất `20261111064132_AddCreatorPackAndSubmissionCredits`;
  - seed `Database/Seed/37_subscription_plans.sql`.
- Cần thêm:
  - 5 cột quyền lợi trên `subscription_plans`;
  - entity và bảng `plan_settings` kèm dòng Id = 1;
  - `child_profiles.ReadingSlotSelectedAt` (lần đổi gần nhất = giá trị lớn nhất trên các hồ sơ của tài khoản);
  - một migration; seed 37 cập nhật và seed mới `72_plan_settings.sql`; cập nhật dbml.
- Còn thiếu / ngoài phạm vi: cột cho giới hạn sinh lại (spec mục 2 để ngoài phạm vi).

**Files:**
- Create: `src/Core/StoryPlatform.Domain/Entities/PlanSettings.cs`
- Create: `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/PlanSettingsConfiguration.cs`
- Modify:
  - `src/Core/StoryPlatform.Domain/Entities/SubscriptionPlan.cs`
  - `src/Core/StoryPlatform.Domain/Entities/ChildProfile.cs`
  - `src/Core/StoryPlatform.Domain/Entities/UserAccount.cs`
  - `src/Core/StoryPlatform.Infrastructure/Persistence/Configurations/SubscriptionPlanConfiguration.cs`
  - `src/Core/StoryPlatform.Infrastructure/Persistence/ApplicationDbContext.cs` (`DbSet<PlanSettings> PlanSettings`)
- Create: migration `AddPlanBenefitsAndSettings` (generated)
- Modify: `Database/Seed/37_subscription_plans.sql`, `Database/ai_storytelling_platform.dbml`
- Create: `Database/Seed/72_plan_settings.sql`

**Interfaces:**
- Produces:
  - `PlanSettings : BaseEntity`, các property đúng tên ở Global Constraints. Giá trị mặc định nằm ở property initializer, nên `new PlanSettings()` chính là cấu hình mặc định.
  - `SubscriptionPlan`: `int MaxChildProfiles`, `int? WeeklyNewLibraryStories`, `bool CanCopyRoadmapTemplates`, `int? ReadAloudPerMonth`, `int WritingAssistPerDay`.
  - `ChildProfile.ReadingSlotSelectedAt : DateTime?`.

- [ ] **Step 1:** Thêm entity, property và configuration. Bảng `plan_settings`, các cột int NOT NULL có default bằng giá trị mặc định. Có CHECK cho 0 ≤ tỷ lệ quỹ ≤ 100 và `"MinimumReadSeconds" BETWEEN 1 AND 3600`.
- [ ] **Step 2:** `dotnet ef migrations add AddPlanBenefitsAndSettings -p src/Core/StoryPlatform.Infrastructure -s src/Core/StoryPlatform.Api`. Sửa tay migration để:
  - cập nhật quyền lợi theo `PlanType` (Reading = 1, Creative = 2) bằng `UPDATE subscription_plans SET ... WHERE "PlanType" = ...`. Dùng giá trị số hay chuỗi phải khớp cách cột PlanType đang lưu; xem migration `20261110071435`.
  - `INSERT INTO plan_settings ("Id", ...) VALUES (1, ...) ON CONFLICT DO NOTHING`.
- [ ] **Step 3:** Seed 37 thêm 5 cột quyền lợi. Seed 72 chèn dòng Id = 1, idempotent như các seed 67–71. Dbml thêm bảng và cột.
- [ ] **Step 4:** Kiểm tra.
  - Run: `dotnet build` → Expected: build thành công.
  - Run: `dotnet test tests/StoryPlatform.IntegrationTests --filter "FullyQualifiedName~Subscription"` → Expected: PASS. Model SQLite của ApiFactory tạo được bảng mới.

---

### Task 2: Giờ Việt Nam, đọc cấu hình, quyền lợi hiệu lực

**Việc này làm gì (nói dễ hiểu):**
Ba công cụ nền mà mọi chỗ chặn sau đó dùng chung:
- tính "đầu ngày / đầu tuần / đầu tháng theo giờ Việt Nam";
- đọc bảng cấu hình (thiếu dòng thì dùng mặc định);
- trả lời câu hỏi "tài khoản này đang có quyền lợi gì": theo gói đang dùng, hoặc theo mức Miễn phí nếu không có gói.

**Cần thêm / còn thiếu:**
- Đã có: `VietnamOffset` khai báo riêng trong `TokenQuotaService.Creator.cs` và `CreatorStatisticsService.cs`; `user_subscriptions` với `PlanId`, `Status`.
- Cần thêm:
  - `VietnamTime` dùng chung, và chuyển hai chỗ trên sang dùng nó;
  - `IPlanSettingsProvider` và `IPlanBenefitService`, đăng ký trong DI.
- Còn thiếu / ngoài phạm vi: hạng Creator đối tác. Creator luôn nhận `CreatorWritingAssistPerDay`.

**Files:**
- Create: `src/Core/StoryPlatform.Application/Common/Time/VietnamTime.cs`
- Create trong `src/Core/StoryPlatform.Application/Features/PlanBenefits/`:
  - `Interfaces/IPlanSettingsProvider.cs`, `Interfaces/IPlanBenefitService.cs`
  - `Services/PlanSettingsProvider.cs`, `Services/PlanBenefitService.cs`
  - `DTOs/PlanBenefitsDto.cs`
- Modify:
  - `src/Core/StoryPlatform.Application/DependencyInjection.cs`
  - `Features/TokenQuota/Services/TokenQuotaService.Creator.cs`
  - `Features/CreatorStatistics/Services/CreatorStatisticsService.cs`
- Test:
  - `tests/StoryPlatform.UnitTests/Common/VietnamTimeTests.cs`
  - `tests/StoryPlatform.UnitTests/Features/PlanBenefits/PlanBenefitServiceTests.cs`

**Interfaces:**
- Produces:
  - `static class VietnamTime`, mọi tham số và kết quả là DateTime UTC:
    - `DateTime StartOfDayUtc(DateTime utcNow)`
    - `DateTime StartOfWeekUtc(DateTime utcNow)` (thứ Hai)
    - `DateTime StartOfMonthUtc(DateTime utcNow)`
    - `DateTime StartOfNextMonthUtc(DateTime utcNow)`
    - `DateTime StartOfNextWeekUtc(DateTime utcNow)`
  - `IPlanSettingsProvider.GetAsync(CancellationToken ct = default) : Task<PlanSettings>`. Trả dòng Id = 1, hoặc `new PlanSettings()` nếu không có.
  - `PlanBenefitsDto`:
    - `string Tier`: "Free", "Creator" hoặc tên gói;
    - `int? PlanId`;
    - `int MaxChildProfiles`, `int? WeeklyNewLibraryStories`, `bool CanCopyRoadmapTemplates`, `int? ReadAloudPerMonth`, `int WritingAssistPerDay`.
  - `IPlanBenefitService.GetForUserAsync(int userId, CancellationToken ct = default) : Task<PlanBenefitsDto>`

- [ ] **Step 1: Test VietnamTime (failing)**
  - `StartOfWeekUtc_SundayLateEveningVietnam_StaysInSameWeek`: `2026-11-15T16:59Z` → `2026-11-08T17:00Z`.
  - `StartOfWeekUtc_MondayMidnightVietnam_StartsNewWeek`: `2026-11-15T17:00Z` → `2026-11-15T17:00Z`.
  - `StartOfDayUtc`: `2026-11-11T18:00Z` → `2026-11-11T17:00Z`.
  - `StartOfNextMonthUtc`: `2026-11-30T17:30Z` → `2026-12-31T17:00Z`. Lúc đó đã là 01/12 giờ Việt Nam, nên tháng sau bắt đầu 01/01.
- [ ] **Step 2: Test PlanBenefitService (failing)**
  - `GetForUser_ActiveCreativeCycle_ReturnsPlanBenefits`: MaxChildProfiles 4, WritingAssistPerDay 20, Tier = tên gói.
  - `GetForUser_NoActiveCycle_ReturnsFreeSettings`: 1 / 3 / false / 5 / 5, Tier "Free". Chu kỳ Expired, Superseded hoặc Scheduled không được tính.
  - `GetForUser_Creator_ReturnsCreatorWritingLimit`: 20, Tier "Creator".
  - `GetForUser_UsesCurrentPlanRow_AfterAdminEdit`: sửa `plan.MaxChildProfiles = 2` thì kết quả là 2.
- [ ] **Step 3: Cài đặt**
  - Chu kỳ hiệu lực = `UserSubscription` có `Status == Active`, `StartAt <= now < EndAt` của user, với gói Reading hoặc Creative.
  - Dịch vụ dùng `TimeProvider` (đã đăng ký trong DI và được dùng ở StoryAssistanceService).
  - Thay hai `VietnamOffset` cục bộ bằng `VietnamTime`.
- [ ] **Step 4:** Run `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~VietnamTime|FullyQualifiedName~PlanBenefit|FullyQualifiedName~TokenQuota|FullyQualifiedName~CreatorStatistics"`. Expected: PASS.

---

### Task 3: Admin xem/sửa cấu hình chung và bỏ các giá trị ghi cứng

**Việc này làm gì (nói dễ hiểu):**
Admin mở trang cấu hình, sửa tỷ lệ quỹ Creator, hạn mức tháng Creator, lượt dùng thử, thời gian đọc tối thiểu và quyền lợi Miễn phí. Mỗi lần lưu đều ghi nhật ký trước/sau. Từ lúc lưu, thống kê doanh thu, thu nhập Creator, hạn mức tháng Creator và lượt dùng thử dùng con số mới thay cho số ghi cứng trong code.

**Cần thêm / còn thiếu:**
- Đã có:
  - hằng `ReadingFundPercent` / `CreativeFundPercent` trong `RevenueStatisticsService`;
  - `MinimumReadSeconds` trong `CreatorStatisticsService`;
  - `CreatorMonthlyAiCredits` / `CreatorMonthlySubmitCredits` trong `TokenQuotaService.Creator`;
  - số 1 trong `EnsureTrialGrantedAsync`;
  - `IAuditLogWriter`.
- Cần thêm:
  - `IPlanSettingsService` (Get / Update có validation và audit);
  - 2 endpoint Admin;
  - 4 dịch vụ trên đọc từ `IPlanSettingsProvider`.
- Còn thiếu / ngoài phạm vi:
  - Báo cáo đã Published giữ snapshot cũ; không tính lại.
  - Lô hạn mức tháng đã cấp giữ nguyên số cũ.

**Files:**
- Create trong `src/Core/StoryPlatform.Application/Features/PlanBenefits/`:
  - `DTOs/PlanSettingsDtos.cs` (`PlanSettingsDto`, `UpdatePlanSettingsRequestDto`, cùng tên property với entity)
  - `Interfaces/IPlanSettingsService.cs`, `Services/PlanSettingsService.cs`
- Modify:
  - `src/Core/StoryPlatform.Api/Controllers/PaymentController.cs` (`GET` và `PUT admin/settings`)
  - `Features/BusinessReports/Services/RevenueStatisticsService.cs`
  - `Features/CreatorStatistics/Services/CreatorStatisticsService.cs`
  - `Features/TokenQuota/Services/TokenQuotaService.cs`, `TokenQuotaService.Creator.cs`
  - `DependencyInjection.cs`
- Test:
  - `tests/StoryPlatform.UnitTests/Features/PlanBenefits/PlanSettingsServiceTests.cs`
  - thêm case vào `RevenueStatisticsServiceTests`, `CreatorStatisticsServiceTests`, `TokenQuotaServiceCreatorTests`, `TokenQuotaServiceTests`

**Interfaces:**
- Consumes: `IPlanSettingsProvider` (Task 2).
- Produces:
  - `IPlanSettingsService.GetAsync(ct) : Task<PlanSettingsDto>`
  - `IPlanSettingsService.UpdateAsync(int adminUserId, UpdatePlanSettingsRequestDto request, ct) : Task<PlanSettingsDto>`
  - Constructor thêm tham số cuối tùy chọn `IPlanSettingsProvider? planSettings = null` cho `RevenueStatisticsService`, `CreatorStatisticsService` và `TokenQuotaService`. Khi null thì dùng `new PlanSettings()`.

- [ ] **Step 1: Test (failing)**
  - `Update_ValidRequest_SavesAndAuditsBeforeAfter`: audit action `PlanSettingsUpdated`; before có `ReadingFundPercent = 30`, after có 25.
  - `Update_Rejects`: các trường hợp dưới đây đều ném `BadRequestException`:
    - fund 101 hoặc -1;
    - `MinimumReadSeconds` 0 hoặc 3601;
    - `FreeMaxChildProfiles` 0;
    - số lượt hoặc số lần âm;
    - `FreeWeeklyNewLibraryStories` âm (null nghĩa là không giới hạn và hợp lệ).
  - `Revenue_UsesConfiguredFundRates`: rates 20/5 với Reading 100 000 và Creative 100 000 → `CreatorFundVnd` = 25 000.
  - `CreatorStatistics_UsesConfiguredMinimumReadSeconds`: đặt 30; phiên 45 giây được tính là lượt đọc hợp lệ.
  - `CreatorQuota_UsesConfiguredMonthlyAllowance`: đặt 3/7 thì lô tháng mới là 3 lượt AI và 7 lượt gửi duyệt.
  - `Trial_UsesConfiguredTrialCredits`: đặt 2 thì lô Trial = 2.
- [ ] **Step 2:** Run các test trên. Expected: FAIL (chưa có service hoặc vẫn dùng hằng).
- [ ] **Step 3:** Cài đặt.
  - Xoá các hằng cũ. `CreatorStatisticsService.MinimumReadSeconds` đang public const; mọi chỗ tham chiếu chuyển sang giá trị đọc từ cấu hình.
  - Endpoint: `[Authorize(Roles = "Administrator")]`, message "Lấy cấu hình gói thành công." và "Cập nhật cấu hình gói thành công.".
- [ ] **Step 4:** Run `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~PlanSettings|FullyQualifiedName~BusinessReports|FullyQualifiedName~CreatorStatistics|FullyQualifiedName~TokenQuota"`. Expected: PASS.

---

### Task 4: Admin tạo gói và sửa quyền lợi; người dùng thấy quyền lợi

**Việc này làm gì (nói dễ hiểu):**
- Admin tạo được gói mới: tên, đối tượng, loại, giá, lượt và quyền lợi.
- Khi sửa gói, Admin chỉnh được cả quyền lợi; nếu bỏ trống phần quyền lợi thì giữ nguyên như cũ.
- Phụ huynh và Creator xem danh sách gói sẽ thấy quyền lợi của từng gói.

**Cần thêm / còn thiếu:**
- Đã có:
  - `PaymentService.UpdatePlanAsync` với validation và audit `SubscriptionPlanUpdated`;
  - `ListAllPlansAsync`, `ListActivePlansAsync`;
  - các DTO `SubscriptionPlanDto`, `AdminSubscriptionPlanDto`, `UpdateSubscriptionPlanRequestDto`.
- Cần thêm:
  - `CreatePlanAsync` + `POST admin/plans`;
  - `PlanBenefitsInputDto` dùng chung cho create và update;
  - trường quyền lợi trong cả hai DTO trả về.
- Còn thiếu / ngoài phạm vi: xoá gói (không bao giờ xoá, chỉ tắt bán).

**Files:**
- Modify:
  - `Features/Payments/DTOs/PaymentDtos.cs`
  - `Features/Payments/Interfaces/IPaymentService.cs`
  - `Features/Payments/Services/PaymentService.cs`
  - `src/Core/StoryPlatform.Api/Controllers/PaymentController.cs`
- Test:
  - `tests/StoryPlatform.UnitTests/Features/Payments/PaymentServiceTests.cs`
  - `tests/StoryPlatform.IntegrationTests/Api/AdminPaymentApiTests.cs`

**Interfaces:**
- Produces:
  - `PlanBenefitsInputDto`: `int MaxChildProfiles`, `int? WeeklyNewLibraryStories`, `bool CanCopyRoadmapTemplates`, `int? ReadAloudPerMonth`, `int WritingAssistPerDay`.
  - `CreateSubscriptionPlanRequestDto`: `Name`, `PlanAudience Audience`, `PlanType PlanType`, `int PriceVnd`, `int AiCredits`, `int SubmitCredits`, `bool IsActive`, `PlanBenefitsInputDto? Benefits`.
  - `UpdateSubscriptionPlanRequestDto`: thêm `PlanBenefitsInputDto? Benefits`.
  - `SubscriptionPlanDto` và `AdminSubscriptionPlanDto`: thêm `PlanBenefitsInputDto? Benefits`, chỉ có giá trị khi gói là Reading hoặc Creative.
  - `IPaymentService.CreatePlanAsync(int adminUserId, CreateSubscriptionPlanRequestDto request, CancellationToken ct = default) : Task<AdminSubscriptionPlanDto>`

- [ ] **Step 1: Test (failing)**
  - `CreatePlan_ValidReading_SavesWithBenefitsAndAudits`: action `SubscriptionPlanCreated`, before null.
  - `CreatePlan_Rejects`: các trường hợp dưới đây đều ném `BadRequestException`:
    - cặp đối tượng/loại sai: User + CreatorPack, Creator + Reading;
    - loại Legacy;
    - giá ≤ 0;
    - số lượt âm;
    - gói chu kỳ mà `Benefits` null hoặc `MaxChildProfiles` < 1;
    - tên rỗng hoặc dài hơn 150 ký tự.
  - `CreatePlan_AddOn_IgnoresBenefits`: lưu 0 / null / false / null / 0.
  - `UpdatePlan_WithBenefits_UpdatesAndAuditsBenefitFields`.
  - `UpdatePlan_WithoutBenefits_KeepsExistingBenefits`.
  - `ListActivePlans_IncludesBenefitsForCyclePlans`.
  - Integration `AdminCreatesPlan_ParentSeesItWithBenefits`: POST trả 201, rồi phụ huynh `GET /api/v1/Payment/plans` thấy gói có `benefits.maxChildProfiles`.
- [ ] **Step 2:** Run test → Expected: FAIL.
- [ ] **Step 3:** Cài đặt. Controller trả 201 giống `POST transactions`, message "Tạo gói thành công.".
- [ ] **Step 4:** Run `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~PaymentService` và `dotnet test tests/StoryPlatform.IntegrationTests --filter FullyQualifiedName~AdminPayment`. Expected: PASS.

---

### Task 5: Chặn số hồ sơ trẻ theo gói

**Việc này làm gì (nói dễ hiểu):**
Tài khoản Miễn phí chỉ có 1 hồ sơ trẻ; Gói Đọc và Gói Sáng tạo tối đa 4 (Admin chỉnh được). Khi phụ huynh tạo hồ sơ mới hoặc khôi phục hồ sơ đã lưu trữ mà đã đủ số, hệ thống từ chối và gợi ý nâng gói. Hồ sơ đang có không bị ảnh hưởng.

**Cần thêm / còn thiếu:**
- Đã có: `ChildProfileService.CreateChildProfileAsync` và `RestoreChildProfileAsync`.
- Cần thêm: đếm hồ sơ chiếm chỗ (Draft, Active, Suspended, chưa xoá) và so với `MaxChildProfiles`.
- Còn thiếu / ngoài phạm vi: hồ sơ đã có vượt số sau khi về Miễn phí được xử lý ở Task 6 (chỉ xem), không bị chặn ở đây.

**Files:**
- Modify: `src/Core/StoryPlatform.Application/Features/ChildProfiles/Profiles/Services/ChildProfileService.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ChildProfiles/ChildProfilePlanLimitTests.cs`

**Interfaces:**
- Consumes: `IPlanBenefitService` (Task 2), truyền vào constructor dưới dạng tham số cuối tùy chọn `IPlanBenefitService? planBenefits = null`.

- [ ] **Step 1: Test (failing)**
  - `Create_FreeAccountWithOneProfile_ThrowsConflict`: thông điệp chứa "tối đa 1 hồ sơ trẻ".
  - `Create_IgnoresArchivedProfiles_CountsDraftAndSuspended`.
  - `Create_ReadingPlanWithThreeProfiles_Allowed`.
  - `Restore_WhenAtLimit_ThrowsConflict`.
- [ ] **Step 2:** Run → Expected: FAIL.
- [ ] **Step 3:** Cài đặt. Kiểm tra số hồ sơ trước khi `AddAsync` hoặc trước khi đổi trạng thái khi khôi phục.
- [ ] **Step 4:** Run `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ChildProfile`. Expected: PASS.
- [ ] **Step 5:** Run `dotnet test tests/StoryPlatform.IntegrationTests`.
  - Test nào tạo nhiều hồ sơ cho một phụ huynh không có gói sẽ fail. Thêm helper `ApiFactory.GivePaidPlanAsync(int userId, PlanType type = PlanType.Reading)`: tạo một `UserSubscription` Active 30 ngày gắn `PaymentTransaction` Paid. Gọi helper này trong các test đó.
  - Expected: PASS.

---

### Task 6: Hồ sơ được đọc khi vượt số hồ sơ (về Miễn phí)

**Việc này làm gì (nói dễ hiểu):**
- Khi gói hết hạn mà phụ huynh có nhiều hồ sơ hơn số cho phép, chỉ một số hồ sơ được vào đọc. Đó là hồ sơ phụ huynh đã chọn; nếu chưa chọn thì là hồ sơ trẻ đọc gần nhất.
- Các hồ sơ khác chỉ xem: trẻ không đăng nhập bằng QR, không được giao truyện, không mở phiên đọc được. Hồ sơ không bị xoá.
- Phụ huynh xem và đổi lựa chọn, tối đa 1 lần mỗi tuần. Khi đổi, trẻ của hồ sơ vừa chuyển sang chỉ xem bị đăng xuất.

**Cần thêm / còn thiếu:**
- Đã có:
  - `ChildAccessCredentialService.LoginWithEasyLoginAsync`, `StartSupervisedSessionAsync`, `StartStoryHandoverAsync`;
  - `ChildReadingService` bắt đầu phiên đọc;
  - `IChildSessionRevoker`;
  - cột `ReadingSlotSelectedAt` (Task 1).
- Cần thêm:
  - `IReadingSlotService`;
  - `GET` / `PUT api/v1/ChildProfile/reading-slots`;
  - chặn ở 4 chỗ vào đọc.
- Còn thiếu / ngoài phạm vi: thông báo cho phụ huynh khi về Miễn phí (chưa có trong spec).

**Files:**
- Create trong `src/Core/StoryPlatform.Application/Features/ChildProfiles/ReadingSlots/`:
  - `IReadingSlotService.cs`, `ReadingSlotService.cs`, `ReadingSlotDtos.cs`
- Modify:
  - `Features/ChildProfiles/AccessCredentials/Services/ChildAccessCredentialService.cs`
  - `Features/ReadingSessions/Services/ChildReadingService.cs`
  - `src/Core/StoryPlatform.Api/Controllers/ChildProfileController.cs`
  - `DependencyInjection.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ChildProfiles/ReadingSlotServiceTests.cs`

**Interfaces:**
- Consumes: `IPlanBenefitService`, `VietnamTime`.
- Produces:
  - `ReadingSlotsDto`:
    - `int Slots`;
    - `bool IsOverLimit`;
    - `DateTime? NextChangeAllowedAt`: null nghĩa là đổi được ngay;
    - `List<ReadingSlotProfileDto> Profiles`, mỗi phần tử gồm `ChildProfileId`, `Nickname`, `bool CanRead`, `bool IsSelected`.
  - `IReadingSlotService`:
    - `GetAsync(int ownerUserId, ct) : Task<ReadingSlotsDto>`
    - `SelectAsync(int ownerUserId, IReadOnlyList<int> childProfileIds, ct) : Task<ReadingSlotsDto>`
    - `GetReadableProfileIdsAsync(int ownerUserId, ct) : Task<IReadOnlySet<int>>`
    - `EnsureCanReadAsync(int childProfileId, ct) : Task`: ném `ForbiddenException` với thông điệp "chỉ xem".
  - `SelectReadingSlotsRequestDto { List<int> ChildProfileIds }`.

- [ ] **Step 1: Test (failing)**
  - `Readable_WhenActiveCountWithinLimit_ReturnsAll`.
  - `Readable_FreeWithTwoActive_NoSelection_PicksMostRecentReadingSession`.
  - `Readable_NoSessionsNoSelection_PicksEarliestCreated`.
  - `Readable_PrefersSelectedProfiles`.
  - `Select_MoreThanSlots_ThrowsBadRequest`.
  - `Select_ProfileNotOwnedOrNotActive_ThrowsBadRequest`.
  - `Select_SecondTimeSameVietnamWeek_ThrowsConflictWithNextMonday`: chọn lúc `2026-11-11T03:00Z` rồi chọn lại cùng ngày → 409, thông điệp chứa "16/11/2026".
  - `Select_NextWeek_Allowed`.
  - `Select_RevokesChildSessionsOfProfilesLosingAccess`: `IChildSessionRevoker` được gọi với hồ sơ bị chuyển sang chỉ xem.
  - `Select_AuditsReadingSlotsChanged`.
  - `EnsureCanRead_ViewOnlyProfile_ThrowsForbidden`.
- [ ] **Step 2:** Run → Expected: FAIL.
- [ ] **Step 3:** Cài đặt theo spec mục 6.
  - Chọn mới: set `ReadingSlotSelectedAt = now` cho hồ sơ được chọn và null cho các hồ sơ khác; lần đổi gần nhất suy ra từ `ReadingSlotSelectedAt` lớn nhất.
  - Gọi `EnsureCanReadAsync` ở `LoginWithEasyLoginAsync` (sau khi tìm được hồ sơ, trước khi phát phiên), `StartSupervisedSessionAsync`, `StartStoryHandoverAsync`, và lúc `ChildReadingService` bắt đầu phiên mới. Tiếp tục phiên đang mở cũng bị chặn, vì hồ sơ chỉ xem không được đọc.
  - Thêm vào cả hai service một tham số cuối tùy chọn `IReadingSlotService? readingSlots = null`.
  - Controller: `[Authorize(Roles = "User")]`, lấy owner từ token.
- [ ] **Step 4:** Run `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ReadingSlot|FullyQualifiedName~ChildAccess|FullyQualifiedName~ReadingSessions"`. Expected: PASS.

---

### Task 7: Giới hạn 3 truyện thư viện mới mỗi tuần

**Việc này làm gì (nói dễ hiểu):**
- Tài khoản Miễn phí chỉ mở được 3 truyện mới trong thư viện công khai mỗi tuần, tính chung cho cả nhà, đếm lại từ 00:00 thứ Hai.
- Đọc lại truyện đã mở, truyện phụ huynh tự tạo hoặc truyện được giao cho gia đình đều không bị tính.
- Hết lượt trong tuần thì trẻ nhận thông báo truyện mới mở lại vào thứ Hai.

**Cần thêm / còn thiếu:**
- Đã có: `ChildReadingService` bắt đầu phiên trong transaction có `AcquireTransactionLockAsync(storyId)`.
- Cần thêm:
  - `IPlanUsageService.CountNewLibraryStoriesThisWeekAsync` (Task 10 dùng lại);
  - kiểm tra trước khi tạo phiên mới;
  - khoá thêm theo tài khoản.
- Còn thiếu / ngoài phạm vi: bước lộ trình hiện chữ "Mở vào thứ Hai" là việc của frontend; backend trả 403 kèm thông điệp.

**Files:**
- Create trong `src/Core/StoryPlatform.Application/Features/PlanBenefits/`:
  - `Interfaces/IPlanUsageService.cs`, `Services/PlanUsageService.cs`
- Modify: `Features/ReadingSessions/Services/ChildReadingService.cs`, `DependencyInjection.cs`
- Test:
  - `tests/StoryPlatform.UnitTests/Features/PlanBenefits/PlanUsageServiceTests.cs`
  - `tests/StoryPlatform.UnitTests/Features/ReadingSessions/ChildReadingLibraryLimitTests.cs`
  - `tests/StoryPlatform.IntegrationTests/Postgres/PlanBenefitsPostgresTests.cs`

**Interfaces:**
- Produces:
  - `IPlanUsageService.CountNewLibraryStoriesThisWeekAsync(int ownerUserId, DateTime utcNow, ct) : Task<int>`: đếm số truyện thư viện công khai có phiên đầu tiên (trên mọi hồ sơ của owner, không tính phiên có `FamilyConfirmationId`) bắt đầu từ `VietnamTime.StartOfWeekUtc(utcNow)`.
  - `IPlanUsageService.HasOpenedLibraryStoryAsync(int ownerUserId, int storyId, ct) : Task<bool>`.
  - `IPlanUsageService.CountWritingAssistsTodayAsync(int userId, DateTime utcNow, ct) : Task<int>`.
  - Lock theo tài khoản: `AcquireTransactionLockAsync` với một khoá phân biệt với storyId. Dùng `-(1_000_000_000 + ownerUserId)` hoặc overload hai khoá nếu `IUnitOfWork` có; kiểm tra chữ ký thật trước khi chọn.

- [ ] **Step 1: Test (failing)**
  - `StartAsync_FreeFourthNewLibraryStoryInWeek_ThrowsForbidden`: thông điệp chứa "3 truyện".
  - `StartAsync_ReopeningAStoryAnotherChildOpened_IsNotCounted`.
  - `StartAsync_StoryOpenedLastWeek_IsNotNewAndAllowed`.
  - `StartAsync_ParentStoryOrFamilyConfirmation_NotLimited`.
  - `StartAsync_ResumingOpenSession_Allowed`.
  - `StartAsync_PaidPlan_Unlimited`.
  - `Count_ResetsAtMondayVietnam`.
  - PostgreSQL `[PostgresFact]` `TwoChildrenOpenThirdAndFourthNewStoriesConcurrently_OnlyOneSucceeds`: tài khoản đã mở 2 truyện; hai trẻ cùng mở hai truyện khác nhau → đúng 1 thành công và 1 bị `ForbiddenException`.
- [ ] **Step 2:** Run → Expected: FAIL.
- [ ] **Step 3:** Cài đặt.
  - Kiểm tra nằm trong transaction, sau khi khoá tài khoản và trước khi `AddAsync` phiên mới. Truyện chưa từng mở và đã đủ giới hạn thì ném lỗi.
  - Thêm vào constructor các tham số cuối tùy chọn `IPlanBenefitService? planBenefits = null` và `IPlanUsageService? planUsage = null`.
- [ ] **Step 4:** Run `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~ReadingSessions|FullyQualifiedName~PlanUsage"`. Với PostgreSQL, đặt `STORYPLATFORM_TEST_PG` rồi chạy `dotnet test tests/StoryPlatform.IntegrationTests --filter FullyQualifiedName~PlanBenefitsPostgres`. Expected: PASS.

---

### Task 8: Chặn sao chép bộ truyện mẫu với tài khoản Miễn phí

**Việc này làm gì (nói dễ hiểu):**
Tài khoản Miễn phí vẫn xem trước được bộ truyện mẫu nhưng không sao chép vào lộ trình của con; hệ thống báo cần nâng gói. Gói Đọc và Gói Sáng tạo sao chép bình thường.

**Cần thêm / còn thiếu:**
- Đã có: `RoadmapLibraryService.CopyAsync` (`RoadmapLibraryService.Copy.cs`); xem trước là method riêng.
- Cần thêm: kiểm tra `CanCopyRoadmapTemplates` ở đầu `CopyAsync`.
- Còn thiếu / ngoài phạm vi: không có.

**Files:**
- Modify:
  - `Features/ChildRoadmaps/Services/RoadmapLibraryService.cs` (constructor, thêm tham số cuối tùy chọn `IPlanBenefitService? planBenefits = null`)
  - `Features/ChildRoadmaps/Services/RoadmapLibraryService.Copy.cs`
- Test: `tests/StoryPlatform.UnitTests/Features/ChildRoadmaps/RoadmapCopyPlanLimitTests.cs`

- [ ] **Step 1: Test (failing)**
  - `Copy_FreeAccount_ThrowsForbidden`: thông điệp chứa "sao chép bộ truyện mẫu".
  - `Copy_ReadingPlan_Proceeds`.
  - `Preview_FreeAccount_Allowed`.
- [ ] **Step 2:** Run → Expected: FAIL.
- [ ] **Step 3:** Cài đặt. Kiểm tra quyền lợi trước mọi thay đổi dữ liệu.
- [ ] **Step 4:** Run `dotnet test tests/StoryPlatform.UnitTests --filter FullyQualifiedName~ChildRoadmaps`, rồi chạy cả integration suite. Test integration nào sao chép bộ truyện mẫu bằng tài khoản không có gói thì gọi `GivePaidPlanAsync` (Task 5). Expected: PASS.

---

### Task 9: Giới hạn trợ lý viết mỗi ngày

**Việc này làm gì (nói dễ hiểu):**
Trợ lý viết AI không tốn lượt nhưng giới hạn số lần mỗi ngày (giờ Việt Nam): Miễn phí và Gói Đọc 5, Gói Sáng tạo 20, Creator 20. Hết số lần thì hệ thống từ chối; mai dùng tiếp.

**Cần thêm / còn thiếu:**
- Đã có:
  - `StoryAssistanceService` (dùng chung cho Parent và Creator);
  - giới hạn mỗi phút `ASSISTANCE_RATE_LIMIT` 429 nằm ngay trước chỗ tạo `StoryAssistanceTask`, trong khoá theo user;
  - xử lý gửi lại cùng `CreateOperationId` trả kết quả cũ trước bước kiểm tra.
- Cần thêm: kiểm tra số task tạo trong ngày ngay sau kiểm tra mỗi phút.
- Còn thiếu / ngoài phạm vi: Creator đối tác 40 (chưa có hạng đối tác).

**Files:**
- Modify:
  - `Features/CreatorAssistance/Services/StoryAssistanceService.cs`: primary constructor thêm tham số cuối tùy chọn `IPlanBenefitService? planBenefits = null, IPlanUsageService? planUsage = null`.
  - `Features/CreatorAssistance/Services/CreatorAssistanceService.cs`
  - `Features/ParentAuthoring/ParentAssistanceService.cs`
- Test:
  - `tests/StoryPlatform.UnitTests/Features/Assistance/AssistanceDailyLimitTests.cs`
  - hoặc file test assistance hiện có, nếu đã có harness phù hợp; tìm bằng `Grep "ASSISTANCE_RATE_LIMIT" tests`.

- [ ] **Step 1: Test (failing)**
  - `Create_ParentFreeSixthTaskToday_Throws429DailyLimit`: code `ASSISTANCE_DAILY_LIMIT`, status 429.
  - `Create_CreatorTwentyFirstTaskToday_Throws429`.
  - `Create_ReplaySameOperationId_NotCounted`.
  - `Create_TasksFromYesterdayVietnam_NotCounted`.
- [ ] **Step 2:** Run → Expected: FAIL.
- [ ] **Step 3:** Cài đặt. Đếm qua `IPlanUsageService.CountWritingAssistsTodayAsync` (Task 7).
- [ ] **Step 4:** Run `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~Assistance"`. Expected: PASS.

---

### Task 10: Hiển thị quyền lợi và mức đã dùng; kiểm thử end-to-end; tài liệu

**Việc này làm gì (nói dễ hiểu):**
- Phụ huynh mở trang "lượt của tôi" sẽ thấy đang ở mức nào, quyền lợi gì, tuần này đã mở bao nhiêu truyện thư viện mới và hôm nay đã dùng trợ lý viết bao nhiêu lần.
- Chạy kịch bản đầy đủ qua HTTP, cập nhật hướng dẫn API, chạy toàn bộ test.

**Cần thêm / còn thiếu:**
- Đã có: `GET api/v1/TokenQuota/me` trả `MyQuotaDto`; tài liệu `docs/PARENT_PLANS_PAYMENT_API_GUIDE.md` mục 1–9.
- Cần thêm:
  - `MyQuotaDto` có `Benefits`, `NewLibraryStoriesThisWeek`, `WritingAssistsToday`;
  - các test integration;
  - mục 10 trong tài liệu và cập nhật mục 8.7 / 9.
- Còn thiếu / ngoài phạm vi: frontend.

**Files:**
- Modify:
  - `Features/TokenQuota/DTOs/*` (nơi khai báo `MyQuotaDto`)
  - `TokenQuotaService.cs` (`GetMyQuotaAsync`, `GetQuotaForUserAsync`)
  - `docs/PARENT_PLANS_PAYMENT_API_GUIDE.md`
- Create: `tests/StoryPlatform.IntegrationTests/Api/PlanBenefitsApiTests.cs`

**Interfaces:**
- Consumes: `IPlanBenefitService`, `IPlanUsageService`.
- Produces: `MyQuotaDto.Benefits : PlanBenefitsDto`, `int NewLibraryStoriesThisWeek`, `int WritingAssistsToday`.

- [ ] **Step 1: Test (failing)**
  - Unit `GetMyQuota_IncludesBenefitsAndUsage`.
  - Integration:
    - `FreeParent_SecondProfile_Returns409`.
    - `FreeParent_FourthNewLibraryStory_Returns403_ReopenAllowed`.
    - `FreeParent_CopyTemplate_Returns403_PreviewOk`.
    - `ExpiredParentWithTwoProfiles_ViewOnlyProfileCannotStartSession_SecondSwitchSameWeekReturns409`: tạo 2 hồ sơ Active khi có gói, cho chu kỳ hết hạn, `GET reading-slots` thấy 1 hồ sơ `canRead = false`, PUT lần 1 → 200, PUT lần 2 → 409.
    - `AdminChangesFundRate_RevenueReflectsIt`.
    - `NonAdmin_CannotReadOrUpdateSettings_403`.
- [ ] **Step 2:** Run → Expected: FAIL.
- [ ] **Step 3:** Cài đặt phần DTO. Viết mục 10 của tài liệu với các API mới, mã lỗi và quy tắc tuần/ngày; sửa mục 8.7 và 9 cho đúng những gì vẫn chưa làm (đọc to, tiến độ 7 ngày, giới hạn sinh lại, Creator đối tác). `git add -f` vì `/docs/*` bị ignore.
- [ ] **Step 4: Verify toàn bộ**
  - Run: `dotnet build` → Expected: 0 error.
  - Run: `dotnet test tests/StoryPlatform.UnitTests` → Expected: PASS hết.
  - Run: `dotnet test tests/StoryPlatform.IntegrationTests` → Expected: PASS hết, trừ các test Postgres bị skip.
  - Run với `STORYPLATFORM_TEST_PG`: `dotnet test tests/StoryPlatform.IntegrationTests --filter "FullyQualifiedName~SubscriptionPostgres|FullyQualifiedName~PlanBenefitsPostgres"` → Expected: PASS.
  - `git status` và `git diff --stat`: chỉ có các file đã liệt kê trong plan.
