# Permission có phạm vi và điều kiện — Phần 1: nền tảng + Luồng 5 (kiểm duyệt) — Thiết kế

Ngày: 2026-11-24. Nguồn: brainstorming với user sau khi phân tích 5 tài liệu "Luồng 1–5" (bản 23/11/2026). Nối tiếp `2026-11-22-phan-quyen-permission-design.md`.

> **Trạng thái:** thiết kế, chờ duyệt. Triển khai thật (user chọn hướng A, 24/11/2026).

## 1. Mục đích

Permission hiện chỉ trả lời "role nào gọi được API nào". Nghiệp vụ có thêm 9 loại luật (phạm vi sở hữu, điều kiện ngữ cảnh, tách bạch trách nhiệm, trạng thái, gói, quyền của bé, ủy quyền, xác minh lại, yêu cầu kèm hành động) đang viết cứng bằng `if (role == ...)` rải trong service. Hệ quả: trang Phân quyền mô tả sai thực tế (Admin "có" `moderation.queue.process` nhưng nghiệp vụ chỉ cho khi đủ điều kiện), cấp chéo bị service chặn, và một số chỗ code lệch luật đã chốt.

Mục tiêu dài hạn: mỗi quyền = **Hành động + Phạm vi + Điều kiện**, luật nằm ở một chỗ có tên, có test, hiện được trên giao diện. Làm theo 3 phần, mỗi phần merge/deploy riêng:

1. **Phần 1 (tài liệu này):** hạ tầng phạm vi + điều kiện; chuyển các cổng kiểm quyền của Luồng 5 – Phần I (kiểm duyệt) sang policy; hiện phạm vi/điều kiện trên trang Phân quyền.
2. Phần 2: Luồng 2 + Luồng 5 – Phần II (gói, chi trả, Creator, nhân sự).
3. Phần 3: Luồng 1, 3, 4 (phụ huynh, Creator, tạo truyện, lộ trình).

## 2. Luật nghiệp vụ Phần 1 phải thể hiện (Luồng 5, chốt 09/11/2026)

| Mã luật | Nội dung | Hiện trạng code |
|---|---|---|
| L5-Q1 | Hàng đợi M1–M3: Moderator làm chính. Admin chỉ nhận mục khi mục **đã chuyển lên (Escalated)**, hoặc **không còn Moderator hoạt động hợp lệ** (khác người tạo mục) | `EnsureAdministratorMayClaimAsync` chỉ xét "không còn Moderator hợp lệ"; **chưa xét Escalated** |
| L5-Q2 | Không ai xử lý mục **do chính mình tạo/nộp** | Có ở claim (`CreatedByUserId == actor`), rải rác chỗ khác |
| L5-Q3 | Chỉ thao tác trên mục **đang do chính mình nhận** và **còn hạn 2 giờ** | Lặp lại trong từng service (`ContentReportReview`, `StoryRatingModeration`…) |
| L5-Q4 | Báo cáo nội dung và đánh giá truyện **chỉ Moderator** xử lý, Admin không được kể cả khi hết Moderator | Kiểm loại mục cứng trong `ClaimAsync` |
| L5-C1 | Cấu hình M4 (rubric, thẻ điều ước, hướng dẫn sáng tác, bộ truyện mẫu): Moderator làm chính; **Admin chỉ xem, chỉ sửa khi không còn Moderator hoạt động**. Kích hoạt rubric: cả hai | **Lệch:** hướng dẫn sáng tác, thẻ điều ước, rubric cho Admin sửa bất kỳ lúc nào; bộ truyện mẫu chặn Admin cả khi hết Moderator |
| L5-O1 | Mở nội dung đã công khai: cả hai, mỗi lần mở ghi audit | Đã có audit |
| L5-A | Tài khoản thao tác phải đang hoạt động (không xoá, không khoá, không bị tạm khoá đăng nhập) | Lặp lại trong từng service |

## 3. Mục tiêu, không phải mục tiêu, điều cấm

**Mục tiêu**
- G1. Danh mục permission có **phạm vi** (`Any`, `Own`, `Assigned`) và mỗi lần cấp quyền cho một role có thể kèm **điều kiện có tên**.
- G2. Một dịch vụ dùng chung `IAccessPolicy.EnsureAsync(actor, permission, resource)` thay các cổng ở mục 2; mọi service kiểm duyệt gọi nó.
- G3. Sửa 2 chỗ lệch nghiệp vụ: L5-Q1 thêm nhánh Escalated; L5-C1 đúng luật Admin chỉ sửa cấu hình M4 khi hết Moderator.
- G4. Trang Phân quyền hiện phạm vi và điều kiện của từng quyền theo role (chỉ đọc).
- G5. Ma trận quyền ở tầng endpoint (`EndpointAuthorizationMatrixTests`) **không đổi**.

**Không phải mục tiêu (Phần 1)**
- Không cho Admin soạn hoặc sửa điều kiện trên giao diện: điều kiện định nghĩa trong code (có review, test).
- Chưa chia việc theo `Expertise` của Moderator (đề xuất cho sau Phần 3).
- Chưa đụng Luồng 1–4 và Luồng 5 – Phần II.

**Điều cấm**
- A1. Không làm mất bước duyệt của người đối với nội dung cho trẻ.
- A2. Không nới quyền nào so với luật đã chốt; nơi code đang rộng hơn luật thì siết theo luật (L5-C1), nơi code chặt hơn luật thì mở đúng bằng luật (bộ truyện mẫu khi hết Moderator).

## 4. Phương án đã cân nhắc

| | **A. Policy có tên theo kiểu resource-based authorization (chọn)** | B. ABAC lưu luật trong DB | C. Chỉ viết tài liệu |
|---|---|---|---|
| Cách làm | Điều kiện là các lớp có tên trong code; service gọi `EnsureAsync(actor, permission, resource)` | Admin soạn luật "nếu… thì…" | Ghi luật vào báo cáo |
| Ưu | Luật an toàn có review/test; hiện được lên UI; làm dần từng luồng | Linh hoạt nhất | Không rủi ro |
| Nhược | Phải sửa từng service | Admin soạn sai là hỏng an toàn của trẻ; nặng | Không gom được luật |

User chọn **A**, triển khai thật.

**Quyết định khi thiết kế:** làm theo đúng mô hình resource-based authorization của ASP.NET Core (requirement + handler + resource), nhưng đặt trong tầng Application bằng interface riêng `IAccessPolicy` / `IAccessCondition`, để service nghiệp vụ không phụ thuộc gói ASP.NET (tầng Application hiện không tham chiếu `Microsoft.AspNetCore.*`). Endpoint vẫn dùng `[HasPermission]` như cũ.

## 5. Thiết kế

### 5.1 Danh mục: phạm vi
`PermissionDefinition` thêm `AccessScope Scope` (`Any` mặc định, `Own`, `Assigned`). Phần 1 đặt:

| Permission | Phạm vi |
|---|---|
| `moderation.queue.process`, `moderation.published.review`, `moderation.report.review`, `moderation.rating.moderate`, `review.public_story`, `review.gray_zone`, `review.roadmap`, `review.creator_publication` | `Assigned` (mục đang do chính mình nhận, còn hạn) |
| Còn lại của Luồng 5 – Phần I | `Any` |

`Assigned` được đánh giá bởi điều kiện `ClaimedBySelf` (5.3) khi resource là một `ModerationItem`; các thao tác không trên một mục cụ thể (liệt kê hàng đợi, nhận mục) không áp `ClaimedBySelf`.

### 5.2 Điều kiện gắn theo role
`DefaultRolePermissions` thêm bảng `GrantConditions : (roleCode, permission) → string[]`. Phần 1:

| Role | Permission | Điều kiện |
|---|---|---|
| Moderator, Administrator | mọi quyền ở 5.1 nhóm `Assigned` | `NotOwnContent` |
| Administrator | `moderation.queue.process`, `review.public_story`, `review.gray_zone`, `review.roadmap`, `review.creator_publication` | `AdminFallback` |
| Administrator | `rubric.manage`, `guide.manage`, `wishcard.manage`, `template.roadmap.author` (ghi) | `NoActiveModerator` |
| Mọi role nhân viên | mọi quyền | `ActiveAccount` (luôn áp, không cần khai báo) |

L5-Q4 không cần điều kiện: `moderation.report.review` và `moderation.rating.moderate` chỉ Moderator giữ; nhận mục loại ContentReport/StoryRating yêu cầu đúng quyền đó (5.4).

Điều kiện không lưu DB: khi Admin trả lại một quyền cho role (cơ chế thu/trả hiện có), điều kiện trong code vẫn áp. Quyền `template.roadmap.author` hiện chỉ Moderator giữ trong `DefaultRolePermissions`; để L5-C1 hoạt động, Administrator được cấp mặc định `template.roadmap.author` **kèm** `NoActiveModerator` (endpoint: thêm Administrator vào các action soạn bộ mẫu — đây là thay đổi ma trận endpoint duy nhất, có chủ đích, phải cập nhật snapshot và ghi rõ).

### 5.3 Điều kiện có tên (Application, `Features/AccessControl/Conditions/`)

| Tên | Đúng khi |
|---|---|
| `ActiveAccount` | Actor tồn tại, không xoá, `Status == Active`, `LockedUntil` đã qua |
| `NotOwnContent` | `resource.CreatedByUserId != actor.Id` (resource có người tạo) |
| `ClaimedBySelf` | `item.Status == Claimed && item.ClaimedByUserId == actor.Id && ClaimedAt + ModerationQueuePolicy.ClaimTimeout > now`; hết hạn → 409 như hiện tại |
| `AdminFallback` | Actor không phải Administrator, **hoặc** `item.Status == Escalated`/`item.EscalatedAt != null`, **hoặc** không còn Moderator `Active` nào khác `item.CreatedByUserId` |
| `NoActiveModerator` | Actor không phải Administrator, **hoặc** không còn Moderator `Active` nào |

Mỗi điều kiện: `interface IAccessCondition { string Name; Task<AccessDecision> EvaluateAsync(AccessContext ctx, CancellationToken ct); }`; `AccessDecision` gồm cho/không và message tiếng Việt (giữ nguyên message hiện có ở mục 2 để client không đổi).

### 5.4 Dịch vụ `IAccessPolicy`
```text
Task<UserAccount> EnsureAsync(int actorUserId, string permission, object? resource = null, CancellationToken ct = default)
Task<bool> CanAsync(...)   // cho trường hợp lọc danh sách / hiện nút
```
Thứ tự: `ActiveAccount` → role có permission (`IPermissionService`) → điều kiện theo `GrantConditions[(role, permission)]` → phạm vi `Assigned` (`ClaimedBySelf`) nếu resource là mục kiểm duyệt. Trả về actor đã nạp để service khỏi đọc lại. Không đạt → `ForbiddenException` (hoặc `ConflictException` cho hết hạn nhận mục) với message của điều kiện đầu tiên thất bại.

Ánh xạ "loại mục → quyền xử lý" để nhận mục (`ModerationQueueService.ClaimAsync`): `ContentReport → moderation.report.review`, `StoryRating → moderation.rating.moderate`, `PublicStoryReview → review.public_story`, `GrayZoneStory → review.gray_zone`, `RoadmapTemplate → review.roadmap`, `PublicStoryRemoval → review.creator_publication`, `PublishedContentReview → moderation.published.review`, còn lại `moderation.queue.process` (đối chiếu enum `ModerationItemType` khi làm).

### 5.5 Service đổi sang `IAccessPolicy` (Phần 1)
`ModerationQueueService` (list, claim, open, `RequireStaffAsync`, `EnsureAdministratorMayClaimAsync`), `GrayZoneWorkflowService`, `PublicStoryReviewService`, `CreatorPublicationService` (quyết định gỡ), `ModerationReviewViewService`, `PublishedContentReviewService`, `ContentReportReviewService`, `StoryRatingModerationService`, `ModerationRubricService`, `RoadmapTemplateService` (+ `.List`), `RoadmapReviewService`, `AuthoringGuideManagementService`, dịch vụ quản lý thẻ điều ước. Các truy vấn chỉ để lọc dữ liệu (đếm Moderator trong `ModerationSweepService`, gửi thông báo cho Moderator) giữ nguyên.

`IAccessPolicy` là tham số constructor tuỳ chọn; thiếu thì dùng bản mặc định dựng từ `IUnitOfWork` + `StaticPermissionService`, để các test đang dựng service bằng tay không phải sửa hàng loạt.

### 5.6 Hiện trên trang Phân quyền
- `GET admin/permissions` thêm `scope`; `GET admin/roles/{code}/permissions` thêm `conditions: { [permission]: string[] }`.
- Frontend: cột "Phạm vi" (Tất cả / Của mình / Mục đã nhận) và dòng "Điều kiện" dưới mô tả, bằng câu tiếng Việt từ danh mục điều kiện (ví dụ `AdminFallback` → "Chỉ khi mục đã chuyển lên hoặc không còn kiểm duyệt viên").

## 6. Kiểm chứng
- Mỗi điều kiện có test riêng (đúng/sai, biên hết hạn 2 giờ, Escalated, Moderator duy nhất là người tạo).
- Test hành vi giữ nguyên cho các cổng cũ (message, mã lỗi).
- Test thay đổi có chủ đích: Admin **không** sửa được hướng dẫn sáng tác / thẻ điều ước / rubric khi còn Moderator hoạt động, **được** khi không còn; Admin nhận được mục Escalated khi vẫn còn Moderator; Admin soạn được bộ truyện mẫu khi không còn Moderator.
- `EndpointAuthorizationMatrixTests`: chỉ thêm Administrator vào các action soạn bộ truyện mẫu (5.2), còn lại không đổi.
- Toàn bộ test backend/frontend pass.

## 7. Rủi ro
| Rủi ro | Giảm thiểu |
|---|---|
| Đổi nhiều service kiểm duyệt cùng lúc | Message giữ nguyên; test hành vi trước khi đổi; làm từng service |
| Siết quyền Admin ở cấu hình M4 làm Admin bất ngờ mất quyền sửa | Đúng luật đã chốt; ghi trong PR; trang Phân quyền hiện rõ điều kiện |
| Đếm Moderator mỗi lần kiểm | Một truy vấn `CountAsync` theo chỉ mục; chỉ chạy khi actor là Administrator |

## 7b. Điều chỉnh khi triển khai (24/11/2026)
- `IAccessPolicy.EnsureAsync(actorId, permission, resource, AccessMode, deniedMessage)`: `AccessMode.Read` chỉ kiểm tài khoản + permission; `Write` áp thêm điều kiện. Kích hoạt rubric và chạy thử rubric gọi `Read` để cả hai role làm được.
- Điều kiện là các nhánh trong `AccessPolicy`, không tách lớp `IAccessCondition` (YAGNI). `AdminFallback` chỉ xét mục đang `Pending`.
- `ClaimedBySelf` giữ ở từng service (mỗi nơi có mã lỗi riêng client đang dùng); danh mục chỉ ghi phạm vi `Assigned`.
- `moderation.published.review` (tạo yêu cầu rà soát) giữ phạm vi `Any`.
- Chưa có chức năng nào ghi `EscalatedAt`; nhánh Escalated sẵn sàng khi có.
- Prod đã có bản gán quyền trong DB nên Administrator chưa tự có `template.roadmap.author`; bật trên trang Phân quyền sau khi deploy.

## 8. Để lại cho người triển khai
- Đối chiếu đầy đủ enum `ModerationItemType` với bảng 5.4.
- Đối chiếu tên mã quyền cấu hình M4 trong bảng 5.2 (`rubric.manage`, `guide.manage`, `wishcard.manage`, `template.roadmap.author`) với `Permissions.cs` thực tế; dùng mã đang có, không đặt mã mới.
- Câu tiếng Việt hiển thị cho từng điều kiện.
- Vị trí đặt kiểu `AccessScope` (Application, cạnh `PermissionCatalog`).
