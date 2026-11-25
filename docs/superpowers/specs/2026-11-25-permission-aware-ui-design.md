# UI bám theo permission — Đợt 1 — Thiết kế ngắn

Ngày: 2026-11-25. User chọn "Làm đợt 1" sau khi rà UI.

## Vấn đề
Menu đã ẩn theo quyền nhưng nút/tab trong trang còn dựa vào role hoặc không kiểm: Admin không có màn hàng đợi (luật dự phòng không có chỗ dùng); tab soạn lộ trình khoá theo role; rubric/hướng dẫn/thẻ ước để Admin soạn xong mới bị 403; tab Nhà sáng tạo và nút Báo cáo không theo quyền; nút Kích hoạt rubric chỉ hiện cho Admin dù luật chốt là cả hai.

## Phương án
| | **Backend trả trạng thái + lý do (chọn)** | Frontend chép luật |
|---|---|---|
| | Một nguồn luật (`AccessPolicy`), có test so với `EnsureAsync` | Dễ lệch khi đổi luật |

## Thiết kế
- `GET /Auth/me/access` thay `me/permissions`: mỗi quyền có `state` (`Allowed` / `Limited` / `Blocked`) và `reason`. Chỉ phục vụ hiển thị.
- UI: không có quyền → ẩn; `Blocked` → khoá nút ghi và hiện lý do (vẫn xem được); `Limited` → dùng được, hiện lý do.
- Rubric, hướng dẫn, thẻ ước (kể cả ghép thẻ), lộ trình mẫu theo `access`; Kích hoạt/kiểm tra/chạy thử rubric không bị khoá.
- Trang Nhà sáng tạo: mỗi tab theo quyền của nó. Nút Báo cáo ở Rà soát thư viện theo `moderation.published.review`.
- Admin có trang "Hàng đợi dự phòng" (`moderation.queue.process`), chỉ hiện loại mục role có quyền nhận.

## Không làm (đợt 2, 3)
Tách ảnh bìa khỏi `community.view`; hỏi lại khi rời trang Phân quyền; khu phụ huynh.
