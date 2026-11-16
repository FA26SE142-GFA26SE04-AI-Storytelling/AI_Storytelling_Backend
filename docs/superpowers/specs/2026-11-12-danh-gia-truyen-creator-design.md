# Đánh giá truyện Creator của phụ huynh — Thiết kế

Ngày: 2026-11-12. Nguồn: brainstorming với user (đường "architectural").

> **Tên trong code:** dùng `StoryRating` (bảng `story_ratings`, `story_rating_reports`) thay cho `StoryReview` vì repo đã có namespace `Features.StoryReview` và `IStoryReviewService` của luồng duyệt truyện AI; trùng tên gây lỗi biên dịch. Nghiệp vụ không đổi.

## 1. Mục tiêu

Phụ huynh chấm sao (1–5) và viết nhận xét công khai cho truyện Creator đang ở thư viện cộng đồng. Phụ huynh khác đọc được nhận xét; Creator xem đánh giá truyện của mình; thư viện hiện điểm trung bình và sắp xếp được theo điểm. Nhận xét có chữ đi qua bộ lọc tự động; nghi ngờ thì vào hàng đợi Moderator. Ai cũng báo cáo được nhận xét đang hiện.

Hiện trạng: chưa có (không có bảng, API hay tài liệu nghiệp vụ). Gần nhất là `ContentReport` (khiếu nại riêng gửi Moderator) và lượt yêu thích của bé (`ReadingProgress.IsFavorited`).

## 2. Quyết định của user

| Câu hỏi | Chọn |
|---|---|
| Dạng phản hồi | Sao 1–5 + nhận xét **công khai** |
| Kiểm duyệt | Lọc tự động + Moderator khi nghi ngờ; ai cũng báo cáo được nhận xét đã hiện |
| Ai được đánh giá | Phụ huynh có bé **đã mở đọc** truyện (có `ReadingSession`) |
| Creator | **Chỉ xem + báo cáo**, không trả lời |
| Danh tính hiển thị | **Tên rút gọn** "Phụ huynh H***" |
| Hướng triển khai | Bảng riêng + dùng lại hàng đợi kiểm duyệt (loại mục mới `StoryRating`) |
| Trạng thái báo cáo | `Open` / `Approved` / `Hidden` |

## 3. Dữ liệu

### 3.1 `story_ratings` (entity `StoryRating : BaseEntity`)

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `StoryId` | int FK → stories | Restrict |
| `ReviewerUserId` | int FK → user_accounts | Restrict |
| `Rating` | int | 1–5, check constraint |
| `Comment` | string(1000)? | ≤ 1000 ký tự, đã che số điện thoại / email trước khi lưu |
| `Status` | string(30) | `StoryRatingStatus`: `Published`, `PendingModeration`, `Hidden` |
| `HiddenReason` | string(1000)? | Lý do Moderator ẩn |
| `ModeratedByUserId` | int? FK → user_accounts | Restrict |
| `ModeratedAt` | DateTime? | |
| `CreatedAt`, `UpdatedAt`, `IsDeleted` | | BaseEntity; query filter `!IsDeleted` |

Unique `(StoryId, ReviewerUserId)` lọc `NOT "IsDeleted"` (xóa rồi viết lại tạo dòng mới). Index `(StoryId, Status)`.

### 3.2 `story_rating_reports` (entity `StoryRatingReport : BaseEntity`)

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `StoryRatingId` | int FK → story_ratings | Restrict |
| `ReporterUserId` | int FK → user_accounts | Restrict; không bao giờ trả ra ngoài cho người viết / Creator |
| `Reason` | string(30) | `StoryRatingReportReason`: `Offensive`, `Spam`, `PersonalInfo`, `Irrelevant`, `Other` |
| `Note` | string(500)? | Đã che số điện thoại / email |
| `ModerationItemId` | int? FK → moderation_items | Mục hàng đợi mà báo cáo gộp vào |
| `Status` | string(30) | `StoryRatingReportStatus`: `Open`, `Approved`, `Hidden` |
| `ResolvedAt` | DateTime? | |
| `CreatedAt`, `IsDeleted` | | BaseEntity |

Unique `(StoryRatingId, ReporterUserId)`. Index `(ModerationItemId, Status)`.

### 3.3 `moderation_items`

- Thêm cột `StoryRatingId` (int? FK → story_ratings, Restrict).
- Unique `StoryRatingId` lọc `"ItemType" = 'StoryRating' AND "Status" IN ('Pending', 'Claimed') AND NOT "IsDeleted"` (mỗi đánh giá tối đa 1 mục mở).
- `ModerationItemType.StoryRating = 7`. Ưu tiên `Low`, SLA 72 giờ (mặc định của policy).
- `CreatedByUserId` = người viết đánh giá; `Flags` jsonb `{"source":"Screening","reasonCode":"..."}` hoặc `{"source":"Report"}`.

Một migration `AddStoryRatings`. Seed: file mới `73_story_ratings.sql`, `74_story_rating_reports.sql` (bắt buộc vì `Seed-Database.ps1` đòi mọi bảng có file seed). Mục hàng đợi seed nằm ở `42_moderation_items.sql` với `StoryRatingId = NULL`, rồi `73_story_ratings.sql` `UPDATE` gắn lại sau khi chèn đánh giá (khóa ngoại được kiểm theo thứ tự file).

### 3.4 Thông báo

`NotificationType.StoryRatingPublished = 33` (gửi Creator khi một đánh giá vừa được hiện), `StoryRatingModerated = 34` (gửi người viết khi Moderator duyệt / ẩn đánh giá đang chờ, hoặc ẩn đánh giá đang hiện).

## 4. Quy tắc

1. **Truyện hợp lệ** (mọi API phụ huynh): `Scope == Creator && IsPublished && Status == Ready && ArchivedAt == null && !IsDeleted`. Không hợp lệ → 404.
2. **Được đánh giá**: tài khoản `User` đang hoạt động, có ít nhất một `ChildProfile` (`OwnerUserId` = mình, `Status == Active`, chưa xóa) có `ReadingSession` với truyện. Không đủ → 403 `RATING_REQUIRES_READING`.
3. **Mỗi phụ huynh 1 đánh giá / truyện**; `PUT .../me` tạo hoặc sửa; `DELETE .../me` xóa mềm và đóng mục hàng đợi mở (`Closed`, `ClosedReason = "RatingDeleted"`).
4. **Lọc tự động** (`StoryRatingScreening`, không gọi AI, version `story-rating-rules-v1`):
   - Che số điện thoại / email (dùng `ModeratorTextMasker`), cắt khoảng trắng; chuỗi rỗng → `null`.
   - Không có chữ → sạch.
   - Có link (`http(s)://`, `www.`, hoặc `tên.miền` đuôi phổ biến) → `LINK`.
   - Có từ trong danh sách từ cấm (so khớp nguyên từ, không phân biệt hoa thường) → `BLOCKED_TERM`.
   - `Rating <= 2` và có chữ → `LOW_RATING_WITH_TEXT`.
5. **Trạng thái sau khi tạo / sửa**: `PendingModeration` nếu (bị lọc giữ) **hoặc** (đánh giá đang `PendingModeration` hoặc `Hidden` trước khi sửa); ngược lại `Published`. Khi `PendingModeration` mà chưa có mục mở → tạo mục `StoryRating` (`source = Screening`). Mỗi lần đánh giá chuyển sang `Published` từ trạng thái khác (tạo mới sạch, hoặc Moderator duyệt mục đang chờ) → thông báo Creator; sửa một đánh giá đang `Published` thì không báo lại.
6. **Báo cáo** (`User` hoặc `ContentCreator`): đánh giá phải `Published`, không phải của mình; Creator chỉ báo cáo đánh giá truyện của mình; trùng → 409. Gộp vào mục mở nếu có, không thì tạo mục (`source = Report`). Đánh giá **vẫn hiện** đến khi Moderator quyết định.
7. **Moderator** (chỉ role `Moderator`, Admin không nhận loại mục này): nhận mục qua `ModerationQueue/{itemId}/claim`, rồi:
   - `approve`: đánh giá `PendingModeration` → `Published` (thông báo người viết + Creator); đang `Published` thì giữ nguyên. Báo cáo `Open` của mục → `Approved`. Mục → `Approved`.
   - `hide { reason }`: đánh giá → `Hidden` + `HiddenReason`; báo cáo `Open` → `Hidden`; thông báo người viết. Mục → `Rejected`.
   - Cả hai: ghi `ModeratedByUserId/At`, `DecisionReason`, `DecidedByUserId/At`, audit log, `RowVersion++`.
8. **Ai thấy gì**: danh sách công khai và điểm chỉ tính `Published`. Người viết thấy `myRating` ở mọi trạng thái (kèm `hiddenReason`). Creator chỉ thấy `Published`. Trẻ em không gọi được API nào (role `User` / `ContentCreator` / `Moderator`; test `ChildToken_IsRejectedByEveryNonChildEndpoint` tự bao phủ).
9. **Danh tính**: `Phụ huynh {chữ cái đầu của từ cuối trong FullName, hoặc Username}***`, viết hoa; không có tên → `Phụ huynh`. Không trả email, id tài khoản, tên bé.

## 5. API

### Phụ huynh — `StoryRatingController` (class `[Authorize]`, role đặt trên từng action: `User`; báo cáo `User,ContentCreator`)

| Method | Route | Body / query | Trả về |
|---|---|---|---|
| GET | `/api/v1/StoryRating/story/{storyId}` | `pageIndex`, `pageSize`, `orderBy=newest\|highest\|lowest` | `StoryRatingListDto { summary, myRating?, canRate, ratings: PagedResult<StoryRatingDto> }` |
| PUT | `/api/v1/StoryRating/story/{storyId}/me` | `{ rating, comment? }` | `MyStoryRatingDto` |
| DELETE | `/api/v1/StoryRating/story/{storyId}/me` | — | `bool` |
| POST | `/api/v1/StoryRating/{ratingId}/reports` (`User,ContentCreator`) | `{ reason, note? }` | `StoryRatingReportResultDto { ratingId, status = "Received" }` |

### Creator — action trong `StoryRatingController`

| GET | `/api/v1/creator/stories/{storyId}/ratings` (`ContentCreator`) | như GET phụ huynh | `StoryRatingListDto` (không có `myRating`, `canRate = false`) |

Truyện phải `Scope == Creator`, `AuthorUserId == người gọi`, chưa xóa (không cần đang công khai).

### Moderator — `StoryRatingModerationController` (`[Authorize(Roles = "Moderator")]`)

| GET | `/api/v1/StoryRatingModeration/{itemId}` | `StoryRatingModerationDetailDto` |
| POST | `/api/v1/StoryRatingModeration/{itemId}/approve` | `StoryRatingDecisionResultDto` |
| POST | `/api/v1/StoryRatingModeration/{itemId}/hide` `{ reason }` | `StoryRatingDecisionResultDto` |

Cổng chung như `ContentReportReviewService`: Moderator đang hoạt động, mục `StoryRating`, đang do chính mình giữ, chưa quá 2 giờ.

### Thư viện

`CommunityStoryDto` thêm `AverageRating` (`double?`, làm tròn 1 chữ số, `null` khi chưa có) và `RatingCount`. `orderBy=rating`: theo trung bình (truyện chưa có đánh giá = 0), rồi số lượt, rồi `CreatedAt`, `Id`. Áp dụng cho `GET /api/v1/Story/community` và `GET /api/v1/creator/stories/published`.

## 6. Ngoài phạm vi

Creator trả lời đánh giá; Admin cấu hình danh sách từ cấm; lọc bằng AI; tự ẩn khi nhiều báo cáo; đánh giá truyện Parent / truyện cộng đồng của phụ huynh; thống kê đánh giá trong `CreatorStatistics`.

## 7. Kiểm thử

Unit: lọc tự động, tên rút gọn, điều kiện đánh giá, chuyển trạng thái khi tạo / sửa / xóa, báo cáo (trùng, tự báo cáo, Creator ngoài truyện), quyền và cổng Moderator, Admin không nhận mục `StoryRating`, điểm trung bình và `orderBy=rating`. Integration: luồng HTTP đầy đủ phụ huynh → Moderator → thư viện; seed alignment.
