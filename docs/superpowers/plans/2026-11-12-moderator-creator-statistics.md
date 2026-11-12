# Kế hoạch F-19 (Thống kê hàng đợi & SLA cho Moderator) và F-22 (Thống kê nội dung cho Creator)

Nguồn: Tài liệu 6 (SEP420_R2_BM04_Commitment) — WF-05, dòng chức năng 19 và 22; quyết định thiết kế “Read-only statistics over existing tables; Creator ID từ JWT; kỳ [from, toExclusive)”.
Test cam kết: TC-WF05-F19-01..03, TC-WF05-F22-01..03, demo DS-05c / DS-05d.

**Nguyên tắc chung**
- Chỉ đọc trên bảng có sẵn, **không thêm bảng / migration**.
- Kỳ dùng `ReportPeriod.Resolve` như F-15/F-20/F-21 để các màn thống kê khớp nhau.
- Chỉ trả số tổng hợp: không nội dung truyện, không tên/ID trẻ, không feedback thô.
- Sai vai trò → 403 qua `[Authorize(Roles=…)]`; Creator ID chỉ lấy từ token (`GetCurrentUserId()`).

---

## Task 1 — F-19: Thống kê hàng đợi & SLA cho Moderator

**Việc này làm gì (nói dễ hiểu):**
Moderator chọn một kỳ và thấy ngay hàng đợi đang thế nào: bao nhiêu mục đang chờ, đang được nhận, đã quá hạn SLA. Đồng thời thấy chính mình đã quyết định bao nhiêu mục (duyệt / từ chối / yêu cầu sửa) theo từng loại, bao nhiêu lượt gửi lại và bao nhiêu mục thuộc vùng xám. Moderator không xem được thống kê doanh thu hay chi phí AI của Admin.

**Cần thêm / còn thiếu:**
- Đã có:
  - Bảng `ModerationItem` có đủ trường: `Status`, `ItemType`, `SlaDueAt`, `IsResubmission`, `DecidedByUserId`, `DecidedAt`, `CreatedAt`.
  - `ModerationQueueController` (`api/v1/ModerationQueue`), `ReportPeriod.Resolve`.
  - Endpoint Admin (`BusinessReportController`, `PaymentController`) đã `[Authorize(Roles = "Administrator")]` → Moderator gọi sẽ 403 sẵn.
- Cần thêm:
  - `GET api/v1/ModerationQueue/statistics?periodStart=&periodEnd=` chỉ cho role `Moderator`.
  - Chỉ số hàng đợi (toàn hàng đợi, tại thời điểm gọi): `Pending`, `Claimed`, `Overdue` (Status ∈ {Pending, Claimed} và `SlaDueAt < now`), kèm `AsOfUtc`; tách theo `ItemType`.
  - Chỉ số quyết định (chỉ mục có `DecidedByUserId == mình`, `DecidedAt` trong kỳ): đếm theo kết quả `Approved / Rejected / ChangesRequested` × `ItemType`.
  - `ResubmissionCount`: mục `IsResubmission = true` tạo trong kỳ.
  - `GrayZoneCount`: mục `ItemType = GrayZoneStory` tạo trong kỳ.
- Còn thiếu / ngoài phạm vi:
  - Thời gian xử lý trung bình / tỉ lệ đúng SLA chưa có trong spec → không làm.
  - Admin có được gọi endpoint này không? Spec chỉ nói Moderator → mặc định chỉ Moderator (hỏi lại người dùng).

**Files**
- MODIFY `src/Core/StoryPlatform.Api/Controllers/ModerationQueueController.cs` — thêm action `GetStatistics` với `[Authorize(Roles = "Moderator")]`.
- MODIFY `src/Core/StoryPlatform.Application/Features/Moderation/DTOs/ModerationQueueDtos.cs` — thêm `ModerationStatisticsDto`, `ModerationQueueCountDto`, `ModerationDecisionCountDto`.
- CREATE `src/Core/StoryPlatform.Application/Features/Moderation/Interfaces/IModerationStatisticsService.cs`
- CREATE `src/Core/StoryPlatform.Application/Features/Moderation/Services/ModerationStatisticsService.cs` (tách riêng vì `ModerationQueueService` đã ~270 dòng và lo nghiệp vụ nhận/mở mục).
- MODIFY `src/Core/StoryPlatform.Application/DependencyInjection.cs` — đăng ký service.
- CREATE `tests/StoryPlatform.UnitTests/Features/Moderation/ModerationStatisticsServiceTests.cs`
- MODIFY/CREATE test tích hợp quyền trong `tests/StoryPlatform.IntegrationTests/Api/` (theo mẫu `CreatorStatisticsApiTests.cs`).

**Interfaces**
```csharp
Task<ModerationStatisticsDto> GetAsync(int moderatorUserId, DateTime periodStart, DateTime periodEnd,
    CancellationToken cancellationToken = default);

public class ModerationStatisticsDto {
    DateTime PeriodStart; DateTime PeriodEnd; DateTime AsOfUtc;
    int Pending; int Claimed; int Overdue;
    List<ModerationQueueCountDto> QueueByType;        // ItemType, Pending, Claimed, Overdue
    int MyApproved; int MyRejected; int MyChangesRequested;
    List<ModerationDecisionCountDto> MyDecisionsByType; // ItemType, Approved, Rejected, ChangesRequested
    int ResubmissionCount; int GrayZoneCount;
}
```

**Steps**
1. Viết test trước (TC-WF05-F19-01..03):
   - 01: seed mục ở nhiều trạng thái, có mục quá `SlaDueAt` → số Pending/Claimed/Overdue đúng.
   - 02: hai Moderator cùng quyết định → mỗi người chỉ thấy số của mình; mục quyết định ngoài kỳ không tính.
   - 03: Moderator gọi `BusinessReport` / `Payment` admin → 403; ContentCreator gọi `/statistics` → 403; JSON không có trường nội dung/feedback/child.
2. Viết service: một truy vấn `FindAsync` cho mục mở (Pending/Claimed), một truy vấn cho mục `DecidedByUserId == me && DecidedAt ∈ kỳ`, một truy vấn mục tạo trong kỳ (resubmission, gray-zone); `now` lấy từ clock dùng chung của dự án.
3. Thêm DTO, action controller, đăng ký DI.
4. `dotnet build`, `dotnet test` cho 2 project test.

---

## Task 2 — F-22: Thống kê nội dung cho Creator

**Việc này làm gì (nói dễ hiểu):**
Creator mở trang thống kê và thấy: mình đang có bao nhiêu truyện ở từng trạng thái (Nháp, Sẵn sàng, Chờ duyệt, Đã xuất bản), trong kỳ đã gửi duyệt bao nhiêu lần và kết quả ra sao (duyệt / từ chối / yêu cầu sửa / tự rút), còn bao nhiêu lượt AI và lượt gửi duyệt, cùng mức độ tiến tới hạng “Creator đối tác” trong 90 ngày gần nhất. Chỉ thấy số của chính mình.

**Cần thêm / còn thiếu:**
- Đã có:
  - `CreatorStatisticsController` (`api/v1/CreatorStatistics`, role `ContentCreator`) và `CreatorStatisticsService` với `GetValidReadsAsync`, `IsCurrentlyPublished`, `Rate`.
  - `ITokenQuotaService.GetCreatorQuotaAsync(creatorUserId)` trả lượt AI / lượt gửi còn lại.
  - `ModerationItem` loại `PublicStoryReview` với `CreatedByUserId`, `Status`, `DecidedAt`, `WithdrawnAt`.
  - `Story.ArchivedReason` để biết truyện bị gỡ vì vi phạm.
- Cần thêm:
  - `GET api/v1/CreatorStatistics/content?periodStart=&periodEnd=`.
  - Đếm truyện theo trạng thái (truyện `Scope = Creator`, `AuthorUserId = me`, chưa archive):
    - Published: `IsCurrentlyPublished`.
    - Chờ duyệt: có `ModerationItem PublicStoryReview` đang mở (Pending/Claimed/Escalated).
    - Ready: `Status = Ready`, chưa published, không có mục đang mở.
    - Draft: còn lại.
  - Lượt gửi trong kỳ theo kết quả: Approved / Rejected / ChangesRequested (theo `DecidedAt`), Withdrawn (theo `WithdrawnAt`).
  - Lượt còn lại: gọi `GetCreatorQuotaAsync`.
  - Tiến độ đối tác 90 ngày: số truyện Published (≥10), tỉ lệ hoàn thành (≥60%), lượt đọc hợp lệ (≥200), không có truyện bị gỡ vì vi phạm; trả cả giá trị hiện tại, ngưỡng và `Met` từng tiêu chí.
  - Truyền `storyId`/`creatorId` từ ngoài không có tác dụng — chỉ dùng token.
- Còn thiếu / ngoài phạm vi:
  - **Hạng “Creator đối tác” chưa tồn tại**: `PlanBenefitService` luôn trả `Tier = "Creator"`, `PartnerMonthly*Credits` có trong `PlanSettings` nhưng chưa nơi nào cấp. Kế hoạch này chỉ **tính và hiển thị tiến độ**, `Tier` vẫn lấy như hiện tại; việc nâng hạng và cấp hạn mức đối tác thuộc F-17 / một task riêng (hỏi người dùng).
  - Tiêu chí 90 ngày dùng cửa sổ trượt `[hôm nay − 90 ngày, hôm nay]`, độc lập với kỳ người dùng chọn.

**Files**
- MODIFY `src/Core/StoryPlatform.Api/Controllers/CreatorStatisticsController.cs` — thêm action `GetContent`.
- MODIFY `src/Core/StoryPlatform.Application/Features/CreatorStatistics/DTOs/CreatorStatisticsDtos.cs` — thêm `CreatorContentStatisticsDto`, `PartnerProgressDto`, `PartnerCriterionDto`.
- MODIFY `src/Core/StoryPlatform.Application/Features/CreatorStatistics/Interfaces/ICreatorStatisticsService.cs` — thêm `GetContentAsync`.
- MODIFY `src/Core/StoryPlatform.Application/Features/CreatorStatistics/Services/CreatorStatisticsService.cs` — thêm `GetContentAsync`, inject `ITokenQuotaService` (kiểm tra không tạo vòng phụ thuộc DI).
- MODIFY `tests/StoryPlatform.UnitTests/Features/CreatorStatistics/CreatorStatisticsServiceTests.cs`
- MODIFY `tests/StoryPlatform.IntegrationTests/Api/CreatorStatisticsApiTests.cs`

**Interfaces**
```csharp
Task<CreatorContentStatisticsDto> GetContentAsync(int creatorUserId, DateTime periodStart, DateTime periodEnd,
    CancellationToken cancellationToken = default);

public class CreatorContentStatisticsDto {
    DateTime PeriodStart; DateTime PeriodEnd;
    int Draft; int Ready; int PendingReview; int Published;
    int SubmissionsApproved; int SubmissionsRejected; int SubmissionsChangesRequested; int SubmissionsWithdrawn;
    int RemainingAiCredits; int RemainingSubmitCredits;
    string Tier;                       // hiện luôn "Creator"
    PartnerProgressDto PartnerProgress; // WindowStart, WindowEnd, IsEligible, Criteria[]
}
public class PartnerCriterionDto { string Key; decimal Current; decimal Target; bool Met; }
```

**Steps**
1. Viết test trước (TC-WF05-F22-01..03):
   - 01: Creator có truyện ở 4 trạng thái + mục gửi duyệt nhiều kết quả → số đếm đúng, chỉ của mình.
   - 02: tiến độ đối tác: seed đủ/thiếu từng tiêu chí, có truyện gỡ vì vi phạm → `Met`/`IsEligible` đúng; lượt còn lại khớp `GetCreatorQuotaAsync`.
   - 03: Creator B gọi với query `creatorId`/`storyId` của A → không thấy số của A (truyện của A → 403); Moderator/User gọi → 403; JSON không có dữ liệu trẻ.
2. Cài `GetContentAsync`, tái dùng `GetCreatorStoriesAsync`, `GetValidReadsAsync` (cửa sổ 90 ngày), `Rate`, `IsCurrentlyPublished`.
3. Thêm DTO, action controller.
4. `dotnet build`, `dotnet test`.

---

## Task 3 — Kiểm thử E2E, chuyển trạng thái và tài liệu

**Việc này làm gì (nói dễ hiểu):**
Chạy lại kịch bản demo DS-05c/DS-05d trên dữ liệu seed: Moderator xem thống kê hàng đợi, Creator xem thống kê của mình, sai vai trò đều bị 403 và không lộ dữ liệu. Cập nhật tài liệu API cho Frontend nối vào.

**Cần thêm / còn thiếu:**
- Đã có: seed trong `Database/Seed`, mẫu test tích hợp `CreatorStatisticsApiTests`, `RevenueStatisticsApiTests`.
- Cần thêm:
  - 1 test E2E WF-05 (Moderator stats → Creator content stats → gọi chéo 403).
  - Seed thêm (nếu thiếu) mục kiểm duyệt quá SLA và mục gửi lại để demo.
  - Ghi chú endpoint mới vào `docs/API_FRONTEND_ACTOR_FLOWS.md`.
- Còn thiếu / ngoài phạm vi:
  - 2 test chuyển trạng thái của WF-05 (Reconciliation Open → Resolved) thuộc F-18, không nằm trong kế hoạch này.
  - Màn hình Frontend (trang thống kê Moderator / Creator) làm sau khi API xong.

**Files**
- MODIFY `tests/StoryPlatform.IntegrationTests/Api/…` (E2E WF-05)
- MODIFY `Database/Seed/…` (chỉ khi thiếu dữ liệu demo)
- MODIFY `docs/API_FRONTEND_ACTOR_FLOWS.md`

**Steps**
1. Viết test E2E, chạy `dotnet test`.
2. Rà diff, xác nhận không có migration mới.

---

## Câu hỏi cần chốt trước khi làm
1. Kỳ thống kê: tài liệu 6 ghi UTC+7, còn `ReportPeriod` hiện tính theo UTC (F-15/20/21 đang dùng). Giữ UTC để đồng bộ hay đổi chung sang UTC+7?
2. F-19: chỉ Moderator, hay cho cả Administrator?
3. F-22 hạng đối tác: chỉ hiển thị tiến độ (đề xuất), hay làm luôn việc nâng hạng + cấp hạn mức đối tác?
