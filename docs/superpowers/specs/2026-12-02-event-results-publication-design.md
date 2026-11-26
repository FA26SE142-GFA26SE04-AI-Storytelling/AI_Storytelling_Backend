# Công bố kết quả sự kiện, khiếu nại sau công bố và pop-up kết quả

Ngày: 02/12/2026 · Phạm vi: backend + frontend · Chỉ áp dụng cho sự kiện theo hoạt động (`ConfigVersion = ActivityBased`).
Thay thế một phần [2026-12-02-event-auto-award-list-design.md](2026-12-02-event-auto-award-list-design.md):
bước "Moderator chỉ xác nhận khi hết hạn khiếu nại" bị bỏ; phần tự lập danh sách tạm và chỉnh tay giữ nguyên.

## Mục đích

Người dùng muốn kết quả được **công bố sớm** ngay khi danh sách được duyệt, rồi mới xử lý khiếu nại; nếu khiếu nại đúng thì
**thu hồi** giải và trao cho người khác. Phụ huynh và nhà sáng tạo phải **thấy ngay kết quả của mình** qua pop-up khi mở/đăng
nhập nền tảng, và có **trang kết quả** sau khi sự kiện kết thúc cho biết giải nằm ở đâu trong nền tảng.

## Mục tiêu / Không làm / Điều phải tránh

- Mục tiêu:
  - Luồng: kết thúc → danh sách tạm (tự động) → Moderator chỉnh & xác nhận (không chờ) → Admin duyệt = **công bố**
    → 48 giờ khiếu nại → hết hạn thì mở nhận thưởng 72 giờ.
  - Trong 48 giờ khiếu nại, người thắng **chưa nhận được thưởng** (phương án A người dùng chọn).
  - Khiếu nại mở cho mọi người tham gia hoạt động có giải mà **không nhận giải**, hai loại: kết quả xác minh / thứ hạng – người nhận giải.
  - Moderator thu hồi suất (bắt buộc lý do) rồi gán người thay thế; có dấu vết đầy đủ.
  - Pop-up kết quả cho **mọi người tham gia** (cả người không có giải), lưu "đã xem" ở backend.
  - Trang kết quả cho sự kiện đã kết thúc: kết quả của tôi, danh sách giải công bố, tổng kết.
- Không làm: sự kiện kiểu cũ (`LegacyRoleBased`); thu hồi sau khi đã trao quà (phương án A làm việc này không xảy ra);
  khiếu nại của trẻ (chỉ phụ huynh/nhà sáng tạo).
- Phải tránh: trao quà rồi mới thu hồi; công khai tên bé khi phụ huynh chưa đồng ý; pop-up hiện lại mãi trên máy khác;
  người giữ giải tự khiếu nại; thu hồi không để lại lý do.

## Cách làm đã chọn (Approaches)

Người dùng chọn **cách 1**: thêm trạng thái suất giải `AppealHold` và `Revoked`, thời điểm công bố trên sự kiện, pop-up dùng
hệ thống thông báo sẵn có kèm API kết quả.

Đã cân nhắc và không chọn:
- Cách 2: giữ `AwaitingClaim`, chỉ khoá nhận thưởng theo giờ — khó hiển thị đúng "đang chờ khiếu nại", hạn nhận phải tính lùi,
  thu hồi chỉ là xoá người nên mất dấu vết.
- Cách 3: pop-up chỉ ở frontend, lưu "đã xem" trong trình duyệt — trái quyết định lưu ở backend.

## Thiết kế

### 1. Trạng thái và dữ liệu (cần migration)

- `EventAwardStatus` thêm `AppealHold` (đã công bố, chờ hết hạn khiếu nại) và `Revoked` (bị thu hồi). Giá trị số mới nối tiếp, không đổi số cũ.
- `Event` thêm `ResultsPublishedAt` (thời điểm Admin duyệt). Hạn khiếu nại = `ResultsPublishedAt + 48 giờ` (`EventAppealWindow.Hours`).
- `EventAward` thêm `RevokedReason` (≤ 500), `RevokedByUserId`, `RevokedAt`.
- `EventAppeal` thêm `Kind` (`Verification` | `Ranking`) và `TargetAwardId` (tuỳ chọn, suất bị khiếu nại).
- `EventAward` (hoặc bản ghi nhận thưởng) thêm `ShowChildNames` (bool, mặc định false) cho giải hoàn thành nhiệm vụ của phụ huynh.
- `NotificationType` thêm `EventEnded`, `EventResultPublished`, `EventAwardRevoked` và `EventAppealAccepted` (số mới nối tiếp).
  `EventAwardApproved` giữ cho tương thích.
- `Event` thêm `EndedNotifiedAt` để thông báo kết thúc chỉ gửi một lần.

### 2. Luồng

1. Hết thời gian sự kiện → worker lập danh sách tạm (đã có) và **gửi ngay thông báo `EventEnded`** cho mọi người tham gia có hồ sơ
   hoạt động (một lần, theo `EndedNotifiedAt`). Mục đích: giữ hứng thú trong lúc chờ kết quả. Nội dung: cảm ơn đã tham gia,
   tóm tắt việc **của chính họ** (số truyện bé đọc xong, số bài nộp, số phiếu đã bình chọn, nhiệm vụ hoàn thành), và
   "Kết quả sẽ được công bố sớm — bạn sẽ nhận thông báo ngay khi có". Không tiết lộ thứ hạng hay danh sách tạm.
2. Moderator chỉnh/xác nhận: **bỏ** điều kiện hết hạn khiếu nại trong `ConfirmAwardsAsync`; vẫn cần đã xác minh xong, chấm đủ bài.
3. Admin duyệt (`ApproveAwardsAsync`, sự kiện theo hoạt động): suất có người → `AppealHold` (không đặt `ClaimDeadlineAt`);
   đặt `Event.ResultsPublishedAt`; gửi `EventResultPublished` cho **mọi** người tham gia có hồ sơ hoạt động (thắng hoặc không).
4. Trong 48 giờ: nhận khiếu nại (mục 3). Nhận thưởng bị từ chối với suất `AppealHold`
   ("Giải sẽ mở nhận từ … sau khi hết hạn khiếu nại").
5. Worker (`EventResultSweepService`) thêm bước: sự kiện đã công bố, quá hạn khiếu nại, **không còn đơn mở** →
   mọi suất `AppealHold` → `AwaitingClaim`, `ClaimDeadlineAt = now + ClaimWindowHours` của hoạt động; gửi thông báo mở nhận thưởng
   (dùng `EventAwardApproved`). Còn đơn mở thì chờ, Moderator xử lý xong mới mở.
6. Phần nhận thưởng, đôn suất quá hạn, trao quà, đóng sự kiện giữ nguyên; `Revoked` được tính như suất đã kết thúc khi đóng sự kiện.

### 3. Khiếu nại sau công bố

- Chỉ nhận khi `ResultsPublishedAt` có và chưa quá 48 giờ. **Không** nhận khiếu nại trước khi công bố (một khung duy nhất);
  khung 48 giờ tính từ lúc xác minh cũ bỏ cho sự kiện theo hoạt động.
- Ai được gửi: chủ hồ sơ tham gia hoạt động có giải (hoạt động nằm trong nguồn xếp hạng của ít nhất một suất), **không** đang giữ suất
  `AppealHold`/`AwaitingClaim` nào của sự kiện. Một đơn mở mỗi hồ sơ (giữ quy tắc cũ). Giữ BR-AP07 (bài bị từ chối F-10 không khiếu nại).
- Loại `Verification`: hồ sơ phải `VerificationFailed`. Chấp nhận → hồ sơ `Verified` (như hiện nay), **không** tự tính lại vì danh sách đã chốt;
  Moderator quyết định có thu hồi suất nào không.
- Loại `Ranking`: hồ sơ `Verified`; `TargetAwardId` tuỳ chọn. Chấp nhận/bác chỉ đổi trạng thái đơn + ghi kết luận.
- Khi đơn được **chấp nhận**: gửi `EventAppealAccepted` cho người khiếu nại (kèm kết luận của Moderator). Nếu sau đó họ được gán vào
  suất thay thế thì nhận thêm `EventResultPublished` như người thắng mới.
- Thu hồi: API mới `POST /api/v1/events/awards/{awardId}/revoke` `{ reason, replacementParticipationId?: int|null }`.
  - Quyền `event.manage` + Moderator phụ trách chính; suất phải `AppealHold` và trong hạn khiếu nại (hoặc còn đơn mở).
  - Suất cũ → `Revoked` + lý do/người/thời điểm; tạo suất mới cùng hạng/phần thưởng (như đôn suất quá hạn, `ReallocatedFromAwardId`)
    ở `AppealHold` gán người thay thế (kiểm tra điều kiện như chỉnh tay: đã xác minh, đúng hoạt động/đối tượng, không giữ suất cùng nguồn),
    hoặc để trống nếu `null`.
  - Gửi `EventAwardRevoked` cho người bị thu hồi (kèm lý do), `EventResultPublished` cho người thay thế. Ghi audit.

### 4. API kết quả cho người tham gia

- `GET /api/v1/events/my-results` (quyền `event.participate`): mỗi sự kiện đã công bố mà người dùng có hồ sơ hoạt động:
  tên sự kiện, `resultsPublishedAt`, `appealDeadlineAt`, kết quả từng hoạt động (đạt/không đạt + lý do ngắn từ
  `VerificationNotesJson`), các suất của mình (hạng, phần thưởng, trạng thái, `claimOpensAt` = hạn khiếu nại, `claimDeadlineAt`,
  lý do thu hồi nếu `Revoked`), có thể khiếu nại hay không.
- `GET /api/v1/events/{eventId}/results` (quyền `event.participate`, chỉ khi đã công bố): danh sách giải công bố gồm hạng, phần thưởng,
  và người nhận theo quy tắc hiển thị (mục 6), không trả suất `Revoked`; kèm tổng kết (số người tham gia, số truyện, số giải).
- Pop-up đọc thông báo chưa đọc loại `EventResultPublished` / `EventAwardRevoked`, gọi `my-results` để dựng nội dung;
  bấm "Đã hiểu" hoặc "Xem chi tiết" thì đánh dấu thông báo đã đọc (API thông báo sẵn có).

### 5. Giao diện (frontend)

- **Pop-up kết quả** trong khung ứng dụng phụ huynh và nhà sáng tạo, hiện khi mở/đăng nhập nếu có thông báo sự kiện chưa đọc
  (`EventEnded`, `EventResultPublished`, `EventAwardRevoked`); nhiều thông báo thì hiện lần lượt. Dùng `components/Dialog`,
  có linh vật ếch và hiệu ứng nhẹ (tôn trọng `prefersReducedMotion`). Nội dung theo trường hợp:
  - Sự kiện vừa kết thúc: "Sự kiện <tên> đã khép lại!", tóm tắt đóng góp của họ, "Kết quả sắp được công bố"; nút **Xem sự kiện** / **Đã hiểu**.
  - Đạt giải: tên giải, phần thưởng, trạng thái hiện tại, kèm khối **Lưu ý** ngay trong pop-up:
    - đang chờ khiếu nại: "Giải mở nhận từ <hạn khiếu nại>; kết quả có thể thay đổi nếu có khiếu nại được chấp nhận";
    - đã mở nhận: "Hãy nhận trước <hạn nhận>; quá hạn suất sẽ chuyển cho người kế tiếp";
    - quà hiện vật/tiền: cần chuẩn bị gì (địa chỉ, số điện thoại / tài khoản ngân hàng); giải phụ huynh: lựa chọn hiện tên bé;
    - nơi giải sẽ nằm sau khi nhận (lượt AI → tài khoản, gói Creator → gói, quà → giao tận nơi).
    Nút **Nhận thưởng** xuất hiện ngay trong pop-up khi đã mở nhận.
  - Không có giải: "Bạn chưa nằm trong danh sách nhận giải" + lý do ngắn + hạn khiếu nại.
  - Bị thu hồi: pop-up riêng, lý do thu hồi.
  - **Khiếu nại được chấp nhận**: pop-up riêng cho người khiếu nại, "Khiếu nại của bạn đã được chấp nhận" + kết luận của Moderator
    + kết quả mới (được gán giải gì, hoặc kết quả xác minh đã được cập nhật).
  - Nút **Xem chi tiết** → trang kết quả của sự kiện, cuộn tới thẻ của người dùng; **Đã hiểu** → đóng.
- **Nhắc lại đến khi nhận**: pop-up giải thưởng **không chỉ hiện một lần**. Chừng nào người dùng còn suất `AppealHold` hoặc
  `AwaitingClaim` chưa nhận, pop-up hiện lại **mỗi lần vào nền tảng**, gồm cả hai trường hợp:
  (a) vừa đăng nhập; (b) mở lại nền tảng khi phiên đăng nhập còn được giữ (tab/trình duyệt mới, không cần đăng nhập lại).
  Trong cùng một lần vào (chuyển màn, tải lại trang trong cùng tab) không hiện lặp — ghi nhớ trong `sessionStorage`, xoá khi đăng xuất. Nguồn sự thật là `my-results` (trạng thái suất), không phải thông báo đã đọc; dừng nhắc khi
  suất đã nhận, hết hạn hoặc bị thu hồi. Pop-up kết thúc sự kiện, thu hồi, khiếu nại được chấp nhận vẫn chỉ hiện một lần (theo thông báo chưa đọc).
- Thứ tự khi có nhiều pop-up: thu hồi / khiếu nại được chấp nhận → kết quả & giải chưa nhận → sự kiện vừa kết thúc.
- **Trang kết quả** (trang chi tiết sự kiện khi sự kiện đã kết thúc, cho phụ huynh và nhà sáng tạo):
  - Trên cùng **Kết quả của bạn**: trạng thái từng hoạt động, các suất giải, trạng thái nhận, hạn khiếu nại, nút **Khiếu nại**
    (chọn loại, lý do), nút **Nhận thưởng** khi đã mở, và chỗ giải nằm trong nền tảng:
    lượt AI → trang tài khoản nơi hiện số lượt; gói Creator → trang tài khoản/gói; quà hiện vật/tiền → điền thông tin ngay trên trang.
  - **Danh sách giải đã công bố** theo hạng, và tổng kết sự kiện.
  - Thẻ hoạt động cũ thu gọn lại cuối trang.
  - Trước khi công bố: "Ban tổ chức đang xét kết quả", kèm tóm tắt đóng góp của họ (như thông báo kết thúc).
- **Nhận thưởng** giải hoàn thành nhiệm vụ của phụ huynh có ô tích "Cho phép hiện tên bé trong danh sách giải" (mặc định không tích).
- Moderator: bảng giải hiện trạng thái `Chờ hết hạn khiếu nại` / `Đã thu hồi` (kèm lý do); nút **Thu hồi** trên suất `AppealHold`
  mở hộp thoại chọn người thay thế (dùng API ứng viên đã có) + lý do; đơn khiếu nại hiện loại và suất bị khiếu nại.

### 6. Quy tắc hiển thị danh sách giải công bố

- Giải theo bài dự thi / bình chọn: tên truyện + tên hiển thị của tác giả.
- Giải hoàn thành nhiệm vụ (phụ huynh): tên hiển thị phụ huynh; tên gọi (nickname) các bé **đã tham gia** chỉ khi phụ huynh tích đồng ý,
  nếu không thì "cùng N bé". Không hiện ảnh hay tuổi của bé.
- "Bé đã tham gia" = bé có lượt đọc hoàn thành tính cho nhiệm vụ của sự kiện.

## Ràng buộc

- Migration mới tự áp khi API khởi động (DB local còn các migration chưa áp).
- Endpoint mới dùng `[HasPermission]`, cập nhật snapshot phân quyền và `docs/core-api-coverage.json` của frontend.
- Thông tin nhận quà vẫn mã hoá như hiện nay; không đưa vào thông báo hay pop-up.

## Quyết định của người thiết kế (để người dùng kiểm tra)

- Hạn 72 giờ nhận thưởng tính từ lúc **hết hạn khiếu nại**, không từ lúc công bố.
- Chỉ một khung khiếu nại (sau công bố); không nhận khiếu nại ở giai đoạn danh sách tạm.
- Chấp nhận khiếu nại không tự tính lại danh sách; Moderator tự quyết thu hồi/gán lại.
- Mở nhận thưởng cho cả sự kiện cùng lúc, chỉ khi không còn đơn mở.
- Thông báo kết thúc chỉ tóm tắt đóng góp cá nhân, không hé lộ danh sách tạm (tránh kỳ vọng sai khi danh sách còn chỉnh).
- Pop-up giải chưa nhận nhắc tối đa một lần mỗi phiên đăng nhập/mở ứng dụng, không hiện lại mỗi lần chuyển màn.

## Để người làm tự quyết

- Tên lớp, cách tách DTO, vị trí chính xác của pop-up trong `ParentShell` / khung nhà sáng tạo.
- Đích "Xem chi tiết" chính xác cho lượt AI/gói Creator theo màn tài khoản hiện có.
