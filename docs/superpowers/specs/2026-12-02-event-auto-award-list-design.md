# Danh sách nhận giải tự động sau khi sự kiện kết thúc

Ngày: 02/12/2026 · Phạm vi: backend + frontend · Chỉ áp dụng cho sự kiện theo hoạt động (`ConfigVersion = ActivityBased`).

## Mục đích

Hiện tại, sau khi sự kiện kết thúc, không có gì tự chạy: Moderator phải tự bấm "Xác minh kết quả" rồi "Đề xuất giải",
và Admin bấm "Duyệt trao giải" mà không thấy trước ai được nhận. Với sự kiện theo hoạt động, người thắng thậm chí
chỉ được gán vào suất lúc Admin bấm duyệt.

Người dùng muốn luồng rõ ràng:

1. Hết ngày kết thúc, hệ thống **tự** xác minh người tham gia và lập **danh sách tạm** người nhận giải.
2. Moderator **xem và chỉnh** danh sách (đổi người, bỏ trống, gán người vào suất trống), có ghi lý do.
3. Hết hạn khiếu nại (48 giờ sau xác minh, `EventAppealWindow.Hours`), Moderator **xác nhận** danh sách → danh sách bị khoá.
4. Admin xem đúng danh sách đã xác nhận và **phê duyệt**.
5. Người thắng nhận thưởng (giữ nguyên như hiện nay).

## Mục tiêu / Không làm / Điều phải tránh

- Mục tiêu: tự xác minh + tự lập danh sách tạm; Moderator chỉnh được trong khuôn khổ; Admin duyệt đúng danh sách đã chốt;
  mọi người vận hành thấy tên người nhận và tên truyện đoạt giải.
- Không làm: sự kiện kiểu cũ (`LegacyRoleBased`) giữ nguyên luồng hiện tại; không đổi cơ cấu/số suất giải;
  không đổi khâu nhận thưởng, đôn suất quá hạn, trao quà.
- Phải tránh: chỉnh tay bị ghi đè âm thầm khi hệ thống tính lại; gán giải cho người chưa xác minh đạt;
  một người nhận hai suất cùng một nguồn xếp hạng; Admin duyệt khi danh sách chưa được Moderator xác nhận.

## Cách làm đã chọn

Người dùng chọn **cách A** (giữ hạn khiếu nại; danh sách ngày đầu là danh sách tạm) và **chỉnh trong khuôn khổ** (lựa chọn 1).

Cách đã cân nhắc và không chọn:
- B: bỏ/rút ngắn hạn khiếu nại để có danh sách chính thức ngay — người tham gia mất quyền khiếu nại.
- Chỉnh tự do (thêm/xoá suất, gán người chưa xác minh) — lệch ngân sách giải Admin đã duyệt, mất tính công bằng.

## Thiết kế

### 1. Tự chạy sau khi kết thúc (backend)

- Worker chạy nền mới `EventResultSweepWorker` (Infrastructure, theo mẫu `ModerationSweepWorker`, mặc định 5 phút/lần,
  cấu hình `Events:ResultSweepWorker:IntervalMinutes`).
- Mỗi lượt gọi dịch vụ Application: tìm sự kiện `ActivityBased`, `Published`, `ResultsFinalizedAt == null`, đã hết thời gian
  thực hiện (`EventExecution`), còn hồ sơ ở trạng thái chờ xác minh (`Registered` / `MissionCompleted` / `PendingVerification`).
  Với mỗi sự kiện: chạy xác minh rồi lập danh sách tạm, trong khoá sự kiện; lỗi của một sự kiện không chặn sự kiện khác.
- Điều kiện "còn hồ sơ chờ xác minh" làm việc tự chạy chỉ xảy ra một lần mỗi sự kiện, không cần cột mới.
- Người thực hiện ghi vào audit là hệ thống (`actorUserId = null`). Xác minh và lập danh sách tách phần lõi khỏi phần
  kiểm tra quyền để worker gọi được mà không cần tài khoản người.

### 2. Danh sách tạm và chỉnh tay

- Danh sách tạm = các suất `AwardProposed` đã gán `ParticipationId`, tính bằng `AssignActivityProposedAwardsAsync` hiện có.
- Thêm vào `EventAward` (cần migration): `ManualAssignmentNote` (≤ 500 ký tự), `ManuallyAssignedByUserId`, `ManuallyAssignedAt`.
- Khi tính lại (nút "Tính lại danh sách", hoặc khi khiếu nại được chấp nhận): **bỏ qua suất đã chỉnh tay**, và người đang
  giữ suất chỉnh tay không được gán thêm suất khác cùng nguồn xếp hạng.
- API chỉnh một suất: `PUT /api/v1/events/awards/{awardId}/assignment` body `{ participationId: int | null, reason: string }`.
  - Quyền: `event.manage` + Moderator phụ trách chính (giống "Đề xuất giải").
  - Điều kiện: sự kiện đã hết thời gian thực hiện, chưa chốt (`ResultsFinalizedAt == null`), suất đang `AwardProposed`.
  - `participationId` phải là hồ sơ `Verified` thuộc đúng hoạt động/nguồn của suất, đúng đối tượng (Creator/Parent),
    và người đó chưa giữ suất khác cùng nguồn. `null` = bỏ trống suất. Lý do bắt buộc.
  - Ghi audit `EventAwardManuallyAssigned`.
- API ứng viên cho một suất: `GET /api/v1/events/awards/{awardId}/candidates` → danh sách người đủ điều kiện theo thứ tự xếp
  hạng của hệ thống (tên, tên truyện, điểm/số sao/số phiếu nếu có, đang giữ suất nào).
- "Thêm" = gán người vào suất trống; "Xoá" = bỏ trống suất. Không thêm/xoá suất.

### 3. Moderator xác nhận

- API mới `POST /api/v1/events/{eventId}/awards/confirm` (quyền như trên).
- Điều kiện: đã hết thời gian thực hiện; không còn hồ sơ chờ xác minh; không còn khiếu nại đang mở; hết hạn khiếu nại
  (`EventAppealWindow.EnsureNoOpenWindowAsync`); đã chấm đủ bài nếu có giải theo điểm.
- Tác dụng: lưu ảnh chụp xếp hạng/bảng xếp hạng như phần chốt hiện có trong `ApproveAwardsLockedAsync`, đặt
  `ResultsFinalizedAt`. Sau đó không chỉnh/tính lại được nữa.

### 4. Admin phê duyệt

- `ApproveAwardsAsync` với sự kiện `ActivityBased`: bắt buộc `ResultsFinalizedAt` đã có, nếu chưa trả 409
  "Moderator chưa xác nhận danh sách người nhận giải."; không tự gán lại người thắng. Phần chuyển `AwaitingClaim`,
  hạn nhận, thông báo giữ nguyên.

### 5. Hiển thị (frontend)

- `EventAwardSlotDto` thêm: `winnerName`, `winnerStoryTitle`, `manualAssignmentNote`, `isManualAssignment`.
- Bảng giải thưởng thêm cột **Người nhận** (tên + tên truyện; nhãn "Chỉnh tay" kèm lý do). Khi được chỉnh: nút **Sửa** mở
  `Dialog` chọn người từ danh sách ứng viên hoặc "Bỏ trống suất", ô lý do bắt buộc.
- Dòng trạng thái trên bảng: "Danh sách tạm · hạn khiếu nại đến …" → "Đã xác nhận, chờ Admin duyệt" → trạng thái nhận giải.
- Hàng nút đầu khung: "Đề xuất giải" đổi thành **Tính lại danh sách**; thêm **Xác nhận danh sách** (Moderator phụ trách);
  "Duyệt trao giải" (Admin) bị khoá kèm lời nhắc khi Moderator chưa xác nhận.

## Ràng buộc

- Migration mới tự áp khi API khởi động; DB local của người dùng còn 10 migration từ `main` chưa áp — sẽ áp cùng lúc.
- Endpoint mới dùng `[HasPermission]`; cập nhật snapshot ma trận phân quyền (`UPDATE_AUTHZ_SNAPSHOT=1`).
- Không in/ghi thông tin nhận giải nhạy cảm; tên người nhận chỉ trả cho người vận hành sự kiện.

## Để người làm tự quyết

- Tên lớp/dịch vụ cụ thể, cách tách phần lõi xác minh khỏi kiểm tra quyền.
- Bố cục chi tiết hộp thoại Sửa, miễn theo quy ước `Dialog` + toast của frontend.
