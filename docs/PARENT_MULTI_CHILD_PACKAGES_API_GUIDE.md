# Parent Manual: một truyện, nhiều bé, gói học liệu riêng

Backend implementation: 03/11/2026. Không có frontend trong thay đổi này.

**Điều kiện chạy:** migration `20261103113109_AddParentStoryPackages` đã được tạo và áp dụng vào PostgreSQL local `localhost:5432/AIStorytellingDB` ngày 03/11/2026. Môi trường khác vẫn phải review/apply migration tăng dần trước khi chạy Core mới. Cập nhật schema không thay thế kiểm thử API với PostgreSQL/provider thật; chưa có xác nhận hoàn tất luồng live.

## 1. Phân biệt các định danh

- `storyId`: tác phẩm Parent sở hữu; không nhân đôi theo bé.
- `versionId` / `baseVersionId`: phiên bản nội dung dùng chung.
- `revisionToken`: token của bản nháp; lấy từ `data.story.revisionToken` sau save/setup.
- `packageId`: gói cho một bé trên một version; có snapshot profile/policy riêng.
- `packageToken`: token mới nhất từ GET package. Worker có thể đổi token, vì vậy đọc lại trước confirm/retry/revise.
- `confirmationId`: quyền giao đã được Parent xác nhận cho đúng bé; không phải version ID hoặc package ID.
- `operationId`: khóa replay. Gửi lại đúng request dùng lại cùng key; một thao tác mới dùng key mới. Không lấy token cũ và đổi operationId để bỏ qua 409.

Role Parent hiện tại là `User`. Creator/Moderator không sử dụng các endpoint authoring này.

## 2. Tạo bản nháp và chốt nội dung

`POST /api/v1/parent/stories/manual`

```json
{
  "operationId": "family-001-create",
  "title": "Chiếc hộp của Mi",
  "language": "vi",
  "ageBand": "6-8",
  "purpose": "family_memory",
  "experienceMode": "reading"
}
```

Không truyền `childProfileId` nếu chọn bé sau khi viết. Request cũ có `childProfileId` vẫn được chấp nhận. Nhóm tuổi trong draft là dự kiến; mỗi bé vẫn được kiểm tra theo profile thật sau đó.

Các API save/import/history/restore giữ nguyên. Ví dụ lưu V1:

`POST /api/v1/parent/stories/{storyId}/manual/versions`

```json
{
  "operationId": "family-001-save-v1",
  "revisionToken": "COPY_CURRENT_DRAFT_TOKEN",
  "baseVersionId": null,
  "title": "Chiếc hộp của Mi",
  "content": "Mi tìm thấy một chiếc hộp giấy bị rách. Mẹ cùng Mi dán lại góc hộp.\n\nMi xếp những tấm thiệp vào hộp. Hai mẹ con vui vẻ ngắm chiếc hộp đã được sửa.",
  "sourceDeclaration": "Tự sáng tác",
  "rightsDeclaration": "Tôi có quyền sử dụng nội dung này"
}
```

Sau save, dùng đúng version ID và revision token vừa nhận:

`POST /api/v1/parent/stories/{storyId}/content/finalize`

```json
{
  "operationId": "family-001-finalize",
  "revisionToken": "COPY_TOKEN_AFTER_SAVE",
  "baseVersionId": 20
}
```

Số 20 chỉ là ví dụ; thay bằng version ID thật. Finalize không tạo thêm version, không giao cho trẻ và không công khai truyện. Save/import-confirm/restore/setup bị khóa cho đến khi reopen.

## 3. Chọn bé và thành phần gói

`POST /api/v1/parent/stories/{storyId}/packages` trả **202**:

```json
{
  "operationId": "family-001-packages",
  "revisionToken": "COPY_TOKEN_AFTER_SAVE",
  "baseVersionId": 20,
  "recipients": [
    {
      "childProfileId": 1,
      "components": { "vocabulary": true, "quiz": true, "listening": false, "images": true, "narration": true }
    },
    {
      "childProfileId": 2,
      "components": { "vocabulary": true, "quiz": false, "listening": false, "images": true, "narration": true }
    }
  ]
}
```

Thay 1 và 2 bằng các hồ sơ Parent thực sự quản lý. Tất cả component có thể false để tạo gói chỉ đọc. `listening` là hoạt động tương tác khi nghe; `narration` là TTS kể truyện. Listening cần audio/ảnh tương tác tương ứng và audio đoạn mẫu cho RolePlay, kể cả khi không chọn kể toàn truyện.

202 chỉ xác nhận đã queue kiểm tra. **Gói chỉ đọc vẫn phải qua kiểm tra suitability/Safety AI và vùng xám**, nhưng không sinh vocabulary/quiz/listening/ảnh/TTS nếu không chọn. Cần consent hợp lệ khi gọi AI; không bypass khi thiếu consent hoặc rubric/provider chưa cấu hình.

Poll:

- `GET /api/v1/parent/stories/{storyId}/packages`
- `GET /api/v1/parent/stories/{storyId}/packages/{packageId}`

Theo dõi `status`, `check.passed`, `check.errors`, `check.safetyScore`, `check.evidence`, `check.reasonCode`, `componentStatus`, các count và `lastErrorCode`.

Safety dùng thang thứ bậc `none/low/moderate/high/critical` → `100/90/70/40/0`, không phải xác suất an toàn. Mỗi bé được so với threshold riêng. Thiếu điểm hoặc bằng chứng không hợp lệ không được coi là đạt; không lấy trung bình điểm hai bé. Vùng xám tiếp tục có queue/claim/decision của Moderator, gắn riêng với package; Clear không bỏ qua safety/readability đã trượt.

`Review` sau bước check chỉ có nghĩa đã có thể chuẩn bị gói; gói có component cần sinh chưa được confirm ngay. Gói không chọn component có thể preview/confirm sau check đạt.

## 4. Sinh học liệu/media và xem trước

Khi check đã đạt:

`POST /api/v1/parent/stories/{storyId}/packages/{packageId}/generate`

```json
{ "operationId": "family-001-generate-a", "packageToken": "COPY_LATEST_PACKAGE_TOKEN" }
```

Trả 202; tiếp tục poll GET package. Chỉ sinh các phần đã chọn. Học liệu có evidence trong chính content; số lượng theo cấu hình/profile và khả năng nguồn, không bịa thêm để đạt target. Worker dùng batch/lease/gate/retry hiện có; không tạo outline hoặc request generation giả.

Ảnh và TTS kể cùng version có thể dùng lại. Ảnh có sẵn được đọc và đánh giá lại cho ngữ cảnh bé mới. Media tương tác được cách ly theo package. Gói được xác nhận ghim các ID asset; regenerate không đổi file đang giao.

`GET /api/v1/parent/stories/{storyId}/packages/{packageId}/preview`

Response có content, manifest, vocabulary/quiz/listening và endpoint preview media. **Parent preview có đáp án**; không chuyển response này trực tiếp cho giao diện trẻ. Asset được đọc qua endpoint kiểm tra quyền, không trả storage key/public URL:

`GET /api/v1/parent/stories/{storyId}/packages/{packageId}/assets/{assetId}`

Frontend gửi Bearer token khi tải file, dùng response binary/blob; không đặt URL bảo vệ vào thẻ ảnh/audio mà bỏ Authorization. Endpoint hỗ trợ range và không cache response.

Khi mọi thành phần đã đạt, đọc packageToken mới nhất rồi gọi:

`POST /api/v1/parent/stories/{storyId}/packages/{packageId}/confirm`

```json
{ "operationId": "family-001-confirm-a", "packageToken": "COPY_LATEST_PACKAGE_TOKEN" }
```

Confirm ghim manifest và trả token của gói đã xác nhận. Không tạo version truyện hoặc grant tại bước này.

## 5. Giao hai bé trong một transaction

`POST /api/v1/parent/stories/{storyId}/family-confirmations/batch`

```json
{
  "operationId": "family-001-deliver",
  "packages": [
    { "packageId": 10, "packageToken": "COPY_CONFIRMED_PACKAGE_A_TOKEN" },
    { "packageId": 11, "packageToken": "COPY_CONFIRMED_PACKAGE_B_TOKEN" }
  ]
}
```

Thay các ID minh họa bằng ID thật. Tất cả package phải thuộc cùng story/version, mỗi bé một gói, còn quyền quản lý, profile/policy khớp và đủ media/học liệu.

- Thành công: 201, trả danh sách confirmation cho từng bé.
- Một gói không đạt: 409 `PARENT_BATCH_NOT_READY`, nêu bé/lý do; không có grant mới cho bé nào.
- Replay đúng request/key: trả các confirmation cũ, không tạo bản ghi mới.
- Bé đã có grant hiệu lực cho story: yêu cầu thu hồi grant cũ trước khi giao revision khác; không âm thầm thay thế.

Trẻ dùng token/session của chính mình:

1. `GET /api/v1/child/manual-stories`: lấy `confirmationId`, `parentStoryPackageId`.
2. `POST /api/v1/child/reading/sessions` với `{ "familyConfirmationId": 123 }`. Không gửi kèm `storyId` hoặc `communitySubmissionId`.
3. `GET /api/v1/child/reading/sessions/{sessionId}/content`; quiz/listening dùng API child hiện hữu trên session đó.

Session ghim grant và package. Backend không trả quiz/listening của package bé khác; các API nộp đáp án cũng kiểm tra scope. Child asset endpoint `/api/v1/child/manual-stories/{confirmationId}/assets/{assetId}` kiểm tra lại grant, profile và manifest, kể cả sau khi đọc file từ storage.

## 6. Sửa, retry và thu hồi

- Provider/validation lỗi: `POST /packages/{id}/retry`, body gồm `operationId`, `packageToken`, `components`. Các tên hợp lệ: `check`, `learning`, `vocabulary`, `quiz`, `listening`, `media`, `images`, `narration`. Mảng rỗng chọn phần thất bại. Phần đã đạt được giữ lại. `AI_OUTPUT_TRUNCATED` không được retry mù cùng cấu hình.
- Gói đã confirm: `POST /packages/{id}/revise` với `operationId`, `packageToken`, `components` đầy đủ và `regenerate` liệt kê phần muốn sinh mới. Endpoint tạo revision/package ID mới, kiểm tra profile lại. Các học liệu không đổi có thể được sao chép metadata để giữ cách ly; không sửa bản đã giao.
- Profile/policy/consent thay đổi: tạo revision mới qua revise; không ép retry trên snapshot cũ.
- Sửa content: nếu còn grant hiệu lực thì phải thu hồi trước. Gọi `/content/reopen` bằng envelope version hiện tại, lấy revisionToken mới, rồi save version mới và finalize lại. Các package nguồn cũ chuyển Stale; không tự áp dụng gói cũ lên V2.
- Thu hồi: giữ endpoint `/family-confirmations/revoke` hiện tại. Thu hồi A không làm mất B; phiên đọc/asset request tiếp theo của A bị chặn.
- Không gọi API công khai/generation/lộ trình để mở quyền cho truyện Parent Manual.

Với truyện legacy đã có scene/media: giữ nguyên layout đã lưu. Nếu listening mới không neo được vào layout đó, trả lỗi có kiểm soát; không sửa các scene trẻ đang đọc. Cần xử lý version mới sau khi thu hồi theo quy trình biên tập.

## 7. Triển khai và kiểm chứng

- AI service cần hỗ trợ schema safety v2 trước Core mới. Worker Core cũ phải drain trước khi nhận job package.
- Migration phải bổ sung bảng package, FK/index, điều chỉnh constraint Parent Manual chưa có bé, cho phép receipt Setup không có child và giữ các constraint Parent AI/Creator.
- Snapshot được cập nhật cùng migration tăng dần AddParentStoryPackages. Giữ nguyên các assertion migration-chain/snapshot; không bỏ test để che schema thiếu ở môi trường khác.
- File seed số 70 là fixture Cancelled không cấp quyền, không phải kết quả safety. Không chạy seed trong đợt coding này.
- Test HTTP/SQLite và provider giả lập không chứng minh PostgreSQL advisory lock hoặc Vertex thật. Cần kiểm thử riêng sau khi được phép migration và chạy live.
