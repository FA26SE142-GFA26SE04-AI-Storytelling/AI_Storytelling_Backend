# Permission có phạm vi và điều kiện — Phần 3: Luồng 1, 3, 4 (phụ huynh, Creator) — Thiết kế ngắn

Ngày: 2026-11-24. Nối tiếp Phần 1 (`2026-11-24-permission-policies-design.md`) và Phần 2 (`2026-11-24-permission-policies-admin-design.md`). User chọn hướng A + B ngày 24/11/2026.

## Mục đích
Luật Luồng 1, 3, 4 chủ yếu là **chỉ chủ sở hữu**: mỗi trẻ có một phụ huynh chủ (Luồng 1); truyện gia đình thuộc phụ huynh, truyện công khai thuộc Creator (Luồng 3); lộ trình, phản hồi đọc, điều ước thuộc hồ sơ trẻ của phụ huynh (Luồng 4). Thêm: tự duyệt theo ngưỡng là phụ huynh ủy quyền trước; Parental Gate khi từ phiên của trẻ vào khu quản lý; giới hạn theo gói.

## Hiện trạng (kiểm kê 24/11/2026)
- Kiểm chủ sở hữu đã gom về `IChildProfileAccessGuard` / `ChildOwnership` (38 file) và kiểm `AuthorUserId` trong service truyện.
- Mọi endpoint phụ huynh/Creator có id trên route đều truyền người gọi xuống service, trừ 2 endpoint đọc dữ liệu công khai (`authoring-guides/{guideId}/render`, `Story/community/{id}/media` chỉ trả truyện đã xuất bản).
- Tự duyệt, Parental Gate, giới hạn gói đã có code khớp bản chốt.

## Phương án
| | **A + B (chọn)** | C. Đưa mọi kiểm chủ sở hữu về `IAccessPolicy` |
|---|---|---|
| | Test API khoá lỗi truy cập chéo + hiện luật trên trang Phân quyền | ~40 file, rủi ro cao, hành vi không đổi |

## Thiết kế
**A. Test chặn truy cập chéo** (`tests/StoryPlatform.IntegrationTests/Api/CrossOwnerAccessApiTests.cs`, API thật trên SQLite):
- Dữ liệu: phụ huynh A có hồ sơ trẻ, truyện gia đình, điều ước; Creator A có truyện. Phụ huynh B, Creator B đăng nhập.
- Lấy danh sách endpoint từ bảng route của ứng dụng: mọi endpoint có `[HasPermission]` thuộc nhóm Phụ huynh / Dùng chung / Creator và có tham số route trỏ tới tài nguyên có chủ (`childProfileId`, `childId`, `storyId`, `wishId`, `id` của các controller hồ sơ trẻ).
- Gọi bằng tài khoản B với id của A (tham số phụ như `taskId`, `packageId` dùng id không tồn tại; body `{}`): **không được trả 2xx**; endpoint GET phải trả 403 hoặc 404.
- Endpoint công khai theo thiết kế nằm trong danh sách miễn trừ có ghi lý do.
- Kiểm số endpoint được quét > 0 theo từng nhóm để test không âm thầm quét rỗng.
- Lỗi nào lộ ra thì sửa trong service bằng `IChildProfileAccessGuard` hoặc kiểm `AuthorUserId` có sẵn.

**B. Điều kiện mô tả cho quyền phụ huynh/Creator** trên trang Phân quyền (chỉ đọc):
- `DefaultRolePermissions.GrantConditions` thêm các điều kiện:
  - `OwnChildProfile` — User: `child.profile.manage`, `parent.feedback.manage`, `parent.story.author`: "Chỉ hồ sơ trẻ và truyện của chính mình".
  - `PlanLimits` — User: `child.profile.manage`, `parent.story.author`: "Giới hạn theo gói (số hồ sơ trẻ, lượt AI)".
  - `DelegatedApproval` — User: `story.review.approve`: "Tự duyệt theo ngưỡng chỉ khi phụ huynh đã ủy quyền; không áp cho truyện công khai; truyện từng vào vùng xám luôn qua kiểm duyệt viên".
  - `ParentalGate` — User: `child.profile.manage`: "Từ phiên của trẻ vào khu quản lý phải qua Parental Gate".
  - `OwnCreatorStory` — ContentCreator: `creator.studio`, `media.generate`: "Chỉ truyện của chính mình".
- Các điều kiện này **được kiểm ở code hiện có** (guard, Safety Policy, middleware phiên trẻ); `AccessPolicy` không đánh giá chúng (nhánh `default` trả đạt). Ghi rõ trong `AccessConditions`.

## Không làm
- Không chuyển kiểm chủ sở hữu về `IAccessPolicy` (hướng C).
- Không đổi luật gói, tự duyệt, Parental Gate.

## Kết quả triển khai (24/11/2026)
- Test quét 134 lượt gọi (phụ huynh B và Creator B): không lượt nào 2xx; mọi GET trả 403/404 → không phát hiện lỗi truy cập chéo, không phải sửa service.
- Giới hạn: 58 endpoint ghi dừng ở kiểm dữ liệu đầu vào (400/415 vì body rỗng) trước khi tới bước kiểm chủ sở hữu; test chứng minh "không thành công" nhưng chưa chứng minh có bước kiểm chủ cho nhóm này. Muốn chặt hơn thì cần body hợp lệ cho từng endpoint (để sau).

## Kiểm chứng
- Test A pass (hoặc lỗi lộ ra được sửa kèm test).
- `RolePermissionAdminServiceTests`: User có điều kiện `OwnChildProfile` trên `child.profile.manage`.
- Toàn bộ test backend pass; snapshot endpoint không đổi.
