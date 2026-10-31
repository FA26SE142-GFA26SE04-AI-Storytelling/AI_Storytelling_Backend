# Bé ước — kịch bản kiểm thử API thật bằng Swagger

## 1. Phạm vi và cách chạy

Luồng chính: **trẻ đăng nhập → chọn thẻ → gửi điều ước → Parent xem điều ước → lấy form từ thẻ → xác nhận tạo truyện AI → outline → nội dung/học liệu → media → trẻ xem truyện**.

Mỗi TC dưới đây là một request thao tác bằng **Try it out → điền path/query/body → Execute** trên Swagger Core, thường tại `http://localhost:5259/index.html`.

Swagger không tự thay biến hoặc chuyển token giữa các TC. Các tên `WISH_ID`, `STORY_ID`, `REQUEST_ID`, `VERSION_ID`, `OUTLINE_NO` bên dưới là chỗ cần điền ID thật từ response, không phải chuỗi gửi nguyên vào API.

Chuẩn bị:

- Core và AI service chạy bản code mới; Core gọi được AI và provider được cấu hình hợp lệ.
- Worker outline, content, học liệu, media và vòng đời điều ước đang chạy.
- Database đã có seed thẻ Bé ước. Request đi xuống database/provider thật và có thể tiêu thụ quota AI.
- Parent demo `parent1@example.com`, dùng mật khẩu hiện hành của bạn. File này không chứa mật khẩu/token thật.
- Dùng **child 2** theo seed, thuộc Parent demo trên. Nếu database đã thay quan hệ, chọn một child Active mà Parent thực sự quản lý và thay mọi số `2` trong kịch bản.
- Giữ consent/policy hiện có. Nếu thiếu consent, hết quota hoặc sai quyền thì ghi nhận bước bị chặn, xử lý bằng luồng nghiệp vụ hợp lệ rồi chạy tiếp.

Giới hạn cần biết trước khi tạo điều ước mới:

- Mỗi trẻ gửi tối đa **1 điều ước/ngày theo UTC+7**.
- Tối đa **2 điều ước đang chờ** (`Pending` hoặc `Accepted`).
- Nháp có hạn **24 giờ**.
- Child 1 đã có wish seed 1 và 4; child 3 có wish seed 5 được tạo ngày chạy seed. Không dùng các wish seed này thay cho bước trẻ tự chọn thẻ của bài test chính.
- Nếu child 2 đã test hôm nay, chọn một hồ sơ test khác đủ điều kiện hoặc chờ ngày kế tiếp. Đóng điều ước không xóa giới hạn gửi trong ngày.

## 2. Các giá trị cần giữ trong lúc test

Chỉ giữ token trong phiên test cá nhân; không chép chúng vào source control hoặc báo cáo.

- `PARENT_TOKEN`: `data.accessToken` của TC01.
- `PARENT_REFRESH`: `data.refreshToken` của TC01.
- `CHILD_TOKEN`: `data.accessToken` của TC02.
- `WISH_ID`: `data.wishId` của TC04/TC10, phải cùng ID.
- `FORM_REQUEST`: nguyên đối tượng `data.request` của TC13, sau khi Parent xem và chỉnh.
- `STORY_ID`: `data.generation.storyId` của TC14.
- `REQUEST_ID`: `data.generation.requestId` của TC14.
- `OUTLINE_NO`: `data.currentVersion.versionNo` của TC17; đây là số phiên outline.
- `VERSION_ID`: ID phiên nội dung hiện tại; khác `OUTLINE_NO`.
- `PACKAGE_REVISION`: revision gói media từ TC20.

Khi đổi actor: mở **Authorize → Logout → dán token mới → Authorize → Close**. Cấu hình Swagger là HTTP Bearer: chỉ dán JWT, không thêm `Bearer` vào ô nhập.

Nếu token hết hạn trong lúc chờ AI (HTTP `401`), đăng nhập Parent lại, lấy token mới và Authorize. Nếu cần token trẻ mới, thực hiện handover với refresh token Parent hiện hành. Tiếp tục bằng các ID đã ghi; không tạo lại điều ước/truyện từ đầu.

## 3. Luồng chính — trẻ chọn thẻ và gửi điều ước

### TC01 — Đăng nhập Parent để bàn giao hồ sơ trẻ

```http
POST /api/v1/Auth/login
```

```json
{
  "identifier": "parent1@example.com",
  "password": "THAY_BANG_MAT_KHAU_HIEN_HANH"
}
```

Mong đợi: HTTP `200`, `success=true`, `data.user.role=User` (actor nghiệp vụ Parent). Giữ `PARENT_TOKEN` và `PARENT_REFRESH`; Authorize bằng `PARENT_TOKEN`.

### TC02 — Tạo Child Session cho child 2

Actor: **Parent**.

```http
POST /api/v1/ChildAccessCredential/2/handover
```

```json
{
  "supervisorRefreshToken": "THAY_BANG_PARENT_REFRESH_TU_TC01"
}
```

Mong đợi: `200`, `data.childProfileId=2`, có `data.accessToken`. Giữ token này làm `CHILD_TOKEN`.

### TC03 — Chuyển sang trẻ và xác minh đúng hồ sơ

Authorize bằng **CHILD_TOKEN**.

```http
GET /api/v1/ChildAccessCredential/me
```

Mong đợi: `200`, `data.childProfileId=2`. Tiếp tục gọi:

```http
GET /api/v1/child/wishes
```

Xem những điều ước hiện có để nhận biết giới hạn ngày/chờ. Không dùng token Parent gọi các API `child/*`.

### TC04 — Bắt đầu hoặc mở lại nháp điều ước

Actor: **trẻ**. Không có request body.

```http
POST /api/v1/child/wishes/draft
```

Mong đợi: `200`, lưu `data.wishId` thành `WISH_ID`.

- `resumed=false`: nháp mới.
- `resumed=true`: tiếp tục nháp còn hạn; xem các thẻ đang có, chọn lại theo TC06–TC09.
- Nếu chưa chọn đủ: `canSubmit=false`.

### TC05 — Lấy thẻ trẻ được phép nhìn thấy

Actor: **trẻ**. Chạy GET bốn lần, thay query `group`:

```http
GET /api/v1/child/wish-cards?group=Character
GET /api/v1/child/wish-cards?group=Place
GET /api/v1/child/wish-cards?group=Challenge
GET /api/v1/child/wish-cards?group=Lesson
```

Mong đợi: `200`, mỗi response là danh sách `data` có `id`, `group`, `label`, `imageUrl`, `audioUrl`.

Với seed và policy child 2 chưa thay đổi, bộ test dự kiến:

- Character: **26 — Mèo Mun**.
- Place: **8 — Ngoi nha cua be**.
- Challenge: **10 — Giup ban**.
- Lesson: **25 — Biết lắng nghe**.

**Chỉ dùng các ID trên nếu thực sự xuất hiện trong response của đúng nhóm.** Danh sách đã lọc theo tuổi, policy và trạng thái thẻ. Nếu một ID không xuất hiện, lấy ID khác trong response và đối chiếu form theo nhãn thực tế đã chọn. Không sửa policy chỉ để hiện một thẻ.

Ảnh seed là URL giữ chỗ; không tải được ảnh từ CDN mẫu không đồng nghĩa API chọn thẻ thất bại.

### TC06 — Chọn nhân vật

```http
PUT /api/v1/child/wishes/draft/cards
```

```json
{
  "group": "Character",
  "cardId": 26,
  "leaveToParent": false
}
```

Mong đợi: `200`, `data.wishId=WISH_ID`, `data.cards` có nhân vật đúng ID/nhãn. Chưa có nơi chốn thì `canSubmit=false`.

### TC07 — Chọn nơi chốn

Cùng endpoint PUT của TC06:

```json
{
  "group": "Place",
  "cardId": 8,
  "leaveToParent": false
}
```

Mong đợi: nhân vật vẫn được giữ, có thêm nơi chốn, `canSubmit=true`. Nhân vật và nơi chốn là hai lựa chọn bắt buộc.

### TC08 — Chọn thử thách

Cùng endpoint PUT:

```json
{
  "group": "Challenge",
  "cardId": 10,
  "leaveToParent": false
}
```

Mong đợi: có thêm thẻ thử thách, các lựa chọn cũ vẫn còn. Challenge là tùy chọn.

### TC09 — Chọn bài học

Cùng endpoint PUT:

```json
{
  "group": "Lesson",
  "cardId": 25,
  "leaveToParent": false
}
```

Mong đợi: `lessonLeftToParent=false`, có đủ bốn thẻ đã chọn, `canSubmit=true`.

Biến thể cho lần test khác: để Parent chọn bài học bằng body dưới đây. Khi đó TC13 sẽ trả `lesson` trống; Parent phải điền trước khi tạo truyện.

```json
{
  "group": "Lesson",
  "cardId": null,
  "leaveToParent": true
}
```

### TC10 — Trẻ gửi điều ước

Không có request body:

```http
POST /api/v1/child/wishes/draft/submit
```

Mong đợi: `200`, `data.wishId=WISH_ID`, `data.status=Pending`, thông báo bố mẹ sẽ xem điều ước. Các thẻ đúng với TC06–TC09.

**Chưa có truyện/outline do AI sinh ở bước này.** Không bấm submit lặp để tạo thêm wish.

### TC11 — Trẻ xem điều ước vừa gửi

```http
GET /api/v1/child/wishes
```

Mong đợi: tìm được `WISH_ID`, trạng thái `Pending`, `fulfilledStoryId=null`. Danh sách `suggestions` có thể rỗng hoặc có truyện phù hợp; không bắt buộc luôn có gợi ý.

## 4. Parent dùng chính lựa chọn của trẻ để tạo truyện

### TC12 — Parent mở điều ước

Đổi Authorize sang **PARENT_TOKEN**. Thay `WISH_ID` bằng số đã lưu:

```http
GET /api/v1/parent/wishes?childProfileId=2
GET /api/v1/parent/wishes/WISH_ID
```

Mong đợi ở detail: `data.wish.childProfileId=2`, `status=Pending`, đúng các thẻ trẻ vừa chọn. `generatedStoryId` và `generationRequestId` chưa có.

### TC13 — Lấy form sinh truyện từ điều ước

```http
GET /api/v1/parent/wishes/WISH_ID/generation-form
```

Mong đợi `200`. Kiểm tra **data.request**:

- `childProfileId=2`.
- `characters` chứa nhãn nhân vật đã chọn, `characterMode=specified`.
- `setting` là nhãn nơi chốn, `settingMode=specified`.
- `topic` tổng hợp nhân vật, nơi chốn và thử thách.
- `lesson` là bài học đã chọn; nếu để Parent chọn thì trống.
- `vocabularyLevel`, `language`, `targetLength` theo cấu hình của child.
- `outputMode=learning`; `idempotencyKey` tương ứng `WISH_ID`.

**Copy nguyên đối tượng `data.request`**, không copy wrapper `success/message/data`, không tự soạn một bộ chủ đề/nhân vật khác từ đầu. Đây là bằng chứng bước tạo truyện lấy đầu vào từ thẻ trẻ đã chọn.

Parent có thể chỉnh wording. Nếu lesson trống, điền ví dụ `Biết lắng nghe và giúp đỡ bạn bè`. Giữ `targetLength` do form trả về trong lần test đầu. Đối tượng sau khi chỉnh là `FORM_REQUEST`; giữ một bản để test replay TC16.

### TC14 — Parent xác nhận yêu cầu tạo truyện AI

```http
POST /api/v1/parent/wishes/WISH_ID/create-story
```

**Request body: dán nguyên FORM_REQUEST từ TC13.** Không lồng nó trong thuộc tính `request`.

- `existingStoryId` nếu có thì để `null`.
- Không truyền `sourceWishId`; đây là trường nội bộ.
- Không thay `childProfileId` sang child khác.

Mong đợi `200`:

- `data.wish.status=Accepted`.
- Giữ `data.generation.storyId` làm `STORY_ID`.
- Giữ `data.generation.requestId` làm `REQUEST_ID`.
- `data.wish.generatedStoryId=STORY_ID`.
- `data.wish.generationRequestId=REQUEST_ID`.

Nếu `data.generation.inputStatus=input_check_failed`, request đã được lưu nhưng kiểm tra đầu vào thất bại; xem TC15. HTTP 200 ở đây chưa chứng minh provider đã xử lý thành công.

### TC15 — Theo dõi Phase 1 và xử lý lỗi

```http
GET /api/v1/parent/wishes/WISH_ID/generation
```

Đối chiếu `storyId/requestId` với TC14. Khi thành công: `inputStatus=input_accepted`, có thông tin handoff outline. Endpoint này chỉ là tiến độ Phase 1.

Nếu `input_check_failed` và `canRetry=true`, dùng:

```http
POST /api/v1/ai-story-input/stories/STORY_ID/requests/REQUEST_ID/retry
```

Body: copy các trường creative input từ `FORM_REQUEST` (`topic`, `genre` nếu có, `characterMode`, `characters`, `settingMode`, `setting`, `lesson`, `vocabularyLevel`, `language`, `targetLength`), bỏ các trường envelope `childProfileId`, `existingStoryId`, `idempotencyKey`, `outputMode`, thêm thuộc tính `"retryKey": "swagger-be-uoc-input-retry-001"` vào cùng đối tượng JSON.

Không Execute chỉ một trường `retryKey`. Đổi key khi tạo lần retry mới. Nếu `canRetry=false` hoặc có lỗi consent/policy thì dừng, ghi `reasonCode`/message, không lặp mù.

### TC16 — Kiểm tra gửi lặp không tạo trùng

Gọi lại POST `create-story` ở TC14 với **đúng FORM_REQUEST cũ**.

Mong đợi: cùng `STORY_ID/REQUEST_ID`, không có truyện/request mới và không trừ thêm lượt. API này trả request đã có, không phải thao tác retry guardrail.

## 5. Tiếp tục pipeline chung đến khi truyện đọc được

### TC17 — Xem và xác nhận outline

Actor: **Parent**.

```http
GET /api/v1/stories/STORY_ID/outline
```

Gọi lại sau khoảng 5 giây nếu job còn Pending/Processing. Khi có `data.currentVersion`, đọc outline, giữ `versionNo` làm `OUTLINE_NO`.

```http
POST /api/v1/stories/STORY_ID/outline/versions/OUTLINE_NO/approve
```

```json
{
  "approvalKey": "swagger-be-uoc-outline-approve-001"
}
```

Mong đợi `202`, xếp hàng sinh nội dung. Không dùng versionId của bản nội dung thay cho `OUTLINE_NO`.

### TC18 — Theo dõi nội dung và học liệu

```http
GET /api/v1/stories/STORY_ID/generation/progress
GET /api/v1/stories/STORY_ID/review
```

Đợi các job liên quan hoàn tất trước khi kết luận thiếu dữ liệu. Đọc nội dung qua:

```http
GET /api/v1/stories/STORY_ID/review/story
GET /api/v1/stories/STORY_ID/review/vocabulary
GET /api/v1/stories/STORY_ID/review/quiz
GET /api/v1/stories/STORY_ID/review/discussion
```

Mong đợi: truyện phù hợp với lựa chọn thẻ và ràng buộc Parent đã xác nhận. Có vocabulary, quiz và discussion theo outputMode learning; nội dung học liệu bám vào truyện.

Nếu job báo lỗi, ghi `errorCode`, trạng thái và ID job. Không gọi tạo lại tất cả học liệu khi các job cũ vẫn chạy.

### TC19 — Parent xác nhận nội dung và xếp hàng media

Sau khi đã đọc từng phần, lần lượt gọi bốn POST không có body:

```http
POST /api/v1/stories/STORY_ID/review/story/complete
POST /api/v1/stories/STORY_ID/review/vocabulary/complete
POST /api/v1/stories/STORY_ID/review/quiz/complete
POST /api/v1/stories/STORY_ID/review/discussion/complete
```

Kiểm tra:

```http
GET /api/v1/stories/STORY_ID/review/validation
```

Chỉ tiếp tục khi `data.canApprove=true`. Nếu false, đọc `checks/issues` và giải quyết đúng điều kiện bị thiếu.

```http
POST /api/v1/stories/STORY_ID/review/approve
```

Mong đợi thành công và pipeline media bắt đầu. Đây là xác nhận truyện riêng của Parent, không phải duyệt công khai thư viện.

### TC20 — Xem trước và xác nhận media

```http
GET /api/v1/stories/STORY_ID/media/progress
GET /api/v1/stories/STORY_ID/media/package
```

Đợi `isGenerated=true`, `isAwaitingConfirmation=true`. Xem/nghe các asset trong package. Giữ `data.storyVersionId` và `data.packageRevision` từ **package hiện tại**.

```http
POST /api/v1/stories/STORY_ID/media/confirm
```

Body ví dụ về cấu trúc — **thay cả hai số bằng response vừa lấy**, không mặc định phiên là 38:

```json
{
  "storyVersionId": 38,
  "packageRevision": 1
}
```

Mong đợi `200`, gói được xác nhận và đạt readiness. Nếu gói còn thiếu hoặc version/revision đã đổi thì `409`; lấy lại package để đối chiếu.

### TC21 — Điều ước hoàn tất và trẻ mở truyện

Vẫn token Parent:

```http
GET /api/v1/parent/wishes/WISH_ID
```

Sau khi truyện Ready và lượt worker sweep kế tiếp chạy: `data.wish.status=Fulfilled`, `fulfilledStoryId=STORY_ID`. Không sửa database để ép Fulfilled nếu job chưa hoàn thành.

Đổi sang **CHILD_TOKEN** của child 2:

```http
GET /api/v1/child/wishes
```

Mong đợi: đúng wish, `status=Fulfilled`, `fulfilledStoryId=STORY_ID`, thông báo điều ước thành truyện. Việc hiển thị ID không bỏ qua quyền đọc/policy.

Mở truyện qua:

```http
POST /api/v1/child/reading/sessions
```

Body ví dụ — thay `100` bằng `STORY_ID` thật:

```json
{
  "storyId": 100,
  "communitySubmissionId": null
}
```

Giữ `data.sessionId`, gọi:

```http
GET /api/v1/child/reading/sessions/SESSION_ID/content
```

Mong đợi đúng truyện và media đã xác nhận; nếu access/policy còn chặn thì ghi rõ lý do, không đánh dấu pass chỉ vì wish đã Fulfilled.

## 6. Các ca kiểm tra lỗi sau luồng chính

Chỉ dùng dữ liệu test, không thay consent/quota hoặc trạng thái trực tiếp trong database.

1. Token Parent gọi `GET /child/wish-cards`: bị chặn do không phải Child Session.
2. Token trẻ gọi `GET /parent/wishes/WISH_ID`: bị chặn do sai actor.
3. Token Parent không có quyền child gọi detail/form: bị chặn; không lộ dữ liệu trẻ khác.
4. Chọn card không có trong danh sách đã lọc: bị từ chối, giữ lựa chọn hợp lệ cũ.
5. Gọi submit khi thiếu Character hoặc Place trên một nháp test khác: bị từ chối.
6. Tạo điều ước nữa trong cùng ngày hoặc khi đủ hai điều ước chờ: `409` theo giới hạn nghiệp vụ.
7. Đổi `childProfileId` của body create-story khác child của wish: bị từ chối trước khi tạo truyện.
8. Gửi create-story lại với cùng input: cùng ID. Sau đó đổi topic nhưng giữ cùng wish đã nhận: `409`, không tạo truyện thứ hai.
9. Trước khi Ready, trẻ thử mở STORY_ID qua reading sessions: không đọc được nháp/gói chưa xác nhận.
10. Confirm media sai version/revision hoặc khi job chưa xong: `409`, không tự chuyển Ready.

Nhánh dùng truyện có sẵn và từ chối cần một **wish Pending khác**, không chạy vào wish đã Accepted/Fulfilled của luồng chính:

```http
GET /api/v1/parent/wishes/WISH_ID_KHAC/suggestions
POST /api/v1/parent/wishes/WISH_ID_KHAC/use-existing
```

```json
{
  "suggestionRank": 1
}
```

Lấy rank thực tế từ `data.items`; rỗng thì ghi không có gợi ý phù hợp. Hoặc dùng wish khác để từ chối:

```http
POST /api/v1/parent/wishes/WISH_ID_KHAC/decline
```

```json
{
  "reason": "Bố mẹ sẽ chọn một câu chuyện phù hợp hơn."
}
```

## 7. Mẫu ghi kết quả

Ghi lại cho từng TC: thời điểm, actor/child ID, endpoint, HTTP status, ID dữ liệu, kết quả thực tế, Pass/Fail/Blocked và errorCode nếu có. Không lưu accessToken/refreshToken vào báo cáo hoặc ảnh chụp.

Ví dụ bản ghi không chứa bí mật:

```json
{
  "testCase": "TC14",
  "actor": "Parent",
  "childProfileId": 2,
  "wishId": 6,
  "storyId": 34,
  "requestId": 12,
  "httpStatus": 200,
  "wishStatus": "Accepted",
  "inputStatus": "input_accepted",
  "result": "Pass"
}
```

Các ID trên chỉ minh họa. File này là kịch bản hướng dẫn được đối chiếu với controller/DTO/service; không phải báo cáo đã chạy Swagger/provider thật.

Luồng AI gắn thẻ truyện có sẵn cho Moderator là tác vụ riêng, xem [WISH_CARD_AI_MATCHING_API_GUIDE.md](WISH_CARD_AI_MATCHING_API_GUIDE.md). Nó không phải điều kiện để Parent tạo truyện mới từ điều ước của trẻ.
