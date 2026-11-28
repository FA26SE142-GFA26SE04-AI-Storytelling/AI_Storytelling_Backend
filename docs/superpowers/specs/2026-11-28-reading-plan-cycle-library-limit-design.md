# Hạn mức truyện thư viện mới theo chu kỳ cho Gói Đọc — thiết kế

Ngày: 2026-11-28 · Trạng thái: chờ duyệt

Nguồn nghiệp vụ: `SEP490/docs/report5/package_design.md` mục 2.2, 3, 8 (bước 3).

## 1. Vì sao

Gói Đọc (49.000 đ) hiện cho mở truyện thư viện mới **không giới hạn**, giống hệt Gói Sáng tạo (99.000 đ), nên chênh lệch 50.000 đ chỉ mua được 4 lượt AI. Tài liệu thiết kế gói đề xuất Gói Đọc chỉ được **24 truyện thư viện mới mỗi chu kỳ thanh toán**, dùng chung cho cả gia đình. Gói Sáng tạo vẫn không giới hạn. Mốc 24 là giả thuyết cần đo, nên chỉ cần làm cơ chế; Admin tự bật trên production.

## 2. Hiện trạng trong code

- `SubscriptionPlan.WeeklyNewLibraryStories` (int?, null = không giới hạn): Admin sửa được, nhưng hiện luôn được đếm **theo tuần** giờ Việt Nam (`PlanUsageService.ListNewLibraryStoryIdsThisWeekAsync`, `VietnamTime.StartOfWeekUtc`). Gói Đọc và Sáng tạo trong seed đều là `NULL`.
- Mức Miễn phí: `PlanSettings.FreeWeeklyNewLibraryStories = 3` mỗi tuần.
- `PlanBenefitService.GetForUserAsync`: tìm chu kỳ `UserSubscription` đang Active có gói Reading/Creative, rồi đọc hạn mức **trực tiếp từ gói**. Tài khoản có `UserAccount.WeeklyNewLibraryStoriesOverride` thì lấy `max(hạn mức, override)`.
- `ChildReadingService.EnsureWeeklyLibraryAllowanceAsync`: khóa giao dịch theo phụ huynh → truyện đã mở thì cho qua → truyện sự kiện được miễn thì đánh dấu waiver → đếm số truyện mới trong tuần → vượt thì 403 với `WeeklyLibraryLimitMessageFormat`.
- "Truyện mới" = lần mở đầu tiên của mỗi truyện Creator trên mọi hồ sơ trẻ của phụ huynh, bỏ qua phiên đọc từ gói truyện gia đình (`FamilyConfirmationId != null`) và phiên có waiver của sự kiện (`PlanUsageService.FirstOpenedLibraryStoriesAsync`).
- Mỗi chu kỳ thanh toán là một dòng `UserSubscription` có `StartAt`, `EndAt` (= `StartAt.AddMonths(1)`), `PaymentId`. Gia hạn sớm / hạ gói tạo dòng `Scheduled` nối tiếp. Nâng Gói Đọc lên Sáng tạo: dòng Đọc thành `Superseded`, dòng Sáng tạo bắt đầu ngay (`SubscriptionService.ApplyMonthlyPlanAsync`).
- `PaymentTransaction.AiCredits` / `SubmitCredits` là số đã chốt lúc tạo giao dịch ("Sửa gói chỉ áp dụng cho giao dịch tạo sau đó").
- `GET /TokenQuota/me` (`TokenQuotaService.BuildQuotaAsync`) trả `Benefits` và `NewLibraryStoriesThisWeek`. Frontend `PaymentSection.tsx` chưa hiển thị hai trường này; type `MyQuotaDto` của FE cũng chưa có. Repo Mobile trống.

**Rủi ro nếu chỉ đặt Gói Đọc = 24:** mọi gia đình đang ở giữa chu kỳ bị giới hạn ngay, và giới hạn lại tính theo tuần. Vi phạm quy tắc "giữ quyền lợi của chu kỳ đã mua".

## 3. Mục tiêu, không làm, phản mục tiêu

**Mục tiêu**
- Với gói chu kỳ (Reading/Creative), `WeeklyNewLibraryStories` của gói được hiểu là **số truyện thư viện mới mỗi chu kỳ thanh toán**. Mức Miễn phí giữ theo tuần.
- Hạn mức được **chốt trên giao dịch** lúc tạo giao dịch; chu kỳ đã mua không đổi khi Admin sửa gói.
- Phụ huynh thấy đã dùng bao nhiêu, hạn mức, và ngày làm mới.
- Production không đổi hành vi cho tới khi Admin đặt hạn mức cho Gói Đọc.

**Không làm**
- Add-on đọc 5 truyện · 9.000 đ.
- Danh sách truyện đã mở trong kỳ trên giao diện (chỉ hiển thị số đếm).
- Quy đổi ngày còn lại khi nâng gói giữa kỳ.
- Đổi tên cột / trường `WeeklyNewLibraryStories` (giữ tên cũ, ghi chú nghĩa mới).
- Data migration đặt 24 cho production.

**Phản mục tiêu** (tính là hỏng dù chạy được)
- Gia đình đang ở giữa một chu kỳ đã mua bị chặn sau khi Admin đặt 24.
- Mở lại truyện đã mở, mở truyện sự kiện được miễn, hoặc đọc qua gói truyện gia đình bị tính lượt hay bị chặn.
- Hai hồ sơ trẻ mở cùng một truyện bị tính 2 lượt; hai request đồng thời vượt hạn mức.
- Mức Miễn phí hoặc Gói Sáng tạo đổi hành vi.

## 4. Phương án

| | Phương án | Kết luận |
|---|---|---|
| **A** | Dùng lại cột `WeeklyNewLibraryStories` của gói; với gói chu kỳ thì hiểu là "mỗi chu kỳ". API thêm `period`. Chốt hạn mức trên `PaymentTransaction`. | **Chọn** (người dùng chọn 2026-11-28). Ít thay đổi, Admin sửa đúng ô cũ. |
| B | Thêm cột mới `NewLibraryStoriesPerCycle` trên gói. | Loại: gói trả phí có hai ô hạn mức, phải quy định ô nào thắng; form Admin thêm ô. |

## 5. Quy tắc

1. **Hạn mức của chu kỳ** = `PaymentTransaction.NewLibraryStoriesLimit` của giao dịch sinh ra chu kỳ đang Active (`UserSubscription.PaymentId`). `null` = không giới hạn. Không đọc lại giá trị hiện tại của gói.
2. **Chốt lúc tạo giao dịch:** khi tạo giao dịch cho gói Reading hoặc Creative, `NewLibraryStoriesLimit = plan.WeeklyNewLibraryStories`. Gói AddOn / CreatorPack: `null`. Giao dịch cũ (trước migration) có `null` → chu kỳ của chúng vẫn không giới hạn.
3. **Khung đếm:** gói chu kỳ đếm lần mở đầu tiên có `StartedAt` trong `[cycle.StartAt, cycle.EndAt)`. Mức Miễn phí (và Creator) đếm trong tuần giờ Việt Nam như cũ.
4. **Truyện mới** giữ định nghĩa hiện tại (mục 2): lần mở đầu tiên **từ trước tới nay** của gia đình. Truyện đã mở ở chu kỳ trước hoặc khi còn Miễn phí thì không tính lại và luôn đọc lại được. Waiver sự kiện và gói truyện gia đình không tính.
5. **Override tài khoản:** giữ `max(hạn mức, WeeklyNewLibraryStoriesOverride)`, áp dụng cho kỳ đang dùng (tuần hoặc chu kỳ).
6. **Hết lượt** (403, giữ `ForbiddenException`):
   - Tuần (giữ nguyên): `"Đã mở đủ {0} truyện thư viện mới trong tuần. Truyện mới mở lại vào thứ Hai."`
   - Chu kỳ (mới): `"Đã mở đủ {0} truyện thư viện mới trong chu kỳ. Truyện mới mở lại từ ngày {1:dd/MM/yyyy} nếu gia hạn gói."`, `{1}` = `cycle.EndAt` theo ngày giờ Việt Nam (`VietnamTime.LocalDate`).
7. Nâng Đọc → Sáng tạo giữa kỳ: chu kỳ Sáng tạo bắt đầu ngay; nếu giá trị đã chốt là `null` thì không giới hạn ngay. Hạ Sáng tạo → Đọc / gia hạn: chu kỳ mới bắt đầu thì đếm lại từ 0.

## 6. Thiết kế Backend (repo `AI_Storytelling_Backend`)

1. **Domain + migration**
   - `PaymentTransaction`: thêm `public int? NewLibraryStoriesLimit { get; set; }`, comment XML: hạn mức truyện thư viện mới mỗi chu kỳ, chốt từ `SubscriptionPlan.WeeklyNewLibraryStories` lúc tạo giao dịch; null = không giới hạn.
   - Migration EF `AddNewLibraryStoriesLimitToPaymentTransactions`: thêm cột nullable `int`, không backfill. Theo đúng cách các migration hiện có trong `src/Core/StoryPlatform.Infrastructure/Migrations` (kèm Designer và cập nhật `ApplicationDbContextModelSnapshot`).
2. **Chốt hạn mức:** `PaymentService` ở mọi chỗ tạo `PaymentTransaction` mới đang đặt `AiCredits = plan.AiCredits` (khoảng dòng 81, 160, 533): thêm `NewLibraryStoriesLimit = plan.PlanType is PlanType.Reading or PlanType.Creative ? plan.WeeklyNewLibraryStories : null`. Không đụng các luồng khác.
3. **`PlanBenefitsDto`** (thêm trường, không xóa trường cũ):
   - `WeeklyNewLibraryStories`: giữ tên; sửa comment: hạn mức truyện thư viện mới của **kỳ hiện tại** (tuần với Miễn phí/Creator, chu kỳ với gói trả phí); null = không giới hạn.
   - `NewLibraryStoriesPeriod` (`string`): hằng `"Week"` hoặc `"Cycle"`.
   - `NewLibraryStoriesPeriodStart`, `NewLibraryStoriesResetsAt` (`DateTime`, UTC): tuần = `StartOfWeekUtc(now)` / `StartOfNextWeekUtc(now)`; chu kỳ = `cycle.StartAt` / `cycle.EndAt`.
4. **`PlanBenefitService.GetForUserAsync`:** khi có chu kỳ Reading/Creative đang Active, lấy hạn mức từ `cycle.Payment.NewLibraryStoriesLimit` (include `Payment`, hoặc đọc giao dịch theo `PaymentId`) thay vì `plan.WeeklyNewLibraryStories`; đặt period = Cycle và hai mốc thời gian. Nhánh Free và Creator: period = Week. Override giữ nguyên quy tắc 5.
5. **`PlanUsageService` / `IPlanUsageService`:** thêm `CountNewLibraryStoriesInPeriodAsync(ownerUserId, periodStart, periodEnd, ct)` (và bản `List…` nếu cần), lọc `StartedAt >= periodStart && StartedAt < periodEnd`, giữ nguyên lọc waiver. Hai hàm `…ThisWeekAsync` gọi lại hàm mới với khung tuần; giữ chữ ký cũ.
6. **`ChildReadingService.EnsureWeeklyLibraryAllowanceAsync`:** lấy benefits một lần, đếm bằng `CountNewLibraryStoriesInPeriodAsync(owner, benefits.NewLibraryStoriesPeriodStart, benefits.NewLibraryStoriesResetsAt)`, chọn thông báo theo period (quy tắc 6). Thêm hằng `CycleLibraryLimitMessageFormat`. Giữ nguyên khóa giao dịch, thứ tự kiểm tra (đã mở → miễn sự kiện → đếm).
7. **`TokenQuotaService.BuildQuotaAsync` / `MyQuotaDto`:** thêm `NewLibraryStoriesUsed` (int) = số đã dùng trong kỳ hiện tại theo `Benefits`. Giữ `NewLibraryStoriesThisWeek` như cũ để không vỡ client.
8. **Seed:** `Database/Seed/37_subscription_plans.sql`, Gói Đọc (Id 2): `WeeklyNewLibraryStories` `NULL` → `24`. Chỉ ảnh hưởng DB seed mới (`ON CONFLICT DO NOTHING`). Không viết data migration.
9. **Tài liệu:** `docs/PARENT_PLANS_PAYMENT_API_GUIDE.md`: phần quyền lợi gói / hạn mức thư viện, response `GET /TokenQuota/me`, response lỗi 403 khi hết lượt, và ghi chú cho Admin (`weeklyNewLibraryStories` của gói chu kỳ là theo chu kỳ, chỉ áp dụng cho giao dịch tạo sau khi sửa).

## 7. Thiết kế Frontend (repo `AI_Storytelling_Frontend`, code ở `src/`)

1. `app/types/backendContracts.ts`: `MyQuotaDto` thêm `benefits` (type quyền lợi với `weeklyNewLibraryStories: number | null`, `newLibraryStoriesPeriod: 'Week' | 'Cycle'`, `newLibraryStoriesPeriodStart: string`, `newLibraryStoriesResetsAt: string`; dùng lại type sẵn có nếu đã có) và `newLibraryStoriesUsed: number`. Đánh dấu optional nếu cần để tương thích Backend cũ.
2. `app/features/account/PaymentSection.tsx`: thêm một dòng trong khu gói hiện tại:
   - Có hạn mức: `Truyện thư viện mới: {used}/{limit} {trong chu kỳ | trong tuần} · làm mới ngày {dd/MM/yyyy}`.
   - `null`: `Truyện thư viện mới: không giới hạn`.
   - `used >= limit`: thêm dòng nhắc `Đã hết lượt mở truyện mới. Bạn vẫn đọc lại được truyện đã mở.`; với Gói Đọc thêm gợi ý nâng lên Gói Sáng tạo bằng đúng nút/luồng nâng gói đã có trong màn hình (không tạo luồng mới).
   - Dùng hàm định dạng ngày đang có trong repo.
3. `app/features/staff/CreatePlanDialog.tsx` (và form sửa gói nếu dùng chung nhãn): nhãn ô hạn mức thư viện của gói thành `Truyện thư viện mới mỗi chu kỳ`, ghi chú ngắn "Áp dụng cho giao dịch tạo sau khi lưu".
4. Không đổi luồng thanh toán, không thêm route.
5. **Form sửa gói của Admin** (bổ sung 2026-11-28; thiếu ở bản đầu, cần để làm bước 2 của mục 9). Form sửa trong `app/features/staff/AdminSystem.tsx` (`AdminPlans`) chưa có ô hạn mức thư viện. Backend đã sẵn: danh sách gói Admin trả `benefits`, `PUT /Payment/admin/plans/{id}` nhận `benefits` (bỏ trống = giữ nguyên) và **ghi đè cả 5 quyền lợi**. Không sửa Backend.
   - Type: `AdminSubscriptionPlanDto` thêm `benefits?: PlanBenefitsInput | null`; `UpdateSubscriptionPlanRequestDto` thêm `benefits?: PlanBenefitsInput` (dùng `PlanBenefitsInput` có sẵn trong `app/types/planSettings.ts`).
   - Với gói Reading/Creative có `benefits`: thêm ô `Truyện thư viện mới mỗi chu kỳ` (số nguyên ≥ 0) kèm ô tích `Không giới hạn` (giống form tạo gói), điền sẵn từ `benefits.weeklyNewLibraryStories`. Gói khác hoặc không có `benefits`: không hiện ô.
   - Nút Lưu tính cả thay đổi của ô này; giá trị không hợp lệ thì khóa nút Lưu.
   - Khi lưu: chỉ khi giá trị ô này đổi mới gửi `benefits = { ...edit.benefits, weeklyNewLibraryStories: <giá trị mới> }`; 4 quyền lợi khác giữ nguyên đúng giá trị đang có. Không đổi thì không gửi `benefits`.
   - Giữ nguyên dòng nhắc sẵn có "Thay đổi chỉ áp cho giao dịch tạo sau khi lưu…".

## 8. Test

**Backend** (`tests/StoryPlatform.UnitTests`, dùng file sẵn có: `Features/PlanBenefits/PlanBenefitServiceTests.cs`, `PlanUsageServiceTests.cs`, `Features/ReadingSessions/ChildReadingLibraryLimitTests.cs`, test của `PaymentService` / `SubscriptionService` / `TokenQuotaService` đang có). Viết test trước, chạy thấy fail, rồi sửa code.

| Nhóm | Tình huống | Kỳ vọng |
|---|---|---|
| Chốt | Tạo giao dịch Gói Đọc khi gói = 24 | `NewLibraryStoriesLimit == 24`; AddOn/CreatorPack `null` |
| Chốt | Chu kỳ từ giao dịch cũ `null`, gói hiện = 24 | Không giới hạn |
| Chốt | Mua lúc gói = 24, sau đó Admin sửa gói = 30 | Chu kỳ vẫn 24 |
| Benefits | Chu kỳ Đọc đang Active | `Period = "Cycle"`, start/resets = `StartAt`/`EndAt` |
| Benefits | Miễn phí | `Period = "Week"`, mốc tuần giờ Việt Nam; hành vi cũ giữ nguyên |
| Benefits | Override = 40, chu kỳ 24 | Hạn mức 40 |
| Đếm | Lần mở đầu trước `cycle.StartAt` (lúc Miễn phí / chu kỳ trước) | Không tính vào chu kỳ |
| Mở truyện | 24 truyện mới trong chu kỳ | Lượt 24 qua; truyện mới thứ 25 → 403 với thông báo chu kỳ và ngày `EndAt` (giờ Việt Nam) |
| Mở truyện | Đã hết lượt, mở lại truyện đã mở | Cho phép |
| Mở truyện | Hai hồ sơ trẻ mở cùng truyện | Tính 1 lượt |
| Mở truyện | Truyện sự kiện được miễn / phiên gói truyện gia đình khi đã hết lượt | Cho phép, không tính |
| Chu kỳ | Gia hạn: chu kỳ mới bắt đầu | Đếm lại từ 0 |
| Chu kỳ | Nâng Đọc → Sáng tạo khi đã hết 24 | Mở được ngay |
| Sáng tạo | Gói Sáng tạo | Không giới hạn như cũ |
| Quota | `GET /TokenQuota/me` của gia đình đã mở 5 truyện trong chu kỳ | `NewLibraryStoriesUsed == 5`, `Benefits` có limit/period/resetsAt |

Chạy:
```
dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~PlanBenefit|FullyQualifiedName~PlanUsage|FullyQualifiedName~ChildReading|FullyQualifiedName~Payment|FullyQualifiedName~Subscription|FullyQualifiedName~TokenQuota"
dotnet test tests/StoryPlatform.UnitTests
dotnet build StoryPlatform.sln
```
**Quyết định trong lúc triển khai (2026-11-28).** `PlanBenefitServiceTests.GetForUser_ReadingOverride_PreservesMoreGenerousPaidLimits(planLimit)` đặt hạn mức trên gói nhưng chu kỳ không có giá trị chốt trên giao dịch, nên theo quy tắc 1 kết quả đúng là không giới hạn. Sửa **dữ liệu**: chu kỳ được tạo kèm giao dịch có `NewLibraryStoriesLimit = planLimit` (dùng helper `GivenReadingCycle`). Giữ nguyên `Assert.Equal(planLimit, …)` và override 99.

Integration test có liên quan tới payment/subscription/quota (nếu chạy được trên máy) và kiểm tra migration áp dụng được trên DB test. Test cũ không được sửa kỳ vọng; nếu hỏng thì dừng và báo.

**Frontend** (trong `src/`): thêm/sửa unit test cho `PaymentSection` (có hạn mức, không giới hạn, đã hết lượt, chu kỳ và tuần). Chạy `npx vitest run tests`, `npx eslint` cho file đã sửa, `npx tsc --noEmit -p . --incremental false`.

## 9. Bật trên production (Admin, sau khi deploy)

1. Deploy Backend (migration thêm cột), rồi Frontend.
2. Admin mở cấu hình gói, đặt Gói Đọc `Truyện thư viện mới mỗi chu kỳ = 24`.
3. Chỉ giao dịch tạo sau thời điểm này nhận hạn mức 24. Gia đình đang ở giữa chu kỳ và chu kỳ đã trả trước vẫn không giới hạn tới hết chu kỳ đó.
4. Theo dõi tỷ lệ chạm hạn mức, nâng gói và ngừng gia hạn (package_design mục 8 bước 5).

## 10. Ràng buộc triển khai

- Không commit, push, branch, worktree; chủ repo tự commit.
- Không tạo file ngoài: migration (2 file) và test mới nếu cần file riêng; ưu tiên thêm vào file test sẵn có.
- Không đổi tên trường/cột hiện có, không xóa trường API.
- Không đọc/in `appsettings.*.json`, `.env`, connection string.
- Báo cáo theo mục 11 của `CLAUDE.md`.
