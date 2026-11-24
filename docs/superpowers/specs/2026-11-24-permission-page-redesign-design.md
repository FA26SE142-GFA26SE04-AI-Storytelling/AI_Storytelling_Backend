# Thiết kế lại trang Phân quyền — Thiết kế ngắn

Ngày: 2026-11-24. User chọn hướng "Ma trận + Chỉnh role" sau khi xem trang trên prod.

## Vấn đề (kiểm trên prod, 3 role)
1. Mỗi role hiện đủ 45 quyền, phần lớn bị khoá (Kiểm duyệt viên 28, Phụ huynh 33, Quản trị viên 21).
2. Chỉ xem được một role mỗi lần; không trả lời được "ai làm được việc X".
3. Cột đầu là mã kỹ thuật; mô tả tiếng Việt nằm sau.
4. Phạm vi "Tất cả" mâu thuẫn điều kiện "của chính mình" (`child.profile.manage`, `parent.story.author`, `media.generate` của Phụ huynh).
5. Điều kiện lặp nguyên câu nhiều lần (Quản trị viên: 5 lần mỗi câu).
6. Lời giải thích đầu trang sai sau Phần 1–2 ("hệ thống vẫn kiểm role"); gợi ý menu chỉ nói Moderator/Administrator.
7. Không thấy quyền nào khác mặc định, không có "về mặc định", không có lịch sử.
8. Xác nhận lưu không nói tên quyền; trên điện thoại mỗi quyền là một thẻ 4 dòng.

## Phương án
| | **Ma trận + Chỉnh role (chọn)** | Chỉ sửa trang chỉnh role |
|---|---|---|
| | Nhìn mọi role một lần + chỉnh từng role gọn | Không trả lời được "ai làm được X" |

## Thiết kế
**Backend** (`GET admin/roles`, `GET/PUT admin/roles/{code}/permissions`, chỉ thêm trường):
- `RoleDto.Id` (để ghép lịch sử trong `audit_logs`, `EntityId` = id role).
- `RolePermissionsDto.DefaultCodes`: quyền mặc định của role.
- `RolePermissionsDto.Scopes`: phạm vi theo role cho từng quyền assignable; quyền có điều kiện `OwnChildProfile` hoặc `OwnCreatorStory` thì là `Own`, còn lại theo danh mục.
- `RolePermissionsDto.ConditionCodes`: mã điều kiện theo quyền (giữ `Conditions` là câu mô tả, cùng thứ tự).

**Frontend** (`features/staff/`):
- Hai tab: **Ma trận** (mặc định) và **Chỉnh role**.
- Ma trận: hàng là việc (mô tả tiếng Việt, mã kỹ thuật hiện nhỏ phía dưới), nhóm theo khu vực: Việc của phụ huynh · Dùng chung · Việc của nhà sáng tạo · Kiểm duyệt – xử lý hàng đợi · Kiểm duyệt – cấu hình và xem · Quản trị. Cột là 5 role. Ô: ✓ đang bật · ○ tắt (bật lại được) · trống = không áp dụng; chấm cam = khác mặc định; nhãn ngắn điều kiện trong ô, rê chuột xem câu đầy đủ. Bấm tên role ở đầu cột → sang Chỉnh role của role đó. Một bảng chú thích điều kiện duy nhất dưới ma trận.
- Chỉnh role: chỉ quyền role được bật/tắt; cột Phạm vi theo role; nhãn điều kiện ngắn; nhãn "Khác mặc định"; nút "Về mặc định"; hộp xác nhận liệt kê tên quyền bật/tắt; lịch sử 10 lần thay đổi gần nhất của role (ai, lúc nào, bật/tắt gì) từ `audit_logs` (`RolePermissionsUpdated`), chỉ hiện khi tải được.
- Viết lại lời giải thích; sửa gợi ý menu thành "Bật/tắt quyền của các role người lớn".
- Nhãn ngắn điều kiện (chữ giao diện): Không tự duyệt · Dự phòng · Khi hết KDV · Của mình · Theo gói · Cần ủy quyền · Parental Gate.

## Không làm
- Không cấp quyền ngoài mặc định (vẫn chỉ thu/trả); không sửa điều kiện trên giao diện.

## Kiểm chứng
- Unit test backend cho 4 trường mới; Vitest cho hàm thuần (dựng ma trận, khác mặc định, khu vực, diễn giải lịch sử).
- Xem trên prod sau deploy ở khổ desktop và điện thoại, theme sáng/tối.
