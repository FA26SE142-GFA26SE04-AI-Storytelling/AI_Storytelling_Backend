# Giới hạn lượt đọc hợp lệ theo gia đình — thiết kế

Ngày: 2026-11-28 · Trạng thái: đã triển khai (Codex), đã review; chờ chủ repo commit

## 1. Vì sao cần sửa

Quỹ Creator chia theo công thức `lượt đọc hợp lệ của Creator × quỹ ÷ tổng lượt đọc hợp lệ`. Hiện có hai lỗ hổng cho phép gian lận có lời:

1. **Không giới hạn theo gia đình.** Mỗi trẻ được tính tối đa 10 lượt/ngày, nhưng một tài khoản phụ huynh có 4 hồ sơ trẻ tạo được 4 × 10 × 30 = 1.200 lượt/tháng, trong khi chỉ góp tối đa 14.700 đ (Gói Đọc) hoặc 19.800 đ (Gói Sáng tạo) vào quỹ. Hệ thống chỉ loại con của chính tài khoản tác giả (`ownerUserId == story.AuthorUserId`). Creator dùng một tài khoản phụ huynh khác, trả 49.000 đ và thu về khoảng 400.000–460.000 đ mỗi tháng ở mọi quy mô (xem `docs/report5/package_design.md` của repo cha, mục 7.3).
2. **Thời gian đọc do client khai.** `ReadingSession.TimeSpentSeconds` được cộng từ `AddedSeconds` mà ứng dụng gửi lên mỗi lần lưu tiến độ (`ChildReadingService`, có chặn mức tối đa mỗi lần lưu). Script gửi đủ 60 giây được mà không cần thời gian thật trôi qua.

## 2. Mục tiêu và phạm vi

**Mục tiêu**

- Gian lận lượt đọc không còn có lời. Với giá trị khoảng 383 đ/lượt, 60 lượt ≈ 23.000 đ, thấp hơn giá Gói Đọc 49.000 đ.
- Creator trung thực không bị pha loãng thu nhập.
- Không đổi API, DTO hay schema. Không cần migration.

**Không làm (để sau)**

- Chia quỹ theo từng gia đình (user-centric).
- Phát hiện tài khoản phụ qua số điện thoại hoặc tài khoản ngân hàng nhận tiền.
- Cho Admin cấu hình con số 60 (cần thêm cột vào `PlanSettings` và migration).
- Thời gian đọc tối thiểu tính theo độ dài truyện.

**Phản mục tiêu**: các điều sau đây là thất bại dù code chạy được.

- Gia đình bị tính lại 60 lượt cho *mỗi lần chốt kỳ* khi Admin chốt giữa tháng (`CloseNowAsync`).
- Số liệu ở dashboard Creator, thống kê Admin và số tiền khi chốt kỳ lệch nhau.

## 3. Các phương án đã cân nhắc

| Phương án | Ưu | Nhược | Kết luận |
|---|---|---|---|
| A. Chia quỹ theo từng gia đình | Gian lận luôn lỗ, hết tình trạng người đọc nhiều hút tiền | Phải tính phần đóng góp theo từng người trả và phân bổ doanh thu theo ngày chu kỳ (quỹ hiện tính theo ngày thanh toán) | Để sau |
| B1. Giới hạn 60 lượt/gia đình/tháng, **quy đổi theo tỷ lệ** | Công bằng giữa các truyện trong cùng gia đình | Một tháng có thể bị chốt nhiều lần, mỗi lần chỉ thấy phần lượt đọc của nó, nên bị lách được. Lượt đọc trở thành số thập phân, phải đổi DTO | Loại |
| **B2. Giới hạn 60 lượt hợp lệ *đầu tiên* của gia đình trong tháng** | Không bị lách khi chốt nhiều lần, giữ số nguyên, không đổi API | Lượt đọc cuối tháng của gia đình đọc rất nhiều không được tính. Chỉ ảnh hưởng trong gia đình đó | **Chọn** |

## 4. Quy tắc mới

Thêm vào định nghĩa lượt đọc hợp lệ (bổ sung, không thay các quy tắc cũ):

- **Quy tắc 8. Giới hạn gia đình.** Mỗi tài khoản phụ huynh (`ChildProfile.OwnerUserId`) được tính tối đa **60 lượt đọc hợp lệ mỗi tháng dương lịch theo giờ Việt Nam**, gộp mọi hồ sơ trẻ và mọi truyện. Giữ 60 lượt sớm nhất theo `CompletedAt`, hòa thì xếp theo `StoryId` rồi `ChildProfileId`. Giới hạn áp *sau* hai giới hạn theo trẻ (1 lượt/truyện/ngày, 10 lượt/ngày).
- **Quy tắc 4 (sửa). Thời gian đọc theo server.** Số giây dùng để so với `minimumReadSeconds` là `min(TimeSpentSeconds, floor((CompletedAt − StartedAt).TotalSeconds))`. Nếu `CompletedAt < StartedAt` thì coi là 0 giây.

## 5. Cách triển khai

Chỉ sửa `src/Core/StoryPlatform.Application/Features/CreatorStatistics/Services/CreatorStatisticsService.cs`. Mọi nơi tính tiền và thống kê (`GetReadingAsync`, `GetRevenueAsync`, `GetPeriodEarningsAsync` dùng khi chốt kỳ, `GetAdminOverviewAsync`, tiến độ Creator đối tác) đều đi qua `GetValidReadsAsync`, nên không cần sửa chỗ khác.

1. Thêm hằng số cạnh `MaxValidReadsPerChildPerDay`:
   ```csharp
   public const int MaxValidReadsPerFamilyPerMonth = 60;
   ```
2. Trong `GetValidReadsAsync(creatorStories, from, toExclusive, ...)`:
   - Tính `budgetStart = VietnamTime.StartOfMonthUtc(from)`, tức 00:00 giờ Việt Nam ngày 1 của tháng chứa `from`. Đọc phiên `Completed` có `CompletedAt` trong `[budgetStart, toExclusive)` thay vì `[from, toExclusive)`. Các lượt trong `[budgetStart, from)` chỉ dùng để trừ dần hạn mức gia đình, **không** được trả về.
   - Gọi `WasPublishedAt(story, completedAt, budgetStart, toExclusive)`, tức mở rộng kỳ cho phép kiểm tra gỡ truyện vì vi phạm. Với các lượt trong `[from, toExclusive)`, kết quả không đổi so với hiện tại, vì truyện bị gỡ trước `from` thì các lượt sau khi gỡ đã bị loại bởi `readAt < ArchivedAt`.
   - Kiểm tra thời gian đọc theo quy tắc 4 mới.
   - Lưu thêm `OwnerUserId` vào record `ValidRead`. Record này là private, chỉ dùng trong service.
   - Giữ nguyên hai bước giới hạn theo trẻ hiện có, rồi thêm bước giới hạn gia đình:
     ```csharp
     .GroupBy(read => (read.OwnerUserId, Month: new DateTime(read.Day.Year, read.Day.Month, 1)))
     .SelectMany(group => group
         .OrderBy(read => read.CompletedAt).ThenBy(read => read.StoryId).ThenBy(read => read.ChildProfileId)
         .Take(MaxValidReadsPerFamilyPerMonth))
     ```
     `read.Day` đã là ngày theo giờ Việt Nam (`VietnamTime.LocalDate`).
   - Cuối cùng chỉ trả các lượt có `CompletedAt >= from`.
3. Cập nhật comment XML của `GetValidReadsAsync` cho đủ quy tắc 4 (sửa) và 8.

**Lưu ý**

- Chốt kỳ giữa tháng: kỳ 1 chốt ngày 1–15, kỳ 2 chốt ngày 16–30. Kỳ 2 đọc lại lượt từ ngày 1, nên hạn mức đã dùng ở kỳ 1 được trừ đúng. Do các bước lọc có tính tất định, kỳ 2 tính lại phần ngày 1–15 cũng ra đúng tập lượt mà kỳ 1 đã tính.
- Kỳ chốt tính theo ngày UTC (`ReportPeriod.Resolve`), còn hạn mức gia đình tính theo tháng giờ Việt Nam. Lượt đọc từ 17:00 tới 24:00 UTC ngày cuối tháng thuộc kỳ của tháng đó nhưng dùng hạn mức tháng sau. `budgetStart = VietnamTime.StartOfMonthUtc(from)`, tức 17:00 UTC ngày cuối tháng trước, nên kỳ sau vẫn thấy các lượt này và trừ hạn mức đúng, không tính trùng. Giữ hành vi này; không đổi cách tính kỳ.
- Cửa sổ 90 ngày của tiến độ Creator đối tác trải qua nhiều tháng. Mỗi tháng có hạn mức riêng, và tháng đầu được trừ dần từ đầu tháng. Đây là hành vi mong muốn.
- Khoảng đọc thêm tối đa khoảng một tháng phiên `Completed`. Truy vấn hiện vẫn đọc qua repository rồi lọc trong bộ nhớ như cũ, không cần tối ưu thêm.

## 6. Test

Thêm vào `tests/StoryPlatform.UnitTests/Features/CreatorStatistics/CreatorStatisticsServiceTests.cs`. Dùng các helper sẵn có: `Completed`, `PublishedStory`, `Child`, `Subscription`, `PaidRevenue`; kỳ `PeriodStart` / `PeriodEnd` là tháng 11/2026; `Morning` = 09:00 giờ Việt Nam ngày 15/11. Viết test trước, chạy cho thấy fail, rồi mới sửa code.

| Test | Dữ liệu | Kỳ vọng |
|---|---|---|
| `ValidRead_AtMostSixtyPerFamilyPerMonth_AcrossChildrenAndStories` | Phụ huynh trả phí, 4 hồ sơ trẻ (thêm 2 bằng `Child`), mỗi ngày mỗi trẻ đọc xong 1 truyện khác nhau từ 1/11 tới 30/11 (120 lượt, đều dưới giới hạn theo trẻ) | `TotalValidReadsAllCreators == 60`; 60 lượt được tính là 60 lượt sớm nhất |
| `ValidRead_FamilyCap_IsPerFamily` | Phụ huynh A chạm 60; phụ huynh B (trả phí) đọc 1 lượt | Tổng = 61 |
| `ValidRead_FamilyCap_ResetsEachVietnamMonth` | 60 lượt từ 1/11 tới 29/11; thêm 1 lượt lúc 02:00 UTC ngày 1/12 (= 09:00 ngày 1/12 giờ Việt Nam) | Kỳ tháng 11 = 60; kỳ tháng 12 (`2026-12-01..2026-12-31`) = 1 |
| `ValidRead_FamilyCap_UtcWindowAndVietnamMonthBoundary` | 60 lượt từ 1/11 tới 29/11; 1 lượt lúc 18:00 UTC ngày 30/11 (= 01:00 ngày 1/12 giờ Việt Nam) | Kỳ tháng 11 = **61** (lượt cuối nằm trong kỳ UTC tháng 11 nhưng dùng hạn mức tháng 12 theo giờ Việt Nam); kỳ tháng 12 không tính lại lượt này, và hạn mức tháng 12 của gia đình còn 59 |
| `ValidRead_FamilyCap_HoldsWhenTheMonthIsClosedInTwoPeriods` | 40 lượt ngày 1–15 và 40 lượt ngày 16–30 | `GetPeriodEarningsAsync(1/11, 15/11).TotalValidReads == 40`; `GetPeriodEarningsAsync(16/11, 30/11).TotalValidReads == 20` |
| `ValidRead_FamilyCap_DoesNotDiluteOtherCreators` | Kịch bản gian lận: phụ huynh phụ đọc 300 lượt truyện của `OtherCreatorId`; phụ huynh thật đọc 30 lượt truyện của `CreatorId`; có `PaidRevenue` | Phần của `OtherCreatorId` ≤ 60/90 quỹ |
| `ValidRead_ReadingTimeIsCappedByWallClock` | `TimeSpentSeconds = 600` nhưng `CompletedAt − StartedAt = 5 giây` | 0 lượt |
| `ValidRead_ReadingTimeWithinWallClock_StillCounts` | `TimeSpentSeconds = 120`, khoảng thời gian thật 10 phút | 1 lượt |

Helper `Completed(storyId, childId, completedAt, seconds)` hiện đặt `StartedAt = completedAt − seconds`. Với hai test cuối, tạo phiên trực tiếp bằng `_store.Seed(new ReadingSession { ... })`, hoặc thêm tham số tùy chọn `startedAt` cho helper.

**Chạy:**

```
dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~CreatorStatistics|FullyQualifiedName~CreatorPayout|FullyQualifiedName~CreatorTier|FullyQualifiedName~BusinessReport"
dotnet build
```

Sau đó chạy `tests/StoryPlatform.IntegrationTests` với filter `CreatorStatisticsApiTests|CreatorPayoutApiTests`, nếu môi trường có DB test.

Test hiện có không được hỏng. Nếu một test cũ hỏng vì dữ liệu của nó vượt 60 lượt/gia đình, báo lại thay vì sửa kỳ vọng.

**Quyết định sau lần chạy đầu (2026-11-28).** Hai test cũ hỏng vì dữ liệu dựng trước khi có quy tắc mới. Được sửa **dữ liệu**, giữ nguyên **kỳ vọng**:

- `ValidRead_OnlyReadsCompletedInsideThePeriod_Count`: lượt `PeriodStart.AddMinutes(-1)` và `PeriodStart.AddHours(1)` cùng một ngày theo giờ Việt Nam, cùng trẻ, cùng truyện. Code cũ tính mỗi lượt vào một kỳ, tức một ngày được 2 lượt, là lỗi tính trùng ở ranh giới UTC. Code mới đúng, vì giới hạn ngày được áp nhất quán qua các kỳ. Sửa: lượt trong kỳ đổi thành `PeriodStart.AddDays(1)` (ngày Việt Nam khác). Giữ `Assert.Equal(2, …)`. Thêm comment giải thích.
- `Content_PartnerProgress_ReportsEachCriterion_AndViolationRemovalBreaksEligibility`: 600 lượt từ một trẻ trong 60 ngày không còn đạt được, vì 60 lượt/gia đình/tháng. Đây là chủ đích: ngưỡng đối tác phải đến từ nhiều gia đình. Sửa: trải 600 lượt ra **10 tài khoản phụ huynh**, mỗi tài khoản có 1 hồ sơ trẻ và gói trả phí phủ cửa sổ, mỗi trẻ đọc 1 truyện khác nhau mỗi ngày trong 60 ngày. Như vậy mỗi gia đình ≤ 31 lượt/tháng theo giờ Việt Nam, dưới giới hạn. Giữ kỳ vọng `600m`, `IsEligible == true` và phần kiểm tra truyện bị gỡ phía sau.

## 7. Tài liệu cần cập nhật

- `docs/PARENT_PLANS_PAYMENT_API_GUIDE.md`, mục 7.3 "Lượt đọc hợp lệ": sửa điều 4 (thời gian đọc lấy giá trị nhỏ hơn giữa thời gian ứng dụng gửi và thời gian thật từ lúc mở tới lúc đọc xong), thêm điều 8 (tối đa 60 lượt mỗi tài khoản phụ huynh mỗi tháng theo giờ Việt Nam, giữ lượt sớm nhất, áp dụng cả khi chốt kỳ giữa tháng).
- `../docs/report5/package_design.md`, thuộc repo tài liệu cha `SEP490/docs`:
  - Mục 5.2: thêm điều kiện 8 vào danh sách lượt đọc hợp lệ, sửa điều kiện 4.
  - Mục 7.3: ghi lỗ hổng đã được chặn bằng giới hạn gia đình.
  - Mục 7.4: đánh dấu giải pháp 1 đã làm phương án nhẹ (giới hạn 60) và giải pháp 2 đã làm phần thời gian đọc theo server.

## 8. Ràng buộc khi triển khai

- Không commit, push hay mở PR. Chủ repo tự commit.
- Không tạo file mới ngoài phạm vi trên, không refactor ngoài `GetValidReadsAsync`.
- Không đổi API, DTO, entity hay migration.
- Cuối việc báo theo mẫu ở mục 11 của `CLAUDE.md`: Created / Modified / Deleted / Tests / Notes.
