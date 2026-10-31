# Trạng thái rà soát Bé ước và AI gắn thẻ

Cập nhật: 29/10/2026.

## Kết luận và cách đọc trạng thái

Bugbot đã rà soát code của các thay đổi chưa commit và không tìm thấy lỗi mới đủ căn cứ để báo cáo. Kết quả này không có nghĩa toàn bộ feature đã được xác minh end-to-end.

Ba trường hợp dưới đây được đánh dấu **CHƯA RÀ SOÁT ĐẦY ĐỦ BẰNG API/POSTGRESQL THẬT**. Chúng đã được đọc code nhưng còn thiếu bằng chứng thực thi; không tính là PASS và cũng chưa kết luận là bug.

## Các ca còn mở

### WISH-LIVE-01 — Core lưu mapping AI

- [ ] **CHƯA XÁC MINH END-TO-END**: Core → AI → Vertex → lưu mapping vào PostgreSQL.
- Đã có: AI classifier thật trả HTTP 200 với ba matches vượt qua validation; CRUD mapping thủ công qua API đã chạy được.
- Còn thiếu: request AI match qua Core thành công và bản ghi mapping Source=Ai tương ứng trong database.
- Lý do: database lúc kiểm thử không có Creator story ở trạng thái Ready đủ điều kiện. Creator Draft bị chặn 409 đúng quy tắc.
- Điều kiện chạy tiếp: chuẩn bị truyện test mới bằng API, hoàn tất các bước hợp lệ để đạt Ready; không ép trạng thái bằng SQL.
- Tiêu chí đóng: gọi AI match bằng Moderator hợp lệ; kiểm tra version/fingerprint, ID thẻ, bằng chứng và mapping Source=Ai được lưu đúng, không trùng; không làm thay đổi quyết định công khai truyện.

### WISH-LIVE-02 — Moderator gỡ thẻ khi AI đang chạy

- [ ] **CHƯA XÁC MINH RACE VỚI POSTGRESQL/PROVIDER THẬT**.
- Đã có: đọc code chống stale mapping, test hồi quy tự động; API thêm/gỡ thẻ ghi đúng marker, replay không sinh marker dư.
- Còn thiếu: hai request thật chồng lấn, Moderator gỡ thẻ sau lúc AI chụp snapshot nhưng trước lúc AI lưu kết quả.
- Điều kiện chạy tiếp: có truyện test Ready và cách quan sát/điều phối thời điểm request để chứng minh đúng thứ tự. Hai request chạy gần nhau nhưng không xác nhận được thứ tự chưa đủ bằng chứng.
- Tiêu chí đóng: request AI cũ nhận 409 WISH_CARD_MAPPING_STALE, không thêm lại thẻ đã gỡ; kiểm tra thêm chuỗi thêm/gỡ quay về cùng mapping ban đầu (ABA), thẻ vẫn giữ quyết định mới của Moderator.

### WISH-LIVE-03 — Rollback và phát thông báo nhắc hạn sau commit

- [ ] **CHƯA XÁC MINH LỖI COMMIT/ROLLBACK VỚI DATABASE THẬT**.
- Đã có: đọc code lưu thông báo trong transaction rồi phát realtime sau commit; test hồi quy tự động. Notification StoryWishSubmitted đã được đối chiếu qua API/database, nhưng không phải reminder hết hạn.
- Còn thiếu: wish đến hạn và bằng chứng khi commit thất bại/hủy trước commit; kiểm tra thêm phát SignalR thất bại sau commit.
- Điều kiện chạy tiếp: dữ liệu test đến hạn tự nhiên và cơ chế kiểm thử lỗi có kiểm soát trên môi trường được cho phép. Chưa có API điều khiển thời gian hoặc inject lỗi commit.
- Tiêu chí đóng: rollback không để lại notification/ExpiryWarnedAt và không phát realtime; commit thành công mới phát; phát realtime thất bại không rollback bản ghi đã commit hoặc tạo notification trùng trong lượt sweep tiếp theo.

## Ràng buộc kiểm thử hiện tại

- Chỉ dùng database local hiện hữu qua API để thay đổi dữ liệu; đối chiếu database chỉ đọc.
- Không migration, seed, reset, sửa timestamp hoặc ép trạng thái bằng SQL.
- Không chạy fixture PostgreSQL có thao tác tự tạo/migrate/seed database khi chưa được cho phép.
- Chưa sửa code, commit hoặc push trong lượt lập báo cáo này.

## Bằng chứng đã có

- Test tự động lần sửa schema: Core unit 2851 PASS, AI unit 128 PASS, AI HTTP integration với provider giả lập 26 PASS; tổng 3005 PASS. Không chạy lại trong lượt lập báo cáo này.
- Lỗi Vertex HTTP 400 đã xử lý bằng cách bỏ maxItems=128 khỏi response schema; xác minh trực tiếp AI API trả 200. Giới hạn và grounding vẫn được kiểm tra tại backend.
- Báo cáo test local: [API thật](../TestResults/wish-api-live/REPORT.md), [sửa schema](../TestResults/wish-schema-fix/REPORT.md).
- File TestResults được Git bỏ qua và có thể chỉ hiện hữu trên máy đã chạy test. Báo cáo này nằm trong docs để bàn giao các ca chưa hoàn tất.

Chỉ đổi từng mục sang PASS khi có kết quả chạy thực tế và bằng chứng tương ứng; không suy ra PASS từ việc Bugbot chưa tìm thấy lỗi hoặc unit test đã đạt.
